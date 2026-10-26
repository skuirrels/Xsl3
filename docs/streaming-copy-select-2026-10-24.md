# Streamed selected-copy cardinality, 2026-10-24

The selected-copy compiler path no longer counts a forward selection before evaluating the copy body.
That count consumed the selected element's descendants and made otherwise streamable copies fail with
`stream-position`. `CompileForEach31` now uses the original `CopySelect` expression in streamed compilation
and applies the existing incremental optional-item type checker with error code XTTE3180. It retains no
inspected node history. Ordinary compilation keeps its existing count-based lowering.

A second defect appeared in the process regression: the loader's synthetic `error()` branch widened the
inferred template result to arbitrary items. That routed node-only copy bodies through content buffering.
Template result inference now uses the original selected expression, preserving genuine atomic/mixed
results while retaining node-only facts. No copy helper, validation operation, public API or saved-data
format changes.

The corrected before-fix regression has five stream-position failures and thirteen passes. The initial
fixture also included two uses of `last()` in a non-guaranteed streamed context; those fixtures were
corrected to use explicit grounding before attributing failures to the compiler. Added tests cover schema
strict/lax/strip/preserve and named-type validation, copied singleton/nested focus, empty selection,
multiple node/atomic items, XTTE3180 output rollback, atomic result types and borrowed reader lifetime.
There are twenty selected-copy cases and two new process-probe cases.

The new `copy-select` process fixture shallow-copies the selected document element and transforms its
rows through the same public APIs as the other fixtures. It shares the identity oracle, exact generated
input and bounded hash sinks. A new early-output test initially failed after the cardinality fix alone:
all four MiB were consumed before output. It passes after the result-inference fix on both frameworks.
The probe/controller now cover twelve workloads; its generated native host has twenty-four saved entry
points. The seven canonical benchmark workloads are unchanged. The checked-in corpus is regenerated
from the shared stylesheet definitions and checked for drift.

Full Debug/Release units pass **2,842 net10.0 / 2,768 net8.0**. The selected schema-aware copy set is
**52/52**. Full main strict conformance improves **2,661/50/1 to 2,664/47/1**, repairing si-copy-028,
si-copy-047 and si-copy-049. Full strict schema-aware improves **835/9/2 to 842/2/2**, repairing
si-copy-121/-122/-123/-124/-125/-127/-129. No new failure IDs occur. Ordinary schema-aware stays
843/1/2. Main strict unmatched expected codes remain seven; schema-aware strict mismatches reduce from
nine to six. No accepted strict baseline or feature claim is added.

The .NET 10 W3C harness passes **46/46 facts**. Normal XSLT remains **8,847/21/41**, unstreamed
development **2,691/20/1**, and QT3 **21,884/0/99**, with their existing error-code mismatches retained.
Upstream CI selection passes **1,654 per framework**, differential tests **394 per framework**, and
the two Python process-controller suites pass **13 tests in total**. The Release solution build has zero errors;
its existing warnings are in unchanged upstream/API test files. No new warning originates in this
increment's production changes or finalized tests.

The fresh rooted NativeAOT consumer passes saved selected-copy focus, empty/atomic selection and multiple
item rollback cases, plus all retained copy-content, type-test, namespace, fork and compatibility probes.
Its final publish has no trim/AOT warnings. It retains explicit stylesheet type roots and normal
globalization. Schema-aware saved assemblies remain explicitly unsupported. The version-2 fork fixture
is preserved, not regenerated.

Evidence so far: `/tmp/xsl3-p7-copy-select-before-corrected.log`, `-focused.log`,
`-probe-focused.log` (the early-output failure), `-probe-focused-final.log`, `-sa-focused.log`,
`-conformance-delta.json`, full regression logs under the same prefix, and saved/native sources and logs
in `/tmp/xsl3-p7-copy-select-native/`. The generator is `/tmp/xsl3-p7-copy-select-generator/`.

The full goal remains active. Remaining analysis/runtime/error-code, exact 20 MiB push budget,
throughput/latency, remote CI and feature-claim requirements are not closed by these fixes.

## Exact native memory matrices

The fresh local Linux arm64 NativeAOT nightly matrix is complete: **36 children, zero failures** at exact 2 GiB input / 200 MiB absolute RSS. All twelve measured streams validate input/output, report a 209,715,200-byte effective managed allowance and have both native-evidence flags false. Normal ICU remains enabled. Tree baselines are separate and uncapped. RSS is the larger child/outer wait4 full-lifetime counter.

| Workload | Measured peak RSS bytes | Live managed bytes | Diagnostic elapsed ms | First output ms | Input at first output |
|---|---:|---:|---:|---:|---:|
| identity | 29196288 | 380808 | 56573.164984 | 0.613125 | 8192 |
| adjacent | 27082752 | 360672 | 15011.591424 | 7.208250 | 356352 |
| accumulator | 30228480 | 361040 | 55051.714859 | 55051.681109 | 2147483648 |
| iterate | 28254208 | 416696 | 14097.665881 | 14097.645548 | 2147483648 |
| merge | 30588928 | 405072 | 57047.790401 | 10.460792 | 122880 |
| fork | 30605312 | 366304 | 236072.293816 | 4.077000 | 36864 |
| source-document | 29319168 | 383544 | 52881.295983 | 0.407875 | 8192 |
| climbing-union | 30257152 | 360584 | 50509.197106 | 2.014959 | 53248 |
| climbing-intersection | 30195712 | 360592 | 47819.720063 | 47.240084 | 204800 |
| type-assertion | 26054656 | 358336 | 10743.913172 | 0.274667 | 36864 |
| type-inspection | 26013696 | 358400 | 7687.135254 | 7687.124587 | 2147483648 |
| copy-select | 29503488 | 380848 | 52798.596774 | 0.289584 | 8192 |

Native timings are diagnostic because functional regressions ran concurrently. They are not isolated throughput comparisons. The selected-copy path consumes all 2,147,483,648 bytes and emits after only 8,192 input bytes, with its independent identity hash validated. Accumulator, iterate and type-inspection totals require end-of-input.

The exact 200 MiB-input / 20 MiB RSS push matrix is also complete at 36 children, with **twelve measured failures**. It is not waived or baseline-subtracted.

| Workload | Status | Measured input bytes | Full-lifetime peak RSS bytes |
|---|---|---:|---:|
| identity | budget-exceeded | 209715200 | 25595904 |
| adjacent | budget-exceeded-before-measurement | 0 | 21975040 |
| accumulator | budget-exceeded-before-measurement | 0 | 21364736 |
| iterate | budget-exceeded-before-measurement | 0 | 22331392 |
| merge | budget-exceeded-before-measurement | 0 | 21569536 |
| fork | budget-exceeded-before-measurement | 0 | 21557248 |
| source-document | budget-exceeded | 209715200 | 25559040 |
| climbing-union | budget-exceeded-before-measurement | 0 | 21307392 |
| climbing-intersection | budget-exceeded-before-measurement | 0 | 21241856 |
| type-assertion | budget-exceeded | 209715200 | 25391104 |
| type-inspection | budget-exceeded | 209715200 | 25350144 |
| copy-select | budget-exceeded | 209715200 | 25395200 |

Raw evidence is `/tmp/xsl3-p7-copy-select-final-linux/`: frozen source and SHA-256 snapshot, `source-check.json` (no differences), `run.sh`, publish/run logs, `measured-summary.json`, both complete reports and captured Docker/controller exit 1 for the failed push gate. The SDK image is the cached official `70b5de4a1846`; this is local arm64 evidence, not remote linux-x64 CI. The managed intermediate is rejected by the native guard.

## Final full paired process matrix

The fresh isolated matrix is **complete: true, 144 trials**: **72 validated streams**, **57 validated tree trials** and **15 failed children**. Captured controller exit is **1**. All fifteen failed children report `OutOfMemoryException` in retained stderr. The exact 100 MB throughput target is **incomplete coverage**, with no pass asserted. Failed comparisons have no ratio or winner.

Runtime is .NET 10.0.11 / macOS 27.0.1 / Arm64. Both modes use the same frozen artifact, disabled tiered compilation, equal 4 GiB managed heap guards and three fresh alternating pairs per workload/size. No competing task-owned build, test or benchmark job ran. Inputs are exact decimal MB/GB.

| Workload | Input MB | Previous stream ms | Current tree ms | Current stream ms | Stream/tree | Winner in current run |
|---|---:|---:|---:|---:|---:|---|
| identity | 1 | 30.942125 | 24.304958 | 31.822542 | 1.309302 | Tree observed median |
| adjacent | 1 | 12.040833 | 13.069458 | 12.226875 | 0.935530 | Stream observed median |
| accumulator | 1 | 43.920917 | 56.524417 | 43.995000 | 0.778336 | Stream observed median |
| iterate | 1 | 13.484125 | 16.158292 | 13.838375 | 0.856426 | Stream observed median |
| merge | 1 | 40.906542 | 116.257209 | 40.322250 | 0.346837 | Stream observed median |
| fork | 1 | 61.701708 | 17.816000 | 58.249167 | 3.269486 | Tree observed median |
| source-document | 1 | 31.467833 | 24.034500 | 31.629375 | 1.315999 | Tree observed median |
| copy-select | 1 | — | 23.599083 | 31.856625 | 1.349909 | Tree observed median |
| identity | 100 | 2997.399125 | 2305.204167 | 3011.052500 | 1.306198 | Tree observed median |
| adjacent | 100 | 1151.102875 | 1795.403459 | 1170.384958 | 0.651879 | Stream observed median |
| accumulator | 100 | 4404.317292 | 6335.806834 | 4228.262125 | 0.667360 | Stream observed median |
| iterate | 100 | 1330.753708 | 1625.229333 | 1307.429458 | 0.804458 | Stream observed median |
| merge | 100 | 4068.536417 | — | 4094.596167 | — | No meaningful winner |
| fork | 100 | 5949.591792 | 1803.030792 | 6231.211458 | 3.455965 | Tree observed median |
| source-document | 100 | 3128.917000 | 2350.857333 | 3016.918084 | 1.283327 | Tree observed median |
| copy-select | 100 | — | 2649.288459 | 3022.586333 | 1.140905 | Tree observed median |
| identity | 1000 | 31304.954875 | 25879.706875 | 29776.844375 | 1.150587 | Tree observed median |
| adjacent | 1000 | 12217.078166 | — | 11353.574625 | — | No meaningful winner |
| accumulator | 1000 | 43640.809083 | — | 41643.741125 | — | No meaningful winner |
| iterate | 1000 | 14552.146791 | 21987.851958 | 12926.087708 | 0.587874 | Stream observed median |
| merge | 1000 | 45404.244416 | — | 39294.453083 | — | No meaningful winner |
| fork | 1000 | 62862.834958 | — | 59009.115708 | — | No meaningful winner |
| source-document | 1000 | 30801.247417 | 25153.598250 | 29531.675750 | 1.174054 | Tree observed median |
| copy-select | 1000 | — | 28371.571416 | 29627.943459 | 1.044283 | No meaningful winner within 5% band |

Previous/current version columns come from separate runs and do not establish paired cross-version significance. Selected-copy is new and has no previous comparison. Current stream/tree ratios require three valid observations per mode. Winner labels use a descriptive 5% band; the throughput requirement uses exactly 1.0. Raw successful stream trials remain visible even when tree failures make the comparison incomplete.

| Incremental workload | 1 MB first ms / input bytes | 100 MB first ms / input bytes | 1 GB first ms / input bytes |
|---|---:|---:|---:|
| identity | 0.243125 / 8192 | 0.229583 / 8192 | 0.230833 / 8192 |
| adjacent | 4.418792 / 356352 | 4.521666 / 356352 | 4.322583 / 356352 |
| merge | 5.328125 / 122880 | 5.456708 / 122880 | 5.298958 / 122880 |
| fork | 2.342000 / 36864 | 2.375834 / 36864 | 2.448750 / 36864 |
| source-document | 0.276333 / 8192 | 0.283792 / 8192 | 0.277042 / 8192 |
| copy-select | 0.239708 / 8192 | 0.239292 / 8192 | 0.233083 / 8192 |

First output is a sink write, including secondary outputs; accumulator/iterate totals require end-of-input. These observations do not assert the full latency exit. Native memory results remain separate. Frozen binaries, exact commands/configuration, raw child stdout/stderr, all measurements and controller/runtime/artifact hashes are under `/tmp/xsl3-p7-copy-select-process-benchmark/`.

The terminal source audit covers **422 files with no differences**. Artifact, engine and controller SHA-256 values match the report; their check is retained in `artifact-check.json`. The native correctness snapshot covers 393 matching files and the Linux memory snapshot covers 527 matching files. No production/harness changes were made during the isolated matrix.

## Final ordinary performance gate

The final ordinary gate exits **0** over exactly three pairs against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`: timed geometric mean **1.027029251298**, maximum allocation ratio **1.043895720117**. Both sides use the same current micro harness with the separate process host excluded equally. This gate ran once, after the full streaming controller and all task-owned functional/native CPU jobs ended.

| Ordinary metric | Base ms | Head ms | Head/base | Winner |
|---|---:|---:|---:|---|
| grouping cold | 139.030500 | 143.090700 | 1.029204 | Tie / No meaningful winner |
| sorting compile | 0.570050 | 0.623600 | 1.093939 | Base observed median |
| keys compile | 0.310100 | 0.547700 | 1.766204 | Base observed median |
| join compile | 0.093200 | 0.101500 | 1.089056 | Base observed median |
| dispatch compile | 1.643400 | 1.751300 | 1.065657 | Base observed median |
| regex 1kb | 0.004700 | 0.005500 | 1.170213 | Base observed median |

Cold-start time is reported but excluded from the timed geometric mean; its allocation is included. The table retains cold start and every timed ratio above 1.05. Descriptive labels do not establish significance. Individual regressions remain attribution work, and an aggregate pass does not establish a fix for earlier outliers. Exact round observations:

- grouping cold: base **144.021600/139.030500/135.438500 ms**, head **143.577900/143.090700/140.164600 ms**.
- sorting compile: base **0.593200/0.499400/0.570050 ms**, head **0.632200/0.549100/0.623600 ms**.
- keys compile: base **0.310100/0.301700/0.329550 ms**, head **0.610600/0.547700/0.285500 ms**.
- join compile: base **0.090200/0.093200/0.100000 ms**, head **0.101500/0.101900/0.091700 ms**.
- dispatch compile: base **1.643400/1.888200/1.579700 ms**, head **1.793900/1.603400/1.751300 ms**.
- regex 1kb: base **0.004700/0.004600/0.004800 ms**, head **0.005400/0.005500/0.005500 ms**.

Evidence is `/tmp/xsl3-p7-copy-select-final-perf.log`, `-perf-exit.txt` and `-perf-data/summary.json`, with exactly rounds 1–3 copied into that directory. All rows and allocations remain in JSON; stale rounds 4/5 in shared artifacts are excluded. The gate removes its temporary base checkout. This does not establish a streaming throughput, exact push-memory, remote CI or full Phase 7 pass. The goal remains active; no commit, push, publication or deployment was performed.
