#!/usr/bin/env python3
"""Paired runtime-only diagnostic using the unchanged canonical benchmark consumer."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import statistics
import subprocess
import sys


def hashes(directory):
    return {str(p.relative_to(directory)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(directory.rglob('*')) if p.is_file()}


def main():
    spec = importlib.util.spec_from_file_location('benchmark', Path(__file__).with_name('streaming-benchmark.py'))
    benchmark = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(benchmark)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline-assembly', type=Path, required=True)
    parser.add_argument('--candidate-engine', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--bytes', type=benchmark.input_size, default=benchmark.SIZES['100mb'])
    parser.add_argument('--rounds', type=int, default=3)
    parser.add_argument('--workloads', nargs='+', choices=benchmark.CANONICAL,
                        default=['identity', 'accumulator', 'fork', 'source-document'])
    args = parser.parse_args()
    if args.rounds < 1 or len(set(args.workloads)) != len(args.workloads):
        parser.error('positive rounds and distinct workloads are required')
    if not args.baseline_assembly.is_file() or not args.candidate_engine.is_file():
        parser.error('existing baseline consumer and candidate engine assemblies are required')
    if args.candidate_engine.name != 'Xsl3.dll':
        parser.error('candidate engine must be Xsl3.dll')
    if not args.candidate_engine.with_suffix('.pdb').is_file():
        parser.error('candidate PDB is required for source verification')
    if args.output.resolve().is_relative_to(args.baseline_assembly.resolve().parent):
        parser.error('output must be outside the baseline binary directory')
    # This diagnostic must not inherit the supplemental framework-symbol policy.
    if any(os.environ.get(k) is not None for k in ('DOTNET_ReadyToRun', 'COMPlus_ReadyToRun')):
        parser.error('remove framework compilation overrides before comparing runtimes')
    args.output.mkdir(parents=True, exist_ok=False)
    output = args.output.resolve()
    report = dict(scope='Paired diagnostic only; not original full P7 acceptance', complete=False,
                  bytes=args.bytes, rounds=args.rounds, workloads=args.workloads, runs=[])
    fingerprints, samples = {}, {}

    def save():
        (output / 'report.json').write_text(json.dumps(report, indent=2, allow_nan=False) + '\n')

    try:
        for variant in ('baseline', 'candidate'):
            shutil.copytree(args.baseline_assembly.resolve().parent, output / variant)
        for suffix in ('.dll', '.pdb', '.xml'):
            source = args.candidate_engine.with_suffix(suffix)
            if source.is_file():
                shutil.copy2(source, output / 'candidate' / source.name)
        frozen = report['binaries'] = {v: hashes(output / v) for v in ('baseline', 'candidate')}
        for name in frozen['baseline'].keys() | frozen['candidate'].keys():
            if name not in ('Xsl3.dll', 'Xsl3.pdb', 'Xsl3.xml'):
                if frozen['baseline'].get(name) != frozen['candidate'].get(name):
                    raise ValueError(f'Consumer differs between variants: {name}')
        save()
        for round_number in range(1, args.rounds + 1):
            order = ('baseline', 'candidate') if round_number % 2 else ('candidate', 'baseline')
            for variant in order:
                run_output = output / f'{variant}-round-{round_number}'
                command = [sys.executable, str(Path(__file__).with_name('streaming-benchmark.py').resolve()),
                           '--assembly', str(output / variant / args.baseline_assembly.name),
                           '--sizes', str(args.bytes), '--rounds', '1', '--heap-limit', '4294967296',
                           '--workloads', *args.workloads, '--output', str(run_output)]
                with (output / f'{run_output.name}.log').open('w') as log:
                    result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT)
                report['runs'].append(dict(variant=variant, round=round_number, exitCode=result.returncode,
                                           report=f'{run_output.name}/report.json'))
                save()
                if result.returncode:
                    raise RuntimeError(f'{run_output.name} failed with exit {result.returncode}')
                data = json.loads((run_output / 'report.json').read_text())
                if not data['complete'] or data['failures'] or len(data['trials']) != 2 * len(args.workloads):
                    raise ValueError(f'Incomplete observations: {run_output.name}')
                if (data['config']['heapLimit'] != 4294967296 or data['config']['tieredCompilation']
                        or data['config']['native'] or data['config']['sizes'] != [args.bytes]
                        or data['engineSha256'] != frozen[variant]['Xsl3.dll']
                        or data['artifactSha256'] != frozen[variant][args.baseline_assembly.name]):
                    raise ValueError(f'Measurement configuration or binaries differ: {run_output.name}')
                if report.setdefault('runtime', data['runtime']) != data['runtime']:
                    raise ValueError('Runtime configuration changed between variants')
                seen = set()
                for trial in data['trials']:
                    value = trial['result']; key = (trial['workload'], trial['mode'])
                    if key in seen or key not in {(w, m) for w in args.workloads for m in ('tree', 'stream')}:
                        raise ValueError('Duplicate or unexpected observation')
                    seen.add(key)
                    if (not trial['passed'] or trial['exitCode'] != 0 or trial['timedOut']
                            or not benchmark._gate.valid_measurement(value, *key, args.bytes, 0, False)
                            or value['AvailableManagedBytes'] != 4294967296
                            or json.loads((run_output / trial['stdout']).read_text()) != value):
                        raise ValueError(f'Invalid original measurement: {run_output.name}/{key}')
                    fingerprint = (value['Records'], value['Outputs'])
                    if fingerprints.setdefault(key[0], fingerprint) != fingerprint:
                        raise ValueError(f'Output differs between variants: {key[0]}')
                    samples.setdefault((variant, *key), []).append(value['ElapsedMs'])
                if hashes(output / variant) != frozen[variant]:
                    raise ValueError(f'Frozen binaries changed: {variant}')
                print(run_output.name, 'validated', flush=True)
        report['summary'] = [dict(workload=w,
            baselineStreamMs=statistics.median(samples['baseline', w, 'stream']),
            candidateStreamMs=statistics.median(samples['candidate', w, 'stream']),
            candidateOverBaseline=statistics.median(samples['candidate', w, 'stream'])
                / statistics.median(samples['baseline', w, 'stream']),
            candidateStreamOverTree=statistics.median(samples['candidate', w, 'stream'])
                / statistics.median(samples['candidate', w, 'tree'])) for w in args.workloads]
        report['complete'] = True
    except Exception as error:
        report['error'] = f'{type(error).__name__}: {error}'
    finally:
        save()
    print(json.dumps({k: report[k] for k in ('complete', 'error', 'summary') if k in report}))
    return 0 if report['complete'] else 1


if __name__ == '__main__':
    sys.exit(main())
