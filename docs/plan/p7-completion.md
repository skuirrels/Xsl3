# P7 completion

**Merge performance update, 6 November 2026:** the draft integration branch now incorporates
[deferred merge source groups](p7-merge-source-groups.md), runtime/compiler `370178a` via `f308ab8`.
The change passes 7,976 affected checks, baseline semantic controls and expanded named-closure
checks. All 60 local and 60 Linux paired observations independently verify; merge improves 4.24%
locally and 3.93% on Linux with fully separated ranges. The Linux stream/tree ratio is 0.987767
for merge in this comparison. Other workload ranges overlap; fork's higher median remains explicit.
Fresh integration CI is required. The prior full original gate remains three-of-seven; this diagnostic
does not replace its 126 observations. Identity, fork and source-document are the next performance
targets. Final-source memory, throughput, compatibility, physical-I/O evidence, supported claims,
review/merge and matching release remain required. Earlier updates below retain their original scope.

**Integrated evidence update, 6 November 2026:** all four jobs in
[CI 42141508372](https://github.com/skuirrels/Xsl3/actions/runs/42141508372) passed on `6c24a92`.
The [current evidence review](p7-integrated-evidence-review.md) independently rechecks strict inventories,
4,968 actual Debug verifications, all 66,643 S1 expressions and reviewed S1/S2 acceptance, and maps
199 passing test methods to the 116 original obligation entries. The new test-only S4-29 contract observes
unchanged physical reader positions across next-match dispatch; all 40 new cases pass in Release/Debug
(257 checks with related Debug coverage). Runtime is unchanged; the new test head still needs hosted CI.

The [current-mode control](https://github.com/skuirrels/Xsl3/blob/feature/p7-current-mode-lowering/docs/plan/p7-current-mode-lowering.md)
at `2401775` and [fork value-event control](https://github.com/skuirrels/Xsl3/blob/feature/p7-fork-value-events/docs/plan/p7-fork-value-events.md)
at `e0ad958` are closed without adoption: their paired timing ranges overlap. Do not repeat or integrate
those controls. Four original throughput targets remain unresolved (identity, merge, fork, source-document).
Final-source original memory/throughput qualification, supported claims, review/merge and release remain required.

**Integrated qualification preparation, 6 November 2026:** `feature/p7-integrated-qualification`
combines the public-selection head `5645098` with corrected-discard controller `18fc9a5`
(runtime `a8d322a`, 256-event default). It includes merged PRs #34–38 and excludes all rejected
runtime controls. Hosted CI passed at `6c24a92`; original final-source qualification remains incomplete.
See [the integration record](p7-integrated-qualification.md). Full P7 remains active; no release claim.

The isolated short-polling control is closed after its 100 MB and 1 GB Linux comparisons; overlapping
fork ranges and other-workload regressions do not justify adoption. The tail-call control is also closed
without useful timing benefit. Lazy frame headers completed all 60 Linux observations in
[42136785894](https://github.com/skuirrels/Xsl3/actions/runs/42136785894), with verified binaries,
409 tracked PDB source hashes per engine and original outputs, but all timing ranges overlap.
It is closed without adoption. Their branches retain the source, audit records and decisions.
The earlier descriptions below are dated investigation history, not current approval to merge them.


**5 November hidden-grounding follow-up:** [S3-27 contracts](p7-hidden-grounding-contracts.md)
now demonstrate a measured budget failure for a real grounding helper inside an otherwise forward
execution. Correct output, actual forward entry and the Debug graph verifier all pass that negative
control, so none is mistaken for a memory proof. All 32 new local cases and 30 related Debug checks
pass. All four CI jobs passed at `ac183fd` in run `42115246979`; the reconciliation with merged PR #37
requires CI on its new head before merging. Runtime and benchmark source are unchanged from merged `a5e89ac`.


**Full objective resumed, 5 November 2026:** the owner instructed “continue with phase 7 until it's complete”.
All original requirements and gates remain in force; the earlier stopping instructions below are historical.
Accounting and extension review shipped in 1.2.0/1.3.0; [PR #33](https://github.com/skuirrels/Xsl3/pull/33)
merged entry and forward-execution evidence at `8582a5b`. PRs #34–36 subsequently merged
[S3-25 verifier coverage](p7-verifier-coverage.md), [S5-14 read positions](p7-group-read-positions.md)
and [S3-04 managed-byte evidence](p7-retained-memory-contracts.md), through main `a5e89ac`.
[S6-11/12 fork grouping/sorting payload contracts](p7-fork-group-retention.md) now pass 260 local
Release/Debug checks and all four hosted CI jobs; PR #37 merged at `f453216`. Internal-grounding
contract integration, original throughput and
memory qualification, final integration and supported-streaming delivery remain required. No intermediate audit or release completes P7.

The owner resumed the full P7 objective on 2026-10-29: complete Phase 7, review all changes,
merge the pull request and release. Branch: `feature/p7-completion`. Baseline: released
1.1.6, `588f2b6c7f050d47087f67b1fd2ef9630f6e19cf`.

On 1 November 2026 the owner requested a stopping point and release of the completed work.
The [1.1.7 checkpoint boundary](p7-runtime-checkpoint.md) supersedes the earlier full-gate/deployment-only
stopping instruction for this release. Full P7 remains incomplete; every original requirement below is
retained for resumption. The checkpoint keeps experimental execution opt-in and excludes unadopted controls.

The remaining full objective follows the original [P7 plan](p7-streaming.md); the completed P7A–P7H
increments and their historical evidence are retained. An audit or another bounded increment does not
complete full P7.

## Verified 1.1.7 checkpoint state, 1 November 2026

**Phase A, from 1 November 2026:** baseline of 1.1.7 and S3–S6 acceptance accounting. The current
stage exits, obligation map, failure dispositions and owner decisions are in [phase A](p7-phase-a.md).

**1.1.7 is released and further P7 work is paused at the owner's request.**
[PR #27](https://github.com/skuirrels/Xsl3/pull/27) merged at
`b0bf3bb3464e319048c230191577f86c326b5cdb`, the commit tagged `v1.1.7`.
All four [reviewed-source CI jobs](https://github.com/skuirrels/Xsl3/actions/runs/41348273376)
passed. [Post-merge CI](https://github.com/skuirrels/Xsl3/actions/runs/41351971235) passed Linux,
Windows and NativeAOT; ordinary performance was skipped by its push condition after passing on the PR.
The [release build and NuGet publish](https://github.com/skuirrels/Xsl3/actions/runs/41352001262)
passed, all three public NuGet payloads matched the tagged artifacts, and the
[GitHub release](https://github.com/skuirrels/Xsl3/releases/tag/v1.1.7) is public with five assets.
Fresh public-package managed/CLI/native consumers and all 576 saved-style contracts passed.

Use the [handoff](p7-handoff.md) for resumption and evidence recovery. The ignored local `artifacts/`
directory disappeared during release verification. Historical paths below are provenance references,
not a guarantee those files still exist. Hosted evidence may also expire. Do not treat historical
measurements as qualification of the release tree or adopt an isolated candidate without fresh review.

## Historical pre-release state and experiments

This section and the detailed development entries below retain their original checkpoint statements.
Their "current", "pending", "active" and "not published" descriptions apply to the named historical
source, not to the released 1.1.7 checkpoint. The current handoff above takes precedence.

Retained head `42e5a73` passes all four ordinary CI jobs in
[run 41311886937](https://github.com/skuirrels/Xsl3/actions/runs/41311886937), including the repaired
Ubuntu W3C profiles. This is independent of streaming qualification. Isolated startup composition
`c7da793` is not adopted: its complete hosted original
run passes **13/16 push budgets**, **16/16 nightly budgets**, and **4/7 throughput targets** with
all **126 benchmark observations** valid. Merge/fork/source-document RSS and identity/fork/source-document
throughput remain failed. Their original inputs, limits, oracles and failed observations are retained.

A newer isolated combined candidate, `b2316fd` on `feature/p7-composed-compact`, completes all
**27,200** full Release/Debug .NET 8/10 checks and the unchanged **739-check** NativeAOT consumer.
Its original local push matrix passes 16/16, but its independently audited hosted x64 push matrix
still passes **13/16**. It is not adopted into PR #27; its full original throughput/nightly
qualification is incomplete. The detailed results and remaining controls are retained below.

The canonical schema-relation and direct native reporter controls are now fully audited and excluded
because neither produces a consistent useful RSS reduction. The public Strict/Fallback default
follow-up now passes all four full unit suites (**7,339 .NET 10 /6,229 .NET 8** per configuration)
and the unchanged **739-check** NativeAOT consumer. Every previous row, all 456 saved inputs and eight
consumer sources remain. Four regressions fail on unchanged source; all twelve new cases pass after
the fix. The source and matching README/package-readme/migration/changelog updates are pushed at `468ea7f`.
No merge, package readiness or publication is claimed.

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
| Review / merge | Full diff reviewed, defects corrected, required hosted CI green for the reviewed head, merge verified | Complete for 1.1.7 checkpoint; full-P7 review remains future work |
| Release handoff | README/package readmes/changelog/release notes updated; merged main and matching packages/publication verified | 1.1.7 published under the owner's later release instruction; full-P7 release remains deferred |

Memory gates use unmodified generated inputs, independent output oracles and full-lifetime counters.
No startup subtraction, disabled globalization, diagnostic zero-limit run, omitted workload, relaxed
budget, incorrect expected output or fixture-dependent runtime branch can satisfy acceptance.

## Current candidate qualification, 2026-11-01

Commit `722ae27` adds the reviewed reference-counted fork projection, immutable-header range claims
and equivalent ASCII JSON reporting described below. All four full local suites pass **7,318 .NET 10
/6,208 .NET 8** checks per configuration; the unchanged native consumer passes **739** checks, and
fresh projected/fallback native fixtures pass **288** checks over 72 saved styles and 1/2/4/6 CPUs.

The [ordinary CI run](https://github.com/skuirrels/Xsl3/actions/runs/41270174470) passes Ubuntu,
ordinary performance and NativeAOT. Windows retains one .NET 10 unit failure: the existing
`FinishingAProngEarlyDoesNotTruncateTheOtherProngs` reaches its unchanged 10-second cancellation
limit. Its other 7,317 .NET 10 checks and all 6,208 .NET 8 checks pass. The log confirms both framework
suites start concurrently. A test-only resource control serializes target frameworks and moves the
fork scheduling class into the existing nonparallel collection; all cases, assertions and deadlines
remain unchanged. The complete CI unit invocation now passes 7,318 /6,208 cases locally; binary
hashes and every case-row multiplicity are unchanged, and .NET 8 starts after .NET 10 completes.
The exact-head follow-up CI passes Windows, Ubuntu, NativeAOT and ordinary performance in
[run 41273080960](https://github.com/skuirrels/Xsl3/actions/runs/41273080960). This verifies the test
scheduling repair on hosted Windows; a runtime deadlock is not established by the retained failure.

| Original hosted gate | Verified `722ae27` result |
|---|---|
| Reviewed S1/S2 | Release/Debug 1,000 /155 cases pass; all 66,643 original expressions preserved |
| Strict runtime | Release, Debug and legacy/saved each retain 2,695 pass /16 fail /1 not run; complete inventory, forwarding and exact-code audits pass |
| Push memory | 48 correct outputs; 13/16 budgets. Merge 22,093,824, fork 22,597,632 and source-document 21,422,080 bytes exceed 20,971,520 |
| Nightly memory | 48 correct outputs; 16/16 budgets. Exact 2,147,483,648-byte inputs and 209,715,200-byte guards; maximum whole-process peak 37,154,816 bytes |
| Complete throughput | All 126 observations valid at original sizes and actual 4 GiB guards; six of seven 100 MB targets pass. Fork remains 1.439458 stream/tree |

The hosted stream/tree ratios are identity **0.978893**, adjacent **0.657111**, accumulator
**0.655624**, iterate **0.619349**, merge **0.973719**, fork **1.439458** and source-document
**0.976564**. The host reports .NET 10.0.14, Ubuntu 24.04.5, x64 and four effective processors.
All benchmark raw records and preceding output fingerprints, binary hashes, controller hash,
58 runtime PDB sources and eight benchmark sources validate. The display band waives no target;
separate hosted cohorts do not establish a controlled causal speedup.

Both memory archives match GitHub's published digests. Every raw output matches its report and the
preceding canonical fingerprints; all 58 audited runtime sources and 33 stylesheet/entrypoint sources
match. The strict artifact preserves every case outcome and fixture definition; local independent
inventory/forwarding/evaluation audits pass. CI also checks the actual W3C engine binary; that engine
is not retained in its artifact for a second local binary-hash comparison. Evidence:
`artifacts/p7-completion/qualification-722ae27/`.

The selected candidate's complete local 100 MB subset has **42 valid observations** and five of seven
targets met. Fork **1.113081** and source-document **1.027426** stream/tree medians still fail. These
Linux ARM64 diagnostics are separate from hosted x64 acceptance. External native mapping snapshots
are instantaneous observations, not a decomposition of historical peaks. All nine mapping controls
retain correct outputs and actual 20 MiB managed guards; diagnostic RSS guards remain disabled.

A distinct, isolated value-collection control builds on the immutable range protocol. It holds every
claimed event count until collection finishes, including on failure, cancellation and concurrent
disposal, and preserves the earlier observer/replay/cursor guards. All **3,917** focused checks pass:
3,910 original case rows plus seven new collection contracts, including repeated display identities.
Its first preparation omits two safety helpers and fails compilation before testing; that attempt
is retained. After the complete unit invocation ends, all six source-verified four-CPU timing
children validate output. The median falls only 1.19%, with overlapping ranges, so the control is
rejected. No native or full-suite qualification is claimed (`value-ranges-722ae27`).

The test-only concurrency control is pushed as `f63305b`. Its new ordinary CI run
[41273080960](https://github.com/skuirrels/Xsl3/actions/runs/41273080960) passes both complete
platform unit-test steps, including the retained Windows failure case. All four jobs now finish
successfully. Runtime C# sources are unchanged from `722ae27`.

An isolated diagnostics branch (`feature/p7-hosted-cpu-diagnosis`, `7f30ccf`) adds explicitly selected
hosted CPU sampling; default/scheduled original acceptance remains unchanged. Its
[diagnostic run](https://github.com/skuirrels/Xsl3/actions/runs/41274407610) profiles the exact
`f63305b` source on the same .NET 10.0.14 /Ubuntu x64 /four-processor allowance. Both 100 MB outputs
match the canonical benchmark with actual 4 GiB guards. All binary hashes and 58 runtime PDB source
checks pass. Native and ReadyToRun caller symbolication is incomplete. Matching the official runtime
module's build ID to the perf data and translating its ELF file/virtual offset identifies the hottest
native instruction as a `pause; dec; jne` spin loop: **482 of 2,762** user-mode on-CPU samples,
about **17.45%** of the complete profiled child's sampled user CPU. This includes startup, warmup and
oracle work; it is not an exact wait-owner or causal elapsed-time attribution. Raw maps, samples,
matching runtime module, build-ID proof and unresolved frames are retained under
`artifacts/p7-completion/hosted-cpu-f63305b/`.

An isolated full-ring parking control (`38d876c`) retains all **455** focused fork/tee checks,
including two new notification/parking race contracts. Six local ARM64 observations show overlapping
ranges and a 2.24% slowdown. A separate unprofiled
[hosted x64 comparison](https://github.com/skuirrels/Xsl3/actions/runs/41276056450) retains twelve
valid 100 MB observations: three per variant/mode, with actual 4 GiB guards. Fork streaming medians
are baseline **3,976.285 ms** /candidate **4,758.173 ms**, a **19.66% regression**. All original
fingerprints, source manifests, binary hashes and 58 runtime PDB sources per variant validate.
The control is rejected and never enters the main P7 branch. The first dispatch supplies an abbreviated
candidate ref and fails checkout before measuring; the replacement uses its full immutable SHA.
Evidence: `artifacts/p7-completion/hosted-waits-38d876c/` and `backpressure-waits-f63305b/`.

A separate bounded frame-pruning control (`d2ce7ee`) reads every omitted XML token but avoids
allocating descendant navigator frames when the compiler proves that their content is unused.
Typed, observed, replayed and consumption-hook inputs retain the ordinary path. Review catches two
continuation failures after early completion and one late-header-hook registration failure; all
three have retained negative controls. The corrected prototype passes **497 .NET 10 /458 .NET 8**
focused cases in both configurations, preserving 488 /449 original rows and adding nine semantic
contracts. The unchanged baseline passes all nine new contracts.

Six local ARM64 JIT controls show a **12.69%** elapsed median reduction. Twenty-four native
200 MiB observations preserve every output and actual 20 MiB managed guard; fork's four-CPU median
falls **11.87%**, while its whole-process RSS median rises **139,264 bytes** and stays above budget.
Six additional one-CPU fork controls have an elapsed ratio of **0.998230** and a higher RSS median.
These diagnostic zero-RSS-limit observations cannot pass an original memory gate. A source inventory
audit overlaps initial native identity controls; no causal timing improvement is attributed to identity.

The decisive [hosted x64 comparison](https://github.com/skuirrels/Xsl3/actions/runs/41277260040)
retains twelve valid 100 MB observations with matching original outputs and actual 4 GiB guards.
Baseline/candidate fork streaming medians are **5,226.458 /5,153.130 ms**: only **1.40%** lower,
with overlapping ranges. The candidate still misses the original target at **1.482354** stream/tree.
Source manifests, both binary sets and 58 runtime PDB sources per variant verify. This complexity is
rejected and its runtime code remains outside the main P7 branch. Preparation, initial defects,
corrected sources and all controls remain under `pruned-frames-f63305b`, `pruned-leaves-f63305b`,
`pruned-cleanup-f63305b` and `hosted-pruning-d2ce7ee`.

A separate direct standard-stream report control keeps the UTF-8 JSON bytes and platform newline,
with 48 focused report/probe checks and all 24 native output/heap checks passing. Seven native
consumer sources and all 58 unchanged runtime PDB sources verify. Its first managed build omits a
`System.Console` qualification and fails before testing; that preparation failure is retained. The
corrected native comparison shows no consistent RSS reduction: identity is unchanged, merge and
source-document each rise **131,072 bytes**, and fork falls only **118,784 bytes** while staying above
budget. The transport control is rejected. No Console/serializer change enters the main branch.
Evidence: `artifacts/p7-completion/report-stream-f63305b/`.

A diagnostic-only batch histogram then checks the small-refill hypothesis before changing the
producer. The exact 100 MB fork output and actual 4 GiB guard validate, with all 58 runtime PDB
sources checked. The measured transformation publishes **4,347,828** events in **195,096** batches;
mean reserved scan space is **53.49 /64**. Only **160** batches reserve one slot, and only **261**
reserve four or fewer. Small refill fragmentation is not established as the dominant cost, so no
refill-delay policy is introduced. These instrumented counts make no timing/RSS acceptance claim.
Evidence: `artifacts/p7-completion/fork-batch-shapes-f63305b/`.

The nine independent fork continuation/observation contracts are retained in
`tests/Xsl3.Tests/ForkContinuationContractsTests.cs` on the unchanged runtime. Their final source
passes all **nine** checks in Release/Debug on .NET 8 and .NET 10, with bounded cancellation and the
existing nonparallel process-state collection. No rejected runtime, parser or report control is
adopted. Exact contract/documentation head `9a4af5d` passes all four ordinary hosted CI jobs in
[run 41278354134](https://github.com/skuirrels/Xsl3/actions/runs/41278354134).

The [hosted native mapping diagnostic](https://github.com/skuirrels/Xsl3/actions/runs/41278605367)
completes twelve correct 200 MiB observations using the unchanged `f63305b` runtime, real NativeAOT
hosting and actual 20 MiB managed guards. All original fingerprints, 48 snapshot hashes, both
source/host records, 58 runtime PDB sources and 33 stylesheet/entrypoint sources validate. The
artifact lacks the native consumer portable PDB for a second independent consumer checksum audit.
The first diagnostic stops at setup because this runner has no cgroup-v2 `cpu.max`; the replacement
records that absence and .NET's actual x64/four-processor allowance instead of inventing a quota.

Late instantaneous snapshots show about **10.5–11.2 MiB** in native executable mappings and
**3.1–3.7 MiB** in anonymous mappings. URI workloads additionally show about **4.34 MiB** in ICU,
C++ and unwind-library mappings. These are not a decomposition of historical peaks. The diagnostic
launcher is below the original RSS threshold; it sets an inherited floor for the identity counters.
No peak is subtracted or adjusted. All three original memory gaps remain unqualified. Evidence:
`artifacts/p7-completion/hosted-native-mappings-f63305b/`.

A narrowly lazy DTD-metadata control moves nine private reflection bindings behind the null-DTD
check without changing their member preservation. Existing focused contracts pass **165** checks.
A deliberate metadata-initializer failure still permits the no-DTD identity path and fails the real
DTD binding test, proving that the cache is actually deferred. All 24 native 200 MiB observations
preserve outputs and actual 20 MiB guards; 59 runtime PDB sources include the newly changed bridge.
There is no useful RSS reduction: identity/source-document medians are unchanged, merge rises
4,096 bytes and fork rises 24,576 bytes. The standalone lazy-cache control is rejected. Evidence:
`artifacts/p7-completion/lazy-dtd-metadata-9a4af5d/`.

A separate .NET 10 reference-accessor control (`6cf57db`) replaces three private reader reflection
lookups and defers DTD bindings until a DTD exists; .NET 8 keeps its reflection path. It preserves
private enum bridges, resolvers, whitespace policy, trimming roots and the existing public API.
Eight additional native DTD assertions cover absent, parsed and ATTLIST-only declarations alongside
the original nine reader assertions. All **17** native checks and the real JIT guard pass.

The candidate passes all four complete unit suites: **7,328 .NET 10 /6,218 .NET 8** cases in each
Release/Debug configuration, with every preceding case-row multiplicity retained and exactly ten
new rows per configuration. Frozen test-engine hashes remain unchanged. The saved native consumer
passes **739** checks with all **456** input files and eight consumer sources unchanged, real
NativeAOT execution, rejected wrong-code/wrong-output/JIT controls, and **59 runtime /eight consumer**
PDB source checks. Evidence: `reference-reader-accessors-9a4af5d/` and `reference-reader-full-6cf57db/`.

The local ARM64 original push matrix passes all **16 budgets /48 outputs**, but this does not qualify
hosted x64. The candidate's [original hosted run](https://github.com/skuirrels/Xsl3/actions/runs/41282119610)
still passes only **13/16** push budgets, preserving all 48 canonical outputs and original sizes/guards.
Merge **22,048,768**, fork **22,155,264** and source-document **21,340,160 bytes** exceed **20,971,520**.
All 59 runtime PDB sources validate. The same hosted run's original nightly matrix passes all
**16 budgets /48 outputs**, at exact 2,147,483,648-byte inputs and actual 209,715,200-byte guards;
maximum whole-process RSS is **36,900,864 bytes**. All 126 throughput observations preserve their
canonical outputs, original sizes and actual 4 GiB guards. Four of seven targets pass: ratios are
identity **1.058915**, adjacent **0.760057**, accumulator **0.947313**, iterate **0.851638**, merge
**0.888849**, fork **1.249870** and source-document **1.103680**. Runtime/benchmark hashes,
controller hash and **59 runtime /eight benchmark** PDB sources validate. Separate hosted cohorts
are not a causal before/after comparison. No full-P7 pass or release readiness follows; this runtime
control is still isolated.
Evidence: `artifacts/p7-completion/qualification-6cf57db/`.

A separate SDK-version-only control leaves the saved loader and reader bridges unchanged. It
uses the actual SDK assembly version only when the SDK generates a canonical four-part assembly
header; short, padded, wildcard and manual-header builds keep the existing authoritative fallback.
All **18** original build/header matrix cases and **64** focused saved-version/probe checks pass.
All **24** NativeAOT observations preserve correct outputs, original 200 MiB inputs, actual 20 MiB
heap guards and 58 runtime PDB source checks. Median RSS deltas are identity **0**, merge **−53,248**,
fork **+4,096** and source-document **+192,512 bytes**. This isolated control is rejected for lack of
a consistent footprint or elapsed improvement. No generated-version target is adopted, and the
older rejected loader/field experiment is not revived. Evidence: `sdk-version-only-9a4af5d/`.

An isolated ring-index control (`bea6c23`) replaces division only when the configured ring capacity
is a power of two; arbitrary capacities keep the original remainder operation. Capacity, event
ownership, publication, cancellation and scheduling remain unchanged. All four focused case
multisets are preserved: **370 .NET 10 /335 .NET 8** checks per configuration. Six local 100 MB JIT
observations show a **3.26%** lower fork median; 24 native 200 MiB controls retain correct outputs
and actual 20 MiB guards, with no original budget qualification inferred.

The [hosted x64 paired comparison](https://github.com/skuirrels/Xsl3/actions/runs/41283847049)
retains all twelve valid original 100 MB outputs and actual 4 GiB guards. Fork streaming medians
are **3,290.198 /2,976.823 ms**, a **9.52%** reduction. Candidate stream/tree is still **1.469389**,
so the original target fails. Both 492-file source manifests, binary hashes, **58 runtime /eight
benchmark** PDB sources per variant and all original fingerprints verify. The reviewed index change is retained on the P7 branch after this controlled hosted gain;
complete final-source compatibility and original qualification remain required. Evidence: `tee-slot-index-9a4af5d/` and `hosted-slot-index-bea6c23/`.

An opt-in [hosted native CPU diagnostic](https://github.com/skuirrels/Xsl3/actions/runs/41284157433)
profiles the unchanged `9a4af5d` runtime with matching executable/debug-symbol build IDs. Both
100 MB NativeAOT outputs, actual 4 GiB guards, native/managed binary hashes, **58 runtime /eight
consumer** PDB sources and original fingerprints validate. All nine controller contracts pass;
original jobs are equivalent when the new option is disabled. The first local YAML check lacks
PyYAML; standard Ruby YAML verifies the complete old-job equivalence instead.

Of **1,133** whole-child user CPU samples, **418 /36.89%** resolve to the native spin loop. Recorded
callers attribute **26.39%** to reader publication waits and **10.06%** to caller-pump waits; **0.44%**
remains unresolved. These are CPU shares including startup/warmup/oracle work, not removable elapsed
time or acceptance results. They support one isolated short-poll control that retains the same
1 ms ceiling, spare-CPU guard, parking fallback, threads and ownership. The earlier rejected
no-yield control resets the BCL's exponential backoff; the new control tests checking state between
short processor waits instead. The [short-poll comparison](https://github.com/skuirrels/Xsl3/actions/runs/41284872315) now completes
with twelve valid original 100 MB outputs, actual 4 GiB guards, both 492-file source manifests,
binary hashes and **58 runtime /eight benchmark** PDB sources per variant verified. Hosted fork
streaming medians are **5,944.407 /5,510.460 ms**, a **7.30%** reduction; candidate stream/tree
**1.639543** still fails. Four local focused suites retain **370 /335** checks; six local JIT and
24 native observations preserve outputs. Native fork median RSS increases **135,168 bytes**.
This policy stays isolated. The first hosted audit correctly stops because the main checkout has
advanced; an exact frozen baseline archive repairs the source audit without repeating measurements.
Evidence: `native-cpu-diagnosis-4ba7d3b/`, `short-poll-9a4af5d/` and `hosted-short-poll-81e2abd/`.

Retained ring-index head `e223b2f` passes all four complete frozen unit suites: **7,327 .NET 10
/6,217 .NET 8** in each configuration, preserving every earlier case row and all test-engine hashes.
The unchanged saved native consumer passes **739** checks, all **456** saved inputs and eight consumer
sources remain unchanged, all deliberate code/output/JIT failures reject, and **58 runtime /eight
consumer** PDB sources match. All four exact-head ordinary CI jobs pass in
[run 41284725062](https://github.com/skuirrels/Xsl3/actions/runs/41284725062). Original acceptance
remains separate. Evidence: `artifacts/p7-completion/slot-index-full-e223b2f/`.

A writer-default-state cache control remains excluded. Its independent contract compares real BCL
enum fields, public/reset/clone/read-only behavior, and eighteen saved-header round trips. An initial
literal `standalone=no` output assertion also fails on the unchanged runtime; it is repaired to derive
output from a separately populated stock writer. Both failed runs and the sandbox-denied test-listener
attempt remain retained. Production code does not change to satisfy that mistaken expectation.

The corrected control retains every earlier selected case and passes **102 .NET 10 /98 .NET 8** checks
per Release/Debug configuration. Real NativeAOT passes the original nine reader and **42 writer**
assertions; its managed intermediate rejects. All **24** native 200 MiB observations retain correct
outputs and actual 20 MiB heap guards. Both **65-source** runtime PDB audits and all **65 saved input**
hashes verify. Median whole-process RSS deltas are identity **0**, merge **+81,920**, fork **−28,672**
and source-document **+212,992 bytes**. The cache is rejected. A smaller control now isolates the
public setter for definite mutable indent states and separate lazy reflection bindings; its unknown,
invalid and read-only states retain the original bridge. No cache or clone helper enters this control.
The smaller control also passes all four **102 /98** focused suites, **nine reader /42 writer**
NativeAOT assertions and the real JIT guard. Its 24 native observations retain every original
fingerprint, source/binary hash and saved input. Both **59-source** runtime PDB audits validate.
RSS deltas are identity **0**, merge **+4,096**, fork **+331,776** and source-document **+8,192 bytes**.
It is also rejected. Neither writer optimization nor its additional runtime cache/clone machinery
enters the branch. Evidence: `writer-defaults-e223b2f/` and `writer-public-indent-e223b2f/`.

A distinct typed saved-provider control keeps the existing Load(Type) entry point and every legacy
loader/data-format path, but emits direct execution/dispatcher delegates for newly compiled styles.
It adds no warning suppressions or publisher changes. This API/emitter change stays in a frozen
prototype. All **395 .NET 10 /49 .NET 8** saved-focused checks pass per configuration, including two
new .NET 10 version/data/function-dispatch round trips. The first population auditor mistakenly
treats parameter labels as method identities; the TRX method definitions repair that audit without
repeating passed tests or changing their filter.

The unchanged NativeAOT publisher succeeds without warnings. All **33 original stylesheet/entry
sources** match; new saved DLL hashes are recorded honestly. All 24 native 200 MiB observations
retain original fingerprints, actual 20 MiB heap guards and unchanged source/binary/saved-input
hashes. Median RSS deltas are identity **0**, merge **+94,208**, fork **−221,184** and source-document
**+356,352 bytes**. The provider is rejected alone; no API or emitter change enters the P7 branch.
Evidence: artifacts/p7-completion/typed-saved-provider-e223b2f/.

One separate composition now tests a shared fixed-overhead hypothesis: the SDK-sourced canonical
version, typed saved provider, reader reference accessors/lazy DTD metadata and default-only writer
cache may need to be cold together. Its four focused configurations pass **441 /95** checks. A
separate guarded native build must reject actual writer/DTD/legacy fallback accesses and permit
default URI workloads before any uninstrumented RSS result can support that mechanism. Each earlier individual control and its failures remains retained.

All six mechanism checks pass: the three default URI workloads run while the guarded writer, DTD,
version and legacy-loader bindings are forbidden; real writer/DTD/legacy fallbacks fail with their
individual markers. Native reader/writer/DTD assertions pass **59** checks and the actual JIT
intermediate rejects. Twenty-four uninstrumented observations retain correct outputs. Median RSS
reductions are merge **393,216**, fork **966,656** and source-document **262,144 bytes**; identity is
unchanged. The same native image passes the complete local original push matrix: **16 budgets /48
outputs**, exact 209,715,200-byte inputs, actual 20,971,520-byte RSS/heap limits, original 10 MiB
controls and maximum RSS **20,602,880 bytes**. All fingerprints, source/saved-input hashes and
**69 runtime** PDB checks verify. This is ARM64 diagnostic evidence.

The isolated composition is pushed as **c7da793** on feature/p7-composed-startup; PR #27's runtime
stays at e223b2f. The first commit preparation stops on quoted non-ASCII Git filenames; a NUL-delimited
inventory repairs it before any branch is created. The exact clean commit passes all **18** build/header
version cases; full Release/Debug and unchanged saved/native compatibility are still running.

Original hosted run **41290706344** retains all 48 push outputs, all 33 stylesheet/entry sources
and 69 runtime PDB checks, but only **13/16 budgets** pass. Merge **21,684,224**, fork **21,950,464**
and source-document **21,061,632 bytes** exceed **20,971,520**. Source-document's own status is ok,
but its external full-lifetime counter exceeds the cap; that remains a failure. Nightly and all
126 throughput observations remain in progress. No composition acceptance, merge or release is
claimed. Evidence: artifacts/p7-completion/composed-cold-start-e223b2f/,
artifacts/p7-completion/composed-full-c7da793/ and artifacts/p7-completion/qualification-c7da793/.

Full P7 remains active. Original failed memory/throughput targets, remaining contract accounting,
public integration, final review/CI, merge and deployment handoff remain required. PR #27 stays draft,
`supports-streaming` remains `no`, and the owner requires stopping before tagging/publication.

### Composed startup compatibility and hosted mapping audit, 2026-11-01

Exact clean candidate `c7da793` completes all four full suites with **7,331 .NET 10 /6,219 .NET 8**
checks in each Release/Debug configuration. Every prior baseline case row and multiplicity is retained,
all frozen binary hashes remain unchanged, and no case fails or is skipped. The unchanged native
consumer passes **739** checks; deliberate incorrect-code/output and real JIT-intermediate controls
reject. All **456 saved inputs /eight consumer sources** are unchanged, the linked engine matches the
full-tested Release engine, and **69 runtime /eight consumer** portable PDB checks pass without
publication warnings. All 18 build/header compatibility cases also pass. Evidence:
`artifacts/p7-completion/composed-full-c7da793/`.

[Hosted native mappings](https://github.com/skuirrels/Xsl3/actions/runs/41291350142) complete with
12 correct original outputs, actual 20 MiB heap guards, 48 verified snapshot hashes, 69 matching runtime
PDB sources and 33 unchanged stylesheet/entry sources. The archive matches GitHub's SHA-256 digest.
The canonical consumer portable PDB is not retained in this mapping artifact. These external snapshots
use a disabled RSS status limit and cannot qualify a budget. They show current executable mappings
around 10.4–11.1 MiB, anonymous mappings around 3.1–3.3 MiB, and about 4.34 MiB of ICU/C++/unwind
libraries for URI workloads. They are instantaneous snapshots from a separate cohort, not historical
peak decomposition or a paired causal reduction. Evidence:
`artifacts/p7-completion/hosted-native-mappings-c7da793/`.

A separate isolated control composes the retained ring-index optimization and candidate cold startup
with the earlier short-poll policy. It preserves the one-millisecond ceiling, spare-CPU guard, lower-CPU
parking, threads, capacities, input/output ownership, inputs, oracles and every original gate.
Individual gains do not establish its composed performance. It remains unadopted pending validation.
Evidence: `artifacts/p7-completion/composed-short-poll-c7da793/`.

### Original nightly completion and isolated controls, 2026-11-01

The composition's original hosted nightly job in run **41290706344** passes **16/16 budgets /48
outputs**, using exact **2,147,483,648-byte** inputs, original 10 MiB controls and actual **209,715,200-byte**
RSS/managed limits. Maximum whole-process RSS is **36,270,080 bytes**. All original output fingerprints,
33 source stylesheet/entry files and 69 runtime PDB checks match. The artifact matches GitHub's published
digest. The original push failures remain; the full 126-observation benchmark is still active.
Evidence: `artifacts/p7-completion/qualification-c7da793/hosted-nightly-audit.json`.

The composed short-poll control completes all four **370 /335** focused suites, six 100 MB JIT
observations and 24 native 200 MiB observations. Every output matches the original oracle; runtime
PDB checks match 69 source documents per variant. Fork elapsed candidate/baseline ratios are
**0.972531 JIT /0.998846 native**, with overlapping ranges. The isolated earlier gain does not reproduce
materially in the composed native path. This policy remains excluded; no hosted acceptance is claimed.
Evidence: `artifacts/p7-completion/composed-short-poll-c7da793/`.

An independent plain-text principal-writer control retains every selected previous case, adds six
stock-writer comparisons covering both APIs, byte/text destinations, empty/mixed content, encoding
failures and lent-output ownership, and passes **184 .NET 10 /173 .NET 8** cases per configuration.
The first preparation guarded against an outdated clone spelling and stops before a source write.
A test-only nonexistent debug option is corrected; a second stock oracle correctly rejects non-ASCII
text under ASCII and is updated to compare actual failures rather than assuming representability.
All failed attempts remain retained. Six JIT and 24 native observations preserve the original outputs;
all 69 runtime PDB sources match per variant. Native merge median RSS falls only **61,440 bytes** and
its elapsed median is **0.17% slower**, so the runtime change remains excluded.
Evidence: `artifacts/p7-completion/direct-text-c7da793/`.

A distinct saved-compiler control moves ordinary generated bodies into a private nested implementation
type. Load(Type)'s broad nonpublic-method preservation can then retain the existing outer loader entry
points without reflection metadata/invokers for every template body. Execute, the function dispatcher,
static fields, format, legacy loaders, in-memory compilation and publisher settings remain unchanged.
All four selected saved/debug/serialization suites pass **941 /578** cases. The inventory auditor's
initial case-sensitive comparison misses one already-passed Recompiled-named method; matching VSTest's
case-insensitive semantics repairs the audit without rerunning suites. Native publication succeeds
without warnings, all 33 original source styles/entrypoint match, and 69 source PDB checks per variant
pass. All 24 original-input native controls complete with original fingerprints, actual 20 MiB heap
guards, unchanged sources/saved inputs and matching native binary hashes. The image shrinks only
**13,056 bytes**. Median RSS deltas are identity **0**, merge **+61,440**, fork **−98,304** and
source-document **−8,192 bytes**; fork elapsed is effectively unchanged (**1.000551**). This does not
establish a useful consistent footprint reduction, so the implementation remains excluded. All local
controllers are terminal; the original hosted full benchmark remains active. No additional compiler,
writer, polling or public API change enters the main P7 branch from these controls.
Evidence: `artifacts/p7-completion/saved-implementation-c7da793/`.

### Completed composed qualification and private-reader control, 2026-11-01

The original [c7da793 qualification](https://github.com/skuirrels/Xsl3/actions/runs/41290706344)
is terminal. All **126 original-size observations** validate, including 1 GB tree controls under the
actual **4 GiB** heap guard. All preceding fingerprints, native/managed host flags, controller/binary
hashes, **69 runtime /eight benchmark** PDB sources and the artifact digest verify. Four of seven
100 MB throughput targets pass. Original stream/tree ratios are identity **1.069088**, adjacent
**0.791757**, accumulator **0.899722**, iterate **0.826535**, merge **0.868607**, fork **1.608402** and
source-document **1.062780**. Every failing target stays failed. Together with the preceding audited
13/16 push and 16/16 nightly results, this leaves the startup composition unadopted; local ARM64
passes cannot substitute for the original hosted x64 gates. Evidence:
`artifacts/p7-completion/qualification-c7da793/hosted-benchmark-audit.json`.

A separate private-reader control preserves the original generic alias-capable tee. Only the internal
fork-event adapter selects a confined cursor, bound to its first consuming thread. Acquire/release
cursor publication then lets the producer reclaim completed prefixes without per-event compare/exchange
and reader-count updates. Cross-thread consuming aliases reject; external detach remains supported.
Ring/staging capacity, prong/caller threads, source/callback ownership, waits and all original gates stay.

All original selected fork contracts pass first, but a new progress contract exposes a missed caller
wakeup when one slot is released and the slow reader waits before requesting more. The repaired
protocol signals a pending caller on the head release. All four configurations preserve their original
370 /335 rows and pass five additional ownership/progress/source-fault contracts: **375 .NET 10
/340 .NET 8** checks per configuration. The first selected-row auditor omits the new class from its
filter; the passed 370-row Release run is reused, its five omitted cases are run separately against
the unchanged runtime, and remaining configurations include them directly. All failures are retained.
All six JIT and 24 native controls complete and match the original fingerprints, actual guards and
host flags. Both native binaries, 69 runtime and eight consumer PDB checks per variant match the
frozen inputs. Fork candidate/baseline elapsed medians are **0.952540 JIT /0.939103 native**; native
median full-lifetime RSS increases **344,064 bytes**. This makes the outstanding footprint problem
worse without closing throughput, so the protocol remains excluded. No generic alias behavior or
private ownership restriction enters the main branch from this control.
Evidence: `artifacts/p7-completion/confined-tee-c7da793/`.

A separate native initializer guard disproves another startup hypothesis at its tested 2 MiB input.
Identity, merge, fork and source-document streaming, plus the iterate tree control, complete with
correct output and actual 20 MiB heap guards while function-registry initialization is forbidden.
A direct registry control on that same instrumented image fails with the exact guard marker,
proving that the guard is active. Its extra friend-assembly access and native entrypoint are strictly
diagnostic, not production changes or acceptance observations. No URI or registry refactor follows
from these cold controls. Evidence: `artifacts/p7-completion/registry-cold-guard-c7da793/`.

A complete frozen e223b2f unit run now checks the existing strict runtime opt-in against every full
unit case, independently of the ordinary default-mode suites. This tests readiness for the required
Strict execution default without changing public settings, feature claims or original performance
gates. All **7,327 .NET 10 Release** cases pass with no failures/skips; every original case identity
and multiplicity and all frozen binary hashes remain unchanged. A short live-stack diagnostic arrives
after the test host has already completed; the existing process is terminal and is not restarted.
Evidence: `artifacts/p7-completion/full-strict-default-e223b2f/`.

A direct native descriptor-reporting control also remains excluded. It keeps the exact UTF-8 JSON
fields/newline and whole-process counters, but avoids Console on the successful Unix reporting path.
Its stdout/EINTR constants are checked against actual Darwin/Linux headers, not workload values.
All four selected 45-case reporting suites retain their original populations. The unchanged native
publisher succeeds without warnings; 69 runtime and eight consumer PDB source checks per variant,
all binary hashes and all 24 original 200 MiB fingerprints verify. Median RSS deltas are identity
**0**, merge **+61,440**, fork **+217,088** and source-document **−77,824 bytes**. This establishes no
consistent reduction and does not justify changing the reporter or acceptance counters.
Evidence: `artifacts/p7-completion/native-report-fd-c7da793/audit.json`.

A distinct schema control defers canonical built-in schema objects through positive type relations,
in addition to their construction. Custom schema identities and derivation, strictness, names,
nillability, RTF restrictions and the actual BCL singleton getters remain unchanged. Four focused
Release/Debug suites pass **1,489 .NET 10 /1,301 .NET 8** cases, preserving every previous selected
row and adding 110 singleton/parallel/custom-schema contracts. An independently loaded fresh-engine
snapshot matches all **126 types /15,876 pairs /47,628 relation checks** against the baseline. Cold
managed allocation falls from **68,504 to 10,000 bytes** in that diagnostic. The completed native
comparison retains all 24 original outputs, actual 20 MiB heap guards, binary hashes and 69 runtime
/eight consumer PDB checks per variant. Native RSS deltas are identity **0**, merge **+139,264**,
fork **−73,728** and source-document **−4,096 bytes**. There is no consistent useful footprint gain;
the schema changes remain excluded. Both allocation and native evidence are retained:
`artifacts/p7-completion/schema-relations-c7da793/snapshot-audit.json` and `audit.json`.

### Public streaming selection follows Strict/Fallback, 2026-11-01

The previous internal opt-in leaves public default/explicit Strict compilation on the tree path.
Twelve contracts cover both APIs, default/Strict/Fallback, declared-streamable/non-streamable modes,
actual first output before/after source EOF, caller-owned readers and settings mutation after compile.
Exactly the four default/Strict streaming cases fail on unchanged source; the other eight pass.

Strict now selects forward execution by default for declared-streamable inputs. Fallback retains the
tree path. Existing per-compiler test overrides and the explicit development `XSL3_STREAM_STRICT=0`
tree control stay intact, so no benchmark mode, input, output oracle or requirement changes. Existing
XDM inputs remain trees; saved styles retain their recorded mode. The initial-mode helper's default
and existing first-output cases now exercise the public selection rather than forcing the internal
switch. The feature claim remains `no` pending original qualification.

All four focused suites pass **408 /407** checks and retain their preceding 396 /395 rows. All full
Release/Debug suites pass **7,339 .NET 10 /6,229 .NET 8**, preserving every earlier case plus twelve
new rows per configuration, with no failures or skips and unchanged frozen binary hashes. Both changed
source PDBs match in each configuration. The unchanged saved/native consumer passes **739** checks;
all deliberate wrong-code, wrong-output and actual JIT-intermediate guards reject. All **456** saved
inputs and **eight** consumer sources remain unchanged, and the linked engine matches the frozen full
Release engine. The queued interpreter loaded its earlier 58-file PDB list before the settings-list
extension; a separate complete **59-source** union audit explicitly verifies the new settings file.
No repeated test or inferred source check is used to hide that distinction.

README, package readme, migration notes and Unreleased changelog describe this local default change
and the incomplete qualification. No version is tagged/published, and no new hosted full qualification
is dispatched merely for this selection change while known memory/throughput failures remain.
Evidence: `artifacts/p7-completion/public-default-e223b2f/compatibility-audit.json`.

### Isolated saved-adapter metadata control, 2026-11-01

A separate control starts from c7da793 and removes the broad reflected-method/field preservation
requirement from its generated consumer through a dedicated stateless adapter entry. Ordinary saved
data parsing is separated from early-bound construction; the new entry explicitly rejects early-bound
classes, including encoded metadata that omits its type array. The old Type loader and early-bound
constructor remain intact. No new warning suppression, private-runtime reference or publisher policy
is added. This control is not on the retained P7 branch.

The first test preparation misses a QIL namespace import and stops before running cases; its source
and failed build are retained. The repaired initial four suites pass **949 /584** checks. Direct C#
construction of a saved adapter then fails CS0012 before native measurement because the existing saved
metadata references the runtime core assembly. This is consistent with the [runtime team's reference-
assembly guidance](https://github.com/dotnet/runtime/issues/103357); no CoreLib dependency is added to the
consumer and no facade-emitter rewrite is included. Instead the dedicated entry uses the existing
`typeof(savedStyle)` syntax, direct interface data/execution access and only the constructor preservation
required by RuntimeHelpers.GetUninitializedObject. Normal direct-object loading remains available for
adapters constructed by their caller. The emitter, serialized format and normal native publisher stay.

The revised four focused suites pass **952 .NET 10 /587 .NET 8** checks, preserving every earlier
941 /578 row. New contracts cover normal/debug direct and Type entry execution, null/non-adapter
Types, incompatible versions, malformed data, absent delegates and both early-bound metadata forms.
Native publication succeeds without warnings. All **24** original 200 MiB observations validate with
actual 20 MiB heap guards; original output fingerprints, 32 stylesheet sources, 65 saved-file hashes,
binary hashes, **69 runtime /eight consumer** PDB checks per variant validate. Only the generated
consumer's loader call changes. The native image shrinks **48,192 bytes**. Median RSS deltas are
identity **0**, merge **−110,592**, fork **−102,400** and source-document **−438,272 bytes**. Fork
elapsed is **1.019031** times baseline, so no throughput improvement is claimed. These are local
ARM64 diagnostics, not original hosted x64 gate passes. The candidate remains isolated pending
hosted footprint evidence and complete compatibility; all original failures remain open.
Evidence: `artifacts/p7-completion/direct-provider-c7da793/audit.json`.

### Hosted saved-adapter rejection and schema-demand trace, 2026-11-01

The isolated combined candidate `29e1152` includes the locally validated public default and saved
adapter control. Its explicit [complete push-memory check](https://github.com/skuirrels/Xsl3/actions/runs/41302072932)
runs the original driver with all 16 workloads, exact 209,715,200-byte inputs, 10 MiB controls and
20,971,520-byte absolute RSS/managed limits. The other default jobs are skipped only for this selected
diagnostic; removing the added input/job and selection clauses restores the default workflow byte-for-byte.

All **48 outputs** match prior original fingerprints, but only **13/16 budgets** pass. Whole-process
peaks remain merge **21,688,320**, fork **21,454,848** and source-document **21,409,792 bytes**.
The runtime is **.NET 10.0.14 /Ubuntu 24.04.5 x64 /four processors**. Artifact digest, both immutable
source records, clean post-run source state, original publisher/ELF layout, 32 stylesheet sources,
**70 runtime /eight consumer** PDB checks and raw reports validate. Different hosted cohorts are not
used to infer causal regressions. This does not justify the extra loader API, which stays excluded.

The combined local .NET 10 Release suite passes all **7,354** cases and retains every earlier row.
Once the original hosted budget failure is established, remaining local validation is explicitly
stopped: the .NET 8 run is incomplete, Debug is unrun and the queued unchanged native consumer never
starts. Exact process groups are verified before termination; this is an adoption decision, not an
observation timeout or an excuse to restart completed work. No partial suite is claimed as full
compatibility. Evidence: `artifacts/p7-completion/saved-adapter-hosted/hosted-audit.json` and
`cancellation.json`. No experimental loader/parser/workflow change enters the retained branch.

A distinct diagnostic wraps 32 built-in-schema lookup sites after the earlier rejected lazy query-type
control. Twelve 2 MiB identity/merge/fork/source-document observations preserve outputs and actual
20 MiB heap guards. All four workloads also complete while those lookups are forbidden; the same
image's direct lookup succeeds under tracing and rejects with the exact guard marker when forbidden.
No workload reaches a wrapped lookup. All **78 runtime /eight consumer** PDB sources and source/binary
hashes validate. The first wrapper declaration incorrectly omitted the BCL complex/code overload's
nullable return; its warning/image are retained without execution, the annotation is corrected and
no warning is suppressed. This rules out further canonical-getter shortcuts for the tested paths;
it proves nothing about uninstrumented BCL internals, frozen image data, larger inputs or absolute
RSS budgets. Evidence: `artifacts/p7-completion/schema-demand-c7da793/audit.json`.

### Typed DTD metadata control, 2026-11-01

A separate .NET 10 control replaces the complete private DTD reflection bridge with typed accessors,
using signatures read from the actual runtime. The .NET 8 implementation remains byte-identical within
its target guard. All four focused suites retain **680 .NET 10 /488 .NET 8** checks per configuration;
the unchanged NativeAOT bridge consumer passes **59** reader/writer/DTD checks. Its actual managed
intermediate rejects under CoreCLR. An initial self-contained Linux runtimeconfig cannot start on the
local host; that setup failure is retained, and a separate framework-selection-only diagnostic config
establishes the real negative control without changing the native consumer or its original config.

All **24** paired original 200 MiB observations preserve outputs and actual 20 MiB managed guards.
Source hashes, **70 runtime /eight consumer** PDB checks and binaries validate. The native image shrinks
only **9,936 bytes**. Median RSS deltas are identity **0**, merge **−53,248**, fork **−196,608** and
source-document **−126,976 bytes**. Elapsed ratios are **0.955829 /1.042799 /0.992610 /0.956876**,
respectively. These mixed local results do not close the original hosted gates or justify maintaining
another private generic bridge. The prototype is excluded; no hosted qualification is claimed.
Evidence: `artifacts/p7-completion/typed-dtd-c7da793/audit.json`.

### Native report-stage diagnostic, 2026-11-01

Twelve isolated original 200 MiB observations capture the self peak before final collection, after
returning the result, after JSON serialization and after successful stdout writing. Every output
matches the original fingerprints under the actual 20 MiB managed guard; **69 runtime /eight consumer**
PDB sources and immutable source/binary hashes validate. No self peak rises across any sampled stage.
The lifetime counter remains higher by **270,336–360,448 bytes** for merge/fork/source-document, and
matches for identity. Added instrumentation changes the image, and diagnostic stderr occurs after all
captures, so this neither locates the later difference nor establishes an operating-system accounting
defect. It supplies no justification for changing report transport, subtracting counters or relaxing a
budget. The control stays outside the branch. All local controllers are terminal and no hosted
qualification is active. Evidence: `artifacts/p7-completion/report-stages-c7da793/audit.json`.

### Current allocation controls and ordinary-profile regression, 2026-11-01

A fresh trace uses retained head `468ea7f`, the original 100 MB fork fixture/oracle and an actual 4 GiB
managed guard. All output fingerprints, binary hashes and **59 runtime /eight benchmark** PDB sources
validate. On .NET 10.0.14 /macOS ARM64, weighted allocation ticks still place source-frame creation
first; they also identify scalar-cast result arrays. These samples include startup, warmup and oracle
work and are not exact allocation totals or hosted timing/RSS qualification.
Evidence: `artifacts/p7-completion/current-allocation-468ea7f/audit.json`.

Two isolated scalar-cast controls share the existing conversion rules and preserve list/schema casts.
The first removes a singleton result array but caches the optional input; the second uses an opaque
optional-result protocol instead. Correctness alone does not justify either. Four final focused suites
retain **295 /263** earlier rows and add **30 /28** contracts, passing **325 .NET 10 /291 .NET 8** per
configuration. The unchanged-source negative control fails exactly six scalar-codegen expectations;
all seventeen initial semantic/fallback cases pass. Initial test-string and test-interface/visitor
preparation failures remain retained. No earlier failed attempt is presented as a runtime regression.

All **27** exact-allocation controls validate their original generated input/output, identical consumer
binary, frozen engines and actual 4 GiB guards. At 100 MB, baseline streaming allocates a median
**3,125,192,312 bytes**; the controls allocate **3,180,859,896 /3,180,860,368 bytes**, about **1.78% more**.
The same regression appears at 10 MB; tree allocation remains effectively unchanged. Sequence-valued
arithmetic consumers still materialize the optional scalar result. Both controls are excluded; no new
runtime ABI, compiler change or opaque token enters the branch. All **60** selected runtime PDB sources
match. No throughput/RSS gain or original gate pass is inferred from allocation correctness.
Evidence: `artifacts/p7-completion/scalar-casts-468ea7f/audit.json`.

The pushed retained head's [ordinary CI run](https://github.com/skuirrels/Xsl3/actions/runs/41307200620)
passes Windows, NativeAOT and ordinary performance. Ubuntu retains one new W3C failure: `merge-051`
requests an accumulator without `streamable="yes"`, and the newly active forward path correctly rejects
it with `XTSE3300`. The [XSLT applicability rule](https://www.w3.org/TR/xslt-30/#accumulator-applicability)
requires that restriction for streamed sources; relaxing the runtime to satisfy an ordinary tree-profile
case would be incorrect. The failed job and original failure stay retained.

The first harness repair explicitly selects public `Fallback` for ordinary/unstreamed runs. It passes
58 selected checks in six modern/legacy/saved Release/Debug combinations, but the complete ordinary
suite catches a new failure in `streamable-141`: that policy also disables a required `XTSE3430` static
check. The failed full suite remains retained and that broader policy change is not adopted.

The narrower repair explicitly sets the existing per-compiler execution flag from the original
`XSL3_STREAM_STRICT` request in both modern and legacy/saved compilation. Static streamability policy
remains Strict in every profile. This restores the harness's original tree/strict protocol after the
public default changes. No fixture, oracle, profile selection, known-failure baseline, original
acceptance command or runtime code changes. Thirteen behavioral contracts cover tree/strict execution,
actual source forwarding and required static errors in ordinary, unstreamed and S1/S2 profiles. The
final source-negative run has **five failures /eight passes** on unchanged source. All **63** focused
checks pass in each of six Release/Debug modern/legacy/saved runs. The complete ordinary suite passes
**97** harness tests. A digest-verified download of the failed hosted run proves that every previous
**84** W3C case remains and passes, adding exactly the thirteen new contracts. Ordinary conformance
returns to **8,850 passed /18 known failures /41 not run**.

Release/Debug reviewed S1 and S2 retain **1,000 /155** passing cases. Raw S1 retains **919** cases and
**66,643** expressions, with no independent reference disagreement; raw S2's two original conflicts
remain visible. Release, Debug and legacy/saved strict inventories each retain **2,695 passed /16
failed /one not run**, zero unmatched strict codes, and all 2,712 selected cases. Each original
inventory/forwarding/evaluation audit passes. The explicitly unstreamed inventory retains **2,693
passed /18 failed /one not run**. No baseline changes or original memory/throughput passes are inferred.

Both configurations verify **59 runtime /three harness** PDB sources. Production runtime sources are
unchanged. The pushed repair `42e5a73` passes all four final-source ordinary CI jobs in
[run 41311886937](https://github.com/skuirrels/Xsl3/actions/runs/41311886937). The ordinary W3C,
reviewed S1/S2, strict Release/Debug, legacy/saved and unstreamed steps all pass. This does not qualify
the original memory or throughput gates.
Evidence: `artifacts/p7-completion/profile-policy-468ea7f/` (failed public-Fallback attempt) and
`v2/audit.json` (final execution-only repair).

### Compact projected-fork producer controls, 2026-11-01

An isolated producer uses an open-ancestor value stack instead of allocating navigation nodes for
every source token. Published events retain immutable metadata; normal, early and failed fork exits
restore source navigation state. It applies only to compiler-proven projections over fresh untyped
stock readers, with ordinary fallback for observers, custom readers, DTDs and validation. Ring
capacity, threads, source lookahead, fixtures, oracles and publication settings stay unchanged.

Both the initial and in-place-parent refinement pass **367 .NET 10 /333 .NET 8** focused contracts
in Release and Debug, retaining every previous selected row and adding ten producer contracts.
The initial producer reduces exact 100 MB fork allocation from **3,125,192,312 to 2,109,538,568 bytes**
(32.5%) under the actual 4 GiB guard. Allocation reduction alone does not establish a gate pass.

A balanced Linux ARM64 four-CPU comparison retains **36 correct observations**, unchanged original
100 MB inputs and actual 4 GiB guards. Initial/refined fork streaming medians are **22.9% /6.9% slower**
than the baseline; identity also has no gain. Both remain excluded from the general execution path.

The refined producer's separately published NativeAOT comparison retains **16 original 10 MiB output
controls and 24 exact 200 MiB observations**, with actual 20 MiB managed guards. Runtime PDB checks
cover **59 baseline /60 candidate** sources and eight consumer sources per image. All outputs match.
Native fork is **11.4% slower**; median full-lifetime RSS increases **20,480 bytes**, from **21,139,456
to 21,159,936**, and all six fork observations exceed **20,971,520**. Merge/source-document median RSS
also increases **77,824 /204,800 bytes**. The zero report limit disables diagnostic RSS rejection only;
the actual managed cap remains 20 MiB, and no observation is an original acceptance pass. Neither
producer nor a NativeAOT specialization is adopted. ARM64 controls do not qualify hosted Linux x64.

Evidence: `artifacts/p7-completion/compact-producer-42e5a73/`,
`compact-producer-byref-42e5a73/`, `compact-producer-linux-42e5a73/` and
`compact-producer-native-42e5a73/audit.json`. Initial test-interface, test-assumption, verifier-count,
nullable scratch-build and local sandbox failures remain retained; none is hidden by a passing rerun.

A final isolated refinement passes the large metadata constructor arguments by readonly reference.
All four focused suites again retain **367 /333** passing cases. Its balanced 18-observation Linux
comparison still has a **1.1012** streaming elapsed ratio against the unchanged baseline (only **0.9925**
against the earlier refinement). All outputs and actual 4 GiB guards validate. Constructor copies do
not explain enough of the slowdown, so the entire producer experiment is closed without adoption;
no further native publication of this refinement is justified. Evidence:
`artifacts/p7-completion/compact-producer-inref-42e5a73/` and `compact-producer-inref-linux-42e5a73/audit.json`.

### Retained-path sampling and reader-guard review, 2026-11-01

Fresh four-CPU Linux thread-stack profiles validate the original 100 MB output and actual 4 GiB
heap guards. The first informational-metadata traces cannot resolve engine method names and are
explicitly uninterpretable. Separate verbose JIT/NGen traces resolve the retained fork, identity and
source-document paths and the rejected producer. DLL/PDB hashes match their previously verified
59 /60 runtime and eight benchmark source documents. Samples include startup, warmup and blocked
stacks: they are neither CPU milliseconds nor acceptance timings. Tee publication/reclamation stacks
remain prominent; previously rejected polling and padding controls are not repeated.
Evidence: `artifacts/p7-completion/current-sampling-42e5a73/audit.json` and
`current-sampling-verbose-42e5a73/audit.json`.

An isolated input-reader control bypasses repeated DTD inspection when its reader is configured to
prohibit DTDs. Its first guard accepts BCL readers under known project wrappers. All four focused
suites pass **296 .NET 10 /294 .NET 8** checks, adding eight contracts. All **84** paired seven-workload
100 MB observations validate. Candidate/baseline streaming ratios are identity **0.987843**, adjacent
**0.992748**, accumulator **0.934884**, iterate **0.996333**, merge **0.995419**, fork **0.996205** and
source-document **0.977773**. Identity/fork still miss no-slowdown targets at **1.001941 /1.173326**.
These small/mixed controls do not establish hosted qualification.

Review finds that a BCL wrapper can conceal a custom `XmlTextReader` subclass with misreported
Prohibit settings. The broad guard then incorrectly preserves DTD element-content whitespace.
A new regression fails that isolated prototype; its original passing controls did not cover this
case. The control is not adopted. A narrower guard requires the actual BCL text-parser identity,
using the existing authoritative `XmlReaderInternals` type name. Cancellation, read-error translation,
borrowed ownership, custom readers, DTD parsing and input settings must retain their original behavior.
Evidence: `artifacts/p7-completion/prohibited-dtd-read-42e5a73/`,
`prohibited-dtd-read-linux-42e5a73/audit.json` and `prohibited-dtd-parser-42e5a73/`.

The unchanged reader passes all **13** contracts on both frameworks, including the new BCL-wrapper
case. The narrowed type-name guard passes **297 .NET 10 /295 .NET 8** focused checks in both
configurations. Its complete local original-size NativeAOT comparison retains **96 correct outputs**:
48 children per variant, all sixteen workloads, exact 200 MiB measured inputs, original 10 MiB
controls, actual 20 MiB managed guards and the unchanged absolute 20 MiB RSS gate. Both images verify
**60 runtime /eight consumer** PDB sources, including the changed reader, and all 33 original
stylesheet/entrypoint sources. Baseline passes **15/16** budgets; candidate **14/16**. Candidate merge
is **20,983,808 bytes** against **20,865,024** baseline; fork remains over budget in both. The
full-lifetime counter retains failures even when the earlier in-process sample reports `ok`.
The narrowed type-name guard is excluded; no causal gain is inferred from these sequential ARM64
matrices. A controller syntax failure occurs before measurements and remains retained; completed
binaries are reused after the syntax correction. Evidence:
`artifacts/p7-completion/prohibited-dtd-parser-native-42e5a73/audit.json`.

A separate refinement reuses the bridge's existing cached parser `Type`, already initialized by
`QueryReaderSettings`, instead of formatting `Type.FullName`. All four **297 /295** focused suites
pass. Its complete 96-child local original-size NativeAOT comparison retains correct outputs and
**15/16** budgets in each variant; fork still fails, at **21,405,696 bytes** for the candidate.
Its balanced **84-observation** seven-workload 100 MB comparison is mixed: identity **1.006997**,
accumulator **1.010825**, iterate **1.065483** and source-document **0.997304** streaming elapsed
ratios against baseline. Fork improves to **0.949758** of baseline but still misses its original
target at **1.123687** stream/tree. Native and benchmark binary hashes and all **60 runtime /eight
consumer** PDB sources per variant validate. No useful memory improvement is established. The entire reader
fast-path experiment is closed without adoption; its new regression, unsafe broad guard, narrowed
controls and failed preparation evidence remain retained. Evidence: `prohibited-dtd-cache-42e5a73/`,
`prohibited-dtd-cache-native-42e5a73/audit.json` and `prohibited-dtd-cache-linux-42e5a73/audit.json`
under the P7 completion artifacts.

### Reference-count reclamation-token control, 2026-11-01

A separate control retains atomic reader-position claims and canonical per-event reference counts.
An extra count reserves a last-release token while clearing the consumed payload, then publishes
zero before the producer may reuse that epoch. Head cleanup stays under the gate before reservation
and at full-ring release. Capacity, threads, ownership and source scan bounds remain unchanged.
This differs from the previously rejected position-based reclamation and polling controls.

All **357 .NET 10 /323 .NET 8** existing focused lifecycle/alias/cancellation/prefix cases pass in
Release and Debug. Twelve balanced Linux ARM64 fork observations preserve original 100 MB inputs,
actual 4 GiB guards, output fingerprints and **59 runtime /eight benchmark** PDB source checks.
Streaming medians are **2,275.597 /2,283.851 ms** baseline/candidate, a **1.003627** ratio with
overlapping ranges. No useful gain justifies the added protocol, so it is excluded and no native
publication/RSS result is claimed. Review also notes that its metric getter can notify the caller;
a future implementation would need a pure observation path before any adoption. Evidence:
`artifacts/p7-completion/tee-reclaim-token-42e5a73/` and `tee-reclaim-token-linux-42e5a73/audit.json`.

### Numeric-cast/arithmetic fusion control, 2026-11-01

A distinct compiler control keeps two forward numeric cast results scalar until their arithmetic
consumer, avoiding the sequence rematerialization that made the earlier cast-only controls worse.
Canonical atomization, casting and arithmetic remain shared. Sequential lets retain left conversion
before right evaluation; optional emptiness stays opaque only between the two helpers. Compatibility
mode, list/schema/non-numeric casts and partial constructor functions retain ordinary compilation.
No function registry IDs, saved-data layout, fixtures, oracles, publisher or GC policy change.

All **392 .NET 10 /356 .NET 8** focused cases pass in Release and Debug, preserving every prior
selected row and adding **34 /32** semantic cases for promotion, emptiness, errors, operand evaluation,
live readers, both APIs and saved normal/debug styles. An initial saved-test syntax failure is retained;
it precedes all test execution. Eighteen balanced exact-allocation observations use the identical
consumer and actual 4 GiB guards. At 100 MB, fork streaming allocation falls **3,125,192,248 →
3,069,540,776 bytes** (**1.78%**); tree allocation stays effectively unchanged. All outputs match and
all **62** selected candidate runtime PDB sources validate, including the frozen allocation engine.

The separate uninstrumented Linux four-CPU comparison retains twelve correct original 100 MB
observations. Fork streaming medians are **2,318.456 /2,298.025 ms** baseline/candidate, only **0.88%**
lower with overlapping ranges; candidate stream/tree is still **1.221308**. Baseline/candidate runtime
PDB checks cover **60 /62** sources and eight benchmark sources each. Instrumented Mac timing worsens.
The small allocation saving establishes no useful throughput/RSS improvement to justify the new ABI
and compiler path, so the control is excluded and no native publication is claimed. Evidence:
`artifacts/p7-completion/cast-arithmetic-fusion-42e5a73/`,
`cast-arithmetic-fusion-allocation-42e5a73/audit.json` and `cast-arithmetic-fusion-linux-42e5a73/audit.json`.

### Ref-addressed compact frame slots, 2026-11-01

A new storage model removes the per-token `List<Frame>` indexer and copied-current-frame state left
in the earlier compact producer. Reusable array slots are addressed by reference; no reference
survives array growth or enters published metadata. Only open ancestors and one current leaf/closed
element retain references. Parent transitions and disposal clear dead slots. Exact parser identity
reuses the existing bridge cache, excluding custom DTD parsers hidden by BCL wrappers. Ring capacity,
threads, source scan bounds, publisher, inputs, oracles and GC/globalization policies stay unchanged.

Initial four focused suites pass **367 .NET 10 /333 .NET 8** cases. Review adds real DTD ID identity
fallback through a BCL wrapper and 100-level ignored namespace/language scopes, with full/early
completion, canonical numbers and ordinary continuation. All four reviewed suites pass **370 /336**
cases, retaining every previous row. Eighteen balanced Linux four-CPU fork observations validate
original 100 MB inputs, actual 4 GiB guards and output fingerprints. Streaming medians are baseline
**2,265.762**, previous compact **2,413.569**, slots **2,171.649 ms**. Slots are **4.15% faster** than
baseline in this cohort, but still miss the original no-slowdown target. No hosted gain is inferred.

Eighteen separate exact-allocation controls use the unchanged consumer. At 100 MB, streaming median
allocation falls **3,125,192,312 →2,109,538,288 bytes** (**32.5%**); tree median allocation is identical
at **1,284,879,976 bytes**. All outputs and actual 4 GiB guards validate. The complete local native
comparison retains **96 correct outputs** over all sixteen original workloads, exact 200 MiB measured
inputs, original 10 MiB controls, actual 20 MiB managed guards and unchanged absolute RSS limits.
Baseline/candidate pass **15/16 /14/16** budgets: merge is **20,865,024 /21,078,016 bytes**, fork
**21,020,672 /21,254,144** and source-document **20,209,664 /20,414,464**. All 33 stylesheet/entrypoint
sources and **59 /60 runtime /eight consumer** PDB sources validate. The general-backend design is
not adoptable: the JIT gains do not waive the additional native failure. A backend-specific variant
is not implemented or qualified. All original hosted qualification obligations remain. Evidence:
`artifacts/p7-completion/compact-slots-42e5a73/`, `compact-slots-linux-42e5a73/audit.json`,
`compact-slots-allocation-42e5a73/audit.json` and `compact-slots-native-42e5a73/`.

### Backend-specific slots and immutable event coallocation, 2026-11-01

A private backend-specific variant enables compact slots only when `RuntimeFeature.IsDynamicCodeSupported`
is true. NativeAOT retains the original forward navigator producer; the real NativeAOT/TPA probe guard,
consumer, publisher and original inputs/limits are unchanged. All four **370 /336** focused suites pass.
Its complete local native comparison preserves **96 correct outputs**, with **15/16** budgets in each
variant. Candidate fork remains over budget at **21,360,640 bytes**. Native image size is **1,176 bytes**
above baseline, versus **11,200** for general compact slots. Compact event/namespace/header-data symbols
are absent; `Read`/`Dispose` producer symbols remain. This is code-pruning evidence, not complete metadata
or RSS equivalence, and no original hosted gate pass is inferred.

All **84** balanced seven-workload Linux 100 MB observations validate under actual 4 GiB guards.
Candidate/baseline streaming ratios are identity **0.998611**, adjacent **1.007347**, accumulator
**1.012863**, iterate **1.017352**, merge **0.991786**, fork **0.954754** and source-document **1.002523**.
Fork is consistently faster locally, but candidate stream/tree remains **1.093928**; identity also
misses its target at **1.029979**. The variant stays isolated pending original qualification.
Evidence: `artifacts/p7-completion/jit-compact-slots-42e5a73/`,
`jit-compact-slots-native-42e5a73/audit.json`, `symbols-audit.json` and
`jit-compact-slots-linux-42e5a73/audit.json`.

A separate coallocation control makes the immutable start event own its metadata; end events reference
that owner, never mutable frame slots. It preserves the backend selection and all original bounds.
All four **370 /336** focused suites pass. Eighteen exact-allocation observations retain the identical
consumer, original inputs/oracles and actual 4 GiB guards. The 100 MB streaming median falls from
**3,125,192,312 to 2,046,929,408 bytes** (**34.5%**), with effectively unchanged tree allocation. The
initial launcher stops before any work because it names the wrong completed contract-report file;
that correction is retained and no passed tests are repeated. Eighteen uninstrumented Linux four-CPU
fork observations retain original inputs/oracles and actual 4 GiB guards. Streaming medians are baseline
**2,322.609**, prior slots **2,283.036**, coallocated **2,197.859 ms**: about **5.37% /3.73%** faster,
respectively, while the original fork no-slowdown target still fails. All **59 /60 /60 runtime /eight
benchmark** PDB sources validate. Complete frozen unit validation is in progress: .NET 10 Release passes
**7,352** cases, preserving every previous row and adding exactly thirteen; other configurations and the
unchanged saved/NativeAOT consumer remain required. No adoption or new native/RSS result is claimed. Evidence:
`artifacts/p7-completion/jit-coallocated-headers-42e5a73/` and
`jit-coallocated-headers-allocation-42e5a73/audit.json`.

## Preceding verified checkpoint, 2026-11-01

Commit `709da5b` passes all four [ordinary CI jobs](https://github.com/skuirrels/Xsl3/actions/runs/41255492209).
The separate [original acceptance run](https://github.com/skuirrels/Xsl3/actions/runs/41255497079) is
terminal and fails. Its unchanged limits remain required:

| Gate | Verified result |
|---|---|
| Reviewed S1 | 1,000 cases pass in Release/Debug; raw 919 cases /66,643 expressions retained |
| Reviewed S2 | 155 cases pass in Release/Debug; raw inventory retained |
| Strict runtime | Release, Debug and legacy/saved sampling each retain 2,695 pass /16 fail /1 not run; all 2,712 cases audited, zero unmatched strict codes |
| Push memory | 48 correct outputs, 13/16 budgets pass; merge 22,192,128, fork 22,544,384 and source-document 21,573,632 bytes exceed 20,971,520 |
| Nightly memory | 48 correct outputs, 16/16 budgets pass; exact 2,147,483,648-byte inputs, actual 209,715,200-byte guards; maximum whole-process RSS 37,281,792 bytes |
| Full throughput | All 126 observations valid at original 1/100/1,000 MB sizes and actual 4 GiB guards; four of seven 100 MB targets pass |

The hosted 100 MB stream/tree ratios are identity **1.056726**, adjacent **0.770432**, accumulator
**0.895177**, iterate **0.797717**, merge **0.848224**, fork **1.706268** and source-document
**1.055614**. The host reports .NET 10.0.14, Linux x64 and four effective processors. Independent
checks match every raw measurement, both binary hashes, the controller hash, **54 runtime sources**
including the saved-function loader fix, and eight benchmark sources. Both memory matrices match
all preceding canonical fingerprints and all 33 stylesheet/entrypoint sources. Evidence:
`artifacts/p7-completion/qualification-709da5b/`.

The final local full suites pass **7,207 .NET 10 /6,121 .NET 8** cases in both Release and Debug;
the unchanged saved/native consumer passes **739** checks. The extra loader-source PDB check also
passes for all four local engines and that native consumer. Small scheduling, namespace, reporting
and no-op-hook controls stay excluded: none closes an original failure. All results are retained.

This checkpoint is **not deployment-ready**. The original failed memory/throughput gates, remaining
S3–S6 contract accounting, public streaming integration and complete final review remain required.
PR #27 stays draft; `supports-streaming` remains `no`.


### Isolated controls after `709da5b`

The current continuation retains all original gates. The selected reference-counted candidate below has entered the branch for qualification.
Rejected controls remain isolated, and the authoritative hosted checkpoint is unchanged.

A bounded static fork projection preserves all parser events, the existing ring capacity and the
worker model. Filtered scans yield after at most the reserved number of original logical events;
empty nonterminal batches publish progress explicitly. All 448 focused contracts pass. The first
prototype preparation omitted three compiler writer/iterator checks (54 failures); those are
corrected, with the initial failures retained. All 42 local 100 MB observations are valid, but
identity /fork /source-document ratios remain **1.023728 /1.161994 /1.009577**. All 24 paired native
controls are valid; fork elapsed median improves about **18.6%**, but its **21,295,104-byte** median
whole-process peak still exceeds the original push budget. These ARM diagnostics are not hosted
x64 acceptance. Evidence: `bounded-projection-709da5b`.

A separate ASCII JSON report control matches independent BCL serialization for every UTF16 code
unit and surrogate boundaries, and passes all 45 report/probe checks. It preserves every field,
number, input, oracle and whole-process counter. Its 24 native controls retain matching outputs;
merge /fork /source-document median whole-process peaks fall by **221,184 /290,816 /270,336**
bytes, but fork remains above budget. Serialization runs after elapsed-time capture, so elapsed
differences here are layout effects, not a transformation-algorithm improvement. Evidence:
`ascii-report-709da5b`.

The composition with corrected immutable-header run claims and explicit guards for existing no-op
observation hooks passes 498 focused checks. Six paired JIT controls validate every output and
show about **12.3%** lower fork elapsed median. Its native/JIT PDBs match 58 runtime sources (including the projected fork entry point), and all
32 generated stylesheet sources match the original hosted inputs. Deliberate unbounded-scan and
lost-publication controls fail their intended regression tests. All 42 composed 100 MB observations
are valid, with five of seven local targets passing; fork /source-document remain **1.131261 /
1.005769**. The one-CPU native control then exposes a **9.99%** fork regression, so this position-based
composition is not adopted. The unneeded two-CPU run is stopped. Evidence: `projection-runs-709da5b`.

Fresh instrumented fork stack samples retain prominent tee-monitor and publication-wait stacks;
stack-sample counts are not elapsed-time or CPU-time attribution. A separate reclaim-gate control
initially fails five existing progress contracts. A narrowed snapshot records an empty ring with
both readers waiting and a pending production request. Correcting notification ordering restores
all 498 unchanged focused contracts. Six direct comparisons against the preceding composition show
only about **3.5%** improvement with overlapping ranges, so the extra synchronization protocol is
rejected. Its new alias tests were prepared but not executed; no native result is claimed. Evidence:
`spin-reclaim-709da5b`.

The next control restores the canonical remaining-reader counts in every mode, and keeps the
original predicate path when there are no spare CPUs. Only pure immutable-header run claims use
the spare-CPU optimization. All 498 focused contracts pass. Native/JIT runtime PDB checks cover
58 sources, probe PDBs cover eight sources each, and all 32 original XSL sources match. The six
one-CPU native observations are valid; the elapsed median is **1.010275** times baseline, with
overlapping ranges, replacing the rejected 9.99% regression. The six two-CPU observations are also
valid, with a **0.990480** ratio. Six four-CPU JIT observations retain a **0.849177** candidate/baseline
fork elapsed ratio. This is still a diagnostic improvement, not complete original qualification.
Evidence: `remaining-runs-709da5b`.

A direct one-line spare-CPU no-yield refinement passes 498 focused contracts, but its six direct
comparisons improve median elapsed time only **1.07%**, with overlapping ranges. It is excluded;
no new native result is claimed (`remaining-no-yield-709da5b`). A separate no-op consumption-hook
guard passes 5,020 selected contracts. Twelve paired observations show identity **0.994507** and
source-document **1.008392** candidate/baseline ratios, so that mixed control is also excluded
(`consumption-guard-709da5b`).

A schema-initialization prototype defers built-in schema singleton lookup without replacing the
BCL objects or changing custom schema identities, subtype semantics, serialized types, validation,
URI/globalization behavior or GC policies. Its first 110 identity/concurrency/custom-schema checks
pass. Correctly hash-verified baseline/candidate snapshots match for 126 types and all 47,628 relation
results. An isolated cold type-factory diagnostic falls **68,504 →9,192 allocated bytes**; this is not
RSS qualification. The snapshot harness initially used the wrong name-factory overload, then caught
an old output DLL selected ahead of the explicit engine hint path. Both stopped attempts are retained;
the corrected harness disables default item globbing and verifies actual copied assembly hashes.
Review adds an explicit deferred-built-in marker so missing custom schema data is never treated as
a built-in. The final version preserves all 47,628 relation results, passes the same 110 identity checks
on .NET 8 and 10, and cold allocation is 10,000 bytes. However, all 24 valid native comparisons retain
the same median whole-process peaks for identity, merge and fork; source-document rises by one page.
The prototype is rejected for this phase. No full-suite result is claimed for it
(`lazy-query-schema-709da5b`).

The reference-counted projection is adopted for qualification. Five additional caller-backed
mixed-alias/disposal cases pass. All four frozen full suites pass: **7,318 .NET 10 /6,208 .NET 8**
checks in both Release and Debug. Every preceding baseline case is present, adding 111 /87 cases;
DLL/PDB hashes remain unchanged. Each engine matches all 58 audited runtime sources, and all six
changed test/probe sources match the tested PDBs. The first full-suite launcher rejected its framework
argument before executing tests; that stopped attempt is retained. During adoption, comment/region
markers move the existing manual optimizer override outside the generated region; its C# statements
are unchanged. The unchanged native consumer passes **739** checks, all three deliberate failure
controls and audits of 456 saved inputs, eight consumer sources and 58 runtime sources. Its first
sandboxed publication could not start MSBuild's arm64 task host; the retained retry with required
process permissions passes without source/input changes. Six fresh fixtures copied from explicit
unit assertions produce 72 saved styles across Release/Debug, capacities 1/7/64 and tree/forward
execution. All **288** NativeAOT checks pass at 1/2/4/6 effective processors. Projected/fallback method
references, execution capture, all negative controls and runtime/consumer PDB sources are verified.
The fixture generator's first friend-assembly-name setup failure is retained separately; no fixture
or expected result changes are made. Full original hosted qualification remains required
(`remaining-full-709da5b`).

## Execution when the owner resumes full P7

These steps describe the remaining full objective, not authorization to restart work after 1.1.7.
Use the released baseline and evidence limits in [the handoff](p7-handoff.md).

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
   Review, validate, merge and prepare the resulting implementation for deployment under the release
   boundary authorized for that future phase.

Full P7 remains incomplete while any original requirement is unverified. Its work is currently paused;
the completed 1.1.7 checkpoint does not imply that the full objective stays active.

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

Evidence: `artifacts/p7-completion/copied-applicability-4c8e203/`. Hosted CI run **41210605246** passes
all four jobs for `ed818c0`: Ubuntu, Windows, NativeAOT and ordinary performance.
This resolves denied-accumulator lookup for these copies; it does not assign phase
values to schema-created nodes when the accumulator is applicable, dispose arbitrary mismatched
internal provenance, or close the original failed memory/throughput gates.

### Published-phase reuse and independent performance controls, 2026-10-31

Finishing a streamed accumulator phase previously repeated `Before` even when the same frame's
before-value was already published. Reuse that volatile value's publication proof to avoid the
redundant locked frame/name lookup. Initialization still runs for an absent before-phase, and
the existing after-phase cycle, fault, lifetime and publication checks stay in place.

All four full Release/Debug suites pass **7,179 .NET 10 /6,093 .NET 8** cases per configuration,
with the exact baseline case populations, no failures/skips, unchanged post-test binary hashes
and 53 runtime PDB source checks. The unchanged saved-style NativeAOT consumer passes **736**
checks and rejects deliberately incorrect codes, incorrect output and JIT hosting. Its linked
runtime equals the full-tested Release engine; six consumer PDB sources and saved inputs match.

Thirty-six alternating native controls independently compare this change and a separate inline
phase-storage candidate against `ed818c0`. All outputs and actual 4 GiB managed guards validate
on exact 100 MB inputs. This change reduces accumulator elapsed median by **3.89%** with one CPU;
twelve additional two-CPU observations repeat a **2.09%** reduction. Its RSS benefit is negligible.
These diagnostic comparisons do not qualify the original memory/throughput matrices.
Evidence: `artifacts/p7-completion/accumulator-phase-guard-ed818c0/` and `inline-only-ed818c0/`.

The independent inline phase-storage candidate passes 1,890 focused contracts and saves about
1.55% of actual observer allocation, but only 1.62% of native accumulator elapsed time and 116 KiB
of median RSS. It is not adopted. A typed saved-binding interface prototype also remains excluded:
455 managed contracts, 48 native smoke children and 24 paired controls validate, but its complete
original push matrix still fails merge, fork and source-document at **23,441,408 /23,887,872 /
22,990,848** bytes. No public interface, budget, input, URI/globalization or GC-policy change enters
the branch. Evidence: `artifacts/p7-completion/direct-binding-ed818c0/`.

A separate, unadopted compiler-derived name-rejection prototype reduces JIT accumulator streaming
elapsed median by **26.49%** in a 24-child Linux ARM64 .NET 10.0.14 comparison. All outputs and
actual 4 GiB guards validate. Its local accumulator stream/tree ratio moves from **1.138970** to
**0.848977**; identity remains above the original target, and this is only a two-workload diagnostic
subset. Saved/native filtering, broader contracts and original qualification are still required;
this prototype makes no support or completion claim. Evidence: `accumulator-filter-ed818c0/`.

The retained native resident-page evidence is now attributed to symbols after verifying its image
hash. Most resident managed code belongs to framework libraries; symbols on a resident page do not
prove those methods ran or predict recoverable RSS. This guides the next memory investigation,
without substituting attribution for operating-system peak counters. Evidence: `resident-symbols-d744911/`.

### Resolved accumulator rejection proofs, 2026-10-31

The forward observer now rejects a rule phase only when every rule has a compiler-resolved pure
element-kind/expanded-name test that cannot match the current frame. It reads the existing QIL
test rather than reparsing patterns or inferring names from fixtures. Complex or unknown tests
keep the full matcher; matching phases retain its original priorities and deferred errors. Initial
values, unchanged phase publication and cancellation between rejection checks remain intact.

Optional filtered constant tables use envelope **7**, explicitly retaining payload layout **3–6**.
Schema-free tables stay schema-free; schema type identities and typed-node aliases use their
original layouts. Tables without usable proofs keep their legacy serialized format. Older formats
remain readable; malformed rule/filter cardinality and unsupported payloads reject.

All four final Release/Debug suites pass **7,200 .NET 10 /6,114 .NET 8** cases per configuration,
preserving every baseline case plus 21 new contracts. All 1,322 source/test files match the verified
tree, frozen hashes survive execution, and 53 runtime PDB sources match. The ordinary accumulator
set preserves all **71** outcomes and details, including its existing permissive wrong-code case.
The strict suite retains **2,695/16/1**, zero wrong codes and the audited 2,712-case inventory.

Old saved styles pass the unchanged **736** native checks and rejection controls. Newly generated
styles pass **739** native checks: the same schema/applicability checks plus typed-node alias/type
identity and malformed-envelope controls. The 144 schema /84 applicability sources are unchanged;
the linked runtime equals the full-tested Release engine, with eight consumer /two generator PDB
source checks. Typed expectations come from the independent BCL schema reader.

Twenty-four native controls repeat accumulator median reductions of **22.16% /23.04%** with
one/two CPUs. The final seven-workload JIT 100 MB subset retains **42 valid observations**, actual
4 GiB guards and independent outputs. Stream/tree ratios are identity **1.009652**, adjacent
**0.719973**, accumulator **0.822222**, iterate **0.777044**, merge **0.644658**, fork **4.018782**
and source-document **1.011386**. The original throughput target still fails; the full 126-observation
matrix has not been rerun for this candidate.

The complete original local Linux ARM64 push matrix has all 48 correct outputs and **13/16**
budget passes. Merge **23,834,624**, fork **23,838,720** and source-document **22,958,080** bytes
still exceed **20,971,520**. Exact 200 MiB measured inputs, 10 MiB controls and the actual 20 MiB
managed guard are preserved. No memory qualification or public streaming claim follows from the
accumulator speedup. Evidence: `artifacts/p7-completion/accumulator-filter-saved-9a7e258/`.

The first Debug run exposed a pre-existing assertion when the new fixture explicitly declared an
empty `xpath-default-namespace`. The unchanged baseline reproduces it. The final fixture uses the
equivalent implicit empty namespace, retaining all ten patterns and independent counts; both
baseline and candidate pass. The compiler issue is deferred. A copied conformance-runner layout
also initially lacked its baseline directory after executing all strict cases; the unchanged files
were restored and its complete final harness/inventory checks pass. Initial failures remain retained.

A distinct private literal-header/delegate-field loader with an SDK-generated version source passes
56 managed and 48 native smoke checks, but its full push matrix still fails the same three budgets
at **23,687,168 /23,719,936 /23,109,632** bytes. It is rejected; no loader, build-version or public
API change from that experiment enters the branch. Evidence: `saved-fields-9a7e258/`.

Hosted CI run **41217612327** passes all four jobs on the preceding `9a7e258` phase-lookup fix.
Final-source hosted CI, remaining original memory/throughput gates, public integration, full-branch
review and deployment readiness remain required. Publishing remains outside the owner's latest
stopping point.

### Native report overhead and closed layout investigation, 2026-10-31

Exact head `2f3bc71` passes all four hosted CI jobs in run **41225255029**: Linux, Windows,
NativeAOT and ordinary performance. This does not qualify the separate original streaming gates.

The native probe now writes its existing result model directly through the BCL UTF-8 JSON writer,
avoiding source-generated object-serializer initialization in the measured process. Computed fields,
fingerprints, escaping, finite double round trips, null timings and nonfinite rejection are preserved.
All **44 focused checks pass on .NET 10 and .NET 8**, including independent ordinary BCL JSON
comparisons and the existing probe oracles. All tracked runtime C# files are unchanged; frozen PDB
checks verify 53 runtime and eight native-consumer sources, with unchanged saved stylesheet inputs.

Twenty-four serial native controls use the original 200 MiB inputs and actual 20 MiB managed guards.
Every output validates. Median whole-process RSS reductions are identity **667,648**, source-document
**466,944**, merge **794,624** and fork **352,256 bytes**. These are harness-footprint improvements;
their timing observations do not replace the original 100 MB throughput qualification.

The complete unchanged local native push matrix retains all **48 correct outputs** and **13/16**
budget passes. Merge **23,027,712**, fork **23,433,216** and source-document **22,511,616 bytes**
still exceed **20,971,520**. Inputs, 10 MiB controls, actual guards, output oracles and failures remain
unchanged. Evidence: `artifacts/p7-completion/direct-report-2f3bc71/`. Final-source hosted CI remains
required for this harness revision; no new nightly or complete throughput qualification is claimed.

Frame packing is rejected. Two corrected layouts reduce a node from 184 to 176 bytes, but their
36 valid 100 MB observations show mixed throughput; the simpler variant slows identity copying.
No node-kind or flag representation change is adopted. Evidence: `packed-flags-2f3bc71/`.

Twenty-seven corrected startup controls separate URI resolution, parsing, saved loading and a tiny
canonical warmup, using actual 20 MiB managed guards. URI initialization adds sampled ICU/C++ pages,
but URI plus saved loading and parsing remains below 20 MiB in that diagnostic image. This does not
establish a platform lower bound or qualify full XSLT execution. Initial controls that inadvertently
initialized CultureInfo before URI resolution are retained and invalidated for attribution. Evidence:
`uri-baseline-2f3bc71/`. No resolver, URI, globalization, GC policy, runtime or acceptance limit changes.


### Internal Linux probe metadata packing, 2026-10-31

Exact head `e78cbe1` passes all four ordinary hosted CI jobs in run **41229255711**.
This does not qualify the separate original streaming acceptance workflow.

The internal glibc Linux NativeAOT probe now uses non-hydrated metadata and LLD packed relative
relocations. The publisher rejects a missing RELR section, retained hydrated metadata or missing
loader ABI. These settings apply only to this nonpackable probe on `linux-x64` / `linux-arm64`;
its documented prerequisites are LLD and glibc 2.36+. Libraries and consumer projects do not inherit
the settings. No runtime features, globalization, GC policy, input sizes, limits or thread model change.

The isolated comparison preserves all 687 tracked C# sources and 65 saved inputs. All 24 original-size
observations validate, with roughly 2 MiB lower whole-process RSS in source-document, merge and fork.
The complete local ARM64 push matrix retains all **48 correct outputs**, with **14/16** budget passes.
Merge **21,057,536** and fork **21,143,552 bytes** remain above **20,971,520**. Failures remain failures;
ARM64 diagnostic results do not substitute for the original hosted Linux x64 qualification.
Evidence: `artifacts/p7-completion/no-hydration-relr-e78cbe1/`.

A fresh publication using the actual branch project/publisher passes all **48** native smoke observations.
All **407 tracked runtime C# files** are unchanged; PDB checks verify 53 runtime and eight probe sources.
The same 32 source stylesheets and entrypoint are retained; 32 saved DLLs are freshly generated, and their
new hashes are recorded rather than claiming byte identity. Source verification and ELF evidence are in
`artifacts/p7-completion/packed-probe-e78cbe1/`. Hosted exact-size memory and complete throughput checks
remain required. Conservative identical-code folding saves only 1,984 binary bytes and is excluded.


### Hosted packed-probe qualification and saved-function review, 2026-10-31

Commit `b812ea1` passes all four ordinary hosted CI jobs in **41248148903**. Its original hosted
Linux x64 nightly matrix in **41248149515** passes **16/16 budgets /48 children**, at exact
2,147,483,648-byte inputs and actual 209,715,200-byte managed guards; maximum whole-process RSS is
**37,392,384 bytes**. All 53 runtime PDB sources match the immutable source snapshot.

The same hosted push matrix retains **48 correct outputs /13 of 16 budgets**. Merge **21,983,232**,
fork **22,798,336** and source-document **21,381,120 bytes** exceed the unchanged **20,971,520**
limit. Its 200 MiB inputs, 10 MiB controls, actual 20 MiB heap guards, native-host flags and full-lifetime
counters validate. Neither correctness nor the nightly pass waives these failures. The hosted full
throughput matrix completes with all 126 observations valid and actual 4 GiB guards. At 100 MB,
stream/tree ratios are identity **1.229341**, adjacent **0.869228**, accumulator **1.003102**, iterate
**0.880915**, merge **0.998150**, fork **3.435557** and source-document **1.174415**. Four targets
still fail. Both binary hashes, all raw stdout/measurements, the controller hash, 53 runtime and eight
benchmark/probe PDB sources validate. Evidence: `artifacts/p7-completion/packed-probe-e78cbe1/`.

Review finds that deserializing a named function accepts a null implementation lookup. This matters
when names are resolved across compatible saved versions without numeric registry IDs. The loader now
rejects an unavailable name/arity with `NotSupportedException` before accepting the table. The new
regression fails on the unchanged loader, while its genuine known-function control passes. Related
saved-format contracts pass **40 .NET 10 /38 .NET 8** checks in each Release/Debug configuration.
This follow-up still needs final-source CI. Evidence: `saved-named-function-b812ea1/`.

Three isolated build/report controls stay excluded: conservative linker folding saves only 1,984 binary
bytes; ordinary static initialization increases affected median RSS by 237,568–495,616 bytes; bypassing
redundant canonical-number parsing yields no useful reduction. CPU-specific code generation saves
167,936 bytes for merge but increases source-document/fork peaks and adds a CPU-portability restriction,
so the portable default remains. All numeric, runtime and original qualification checks are retained.

The previously held ordinary-parser cleanup has a fresh **42-valid-observation** seven-workload
100 MB .NET 10.0.14 subset, with actual 4 GiB guards and unchanged outputs. It still misses identity
**1.013329**, fork **3.960180** and source-document **1.025273** stream/tree targets, so it stays out;
no new native qualification is inferred from its unmeasured packed executable. Evidence:
`artifacts/p7-completion/child-origin-packed-b812ea1/`.

Benchmark runtime metadata now records the effective processor allowance. Earlier one/two-CPU wait
controls do not test spare-core scheduling; a separate four-CPU fork control is being evaluated with
unchanged thread count, ring capacity, source/callback ownership, inputs, oracles and guards.


### Spare-processor fork handoff qualification, 2026-10-31

A four-CPU control covers a scheduling condition absent from earlier one/two-CPU spin experiments.
The effective .NET processor allowance is recorded as **4**, with matching cgroup CPU quota. Reader
publication waits first reduce the 200 MiB fork median from **12,931.874 to 8,488.299 ms** across
12 valid identity/fork observations, under actual 20 MiB managed guards. Total fork CPU rises from
14,817.945 to 20,526.262 ms; the elapsed improvement alone does not establish the final policy.

The complementary caller-pump and callback-completion policy reduces fork median from
**8,394.281 to 3,924.793 ms**, and total CPU from **20,220.332 to 11,295.958 ms**, in another
12 valid observations. Median fork RSS moves from **21,295,104 to 21,254,144 bytes**, still over
20 MiB. All sources, callbacks, payload slots and thread counts retain their ownership and bounds.

All three waits share a stack-only **1 ms** budget. They release their monitor while polling, then
recheck protected state before parking. Work enqueue, worker completion, production/cancellation and
disposal advance the caller's notification version. A running callback still finishes before its
worker can reuse arguments. Optimism applies only when the effective processor count exceeds caller
plus prongs, preserving ordinary parking at lower allowances. Focused Release/Debug contracts pass
**102 .NET 10 /101 .NET 8** cases per configuration, with 53 native runtime PDB source checks.
Evidence: `artifacts/p7-completion/spare-core-wait-b812ea1/`.

The combined policy and saved-function review fix have a source-verified .NET 10.0.14 Linux ARM64
four-CPU 100 MB subset: **42 valid observations**, actual 4 GiB guards, independent output checks and
byte-identical JIT/native-link input engines. Ratios are identity **1.023908**, adjacent **0.706812**,
accumulator **0.849566**, iterate **0.797248**, merge **0.621815**, fork **1.244550** and
source-document **1.010016**. Three local targets still fail. Original hosted memory/throughput gates
and remaining public integration remain required.
Evidence: `artifacts/p7-completion/spare-core-qualification-b812ea1/`.


### Final handoff-policy verification, 2026-11-01

The unchanged one/two-CPU parking controls retain all six observations per allowance, correct outputs
and actual 20 MiB managed guards. Fork elapsed ratios against the baseline are **0.991995 /0.997351**;
median RSS increases **86,016 /286,720 bytes**. These costs remain recorded and are not budget passes.

All four full unit configurations pass with no failures or skips: **7,207 .NET 10 /6,121 .NET 8** in
both Release and Debug. Every previous 7,200 /6,114 case remains, plus five reporting contracts and
two named-function cases. An initial count verifier used a stale expected count; its failure is retained,
the exact added case names are audited, and no passed suite is rerun to conceal that correction.
Source and binary hashes remain unchanged throughout, with 53 runtime PDB source checks.

The unchanged saved/native consumer passes **739 checks**, preserving all 456 input files and eight
consumer sources. Wrong-code, wrong-output and real JIT-intermediate controls reject. Its linked engine
matches the full-tested Release engine; 53 runtime and eight consumer PDB checks match. Consumer build
settings retain their defaults. Evidence: `artifacts/p7-completion/spare-core-full-b812ea1/`.

### Coallocated compact producer: completed compatibility and native exclusion check

The private coallocated candidate completes all **27,188** full unit rows (7,352 .NET 10 and
6,242 .NET 8 in each configuration) and all **739** unchanged saved/NativeAOT consumer checks.
Source/PDB audits preserve all previous cases, all 456 saved inputs and eight consumer sources.
The fresh local ARM64 NativeAOT comparison has 96 correct observations, but the baseline meets
16/16 original push budgets while the candidate meets 15/16: fork peaks at **21,622,784** bytes
against **20,971,520**. No adoption or hosted acceptance is claimed. The native image still
contains two compact-producer method symbols despite the creation guard. An isolated follow-up
guards every producer access with the same backend invariant to investigate code elimination;
all original publisher, inputs, oracles, limits and acceptance requirements remain unchanged.
Evidence: `artifacts/p7-completion/jit-coallocated-headers-42e5a73/compatibility-audit.json`,
`jit-coallocated-headers-native-42e5a73/comparison.json` and its `symbols-audit.json`.

The all-access backend guard follow-up passes **370 /336** focused checks per configuration
and removes every compact-producer/event/header method symbol from the native image. It does
not establish RSS equivalence: both full 48-child matrices have correct outputs, the baseline
passes 16/16, and the candidate passes 15/16 (fork **21,422,080** bytes). The internal fork peak
also exceeds the limit (**21,037,056**), so this is not solely an outer-observer difference.
The follow-up remains unadopted. Evidence: `artifacts/p7-completion/jit-guarded-headers-native-42e5a73/`.

### Backend metadata inlining and combined startup control

The direct-accessor refinement passes **370 /336** focused cases in each configuration.
Its guarded IL nevertheless leaves nine out-of-line native metadata getters; the original
native image has none. Explicit getter inlining restores that original code shape and removes
the nine getter symbols while retaining the same complete focused population.

Each refinement completes the two original 48-child local NativeAOT push matrices with correct
outputs, exact input sizes and actual 20 MiB guards. Both variants pass 15/16 budgets in each
of those cohorts. Direct-accessor fork peaks are baseline **21,381,120** /candidate **21,233,664**;
inlined-accessor peaks are baseline **21,147,648** /candidate **21,012,480 bytes**. The latter
candidate still exceeds the unchanged **20,971,520-byte** limit. Variability between cohorts
and a smaller native image do not establish an RSS pass or causal memory improvement.

The inlined candidate's balanced **84-observation** seven-workload 100 MB comparison retains
all original outputs and effective **4,294,967,296-byte** heap guards. Its stream/tree ratios
are identity **1.017317**, adjacent **0.720864**, accumulator **0.815854**, iterate **0.840741**,
merge **0.619244**, fork **1.125669** and source-document **0.985387**. Identity and fork remain
failed. All source/PDB and frozen binary checks pass: 59 baseline /60 candidate runtime sources
and eight benchmark sources. These local ARM64 subsets do not replace the full hosted 126-observation gate.
Evidence: `jit-direct-headers-native-42e5a73/audit.json`,
`jit-inline-headers-native-42e5a73/audit.json` and `jit-inline-headers-linux-42e5a73/audit.json`
under the P7 completion artifacts. The initial post-auditor assumed every refinement republished
the baseline; it fails before writing results. The repaired audit verifies the shared frozen
baseline against its retained original premeasurement source/PDB/binary hashes. Measurements do not repeat.

A separate 27-path private composition combines the already-tested startup binding changes from
`c7da793` with the compact source and native metadata inlining. Both earlier failed qualification
records remain. Its initial patch check stops at the parser bridge without applying anything;
the non-overlapping twenty files then apply cleanly and the bridge preserves the compact producer's
exact cached parser-identity helper. The combined candidate passes **807 .NET 10 /430 .NET 8**
focused checks per configuration, retaining the union of all previous saved, provider, bridge,
version and fork cases. Its own native publication/qualification is pending. No production runtime
change, hosted acceptance, merge, deployment readiness or streaming-support claim follows from
these experiments. Evidence: `composed-compact-42e5a73/` and `composed-compact-native-42e5a73/`.


The combined source is frozen at **b2316fd** on `feature/p7-composed-compact`; PR #27 stays at
`42e5a73`. All four full suites pass **27,200 checks** (7,356 .NET 10 /6,244 .NET 8 per
configuration), retaining the complete previous population union. The unchanged NativeAOT consumer
passes **739** checks, preserves all 456 saved inputs/eight consumer sources, and rejects the
original bad-code, bad-output and JIT-intermediate controls. Frozen-source/binary audits pass,
including the complete **71-source** runtime PDB union and all changed test sources. No publisher
warning occurs. Evidence: `composed-compact-42e5a73/compatibility-audit.json`.

Its local original NativeAOT push matrix passes **16/16 budgets /48 outputs**, maximum **20,717,568
bytes**. The complete hosted x64 original push matrix in
[run 41335503127](https://github.com/skuirrels/Xsl3/actions/runs/41335503127) still passes **13/16**:
merge **21,798,912**, fork **21,835,776**, source-document **21,311,488 bytes**. All 48 outputs,
original fingerprints, inputs, actual 20 MiB guards, artifact digest, exact source tree and complete
71-source runtime/eight-source consumer PDB checks verify. The dispatch controller is retained
`29e1152`; its separately checked immutable tested source is `b2316fd`. This diagnostic run does not
qualify ordinary CI, nightly memory or throughput. Evidence:
`composed-compact-hosted-b2316fd/hosted-audit.json` and `runtime-pdb-complete-union.json`.

JIT assembly inspection proves compact generic fresh-node creation dispatches separately through
nine metadata accessors and calls the existing node constructor. A separate sealed-header fresh-node
projection control is prepared without changing the frozen candidate. Another isolated one-file
control tests obtaining the actual parser type through the existing typed BCL implementation getter
instead of parsing its assembly-qualified name; missing bindings retain the original lookup, and
.NET 8 is unchanged. Neither control has a performance or memory claim. Full original P7 qualification
and deployment readiness remain open; no tag or package publication occurs.


The separate parser-type-origin control passes **807 /430** focused checks per configuration and
both complete local original push matrices (**96 correct outputs**, 16/16 budgets per variant).
Three alternating repetitions of merge/fork/source-document preserve another **54 original-size
outputs**, the actual 20 MiB guards, source/binary hashes and **71 runtime /eight consumer** PDB
sources per variant. Median absolute RSS deltas are merge **−274,432**, fork **−188,416** and
source-document **−143,360 bytes**. These are local ARM64 control results, not a hosted pass or a
claim that every type-name lookup is cold. Existing single-cohort observations remain. Its first
post-replicate auditor has a Python syntax typo and does not run; the corrected stored auditor checks
the same observations without repeating them. Evidence: `reader-type-origin-native-b2316fd/audit.json`
and `replicates/audit.json`. No adoption follows before remaining qualification.

A constructor-inlining hint is now under broader existing streaming/lifetime/saved/bridge contracts.
The first complete Release configurations pass **4,438 .NET 10 /3,480 .NET 8** checks; Debug remains
in progress. A separate combined fresh-header projection/constructor control is prepared but untested.
Its planned three-variant benchmark has 126 **100 MB-only diagnostic** observations; that population
is explicitly different from the required original 126 observations at 1 MB /100 MB /1 GB. The
original full multi-size qualification remains required.


The constructor hint completes all **15,836** broader existing streaming/lifetime/saved/bridge
checks (4,438 .NET 10 /3,480 .NET 8 per configuration). The one-dispatch header projection with
that hint also passes all **807 /430** selected checks per configuration. A balanced three-variant
**100 MB-only** diagnostic validates all 126 observations, original outputs, actual 4 GiB guards,
71 runtime/eight benchmark PDB sources per variant and every frozen source/binary hash.

Against its same-cohort baseline, constructor-only fork elapsed is **0.952349** and combined
projection/constructor elapsed is **0.937817**. Fork stream/tree remains **1.063152** and
**1.019498**, respectively. The combined identity ratio is **1.040074**. Neither variant meets all
seven original targets; no 5% display band or cross-cohort combination changes that result. Evidence:
`fresh-constructor-linux-b2316fd/audit.json`. This is not the original multi-size 126-observation gate.

Captured JIT code verifies a specialized fresh-header projection of **336 bytes**, compared with
**1,036 bytes** for generic creation after constructor inlining. Literal assign-reference helper
counts alone do not measure all barrier work. The specialized code still initializes six empty
strings/default arrays before assigning real metadata. A separate direct-metadata constructor
control preserves every node field/default and field declaration order, and passes **807 /430**
focused checks per configuration. Its balanced timing comparison is pending; no improvement or
native RSS equivalence is claimed. The JIT section parser initially fails on an empty leading split
section; corrected analysis verifies the same captured files without rerunning execution. Evidence:
`fresh-constructor-jit-inspection-b2316fd/assembly-audit.json` and `direct-node-metadata-b2316fd/`.

Review also checks the untyped eligibility boundary against the [.NET 10 baseline parser source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.Xml/src/System/Xml/Core/XmlTextReaderImpl.cs#L2812):
parser-context DTD information follows `DtdProcessing`, including rejection under `Prohibit`.
This supports the guard review; installed-runtime contracts and source checks remain the evidence
for the actual .NET 10.0.14 runs.


The direct metadata constructor completes all **807 /430** focused checks per configuration.
Its balanced three-variant **100 MB-only** comparison retains all **126 correct observations**,
original fingerprints, actual 4 GiB guards, 71 runtime/eight benchmark PDB checks per variant and
all frozen hashes. In that cohort direct/baseline streaming elapsed ratios are identity **0.996482**,
adjacent **1.007302**, accumulator **0.961996**, iterate **0.981463**, merge **1.007105**, fork
**0.983063** and source-document **1.001629**. Direct stream/tree ratios remain failed for identity
(**1.059437**) and fork (**1.100169**). The preceding fresh-header variant also fails both in the
same cohort; its fork/baseline elapsed ratio is **0.985785**, retaining the variability from the
previous cohort rather than selecting only its earlier favorable observations.

The optimized direct projection is **312 bytes /nine literal assign-reference helper sites**,
compared with **336 bytes /ten sites** for the preceding fresh projection. Those instruction
reductions do not establish a reliable elapsed-time gain. The constructor refactor stays unadopted;
no NativeAOT publication is justified for this timing control. All original multi-size, hosted
memory and throughput gates remain required. Evidence: `direct-node-linux-b2316fd/audit.json` and
`direct-node-jit-inspection-b2316fd/assembly-audit.json`.

### Composed fork CPU attribution and hosted reader-type check, 2026-11-01

Six alternating original 100 MB fork-stream diagnostics retain canonical outputs, frozen binaries,
actual 4 GiB managed guards and four effective CPUs. External per-thread scheduler counters show
no cgroup CPU throttling. The three busiest observed threads consume roughly 2.1–2.8 CPU seconds;
two spend less than 2 ms on the runnable queue. These sampled cumulative counters include the full
invocation and do not identify useful work versus busy waiting. Evidence:
`artifacts/p7-completion/fork-thread-cpu-b2316fd/audit.json`.

A disposable diagnostic image adds only Linux perf tools to the existing toolchain. User-mode CPU
sampling needs the container's `PERFMON` capability; host settings, production binaries, original
runtime policy, source ownership, capacities and gates remain unchanged. The first DWARF captures
cannot unwind above ReadyToRun `SpinOnceCore`. Their missing `-k 1` also prevents perf JIT injection;
that failure and original data remain retained. An offline jitdump decoder recovers the managed
names without disabling ReadyToRun. A separate frame-pointer capture resolves the full callers.

The runtime module's recorded and actual build IDs match. Recorded mappings and ELF segments map
the hot file offset `0x44b9b0` to virtual address `0x45b9b0`, whose instructions form a processor
spin loop. Of **1,510** full-invocation CPU samples, **462** hit that instruction: **342** in reader
publication waits, **119** in the caller pump and **one** in caller invocation. **554** have
`SpinOnceCore` as their first resolved managed frame. All **1,326** optimized engine code ranges
cross-check against the independent text perf map; outputs, binary hashes and actual heap guards
verify. These are ARM64 CPU diagnostics, not removable elapsed time or hosted acceptance. Earlier
wait-policy controls remain closed; this attribution does not justify repeating them. Evidence:
`fork-perf-cpu-b2316fd/`, `fork-perf-cpu-r2r-b2316fd/` and `fork-perf-fp-b2316fd/audit.json`.

The independently tested reader-type lookup control is frozen at `b3be00fc` on
`feature/p7-reader-type-origin`, one runtime file beyond `b2316fd`. It obtains the same exact BCL
parser type through the existing typed accessor, retaining the original lookup fallback and .NET 8
path. No runtime change enters PR #27. The unchanged original complete push matrix finishes in
[hosted run 41344662345](https://github.com/skuirrels/Xsl3/actions/runs/41344662345), using the existing
`29e1152` diagnostic controller. All **48 outputs** match prior canonical fingerprints, but only
**13/16 budgets** pass. Merge peaks at **21,823,488**, fork **21,868,544** and source-document
**21,291,008 bytes**, above the unchanged **20,971,520-byte** limit. The artifact digest, exact source
tree, clean source state, original publisher/fixtures/entrypoint, actual native/heap guards and
**71 runtime /eight consumer** PDB sources verify. The local reduction does not establish a hosted
gate improvement; the control remains excluded. Separate hosted cohorts do not establish a causal
before/after RSS difference. All controllers are terminal; no passed measurements need restarting.
Evidence: `reader-type-origin-hosted-b3be00f/hosted-audit.json`. Deployment readiness remains incomplete.

### Native first-touch attribution and complete hosted throughput check, 2026-11-01

An external minor-fault trace uses the unchanged, previously source-audited `b2316fd` NativeAOT
image, original **209,715,200-byte** inputs and actual **20,971,520-byte** managed guards on four
Linux ARM64 CPUs. All four identity/source-document/merge/fork outputs match prior canonical
fingerprints. Frozen image/source hashes and image/debug/recorded build IDs match; no sample losses
are reported. The four captures contain **1,723 /1,894 /1,923 /1,970** minor-fault events. The offline
parser initially omits thread names containing spaces; its corrected result accounts for every
event without repeating workloads.

Source-document and merge each have **164** fault stacks in ICU loading through
`XmlQueryContext.OpenStreamedSource`; fork has **164** through `DocumentResources.ClaimWrite`.
Identity has none. Ordinary node allocations account for many remaining resolved engine callers.
Faults are not resident bytes: read-ahead, repeated faults and data allocation prevent treating
these counts as removable RSS. This does not establish a new source fix or a dependency lower bound;
existing URI/globalization behavior and original counters stay intact. Evidence:
`artifacts/p7-completion/native-page-faults-b2316fd/audit.json`.

The combined candidate has not yet had the original complete hosted throughput measurement.
A separate manual controller, `c69b8186` on `feature/p7-canonical-benchmark-diagnosis`, checks out
immutable source `b2316fd` and runs the original benchmark arguments exactly, with source/artifact
path prefixes only. It retains source-tree and before/after binary hashes, actual runtime provenance,
raw observations and both engine/benchmark PDBs. The primary branch and original workflow stay
unchanged. It selects only this outstanding measurement, avoiding another known-failing push rerun.

[Hosted run 41345905819](https://github.com/skuirrels/Xsl3/actions/runs/41345905819) is executing the
original **126 observations**: all seven workloads, 1/100/1,000 decimal MB, three rounds per mode,
actual **4 GiB** heap guard and the strict no-slowdown target. Results and the artifact audit remain
pending; an observation timeout must not restart the live run. A separate auditor is prepared to
validate every original fingerprint, all 126 rows, source/binary hashes and **71 runtime /eight
benchmark** PDB sources, then independently recompute all seven medians. No timing result or P7
completion is inferred. Evidence: `canonical-benchmark-controller-b2316fd/dispatch.json` and
`composed-compact-hosted-benchmark-b2316fd/audit.py`.

### Owner-directed release boundary, 2026-11-01

The owner requested a release with completed work rather than further full-P7 optimization.
The bounded 1.1.7 checkpoint uses retained PR #27 source plus release corrections. It restores
published opt-in execution instead of enabling the unqualified backend through default Strict analysis.
The unsuccessful private compositions remain excluded. Their hosted benchmark was cancelled at the
owner's stopping request; partial observations cannot qualify acceptance. All three private combined
strict profiles finished before the stop (unadopted source, not release evidence). Original gates and
negative results remain intact. Final release-source review, CI and package/publication checks are
tracked in [the checkpoint boundary](p7-runtime-checkpoint.md).
