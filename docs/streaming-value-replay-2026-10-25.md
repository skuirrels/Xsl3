# Overlapping descendant values, 2026-10-25

This is the value-replay checkpoint before the subsequent node-set and mode fixes. See
[the follow-up](streaming-set-merge-mode-2026-10-25.md) for current validation. Exact native memory
tables below refer to the frozen value-replay source.


Value consumers now retain nested descendant matches when atomizing an ancestor advances past them.
For example, `<n><n>1</n><n>2</n><n>3</n></n><n>4</n>` yields `123|1|2|3|4` from a descendant string join,
and `133` from its sum. The old forward cursor reported `stream-position` after absorbing the first
ancestor. All seven initial regression cases failed before the implementation.

## Consumer proof and runtime behavior

The compiler identifies unfiltered child/descendant paths and eligible sequences containing grounded
values. Atomization, string conversion, numeric aggregation, simple content and AVT lowering prepare
these operands through the additive `X31ValueOperand` runtime helper. Child-only populations avoid the
helper. Ordinary compilation keeps its existing expression dispatcher.

A prepared navigator identifies the exact descendant cursor that produced it. Reading its value
allows that cursor to preserve nested matching headers and character ranges while the reader
advances. Other active cursors retain their existing movement checks. The source position resumes
after the absorbed subtree when preserved matches have been returned in document order.

Each replay store uses two invocation-local temporary files: matching headers in document order and
native UTF-16 character ranges. Open matching nodes alone remain on the capture stack. Historical
headers retain original node numbers, ancestor/namespace/attribute headers and final schema
information; values use the existing canonical atomization and conversion rules. The store contains
matching headers and character data rather than a navigable retained subtree.

The document reuses one spare store between sequential windows. File storage reuses its high-water
space, and range generations prevent stale positions from reading a later window. Generations use
64 bits, matching source-node numbering. Value reads rent a shared byte buffer and preserve UTF-16
code units, including surrogate pairs, without allocating a reader buffer for each value.

## Ownership and operational requirements

Overlapping values require a writable OS temporary directory. Unix files are created atomically with
owner-read/write mode; the normal Windows sharing contract is exclusive. Delete-on-close handles are
owned by the source document, including fork views. Normal completion, early abandonment, invalid
input and cancellation release them; completed cursors return a reset store to the document's pool.

The permission regression demonstrated `0644` files before hardening and passes with owner-only
creation. The earlier Linux snapshot was deliberately stopped after 17 completed children when this
hardening and 64-bit generations changed the source. It is an incomplete checkpoint, preserved with
its terminal Docker exit 137 and `termination-reason.json`, not a memory-gate result.

Replay advancement checks the source document cancellation token as well as value reads. A regression
first demonstrated that advancing historical headers after cancellation returned a node; both paths now
throw cancellation. The preceding `value-replay-final` matrices and performance run are preserved as
checkpoints from before that correction. Their timings overlap later development checks and are not
the final isolated comparison.

## Tests and shared workload

Regression coverage includes both APIs, Debug, saved assemblies, sums, string joins, attributes and
AVTs, mixed-content boundaries, Unicode, typed lists, union members, nil and QName annotations.
A 2,000-member overlap exercises disk-backed header storage while retaining only the reader's open
frames. Other cases check file reuse, stale-range rejection, cancellation and invalid-input cleanup.
Raw forward navigation still rejects lost descendants; flat value populations create no spill files.

The shared probe adds `overlap-values`: each generated row atomizes its descendant `value`, `text` and
nested `em` elements, emits their values, and then emits the caller position. Its independent oracle
uses the generated input fields. Production execution reads the XML through the public APIs. The
first output occurs after the first row, and all fifteen preceding corpus stylesheets are byte-identical.
There are now sixteen workloads and thirty-two statically referenced saved entry points.

Four new normal/debug macOS NativeAOT styles exercise overlapping sums, simple content, AVTs and
attributes inside fork workers, alongside retained compatibility probes. The immutable version-2 fork
assembly remains unchanged. Evidence is in `/tmp/xsl3-p7-value-replay-owned-native/` and its separate
generator; the earlier pre-hardening native checkpoint remains separate.

## Functional and compatibility validation

Twenty .NET 10 / eighteen .NET 8 cases were added, including the shared workload checks. Full Debug
and Release units pass **3,060 on .NET 10 / 2,966 on .NET 8**. The solution, 46 W3C harness facts,
upstream 1,654 per framework, differential 394 per framework and thirteen controller tests pass.
Eight full profile reports have **no new failure IDs**. Main strict improves **2,672/39/1 to
2,680/31/1**, with seven unmatched expected codes unchanged. The eight fixed cases are:

- `sf-sum/sf-sum-056`
- `si-attribute/si-attribute-072`
- `si-attribute/si-attribute-073`
- `si-choose/si-choose-011`
- `si-value-of/si-value-of-027`
- `si-value-of/si-value-of-072`
- `si-value-of/si-value-of-073`
- `streamable/streamable-032`

| Profile | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| XSLT 3.0 | 8847 | 21 | 41 | 34 |
| XSLT 2.0 | 4588 | 9 | 11 | 7 |
| Streaming, unstreamed execution | 2691 | 20 | 1 | 5 |
| Streaming, strict inputs | 2680 | 31 | 1 | 7 |
| Posture/sweep | 790 | 129 | 0 | 0 |
| Schema aware | 843 | 1 | 2 | 6 |
| Schema aware, strict inputs | 842 | 2 | 2 | 6 |
| QT3 | 21884 | 0 | 99 | 67 |

The final NativeAOT consumer and retained compatibility probes pass without trim/AOT warnings. All
**400 current/frozen core/configuration hashes** match. Its four new saved styles cover normal/debug
forked sums, simple content, AVTs and attributes; the immutable version-2 fork assembly remains
SHA-256 `89ba201ad9e21c7a3a4add5231c114f5aee9a2f9eef4bdd9472a41e680b5caee`. The existing saved-data
version and navigator helper ABI remain. `X31ValueOperand` is an additive runtime helper.

Evidence: `/tmp/xsl3-p7-value-replay-owned-verified-regressions.log`, its individual logs and terminal
exit `0`; frozen `/tmp/xsl3-p7-value-replay-owned-reports/`, including `profile-comparison.json`; and
`/tmp/xsl3-p7-value-replay-owned-native/`, including source/artifact hashes, generation/publish/run logs
and terminal driver exit `0`. The generated conformance tables use these completed reports. No
conformance baseline was changed for this increment.


## Final-source exact native memory matrices

The fresh local Linux arm64 NativeAOT nightly matrix is complete: **48 children, 0 failures** at exact 2 GiB input / 200 MiB absolute RSS. All sixteen measured streams consume exactly 2,147,483,648 bytes and validate their independent output fingerprints. Effective managed allowance is 209,715,200 bytes. Both native-evidence flags are false; normal ICU is enabled. RSS is the greater child/outer wait4 full-lifetime counter, including startup and warmup. Tree baselines are separate and uncapped.

| Workload | Full-lifetime peak RSS bytes | Live managed bytes | Diagnostic elapsed ms | First output ms | Input at first output |
|---|---:|---:|---:|---:|---:|
| identity | 29224960 | 383424 | 54818.149484 | 0.623792 | 8192 |
| adjacent | 27521024 | 363824 | 13408.556090 | 3.281834 | 356352 |
| accumulator | 30662656 | 363656 | 56166.256567 | 56166.235567 | 2147483648 |
| iterate | 28770304 | 419704 | 14968.130924 | 14968.110965 | 2147483648 |
| merge | 31002624 | 407688 | 56138.090483 | 5.416542 | 122880 |
| fork | 30871552 | 368920 | 225584.177602 | 5.363333 | 36864 |
| source-document | 29777920 | 386160 | 52613.421981 | 0.296500 | 8192 |
| climbing-union | 30666752 | 363200 | 44989.545188 | 3.673000 | 53248 |
| climbing-intersection | 30670848 | 363208 | 43646.229478 | 7.586416 | 204800 |
| type-assertion | 26664960 | 360952 | 10511.010588 | 0.377625 | 36864 |
| type-inspection | 26542080 | 361016 | 7429.570253 | 7429.554753 | 2147483648 |
| copy-select | 29507584 | 383464 | 53291.161191 | 0.572833 | 8192 |
| conditional-content | 29798400 | 379920 | 33624.484723 | 0.369542 | 8192 |
| template-parameter | 30715904 | 373272 | 20052.864509 | 2.197000 | 122880 |
| function-parameter | 30769152 | 373144 | 33092.062765 | 3.529792 | 122880 |
| overlap-values | 30961664 | 382512 | 103157.691880 | 1.944458 | 24576 |

These native timings are diagnostic: functional checks overlapped part of the matrix. They are not paired throughput comparisons. Accumulator/iterate/type-inspection results require end-of-input.

The exact 200 MiB-input / 20 MiB RSS push matrix completes all **48 children**, retaining **16 failures**. No startup/warmup or baseline is subtracted.

| Workload | Status | Measured input bytes | Full-lifetime peak RSS bytes |
|---|---|---:|---:|
| identity | budget-exceeded | 209715200 | 25899008 |
| adjacent | budget-exceeded-before-measurement | 0 | 22228992 |
| accumulator | budget-exceeded-before-measurement | 0 | 21274624 |
| iterate | budget-exceeded-before-measurement | 0 | 22491136 |
| merge | budget-exceeded-before-measurement | 0 | 21561344 |
| fork | budget-exceeded-before-measurement | 0 | 21536768 |
| source-document | budget-exceeded | 209715200 | 25063424 |
| climbing-union | budget-exceeded-before-measurement | 0 | 21401600 |
| climbing-intersection | budget-exceeded-before-measurement | 0 | 21446656 |
| type-assertion | budget-exceeded | 209715200 | 25591808 |
| type-inspection | budget-exceeded | 209715200 | 25554944 |
| copy-select | budget-exceeded | 209715200 | 25681920 |
| conditional-content | budget-exceeded | 209715200 | 26042368 |
| template-parameter | budget-exceeded-before-measurement | 0 | 21245952 |
| function-parameter | budget-exceeded-before-measurement | 0 | 21397504 |
| overlap-values | budget-exceeded-before-measurement | 0 | 21409792 |

Evidence is `/tmp/xsl3-p7-value-replay-owned-linux/`: both complete reports, frozen source/SHA-256 snapshot, `source-check.json`, `run.sh`, publish/run logs, native-intermediate rejection and `measured-summary.json`. The cached official SDK image is `70b5de4a1846`, .NET SDK 10.0.400 / runtime 10.0.11. This is local arm64 evidence; remote linux-x64 CI remains unverified. The nightly pass does not close the exact push budget or the full Phase 7 goal.


All **553 current/frozen source/controller/fixture hashes** match. The native source includes the
owner-only Unix creation mode, 64-bit generations and the replay-header cancellation check.

## Ordinary performance gate

One gate run exits **0** against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` over exactly three alternating pairs. Timing geometric mean is **1.029701487659**; maximum allocation ratio is **1.046810748541** (grouping cold, 1,597,240/1,672,008 bytes). Cold time is excluded from the 39-row timed mean; cold allocation is included. Cold time ratio is **1.070781730658** (135.361200/144.942300 ms).

The same ordinary harness and corpus runs on both versions with the separate process host excluded equally. All task-owned functional/native CPU jobs were terminal before this comparison. The temporary base worktree is removed. This compares the complete working tree with the base; it does not isolate the causal effect of this increment.

| Workload | Size | Base 0f2ddcb ms | Current branch ms | Head/base | Base allocated bytes | Current allocated bytes | Allocation ratio | Winner |
|---|---|---:|---:|---:|---:|---:|---:|---|
| grouping | cold | 135.361200 | 144.942300 | 1.070782 | 1597240 | 1672008 | 1.046811 | Base observed median |
| grouping | compile | 0.629700 | 0.608500 | 0.966333 | 186810 | 191471 | 1.024950 | No meaningful winner (5% band) |
| sorting | compile | 0.512100 | 0.576400 | 1.125561 | 182431 | 186793 | 1.023910 | Base observed median |
| keys | compile | 0.354350 | 0.481600 | 1.359108 | 194239 | 199584 | 1.027518 | Base observed median |
| join | compile | 0.090300 | 0.099900 | 1.106312 | 150630 | 154127 | 1.023216 | Base observed median |
| regex | compile | 0.081700 | 0.083750 | 1.025092 | 165112 | 168231 | 1.018890 | No meaningful winner (5% band) |
| strings | compile | 0.082800 | 0.086500 | 1.044686 | 177988 | 180666 | 1.015046 | No meaningful winner (5% band) |
| maps | compile | 0.155000 | 0.160600 | 1.036129 | 285533 | 291880 | 1.022229 | No meaningful winner (5% band) |
| fold-left | compile | 0.180900 | 0.183700 | 1.015478 | 305117 | 308701 | 1.011746 | No meaningful winner (5% band) |
| iterate | compile | 0.144500 | 0.151300 | 1.047059 | 238285 | 242413 | 1.017324 | No meaningful winner (5% band) |
| identity | compile | 0.044200 | 0.045200 | 1.022624 | 97900 | 99372 | 1.015036 | No meaningful winner (5% band) |
| dispatch | compile | 1.533750 | 2.269800 | 1.479902 | 4004366 | 4117626 | 1.028284 | Base observed median |
| recursion | compile | 0.119400 | 0.122600 | 1.026801 | 237817 | 246673 | 1.037239 | No meaningful winner (5% band) |
| json | compile | 0.066200 | 0.068400 | 1.033233 | 156509 | 158367 | 1.011872 | No meaningful winner (5% band) |
| grouping | 1kb | 0.002500 | 0.002500 | 1.000000 | 13113 | 13183 | 1.005338 | No meaningful winner (5% band) |
| sorting | 1kb | 0.002300 | 0.002200 | 0.956522 | 12205 | 12293 | 1.007210 | No meaningful winner (5% band) |
| keys | 1kb | 0.004500 | 0.004500 | 1.000000 | 17468 | 17556 | 1.005038 | No meaningful winner (5% band) |
| join | 1kb | 0.003600 | 0.003600 | 1.000000 | 14120 | 14192 | 1.005099 | No meaningful winner (5% band) |
| regex | 1kb | 0.004700 | 0.005500 | 1.170213 | 15613 | 15688 | 1.004804 | Base observed median |
| strings | 1kb | 0.003300 | 0.003400 | 1.030303 | 12830 | 12902 | 1.005612 | No meaningful winner (5% band) |
| maps | 1kb | 0.006500 | 0.006500 | 1.000000 | 27221 | 27365 | 1.005290 | No meaningful winner (5% band) |
| fold-left | 1kb | 0.004300 | 0.004200 | 0.976744 | 18803 | 18963 | 1.008509 | No meaningful winner (5% band) |
| iterate | 1kb | 0.004200 | 0.004200 | 1.000000 | 23087 | 23203 | 1.005024 | No meaningful winner (5% band) |
| identity | 1kb | 0.002800 | 0.002900 | 1.035714 | 10810 | 10885 | 1.006938 | No meaningful winner (5% band) |
| dispatch | 1kb | 0.007200 | 0.007500 | 1.041667 | 23593 | 23665 | 1.003052 | No meaningful winner (5% band) |
| recursion | 1kb | 0.003600 | 0.003500 | 0.972222 | 17569 | 17650 | 1.004610 | No meaningful winner (5% band) |
| json | 1kb | 0.005900 | 0.005900 | 1.000000 | 35038 | 35175 | 1.003910 | No meaningful winner (5% band) |
| grouping | 1mb | 0.847850 | 0.833750 | 0.983370 | 1985614 | 1985686 | 1.000036 | No meaningful winner (5% band) |
| sorting | 1mb | 2.084700 | 2.149300 | 1.030988 | 3410835 | 3428508 | 1.005181 | No meaningful winner (5% band) |
| keys | 1mb | 3.695700 | 3.580300 | 0.968775 | 6152454 | 6152542 | 1.000014 | No meaningful winner (5% band) |
| join | 1mb | 2.467000 | 2.338700 | 0.947994 | 4525269 | 4525339 | 1.000015 | Current observed median |
| regex | 1mb | 5.753800 | 5.820650 | 1.011618 | 13071944 | 13072017 | 1.000006 | No meaningful winner (5% band) |
| strings | 1mb | 2.840500 | 2.810900 | 0.989579 | 5462928 | 5462999 | 1.000013 | No meaningful winner (5% band) |
| maps | 1mb | 4.032600 | 4.060100 | 1.006819 | 10123130 | 10165411 | 1.004177 | No meaningful winner (5% band) |
| fold-left | 1mb | 2.801000 | 2.641900 | 0.943199 | 7319895 | 7443142 | 1.016837 | Current observed median |
| iterate | 1mb | 1.225350 | 1.178700 | 0.961929 | 3114677 | 3132470 | 1.005713 | No meaningful winner (5% band) |
| identity | 1mb | 1.901900 | 1.945350 | 1.022846 | 3154328 | 3154385 | 1.000018 | No meaningful winner (5% band) |
| dispatch | 1mb | 1.369950 | 1.368900 | 0.999234 | 2650127 | 2650199 | 1.000027 | No meaningful winner (5% band) |
| recursion | 1mb | 1.197050 | 1.214600 | 1.014661 | 2781905 | 2781958 | 1.000019 | No meaningful winner (5% band) |
| json | 1mb | 9.587600 | 9.289350 | 0.968892 | 18294351 | 18346954 | 1.002875 | No meaningful winner (5% band) |

Winner labels use a 5% diagnostic band; they do not replace an uncertainty test. Exact round files and per-row medians/allocations are retained in `/tmp/xsl3-p7-value-replay-owned-perf-data/`. Only rounds 1–3 are used.

Timed rows above 1.05 remain observed regressions requiring attribution:

- **sorting compile**: ratio **1.125561413786**, base/current **0.512100/0.576400 ms**. Base rounds `[0.5121, 0.5921, 0.4965]`, current rounds `[0.57795, 0.46565, 0.5764]`. Allocation ratio **1.023910409963**.
- **keys compile**: ratio **1.359108226330**, base/current **0.354350/0.481600 ms**. Base rounds `[0.3556, 0.2786, 0.35435]`, current rounds `[0.4816, 0.30874999999999997, 0.5555]`. Allocation ratio **1.027517645787**.
- **join compile**: ratio **1.106312292359**, base/current **0.090300/0.099900 ms**. Base rounds `[0.0978, 0.0896, 0.0903]`, current rounds `[0.0999, 0.095, 0.1]`. Allocation ratio **1.023215826861**.
- **dispatch compile**: ratio **1.479902200489**, base/current **1.533750/2.269800 ms**. Base rounds `[1.53375, 1.6016, 1.4795]`, current rounds `[2.52765, 2.1295, 2.2698]`. Allocation ratio **1.028284127874**.
- **regex 1kb**: ratio **1.170212765957**, base/current **0.004700/0.005500 ms**. Base rounds `[0.0046, 0.0047, 0.0047]`, current rounds `[0.0055, 0.0055, 0.0055]`. Allocation ratio **1.004803689233**.

No additional performance run was selected to replace these results. Earlier individual outliers are not declared fixed by their absence. A fresh 100 MB process comparison over all sixteen workloads follows this isolated gate. It has its own raw trials and provenance; the earlier full three-size matrix remains evidence for its own snapshot. Throughput/latency exits remain open.


## Remaining scope

The full P7 goal remains active. The capture stack is bounded by nesting depth and the configured
schema vocabulary; character/header storage spills to disk. A materialized atomic value or final
string result can still grow with its value size. This does not establish constant memory for every
guaranteed-streamable expression.

Deferred predicates, arbitrary higher-order consumers and function-parameter continuations need
consumer-aware lowering before their historical nodes can safely expose runtime observer facts.
Existing explicit `copy-of()`/`snapshot()` replay retains its separate semantics. Remaining S1
catalogue/spec audits, strict failures and error-code mismatches, exact push RSS, throughput/latency,
schema-aware saved assemblies, remote Linux x64 CI and the final streaming feature claim remain open.
`supports-streaming` remains `no`.
