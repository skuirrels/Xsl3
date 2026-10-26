# Streamed node-only content and namespace scopes, 2026-10-24

This increment fixes a streaming buffering defect and repeated namespace-scope allocation. It does not complete Phase 7. The exact 20 MiB push budget, streaming throughput, remaining strict/S1/error-code work and feature claim remain open.

## Defect and changes

`CompileCopyOf31` described every option-aware/streamed copy as arbitrary `item()*`, including copies whose input was statically known to contain only nodes. The containing constructor therefore used `X31ContentItems`, which buffered its complete content before emitting it. A row with 10,000 child elements consumed all **180,031 input bytes** before the first caller write in each of four copy-option variants. The previous small-row process corpus did not expose this unbounded containing-row buffer.

Known non-RTF node input now retains the broad node-only result type after the existing copy and validation helpers. Schema annotations remain unconstrained because validation/stripping may change them. This lets `ContentNodes` write the node sequence directly. The helpers still create distinct copied nodes, apply namespace/annotation/accumulator options and raise their original errors. Atomic or mixed input keeps the arbitrary-item path. No new runtime helper, public API or saved-data format is introduced.

The six new copy-content cases cover incremental caller delivery for default, strip, preserve and no-copy-namespaces options, plus parentless/distinct copied attributes under strip and preserve. All four emission cases fail before the fix and pass afterward: the first write must occur before half the input is consumed, and the complete output must still contain every child and copied attribute. The native consumer independently checks the same four saved stylesheets through the public legacy load/transform path. New compilations receive the QIL improvement; older saved assemblies remain compatible and need recompilation to receive that emitted-code change.

`StreamedNavigator.Namespaces()` previously built a hash set, list and array for every enumeration. The new allocation regression records **3,520,000 bytes** for 10,000 warmed namespace enumerations in each scope mode before the change. It now passes a **less than 4,096 bytes** tolerance for the same repeated public navigator operations. Immutable resolved scopes belong to source frames. Declaration-free frames share their ancestor scope, including declaration-owner numbers; implicit XML-only scope data can be shared. Scope lookup walks ancestors iteratively. Compare/exchange publication supports safe shared reads without a document-wide history of discarded frame keys.

Fork headers copy declarations into fresh local frames/name tables; they do not copy the resolved-scope cache. Tests warm the source cache before forking and verify declared prefix/URI atomization in each view, local ownership, rebinding between siblings, readable expired headers and collection of discarded frames. Five new namespace cases plus the strengthened fork case cover these invariants. Explicit replay/grounding ownership is unchanged.

## Diagnostics and targeted measurements

Eight `dotnet-stack` captures of the frozen pre-change 1 GB identity workload stopped in namespace enumeration/array copying. Such captures can favor GC safe points and do not quantify a CPU percentage. Diagnostic EventPipe traces were also collected for the previous engine, the namespace-only engine and its tree mode using the already-cached official TraceEvent library. The previous trace is dominated by GC-poll frames; it is not treated as an unbiased CPU attribution. The namespace-only trace and QIL inspection exposed the copy/content conversion path, which the emission regression then reproduced directly. Instrumented timings are excluded from benchmark/gate results. See the [Microsoft diagnostics-client method](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/diagnostics-client-library).

Uninstrumented targeted process runs have three fresh alternating pairs at 1 MB and 100 MB, all exact-input/oracle checks passing (**36/36 children** for each implementation). The table shows 100 MB medians from separately collected version runs; it is descriptive, not a paired statistical comparison between versions. The stream/tree column is paired within the latest targeted run.

| Workload | Previous stream ms | Namespace-only stream ms | Both fixes stream ms | Both fixes tree ms | Stream/tree | Winner within latest run |
|---|---:|---:|---:|---:|---:|---|
| identity | 4764.436000 | 4448.350792 | 3028.680416 | 2346.177416 | 1.290900 | Tree observed median |
| fork | 5921.473333 | 6061.361084 | 6224.039125 | 1776.608167 | 3.503327 | Tree observed median |
| source-document | 5011.438875 | 4570.432792 | 3158.651375 | 2303.388625 | 1.371306 | Tree observed median |

Identity and source-document improve in these observations but still exceed the exact 1.0 target. Fork remains unresolved. The subset does not satisfy canonical seven-workload coverage, and the descriptive Winner labels do not relax the throughput requirement.

Raw diagnostics: `/tmp/xsl3-p7-identity-profile/`, `/tmp/xsl3-p7-namespace-eventpipe/`, `/tmp/xsl3-p7-namespace-trace-analyzer/` and `/tmp/xsl3-p7-namespace-identity.qil.xml`. Targeted timing reports: `/tmp/xsl3-p7-namespace-targeted-benchmark/` and `/tmp/xsl3-p7-copy-content-targeted-benchmark/`. Both are separate from the final full process matrix.

## Regression and compatibility evidence

- Before-fix allocation and emission logs: `/tmp/xsl3-p7-namespace-before.log` (three failures) and `/tmp/xsl3-p7-copy-content-before.log` (four failures; copied-attribute identity cases pass).
- Focused tests pass **85/85 per framework** before the test-helper field was changed to a property to remove its CS1690 warning. Full Debug/Release tests then pass **2,820 net10.0 / 2,746 net8.0**. There are eleven new cases.
- The .NET 10 W3C harness passes **46/46 facts**. The harness targets net10.0; unit, upstream and differential projects target both frameworks. Normal XSLT remains **8,847/21/41**, unstreamed development **2,691/20/1**, strict development **2,661/50/1**, and QT3 **21,884/0/99**, with unchanged failure IDs. Strict expected-code mismatches remain seven; no accepted strict baseline was added.
- Fresh schema-aware audits are **843/1/2** in ordinary mode and **835/9/2** with strict forward inputs. The same current W3C harness run against the frozen pre-change engine gives exactly those counts and failure IDs. The older stored ordinary report was 842/2/2 and the old strict report covered only twelve cases; neither is used to attribute an improvement or regression to this increment. All nine full strict schema-aware failures remain open.
- Upstream CI selection passes **1,654 per framework**; differential tests pass **394 per framework**; both process-controller suites pass **13 tests**.
- Release solution build passes with zero errors and thirty existing warnings in unchanged upstream/API test files. No warning originates in the three changed production files or the finalized new tests. The unchanged API test has xUnit2013; upstream test warnings include CA2200.
- The fresh macOS NativeAOT consumer passes the four new saved-copy emission cases and every retained correctness probe. It asserts both unsupported dynamic code and absent trusted platform assemblies. Its final publish has no trim/AOT warnings. The frozen version-2 fork DLL hash remains unchanged.
- The temporary native consumer first included the copied core source through its default file glob, then failed after an unrooted type array lost saved-stylesheet private metadata. Those harness failures are retained. The final project excludes the source subtree and uses explicit annotated type roots; no metadata/globalization feature was disabled and no warning was suppressed.

Logs use `/tmp/xsl3-p7-copy-content-{solution,units-release,units-debug,w3c,unstreamed,strict,upstream,differential,python,sa,sa-strict}.log`. Exact report deltas are `/tmp/xsl3-p7-copy-content-regression-delta.json` and `-schema-delta.json`; the frozen pre-change schema harness is `/tmp/xsl3-p7-copy-content-before-w3c/`. Saved/native sources, four generated stylesheets, final and initial-failure logs are under `/tmp/xsl3-p7-copy-content-native/`, with its generator in `/tmp/xsl3-p7-copy-content-generator/`.

## Fresh exact NativeAOT memory matrices

The frozen current source passes the complete local Linux arm64 **2 GiB / 200 MiB** matrix: **33 children, zero failures**. All eleven measured streams consume exactly 2,147,483,648 bytes, validate their independent output hashes, report a 209,715,200-byte effective managed allowance and prove both native-evidence flags false. Normal ICU globalization is enabled. Peak RSS is the greater of the child and outer wait4 counters, including full lifetime. Tree baselines are separate and uncapped.

| Workload | Measured peak RSS bytes | Live managed bytes | Diagnostic elapsed ms | First-output ms | Input at first output |
|---|---:|---:|---:|---:|---:|
| type-assertion | 26312704 | 357880 | 11053.016546 | 0.217375 | 36864 |
| type-inspection | 26296320 | 357944 | 8112.930546 | 8112.902504 | 2147483648 |
| identity | 29540352 | 380352 | 55616.883317 | 0.413959 | 8192 |
| adjacent | 27459584 | 360216 | 15521.105757 | 4.381042 | 356352 |
| accumulator | 30576640 | 360584 | 54620.128318 | 54620.077984 | 2147483648 |
| iterate | 28491776 | 416240 | 14761.176841 | 14761.138966 | 2147483648 |
| merge | 30945280 | 404616 | 57222.025776 | 5.535333 | 122880 |
| fork | 30769152 | 365848 | 241521.342653 | 4.794667 | 36864 |
| source-document | 29392896 | 383088 | 52731.105649 | 0.672958 | 8192 |
| climbing-union | 30474240 | 360128 | 46566.861479 | 2.016583 | 53248 |
| climbing-intersection | 30547968 | 360136 | 44358.509895 | 7.303583 | 204800 |

The maximum measured large-input RSS is **30,945,280 bytes (29.511719 MiB)**. These native timings are diagnostic because other functional regressions ran concurrently; they are not the isolated throughput benchmark. Accumulator, iterate and type-inspection results require end-of-input.

The exact **200 MiB / 20 MiB** matrix remains red: **33 children, eleven measured failures**, controller and Docker exit 1. Four measured children consume their entire input and validate output before exceeding RSS; seven fail during startup/warmup with zero measured input. No limit or effective cap is relaxed and no startup/baseline subtraction is used.

| Workload | Status | Measured input bytes | Full-lifetime peak RSS bytes |
|---|---|---:|---:|
| identity | budget-exceeded | 209715200 | 25460736 |
| adjacent | budget-exceeded-before-measurement | 0 | 22106112 |
| accumulator | budget-exceeded-before-measurement | 0 | 21372928 |
| iterate | budget-exceeded-before-measurement | 0 | 22482944 |
| merge | budget-exceeded-before-measurement | 0 | 21676032 |
| fork | budget-exceeded-before-measurement | 0 | 21397504 |
| source-document | budget-exceeded | 209715200 | 25776128 |
| climbing-union | budget-exceeded-before-measurement | 0 | 21409792 |
| climbing-intersection | budget-exceeded-before-measurement | 0 | 21413888 |
| type-assertion | budget-exceeded | 209715200 | 25718784 |
| type-inspection | budget-exceeded | 209715200 | 25575424 |

Evidence: `/tmp/xsl3-p7-copy-content-final-linux/`, including `source-snapshot.json`, `source-check.json` (no differences), frozen `source/`, `run.sh`, `run.log`, `measured-summary.json`, genuine native artifacts and both complete controller reports. The cached official SDK image is `70b5de4a1846`; this is local Linux arm64 evidence, not a remote linux-x64 CI run. The managed publish intermediate is rejected by the native guard.

No feature-support claim, public API, runtime budget or saved-assembly format changes in this increment. The full goal remains active.

## Final full paired process matrix

The fresh isolated matrix is **complete: true, 126 trials**, with **63 validated streams**, **48 validated tree runs** and **15 failed children**. All failed children report OOM in retained stderr: **True**. The captured controller exit is **1**. `--require-throughput-target` is enabled. The 100 MB target is **incomplete coverage**, with passed=None. No failed comparison receives a ratio or winner.

Runtime is .NET 10.0.11 / macOS 27.0.1 / Arm64, with the same frozen artifact for both modes, tiered compilation disabled, three alternating pairs per workload/size and a 4 GiB managed heap guard. No other task-owned build, test or benchmark job ran concurrently. Sizes are exact decimal MB/GB.

| Workload | Input MB | Previous stream ms | Current tree ms | Current stream ms | Current stream/tree | Winner in current run |
|---|---:|---:|---:|---:|---:|---|
| identity | 1 | 47.668791 | 24.229542 | 30.942125 | 1.277041 | Tree observed median |
| adjacent | 1 | 11.750750 | 13.088750 | 12.040833 | 0.919938 | Stream observed median |
| accumulator | 1 | 43.283166 | 62.053084 | 43.920917 | 0.707796 | Stream observed median |
| iterate | 1 | 13.329458 | 18.653417 | 13.484125 | 0.722877 | Stream observed median |
| merge | 1 | 44.844416 | 124.945583 | 40.906542 | 0.327395 | Stream observed median |
| fork | 1 | 61.234791 | 17.712667 | 61.701708 | 3.483479 | Tree observed median |
| source-document | 1 | 50.715333 | 23.902167 | 31.467833 | 1.316526 | Tree observed median |
| identity | 100 | 4764.436000 | 2302.924667 | 2997.399125 | 1.301562 | Tree observed median |
| adjacent | 100 | 1129.268916 | 1798.267667 | 1151.102875 | 0.640118 | Stream observed median |
| accumulator | 100 | 4185.497250 | 6664.934750 | 4404.317292 | 0.660819 | Stream observed median |
| iterate | 100 | 1358.782375 | 1732.197667 | 1330.753708 | 0.768246 | Stream observed median |
| merge | 100 | 4546.305750 | 0/3 valid | 4068.536417 | — | No meaningful winner |
| fork | 100 | 5921.473333 | 1825.198917 | 5949.591792 | 3.259695 | Tree observed median |
| source-document | 100 | 5011.438875 | 2414.419667 | 3128.917000 | 1.295929 | Tree observed median |
| identity | 1000 | 49239.938167 | 26367.770000 | 31304.954875 | 1.187243 | Tree observed median |
| adjacent | 1000 | 11492.868167 | 0/3 valid | 12217.078166 | — | No meaningful winner |
| accumulator | 1000 | 43167.917334 | 0/3 valid | 43640.809083 | — | No meaningful winner |
| iterate | 1000 | 13223.421083 | 36486.410500 | 14552.146791 | 0.398837 | Stream observed median |
| merge | 1000 | 45146.081417 | 0/3 valid | 45404.244416 | — | No meaningful winner |
| fork | 1000 | 61351.591709 | 0/3 valid | 62862.834958 | — | No meaningful winner |
| source-document | 1000 | 51393.275792 | 26392.932959 | 30801.247417 | 1.167026 | Tree observed median |

Previous/current version columns are separate runs, not a paired cross-version significance claim. The current stream/tree ratios use the same current artifact and three valid pairs. Winner labels have a descriptive 5% band; the throughput gate uses exactly 1.0. The streaming target and fork gap remain open.

| Incremental workload | Current 1 MB first ms / input bytes | Current 100 MB first ms / input bytes | Current 1 GB first ms / input bytes |
|---|---:|---:|---:|
| identity | 0.260959 / 8192 | 0.214292 / 8192 | 0.262917 / 8192 |
| adjacent | 4.267875 / 356352 | 4.384083 / 356352 | 4.508750 / 356352 |
| merge | 5.270917 / 122880 | 5.641333 / 122880 | 5.407541 / 122880 |
| fork | 2.355500 / 36864 | 2.312416 / 36864 | 2.452875 / 36864 |
| source-document | 0.281375 / 8192 | 0.321250 / 8192 | 0.282625 / 8192 |

First-output observations are not a quantitative latency exit. Accumulator/iterate final totals require end-of-input. Source/fixture/controller hashes, frozen binaries, exact command configuration and all stdout/stderr are under `/tmp/xsl3-p7-copy-content-process-benchmark/`. The terminal source audit covers 409 files and has no differences. Native memory matrices above remain separate.

## Final ordinary performance gate

The final ordinary gate exits **0** over exactly three pairs against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`: timed geometric mean **1.009512334383**, maximum allocation ratio **1.043895720117**. The same current ordinary micro harness is built on both sides with its separate process host excluded equally. It starts only after the full streaming controller and all task-owned functional/native jobs are terminal. It was run once for these final production changes.

| Ordinary metric | Base ms | Head ms | Head/base | Winner |
|---|---:|---:|---:|---|
| grouping cold | 146.021500 | 155.120600 | 1.062313 | Base observed median |
| sorting compile | 0.510000 | 0.633300 | 1.241765 | Base observed median |
| join compile | 0.095500 | 0.101200 | 1.059686 | Base observed median |
| regex 1kb | 0.004700 | 0.005500 | 1.170213 | Base observed median |

Cold time is reported, not included in the timed geometric mean; its allocation is included in the gate. Descriptive labels do not establish significance. Individual regressions remain attribution work and are not declared fixed by an aggregate pass. Exact round observations:

- grouping cold: base **146.021500/145.022200/158.426900 ms**, head **155.120600/157.437400/153.686200 ms**.
- sorting compile: base **0.475550/0.510000/0.603250 ms**, head **0.633300/0.635450/0.622500 ms**.
- join compile: base **0.095100/0.095500/0.101000 ms**, head **0.095800/0.101200/0.103300 ms**.
- regex 1kb: base **0.004700/0.004700/0.004700 ms**, head **0.005600/0.005500/0.005400 ms**.

Evidence is `/tmp/xsl3-p7-copy-content-final-perf.log`, `-perf-exit.txt` and `-perf-data/summary.json` plus exactly rounds 1–3. Stale rounds 4/5 in shared artifacts are excluded. The gate removes its temporary base checkout. This is not a streaming throughput or exact push-memory pass. The goal remains active; no commit, push, publication or deployment was performed.
