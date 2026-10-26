# Streaming performance investigation — 23 October 2026

The streaming conversion/filtering overhead came largely from transient allocation and repeated simple-content atomization.
Three targeted changes are implemented in the local working copy. Original and optimized binaries were frozen
and interleaved with identical workloads, inputs, warmup and output sinks. This report records the new measurements;
the earlier benchmark report remains the historical baseline.

## Cause and implementation

1. Streamed `xsl:value-of` and value-template inputs went through `fn:data`, wrapped every non-text
   item in an immutable XDM array, and later flattened/atomized the wrapper again for simple content.
   The original conversion profile contains `X31SimpleContent → Sequences.Atomize → Sequences.Flatten`
   and immutable-list enumeration. Allocation samples include the arrays, immutable-list nodes and enumerators.
   Normal tree compilation does not introduce that streamed pre-atomization/wrapping round trip.
2. The forward reader allocated temporary attribute and namespace lists for every element, including
   elements with no attributes/namespaces, then copied their contents into arrays.
3. Text coalescing and element string-value reads allocated string builders even when a single existing
   string already represented the complete value. Some text paths also called `ToString()` twice.

The optimized compiler emits a direct per-item simple-content call. It returns a single atom directly,
consumes untyped node values while current, and retains the existing wrapped canonical path for
zero/multiple typed atoms. Text nodes keep their adjacency; empty typed values/arrays remain boundaries.
The forward reader fills the final attribute array directly, creates namespace lists only when needed,
and uses a string builder only when another text segment must be appended. No whole-document cache,
node recycling or extra read-ahead is introduced. The normal tree compiler keeps its existing path.

The matching optimized conversion profile no longer contains the immutable-array flattening path
among its captured hot stacks. Allocation-type samples are approximate and CPU sampling on this runtime
often stops in GC polling; the speed/allocation claims below come from separate unprofiled measurements.

## Execution time

Medians in milliseconds; Release .NET 10.0.11, macOS ARM64. Lower is better. “Saved” is the reduction
in streaming elapsed time. Winner compares optimized streaming with optimized tree.

| Input | Workload | Tree before, ms | Tree after, ms | Streaming before, ms | Streaming after, ms | Streaming time saved | Winner now |
|---|---|---:|---:|---:|---:|---:|---|
| 1 MB | convert | 12.38 | 12.51 | 27.59 | 12.38 | 55.1% | No meaningful winner |
| 1 MB | filter | 9.00 | 8.90 | 15.45 | 11.11 | 28.1% | Tree |
| 1 MB | sum | 6.05 | 6.64 | 6.12 | 5.94 | 2.8% | No meaningful winner (recheck) |
| 5 MB | convert | 68.15 | 68.40 | 138.68 | 61.35 | 55.8% | Streaming |
| 5 MB | filter | 53.98 | 52.31 | 72.66 | 54.03 | 25.6% | No meaningful winner |
| 5 MB | sum | 39.21 | 38.10 | 31.49 | 29.69 | 5.7% | Streaming |
| 10 MB | convert | 138.77 | 141.30 | 273.03 | 121.48 | 55.5% | Streaming |
| 10 MB | filter | 119.42 | 119.43 | 144.18 | 107.32 | 25.6% | Streaming |
| 10 MB | sum | 87.11 | 85.77 | 61.35 | 59.01 | 3.8% | Streaming |

Winners require a median difference over 5% and a 95% paired-bootstrap interval excluding equal
performance (10,000 resamples of the nine after-stream/after-tree ratios; deterministic seed).
This describes the local sample, not all workloads or hardware.

## Peak process memory and first output

RSS is the median sampled peak of three separate transform-phase observations. It includes the loaded
engine/runtime, code and committed GC pages. It is not a managed retained-heap bound or allocation total.
First output is the first nonempty serialized byte write, with the usual serializer buffering.

| Input | Workload | Tree after peak RSS, MB | Streaming before peak RSS, MB | Streaming after peak RSS, MB | Streaming first output before, ms | Streaming first output after, ms |
|---|---|---:|---:|---:|---:|---:|
| 1 MB | convert | 87.13 | 90.39 | 86.11 | 0.313 | 0.132 |
| 1 MB | filter | 87.93 | 90.36 | 88.85 | 0.714 | 0.515 |
| 1 MB | sum | 82.92 | 86.75 | 82.28 | 6.113 | 5.940 |
| 5 MB | convert | 101.81 | 90.42 | 86.05 | 0.305 | 0.163 |
| 5 MB | filter | 104.05 | 90.47 | 89.06 | 0.727 | 0.535 |
| 5 MB | sum | 94.99 | 91.32 | 87.49 | 31.477 | 29.689 |
| 10 MB | convert | 116.47 | 90.36 | 86.16 | 0.287 | 0.153 |
| 10 MB | filter | 118.88 | 90.39 | 88.98 | 0.739 | 0.566 |
| 10 MB | sum | 110.79 | 92.21 | 88.52 | 61.351 | 59.009 |

Conversion first output still arrives after reading 12,288 input bytes; filtering after 49,152 bytes,
for both original and optimized streaming at every size. Sum needs the full input to produce its final result.

## Allocation and timing variation

Allocations are cumulative bytes allocated during one transformation, not simultaneously live memory.

| Input | Workload | Streaming allocation before, MB | Streaming allocation after, MB | Reduction | Original streaming time range, ms | Optimized streaming time range, ms |
|---|---|---:|---:|---:|---:|---:|
| 1 MB | convert | 42.25 | 14.77 | 65.0% | 27.27–28.58 | 11.92–13.81 |
| 1 MB | filter | 15.05 | 8.89 | 40.9% | 14.85–17.26 | 10.82–18.62 |
| 1 MB | sum | 4.53 | 3.69 | 18.5% | 5.93–7.25 | 5.68–7.48 |
| 5 MB | convert | 209.95 | 73.38 | 65.0% | 135.89–158.97 | 60.10–66.31 |
| 5 MB | filter | 74.75 | 44.14 | 40.9% | 70.06–85.39 | 52.64–58.15 |
| 5 MB | sum | 22.77 | 18.65 | 18.1% | 30.87–34.60 | 29.03–32.48 |
| 10 MB | convert | 419.42 | 146.59 | 65.1% | 267.75–284.99 | 119.52–137.50 |
| 10 MB | filter | 149.33 | 88.18 | 41.0% | 140.94–150.63 | 105.26–115.20 |
| 10 MB | sum | 45.48 | 37.24 | 18.1% | 60.40–65.30 | 57.40–61.24 |

## Correctness and compatibility

- All 36 complete before/after tree/stream output files match independently generated expected output byte for byte;
  thus all four variants agree for each of the nine size/workload cases. SHA-256 values are retained in raw data.
- Full Release unit suites: **1,817 passed on .NET 10; 1,798 on .NET 8**.
- Added regression coverage for empty-atom/text boundaries, singleton and nested arrays, multi-valued and empty
  schema list values, canonical atomization errors, consuming sibling nodes, and saved stylesheet assemblies.
- Upstream CI filter: **1,654 passed** on .NET 10; differential suite: **394 passed**.
  An initial unfiltered upstream run had four known platform-specific failures; the existing CI filter
  (`category!=failing`) excludes those. No baseline or test policy was changed.
- Selected strict-forward W3C sets with saved-assembly sampling: **264 passed, 15 existing failures, zero not run**
  before and after; the complete reports are byte-identical. Three expected errors have the same existing code mismatches.
- NativeAOT saved-stylesheet smoke test passed with dynamic code disabled, correct mixed-text/attribute
  output, early XML-writer delivery before EOF, and borrowed input kept open; publication emitted no warnings.
- New runtime entry point recorded in `PublicAPI.Unshipped.txt`; existing saved-assembly entry points remain intact.

## Method, scope and reproduction

- 9 timing observations and 3 independent memory observations for each of four variants and nine cases:
  **432 measured processes**, plus **36** full-output correctness processes.
- Each child compiles its stylesheet and warms five transformations of the same 65,536-byte corpus before a full GC.
  Timing includes reader creation, parsing, transformation and serialization, excluding compilation/warmup/startup.
- Variants run serially in rotating/reversing order. Timing runs have no sampler or profiler attached.
- Memory runs use an external macOS RSS sampler targeting 1 ms; brief peaks between samples can be missed.
- Exact decimal 1/5/10 MB synthetic files; cache-warm local input and counting/discarding output sink.
- Tiered compilation disabled in every process. Fresh-process/small-input warmup does not model a long-running
  service heap already sized by repeated full-size inputs. Network/output-disk delays are not simulated.
- Unrelated global-binding/emitter changes landed in the shared checkout during this comparison. The whole
  working-tree source fingerprint therefore changed. Both measured DLLs remained frozen; all three optimized
  source files still match their measured-library PDB checksums. The new unrelated changes are preserved
  and are outside this measured/tested build. Binary hashes identify the exact build evaluated here.
- These results preserve the measured memory/latency benefits at 1–10 MB; they do not close P7’s full
  conformance, 2 GB memory or release-readiness gates. Existing unrelated working-copy changes are preserved.

- Original library SHA-256: `5ace7edd07729fd1404bc771f1f6d0dc60ac49101961a8852e8d8b6552cc54cf`.
- Optimized library SHA-256: `76116bc6decc0e4594ffcea402c74278a559c9e3a91c57ef2715b7bafccd0738`.
- Working-tree source fingerprint at measurement start: `2bdcb33501083f0a3e9f6cae485ea89f7e693ef48ce44f022b96dfb9688f7551`.
- [Raw measurements](/Users/tyomidi/Source/Xsl3/artifacts/streaming-investigation-2026-10-23/comparison/results.json).
- [Before profile](/Users/tyomidi/Source/Xsl3/artifacts/streaming-investigation-2026-10-23/stream-convert-before-profile.json), [after profile](/Users/tyomidi/Source/Xsl3/artifacts/streaming-investigation-2026-10-23/stream-convert-after-profile.json).
- [Validation details](/Users/tyomidi/Source/Xsl3/artifacts/streaming-investigation-2026-10-23/validation.json).
- [Focused performance patch](/Users/tyomidi/Source/Xsl3/artifacts/streaming-investigation-2026-10-23/performance-increment.patch).
- [Harness instructions](/Users/tyomidi/Source/Xsl3/bench/Xsl3.StreamingBenchmarks/README.md).

```sh
python3 bench/Xsl3.StreamingBenchmarks/compare.py \
  --before-dll artifacts/streaming-investigation-2026-10-23/baseline-bin/Xsl3.StreamingBenchmarks.dll \
  --after-dll artifacts/streaming-investigation-2026-10-23/optimized-bin/Xsl3.StreamingBenchmarks.dll \
  --sizes 1,5,10 --timing-runs 9 --memory-runs 3 \
  --output artifacts/streaming-investigation-2026-10-23/comparison
python3 bench/Xsl3.StreamingBenchmarks/summarize-comparison.py artifacts/streaming-investigation-2026-10-23/comparison/results.json --output docs/streaming-performance-investigation-2026-10-23.md
```

## Small-sum control recheck

The initial 1 MB sum tree control varied enough to affect its apparent winner. A separate
follow-up of 27 timing observations per variant repeated just this case with the
same binaries/corpus/settings. Original tree: 6.228 ms; optimized tree: 6.127 ms. Original streaming: 6.304 ms; optimized streaming: 5.941 ms.
The tree-control difference did not persist; the after-stream/after-tree difference is below
the 5% practical threshold, so this case has no meaningful winner. The primary table keeps
the original matrix medians and explicitly uses the recheck for its winner label.

The harness DLL hashes differ after recompiling against the changed reference assembly.
All 48 method bodies, method signatures, local signatures and PDB source document checksums
were independently compared and are identical; the differences are build/debug identity metadata.

[Raw control recheck](/Users/tyomidi/Source/Xsl3/artifacts/streaming-investigation-2026-10-23/sum-control-recheck/results.json).
