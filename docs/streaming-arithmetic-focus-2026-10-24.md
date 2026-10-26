# Forward arithmetic and numeric focus, 2026-10-24

This is the arithmetic/focus checkpoint. The later [generic item-parameter and debug-worker increment](streaming-item-parameters-2026-10-24.md) has its own source snapshots and validation. Measurements below remain tied to this checkpoint.

Forward arithmetic now atomizes each selected node before its selection advances. The minimal
`price + (1,2)[last()]` argument returns `3.25` and `4.75` from source prices `1.25` and `2.75`, matching
ordinary execution. Unary arithmetic and a node on either side of a binary operator use the same
operand preparation. The existing arithmetic implementation retains conversion and cardinality errors.

Compatibility mode retains its distinct first-item rule. `(price, map{}) + 0` converts the first price
without trying to atomize the later map. A statically empty operand still produces NaN. Atomic and
statically empty operands bypass the forward window; an unnecessary window triggered the Debug QIL
movement verifier when an empty arithmetic expression was optimized. The verifier remains enabled.

## Focus analysis and capture

`FocusAnalysis` describes numeric focus imported from the surrounding expression. Predicates, path
right operands and simple-map right operands bind their own focus; inline function bodies start with
absent focus. Map keys/values, array members, lookup expressions, arrows and function references are
visited explicitly. Built-in function names are resolved through the existing namespace/type resolver.
Unknown future expression kinds conservatively retain both numeric properties.

The shared analysis serves XPath context-size binding, the S1 scanning-predicate rule, and the forward
header-predicate eligibility check. It fixes array/map expressions such as `(1 to 3) ! ([last()]?1)` and
`(1 to 3) ! (map{1:last()}?1)`, which previously failed with `last() not prepared`. Grounded local
`position()`/`last()` no longer makes a containing scanning predicate positional.

A filtered child following a descendant selection can use the existing header scan, avoiding a return
to an ancestor whose child selection advanced the reader. For example,
`count(descendant::row/price[exists((1,2) ! last())])` counts both flat and nested input correctly.
Direct descendant selections retain their existing absorption-aware iterator. Extending the header
scan to every single-descendant path caused five regressions in cardinality, JSON and data absorption;
the final eligibility rule is restricted to the paths needing this repair.

Named `position#0` and `last#0` references capture the template's numeric focus instead of a synthetic
singleton focus. A `function-lookup#2` reference retains those captured numeric function values when
the requested name is supplied later. A reference can be created with absent focus; calling its
captured numeric function raises XPDY0002. No public helper or saved-data version changes.
The closure behavior follows the [F&O 3.1 context-dependence rules](https://www.w3.org/TR/xpath-functions-31/#func-function-lookup).
This increment addresses numeric focus; it does not establish complete computed-name or recursive
lookup support for every context-dependent function.

The stylesheet-wide conservative template-focus policy is retained. The original scoped-last
investigation attributed the arithmetic failure to that policy. A consistent comparison with the old
policy and repaired atomic operands passes all 33 net10 / 31 net8 cases then present. The arithmetic
boundary caused that failure; a template call-graph rewrite was unnecessary and was removed.

## Reproductions and validation

The new coverage consists of 51 net10 / 49 net8 end-to-end focus/arithmetic cases and four S1 expression
cases. It includes ordinary and forward execution, both APIs, debug compilation, saved assemblies,
transitive calls, mode dispatch, absent focus and compatibility-mode first-item/empty behavior.

Evidence retained separately:

- `/tmp/xsl3-p7-focus-scope-before-corrected.log`: sixteen failed / two passed per framework before the repairs; this reproduction combines arithmetic and focus examples and does not isolate their causes.
- `/tmp/xsl3-p7-focus-scope-old-demand-corrected.log`: consistent old template-focus policy with repaired operands; 33 / 31 cases pass. The earlier `old-demand-only.log` combined inconsistent template/mode policies and is invalid causal evidence.
- `/tmp/xsl3-p7-focus-scope-old-xpath-analysis.log`: four genuine missing-size failures and three S1 positional false positives. Its nested-filter control initially asserted Striding instead of Crawling; that incorrect expectation is excluded from the defect count.
- `/tmp/xsl3-p7-focus-scope-compat-diagnostic/run.log`: ordinary first-item conversion succeeds while the initial forward atomization raises FOTY0013.
- `/tmp/xsl3-p7-focus-scope-final-verified-units-debug.log`: the empty-operand Debug verifier failure before the window eligibility repair.
- `/tmp/xsl3-p7-focus-scope-compat-empty-repair.log`: all 51 / 49 focused cases pass after that repair.

The corrected complete functional driver exits 0. Debug and Release each pass **3,011 net10 / 2,921
net8** unit cases. The solution build, 46 .NET 10 W3C harness facts, 1,654 upstream cases per framework,
394 differential cases per framework and thirteen controller tests pass. The current conformance
reports retain the preceding counts and have no new failure IDs across eight compared profiles:

| Profile | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| Normal XSLT 3.0 | 8847 | 21 | 41 | 34 |
| Streaming corpus, ordinary execution | 2691 | 20 | 1 | 5 |
| Streaming corpus, strict forward inputs | 2671 | 40 | 1 | 7 |
| Schema-aware, ordinary | 843 | 1 | 2 | 6 |
| Schema-aware, strict forward inputs | 842 | 2 | 2 | 6 |
| QT3 | 21884 | 0 | 99 | 67 |
| Posture and sweep | 790 | 129 | 0 | 0 |
| XSLT 2.0 | 4588 | 9 | 11 | 7 |

The strict development audit retains its failing exit status; it is not promoted to a passing gate.
The frozen reports and exact failure-ID comparison are `/tmp/xsl3-p7-focus-scope-corrected-reports/`
and `/tmp/xsl3-p7-focus-scope-conformance-delta.json`.

A fresh macOS arm64 NativeAOT consumer passes twelve new normal/debug saved stylesheets and every
retained compatibility probe, including the immutable legacy version-2 fork assembly. The twelve
styles cover arithmetic, template numeric capture, scanning, first-item compatibility, local XPath
map/array size and empty compatibility operands. All **396** current/frozen core/configuration hashes
match; **80** native/saved artifact hashes are recorded. Publish emits no trim/AOT warnings. The
legacy assembly SHA-256 remains `89ba201ad9e21c7a3a4add5231c114f5aee9a2f9eef4bdd9472a41e680b5caee`.

Corrected evidence is under `focus-scope-corrected-verified` and `focus-scope-verified-native`.
The exact corrected memory matrix is complete under `focus-scope-final-linux`: 42 nightly children
pass and fourteen push workloads exceed the RSS limit. The ordinary three-pair gate starts only after
all task-owned functional/native CPU jobs terminate.
The earlier `focus-scope-verified`, `focus-scope-native`, `focus-scope-final-native` and
`focus-scope-linux` artifacts are intermediate checkpoints; they do not verify the final compatibility
and empty-operand repairs.


## Final-source exact native memory matrices

The fresh local Linux arm64 NativeAOT nightly matrix is complete: **42 children, 0 failures** at exact 2 GiB input / 200 MiB absolute RSS. All fourteen measured streams consume exactly 2,147,483,648 bytes and validate their independent output fingerprints. Effective managed allowance is 209,715,200 bytes. Both native-evidence flags are false; normal ICU is enabled. RSS is the greater child/outer wait4 full-lifetime counter, including startup and warmup. Tree baselines are separate and uncapped.

| Workload | Full-lifetime peak RSS bytes | Live managed bytes | Diagnostic elapsed ms | First output ms | Input at first output |
|---|---:|---:|---:|---:|---:|
| identity | 29810688 | 381952 | 55402.818609 | 0.567042 | 8192 |
| adjacent | 27570176 | 362352 | 13635.908339 | 6.560875 | 356352 |
| accumulator | 30547968 | 362184 | 54293.493274 | 54293.462983 | 2147483648 |
| iterate | 28901376 | 417696 | 15130.209799 | 15130.203424 | 2147483648 |
| merge | 31117312 | 406216 | 55230.061650 | 10.541250 | 122880 |
| fork | 31117312 | 367448 | 233966.287150 | 5.307834 | 36864 |
| source-document | 29777920 | 384688 | 52478.337565 | 0.272750 | 8192 |
| climbing-union | 30531584 | 361728 | 45192.671436 | 1.770625 | 53248 |
| climbing-intersection | 30593024 | 361736 | 42937.188311 | 7.632792 | 204800 |
| type-assertion | 26701824 | 359480 | 10751.958755 | 0.203542 | 36864 |
| type-inspection | 26341376 | 359544 | 7947.739629 | 7947.717754 | 2147483648 |
| copy-select | 29732864 | 381992 | 51910.239440 | 0.432709 | 8192 |
| conditional-content | 29941760 | 378448 | 33567.312431 | 0.161916 | 8192 |
| template-parameter | 30715904 | 371800 | 20047.891968 | 3.068875 | 122880 |

These native timings are diagnostic: functional checks overlapped part of the matrix. They are not paired throughput comparisons. Accumulator/iterate/type-inspection results require end-of-input.

The exact 200 MiB-input / 20 MiB RSS push matrix completes all **42 children**, retaining **14 failures**. No startup/warmup or baseline is subtracted.

| Workload | Status | Measured input bytes | Full-lifetime peak RSS bytes |
|---|---|---:|---:|
| identity | budget-exceeded | 209715200 | 25972736 |
| adjacent | budget-exceeded-before-measurement | 0 | 22487040 |
| accumulator | budget-exceeded-before-measurement | 0 | 21385216 |
| iterate | budget-exceeded-before-measurement | 0 | 22740992 |
| merge | budget-exceeded-before-measurement | 0 | 21626880 |
| fork | budget-exceeded-before-measurement | 0 | 21540864 |
| source-document | budget-exceeded | 209715200 | 25915392 |
| climbing-union | budget-exceeded-before-measurement | 0 | 21508096 |
| climbing-intersection | budget-exceeded-before-measurement | 0 | 21495808 |
| type-assertion | budget-exceeded | 209715200 | 26034176 |
| type-inspection | budget-exceeded | 209715200 | 25706496 |
| copy-select | budget-exceeded | 209715200 | 25681920 |
| conditional-content | budget-exceeded | 209715200 | 26271744 |
| template-parameter | budget-exceeded-before-measurement | 0 | 21409792 |

Evidence is `/tmp/xsl3-p7-focus-scope-final-linux/`: both complete reports, frozen source/SHA-256 snapshot, `source-check.json`, `run.sh`, publish/run logs, native-intermediate rejection and `measured-summary.json`. The cached official SDK image is `70b5de4a1846`, .NET SDK 10.0.400 / runtime 10.0.11. This is local arm64 evidence; remote linux-x64 CI remains unverified. The nightly pass does not close the exact push budget or the full Phase 7 goal.

The container's exit code **1** is recorded directly by `docker wait`. The initial local zsh wrapper
also used its read-only `status` variable after the container completed; `host-wrapper-status.json`
retains that separate wrapper error. It does not replace the daemon exit status or either complete
controller report. The repository's Bash container driver retains the actual push failure status.


## Ordinary performance gate

One gate run exits **0** against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` over exactly three alternating pairs. Timing geometric mean is **0.994790425361**; maximum allocation ratio is **1.046024392076** (grouping cold, 1,597,240/1,670,752 bytes). Cold time is excluded from the 39-row timed mean; cold allocation is included. Cold time ratio is **1.033200447423** (134.727100/139.200100 ms).

The same ordinary harness and corpus runs on both versions with the separate process host excluded equally. All task-owned functional/native CPU jobs were terminal before this comparison. The temporary base worktree is removed. This compares the complete working tree with the base; it does not isolate the causal effect of this increment.

| Workload | Size | Base 0f2ddcb ms | Current branch ms | Head/base | Base allocated bytes | Current allocated bytes | Allocation ratio | Winner |
|---|---|---:|---:|---:|---:|---:|---:|---|
| grouping | cold | 134.727100 | 139.200100 | 1.033200 | 1597240 | 1670752 | 1.046024 | No meaningful winner (5% band) |
| grouping | compile | 0.654200 | 0.658500 | 1.006573 | 186810 | 191472 | 1.024956 | No meaningful winner (5% band) |
| sorting | compile | 0.590200 | 0.494150 | 0.837259 | 182433 | 186789 | 1.023877 | Current observed median |
| keys | compile | 0.349400 | 0.304200 | 0.870635 | 194239 | 199587 | 1.027533 | Current observed median |
| join | compile | 0.094100 | 0.093000 | 0.988310 | 150719 | 154081 | 1.022306 | No meaningful winner (5% band) |
| regex | compile | 0.084700 | 0.084200 | 0.994097 | 165111 | 168235 | 1.018921 | No meaningful winner (5% band) |
| strings | compile | 0.084800 | 0.086900 | 1.024764 | 177987 | 180665 | 1.015046 | No meaningful winner (5% band) |
| maps | compile | 0.162400 | 0.162900 | 1.003079 | 285534 | 291880 | 1.022225 | No meaningful winner (5% band) |
| fold-left | compile | 0.186000 | 0.186400 | 1.002151 | 305117 | 308701 | 1.011746 | No meaningful winner (5% band) |
| iterate | compile | 0.153000 | 0.155100 | 1.013725 | 238285 | 242413 | 1.017324 | No meaningful winner (5% band) |
| identity | compile | 0.044500 | 0.045300 | 1.017978 | 97900 | 99371 | 1.015026 | No meaningful winner (5% band) |
| dispatch | compile | 1.637800 | 1.661800 | 1.014654 | 4004367 | 4117626 | 1.028284 | No meaningful winner (5% band) |
| recursion | compile | 0.123000 | 0.123800 | 1.006504 | 237817 | 246673 | 1.037239 | No meaningful winner (5% band) |
| json | compile | 0.068100 | 0.069000 | 1.013216 | 156509 | 158367 | 1.011872 | No meaningful winner (5% band) |
| grouping | 1kb | 0.002600 | 0.002500 | 0.961538 | 13113 | 13184 | 1.005414 | No meaningful winner (5% band) |
| sorting | 1kb | 0.002300 | 0.002200 | 0.956522 | 12205 | 12293 | 1.007210 | No meaningful winner (5% band) |
| keys | 1kb | 0.004500 | 0.004600 | 1.022222 | 17469 | 17556 | 1.004980 | No meaningful winner (5% band) |
| join | 1kb | 0.003600 | 0.003600 | 1.000000 | 14120 | 14192 | 1.005099 | No meaningful winner (5% band) |
| regex | 1kb | 0.004700 | 0.005500 | 1.170213 | 15613 | 15688 | 1.004804 | Base observed median |
| strings | 1kb | 0.003300 | 0.003300 | 1.000000 | 12831 | 12902 | 1.005533 | No meaningful winner (5% band) |
| maps | 1kb | 0.006500 | 0.006600 | 1.015385 | 27221 | 27366 | 1.005327 | No meaningful winner (5% band) |
| fold-left | 1kb | 0.004300 | 0.004200 | 0.976744 | 18803 | 18963 | 1.008509 | No meaningful winner (5% band) |
| iterate | 1kb | 0.004200 | 0.004300 | 1.023810 | 23091 | 23204 | 1.004894 | No meaningful winner (5% band) |
| identity | 1kb | 0.002800 | 0.002900 | 1.035714 | 10812 | 10885 | 1.006752 | No meaningful winner (5% band) |
| dispatch | 1kb | 0.007200 | 0.007400 | 1.027778 | 23593 | 23666 | 1.003094 | No meaningful winner (5% band) |
| recursion | 1kb | 0.003600 | 0.003600 | 1.000000 | 17570 | 17650 | 1.004553 | No meaningful winner (5% band) |
| json | 1kb | 0.005900 | 0.006000 | 1.016949 | 35039 | 35160 | 1.003453 | No meaningful winner (5% band) |
| grouping | 1mb | 0.887600 | 0.854250 | 0.962427 | 1985615 | 1985686 | 1.000036 | No meaningful winner (5% band) |
| sorting | 1mb | 2.106250 | 2.180950 | 1.035466 | 3410835 | 3428508 | 1.005181 | No meaningful winner (5% band) |
| keys | 1mb | 3.840300 | 3.508900 | 0.913705 | 6152455 | 6152541 | 1.000014 | Current observed median |
| join | 1mb | 2.476500 | 2.340100 | 0.944922 | 4525269 | 4525339 | 1.000015 | Current observed median |
| regex | 1mb | 5.817050 | 5.830000 | 1.002226 | 13071945 | 13072017 | 1.000006 | No meaningful winner (5% band) |
| strings | 1mb | 2.851650 | 2.846850 | 0.998317 | 5462928 | 5462999 | 1.000013 | No meaningful winner (5% band) |
| maps | 1mb | 4.118900 | 4.181300 | 1.015150 | 10153657 | 10175778 | 1.002179 | No meaningful winner (5% band) |
| fold-left | 1mb | 2.783300 | 2.686000 | 0.965041 | 7319895 | 7443142 | 1.016837 | No meaningful winner (5% band) |
| iterate | 1mb | 1.231700 | 1.197250 | 0.972031 | 3114677 | 3132470 | 1.005713 | No meaningful winner (5% band) |
| identity | 1mb | 1.939200 | 1.967100 | 1.014387 | 3154312 | 3154384 | 1.000023 | No meaningful winner (5% band) |
| dispatch | 1mb | 1.380000 | 1.405200 | 1.018261 | 2650127 | 2650199 | 1.000027 | No meaningful winner (5% band) |
| recursion | 1mb | 1.235050 | 1.247000 | 1.009676 | 2781848 | 2781988 | 1.000050 | No meaningful winner (5% band) |
| json | 1mb | 9.400250 | 9.352800 | 0.994952 | 18294345 | 18346955 | 1.002876 | No meaningful winner (5% band) |

Winner labels use a 5% diagnostic band; they do not replace an uncertainty test. Exact round files and per-row medians/allocations are retained in `/tmp/xsl3-p7-focus-scope-final-perf-data/`. Only rounds 1–3 are used.

Timed rows above 1.05 remain observed regressions requiring attribution:

- **regex 1kb**: ratio **1.170212765957**, base/current **0.004700/0.005500 ms**. Base rounds `[0.0046, 0.0047, 0.0047]`, current rounds `[0.0055, 0.0055, 0.0055]`. Allocation ratio **1.004803689233**.

No additional performance run was selected to replace these results. Earlier individual outliers are not declared fixed by their absence. No fresh paired streaming process matrix was run for this source: the preceding 60-trial 100 MB matrix and full three-size matrix remain evidence for their own earlier snapshots. Throughput/latency exits remain open.


The full P7 goal remains active. Main strict failures and unmatched error codes, the exact 20 MiB push
RSS budget, throughput/latency coverage, remaining S1/runtime cases, remote CI, schema-aware saved
assemblies and the streaming feature claim are separate outstanding requirements. No commit or push
is performed.
