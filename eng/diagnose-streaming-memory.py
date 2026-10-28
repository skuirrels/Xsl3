#!/usr/bin/env python3
"""P7E identity diagnostics. These runs never satisfy a streaming acceptance gate."""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import platform
import shutil
import signal
import subprocess
import sys
import time

PHASES = ('entry', 'loaded', 'warmup', 'collected', 'transformed', 'oracle', 'result')
MARKER = 'P7E_MEMORY_DIAGNOSTICS '
HOOK = r'''// Diagnostic-only P7E hooks in an isolated source copy, never in the runtime.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
namespace Xsl3.Tests;
internal static partial class StreamingProbe
{
    internal sealed record MemoryPoint(string Phase, long PeakBytes, long AllocatedBytes,
        long LiveBytes, long CommittedBytes, long HeapSizeBytes, long FragmentedBytes,
        long AvailableBytes, string? LinuxRollup);
    private static readonly List<MemoryPoint> DiagnosticPoints = new();
    private static void Capture(string phase)
    {
        var memory = GC.GetGCMemoryInfo();
        long peak = PeakWorkingSet(), allocated = GC.GetTotalAllocatedBytes(precise: true), live = GC.GetTotalMemory(false);
        string? rollup = OperatingSystem.IsLinux() ? File.ReadAllText("/proc/self/smaps_rollup") : null;
        DiagnosticPoints.Add(new(phase, peak, allocated, live, memory.TotalCommittedBytes,
            memory.HeapSizeBytes, memory.FragmentedBytes, memory.TotalAvailableMemoryBytes, rollup));
    }
    private static void WriteDiagnostics() => Console.Error.WriteLine("P7E_MEMORY_DIAGNOSTICS " +
        JsonSerializer.Serialize(DiagnosticPoints, StreamingProbeJsonContext.Default.ListMemoryPoint));
}
'''


def write_json(path, value):
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(value, indent=2) + '\n')
    temporary.replace(path)


def replace_once(text, before, after):
    if text.count(before) != 1:
        raise ValueError('Source anchor is missing or ambiguous: ' + before)
    return text.replace(before, after, 1)


def prepare(args):
    output = args.output.resolve()
    source = args.source.resolve()
    if output.is_relative_to(source):
        raise ValueError('Diagnostic output must be outside its source input')
    output.mkdir(parents=True, exist_ok=False)
    copy = output / 'source'
    shutil.copytree(source, copy, ignore=shutil.ignore_patterns('bin', 'obj', 'artifacts', '.git', '__pycache__'))
    hashes = {str(p.relative_to(copy)): hashlib.sha256(p.read_bytes()).hexdigest()
              for p in sorted(copy.rglob('*')) if p.is_file()}
    write_json(output / 'source-sha256.json', hashes)
    probe = copy / 'tests/Xsl3.Tests/StreamingProbe.cs'
    text = probe.read_text()
    replacements = (
        ('        Invocation invoke = CreateInvocation(workload, forward);',
         '        Capture("entry");\n        Invocation invoke = CreateInvocation(workload, forward);\n        Capture("loaded");'),
        ('        Execute(invoke, workload, GeneratedInput.MinimumBytes * 4);',
         '        Execute(invoke, workload, GeneratedInput.MinimumBytes * 4);\n        Capture("warmup");'),
        ('        long initialPeak = PeakWorkingSet();', '        Capture("collected");\n        long initialPeak = PeakWorkingSet();'),
        ('        Execution actual = Execute(invoke, workload, bytes);',
         '        Execution actual = Execute(invoke, workload, bytes);\n        Capture("transformed");'),
        ('        Dictionary<string, Fingerprint> expected = Expected(workload, actual.Records, actual.Padding);',
         '        Dictionary<string, Fingerprint> expected = Expected(workload, actual.Records, actual.Padding);\n        Capture("oracle");'),
        ('            Console.WriteLine(JsonSerializer.Serialize(result, StreamingProbeJsonContext.Default.Result));',
         '            Capture("result");\n            WriteDiagnostics();\n            Console.WriteLine(JsonSerializer.Serialize(result, StreamingProbeJsonContext.Default.Result));'),
    )
    for before, after in replacements:
        text = replace_once(text, before, after)
    probe.write_text(text)
    (probe.parent / 'StreamingProbe.Diagnostics.cs').write_text(HOOK)
    context = probe.parent / 'StreamingProbe.Json.cs'
    context.write_text(replace_once(context.read_text(), '[JsonSerializable(typeof(StreamingProbe.Result))]',
        '[JsonSerializable(typeof(System.Collections.Generic.List<StreamingProbe.MemoryPoint>))]\n[JsonSerializable(typeof(StreamingProbe.Result))]'))
    project = copy / 'tests/Xsl3.StreamingProbe.Native/Xsl3.StreamingProbe.Native.csproj'
    project.write_text(replace_once(project.read_text(), '</Project>',
        '<ItemGroup><Compile Include="../Xsl3.Tests/StreamingProbe.Diagnostics.cs" Link="StreamingProbe.Diagnostics.cs" /></ItemGroup>\n</Project>'))
    command = ['dotnet', 'publish', str(project), '-c', 'Release', '-r', args.rid, '-m:1',
               '-p:UseSharedCompilation=false', '-p:ProbeStylesheetDirectory=' + str(args.saved.resolve()),
               '-o', str(output / 'bin')]
    with (output / 'publish.log').open('wb') as log:
        result = subprocess.run(command, cwd=copy, stdout=log, stderr=subprocess.STDOUT, timeout=900)
    write_json(output / 'preparation.json', dict(acceptance=False, source=str(source), command=command,
        exit_code=result.returncode, rid=args.rid, instrumented_files=[str(probe.relative_to(copy)),
            str(context.relative_to(copy)), str(project.relative_to(copy)), 'tests/Xsl3.Tests/StreamingProbe.Diagnostics.cs']))
    if result.returncode:
        raise RuntimeError('Diagnostic publish failed; see ' + str(output / 'publish.log'))


def validate_points(points, heap_limit):
    if (not isinstance(points, list) or len(points) != len(PHASES)
            or not all(isinstance(p, dict) for p in points)
            or tuple(p.get('Phase') for p in points) != PHASES):
        return False
    fields = ('PeakBytes', 'AllocatedBytes', 'LiveBytes', 'CommittedBytes', 'HeapSizeBytes',
              'FragmentedBytes', 'AvailableBytes')
    return (all(all(type(p.get(f)) is int and p[f] >= 0 for f in fields) for p in points)
            and all(p['PeakBytes'] > 0 and 0 < p['AvailableBytes'] <= heap_limit for p in points)
            and all(a['PeakBytes'] <= b['PeakBytes'] and a['AllocatedBytes'] <= b['AllocatedBytes']
                    for a, b in zip(points, points[1:]))
            and all(p.get('LinuxRollup') is None or isinstance(p['LinuxRollup'], str) for p in points))


def measure(args):
    if sys.platform not in ('darwin', 'linux') or not hasattr(os, 'wait4'):
        raise RuntimeError('Diagnostics require Linux/macOS wait4')
    directory = args.output.resolve()
    directory.mkdir(parents=True, exist_ok=False)
    controller = Path(__file__).with_name('streaming-gate.py')
    spec = importlib.util.spec_from_file_location('streaming_gate', controller)
    gate = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(gate)
    report = dict(acceptance=False, complete=False, interpretation='Identity diagnostics, RSS guard disabled; heap cap retained. Never a budget pass.',
        host=platform.platform(), executable=str(args.native.resolve()),
        executable_sha256=hashlib.sha256(args.native.read_bytes()).hexdigest(),
        instrumented=args.instrumented, heap_limit=args.heap_limit, runs=[], failures=[])
    write_json(directory / 'report.json', report)
    for size in args.sizes:
        for round_number in range(1, args.rounds + 1):
            env = dict(os.environ)
            for key in list(env):
                if key.startswith(('DOTNET_GCHeapHardLimit', 'COMPlus_GCHeapHardLimit')):
                    del env[key]
            env['DOTNET_GCHeapHardLimit'] = f'{args.heap_limit:X}'
            command = [str(args.native.resolve()), 'stream-probe', 'identity', 'stream', str(size), '0']
            stem = directory / f'identity-{size}-{round_number}'
            timed_out = False
            with stem.with_suffix('.stdout').open('w') as stdout, stem.with_suffix('.stderr').open('w') as stderr:
                child = subprocess.Popen(command, stdout=stdout, stderr=stderr, env=env)
                deadline = time.monotonic() + args.timeout_seconds
                while True:
                    pid, status, usage = os.wait4(child.pid, os.WNOHANG)
                    if pid:
                        break
                    if time.monotonic() >= deadline:
                        timed_out = True
                        try:
                            os.kill(child.pid, signal.SIGKILL)
                        except ProcessLookupError:
                            pass
                        _, status, usage = os.wait4(child.pid, 0)
                        break
                    time.sleep(0.1)
                child.returncode = os.waitstatus_to_exitcode(status)
            outer = usage.ru_maxrss * (1 if sys.platform == 'darwin' else 1024)
            item = dict(input_bytes=size, round=round_number, command=command, exit_code=child.returncode,
                        outer_peak_bytes=outer, timed_out=timed_out, valid=False)
            try:
                result = json.loads(stem.with_suffix('.stdout').read_text())
                item['result'] = result
                valid = (not timed_out and child.returncode == 0 and outer > 0
                         and gate.valid_measurement(result, 'identity', 'stream', size, 0, True)
                         and result['AvailableManagedBytes'] <= args.heap_limit)
                if args.instrumented:
                    lines = [line[len(MARKER):] for line in stem.with_suffix('.stderr').read_text().splitlines() if line.startswith(MARKER)]
                    points = json.loads(lines[0]) if len(lines) == 1 else None
                    item['points'] = points
                    valid = valid and validate_points(points, args.heap_limit)
                item['valid'] = valid
            except (ValueError, KeyError, TypeError) as error:
                item['error'] = str(error)
            if not item['valid']:
                report['failures'].append(stem.name)
            report['runs'].append(item)
            write_json(directory / 'report.json', report)
            print(stem.name + ': valid=' + str(item['valid']) + ' absolute outer peak=' + str(outer), flush=True)
    report['complete'] = True
    write_json(directory / 'report.json', report)
    return 1 if report['failures'] else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    prep = sub.add_parser('prepare');prep.add_argument('--source', type=Path, required=True)
    prep.add_argument('--saved', type=Path, required=True);prep.add_argument('--output', type=Path, required=True)
    prep.add_argument('--rid', required=True)
    run = sub.add_parser('measure');run.add_argument('--native', type=Path, required=True)
    run.add_argument('--output', type=Path, required=True);run.add_argument('--sizes', type=int, nargs='+', required=True)
    run.add_argument('--rounds', type=int, required=True);run.add_argument('--heap-limit', type=int, required=True)
    run.add_argument('--timeout-seconds', type=float, default=900);run.add_argument('--instrumented', action='store_true')
    args = parser.parse_args()
    if args.command == 'prepare':
        prepare(args);return 0
    if args.rounds <= 0 or args.heap_limit <= 0 or not math.isfinite(args.timeout_seconds) or args.timeout_seconds <= 0 or any(s <= 0 for s in args.sizes) or not args.native.is_file():
        parser.error('positive sizes/rounds/limits/timeouts and an existing native executable are required')
    return measure(args)


if __name__ == '__main__':
    sys.exit(main())
