# Forward node predicates and prepared pull calls, 2026-10-24

A node-valued predicate such as `//row[@keep]` was lowered through the general numeric-predicate
helper. That helper requested a context position even though the predicate's truth depends only on
node existence. The synthetic position demand prevented the single descendant scan from being
retained. Its replacement traversal consumed the document before the suspended descendant cursor
could resume. Conditional temporary document construction exposed the resulting expired nodes.

The forward XPath compiler now uses its existing effective-boolean-value operation when the predicate
has a statically proven node-sequence type. Numeric and uncertain predicates retain the general helper.
An explicit `position()` inside a node-valued expression still requests position while its body is
compiled. Ordinary compilation keeps its existing path. No tree grounding, replay, public helper,
saved-data format change, runtime fixture source or fallback is introduced.

Eight temporary-document cases failed before the fix on each framework. They now pass for matching
and empty selections, `xsl:on-empty` and `xsl:where-populated`, borrowed principal readers and owned
`xsl:source-document` readers. The constructed `document-node()?` is reused for both existence and
child counting. Ownership checks verify that borrowed readers remain open and resolver streams close.
Ten predicate cases compare ordinary and forward execution, including empty-string attributes,
`'0'`, absent attributes, nested predicates, numeric positions, mixed node/numeric values and explicit
positions inside node-valued conditions. Three additional .NET 10 cases exercise saved assemblies.
A 10,000-row `xsl:for-each` probe checks the complete independently generated output and verifies
emission before half the input is read. A single `xsl:value-of` over all selected IDs constructs one
combined string; its initial full-input observation is not evidence of an incremental row population.

Main strict conformance improves from **2,668 passed / 43 failed / 1 not run** to
**2,670 / 41 / 1**, with **seven unmatched expected codes** and **no new failure IDs**. The repaired
suite cases are `si-on-empty/si-on-empty-012` and `si-where-populated/si-coco-012`. Temporary cursor tracing
was removed before verification. The diagnostic confirmed the premature traversal; it is not part of
the implementation.

## Prepared compiled calls

Each compiled pull resumption previously created a pending call, argument list and copied converted
state argument. A pull cursor now prepares that call once, using the same argument conversion and
compiled dispatcher selection as ordinary calls. The opaque cursor argument is unchanged between
resumptions. Runtime selection still happens on every invocation, including fork-context dispatch.
Native producers retain fresh mutable argument containers. Completion and failure clear the cached
call together with the producer and owner; sticky faults and annotation-context restoration remain.

The original identity-resumption probe allocated **2,880,000 bytes for 10,000 resumptions** on both
frameworks. After the change the same loop allocated **880,000 bytes**. Those figures include creation
of the singleton argument wrapper in the original path. A controlled regression compares the same
compiled identity body with a precreated singleton argument: **2,560,000 bytes for ordinary calls**,
**880,000 bytes for resumptions**, a **65.625% reduction**, on both frameworks. The generated body's
result-sequence containers still allocate. An initial zero-allocation assertion was therefore replaced
with a comparison of the two actual call paths, without changing production code to conceal that cost.
The regression requires resumptions to allocate less than half as much as ordinary calls.

Five runtime cases cover that allocation reduction, compiled producer errors, canonical typed-argument
conversion failures, producer release after completion and fresh mutable native arguments. The initial
native fixture attempted to resize a fixed-size argument array; it was corrected to mutate an element,
which is the supported operation under test. Its initial failure is not attributed to production code.

## Verification and evidence

This increment adds **27 .NET 10 / 24 .NET 8 cases**. Full Release and Debug unit suites pass
**2,913 / 2,832**. The focused conditional, grouping, global-binding, predicate, temporary-document and
pull-call checks also pass. The Release solution build succeeds with no new warnings. The .NET 10 W3C
harness passes 46 facts, upstream passes 1,654 cases per framework, differential passes 394 per framework
and all thirteen Python process-controller tests pass.

| Full audit | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| Ordinary XSLT 3.0 | 8,847 | 21 | 41 | 34 |
| Streaming development, ordinary execution | 2,691 | 20 | 1 | 5 |
| Streaming development, strict forward input | 2,670 | 41 | 1 | 7 |
| Ordinary schema-aware | 843 | 1 | 2 | 6 |
| Strict schema-aware | 842 | 2 | 2 | 6 |
| QT3 | 21,884 | 0 | 99 | 67 |

The comparison with the preceding full audit finds only the two repaired strict cases and no new
failure IDs in any profile. Strict main still returns a failing audit status. No failure baseline is
relaxed. `docs/conformance-sets.md` is regenerated from these completed reports. The completed process-gate results
are recorded below; older performance artifacts remain evidence for their own frozen source.

A fresh macOS arm64 NativeAOT consumer passes five new predicate/document stylesheets and every
retained correctness/compatibility probe, including the immutable version-2 fork assembly. Its publish
has no trim/AOT warnings. Normal globalization is enabled. The native core snapshot contains 395
source/configuration files. The five newly generated stylesheets use the current compiler; retained
saved assemblies additionally check compatibility with earlier code generation. The first generator
attempt waited before compilation; its restored inputs were reused by an explicit `--no-restore` run.

Evidence paths:

- `/tmp/xsl3-p7-temporary-doc-before.log` and `/tmp/xsl3-p7-temporary-doc-cursor-diagnostic.log`:
  failing reproductions and temporary traversal tracing.
- `/tmp/xsl3-p7-predicate-pull-conformance-delta.json`: all profile counts and exact fixed/new failure IDs.
- `/tmp/xsl3-p7-predicate-pull-reports/`: frozen completed W3C reports.
- `/tmp/xsl3-p7-predicate-pull-allocation-final.log`: successful allocation and early-output probes.
- `/tmp/xsl3-p7-predicate-pull-units-final-{release,debug}.log`: complete unit suites.
- `/tmp/xsl3-p7-predicate-pull-verified-*`: solution, W3C, upstream, differential and controller checks.
- `/tmp/xsl3-p7-predicate-pull-native/`: current frozen core, generator/publish logs and native results.
- `/tmp/xsl3-p7-predicate-pull-linux/`: current frozen source and both exact native memory matrices.
- `/tmp/xsl3-p7-predicate-pull-process-benchmark/`: isolated 100 MB matrix of nine workloads,
  three fresh alternating stream/tree pairs, with equal 4 GiB managed limits and tiering disabled.
- `/tmp/xsl3-p7-predicate-pull-final-perf*`: ordinary three-pair gate against `0f2ddcb`.


## Corrected-source exact native memory matrices

The fresh local Linux arm64 NativeAOT nightly matrix is complete: **39 children, 0 failures** at exact 2 GiB input / 200 MiB absolute RSS. All thirteen measured streams consume exactly 2,147,483,648 bytes and validate their independent output fingerprints. Effective managed allowance is 209,715,200 bytes. Both native-evidence flags are false; normal ICU is enabled. RSS is the greater child/outer wait4 full-lifetime counter, including startup and warmup. Tree baselines are separate and uncapped.

| Workload | Full-lifetime peak RSS bytes | Live managed bytes | Diagnostic elapsed ms | First output ms | Input at first output |
|---|---:|---:|---:|---:|---:|
| identity | 29376512 | 381472 | 54244.140525 | 0.439292 | 8192 |
| adjacent | 27463680 | 361872 | 13706.601090 | 6.733708 | 356352 |
| accumulator | 30535680 | 361704 | 53148.552357 | 53148.528149 | 2147483648 |
| iterate | 28577792 | 417752 | 13778.578631 | 13778.555923 | 2147483648 |
| merge | 30912512 | 405736 | 53912.324859 | 9.164709 | 122880 |
| fork | 30490624 | 366968 | 240933.958067 | 5.552250 | 36864 |
| source-document | 29548544 | 384208 | 51261.266774 | 0.297833 | 8192 |
| climbing-union | 30494720 | 361248 | 44863.537521 | 2.025250 | 53248 |
| climbing-intersection | 30588928 | 361256 | 41914.205478 | 8.942875 | 204800 |
| type-assertion | 26251264 | 359000 | 10340.916755 | 0.208208 | 36864 |
| type-inspection | 26275840 | 359064 | 7308.439461 | 7308.436086 | 2147483648 |
| copy-select | 29388800 | 381512 | 50912.009065 | 0.610208 | 8192 |
| conditional-content | 29806592 | 377968 | 33111.604766 | 0.155000 | 8192 |

These native timings are diagnostic: functional checks overlapped part of the matrix. They are not paired throughput comparisons. Accumulator/iterate/type-inspection results require end-of-input.

The exact 200 MiB-input / 20 MiB RSS push matrix completes all **39 children**, retaining **13 failures**. No startup/warmup or baseline is subtracted.

| Workload | Status | Measured input bytes | Full-lifetime peak RSS bytes |
|---|---|---:|---:|
| identity | budget-exceeded | 209715200 | 25554944 |
| adjacent | budget-exceeded-before-measurement | 0 | 22331392 |
| accumulator | budget-exceeded-before-measurement | 0 | 21315584 |
| iterate | budget-exceeded-before-measurement | 0 | 22495232 |
| merge | budget-exceeded-before-measurement | 0 | 21544960 |
| fork | budget-exceeded-before-measurement | 0 | 21610496 |
| source-document | budget-exceeded | 209715200 | 25751552 |
| climbing-union | budget-exceeded-before-measurement | 0 | 21381120 |
| climbing-intersection | budget-exceeded-before-measurement | 0 | 21446656 |
| type-assertion | budget-exceeded | 209715200 | 25456640 |
| type-inspection | budget-exceeded | 209715200 | 25522176 |
| copy-select | budget-exceeded | 209715200 | 25464832 |
| conditional-content | budget-exceeded | 209715200 | 25882624 |

Evidence is `/tmp/xsl3-p7-predicate-pull-linux/`: both complete reports, frozen source/SHA-256 snapshot, `source-check.json`, `run.sh`, publish/run logs, native-intermediate rejection and `measured-summary.json`. The cached official SDK image is `70b5de4a1846`, .NET SDK 10.0.400 / runtime 10.0.11. This is local arm64 evidence; remote linux-x64 CI remains unverified. The nightly pass does not close the exact push budget or the full Phase 7 goal.


## Isolated 100 MB process comparison

The controller completes **54 trials**: **27 valid streams**, **24 valid tree trials**, **3 failed trials**. Controller exit is **1**; the canonical throughput target reports **incomplete coverage**. Each side uses the same current source and corpus, equal 4 GiB managed limits, tiering disabled and three fresh alternating pairs. This matrix measures decimal 100 MB only. It does not replace the earlier three-size matrix or establish a full throughput/latency exit.

| Workload | Tree, current snapshot ms | Stream, current snapshot ms | Stream/tree | Winner |
|---|---:|---:|---:|---|
| identity | 2262.672333 | 2943.383125 | 1.300844 | Tree observed median |
| adjacent | 1732.031125 | 1115.392541 | 0.643980 | Stream observed median |
| accumulator | 6117.250958 | 4136.834167 | 0.676257 | Stream observed median |
| iterate | 1593.180667 | 1282.761875 | 0.805158 | Stream observed median |
| merge | No valid run | 3797.098791 | Not comparable | No valid comparison |
| fork | 1742.826333 | 5918.863792 | 3.396129 | Tree observed median |
| source-document | 2251.802958 | 2918.314958 | 1.295990 | Tree observed median |
| copy-select | 2555.771000 | 2945.399292 | 1.152450 | Tree observed median |
| conditional-content | 1935.161042 | 2027.957667 | 1.047953 | No meaningful winner within 5% band |

A failed or incomplete comparison has no ratio or winner. Merge stream diagnostics are medians of its three valid raw stream trials; they do not establish a comparison. Observed median winners do not quantify uncertainty or demonstrate a causal improvement against the preceding snapshot. All trial output hashes, raw timings, failures, first-output measurements and runtime/artifact hashes are retained in the report.

| Workload | Stream first-output median ms | Input bytes at first output |
|---|---:|---:|
| identity | 0.231375 | 8192 |
| adjacent | 4.208166 | 356352 |
| accumulator | 4136.829042 | 100000000 |
| iterate | 1282.749709 | 100000000 |
| merge | 5.213708 | 122880 |
| fork | 2.246875 | 36864 |
| source-document | 0.245500 | 8192 |
| copy-select | 0.204125 | 8192 |
| conditional-content | 0.183958 | 8192 |

All 425 source/fixture/controller hashes and the assembly, engine and controller hashes match. Runtime: .NET 10.0.11, macOS 27.0.1, Arm64. This is a managed process comparison; native absolute RSS is assessed separately.


## Final ordinary three-pair gate

The gate exits **0** against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` over exactly three alternating base/head pairs: timing geometric mean **0.982051247778**, maximum allocation ratio **1.044672059302** (grouping, cold; **1,597,240/1,668,592 bytes**). Cold time is excluded from the 39-row timed mean; cold allocation is included. The same ordinary harness/corpus runs on both versions with the separate process host excluded equally.

The gate ran once after the complete 54-trial matrix and all task-owned functional/native CPU jobs finished. Its temporary base worktree is removed. An aggregate pass does not establish fixes for preceding individual outliers.

| Workload | Size | Base 0f2ddcb ms | Current branch ms | Head/base | Base allocated bytes | Current allocated bytes | Allocation ratio | Winner |
|---|---|---:|---:|---:|---:|---:|---:|---|
| grouping | cold | 142.768400 | 140.143800 | 0.981616 | 1597240 | 1668592 | 1.044672 | No meaningful winner (5% band) |
| grouping | compile | 0.643900 | 0.626300 | 0.972667 | 186814 | 189336 | 1.013500 | No meaningful winner (5% band) |
| sorting | compile | 0.604200 | 0.598550 | 0.990649 | 182433 | 184394 | 1.010749 | No meaningful winner (5% band) |
| keys | compile | 0.552950 | 0.300100 | 0.542725 | 194229 | 198412 | 1.021536 | Current observed median |
| join | compile | 0.092000 | 0.095900 | 1.042391 | 150626 | 152670 | 1.013570 | No meaningful winner (5% band) |
| regex | compile | 0.081800 | 0.084000 | 1.026895 | 165111 | 166709 | 1.009678 | No meaningful winner (5% band) |
| strings | compile | 0.083500 | 0.085400 | 1.022754 | 177988 | 179793 | 1.010141 | No meaningful winner (5% band) |
| maps | compile | 0.162700 | 0.163100 | 1.002459 | 285536 | 289419 | 1.013599 | No meaningful winner (5% band) |
| fold-left | compile | 0.180500 | 0.184100 | 1.019945 | 305117 | 307054 | 1.006348 | No meaningful winner (5% band) |
| iterate | compile | 0.148300 | 0.152700 | 1.029670 | 238285 | 240046 | 1.007390 | No meaningful winner (5% band) |
| identity | compile | 0.044400 | 0.045300 | 1.020270 | 97900 | 98683 | 1.007998 | No meaningful winner (5% band) |
| dispatch | compile | 1.474200 | 1.619700 | 1.098698 | 4004366 | 4068337 | 1.015975 | Base observed median |
| recursion | compile | 0.119100 | 0.121900 | 1.023510 | 237816 | 241745 | 1.016521 | No meaningful winner (5% band) |
| json | compile | 0.067100 | 0.067500 | 1.005961 | 156509 | 157357 | 1.005418 | No meaningful winner (5% band) |
| grouping | 1kb | 0.002500 | 0.002500 | 1.000000 | 13113 | 13184 | 1.005414 | No meaningful winner (5% band) |
| sorting | 1kb | 0.002300 | 0.002200 | 0.956522 | 12205 | 12293 | 1.007210 | No meaningful winner (5% band) |
| keys | 1kb | 0.004500 | 0.004600 | 1.022222 | 17468 | 17556 | 1.005038 | No meaningful winner (5% band) |
| join | 1kb | 0.003600 | 0.003700 | 1.027778 | 14120 | 14192 | 1.005099 | No meaningful winner (5% band) |
| regex | 1kb | 0.004700 | 0.004600 | 0.978723 | 15613 | 15685 | 1.004612 | No meaningful winner (5% band) |
| strings | 1kb | 0.003400 | 0.003400 | 1.000000 | 12831 | 12901 | 1.005456 | No meaningful winner (5% band) |
| maps | 1kb | 0.006500 | 0.006500 | 1.000000 | 27221 | 27364 | 1.005253 | No meaningful winner (5% band) |
| fold-left | 1kb | 0.004300 | 0.004200 | 0.976744 | 18803 | 18963 | 1.008509 | No meaningful winner (5% band) |
| iterate | 1kb | 0.004200 | 0.004200 | 1.000000 | 23088 | 23200 | 1.004851 | No meaningful winner (5% band) |
| identity | 1kb | 0.002800 | 0.002900 | 1.035714 | 10809 | 10882 | 1.006754 | No meaningful winner (5% band) |
| dispatch | 1kb | 0.007300 | 0.007400 | 1.013699 | 23592 | 23664 | 1.003052 | No meaningful winner (5% band) |
| recursion | 1kb | 0.003700 | 0.003500 | 0.945946 | 17570 | 17649 | 1.004496 | Current observed median |
| json | 1kb | 0.005900 | 0.006000 | 1.016949 | 35038 | 35172 | 1.003824 | No meaningful winner (5% band) |
| grouping | 1mb | 0.847100 | 0.836900 | 0.987959 | 1985615 | 1985686 | 1.000036 | No meaningful winner (5% band) |
| sorting | 1mb | 2.175300 | 2.092500 | 0.961936 | 3410836 | 3428508 | 1.005181 | No meaningful winner (5% band) |
| keys | 1mb | 3.768150 | 3.629300 | 0.963152 | 6152455 | 6152542 | 1.000014 | No meaningful winner (5% band) |
| join | 1mb | 2.604800 | 2.419400 | 0.928824 | 4525269 | 4525340 | 1.000016 | Current observed median |
| regex | 1mb | 5.913900 | 5.912800 | 0.999814 | 13071945 | 13072017 | 1.000006 | No meaningful winner (5% band) |
| strings | 1mb | 2.955000 | 2.894600 | 0.979560 | 5462929 | 5463000 | 1.000013 | No meaningful winner (5% band) |
| maps | 1mb | 4.131750 | 4.170400 | 1.009354 | 10150682 | 10162530 | 1.001167 | No meaningful winner (5% band) |
| fold-left | 1mb | 2.868100 | 2.737600 | 0.954499 | 7319896 | 7443143 | 1.016837 | No meaningful winner (5% band) |
| iterate | 1mb | 1.209400 | 1.179000 | 0.974864 | 3114677 | 3132497 | 1.005721 | No meaningful winner (5% band) |
| identity | 1mb | 1.914350 | 1.913700 | 0.999660 | 3154312 | 3154385 | 1.000023 | No meaningful winner (5% band) |
| dispatch | 1mb | 1.362100 | 1.352900 | 0.993246 | 2650127 | 2650199 | 1.000027 | No meaningful winner (5% band) |
| recursion | 1mb | 1.263350 | 1.180500 | 0.934420 | 2781909 | 2781927 | 1.000006 | Current observed median |
| json | 1mb | 9.211700 | 9.281100 | 1.007534 | 18294346 | 18347001 | 1.002878 | No meaningful winner (5% band) |

Winner labels use a 5% diagnostic band and do not replace an uncertainty test. All exact base/head round files, per-row medians, allocations and round timings are retained in `/tmp/xsl3-p7-predicate-pull-final-perf-data/`. Only rounds 1–3 are used; stale repository artifact rounds are excluded.

Individual timed rows above 1.05 remain observed regressions requiring attribution:

- **dispatch compile**: ratio **1.098697598698**, base/current medians **1.474200/1.619700 ms**. Base rounds `[1.4712, 1.5893, 1.4742]`; current rounds `[2.7344, 1.6036, 1.6197]`. Allocation ratio **1.015975312946**.

The preceding regex 1 KiB and other historical outliers are not declared fixed by their absence from this run. No extra performance run was selected to replace these results.


The full Phase 7 goal remains active. Main strict and schema-aware failures, S1 and error-code gaps,
exact push RSS, complete throughput/latency evidence, remote Linux x64 CI and the feature claim remain
required. Early output and reduced allocation do not establish those exits. The existing full
1/100/1,000 MB matrix belongs to the preceding conditional-content snapshot; this increment's process
matrix is explicitly limited to 100 MB. No commit or push is performed.
