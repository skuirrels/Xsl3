# Xsl3 streaming versus tree benchmark — 23 October 2026

Measured against the local development working copy, using Release .NET 10 on macOS ARM64.
The three workloads use identical input and stylesheet text for both modes. All nine size/workload
comparisons produced byte-identical tree and streaming output, each also matching independently
generated expected output. Streaming remains a development feature.

Tree execution wins conversion at all three sizes and filtering at 1 and 10 MB. The 5 MB filtering
timings are too variable to declare a reliable winner. Streaming wins the final-sum workload at
5 and 10 MB; the 1 MB sum has no meaningful winner. Streaming delivers conversion/filter output
in under 1 ms at every size, and cuts sampled peak RSS by 17–24% at 10 MB. At 1 MB it has no
memory advantage. These conclusions apply to this measured working copy and these workloads.

## Results

Times and RSS are medians. Input and RSS MB use 1,000,000 bytes. Lower is better.

### Execution time

| Input | Workload | Tree (.NET 10), ms | Streaming (.NET 10), ms | Streaming / tree | Winner |
|---|---|---:|---:|---:|---|
| 1 MB | convert | 13.17 | 29.13 | 2.21× | Tree |
| 1 MB | filter | 9.30 | 15.70 | 1.69× | Tree |
| 1 MB | sum | 6.29 | 6.39 | 1.02× | No meaningful winner |
| 5 MB | convert | 69.83 | 143.86 | 2.06× | Tree |
| 5 MB | filter | 64.34 | 73.39 | 1.14× | No meaningful winner |
| 5 MB | sum | 39.95 | 31.07 | 0.78× | Streaming |
| 10 MB | convert | 145.76 | 285.91 | 1.96× | Tree |
| 10 MB | filter | 120.12 | 145.43 | 1.21× | Tree |
| 10 MB | sum | 87.05 | 62.04 | 0.71× | Streaming |

### Sampled peak process RSS

Absolute RSS includes the loaded engine, runtime, compiler code and committed GC pages.
These are externally sampled transform-phase peaks, not allocation totals or retained-heap measurements.

| Input | Workload | Tree, MB | Streaming, MB | Change with streaming | Winner |
|---|---|---:|---:|---:|---|
| 1 MB | convert | 86.92 | 90.32 | +3.9% | No meaningful winner |
| 1 MB | filter | 88.05 | 90.14 | +2.4% | No meaningful winner |
| 1 MB | sum | 82.79 | 86.75 | +4.8% | No meaningful winner |
| 5 MB | convert | 101.83 | 90.32 | -11.3% | Streaming |
| 5 MB | filter | 103.92 | 90.42 | -13.0% | Streaming |
| 5 MB | sum | 95.13 | 91.26 | -4.1% | No meaningful winner |
| 10 MB | convert | 116.54 | 90.44 | -22.4% | Streaming |
| 10 MB | filter | 119.03 | 90.31 | -24.1% | Streaming |
| 10 MB | sum | 110.69 | 91.98 | -16.9% | Streaming |

### Time to first serialized output

The first nonempty stream write includes normal serializer buffering. Sum produces one final result
after consuming the document; it cannot provide a partial final answer.

| Input | Workload | Tree, ms | Streaming, ms | Winner |
|---|---|---:|---:|---|
| 1 MB | convert | 6.189 | 0.341 | Streaming |
| 1 MB | filter | 6.257 | 0.764 | Streaming |
| 1 MB | sum | 6.282 | 6.385 | No meaningful winner |
| 5 MB | convert | 38.310 | 0.350 | Streaming |
| 5 MB | filter | 47.369 | 0.777 | Streaming |
| 5 MB | sum | 39.946 | 31.062 | Streaming |
| 10 MB | convert | 78.062 | 0.338 | Streaming |
| 10 MB | filter | 77.937 | 0.789 | Streaming |
| 10 MB | sum | 87.046 | 62.032 | Streaming |

## Variation and baseline memory

Execution winners require a median difference over 5% and a 95% paired bootstrap interval that
excludes equal performance. The bootstrap resamples the nine stream/tree timing-pair ratios
10,000 times with a fixed seed; this describes this local run, not all hosts or workloads.
RSS winners use a descriptive 5% median difference threshold across three memory observations;
small RSS differences are labelled no meaningful winner.

| Input | Workload | Tree execution range, ms | Streaming execution range, ms | Stream/tree paired ratio 95% interval | Baseline RSS tree / streaming, MB |
|---|---|---:|---:|---:|---:|
| 1 MB | convert | 12.56–13.94 | 27.63–35.57 | 2.10–2.41 | 82.36 / 89.52 |
| 1 MB | filter | 8.99–9.42 | 15.38–17.96 | 1.65–1.89 | 83.03 / 87.83 |
| 1 MB | sum | 6.16–6.77 | 6.30–7.33 | 0.97–1.07 | 80.82 / 84.85 |
| 5 MB | convert | 67.78–75.32 | 140.51–154.33 | 1.99–2.11 | 82.48 / 89.49 |
| 5 MB | filter | 53.31–169.18 | 72.05–237.68 | 0.96–1.35 | 82.89 / 88.06 |
| 5 MB | sum | 38.48–54.59 | 30.77–31.77 | 0.63–0.81 | 80.89 / 84.84 |
| 10 MB | convert | 142.89–151.52 | 279.87–298.18 | 1.91–2.05 | 82.53 / 89.54 |
| 10 MB | filter | 117.70–128.84 | 143.85–159.46 | 1.21–1.24 | 83.12 / 87.93 |
| 10 MB | sum | 84.67–88.18 | 61.14–64.28 | 0.70–0.73 | 80.90 / 84.75 |

## Correctness and progressive output

| Input | Records | Expected sum | Conversion bytes before first output, tree / streaming | Filtering bytes before first output, tree / streaming | Output verification |
|---|---:|---:|---:|---:|---|
| 1 MB | 6,909 | 3,416,073 | 1,000,000 / 12,288 | 1,000,000 / 49,152 | Both modes match expected output for every workload |
| 5 MB | 34,348 | 17,017,027 | 5,000,000 / 12,288 | 5,000,000 / 49,152 | Both modes match expected output for every workload |
| 10 MB | 68,620 | 34,170,928 | 10,000,000 / 12,288 | 10,000,000 / 49,152 | Both modes match expected output for every workload |

## Method and limits

- 9 timing observations and 3 memory observations per mode/case: 216 measured child processes, plus 18 full-output correctness processes.
- Fresh processes run serially, with alternating engine order. Each compiles the same stylesheet,
  warms five transformations on a 65,536-byte corpus using the same wrappers/sink, then requests a full GC.
- Execution includes input parsing, transformation and serialization to a counting/discarding stream.
  Compilation, warmup, process startup and full-output verification are outside the timed interval.
- Timing runs have no RSS sampler. Separate memory runs sample the child externally around every 1 ms;
  sampling can miss brief peaks. Raw data includes every sample count, maximum interval, baseline,
  lifetime process peak, allocation totals and GC collection counts.
- Fresh-process/small-input warmup measures warmed code without a heap pre-sized for the full input; this
  does not establish sustained service performance with repeated full-size inputs and different GC history.
- Filesystem-cache-warm local input; no network, artificial input delay or output disk speed.
- Tiered compilation is disabled in both modes to stabilize JIT policy. Results apply to that configuration.
- The first exploratory matrix was superseded by this complete pass after two stalled setup build processes
  were stopped. Its raw data remains in the adjacent non-final artifact directory.
- These results cover three synthetic one-pass stylesheets, one machine and .NET 10. They do not establish
  a universal crossover size, streaming throughput on other constructs, or production memory guarantees.

## Provenance and reproduction

- Platform: `macOS-27.0.1-arm64-arm-64bit-Mach-O`; `arm64`.
- SDK: `10.0.400`; runtime: `10.0.11`; processors visible to runtime: 14.
- Base Git commit: `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`; branch: `feature/p7-streaming-analysis`; existing development changes are included.
- Measured Xsl3.dll SHA-256: `5ace7edd07729fd1404bc771f1f6d0dc60ac49101961a8852e8d8b6552cc54cf`.
- Source-tree fingerprint: `8ab59889028ba4ccea3fc57d1ab9db8963a75302fb7d2a3d584d7c969c165bc8`.
- Raw observations: [results.json](/Users/tyomidi/Source/Xsl3/artifacts/streaming-benchmark-2026-10-23-final/results.json).
- Working-copy state: [git-status.txt](/Users/tyomidi/Source/Xsl3/artifacts/streaming-benchmark-2026-10-23-final/git-status.txt).
- Harness and measurement details: [README.md](/Users/tyomidi/Source/Xsl3/bench/Xsl3.StreamingBenchmarks/README.md).

```sh
dotnet build bench/Xsl3.StreamingBenchmarks -c Release --disable-build-servers -p:UseSharedCompilation=false
python3 bench/Xsl3.StreamingBenchmarks/run.py --sizes 1,5,10 --timing-runs 9 --memory-runs 3 --output artifacts/streaming-benchmark-2026-10-23-final
python3 bench/Xsl3.StreamingBenchmarks/summarize.py artifacts/streaming-benchmark-2026-10-23-final/results.json --output docs/streaming-benchmark-2026-10-23.md
```
