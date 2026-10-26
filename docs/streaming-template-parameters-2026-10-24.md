# Forward template parameters and debug focus, 2026-10-24

The measurements below cover the frozen template-parameter checkpoint. The subsequent
[arithmetic/focus increment](streaming-arithmetic-focus-2026-10-24.md) supersedes that source;
these artifacts and hash comparisons remain evidence for their own recorded checkpoint.

A streamed node passed to an atomic `xsl:call-template` parameter was collected into the argument list
before the callee atomized it. The node had expired by then. A minimal two-row reproduction compares
ordinary and forward execution through both API paths; fourteen of eighteen cases failed on each
framework before the repair, including canonical conversion failures replaced by `stream-position`.
The first fixture omitted an unused namespace from `exclude-result-prefixes`, masking some lifetime
failures with formatting differences. The corrected before-run is the failure baseline.

Template parameter types are now resolved in the declaration's namespace scope while compiling their
headers. This also fixes type prefixes declared directly on `xsl:param`, which the callee previously
resolved under the enclosing template's namespaces. A resolved atomic target allows the caller to
atomize each selected node before advancing its iterator. The callee retains canonical conversion,
cardinality and XTTE0590 checks. Node and tunnel argument types retain their contracts; this is not a
blanket grounding operation or a change to tunnel-value semantics.

An explicit node type on `xsl:with-param` exposed a second eager boundary: its own type conversion
collected the nodes before the enclosing atomic adaptation could use them. Four additional cases
failed before this correction. Node-only argument checks now reuse the existing incremental type and
cardinality matcher. The argument's own checks complete before the callee body executes. Atomic
`xsl:with-param` conversions retain their canonical path; a string conversion cannot be bypassed to
allow an otherwise invalid decimal argument. No public helper or saved-data version is added.

## Declaration errors remain errors

Forward-compatible compilation can defer a type-resolution error until execution. Review found that
an invalid type could be resolved again under an outer namespace binding and incorrectly succeed.
A version-4 stylesheet declares `t` as XML Schema on the stylesheet, shadows it with an unknown type
namespace on a required parameter, and supplies an integer. Before the repair both API paths returned
`1`; the two regression cases failed because no exception was thrown. The declaration cache now retains
either its resolved type or its deferred error. The callee emits the captured error rather than resolving
the same spelling in another scope. Both API paths and a saved assembly now report XPST0051. The modern
API wraps this as `XsltDynamicException`; the legacy path retains its existing `XdmException` wrapper
for a deferred static code. The test checks the established wrappers and exact code.

## Debugger and focus repairs

Debug metadata requested `last()` even where the stylesheet needed only the current item or position.
That counted forward populations before their bodies ran. Streamed debug compilation now keeps the
same actual focus flags as normal compilation and does not force a context-size variable merely for
inspection. Position-only demand is kept separate from `last()` in the conservative stylesheet scan.
Ordinary compilation retains its prior full-focus policy. Explicit language-level context-size demands
remain; a grounded two-item population still passes position and size to called templates.

The focus scan remains conservative for explicit `last()` and dynamic lookup. A separate diagnostic
recorded `price + (1,2)[last()]` passed to an `xs:double` parameter returning `3.25` and `4.75` ordinarily
but raising `stream-position` forward. Subsequent isolation established that arithmetic collected the
price node before atomization; the conservative template-focus policy did not cause that failure.
The [arithmetic and focus repair](streaming-arithmetic-focus-2026-10-24.md) corrects that boundary and
separate XPath local-focus defects while retaining the template policy. The earlier evidence is
`/tmp/xsl3-p7-scoped-last-diagnostic/run-corrected.log`; its initial decimal-parameter attempt also had
a canonical double-to-decimal conversion error and is not the valid-result comparison. This diagnostic
changed no validated source. The position-only repair does not establish every remaining S1 exit.

The grounded context-size control exposed a pre-existing debug IL error on both frameworks and both
execution modes. The node-change hook expected a navigator but was emitted for atomic items without
a compatible argument. Generated code now invokes the existing hook only for node context items;
non-node items retain their debug variables and source sequence points. The preceding frozen engine
reproduces the invalid program, so this is separate from the atomic-argument repair.

A mixed atomic/map/array/function/node control then exposed a second pre-existing debug issue:
construction analysis changed the runtime function dispatcher's method to return `void`, while the
runtime binds a delegate returning `item()*`. Both old execution modes reproduce the delegate mismatch;
saved IL confirms the `void` signature. The dispatcher now keeps its iterator ABI regardless of whether
its body could otherwise be pushed to a writer. Existing runtime ABI and saved formats remain.

## Specification audit of merge failures

The Recommendation's [XTSE3195 rule](https://www.w3.org/TR/xslt-30/#err-XTSE3195) requires `streamable`
to be absent with `for-each-item`, including when its value would be `no`. Its
[merge-action focus rules](https://www.w3.org/TR/xslt-30/#element-merge-action) make context size absent
with a streamable source. Validation and XPDY0002 behavior are retained. Raw failures for merge-073,
merge-081, merge-082, si-merge-004ns and merge-084 remain recorded as suite/specification conflicts;
no baseline, expected result or implementation rule is relaxed to remove them.

## Shared process workload

The fourteenth workload, `template-parameter`, reads each generated row's child `value`, checks the
argument as `element(value)`, and calls a context-absent template whose parameter is `xs:integer`.
The caller outputs its actual `position()`, followed by the converted value. Its independent oracle
uses the generated record IDs and the same source-record definition; runtime values come from the
XML through the public transformation paths. The first-output probe checks emission before half the
source is read. All thirteen preceding checked-in corpus files remain byte-for-byte unchanged.
The shared generator now produces twenty-eight statically referenced entry points. Both exact native
memory matrices consequently contain forty-two children. Canonical throughput workloads remain seven;
the new workload is an additional regression workload. The protected standalone streaming benchmark
project is unchanged.

## Validation checkpoints

The initial full checkpoint passes Release and Debug units at **2,953 .NET 10 / 2,866 .NET 8**.
The focused suite and original strict named-template set pass. Main strict improves **2,670/41/1 to
2,671/40/1**, fixing `si-call-template/si-call-template-002`, with seven unmatched expected codes and
no new failure IDs. The initial fresh macOS NativeAOT consumer passes five new stylesheets and every
retained compatibility probe. Its first 42-child nightly/push matrices are retained under
`/tmp/xsl3-p7-template-parameter-linux/`.

Review then added the deferred declaration-error repair. The initial snapshots remain immutable, with
live-source differences explicitly recorded. Their queued performance driver was stopped before any
measurements began. Final-source functional, native, exact memory and isolated performance results
are complete and recorded below. No intermediate result is presented as validation
of the revised source.

The final source adds **43 .NET 10 / 36 .NET 8 cases**, including process-fixture coverage. Full Release
and Debug units pass **2,956 / 2,868**. The solution build, 46 .NET 10 W3C facts, 1,654 upstream cases
per framework, 394 differential cases per framework and all thirteen Python controller tests pass.
The final full audit has no new failure IDs in any profile:

| Audit | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| Ordinary XSLT 3.0 | 8,847 | 21 | 41 | 34 |
| Streaming development, ordinary execution | 2,691 | 20 | 1 | 5 |
| Streaming development, strict forward input | 2,671 | 40 | 1 | 7 |
| Ordinary schema-aware | 843 | 1 | 2 | 6 |
| Strict schema-aware | 842 | 2 | 2 | 6 |
| QT3 | 21,884 | 0 | 99 | 67 |

`docs/conformance-sets.md` is regenerated from the final reports. Main strict retains its failing audit
status. The final macOS arm64 NativeAOT consumer passes all six new stylesheets and every retained
correctness/compatibility probe, including the immutable version-2 fork fixture. Its publish has no
trim/AOT warnings, normal globalization is retained, and all 395 frozen source hashes match.

Final-source evidence locations:

- `/tmp/xsl3-p7-template-parameter-before-corrected.log`: corrected failing lifetime reproductions.
- `/tmp/xsl3-p7-template-parameter-own-type-before.log`: four failing explicit argument-type cases.
- `/tmp/xsl3-p7-grounded-debug-before/`: preceding-engine invalid-IL reproductions and saved IL.
- `/tmp/xsl3-p7-mixed-debug-before/` and `/tmp/xsl3-p7-mixed-debug-diagnostic/`: preceding-engine
  delegate failures and the saved dispatcher's `void` signature.
- `/tmp/xsl3-p7-template-type-error-before/` and `/tmp/xsl3-p7-template-type-error-test-before.log`:
  incorrect namespace fallback and its two failing regressions per framework.
- `/tmp/xsl3-p7-template-parameter-final-focused.log`: all final parameter/focus/saved cases.
- `/tmp/xsl3-p7-template-parameter-final-verified-*`: complete functional/conformance checks.
- `/tmp/xsl3-p7-template-parameter-final-reports/` and `/tmp/xsl3-p7-template-parameter-conformance-delta.json`: frozen reports and exact profile/failure comparisons.
- `/tmp/xsl3-p7-template-parameter-final-native/`: six new saved normal/debug/error stylesheets,
  retained compatibility probes, frozen source and NativeAOT logs.
- `/tmp/xsl3-p7-template-parameter-final-linux/`: both final exact native memory matrices.
- `/tmp/xsl3-p7-template-parameter-process-benchmark/`: final isolated ten-workload 100 MB matrix.
- `/tmp/xsl3-p7-template-parameter-final-perf*`: final ordinary three-pair gate against `0f2ddcb`.
- `/tmp/xsl3-p7-template-parameter-corpus-check.json`: unchanged hashes of the thirteen prior fixtures.


## Final-source exact native memory matrices

The fresh local Linux arm64 NativeAOT nightly matrix is complete: **42 children, 0 failures** at exact 2 GiB input / 200 MiB absolute RSS. All fourteen measured streams consume exactly 2,147,483,648 bytes and validate their independent output fingerprints. Effective managed allowance is 209,715,200 bytes. Both native-evidence flags are false; normal ICU is enabled. RSS is the greater child/outer wait4 full-lifetime counter, including startup and warmup. Tree baselines are separate and uncapped.

| Workload | Full-lifetime peak RSS bytes | Live managed bytes | Diagnostic elapsed ms | First output ms | Input at first output |
|---|---:|---:|---:|---:|---:|
| identity | 29691904 | 381952 | 54483.790691 | 0.390042 | 8192 |
| adjacent | 27672576 | 362352 | 13644.577631 | 7.223291 | 356352 |
| accumulator | 30617600 | 362184 | 52053.581649 | 52053.444149 | 2147483648 |
| iterate | 28774400 | 418232 | 13963.210174 | 13963.183257 | 2147483648 |
| merge | 31154176 | 406216 | 54061.792315 | 4.949000 | 122880 |
| fork | 30883840 | 367448 | 228605.056771 | 5.251750 | 36864 |
| source-document | 29585408 | 384688 | 52283.158023 | 0.449292 | 8192 |
| climbing-union | 30695424 | 361728 | 45617.506229 | 1.775667 | 53248 |
| climbing-intersection | 30695424 | 361736 | 42849.349061 | 7.233333 | 204800 |
| type-assertion | 26624000 | 359480 | 10616.882171 | 0.416833 | 36864 |
| type-inspection | 26546176 | 359544 | 10540.280630 | 10540.267255 | 2147483648 |
| copy-select | 29810688 | 381992 | 51389.276314 | 0.426208 | 8192 |
| conditional-content | 29855744 | 378448 | 32792.031557 | 0.153125 | 8192 |
| template-parameter | 30679040 | 371800 | 20121.943509 | 2.169000 | 122880 |

These native timings are diagnostic: functional checks overlapped part of the matrix. They are not paired throughput comparisons. Accumulator/iterate/type-inspection results require end-of-input.

The exact 200 MiB-input / 20 MiB RSS push matrix completes all **42 children**, retaining **14 failures**. No startup/warmup or baseline is subtracted.

| Workload | Status | Measured input bytes | Full-lifetime peak RSS bytes |
|---|---|---:|---:|
| identity | budget-exceeded | 209715200 | 25530368 |
| adjacent | budget-exceeded-before-measurement | 0 | 22237184 |
| accumulator | budget-exceeded-before-measurement | 0 | 21311488 |
| iterate | budget-exceeded-before-measurement | 0 | 22609920 |
| merge | budget-exceeded-before-measurement | 0 | 21540864 |
| fork | budget-exceeded-before-measurement | 0 | 21409792 |
| source-document | budget-exceeded | 209715200 | 25788416 |
| climbing-union | budget-exceeded-before-measurement | 0 | 21405696 |
| climbing-intersection | budget-exceeded-before-measurement | 0 | 21385216 |
| type-assertion | budget-exceeded | 209715200 | 25649152 |
| type-inspection | budget-exceeded | 209715200 | 25718784 |
| copy-select | budget-exceeded | 209715200 | 25501696 |
| conditional-content | budget-exceeded | 209715200 | 26066944 |
| template-parameter | budget-exceeded-before-measurement | 0 | 21364736 |

Evidence is `/tmp/xsl3-p7-template-parameter-final-linux/`: both complete reports, frozen source/SHA-256 snapshot, `source-check.json`, `run.sh`, publish/run logs, native-intermediate rejection and `measured-summary.json`. The cached official SDK image is `70b5de4a1846`, .NET SDK 10.0.400 / runtime 10.0.11. This is local arm64 evidence; remote linux-x64 CI remains unverified. The nightly pass does not close the exact push budget or the full Phase 7 goal.


## Isolated 100 MB process comparison

The controller completes **60 trials**: **30 valid streams**, **27 valid tree trials**, **3 failed trials**. Controller exit is **1**; the canonical throughput target reports **incomplete coverage**. Each side uses the same current source and corpus, equal 4 GiB managed limits, tiering disabled and three fresh alternating pairs. This matrix measures decimal 100 MB only. It does not replace the earlier three-size matrix or establish a full throughput/latency exit.

| Workload | Tree, current snapshot ms | Stream, current snapshot ms | Stream/tree | Winner |
|---|---:|---:|---:|---|
| identity | 2594.470083 | 2942.150125 | 1.134008 | Tree observed median |
| adjacent | 1783.380375 | 1136.669375 | 0.637368 | Stream observed median |
| accumulator | 6047.424125 | 4124.767334 | 0.682070 | Stream observed median |
| iterate | 1580.938625 | 1289.258625 | 0.815502 | Stream observed median |
| merge | No valid run | 3666.519916 | Not comparable | No valid comparison |
| fork | 1744.130459 | 5899.139875 | 3.382281 | Tree observed median |
| source-document | 2257.757667 | 2926.751042 | 1.296309 | Tree observed median |
| copy-select | 2271.225833 | 2917.156792 | 1.284398 | Tree observed median |
| conditional-content | 2225.919500 | 2033.767833 | 0.913675 | Stream observed median |
| template-parameter | 1960.900209 | 1761.378541 | 0.898250 | Stream observed median |

A failed or incomplete comparison has no ratio or winner. Merge stream diagnostics are medians of its three valid raw stream trials; they do not establish a comparison. Observed median winners do not quantify uncertainty or demonstrate a causal improvement against the preceding snapshot. All trial output hashes, raw timings, failures, first-output measurements and runtime/artifact hashes are retained in the report.

| Workload | Stream first-output median ms | Input bytes at first output |
|---|---:|---:|
| identity | 0.204833 | 8192 |
| adjacent | 4.204167 | 356352 |
| accumulator | 4124.761500 | 100000000 |
| iterate | 1289.243792 | 100000000 |
| merge | 4.974250 | 122880 |
| fork | 2.251125 | 36864 |
| source-document | 0.247125 | 8192 |
| copy-select | 0.211375 | 8192 |
| conditional-content | 0.201458 | 8192 |
| template-parameter | 2.163250 | 122880 |

All 426 source/fixture/controller hashes and the assembly, engine and controller hashes match. Runtime: .NET 10.0.11, macOS 27.0.1, Arm64. This is a managed process comparison; native absolute RSS is assessed separately.


## Final ordinary three-pair gate

The gate exits **0** against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` over exactly three alternating base/head pairs: timing geometric mean **0.998328693944**, maximum allocation ratio **1.045944253838** (grouping, cold; **1,597,240/1,670,624 bytes**). Cold time is excluded from the 39-row timed mean; cold allocation is included. The same ordinary harness/corpus runs on both versions with the separate process host excluded equally.

The gate ran once after the complete 60-trial matrix and all task-owned functional/native CPU jobs finished. Its temporary base worktree is removed. An aggregate pass does not establish fixes for preceding individual outliers.

| Workload | Size | Base 0f2ddcb ms | Current branch ms | Head/base | Base allocated bytes | Current allocated bytes | Allocation ratio | Winner |
|---|---|---:|---:|---:|---:|---:|---:|---|
| grouping | cold | 133.126100 | 140.889700 | 1.058318 | 1597240 | 1670624 | 1.045944 | Base observed median |
| grouping | compile | 0.624000 | 0.634700 | 1.017147 | 186811 | 191350 | 1.024297 | No meaningful winner (5% band) |
| sorting | compile | 0.568700 | 0.604600 | 1.063126 | 182433 | 186858 | 1.024255 | Base observed median |
| keys | compile | 0.521700 | 0.458300 | 0.878474 | 194230 | 199694 | 1.028132 | Current observed median |
| join | compile | 0.089600 | 0.093800 | 1.046875 | 150576 | 153772 | 1.021225 | No meaningful winner (5% band) |
| regex | compile | 0.082100 | 0.082300 | 1.002436 | 165108 | 168235 | 1.018939 | No meaningful winner (5% band) |
| strings | compile | 0.083300 | 0.084000 | 1.008403 | 177988 | 180601 | 1.014681 | No meaningful winner (5% band) |
| maps | compile | 0.157700 | 0.158600 | 1.005707 | 285535 | 291909 | 1.022323 | No meaningful winner (5% band) |
| fold-left | compile | 0.181200 | 0.181200 | 1.000000 | 305117 | 308701 | 1.011746 | No meaningful winner (5% band) |
| iterate | compile | 0.148400 | 0.148000 | 0.997305 | 238285 | 242413 | 1.017324 | No meaningful winner (5% band) |
| identity | compile | 0.043900 | 0.044500 | 1.013667 | 97900 | 99371 | 1.015026 | No meaningful winner (5% band) |
| dispatch | compile | 1.579000 | 1.605150 | 1.016561 | 4004366 | 4117626 | 1.028284 | No meaningful winner (5% band) |
| recursion | compile | 0.119200 | 0.120900 | 1.014262 | 237817 | 246673 | 1.037239 | No meaningful winner (5% band) |
| json | compile | 0.066800 | 0.067100 | 1.004491 | 156508 | 158288 | 1.011373 | No meaningful winner (5% band) |
| grouping | 1kb | 0.002500 | 0.002500 | 1.000000 | 13114 | 13183 | 1.005262 | No meaningful winner (5% band) |
| sorting | 1kb | 0.002300 | 0.002200 | 0.956522 | 12206 | 12293 | 1.007128 | No meaningful winner (5% band) |
| keys | 1kb | 0.004500 | 0.004600 | 1.022222 | 17469 | 17556 | 1.004980 | No meaningful winner (5% band) |
| join | 1kb | 0.003600 | 0.003700 | 1.027778 | 14120 | 14192 | 1.005099 | No meaningful winner (5% band) |
| regex | 1kb | 0.004700 | 0.005600 | 1.191489 | 15613 | 15689 | 1.004868 | Base observed median |
| strings | 1kb | 0.003300 | 0.003300 | 1.000000 | 12830 | 12902 | 1.005612 | No meaningful winner (5% band) |
| maps | 1kb | 0.006500 | 0.006600 | 1.015385 | 27221 | 27365 | 1.005290 | No meaningful winner (5% band) |
| fold-left | 1kb | 0.004300 | 0.004200 | 0.976744 | 18803 | 18963 | 1.008509 | No meaningful winner (5% band) |
| iterate | 1kb | 0.004200 | 0.004200 | 1.000000 | 23088 | 23203 | 1.004981 | No meaningful winner (5% band) |
| identity | 1kb | 0.002900 | 0.003000 | 1.034483 | 10810 | 10884 | 1.006846 | No meaningful winner (5% band) |
| dispatch | 1kb | 0.008000 | 0.007500 | 0.937500 | 23593 | 23665 | 1.003052 | Current observed median |
| recursion | 1kb | 0.003700 | 0.003600 | 0.972973 | 17569 | 17650 | 1.004610 | No meaningful winner (5% band) |
| json | 1kb | 0.005800 | 0.006000 | 1.034483 | 35036 | 35161 | 1.003568 | No meaningful winner (5% band) |
| grouping | 1mb | 0.844100 | 0.836500 | 0.990996 | 1985614 | 1985686 | 1.000036 | No meaningful winner (5% band) |
| sorting | 1mb | 2.073000 | 2.124700 | 1.024940 | 3410835 | 3428508 | 1.005181 | No meaningful winner (5% band) |
| keys | 1mb | 3.778600 | 3.576500 | 0.946515 | 6152455 | 6152541 | 1.000014 | Current observed median |
| join | 1mb | 2.653800 | 2.336250 | 0.880341 | 4525269 | 4525339 | 1.000015 | Current observed median |
| regex | 1mb | 5.937900 | 5.775050 | 0.972574 | 13071945 | 13072017 | 1.000006 | No meaningful winner (5% band) |
| strings | 1mb | 2.942500 | 2.853750 | 0.969839 | 5462928 | 5463000 | 1.000013 | No meaningful winner (5% band) |
| maps | 1mb | 4.131100 | 4.121550 | 0.997688 | 10118234 | 10168481 | 1.004966 | No meaningful winner (5% band) |
| fold-left | 1mb | 2.873200 | 2.780750 | 0.967823 | 7319896 | 7443143 | 1.016837 | No meaningful winner (5% band) |
| iterate | 1mb | 1.217200 | 1.203150 | 0.988457 | 3114678 | 3132471 | 1.005713 | No meaningful winner (5% band) |
| identity | 1mb | 1.927300 | 1.959500 | 1.016707 | 3154312 | 3154384 | 1.000023 | No meaningful winner (5% band) |
| dispatch | 1mb | 1.360650 | 1.368600 | 1.005843 | 2650127 | 2650199 | 1.000027 | No meaningful winner (5% band) |
| recursion | 1mb | 1.251800 | 1.214700 | 0.970363 | 2781849 | 2781929 | 1.000029 | No meaningful winner (5% band) |
| json | 1mb | 9.189500 | 9.279250 | 1.009767 | 18294347 | 18346954 | 1.002876 | No meaningful winner (5% band) |

Winner labels use a 5% diagnostic band and do not replace an uncertainty test. All exact base/head round files, per-row medians, allocations and round timings are retained in `/tmp/xsl3-p7-template-parameter-final-perf-data/`. Only rounds 1–3 are used; stale repository artifact rounds are excluded.

Individual timed rows above 1.05 remain observed regressions requiring attribution:

- **sorting compile**: ratio **1.063126428697**, base/current medians **0.568700/0.604600 ms**. Base rounds `[0.5936, 0.46315, 0.5687]`; current rounds `[0.58155, 0.6296, 0.6046]`. Allocation ratio **1.024255480094**.
- **regex 1kb**: ratio **1.191489361702**, base/current medians **0.004700/0.005600 ms**. Base rounds `[0.0047, 0.0047, 0.0047]`; current rounds `[0.0056, 0.0056, 0.0055]`. Allocation ratio **1.004867738423**.

Earlier individual outliers, including dispatch compilation, are not declared fixed by their absence from this run. No extra performance run was selected to replace these results.


The full Phase 7 goal remains active, including remaining runtime/S1/error-code failures, exact push
RSS, complete throughput/latency requirements, remote Linux x64 CI and the feature claim. Streaming
support is not claimed. No commit or push is performed.
