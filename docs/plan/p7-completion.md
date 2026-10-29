# P7 completion

The owner resumed the full P7 objective on 2026-10-29: complete Phase 7, review all changes,
merge the pull request and release. Branch: `feature/p7-completion`. Baseline: released
1.1.6, `588f2b6c7f050d47087f67b1fd2ef9630f6e19cf`.

This resumes the original [P7 plan](p7-streaming.md); the completed P7A–P7H increments and their
historical evidence are retained. An audit or another bounded increment does not complete this objective.

## Requirements and evidence

| Requirement | Evidence required before completion | Starting status |
|---|---|---|
| S0 owner decisions | Pull navigator, core assembly, Strict default, fork threads, both API paths, original CI budgets | Approved; preserve |
| S1 / S2 | Reviewed 1,000-case and 155-case gates, complete original raw inventories, exact diagnostics and original expression coverage | Previously passed; reverify after changes |
| S3 forward runtime | Complete selected strict case inventory; source-document, grounding, expression/function paths and optimizer/debug-verifier restrictions | Acceptance audit required |
| S4 streamable modes | Initial-mode and both API paths, global context, attribute sets, function categories and schema contracts | Acceptance audit required |
| S5 stateful constructs | Strict accumulator, iterate, grouping and merge coverage, including required merge errors | Acceptance audit required |
| S6 fork | Strict fork coverage, order/ownership, failure and cancellation propagation, measured tee bounds with payload assumptions | Acceptance audit required |
| Strict conformance | At least 90% on the applicable full streamed suite with guaranteed-streamable sources actually forwarded; report every result and exact-code limitation | Retained development baseline 2,695/16/1; not final acceptance |
| Push memory | Entire canonical 16-workload NativeAOT matrix: exact 209,715,200 input bytes under 20,971,520-byte absolute RSS and managed-heap guard | Previously failed |
| Nightly memory | Entire canonical 16-workload NativeAOT matrix: exact 2,147,483,648 input bytes under 209,715,200-byte absolute RSS and managed-heap guard | Earlier passes need current-source/hosted verification |
| Throughput | All seven canonical 100 MB stream/tree comparisons, at least three valid observations per mode, streaming median no greater than tree-plus-transform | Previously failed |
| First output | Actual sink writes, input consumed before emission and multi-size observations; document inherent end-of-input totals explicitly | Final qualification required |
| Compatibility | Full Release/Debug .NET 8/10 units, upstream, differential, ordinary W3C, saved and NativeAOT consumers, ordinary performance gate | Reverify final source |
| Public integration | Supported streaming selection through both APIs, saved-style behavior, feature-profile integration and truthful `supports-streaming` | Unsupported until qualification passes |
| Review / merge | Full diff reviewed, defects corrected, required hosted CI green for the reviewed head, merge verified | Pending |
| Release | README/package readmes/changelog/release notes updated, immutable tag on main, exact packages verified, public NuGet and GitHub assets plus fresh consumers verified | Pending |

Memory gates use unmodified generated inputs, independent output oracles and full-lifetime counters.
No startup subtraction, disabled globalization, diagnostic zero-limit run, omitted workload, relaxed
budget, incorrect expected output or fixture-dependent runtime branch can satisfy acceptance.

## Execution

1. Capture fresh per-case outcomes using the existing runner selection and pinned suite. Assign stage
   ownership without double-counting overlapping sets; retain both successful and unsuccessful cases.
2. Reassess the 16 retained failures from actual inputs, configuration and authoritative specification
   rules. Preserve the raw suite. Any reviewed correction must be separate, provenance-backed and
   independently checked, as with P7B; an existing disposition is not proof of correctness.
3. Correct established runtime/harness defects and fill missing S3–S6 evidence. Record remaining
   blockers explicitly and continue implementing; do not stop the full objective at audit completion.
4. Measure current native memory and canonical performance, address verified causes, and retain
   failed trials alongside successful runs. Run performance measurements in isolation from task-owned
   CPU-intensive verification; preserve measurement/artifact provenance.
5. Complete public streaming integration only when its actual execution and qualification gates pass.
   Review, validate, merge and release the resulting implementation; required owner NuGet deployment
   approval remains a release gate.

The goal stays active while any original requirement is incomplete or supported only by indirect evidence.

## Runtime inventory gate, 2026-10-29

The optional W3C runner export (`XSL3_XSLT30_RESULTS`) records the runner's actual selected cases;
it does not implement another selector. The initial complete Release run preserves **2,712 cases**:
**2,695 pass / 16 fail / 1 not run**, with zero unmatched expected error codes. The collection case
remains explicitly unsupported. Its inventory is frozen in [p7-runtime-inventory.json](p7-runtime-inventory.json)
against the clean `xslt30-test` commit `fddf1cf920087e791f13315d68dfbe874d97dc56`.

The inventory contains every selected case's identity, definition fingerprint and initial outcome,
plus shared test-set fingerprints and the suite revision. Improvements to failing cases do not require
regenerating it. Lost cases, fixture changes and regressions from a passing outcome fail validation.
Changing the population or suite pin requires explicit review; replacing the manifest is not a runtime fix.

`eng/audit-streaming-runtime.py` also rejects incomplete/duplicate records, inconsistent totals,
unmatched-code notes hidden in passing cases, filtered/non-strict runs and dirty fixture checkouts.
Checking the entire pinned checkout covers referenced stylesheet, input and schema files as well as
the exported XML definitions. The engine fingerprint can be checked against the actual tested DLL.
New output files are created exclusively; earlier evidence is not overwritten.

Primary stage ownership follows the original stage table. Stateful sets belong to S5, fork to S6,
streamable modes/functions and integration sets to S4, forward expressions/instructions to S3 and
the feature-property case to S7. Cross-cutting cases have one primary owner, so totals do not double-count
them. Ownership and outcomes do not independently prove all of a stage's behavioral contracts.

Example complete run and validation (use a fresh output name for each execution):

```sh
XSL3_XSLT30_PROFILE=strm XSL3_STREAM_STRICT=1 \
  XSL3_XSLT30_RESULTS="$PWD/artifacts/strict-runtime.jsonl" \
  dotnet test tests/Xsl3.W3C -c Debug --filter FullyQualifiedName~Xslt30Conformance
python3 eng/audit-streaming-runtime.py \
  --report artifacts/strict-runtime.jsonl --suite external/xslt30-test \
  --engine src/Xsl3/bin/Debug/net10.0/Xsl3.dll \
  --manifest docs/plan/p7-runtime-inventory.json --output artifacts/strict-runtime-audit.json
```

CI retains exports and audits for Release, Debug, and the legacy API with saved-assembly sampling.
The audit proves inventory preservation and reported outcomes. The strict execution settings, runtime
entry-point/lifetime tests and native probes remain separate evidence for actual forwarding; an inventory
audit is never a substitute for them or the S7 qualification gates.

### Debug optimizer discrepancy

The first full Debug export exposed an additional failure in `function/function-5014`: **2,694/17/1**.
Nested singleton filter calls inside a predicate triggered the preservation verifier because
`EliminateIsEmpty` added `Let` caches around an operand with side effects. A reduced regression reproduced
the failure with both stylesheet-debug settings on both .NET 8 and .NET 10.

The streamed optimizer now keeps the direct emptiness test. The ordinary optimizer defaults and the
preservation verifier are unchanged. The focused singleton/verifier suites pass **32 checks on .NET 10**
and **31 on .NET 8** in Debug. The complete Debug strict run returns to **2,695/16/1**, with zero
unmatched expected error codes and no inventory regressions. The complete Release legacy run with
one-in-ten saved-assembly sampling has the same results. Full unit suites pass in both Release and
Debug: **4,344 checks on .NET 10** and **3,991 on .NET 8**, per configuration. Broader ordinary W3C,
upstream, differential, saved/native consumers and performance gates remain required before delivery.
The initial failing export and regression logs are retained under `artifacts/p7-completion/`.

### Fresh native memory baseline

Before the optimizer change, the full canonical push matrix was rerun from the released runtime with
all 48 children, original fixtures/oracles and the exact 200 MiB input / 20 MiB limit. On local macOS ARM64,
**13/16 measured workloads pass**. Iterate, merge and fork fail at respectively **23,347,200**, **21,037,056**
and **23,265,280** peak bytes. Their post-collection managed estimates are respectively **833,528**,
**807,160** and **733,008** bytes. These estimates do not decompose the historical peak.

Raw evidence: `artifacts/p7-completion/native-baseline/push/report.json` and all child stdout/stderr files.
The existing isolated diagnostic tool now accepts a canonical `--workload` (identity remains its default)
to locate phase-specific costs in those failing paths. Diagnostic RSS guards remain disabled explicitly;
diagnostic results cannot satisfy either original memory gate. The current-source candidate, hosted
platforms, nightly matrix and throughput/latency qualification are still required.

### Atomic type-code check

Additional checkpoints in an isolated diagnostic copy located iterate's extra saved-load cost in the
query-type table. Its only entry was the unconstrained `xs:anyAtomicType` check; reading that entry
initialized the compiler's full query-type machinery. The instrumented interval added **2,605,056 peak
bytes**. Instrumented counters are diagnostic evidence and are not substituted for canonical budget results.

The IL generator now uses the existing type-code matching path for that exact broad atomic test.
Specific schema/type tests continue through their existing descriptors. The code-based checker now
distinguishes atomic values from functions, maps, arrays and internal control items; the old code-based
implementation incorrectly accepted every non-node. Regression tests compare it with the general
checker, cover empty/singleton/multiple sequences and nodes, and check emitted metadata.

The canonical iterate saved assembly now contains no query-type entries. All **32 fixture stylesheets**
are byte-for-byte unchanged. No saved-data version, compatibility check, budget, oracle or input changed.
The complete fresh push matrix finishes all **48 children** with correct outputs and improves from
**13/16 to 14/16** budget passes on local macOS ARM64. Iterate falls from **23,347,200 to 20,676,608 bytes**
and passes the original **20,971,520-byte** limit. Merge (**21,053,440**) and fork (**23,494,656**) still fail.

Current-source full units pass Release and Debug on .NET 10 (**4,349**) and .NET 8 (**3,996**).
All **74 ordinary W3C harness checks** pass with the retained raw results unchanged:
QT3 **21,884/0/99**, XSLT 3.0 **8,850/18/41**, XSLT 2.0 **4,590/8/10**.
Complete strict Debug and Release legacy/saved-sampling runs both retain **2,695/16/1** and zero
unmatched expected error codes, with the frozen inventory validated. Ordinary W3C's retained permissive
error-code counts remain visible in its reports; these results do not claim every ordinary diagnostic is exact.

Candidate provenance and the complete memory matrix are retained under
`artifacts/p7-completion/native-atomic-final/`. The native image SHA-256 is
`4e92c09eff713591748c18163799262f52e0cbd1c3c232fb6d33fcdd079c2df8`.
The full seven-workload, three-round 100 MB throughput comparison finishes **42/42 valid observations**.
The original no-slowdown target still fails: four workloads pass, while identity, fork and source-document
remain slower in streaming mode. The 5% reporting band is not used to waive this target.

| Canonical workload | Tree median ms | Stream median ms | Stream / tree | Original throughput target |
|---|---:|---:|---:|---|
| identity | 2,075.723 | 2,369.340 | 1.141 | Fail |
| adjacent | 1,421.103 | 707.839 | 0.498 | Pass |
| accumulator | 5,428.640 | 2,903.167 | 0.535 | Pass |
| iterate | 1,051.580 | 668.476 | 0.636 | Pass |
| merge | 12,408.936 | 2,466.330 | 0.199 | Pass |
| fork | 1,051.102 | 5,047.329 | 4.802 | Fail |
| source-document | 2,057.626 | 2,271.455 | 1.104 | Fail |

These are local observations for the recorded candidate, not hosted qualification or a before/after
throughput claim. Raw trials, outputs, configuration and artifact/controller hashes are in
`artifacts/p7-completion/native-atomic-final/throughput-100mb/`. All task-owned functional runs had finished
before this performance run. The 1 MB/1 GB latency observations, current-source nightly and hosted memory
matrices, ordinary performance gate and final delivery checks remain open.

Next implementation work targets fork's allocation/synchronization cost and the forward copying paths,
while preserving merge's remaining absolute memory failure. Any optimization must retain source identity,
private prong name tables, cancellation/error ordering and the original fixtures, oracles and limits.
Full P7 remains active; no streaming claim, merge or release is justified yet.

### Fork event projection and direct atomization

Fork views now project already normalized, validated logical events directly into their private
navigation frames. They retain source node numbers, private name tables, independent consumption
flags and schema snapshots. The source still owns input reads and validation; the threaded prongs,
caller-thread callbacks, declaration-order errors and configured tee capacity are unchanged.
Mixed text/CDATA, significant whitespace and nested subtree boundaries have explicit regressions.

An unselected fork subtree can also consume buffered events up to its matching end header under one
tee lock. Consumed prefixes release backpressure before further source I/O and on cancellation or
failure. This does not reserve a second payload queue or raise the event capacity. The stopping
predicate only inspects immutable headers; source I/O and prong callbacks remain outside the lock.

Saved IL inspection then identified `fn:data`, rather than constructor casting, as the numeric
workloads' generic built-in dependency. The compiler now emits a direct `X31Data` runtime call for
streamed per-item atomization. It uses the same canonical list-valued atomizer, including arrays,
schema-list typed values and function/map errors. It preserves cancellation and copies an atomic
result when QIL would reuse its input sequence. No function registration or numeric ID changes.
The runtime entry point is recorded in the unshipped public ABI inventory.

Saved static-data minor format 6 appends the emitter's built-in-ID usage flag. The real function
fingerprint stays in its original header position. Readers keep the unconditional compatibility
check for formats 3–5 and check it for new stylesheets whenever the emitted IL references a numeric
built-in ID. Only an explicit absence proof avoids initializing the registry for that check;
truncated new metadata fails loading. QName-based dynamic lookup retains its normal resolution.
All six numeric call ABIs, old-format checks and fingerprint mismatches have regression coverage.

The generated identity, iterate, merge and fork stream styles now all report
`UsesBuiltinFunctionIds=False`; these are inspected saved artifacts, not fixture-name exceptions
in the compiler. All 32 generated canonical stylesheets remain byte-for-byte unchanged.
Inspection logs and image/source hashes are under `artifacts/p7-completion/`.

The earlier candidates remain retained independently:

| Native candidate | Complete push children | Original budget passes | Output correctness |
|---|---:|---:|---|
| `native-fork-direct` | 48 | 13/16 | All correct |
| `native-saved-usage` | 48 | 11/16 | All correct |
| `native-fork-bulk` | 48 | 8/16 | All correct |
| `native-direct-data` | 48 | 14/16 | All correct |

The bulk-skip cohort's seven-workload throughput run has 42 valid observations and zero output
failures. Its fork stream median is **4,148.861 ms**, versus **991.565 ms** for tree, a ratio of
**4.184**. Identity, fork and source-document still miss the original target. Different candidates'
absolute times are retained observations, not a controlled attribution of their differences.
Small outer-process RSS overruns count as failures even when the child's internal status is `ok`.
Earlier active-reader/pulse locking experiments did not establish an improvement and were reverted.

For the direct-atomization source, full Release and Debug units pass on .NET 10 (**4,375**) and
.NET 8 (**4,022**), per configuration. Upstream passes **1,654** and differential **394** on each
framework; all **74** ordinary W3C harness checks pass with saved sampling enabled. Complete
strict Debug and Release legacy/saved-sampling runs retain **2,695/16/1**, zero unmatched expected
error codes and successful frozen-inventory audits. The first Release sampling invocation used
the new API because its API environment variable was misspelled; its report is retained separately.
The subsequent legacy report verifies `Api=False` and `SavedSampling=10` explicitly.

One earlier direct-projection Release run failed the .NET 8 namespace-allocation guard at 5,672
bytes against its unchanged 4,096-byte threshold. The isolated namespace tests, a full committed
`8fd76ea` baseline in a temporary source copy, and both current full configurations pass. The
original failure remains evidence of an intermittent observation, not a proven baseline defect
or a reason to relax the assertion.

The fresh direct-atomization native image is recorded under `native-direct-data` with SHA-256
`e2a210052056a8902d38e4d4a30b634b491e2cca4200b6ff06ce783c243a8ea7`.
Its original memory/throughput qualification is separate from functional verification. Full P7
and every remaining original acceptance, hosted CI, review, merge and release requirement stay open.

The direct-atomization push matrix finishes all 48 children: merge now passes at **20,578,304 bytes**,
while fork (**22,642,688**) and overlap-values (**21,037,056**) fail the unchanged **20,971,520-byte**
limit. The complete 42-observation throughput cohort validates every output but still fails the
original target: identity, fork and source-document have stream/tree ratios **1.158**, **4.143**
and **1.147** respectively. Fork's stream median is **4,122.852 ms**, versus **995.041 ms** for tree.
The local results do not qualify streaming support or replace current-source nightly/hosted gates.

### Fork culture capture

An isolated copy of `fdba07e` adds checkpoints around fork setup without changing its inputs,
results or culture selection. The first `CurrentCulture` read moves the recorded peak from
**16,596,992 to 18,563,072 bytes**, an interval of **1,966,080 bytes**, while allocated managed bytes
increase by **1,392**. This locates a startup cost; it does not decompose the final process peak or
count as a memory-budget pass. The diagnostic controller explicitly disables the RSS guard and
retains the 20 MiB managed-heap cap. Its output, source patch and hashes are retained under
`fork-startup-diagnostic` and `fork-startup-diagnostic-provenance` in the P7 artifacts directory.

The runtime now retains the captured execution context instead of eagerly reading platform-default
cultures. When the worker has that same context, culture is already inherited and no assignment is
needed. A different worker context extracts only the captured cultures and restores them explicitly;
it preserves unrelated worker `AsyncLocal` values. Suppressed flow retains immediate explicit
culture capture because it supplies no context to retain. The implementation uses the normal
[capture](https://learn.microsoft.com/en-us/dotnet/api/system.threading.executioncontext.capture?view=net-10.0)
and [run/restore](https://learn.microsoft.com/en-us/dotnet/api/system.threading.executioncontext.run?view=net-10.0)
contracts; it does not force an invariant culture or disable globalization.

Review exposed a lifetime issue in the first candidate: a completed prong could retain unrelated
host objects through the captured context. A regression failed on both frameworks before cleanup.
The prong now releases its culture/context snapshot in its `finally` block, before source cleanup;
the regression passes while keeping the completed runtime alive. Culture inheritance/restoration,
suppressed flow, unrelated ambient state, success and failure have explicit coverage.
The full final Release and Debug unit suites pass **4,380** checks on .NET 10 and **4,027** on .NET 8,
per configuration, without the nullable warning from the intermediate candidate. The earlier
failed retention test and intermediate runs remain retained.

The new canonical native candidate is `native-fork-culture`. Its original budget and throughput
results must be recorded independently; diagnostic checkpoint savings are never substituted for them.

Its complete push matrix finishes all 48 children with correct outputs and **15/16 budget passes**.
Fork passes at **20,643,840 bytes**; overlap-values is the sole failure at **21,037,056 bytes**,
**65,536 bytes** over the original limit. The complete 42-observation throughput run has no invalid
outputs but still fails the target for identity (**1.176**), fork (**4.453**) and source-document
(**1.155**). Fork's stream median is **4,602.018 ms**, against **1,033.533 ms** for tree. These cohorts
are retained observations; they do not establish a controlled before/after throughput effect.
The image SHA-256 is `c99688cfade71e33e42dd1135589b4b53e570a731aa5e540147b877fb37c9fbf`.

Final strict Debug and Release legacy/saved-sampling runs retain **2,695/16/1** and zero unmatched
expected error codes, with both full frozen-inventory audits passing. Upstream, differential and
ordinary W3C results in the preceding section are from the direct-atomization source, before the
culture change; final full compatibility qualification remains required before delivery.

The remaining push failure's actual saved assembly still contains `X31Call2` and reports
`UsesBuiltinFunctionIds=True`. Its canonical stylesheet uses `string-join` over overlapping
descendants with a literal separator. The next memory investigation should trace that generic
built-in path and its initialization cost, preserving atomization of overlapping values, list types,
argument cardinality and diagnostics. The identity/source-document copying and fork coordination
throughput failures remain separate implementation work. No gate or streaming-support claim changes.

### Direct streamed string joining

The compiler now emits `X31StringJoin` for streamed joins with a proven string separator and for
the one-argument form. The omitted separator is the empty string specified by
[F&O 3.1 §5.4.2](https://www.w3.org/TR/xpath-functions-31/#func-string-join). Node-valued or otherwise
unproven separators keep the existing function-conversion and cardinality checks. The runtime and
ordinary function registry share lexical joining; registry names, arities and fingerprint are unchanged.
Forward arguments still atomize before advancing beyond overlapping selected nodes. Arrays, schema
lists, nilled elements, non-simple typed content, map errors, cancellation and saved execution have
explicit regressions. The new runtime ABI is recorded in the unshipped public inventory.

The actual saved overlap-values stream style now reports `UsesBuiltinFunctionIds=False` and its
static data decreases from **523 to 246 bytes**. All 32 generated stylesheets are unchanged. This
is a general expression lowering, with no workload name or expected-output branch in the runtime.
Full Release/Debug units pass **4,399** checks on .NET 10 and **4,044** on .NET 8 per configuration.
Strict Debug and legacy/saved-sampling reports retain **2,695/16/1**, zero unmatched expected codes
and passing complete-inventory audits. All **74** ordinary W3C harness checks pass with unchanged
raw counts and permissive-code notes: QT3 **21,884/0/99** (25 notes), XSLT 3.0 **8,850/18/41**
(34 notes), XSLT 2.0 **4,590/8/10** (7 notes).

The fresh `native-direct-join` image has SHA-256
`cae7ea038bff690ea9fdcf6ebe9f4bf7006eeb4612cfb0c995eca91e9f2a692e`.
The complete canonical push matrix finishes all **48 children**, validates every output and passes
**16/16 workloads** under the original **209,715,200-byte input / 20,971,520-byte absolute limit**.
Overlap-values passes at **20,545,536 bytes**; the maximum observed workload peak is fork at
**20,692,992 bytes**. This is local macOS ARM64 evidence, not hosted qualification.

The complete seven-workload 100 MB comparison has **42 valid observations** and zero invalid outputs.
The original throughput target remains unmet:

| Workload | Tree median ms | Stream median ms | Stream / tree |
|---|---:|---:|---:|
| identity | 1,991.534 | 2,335.402 | 1.173 |
| adjacent | 1,350.852 | 624.667 | 0.462 |
| accumulator | 5,074.338 | 2,937.377 | 0.579 |
| iterate | 1,000.117 | 624.076 | 0.624 |
| merge | 12,868.277 | 2,468.882 | 0.192 |
| fork | 998.342 | 4,293.469 | 4.301 |
| source-document | 2,000.400 | 2,319.278 | 1.159 |

Raw memory children, measurements, output fingerprints and artifact/controller provenance are retained
under `artifacts/p7-completion/native-direct-join/`. Its full local nightly matrix also completes
all **48 children** with correct outputs and **16/16 budget passes**, using the original
**2,147,483,648-byte input / 209,715,200-byte absolute limit**. Its maximum observed workload
peak is **26,214,400 bytes** (merge and type-inspection). This remains local macOS evidence.

### Hosted Linux qualification at a25acbb

[Ordinary CI](https://github.com/skuirrels/Xsl3/actions/runs/40963890105) passes Linux and Windows
build/test, NativeAOT and the ordinary performance check for
`a25acbbde59017f9aa9aed1e39d9fedf5f2240ee`. The separate original
[streaming acceptance run](https://github.com/skuirrels/Xsl3/actions/runs/40964357256) fails the
Linux push matrix: all **48 children** finish but **0/16 measured workloads** pass the unchanged
**20,971,520-byte** limit. Fifteen measured transformations consume the full input and validate
output; merge is rejected before measurement and is not counted as a valid transformation.

Linux's maximum of child/external peak ranges from **22,339,584 to 25,690,112 bytes**. Every
measured child's own peak also exceeds the limit; the failures are not solely an external-counter
discrepancy. The complete artifact is retained under
`artifacts/p7-completion/linux-a25acbb-push/streaming-probe-push/`. The low post-collection managed
estimates do not decompose those historical peaks. No runtime cause has yet been established.
The existing isolated diagnostic run
[40966348676](https://github.com/skuirrels/Xsl3/actions/runs/40966348676) targets the same immutable
source. Diagnostic RSS guards remain disabled, so its observations cannot satisfy acceptance.

Current-source hosted nightly memory, multi-size first-output evidence, full final compatibility/review
and the three throughput failures remain open. Full P7 is not complete.

### Direct stripped attribute construction

The local CPU profile identifies temporary compact attribute trees in `XsltLibrary.Copies` before
insertion into constructed content. The candidate compiler lowers statically attribute-only streamed
`xsl:copy-of` with `validation="strip"` through the output construction path. Insertion retains
namespace fixup, `xml:id` normalization and ID/IDREF metadata without retaining schema annotations.
Explicit validation and preservation continue through their canonical paths. This follows the
existing [copy/construction contracts](https://www.w3.org/TR/xslt-30/#constructing-complex-content),
including the errors for attributes in document content or after child content.

The first candidate failed the existing returned-attribute P7G tests because a raw destination can
use an unannotated result writer. The corrected implementation keeps canonical compact copies for
raw sequences, preserving fresh identity, parentlessness and metadata. All existing attribute-copy
and P7G focused tests pass (**634 .NET 10 / 438 .NET 8**). Added tests cover typed identity, QName
stripping under preserve defaults, namespace conflicts, duplicate names, construction errors and
normal/debug saved execution; emitted metadata confirms the direct entry point.

Earlier failing test evidence is retained. The new schema fixture initially referenced an undeclared
ID and was corrected to a valid repeated reference; that failure is not attributed to the runtime.
Full final compatibility and native qualification for this candidate remain in progress. Neither a
Linux memory improvement nor a throughput improvement is claimed before measurement.

Full Release and Debug units pass **4,413 checks on .NET 10 / 4,056 on .NET 8**, per configuration.
Complete strict Debug and Release legacy/saved-sampling runs retain **2,695/16/1** and zero unmatched
expected codes, with both full frozen-inventory audits passing. All **74** ordinary W3C checks pass.
The CI-defined upstream gate (`category!=failing`) passes **1,654 checks per framework**. An earlier
unfiltered invocation included excluded platform/active-issue cases and failed; it is retained rather
than attributed to the candidate. Differential tests pass **394 checks per framework**.
Final saved/native and performance qualification remain separate.

Linux diagnosis now has an optional `--mapping-snapshots` mode. The external controller retains
bounded-buffer copies of a live child's `/proc/<pid>/smaps`, with elapsed times and fingerprints.
The selected diagnostic workflow enables this for its unchanged canonical controls; neither the
native control image nor either original acceptance controller changes. The snapshots can attribute
instantaneous resident mappings, not decompose a historical peak. Missing/empty snapshots remain
visible, and a requested cohort without any successful snapshot cannot pass diagnostic validation.
Local controller regressions pass **38 tests**, with the live Linux procfs test explicitly skipped
on macOS; hosted verification of that test and actual mapping evidence remain required.

### Attribute candidate qualification at 2afb15c

The attribute-copy and external-diagnostic changes are committed at
`2afb15c9262519042a812f6aab7cc67ae095b9ee` on PR #27. All three saved contract cohorts (original
P7G and bare-variable P7H generated by Release/Debug compilers) pass on .NET 8, .NET 10 and NativeAOT:
**192 checks per consumer / 576 per cohort**. The canonical native probe image has SHA-256
`a20e1b372756d605dc2461fa6e1333ce781727b1e05edf067964a4e2cfa4ce55` and all 32 fixture stylesheets
match the retained baseline byte-for-byte. Evidence is under
`artifacts/p7-completion/native-direct-attribute/` and the `attribute-direct-*-native` directories.

The complete local push matrix finishes **48 children**, with correct output and **16/16 original
budget passes**. The maximum peak is fork at **20,643,840 bytes**. The complete 100 MB throughput
matrix has **42 valid observations**, no incorrect outputs and a failing original overall target:

| Workload | Tree median ms | Stream median ms | Stream / tree | Original target |
|---|---:|---:|---:|---|
| identity | 2,059.535 | 2,028.517 | 0.985 | Pass |
| adjacent | 1,416.608 | 623.189 | 0.440 | Pass |
| accumulator | 5,903.667 | 2,995.760 | 0.507 | Pass |
| iterate | 1,167.384 | 671.233 | 0.575 | Pass |
| merge | 14,462.433 | 3,018.337 | 0.209 | Pass |
| fork | 1,165.837 | 5,248.327 | 4.502 | Fail |
| source-document | 2,310.635 | 2,391.356 | 1.035 | Fail |

These are the recorded cohort's observations, not controlled attribution of a before/after speedup.
Source-document's 3.5% slowdown still fails; no 5% waiver applies. The local nightly matrix also
finishes all **48 children / 16 measured workloads** with correct outputs and original budget passes;
its maximum absolute peak is **26,312,704 bytes**.

The [current Linux push run](https://github.com/skuirrels/Xsl3/actions/runs/40969126644) also finishes
all 48 children but retains **0/16 budget passes**. All sixteen measured transformations now consume
the full 209,715,200 bytes and validate output, including merge; absolute peaks range from
**24,313,856 to 25,137,152 bytes**, exceeding the unchanged 20,971,520-byte limit. Its raw artifact is
retained under `artifacts/p7-completion/linux-2afb15c-push/`. No Linux memory fix is claimed.

The earlier a25acbb [hosted nightly run](https://github.com/skuirrels/Xsl3/actions/runs/40964357256)
is now complete: all **48 children / 16 measured workloads** pass, every measured input is exactly
2,147,483,648 bytes, and all outputs validate. The maximum absolute peak is **40,333,312 bytes**.
Its downloaded report has been independently checked against every field of the original gate;
evidence is in `artifacts/p7-completion/linux-a25acbb-nightly/`. This qualifies that older source's
nightly matrix; current-source verification remains separate.

The completed a25acbb isolated Linux diagnosis retains nine valid control and nine valid instrumented
runs. At 200 MiB input, the instrumented medians show live RSS of **20,762,624 bytes** after warmup
collection and **24,072,192 bytes** after transformation; last-GC commitment grows from **1,007,616
to 3,170,304 bytes** over those checkpoints. Anonymous and proportional file-backed resident data
both contribute. These are simultaneous checkpoint observations, not a decomposition of the full
historical peak or proof of one runtime cause. Controls and snapshots are retained under
`artifacts/p7-completion/linux-a25acbb-diagnosis/`.

The first mapping dispatch, run 40968902572, failed at checkout because its abbreviated commit ID
was interpreted as a branch/tag pattern; it produced no measurements. The corrected
[full-SHA dispatch](https://github.com/skuirrels/Xsl3/actions/runs/40969355113) passes, including all ten
Linux diagnostic tests and nine valid controls/nine valid instrumented runs. Its external snapshots
are retained in `artifacts/p7-completion/linux-2afb15c-mappings/`. A 200 MiB control snapshot at about
five seconds records **24,006,656 bytes** live RSS: **10,469,376 bytes** in the native executable,
**6,672,384 anonymous bytes**, and **3,108,864 bytes** across the ICU mappings, alongside libc,
libstdc++ and other libraries. This establishes resident categories, not which caller first loads
them or an irreducible platform minimum. Globalization remains enabled.

All four [required ordinary CI jobs](https://github.com/skuirrels/Xsl3/actions/runs/40968827722)
pass for 2afb15c: Linux/Windows build-test, NativeAOT and ordinary performance. The current-source
hosted nightly matrix also passes **48/48 children / 16 measured workloads**, with a maximum peak of
**40,361,984 bytes**. The downloaded report was independently validated against the original gate,
including exact input, native execution, both memory counters, baseline fingerprints and output validity.
Raw evidence is under `artifacts/p7-completion/linux-2afb15c-final/streaming-memory-nightly/`.

The hosted benchmark completes **126 attempts**, of which **105 are valid**. All 1 MB and 100 MB
tree/stream trials validate output. All 21 streaming trials at 1 GB also validate output; all 21
1 GB tree trials throw `OutOfMemoryException` under the configured **4 GiB heap guard**, including
array growth and snapshot materialization. These remain failed comparisons, not valid timing samples.
The earlier a25acbb cohort has the same 21 invalid tree trials and is retained separately.

The full valid hosted 100 MB cohort still fails the original throughput target:

| Workload | Tree median ms | Stream median ms | Stream / tree | Original target |
|---|---:|---:|---:|---|
| identity | 3,102.639 | 3,256.720 | 1.050 | Fail |
| adjacent | 2,404.194 | 1,391.366 | 0.579 | Pass |
| accumulator | 7,553.315 | 5,079.328 | 0.672 | Pass |
| iterate | 2,021.648 | 1,494.404 | 0.739 | Pass |
| merge | 16,164.774 | 4,724.139 | 0.292 | Pass |
| fork | 2,064.121 | 9,391.888 | 4.550 | Fail |
| source-document | 3,159.649 | 3,161.232 | 1.001 | Fail |

Source-document's actual ratio is **1.000501127197232**, so it fails even though the rounded table
looks close to parity. No tolerance band applies. This is a JIT-hosted cohort; its timings are not
substituted for the local native observations or used as a paired attribution of source changes.
The report and all stdout/stderr are under
`artifacts/p7-completion/linux-2afb15c-final/streaming-benchmark-nightly/`.

Across all three sizes, streamed median input consumption before the actual first sink write is
**8,192 bytes** for identity/source-document, **356,352** for adjacent, **122,880** for merge and
**36,864** for fork. Identity's first-output medians are **0.493 / 0.456 / 0.466 ms** at 1/100/1,000 MB.
Accumulator and iterate intentionally emit their final total only after consuming the whole input;
their EOF-dependent first writes are retained explicitly, not reported as constant-latency output.
These are valid streaming latency observations; the missing 1 GB tree counterparts leave the full
benchmark job unsuccessful.

### Follow-up diagnostic attribution

An isolated 2afb15c source copy brackets DTD identity lookup without changing the working runtime.
Its 10 MiB identity run validates output with a 20 MiB managed cap and disabled RSS guard. The first
bracketed ID lookup interval adds **81,920 peak bytes / 45,768 allocated bytes** on macOS. That interval
does not establish a Linux budget fix or account for all startup costs, so no DTD runtime policy was
changed. The source patch, build result, checkpoints and provenance are retained under
`artifacts/p7-completion/dtd-startup-diagnostic/`.

Fresh fork and source-document CPU samples use the actual qualified native image, 1 GB inputs and
the throughput cohort's 4 GiB heap guard; both validate full output. Sampling makes them diagnostic
observations, not acceptance timings. Fork still shows substantial waiting, synchronization and
allocation. A separate aggregate-only fork diagnostic records **321,158 source callbacks** for
100 MB, averaging **32.491 events per requested refill**; only **1,287** requests are four events or
smaller. Thus tiny refills alone do not explain the gap. Caller-owned source handoff and event
allocation remain implementation targets while preserving the existing 64-event capacity, caller
I/O/output ownership, declaration-order faults and cancellation/lifetime contracts. Evidence is in
`fork-profile-2afb15c`, `source-document-profile-2afb15c` and `fork-batch-diagnostic` under the P7 artifacts.

Original Linux push/throughput gates, the failed 1 GB paired comparisons, actual-forwarding evidence,
remaining runtime/integration requirements, final review, merge and release remain open. P7 is active.

### Caller-owned fork production

Fork consumers now signal input demand to the invoking thread's existing callback pump. The pump
refills the tee directly outside both synchronization gates, avoiding a synchronous queued closure
and worker acknowledgement for every source batch. A wake generation prevents requests arriving
between the source check and `Monitor.Wait` from being lost. Standalone tees retain their original
self-producing behavior. The configured 64-event capacity, source/output ownership, accepted-prefix
faults, declaration-order error selection and cancellation/lifetime rules are unchanged.

Five additional checks cover the missed-wakeup interval, an invoking-thread callback reading fork
input, callback service while the ring is full, and caller-owned accepted-prefix faults at capacities
one and seven. The focused suites pass **47 checks on each framework**. Complete Release and Debug
units pass **4,418 on .NET 10 / 4,061 on .NET 8**, per configuration. Full strict Debug and Release
legacy/saved-sampling exports retain **2,695/16/1**, zero unmatched error codes and passing frozen
inventory audits. Evidence is under `artifacts/p7-completion/caller-pump-*`.

The fresh native image is
`5a3f784403333eb49028109875fc57b58bbca38b750c4dd88e23e5a96f4e6238`; all 32 generated stylesheets
match the retained attribute candidate byte-for-byte. A serial, alternating-image 100 MB fork
comparison validates all twelve output fingerprints under the same 4 GiB heap guard. The retained
image's stream median is **9,405.116 ms** (range 8,216.923–9,497.366); the candidate is **7,641.989 ms**
(7,253.895–7,781.896), an observed **18.75% reduction**. Tree medians are respectively 1,313.767 and
1,151.393 ms. Other host work was active; these observations require confirmation and are not an
isolated-machine speedup claim. The candidate's **6.637 stream/tree ratio still fails** the original
no-slowdown target. Raw trials and both executable hashes are retained in
`artifacts/p7-completion/caller-pump-paired-100mb/`. Complete current-source memory, seven-workload
throughput and hosted qualification remain required; this optimization does not complete P7.
