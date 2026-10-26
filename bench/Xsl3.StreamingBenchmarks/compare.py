#!/usr/bin/env python3
"""Interleave frozen before/after binaries with the same corpus, sink and RSS sampler."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import statistics
import struct
import subprocess
import sys

sys.dont_write_bytecode = True
from run import ROOT, WORKLOADS, ALL_WORKLOADS, corpus, digest, rss_reader, run_child, same_bytes

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--before-dll', type=Path, required=True)
p.add_argument('--after-dll', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--sizes', default='1,5,10')
p.add_argument('--workloads', default=','.join(WORKLOADS))
p.add_argument('--timing-runs', type=int, default=9)
p.add_argument('--memory-runs', type=int, default=3)
a = p.parse_args()
if min(a.timing_runs, a.memory_runs) < 1:
    p.error('Run counts must be positive')
sizes = [int(s) for s in a.sizes.split(',')]
workloads = a.workloads.split(',')
if not workloads or any(w not in ALL_WORKLOADS for w in workloads):
    p.error('Workloads must be selected from ' + ','.join(ALL_WORKLOADS))
if min(sizes) < 1:
    p.error('Sizes must be positive integer MB')
out = a.output.resolve(); out.mkdir(parents=True, exist_ok=True)
dlls = {'before': a.before_dll.resolve(), 'after': a.after_dll.resolve()}
variants = [('before', 'tree'), ('after', 'tree'), ('before', 'stream'), ('after', 'stream')]
rss = rss_reader()
warmup = out / 'warmup.xml'; corpus(warmup, 65536)
fingerprint = hashlib.sha256()
for source in sorted(p for p in (ROOT / 'src/Xsl3').rglob('*') if p.is_file() and not {'bin', 'obj'} & set(p.parts)):
    fingerprint.update(str(source.relative_to(ROOT)).encode()); fingerprint.update(source.read_bytes())
d = {'method': {'sizesMB': sizes, 'timingRuns': a.timing_runs, 'memoryRuns': a.memory_runs,
                'warmupBytes': 65536, 'warmupTransforms': 5, 'mbBytes': 1000000,
                'order': 'all four variants interleaved; rotate and reverse order by repetition',
                'timing': 'parse + transform + serialized counting sink, compilation excluded',
                'memory': 'separate externally sampled transform-phase absolute RSS, target 1 ms',
                'jit': 'DOTNET_TieredCompilation=0', 'workloads': workloads},
     'environment': {'platform': platform.platform(), 'candidateSourceSha256': fingerprint.hexdigest(),
                     'gitHead': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
                     'binaries': {label: {'path': str(dll), 'harnessSha256': digest(dll),
                                          'librarySha256': digest(dll.parent/'Xsl3.dll')} for label,dll in dlls.items()}},
     'corpora': {}, 'correctness': [], 'runs': [], 'summary': []}
def save():
    temp = out / 'results.json.tmp'; temp.write_text(json.dumps(d, indent=2)+'\n'); temp.replace(out/'results.json')
for size in sizes:
    folder = out / f'{size}mb'; folder.mkdir(exist_ok=True)
    input_path = folder / 'input.xml'
    d['corpora'][str(size)] = corpus(input_path, size*1000000, folder)
    for workload in workloads:
        for variant, mode in variants:
            actual = folder / f'{workload}-{variant}-{mode}.actual'
            r = run_child(dlls[variant], mode, workload, input_path, warmup, actual, False, rss)
            if not same_bytes(actual, folder/f'{workload}.expected'):
                raise AssertionError(f'{size} MB {workload} {variant} {mode} differs from expected output')
            if workload in ('avg', 'min', 'max'):
                actual_bits = struct.pack('>d', float(actual.read_text())).hex()
                if actual_bits != d['corpora'][str(size)]['expectedFloat64Bits'][workload]:
                    raise AssertionError('Aggregate binary64 result differs from independently calculated value')
            d['correctness'].append({'sizeMB': size, 'workload': workload, 'variant': variant, 'mode': mode,
                                    'identicalToExpected': True, 'sha256': digest(actual), 'outputBytes': r['outputBytes']})
        for kind, repetitions in [('timing', a.timing_runs), ('memory', a.memory_runs)]:
            for rep in range(repetitions):
                ordered = variants[rep%4:] + variants[:rep%4]
                if rep%2:
                    ordered = ordered[::-1]
                for variant, mode in ordered:
                    r = run_child(dlls[variant], mode, workload, input_path, warmup, None, kind=='memory', rss)
                    if r['outputBytes'] != (folder/f'{workload}.expected').stat().st_size:
                        raise AssertionError('Measured output length mismatch')
                    r.update(sizeMB=size, variant=variant, kind=kind, repetition=rep+1)
                    d['runs'].append(r)
                save()
        for variant, mode in variants:
            runs = [r for r in d['runs'] if r['sizeMB']==size and r['workload']==workload and r['variant']==variant and r['mode']==mode]
            times = [r for r in runs if r['kind']=='timing']; memory = [r for r in runs if r['kind']=='memory']
            d['summary'].append({'sizeMB': size, 'workload': workload, 'variant': variant, 'mode': mode,
                'elapsedMs': statistics.median(r['elapsedMs'] for r in times),
                'minElapsedMs': min(r['elapsedMs'] for r in times), 'maxElapsedMs': max(r['elapsedMs'] for r in times),
                'firstOutputMs': statistics.median(r['firstOutputMs'] for r in times),
                'inputBytesAtFirstOutput': statistics.median(r['inputBytesAtFirstOutput'] for r in times),
                'allocatedMB': statistics.median(r['allocatedBytes'] for r in times)/1e6,
                'peakRssMB': statistics.median(r['phasePeakRssBytes'] for r in memory)/1e6,
                'baselineRssMB': statistics.median(r['baselineRssBytes'] for r in memory)/1e6,
                'maxObservedRssMB': max(r['phasePeakRssBytes'] for r in memory)/1e6})
        save()
        print(f'{size} MB {workload}: four outputs verified; before/after timing and RSS complete', flush=True)
for variant, dll in dlls.items():
    if digest(dll.parent/'Xsl3.dll') != d['environment']['binaries'][variant]['librarySha256']:
        raise AssertionError('A frozen library binary changed during measurement')
print(out/'results.json', flush=True)
