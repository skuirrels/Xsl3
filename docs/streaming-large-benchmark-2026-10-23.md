# Optimized streaming versus tree at 50, 100 and 500 MB — 23 October 2026

Both modes use one frozen current development build of Xsl3, Release .NET 10.0.11, macOS ARM64.
The preceding arithmetic/literal atomization, skipped-subtree and attribute-header optimizations are present.
This is a newly frozen current checkout, including other work since the earlier 1–10 MB investigation;
its source manifest and exact binaries identify the measured version. It is not a published-package benchmark.

**All 18 complete outputs are byte-identical to the independently generated expected results,**
and tree/streaming outputs agree for each of the nine size/workload cases.

Streaming wins execution time in all nine measured cases. At 500 MB it is **1.46–2.34 times faster**
and uses **90–94% less sampled peak RSS**. Conversion/filtering retain submillisecond first output;
aggregation produces its final total only after reading the full input.

## Execution time

Medians of nine observations per mode/case. Seconds; lower is better. Input MB means 1,000,000 bytes.

| Input | Workload | Tree (.NET 10.0.11), s | Streaming (.NET 10.0.11), s | Streaming speedup | Winner |
|---|---|---:|---:|---:|---|
| 50 MB | convert | 0.718 | 0.607 | 1.18× | Streaming |
| 50 MB | filter | 0.601 | 0.449 | 1.34× | Streaming |
| 50 MB | sum | 0.451 | 0.302 | 1.49× | Streaming |
| 100 MB | convert | 1.526 | 1.181 | 1.29× | Streaming |
| 100 MB | filter | 1.304 | 0.871 | 1.50× | Streaming |
| 100 MB | sum | 1.053 | 0.582 | 1.81× | Streaming |
| 500 MB | convert | 9.084 | 6.206 | 1.46× | Streaming |
| 500 MB | filter | 7.743 | 4.310 | 1.80× | Streaming |
| 500 MB | sum | 6.345 | 2.714 | 2.34× | Streaming |

Winners require a median difference greater than 5% and a 95% paired-bootstrap interval excluding
equal performance: 10,000 resamples of the nine stream/tree timing ratios, fixed seed.

## Sampled peak process memory

Medians of three separately sampled transform-phase RSS observations. Decimal MB; lower is better.
RSS includes runtime, loaded/JIT code and committed GC pages; this is not cumulative allocations or a retained-heap bound.

| Input | Workload | Tree peak RSS, MB | Streaming peak RSS, MB | Reduction | Winner |
|---|---|---:|---:|---:|---|
| 50 MB | convert | 238.98 | 86.84 | 63.7% | Streaming |
| 50 MB | filter | 240.63 | 89.00 | 63.0% | Streaming |
| 50 MB | sum | 236.06 | 94.45 | 60.0% | Streaming |
| 100 MB | convert | 402.95 | 86.87 | 78.4% | Streaming |
| 100 MB | filter | 403.57 | 89.15 | 77.9% | Streaming |
| 100 MB | sum | 402.42 | 101.43 | 74.8% | Streaming |
| 500 MB | convert | 1436.40 | 87.05 | 93.9% | Streaming |
| 500 MB | filter | 1375.17 | 89.37 | 93.5% | Streaming |
| 500 MB | sum | 1500.23 | 148.21 | 90.1% | Streaming |

Conversion and filtering streaming RSS stay near 87 MB and 89 MB respectively across these sizes.
Aggregation RSS is not flat: it rises from 94 MB to 101 MB to 148 MB. At 500 MB this is still 90%
below tree. These process-residency measurements alone do not distinguish live-object retention
from committed GC pages and other runtime working memory.

## Time to first output

Medians from the nine timing observations. Milliseconds to the first nonempty serialized byte write,
including the normal serializer buffering. This is delivery to the caller, not network delivery.

| Input | Workload | Tree first output, ms | Streaming first output, ms | Winner |
|---|---|---:|---:|---|
| 50 MB | convert | 431.033 | 0.167 | Streaming |
| 50 MB | filter | 424.841 | 0.478 | Streaming |
| 50 MB | sum | 450.739 | 301.896 | Streaming |
| 100 MB | convert | 967.118 | 0.163 | Streaming |
| 100 MB | filter | 995.509 | 0.439 | Streaming |
| 100 MB | sum | 1053.193 | 582.234 | Streaming |
| 500 MB | convert | 6232.571 | 0.172 | Streaming |
| 500 MB | filter | 6124.720 | 0.455 | Streaming |
| 500 MB | sum | 6344.996 | 2713.534 | Streaming |

Streamed conversion first writes after **12,288 input bytes** and filtering after **49,152 bytes**,
at every requested size. Tree reads the complete input before output. Sum emits one final total after
reading the complete input in both modes, so streaming cannot provide an early partial final answer.

## Correctness

| Input | Records | Independent expected sum | Conversion bytes | Filtering bytes | Result |
|---|---:|---:|---:|---:|---|
| 50 MB | 341,143 | 170,160,560 | 32,601,581 | 6,520,263 | All three workloads identical |
| 100 MB | 681,530 | 339,963,038 | 65,241,905 | 13,048,387 | All three workloads identical |
| 500 MB | 3,388,366 | 1,690,672,834 | 327,193,308 | 65,438,649 | All three workloads identical |

The generator independently derives expected lines and totals from the synthetic input fields.
Untimed correctness runs write complete output files and compare them byte for byte against expected
output and against each other. SHA-256 digests and lengths are retained; the report also rehashes each
actual output. Measured runs verify output length and complete input consumption without hashing in the timed interval.

## Variation and memory baselines

| Input | Workload | Tree time range, s | Streaming time range, s | Baseline RSS tree / stream, MB | Max observed peak RSS tree / stream, MB |
|---|---|---:|---:|---:|---:|
| 50 MB | convert | 0.702–0.813 | 0.587–0.678 | 82.69 / 83.53 | 239.65 / 86.98 |
| 50 MB | filter | 0.573–1.048 | 0.430–0.766 | 83.03 / 83.36 | 240.75 / 89.05 |
| 50 MB | sum | 0.428–0.570 | 0.278–0.369 | 81.13 / 81.26 | 236.18 / 94.50 |
| 100 MB | convert | 1.497–1.578 | 1.173–1.203 | 82.43 / 83.49 | 403.42 / 86.90 |
| 100 MB | filter | 1.258–1.369 | 0.859–0.918 | 83.05 / 83.48 | 405.77 / 89.28 |
| 100 MB | sum | 0.982–1.850 | 0.546–1.002 | 81.08 / 81.31 | 402.46 / 101.52 |
| 500 MB | convert | 9.010–9.583 | 5.906–7.366 | 82.58 / 83.54 | 1447.69 / 87.20 |
| 500 MB | filter | 7.556–8.247 | 4.272–4.803 | 83.33 / 83.53 | 1512.21 / 89.39 |
| 500 MB | sum | 6.203–6.914 | 2.693–2.822 | 81.20 / 81.17 | 1507.80 / 148.37 |

## Method and limits

- Nine size/workload cases, two modes, nine timing and three memory runs per mode: **216 measured
  transformations**, plus **18** complete-output correctness transformations. Modes alternate order
  within each repetition and observations run serially. No profiler or memory sampler runs during timing.
- Public `XslCompiledTransform.Load` and `Transform(XmlReader, ..., Stream)` entry points. The same
  stylesheet declares a streamable mode for both; `XSL3_STREAM_STRICT=0` selects tree and `=1` streaming.
- Conversion writes every record as `id|code|value|label`; filtering writes records with `@id mod 5 = 0`;
  sum computes `xs:integer(sum(doc/record/@value))`. These are synthetic one-pass workloads.
- Timing includes reader creation, parsing, transformation and serialization to a counting/discarding sink.
  Source compilation, process startup and five 65,536-byte warmup transformations are excluded. Full GC
  follows warmup. Every observation is a fresh child process; tiered compilation is disabled.
- Input is cache-warm local XML; correctness passes read it before timed passes. There is no network,
  downstream backpressure, output disk I/O or hashing during measured transformation.
- Memory is absolute RSS externally sampled about every millisecond during separately signalled execution.
  Maximum observed sampler gap: **33.83 ms**; minimum samples per run: **220**.
  Short peaks between samples may be missed. OS residency, compression and GC policy affect process RSS.
- Host: **Apple M4 Pro, 14 logical CPUs, 24 GiB installed RAM**. Results describe this host,
  runtime, fresh-process policy and record shape;
  they do not establish universal throughput, cold storage behavior, sustained-service heap behavior,
  streaming conformance, arbitrary stylesheet memory bounds or release readiness.
- No engine or application source was changed for this benchmark. Binary hashes and all frozen source
  checksums were verified again after measurement. Unrelated live-checkout changes were preserved.

- Engine SHA-256: `5e7fabedbcf42d2bc3cf6a899b7a533f789e69b9b0541dacf31e394f0b6322db`.
- SDK: `10.0.400`; platform: `macOS-27.0.1-arm64-arm-64bit-Mach-O`.
- [Raw observations](/Users/tyomidi/Source/Xsl3/artifacts/streaming-large-benchmark-2026-10-23/measurement/results.json).
- [Paired statistics](/Users/tyomidi/Source/Xsl3/artifacts/streaming-large-benchmark-2026-10-23/statistics.json).
- [Source manifest](/Users/tyomidi/Source/Xsl3/artifacts/streaming-large-benchmark-2026-10-23/source-manifest.json).
- [Build evidence](/Users/tyomidi/Source/Xsl3/artifacts/streaming-large-benchmark-2026-10-23/build-evidence.json).
- [Benchmark harness and definitions](/Users/tyomidi/Source/Xsl3/bench/Xsl3.StreamingBenchmarks/README.md).

```sh
python3 artifacts/streaming-large-benchmark-2026-10-23/source/bench/Xsl3.StreamingBenchmarks/run.py \
  --sizes 50,100,500 --timing-runs 9 --memory-runs 3 \
  --output artifacts/streaming-large-benchmark-2026-10-23/measurement
```
