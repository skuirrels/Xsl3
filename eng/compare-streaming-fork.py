#!/usr/bin/env python3
"""Unprofiled, interleaved fork controls. Diagnostic only; no P7 gate is waived."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import statistics
import subprocess


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def validate_trials(trials, rounds, gate, size, heap):
    expected = {(variant, mode) for variant in ('baseline', 'candidate') for mode in ('tree', 'stream')}
    if len(trials) != rounds * 4:
        raise ValueError('Incomplete trial coverage')
    fingerprints = None
    for key in expected:
        selected = [t for t in trials if (t['variant'], t['mode']) == key]
        if len(selected) != rounds:
            raise ValueError('Incomplete variant/mode coverage')
        for trial in selected:
            result = trial['result']
            if trial['exitCode'] or not gate.valid_measurement(result, 'fork', key[1], size, 0, False):
                raise ValueError('Invalid fork observation')
            if result['AvailableManagedBytes'] != heap:
                raise ValueError('Actual managed guard differs')
            if not result['DynamicCodeSupported'] or not result['HasTrustedPlatformAssemblies']:
                raise ValueError('Expected the managed benchmark child')
            current = (result['Records'], result['Outputs'])
            if fingerprints is None:
                fingerprints = current
            elif current != fingerprints:
                raise ValueError('Cross-variant/mode output mismatch')
    return {variant: {mode: statistics.median(t['result']['ElapsedMs'] for t in trials
                                             if t['variant'] == variant and t['mode'] == mode)
                      for mode in ('tree', 'stream')} for variant in ('baseline', 'candidate')}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', required=True, type=Path)
    parser.add_argument('--candidate', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--rounds', type=int, default=3)
    args = parser.parse_args()
    if args.rounds < 3:
        parser.error('At least three rounds are required')
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    dotnet = shutil.which('dotnet')
    if not dotnet:
        raise RuntimeError('dotnet is required')
    profile = module('fork_profile', 'profile-streaming-cpu.py')
    gate = module('fork_gate', 'streaming-gate.py')
    size, heap = 100_000_000, 4_294_967_296
    env = profile.environment(os.environ, heap)
    report = dict(acceptance=False, complete=False, trials=[], sources={}, commands=[],
                  interpretation='Paired diagnostic only. Original full P7 qualification remains required.')

    def save():
        (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')

    def run(label, command, cwd):
        report['commands'].append(dict(label=label, argv=list(map(str, command))))
        save()
        with (output / (label + '.stdout')).open('w') as stdout, (output / (label + '.stderr')).open('w') as stderr:
            process = subprocess.run(command, cwd=cwd, env=env, stdout=stdout, stderr=stderr, timeout=300)
        if process.returncode:
            raise RuntimeError(f'{label} failed with exit {process.returncode}; evidence retained')
        return (output / (label + '.stdout')).read_text()

    assemblies = {}
    for variant, root in [('baseline', args.baseline.resolve()), ('candidate', args.candidate.resolve())]:
        commit = run(variant + '-commit', ['git', 'rev-parse', 'HEAD'], root).strip()
        if run(variant + '-status', ['git', 'status', '--porcelain', '--untracked-files=no'], root).strip():
            raise RuntimeError('Tracked source modifications in ' + variant)
        tracked = run(variant + '-files', ['git', 'ls-files', 'src', 'bench', 'Directory.Build.props',
                                         'Directory.Build.targets', 'global.json'], root).splitlines()
        hashes = {name: hashlib.sha256((root / name).read_bytes()).hexdigest()
                  for name in tracked if (root / name).is_file()}
        run(variant + '-build', [dotnet, 'build', 'bench/Xsl3.Benchmarks', '-c', 'Release', '-f',
                                'net10.0', '-m:1', '-nr:false', '-p:UseSharedCompilation=false',
                                '-p:NuGetAudit=false'], root)
        assembly = root / 'bench/Xsl3.Benchmarks/bin/Release/net10.0/Xsl3.Benchmarks.dll'
        runtime = json.loads(run(variant + '-runtime', [dotnet, str(assembly), 'streaming-info'], root))
        if runtime['Architecture'] != 'X64' or runtime['ProcessorCount'] != 4:
            raise RuntimeError('Expected the hosted x64 four-processor allowance')
        report['sources'][variant] = dict(commit=commit, runtime=runtime, sourceHashes=hashes,
                                         binaryHashes={p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                                       for p in assembly.parent.iterdir()
                                                       if p.suffix in ('.dll', '.pdb', '.json')})
        assemblies[variant] = assembly
        save()
    if report['sources']['baseline']['runtime'] != report['sources']['candidate']['runtime']:
        raise RuntimeError('Benchmark runtime environments differ')
    order = [('baseline', 'tree'), ('candidate', 'stream'), ('candidate', 'tree'), ('baseline', 'stream')]
    for round_number in range(args.rounds):
        for variant, mode in order if round_number % 2 == 0 else reversed(order):
            label = f'{round_number + 1}-{variant}-{mode}'
            result = json.loads(run(label, [dotnet, str(assemblies[variant]), 'streaming-child',
                                           'fork', mode, str(size), '0'], assemblies[variant].parent))
            report['trials'].append(dict(variant=variant, mode=mode, round=round_number + 1,
                                         exitCode=0, result=result))
            save()
            print(label, result.get('ElapsedMs'), flush=True)
    medians = validate_trials(report['trials'], args.rounds, gate, size, heap)
    report.update(complete=True, medians=medians,
                  candidateToBaselineStreamRatio=medians['candidate']['stream'] / medians['baseline']['stream'],
                  streamToTreeRatios={v: m['stream'] / m['tree'] for v, m in medians.items()})
    save()
    print(json.dumps({k: report[k] for k in ('medians', 'candidateToBaselineStreamRatio', 'streamToTreeRatios')}))


if __name__ == '__main__':
    main()
