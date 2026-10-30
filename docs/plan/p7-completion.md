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

### Completed caller-pump qualification at e407054

The complete local push matrix passes **16/16 workloads / 48 children** under the original
200 MiB input and 20 MiB absolute limit, with maximum peak **20,070,400 bytes**. The complete local
nightly matrix passes the same full population at exact 2 GiB inputs / 200 MiB limit, with maximum
peak **26,230,784 bytes**. Every output validates. Raw evidence is under
`artifacts/p7-completion/native-caller-pump/`.

The complete local 100 MB throughput matrix has **42 valid observations** and still fails the
original target: identity **1.200507**, fork **5.883849** stream/tree. Source-document passes this
particular cohort at **0.928778**; variation across cohorts remains retained.

The [hosted e407054 acceptance run](https://github.com/skuirrels/Xsl3/actions/runs/40976023841)
is terminal. Linux push remains **0/16 original budgets**, with peaks **23,932,928–25,726,976 bytes**.
All sixteen transformations consume the full input and validate output; their status remains
`budget-exceeded`, not an acceptance pass. Linux nightly passes **16/16 workloads / 48 children**
with exact inputs, both original memory counters and maximum peak **40,124,416 bytes**. Downloaded
ZIP digests, complete reports and child output are retained under `caller-pump-linux-*`.

The hosted benchmark has **105 valid observations / 126 attempts**. All twenty-one 1 GB tree trials
again throw `OutOfMemoryException` under the unchanged 4 GiB heap guard. All 1 MB and 100 MB trials
and all twenty-one 1 GB streaming trials validate output. Recomputed 100 MB medians are:

| Workload | Tree median ms | Stream median ms | Stream / tree | Original target |
|---|---:|---:|---:|---|
| identity | 5,002.573 | 5,169.102 | 1.033289 | Fail |
| adjacent | 3,767.117 | 2,429.281 | 0.644865 | Pass |
| accumulator | 11,878.518 | 8,544.997 | 0.719366 | Pass |
| iterate | 3,230.405 | 2,547.429 | 0.788579 | Pass |
| merge | 23,553.885 | 8,499.061 | 0.360835 | Pass |
| fork | 3,531.392 | 13,855.954 | 3.923653 | Fail |
| source-document | 5,158.093 | 5,266.846 | 1.021084 | Fail |

Ordinary CI initially failed the unchanged namespace-enumeration allocation check on Linux:
49,200 bytes against the existing 4,096-byte limit. The original log and test artifact are retained.
Windows, NativeAOT and ordinary performance passed. Once Windows completed, the Linux job was
retried without source or threshold changes; its unit tests pass. Remaining retry steps still
require terminal verification before ordinary CI can be called green.

### Targeted fork wakeups

The tee now distinguishes a producer waiting for buffer space from consumers waiting for publication.
Consuming buffered events only signals operations that become eligible: a parked space waiter, or
the invoking-thread producer when a full ring gains space. Publication, cancellation and disposal
retain their notifications. Refills can still use every free slot; capacity and caller ownership
are unchanged.

Two additional regressions protect a caller callback reading partially drained input and a faster
prong advancing within capacity while its peer retains buffered events. Full Release/Debug units
pass **4,420 checks on .NET 10 / 4,063 on .NET 8**, per configuration. Complete strict Debug and
Release legacy/saved-sampling exports retain **2,695/16/1**, zero unmatched codes and passing frozen
inventory audits. Raw results use the `targeted-wakeup-*` prefix under the P7 artifacts.

Two serial, alternating-image native fork cohorts each validate twelve outputs with unchanged
100 MB inputs and 4 GiB heap guard:

| Cohort | e407054 stream ms | Candidate stream ms | e407054 child CPU ms | Candidate child CPU ms |
|---|---:|---:|---:|---:|
| First | 3,920.999 | 3,819.275 | 6,621.104 | 5,279.453 |
| Confirmation | 4,048.741 | 3,798.656 | 6,923.429 | 5,270.211 |

Observed full-child CPU medians fall **20.3% / 23.9%**. Those counters include startup, warmup and
oracle generation. Elapsed medians fall **2.6% / 6.2%**; candidate stream/tree ratios **3.694 / 3.663**
still fail the original target. Reports, CPU counters, both native image hashes and the source patch
are retained in `artifacts/p7-completion/fork-wakeup-prototype/`. Complete qualification of the
working-branch image remains required.

Rejected isolated candidates are retained independently. Deferring built-in schema-type fields
establishes no useful footprint reduction across twelve valid diagnostic trials. Bounded spinning
raises fork elapsed median **8.1%** and full-child CPU about **31%**. Draining the entire ring before
refilling reduces CPU but fails the new free-slot progress regression at its cancellation deadline;
it can strand a prong despite available capacity. None of these policies is included in the branch.
Their evidence is under `schema-lazy-prototype`, `fork-spin-prototype` and `fork-drain-prototype`.

Full P7 remains active: Linux push memory, the full throughput target, the 1 GB tree comparisons,
actual-forwarding and integration evidence, final review, merge and release remain required.

### Completed targeted-wakeup qualification at fab882f

The local NativeAOT matrices are terminal. Push passes **16/16 workloads / 48 children** with
maximum peak **20,480,000 bytes**; nightly passes the same full population with maximum peak
**26,492,928 bytes**. The local 100 MB throughput matrix validates all **42 observations**. Fork
still fails the original target at **3.542325 stream/tree**; the other six medians pass this cohort.
The original budgets, inputs and counters are unchanged. Evidence is under `native-targeted-wakeup`.

[Ordinary CI for fab882f](https://github.com/skuirrels/Xsl3/actions/runs/40982585852) is terminal and
passes Linux, Windows, NativeAOT and the ordinary performance gate. The separate
[hosted P7 qualification](https://github.com/skuirrels/Xsl3/actions/runs/40982648282) retains its
original failures. Downloaded archive digests match GitHub's published artifact digests:

- Linux push passes **0/16 budgets**, with process-lifetime peaks **21,684,224–25,137,152 bytes**.
  Fourteen transformations consume the exact input and validate output. Merge and fork exceed the
  budget during startup/warmup and stop before the measured input, so these two are not completed
  transformations or valid throughput observations.
- Linux nightly passes **16/16 workloads / 48 children**, with exact 2 GiB inputs, both original
  memory counters and maximum peak **40,337,408 bytes**.
- The hosted benchmark has **105 valid observations / 126 attempts**. All twenty-one 1 GB tree
  attempts throw `OutOfMemoryException` under the original **4 GiB heap guard**. The invalid trials
  and their complete error output are retained. At 100 MB, identity, fork and source-document
  still fail the original throughput target at **1.034118**, **3.134382** and **1.032397** respectively.

Raw hosted evidence is under `targeted-wakeup-linux-push`, `targeted-wakeup-linux-nightly` and
`targeted-wakeup-linux-benchmark`. These outcomes do not qualify full P7 or public streaming support.

### Compact value-pool investigation

The 1 GB tree failures initially occur while growing compact-tree arrays. The full-tree builder also
owns a dictionary of short strings that are already retained in its value array. An isolated
hash/index table preserves the existing ordinal equality, complete interning, stable value indices
and 32-character boundary while eliminating those duplicated key references. Its four-entry initial
table also avoids a large up-front cache for small documents. No cache cap, eviction or fixture-specific
behavior is introduced.

The reviewed isolated candidate passes full Release/Debug units, **4,427 on .NET 10 / 4,070 on
.NET 8**, per configuration. These runs include a hash-bucket collision/growth regression. An
additional full-hash collision regression derives a pair using the current process's randomized
ordinal comparer; all eight value-pool tests pass on both frameworks. The published native probe
retains the same 32 saved entry points and corpus definitions.

Serial alternating 100 MB controls validate all **42 native observations** across seven workloads,
plus **six JIT identity observations**. Native identity peak RSS median falls from **381,140,992** to
**345,309,184 bytes**; JIT identity falls from **488,898,560** to **410,697,728 bytes**. Other native
memory outcomes vary by workload, including a small increase for fork. Native adjacent grouping
is **4.6% slower**, confirmed at **4.4%** in a second six-observation cohort. A native sample shows
the table lookup and ordinal hashing on the hot path. Explicit lookup inlining, mixed single-unit
hashes and direct ASCII-value slots are retained as separate experimental variants. Their additional
18/18/42/42-observation cohorts validate output but do not consistently eliminate the grouping cost.
Those extra policies are excluded from the branch; a memory saving alone does not establish a
throughput win or waive an ordinary performance or original streaming qualification gate.

A one-round diagnostic runs all seven JIT tree workloads at exact 1 GB input and the original
4 GiB heap guard. Identity, iterate and source-document validate output; adjacent, accumulator,
merge and fork still throw `OutOfMemoryException`. Their retained stacks now identify grouping tuple
clones, grounded accumulator phase dictionaries, snapshot copy-origin storage and temporary result
text construction. This is **3/7 diagnostic successes**, not the required twenty-one hosted tree
observations or complete benchmark acceptance. All raw trials, errors, original images, patches and
hashes remain under `tree-hash-index-prototype`. The earlier index-only attempt and its observed
1 GB slowdown remain separately retained under `tree-index-prototype`.

The branch now incorporates the simpler reviewed hash/index runtime. Its runtime source digest
matches the measured `bin-reviewed` image exactly. Eleven public-loader regressions cover full-hash
collisions, bucket collisions through growth and wraparound, repeated attribute/text values, ASCII and Unicode
single-unit values, mixed long/short value indices, ordinal distinctions and the 31/32/33-unit
boundary. The single-unit regressions also apply to the ordinary randomized-hash table; they do not
enable any experimental special-key policy. Final-source compatibility and hosted qualification
remain required, including the unresolved tree-grouping timing cost and four large-tree failures.

Final branch Release and Debug unit suites each pass **4,431 on .NET 10 / 4,074 on .NET 8**. Complete
strict inventories run through both the public API and the legacy API with saved sampling, in both
configurations. All four retain **2,695 pass / 16 fail / 1 not run**, zero unmatched error codes and
passing frozen-case audits. The tested engine copies and JSONL fingerprints are independently
checked; evidence uses the `compact-pool-*` prefix. These inventory checks preserve the full raw
population but do not prove actual forwarding or close any original P7 acceptance requirement.

### Grounded accumulator phase storage

The retained large accumulator failure occurs while growing a dictionary keyed by a generated string
for every visited node. Compact trees now identify before/after phases by exact depth-first event
position, derived from their preorder index, exclusive subtree end and ancestor depth. Their cache
stores changes in the phase sequence, carrying the same immutable value reference across unmatched
nodes. A separate last-recorded position keeps future phases undefined during recursive demands and
after partial failures. Foreign and vendored navigators retain their existing string-key path.

An initial contiguous change list passes full Release/Debug units, **4,441 on .NET 10 / 4,084 on
.NET 8**, per configuration, and twelve paired 100 MB observations. It still fails all three 1 GB
candidate attempts while resizing that list; the baseline also fails while resizing its dictionary.
The complete failed trials and first image remain retained under `accumulator-timeline-prototype`.

The adopted implementation uses small initial storage and fixed-size blocks thereafter, avoiding a
large old/new timeline-buffer pair during growth. Every phase value remains available: no input
limit, eviction, fixture test or qualification-budget change is introduced. Existing gate ownership,
dependency-cycle handling, cancellation, copy origins and weak document lifetimes remain intact.
Thirteen additional regressions check mixed content and reverse lookup through compact, vendored
and genuinely foreign navigators; many value changes across storage blocks; partial and recursive
phase availability; and document/fragment start/end event positions. Full final-branch Release and
Debug suites each pass **4,444 on .NET 10 / 4,087 on .NET 8**.

The segmented JIT cohort validates all **15 observations** with the original 4 GiB heap guard:
twelve alternating tree/stream controls at exact 100 MB and three correct 1 GB tree
observations (the first large trial precedes the controls). Large-input peaks are **3,383,476,224**,
**3,842,113,536** and **3,662,856,192 bytes**; all three output oracles pass. This is local evidence for
one workload, not complete hosted large-input acceptance. At 100 MB, tree median falls from
**5,556.670 ms** to **2,978.253 ms** and median RSS from **1,361,166,336** to **487,702,528 bytes**.
Streaming medians remain **4,279.055 / 4,260.305 ms**. The faster tree baseline therefore exposes an
accumulator throughput shortfall at the original stream-no-slower-than-tree target. That target is
still required; no performance waiver or full streaming-support claim follows from this memory fix.

[Ordinary CI at the preceding 49475dd checkpoint](https://github.com/skuirrels/Xsl3/actions/runs/40990263039)
passes Linux, Windows, NativeAOT and ordinary performance. Its full hosted push matrix remains
**0/16 budgets**, although all sixteen measured transformations complete correctly; peaks are
**24,395,776–25,616,384 bytes**. Its hosted nightly matrix passes **16/16 workloads / 48 children**,
maximum peak **40,177,664 bytes**, with exact 2 GiB inputs and both original counters. Downloaded
ZIPs match the published SHA-256 digests and all measurement fields are independently checked.
The source's hosted multi-size benchmark is now terminal: **105 valid observations / 126 attempts**,
with all twenty-one 1 GB tree trials still throwing `OutOfMemoryException`. All other size/mode trials
validate. Initial compact-tree growth still fails on Linux for the six non-merge populations; merge
also retains snapshot allocation failures. At 100 MB, identity and fork fail the original throughput
target at **1.018245** and **3.867905** respectively; the other five pass that cohort. This preceding
source's raw results and verified artifact digest are under `compact-pool-linux-benchmark`.

Final-source strict API and legacy/saved-sampling exports, in both Release and Debug, retain
**2,695 pass / 16 fail / 1 not run**, zero unmatched error codes and four passing frozen-inventory
audits. A new warnings-as-errors NativeAOT publication executes all **48 saved smoke children**
correctly across sixteen workloads. Its additional exact 1 GB accumulator pair validates both tree
and stream outputs under the original 4 GiB heap guard, with peaks **3,667,017,728** and
**26,345,472 bytes** respectively. These are diagnostic/smoke observations, not passes of the
original full memory or throughput matrices. Native images and all raw outcomes are retained in
`accumulator-timeline-prototype`.

Final-source hosted qualification, remaining large-tree
failure sites, Linux push memory, all throughput targets, actual-forwarding/public integration,
review, merge and release remain required.

### Full-tree column growth

Retained hosted stacks identify `CompactDocument.Builder.Grow`, including resizing the exclusive
subtree-end column, as a shared 1 GB input failure. Full-tree columns now grow by half their current
capacity instead of doubling, reducing unused space and the old/new buffers present during a resize.
Temporary trees retain their existing growth policy. Capacity arithmetic uses a wider intermediate
and the runtime's natural array maximum; no input limit, fixture branch, GC/globalization switch,
budget or expected-output change is introduced.

Isolated public-loader diagnostics use the unchanged canonical generated input and retain both
images. At exact 1 GB, both versions retain **8,695,652 records, 69,565,219 content nodes,
26,086,956 attributes and 8,695,661 distinct stored values**. The old node capacity is **134,217,728**;
the candidate's is **87,266,902**, avoiding **798,164,042 bytes** of unused capacity across the five
columns. At 100 MB the same shape counters also match. The diagnostic-only test entry-point command
is excluded from the branch and canonical benchmark image; this capacity evidence does not itself
satisfy an acceptance gate.

Full isolated Release and Debug suites each pass **4,446 on .NET 10 / 4,089 on .NET 8**. Two additional
comparisons against `XPathDocument` exercise content, attributes, namespaces, parent/sibling axes and
values across many column resizes. All **42 alternating 100 MB tree controls** validate output.
Candidate median times range from **1.9% faster to 2.7% slower** by workload; no universal speedup
or performance waiver is claimed. Five of seven candidate 1 GB tree trials validate output under
the original 4 GiB heap guard: identity, accumulator, iterate, fork and source-document. Adjacent
grouping still runs out of memory while constructing grouping keys, and merge still runs out of
memory in its snapshot path. The complete **49 transformation attempts / 47 valid observations**,
four valid layout diagnostics, images, hashes and source patch are under `tree-growth-prototype`.

The branch adopts only the reviewed runtime and navigation regressions. Final-source compatibility,
NativeAOT/saved execution and the complete hosted original matrices remain required. The known
grouping/merge allocations, Linux push memory, all throughput targets, actual-forwarding/public
integration and final review/merge/release are still open; full P7 is not complete.

The final branch Release/Debug suites retain the same **4,446 / 4,089** unit counts. Complete strict
inventories through both API paths, including legacy saved sampling, run in both configurations;
all four retain **2,695 pass / 16 fail / 1 not run**, zero unmatched error codes and verified frozen
case/engine audits. Raw final-source evidence uses the `tree-growth-*` prefix. These checks do not
replace the required full hosted qualification or prove actual forwarding.

A warnings-as-errors NativeAOT publication also validates all **48 saved smoke children** and four
exact 1 GB identity/fork tree/stream controls under the original 4 GiB heap guard. The tree peaks are
**3,643,604,992 / 4,063,559,680 bytes** for identity/fork, and stream peaks are **26,034,176 /
26,591,232 bytes**. These smoke/diagnostic checks retain both native flags and independent output
oracles; they are not the original full memory/throughput acceptance matrices. Raw results and the
native image digest are retained under `tree-growth-prototype`.


### Incremental snapshots for tree-backed merge inputs

The retained exact 1 GB merge control at `ef57091` fails in `X31Snapshot`: the tree-backed path
materializes snapshots for the complete merge population. A candidate compiler uses the existing
pull merge engine whenever any source is declared streamable, including when execution explicitly
selects tree inputs. Opening a forward source still requires streamed compilation. Merges without
streamable-declared sources retain materialized grouping and their total action context size.
No snapshot, source selection, input, oracle, heap guard or acceptance limit is removed.

The isolated full Release and Debug unit suites each pass **4,460 on .NET 10 / 4,100 on .NET 8**.
Expanded contracts cover explicit tree selection, source ownership on success/failure, retained
snapshots and closures, mixed-source identity, and normal/debug saved compilation. Existing tests
retain key/action focus, stable sorting, schema annotations, accumulator provenance and error codes.
The new retained-result test initially used a document-ordering path to assert merge sequence order;
its corrected simple-map expression asserts sequence order explicitly. Both logs are retained.

The unchanged exact 1 GB candidate tree trial completes with correct output under the original
4 GiB heap guard, with full-lifetime RSS **3,073,327,104 bytes**. Its failed predecessor is retained.
The serial comparison is complete: **25 attempts / 24 valid observations**, with the one failed
predecessor retained. Every candidate mode at exact 1 MB, 100 MB and 1 GB has three valid observations
with matching output fingerprints, full input counters and equal effective heap capacities.
At 100 MB, median tree elapsed time changes from **12,896.914 to 3,902.390 ms** and median full-lifetime
RSS from **2,088,501,248 to 531,922,944 bytes**. Streaming median elapsed time changes from
**3,426.938 to 3,442.933 ms**; the candidate stream/tree ratio is **0.882263**. At 1 MB the ratio is
**1.111226**, and at 1 GB **0.316684**. These are isolated merge controls, not the complete seven-workload
acceptance matrix or a universal speedup. Final branch strict inventories, freshly generated saved
NativeAOT stylesheets and full hosted qualification remain required.
Evidence is under `artifacts/p7-completion/pull-merge-prototype/` and `pull-merge-*` logs.
This result does not complete P7 or replace any original memory/throughput/public integration gate.


Hosted qualification for the preceding `ef57091` growth change retains the original populations
and limits. The nightly matrix passes **16/16 workloads**, with all **48 children** independently
validated and maximum absolute RSS **40,288,256 bytes**. The push matrix passes **0/16**: fourteen
measured children consume the full input and validate output, while merge and fork stop at the
startup budget check. All 32 push controls validate and match fingerprints. Archive digests and
raw reports are retained under `tree-growth-linux-{nightly,push}`.
These results qualify that preceding source, not the subsequent compiler change.


The preceding growth commit's ordinary hosted CI finishes with all four jobs passing at run
`40998445809`. Its complete original Linux benchmark (`40998473014`) validates **105/126** attempts;
all **21 exact 1 GB tree trials** still throw out-of-memory exceptions. Six workload stacks fail
while loading the input, including resizing the compact name column; merge fails creating snapshots.
The 100 MB stream/tree ratios are identity **1.041535**, adjacent **0.633517**, accumulator **1.381856**,
iterate **0.789006**, merge **0.311745**, fork **3.462467** and source-document **1.042586**. Identity,
accumulator, fork and source-document miss the original target. All trials, errors, fingerprints,
effective heap checks and the verified benchmark archive are retained under `tree-growth-linux-benchmark`.


Final branch qualification includes four additional fallback anchor-position/size contracts through
both API paths. The default Strict policy is preserved: the non-guaranteed `last()` selection is
rejected there, and its anchor focus is verified under the explicitly selected public Fallback policy.
Final full Release and Debug suites each pass **4,464 on .NET 10 / 4,104 on .NET 8**. All four strict
inventories retain **2,695/16/1**, zero unmatched codes and verified frozen-engine audits. Every
per-case outcome and detail matches the preceding growth checkpoint, not just its aggregate totals.

Freshly regenerated saved probe stylesheets and a warnings-as-errors NativeAOT build validate all
**48 smoke children** and all four canonical 100 MB / 1 GB merge controls. Inspection confirms the
saved tree merge now calls the pull merge helpers without opening a forward source; the stream
assembly retains forward opening. At 1 GB, tree/stream full-lifetime RSS is **2,870,181,888 /
26,574,848 bytes**. Tree mode consumes the complete input before the first output write; stream mode
writes after **122,880 input bytes** at both measured sizes. These controls retain the original
4 GiB heap guard and correct independent outputs/native flags, but do not replace the complete
original acceptance matrices. The native image digest and all raw trials are retained in
`pull-merge-qualification-state.json` and `native-pull-merge/`.


### Fixed-size pages for large source-node columns

The complete preceding Linux benchmark still fails during large input-column resizes. Large source
trees now append fixed-size pages after their first page; kind, parent, exclusive subtree end, name
and data retain the same linear node indexes. Small source trees and temporary trees keep flat
arrays. Attributes, namespace declarations, pooling, input generation, independent oracles and
all acceptance limits are unchanged. Ref-returning accessors cover both layouts, including the
optimized named following scan and accumulator traversal clocks.

Isolated full Release and Debug suites each pass **4,466 on .NET 10 / 4,106 on .NET 8**. Additional
comparisons cross the first two page boundaries and verify every content node, values, attributes,
namespaces, parent/sibling axes and named following scans against `XPathDocument`. The expanded
predecessor test repeatedly compared whole parent string values, making large cases quadratic;
the interrupted log is retained. The linear version compares every node value once and checks
parent metadata without rereading its whole subtree for every child.

All four public-loader diagnostics retain matching record, node, attribute and value counts. At
exact 1 GB, node capacity falls from **87,266,902 to 69,730,304**, avoiding **298,122,166 bytes** of
unused node-column capacity. Diagnostic entry points are excluded from this branch and from the
canonical benchmark image. Layout/GC snapshots are diagnostics, not acceptance measurements.

All **42 alternating canonical 100 MB tree controls** validate output. Median times range from
**0.5% faster to 3.6% slower**; measured RSS decreases by workload. Six of seven exact 1 GB candidate
tree trials validate under the original 4 GiB managed-heap guard. Adjacent grouping still runs out
of memory materializing its groups. All **49 attempts / 48 valid transformations**, four loader
diagnostics, images, source patch and independent audit are retained in `node-pages-prototype`.
These local macOS results do not establish that the Linux failure is fixed. Final branch strict
inventories, NativeAOT/saved checks and the complete original hosted matrices remain required.

A local Linux diagnostic was stopped after its Docker inventory showed other applications using
most VM memory. Only the new diagnostic container was stopped. Its aborted output is retained and
is neither reproduction nor acceptance evidence; large Linux qualification remains hosted.


Final branch Release and Debug suites retain **4,466 / 4,106** passes. All four strict inventories
retain **2,695/16/1**, zero unmatched codes and verified frozen-engine/input audits. Every case's
outcome and detail matches the preceding pull-merge checkpoint. The shipping test entry point
remains unchanged; only the isolated public-loader diagnostics contain the layout command.

A warnings-as-errors NativeAOT build uses the preceding compiler's saved entry points, validating
all **48 smoke children** and four exact 1 GB identity/fork tree/stream controls under the original
4 GiB heap guard. Full-lifetime tree RSS is **2,435,940,352 / 3,769,581,568 bytes**, and stream RSS is
**25,968,640 / 26,509,312 bytes**, with independent outputs and both native flags verified. Image
hashes, full reports and audits are retained under `native-node-pages` and the qualification state.
These local controls do not replace the original complete hosted matrices or prove Linux completion.

The preceding pull-merge commit's CI passes NativeAOT, ordinary performance and Ubuntu. Its first
Windows attempt cancels `ContextFreeRawNamedTemplateFinishesAfterItsFork` at the existing two-second
guard; the complete failed log is retained. The exact-source Windows retry passes its unit stage
with no guard or source changes and continues remaining checks. Its original Linux push matrix
still passes **0/16 budgets**: fifteen measured transformations finish correctly, while fork stops
at startup. All 32 controls validate and match. Nightly independently validates **16/16 budgets /48 children**, with maximum absolute RSS
**39,907,328 bytes**; its verified raw archive is retained. No acceptance budget or runtime liveness guard is relaxed.


### Grounded adjacent-group boundaries and independent frames

The remaining local 1 GB tree failure materializes every adjacent `Group` and member list. The
compiler now computes boundaries for grounded, unsorted adjacent grouping and consumes independent
group frames with a cursor. A supplied loop context-size binding preserves the total for `last()`
without caching all frames. Forks capture the scalar count and the current frame; retained frames
own their member lists and keys, without retaining the cursor or unrelated tuple metadata after
completion. Sorted and other grouping paths retain their existing behavior. Key evaluation remains
once per population item, with comparison/cardinality errors raised before actions start.

Untyped node keys already stored as strings compare through the canonical collation without filling
per-row atom caches. Each consumed frame receives its own atomic key; mixed typed/composite keys
retain the canonical comparison rules. Explicit contracts cover population/action focus, repeated
member reads, retained closures across call-site reuse, fork capture, saved normal/debug compilation,
API-specific exception types/codes, cancellation, empty input and metadata release. The initial
new legacy exception expectation was checked against the unchanged baseline and corrected to its
existing raw `XdmException`; the modern API retains `XsltDynamicException`. Failed logs are retained.

The first cursor passes all candidate 1 MB / 100 MB / 1 GB cases with three observations per mode,
retaining its predecessor's 1 GB out-of-memory failure (**25 attempts /24 valid**). The selected
uncached-key variant also passes every trial (**25/25**), including three observations per candidate
size/mode under the original 4 GiB heap guard. At 100 MB, compared with the first cursor, tree median
time changes from **1,485.868 to 1,345.641 ms** and median RSS from **584,515,584 to 516,325,376 bytes**.
The selected 1 GB tree trials use **3,011,248,128–3,590,651,904 bytes** RSS; these local observations
do not establish Linux qualification. Isolated full Release and Debug suites each pass **4,494 on
.NET 10 / 4,132 on .NET 8**. Both prototypes, all unsuccessful attempts, immutable images, source
patches and independent audits remain under `adjacent-cursor-prototype`.

Final branch strict inventories, newly compiled saved NativeAOT stylesheets and the full original
hosted memory/throughput matrices remain required. This change does not close P7 or relax any gate.


Final branch Release and Debug suites retain **4,494 / 4,132** passes. All four complete strict
inventories retain **2,695/16/1**, zero unmatched codes, verified frozen-engine/input audits and
identical per-case outcomes/details to the preceding node-page checkpoint. Fresh saved NativeAOT
stylesheets call the new tree cursor/count helpers; the stream style retains its forward group
helpers. The warnings-as-errors native build validates all **48 smoke children** and four canonical
100 MB / 1 GB adjacent tree/stream controls. At 1 GB, native tree/stream full-lifetime RSS is
**3,741,237,248 /26,624,000 bytes** with independent outputs, native flags and the original heap guard
verified. The complete reports and native image digest remain in `native-adjacent-cursor`.

The final canonical JIT image also validates **all seven exact 1 GB tree workloads**, one observation
each, under the original 4 GiB managed-heap guard. Records and output fingerprints match the prior
valid controls. Identity, adjacent, accumulator, iterate, merge, fork and source-document peaks are
**2,553,692,160 /3,603,841,024 /2,594,193,408 /2,604,171,264 /2,086,240,256 /3,469,066,240 /
2,592,915,456 bytes**. This is retained local diagnostic evidence, not the original full paired
three-round hosted matrix. All seven trials and independent audit are under `final-seven-large`.

Hosted qualification of preceding commits remains source-specific. All four ordinary CI jobs pass
for `fecbc83`; its nightly passes **16/16 budgets /48 children**, maximum absolute RSS **44,961,792**.
Its push passes **0/16 budgets**, with fifteen correct complete transformations and merge stopping
at startup. All 32 controls validate. The earlier `8734c4d` full Linux benchmark is terminal with
**114/126 valid attempts** and twelve retained large-tree out-of-memory failures. Its 100 MB ratios
are identity **1.058307**, adjacent **0.674070**, accumulator **1.374825**, iterate **0.814353**, merge
**0.882939**, fork **3.305520** and source-document **1.072508**. No failed trial, budget or throughput
shortfall is omitted. Final-source hosted qualification and every remaining original P7 gate stay open.

### Linux memory attribution and literal simple content, 2026-10-30

All four ordinary CI jobs pass for `2b12932`. Its complete original hosted nightly matrix independently
validates **16/16 budgets /48 children**, with maximum absolute RSS **40,128,512 bytes**. Its original
push matrix passes **0/16 budgets**: fifteen measured transformations consume all 209,715,200 bytes
and validate output; fork stops during warmup. All 32 controls validate and match. The artifacts and
independent audits are retained under `adjacent-cursor-linux-push` and `adjacent-cursor-linux-nightly`.
Its full original benchmark remains active; these results do not qualify subsequent source changes.

The preceding `fecbc83` full hosted benchmark is terminal with **123/126 valid attempts**. Only the
three adjacent tree trials at exactly 1 GB fail, each with a retained out-of-memory result. The
100 MB stream/tree median ratios are identity **1.116026**, adjacent **0.722598**, accumulator
**1.658151**, iterate **0.862772**, merge **1.015752**, fork **3.679577** and source-document
**1.087057**. Five workloads still miss the original throughput target. Every trial, original
configuration and independent audit is under `node-pages-linux-benchmark`.

A diagnostic-only `dlopen` interposer resolves the first ICU load in a retained Linux ARM64 native
image to `StreamingProbe.RunCommand` accessing `CultureInfo.InvariantCulture`, before stylesheet
loading or execution. That command method is byte-identical to the current source. The historical
image and platform remain explicit: this establishes that caller in that image, not current hosted
x64 memory qualification or an irreducible platform minimum. Globalization remains enabled. Raw
offsets, symbolized callers, image/source fingerprints and output are under `linux-native-callers-2b12932`.

A separate unchanged-builder prototype avoids parsing the internal absent-context sentinel. All
18 alternating 200 MiB identity/merge/fork controls validate with the original 20 MiB managed guard,
but macOS median RSS improves by only **16–128 KiB**. No Linux budget fix is established, so that
prototype is not adopted. Its source, initial test-launch failure, focused contracts, image hashes
and controls remain under `absent-builder-prototype`.

Current-engine allocation traces identify repeated temporary compact trees/builders beneath the
canonical identity row template. A compiler prototype substitutes a text constructor's string value
when its simple-content population contains exactly that one text constructor and the separator is
constant. Its first 24 alternating 100 MB identity/source-document tree/stream controls all validate
the original input, output fingerprints and 4 GiB managed guard. Initial identity tree/stream medians
change from **2,211.094 /2,320.868 ms** to **1,977.197 /2,100.042 ms**, and source-document from
**2,254.669 /2,335.648 ms** to **2,036.396 /2,132.382 ms**. These local diagnostic cohorts still miss
the original stream-no-slower-than-tree target. Allocation-tick estimates fall from about **3.48 GB
to 2.13 GB** at 100 MB input; sampling is not an exact per-type allocation accounting or an
uninstrumented acceptance timing. Raw traces, named stacks, frozen images and outputs are retained.

Review of that initial prototype finds that dropping the constructor also drops its streamed
cancellation checkpoint. A deterministic test cancels after actual output inside an already-running
loop; the negative control completes without throwing, proving the regression. The adopted candidate
therefore applies only to literal text with a constant separator and calls a stateful, allocation-free
`X31SimpleContentText` checkpoint for streamed compilation. Dynamic text/separators retain their
existing evaluation path. The new helper is recorded in the unshipped runtime ABI; the saved format,
function IDs, fixtures, oracles, GC/globalization settings and all original limits remain unchanged.
Corrected contracts cover text merging/empty nodes, distinct retained text identities, raw text,
attributes/comments/PIs, dynamic separator errors, rollback, both API paths, saved Release/Debug
entry points and cancellation.

The initial cancellation test expected an unwrapped exception; the established legacy API wraps it
in `XslTransformException` with an `OperationCanceledException` inner cause. That assertion is corrected
to the existing contract, with failed logs retained. Full corrected-candidate Release and Debug
units each pass **4,500 .NET 10 /4,138 .NET 8** checks. All four complete strict inventories retain
**2,695/16/1**, zero unmatched codes, verified frozen-engine/input audits and unchanged per-case
outcomes/details. The added retained-text identity contract also passes in current-branch focused
Release/Debug reruns through both APIs and saved compilers.

A fresh warnings-as-errors NativeAOT build regenerates all 32 saved entry points; its identity
stylesheet calls `X31SimpleContentText`. All **48 smoke children** and **12 exact 100 MB/1 GB
identity/source-document/copy-select tree/stream controls** validate their independent outputs,
effective heap guards and native flags. The original 4 GiB guard is unchanged for large comparisons.
The full seven-workload, three-round native 100 MB matrix validates **42/42 observations**; its
stream/tree ratios are identity **0.985232**, adjacent **0.654874**, accumulator **1.291422**, iterate
**0.614513**, merge **0.794948**, fork **3.654435** and source-document **0.976672**. The original
target still fails on accumulator and fork; the report's descriptive 5% winner band is not a waiver.

All **12** alternating corrected-candidate native identity/source-document controls consume exactly
209,715,200 bytes and preserve output fingerprints under the original 20 MiB managed guard, with
the diagnostic RSS guard explicitly disabled. Identity median time changes from **4,136.026 to
3,815.442 ms**, and source-document from **4,166.396 to 3,776.548 ms**. Median RSS changes from
**20,119,552 to 19,972,096 bytes** and **20,086,784 to 20,135,936 bytes** respectively. These small,
mixed RSS differences establish no Linux memory fix. A fresh corrected-candidate allocation trace
preserves exact output and records about **2.13 GB** sampled allocation, **38.8%** below the retained
baseline estimate. Final reports, source/binary hashes and independent audits are under
`text-content-prototype` and `native-literal-content`.

These are local diagnostics, not the original full hosted acceptance matrices. Every original P7
requirement, including Linux push memory, all throughput targets, forwarding/public integration
and final full review/merge/release, remains required.

### Hosted large-input completion and loader attribution, 2026-10-30

The complete original hosted benchmark for `2b12932` independently validates **126/126 observations**:
all seven canonical workloads at exact 1 MB /100 MB /1 GB, three observations per tree/stream mode,
with the original **4,294,967,296-byte** effective managed-heap guard. Every record count/output
fingerprint matches preceding valid controls. Earlier out-of-memory trials remain retained. This
establishes the large-tree repair on Linux for that source, without substituting local diagnostics.
Its 100 MB stream/tree ratios are identity **1.138184**, adjacent **0.849365**, accumulator **1.512725**,
iterate **0.873307**, merge **0.976321**, fork **3.652861** and source-document **1.113490**. Four
workloads still fail the original throughput target. The report and audit are under
`artifacts/p7-completion/adjacent-cursor-linux-benchmark/`.

All four ordinary CI jobs pass for `1c98c38`. Its original hosted push matrix validates every full
209,715,200-byte transformation and all 32 controls, but still passes **0/16 memory budgets**.
Absolute peaks range from **24,064,000 to 25,559,040 bytes**. This improvement in completion count
does not establish a controlled source-change effect or waive the failed budget. The downloaded
artifact and independent audit are under `literal-content-linux-push`.

A compact annotation prototype reduces streamed frame size from **184 to 160 bytes** and the 100 MB
identity allocation estimate from about **2.13 GB to 1.96 GB**. Focused tests preserve insertion/removal
order, opaque node payloads, concurrent independent updates, allocation-free single annotations,
discarded-value lifetime, replay, accumulators and fork ownership. All **18** alternating native
200 MiB identity/accumulator/fork controls validate outputs and the original managed guard. However,
median RSS rises by **144 /16 /80 KiB**, identity/accumulator times remain flat, and fork's observed
median improvement is only about **1.5%**. This does not usefully advance the original gates; the
prototype is not adopted. Source, contracts, size traces and all controls remain under
`node-annotations-prototype`.

The opt-in identity memory diagnosis now also brackets saved-type initialization, generated-code
attribute reflection, static fields/data, execution-method lookup/delegate binding, static-data
deserialization and dispatcher binding. `--loader-details` adds these checkpoints only to an isolated
source copy and records its generated source hashes. Original saved assemblies, fixtures, output
oracles, globalization, heap guards and all acceptance jobs remain unchanged. The generated observer
and its temporary API manifest entries are absent from the production library.

The first native trace exposes a diagnostic ordering assumption: the CLR's `BeforeFieldInit` type
initializer runs after generated-code attribute lookup here, rather than before `Load`. The revised
validator requires every checkpoint, the ordered initializer block, the ordered loading operations,
and initialization before the version-dependent field lookup; it accepts each legal early/late
initialization point. It rejects missing, duplicate, reordered, malformed or uninstrumented records.
The initial rejected report is retained. The runtime's initialization timing is not changed to fit
the diagnostic schema.

A warnings-as-errors native diagnostic build validates both 10 MiB and 200 MiB outputs under the
original 20 MiB managed guard, with all **20 checkpoints** present and all instrumented source/image
hashes verified. The observer adds measurement overhead; intervals are not exclusive attribution
of the uninstrumented process peak. MacOS validation and the default `v1.1.3` source anchors are
verified; current-source hosted Linux diagnosis is still required. Evidence is under `loader-details`.
Full P7 remains active, including original Linux memory/throughput, forwarding/public integration,
final review, merge and release requirements.
