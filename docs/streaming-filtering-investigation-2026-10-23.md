# Closing the 1 MB streaming-filter gap — 23 October 2026

The remaining gap is closed for this benchmark: **9.056 ms streaming versus 9.070 ms tree**, a practical tie.
The starting streaming build took **11.387 ms**. The changes reduce its elapsed time by **20.5%** and
allocated bytes by **21.1%**, while preserving progressive output and the forward reader's memory behavior.
This is a local synthetic-workload result, not a universal performance guarantee.

## What caused the remaining work

- The forward compiler treated arithmetic results and literals as potentially containing nodes. In
  `@id mod 5 = 0`, both the arithmetic result and the literal comparison operand went through redundant
  per-item `fn:data` lowering. Their values are already atomic (or empty for arithmetic).
- Discarding an unselected record invoked the outer reader's full whitespace-filtering path on every
  discarded token, even though none of that subtree would be exposed to a navigator.
- Each immutable attribute header was a separate heap object as well as an entry in an attribute array.
  The header loop also fetched reader properties repeatedly.

The compiler now retains the atomic-result contract for constants and arithmetic in forward mode.
It still uses the existing arithmetic, comparison, casting and error logic; no numeric shortcut is
specialized to this predicate, input size or fixture. Tree lowering is unchanged.

Skipping still calls the inner reader's `Read` for every token, checks cancellation each time, and
wraps read errors through the existing `FODC0002` path. Only the outer wrapper's filtering of discarded
content is omitted. Its ordinary `Read` handles the next exposed node. Validation and inner
whitespace-reader state therefore continue to advance normally.

Attribute headers are immutable value records stored directly in their array. Navigators continue
to identify an attribute by its source node and index; headers retain schema information, namespace,
prefix and value. Attribute lookup uses an array loop, and the header-copy loop fetches names/counts once.
No whole-document cache, pooling, recycling, extra read-ahead or changed buffer size is introduced.

## Primary 1 MB filtering result

Release .NET 10.0.11, macOS ARM64. Exact decimal 1,000,000-byte input. Medians of **27 timing observations**
and **5 separate sampled-RSS observations** per variant, interleaved in rotating/reversing order.
The benchmark DLL is byte-identical for all four variants; only the engine DLL differs.

| Metric | Tree after | Streaming before | Streaming after | Winner after |
|---|---:|---:|---:|---|
| Execution, ms | 9.070 | 11.387 | 9.056 | Tie / no meaningful winner |
| First serialized bytes, ms | 6.128 | 0.580 | 0.476 | Streaming |
| Input read at first output, bytes | 1,000,000 | 49,152 | 49,152 | Streaming |
| Allocated during transform, MB | 8.436 | 8.890 | 7.018 | Streaming |
| Sampled peak process RSS, MB | 88.06 | 89.01 | 87.15 | Small streaming advantage in this sample |

The original tree control was 9.123 ms. The new streaming/tree **paired** median time ratio is 0.996;
its 95% paired-bootstrap interval is **0.984–1.011**, which includes equal performance. Streaming's
before/after paired ratio is 0.797 (interval **0.779–0.817**). Bootstrap: 10,000 resamples, fixed seed.
The original and new streaming time ranges were 11.001–13.921 ms and 8.716–12.135 ms respectively.

First output is approximately **13 times sooner** than tree, with exactly the same input consumption as
streaming before. The small 1 MB RSS difference includes runtime/compiler/code and committed GC pages;
it is not a retained-heap bound. Allocations are cumulative and are a different metric from peak RSS.

## Larger-input and workload checks

An independent full matrix uses nine timing and three sampled-RSS observations per variant/case.
The dedicated 27-observation result above is the primary 1 MB filter estimate. Lower is better.

| Input | Workload | Tree before, ms | Tree after, ms | Streaming before, ms | Streaming after, ms | Winner after |
|---|---|---:|---:|---:|---:|---|
| 1 MB | convert | 12.73 | 12.90 | 12.64 | 12.39 | No meaningful winner |
| 1 MB | filter | 9.07 | 8.97 | 11.20 | 8.97 | No meaningful winner |
| 1 MB | sum | 6.28 | 6.31 | 5.98 | 5.85 | No meaningful winner |
| 5 MB | convert | 69.54 | 70.70 | 61.93 | 62.40 | Streaming |
| 5 MB | filter | 59.30 | 60.26 | 56.78 | 49.10 | Streaming |
| 5 MB | sum | 40.85 | 40.23 | 32.15 | 29.39 | Streaming |
| 10 MB | convert | 143.48 | 141.73 | 124.92 | 118.71 | Streaming |
| 10 MB | filter | 117.66 | 118.69 | 106.84 | 87.80 | Streaming |
| 10 MB | sum | 87.48 | 87.31 | 58.10 | 55.87 | Streaming |

Winner requires a difference over 5% and a 95% paired-bootstrap interval excluding equal performance.
Conversion shows no regression in this sample. The 5 MB and 10 MB filter cases favor streaming after the change.

| Input | Workload | Tree after peak RSS, MB | Streaming before peak RSS, MB | Streaming after peak RSS, MB | Tree first output, ms | Streaming before first output, ms | Streaming after first output, ms |
|---|---|---:|---:|---:|---:|---:|---:|
| 1 MB | convert | 87.20 | 86.21 | 86.05 | 6.022 | 0.162 | 0.163 |
| 1 MB | filter | 88.10 | 89.08 | 87.26 | 6.042 | 0.555 | 0.475 |
| 1 MB | sum | 82.89 | 82.36 | 81.69 | 6.303 | 5.976 | 5.844 |
| 5 MB | convert | 101.81 | 86.21 | 86.18 | 39.062 | 0.171 | 0.180 |
| 5 MB | filter | 81.07 | 89.08 | 67.44 | 44.242 | 0.589 | 0.503 |
| 5 MB | sum | 95.22 | 87.65 | 87.49 | 40.224 | 32.143 | 29.380 |
| 10 MB | convert | 116.57 | 86.39 | 86.23 | 75.996 | 0.158 | 0.169 |
| 10 MB | filter | 118.93 | 89.08 | 88.57 | 78.165 | 0.556 | 0.457 |
| 10 MB | sum | 110.51 | 88.38 | 88.38 | 87.306 | 58.095 | 55.868 |

Conversion still first writes after 12,288 input bytes, filtering after 49,152 bytes, for every
before/after streamed case. Tree reads the full input first. Sum necessarily finishes reading before output.

The 5 MB filtering memory observations include an approximately 23 MB decrease in the process RSS
baseline for two final-tree and two final-streaming processes. Their transform-phase RSS increases
remain about **21.0 MB tree** and **5.6 MB streaming**; the earlier streaming increase was about 4.4 MB.
The absolute peak reductions in that row must therefore not be attributed entirely to this optimization.
A separate recheck of **five memory observations per variant** restored consistent baselines: final
tree 83.07 MB, final streaming 83.05 MB. Peak RSS was **104.09 MB tree**,
**89.03 MB original streaming**, and **88.65 MB final streaming**. The streaming memory
advantage is preserved. The original full-matrix row above is retained as measured. This recheck also
verified four complete outputs and used three timing observations per variant; those timings do not
replace the primary timing estimates.

[Raw 5 MB memory recheck](/Users/tyomidi/Source/Xsl3/artifacts/filtering-investigation-2026-10-23/filter-5mb-memory-recheck/results.json).

## Correctness and compatibility

- All four complete primary output files and all **36 full-matrix output files** match independently
  generated expected output byte for byte.
- Full final unit suites: **1,850 passed on .NET 10; 1,829 on .NET 8**, zero failures or skips.
- New regressions cover numeric and date/duration arithmetic, constants used as comparisons/function
  arguments, empty sequences, divide-by-zero and type errors, plus skipped DTD whitespace, schema
  validation, cancellation and malformed XML. Existing navigator/schema/fork/replay tests also pass.
- Selected strict-forward W3C streaming sets with saved-assembly sampling: **264 passed, 15 existing
  failures, zero not run** before and after. The complete reports are byte-identical; three existing
  expected-error code mismatches are unchanged. Sets: `source-document`, `si-value-of`, `si-attribute`,
  `si-apply-templates`, `si-for-each`, `sf-data`, `sf-string`; `XSL3_W3C_SAVED=10`.
- NativeAOT saved-stylesheet filtering probe passed with dynamic code disabled, correct output
  `5:value5;10:value10;`, output before input EOF, and borrowed input kept open. Native publication
  completed with zero warnings.

## Evidence and limits

The primary comparison contains 128 measured processes; the full matrix contains 432, plus 40
complete-output correctness processes between them. The memory recheck adds 32 measurements and
four correctness processes. Independent source snapshots were built before and after, preventing
concurrent checkout work from changing the measured code. Only four engine files and two test files were applied back to the checkout;
unrelated changes were preserved. The modified live files match the frozen final snapshot.

Unprofiled measurements establish the speed/allocation results. Separate EventPipe profiles of 250
transforms support the diagnosis: separate attribute-header allocations disappear, atomization argument
lists disappear from sampled allocation types, and XPath-item array allocations fall. Sampling is
approximate; CPU sampling often lands in GC polling/native reads on this runtime, so no precise
CPU-percentage attribution is claimed.

Compilation, process startup and five small-input warmups are excluded. Each observation uses a fresh
process, then a full GC, cache-warm local input, and a counting/discarding serialized output sink.
Tiered compilation is disabled. RSS is sampled externally around every millisecond in separate runs;
brief peaks can be missed. This does not simulate sustained service heaps, network delivery or
backpressure, and does not establish broader P7 conformance or large-document memory gates.

- Starting engine SHA-256: `21657272381b0e12273c8e077fd17bf657610104359b85a6ba8698489387429c`.
- Final engine SHA-256: `d6a56df9b321e52484a0587e2df4811991b956e1f4eaf66c67e45402be5963e5`.
- [Full matrix](/Users/tyomidi/Source/Xsl3/artifacts/filtering-investigation-2026-10-23/full-matrix/results.json).
- [Validation details](/Users/tyomidi/Source/Xsl3/artifacts/filtering-investigation-2026-10-23/validation.json).
- [Primary raw measurements](/Users/tyomidi/Source/Xsl3/artifacts/filtering-investigation-2026-10-23/filter-1mb-final/results.json).
- [Paired statistics](/Users/tyomidi/Source/Xsl3/artifacts/filtering-investigation-2026-10-23/filter-1mb-statistics.json).
- [Focused source/test patch](/Users/tyomidi/Source/Xsl3/artifacts/filtering-investigation-2026-10-23/performance-increment.patch).
- [Before allocation profile](/Users/tyomidi/Source/Xsl3/artifacts/filtering-investigation-2026-10-23/filter-before-profile.json), [after profile](/Users/tyomidi/Source/Xsl3/artifacts/filtering-investigation-2026-10-23/filter-after-profile.json).
- [Before W3C report](/Users/tyomidi/Source/Xsl3/artifacts/filtering-investigation-2026-10-23/w3c-before-streaming.md), [after report](/Users/tyomidi/Source/Xsl3/artifacts/filtering-investigation-2026-10-23/w3c-after-streaming.md).
- [Harness and measurement definitions](/Users/tyomidi/Source/Xsl3/bench/Xsl3.StreamingBenchmarks/README.md).

```sh
python3 bench/Xsl3.StreamingBenchmarks/compare.py \
  --before-dll artifacts/filtering-investigation-2026-10-23/before-bin/Xsl3.StreamingBenchmarks.dll \
  --after-dll artifacts/filtering-investigation-2026-10-23/after-bin/Xsl3.StreamingBenchmarks.dll \
  --sizes 1 --workloads filter --timing-runs 27 --memory-runs 5 \
  --output artifacts/filtering-investigation-2026-10-23/filter-1mb-final
```
