#!/usr/bin/env python3
"""P7F paired identity allocation/throughput diagnostics; never a memory acceptance pass."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import platform
import statistics
from types import SimpleNamespace

SPEC = importlib.util.spec_from_file_location('identity_diagnostics', Path(__file__).with_name('diagnose-streaming-memory.py'))
DIAG = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DIAG)


def summarize(runs, sizes, rounds, reduction, slowdown):
    if len(runs) != len(sizes) * rounds * 4:
        raise ValueError('Unexpected comparison run count')
    rows = []
    for size in sizes:
        medians = {}
        for variant in ('baseline', 'candidate'):
            values = {}
            for kind in ('control', 'instrumented'):
                selected = [r for r in runs if (r['input_bytes'], r['variant'], r['kind']) == (size, variant, kind)]
                if len(selected) != rounds or sorted(r['round'] for r in selected) != list(range(1, rounds + 1)):
                    raise ValueError('Missing, duplicate or unexpected comparison rounds')
                if not all(r['valid'] for r in selected):
                    raise ValueError('Invalid input/output or diagnostic evidence')
                if kind == 'control':
                    if not all(type(r['elapsed_ms']) in (int, float) and math.isfinite(r['elapsed_ms'])
                               and r['elapsed_ms'] > 0 for r in selected):
                        raise ValueError('Invalid elapsed time')
                    values['elapsed_ms'] = statistics.median(r['elapsed_ms'] for r in selected)
                else:
                    if not all(type(r['allocated_bytes']) is int and r['allocated_bytes'] > 0 for r in selected):
                        raise ValueError('Invalid transformation allocation')
                    values['allocated_bytes'] = statistics.median(r['allocated_bytes'] for r in selected)
                    values['allocated_bytes_per_input_byte'] = values['allocated_bytes'] / size
            if values['elapsed_ms'] <= 0 or values['allocated_bytes'] <= 0:
                raise ValueError('Positive measured time and transformation allocation are required')
            medians[variant] = values
        allocation_ratio = medians['candidate']['allocated_bytes'] / medians['baseline']['allocated_bytes']
        time_ratio = medians['candidate']['elapsed_ms'] / medians['baseline']['elapsed_ms']
        rows.append(dict(input_bytes=size, medians=medians, allocation_ratio=allocation_ratio,
                         time_ratio=time_ratio, passed=allocation_ratio <= 1 - reduction and time_ratio <= slowdown))
    return rows


def compare(args):
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    report = dict(memory_acceptance=False, complete=False, host=platform.platform(), rounds=args.rounds,
        sizes=args.sizes, heap_limit=args.heap_limit, minimum_allocation_reduction=args.minimum_allocation_reduction,
        maximum_slowdown=args.maximum_slowdown, executables={}, runs=[], failures=[])
    for variant in ('baseline', 'candidate'):
        for kind, suffix in (('control', 'native'), ('instrumented', 'diagnostic')):
            path = getattr(args, variant + '_' + suffix).resolve()
            report['executables'][variant + '_' + kind] = dict(path=str(path), sha256=DIAG.sha256_file(path))
    DIAG.write_json(output / 'report.json', report)
    for kind in ('control', 'instrumented'):
        for size in args.sizes:
            for round_number in range(1, args.rounds + 1):
                order = ('baseline', 'candidate') if round_number % 2 else ('candidate', 'baseline')
                for variant in order:
                    name = f'{kind}-{size}-{round_number}-{variant}'
                    directory = output / name
                    suffix = 'native' if kind == 'control' else 'diagnostic'
                    status = DIAG.measure(SimpleNamespace(output=directory, native=getattr(args, variant + '_' + suffix),
                        sizes=[size], rounds=1, heap_limit=args.heap_limit, timeout_seconds=args.timeout_seconds,
                        instrumented=kind == 'instrumented', mapping_snapshots=False))
                    child = json.loads((directory / 'report.json').read_text())
                    run = child['runs'][0]
                    row = dict(variant=variant, kind=kind, input_bytes=size, round=round_number,
                        valid=status == 0 and child['complete'] and run['valid'], evidence=name + '/report.json',
                        report_sha256=DIAG.sha256_file(directory / 'report.json'),
                        outer_peak_bytes=run['outer_peak_bytes'], elapsed_ms=run.get('result', {}).get('ElapsedMs'))
                    if row['valid'] and kind == 'instrumented':
                        points = {p['Phase']: p for p in run['points']}
                        row['allocated_bytes'] = points['transformed']['AllocatedBytes'] - points['collected']['AllocatedBytes']
                    if not row['valid']:
                        report['failures'].append(name)
                    report['runs'].append(row)
                    DIAG.write_json(output / 'report.json', report)
    if not report['failures']:
        report['comparison'] = summarize(report['runs'], args.sizes, args.rounds,
            args.minimum_allocation_reduction, args.maximum_slowdown)
        report['passed'] = all(row['passed'] for row in report['comparison'])
    else:
        report['passed'] = False
    report['complete'] = True
    DIAG.write_json(output / 'report.json', report)
    return 0 if report['passed'] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for variant in ('baseline', 'candidate'):
        for kind in ('native', 'diagnostic'):
            parser.add_argument('--' + variant + '-' + kind, type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--sizes', type=int, nargs='+', required=True)
    parser.add_argument('--rounds', type=int, required=True)
    parser.add_argument('--heap-limit', type=int, required=True)
    parser.add_argument('--minimum-allocation-reduction', type=float, required=True)
    parser.add_argument('--maximum-slowdown', type=float, required=True)
    parser.add_argument('--timeout-seconds', type=float, default=900)
    args = parser.parse_args()
    if (args.rounds < 3 or args.heap_limit <= 0 or len(args.sizes) != len(set(args.sizes))
            or any(size <= 0 for size in args.sizes) or not math.isfinite(args.timeout_seconds)
            or args.timeout_seconds <= 0 or not 0 < args.minimum_allocation_reduction < 1
            or not math.isfinite(args.maximum_slowdown) or args.maximum_slowdown < 1
            or any(not getattr(args, v + '_' + k).is_file() for v in ('baseline', 'candidate') for k in ('native', 'diagnostic'))):
        parser.error('Existing images, unique positive sizes/limits, at least three rounds and finite comparison thresholds are required')
    return compare(args)


if __name__ == '__main__':
    raise SystemExit(main())
