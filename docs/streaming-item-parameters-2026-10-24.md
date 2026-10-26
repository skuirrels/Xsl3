# Generic absorbing-function parameters and debug output workers, 2026-10-24 UTC

This is the item-parameter checkpoint. The later [overlapping-value increment](streaming-value-replay-2026-10-25.md) has its own source snapshots and validation. Measurements below remain tied to this checkpoint.

Absorbing stylesheet functions can now accept an `item()*` first parameter without collecting live
source nodes into an argument list. The same continuation contract serves omitted parameter types and
broad node types. This repairs W3C `su-absorbing-301`: adjacent groups can pass through an untyped outer
function and a typed inner function while each group remains usable.

## Argument ownership and result semantics

The runtime parameter frame handles atomic values, maps, arrays, function items and nodes. Grounded
argument lists and navigator positions are copied into the invocation. A live source retains its first
header and the existing forward-position checks; grounded items retain independent cursor access.
Type/cardinality checks and completion remain outside the function body's catch boundary.

Argument preparation uses the existing analysis result to recognize grounded function results. It
passes already grounded arguments directly, preserves forwarded grounded values and eligible fork
origins, and retains the child-order proof for materialized singletons. Without that singleton proof,
recursive child paths attempted to normalize live nodes before consuming their descendants. The full
Release run exposed that regression, and the targeted repair was verified before final-source checks.

Inlining preserves the `PullSequence` protocol annotation while clearing construction analysis that
belongs to the original graph. Losing that annotation read only the first batch of a grounded
population. Generic item-to-node navigation validates one yielded item at a time through the existing
node checker; it does not cache a live population before validating its items.

Two additive runtime helpers, `X31FunctionParameterItemNext` and `X31FunctionArgumentForward`, are listed
in `PublicAPI.Unshipped.txt`. The existing navigator-returning `X31FunctionParameterNext` signature and
saved-data version remain unchanged.

## Debug fork and secondary-output repair

A nonempty debug `xsl:fork` acquired an end-tag `Nop` from the loader. Validating it as a prong raised
XTSE0010. The end-tag instruction now follows the completed fork in an enclosing sequence, preserving
its debug location and keeping it out of prong validation, group analysis and worker creation.

That repair exposed a second fault: debug construction analysis converted output-worker closures to
writer-returning functions. The dispatcher captured their writes into a returned sequence, which the
fork runtime discarded. Generated fork and secondary-output workers now retain their explicit
iterator-return ABI through an internal QIL flag. Their bodies still write directly to the current
output; normal debug locations remain. Early-output tests cover direct and forked secondary results,
so collecting a complete secondary result before writing is not an accepted workaround.

Generated closures also start at a hidden entry point before body sequence points. This avoids the
Debug sequence-point assertion while retaining source locations in the closure body.

## Shared probe workload

The shared probe adds `function-parameter`, bringing it to fifteen workloads and thirty statically
referenced saved entry points. Each generated row's `value` goes through an `item()*` relay into a
function requiring `element(value)*`, then emits the actual caller position and converted integer.
The expected output derives independently from the generated input record ID and value. Production
execution uses the ordinary public APIs and reads the XML value. All fourteen preceding stylesheet
corpus files are byte-identical to the prior checkpoint. The new corpus fixture matches its shared
probe definition; tree parity and first-output checks pass.

## Functional and compatibility validation

Twenty-nine .NET 10 / twenty-seven .NET 8 regression cases cover mixed XDM values, forwarded and
singleton fork bindings, recursive navigation, independent cursor ownership, adjacent groups, typed
errors, saved/debug execution and early secondary output. Complete Debug and Release unit suites pass
**3,040 on .NET 10 / 2,948 on .NET 8**.

The fresh macOS arm64 NativeAOT consumer passes ten new normal/debug item-parameter styles and all
retained probes. These include the earlier focus/template fixtures and immutable version-2 fork DLL.
The new styles cover grouping, mixed items, forwarded forks, canonical grounded paths and recursive
child navigation. Publish has no trim/AOT warnings. All **396 current/frozen core/configuration hashes**
match. The version-2 DLL remains SHA-256
`89ba201ad9e21c7a3a4add5231c114f5aee9a2f9eef4bdd9472a41e680b5caee`.

Evidence: `/tmp/xsl3-p7-item-parameter-worker-native/`, its separate generator, `source-snapshot.json`,
`source-check.json`, `artifact-hashes.json`, generation/publish/run logs and terminal driver exit 0.
Older retained saved assemblies exercise compatibility; they were not represented as freshly generated
fixtures. The earlier `/tmp/xsl3-p7-item-parameter-native/` checkpoint failed while generating the debug
fork and remains preserved separately.

## Conformance comparison

The solution build, 46 W3C harness facts, upstream 1,654 per framework, differential 394 per framework,
and thirteen controller tests pass. The full strict audit retains its failing status. Eight full
profile reports have **no new failure IDs** relative to the arithmetic/focus checkpoint. Main strict
improves **2,671/40/1 to 2,672/39/1**, fixing only `su-absorbing/su-absorbing-301`; its seven unmatched
expected codes remain. No conformance baseline was changed for this increment.

| Profile | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| XSLT 3.0 | 8847 | 21 | 41 | 34 |
| XSLT 2.0 | 4588 | 9 | 11 | 7 |
| Streaming, unstreamed execution | 2691 | 20 | 1 | 5 |
| Streaming, strict inputs | 2672 | 39 | 1 | 7 |
| Posture/sweep | 790 | 129 | 0 | 0 |
| Schema aware | 843 | 1 | 2 | 6 |
| Schema aware, strict inputs | 842 | 2 | 2 | 6 |
| QT3 | 21884 | 0 | 99 | 67 |

Evidence: `/tmp/xsl3-p7-item-parameter-worker-verified-regressions.log`, its individual logs, terminal
exit file `0`, and frozen `/tmp/xsl3-p7-item-parameter-worker-reports/`, including
`profile-comparison.json`. The generated conformance tables use those completed reports. Earlier
failed checkpoints remain separate: the initial new cases exposed live-node collection, debug
sequence-point generation and lost pull semantics; the first full Release gate exposed singleton
child ordering. The debug-fork generation checkpoint then exposed prong validation and output capture.


## Final-source exact native memory matrices

The fresh local Linux arm64 NativeAOT nightly matrix is complete: **45 children, 0 failures** at exact 2 GiB input / 200 MiB absolute RSS. All fifteen measured streams consume exactly 2,147,483,648 bytes and validate their independent output fingerprints. Effective managed allowance is 209,715,200 bytes. Both native-evidence flags are false; normal ICU is enabled. RSS is the greater child/outer wait4 full-lifetime counter, including startup and warmup. Tree baselines are separate and uncapped.

| Workload | Full-lifetime peak RSS bytes | Live managed bytes | Diagnostic elapsed ms | First output ms | Input at first output |
|---|---:|---:|---:|---:|---:|
| identity | 29437952 | 382432 | 54048.235817 | 0.380666 | 8192 |
| adjacent | 27410432 | 362832 | 13825.245132 | 4.086334 | 356352 |
| accumulator | 30400512 | 362664 | 54684.644775 | 54684.612566 | 2147483648 |
| iterate | 28741632 | 418176 | 15117.250506 | 15117.186840 | 2147483648 |
| merge | 30941184 | 406696 | 56852.627109 | 4.815833 | 122880 |
| fork | 30912512 | 367928 | 243320.828945 | 5.629916 | 36864 |
| source-document | 29392896 | 385168 | 51809.911857 | 0.423667 | 8192 |
| climbing-union | 30388224 | 362208 | 45297.423688 | 1.875250 | 53248 |
| climbing-intersection | 30388224 | 362216 | 43218.320604 | 6.514542 | 204800 |
| type-assertion | 26226688 | 359960 | 10570.600880 | 0.185209 | 36864 |
| type-inspection | 26202112 | 360024 | 7565.925337 | 7565.905670 | 2147483648 |
| copy-select | 29458432 | 382472 | 51376.747981 | 0.531292 | 8192 |
| conditional-content | 29634560 | 378928 | 33525.700224 | 0.272458 | 8192 |
| template-parameter | 30576640 | 372280 | 20395.579009 | 1.847084 | 122880 |
| function-parameter | 30539776 | 372152 | 32216.992598 | 3.470916 | 122880 |

These native timings are diagnostic: functional checks overlapped part of the matrix. They are not paired throughput comparisons. Accumulator/iterate/type-inspection results require end-of-input.

The exact 200 MiB-input / 20 MiB RSS push matrix completes all **45 children**, retaining **15 failures**. No startup/warmup or baseline is subtracted.

| Workload | Status | Measured input bytes | Full-lifetime peak RSS bytes |
|---|---|---:|---:|
| identity | budget-exceeded | 209715200 | 25550848 |
| adjacent | budget-exceeded | 209715200 | 26562560 |
| accumulator | budget-exceeded | 209715200 | 26456064 |
| iterate | budget-exceeded-before-measurement | 0 | 22441984 |
| merge | budget-exceeded-before-measurement | 0 | 21381120 |
| fork | budget-exceeded-before-measurement | 0 | 21360640 |
| source-document | budget-exceeded | 209715200 | 25575424 |
| climbing-union | budget-exceeded-before-measurement | 0 | 21233664 |
| climbing-intersection | budget-exceeded-before-measurement | 0 | 21225472 |
| type-assertion | budget-exceeded | 209715200 | 25526272 |
| type-inspection | budget-exceeded | 209715200 | 25407488 |
| copy-select | budget-exceeded | 209715200 | 25505792 |
| conditional-content | budget-exceeded | 209715200 | 25784320 |
| template-parameter | budget-exceeded | 209715200 | 26779648 |
| function-parameter | budget-exceeded-before-measurement | 0 | 21258240 |

Evidence is `/tmp/xsl3-p7-item-parameter-worker-linux/`: both complete reports, frozen source/SHA-256 snapshot, `source-check.json`, `run.sh`, publish/run logs, native-intermediate rejection and `measured-summary.json`. The cached official SDK image is `70b5de4a1846`, .NET SDK 10.0.400 / runtime 10.0.11. This is local arm64 evidence; remote linux-x64 CI remains unverified. The nightly pass does not close the exact push budget or the full Phase 7 goal.


The earlier `/tmp/xsl3-p7-item-parameter-linux/` matrix is retained as an intermediate
checkpoint from before the debug-worker repair. The tables above use the fresh worker snapshot, not
that earlier source. All **548 current/frozen source/controller/fixture hashes** match.

## Ordinary performance gate

One gate run exits **0** against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` over exactly three alternating pairs. Timing geometric mean is **0.994644134262**; maximum allocation ratio is **1.046490195587** (grouping cold, 1,597,240/1,671,496 bytes). Cold time is excluded from the 39-row timed mean; cold allocation is included. Cold time ratio is **1.052512405137** (137.040000/144.236300 ms).

The same ordinary harness and corpus runs on both versions with the separate process host excluded equally. All task-owned functional/native CPU jobs were terminal before this comparison. The temporary base worktree is removed. This compares the complete working tree with the base; it does not isolate the causal effect of this increment.

| Workload | Size | Base 0f2ddcb ms | Current branch ms | Head/base | Base allocated bytes | Current allocated bytes | Allocation ratio | Winner |
|---|---|---:|---:|---:|---:|---:|---:|---|
| grouping | cold | 137.040000 | 144.236300 | 1.052512 | 1597240 | 1671496 | 1.046490 | Base observed median |
| grouping | compile | 0.586600 | 0.643850 | 1.097596 | 186802 | 191475 | 1.025016 | Base observed median |
| sorting | compile | 0.577950 | 0.605400 | 1.047495 | 182432 | 186795 | 1.023916 | No meaningful winner (5% band) |
| keys | compile | 0.480950 | 0.321300 | 0.668053 | 194232 | 199589 | 1.027580 | Current observed median |
| join | compile | 0.094800 | 0.095200 | 1.004219 | 150677 | 154105 | 1.022751 | No meaningful winner (5% band) |
| regex | compile | 0.083900 | 0.084500 | 1.007151 | 165113 | 168231 | 1.018884 | No meaningful winner (5% band) |
| strings | compile | 0.085400 | 0.085600 | 1.002342 | 177989 | 180664 | 1.015029 | No meaningful winner (5% band) |
| maps | compile | 0.161800 | 0.164350 | 1.015760 | 285536 | 291882 | 1.022225 | No meaningful winner (5% band) |
| fold-left | compile | 0.185700 | 0.185400 | 0.998384 | 305117 | 308701 | 1.011746 | No meaningful winner (5% band) |
| iterate | compile | 0.152000 | 0.154000 | 1.013158 | 238285 | 242414 | 1.017328 | No meaningful winner (5% band) |
| identity | compile | 0.044500 | 0.045000 | 1.011236 | 97900 | 99371 | 1.015026 | No meaningful winner (5% band) |
| dispatch | compile | 1.580100 | 2.327900 | 1.473261 | 4004368 | 4117627 | 1.028284 | Base observed median |
| recursion | compile | 0.123100 | 0.121500 | 0.987002 | 237818 | 246673 | 1.037234 | No meaningful winner (5% band) |
| json | compile | 0.069400 | 0.069700 | 1.004323 | 156510 | 158369 | 1.011878 | No meaningful winner (5% band) |
| grouping | 1kb | 0.002600 | 0.002500 | 0.961538 | 13113 | 13184 | 1.005414 | No meaningful winner (5% band) |
| sorting | 1kb | 0.002300 | 0.002200 | 0.956522 | 12206 | 12293 | 1.007128 | No meaningful winner (5% band) |
| keys | 1kb | 0.004600 | 0.004500 | 0.978261 | 17469 | 17556 | 1.004980 | No meaningful winner (5% band) |
| join | 1kb | 0.003600 | 0.003700 | 1.027778 | 14120 | 14192 | 1.005099 | No meaningful winner (5% band) |
| regex | 1kb | 0.004700 | 0.005500 | 1.170213 | 15613 | 15688 | 1.004804 | Base observed median |
| strings | 1kb | 0.003300 | 0.003400 | 1.030303 | 12831 | 12903 | 1.005611 | No meaningful winner (5% band) |
| maps | 1kb | 0.006600 | 0.006500 | 0.984848 | 27221 | 27365 | 1.005290 | No meaningful winner (5% band) |
| fold-left | 1kb | 0.004400 | 0.004200 | 0.954545 | 18804 | 18963 | 1.008456 | No meaningful winner (5% band) |
| iterate | 1kb | 0.004200 | 0.004200 | 1.000000 | 23088 | 23200 | 1.004851 | No meaningful winner (5% band) |
| identity | 1kb | 0.002800 | 0.002900 | 1.035714 | 10811 | 10883 | 1.006660 | No meaningful winner (5% band) |
| dispatch | 1kb | 0.007200 | 0.007500 | 1.041667 | 23592 | 23664 | 1.003052 | No meaningful winner (5% band) |
| recursion | 1kb | 0.003600 | 0.003500 | 0.972222 | 17570 | 17650 | 1.004553 | No meaningful winner (5% band) |
| json | 1kb | 0.005900 | 0.006000 | 1.016949 | 35036 | 35160 | 1.003539 | No meaningful winner (5% band) |
| grouping | 1mb | 0.866600 | 0.849300 | 0.980037 | 1985600 | 1985687 | 1.000044 | No meaningful winner (5% band) |
| sorting | 1mb | 2.182100 | 2.164000 | 0.991705 | 3410836 | 3428508 | 1.005181 | No meaningful winner (5% band) |
| keys | 1mb | 3.939900 | 3.604000 | 0.914744 | 6152455 | 6152541 | 1.000014 | Current observed median |
| join | 1mb | 2.656500 | 2.438000 | 0.917749 | 4525270 | 4525340 | 1.000015 | Current observed median |
| regex | 1mb | 6.193900 | 5.961850 | 0.962536 | 13071946 | 13072017 | 1.000005 | No meaningful winner (5% band) |
| strings | 1mb | 3.071600 | 2.932000 | 0.954551 | 5462929 | 5463000 | 1.000013 | No meaningful winner (5% band) |
| maps | 1mb | 4.421900 | 4.272000 | 0.966101 | 10138203 | 10158113 | 1.001964 | No meaningful winner (5% band) |
| fold-left | 1mb | 2.860450 | 2.784800 | 0.973553 | 7319896 | 7443143 | 1.016837 | No meaningful winner (5% band) |
| iterate | 1mb | 1.234300 | 1.193700 | 0.967107 | 3114680 | 3132494 | 1.005719 | No meaningful winner (5% band) |
| identity | 1mb | 1.944350 | 1.937900 | 0.996683 | 3154312 | 3154384 | 1.000023 | No meaningful winner (5% band) |
| dispatch | 1mb | 1.407000 | 1.392700 | 0.989837 | 2650127 | 2650200 | 1.000028 | No meaningful winner (5% band) |
| recursion | 1mb | 1.229000 | 1.224300 | 0.996176 | 2781850 | 2781961 | 1.000040 | No meaningful winner (5% band) |
| json | 1mb | 10.257400 | 9.397700 | 0.916187 | 18294349 | 18346957 | 1.002876 | Current observed median |

Winner labels use a 5% diagnostic band; they do not replace an uncertainty test. Exact round files and per-row medians/allocations are retained in `/tmp/xsl3-p7-item-parameter-worker-perf-data/`. Only rounds 1–3 are used.

Timed rows above 1.05 remain observed regressions requiring attribution:

- **grouping compile**: ratio **1.097596317763**, base/current **0.586600/0.643850 ms**. Base rounds `[0.64035, 0.5756, 0.5866]`, current rounds `[0.6754, 0.64165, 0.64385]`. Allocation ratio **1.025015792122**.
- **dispatch compile**: ratio **1.473261186001**, base/current **1.580100/2.327900 ms**. Base rounds `[1.5801, 1.53935, 3.2104]`, current rounds `[1.70615, 2.6025, 2.3279]`. Allocation ratio **1.028283864020**.
- **regex 1kb**: ratio **1.170212765957**, base/current **0.004700/0.005500 ms**. Base rounds `[0.0047, 0.0047, 0.0047]`, current rounds `[0.0055, 0.0055, 0.0055]`. Allocation ratio **1.004803689233**.

No additional performance run was selected to replace these results. Earlier individual outliers are not declared fixed by their absence. No fresh paired streaming process matrix was run for this source: the preceding 60-trial 100 MB matrix and full three-size matrix remain evidence for their own earlier snapshots. Throughput/latency exits remain open.


## Remaining scope

The S1 audit also inspected **4,760 expressions across all 129 raw failing catalogue cases**. It
retains **2,723 subexpression mismatches**, all ending in attribute, namespace or statically empty
results; eight cases contain no expressions. The 2014 attribute/namespace expectations need an audit
against the final Recommendation's §19.8.8.9 axis table, which assigns striding posture to these axes.
No expected value, external fixture, baseline or pass count was changed. Complete diagnostics are in
`/tmp/xsl3-p7-item-parameter-s1-audit/all-mismatches.json`; this is an investigation, not a completed S1
acceptance result.

The full P7 goal remains active. Already inspected or stateful parameter continuations still require
fork projection. Explicitly grounded values and constructed outputs may grow; these changes do not
establish bounded memory for arbitrary results. Remaining strict failures, S1 posture/sweep and
error-code mismatches, exact push RSS, throughput/latency, schema-aware saved assemblies and remote
Linux x64 CI still need work. `supports-streaming` remains `no`.
