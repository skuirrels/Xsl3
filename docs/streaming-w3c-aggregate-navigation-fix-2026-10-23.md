# W3C streaming aggregate navigation fix — 23 October 2026

All four remaining failures in the strict-forward `sf-avg`, `sf-min` and `sf-max` comparison are fixed:
**105 passed / 4 failed before; 109 passed / 0 failed after**. Saved-assembly checks are enabled.
The existing incremental untyped-node aggregate paths are retained.

## Causes and changes

- `sf-min-023` and `sf-max-023` evaluate `outermost(//ITEM/DIMENSIONS)` and cast its values.
  The crawling parent/child selection could advance past a subtree before its contents were read.
  It now uses the existing pruning descendant iterator with a complete expanded-name parent/child
  filter and the existing one-item pull protocol. Only accepted candidates prune descendants;
  rejected outer candidates cannot hide matching descendants. Disjoint child-only anchors need no
  result sort. The cursor keeps a fixed amount of state plus the open ancestor stack, rather than
  collecting input nodes or grounding the XML document.
- `sf-min-032` and `sf-max-032` cast a selected child to `xs:date` inside duration arithmetic.
  A list-valued cast argument could finish the child scan before atomizing the selected node.
  Constructor functions and cast/castable syntax now use the existing per-item atomization path.
  The canonical cast still enforces cardinality and schema/list conversion rules; its result is
  recorded as atomic so downstream operations need not try to atomize live nodes again.
- Numeric average/minimum/maximum arithmetic and the direct untyped-node folding path are unchanged.
  The two new runtime cursor entry points are recorded in `PublicAPI.Unshipped.txt` for generated
  and saved stylesheet calls. No runtime values depend on the W3C fixture names or expected outputs.

## Correctness and compatibility

- Thirteen focused .NET 10 regression cases pass (twelve on .NET 8, without the saved-assembly case).
  They reproduce all four failures and cover nested paths, rejected candidates, non-consuming bodies,
  disjoint multiple anchors, grounded inputs, expanded names, cast/castable syntax, list constructors
  and a saved-assembly roundtrip.
- Debug aggregate/navigation/outermost/QIL-verifier suite: **79 passed, zero failures**.
- NativeAOT probe: **passed**, dynamic code disabled, zero publish warnings. Both repaired shapes
  run from a saved stylesheet, produce the expected numeric/duration values, emit grouped output
  before EOF and leave the borrowed reader open.
- Full unit suites and the same frozen baseline were both tested. Two unrelated fork-annotation
  cases fail in both versions (`TypedOutputAnnotationsSurviveBothProngSinks`, forward true/false).
  There are **zero new unit failures**.

| Framework | Before passed / failed | After passed / failed |
|---|---:|---:|
| .NET 10 | 2091 / 2 | 2104 / 2 |
| .NET 8 | 2060 / 2 | 2072 / 2 |

## Existing aggregate performance

Seconds, medians of five timing observations per variant/case, with nine for 500 MB streaming.
Peak process RSS is decimal MB,
median of three separate memory observations. These before binaries already contain the previous
incremental avg/min/max optimisation. The comparison isolates this navigation/cast fix.

| Input | Function | Stream before, s | Stream after, s | Tree after, s | Stream RSS before / after, MB | Timing winner before vs after |
|---|---|---:|---:|---:|---:|---|
| 50 MB | avg | 0.280 | 0.277 | 0.529 | 87.10 / 87.34 | No meaningful winner |
| 50 MB | min | 0.275 | 0.275 | 0.508 | 87.39 / 87.34 | No meaningful winner |
| 50 MB | max | 0.274 | 0.271 | 0.516 | 87.41 / 87.47 | No meaningful winner |
| 100 MB | avg | 0.544 | 0.547 | 1.209 | 87.29 / 87.44 | No meaningful winner |
| 100 MB | min | 0.544 | 0.545 | 1.129 | 87.41 / 87.24 | No meaningful winner |
| 100 MB | max | 0.539 | 0.540 | 1.249 | 87.62 / 87.52 | No meaningful winner |
| 500 MB | avg | 2.688 | 2.710 | 8.110 | 87.59 / 87.74 | No meaningful winner |
| 500 MB | min | 2.696 | 2.711 | 7.929 | 87.75 / 87.44 | No meaningful winner |
| 500 MB | max | 2.692 | 2.707 | 7.969 | 87.47 / 87.56 | No meaningful winner |

New streaming peak medians span **87.24–87.74 MB** across the requested sizes/functions.
First output for these whole-file aggregates still requires EOF, and is approximately the execution
time shown above. Per-group output remains progressive, as checked by the NativeAOT probe.

Timing winner requires a median difference over 5% and a paired-bootstrap 95% interval excluding
equal performance (10,000 resamples of five paired ratios, fixed seed). This is a local regression
check; it does not establish exact equality of timings or a universal performance guarantee.

## Output verification and method

- All **36 complete outputs** are byte-identical across before/after tree/streaming and independent
  expected outputs. Parsed binary64 results also match independently calculated bit patterns.
- **312 measured transformations**: nine cases, four rotating/reversing variants, five timing and
  three separate memory observations, plus four additional streaming timing pairs per 500 MB function.
  The extra pairs reduce uncertainty from slow timing samples and reverse before/after order each pair.
  Children run serially, with identical benchmark executable,
  synthetic inputs and counting sink. No profiling or RSS sampling is attached to timing runs.
- Release .NET 10.0.11, macOS ARM64, Apple M4 Pro, 14 logical CPUs, 24 GiB RAM. Each observation
  starts in a fresh process, with five 65,536-byte warmups, full GC before measurement and tiered
  compilation disabled. Compilation, startup, warmup and correctness file I/O are excluded.
- Timed work includes parsing, transformation and serialization. Inputs are local and cache-warm.
  Memory includes runtime/JIT code and committed GC pages; it is sampled process RSS, not live heap.
- A compatibility check interrupted one case. Completed cases were retained from checkpoints;
  the incomplete case was discarded and rerun before continuing. No timing observation overlaps
  a compatibility test or a build.
- Frozen source snapshots and binaries isolate concurrent checkout changes. Hashes, PDB source
  checksums and complete expected output bytes are rechecked when this report is generated.
- The flat-memory benchmark evaluates direct untyped attribute avg/min/max. It does not generalize
  that guarantee to every typed/collation/higher-order aggregate expression. General typed aggregate
  arguments still use the canonical atomic-list interface; this fix streams their node navigation
  and does not add input-document materialization.

## Evidence

- [Raw measurements](/Users/tyomidi/Source/Xsl3/artifacts/streaming-w3c-min-max-fix-2026-10-23/comparison/results.json).
- [Statistics](/Users/tyomidi/Source/Xsl3/artifacts/streaming-w3c-min-max-fix-2026-10-23/statistics.json).
- [Validation and hashes](/Users/tyomidi/Source/Xsl3/artifacts/streaming-w3c-min-max-fix-2026-10-23/validation.json).
- [Focused patch](/Users/tyomidi/Source/Xsl3/artifacts/streaming-w3c-min-max-fix-2026-10-23/fix.patch).
- [Before W3C report](/Users/tyomidi/Source/Xsl3/artifacts/streaming-w3c-min-max-fix-2026-10-23/w3c-before.md), [after W3C report](/Users/tyomidi/Source/Xsl3/artifacts/streaming-w3c-min-max-fix-2026-10-23/w3c-after.md).
- [Regression tests](/Users/tyomidi/Source/Xsl3/tests/Xsl3.Tests/StreamedAggregateNavigationTests.cs).

```sh
python3 artifacts/streaming-w3c-min-max-fix-2026-10-23/source-after/bench/Xsl3.StreamingBenchmarks/compare.py \
  --before-dll artifacts/streaming-w3c-min-max-fix-2026-10-23/before-bin/Xsl3.StreamingBenchmarks.dll \
  --after-dll artifacts/streaming-w3c-min-max-fix-2026-10-23/after-bin/Xsl3.StreamingBenchmarks.dll \
  --sizes 50,100,500 --workloads avg,min,max --timing-runs 5 --memory-runs 3 \
  --output artifacts/streaming-w3c-min-max-fix-2026-10-23/comparison
python3 artifacts/streaming-w3c-min-max-fix-2026-10-23/refine-500-timing.py
```
