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

### Streamed accumulator frame storage, 2026-10-30

All four ordinary CI jobs pass for `efcb7f8`. The original hosted benchmark for runtime checkpoint
`1c98c38` is terminal with **126/126 valid observations**, all three original sizes/rounds/modes and
the original **4,294,967,296-byte** effective heap guard. Every output fingerprint matches preceding
valid controls. Its 100 MB stream/tree ratios are identity **1.257418**, adjacent **0.877160**,
accumulator **1.569434**, iterate **0.856084**, merge **0.996829**, fork **3.773996** and source-document
**1.171731**. Four original throughput targets still fail. Its original nightly passes **16/16 budgets
/48 children**, maximum absolute RSS **40,230,912 bytes**; push still passes **0/16 budgets** despite
all sixteen measured transformations consuming the full input with correct outputs. These results
remain specific to that runtime checkpoint.

The hosted loader diagnosis completes nine control and nine instrumented runs, with all **20**
checkpoints present. Independent audit checks every raw output, paired fingerprint, effective guard,
checkpoint record and retained mapping digest; all **1,507 tracked source files** match the pinned
source, and all **seven injected source hashes** match the independently prepared local copy. Native
images were not included in the hosted artifact, so their reported digests cannot be checked against
the actual files independently. Loader intervals do not account for the full observed growth:
collection/setup and sustained transformation also increase resident memory. Observer overhead and
overlapping intervals prevent exclusive attribution or an irreducible-platform-floor conclusion.
The diagnosis establishes no original memory-budget pass. Evidence: `loader-details-linux`.

The accumulator allocation trace estimates about **9.98 GB** allocated for the canonical 100 MB
streamed workload. Per-node name dictionaries/entry arrays and navigators created for empty rule
phases account for avoidable allocations. The implementation stores the first accumulator's expanded
name and phase directly and creates an ordinal dictionary only for additional names. It caches
whether each rule phase exists and avoids navigator/rule dispatch setup for an absent phase while
still initializing and publishing its unchanged value. Existing locks, cycle/fault behavior, phase
ownership, snapshot encoding and publication to fork/replay readers are preserved. Public APIs,
saved formats, compiler analysis, fixtures, oracles and acceptance settings are unchanged.

The isolated prototype's twelve alternating JIT tree/stream observations validate exact 100 MB
inputs, the original 4 GiB managed guard and identical outputs. Accumulator tree/stream medians
change from **2,940.816 /4,097.626 ms** to **2,922.292 /3,516.270 ms**. All six alternating native
200 MiB streamed controls also validate; median time changes from **5,985.498 to 5,479.583 ms** and
median absolute RSS from **20,299,776 to 20,250,624 bytes**, with the original 20 MiB managed guard
and diagnostic RSS guard explicitly disabled. Sampled allocation falls to about **8.31 GB**, a
**16.7%** reduction in the estimate. These diagnostics establish no original Linux budget or full
throughput pass. The retained JIT report's patch digest references the initial, non-compiling
enumerator-accessibility patch. Its report is preserved; the independent audit records that error,
the corrected patch digest and a matching frozen-candidate PDB source checksum. Evidence:
`accumulator-slots-prototype`.

Six contract areas cover single/multiple counters, namespace/case distinctions, empty start/end
phases, copies and deferred snapshots, fork readers, and failures/cancellation. All **16 new cases**
pass on .NET 8/10 through both public APIs and saved .NET 10 entry points, with normal/debug compilers.
The first fixture run is retained: its combined consuming expressions, fork-child syntax and resolver
return type were invalid and corrected without changing production behavior. Cancellation is triggered
after actual sink output and must stop before input EOF. Full Release and Debug units each pass
**4,516 .NET 10 /4,154 .NET 8** checks. All four complete strict inventories retain **2,695/16/1**,
zero unmatched codes and identical per-case definitions, outcomes, details and dependencies.

The fresh final-source NativeAOT build regenerates all **32 saved entry points** and validates
**48/48 smoke children** plus **12/12 exact 100 MB/1 GB accumulator/fork/identity tree/stream controls**.
The large controls preserve the original 4 GiB effective managed guard and match preceding hosted
output fingerprints. The final native image SHA-256 is
`3fdddbfaf298da4a15f8ce7df15e5a3e02383893c8620baf85c57c46cfdb617d`.

Final-source paired measurements independently validate all twelve JIT and six native observations,
their raw outputs, image/engine/source hashes, effective guards and matching fingerprints. The frozen
JIT candidate's PDB checksum also matches the actual final runtime source. JIT tree medians remain
**3,078.786 /3,085.799 ms**; streaming improves from **4,279.657 to 3,650.100 ms** (**14.7%**).
Native streaming improves from **5,979.555 to 5,457.543 ms** (**8.7%**), with median absolute RSS
**20,299,776 /20,234,240 bytes**. JIT RSS changes by **112 KiB**, native RSS by **-64 KiB**; neither
establishes an original Linux memory-budget pass. Diagnostic RSS guards remain explicitly disabled.

The complete final-source local native 100 MB matrix validates **42/42 observations**. Stream/tree
ratios are identity **0.984842**, adjacent **0.648028**, accumulator **1.161259**, iterate **0.627506**,
merge **0.789350**, fork **3.665512** and source-document **1.001595**. The original target still fails
on accumulator, fork and source-document, including the small source-document overrun; no reporting
band waives a failure. Final-source evidence and independent audits are under
`native-accumulator-slots`, `accumulator-slots-final-measurements` and `accumulator-slots-tested-engines`.

Every original P7 requirement retains its completion gate. Current-source hosted qualification,
remaining memory/throughput repairs, actual forwarding/public integration and final full review,
merge and release remain required. This change does not close P7 or establish a streaming-support claim.


### Atomic buffered fork reads and diagnostic caller repair, 2026-10-30

All four ordinary CI jobs pass for `27b417a`. Its original hosted nightly independently passes
**16/16 budgets /48 children** at exact **2,147,483,648-byte** inputs with the original **209,715,200-byte**
absolute RSS and managed guards. Maximum absolute RSS is **40,255,488 bytes**. All raw outputs and
preceding output fingerprints match. Its original push still passes **0/16 budgets**: fourteen full
transformations validate, while merge and climbing-union stop during warmup; all 32 controls validate.
Absolute peaks are **21,901,312–25,423,872 bytes**. The original full paired benchmark independently
validates **126/126 observations**, all seven workloads, original sizes/modes/rounds and the original
4 GiB effective heap guard. Every raw output and preceding fingerprint matches. Its 100 MB ratios
are identity **1.235994**, adjacent **0.855578**, accumulator **1.298077**, iterate **0.848990**, merge
**0.973949**, fork **3.667523** and source-document **1.238591**. Four original throughput targets
still fail. The benchmark image file was not included in its artifact; its reported image digest
cannot be checked against that file independently. These observations do not qualify subsequent
source changes. Audits: `accumulator-slots-linux-*`.

Fresh fork allocation and native CPU traces use the actual `27b417a` engines and preserve independent
outputs. CPU sampling uses exact 1 GB input and the original 4 GiB guard; observer overhead excludes
it from acceptance timings. Source-publication waits remain prominent. A separate immutable
header/event representation experiment passes focused contracts and all paired outputs, but native
median time stays flat and median RSS rises **464 KiB**. It is not adopted. Its rejected measurements
remain under `fork-event-prototype`; current-source traces are under `fork-allocation-profile-27b417a`
and `fork-cpu-profile-27b417a`.

Normal reads of already-published tee events now claim positions atomically before releasing their
remaining-reader count. Both the buffered path and the gated path use the same claim operation;
detachment also claims its position atomically. Batch payload/count initialization precedes release
publication. The last reader still releases the slot under the ring gate. Skipping, source production,
capacity, accepted-prefix faults and caller-owned I/O retain their existing contracts. This introduces
no spin policy, deferred payload retention or speculative source reads.

Review exposes an initial race between the fast path and the old non-atomic gated path. Concurrent
aliases stall at capacities one, seven and 64; the failing logs and initial patch are retained. The
corrected contracts cover concurrent normal/mixed skip reads, concurrent detachment and owner disposal.
All **eight new cases** pass on the unchanged frozen engine and the corrected implementation. Full
Release and Debug units each pass **4,524 .NET 10 /4,162 .NET 8** checks. All four strict inventories
retain **2,695/16/1**, zero unmatched expected codes and identical per-case definitions/outcomes/details.

The isolated prototype validates twelve paired JIT observations, six native 200 MiB observations and
a twelve-observation native 100 MB confirmation. The confirmation's native stream median changes from
**3,731.015 to 3,575.736 ms** (**4.2%**), and full-child CPU from **5,304.909 to 5,114.123 ms** (**3.6%**).
Those CPU counters include startup, warmup and oracle generation. Image/source/PDB hashes and all raw
outputs are independently verified; all failed and outlying trials remain retained.

Fresh final-source NativeAOT generation validates all **32 saved stylesheets**, **48 smoke children**
and **12 exact 100 MB/1 GB accumulator/fork/identity tree/stream controls**. Large controls retain the
original 4 GiB managed guard and preceding hosted fingerprints. Native image SHA-256:
`af8640d22b88d674ec0645e48672168b084f2c542e0cca6fb78bb7406a18ad02`.
Final-source paired native 200 MiB stream medians change from **8,174.953 to 7,698.730 ms** (**5.8%**),
with median absolute RSS **20,594,688 /20,611,072 bytes**; these diagnostic RSS guards are disabled.
JIT stream medians are **4,830.923 /4,869.320 ms**, so no JIT speedup is claimed. All eighteen paired
outputs validate, as do their raw outputs, image/source/PDB hashes and effective guards.

The complete final-source local native 100 MB matrix validates **42/42 observations**. Stream/tree
ratios are identity **0.977623**, adjacent **0.634149**, accumulator **1.135194**, iterate **0.623450**,
merge **0.806387**, fork **3.434250** and source-document **0.957150**. Accumulator and fork still fail
the original throughput target. No reporting band waives a failure. Full final-source evidence is in
`tee-fast-read-final-*`, `tee-fast-read-tested-engines` and `native-tee-fast-read`.

The broader review also reproduces a diagnostic integration defect: the optional identity-allocation
comparison omits newly required workload/loader arguments and crashes before measurement. The caller
now explicitly selects its original identity/seven-checkpoint mode. A real failing-executable test
exercises the caller/controller boundary and requires all twelve failed child reports to remain
invalid and retained. It fabricates no successful native measurements. All **44 streaming-harness
tests** pass on macOS, with one Linux-only check skipped; the initial integration failure is retained.

Original Linux push RSS, every throughput target, current-source hosted qualification, actual
forwarding/public integration, final full review/merge and release remain required. P7 stays active.


### Invariant numeric initialization, 2026-10-30

All four ordinary CI jobs pass for `c9fcb00`. Its hosted nightly independently passes
**16/16 budgets /48 children**, with exact original inputs/guards and a maximum absolute RSS of
**40,226,816 bytes**. Its push passes **0/16 budgets**, although all sixteen full transformations
and all 32 controls validate. Absolute push peaks are **24,031,232–25,571,328 bytes**. Every raw
output and preceding output fingerprint is checked. Evidence: `tee-fast-read-linux-push` and
`tee-fast-read-linux-nightly`. These hosted results qualify that checkpoint, not later source.

Fresh small Linux ARM64 processes show that both `CultureInfo.InvariantCulture` and
`NumberFormatInfo.InvariantInfo` initialize ICU during numeric parsing. A read-only instance from
[the BCL invariant numeric constructor](https://learn.microsoft.com/en-us/dotnet/api/system.globalization.numberformatinfo.-ctor?view=net-10.0)
does not. The runtime now shares that provider for XDM integer/decimal/floating conversions and
climbing-set plan indices. No project-defined separators, symbols or domain values are introduced.
Captured numbers and accumulator priorities reuse their typed double/integer/decimal values instead
of formatting and reparsing them. Float and other items retain the previous lexical conversion;
float-to-double rounding, canonical NaN bits, signed zero and conversion errors have explicit tests.

Native caller traces establish three additional initialization paths before any memory claim:
`Assembly.GetName()` constructs a culture just to retrieve the engine version; the probe's lazy
P/Invoke library search initializes globalization; and JSON double formatting initializes it after
the probe's internal RSS sample. The external full-lifetime counter correctly rejects that last
prototype despite its internal sample being below the limit. Its failed exact-size control remains
retained. No counter is moved or subtracted.

The SDK now records the effective assembly-version property as metadata. Canonical version text
avoids assembly-name construction and an identical saved-version string proves the existing version
comparison directly. Differing saved versions retain the original `Version` comparison and exception
behavior. Noncanonical or absent metadata uses reflection so compiler normalization stays authoritative.
Five isolated builds verify full, short, padded, wildcard and metadata-disabled versions against the
actual assembly header. Public signatures and saved-data format 6 remain unchanged.

The native probe directly binds the same Unix `getrusage` entry point using
[documented NativeAOT interop](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop).
Its numeric CLI/fixture operations use BCL invariant data; report doubles retain validated numeric
JSON with the BCL's round-trip digits and normal rejection of non-finite values. The source-generated
report is checked against ordinary BCL JSON representations and bitwise round trips. Input bytes,
stylesheets, independent output oracles, full-lifetime internal/external counters and effective guards
are unchanged. Globalization stays enabled and available.

The final full Release and Debug units each pass **4,537 .NET 10 /4,171 .NET 8** checks. All four
strict inventories retain **2,695/16/1**, zero unmatched expected codes and identical complete per-case
records. A portable-PDB audit independently matches all nine changed runtime source checksums in the
Linux engine and the Release/Debug macOS engines. Initial malformed diagnostic commands, the ignored
heap-variable spelling, a missing test import and all failed budget trials remain recorded and excluded
from successful qualification. Evidence: `numeric-provider-diagnosis` and `numeric-init-final-*`.

Review also catches a stale version-field anchor in the optional loader diagnostic. The observer now
brackets the selected initializer through a typed callback, preserving either historical reflection
or the current metadata expression. Missing, duplicate and ambiguous anchors fail without rewriting
the source. The initial failing harness log is retained; the corrected harness runs **45 tests**, with
one Linux-only skip on macOS. A real newly compiled instrumented NativeAOT image validates its output,
effective 20 MiB managed guard and all **20 loader/process checkpoints** in their legal order. This
diagnostic keeps RSS guards disabled and establishes no budget pass.

The final-source Linux ARM64 push matrix completes **48/48 children**, validates every full measured
input and matches all preceding output fingerprints. It passes **13/16 original budgets**, with
passing absolute peaks **18,161,664–18,927,616 bytes**. Merge (**24,100,864**), fork (**24,268,800**) and
source-document (**23,252,992**) still exceed the original **20,971,520-byte** absolute limit. Native
traces locate their ICU initialization inside .NET URI scheme handling. URI resolution, resource
identity and globalization semantics remain unchanged. This local ARM64 result is separate from
hosted x64 qualification and is not a comparison against the preceding hosted platform.

Fresh macOS NativeAOT generation validates all **32 saved stylesheets**, **48 smoke children** and
**12 exact 100 MB/1 GB accumulator/fork/identity tree/stream controls**. All raw outputs, native flags,
effective guards and preceding fingerprints are independently checked. The complete native 100 MB
matrix validates **42/42 observations** under the original **4,294,967,296-byte** managed guard.
Stream/tree ratios are identity **0.980455**, adjacent **0.635381**, accumulator **1.114835**, iterate
**0.608041**, merge **0.793123**, fork **3.479671** and source-document **0.955863**. Accumulator and
fork still fail the original target; no reporting band waives either failure. These observations do
not establish a paired speedup against an earlier source. The final Linux/macOS native image hashes
are respectively `dc4db3dc4f744b6c3fe96ca29ebb090cf748b5a82b7cd4e87573854ffea59754` and
`2c66b8ae303a05fb2276e40b4b668480d591bb728cc28b382abdb59caebe37a0`.

Every original Linux memory/throughput, forwarding/public integration, final review/CI, merge and
release requirement remains in force. P7 stays active. The remaining URI-path footprint and measured
throughput failures require further implementation; this checkpoint makes no streaming-support claim.


### Explicit native report encoding, 2026-10-30

All four ordinary CI jobs pass for `bd0031e`. Its hosted nightly independently passes
**16/16 original budgets /48 children**, with maximum absolute RSS **40,235,008 bytes**. Its push
passes **0/16 budgets**, although every full transformation and all 32 controls validate. Thirteen
workloads report an internal peak below the budget but exceed it in the external lifetime counter.
The hosted full paired benchmark is still running; it is not restarted. These results qualify that
preceding checkpoint, not later source. Independent audits are under `numeric-init-linux-*`.

An isolated Ubuntu x64 diagnostic retains the actual native executable/debug symbols and matches
its source and artifact hashes. Its first ICU caller is console charset discovery:
`Console.WriteLine -> Console.OutputEncoding -> EncodingHelper.GetCharset -> TextInfo`. It runs
after the internal peak sample. A high-RSS parent control affects both counters and does not explain
the late-only increase; no counter is moved, subtracted or replaced. The diagnostic workflow succeeds
in run **41043588197**, with evidence under `native-initialization-x64`.

The native probe now explicitly selects UTF-8 without a BOM for its JSON process protocol. This
prevents host charset discovery while leaving globalization available. A Linux ARM64 .NET 10.0.14
control with `LANG`/`LC_ALL=en_US.UTF-8` reproduces the preceding ICU stack; the candidate preserves
its output fingerprint without that stack. All **48 macOS smoke children** and four alternating small
baseline/candidate controls validate. Those small controls show no material RSS regression and do not
establish a throughput improvement. A portable-PDB checksum matches the changed program source.

The complete local Linux ARM64 original push matrix verifies all **48 children**, all full input
bytes, raw UTF-8 reports and preceding output fingerprints. It passes **13/16 budgets**. Remaining
absolute peaks are merge **24,186,880**, fork **24,510,464** and source-document **23,314,432 bytes**,
against the unchanged **20,971,520-byte** limit and managed guard. Their URI initialization still
requires repair. The macOS smoke run has no acceptance RSS limit and makes no memory-budget claim.
Linux/macOS image hashes are respectively
`9c02f737ad20c43f6e375719d29c53da551a181a0de808d0dabddc7264ed6c08` and
`d249fc79cb7151ee00578e57abab5cff8d727554d3c7390de0e10826bc3f0d5d`.
Evidence: `uri-footprint-bd0031e/console-utf8-independent-audit.json` and retained raw files.

Three isolated alternatives are rejected with their failures retained: immediate tee-payload release with deferred head-metadata reclamation
provides no measured gain; disabling NativeAOT dehydration worsens small-process RSS; and
serialization-only JSON source generation lacks metadata required by the report converter and fails
at runtime. None is adopted. No fixture, URI behavior, input/oracle, counter, memory limit, GC policy
or public streaming claim changes. Current-source hosted qualification and every original P7 gate
remain required before final review, merge and release.


### Reader bridge metadata retention, 2026-10-30

Hosted x64 qualification of `06dacc5` independently verifies **13/16 original push budgets**,
**48 children** and **32 valid controls**. Fifteen measured transformations validate their complete
input/output fingerprints; merge stops during warmup and makes no full-input correctness claim.
Remaining absolute peaks are merge **21,950,464**, fork **25,665,536** and source-document
**24,264,704 bytes**. This establishes the console-encoding repair on the hosted platform without
closing the memory gate. Evidence: `console-utf8-linux-push/independent-audit.json`.

The preceding `bd0031e` paired hosted benchmark has now finished. All **126 observations** validate
the original sizes, modes, three rounds, 4 GiB effective managed guard, raw outputs and preceding
fingerprints. The original 100 MB stream/tree ratios are identity **1.126770**, adjacent **0.869563**,
accumulator **1.254030**, iterate **0.847368**, merge **0.962099**, fork **3.664317** and source-document
**1.105897**. Four targets fail. The actual benchmark image is absent from the artifact, so its
reported image hash is not independently verified. Evidence: `numeric-init-linux-benchmark`. This
older checkpoint does not qualify later changes.

Review identifies unnecessarily broad reflection retention in `XmlReaderInternals`: preserving every
public/nonpublic method and property on `XmlTextReaderImpl` roots parser members that the bridge never
looks up. Each lookup now carries its literal BCL type identity directly so trimming preserves the
requested resolver method and whitespace property. The instance-type check, existing exact member
dependencies, reader policies and fallback behavior remain unchanged; no warning suppression or new
parser implementation is introduced.

Eight alternating small native controls retain correct outputs and the original 20 MiB managed guard.
The isolated native executable shrinks from **14,789,224 to 13,794,080 bytes**, and both small workloads
show about **0.9 MiB lower RSS**. These diagnostic controls have no RSS acceptance limit and establish
no throughput improvement. The first comparison's whole-file binary hashing inflated the Python
parent high-water mark; its results remain retained and excluded. Incremental hashing corrects the
observer without altering any child counter.

Nine shared contracts now verify modern/legacy resolver identity, explicit null resolvers, both
whitespace policies, external-DTD retrieval through captured reader settings and actual provider
read counts. Full Release and Debug units each pass **4,538 .NET 10 /4,172 .NET 8** cases. All four complete strict inventories retain **2,695/16/1**, zero unmatched expected codes and
identical per-case definitions, outcomes, details and dependencies. This inventory comparison does
not independently prove actual forwarding. The same
contracts pass in newly compiled Linux/macOS native consumers; the managed intermediate is rejected.
The initial isolated consumer incorrectly ignored legacy entity-reference events, and its failure is
retained; the corrected fixture honors the reader's captured entity policy. An initial missing-using
compile error is also retained. The permanent native consumer is a test-only friend assembly and CI
retains its publish, run and JIT-rejection logs.

The final-source Linux ARM64 probe verifies **48/48 children**, all sixteen full measured inputs,
32 controls, raw reports and preceding output fingerprints. It still passes **13/16 original push
budgets**. Remaining peaks are merge **23,285,760**, fork **23,511,040** and source-document
**22,556,672 bytes**. No paired full-size speedup or hosted memory pass is inferred. Native image:
`c6f8e9470936287d5a419adf87698647593f6a7a0f807d500e8703786b770d4a`. Source/PDB checks match the
changed reader bridge and both native consumers' shared contract/program files. Full evidence and
failed trials are under `reader-trim-prototype`.

Current-source hosted qualification, the three remaining memory failures, every original throughput
target, actual forwarding/public integration, full final review, merge and release remain open.
URI resolution and globalization semantics, workload fixtures, limits, counters and output oracles
remain unchanged. P7 is not complete.

### Fork header arrays and measurement integrity, 2026-10-30

All four ordinary hosted CI jobs pass for `76f99af`. Its original hosted push matrix passes
**13/16 budgets**, with all 48 children and sixteen full measured outputs verified. Its nightly
matrix passes **16/16 budgets**, with a maximum absolute peak of **39,190,528 bytes**. The complete
paired benchmark validates all **126 observations**, original sizes/modes/rounds, raw outputs,
fingerprints and effective **4,294,967,296-byte** managed guards. Its 100 MB stream/tree ratios are
identity **1.180575**, adjacent **0.837277**, accumulator **1.254481**, iterate **0.844591**, merge
**0.950282**, fork **3.635142** and source-document **1.156506**. Four targets still fail. The actual
benchmark executable is absent from the uploaded artifact, so its reported image hash cannot be
independently verified. Evidence is retained under `reader-trim-linux-*`; this qualifies that
preceding checkpoint, not the later working candidate.

Fork projections now reuse immutable attribute/namespace arrays when their prong's name table
returns the same name references. The first changed atom copies the array; mutable schema snapshots
still receive private storage. Each prong retains its own name table and navigation state. Six new
contracts cover preseeded element/attribute/namespace atoms and independently mutable schema
attributes at tee capacities 1, 7 and 64. The schema test synchronizes both projections before
capturing or mutating their metadata. Existing tee bounds, cancellation, caller-thread I/O and
declaration-order errors remain unchanged.

Two observations per variant using frozen .NET 10 engine/test binaries and a 100 MB fork input
reduce median allocated bytes from **4,842,554,440 to 4,592,118,232**, a reduction of **250,436,208
bytes (5.17%)**. Portable-PDB checks tie both frozen engines to their respective sources. An earlier
unfrozen allocation cohort is retained but superseded because its original binary hash was not
captured before later builds. Six two-CPU Linux ARM64 native stream observations show medians of
**6,638.396/6,503.377 ms** and child CPU **7,764.605/7,550.284 ms** for baseline/candidate respectively.
Their median RSS is **25,088,000/25,153,536 bytes**, establishing no material RSS reduction. A separate
one-CPU paired cohort is retained; its RSS is affected by whole-image hashing in the parent and is
excluded from memory attribution. These small cohorts do not pass the canonical throughput target.

Review reproduces two version defects in actual builds: duplicate reserved metadata selects
`99.0.0.0` despite a `4.3.2.1` assembly header, and oversized numeric metadata is accepted as a version.
The runtime now accepts a single canonical reserved entry whose four components fit the header's
16-bit fields. Ambiguous, invalid, absent and compiler-normalized metadata retains the original
assembly-header fallback. Both reproductions now match their headers; five separate full, short,
padded, wildcard and metadata-disabled builds also match. Sixteen unit contracts cover the metadata
rules. No public signature or saved-data format changes.

Review also reproduces a benchmark observer defect: hashing the entire native executable allocates
it in the parent before child creation. With the same Linux image and input, the original observer
reports **32,256,000 bytes** for the stream child; bounded hashing reports **18,923,520 bytes**.
Raw reports, outputs and artifact fingerprints validate in both controls. The benchmark now hashes
the artifact, engine and controller with bounded reads. A regression executes the controller with
unbounded binary reads rejected and fails against the original implementation. All **46 Python
harness tests** pass, with one Linux-only skip on macOS. The original memory-gate controller, both
full-lifetime counters, limits and generated inputs are unchanged.

Full Release and Debug units each pass **4,560 .NET 10 /4,194 .NET 8** checks. Four complete strict
inventories remain **2,695/16/1**, with zero unmatched expected codes and all **2,712 per-case records
identical** to the preceding checkpoint. This comparison does not prove actual forwarding. The
three Linux/macOS engine PDB audits match all **42 changed runtime C# sources**. Fresh macOS native
generation preserves all **32 canonical stylesheets** and validates all **48 smoke children**.

The candidate's complete Linux ARM64 push matrix verifies all **48 children**, all full measured
inputs, 32 controls and preceding output fingerprints. It still passes **13/16 original budgets**:
merge peaks at **23,220,224**, fork at **23,490,560** and source-document at **22,614,016 bytes** against
the original **20,971,520-byte** absolute limit and managed guard. Native Linux/macOS image hashes
are respectively `c32a7040e2f698ffb0f7caf1e62d2bc4978c69fb21e56bc454a530b7f7e10a43` and
`e9ae4de31e172d16ea3df8dd04274f35efb5e6ff60f8433ba9406ecdcf8b35f2`. Evidence, failed controls and
source/frozen-binary audits are retained under `fork-header-arrays-76f99af` and
`native-fork-header-final`. Twelve fresh native tree/stream controls validate exact 100 MB/1 GB
identity, accumulator and fork inputs under the original 4 GiB heap guard. All raw reports,
preceding fingerprints and actual binary/controller hashes match. This one-round control cohort
does not establish the three-round throughput target or either absolute memory gate.

The next hosted acceptance artifacts also retain the tested benchmark/engine DLLs and PDBs, and
the native publish directory and generated saved stylesheets. Their actual bytes can therefore
be checked independently against reported hashes instead of relying on the report alone. The
workflow parses successfully; original commands, complete matrix, input sizes, limits and timeout
settings are unchanged. Current-source hosted qualification remains required.

Every original memory, throughput, forwarding/public integration, final review, merge and release
requirement remains open where not yet verified. This allocation improvement does not complete P7
or change `supports-streaming`.

### Fork schema ownership and runtime contracts, 2026-10-30

Review reproduces shared mutable schema snapshots in the initial fork boundary and ancestor
headers. Event projections already copied those snapshots, but the other projections did not.
Both reduced cases fail on .NET 8 and .NET 10 before the repair. Every header projection now
copies node and attribute PSVI, while preserving copy-on-write sharing of immutable name and
namespace records. The synchronized tests mutate one prong only after both views exist. The
focused fork/tee suites pass **101 .NET 10 /100 .NET 8** checks.

General sequence checks and specific atomic type-code checks also dereferenced absent schema
annotations on functions, maps and arrays. Two reduced facts reproduce those null-reference
failures on both frameworks. The shared general type conversion now represents those values as
`item()`, consistently with the existing single-item contract, and specific atomic checks reject
them. Both repairs preserve the broad atomic fast path and ordinary schema matching.

Actual SDK builds expose two additional assembly-version cases: disabling only the generated
version attribute leaves a zero version header, and a manual header can coexist with misleading
reserved metadata. SDK provenance is now emitted only alongside the SDK-generated version
attribute. Missing, unrecognized or duplicate provenance uses the authoritative header, as do
ambiguous, invalid or normalized version values. `eng/verify-runtime-version.py` compiles nine
independent configurations on each actual .NET 8/10 host and compares runtime text with the real
assembly header. All **18 builds** pass. Their source, fixture, controller and engine fingerprints
are independently checked; all **422 base source/configuration files** match this checkpoint.
Required Ubuntu CI now repeats these build contracts and retains their files and logs.

The source-pumping reader count uses volatile reads and writes, with decrements still serialized
by the tee gate. Existing capacity, detachment, payload release, cancellation and ordered-fault
contracts are unchanged. Six alternating two-CPU Linux ARM64 100 MB fork observations show
baseline/candidate medians of **6,559.310 /6,319.913 ms** and child CPU **7,440.459 /7,235.939 ms**.
These are diagnostic controls, not a canonical throughput pass or an RSS improvement. The
alternative SDK-generated version-source experiment produces no material RSS benefit and is
rejected; its failed initial generation and all controls remain retained. The runtime hash-collision
test also has a finite search bound and fails explicitly if no actual collision is found.

Full Release and Debug units each pass **4,570 .NET 10 /4,204 .NET 8** checks. Fresh macOS native
generation preserves all **32 canonical stylesheet source files** and passes all **48 smoke
children**. Independently audited Linux/macOS Release PDBs match all **42 changed runtime C#
sources**. Fresh saved assembly timestamps and module identifiers are not fixture source changes.
The macOS smoke limit is zero and makes no memory-acceptance claim.

The fresh Linux ARM64 original push matrix validates **48 children**, **32 controls**, all
**16 complete measured transformations** and their preceding output fingerprints. It still passes
only **13/16 budgets**: merge peaks at **23,343,104**, fork at **23,425,024** and source-document at
**22,544,384 bytes**, against the unchanged **20,971,520-byte** absolute limit and managed guard.
Linux/macOS native image SHA-256 values are respectively
`3c3c145bc2bad621dd9f168786d9743cb742a45bffc61ea00595b1b1f08028f8` and
`40f79b24c46347c8a0f8a6b88cbe05f932d17e5ee70d45a3d2307c5a535fbae5`.
Evidence and unsuccessful controls are retained under `build-version-823e677`,
`tee-reader-count-823e677` and `native-boundary-final`.

The preceding committed checkpoint `823e677` is now fully observed. All four required CI jobs
pass in run **41058722486**. Acceptance run **41058734124** independently verifies **13/16 push
budgets**, **16/16 nightly budgets** (maximum absolute RSS **39,292,928 bytes**) and all **126**
paired benchmark observations under the original **4 GiB** guard. The tested native and managed
binaries are retained and their actual hashes and source PDB checksums validate. Its canonical
100 MB stream/tree ratios are identity **1.198732**, adjacent **0.832245**, accumulator **1.350917**,
iterate **0.846919**, merge **0.977374**, fork **3.690490** and source-document **1.164916**. Four
original throughput targets fail. These hosted observations qualify that preceding commit only.

Four complete Release/Debug API/legacy strict inventories retain **2,695/16/1** with zero unmatched
expected codes. All **2,712 per-case records** are identical to the preceding committed checkpoint,
and the frozen engine hashes match the reports. The Debug PDB also matches all **42 changed runtime
C# sources**. This comparison establishes inventory preservation, not actual forwarding.

Current-source hosted results remain required for this checkpoint. Original memory, throughput,
actual forwarding, public integration, complete review,
merge and release gates remain open. No budget, fixture, URI semantics, oracle, globalisation
setting, source population or public streaming claim changes.

### Closing-event schema contracts and deferred header reuse

Three additional S6 cases exercise tee capacities **1, 7 and 64** with nested closing events,
`xs:int` values, schema-supplied element defaults and independently mutable final PSVI for both
prongs. They pass on the original `260236b` runtime on .NET 8/10, in Release and Debug. The tests
use explicit forward child/sibling/parent navigation: the initial test draft used a general
descendant iterator, which revisits consumed nodes and is not a valid streamed traversal.
Its failed runs are retained separately. The original behavior is covered without adopting a new
runtime optimization.

An isolated alternative retains immutable start headers until their closing events and carries
final PSVI separately. All **104 .NET 10 /103 .NET 8** focused contracts pass. Four alternating
frozen-engine 100 MB fork controls validate unchanged outputs and reduce median allocated bytes
from **4,592,118,232 to 4,258,203,688** (**333,914,544 bytes /7.27%**). Source PDB checks match all
42 changed runtime C# files for each frozen variant. Six two-CPU Linux ARM64 native controls give
baseline/candidate elapsed medians of **6,785.988 /6,758.808 ms**, child CPU **7,751.735 /7,710.012 ms**
and absolute peak RSS **25,223,168 /25,145,344 bytes**. This small cohort establishes no material
elapsed-time or RSS improvement toward the outstanding gates. The implementation remains deferred;
the additional source-depth header retention is not adopted solely for an allocation-only result.
The exact inputs, outputs, original 4 GiB heap guard, raw reports, binaries, patch and independent
audits are retained under `fork-end-header-260236b`.

Hosted push qualification for committed `260236b` in run **41074093851** independently verifies
all **48 children**, **32 controls**, **16 full measured outputs**, original limits and prior output
fingerprints. It passes **13/16 budgets**: merge **24,522,752**, fork **25,030,656** and source-document
**23,556,096 bytes** exceed the original **20,971,520-byte** limit. The retained native image SHA-256
is `73633b94f6f30dd80328928c41316f1ef9c9d6cac58e497da20122d6dc21273d`; its PDB matches all **42**
changed runtime sources against the actual committed blobs, and all **32** canonical stylesheet
source files match. Evidence: `contracts-linux-push-260236b/independent-audit.json`. NativeAOT and
ordinary performance CI pass for that commit; the other hosted jobs remain on their original handles.
Every original P7 acceptance and delivery requirement remains in force.

### Actual source-representation evidence, 2026-10-30

An optional internal capture observes the navigator types that the command's initial-selection
and forward source-document paths actually return. It records execution nesting, input kind,
body completion and failed source openings. A failed opening has no claimed navigator type or
forwarding result. The capture does not choose an execution path, inspect node values or read an
input. Its ambient storage and queues are allocated only by an explicit test capture; ordinary
consumers install none. Dedicated threads and fork workers inherit the observation scope.

`XSL3_XSLT30_FORWARDING=1` adds versioned observations to the optional per-case JSONL export.
The auditor rejects missing captures, unfinished or duplicate executions, inconsistent types,
missing successful eligible initial sources, unexpected grounding and failed openings that claim
a representation. All three hosted strict inventory steps now require this evidence with
`--require-forwarding`. Case selection, assertion evaluation and the frozen inventory are unchanged.

Four complete Release/Debug API/legacy runs retain **2,695/16/1** and zero unmatched expected error
codes. All **2,712 underlying case records** match `8da25f4`'s preceding runtime checkpoint; only
the explicitly added observations differ. Every run observes **274 forward initial sources** and
**4,635 forward source-document entries**, spanning **2,459 cases**. It also records **two failed
source openings** without inventing a navigator. None of the observed requested-forward paths
returns a tree.

The **130 cases with commands but no forward source** and **123 cases without a command** remain
explicitly listed for applicability review. Commands can also execute during compilation and
assertion evaluation; their counts are not a count of principal stylesheet invocations. The seven
reader-input cases selecting a tree have non-streamable effective mode declarations: four omit
streamability or explicitly set it to `no`, and three disable it through static parameters.
Their definitions and stylesheet fingerprints are retained in `reader-input-tree-case-definitions.json`.
This observation gate does not by itself establish all stylesheet applicability, physical reads,
memory bounds or full P7 acceptance.

Seventeen capture contracts cover both public APIs, reader/stream/URI inputs, borrowed-stream
ownership, dedicated threads, fork-worker source documents, nested captures, incomplete executions
and failures before or during source opening. Five report contracts cover exclusive evidence files,
interruption and actual captured representations. All **18 Python runtime-audit contracts** pass;
the complete streaming harness suite passes **52 tests**, with one Linux-only skip on macOS.
Full Release and Debug units each pass **4,590 .NET 10 /4,224 .NET 8** checks. The workflow parses
successfully. Linux Release and macOS Release/Debug PDBs match all **47 changed runtime C# sources**.

With capture disabled, all **48 fresh macOS native smoke children** validate and all **32 canonical
stylesheet source files** are unchanged. The fresh Linux ARM64 original push verifies **48 children**,
**32 controls**, all **16 complete measured outputs** and preceding fingerprints. It still passes
**13/16 budgets**: merge **23,285,760**, fork **23,449,600** and source-document **22,491,136 bytes**
exceed the unchanged **20,971,520-byte** absolute limit and managed guard. Linux/macOS image SHA-256
values are respectively `51ca0492c3e9003757a5565064137d795f31847e2570915ef1de0cb3002cb0cd` and
`45c7b9e6d7d14dc2e08f3b75fa6d9cce347005859c5e3e90f7a20837e99068e0`.
Evidence, earlier pilot formats and failed test drafts are retained under `forwarding-8da25f4`;
the first pilot engine and its 46-source PDB audit are frozen separately.

Both preceding required CI runs are green: **41074066095** for `260236b` and **41078285402** for
the source-equivalent S6 tests/documentation head `8da25f4`. Original acceptance run **41074093851**
is terminal. Independently audited nightly passes **16/16 budgets /48 children**, with maximum
absolute RSS **39,268,352 bytes**. Its complete **126-observation** paired benchmark validates
all original sizes, rounds, modes, raw outputs and **4 GiB** guards, but still fails the no-slowdown
target. Its 100 MB stream/tree ratios are identity **1.156786**, adjacent **0.827880**, accumulator
**1.196930**, iterate **0.820468**, merge **0.964893**, fork **3.582623** and source-document **1.138794**.
The actual native/managed binaries, controller hashes and immutable-source PDB checks validate.
Evidence is under `contracts-linux-{push,nightly,benchmark}-260236b`. These hosted results qualify
that preceding runtime, not the observation implementation. Current-source CI and original acceptance
remain required; public integration, full review, merge and release remain open.


### Fork publication-wait experiment and current hosted push, 2026-10-30

A fresh native fork sample uses the committed `784408a` macOS image and validates a full
1,000,000,000-byte transformation. Its sampling overhead excludes it from canonical timings.
Both prongs frequently wait for source publication; waiting stack samples alone do not measure
CPU waste. An isolated candidate tests the BCL's bounded optimistic `SpinWait` before parking,
without custom spin counts, new buffers, capacity changes or moving source I/O and callbacks.

The candidate passes focused Release and Debug contracts on both frameworks (**121 .NET 10 /
120 .NET 8**, each configuration). Review fixes a redundant `Volatile.Read` on an already volatile
field; the earlier warning and the sandbox-denied test-runner attempt remain retained. The final
native PDB matches all **47 changed runtime sources**; among **428 runtime/probe inputs**, only
`StreamTee.cs` differs from the committed baseline.

Twelve alternating Linux ARM64 native observations validate unchanged 100 MB inputs, identical
output fingerprints and effective **4 GiB** guards. With two CPUs, baseline/candidate median
elapsed times are **6,470.499 /6,519.738 ms** and CPU times **7,356.110 /8,377.938 ms**. With one
CPU, elapsed medians are **10,681.398 /10,641.018 ms** and CPU medians **10,798.710 /10,749.427 ms**.
The candidate provides no material elapsed-time benefit and increases two-CPU consumption by
**13.89%**. It is rejected, its extra synchronization is not adopted, and the disposable source
copy is restored. Raw trials, source patch, binary hashes, PDB and decision remain under
`artifacts/p7-completion/tee-publication-spin-784408a/`. These controls do not satisfy canonical
throughput or memory acceptance.

Current-source hosted acceptance run **41087810596** has completed its original push job. An
independent audit validates all **48 children**, **32 controls**, **16 full measured outputs**,
preceding output fingerprints, the original **209,715,200-byte inputs**, effective **20,971,520-byte**
guards and all **32 unchanged stylesheet sources**. Its retained PDB matches all **47 changed
runtime C# files**. It passes **13/16 budgets**: merge **24,453,120**, fork **24,813,568** and
source-document **23,687,168 bytes** exceed the absolute limit. The image SHA-256 is
`582f78f5379eb89a6c6436a5d484e38946dc83cd3ba00fc35b0b861c02b39582`.
Evidence is under `artifacts/p7-completion/contracts-linux-push-784408a/`.

The same run's nightly and full paired benchmark remain under observation; neither is restarted.
Required CI **41087738727** has successful native and ordinary performance jobs while Ubuntu and
Windows functional jobs continue. The compact-page/value-pool, accumulator phase, grouping,
saved-version and synchronization review records no additional established defect in the inspected
changes; it is explicitly partial. All remaining original qualification, applicability, public
integration, full review, merge and release requirements remain open.


The exclusion audit now accounts for all **253 cases without an observed forward source** in
all four Release/Debug API/legacy inventories, with identical case classifications. Of the
**130 cases with commands**, **123** use only materialized or absent inputs; the other **seven**
use readers with non-streamable effective initial modes, matched to their retained stylesheet
and static-parameter reviews. All their observed initial-mode flags are false.

Of the **123 cases without commands**, **118** pass their declared static errors, one passes
`XTDE3490` for a literal nonexistent merge-source name detected during compilation, **three**
retain their reported `XTSE3195` failures and **one** retains the collection-environment skip.
The audit verifies the pinned clean suite, test-set and stylesheet fingerprints and the actual
compiler branch for the literal merge-source diagnostic. It retains every case, outcome and
reason under `forwarding-8da25f4/applicability-exclusions-audit.json`. This explains the observation
gaps; independent principal-invocation, static streamability, physical-read and qualification
requirements are not inferred from these classifications.


### Producer-side control and hosted CI, 2026-10-30

The separate producer-side bounded-spin control is also rejected. It passes **121 .NET 10 /
120 .NET 8** Release contracts and its PDB matches all **47 changed runtime sources**. Of **428**
runtime/probe inputs, only the disposable `ForkCaller.cs` differs from `784408a`. Six alternating
two-CPU Linux ARM64 native 100 MB controls retain identical outputs and original **4 GiB** guards.
Baseline/candidate median elapsed times are **6,506.052 /6,509.850 ms** and CPU times
**7,417.486 /7,917.651 ms**. The negligible elapsed difference and **6.74%** CPU increase do not
justify the extra waiting protocol. The source copy is restored, and no further spin variants are
planned without new evidence. Debug and single-CPU candidate runs are unnecessary for this rejected
implementation; they are not claimed. Evidence: `caller-publication-spin-784408a`.

Required hosted CI **41087738727** is now terminal with all four jobs successful: Ubuntu, Windows,
NativeAOT and ordinary performance. It validates source commit `784408a`. Subsequent documentation
records do not change runtime/probe inputs, fixtures, selectors or acceptance limits. The original
acceptance run **41087810596** remains independent: its push result is the audited **13/16**, and
its nightly and full paired benchmark continue under their original handles. Full P7 qualification,
public integration, final full-branch review, merge and release are still required.


### Stylesheet-stage forwarding contracts, 2026-10-30

Optional captures now label preparation, compilation, stylesheet execution and assertions.
Deferred result-tree replay restores the stylesheet stage when an assertion invokes it. A reduced
real-transform regression fails with `Assertion` before that repair and passes with `Stylesheet`
after it. Scopes restore across nested commands, exceptions, dedicated threads and fork workers;
ordinary consumers install none. Command counts can still include nested work within a harness
operation and are not counts of principal invocations. Nested capture format **2** adds stages;
retained format **1** remains valid for source-only audits but cannot prove stage qualification.
No public API or saved-stylesheet format changes.

Four final Release/Debug API/legacy inventories preserve all **2,712 underlying case records**,
**2,695/16/1** outcomes and zero unmatched expected codes. Every **274 forward initial** and
**4,635 forward source-document** observation now belongs to stylesheet execution/replay;
assertion evaluation has no forward-source observation. All four cohorts observe the same
**2,459 cases** forwarding during the stylesheet stage. The inventory adds those case identities
as a reviewed minimum, with source/report fingerprints. Its population, definition fingerprints,
outcomes and suite pin are unchanged. `--require-stylesheet-forwarding` rejects unclassified or
legacy evidence and any individual loss from that minimum, including when other cases still
forward. All three hosted strict steps require it.

Full Release and Debug units each pass **4,591 .NET 10 /4,225 .NET 8** checks. The capture and
report contracts pass **18** and **seven** tests; all **22** auditor contracts pass. The full
streaming Python harness passes **56** tests with one Linux-only skip on macOS. Frozen Release/
Debug PDBs match all **47 changed runtime** and **five changed harness C# sources**. The final
Linux native image matches the same runtime sources and all **428 runtime/probe inputs**.
Its original push matrix validates **48 children**, **32 controls**, **16 full measured outputs**
and all **32 unchanged stylesheet sources**, but still passes **13/16 budgets**: merge
**23,285,760**, fork **23,445,504** and source-document **22,528,000 bytes** exceed the unchanged
**20,971,520-byte** absolute limit and managed guard. Image SHA-256:
`6a4d17163a0581e88f885bda3846c7474ae600c42c253e65a2730107ab58f30a`.
Evidence, initial stage pilots and failed regression attempts: `staged-forwarding-9d5344c`.

The preceding source checkpoint `784408a` is fully observed. Its required CI **41087738727** and
documentation-only head `9d5344c`'s CI **41092363988** both pass all four jobs. Original acceptance
**41087810596** is terminal: independently audited nightly passes **16/16 budgets /48 children**
with maximum absolute RSS **39,415,808 bytes**. Its benchmark validates all **126 observations**,
original sizes/modes/rounds, output fingerprints and **4 GiB** guards. The 100 MB stream/tree
ratios are identity **1.096061**, adjacent **0.808101**, accumulator **1.187782**, iterate **0.821138**,
merge **0.865522**, fork **3.010683** and source-document **1.070866**. Four original targets fail;
these separate cohorts do not establish a paired speedup over an earlier commit. Retained binaries,
controller hashes and immutable-source PDB checks match **47 runtime /six probe sources**.
Evidence: `contracts-linux-{push,nightly,benchmark}-784408a`. These hosted results qualify that
preceding runtime; the staged observer still requires hosted CI. The original memory/throughput,
remaining applicability/physical-read, public integration, full review, merge and release gates
remain in force.


### Copying profile and output callback reuse, 2026-10-30

The current `662d326` hosted memory artifacts are independently audited against all **47 changed
runtime sources**, **32 unchanged stylesheet sources**, raw reports and preceding output fingerprints.
Push still passes **13/16 budgets**: merge **24,408,064**, fork **24,936,448** and source-document
**23,674,880 bytes** exceed the original **20,971,520-byte** limit. Nightly passes all **16 budgets /
48 children**, with maximum absolute RSS **39,374,848 bytes**, exact **2,147,483,648-byte** measured
inputs and original **209,715,200-byte** guards. Archive digests, independent audits and native images
are retained in `contracts-linux-{push,nightly}-662d326`. The paired hosted benchmark remains a
separate qualification; these memory results do not prove throughput or full P7 completion.

A fresh macOS NativeAOT identity profile validates both exact 1 GB outputs. Its PDB matches all
47 changed runtime sources, all 32 stylesheet sources are unchanged, and 48 small smoke children
pass. CPU sampling makes its timings diagnostic, never canonical acceptance. Evidence:
`copy-profile-662d326`.

Two isolated reader candidates are rejected. A metadata-reader bypass adds another retained reader
reference but improves the twelve 100 MB controls by only **0.4% /1.1% elapsed** for identity/source-
document, with roughly **0.7% CPU** changes. Per-token node-type caching initially improves the same
controls by about **1.8% /1.9%**, but twelve longer 1 GB controls show identity **0.8% slower** and
source-document **1.4% faster**. These mixed results do not justify adoption. All trials, initial
compile/test attempts, source patches and checksums are retained in `reader-metadata-662d326` and
`token-kind-662d326`; their isolated runtime sources were restored. Neither candidate changes the
branch or any acceptance threshold.

The same native profile identifies allocation inside `XmlQueryOutput.Writer` when each element
reattaches its reusable attribute cache. The output now retains its own removal callback once,
while every attachment still registers it with the current removable writer. Callback targets,
writer replacement, nested ownership, output resets and saved formats are unchanged. No reader
unwrapping or node-type caching is adopted.

Six new contracts cover nested attribute-cache ownership, alternating pooled sequence/event
outputs, and automatic XML/HTML selection through modern, legacy and saved execution. Corrected
fixtures pass the unchanged baseline and the candidate. Initial fixture failures involving nullable
raw-writer namespace arguments, XML empty-tag spacing and RTF conversion are retained separately;
they are not runtime defect claims. Full Release and Debug units each pass **4,597 .NET 10 /
4,231 .NET 8** checks. Focused checks pass **717 /519**, respectively.

A source-verified warmed JIT allocation diagnostic on exact 100 MB identity input falls from
**2,129,114,168 to 1,906,504,520 bytes**, a **10.5%** reduction, with matching record/output
fingerprints and equal **4 GiB** guards. This is total allocation after warmup, not absolute RSS.
All **48 NativeAOT smoke children** pass; the image matches the 47 runtime PDB sources and all
428 runtime/probe input hashes. Twelve alternating 100 MB native controls validate every output.
Identity baseline/candidate medians are **1,902.376 /1,861.383 ms elapsed** and
**2,315.576 /2,268.933 ms CPU**; source-document medians are **1,905.679 /1,864.294 ms elapsed**
and **2,313.081 /2,279.607 ms CPU**. Median absolute peaks are unchanged in these controls. They do not
establish an original seven-workload throughput or memory pass. Evidence: `writer-callback-662d326`.

The final local original push matrix validates all **48 children /32 controls /16 full measured
outputs** and preceding output fingerprints, but still passes **13/16 budgets**. Absolute peaks are
merge **23,281,664**, fork **23,511,040** and source-document **22,503,424 bytes**. The exact 200 MiB
input, 20 MiB managed and absolute RSS guards, all workloads and full-lifetime counters are unchanged.
Native image SHA-256: `a55e612651a4dd139dbb9a8bcf12bc1f27f2cd8058cffeb20e4551cb5483bdf6`.
Four fresh Release/Debug API/legacy strict inventories preserve all **2,712 underlying case records**,
**2,695/16/1** outcomes, zero unmatched expected codes and the identical **2,459-case** stylesheet-
forwarding minimum. Frozen engines match their report fingerprints and all 47 runtime PDB sources.
Required CI **41097655800** passes all four jobs for preceding commit `662d326`; the callback source
still requires its own hosted qualification.

The gate review also reproduces a controller validation defect: an untrusted child can claim `ok`
and the correct configured heap cap while reporting live managed memory above that cap. Its real
process regression fails before the fix because the controller exits successfully. The shared
validator now independently rejects that contradiction. All **57 Python harness tests** pass with
one Linux-only skip on macOS. Revalidating all 48 retained native memory reports with the stronger
validator preserves every output/guard observation and the same three budget failures. Neither
budget nor workload changes. Initial and corrected regression logs are retained with this checkpoint.

Current-head hosted qualification, the original nightly/full paired benchmark and every other P7
acceptance, public integration, full-branch review, merge and release requirement remain in force.


### S7 qualification and resident-page attribution, 2026-10-30

Required hosted CI **41103792667** passes all four jobs for `d744911`. Independent audits of
acceptance **41103827041** verify all 48 push children, 32 controls, sixteen full measured outputs,
32 unchanged stylesheet sources and 47 changed runtime PDB sources. Push remains **13/16 budgets**:
merge **24,461,312**, fork **24,821,760** and source-document **23,613,440 bytes** exceed the original
20,971,520-byte limit. Nightly passes **16/16 budgets /48 children**, exact 2,147,483,648-byte measured
inputs and original 209,715,200-byte guards, with maximum absolute RSS **39,211,008 bytes**. Both
native images, raw reports and archive digests are retained in `contracts-linux-{push,nightly}-d744911`.
The current paired benchmark continues under its original handle; no duplicate run is dispatched.

The preceding `662d326` acceptance **41097764886** is now terminal. Its complete paired benchmark
independently verifies all **126 observations**, original 1 MB/100 MB/1 GB sizes, three rounds per
mode, exact output fingerprints, 4 GiB guards and tested engine/benchmark hashes. Immutable source
checks verify 47 runtime and six probe PDB documents. Canonical 100 MB stream/tree ratios are
identity **1.203796**, adjacent **0.840929**, accumulator **1.291537**, iterate **0.854712**, merge
**0.980481**, fork **3.541268** and source-document **1.186725**. Four original targets fail.
Evidence: `contracts-linux-benchmark-662d326`. These separate cohorts are not paired speedup evidence.

An isolated destination-aware annotation prototype moves ID/IDREF lookup after checking whether
the destination can store annotations. It passes 717 .NET 10 /519 .NET 8 focused checks and 48 native
smoke children, but twelve alternating original-output-checked controls do not demonstrate a copying
benefit. Early identity trials have substantial variation and all raw observations are retained;
no causal regression magnitude is claimed. The prototype is rejected and its source restored.
Evidence: `annotation-destination-d744911`.

Fresh `/proc` mapping attribution uses the source-verified `d744911` Linux native image on identity,
merge, fork and source-document, consuming exact 200 MiB inputs and verifying preceding full-output
fingerprints. The actual managed guard remains 20 MiB; the diagnostic disables the RSS status limit
only so the failing processes complete. These sampled mappings and timings cannot satisfy acceptance.
The three failing workloads each map **2,985,984 resident bytes of ICU libraries** and **1,499,136 bytes
of the C++ runtime**; identity maps neither. Reported final live managed bytes are respectively
**286,856 /249,024 /247,952**, versus identity **244,776**. Anonymous mappings include runtime and
committed heap/stack pages and must not be equated with live managed objects.

A further exact 1 GB source-document run attributes resident image pages through `/proc/pagemap`.
All output and the original 20 MiB managed guard are verified. At its maximum sampled image presence,
**9,338,880 bytes** are resident: managed-code section pages account for **4,444,160 bytes** and
read-only data for **2,691,072 bytes**, with the remaining pages in native code, unwind/exception,
linkage and data sections. Section page counts may share boundary pages; they are attribution,
not a replacement for full-lifetime operating-system counters. No dependency, URI, globalization,
GC policy, source size, output oracle or budget changes. Evidence: `memory-mappings-d744911`.

An isolated loader prototype reads existing version/generated-code attribute data directly while
retaining authoritative header fallback and saved-version checks. It passes 22 .NET 10 /20 .NET 8
focused checks, 48 native smoke children and sixteen alternating full 200 MiB controls under the
original managed guard. All four median absolute peaks increase: identity **233,472**, merge
**104,448**, fork **57,344** and source-document **391,168 bytes**. It establishes no memory reduction
and is rejected. Its source is restored, and no SDK version source, public API or saved format is
changed. Broader build-configuration qualification is unnecessary for this rejected candidate and
is not claimed. Evidence: `metadata-readers-d744911`.

The resident dependency/image footprint and fork/accumulator throughput require further work.
Original acceptance, applicability/physical-read and public-integration evidence, full-branch review,
merge and release remain open. This record makes no streaming-support or P7 completion claim.

### Physical read contracts and complete hosted benchmark, 2026-10-30

New [runtime contract evidence](p7-runtime-contracts.md) links the seven canonical workloads to
physical input reads and actual nonempty sink writes. Unit-only guards wrap the existing generated
streams and refuse reads beyond each source's halfway boundary before output. Identity, adjacent,
source-document, merge and fork must produce output within that boundary; matching tree controls
must fail there before any sink write. Accumulator and iterate totals instead require complete
input in both modes. Every successful run consumes the exact input and matches the unchanged
independent output fingerprint, including both fork secondary results.

Exact 1 MiB and 4 MiB cases cover both public APIs and .NET 10 saved execution: **72 checks on
.NET 10 /48 on .NET 8**, in both Release and Debug. Final combined stateful/fork/read-boundary
verification passes **378 /338**, respectively, in each configuration. Optional captures verify
actual stylesheet-stage forward representations. Principal writes and secondary-result opening
stay on the invoking thread; prongs may write their independent secondary sinks. The first fixture
incorrectly omitted merge's principal launch-source observation; a later fixture incorrectly required
secondary-sink writes on the caller thread. Both failures are retained and corrected without engine
changes. Evidence: `artifacts/p7-completion/read-boundary-*.log`.

The new file belongs only to the unit-test project. Runtime, native/benchmark probe sources, canonical
fixtures, input sizes, output oracles and acceptance guards are unchanged. Existing runtime inventories
remain evidence for the unchanged runtime, rather than being relabeled as fresh executions. The
contract map explicitly retains whole-suite applicability and full retained-state qualification as open.

Hosted acceptance **41103827041** is now terminal. Its benchmark artifact **13004297246** independently
verifies all **126 observations**, original 1 MB/100 MB/1 GB sizes, three rounds per mode, 4 GiB managed
guards, raw child outputs, prior full-output fingerprints and actual tested engine/benchmark hashes.
Portable PDB checks match all **47 runtime /six probe source documents**. Canonical 100 MB stream/tree
ratios are identity **1.254050**, adjacent **0.847529**, accumulator **1.335710**, iterate **0.841212**,
merge **0.955344**, fork **3.456069** and source-document **1.194419**. Four targets still fail; the
report's descriptive 5% comparison band does not waive the original target. Evidence:
`contracts-linux-benchmark-d744911`, verified archive SHA-256
`6dede50922e65028605f08a5b5acb549b252e23205eb0eebe590ee443fbbc968`.

Required CI **41110967828** passes all four jobs for preceding documentation commit `d90ffb3`.
The new unit contracts require their own hosted CI. Original push memory, throughput, complete
applicability/state-bound evidence, full-branch review, public integration, merge and release remain open.

### Primary diagnostics and non-forwarding case review, 2026-10-30

The optional runtime report now captures the actual evaluation result before assertion checking,
including primary compilation/runtime diagnostics and declared principal-source metadata. The observer
is bound to one invocation, so a late worker cannot populate the following case's observation. Legacy
compilation reads the actual first non-warning compiler diagnostic; legacy dynamic exceptions use the
existing public API translation. Assertions do not provide the observed code or alter the verdict.
CI requires complete, well-formed observations independently of the original forwarding minimum.

Four fresh Release/Debug API/legacy runs preserve every underlying case record, **2,695 pass /16 fail
/one not run**, zero unmatched codes, **2,459** stylesheet-forward cases and the same source observations.
Each records **2,442 returned evaluations /269 errors /one unobserved collection case**. Primary codes,
compilation/runtime classification and initial-source declarations agree across all four runs. Tested
Release/Debug PDBs match **47 runtime and six harness files**. Runtime and native/benchmark inputs are
unchanged; this checkpoint does not introduce a new performance candidate.

The [253-case review](p7-runtime-nonforward-review.csv) retains exact case identities and definition
fingerprints. Of the cases without stylesheet forwarding, **156** raise an expected compilation error,
**eight** an expected dynamic error, **76** receive tree/absent principal input and **six** explicitly
invoke a nonstreamed mode despite being supplied a reader. Every observed error code matches a pinned
error assertion. The remaining **six failures and one collection skip remain unchanged**. No case is
removed or relabeled as passing. The contract map distinguishes this accounting from complete static
applicability and S3–S6 retained-state qualification.

Final Release legacy and Debug API report/coverage checks each pass **18** checks; all four fresh
inventories also execute the report contracts. Original and
reviewed S2 runs also pass their established gates in Release and Debug: the reviewed suite remains
**155/155** with **152 exact codes /three allowed results**; the original remains **153 pass /two retained
fixture failures**, with no new mismatch. The full Python harness passes **75 tests**, with one
Linux-only skip on macOS. Initial compiler-type import errors and the missing legacy dynamic-code
translation regression are retained beside the corrected results. Evidence:
`artifacts/p7-completion/evaluation-observation-de179f8`, `evaluation-report-*.log` and
`evaluation-python-full.log`.

Full P7 remains open: original memory/throughput targets, complete construct and state-bound evidence,
full-branch review, public integration, final-source hosted CI, merge and release are still required.

### Measured tee and scalar accumulator retention, 2026-10-30

New unit-only measurements address two parts of the original S5/S6 state-bound evidence. The
[contract map](p7-runtime-contracts.md#measured-retention-with-explicit-payload-assumptions) states the
payload, reader, source-shape and checkpoint assumptions and keeps process RSS qualification separate.
They reuse the actual tee/caller and accumulator observer; no engine, benchmark/native probe,
canonical fixture, heap guard, event-capacity policy or original acceptance threshold changes.

Six tee cases use capacities **1 /7 /64**, **two /three /four readers**, payloads **1,024 /8,192 bytes**,
and paired input lengths with **16 /64 checkpoints**. Every checkpoint counts reachable payload
arrays after readers have returned from `Read`; all readers hold the same current event. Bytes equal
`(BufferedCount + 1) * payloadBytes` and remain below the configured `(capacity + 1)` bound. Finishing
the readers and dropping current references leaves zero live payloads. The weak-reference recorder
is test instrumentation, not evidence of constant total process memory.

A separate diagnostic copy matches all **407 runtime C#/project inputs** before disabling only
consumed ring-slot clearing. All **six** cases fail against that deliberate retention mutation and
pass after source restoration and rebuild. The checked-out runtime is never modified. Source hashes,
both logs and the original source are retained in `tee-payload-f8915f6`; its managed diagnostic outputs
are rebuilt from restored source, while the existing canonical native image is unchanged.

Two accumulator cases stream exact **65,536 /1,048,576 bytes**, **569 /9,117 records**, through the
actual initial-source observer and native rule fixture. Sample maxima stay at **one row frame /one
metadata object /two tracked scalar phase lists**. Closing after moving to the root releases row
frames and metadata; the root may retain its final scalar after-value. Reader input ownership is
preserved. The shared fixture now also accepts an explicitly supplied reader. Initial fixture failures
(the batch callback signature and padding whitespace selected as a row) are retained and corrected.

Final Release and Debug related checks pass **167 .NET 10 /164 .NET 8** in each configuration,
including all **eight** new measurement cases per framework. TRX files preserve actual measurements
under `state-bounds-final-{release,debug}`. Generic construct/state accounting, the original absolute
memory and throughput failures, full review/CI, public integration, merge and release remain open.


### Compact fork events and intermediate-frame lifetime, 2026-10-30

Fork events now inherit their header instead of allocating a separate wrapper. Immutable kind,
name/text, base URI and language data come from the existing provenance frame. Attribute/namespace
arrays, schema references and mutable navigation flags still snapshot the actual publishing frame;
prong atomization and private PSVI copies are unchanged. No public API, saved format, tee capacity,
threading policy, canonical fixture, oracle, input size or acceptance limit changes.

Review rejected an earlier candidate that retained the immediate prong frame for metadata access.
A new nested-fork lifetime test proves the defect on .NET 8 and 10: a slow reader retaining the first
published event also retained private runtime state from the consumed intermediate frame. The
unchanged production baseline passes this contract on both frameworks. The revised representation
passes it in Release and Debug without retaining that intermediate frame. Lazy-header and smaller
schema-only trials remain separate, unadopted experiments; their focused checks are not qualification.

Six alternating JIT fork controls validate unchanged outputs with an actual 4 GiB heap guard.
Measured allocated-byte medians fall from **4,592,121,984 to 3,673,856,192** (approximately **20%**)
for the existing 100 MB input after the same 64 KiB warmup. This diagnostic includes complete probe
invocation allocation and does not substitute for native RSS or canonical throughput acceptance.
Six alternating NativeAOT controls also validate all original output fingerprints. Median elapsed
is **6,597.302 /6,491.761 ms** baseline/candidate and full-child CPU **7,616.725 /7,450.879 ms**.
The trial ranges overlap; these controls do not establish a decisive throughput improvement or an
absolute memory reduction. The allocation reduction is the demonstrated benefit.

The corrected native candidate passes **48/48 smoke children**. Its complete original local Linux
push matrix validates all **48 children /32 controls /16 full measured outputs**, matching preceding
output fingerprints and record counts with unchanged actual heap caps. It still passes **13/16
budgets**: merge **23,220,224**, fork **23,572,480** and source-document **22,437,888 bytes** exceed
the original **20,971,520-byte** absolute limit. Native image SHA-256:
`c93c6fa458d74a68547a9dc76cf303139aec412a478041759dc854265d550578`.
All 407 runtime C#/project inputs were compared with the diagnostic copy, with only the intended
fork source differing before adoption. Portable PDB checks match **47 runtime /six native probe**
sources and the unchanged generated saved-entrypoint source.

Full units on the actual branch pass **4,678 .NET 10 /4,288 .NET 8** checks in both Release and Debug.
Final artifact checks initially detect a stale .NET 8 Release engine containing the preceding fork
source: a copied source timestamp allowed incremental compilation to skip that target. That initial
binary and its successful baseline-checksum audit are retained. Explicit rebuilds of both configurations
then verify all **47 runtime /seven probe-and-retention source documents** in each framework's actual
test-directory binaries before the complete suites are rerun with `--no-build`. Initial test counts
alone are not treated as proof that the changed runtime was executed.
Focused fork/tee/accumulator/read contracts pass **426 /384**, respectively, in each configuration.
The preceding `8a909b6` required CI **41120177647** is terminal with all four jobs successful. Fresh
hosted CI, strict inventories, original nightly and full paired benchmark qualification are required
for the changed runtime. Evidence, including the rejected candidate's failing lifetime test and
full push results, is retained under `artifacts/p7-completion/lazy-fork-header-8a909b6`.

Original memory/throughput failures, complete construct/state applicability, public integration,
full-branch review, final-source hosted CI, merge and release remain open. Full P7 is not complete.


### Accumulator argument ownership and intrinsic frame notifications, 2026-10-30

A fresh allocation profile of the exact `a7e6381` Release engine validates the canonical 100 MB
accumulator output under its actual 4 GiB heap cap. Weighted allocation ticks identify navigator
copies in matcher argument conversion and source notifications as large contributors. The trace
includes startup/warmup and collector overhead; sampled per-type bytes are estimates, not canonical
elapsed-time measurements. Existing identity profiles instead locate their largest source-frame
allocation in template traversal, so a deep-copy writer change alone would miss that cost.

Accumulator matchers now use the existing owned-argument function-call path for their freshly
allocated sequence and cloned cursor. Argument conversion still runs, and the clone separating the
matcher from its caller and rule body remains. A native-rule contract moves every matcher cursor
to the root while requiring row focus and before/after values in the body and caller. The original
runtime and candidate pass on both frameworks. Removing just the required clone in a separate copy
fails that contract on both frameworks; the source is restored and rebuilt afterward.

The intrinsic accumulator observer also accepts source frames directly, avoiding a temporary
navigator for each start/end notification. Default internal interface adapters retain the existing
fresh-navigator callbacks for other observers. A new contract retains all ordinary callback
positions and verifies their independent positions after source completion. Original event ordering,
caller-thread execution, motionless-read guards, phase publication, error/cancellation handling and
copy/fork ownership remain covered by the focused suite. Saved formats and public APIs are unchanged.

The matcher-only prototype has six valid alternating JIT controls. Its cumulative allocation median
falls from **8,306,635,784 to 7,638,806,216 bytes**, with elapsed medians **3,497.188 /3,340.972 ms**.
The combined candidate's independent six-control cohort reduces cumulative allocation from
**8,306,635,784 to 6,748,366,792 bytes** (approximately **19%**) and elapsed medians from
**3,510.880 to 3,230.171 ms**. Every process uses the same 64 KiB warmup, canonical 100 MB input,
actual 4 GiB heap cap and preceding full-output fingerprint. These controls preserve their complete
invocation allocation and do not replace the original seven-workload throughput comparison.

Six alternating NativeAOT controls verify the original outputs and actual 4 GiB caps. Accumulator
elapsed medians are **2,370.523 /2,178.422 ms** baseline/candidate, and full-child CPU medians are
**2,382.225 /2,192.891 ms**. The candidate passes **48/48 native smoke children**. Its complete original
local Linux push matrix verifies **48 children /32 controls /16 full measured outputs**, with
unchanged input sizes, actual heap caps and full-output fingerprints. It still passes **13/16
budgets**: merge **23,220,224**, fork **23,511,040** and source-document **22,507,520 bytes** exceed
the unchanged **20,971,520-byte** absolute limit. Image SHA-256:
`8b89b6c03eeacf152dcb7e44fc67c9ed09e50eacf98c60c5d0c766e51184131d`.
All 407 runtime C#/project inputs are compared before adoption; only the four intended runtime
files differ. Portable PDB checks verify **49 runtime /six native probe** sources and the unchanged
generated saved-entrypoint source. The canonical sources, oracles, capacities and budgets are intact.

Focused Release/Debug checks pass **493 .NET 10 /449 .NET 8** per configuration. Full source-verified
units pass **4,680 /4,290**, respectively, in both configurations. Every actual test-directory engine
is explicitly rebuilt, frozen and checked against **49 runtime /eight probe-and-contract sources**
before the full `--no-build` invocation. Evidence, raw controls, mutation and restored source hashes,
all native children and frozen engines are retained in `artifacts/p7-completion/accumulator-phase-a7e6381`.

The preceding `a7e6381` required CI **41127550335** is terminal with all four jobs successful. Hosted
acceptance **41127579684** has independently verified push **13/16 budgets /48 children**, unchanged
32 stylesheet sources and original output fingerprints. Peaks are merge **24,313,856**, fork
**24,567,808** and source-document **23,777,280 bytes**. Its nightly matrix verifies **16/16 budgets
/48 children**, exact **2,147,483,648-byte** inputs and original **209,715,200-byte** heap/RSS guards;
maximum absolute RSS is **39,309,312 bytes**. Its original full paired benchmark remains under the
same live handle at this checkpoint. Archived digests and 49-source runtime PDB checks are retained
in `contracts-linux-{push,nightly}-a7e6381`. These observations remain attributed to that source.

Current-source hosted CI, strict inventories, original full memory/benchmark qualification, complete
construct/state applicability, public integration, full-branch review, merge and release remain
required. No original acceptance criterion or streaming-support claim changes; full P7 stays open.


### Bounded rolling accumulator values, 2026-10-30

The [runtime contract map](p7-runtime-contracts.md#measured-retention-with-explicit-payload-assumptions)
now includes variable-sized atomic state through the actual initial-source accumulator observer.
The existing native-rule test fixture accepts explicit initial/update functions; its scalar defaults
and all runtime/probe sources remain unchanged. The rolling program appends strings derived from
actual row IDs and configured repetitions, retaining the last **1 /7 /64** values. It runs at exact
**65,536 /1,048,576-byte** generated inputs, **569 /9,117 records** and **five /72 checkpoints**.

The initial assertion incorrectly counted the globally shared empty sequence alongside live current
phase arrays. Runtime `Copy` returns `Sequences.Empty` for an empty value; the corrected contract
asserts that identity explicitly and measures nonempty arrays separately. The original six failing
cases per framework remain retained. A conditional weak table deduplicates atomic object identities
without retaining targets; the growing weak recorder is instrumentation rather than a process-memory
claim. Each checkpoint requires at most one row frame, one row metadata object, two nonempty phase
arrays, `W + 1` live atomic objects and `(W + 1) * P` logical payload characters. The root may retain
its final window after closure; caller input ownership remains intact.

All **24 final TRX measurements** independently verify the short/long input pairs on both frameworks
and configurations. Window widths **1 /7 /64** retain maxima of **2 /8 /65** objects and
**40 /1,310,720 /166,400** logical payload characters, respectively. Maximum item lengths are
**20 /163,840 /2,560** characters. Logical characters are distinct from actual object/array/string
allocation and full-lifetime RSS; generic growing or opaque/node-valued state, explicit copies and
remaining S3–S6 accounting still need their own qualification.

A diagnostic copy first matches all **407 runtime C#/project inputs**. Adding a private history of
prior phase values, while leaving their computed contents unchanged, fails all **six** window cases
on each framework at the retention checks. Restoring and rebuilding passes all six on both frameworks.
The canonical native image is unchanged. Final related Release and Debug contracts each pass
**205 .NET 10 /200 .NET 8** checks, including the original scalar and new rolling cases. Frozen engine
and test PDB checks verify **49 runtime /two changed test-source documents** for all four executions.
Raw measurements, initial failures, mutation/control logs and hashes are retained in
`artifacts/p7-completion/window-state-bd89eae`.

Hosted `bd89eae` acceptance **41132108776** has a terminal independently audited push matrix:
**48 children /32 controls /16 full measured outputs**, unchanged 32 stylesheet sources, 49 matching
runtime PDB documents and preceding output fingerprints. It remains **13/16 budgets**; merge
**24,080,384**, fork **24,768,512** and source-document **23,633,920 bytes** exceed the unchanged
**20,971,520-byte** absolute limit. Archive SHA-256:
`db0fbfe4768746848487fab0ed5876db59b6c36ce78883200db66c261fb3e1a2`.
Image SHA-256: `e8d4bfe9913b2e023e2c3c8910717f75fd729226cdb3051c03485201f404aef0`.
Its nightly/benchmark jobs and required CI continue under their existing handles at this checkpoint.
This unit/documentation change does not dispatch another acceptance run for the unchanged runtime.

Original memory/throughput targets, complete runtime applicability/state accounting, public integration,
full-branch review, final-source hosted CI, merge and release remain open. Full P7 stays active.


### Merge retention and function-context isolation, 2026-10-30

The [runtime contract map](p7-runtime-contracts.md#merge-groups-lookahead-and-explicit-retained-values)
now measures real compiled merge snapshots and keys across two input lengths, three group/payload
shapes, both APIs, normal/debug stylesheets and supported saved loading. The new suite adds **126
.NET 10 /84 .NET 8** cases. Its six contract families cover distinct keys, tied groups, payload and
ancestors, explicitly retained snapshots/closures, termination/ownership, and failure/cancellation.

For 16 and 128 groups, measured snapshot maxima remain **4 /24 /8**, with observed key maxima
**3 /4 /3**, for the documented shapes. Bounds include current equal-key group size and one lookahead
per input. Logical character accounting uses actual generated IDs/keys and copied ancestor attributes;
it is not physical allocation, parser/provenance accounting or process RSS. Explicitly retaining two
groups adds twelve snapshots; they remain valid after source closure and collect once released.

An isolated copy first matches all **410 C#/project inputs under `src/`**. Retaining completed group
lists without changing their values fails all **36 /24** measurement cases on .NET 10/8; restoration
and rebuild passes all of them. Production merge state, canonical inputs/oracles and all limits stay
unchanged. Initial fixture failures (exception-wrapper assumptions, namespace serialization and a
recursive test-stream adapter) remain retained with their corrected controls. The eager `head()` over
a grounded merge-result variable remains recorded without being treated as a streaming guarantee.

The new closure coverage also establishes a compiler defect: directly referring to implicit merge
group/key context inside an inline or named function value loads an iterator local owned by another
IL generator. Six reduced forms fail compilation on both frameworks before the fix. The XPath
compiler now enters an internal host scope while building function bodies; the XSLT host clears only
the implicit merge context and restores it on scope disposal. Explicit variable captures continue
through their existing closure ABI. The six forms now report `XTDE3480` / `XTDE3510`, while the outer
merge action keeps its group/key. Tree, forward, normal/debug, both API and .NET 10 saved paths pass.

Full source-verified unit suites pass Release and Debug on **.NET 10: 4,818 / .NET 8: 4,386** per
configuration. Each actual test-directory engine/test assembly is rebuilt, frozen and checked against
**50 runtime /two changed test-source PDB documents** before its full `--no-build` invocation. The
first Release .NET 8 full run retains one 30-second fork read-boundary timeout (**4,385/4,386**);
the unchanged isolated read-boundary family passes **48/48**, and the unchanged complete rerun passes
**4,386/4,386**. Its scheduling/root-cause attribution remains unproven; no timeout is relaxed.
Raw evidence, deliberate mutation, restored-source comparison and frozen artifacts are retained in
`artifacts/p7-completion/merge-state-ad659a9`.

The preceding runtime's hosted acceptance **41132108776** is now terminal. Nightly independently
verifies **16/16 budgets /48 children** at exact 2 GiB inputs and original 200 MiB heap/RSS guards,
with maximum absolute RSS **38,678,528 bytes**. Push remains **13/16 budgets**, as recorded above.
The full paired benchmark independently validates all **126 observations**, original outputs, all
three sizes and actual **4,294,967,296-byte** heap caps, with **50 runtime /six probe** PDB source
checks. Original 100 MB stream/tree ratios are:

| Workload | Stream /tree median | Original target |
|---|---:|---|
| identity | 1.086410 | Fail |
| adjacent | 0.801045 | Pass |
| accumulator | 1.183693 | Fail |
| iterate | 0.830945 | Pass |
| merge | 0.887267 | Pass |
| fork | 3.043689 | Fail |
| source-document | 1.120546 | Fail |

These hosted observations belong to `bd89eae`, not the new compiler change. The seven-workload target
still fails; comparisons between separate hosted runs are not controlled attribution of improvements.
Benchmark archive SHA-256: `168c95f5bdc1201beb63e28b6ce1d9872e5fafa905f1169983a3558ce99f6f9b`.
Evidence: `contracts-linux-{push,nightly,benchmark}-bd89eae`. Broader review of saved compatibility,
paged tree storage, numeric conversion, schema identity and fork culture ownership establishes no
additional actionable defect in those reviewed areas; full-branch review remains open.

Current-source hosted CI, remaining applicability/state accounting, original absolute memory and
throughput targets, public integration, complete review, merge and release remain required. Full P7
is not complete, and `supports-streaming` stays `no`.

### Process-wide GC isolation and fork projection investigation, 2026-10-31

The retained full-suite fork timeout now has direct process evidence. A fresh .NET 8 run of the
frozen prototype engine records **2,927 induced collections** and **77.353 seconds** of paired
runtime suspension/resumption intervals. Its 4 MiB legacy fork read-boundary failure overlaps
**1,279 pauses /28.525 seconds** of its **30.319-second** wall time. The same frozen binaries pass
**100** standalone repetitions of that case and **1,000** accumulator/fork repetitions. Another
full run retains both fork and merge 30-second timeouts; the Debug .NET 10 run retains one
10-second accumulator/fork cancellation. Those failures and all stack/GC traces remain recorded.

`StreamedAccumulatorLifetimeTests`, `StreamedMergeLifetimeTests` and `StreamTeePayloadTests` now
use the repository's existing nonparallel `Process-wide state` collection. Their forced collections,
input sizes, assertions, case counts and original cancellation deadlines remain intact. Ordinary
collections retain their parallel execution. The [contract map](p7-runtime-contracts.md#process-wide-collection-isolation)
records the process-wide mutation and its isolation.

All four current-branch full suites pass: **.NET 10: 4,818 / .NET 8: 4,386**, each in Release and
Debug. Every actual engine/test DLL and PDB is frozen before its complete `--no-build` run; source
checks match **50 runtime /four changed test documents**, and hashes still match after execution.
The independent prototype also passes all four full suites (**4,895 /4,445** per configuration),
with **54 runtime /five test documents** checked. Its runtime DLLs and PDBs are byte-identical
before and after the test-isolation fix in all three configurations with a retained earlier engine.
The repaired test scheduling preserves those runtime binaries and GC policy.
Evidence: `artifacts/p7-completion/suite-gc-isolation-79cb441/` and
`fork-projection-79cb441/{full-net8-gc-diagnostic,full-net8-diagnostic,isolation}/`.

Fork input projection remains an **isolated, unadopted prototype**. The compiler derives a union
of required child paths, retains complete selected values and structural headers, and falls back
to the existing full input for unproven shapes. Review/tests expose and repair missing writer
classification, inherited expanded-text dependencies (**18 failing controls before repair**) and
unbounded filtering behind a partial batch. The corrected producer counts original logical source
events toward its existing batch capacity, preserving bounded lookahead, cancellation and early
prong completion. Source parsing and validation continue for omitted events.

The initial unbounded candidate's faster result is ineligible. Both corrected macOS cohorts remain
retained: the second does not reproduce the first's elapsed-time benefit. Fresh Linux ARM64 native
baseline/candidate controls use pinned runtime **10.0.14**, two CPUs, exact **100,000,000-byte** inputs
and the original **4,294,967,296-byte** heap guard. All **12** controls validate original output
fingerprints and all **32 stylesheet sources** remain unchanged. Runtime PDB checks match **50 /54**
source documents and each probe matches its six source documents. Candidate/baseline medians are
**5,656.942 /6,087.893 ms** elapsed and **6,642.715 /7,094.642 ms** full-child CPU, respectively:
about **7.1% /6.4%** lower. Median absolute RSS rises **135,168 bytes**, from **25,331,712** to
**25,466,880**. The footprint cost requires further work before adoption. These are controlled
local diagnostics, not full hosted P7 acceptance. Evidence: `fork-projection-79cb441/linux-paired-audit.json`
and its complete retained raw cohorts, failed controls, source snapshots and frozen engines.

The current branch's runtime remains unchanged by this test-isolation repair. The original memory,
throughput, remaining applicability/state, public integration, complete review, final-source CI,
merge and release requirements stay open. Full P7 remains active and `supports-streaming` stays `no`.

### Fork reclamation qualification and partial-prefix contracts, 2026-10-31

The projection experiment recorded above is now rejected. Its original-guard single-CPU controls
retain exact 200 MiB inputs, full original outputs and actual 20 MiB managed/RSS guards. The compact
candidate uses about 12% more full-child CPU and slightly more RSS; neither variant passes the RSS
budget. Earlier multicore gains remain recorded and do not establish eligibility. No projection
compiler/runtime source is adopted. Evidence: `artifacts/p7-completion/fork-projection-79cb441/decision.json`
and `original-guard-audit.json`.

An independent position-based reclamation experiment also remains unadopted. Twelve alternating Linux
ARM64 NativeAOT 100 MB controls validate all outputs and original 4 GiB heap guards. Baseline/candidate
medians are 6,135.450 /6,121.551 ms; the 0.23% elapsed reduction accompanies 32,768 bytes more absolute RSS.
The initially missing alias reconciliation causes two retained timeouts before correction; corrected
focused runs pass 340 .NET 10 /301 .NET 8 checks. No useful throughput benefit is established, and its
runtime source is restored. Evidence: `tee-position-reclaim-ad6a998`.

Fresh CPU/contention traces of the source-verified production engine separate executing samples from
parked-thread samples. A diagnostic-only counter build records 6,757,348 reclamation calls and 421,103
waits while forwarding 100 MB. Instrumented traces/counts guide investigation; they cannot replace
uninstrumented timings or process-memory acceptance. Evidence: `fork-cpu-ad6a998`.

A separate atomic-reclamation prototype clears slots only after the final owner releases them and
advances the completed prefix without taking the ring gate for every event. Review corrects a
full-ring wakeup race (two original focused failures retained) and bounds scan work to the captured
publication boundary. The corrected single-slot variant passes 225 focused checks, but twelve native
controls show 10.53% more elapsed time and 5.27% more full-child CPU. It is rejected. A bulk-prefix
variant with atomic slot clearing passes the same 225 checks; its first twelve native controls show
3.30% lower elapsed time and 3.38% lower CPU, with 67,584 bytes less median reported RSS. Six additional
single-CPU controls consume exact 209,715,200-byte inputs, validate full original outputs and enforce
actual 20,971,520-byte heap/RSS guards. CPU medians improve 5.81%, but full-lifetime RSS medians rise
61,440 bytes (23,552,000 /23,613,440); both variants fail all three RSS controls. The launcher uses a
streaming hash and stays below 20 MiB before every child. All four source-verified candidate unit
configurations pass (4,820 .NET 10 /4,388 .NET 8 in each configuration, 50 runtime /five test PDB documents).
The candidate is nevertheless rejected: the throughput gain does not establish the required memory
non-regression. All failed controls, raw cohorts, source snapshots and frozen engines are retained;
production runtime source is restored. Evidence: `tee-lock-free-reclaim-ad6a998`.

The branch adds the [partial-prefix contract](p7-runtime-contracts.md#partial-prefix-reclamation-before-another-read)
for one and four aliases. Both cases pass on the production tee and fail an isolated delayed-reclamation
mutation before another read is permitted. The source mutation and all runtime optimization candidates
remain outside the branch. All original P7 completion, review, merge and release gates remain required.

Final production-source verification passes all four full unit suites: Release/Debug each pass
4,820 checks on .NET 10 and 4,388 on .NET 8, including both new alias cases. Every actual engine/test
DLL and PDB is frozen before execution; source checks match 50 runtime /five test documents and
all four file hashes remain unchanged afterward. Runtime/probe sources, original fixture inventories,
public APIs and acceptance guards are unchanged. Evidence: `tee-prefix-contract-ad6a998/main-full`.

### Copied accumulator value-graph ownership, 2026-10-31

The preceding branch head `138fcee` passes every required hosted CI job in run **41156042431**.
The next S5 qualification finds an actual retention defect: copies of two early rows keep all 128
later payload documents through their source-navigator provenance. Completed, corresponding copied
subtrees now own their recorded accumulator phases instead of retaining the source execution.
Ordinary copy chains and validation preserve those phases; cycle notifications remain shared without
subscribing early copies to later ordinary error payloads. An initial prototype does retain a later
127-node error value; its six failures remain recorded beside the corrected results.

The [graph contracts](p7-runtime-contracts.md#copied-accumulator-graphs-and-error-ownership) cover node
windows, opaque functions, intentionally growing maps, explicit copies and required error objects.
All four complete source-verified unit suites pass: **5,285 .NET 10 /4,699 .NET 8** in both Release and
Debug. A source-retention mutation fails all six selected controls and the restored implementation
passes all six. A separate strictly trimmed NativeAOT consumer passes **72** saved-style graph cases
with actual forwarded sources. The canonical native generator still emits all **32 unchanged styles**;
actual native PDB checks verify **51 runtime /six probe** source documents. Evidence:
`artifacts/p7-completion/accumulator-graphs-138fcee`.

This fixes completed-copy ownership, not all S3–S6 applicability and retained-state obligations.
Open replay captures and structural changes retain their existing provenance behavior and need further
qualification. Original exact-size native memory/throughput gates, complete review, final-source CI,
public integration, merge and release remain required. Streaming support stays unclaimed.

The same candidate completes the unchanged local Linux ARM64 push matrix: all **48 children**
validate original outputs and **13/16** measured workloads meet the exact 209,715,200-byte input /
20,971,520-byte absolute RSS and managed guard. Merge (**23,597,056**), fork (**24,051,712**) and
source-document (**22,917,120**) exceed the original budget. The 48-child small-input zero-limit smoke
also passes, as functional evidence only. Reviewed main files match the frozen tested and native
sources byte-for-byte. These results do not close the original memory gate. Raw children and audits:
`accumulator-graphs-138fcee/canonical-push` and `canonical-push-audit.json`.

Hosted Linux x64 qualification of committed **cddc819** in run **41161002015** completes both original
memory matrices. Push retains **13/16** budget passes: merge **24,363,008**, fork **24,907,776** and
source-document **23,941,120** bytes exceed 20,971,520. Nightly passes **16/16**, with maximum absolute
RSS **39,391,232 bytes** under 209,715,200. All **96 children** validate outputs; measured input sizes
are exactly 209,715,200 /2,147,483,648 bytes and their actual managed guards match the original limits.
All 32 canonical stylesheet sources remain unchanged. Downloaded archives match GitHub artifact
digests; raw children and audits remain under `contracts-linux-{push,nightly}-cddc819`. This is
committed-source evidence; it does not qualify a later prototype or close the failed push gate.

### Deferred copied phases at replay completion, 2026-10-31

The [enclosing-replay contracts](p7-runtime-contracts.md#copies-made-during-an-enclosing-replay) extend
the preceding completed-copy repair. An isolated `cddc819` regression retains all 16 /128 payloads
through two early copies. The fix keeps live lookup while a capture is open, then atomically hands
matching completed subtree phases to the copy through a weak completion notification. An interrupted
capture becomes immutable without being declared complete or replayable. Source closure, rule-error
payloads, cycle propagation, cancellation and collection of discarded copies have explicit controls.

Full source-verified Release/Debug suites pass 5,441 .NET 10 /4,807 .NET 8 checks per configuration.
The compiled graph matrix adds 144 /96 cases; an independent NativeAOT consumer passes all 96 saved
cases. All 1,313 tracked source/test files match the committed base except the five candidate files.
The retention mutation fails eleven controls and restoration passes all twelve. Evidence and all
failed fixture/prototype controls: `artifacts/p7-completion/accumulator-open-replay-cddc819`.

The same candidate completes the original local Linux ARM64 native push matrix, with all 48 outputs
correct and 13/16 budget passes. Merge **23,683,072**, fork **23,773,184** and source-document
**23,064,576** bytes exceed the unchanged 20,971,520-byte limit. Exact measured inputs and actual heap
guards remain 209,715,200 /20,971,520 bytes, and all 32 canonical stylesheets are unchanged. These
observations do not establish a controlled performance improvement or qualify the failed memory gate.

Pending-copy validation chains, fork-specific lifetime and structural-mismatch applicability still
need complete qualification. Original memory/throughput targets, remaining S3–S6 contracts, public
integration, full review, final-source CI, merge and release remain required. Streaming support stays
unclaimed and the full P7 objective remains active.

### Pending-copy validation and fork graph contracts, 2026-10-31

The [additional copied-graph contracts](p7-runtime-contracts.md#pending-copy-chains-validation-and-fork-views)
qualify pending copy chains, lax validation, schema-added default text on copied parents and enclosing
replays in one or two fork prongs. The committed `a2998a2` runtime requires no change. All new checks
pass in Release/Debug on both frameworks: 508 .NET 10 /340 .NET 8 per configuration. The preceding full
unit results remain 5,441 /4,807; the new figures describe additional focused checks. Actual engines
and PDBs are frozen, with 51 runtime /two changed test documents checked. All 1,314 tracked source/test
files match the base except those two tests. The retention mutation fails forty controls /passes
forty-eight; restored source passes all 88. Independent NativeAOT passes 180 saved-style cases plus
four supplemental runtime fork lifetime cases. Evidence: `accumulator-pending-forms-a2998a2`.

The completed hosted **cddc819** benchmark in run **41161002015** preserves all **126 valid
observations**, exact 1,000,000 /100,000,000 /1,000,000,000-byte inputs and actual 4,294,967,296-byte
managed guards in both modes. All output fingerprints agree; no observation is failed or missing.
The downloaded archive matches GitHub's digest, engine/driver hashes match the tested files, and
51 runtime PDB documents match the immutable commit. The original 100 MB throughput target still
fails for identity (**1.080878** stream/tree), accumulator (**1.191722**), fork (**2.974394**) and
source-document (**1.074152**). Adjacent (**0.806387**), iterate (**0.840719**) and merge (**0.890579**)
pass. The 5% display band does not waive any target. Raw trials, first-output observations and audit
remain under `contracts-linux-benchmark-cddc819`. This is cddc819 evidence, not a later-head timing claim.

Schema-created nodes without source counterparts and arbitrary structural mismatch need an explicit
applicability disposition. Complete remaining runtime/state qualification, the original memory and
throughput targets, final-source CI, full review, public integration, merge and release remain open.

### Shared source-frame metadata investigation, 2026-10-31

Fresh profiling of the `711b0b3` runtime attributes about 1.28 GB of weighted allocation samples to
184-byte streamed nodes while forwarding the unchanged 100 MB identity input. These EventPipe
samples locate allocation costs; they are not exact allocation totals or acceptance timings.
An isolated shared-context prototype reduces nodes to 152 bytes and total sampled allocation by
11.67%. Its focused Release .NET 10 run passes 3,174 checks. Two alternating single-CPU Linux ARM64
NativeAOT cohorts retain all 48 valid outputs, but gains do not repeat consistently. Same-input,
warmed exact allocation controls additionally find about 22.9% more allocation for schema-validated
rows. That design is rejected.

A revision keeps mutable schema snapshots on their individual nodes. Nodes measure 160 bytes and
all 137 focused navigator, namespace, replay, fork-input and spool checks pass in Release .NET 10.
Exact allocation controls at 1,024 /8,192 rows improve plain input by about 12.2% and schema input by
7.2%; repeated language or namespace overrides still cost about 1.5% /1.2% more. A separate 24-child
native cohort validates every original output. Identity and source-document elapsed medians improve
3.61% /3.80%, but fork is 2.29% slower and its median absolute peak rises 176,128 bytes. This revision
is also left unadopted. Neither an allocation-only benefit nor one workload's improvement establishes
eligibility for the full P7 objective.

All 72 native controls use exact 100,000,000-byte inputs and actual 4,294,967,296-byte managed guards,
with diagnostic RSS guards disabled. They do not replace the original push/nightly memory matrices
or seven-workload throughput acceptance. Each native image and frozen unit engine matches 51 runtime
PDB documents; each native probe matches six probe documents, and all 32 canonical stylesheets remain
unchanged. Both candidate sources, frozen engines, traces, raw controls and decisions are retained in
`artifacts/p7-completion/node-header-711b0b3`. A stale expected baseline hash stops the first controller
before any child launches; a diagnostic constructor-selection failure is also retained separately.
The corrected controls use the original independently audited baseline image.

No prototype runtime change enters the branch. The isolated source is restored and all 1,312 regular tracked
source/test files match `711b0b3`. Remaining S3–S6 contracts, original memory/throughput gates, public
integration, final review/CI, merge and release remain required. These investigations close no original
acceptance gate and do not authorize a streaming-support claim.


### Fork frame-graph lifetime contracts, 2026-10-31

The preceding committed contract head `711b0b3` passes all four hosted CI jobs in run **41165601804**.
The new [fork graph contracts](p7-runtime-contracts.md#fork-frame-graphs-with-variable-header-payloads)
measure original and projected frames with variable text/attribute payloads, fixed ancestor chains,
namespace scopes and quiescent source/prong checkpoints. Three capacity/prong/depth models run at two
input lengths, each with normal completion, first-prong failure and external cancellation. Closed
sources account for their final current frame chain; all observed frames collect after owners release.
Three independent parser/EOF controls cover the generated non-seekable input. The initial repeated-EOF
fixture failures remain retained and do not establish a production defect.

All four full source-verified unit suites pass: **5,970 .NET 10 /5,168 .NET 8** checks in both Release
and Debug. Every actual engine/test DLL and PDB is frozen before testing, source checks cover 51 runtime
/nine test documents, and all four binary hashes remain unchanged afterward. A retained-frame-history
mutation fails all eighteen graph cases /passes the three generator controls; restoration passes all
21. An independent warning-free NativeAOT consumer passes the same 21 checks and eight assertion-failure
controls, with JIT-host rejection and exact fixture/engine/PDB verification. Evidence:
`artifacts/p7-completion/fork-frame-graphs-beee608`.

Production runtime, tee capacity/thread model, canonical fixtures, original RSS/heap limits, static
analysis inventories and public APIs are unchanged. The graph's logical-character counts exclude
parser/name-table implementation storage, schema objects, array/object overhead, explicit replays and
buffered output; those remain separate accounting obligations. Original memory/throughput acceptance,
remaining S3–S6 qualification, public integration, complete review, final-source CI, merge and release
remain required. No streaming-support claim is made.


### Disposed current-frame ownership and namespace measurement, 2026-10-31

The closed-source graph review establishes a retention defect: `65423ee` keeps its last row through
its private current-frame reference after disposal. Releasing that reference to the document root
fixes all twenty ownership regressions, including callback failure and caller-retained headers. The
[fork/disposal contracts](p7-runtime-contracts.md#disposed-source-current-frame-ownership) now require
zero unrequested row frames after disposal. Closed-navigation errors, borrowed-reader ownership,
identities and header values remain unchanged. The omitted-release control fails 32 combined checks
/nine pass; restoration passes all 41.

The corrected runtime passes all four full unit suites: **5,990 .NET 10 /5,188 .NET 8** checks in Release
and Debug, with frozen binaries and source checks. A separate warning-free NativeAOT consumer passes
46 graph/disposal/namespace checks and nine assertion-failure controls. The namespace allocation test
also warms its cached path before the unchanged 10,000-traversal /4,096-byte measurement; all twelve
per-enumeration allocation controls fail and all twelve restored checks pass. This follows the
retained Ubuntu CI failure in run **41169823881**, not a runtime namespace change. Its exact 98,352-byte
Linux observation has not been reproduced locally. The final full suites including the measurement-only
correction also pass all four configurations with the same counts. Every actual engine/test DLL and PDB
is frozen before testing, 52 runtime /11 test source documents are checked per configuration, and all
four binary hashes remain unchanged afterward. Fresh hosted CI remains pending.

The unchanged original local Linux ARM64 push matrix finishes all **48 children** with correct output
and **13/16 budgets**. Merge **23,707,648**, fork **24,096,768** and source-document **23,048,192 bytes**
exceed the original 20,971,520-byte RSS limit. Every measured input is exactly 209,715,200 bytes with
an actual 20,971,520-byte managed guard; all 32 canonical stylesheets are unchanged. The separate
small-input zero-limit smoke passes all 48 children as functional evidence only. Source/runtime/probe
PDB checks and all failed fixture/adapter/control runs remain under `disposed-current-65423ee`.

Hosted Linux x64 qualification of committed **42519e9** in run **41172520570** completes both original
memory matrices. Push retains **13/16** budget passes: merge **24,506,368**, fork **24,768,512** and
source-document **24,043,520** bytes exceed the unchanged 20,971,520-byte limit. Nightly passes all
**16/16**, with maximum absolute RSS **39,432,192** bytes under 209,715,200. All **96 children** validate
original outputs; measured inputs and actual managed guards match the original exact sizes/limits.
Both downloaded archives match GitHub artifact digests, all 32 canonical stylesheet sources are
unchanged, and 52 runtime PDB documents match the committed sources. Raw children and independent
audits remain under `contracts-linux-{push,nightly}-42519e9`.

The current-source nightly memory gate passes; push still fails. Final-source CI and hosted throughput
qualification, remaining S3–S6 accounting and public integration are still required before full review,
merge and release. Streaming support remains unclaimed.

### Fork string-value batching investigation, 2026-10-31

An isolated prototype reuses the tee's existing buffered-event predicate to collect an element's
string value without constructing unneeded descendant frames. It conservatively preserves the
ordinary event path for observers, replay captures, consumption proofs and interested descendant
cursors. A correction checks the individual prong's cancellation token between collected events.
The source-verified diagnostic JIT engine confirms that all 9,121 string-value calls in the unchanged
1 MiB canonical fork probe, including warmup, qualify for batching.

The first native prototype has a 1.91% lower elapsed median with one CPU. The corrected prototype
has a 2.01% lower median with one CPU, but a **0.26% higher** median with two CPUs. Absolute peak RSS
is unchanged within each cohort. All **21 native children** validate identical original outputs on
exact 100,000,000-byte inputs with actual 4,294,967,296-byte managed guards. Diagnostic RSS guards
are disabled; these are controlled comparisons, not original acceptance passes. Each candidate's
runtime/probe PDB checks cover 52 /six source documents, and all 32 canonical stylesheets are unchanged.
The modest gain does not repeat in the two-CPU cohort, so neither prototype is adopted.

The [fork value contracts](p7-runtime-contracts.md#fork-string-value-consumption) remain useful without
the optimization. All 57 pass against the unchanged `42519e9` runtime in Release/Debug .NET 8/10.
The guarded candidate also passes 1,667 .NET 10 /1,278 .NET 8 focused checks in each configuration.
Removing its observer/replay/cursor guards fails nine contracts while 48 controls pass. Frozen actual
engines, source checks, raw failed controls, all three native cohorts and the rejection decision remain
under `artifacts/p7-completion/fork-values-42519e9`. The initial fixture-only compile, replay-setup and
tiny-capacity synchronization failures are retained separately. None establishes a production defect.

Production runtime, fork threads/capacity, fixtures, original budgets and public streaming claims are
unchanged. Original failed push/throughput targets, remaining runtime accounting and public integration,
final review/CI, merge and release remain required.

### Explicit simple-type copy provenance, 2026-10-31

The next schema-copy review establishes a runtime defect in `b219c7c`: the `xs:untypedAtomic`
validation branch returns before preserving copied accumulator provenance. On three source rows,
inherited before/after values restart at one for every copy instead of retaining their source
positions. Twelve valid .NET 10 baseline cases fail; the other 132 controls pass. The corrected
branch reuses the existing schema-copy provenance helper after successful validation.

The [six-form copy contracts](p7-runtime-contracts.md#explicit-simple-type-validation-and-copied-phases)
cover both APIs, normal/debug compilation, actual forward/tree paths, supported saved loading,
inheritance enabled/disabled and unchanged `XTTE1540` errors. All four full unit suites pass:
**6,215 .NET 10 /5,357 .NET 8** checks in both Release and Debug. The new fixture adds 168 /112 cases;
actual binaries are frozen and source checks cover 53 runtime /14 test documents. An isolated
omitted-handoff control fails twelve cases /passes 156; restoration passes all 168.

A separate strictly trimmed NativeAOT consumer passes 104 phase/error cases from 48 saved styles,
with independent 7/32-row inputs, two assertion-failure controls, source-verified engines and actual
forwarding. All three consumer PDB documents, including the exact shared fixture source, are checked.
Raw failures, the invalid initial fixture, frozen engines, native sources and all controls remain
under `artifacts/p7-completion/validated-copy-b219c7c`.

The unchanged complete canonical NativeAOT probe is rebuilt with 53 runtime /six probe source checks
and all 32 original stylesheet sources. Its generator uses the installed JIT host; the measurement
binary retains pinned 10.0.14 runtime/reference/compiler packs. The initial generator requested an
unavailable JIT host and stops before generating styles; that tooling failure is retained separately.
The original local Linux ARM64 push matrix finishes all **48 children** with correct outputs and
**13/16** budgets. Merge **23,605,248**, fork **23,941,120** and source-document **23,093,248** bytes
exceed the unchanged 20,971,520-byte RSS limit. Measured input sizes and actual managed guards are
exactly 209,715,200 /20,971,520 bytes. Raw children and the independent audit are retained under
`validated-copy-b219c7c/canonical-push`. This is current-source qualification, not a controlled
before/after performance comparison. No original throughput or failed push-memory target closes
here. Remaining runtime applicability/accounting, public integration, final review/CI, merge and
release remain required, and streaming support remains unclaimed.

### Hosted throughput and tee checkpoint liveness, 2026-10-31

The completed hosted `42519e9` benchmark in run **41172520570** retains all **126 valid observations**:
three per mode/workload at exact 1,000,000 /100,000,000 /1,000,000,000-byte inputs with actual
4,294,967,296-byte managed guards. All raw output fingerprints agree; no observation is failed or
missing. The archive matches GitHub's digest, the tested driver/engine hashes match the retained
files, and 53 runtime PDB documents match immutable `42519e9` sources. At 100 MB, the original target
still fails for identity (**1.185497** stream/tree), accumulator (**1.301764**), fork (**3.503189**) and
source-document (**1.190550**). Adjacent (**0.851996**), iterate (**0.881842**) and merge (**0.978959**)
pass. These are `42519e9` measurements, not timings for the later schema repair. Raw trials and the
independent audit remain under `contracts-linux-benchmark-42519e9`; no display band waives a target.

Hosted `b219c7c` CI passes Ubuntu, NativeAOT and ordinary performance. Windows .NET 8 passes all
5,245 units; .NET 10 passes 6,046 /fails one existing tee prefix test. Its single ten-second token
expires at a barrier during 128 independent checkpoints; no reclamation assertion fails. Raw log:
`artifacts/p7-completion/ci-b219c7c-windows`. The source tee and its original checkpoint populations
remain unchanged. The test now gives each rendezvous the same ten-second liveness guard, with
cancellation reserved for failure/cleanup, while retaining every immediate partial-prefix assertion.

A diagnostic-only 100 ms delay at each checkpoint reproduces whole-run cancellation in both alias
cases on the old fixture. The corrected guard passes the same two slow-progress cases in about
26 seconds total. This reproduces the timeout condition, not the exact Windows scheduling load.
Delaying production reclamation still fails both immediate prefix assertions. All 37 tee checks
pass in Release/Debug .NET 8/10, with frozen engines, 53 runtime /one fixture PDB source checks and
unchanged binary hashes. The restored runtime DLLs equal the four full-tested schema-fix engines.
Controls, source snapshots and failures remain under `artifacts/p7-completion/prefix-progress-a844867`.
Final-head CI, original failed memory/throughput targets, remaining runtime applicability/accounting,
public integration, complete review, merge and release remain required.

### Hosted original gates on the tee-guard head, 2026-10-31

Hosted qualification run **41177833642** tests immutable `5a53141`. Both downloaded native memory
archives match GitHub's published digests. All **96 children** have valid output, the original 32
stylesheet sources are unchanged, and 53 runtime PDB documents match that commit. The nightly
matrix passes **16/16**, with exact 2,147,483,648-byte measured inputs, actual 209,715,200-byte managed
guards and maximum absolute peak **39,497,728 bytes**. The push matrix passes **13/16** with exact
209,715,200-byte inputs and actual 20,971,520-byte managed guards. Merge **24,354,816**, fork
**24,702,976** and source-document **23,912,448 bytes** exceed the original absolute RSS budget.
Raw children, immutable source snapshots and audits remain under
`artifacts/p7-completion/contracts-linux-{push,nightly}-5a53141`.

The same run's complete paired benchmark retains **126 valid observations**, three per mode in
all 42 workload/size groups. Inputs are exactly 1,000,000 /100,000,000 /1,000,000,000 bytes, and each
child reports the actual 4,294,967,296-byte managed guard. All output fingerprints agree; no
observation is failed or missing. Archive, engine/driver/controller hashes and 53 immutable runtime
PDB source documents are verified. The original 100 MB target still fails for identity
(**1.115087** stream/tree), accumulator (**1.165179**), fork (**3.187783**) and source-document
(**1.137174**). Adjacent (**0.764559**), iterate (**0.792683**) and merge (**0.918490**) pass. The 5%
display band waives no target. Raw first-output observations and the audit remain under
`contracts-linux-benchmark-5a53141`.

These are hosted `5a53141` observations, not a controlled comparison with earlier hosts or evidence
for the subsequent schema-default repair. The failed original push/throughput targets and all
remaining P7 completion requirements stay open.

### Assessed schema-default element content, 2026-10-31

The [default/fixed-content contracts](p7-runtime-contracts.md#schema-supplied-default-and-fixed-element-content)
establish default insertion and normalization defects. The reviewed 712-case .NET 10 baseline on
`5a53141` fails 556 /passes 156. The repair uses the validator's actual default selection, assessed
strings and existing whitespace facets while preserving QName prefixes, date/time offsets and
calendar duration units from schema declarations. The new fixture has 712 /568 cases on .NET 10/8,
covering both APIs, normal/debug compilation, supported saved execution, actual forward/tree paths,
borrowed-reader ownership and copied-parent phases. Numeric/list lexical alternatives are compared
by typed value rather than requiring the BCL reader's canonical text representation.

Review rejects the initial CLR datatype-to-string candidate. Its original 688-case, full-unit and
native passes do not establish XDM value correctness: all 24 newly added semantic cases fail.
The fifteen-datatype text comparison passes sixty BCL comparisons but fails twelve actual XDM
QName/dateTime/duration comparisons. Those results, the original fixtures and all 48 original-budget
native children are retained. That rejected candidate passes 13/16 local push budgets; merge
23,695,360, fork 24,088,576 and source-document 23,048,192 bytes exceed the unchanged 20,971,520-byte
RSS limit. Those measurements are not qualification for the reviewed revision.

The corrected five-variant control run executes all 712 cases per variant with source-verified,
frozen engines: baseline 556 failures; rejected CLR conversion 24; omitted miscellaneous-only
insertion 540; omitted normalization 256; restored repair zero. Socket-denied zero-case controls are
explicitly invalidated. Raw evidence remains under `artifacts/p7-completion/schema-defaults-5a53141`.
The reviewed revision passes all four full Release/Debug .NET 10/8 suites (**6,927 /5,925** checks per
configuration), with frozen binaries and 53 runtime /16 test PDB source checks. Its warning-free
trimmed NativeAOT consumer passes **568** functional checks from the shared fixture, including the
24 typed-value regressions, 144 saved styles at independent 7/32-row inputs, actual forwarding,
borrowed-reader ownership and assertion-failure controls. The engine equals the full-tested Release
engine; 53 runtime /three consumer documents match their sources. The separate fifteen-datatype
review now passes all sixty semantic comparisons. These functional checks do not qualify performance.

The preceding `5a53141` head passes all four hosted CI jobs in run **41177801476**, including Windows;
that is not CI evidence for this revision. Remaining schema-created-node applicability, original
qualification gates, public integration, full review, final-source CI, merge and release are required.

### URI scheme initialization call stack, 2026-10-31

A diagnostic-only `dlopen` tracer records ICU initialization in the reviewed native source-document
workload. Symbolization against the exact native debug file locates the path in
`XmlQueryContext.OpenStreamedSource` → configured `XmlResolver.ResolveUri` → `Uri.CheckSchemeSyntax`
→ `UriHelper.SpanToLowerInvariantString` → `MemoryExtensions.ToLowerInvariant` → globalization/ICU
initialization. The canonical 209,715,200-byte input and output fingerprint are unchanged; the child
reports the original 20,971,520-byte managed guard. The tracer disables the RSS status gate explicitly
and runs concurrently with functional tests, so neither timing nor RSS is acceptance evidence.

This attributes an initialization cause; it does not establish a safe replacement for URI resolution
or change resolver callbacks, globalization or runtime policy. Source/binary hashes, raw loader
stacks, symbols and guard/output verification remain under
`artifacts/p7-completion/icu-callstack-lexical-5a53141`. An initial approval review misreads the original
hexadecimal GC setting as decimal; read-only controller/child evidence verifies its actual 20 MiB
meaning before the unchanged diagnostic is approved and run. No original guard is relaxed.

### Reviewed schema-default checkpoint and original gates, 2026-10-31

Commit `87c40e7` passes all four hosted CI jobs in **41182562412**, including ordinary performance,
NativeAOT and both operating systems. All four local full Release/Debug suites pass **6,927 .NET 10
/5,925 .NET 8** checks per configuration. All 1,318 present committed source/test files match
the tested checkout; frozen DLL/PDB files remain unchanged during testing. The schema-default
repair, changelog and contracts are pushed to draft PR **#27**.

Its fresh local Linux ARM64 original push matrix validates all **48 children** and passes **13/16**
budgets. Merge **23,707,648**, fork **23,642,112** and source-document **23,093,248 bytes** exceed the
original 20,971,520-byte absolute RSS limit. Measured inputs are exactly 209,715,200 bytes; each child
reports the original 20,971,520-byte managed guard. The native image matches 53 runtime /six probe
PDB source documents, and all 32 stylesheet sources remain unchanged. Evidence:
`schema-defaults-5a53141/lexical-canonical-push` under the P7 completion artifacts.

Hosted qualification **41182595937** is terminal. Independently audited archives match GitHub's
digests and immutable `87c40e7` sources. All **96 memory children** produce correct output. Nightly
passes **16/16** with exact 2,147,483,648-byte inputs, actual 209,715,200-byte managed guards and
maximum absolute peak **39,686,144 bytes**. Push passes **13/16** with exact 209,715,200-byte inputs
and actual 20,971,520-byte managed guards. Merge **24,387,584**, fork **25,006,080** and source-document
**23,154,688 bytes** exceed the original RSS limit. Raw children, immutable source snapshots and
audits remain under `contracts-linux-{push,nightly}-87c40e7`.

The full paired benchmark retains **126 valid observations**, with zero failed/missing trials,
three per mode in every workload/size group, exact 1,000,000 /100,000,000 /1,000,000,000-byte inputs
and actual 4,294,967,296-byte managed guards. All output fingerprints, engine/driver/controller
hashes and 53 immutable runtime PDB source documents are verified. At 100 MB:

| Workload | Stream / tree median | Original no-slowdown target |
|---|---:|---|
| identity | 1.111446 | Fail |
| adjacent | 0.781133 | Pass |
| accumulator | 1.198711 | Fail |
| iterate | 0.805264 | Pass |
| merge | 0.900192 | Pass |
| fork | 3.175741 | Fail |
| source-document | 1.131682 | Fail |

The 5% display band waives no target. Raw first-output observations and the independent audit remain
under `contracts-linux-benchmark-87c40e7`. These separately hosted/local cohorts do not establish
causal before/after performance changes. The failed original targets, remaining S3–S6 applicability
and accounting, public integration, final review, merge and release remain open.

### Rejected cursor and optional-state experiments, 2026-10-31

An isolated padded reader-position array passes all existing tee/fork cases in Release/Debug:
**348 .NET 10 /333 .NET 8** per configuration, preserving the exact baseline case population.
All 24 alternating NativeAOT controls at one/two CPUs have valid output, exact 100,000,000-byte
inputs and actual 4,294,967,296-byte managed guards. Streaming elapsed medians are **5.17% /0.22%
higher**, respectively, so the candidate is rejected. Its initial explicit-layout type nested in
a generic class fails CLR loading; those failures are retained separately. This repeats the earlier
`tee-position-padding-79cb441` investigation and confirms no benefit on the current checkpoint;
the earlier artifact should have been consulted before repeating it. Evidence: `position-padding-87c40e7`.

A distinct experiment adds a peer-progress check before locking to the earlier position-based
reclamation protocol. Its initial release-only head publication fails three alias-progress/prefix
checks. Full-fence head publication before cursor resampling passes all four 348 /333-case focused
configurations and 160 repeated checks using the actual test assembly. This follows the distinction
between release and full-fence operations in the [.NET memory model](https://github.com/dotnet/runtime/blob/main/docs/design/specs/Memory-model.md).
The new 24-child NativeAOT cohort validates all outputs, but one-CPU elapsed median rises **5.65%**
while the two-CPU reduction is only **0.89%**. No useful overall improvement is established, so this
protocol is also rejected. Source checks cover 53 runtime /six probe documents, and every canonical
stylesheet source is unchanged. A generator invocation against an existing output directory is a
retained setup failure; its corrected build uses a new workspace. Evidence: `reclaim-progress-87c40e7`.

An optional-frame-state prototype packs schema, full-replay and value-replay references into one
slot, leaving atomic runtime facts and observer state independent. Identical allocation controls
on both frozen engines cover 1,024 /8,192 nodes and all eight field combinations. Both engines pass
216 metadata add/clear transitions. Empty/single-value nodes shrink **184 →168 bytes**, but nodes
with multiple optional values grow **184 →208 bytes**. This shifts allocation onto schema-aware
grounding, so the design is rejected before native performance measurement. Its complete Release
focused suites pass **4,698 .NET 10 /3,922 .NET 8** cases; remaining verification is explicitly stopped
after rejection and is not claimed to pass. Evidence: `optional-frame-state-87c40e7`.

None of these experiments enters production. Source callbacks, capacities, thread model, URI and
globalization behavior, original qualification limits and fixture/oracle data remain unchanged.

### Leaf-frame layout and dynamic-call storage controls, 2026-10-31

The owner requires stopping at a reviewed, deployable version and prioritizing the failed memory
and throughput gates. Speculative candidates remain local; another hosted acceptance run is not
dispatched merely because an allocation microbenchmark improves. Original qualification limits
remain unchanged.

An isolated leaf/header inheritance prototype reduces text, whitespace and comment frames from
184 to 136 bytes while elements remain 184 bytes. All 80 direct allocation rows preserve replay
field combinations without an increase. Actual traversal controls at 1,024 /8,192 rows reduce
allocation by about 12.2% for plain input and 7–9% for schema, namespace and language cases.
All four focused Release/Debug suites pass 4,698 .NET 10 /3,922 .NET 8 cases with unchanged baseline
case populations. An unchanged saved-style NativeAOT consumer passes all 568 functional checks,
including both assertion-failure controls. Frozen source checks cover 53 runtime documents.

The subsequent isolated single-CPU Linux ARM64 cohort retains 24 valid NativeAOT children, with
exact 100,000,000-byte inputs, actual 4,294,967,296-byte managed guards and diagnostic RSS guards
disabled. Candidate/baseline elapsed median ratios are identity 1.004138, source-document
1.036733, accumulator 0.983247 and fork 1.051119. RSS medians increase by 192,512 /200,704 /184,320
bytes for identity /accumulator /fork; source-document is unchanged. The design is rejected and
the primary verification source is restored byte-for-byte to all 1,318 present committed source/test
files. Evidence: `artifacts/p7-completion/leaf-frame-87c40e7`. The initial sandbox-blocked VSTest
launch and malformed diagnostic PDB arguments remain retained, separately from passing checks.

A separate dynamic-call prototype, based directly on `87c40e7`, stores its first two arguments
on the existing `PendingCall` object and retains array storage for additional arguments. Direct
controls cover arities 0 /1 /2 /3 /4 /7 /16 /64, capacity growth beyond the initial arity, argument
identity and independent returned arrays. One/two-argument call storage falls from 104 /112 to
64 bytes. Actual runtime observer controls reduce allocation by about 5.6% while preserving
computed accumulator values. The source-verified .NET 10 Release engine passes all 435 selected
function, pull, fork-runtime and fork-accumulator contracts.

All 24 isolated native children validate unchanged outputs. Relative elapsed medians are identity
0.981065, source-document 0.989390, accumulator 0.893797 and fork 1.009919. Accumulator's median
absolute RSS falls by 208,896 bytes; the other three medians are unchanged. These diagnostic
controls establish neither the original absolute memory gates nor the complete throughput gate.
Raw evidence and 53 runtime /six probe source checks are under `call-arguments-87c40e7`.
The follow-up removes temporary one/two-argument params arrays with the existing copy/conversion
policy.

The follow-up is retained after all four full Release/Debug suites pass 6,927 .NET 10 /5,925 .NET 8
cases, preserving every baseline case name. Frozen runtime/test binaries remain unchanged during
testing; source checks cover 53 runtime /16 test documents per configuration. All 1,318 present committed
source/test files match the verified candidate after adopting only `XsltLibrary.FunctionItems.cs`
and `XsltLibrary.Accumulators.cs`. The unchanged 144-style NativeAOT consumer passes all 568
functional checks and both assertion-failure controls. Public API and saved format 6 are unchanged.

Its 24 isolated one-CPU native observations validate all original outputs. Candidate/baseline elapsed
median ratios are identity 0.989099, source-document 0.970879, accumulator 0.953054 and fork 0.993543.
RSS medians change by -4,096 /+53,248 /+4,096 /-81,920 bytes, respectively. Six additional tree
accumulator observations remain separately retained; improving both modes is not a streaming
throughput pass. These cohorts do not replace the original full memory or paired benchmark gates.
The original failures, full S3–S6 accounting, public integration and delivery requirements remain open.

A separate inline accumulator-phase prototype passes 1,469 .NET 10 contracts but includes the
rejected leaf layout. It has no isolated workload benefit and is not adopted. Its frozen sources,
PDB checks, allocation controls and explicit source restoration remain under `inline-phase-87c40e7`.

A nullable value-event prototype keeps immutable fork headers inline in the existing bounded ring.
Its four focused configurations pass 361 .NET 10 /346 .NET 8 cases, including 13 new checks for
default values, ordered completion, concurrent aliases, disposal races and immediate reclamation.
All original cases remain present. Its 24 NativeAOT tree/stream children at one/two CPUs preserve
original input, output and heap guards, but fork stream medians regress 10.60% /6.07%, respectively.
It is rejected, with no production event, tee, capacity or thread-model change. Evidence:
`value-events-87c40e7`.

An isolated native layout build uses the SDK's profile-guided layout options with unchanged Size
preference and runtime policies. Its response file contains no MIBC input, and the SDK fails in
`FileLayoutOptimizer.PettisHansenSort` before producing an image. This is retained tooling evidence,
not a memory result or a runtime defect; neither these publishing settings nor a profile enter
production. Evidence: `native-layout-v3-87c40e7`.

A later zero-delimited tracked-file inventory identifies one omitted upstream XML fixture in the
isolated source copies (`Straße.xml`): 1,318 files are present and match, out of 1,319 tracked
source/test files. The preceding inventories are corrected accordingly. The 6,927 /5,925-case
claims describe `Xsl3.Tests`; they do not include the separate legacy upstream suite. A fresh
archive plus the two adopted runtime files verifies all 1,319 tracked files, including the original
fixture, before that independent suite runs. Evidence: `call-arguments-87c40e7/v3-upstream` and
`v3-source-inventory-correction.json`.
The separate Release upstream suite passes **1,654 .NET 10 /1,654 .NET 8** cases with zero
failures/skips under the unchanged CI filter (`category!=failing`); all original fixture bytes are
present. Its runtime/test binaries are retained separately from the four `Xsl3.Tests` runs.

A measured-profile native layout control collects the same sixteen 100,000,000-byte streaming
fixtures through an explicitly diagnostic JIT harness, using the exact V3 managed engine and
unchanged saved assemblies. The pinned .NET 10 PGO reader ignores the raw managed EventPipe
sample type; an isolated adapter applies its existing stack/count algorithm to those samples.
The original tool, source patch, all traces and all sixteen profiles are retained. A separate
JSON-exporter repair permits call-weight auditing; original method/instrumentation data remain
identical in the adapter control. These are managed stack samples, not exclusive CPU-time
measurements, and no synthetic weights or reachability restrictions are introduced.

The compiler response verifies the real MIBC input. The native image preserves the Size
preference, all methods, all 53 runtime /six probe sources and the original runtime policies.
All 24 isolated parent/candidate children validate their outputs and actual 4 GiB heap guards.
Elapsed median ratios are identity 0.850229, source-document 0.830887, accumulator 0.896171 and
fork 0.916056. Absolute RSS medians fall only 131,072 /196,608 /266,240 /65,536 bytes, respectively.
Source-document remains 24,555,520 bytes and fork 25,411,584 bytes, above the original 20 MiB
budget. The publishing/profile pipeline is not adopted: it does not resolve the failed memory
gates or the complete throughput requirement. No hosted qualification is dispatched. Evidence:
`artifacts/p7-completion/native-layout-v3-87c40e7/pgo-decision.json`, with raw controls alongside it.

### Current-source throughput and remaining GC-isolation gaps, 2026-10-31

Commit `1525beb` passes all four ordinary hosted CI jobs in run **41198559468**: Ubuntu, Windows,
NativeAOT and ordinary performance. The micro-suite time geometric mean is **1.003** against main;
the 1 MB identity allocation ratio is **0.723**. Those results do not qualify full streaming support.

A fresh local Linux ARM64 cohort runs all seven canonical 100,000,000-byte workloads, three
observations per mode, on .NET **10.0.14** with actual **4,294,967,296-byte** managed guards.
All 42 children validate their original outputs. The unchanged controller's stream/tree elapsed
median ratios are identity **0.973225**, adjacent **0.743690**, accumulator **1.126988**, iterate
**0.817793**, merge **0.640277**, fork **4.122444** and source-document **1.036856**. The target fails
for accumulator, fork and source-document. This is a local 100 MB subset, not a rerun of the entire
126-observation matrix. Frozen binaries and source checks remain under
`call-arguments-87c40e7/v3-jit-seven-100mb`.

The retained managed-memory diagnostic is rerun with streaming binary hashing and launcher RSS
recorded. All eight original-size diagnostic children validate outputs and actual 20 MiB managed
guards. The launcher peaks at 15,667,200 bytes. At 200 MiB input, native peaks are identity
17,215,488, merge 22,855,680, fork 23,031,808 and source-document 22,089,728 bytes. Last-completed-GC
committed snapshots remain about 3.17 /3.30 /3.69 /3.17 MB across small and large inputs. The earlier
uniform 29 MB peak was launcher-contaminated; the corrected observations still exceed three
original RSS budgets. These extra snapshot fields remain absent from production. Raw observations
and the interpretation limits are retained under
`call-arguments-87c40e7/managed-commit-diagnostic/streaming-hash-control`.

An additional compiled-method review finds 25 unit-test classes whose tests can force collections
without the repository's existing nonparallel collection, including helper calls from in-process
streaming probes. This leaves 496 .NET 10 cases able to interfere with unrelated timed tests.
The baseline TRX contains an unconditional GC test wholly inside a timed fork test; it does not
establish a new timeout. The [contract map](p7-runtime-contracts.md#process-wide-collection-isolation)
records the expanded isolation rule. Only collection attributes are changed; test bodies, inputs,
assertions, deadlines, runtime/probe code and original acceptance gates remain unchanged.
All four full configurations pass: **6,927 .NET 10 /5,925 .NET 8** cases in both Release and Debug,
with no failures or skips and the exact previous case populations preserved. Normalized method
bodies are identical; only the 25 intended collection memberships change. All 32 classes with
reachable forced-collection calls are isolated in the compiled metadata. Each run has **zero**
GC-test overlaps with other collections while ordinary tests still overlap. Runtime DLL/PDB bytes
are unchanged from the baseline, frozen test/runtime hashes survive execution, and PDB checks cover
53 runtime /38 test sources. All 1,319 tracked source/test files match the final verification tree.
Evidence: `artifacts/p7-completion/gc-isolation-1525beb/`. Hosted CI run **41204348163** passes all four
jobs on `4c8e203`; its ordinary micro-suite time geometric mean is **1.032**, with **0.723** identity
allocation at 1 MB. This verifies the scheduling checkpoint, not subsequent runtime changes or the
failed original runtime memory/throughput targets.

### Copied accumulator applicability before positional lookup, 2026-10-31

A reduced 32-case check of validated copies finds twelve failures: declared-streamable accumulators
excluded from the source selection become usable in grounded validated copies, while schema-created
text in a forwarded copy can raise `stream-position` before checking applicability. The source has
no corresponding text node, but its tree's accumulator selection is still available.

The runtime now checks copied-tree policy independently of position lookup. Completed and pending
captured copies expose their existing selected names; ordinary provenance follows the original tree.
The existing fully grounded nonstreamable validation policy remains unchanged. An initial unconditional
check changed that policy and failed ordinary `accumulator-073`; it is not adopted. No upstream
fixture, expected XML, baseline or frozen strict inventory changes.

The expanded fixture covers both APIs, normal/debug compilation, actual tree/forward paths, supported
saved loading, strict/lax validation, copied parents and schema-created text, empty/other-accumulator
selection, copies of copies, and the legacy nonstreamable policy. On the original runtime, **96 fail
/156 pass**; the reviewed candidate passes all **252 .NET 10** cases with identical case identities
and unchanged post-test engine hashes. The ordinary accumulator set retains **71/71** outcomes and
its one existing permissive wrong-code observation (`accumulator-038`). The final candidate preserves
the full strict **2,695/16/1**, zero wrong codes, and every frozen forwarding/evaluation requirement.

All four full Release/Debug suites pass **7,179 .NET 10 /6,093 .NET 8** cases per configuration.
The original case populations are preserved alongside 252 /168 new cases, with no failures or skips.
Frozen engine/test hashes remain unchanged after execution; PDB checks cover 53 runtime /39 test
sources, and all 1,320 source/test files match the verification tree. All 32 GC-reachable classes
remain isolated, with zero overlaps with other collections and ordinary parallelism retained.

A warning-free NativeAOT consumer passes **736** checks: the unchanged 568 schema checks plus 168
applicability checks from 84 saved styles, generated from the actual full-tested cases and run on
independent 7/32-row inputs. It verifies exact errors/results, actual forward/tree representations,
body completion and borrowed-reader ownership. Incorrect-code/output controls and a JIT-hosting
guard reject; frozen hashes survive execution, and six consumer /one generator source documents
match their PDBs. The linked runtime equals the full-tested Release engine. The final consumer reads
its reported runtime flag directly; the earlier guard-derived constant report remains retained.

Evidence: `artifacts/p7-completion/copied-applicability-4c8e203/`. Hosted CI remains required for this
runtime change. This resolves denied-accumulator lookup for these copies; it does not assign phase
values to schema-created nodes when the accumulator is applicable, dispose arbitrary mismatched
internal provenance, or close the original failed memory/throughput gates.
