# Forward conditional and complex content, 2026-10-24

Conditional sequence construction previously materialized all ordinary items before choosing
`xsl:on-empty` or `xsl:on-non-empty` content. Forward source nodes expired before the caller copied
them. `xsl:where-populated` similarly filtered a completed list of expired nodes. Fourteen new
regressions reproduce **eleven failures and three passes per framework** before the fixes. The first
test attempt was blocked by the test runner's sandbox socket restriction and is not failure evidence;
the authorized rerun executed the regressions.

The streamed compiler now lifts the ordinary population and each deferred conditional body as a
resumable iterator. The conditional cursor transmits each non-vacuous source item before advancing
its producer, evaluates conditional bodies at their sequence positions, and retains only an undecided
vacuous prefix and its deferred instructions. The vacuity/population rules are shared with ordinary
execution. Ordinary execution also preserves vacuous items when no `xsl:on-empty` replaces them;
previously it incorrectly dropped these values even with only `xsl:on-non-empty` present.

The [W3C evaluation algorithm in section 8.4.4](https://www.w3.org/TR/xslt-30/)
permits buffering insignificant items until the conditional choice is known. This implementation
does not claim that an arbitrary vacuous prefix has constant size. Explicit grounded results and
array values may also grow; the measured non-vacuous-row workload does not prove every streaming exit.

The first repair exposed a second eager operation: complex-content construction passed the entire
conditional result to `X31ContentItems`. Its streamed replacement pulls one item at a time, preserves
atomic spacing across array members, flattens document children while each remains readable, and
uses a stack of array positions instead of copying flattened arrays. Function/map errors use the
same XTDE0450 factory as ordinary construction. The node-only fast path and ordinary conversion
remain in place. This is runtime/compiler logic; no fixture becomes a runtime source of values.

Root content construction can register another resumable function. Dispatcher finalization now follows
root compilation, so the dispatcher includes those functions. Existing selected-copy focus and try
rollback regressions exposed the incomplete dispatcher during development and pass after this correction.
Five generated-code runtime helpers are recorded in `PublicAPI.Unshipped.txt`; existing helpers and
saved-data formats are retained.

New coverage consists of **44 net10.0 / 40 net8.0 cases**: fourteen conditional cases, fifteen
complex-content cases, nine runtime lifecycle cases, four net10 saved-assembly cases, and two process
fixture cases. It covers conditional ordering/captured values/nesting, atomic item types and vacuous
values, early emission, arrays/document boundaries, function errors, late-error rollback, borrowed
reader ownership, sticky faults, cancellation and collection of transmitted source frames. Expanded
root-attribute fixtures were corrected to explicit `data()` atomization to satisfy the existing S1
grounded-result requirement; the rejected fixtures are not runtime-regression evidence.

The first conditional repair leaves seven failures; incremental complex-content conversion and complete
root dispatch repair those failures and the two preceding selected-copy regressions. The final focused
run passes **56 net10.0 / 52 net8.0**; the process/conditional selection passes **68 / 64**. After the
context corrections below, full Debug and Release unit suites pass **2,886 / 2,808**. The Release solution build has zero errors and 28 existing
warnings in unchanged upstream files. No warning originates in these finalized production changes or tests.

The initial macOS NativeAOT consumer passes six new saved stylesheets covering conditional rows, empty
selection, vacuous item types, nested arrays, rollback and root atomic content, plus every retained
selected-copy, copy-content, type-test, namespace, fork, API and compatibility probe. Its publish has no
trim/AOT warnings, normal globalization remains enabled, and explicit annotated stylesheet type roots
are retained. The source snapshot covers 395 files. The frozen version-2 fork fixture is not regenerated.

The shared probe adds `conditional-content`: a conditional header, selected populated rows and a
conditional footer. Its independent oracle uses the generated records without their source padding;
the existing twelve corpus files remain byte-for-byte unchanged. The default memory matrix now has
thirteen workloads and twenty-six saved entry points. The seven canonical throughput workloads are
unchanged. No accepted strict baseline or streaming-support claim is added.

Evidence: `/tmp/xsl3-p7-conditional-before-measured.log`, `-first-fix.log`, `-second-fix.log`,
`-third-fix.log`, `-focused-final.log`, `-probe-focused.log` and full regression logs under the same
prefix. Frozen before-fix inputs/reports are under `/tmp/xsl3-p7-conditional-before/`. The saved/native
consumer, six generated stylesheets, source snapshot and logs are `/tmp/xsl3-p7-conditional-native/`,
with its generator in `/tmp/xsl3-p7-conditional-generator/`.

## Continuation context and lazy global evaluation

The first full strict audit repairs four conditional cases but exposes four failures: three annotation
assertions and one fork/document-order assertion. A resumable content adapter had forced a global before
its source document opened, changing relative document creation order. Population adapters now retain
lazy references to globals; the existing `xsl:try` capture policy is preserved. The ordering reproduction
fails before this change and passes afterward.

Suspended annotation scopes must also remain private to their producer. The pull state now holds a copy
of its annotation stack and pending constructor type. Each resume installs that state and restores the
caller's state in `finally`, including after completion, cancellation or failure. Completed cursors release
the saved stack. The existing accumulator isolation and the pull mechanism share the stack-copy operation.
Two direct scope regressions fail before the fix; three public annotated-writer cases reproduce the W3C
type failures with schema-awareness enabled. The initial wrong-schema-mode test fixtures are retained in
diagnostic logs and are not attributed to production regressions. The corrected context-focused selection
passes **129 net10.0 / 123 net8.0**.

All first-pass reports are retained in `/tmp/xsl3-p7-conditional-first-reports/` and
`-initial-conformance-delta.json`. A fresh corrected regression driver uses the
`/tmp/xsl3-p7-conditional-verified-` prefix. Fresh corrected native source snapshots are
`/tmp/xsl3-p7-conditional-context-native/` (395 files) and `-context-linux/` (545 files).
The earlier Linux matrices complete 39 children each (nightly zero failures, push thirteen failures),
but predate these corrections and are intermediate evidence only. The corrected native consumer adds
a seventh saved stylesheet for lazy globals/fork ordering. Its first invocation omitted the caller's
document-function permission; that configuration failure is retained separately from the corrected run.

The corrected native consumer exits **0** with all seven new stylesheets and all retained probes passing.
`publish-final.log` has no trim/AOT warnings; `run-final.log` and `final-exit-code.txt` retain the result.
The missing-permission failure is in `initial-run.log`. Both corrected source audits have no differences.
Schema-aware saved assemblies remain explicitly unsupported, so schema-aware assertions above are live
managed checks rather than a saved-schema claim.

## Corrected conformance audit

Main strict improves **2,664/47/1 to 2,668/43/1**, repairing `si-on-empty-022/-024` and
`si-on-non-empty-022/-024`, with **no new failure IDs**. Seven expected-error-code mismatches remain.
The corrected report is `/tmp/xsl3-p7-conditional-verified-conformance-delta.json`. Normal XSLT stays
**8,847/21/41**, unstreamed development **2,691/20/1**, and QT3 **21,884/0/99**, with their original
failure IDs and mismatches retained. The .NET 10 W3C harness passes **46/46 facts**.

Conditional/document coverage is incomplete: `si-on-empty-012` and `si-where-populated/si-coco-012`
still fail with `stream-position` while constructing temporary documents from streamed selections.
They remain required fixes. The selected-row memory/emission result does not cover these paths or
bound an arbitrary vacuous prefix.

The corrected full regression driver exits **0**. Ordinary schema-aware stays **843/1/2** and full strict
schema-aware **842/2/2**, with six unmatched expected codes and no new failure IDs. Upstream CI selection
passes **1,654 per framework**, differential tests **394 per framework**, and the two process-controller
suites pass **13 tests in total**. The strict main audit retains its expected failing exit 1; no strict
failure is accepted into a baseline. The source schema and runtime requirements remain open.

Conformance and native memory audits remain separate from the isolated performance gates. The full
Phase 7 goal remains active until the remaining S1/runtime/error-code, exact push-memory,
throughput/latency, remote CI and feature-claim requirements are verified.

## Corrected-source exact native memory matrices

The fresh local Linux arm64 NativeAOT nightly matrix is complete: **39 children, 0 failures** at exact 2 GiB input / 200 MiB absolute RSS. All thirteen measured streams consume exactly 2,147,483,648 bytes and validate their independent output fingerprints. Effective managed allowance is 209,715,200 bytes. Both native-evidence flags are false; normal ICU is enabled. RSS is the greater child/outer wait4 full-lifetime counter, including startup and warmup. Tree baselines are separate and uncapped.

| Workload | Full-lifetime peak RSS bytes | Live managed bytes | Diagnostic elapsed ms | First output ms | Input at first output |
|---|---:|---:|---:|---:|---:|
| identity | 29343744 | 381472 | 56698.667609 | 0.262125 | 8192 |
| adjacent | 27312128 | 361336 | 15414.804507 | 8.517042 | 356352 |
| accumulator | 30388224 | 361704 | 53764.263025 | 53764.176150 | 2147483648 |
| iterate | 28684288 | 417752 | 14064.641381 | 14064.612006 | 2147483648 |
| merge | 30883840 | 405736 | 57638.106736 | 53.693042 | 122880 |
| fork | 30818304 | 366968 | 230784.303815 | 3.327542 | 36864 |
| source-document | 29458432 | 384208 | 52258.908273 | 0.327166 | 8192 |
| climbing-union | 30539776 | 361248 | 44912.567854 | 1.847958 | 53248 |
| climbing-intersection | 30552064 | 361256 | 43557.480478 | 8.398667 | 204800 |
| type-assertion | 26312704 | 359000 | 10513.785047 | 0.376209 | 36864 |
| type-inspection | 26423296 | 359064 | 7771.075420 | 7771.061670 | 2147483648 |
| copy-select | 29458432 | 381512 | 51971.482857 | 0.266167 | 8192 |
| conditional-content | 29777920 | 377968 | 34984.284390 | 0.219333 | 8192 |

These native timings are diagnostic: functional checks overlapped part of the matrix. They are not paired throughput comparisons. Accumulator/iterate/type-inspection results require end-of-input.

The exact 200 MiB-input / 20 MiB RSS push matrix completes all **39 children**, retaining **13 failures**. No startup/warmup or baseline is subtracted.

| Workload | Status | Measured input bytes | Full-lifetime peak RSS bytes |
|---|---|---:|---:|
| identity | budget-exceeded | 209715200 | 25591808 |
| adjacent | budget-exceeded-before-measurement | 0 | 22331392 |
| accumulator | budget-exceeded-before-measurement | 0 | 21245952 |
| iterate | budget-exceeded-before-measurement | 0 | 22487040 |
| merge | budget-exceeded-before-measurement | 0 | 21536768 |
| fork | budget-exceeded-before-measurement | 0 | 21819392 |
| source-document | budget-exceeded | 209715200 | 25751552 |
| climbing-union | budget-exceeded-before-measurement | 0 | 21430272 |
| climbing-intersection | budget-exceeded-before-measurement | 0 | 21450752 |
| type-assertion | budget-exceeded | 209715200 | 25559040 |
| type-inspection | budget-exceeded | 209715200 | 25518080 |
| copy-select | budget-exceeded | 209715200 | 25391104 |
| conditional-content | budget-exceeded | 209715200 | 26013696 |

Evidence is `/tmp/xsl3-p7-conditional-context-linux/`: both complete reports, frozen source/SHA-256 snapshot, `source-check.json`, `run.sh`, publish/run logs, native-intermediate rejection and `measured-summary.json`. The cached official SDK image is `70b5de4a1846`, .NET SDK 10.0.400 / runtime 10.0.11. This is local arm64 evidence; remote linux-x64 CI remains unverified. The nightly pass does not close the exact push budget or the full Phase 7 goal.


## Final full paired process matrix

The fresh isolated matrix is **complete: true, 162 trials**: **81 validated streams**, **63 validated tree trials** and **18 failed children**. Captured controller exit is **1**. All eighteen failed children have OOM evidence in retained stderr. The exact 100 MB throughput target is **incomplete coverage**, with no pass asserted. Failed comparisons have no ratio or winner.

Runtime is .NET 10.0.11 / macOS 27.0.1 / Arm64. Both modes use the same frozen artifact, disabled tiered compilation, equal 4 GiB managed heap guards and three fresh alternating pairs per workload/size. No competing task-owned build, test or benchmark job ran. Inputs are exact decimal MB/GB.

| Workload | Input MB | Previous stream ms | Current tree ms | Current stream ms | Stream/tree | Winner in current run |
|---|---:|---:|---:|---:|---:|---|
| identity | 1 | 31.822542 | 23.745375 | 30.357916 | 1.278477 | Tree observed median |
| adjacent | 1 | 12.226875 | 12.784334 | 12.390958 | 0.969230 | No meaningful winner within 5% band |
| accumulator | 1 | 43.995000 | 56.590208 | 45.662916 | 0.806905 | Stream observed median |
| iterate | 1 | 13.838375 | 16.637625 | 14.020167 | 0.842678 | Stream observed median |
| merge | 1 | 40.322250 | 111.020000 | 43.686583 | 0.393502 | Stream observed median |
| fork | 1 | 58.249167 | 18.023417 | 61.137333 | 3.392106 | Tree observed median |
| source-document | 1 | 31.629375 | 23.952166 | 30.976250 | 1.293255 | Tree observed median |
| copy-select | 1 | 31.856625 | 23.467000 | 30.884208 | 1.316070 | Tree observed median |
| conditional-content | 1 | — | 18.255791 | 22.829916 | 1.250557 | Tree observed median |
| identity | 100 | 3011.052500 | 2323.281959 | 3012.429375 | 1.296627 | Tree observed median |
| adjacent | 100 | 1170.384958 | 1794.682125 | 1142.953792 | 0.636856 | Stream observed median |
| accumulator | 100 | 4228.262125 | 6231.738125 | 4270.537583 | 0.685288 | Stream observed median |
| iterate | 100 | 1307.429458 | 1592.122625 | 1303.657125 | 0.818817 | Stream observed median |
| merge | 100 | 4094.596167 | — | 3947.194041 | — | No meaningful winner |
| fork | 100 | 6231.211458 | 1785.723958 | 5925.664334 | 3.318354 | Tree observed median |
| source-document | 100 | 3016.918084 | 2301.428625 | 2977.375042 | 1.293707 | Tree observed median |
| copy-select | 100 | 3022.586333 | 2597.839083 | 2978.459333 | 1.146514 | Tree observed median |
| conditional-content | 100 | — | 1975.912917 | 2189.526208 | 1.108109 | Tree observed median |
| identity | 1000 | 29776.844375 | 25005.524667 | 29707.661417 | 1.188044 | Tree observed median |
| adjacent | 1000 | 11353.574625 | — | 11399.755750 | — | No meaningful winner |
| accumulator | 1000 | 41643.741125 | — | 41737.122542 | — | No meaningful winner |
| iterate | 1000 | 12926.087708 | 21221.011292 | 12975.054625 | 0.611425 | Stream observed median |
| merge | 1000 | 39294.453083 | — | 39289.517750 | — | No meaningful winner |
| fork | 1000 | 59009.115708 | — | 60187.587375 | — | No meaningful winner |
| source-document | 1000 | 29531.675750 | 25802.989625 | 29979.103000 | 1.161846 | Tree observed median |
| copy-select | 1000 | 29627.943459 | 26125.238750 | 29985.131083 | 1.147746 | Tree observed median |
| conditional-content | 1000 | — | — | 21985.462000 | — | No meaningful winner |

Previous/current version columns come from separate runs and do not establish paired cross-version significance. Conditional-content is new and has no previous comparison. Current stream/tree ratios require three valid observations per mode. Winner labels use a descriptive 5% band; the throughput requirement uses exactly 1.0. Raw successful stream trials remain visible even when tree failures make the comparison incomplete.

| Incremental workload | 1 MB first ms / input bytes | 100 MB first ms / input bytes | 1 GB first ms / input bytes |
|---|---:|---:|---:|
| identity | 0.220291 / 8192 | 0.211750 / 8192 | 0.212667 / 8192 |
| adjacent | 4.433917 / 356352 | 4.559875 / 356352 | 4.411125 / 356352 |
| merge | 5.409541 / 122880 | 5.420958 / 122880 | 5.306375 / 122880 |
| fork | 2.386291 / 36864 | 2.346042 / 36864 | 2.377042 / 36864 |
| source-document | 0.246333 / 8192 | 0.241542 / 8192 | 0.286917 / 8192 |
| copy-select | 0.226750 / 8192 | 0.222250 / 8192 | 0.232541 / 8192 |
| conditional-content | 0.186167 / 8192 | 0.185500 / 8192 | 0.237125 / 8192 |

First output is a sink write, including secondary outputs; accumulator/iterate totals require end-of-input. These observations do not assert the full latency exit. Native memory results remain separate. Frozen binaries, exact commands/configuration, raw child stdout/stderr, all measurements and controller/runtime/artifact hashes are under `/tmp/xsl3-p7-conditional-process-benchmark/`.

The terminal source audit covers **425 files with no differences**. Artifact, engine and controller hashes match the report; `artifact-check.json` retains those checks. No production/harness change was made during the isolated matrix.

## Final ordinary performance gate

The final ordinary gate exits **0** over exactly three pairs against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`: timed geometric mean **0.990893315951**, maximum allocation ratio **1.044596929704**. Both sides use the same current micro harness with the separate process host excluded equally. This gate ran once, after the full streaming controller and all task-owned functional/native CPU jobs ended.

| Ordinary metric | Base ms | Head ms | Head/base | Winner |
|---|---:|---:|---:|---|
| grouping cold | 136.824600 | 138.905500 | 1.015209 | Tie / No meaningful winner |
| regex 1kb | 0.004700 | 0.005300 | 1.127660 | Base observed median |

Cold-start time is reported but excluded from the timed geometric mean; its allocation is included. The table retains cold start and every timed ratio above 1.05. Descriptive labels do not establish significance. Individual regressions remain attribution work, and an aggregate pass does not establish a fix for earlier outliers. Exact round observations:

- grouping cold: base **137.587500/136.824600/136.763000 ms**, head **138.905500/137.583000/141.376800 ms**.
- regex 1kb: base **0.004700/0.004700/0.004800 ms**, head **0.005300/0.004700/0.005500 ms**.

Evidence is `/tmp/xsl3-p7-conditional-final-perf.log`, `-perf-exit.txt` and `-perf-data/summary.json`, with exactly rounds 1–3 copied into that directory. All rows and allocations remain in JSON; stale rounds 4/5 in shared artifacts are excluded. The gate removes its temporary base checkout. This does not establish a streaming throughput, exact push-memory, remote CI or full Phase 7 pass. The goal remains active; no commit, push, publication or deployment was performed.

