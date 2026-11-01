#!/usr/bin/env python3
"""Streaming process diagnostics. These runs never satisfy an acceptance gate.

Identity remains the default used by the retained P7E/P7F evidence; --workload
can inspect another canonical workload without changing its fixture or oracle.
"""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import platform
import resource
import shutil
import signal
import subprocess
import sys
import time

PHASES = ('entry', 'loaded', 'warmup', 'collected', 'transformed', 'oracle', 'result')
LOADER_PHASES = ('before-version', 'after-version', 'default-writer', 'absent-input',
                 'load-type', 'generated-attribute', 'static-fields', 'saved-data',
                 'execute-method', 'execute-delegate', 'deserialized', 'dispatcher', 'command')
MARKER = 'P7E_MEMORY_DIAGNOSTICS '
LOADER_HOOK = r'''// Diagnostic-only observer; generated exclusively inside the isolated source copy.
using System;
using System.IO;
using System.Xml;
using System.Xml.XPath;
namespace Xsl3.Runtime;
public static class P7LoaderObserver
{
    public static Action<string>? Callback { get; set; }
    internal static void Mark(string phase) => Callback?.Invoke(phase);
    internal static T ReadVersion<T>(Func<T> read)
    {
        Mark("before-version");
        T version = read();
        Mark("after-version");
        return version;
    }
    internal static XmlWriterSettings DefaultWriter()
    {
        var settings = new XmlWriterSettings();
        Mark("default-writer");
        return settings;
    }
    internal static XPathNavigator AbsentInput()
    {
        XPathNavigator node = Xsl3.Tree.CompactDocument.Load(
            XmlReader.Create(new StringReader(""), new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment })).CreateNavigator();
        Mark("absent-input");
        return node;
    }
}
'''
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


def sha256_file(path):
    # Linux can carry the launcher's pre-exec high-water mark into child rusage.
    # Do not inflate it by reading the complete NativeAOT image into memory.
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(64 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def snapshot_mappings(pid, destination, proc_root=Path('/proc')):
    # Read from outside the native process; don't retain the mappings in either
    # its managed heap or the launcher's pre-exec high-water mark for later runs.
    created = False
    try:
        with (proc_root / str(pid) / 'smaps').open('rb') as source, destination.open('xb') as output:
            created = True
            shutil.copyfileobj(source, output, length=64 * 1024)
        if destination.stat().st_size == 0:
            raise OSError('Process mappings are empty; the child may have exited')
    except OSError:
        if created:
            destination.unlink(missing_ok=True)
        raise


def replace_once(text, before, after):
    if text.count(before) != 1:
        raise ValueError('Source anchor is missing or ambiguous: ' + before)
    return text.replace(before, after, 1)


def instrument_loader(copy):
    """Bracket the existing loader, preserving its expressions and saved inputs."""
    runtime = copy / 'src/Xsl3/Xslt/XslCompiledTransform.cs'
    text = runtime.read_text()
    version_initializers = (
        ('private static readonly Version? s_version = typeof(XslCompiledTransform).Assembly.GetName().Version;',
         'private static readonly Version? s_version = P7LoaderObserver.ReadVersion(() => typeof(XslCompiledTransform).Assembly.GetName().Version);'),
        ('private static readonly string? s_version = GetAssemblyVersionText();',
         'private static readonly string? s_version = P7LoaderObserver.ReadVersion(() => GetAssemblyVersionText());'),
    )
    matches = [(before, after) for before, after in version_initializers if before in text]
    if len(matches) != 1:
        raise ValueError('Version initialization is missing or ambiguous')
    replacements = (
        matches[0],
        ('private static readonly XmlWriterSettings s_defaultWriterSettings = new XmlWriterSettings();',
         'private static readonly XmlWriterSettings s_defaultWriterSettings = P7LoaderObserver.DefaultWriter();'),
        ('internal static readonly XPathNavigator AbsentInput = Xsl3.Tree.CompactDocument.Load(\n'
         '            XmlReader.Create(new StringReader(""), new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment })).CreateNavigator();',
         'internal static readonly XPathNavigator AbsentInput = P7LoaderObserver.AbsentInput();'),
        ('            ArgumentNullException.ThrowIfNull(compiledStylesheet);',
         '            ArgumentNullException.ThrowIfNull(compiledStylesheet);\n            P7LoaderObserver.Mark("load-type");'),
        ('            object[] customAttrs = compiledStylesheet.GetCustomAttributes(typeof(GeneratedCodeAttribute), false);',
         '            object[] customAttrs = compiledStylesheet.GetCustomAttributes(typeof(GeneratedCodeAttribute), false);\n            P7LoaderObserver.Mark("generated-attribute");'),
        ('                FieldInfo? fldTypes = compiledStylesheet.GetField(XmlQueryStaticData.TypesFieldName, BindingFlags.Static | BindingFlags.NonPublic);',
         '                FieldInfo? fldTypes = compiledStylesheet.GetField(XmlQueryStaticData.TypesFieldName, BindingFlags.Static | BindingFlags.NonPublic);\n                P7LoaderObserver.Mark("static-fields");'),
        ('                    byte[]? queryData = fldData.GetValue(/*this:*/null) as byte[];',
         '                    byte[]? queryData = fldData.GetValue(/*this:*/null) as byte[];\n                    P7LoaderObserver.Mark("saved-data");'),
        ('                        Type[]? earlyBoundTypes = (Type[]?)fldTypes.GetValue(null);',
         '                        Type[]? earlyBoundTypes = (Type[]?)fldTypes.GetValue(null);\n                        P7LoaderObserver.Mark("execute-method");'),
        ('            var staticData = new XmlQueryStaticData(queryData, earlyBoundTypes);',
         '            P7LoaderObserver.Mark("execute-delegate");\n            var staticData = new XmlQueryStaticData(queryData, earlyBoundTypes);\n            P7LoaderObserver.Mark("deserialized");'),
        ('            _command = new XmlILCommand((ExecuteDelegate)delExec, staticData);\n            OutputSettings = _command.StaticData.DefaultWriterSettings;',
         '            P7LoaderObserver.Mark("dispatcher");\n            _command = new XmlILCommand((ExecuteDelegate)delExec, staticData);\n            OutputSettings = _command.StaticData.DefaultWriterSettings;\n            P7LoaderObserver.Mark("command");'),
    )
    for before, after in replacements:
        text = replace_once(text, before, after)
    observer = copy / 'src/Xsl3/Xsl/Runtime/P7LoaderObserver.cs'
    api = copy / 'src/Xsl3/PublicAPI.Unshipped.txt'
    if not api.is_file():
        raise ValueError('The isolated source is missing its unshipped API manifest')
    if observer.exists():
        raise FileExistsError(observer)
    runtime.write_text(text)
    with observer.open('x') as output:
        output.write(LOADER_HOOK)
    with api.open('a') as output:
        output.write('\nXsl3.Runtime.P7LoaderObserver\n'
            'static Xsl3.Runtime.P7LoaderObserver.Callback.get -> System.Action<string!>?\n'
            'static Xsl3.Runtime.P7LoaderObserver.Callback.set -> void\n')
    return [runtime, observer, api]


def prepare(args):
    output = args.output.resolve()
    source = args.source.resolve()
    if output.is_relative_to(source):
        raise ValueError('Diagnostic output must be outside its source input')
    output.mkdir(parents=True, exist_ok=False)
    copy = output / 'source'
    shutil.copytree(source, copy, ignore=shutil.ignore_patterns('bin', 'obj', 'artifacts', '.git', '__pycache__'))
    hashes = {str(p.relative_to(copy)): sha256_file(p)
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
    loader_files = []
    if args.loader_details:
        loader_files = instrument_loader(copy)
        text = replace_once(text, '        Invocation invoke = CreateInvocation(workload, forward);',
            '        Xsl3.Runtime.P7LoaderObserver.Callback = Capture;\n'
            '        Invocation invoke = CreateInvocation(workload, forward);\n'
            '        Xsl3.Runtime.P7LoaderObserver.Callback = null;')
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
    instrumented = [probe, context, project, probe.parent / 'StreamingProbe.Diagnostics.cs', *loader_files]
    write_json(output / 'preparation.json', dict(acceptance=False, source=str(source), command=command,
        loader_details=args.loader_details,
        exit_code=result.returncode, rid=args.rid, instrumented_files=[str(probe.relative_to(copy)),
            str(context.relative_to(copy)), str(project.relative_to(copy)), 'tests/Xsl3.Tests/StreamingProbe.Diagnostics.cs',
            *(str(path.relative_to(copy)) for path in loader_files)],
        instrumented_sha256={str(path.relative_to(copy)): sha256_file(path) for path in instrumented}))
    if result.returncode:
        raise RuntimeError('Diagnostic publish failed; see ' + str(output / 'publish.log'))


def valid_phases(phases, loader_details):
    if not all(isinstance(phase, str) for phase in phases):
        return False
    if not loader_details:
        return phases == PHASES
    expected = set(PHASES + LOADER_PHASES)
    if len(phases) != len(expected) or set(phases) != expected:
        return False
    if tuple(p for p in phases if p in PHASES) != PHASES or phases[0] != 'entry':
        return False
    # BeforeFieldInit allows the type initializer to run before the type is
    # constructed or later, when Load first reads s_version. Preserve that real
    # ordering rather than changing the runtime merely to fit a diagnostic schema.
    initialization, loading = LOADER_PHASES[:4], LOADER_PHASES[4:]
    start = phases.index(initialization[0])
    return (phases[start:start+len(initialization)] == initialization
            and tuple(p for p in phases if p in loading) == loading
            and phases.index('absent-input') < phases.index('static-fields')
            and phases.index('loaded') == 1+len(LOADER_PHASES))


def validate_points(points, heap_limit, loader_details=False):
    if (not isinstance(points, list) or not all(isinstance(p, dict) for p in points)
            or not valid_phases(tuple(p.get('Phase') for p in points), loader_details)):
        return False
    fields = ('PeakBytes', 'AllocatedBytes', 'LiveBytes', 'CommittedBytes', 'HeapSizeBytes',
              'FragmentedBytes', 'AvailableBytes')
    return (all(all(type(p.get(f)) is int and p[f] >= 0 for f in fields) for p in points)
            and all(p['PeakBytes'] > 0 and 0 < p['AvailableBytes'] <= heap_limit for p in points)
            and all(a['PeakBytes'] <= b['PeakBytes'] and a['AllocatedBytes'] <= b['AllocatedBytes']
                    for a, b in zip(points, points[1:]))
            and all(p.get('LinuxRollup') is None or isinstance(p['LinuxRollup'], str) for p in points))


def load_gate():
    controller = Path(__file__).with_name('streaming-gate.py')
    spec = importlib.util.spec_from_file_location('streaming_gate', controller)
    gate = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(gate)
    return gate


def measure(args):
    if sys.platform not in ('darwin', 'linux') or not hasattr(os, 'wait4'):
        raise RuntimeError('Diagnostics require Linux/macOS wait4')
    directory = args.output.resolve()
    directory.mkdir(parents=True, exist_ok=False)
    gate = load_gate()
    report = dict(acceptance=False, complete=False, interpretation='Diagnostics, RSS guard disabled; heap cap retained. Never a budget pass.',
        workload=args.workload,
        host=platform.platform(), executable=str(args.native.resolve()),
        executable_sha256=sha256_file(args.native),
        instrumented=args.instrumented, loader_details=args.loader_details, heap_limit=args.heap_limit,
        mapping_snapshots_requested=args.mapping_snapshots, runs=[], failures=[])
    report['launcher_peak_bytes_before_first_child'] = resource.getrusage(resource.RUSAGE_SELF).ru_maxrss * (1 if sys.platform == 'darwin' else 1024)
    write_json(directory / 'report.json', report)
    for size in args.sizes:
        for round_number in range(1, args.rounds + 1):
            env = dict(os.environ)
            for key in list(env):
                if key.startswith(('DOTNET_GCHeapHardLimit', 'COMPlus_GCHeapHardLimit')):
                    del env[key]
            env['DOTNET_GCHeapHardLimit'] = f'{args.heap_limit:X}'
            command = [str(args.native.resolve()), 'stream-probe', args.workload, 'stream', str(size), '0']
            stem = directory / f'{args.workload}-{size}-{round_number}'
            timed_out = False
            snapshots = []
            next_snapshot = 0
            snapshot_times = (0.0, 0.25, 1.0, 5.0)
            with stem.with_suffix('.stdout').open('w') as stdout, stem.with_suffix('.stderr').open('w') as stderr:
                child = subprocess.Popen(command, stdout=stdout, stderr=stderr, env=env)
                started = time.monotonic()
                deadline = started + args.timeout_seconds
                try:
                    while True:
                        pid, status, usage = os.wait4(child.pid, os.WNOHANG)
                        if pid:
                            break
                        if args.mapping_snapshots and next_snapshot < len(snapshot_times) and time.monotonic() - started >= snapshot_times[next_snapshot]:
                            path = stem.with_name(stem.name + f'-mappings-{next_snapshot + 1}.txt')
                            snapshot = dict(elapsed_seconds=time.monotonic() - started, file=path.name)
                            try:
                                snapshot_mappings(child.pid, path)
                                snapshot['sha256'] = sha256_file(path)
                            except OSError as error:
                                snapshot['error'] = str(error)
                            snapshots.append(snapshot)
                            next_snapshot += 1
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
                finally:
                    if child.returncode is None:
                        child.kill()
                        child.wait()
            outer = usage.ru_maxrss * (1 if sys.platform == 'darwin' else 1024)
            item = dict(input_bytes=size, round=round_number, command=command, exit_code=child.returncode,
                        outer_peak_bytes=outer, timed_out=timed_out, valid=False)
            if args.mapping_snapshots:
                item['mapping_snapshots'] = snapshots
            try:
                result = json.loads(stem.with_suffix('.stdout').read_text())
                item['result'] = result
                valid = (not timed_out and child.returncode == 0 and outer > 0
                         and gate.valid_measurement(result, args.workload, 'stream', size, 0, True)
                         and result['AvailableManagedBytes'] <= args.heap_limit)
                if args.mapping_snapshots:
                    valid = valid and any('sha256' in snapshot for snapshot in snapshots)
                if args.instrumented:
                    lines = [line[len(MARKER):] for line in stem.with_suffix('.stderr').read_text().splitlines() if line.startswith(MARKER)]
                    points = json.loads(lines[0]) if len(lines) == 1 else None
                    item['points'] = points
                    valid = valid and validate_points(points, args.heap_limit, args.loader_details)
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
    prep.add_argument('--loader-details', action='store_true', help='Bracket saved-type initialization and loading in the isolated diagnostic copy')
    run = sub.add_parser('measure');run.add_argument('--native', type=Path, required=True)
    run.add_argument('--workload', choices=load_gate().WORKLOADS, default='identity')
    run.add_argument('--output', type=Path, required=True);run.add_argument('--sizes', type=int, nargs='+', required=True)
    run.add_argument('--rounds', type=int, required=True);run.add_argument('--heap-limit', type=int, required=True)
    run.add_argument('--timeout-seconds', type=float, default=900);run.add_argument('--instrumented', action='store_true')
    run.add_argument('--loader-details', action='store_true', help='Require the complete instrumented loader phase sequence')
    run.add_argument('--mapping-snapshots', action='store_true', help='Linux only: retain external instantaneous smaps snapshots, never a decomposition of the historical peak')
    args = parser.parse_args()
    if args.command == 'prepare':
        prepare(args);return 0
    if args.mapping_snapshots and sys.platform != 'linux':
        parser.error('mapping snapshots require Linux /proc')
    if args.loader_details and not args.instrumented:
        parser.error('loader details require an instrumented diagnostic image')
    if (args.rounds <= 0 or args.heap_limit <= 0 or not math.isfinite(args.timeout_seconds)
            or args.timeout_seconds <= 0 or any(s <= 0 for s in args.sizes)
            or len(set(args.sizes)) != len(args.sizes) or not args.native.is_file()):
        parser.error('unique positive sizes, positive rounds/limits, a finite positive timeout and an existing native executable are required')
    return measure(args)


if __name__ == '__main__':
    sys.exit(main())
