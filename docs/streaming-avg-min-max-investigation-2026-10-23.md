# Streaming avg(), min() and max() investigation — 23 October 2026

All three functions used input-sized buffers for the benchmark’s direct untyped-node calls.
The forward compiler now uses the existing incremental QIL/runtime aggregates, with a small
double-result conversion helper. Results below compare frozen current source before and after.
The earlier incremental sum optimisation remains present in both versions.

## Cause and change

- The general `CallLibrary` path atomized and cached the complete argument for its list-based
  function interface. `Aggregates.Avg` then used `Prepare`/`Sum`, creating a list of numeric values.
  `Aggregates.MinMax` created converted values and a promoted-values list for untyped input.
- Direct one-argument untyped-node calls in forward mode now lower to native `Average`, `Minimum`
  or `Maximum` QIL. Existing IL generation and `DoubleAggregator` fold each converted value as it
  arrives. The compiler factory now exposes these previously unused node constructors.
- The optional native double is converted with `X31DoubleItem`, preserving `xs:double`, signed
  zero and NaN. Empty input yields no item; it does not acquire the integer-zero default of sum.
- The average counter is widened to 64 bits for long forward inputs. Shared untyped-node
  classification/conversion is reused by sum and these aggregates; arithmetic semantics stay in
  the existing runtime operators.
- Min/max optimisation requires the normal codepoint default collation. Explicit-collation,
  schema-aware, comment/PI/namespace string and other general input paths retain their canonical
  runtime behavior. This report establishes the direct untyped-node path, not every aggregate expression.
- No file-size condition, GC policy, forced collection, pooling threshold or fixture-dependent result
  is introduced. The benchmark still calls the public processor APIs.

Separate before/after allocation profiles were collected at 50 MB for each function. The original
profiles contain growing item/value arrays and list preparation; the optimized profiles contain no
sampled `XPathItem[]`/`AtomicValue[]` allocation event. Sampling is approximate and no CPU-percentage
attribution is claimed. Runtime/RSS conclusions come from separate unprofiled measurements.

## Execution time

Release .NET 10.0.11, macOS ARM64. Seconds, median of nine timing observations per variant/case.
Both engine versions use an identical benchmark executable and workloads.

| Input | Function | Tree before, s | Tree after, s | Streaming before, s | Streaming after, s | Streaming speedup | Winner after |
|---|---|---:|---:|---:|---:|---:|---|
| 50 MB | avg | 0.526 | 0.531 | 0.416 | 0.276 | 1.51× | Streaming |
| 50 MB | min | 0.519 | 0.518 | 0.411 | 0.276 | 1.49× | Streaming |
| 50 MB | max | 0.585 | 0.534 | 0.426 | 0.285 | 1.49× | Streaming |
| 100 MB | avg | 1.196 | 1.189 | 0.849 | 0.538 | 1.58× | Streaming |
| 100 MB | min | 1.211 | 1.174 | 0.884 | 0.562 | 1.57× | Streaming |
| 100 MB | max | 1.137 | 1.136 | 0.851 | 0.544 | 1.57× | Streaming |
| 500 MB | avg | 8.106 | 8.040 | 5.004 | 2.720 | 1.84× | Streaming |
| 500 MB | min | 7.938 | 7.693 | 4.897 | 2.708 | 1.81× | Streaming |
| 500 MB | max | 7.954 | 7.862 | 4.932 | 2.680 | 1.84× | Streaming |

Speedup compares new versus original streaming. Winner compares new streaming with new tree:
a median difference over 5% and a 95% paired-bootstrap interval excluding equal performance.
Bootstrap: 10,000 resamples of nine paired ratios, fixed seed.

## Peak process memory

Decimal MB; medians of three separate transform-phase RSS observations. This includes runtime,
JIT/compiler code and committed GC pages; it is not cumulative allocations or a retained-heap bound.

| Input | Function | Tree after peak RSS, MB | Streaming before peak RSS, MB | Streaming after peak RSS, MB | Reduction versus original streaming |
|---|---|---:|---:|---:|---:|
| 50 MB | avg | 279.71 | 129.56 | 87.44 | 32.5% |
| 50 MB | min | 271.22 | 127.11 | 87.38 | 31.3% |
| 50 MB | max | 270.88 | 127.14 | 87.39 | 31.3% |
| 100 MB | avg | 461.32 | 172.59 | 87.41 | 49.4% |
| 100 MB | min | 452.64 | 169.75 | 87.52 | 48.4% |
| 100 MB | max | 452.64 | 169.90 | 87.52 | 48.5% |
| 500 MB | avg | 1584.96 | 485.31 | 87.49 | 82.0% |
| 500 MB | min | 1658.88 | 503.91 | 87.57 | 82.6% |
| 500 MB | max | 1642.46 | 503.94 | 87.61 | 82.6% |

## First output and allocation

Every aggregate needs all its input to produce its final value. All four variants consume the full
file before the first serialized output byte. First output is therefore approximately completion
time; the optimisation does not change buffering of the final serialized result.

| Input | Function | Streaming first output before / after, ms | Streaming allocated before / after, MB |
|---|---|---:|---:|
| 50 MB | avg | 415.66 / 275.95 | 217.73 / 152.02 |
| 50 MB | min | 410.76 / 276.20 | 209.55 / 152.02 |
| 50 MB | max | 425.98 / 285.05 | 209.55 / 152.02 |
| 100 MB | avg | 848.80 / 538.39 | 435.78 / 304.49 |
| 100 MB | min | 884.18 / 561.63 | 419.43 / 304.49 |
| 100 MB | max | 851.07 / 543.72 | 419.43 / 304.49 |
| 500 MB | avg | 5004.04 / 2719.82 | 2153.36 / 1516.96 |
| 500 MB | min | 4896.72 / 2708.12 | 2072.04 / 1516.96 |
| 500 MB | max | 4931.80 / 2680.41 | 2072.04 / 1516.96 |

Allocated bytes are cumulative across execution; they still scale with the number of records.
The memory improvement removes the growing live numeric/atomic sequence buffers.

## Independent correctness

**All 36 complete primary outputs are byte-identical** across before/after tree/streaming and to
the independently generated expected output. The parsed IEEE-754 binary64 results also match
the independently calculated bit patterns. No rounding or integer truncation hides differences.

| Input | Records | Expected avg | Expected min | Expected max |
|---|---:|---:|---:|---:|
| 50 MB | 341,143 | 498.79540251448805 | 1 | 997 |
| 100 MB | 681,530 | 498.82329171129663 | 1 | 997 |
| 500 MB | 3,388,366 | 498.96405347002064 | 1 | 997 |

Expected values come from the synthetic generator: exact integer total/count for avg and tracked
actual extrema for min/max. The positive integer inputs and totals are exactly representable as
binary64 before the average division. Expected text uses shortest roundtrip representation.
Complete output files are compared outside timing, and measured observations check output lengths
and full input consumption. The report rechecks all complete output bytes, hashes and numeric bits.

## Regression and compatibility checks

- Full final unit suites: **2,058 passed on .NET 10; 2,028 on .NET 8**, zero failures/skips.
- New regressions cover empty/static-empty input, double result type, signed zero and ties,
  infinities, sticky NaN, late invalid values, addition order, repeated groups, lexical comment
  ordering/collations, exact schema-typed decimals and saved stylesheet assemblies.
- Debug aggregate, existing sum and streamed-QIL verifier tests: **63 passed**, zero failures.
- W3C strict-streaming `sf-avg,sf-min,sf-max`, with saved-assembly roundtrips for eligible cases:
  **105 passed, 4 existing failures, zero not run** before and after; reports are byte-identical.
  The four failures revisit streamed nodes in sf-min/sf-max cases 023 and 032. An initial stale
  runner produced 33 additional method-signature errors; rebuilding it against the frozen engine
  removed those errors in both versions. Only the rebuilt-runner results are used here.
- NativeAOT saved-stylesheet probe passed with dynamic code disabled and zero publish warnings.
  It checks nonempty, empty and signed-zero results for each function, output before the full
  grouped input ends, and a borrowed reader kept open.
- A 1 MB smoke comparison verified another 12 complete outputs across the four variants;
  its single-observation timings are not used for performance conclusions.
- Existing runtime entry points remain. The new primitive conversion helper is recorded in
  `PublicAPI.Unshipped.txt`. No unrelated source changes are replaced.

## Variation and baselines

| Input | Function | Streaming before time range, s | Streaming after time range, s | Baseline RSS before / after streaming, MB | Max observed new streaming peak RSS, MB |
|---|---|---:|---:|---:|---:|
| 50 MB | avg | 0.412–0.427 | 0.269–0.280 | 83.84 / 81.12 | 87.46 |
| 50 MB | min | 0.400–0.452 | 0.270–0.310 | 83.80 / 81.07 | 87.52 |
| 50 MB | max | 0.416–0.441 | 0.276–0.310 | 83.82 / 81.08 | 87.56 |
| 100 MB | avg | 0.831–0.896 | 0.526–0.547 | 83.69 / 81.07 | 87.59 |
| 100 MB | min | 0.855–1.057 | 0.542–0.669 | 83.87 / 81.18 | 87.56 |
| 100 MB | max | 0.844–0.867 | 0.535–0.551 | 83.92 / 81.20 | 87.54 |
| 500 MB | avg | 4.942–5.103 | 2.620–2.816 | 83.87 / 81.04 | 87.69 |
| 500 MB | min | 4.840–5.011 | 2.665–2.978 | 83.74 / 81.12 | 87.77 |
| 500 MB | max | 4.843–4.985 | 2.631–2.717 | 83.80 / 81.15 | 87.72 |

Optimized streaming peak medians across all requested cases span **87.38–87.61 MB**.

## Method and limits

- Nine cases, four rotating/reversing before/after tree/streaming variants, nine timing and three
  separate memory observations: **432 measured transformations**, plus **36** complete correctness
  transformations. Observations run serially; timing has no profiler or RSS sampler attached.
- Frozen source snapshots and copied Release binaries prevent concurrent checkout edits from changing
  the experiment. Both versions use byte-identical benchmark DLLs; hashes/checksums are reverified.
- Public `XslCompiledTransform.Load` / `Transform(XmlReader, ..., Stream)` paths and the same
  stylesheet/corpus in both modes. The original default workload set remains convert/filter/sum;
  avg/min/max are explicit additional benchmark selections.
- Fresh process for each observation, five 65,536-byte warmups, full GC before the measured phase,
  tiered compilation disabled. Source compilation, startup and warmup are excluded.
- Timing includes reader creation, XML parsing, transformation and serialization to a counting sink.
  Correctness-file writes, hashing and report generation occur outside measured timing.
- Cache-warm local synthetic XML with small repeated records; no network, output disk delay or
  backpressure in timing. This is not a sustained-service heap or arbitrary stylesheet guarantee.
- Memory uses external macOS RSS sampling targeting 1 ms in separate runs.
  Maximum observed sample gap: **27.39 ms**; minimum samples per run: **180**.
  Short peaks may be missed. Process residency and committed GC pages can vary independently of live data.
- Host: Apple M4 Pro, 14 logical CPUs, 24 GiB RAM. These measurements do not establish full P7
  conformance or release readiness; the existing W3C failures above remain.

- Before engine SHA-256: `9299df23f1ca6e5bc75597dbf4c7a66218dae66cf2953086badf95756be98831`.
- After engine SHA-256: `f390c58938f39b4650d001ee4062769a5e17a488caf5dbba621f606d052d77df`.
- [Raw comparison](/Users/tyomidi/Source/Xsl3/artifacts/streaming-avg-min-max-investigation-2026-10-23/comparison/results.json).
- [Statistics](/Users/tyomidi/Source/Xsl3/artifacts/streaming-avg-min-max-investigation-2026-10-23/statistics.json).
- [Validation](/Users/tyomidi/Source/Xsl3/artifacts/streaming-avg-min-max-investigation-2026-10-23/validation.json).
- [Engine and regression-test patch](/Users/tyomidi/Source/Xsl3/artifacts/streaming-avg-min-max-investigation-2026-10-23/engine-increment.patch).
- [Before W3C report](/Users/tyomidi/Source/Xsl3/artifacts/streaming-avg-min-max-investigation-2026-10-23/w3c-rebuilt-before.md), [after W3C report](/Users/tyomidi/Source/Xsl3/artifacts/streaming-avg-min-max-investigation-2026-10-23/w3c-rebuilt-after.md).
- [Harness and reproduction](/Users/tyomidi/Source/Xsl3/bench/Xsl3.StreamingBenchmarks/README.md).

```sh
python3 artifacts/streaming-avg-min-max-investigation-2026-10-23/source-after/bench/Xsl3.StreamingBenchmarks/compare.py \
  --before-dll artifacts/streaming-avg-min-max-investigation-2026-10-23/before-bin/Xsl3.StreamingBenchmarks.dll \
  --after-dll artifacts/streaming-avg-min-max-investigation-2026-10-23/after-bin/Xsl3.StreamingBenchmarks.dll \
  --sizes 50,100,500 --workloads avg,min,max --timing-runs 9 --memory-runs 3 \
  --output artifacts/streaming-avg-min-max-investigation-2026-10-23/comparison
```
