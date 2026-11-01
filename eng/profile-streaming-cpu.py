#!/usr/bin/env python3
"""Isolated on-CPU diagnostics, never original P7 acceptance measurements.

Perf map settings: https://learn.microsoft.com/en-us/dotnet/core/runtime-config/debugging-profiling
CPU tracing: https://github.com/dotnet/runtime/blob/main/docs/project/linux-performance-tracing.md
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess


def environment(original, heap_limit):
    result = {k: v for k, v in original.items()
              if not k.startswith(('DOTNET_GCHeapHardLimit', 'COMPlus_GCHeapHardLimit'))}
    result.update(DOTNET_GCHeapHardLimit=f'{heap_limit:X}', DOTNET_TieredCompilation='0',
                  DOTNET_PerfMapEnabled='0', DOTNET_CLI_USE_MSBUILD_SERVER='0')
    return result


def validate_pair(results, gate, size, heap_limit, native=False):
    for mode, result in results.items():
        if not gate.valid_measurement(result, 'fork', mode, size, 0, native):
            raise ValueError('Profiled child returned an invalid result: ' + mode)
        if result['AvailableManagedBytes'] != heap_limit:
            raise ValueError('Profiled child did not use the requested managed guard')
        if native and (result['DynamicCodeSupported'] or result['HasTrustedPlatformAssemblies']):
            raise ValueError('The native CPU control requires an actual NativeAOT host')
        if not native and (not result['DynamicCodeSupported'] or not result['HasTrustedPlatformAssemblies']):
            raise ValueError('The CPU control requires the actual managed benchmark child')
    if set(results) != {'tree', 'stream'} or results['tree']['Outputs'] != results['stream']['Outputs']:
        raise ValueError('Profiled tree/stream fingerprints differ or coverage is incomplete')
    if results['tree']['Records'] != results['stream']['Records']:
        raise ValueError('Profiled tree/stream record counts differ')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--perf', required=True, type=Path)
    parser.add_argument('--native', type=Path, help='Existing canonical NativeAOT probe, with adjacent .dbg symbols')
    parser.add_argument('--bytes', type=int, default=100_000_000)
    parser.add_argument('--heap-limit', type=int, default=4_294_967_296)
    args = parser.parse_args()
    if args.bytes <= 0 or args.heap_limit <= 0:
        parser.error('Input and managed guard must be positive')
    source, output = args.source.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    dotnet = shutil.which('dotnet')
    if not dotnet or not args.perf.is_file():
        raise RuntimeError('Real dotnet and perf executables are required')
    env = environment(os.environ, args.heap_limit)
    report = dict(acceptance=False, complete=False, source=str(source), commands=[],
                  interpretation='Instrumented on-CPU samples include startup, warmup and oracle work. '
                  'Profiling overhead and elapsed times cannot qualify original throughput targets.')

    def save():
        (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')

    def run(label, command, capture=False):
        report['commands'].append(dict(label=label, argv=list(map(str, command))))
        save()
        with (output / (label + '.stderr')).open('w') as errors:
            if capture:
                result = subprocess.run(command, cwd=source, env=env, text=True,
                                        stdout=subprocess.PIPE, stderr=errors, timeout=300)
                (output / (label + '.stdout')).write_text(result.stdout)
            else:
                with (output / (label + '.stdout')).open('w') as stdout:
                    result = subprocess.run(command, cwd=source, env=env, stdout=stdout,
                                            stderr=errors, timeout=300)
        if result.returncode:
            raise RuntimeError(f'{label} failed with exit {result.returncode}; evidence retained')
        return result.stdout if capture else None

    report['sourceCommit'] = run('source-commit', ['git', 'rev-parse', 'HEAD'], True).strip()
    if run('source-status', ['git', 'status', '--porcelain', '--untracked-files=no'], True).strip():
        raise RuntimeError('Diagnostic source contains tracked modifications')
    run('build', [dotnet, 'build', 'bench/Xsl3.Benchmarks', '-c', 'Release', '-f', 'net10.0',
                  '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false'])
    assembly = source / 'bench/Xsl3.Benchmarks/bin/Release/net10.0/Xsl3.Benchmarks.dll'
    report['runtime'] = json.loads(run('runtime', [dotnet, str(assembly), 'streaming-info'], True))
    if report['runtime']['Architecture'] != 'X64' or report['runtime']['ProcessorCount'] != 4:
        raise RuntimeError('This control requires the qualified hosted x64/four-processor allowance')
    report['binaryHashes'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                              for p in assembly.parent.glob('*') if p.suffix in ('.dll', '.pdb')}
    script = Path(__file__).resolve().with_name('streaming-gate.py')
    spec = importlib.util.spec_from_file_location('gate', script)
    gate = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(gate)
    native = args.native.resolve() if args.native else None
    if native:
        symbols = native.with_name(native.name + '.dbg')
        if not native.is_file() or not symbols.is_file():
            raise RuntimeError('Native executable and adjacent debug symbols are required')
        report['nativeBinaryHashes'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                       for p in (native, symbols)}
        def build_id(path):
            import re
            notes = run(path.name + '-notes', ['readelf', '-n', str(path)], True)
            matches = re.findall(r'Build ID: ([0-9a-f]+)', notes)
            if len(matches) != 1:
                raise RuntimeError('Native executable/symbol build identity is missing or ambiguous')
            return matches[0]
        report['nativeBuildId'] = build_id(native)
        if build_id(symbols) != report['nativeBuildId']:
            raise RuntimeError('Native symbols do not match the executable')
    results = {}
    for mode in ('tree', 'stream'):
        data = output / (mode + '.perf.data')
        # GNU timeout owns and terminates the entire privileged profiling process group.
        # Perf maps alone provide symbols without adding a second jitdump recorder.
        child = ([str(native), 'stream-probe'] if native else [dotnet, str(assembly), 'streaming-child'])
        run(mode, ['sudo', '-n', '/usr/bin/timeout', '--kill-after=5s', '120s', str(args.perf),
                   'record', '-e', 'cpu-clock:u', '-F', '199', '--call-graph', 'dwarf,16384',
                   '-o', str(data), '--', '/usr/bin/env',
                   'DOTNET_GCHeapHardLimit=' + f'{args.heap_limit:X}', 'DOTNET_TieredCompilation=0',
                   'DOTNET_PerfMapEnabled=' + ('0' if native else '3'), *child,
                   'fork', mode, str(args.bytes), '0'])
        run(mode + '-readability', ['sudo', '-n', 'chmod', 'a+r', str(data)])
        results[mode] = json.loads((output / (mode + '.stdout')).read_text())
        pids = run(mode + '-pids', [str(args.perf), 'script', '-i', str(data), '-F', 'pid'], True)
        sampled = {int(line.strip()) for line in pids.splitlines() if line.strip().isdigit()}
        if not sampled:
            raise RuntimeError('No on-CPU samples were recorded')
        symbols = output / (mode + '-symbols')
        symbols.mkdir()
        for pid in sampled:
            for name in (f'perf-{pid}.map', f'perfinfo-{pid}.map'):
                path = Path('/tmp') / name
                if path.is_file():
                    shutil.copy2(path, symbols / name)
        if not native and not list(symbols.glob('perf-*.map')):
            raise RuntimeError('Managed perf maps were not retained; no symbolication claim')
        run(mode + '-exclusive', [str(args.perf), 'report', '-i', str(data), '--stdio',
                                  '--no-children', '--sort', 'symbol,dso', '--percent-limit', '0.5'])
        run(mode + '-stacks', [str(args.perf), 'script', '-i', str(data)])
    validate_pair(results, gate, args.bytes, args.heap_limit, native=native is not None)
    if native:
        after = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (native, native.with_name(native.name + '.dbg'))}
        if after != report['nativeBinaryHashes']:
            raise RuntimeError('Native binary or symbols changed during profiling')
    report.update(complete=True, results=results, profiledOutputsValidated=True)
    save()


if __name__ == '__main__':
    main()
