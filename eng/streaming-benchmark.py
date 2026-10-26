#!/usr/bin/env python3
"""P7 process benchmark: source parsing plus transformation, with independent output oracles."""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import signal
import statistics
import subprocess
import sys
import time

_spec = importlib.util.spec_from_file_location('streaming_gate', Path(__file__).with_name('streaming-gate.py'))
_gate = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_gate)
CANONICAL = ('identity', 'adjacent', 'accumulator', 'iterate', 'merge', 'fork', 'source-document')
SIZES = {'1mb': 1_000_000, '100mb': 100_000_000, '1gb': 1_000_000_000}


def input_size(value):
    if value.lower() in SIZES:
        return SIZES[value.lower()]
    try:
        result = int(value)
    except ValueError:
        raise argparse.ArgumentTypeError('use 1mb, 100mb, 1gb or an exact byte count')
    if result < 512:
        raise argparse.ArgumentTypeError('input must contain at least 512 bytes')
    return result


def child_environment(heap_limit):
    env = dict(os.environ)
    for key in list(env):
        if key.startswith(('DOTNET_GCHeapHardLimit', 'COMPlus_GCHeapHardLimit')):
            del env[key]
    if heap_limit:
        env['DOTNET_GCHeapHardLimit'] = f'{heap_limit:X}'
    # Warm processes use one stable JIT policy on both sides; native children ignore it.
    env['DOTNET_TieredCompilation'] = '0'
    return env


def run_child(command, prefix, env, timeout):
    start = time.monotonic()
    with prefix.with_suffix('.stdout').open('w') as stdout, prefix.with_suffix('.stderr').open('w') as stderr:
        child = subprocess.Popen(command, stdout=stdout, stderr=stderr, env=env)
        deadline = start + timeout
        timed_out = False
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
            time.sleep(min(0.05, max(0, deadline - time.monotonic())))
        child.returncode = os.waitstatus_to_exitcode(status)
    raw = prefix.with_suffix('.stdout').read_text()
    try:
        result = json.loads(raw) if len(raw) <= 1024 * 1024 else None
        # Non-finite child JSON remains in stdout, never in an apparently valid checkpoint.
        json.dumps(result, allow_nan=False)
    except (json.JSONDecodeError, ValueError):
        result = None
    peak = usage.ru_maxrss * (1024 if sys.platform == 'linux' else 1)
    return dict(command=command, exitCode=child.returncode, timedOut=timed_out,
                childWallMs=(time.monotonic() - start) * 1000, outerPeakWorkingSetBytes=peak,
                result=result, stdout=prefix.with_suffix('.stdout').name, stderr=prefix.with_suffix('.stderr').name)


def summarize(trials, rounds):
    groups = {}
    for trial in trials:
        groups.setdefault((trial['workload'], trial['bytes']), []).append(trial)
    rows = []
    for (workload, size), group in groups.items():
        modes = {mode: [t for t in group if t['mode'] == mode and t['passed']] for mode in ('tree', 'stream')}
        complete = all(len(v) == rounds for v in modes.values()) and len(group) == rounds * 2
        row = dict(workload=workload, bytes=size, complete=complete, winner='No meaningful winner',
                   treeRuns=len(modes['tree']), streamRuns=len(modes['stream']))
        if complete:
            for mode, values in modes.items():
                elapsed = statistics.median(t['result']['ElapsedMs'] for t in values)
                row[mode] = dict(medianMs=elapsed, minMs=min(t['result']['ElapsedMs'] for t in values),
                                 maxMs=max(t['result']['ElapsedMs'] for t in values),
                                 mibPerSecond=size / (1024 * 1024) / (elapsed / 1000) if elapsed else None,
                                 medianFirstOutputMs=statistics.median(t['result']['FirstOutputMs'] for t in values),
                                 medianInputAtFirstOutput=statistics.median(t['result']['InputBytesAtFirstOutput'] for t in values),
                                 medianPeakRss=statistics.median(t['outerPeakWorkingSetBytes'] for t in values))
            row['streamOverTree'] = row['stream']['medianMs'] / row['tree']['medianMs'] if row['tree']['medianMs'] else None
            ratio = row['streamOverTree']
            if ratio is not None:
                row['winner'] = 'Stream observed median' if ratio < 0.95 else 'Tree observed median' if ratio > 1.05 else 'No meaningful winner within 5% band'
        rows.append(row)
    return rows


def throughput_target(rows):
    selected = {r['workload']: r for r in rows if r['bytes'] == SIZES['100mb']}
    if not set(CANONICAL).issubset(selected) or not all(selected[w]['complete'] for w in CANONICAL):
        return dict(status='incomplete coverage', passed=None,
                    requirement='All seven canonical workloads at 100 MB, stream median no slower than tree plus transform')
    if any(selected[w].get('treeRuns', 0) < 3 or selected[w].get('streamRuns', 0) < 3 for w in CANONICAL):
        return dict(status='insufficient observations', passed=None, requirement='At least three paired observations for every canonical 100 MB workload')
    passed = all(selected[w]['streamOverTree'] is not None and selected[w]['streamOverTree'] <= 1 for w in CANONICAL)
    return dict(status='evaluated', passed=passed,
                requirement='All seven canonical workloads at 100 MB, stream median no slower than tree plus transform')


def checkpoint(output, report, complete):
    report['complete'] = complete
    temporary = output / 'report.tmp'
    temporary.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n')
    temporary.replace(output / 'report.json')


def write_markdown(output, report):
    lines = ['# Streaming versus tree plus transformation', '',
             'Fresh child processes, alternating modes, identical generated input and hash sinks. Every run validates an independent oracle.',
             'Execution includes source parsing and transformation; compilation, warmup and oracle generation are outside the timed interval.',
             'RSS is full-lifetime wait4 RSS; it is not baseline-subtracted. First output means bytes delivered to the caller.',
             f"Rounds per mode: {report['config']['rounds']}; managed heap guard: {report['config']['heapLimit']} bytes for both modes.",
             'The 5% winner band is descriptive, not statistical significance. Failed or missing trials have no winner.', '',
             '| Workload | Input bytes | Tree ms | Stream ms | Stream/tree | Tree MiB/s | Stream MiB/s | Tree first ms | Stream first ms | Stream first input bytes | Winner |',
             '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|']
    for row in report['summary']:
        if not row['complete']:
            lines.append(f"| {row['workload']} | {row['bytes']} | incomplete | incomplete | — | — | — | — | — | — | No meaningful winner |")
            continue
        tree, stream = row['tree'], row['stream']
        fmt = lambda x: f'{x:.6f}' if x is not None else '—'
        lines.append(f"| {row['workload']} | {row['bytes']} | {fmt(tree['medianMs'])} | {fmt(stream['medianMs'])} | {fmt(row['streamOverTree'])} | {fmt(tree['mibPerSecond'])} | {fmt(stream['mibPerSecond'])} | {fmt(tree['medianFirstOutputMs'])} | {fmt(stream['medianFirstOutputMs'])} | {stream['medianInputAtFirstOutput']} | {row['winner']} |")
    lines += ['', '100 MB throughput target: ' + json.dumps(report['throughputTarget']), '',
              'Complete matrix: ' + str(report['complete']) + '; failed trials: ' + str(len(report['failures'])) + '.',
              'Accumulator, iterate and type-inspection results require end-of-input; their final-result latency is not an early-emission result.']
    (output / 'summary.md').write_text('\n'.join(lines) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument('--assembly', type=Path, help='Xsl3.Benchmarks.dll with streaming-child')
    source.add_argument('--native', type=Path, help='Saved NativeAOT process probe')
    parser.add_argument('--dotnet', default=os.environ.get('DOTNET_HOST_PATH', 'dotnet'))
    parser.add_argument('--sizes', nargs='+', type=input_size, default=list(SIZES.values()))
    parser.add_argument('--workloads', nargs='+', choices=_gate.WORKLOADS, default=list(CANONICAL))
    parser.add_argument('--rounds', type=int, default=3)
    parser.add_argument('--heap-limit', type=int, default=4 * 1024 ** 3, help='Equal managed heap guard for both modes; 0 disables it (default 4 GiB)')
    parser.add_argument('--timeout-seconds', type=float, default=900)
    parser.add_argument('--require-throughput-target', action='store_true')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    executable = args.native or args.assembly
    if not executable.is_file() or args.rounds < 1 or args.heap_limit < 0 or not math.isfinite(args.timeout_seconds) or args.timeout_seconds <= 0:
        parser.error('existing artifact, positive rounds/timeout and nonnegative heap guard required')
    if len(set(args.sizes)) != len(args.sizes) or len(set(args.workloads)) != len(args.workloads):
        parser.error('duplicate sizes/workloads are not allowed')
    if sys.platform not in ('darwin', 'linux') or not hasattr(os, 'wait4'):
        parser.error('Linux/macOS wait4 is required for full-lifetime RSS')
    args.output.mkdir(parents=True, exist_ok=False)
    env = child_environment(args.heap_limit)
    report = dict(formatVersion=1, complete=False, config=dict(sizes=args.sizes, workloads=args.workloads,
                  rounds=args.rounds, heapLimit=args.heap_limit, timeoutSeconds=args.timeout_seconds,
                  tieredCompilation=False, native=bool(args.native)), artifact=str(executable.resolve()),
                  artifactSha256=hashlib.sha256(executable.read_bytes()).hexdigest(),
                  controllerSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                  platform=sys.platform, trials=[], failures=[], summary=[], throughputTarget=dict(status='incomplete coverage', passed=None))
    engine = executable.parent / 'Xsl3.dll'
    if args.assembly and engine.is_file():
        report['engineSha256'] = hashlib.sha256(engine.read_bytes()).hexdigest()
    if args.assembly:
        info = subprocess.run([args.dotnet, str(args.assembly.resolve()), 'streaming-info'], env=env,
                              capture_output=True, text=True, timeout=30)
        try:
            report['runtime'] = json.loads(info.stdout) if info.returncode == 0 else None
        except (ValueError, json.JSONDecodeError):
            report['runtime'] = None
    checkpoint(args.output, report, False)
    fingerprints = {}
    heap_capacities = {}
    for si, size in enumerate(args.sizes):
        for wi, workload in enumerate(args.workloads):
            for round_index in range(args.rounds):
                modes = ('tree', 'stream') if (si + wi + round_index) % 2 == 0 else ('stream', 'tree')
                for mode in modes:
                    command = [str(args.native.resolve()), 'stream-probe'] if args.native else [args.dotnet, str(args.assembly.resolve()), 'streaming-child']
                    command += [workload, mode, str(size), '0']
                    prefix = args.output / f'{workload}-{size}-{round_index + 1}-{mode}'
                    trial = run_child(command, prefix, env, args.timeout_seconds)
                    result = trial['result']
                    passed = trial['exitCode'] == 0 and not trial['timedOut'] and trial['outerPeakWorkingSetBytes'] > 0 and _gate.valid_measurement(result, workload, mode, size, 0, bool(args.native))
                    passed = passed and (not args.heap_limit or result['AvailableManagedBytes'] <= args.heap_limit)
                    reason = None
                    if passed:
                        key = (workload, size)
                        fingerprint = (result['Records'], result['Outputs'])
                        capacity = result['AvailableManagedBytes']
                        if key in heap_capacities and heap_capacities[key] != capacity:
                            passed = False
                            reason = 'effective managed heap configuration differs between trials'
                        heap_capacities.setdefault(key, capacity)
                        if key in fingerprints and fingerprints[key] != fingerprint:
                            passed = False
                            reason = 'tree/stream or repeated output fingerprint mismatch'
                        else:
                            fingerprints[key] = fingerprint
                    trial.update(workload=workload, mode=mode, bytes=size, round=round_index + 1, passed=bool(passed))
                    if not passed:
                        reason = reason or ('timeout' if trial['timedOut'] else 'failed, malformed or invalid measurement')
                        trial['failure'] = reason
                        report['failures'].append(f'{prefix.name}: {reason}')
                    report['trials'].append(trial)
                    report['summary'] = summarize(report['trials'], args.rounds)
                    report['throughputTarget'] = throughput_target(report['summary'])
                    checkpoint(args.output, report, False)
                    print(f'{workload} {size} round={round_index + 1} {mode}: {"ok" if passed else reason}', flush=True)
    checkpoint(args.output, report, True)
    write_markdown(args.output, report)
    return 1 if report['failures'] or args.require_throughput_target and report['throughputTarget']['passed'] is not True else 0


if __name__ == '__main__':
    sys.exit(main())
