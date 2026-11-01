#!/usr/bin/env python3
"""P7 child-process probe controller. Budgets are absolute, never baseline-subtracted."""
import argparse
import json
import math
import os
import re
import signal
from pathlib import Path
import subprocess
import sys
import time

WORKLOADS = ('identity', 'adjacent', 'accumulator', 'iterate', 'merge', 'fork', 'source-document', 'climbing-union', 'climbing-intersection', 'type-assertion', 'type-inspection', 'copy-select', 'conditional-content', 'template-parameter', 'function-parameter', 'overlap-values')


def valid_fingerprints(outputs, workload):
    names = {'principal'}
    if workload == 'fork':
        names.update(('urn:stream-probe:first', 'urn:stream-probe:second'))
    return (isinstance(outputs, dict) and set(outputs) == names
            and all(isinstance(value, dict) and type(value.get('Bytes')) is int and value['Bytes'] >= 0
                    and isinstance(value.get('Sha256'), str) and re.fullmatch(r'[0-9A-Fa-f]{64}', value['Sha256'])
                    for value in outputs.values()))


def valid_measurement(result, workload, mode, size, limit, native):
    if not isinstance(result, dict):
        return False
    integers = ('RequestedInputBytes', 'InputBytes', 'LimitBytes', 'PeakWorkingSetBytes',
                'LiveManagedBytes', 'Records', 'InputBytesAtFirstOutput', 'AvailableManagedBytes')
    times = ('ElapsedMs', 'FirstOutputMs')
    return (all(type(result.get(name)) is int and result[name] >= 0 for name in integers)
            and all(type(result.get(name)) in (int, float) and math.isfinite(result[name]) and result[name] >= 0 for name in times)
            and result.get('Status') == 'ok' and result.get('ValidOutput') is True
            and result.get('Workload') == workload and result.get('Mode') == mode
            and result['RequestedInputBytes'] == size and result['InputBytes'] == size
            and result['LimitBytes'] == limit and result['PeakWorkingSetBytes'] > 0
            and isinstance(result.get('PeakMemorySource'), str) and bool(result['PeakMemorySource'])
            and result['Records'] > 0 and 0 < result['InputBytesAtFirstOutput'] <= size
            and result['FirstOutputMs'] <= result['ElapsedMs']
            and type(result.get('DynamicCodeSupported')) is bool
            and type(result.get('HasTrustedPlatformAssemblies')) is bool
            and (not native or result['DynamicCodeSupported'] is False and result['HasTrustedPlatformAssemblies'] is False)
            and result['AvailableManagedBytes'] > 0
            and (not limit or result['AvailableManagedBytes'] <= limit and result['LiveManagedBytes'] <= limit)
            and valid_fingerprints(result.get('Outputs'), workload))


def save_report(directory, reports, failures, complete):
    # A checkpoint survives interruption without presenting an unfinished matrix as a pass.
    report = directory / 'report.json'
    temporary = directory / 'report.tmp'
    temporary.write_text(json.dumps({'complete': complete, 'runs': reports, 'failures': failures}, indent=2) + '\n')
    temporary.replace(report)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument('--assembly', type=Path, help='Built Xsl3.Tests.dll containing stream-probe')
    source.add_argument('--native', type=Path, help='Published NativeAOT process probe executable')
    parser.add_argument('--bytes', type=int, required=True, help='Exact measured generated-input bytes; combined for merge')
    parser.add_argument('--limit', type=int, required=True, help='Absolute process peak RSS and managed heap limit in bytes; 0 disables both')
    parser.add_argument('--baseline-bytes', type=int, default=10 * 1024 * 1024, help='Tree/stream fingerprint comparison size (default 10 MiB)')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--timeout-seconds', type=float, default=0, help='Per-child wall-time guard; 0 disables it. A timeout fails and retains the full-lifetime RSS counter')
    parser.add_argument('--workloads', nargs='+', choices=WORKLOADS, default=list(WORKLOADS))
    parser.add_argument('--dotnet', default=os.environ.get('DOTNET_HOST_PATH', 'dotnet'))
    args = parser.parse_args()
    executable = args.native or args.assembly
    if args.bytes <= 0 or args.baseline_bytes <= 0 or args.limit < 0 or not math.isfinite(args.timeout_seconds) or args.timeout_seconds < 0 or not executable.is_file():
        parser.error('positive input sizes, nonnegative finite limits/timeouts and an existing probe artifact are required')
    # Preserve previous evidence. A new invocation needs a new directory.
    if sys.platform not in ('linux', 'darwin') or not hasattr(os, 'wait4'):
        parser.error('the controller needs Linux/macOS wait4 for a full-lifetime peak RSS measurement')
    args.output.mkdir(parents=True, exist_ok=False)
    reports = []
    failures = []
    save_report(args.output, reports, failures, False)
    for workload in args.workloads:
        baseline = {}
        for phase, mode, size, limit in (
            ('baseline-tree', 'tree', args.baseline_bytes, 0),
            ('baseline-stream', 'stream', args.baseline_bytes, 0),
            ('measured-stream', 'stream', args.bytes, args.limit),
        ):
            env = dict(os.environ)
            # Baselines validate output and do not inherit the measured child's
            # heap budget. The measured process gets the explicitly requested cap.
            for key in list(env):
                if key.startswith(('DOTNET_GCHeapHardLimit', 'COMPlus_GCHeapHardLimit')):
                    del env[key]
            if limit:
                env['DOTNET_GCHeapHardLimit'] = f'{limit:X}'
            command = ([str(args.native.resolve())] if args.native else [args.dotnet, str(args.assembly.resolve())])
            command += ['stream-probe', workload, mode, str(size), str(limit)]
            prefix = args.output / f'{workload}-{phase}'
            with prefix.with_suffix('.stdout').open('w') as stdout, prefix.with_suffix('.stderr').open('w') as stderr:
                child = subprocess.Popen(command, stdout=stdout, stderr=stderr, env=env)
                timed_out = False
                try:
                    deadline = time.monotonic() + args.timeout_seconds if args.timeout_seconds else None
                    if deadline is None:
                        _, status, usage = os.wait4(child.pid, 0)
                    else:
                        while True:
                            pid, status, usage = os.wait4(child.pid, os.WNOHANG)
                            if pid:
                                break
                            remaining = deadline - time.monotonic()
                            if remaining <= 0:
                                timed_out = True
                                # Popen.kill() polls/reaps first, which can lose wait4's
                                # counter if the child exits at this deadline boundary.
                                try:
                                    os.kill(child.pid, signal.SIGKILL)
                                except ProcessLookupError:
                                    pass
                                _, status, usage = os.wait4(child.pid, 0)
                                break
                            time.sleep(min(0.1, remaining))
                    child.returncode = os.waitstatus_to_exitcode(status)
                finally:
                    if child.returncode is None:
                        child.kill()
                        child.wait()
            outer_peak = usage.ru_maxrss * (1 if sys.platform == 'darwin' else 1024)
            stdout = prefix.with_suffix('.stdout').read_text()
            item = {'workload': workload, 'phase': phase, 'exitCode': child.returncode, 'command': command,
                    'outerPeakWorkingSetBytes': outer_peak, 'outerPeakMemorySource': 'wait4', 'passed': False,
                    'timeoutSeconds': args.timeout_seconds, 'timedOut': timed_out}
            if timed_out:
                failures.append(f'{workload}/{phase}: child exceeded {args.timeout_seconds:g}s wall-time guard')
            try:
                result = json.loads(stdout)
                item['result'] = result
                passed = (not timed_out and child.returncode == 0 and valid_measurement(result, workload, mode, size, limit, args.native is not None)
                          and outer_peak > 0 and (not limit or max(outer_peak, result['PeakWorkingSetBytes']) <= limit))
                item['passed'] = passed
                if phase.startswith('baseline-'):
                    baseline[mode] = result['Outputs'] if passed else None
                if not passed:
                    failures.append(f'{workload}/{phase}: invalid or failed result, status {result["Status"]} (exit {child.returncode}, outer peak {outer_peak}, limit {limit})')
                print(f'{workload:16} {phase:16} {result["Status"]:36} input={result["InputBytes"]} peak={max(outer_peak, result["PeakWorkingSetBytes"])}', flush=True)
            except (ValueError, KeyError, TypeError):
                failures.append(f'{workload}/{phase}: missing or malformed probe result (exit {child.returncode})')
                print(f'{workload:16} {phase:16} no valid result (exit {child.returncode})', flush=True)
            reports.append(item)
            save_report(args.output, reports, failures, False)
        if baseline.get('tree') is None or baseline.get('stream') is None or baseline['tree'] != baseline['stream']:
            failures.append(f'{workload}: tree/stream baseline fingerprints differ or are unavailable')
    save_report(args.output, reports, failures, True)
    if failures:
        print('\n'.join(failures), file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
