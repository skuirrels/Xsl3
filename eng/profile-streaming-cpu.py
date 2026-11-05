#!/usr/bin/env python3
"""Capture Linux on-CPU evidence from one canonical child; never an acceptance gate."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assembly', type=Path, required=True)
    parser.add_argument('--trace-tool', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--workload', default='fork', choices=('identity', 'adjacent', 'accumulator', 'iterate', 'merge', 'fork', 'source-document'))
    parser.add_argument('--mode', default='stream', choices=('tree', 'stream'))
    args = parser.parse_args()
    if sys.platform != 'linux' or os.geteuid() != 0:
        parser.error('collect-linux requires a Linux host and root privileges')
    if not args.assembly.is_file() or not args.trace_tool.is_file():
        parser.error('an existing benchmark assembly and dotnet-trace tool are required')
    args.output.mkdir(parents=True, exist_ok=False)
    output = args.output.resolve()
    spec = importlib.util.spec_from_file_location('benchmark', Path(__file__).with_name('streaming-benchmark.py'))
    benchmark = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(benchmark)
    heap_limit = 4 * 1024 ** 3
    size = benchmark.SIZES['1gb']
    env = benchmark.child_environment(heap_limit)
    command = ['dotnet', str(args.assembly.resolve()), 'streaming-child', args.workload, args.mode, str(size), '0']
    report = dict(scope='Instrumented diagnostic only; elapsed time is not throughput acceptance',
                  command=command, heapLimit=heap_limit, tieredCompilation=False,
                  hostCpuCount=os.cpu_count(), processorCountOverride=env.get('DOTNET_PROCESSOR_COUNT'),
                  sourceArtifacts={p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                   for p in args.assembly.parent.iterdir() if p.suffix in ('.dll', '.pdb')},
                  complete=False, validOutput=False)
    child = collector = None
    try:
        # A short shared TMPDIR lets the collector find only this child's diagnostic port.
        with tempfile.TemporaryDirectory(prefix='xsl3-cpu-') as ipc, \
                (output / 'child.stdout').open('w') as stdout, \
                (output / 'child.stderr').open('w') as stderr, \
                (output / 'collector.log').open('w') as trace_log:
            env['TMPDIR'] = ipc
            start = time.monotonic()
            child = subprocess.Popen(command, env=env, stdout=stdout, stderr=stderr)
            report['childPid'] = child.pid
            while not list(Path(ipc).glob(f'dotnet-diagnostic-{child.pid}-*-socket')):
                if child.poll() is not None or time.monotonic() - start > 10:
                    raise RuntimeError('Child exited or did not expose its diagnostic port within 10 seconds')
                time.sleep(0.02)
            trace_command = [str(args.trace_tool.resolve()), 'collect-linux',
                             '--profile', 'dotnet-common,cpu-sampling,thread-time',
                             '--process-id', str(child.pid), '--duration', '00:00:00:30',
                             '--output', str(output / 'cpu.nettrace')]
            report['traceCommand'] = trace_command
            collector = subprocess.Popen(trace_command, env=env, stdin=subprocess.PIPE,
                                         stdout=trace_log, stderr=subprocess.STDOUT)
            collector.wait(timeout=90)
            report['collectorExitCode'] = collector.returncode
            child.wait(timeout=max(1, 180 - (time.monotonic() - start)))
            report['childExitCode'] = child.returncode
            stdout.flush()
            result = json.loads((output / 'child.stdout').read_text())
            json.dumps(result, allow_nan=False)
            report['result'] = result
            report['validOutput'] = (child.returncode == 0
                                     and benchmark._gate.valid_measurement(result, args.workload, args.mode, size, 0, False)
                                     and result['AvailableManagedBytes'] <= heap_limit)
            report['traceBytes'] = (output / 'cpu.nettrace').stat().st_size if (output / 'cpu.nettrace').exists() else 0
            report['complete'] = collector.returncode == 0 and report['validOutput'] and report['traceBytes'] > 0
    except Exception as error:
        report['error'] = f'{type(error).__name__}: {error}'
    finally:
        # Terminate only processes owned by this invocation, and retain every failure log.
        for process in (collector, child):
            if process is not None:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()
                if process.stdin:
                    process.stdin.close()
        (output / 'report.json').write_text(json.dumps(report, indent=2, allow_nan=False) + '\n')
    print(json.dumps({key: report.get(key) for key in ('complete', 'validOutput', 'traceBytes', 'error')}))
    return 0 if report['complete'] else 1


if __name__ == '__main__':
    sys.exit(main())
