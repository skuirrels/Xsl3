# Node-set intersection and mode declarations, 2026-10-25

This is the node-set/mode checkpoint before the subsequent bounded-value and merge-output fixes.
See [the follow-up](streaming-bounded-values-merge-2026-10-25.md) for current validation.

This follow-up repairs two issues found while reviewing Phase 7. It follows the
[value-replay checkpoint](streaming-value-replay-2026-10-25.md); that checkpoint's exact native
memory results remain attached to its frozen source.

## Grounded node-set merge

The 100 MB process comparison exposed a quadratic scan in `X31SetOp`: after sorting and deduplicating
both operands, intersection and difference searched the complete right operand for every left node.
A managed stack capture identified this method while the tree intersection child was still computing.
The comparison-count regression reproduced **1,015,882 comparisons** for two disjoint 1,000-node
operands, including ordering and deduplication. Both operations also missed cancellation during the scan.

Intersection and difference now merge the two ordered operands with forward cursors. The merge costs
O(n + m) comparisons after ordering. Both complete operands are still validated before evaluation;
an empty operand cannot hide a late atomic item. Results retain the runtime's existing node identity
and stable document ordering, including nodes from different documents. Union keeps its existing path.
Cancellation is checked before and during the merge, including the difference's remaining left items.

Thirteen new runtime cases cover the comparison bound, unsorted duplicate inputs, foreign-document
ordering, attributes and namespace nodes, empty inputs, late type errors and cancellation during a
comparison. The complexity assertion includes sorting and permits at most 50,000 comparisons; it does
not depend on wall-clock timing. These tests pass on .NET 10 and .NET 8.

## Per-attribute mode precedence

A higher-precedence `xsl:mode` that supplies only `on-no-match` previously concealed conflicting
lower-precedence `streamable` attributes. The merged mode retained one precedence for the entire
mode, losing the precedence at which each inherited attribute was defined. This is the failure in
W3C `mode/mode-1506`.

The loader now retains the defining precedence for each effective attribute. Equal-precedence
conflicts raise XTSE0545 unless that same attribute has a higher-precedence definition. Equivalent
boolean lexical forms compare as values. Merging a later same-precedence `typed` attribute also updates
the effective typed-mode behavior; previously this attribute could remain `unspecified`.

These rules follow [XSLT 3.0 section 6.6.1](https://www.w3.org/TR/xslt-30/#declaring-modes).
Fourteen new cases cover named and unnamed modes, imported conflicts, higher definitions of the same
attribute, boolean aliases, independent inheritance and effective typed-mode checks.

## Exhaustive S1 discrepancy audit

The preceding audit enumerated all 4,760 expressions in 129 raw failing posture/sweep cases.
This follow-up checks **all 2,723 mismatches**, rather than only the first failure in each case,
against a separate, restricted transcription of the final Recommendation's type/axis/path rules.
Every mismatch is a predicate-free two-axis relative path in a striding context. The checker rejects
unsupported shapes and verifies each outcome against the frozen current engine as well as the earlier
raw result. All 2,723 checks pass.

| Final result | Subexpression mismatches |
|---|---:|
| Attribute, striding/consuming | 850 |
| Attribute, striding/motionless | 925 |
| Namespace, striding/consuming | 272 |
| Namespace, striding/motionless | 272 |
| Empty, grounded/consuming | 383 |
| Empty, grounded/motionless | 21 |

The applicable rules are [static axis types, section 19.1.1](https://www.w3.org/TR/xslt-30/#static-type-of-steps),
[path composition, section 19.8.8.8](https://www.w3.org/TR/xslt-30/#streamability-of-path-expressions),
and [axis transitions, section 19.8.8.9](https://www.w3.org/TR/xslt-30/#streamability-of-axis-steps).
Attribute/namespace steps produce striding posture in the applicable final table rows; statically empty
steps produce grounded/motionless results, while path composition preserves any consuming left sweep.
Namespace names with a non-empty namespace URI cannot match namespace nodes, whose names have empty
namespace URI under XDM section 6.4.1.

The eight other cases have neither XPath nor XSLT input. They remain visible raw failures. The raw
profile is still **790 passed / 129 failed / 0 not run**: no catalogue, filtering rule, acceptance count
or S1 baseline is changed. This audit explains the remaining recorded discrepancies; it does not turn
the raw profile into a passing exit gate or prove every expression outside those discrepancies.

Evidence is `/tmp/xsl3-p7-set-merge-mode-s1-proof/`: the restricted checker, project, terminal run log,
per-expression proof records, hashes of all referenced catalogues and artifact/specification hashes.
The full preceding expression inventory is `/tmp/xsl3-p7-item-parameter-s1-audit/all-mismatches.json`.

## Validation

Full Release and Debug unit suites pass **3,087 on .NET 10 / 2,993 on .NET 8**. The normal W3C harness
passes. Saved normal/debug macOS arm64 NativeAOT probes, including the overlapping value/fork probes
and retained compatibility consumers, complete successfully without trim/AOT warnings. The immutable
version-2 fork assembly remains SHA-256
`89ba201ad9e21c7a3a4add5231c114f5aee9a2f9eef4bdd9472a41e680b5caee`.

Evidence prefixes are `/tmp/xsl3-p7-set-merge-mode-verified-` and
`/tmp/xsl3-p7-set-merge-mode-native/`. The resumed functional driver exits 0; the initial driver
retains exit 1 for the stale unstreamed baseline entry. The strict profile is a deliberately failing
audit with exit 1, retained separately from the other checks. All 400 current/frozen core and
configuration hashes match; saved-data and public runtime ABI are unchanged.
The unstreamed profile's first run correctly rejected its stale known-failure entry for
`mode/mode-1506` after the test passed. Only that entry is removed from its baseline.

Eight full profiles have **no new failure IDs** compared with the value-replay checkpoint. Only
`mode/mode-1506` changes from failure to pass in both streaming execution profiles.

| Profile | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| XSLT 3.0 | 8847 | 21 | 41 | 34 |
| XSLT 2.0 | 4588 | 9 | 11 | 7 |
| Streaming, unstreamed | 2692 | 19 | 1 | 5 |
| Streaming, strict | 2681 | 30 | 1 | 7 |
| Posture/sweep | 790 | 129 | 0 | 0 |
| Schema aware | 843 | 1 | 2 | 6 |
| Schema aware, strict | 842 | 2 | 2 | 6 |
| QT3 | 21884 | 0 | 99 | 67 |

The solution, 46 W3C harness facts, upstream 1,654 per framework, differential 394 per framework and
thirteen controller tests pass. Frozen reports and `profile-comparison.json` are in
`/tmp/xsl3-p7-set-merge-mode-reports/`. Generated conformance tables use these reports.

## Benchmark provenance

The preceding process matrix was intentionally stopped to repair the verified algorithmic issue. Its
48 completed trials, three tree-merge out-of-memory failures and profiling captures remain in
`/tmp/xsl3-p7-value-replay-owned-process-data/` and the adjacent stack/sample files.
`termination-reason.json` records the reason; the controller exited 143. This is an incomplete matrix.

The follow-up process harness copies its host and engine into
`/tmp/xsl3-p7-set-merge-mode-process-host/` and hashes the copied artifacts. Later repository builds
cannot change these child binaries. It retains all sixteen workloads, 100 MB exact inputs, three
alternating tree/stream pairs and the equal 4 GiB managed-heap guard. Failed comparisons must retain
no ratio or winner. The ordinary three-pair gate precedes this matrix and follows completed functional
and NativeAOT jobs.

## Ordinary performance gate

One gate run exits **0** against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` over exactly three alternating pairs. Timing geometric mean is **0.990166466732**; maximum allocation ratio is **1.046810748541** (grouping cold, 1,597,240/1,672,008 bytes). Cold time is excluded from the 39-row timed mean; cold allocation is included. Cold time ratio is **1.047410986817** (135.010900/141.411900 ms).

The same ordinary harness and corpus runs on both versions with the separate process host excluded equally. All task-owned functional/native CPU jobs were terminal before this comparison. The temporary base worktree is removed. This compares the complete working tree with the base; it does not isolate the causal effect of this increment.

| Workload | Size | Base 0f2ddcb ms | Current branch ms | Head/base | Base allocated bytes | Current allocated bytes | Allocation ratio | Winner |
|---|---|---:|---:|---:|---:|---:|---:|---|
| grouping | cold | 135.010900 | 141.411900 | 1.047411 | 1597240 | 1672008 | 1.046811 | No meaningful winner (5% band) |
| grouping | compile | 0.619800 | 0.645600 | 1.041626 | 186810 | 191479 | 1.024993 | No meaningful winner (5% band) |
| sorting | compile | 0.562800 | 0.483300 | 0.858742 | 182433 | 186794 | 1.023905 | Current observed median |
| keys | compile | 0.501550 | 0.296700 | 0.591566 | 194232 | 199587 | 1.027570 | Current observed median |
| join | compile | 0.096500 | 0.092400 | 0.957513 | 150946 | 154034 | 1.020458 | No meaningful winner (5% band) |
| regex | compile | 0.081100 | 0.082500 | 1.017263 | 165111 | 168236 | 1.018927 | No meaningful winner (5% band) |
| strings | compile | 0.083600 | 0.084700 | 1.013158 | 177987 | 180664 | 1.015040 | No meaningful winner (5% band) |
| maps | compile | 0.155400 | 0.158300 | 1.018662 | 285536 | 291832 | 1.022050 | No meaningful winner (5% band) |
| fold-left | compile | 0.178200 | 0.181400 | 1.017957 | 305116 | 308652 | 1.011589 | No meaningful winner (5% band) |
| iterate | compile | 0.146400 | 0.150600 | 1.028689 | 238285 | 242413 | 1.017324 | No meaningful winner (5% band) |
| identity | compile | 0.044200 | 0.044500 | 1.006787 | 97900 | 99595 | 1.017314 | No meaningful winner (5% band) |
| dispatch | compile | 1.545000 | 1.837000 | 1.188997 | 4004366 | 4117850 | 1.028340 | Base observed median |
| recursion | compile | 0.118700 | 0.121100 | 1.020219 | 237817 | 246673 | 1.037239 | No meaningful winner (5% band) |
| json | compile | 0.066500 | 0.067300 | 1.012030 | 156509 | 158367 | 1.011872 | No meaningful winner (5% band) |
| grouping | 1kb | 0.002500 | 0.002500 | 1.000000 | 13113 | 13183 | 1.005338 | No meaningful winner (5% band) |
| sorting | 1kb | 0.002400 | 0.002200 | 0.916667 | 12206 | 12293 | 1.007128 | Current observed median |
| keys | 1kb | 0.004500 | 0.004600 | 1.022222 | 17468 | 17556 | 1.005038 | No meaningful winner (5% band) |
| join | 1kb | 0.003600 | 0.003700 | 1.027778 | 14120 | 14192 | 1.005099 | No meaningful winner (5% band) |
| regex | 1kb | 0.004700 | 0.005500 | 1.170213 | 15613 | 15688 | 1.004804 | Base observed median |
| strings | 1kb | 0.003300 | 0.003300 | 1.000000 | 12830 | 12902 | 1.005612 | No meaningful winner (5% band) |
| maps | 1kb | 0.006500 | 0.006500 | 1.000000 | 27221 | 27365 | 1.005290 | No meaningful winner (5% band) |
| fold-left | 1kb | 0.004300 | 0.004200 | 0.976744 | 18803 | 18963 | 1.008509 | No meaningful winner (5% band) |
| iterate | 1kb | 0.004200 | 0.004200 | 1.000000 | 23090 | 23203 | 1.004894 | No meaningful winner (5% band) |
| identity | 1kb | 0.002800 | 0.002900 | 1.035714 | 10812 | 10884 | 1.006659 | No meaningful winner (5% band) |
| dispatch | 1kb | 0.007300 | 0.007400 | 1.013699 | 23594 | 23665 | 1.003009 | No meaningful winner (5% band) |
| recursion | 1kb | 0.003700 | 0.003600 | 0.972973 | 17571 | 17650 | 1.004496 | No meaningful winner (5% band) |
| json | 1kb | 0.005900 | 0.005900 | 1.000000 | 35039 | 35173 | 1.003824 | No meaningful winner (5% band) |
| grouping | 1mb | 0.842500 | 0.837700 | 0.994303 | 1985614 | 1985686 | 1.000036 | No meaningful winner (5% band) |
| sorting | 1mb | 2.144950 | 2.100300 | 0.979184 | 3410835 | 3428507 | 1.005181 | No meaningful winner (5% band) |
| keys | 1mb | 3.606600 | 3.563350 | 0.988008 | 6152454 | 6152541 | 1.000014 | No meaningful winner (5% band) |
| join | 1mb | 2.456350 | 2.429000 | 0.988866 | 4525269 | 4525339 | 1.000015 | No meaningful winner (5% band) |
| regex | 1mb | 5.737000 | 5.756850 | 1.003460 | 13071945 | 13072017 | 1.000006 | No meaningful winner (5% band) |
| strings | 1mb | 2.836250 | 2.815500 | 0.992684 | 5462927 | 5462999 | 1.000013 | No meaningful winner (5% band) |
| maps | 1mb | 3.988400 | 4.182700 | 1.048716 | 10118617 | 10197281 | 1.007774 | No meaningful winner (5% band) |
| fold-left | 1mb | 2.728700 | 2.621600 | 0.960751 | 7319894 | 7443142 | 1.016837 | No meaningful winner (5% band) |
| iterate | 1mb | 1.220100 | 1.164500 | 0.954430 | 3114704 | 3132445 | 1.005696 | No meaningful winner (5% band) |
| identity | 1mb | 1.891700 | 1.901050 | 1.004943 | 3154328 | 3154384 | 1.000018 | No meaningful winner (5% band) |
| dispatch | 1mb | 1.356400 | 1.364500 | 1.005972 | 2650127 | 2650199 | 1.000027 | No meaningful winner (5% band) |
| recursion | 1mb | 1.218000 | 1.212000 | 0.995074 | 2781847 | 2781928 | 1.000029 | No meaningful winner (5% band) |
| json | 1mb | 9.694900 | 9.292750 | 0.958519 | 18294390 | 18346958 | 1.002873 | No meaningful winner (5% band) |

Winner labels use a 5% diagnostic band; they do not replace an uncertainty test. Exact round files and per-row medians/allocations are retained in `/tmp/xsl3-p7-set-merge-mode-perf-data/`. Only rounds 1–3 are used.

Timed rows above 1.05 remain observed regressions requiring attribution:

- **dispatch compile**: ratio **1.188996763754**, base/current **1.545000/1.837000 ms**. Base rounds `[1.6916, 1.545, 1.5248499999999998]`, current rounds `[1.9321, 1.68495, 1.837]`. Allocation ratio **1.028340066817**.
- **regex 1kb**: ratio **1.170212765957**, base/current **0.004700/0.005500 ms**. Base rounds `[0.0048, 0.0046, 0.0047]`, current rounds `[0.0056, 0.0054, 0.0055]`. Allocation ratio **1.004803689233**.

No additional performance run was selected to replace these results. Earlier individual outliers are not declared fixed by their absence. A fresh 100 MB process comparison over all sixteen workloads follows this isolated gate. It has its own raw trials and provenance; the earlier full three-size matrix remains evidence for its own snapshot. Throughput/latency exits remain open.


## Isolated 100 MB process comparison

The corrected source completes **96 trials**: three alternating tree/stream pairs over all sixteen
workloads, including the canonical seven. There are **48 valid streams**,
**45 valid trees** and **3 tree merge OOM failures** under equal 4 GiB
managed heap guards. Tiered compilation is disabled equally. Every valid trial checks its independent
output fingerprint. The canonical throughput target remains incomplete because merge has no valid tree
comparison. A failed trial receives no ratio or winner.

This matrix starts after all functional/native checks and the isolated ordinary comparison finish.
It measures source parsing plus transformation; compilation, warmup and oracle generation are outside
the timed interval. RSS is the full-lifetime wait4 counter. Both modes use the same engine and generated
source. Runtime: .NET 10.0.11, macOS 27.0.1, Arm64.

| Workload, 100 MB | Xsl3 tree ms | Xsl3 stream ms | Stream/tree | Tree MiB/s | Stream MiB/s | Winner |
|---|---:|---:|---:|---:|---:|---|
| identity | 2269.714458 | 2991.570208 | 1.318038 | 42.017370 | 31.878721 | Tree observed median |
| adjacent | 1793.606208 | 1078.831333 | 0.601487 | 53.170775 | 88.398834 | Stream observed median |
| accumulator | 6188.303625 | 4410.882166 | 0.712777 | 15.410917 | 21.620943 | Stream observed median |
| iterate | 1628.776042 | 1403.830708 | 0.861893 | 58.551593 | 67.933712 | Stream observed median |
| merge | incomplete | incomplete | — | — | — | No comparison |
| fork | 1879.115000 | 6033.836042 | 3.210999 | 50.751248 | 15.805440 | Tree observed median |
| source-document | 2268.507750 | 2956.041833 | 1.303078 | 42.039720 | 32.261868 | Tree observed median |
| climbing-union | 2070.556917 | 2811.288125 | 1.357745 | 46.058831 | 33.923037 | Tree observed median |
| climbing-intersection | 1809.021667 | 2660.567500 | 1.470722 | 52.717683 | 35.844771 | Tree observed median |
| type-assertion | 1309.877959 | 932.731084 | 0.712075 | 72.806349 | 102.245367 | Stream observed median |
| type-inspection | 1112.506625 | 733.622958 | 0.659432 | 85.723024 | 129.995157 | Stream observed median |
| copy-select | 2243.039000 | 2952.716875 | 1.316391 | 42.517064 | 32.298197 | Tree observed median |
| conditional-content | 1927.522416 | 2052.682791 | 1.064933 | 49.476691 | 46.459897 | Tree observed median |
| template-parameter | 1646.284875 | 1779.843750 | 1.081127 | 57.928876 | 53.581912 | Tree observed median |
| function-parameter | 1582.057500 | 2650.770666 | 1.675521 | 60.280636 | 35.977247 | Tree observed median |
| overlap-values | 1801.202583 | 8607.991000 | 4.779024 | 52.946533 | 11.078942 | Tree observed median |

Winner labels use the controller's descriptive 5% band, not a significance test.

| Workload | Tree first output ms | Stream first output ms | Stream input bytes at first output | Tree full-lifetime median RSS bytes | Stream full-lifetime median RSS bytes |
|---|---:|---:|---:|---:|---:|
| identity | 1042.160750 | 0.211208 | 8192 | 450183168 | 92700672 |
| adjacent | 1461.479875 | 3.984875 | 356352 | 704266240 | 95617024 |
| accumulator | 6188.264583 | 4410.873125 | 100000000 | 1306263552 | 94650368 |
| iterate | 1628.766208 | 1403.802500 | 100000000 | 505692160 | 95600640 |
| merge | — | — | — | — | — |
| fork | 1234.287708 | 2.335292 | 36864 | 523042816 | 96567296 |
| source-document | 1036.795584 | 0.251375 | 8192 | 491962368 | 92946432 |
| climbing-union | 1916.876084 | 1.641250 | 53248 | 679903232 | 95682560 |
| climbing-intersection | 1754.195375 | 7.416916 | 204800 | 679952384 | 95666176 |
| type-assertion | 1105.959875 | 0.348334 | 36864 | 551075840 | 92405760 |
| type-inspection | 1112.496292 | 733.607500 | 100000000 | 534118400 | 92389376 |
| copy-select | 1018.099875 | 0.203708 | 8192 | 432930816 | 92749824 |
| conditional-content | 1281.158791 | 0.167958 | 8192 | 610205696 | 92782592 |
| template-parameter | 1097.060333 | 2.255583 | 122880 | 552927232 | 95420416 |
| function-parameter | 1025.816916 | 3.535458 | 122880 | 489979904 | 95862784 |
| overlap-values | 1030.047916 | 2.781542 | 24576 | 449953792 | 95567872 |

Accumulator, iterate and type-inspection produce final results at end-of-input. Their latency is not
an early-emission claim. This is a 100 MB follow-up; the preceding full three-size matrix belongs to
its earlier source. A full current-source size/latency sweep and the throughput exit remain open.

Evidence is `/tmp/xsl3-p7-set-merge-mode-process-data/`: complete `report.json`, all child
stdout/stderr, controller summary and this source's hashes. Engine SHA-256 is `38a24f08d84113da73782a2c64b94416b026ff5989754ae90e8c7934502bcf2d`;
host is `815dc1f7aa517246086773ce72770ef28d762a90f921c0c674d0d65737f8a7f6`; controller is `104d46f91bf083e8dca587cc3dbd56d46534b194450eb47e3d0ea67383d8e5ac`. The controller's terminal
exit is 1 for the retained tree failures. No follow-up run was chosen to replace these results.


The 100 MB tree intersection completes all three rounds at median **1,809.021667 ms**. The original
profiled child was still scanning after nine minutes when intentionally stopped; that incomplete,
briefly sampled trial is not used to calculate a before/after timing ratio. The deterministic comparison
bound independently verifies removal of the quadratic scan.

The follow-up host's **83 artifact hashes** remain unchanged, all **400 current/frozen core hashes**
match, and every valid process trial reports exactly **4,294,967,296 available managed bytes**. No
process trial times out. Canonical identity, fork and source-document streaming are slower than their
tree medians; adjacent, accumulator and iterate are faster. Merge has no valid tree comparison.
The canonical throughput requirement remains incomplete and has observed regressions. In particular,
`overlap-values` has stream/tree ratio **4.779024348090** and needs performance work.

## Remaining scope

The full Phase 7 goal remains active. The exact 20 MiB absolute RSS push requirement still fails at
the preceding value-replay checkpoint. Full throughput and first-output requirements, deferred value
consumers, the raw posture/sweep acceptance gate, remaining strict conformance failures, schema-aware saved assemblies,
and remote Linux x64 evidence remain open. `supports-streaming` stays `no`.
