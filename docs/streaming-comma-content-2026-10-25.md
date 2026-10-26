# Mixed node paths and incremental simple content, 2026-10-25

This follow-up continues the [relative-value/signature checkpoint](streaming-relative-values-signatures-2026-10-25.md)
on `feature/p7-streaming-analysis`. The full Phase 7 goal remains active; `supports-streaming` stays `no`.

The [recursive-function follow-up](streaming-recursive-functions-2026-10-25.md) supersedes the function
argument/continuation finding at the end of this checkpoint for its documented eligible call shapes.

## Mixed comma paths

Seven new ordered-path regressions initially failed. An expression such as
`($extraItem, /BOOKLIST/BOOKS/ITEM)/PRICE` fell through to a document-order sort that exhausted live
source positions before their children were consumed. The equivalent insertion expression already
had an ordered merge.

The compiler now shares that lowering between comma sequences and `insert-before`: flatten nested
sequence/insertion populations, project the complete predicate-free child path within each producer,
and merge their results by document order and node identity. Insertion-position validation remains.
The live input advances after its current result is consumed. Ordered-source proofs and grounded
sorting retain their existing roles. The result carries its canonical-order type, allowing a following
attribute path to avoid another eager sort. No general navigation guard is removed.

Coverage includes mixed and nested sequences/insertions, empty inputs, node/position errors, reordered
and aliased grounded inputs, independent constructors with equal values, copying, text joining,
attributes, both APIs and saved/debug compilation. Crawling mixed populations retain static rejection.
A 5,000-record regression emits before source EOF and retains at most two fixture-depth chains of open
source headers; that assertion measures open headers rather than whole-process RSS.

## Incremental text construction

The first focused NativeAOT scale probe exposed an additional defect after small mixed-path cases
passed. At 200 MiB input, `xsl:value-of` emitted its first bytes only at EOF and peak RSS was about
169–171 MB. Both 2 GiB runs exited nonzero under a 200 MiB heap guard. The scalar simple-content call
cached the atomized population before joining it, so fixing path traversal alone did not bound memory.

Streamed simple-content calls now carry an IL-generation annotation. A text constructor with that
annotation iterates its source directly and emits chunks through a small per-constructor state. The
constructor still creates exactly one text node when its result is materialized; separate constructors
retain separate identities and empty text construction remains valid. Attribute/comment/PI/namespace
writer states retain their routing, and raw-text construction retains disable-output-escaping.
Conditional and singleton-loop normalization keep annotated text constructors on the writing path.
The pattern source and generated optimizer are updated together.

The state merges adjacent source text without separators, drops empty text, retains boundaries caused
by empty arrays/nilled/list-typed values, atomizes other items with canonical rules, and inserts the
separator between resulting parts. Cancellation and atomization faults remain sticky. The three new
runtime entry points are additive; the scalar `X31SimpleContent` entry point remains for old saved
consumers and contexts requiring a string.

Nine new early-output cases initially failed, covering direct output, conditional branches and
singleton loops. Regression coverage also checks captured single/empty/distinct text nodes and
rollback after a late atomization error. A runtime test compares all 125 triples of representative
atomic, text and array items against the unchanged canonical scalar function. Existing typed-list,
nilled-value, namespace, replay and incremental normalization tests remain covered.

A full Debug run exposed source-line reuse in the 2.0+ choose compiler: the choose instruction reused
the first when branch's QIL node. The debug result now has the same separating sequence wrapper used
by the 1.0 compiler. The assertion remains enabled. Normal/debug saved conditional styles also pass.

## Focused large-input native verification

The final macOS arm64 NativeAOT probe passes normal/debug styles at exact 200 MiB and 2 GiB input.
It uses the existing generated-input fixture and test SHA-256 implementation, separate streaming
oracles for the two permitted cross-document orders, public transformation/writer APIs and normal ICU.
The process consumes the full requested input and validates output digests without retaining output.
The effective heap allowance and absolute RSS limit are both 209,715,200 bytes for this focused test.
This is not the plan's 20 MiB push gate.

| Compilation | Input bytes | Records | Output bytes | Full-lifetime RSS bytes | Live managed bytes | Input bytes at first output |
|---|---:|---:|---:|---:|---:|---:|
| Normal | 209715200 | 1823610 | 3647225 | 25411584 | 702664 | 356352 |
| Normal | 2147483648 | 18673770 | 37347545 | 25427968 | 702872 | 356352 |
| Debug | 209715200 | 1823610 | 3647225 | 27787264 | 814632 | 356352 |
| Debug | 2147483648 | 18673770 | 37347545 | 27295744 | 814304 | 356352 |

RSS is the greater of child getrusage and outer wait4, including startup and reporting. No baseline
subtraction is used. Timings in these logs are diagnostic because other correctness/native checks
were active. The 2 GiB result emits its first bytes after 356,352 input bytes; RSS stays around 25–28 MB
across the two sizes. Individual large atomic strings and explicit materialized results can still grow.

Raw before-probe failures are `/tmp/xsl3-p7-comma-paths-scale-native/`; final results are
`/tmp/xsl3-p7-comma-content-scale-native/`. The first candidate's queued benchmark was deliberately
stopped after this scale failure. The next candidate's partial Linux matrix and queued benchmark were
stopped after the Debug source-line failure. Their partial reports, exits and termination reasons are
retained. They are not final-source passes. The initial focused generator also required correcting its
friend-assembly name; the original compile-error log is retained.

## Functional and saved-binary verification

Full Release and Debug units pass **3,172 on .NET 10 / 3,073 on .NET 8**. The solution builds.
Twelve new normal/debug saved styles exercise comma-path values/copy/attributes, text-node identity,
empty text, escaping and choose branches. Retained version-2 fork and old materialized-merge consumers
remain unchanged and pass. The final native publish has no trim/AOT warnings. There are 64 hashed
saved/native artifacts in the main probe and three in the focused scale probe.

Final evidence prefixes are `/tmp/xsl3-p7-comma-content-verified-`,
`/tmp/xsl3-p7-comma-content-native/`, `/tmp/xsl3-p7-comma-content-scale-native/` and
`/tmp/xsl3-p7-comma-content-linux/`. Source snapshots contain 404 core/configuration and 570 Linux
source/controller/probe/test files. The 14-file implementation/test increment against the preceding
checkpoint is `/tmp/xsl3-p7-comma-content-increment.patch`, with hashes in `.json`.


All eight complete conformance profiles have no new failure IDs. Main strict improves from
**2,683/28/1 to 2,687/24/1**, closing `sx-comma-041`, `sx-comma-043`, `sx-comma-141` and `sx-comma-143`.
Other profile failure sets and expected-code counts remain unchanged. No regression baseline is
edited; `docs/conformance-sets.md` is regenerated from the final reports.

| Profile | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| XSLT 3.0 | 8847 | 21 | 41 | 34 |
| XSLT 2.0 | 4588 | 9 | 11 | 7 |
| Streaming unstreamed | 2692 | 19 | 1 | 5 |
| Streaming strict | 2687 | 24 | 1 | 7 |
| S1 posture/sweep | 790 | 129 | 0 | 0 |
| Schema-aware | 843 | 1 | 2 | 6 |
| Schema-aware strict | 842 | 2 | 2 | 6 |
| QT3 | 21884 | 0 | 99 | 67 |

Normal W3C passes 46 facts, upstream passes 1,654 and differential passes 394 on each framework, and
all 13 controller tests pass. The strict audit retains exit 1 for its remaining failures; the full
regression driver exits 0. Comparisons and failure IDs are in
`/tmp/xsl3-p7-comma-content-reports/profile-comparison.json`.


## Exact native memory gates

The final local Linux arm64 NativeAOT nightly matrix passes **48 children / zero failures**. All 16
measured streams consume exactly **2,147,483,648 bytes** and validate independent output fingerprints
under the effective **209,715,200-byte** managed allowance. The push matrix completes **48 children with
16 RSS failures** at requested **209,715,200-byte** input and **20,971,520-byte** allowance. 12 measured
streams consume and validate the whole input. Startup/warmup exits consume zero measured input and
have no measured output fingerprint: iterate, merge, fork, function-parameter.

Both native-evidence flags are false; normal ICU remains enabled. RSS is the maximum child getrusage
and outer wait4 full-lifetime counter, including startup and reporting. No baseline subtraction is
used. The cached SDK image is
`sha256:70b5de4a18461652574704cee7c7f8797788c54aa45a5de4918ea1cae97cd1e8` (SDK 10.0.400/runtime 10.0.11).
The managed intermediate remains rejected by the native guard. The Docker driver retains exit 1 for
the push failures. These are local Linux arm64 results; remote Linux x64 CI is unverified.

| Workload | Nightly full-lifetime RSS bytes | Push measured input bytes | Push full-lifetime RSS bytes | Push outcome |
|---|---:|---:|---:|---|
| identity | 29192192 | 209715200 | 24465408 | budget-exceeded |
| adjacent | 26722304 | 209715200 | 25661440 | budget-exceeded |
| accumulator | 30478336 | 209715200 | 25698304 | budget-exceeded |
| iterate | 27860992 | 0 | 22401024 | budget-exceeded-before-measurement |
| merge | 30973952 | 0 | 21766144 | budget-exceeded-before-measurement |
| fork | 31047680 | 0 | 21676032 | budget-exceeded-before-measurement |
| source-document | 29356032 | 209715200 | 24608768 | budget-exceeded |
| climbing-union | 30486528 | 209715200 | 25706496 | budget-exceeded |
| climbing-intersection | 30531584 | 209715200 | 25673728 | budget-exceeded |
| type-assertion | 25837568 | 209715200 | 24526848 | budget-exceeded |
| type-inspection | 25817088 | 209715200 | 24477696 | budget-exceeded |
| copy-select | 29192192 | 209715200 | 24469504 | budget-exceeded |
| conditional-content | 29298688 | 209715200 | 24932352 | budget-exceeded |
| template-parameter | 27017216 | 209715200 | 25804800 | budget-exceeded |
| function-parameter | 30642176 | 0 | 21544960 | budget-exceeded-before-measurement |
| overlap-values | 30654464 | 209715200 | 26054656 | budget-exceeded |

Raw reports, commands, child stdout/stderr and measured summaries are retained under
`/tmp/xsl3-p7-comma-content-linux/`. Gate elapsed times are diagnostic because correctness checks
overlapped part of the matrix. The later ordinary/process timing comparisons wait for all owned
correctness, native and focused scale jobs to finish.


## Ordinary performance gate

Three alternating pairs against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` pass with exit **0**.
Both sides use the same current harness. The timing geometric mean across 39 non-cold rows is
**1.024354047451**. Maximum allocation ratio is
**1.037242922079** (recursion compile).
All 40 rows, three raw pairs and summary are retained in
`/tmp/xsl3-p7-comma-content-perf-data/`. Individual timing outliers remain visible despite aggregate success:

| Case | Base median ms | Head median ms | Head/base | Allocation ratio |
|---|---:|---:|---:|---:|
| grouping compile | 0.6250000 | 0.6667000 | 1.066720000000 | 1.024978319290 |
| sorting compile | 0.5560000 | 0.6190000 | 1.113309352518 | 1.023910278898 |
| keys compile | 0.3776000 | 0.5864000 | 1.552966101695 | 1.027522935780 |
| dispatch compile | 1.5989000 | 1.8808500 | 1.176339983739 | 1.028340316544 |
| regex 1kb | 0.0047000 | 0.0055000 | 1.170212765957 | 1.004803689233 |


## Paired 100 MB process comparison

All **96 trials validate / zero failures**, using three alternating tree/stream pairs per workload,
exactly 100,000,000 bytes and equal 4 GiB managed guards. All 83 copied host artifacts remain unchanged.
The process driver exits **0**. The canonical throughput target is separately **evaluated and failed**:
identity, fork and source-document streaming remain slower than tree plus transform.

| Workload | Tree median ms | Stream median ms | Stream/tree | Winner |
|---|---:|---:|---:|---|
| identity | 2342.125208 | 2956.697833 | 1.262399560408 | Tree observed median |
| adjacent | 1780.014167 | 1055.423125 | 0.592929620767 | Stream observed median |
| accumulator | 6142.866625 | 4337.013917 | 0.706024431549 | Stream observed median |
| iterate | 1924.063084 | 1400.677000 | 0.727978729828 | Stream observed median |
| merge | 13448.630583 | 3676.172458 | 0.273349203498 | Stream observed median |
| fork | 1901.942417 | 6061.097667 | 3.186793465893 | Tree observed median |
| source-document | 2284.191125 | 2904.188084 | 1.271429545546 | Tree observed median |
| climbing-union | 1984.230500 | 2956.224500 | 1.489859419054 | Tree observed median |
| climbing-intersection | 1798.130625 | 2670.236250 | 1.485006824796 | Tree observed median |
| type-assertion | 1285.992375 | 890.870084 | 0.692749118361 | Stream observed median |
| type-inspection | 1087.714500 | 733.550041 | 0.674395754584 | Stream observed median |
| copy-select | 2259.918000 | 2942.055667 | 1.301841777888 | Tree observed median |
| conditional-content | 1965.323500 | 2056.630375 | 1.046458954467 | No meaningful winner within 5% band |
| template-parameter | 1633.156750 | 1665.139709 | 1.019583520688 | No meaningful winner within 5% band |
| function-parameter | 1558.577667 | 2558.763208 | 1.641729675830 | Tree observed median |
| overlap-values | 1895.179750 | 4891.004375 | 2.580760149532 | Tree observed median |

The same trials record early output and RSS. These heap-guarded process comparisons do not substitute
for the absolute native RSS gates above.

| Workload | Tree first output ms | Stream first output ms | Tree input at first output | Stream input at first output | Tree median RSS bytes | Stream median RSS bytes |
|---|---:|---:|---:|---:|---:|---:|
| identity | 1067.807208 | 0.205458 | 100000000 | 8192 | 488816640 | 92569600 |
| adjacent | 1454.441084 | 3.995042 | 100000000 | 356352 | 747765760 | 95518720 |
| accumulator | 6142.844417 | 4337.006709 | 100000000 | 100000000 | 1427423232 | 95633408 |
| iterate | 1924.053000 | 1400.660541 | 100000000 | 100000000 | 552337408 | 95354880 |
| merge | 10264.747375 | 5.115250 | 100000000 | 122880 | 1961492480 | 96059392 |
| fork | 1257.615125 | 2.357625 | 100000000 | 36864 | 522895360 | 96583680 |
| source-document | 1041.062791 | 0.233500 | 100000000 | 8192 | 449921024 | 92979200 |
| climbing-union | 1829.318500 | 1.639875 | 100000000 | 53248 | 679559168 | 95780864 |
| climbing-intersection | 1741.634958 | 8.001250 | 100000000 | 204800 | 680001536 | 95567872 |
| type-assertion | 1073.675750 | 0.351584 | 100000000 | 36864 | 550912000 | 92405760 |
| type-inspection | 1087.706834 | 733.529458 | 100000000 | 100000000 | 531218432 | 92307456 |
| copy-select | 1023.325125 | 0.199666 | 100000000 | 8192 | 427442176 | 92864512 |
| conditional-content | 1306.548791 | 0.180584 | 100000000 | 8192 | 657440768 | 92864512 |
| template-parameter | 1097.351083 | 2.132250 | 100000000 | 122880 | 503676928 | 95404032 |
| function-parameter | 1014.632958 | 3.463333 | 100000000 | 122880 | 448086016 | 95879168 |
| overlap-values | 1134.850291 | 1.159083 | 100000000 | 24576 | 470597632 | 95682560 |

Full commands, per-round values, digests, engine/runtime hashes and child stdout/stderr are under
`/tmp/xsl3-p7-comma-content-process-data/`. Accumulator, iterate and type-inspection scalar results
naturally require EOF. The same frozen-host sweep for the seven canonical workloads at the plan's
1 MB and 1 GB sizes has now completed under `/tmp/xsl3-p7-comma-content-scaling-data/` (exit **1**):
**84 attempted trials, 72 valid, 12 failed**. All 42 trials at 1 MB and all 21 streamed trials at 1 GB
passed. At 1 GB, all three tree trials for adjacent, accumulator, merge and fork failed with
`System.OutOfMemoryException` under the unchanged 4 GiB heap guard. Identity, iterate and source-document
passed in both modes. All 83 host artifacts retained their recorded hashes. These failures leave four
large-input speed comparisons unavailable; they are not successful performance results. Combined with
the completed 100 MB trials above, all three planned sizes have been attempted. The throughput target
remains failed at 100 MB. Per-trial streams and a derived summary are retained with the raw report.

## Remaining Phase 7 work

The full goal remains active. The absolute 20 MiB RSS gate and canonical throughput target still fail.
All three planned scaling sizes have been attempted, with the large-input tree failures above. Remaining semantic work includes shallow
function argument/continuation delivery, non-monotonic ancestor difference, source validation and
typed/serialization cases. URI-fragment policy and remote Linux x64 CI evidence remain open. The
completed S1 discrepancy audit and final-Recommendation/suite conflicts remain documented in prior
checkpoints; raw S1 is unchanged at 790/129/0. Large atomic/final-string results and explicit
materialization can still grow. `supports-streaming` stays `no`; no commit or push is performed.

Reduced first-navigation failures for two shallow-descent functions are captured in
`/tmp/xsl3-p7-shallow-continuation-repro/`: tree execution succeeds while forward execution fails inside
FirstChild/HasChildren in the function body. This narrows the next investigation to argument delivery;
the precise invalidation point is not yet proven.
