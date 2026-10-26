# Deferred header storage, 2026-10-25

This continues the [ID-provenance checkpoint](streaming-id-provenance-2026-10-25.md) on
`feature/p7-streaming-analysis`. It supplies storage needed for general non-monotonic climbing sets.
**At this historical checkpoint the compiler was not connected to this store.**
The subsequent [deferred climbing implementation](streaming-deferred-climbing-2026-10-25.md)
connects it and fixes the five climbing `except` failures.
Full Phase 7 remains active; `supports-streaming` remains `no`.

## Required ordering and storage

A later right-hand match can remove an earlier ancestor from an `except` result. Differently projected
operands can also discover an earlier node late. The existing monotonic union/intersection helper
cannot safely emit those results immediately. Retaining every pending source navigator would make
memory grow with input length.

The new `StreamedHeaderSpool` holds intrinsic headers and mutable membership records in source-owned
scratch storage. Owner records refer to parent records and replaceable header payloads by offsets;
cell records hold queue links, attribute/content positions and membership bits. Those links retain
no closed source nodes. Handles are scoped to their store, parent links move backwards, queue links
move forwards, and updates retain the original node number and kind. Reconstructed headers retain
source document identity, names, namespaces, base URI, language and schema metadata. They permit
intrinsic inspection and reject attempts to revisit unstored children or element content. An unfinished
nonempty header whose first child is not yet known raises the position guard; it does not appear empty.

One lazy store belongs to each `StreamedDocument`. Operations will share its file pair, rather than
opening files for every short-lived iterator. Source disposal closes both files, including fault and
cancellation exits. The store starts in a shared **64 KiB** serialized cache and spills both streams
to private **0600**, delete-on-close files when that existing limit is exceeded. It does not maintain
a document-wide in-memory node or offset index. This is disposable execution scratch data.

`StreamedReplayStorage` and `StreamedHeaderCodec` are extracted from value replay and shared with it.
Value replay retains its existing binary layout, cache limit, source navigation contract and saved
entry points. The codec now snapshots schema-info fields, so mutating the original `IXmlSchemaInfo`
object cannot change a stored annotation. Schema types/declarations remain references to the finite
compiled schema model. No public API, saved helper signature, feature claim or configured budget changes.

## Remaining integration

The planned runtime uses the fused prefix scan already used by header sets. It must register potential
result slots in document order before evaluating matched projections, update membership through
frame-owned state, defer an unresolved queue head, and drain one result per resumable call. Static
potential tests must overapproximate final schema tests and must not evaluate throwing predicates
early. Owner/invocation completion and final validation metadata need separate treatment when a
bounded-depth scan ends before its source ancestor closes.

The existing multi-item monotonic drain must retain its ABI. Simply tagging it as a pull sequence
would lose results because that protocol reads one item per call. Positional/differing projections,
grounded and multiple roots, nested operators, early consumer abandonment, errors and source identity
need end-to-end coverage when the compiler is connected.

Accumulator observer snapshots are arbitrary XDM data and are not serialized by this primitive yet.
A non-null observer snapshot raises the explicit `stream-header-metadata` error; it is never silently
dropped or replaced with an empty value. Actual snapshot serialization remains required before this
can support those deferred results. These are outstanding implementation requirements, not relaxed
completion criteria.

## Verification

Seven new primitive tests cover intrinsic identity and typed attributes; namespace/base/language
metadata; membership mutation and final owner updates; downward-navigation guards; **10,000** closed
headers spilling within the cache bound; source cleanup and Unix permissions; schema-info snapshots;
foreign/invalid handles; explicit observer rejection; and cancellation. Together with existing replay
and climbing tests, Release passes **60 net10.0 / 56 net8.0** focused checks. Full Release and Debug each
pass **3,256 net10.0 / 3,153 net8.0** unit tests. The solution builds.

A frozen **411**-file core snapshot is used by the NativeAOT consumers. The native primitive probe writes
**100,000** closed headers plus an incomplete owner, and checks typed metadata, source identity,
membership, the incomplete-header guard, bounded cache and private file cleanup. It passes alongside
the retained saved consumers, including the legacy merge
and immutable v2 fork cases. This primitive probe is not a 2 GiB XML transform or an `except` execution
claim. Native publication has no warnings. Evidence is under `/tmp/xsl3-p7-deferred-header-final-native/`.

Final ordinary W3C **46**, upstream **1,654** and differential **394** per framework, and all **13**
streaming controller tests pass. Every execution profile is freshly run, including explicit XSLT 2.0
and posture/sweep runs. Nine report files (eight execution profiles plus parse coverage) have no new
failure IDs against the preceding ID-provenance checkpoint. Strict auditing retains exit **1**.
No baselines, commits or pushes are changed by this increment.

| Profile | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| XSLT 3.0 | 8849 | 19 | 41 | 34 |
| XSLT 2.0 | 4590 | 8 | 10 | 7 |
| Unstreamed development | 2693 | 18 | 1 | 5 |
| Strict forward inputs | 2690 | 21 | 1 | 7 |
| Posture and sweep | 790 | 129 | 0 | 0 |
| Schema-aware | 843 | 1 | 2 | 6 |
| Schema-aware strict | 842 | 2 | 2 | 6 |
| QT3 | 21884 | 0 | 99 | 67 |

QT3 parse coverage is **22,359/22,359**. Final functional logs are
`/tmp/xsl3-p7-deferred-header-final-verified-*`; reports and the failure-ID comparison are in
`/tmp/xsl3-p7-deferred-header-final-reports/`.

## Ordinary performance

The final isolated three-pair gate against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` passes, exit **0**.
Time geometric mean over **39** timed rows is **1.005276705514**; maximum allocation ratio is **1.037242922079**.
The same harness measures the accumulated branch against the baseline. This is not a per-increment
speedup claim. Sorting compilation (**1.166**) and regex 1 KB (**1.170**) remain individual timing
outliers. Aggregate success does not make every row faster.

Only rounds **1–3** are archived in `/tmp/xsl3-p7-deferred-header-final-perf-data/`, with `compare.txt`
and the exact `summary.json`. No other task-owned CPU job ran during this final gate. Its log is
`/tmp/xsl3-p7-deferred-header-final-perf.log`.

An earlier candidate and its measurements remain under `/tmp/xsl3-p7-deferred-header-*`. Final review
added the incomplete-child guard after that candidate was frozen, and its old timing overlapped the
focused corrective build. Those measurements are superseded and are not used as acceptance evidence.
All final native and timing evidence uses the corrected frozen source.

## Generated-input native probes

The retained fragment probe passes all four normal/debug children at exact **209,715,200**
and **2,147,483,648** input bytes. Its stylesheet reads `doc/row/value` through the forward
source-document path. It does not exercise overlapping value replay or a deferred climbing `except`
expression; replaying styles are covered separately by the main native consumer. Each child consumes the full requested input, validates an independently
computed output digest, emits before EOF, and closes its owned input.

| Saved style | Exact input bytes | First output after input bytes | Child elapsed ms | Lifetime peak RSS bytes |
|---|---:|---:|---:|---:|

| normal | 209715200 | 105213952 | 1161.7976 | 25083904 |
| normal | 2147483648 | 1074098176 | 11770.9667 | 24313856 |
| debug | 209715200 | 105213952 | 1168.7632 | 27443200 |
| debug | 2147483648 | 1074098176 | 12141.1938 | 27131904 |

RSS is the maximum of controller-observed and child-reported full-lifetime peaks, with no baseline
subtraction. Ordinary ICU support and the **209,715,200-byte** heap/RSS guard remain in place. These
focused probes do not satisfy the separate **20,971,520-byte** push RSS limit, the canonical throughput
target, or the future deferred-set memory proof. Single-child elapsed times are not comparative
benchmarks. Evidence is in `/tmp/xsl3-p7-deferred-header-final-scale-native/`.

After execution, all **411** live/frozen source/configuration hashes match. Native artifact manifests
record **98** main artifacts (**95** saved DLLs plus three native publish artifacts) and **5** scale
artifacts (**2** saved DLLs plus three publish artifacts). Every retained saved-DLL hash is unchanged,
including the separately protected v2 fork consumer. Final functional, publish, native, performance
and scale terminal statuses are **0**; strict conformance separately retains its nonzero audit status.
Both final native publications have no warnings.

Full P7 remains active. At this storage checkpoint compiler integration was still required; the
[subsequent deferred climbing checkpoint](streaming-deferred-climbing-2026-10-25.md) supplies it.
Deferred accumulator snapshots, the remaining semantic cases and analysis gates, exact push RSS,
canonical throughput and current-source remote Linux x64 evidence remain required. The integration design is recorded in
`/tmp/xsl3-p7-deferred-header-integration-design.md`; no stage is declared complete by this storage work.

The subsequent [accumulator-phase checkpoint](streaming-accumulator-phases-2026-10-25.md) integrates
observer snapshots and late publication with this source-owned store. Its scalar/container path spills;
general identity-bearing node/closure histories and the remaining full P7 gates remain open.
