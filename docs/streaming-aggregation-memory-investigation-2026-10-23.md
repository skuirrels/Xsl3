# Streaming aggregation memory investigation — 23 October 2026

The untyped-node `sum()` path was caching every numeric input before calculating the total.
The forward compiler now folds values with the existing native QIL aggregate and passes only its
optional final value to the existing result helper. This removes the input-sized numeric cache.
The measured source is frozen separately before and after; the normal tree lowering is unchanged.

## Cause

`XPath31Compiler` converted each untyped node to double and passed that sequence to
`X31SumDoubles(IList<double>)`. The IL generator must cache the complete sequence for a list-valued
argument (`VisitBoundCall` / `NestedVisitEnsureStack(..., true)`). Thus the helper received a list
containing one double per input record, then traversed it to add the values.
The growing array and the earlier arrays replaced while growing it both contribute to peak RSS.

A separate 500 MB allocation profile sampled **67.21 MB of `System.Double[]` allocations**
before the change, with `XmlQuerySequence<double>.EnsureCache` and `CopyTo` stacks.
The candidate profile has no sampled `double[]` allocation event; a constant-size empty/singleton
result cache still exists. This was an input-sized sequence cache, rather than evidence solely
of GC changing its heap policy. Allocation sampling is approximate and is separate from timing.

## Implementation and semantics

- In forward mode, the existing untyped-node fast path now supplies `_f.Sum(doubles)` to
  `X31SumDoubles`, instead of the original sequence. The native `DoubleAggregator` stores only
  an accumulated double and an empty/nonempty flag; it folds inputs in the same order from the first value.
- The existing helper sees zero or one value, retaining `xs:integer(0)` for empty input and
  `xs:double` for nonempty input. Starting from the first value preserves negative zero.
- Every value is still converted: NaN/infinity do not stop reading early or hide a later conversion error.
  Existing casting, validation, cancellation and schema-aware calculation paths remain in use.
- No new public runtime method, GC policy, forced collection, cache-size threshold, pooling or
  file-size switch is introduced. Saved assemblies retain the existing helper contract.
- This change applies to the direct untyped-node `sum()` fast path in forward mode.
  It does not establish flat memory for arbitrary schema-typed, higher-order or other aggregate expressions.

## Execution time

Release .NET 10.0.11, macOS ARM64. Medians of nine timing observations per variant/case.
Lower is better. Both engine versions use a byte-identical benchmark harness.

| Input | Tree before, ms | Tree after, ms | Streaming before, ms | Streaming after, ms | Streaming time saved | Winner after |
|---|---:|---:|---:|---:|---:|---|
| 1 MB | 8.29 | 6.44 | 6.30 | 6.97 | -10.8% | No meaningful winner |
| 50 MB | 435.72 | 447.44 | 277.96 | 275.55 | 0.9% | Streaming |
| 100 MB | 976.77 | 1000.52 | 556.13 | 549.83 | 1.1% | Streaming |
| 500 MB | 6272.27 | 6205.02 | 2724.19 | 2705.68 | 0.7% | Streaming |

### Dedicated 1 MB control recheck

The initial 1 MB timing pass has broad outliers in both the changed streaming path and unchanged
tree control (tree-before 6.13–16.15 ms; tree-after 6.13–12.61 ms). Its raw row above is preserved.
A separate 27-pair timing recheck, with three memory observations per variant, found stable controls:

| Variant | Tree, ms | Streaming, ms |
|---|---:|---:|
| Before | 5.969 | 5.513 |
| After | 5.984 | 5.482 |

Use this longer recheck for small-file conclusions; it shows no meaningful streaming slowdown.
[Raw 1 MB recheck](/Users/tyomidi/Source/Xsl3/artifacts/streaming-aggregation-investigation-2026-10-23/small-control-recheck/results.json).

Winner uses a difference over 5% and a 95% paired-bootstrap interval excluding equal performance.
Bootstrap: 10,000 resamples with a fixed seed; nine paired ratios for the primary run and 27 for the recheck. The raw data retains ranges and all pairs.

## Peak memory, allocation and first output

RSS is the median of three separate transform-phase sampling runs, in decimal MB. It includes
runtime, JIT code and committed GC pages. Allocations are cumulative bytes, not simultaneous live memory.

| Input | Tree after peak RSS, MB | Streaming before peak RSS, MB | Streaming after peak RSS, MB | Streaming allocated before / after, MB | Streaming first output before / after, ms |
|---|---:|---:|---:|---:|---:|
| 1 MB | 83.00 | 82.26 | 82.20 | 3.19 / 3.07 | 6.29 / 6.97 |
| 50 MB | 235.90 | 94.49 | 87.52 | 160.41 / 152.02 | 277.95 / 275.55 |
| 100 MB | 402.36 | 101.32 | 87.52 | 321.28 / 304.49 | 556.12 / 549.82 |
| 500 MB | 1521.75 | 148.34 | 87.54 | 1584.08 / 1516.96 | 2724.18 / 2705.67 |

At 50–500 MB, optimized streaming peaks span **87.52–87.54 MB**.
Sum produces one final total, so its first output still requires the complete input. All measured
variants read the entire file before their first nonempty serialized byte write.
The single-pass aggregate does not add lookahead or change the serializer buffering.

## Correctness and compatibility

- **All 16 complete primary outputs, plus four control-recheck outputs,** match the independently generated expected
  totals byte for byte, including 1,690,672,834 for the 500 MB input. Measured runs also verify output
  length and complete input consumption. No expected result is used by engine execution.
- Full final Release unit suites: **1,980 passed on .NET 10; 1,956 on .NET 8**, zero failures/skips.
- New sum regressions cover empty integer zero versus nonempty double, addition order, negative zero,
  NaN/infinity, later invalid values, repeated groups, attribute inputs, exact schema-typed integers
  and saved stylesheet assemblies.
- Debug sum and streamed-QIL-verifier checks: **30 passed**, including preservation of read order.
- Strict-streaming W3C `sf-sum`, saved-assembly roundtrips for eligible cases (`XSL3_W3C_SAVED=1`):
  **24 passed, 14 existing failures, zero not run** before and after; complete reports are byte-identical.
- NativeAOT saved-stylesheet probe passed with dynamic code disabled and zero publication warnings.
  It verifies double totals, empty integer zero and negative zero across groups; output is produced
  before input EOF and the borrowed reader stays open.

## Method and limits

- Four cases, four interleaved variants, nine timing and three independent memory observations:
  **192 measured processes**, plus **16** complete-output correctness processes. The 1 MB recheck
  adds 120 measured processes and four complete-output correctness processes.
- The same public `XslCompiledTransform` API, exact decimal synthetic corpus, stylesheet, small-input
  warmup, JIT policy and counting output sink are used before/after. Cases are untyped sums of `@value`.
- Each fresh process compiles the stylesheet, warms five 65,536-byte transforms, then runs full GC
  before measurement. Timing includes reader creation, parsing, transformation and serialization;
  compilation/startup/warmup and output-file correctness writes are excluded.
- No profiler or memory sampler is attached during timing. Memory is sampled externally around 1 ms
  during separate observations. Short peaks may be missed. Absolute RSS is not a retained-heap bound.
- Input is cache-warm local XML. There is no network, output disk delay or backpressure in timing.
  Results describe this workload and fresh-process policy, not every production heap or stylesheet.
- Only one compiler expression and a new regression test file are changed in the live checkout;
  unrelated work is preserved. Exact source snapshots and binary hashes identify the measured build.

- Before engine SHA-256: `54859252d158ae2d21ad9e178065b5c39acf5e8e1a8467a371f524a29395f833`.
- After engine SHA-256: `429c99cb325f877fe6f1cdc200df32700760972a697b83e7398eb663e5131897`.
- [Raw comparison](/Users/tyomidi/Source/Xsl3/artifacts/streaming-aggregation-investigation-2026-10-23/comparison/results.json).
- [Paired statistics](/Users/tyomidi/Source/Xsl3/artifacts/streaming-aggregation-investigation-2026-10-23/statistics.json).
- [Before profile](/Users/tyomidi/Source/Xsl3/artifacts/streaming-aggregation-investigation-2026-10-23/sum-before-profile.json), [after profile](/Users/tyomidi/Source/Xsl3/artifacts/streaming-aggregation-investigation-2026-10-23/sum-after-profile.json).
- [Before W3C report](/Users/tyomidi/Source/Xsl3/artifacts/streaming-aggregation-investigation-2026-10-23/w3c-before.md), [after W3C report](/Users/tyomidi/Source/Xsl3/artifacts/streaming-aggregation-investigation-2026-10-23/w3c-after.md).
- [Focused source/test patch](/Users/tyomidi/Source/Xsl3/artifacts/streaming-aggregation-investigation-2026-10-23/performance-increment.patch).
- [Validation details](/Users/tyomidi/Source/Xsl3/artifacts/streaming-aggregation-investigation-2026-10-23/validation.json).

```sh
python3 bench/Xsl3.StreamingBenchmarks/compare.py \
  --before-dll artifacts/streaming-aggregation-investigation-2026-10-23/before-bin/Xsl3.StreamingBenchmarks.dll \
  --after-dll artifacts/streaming-aggregation-investigation-2026-10-23/after-bin/Xsl3.StreamingBenchmarks.dll \
  --sizes 1,50,100,500 --workloads sum --timing-runs 9 --memory-runs 3 \
  --output artifacts/streaming-aggregation-investigation-2026-10-23/comparison
```
