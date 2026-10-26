# Deferred climbing set execution, 2026-10-25

This connects the [deferred header store](streaming-deferred-headers-2026-10-25.md) to the compiler on
`feature/p7-streaming-analysis`. Climbing `except` expressions, mixed nested set operators, differing
ancestor projections and nearest-ancestor predicates now use a fused prefix scan and deferred
membership. Full Phase 7 remains active; `supports-streaming` remains `no`.
The subsequent [accumulator-phase checkpoint](streaming-accumulator-phases-2026-10-25.md) adds observer
snapshots to this execution path; its identity-bearing value storage requirements remain open.

## Execution and ordering

Potential ancestor and attribute slots are registered in document order before matched projections
run. Registration strips predicates and broadens schema-dependent tests; it does not execute throwing
predicates or inspect descendant content. The actual projection runs only when its prefix matches,
preserving its axis position, predicates, errors and source identity.

Each leaf records membership once. A three-valued evaluation of the complete union/intersection/except
plan distinguishes selected, excluded and undecided results. A later right-hand match can remove an
earlier ancestor. An unresolved queue head delays later results, and owner sealing completes its
membership after the scan leaves that ancestor. Source end-of-element metadata and invocation sealing
are separate flags: replayed input may already have ended before an invocation starts.

Source-frame facts retain only current ancestors. Potential result owners and required parents are
spilled lazily through offsets in the source-owned header store. Closed queue records do not retain
source navigators, and all invocations share the source's private scratch files and existing cache
bound. Parent serialization is iterative, avoiding recursion proportional to document depth.

An explicitly materialized or replayed population retains its actual nodes. Those results wait until
capture completes, then sort in the runtime's canonical document order. Mixed source/replay views
synchronize membership and remove duplicate positions. Completion and terminal errors release the
foreign-document comparer as well as queued nodes. Scratch files remain owned by their source,
including when a consumer stops early.

The compiler marks the new drains as resumable sequences: each call returns at most one result,
including the final drain after the scan. Four new saved-runtime helper entry points are recorded in
the unshipped public API. Existing monotonic union/intersection helper signatures and multi-item drain
behavior remain unchanged for saved-consumer compatibility.

## Functional and saved-consumer evidence

Focused Release tests pass **58 net10.0 / 54 net8.0**. They cover nested operators, different
projections, late exclusions, nearest ancestors, final-drain resumption, modern and legacy APIs,
normal/debug compilation, saved assemblies, explicit replay, fork isolation, row-local invocations,
rollback, cancellation, grounded ordering, foreign-tree collection and intrinsic header guards.

Full Release and Debug each pass **3,285 net10.0 / 3,180 net8.0** units. The solution builds; ordinary
W3C **46**, upstream **1,654** and differential **394** per framework, and all **13** streaming
controller tests pass. No baseline files are changed by this increment.

All execution profiles are freshly run, including explicit XSLT 2.0 and posture/sweep runs. Comparing
the nine reports with the storage-foundation checkpoint finds no new failure IDs. Strict execution
repairs `sx-except-331` through `sx-except-335`; the complete `sx-ExceptExpr` set passes **66/66**.
The strict audit retains exit **1** for its remaining failures.

| Profile | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| XSLT 3.0 | 8849 | 19 | 41 | 34 |
| XSLT 2.0 | 4590 | 8 | 10 | 7 |
| Unstreamed development | 2693 | 18 | 1 | 5 |
| Strict forward inputs | 2695 | 16 | 1 | 7 |
| Posture and sweep | 790 | 129 | 0 | 0 |
| Schema-aware | 843 | 1 | 2 | 6 |
| Schema-aware strict | 842 | 2 | 2 | 6 |
| QT3 | 21884 | 0 | 99 | 67 |

QT3 parse coverage remains **22,359/22,359**. Raw functional logs are
`/tmp/xsl3-p7-deferred-climbing-final-verified-*`; reports and failure-ID comparison are in
`/tmp/xsl3-p7-deferred-climbing-final-reports/`. The generated conformance table includes this strict
execution improvement without changing expected failures or weakening error matching.

A frozen **412**-file core/configuration snapshot builds both NativeAOT consumers. Eight new saved
normal/debug styles exercise actual deferred attributes, nested set operations, differing projections
and multiple final-drain results. The main native consumer passes these and all retained compatibility
cases, including the legacy merge and immutable v2 fork consumers. All **95** retained saved-DLL hashes
are unchanged. Both native publications have no warnings. The artifact manifests record **106** main
artifacts (**103** saved DLLs and three native publish files) and **5** scale artifacts. Source and
artifact hashes are under `/tmp/xsl3-p7-deferred-climbing-final-native/` and
`/tmp/xsl3-p7-deferred-climbing-final-scale-native/`.

## Ordinary performance

The isolated three-pair gate against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` passes, exit **0**.
The time geometric mean over **39** measured rows is **1.027012847257**; the maximum allocation ratio
is **1.037238717165**. These medians compare the accumulated branch with the baseline using the same
harness. They are not a per-increment speedup claim, and cold rows are excluded by the existing gate.

Aggregate success does not make every row faster. Individual timing ratios above 1.05 are grouping
compilation **1.056**, sorting compilation **1.201**, keys compilation **1.722**, join compilation
**1.077**, iterate compilation **1.060**, regex 1 KB **1.170**, and maps 1 MB **1.154**.
Only rounds **1–3** are archived in `/tmp/xsl3-p7-deferred-climbing-final-perf-data/`, including the
full comparison and exact `summary.json`. The log is `/tmp/xsl3-p7-deferred-climbing-final-perf.log`.
The functional and native build jobs completed before this timing run; large input runs started
after it. No threshold, input size or allocation policy was relaxed.

## Actual deferred-set native input runs

Both normal and debug saved styles pass at exact **209,715,200** and **2,147,483,648** input bytes.
The stylesheet selects:

```xpath
doc/row/value/ancestor::row/@id
except doc/row[number(@id) mod 2 = 0]/text/ancestor::row/@id
```

The later sibling `text` match excludes even row IDs. Each child consumes the full source, verifies
an independently generated SHA-256 over the odd IDs, emits before EOF, and closes its owned input.
The generated XML source is byte-for-byte identical to the preceding probe's generator; no desired
fingerprint or output data is substituted. A borrowed initial reader remains open.

| Saved style | Exact input bytes | First output after input bytes | Child elapsed ms | Lifetime peak RSS bytes |
|---|---:|---:|---:|---:|
| normal | 209715200 | 69632 | 56348.3656 | 30048256 |
| normal | 2147483648 | 69632 | 577909.3575 | 30064640 |
| debug | 209715200 | 69632 | 55855.6244 | 30572544 |
| debug | 2147483648 | 69632 | 590306.4674 | 30572544 |

RSS is the maximum of controller-observed and child-reported full-lifetime peaks, with no baseline
subtraction. The existing **209,715,200-byte** heap/RSS guard and ordinary ICU support remain in place.
These actual deferred-set runs extend the earlier scalar/primitive evidence; they do not satisfy the
separate **20,971,520-byte** push RSS limit or canonical throughput requirement. Child elapsed times
are observations, not comparative throughput benchmarks. All four terminal statuses are **0**;
raw measurements, controller source, source hashes and output digests are in
`/tmp/xsl3-p7-deferred-climbing-final-scale-native/`.

## Remaining requirements

At this historical checkpoint arbitrary accumulator snapshots were explicitly rejected. The subsequent
[phase implementation](streaming-accumulator-phases-2026-10-25.md) preserves those snapshots and spills
scalar/container structure; general identity-bearing node/closure spill remains required.
Other expression shapes, remaining analysis/semantic failures, exact push RSS, canonical throughput,
and current-source Linux x64 acceptance remain open. This increment does not complete an S stage or
the full Phase 7 plan.
