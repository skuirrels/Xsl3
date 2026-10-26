#!/usr/bin/env python3
"""Render the complete benchmark observations as a Markdown comparison."""
import argparse
import json
from pathlib import Path
import random
import statistics

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('results', type=Path)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
d = json.loads(a.results.read_text())
sizes = d['method']['sizesMB']
workloads = list(d['method']['workloads'])
expected_runs = len(sizes) * len(workloads) * 2 * (d['method']['timingRuns'] + d['method']['memoryRuns'])
assert len(d['runs']) == expected_runs
assert len(d['correctness']) == len(sizes) * len(workloads) * 2
assert all(r['identicalToExpected'] for r in d['correctness'])
rows = {(r['sizeMB'], r['workload'], r['mode']): r for r in d['summary']}
assert len(rows) == len(sizes) * len(workloads) * 2

def time_verdict(size, workload, metric='elapsedMs'):
    runs = {(r['mode'], r['repetition']): r for r in d['runs']
            if r['kind'] == 'timing' and r['sizeMB'] == size and r['workload'] == workload}
    ratios = [runs['stream', n][metric] / runs['tree', n][metric]
              for n in range(1, d['method']['timingRuns'] + 1)]
    rng = random.Random(20261023)
    boot = sorted(statistics.median(rng.choices(ratios, k=len(ratios))) for _ in range(10000))
    lo, hi = boot[249], boot[9749]
    ratio = rows[size, workload, 'stream'][metric] / rows[size, workload, 'tree'][metric]
    verdict = 'Tree' if ratio > 1.05 and lo > 1 else 'Streaming' if ratio < .95 and hi < 1 else 'No meaningful winner'
    return verdict, ratio, lo, hi

def link(path):
    return '/' + str(path.resolve()).lstrip('/')

text = ['# Xsl3 streaming versus tree benchmark — 23 October 2026', '',
        'Measured against the local development working copy, using Release .NET 10 on macOS ARM64.',
        'The three workloads use identical input and stylesheet text for both modes. All nine size/workload',
        'comparisons produced byte-identical tree and streaming output, each also matching independently',
        'generated expected output. Streaming remains a development feature.', '',
        'Tree execution wins conversion at all three sizes and filtering at 1 and 10 MB. The 5 MB filtering',
        'timings are too variable to declare a reliable winner. Streaming wins the final-sum workload at',
        '5 and 10 MB; the 1 MB sum has no meaningful winner. Streaming delivers conversion/filter output',
        'in under 1 ms at every size, and cuts sampled peak RSS by 17–24% at 10 MB. At 1 MB it has no',
        'memory advantage. These conclusions apply to this measured working copy and these workloads.', '',
        '## Results', '',
        'Times and RSS are medians. Input and RSS MB use 1,000,000 bytes. Lower is better.', '',
        '### Execution time', '',
        '| Input | Workload | Tree (.NET 10), ms | Streaming (.NET 10), ms | Streaming / tree | Winner |',
        '|---|---|---:|---:|---:|---|']
for size in sizes:
    for w in workloads:
        t, s = rows[size, w, 'tree'], rows[size, w, 'stream']
        win, ratio, lo, hi = time_verdict(size, w)
        text.append(f'| {size} MB | {w} | {t["elapsedMs"]:.2f} | {s["elapsedMs"]:.2f} | {ratio:.2f}× | {win} |')
text += ['', '### Sampled peak process RSS', '',
         'Absolute RSS includes the loaded engine, runtime, compiler code and committed GC pages.',
         'These are externally sampled transform-phase peaks, not allocation totals or retained-heap measurements.', '',
         '| Input | Workload | Tree, MB | Streaming, MB | Change with streaming | Winner |',
         '|---|---|---:|---:|---:|---|']
for size in sizes:
    for w in workloads:
        t, s = rows[size, w, 'tree'], rows[size, w, 'stream']
        delta = (s['peakRssMB'] / t['peakRssMB'] - 1) * 100
        win = 'Streaming' if delta < -5 else 'Tree' if delta > 5 else 'No meaningful winner'
        text.append(f'| {size} MB | {w} | {t["peakRssMB"]:.2f} | {s["peakRssMB"]:.2f} | {delta:+.1f}% | {win} |')
text += ['', '### Time to first serialized output', '',
         'The first nonempty stream write includes normal serializer buffering. Sum produces one final result',
         'after consuming the document; it cannot provide a partial final answer.', '',
         '| Input | Workload | Tree, ms | Streaming, ms | Winner |',
         '|---|---|---:|---:|---|']
for size in sizes:
    for w in workloads:
        t, s = rows[size, w, 'tree'], rows[size, w, 'stream']
        win, *_ = time_verdict(size, w, 'firstOutputMs')
        text.append(f'| {size} MB | {w} | {t["firstOutputMs"]:.3f} | {s["firstOutputMs"]:.3f} | {win} |')
text += ['', '## Variation and baseline memory', '',
         'Execution winners require a median difference over 5% and a 95% paired bootstrap interval that',
         'excludes equal performance. The bootstrap resamples the nine stream/tree timing-pair ratios',
         '10,000 times with a fixed seed; this describes this local run, not all hosts or workloads.',
         'RSS winners use a descriptive 5% median difference threshold across three memory observations;',
         'small RSS differences are labelled no meaningful winner.', '',
         '| Input | Workload | Tree execution range, ms | Streaming execution range, ms | Stream/tree paired ratio 95% interval | Baseline RSS tree / streaming, MB |',
         '|---|---|---:|---:|---:|---:|']
for size in sizes:
    for w in workloads:
        t, s = rows[size, w, 'tree'], rows[size, w, 'stream']
        _, _, lo, hi = time_verdict(size, w)
        text.append(f'| {size} MB | {w} | {t["minElapsedMs"]:.2f}–{t["maxElapsedMs"]:.2f} | {s["minElapsedMs"]:.2f}–{s["maxElapsedMs"]:.2f} | {lo:.2f}–{hi:.2f} | {t["baselineRssMB"]:.2f} / {s["baselineRssMB"]:.2f} |')
text += ['', '## Correctness and progressive output', '',
         '| Input | Records | Expected sum | Conversion bytes before first output, tree / streaming | Filtering bytes before first output, tree / streaming | Output verification |',
         '|---|---:|---:|---:|---:|---|']
for size in sizes:
    c = d['corpora'][str(size)]
    ct, cs = rows[size, 'convert', 'tree'], rows[size, 'convert', 'stream']
    ft, fs = rows[size, 'filter', 'tree'], rows[size, 'filter', 'stream']
    text.append(f'| {size} MB | {c["records"]:,} | {c["expectedSum"]:,} | {ct["inputBytesAtFirstOutput"]:,} / {cs["inputBytesAtFirstOutput"]:,} | {ft["inputBytesAtFirstOutput"]:,} / {fs["inputBytesAtFirstOutput"]:,} | Both modes match expected output for every workload |')
text += ['', '## Method and limits', '',
         f'- {d["method"]["timingRuns"]} timing observations and {d["method"]["memoryRuns"]} memory observations per mode/case: {len(d["runs"])} measured child processes, plus {len(d["correctness"])} full-output correctness processes.',
         '- Fresh processes run serially, with alternating engine order. Each compiles the same stylesheet,',
         '  warms five transformations on a 65,536-byte corpus using the same wrappers/sink, then requests a full GC.',
         '- Execution includes input parsing, transformation and serialization to a counting/discarding stream.',
         '  Compilation, warmup, process startup and full-output verification are outside the timed interval.',
         '- Timing runs have no RSS sampler. Separate memory runs sample the child externally around every 1 ms;',
         '  sampling can miss brief peaks. Raw data includes every sample count, maximum interval, baseline,',
         '  lifetime process peak, allocation totals and GC collection counts.',
         '- Fresh-process/small-input warmup measures warmed code without a heap pre-sized for the full input; this',
         '  does not establish sustained service performance with repeated full-size inputs and different GC history.',
         '- Filesystem-cache-warm local input; no network, artificial input delay or output disk speed.',
         '- Tiered compilation is disabled in both modes to stabilize JIT policy. Results apply to that configuration.',
         '- The first exploratory matrix was superseded by this complete pass after two stalled setup build processes',
         '  were stopped. Its raw data remains in the adjacent non-final artifact directory.',
         '- These results cover three synthetic one-pass stylesheets, one machine and .NET 10. They do not establish',
         '  a universal crossover size, streaming throughput on other constructs, or production memory guarantees.', '',
         '## Provenance and reproduction', '',
         f'- Platform: `{d["environment"]["platform"]}`; `{d["environment"]["machine"]}`.',
         f'- SDK: `{d["environment"]["dotnet"]}`; runtime: `{d["runs"][0]["runtime"]}`; processors visible to runtime: {d["runs"][0]["processors"]}.',
         f'- Base Git commit: `{d["environment"]["gitHead"]}`; branch: `{d["environment"]["gitBranch"]}`; existing development changes are included.',
         f'- Measured Xsl3.dll SHA-256: `{d["environment"]["librarySha256"]}`.',
         f'- Source-tree fingerprint: `{d["environment"]["sourceSha256"]}`.',
         f'- Raw observations: [results.json]({link(a.results)}).',
         f'- Working-copy state: [git-status.txt]({link(a.results.parent / "git-status.txt")}).',
         f'- Harness and measurement details: [README.md]({link(Path(__file__).parent / "README.md")}).', '',
         '```sh',
         'dotnet build bench/Xsl3.StreamingBenchmarks -c Release --disable-build-servers -p:UseSharedCompilation=false',
         'python3 bench/Xsl3.StreamingBenchmarks/run.py --sizes 1,5,10 --timing-runs 9 --memory-runs 3 --output artifacts/streaming-benchmark-2026-10-23-final',
         'python3 bench/Xsl3.StreamingBenchmarks/summarize.py artifacts/streaming-benchmark-2026-10-23-final/results.json --output docs/streaming-benchmark-2026-10-23.md',
         '```', '']
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text('\n'.join(text))
print(a.output)
