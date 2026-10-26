# P7B: static streamability analysis close-out

**Completed under the owner's approved suite revision on 2026-10-26, after the 1.1.0 P7A release.** This is a finite follow-up to
[P7A](p7a-closeout.md), covering the outstanding S1 expression-analysis reconciliation and S2 static
diagnostic evidence. The [implementation checkpoint](#implementation-checkpoint) records the bounded work
performed before the fixture decision. The [completion record](#p7b-completion) records the adopted
1,000-case population, retained original results and final acceptance evidence.
Full P7 remains deferred. Normal transforms build trees and `supports-streaming` remains `no`.

## Baseline and provenance

- Source: `bc2307c22c0fa5994f509a9f6b3cafc9f53ee1e1` (`main` after the 1.1.0 merge), whose tree matches
  release commit `c989c9273991da874274a71575decac82316ad12`.
- W3C xslt30-test: `fddf1cf920087e791f13315d68dfbe874d97dc56`, with no tracked suite changes.
- Refreshed .NET 10 Release `sandp`: **790 passed, 129 failed, 0 not run**, across 919 cases and
  66,643 XPath expressions. These are conformance counts; the enclosing xUnit test passing means that
  the failure baseline matched.
- Refreshed unstreamed `strm`: **2,693 passed, 18 failed, 1 not run**, with one unmatched expected-error
  code (`streamable/streamable-149`, XTTE1510). That dynamic validation case is outside this phase.
  The aggregate pass count does not prove exact S2 codes or streaming execution.
- Local logs, raw reports and inventory hashes: `artifacts/p7b-plan/`. This ignored directory is supporting
  evidence; the two checked-in inventories below retain the case identities and source paths.

The [S1 inventory](p7b-s1-cases.csv) assigns each of the 129 current failure IDs to exactly one ticket.
It records expression counts, context and the first reported mismatch. The harness assesses all expressions
before checking assertions, but reports only the first mismatch per case. Classification therefore identifies
the initial failure family; it does not prove that every assertion in that case has the same cause.

| Ticket | Initial failure family | Cases |
|---|---|---:|
| P7B-01 | Attribute/namespace expectations disagree with the implemented final-Recommendation rules | 99 |
| P7B-02 | Expectations disagree with statically empty selections | 22 |
| P7B-03 | Generated posture-and-sweep input contains no expressions | 8 |

These are the documented discrepancies in the original plan, reproduced without changing analysis,
suite data or baselines. Each ticket must establish the full case disposition before choosing a fix.
Use the [published XSLT 3.0 Recommendation](https://www.w3.org/TR/xslt-30/), particularly
[§19.1.1](https://www.w3.org/TR/xslt-30/#static-type-of-steps),
[§19.8.8.8](https://www.w3.org/TR/xslt-30/#streamability-of-path-expressions) and
[§19.8.8.9](https://www.w3.org/TR/xslt-30/#streamability-of-axis-steps), as rule authority.

### Reconcile the S2 denominator

The original plan's **136 XTSE3430 + 2 XTSE3155** target describes failures at the October 22 baseline;
136 was not the complete catalogue population mentioning XTSE3430. The
[S2 inventory](p7b-s2-cases.csv) follows active `catalog.xml` references and effective dependencies:

| Scope | Cases | Treatment |
|---|---:|---|
| Streaming-required XTSE3430 | 151 | S2 exact-code audit |
| Streaming-required XTSE3155 | 2 | S2 exact-code audit |
| Ordinary-profile required XTSE3430: `streamable/streamable-141` | 1 | Preserve the existing ordinary-profile diagnostic |
| Ordinary-profile alternative XTSE3430: `arrays/square-array-201` | 1 | Preserve its `any-of`; successful output is also allowed |

All dependencies of the 153 streaming-required cases are supported by `Profile.Streaming` at the source
revision above. The manifest includes schema-aware cases. It is a catalogue expectation inventory, not a
per-case execution or exact-code result. Two of its cases are current reported failures:
`si-fork/si-fork-953` and `sx-UnionExpr/sx-union-202`. No other S2 manifest ID appears in the refreshed
unstreamed failure list; an explicit outcome audit is still needed to rule out not-run or wrong-code results.

## Six tickets

### P7B-01 — Reconcile attribute and namespace posture expectations

**Scope:** the 99 IDs assigned to P7B-01 in the S1 inventory. Inspect every expression/assertion in those
cases, including namespace bindings and the schema-default `item()` context. Examples are
`foo/attribute()` and `attribute()/self::node()`.

**Entry points:** [ExpressionAnalysis.cs](../../src/Xsl3/Streaming/ExpressionAnalysis.cs), methods
`AxisStep_19_8_8_9` and `Path_19_8_8_8`, and
[Runner.Streamability.cs](../../tests/Xsl3.W3C/Xslt30/Runner.Streamability.cs).

**Done when:** each case has a cited rule derivation and a disposition: implementation defect, harness
context defect, or fixture conflict. Fix established defects in those existing paths with focused regression
coverage in `StreamabilityAnalysisTests` / `StreamabilityExpressionTests`. For a fixture conflict, obtain an
upstream correction or a reviewed, provenance-backed suite correction before counting it as conformant.
Do not change correct analysis to reproduce an obsolete expected label. Reassess the complete case after
each correction and record any additional mismatch under this same ticket.

**Dependency:** baseline inventory only; can be investigated independently of P7B-02/03.

### P7B-02 — Reconcile statically empty path selections

**Scope:** the 22 IDs assigned to P7B-02. Verify reachable node kinds and the ordering of the axis/path
rules. Distinguish an empty result from the sweep needed to evaluate its left operand; emptiness alone
does not erase prior consuming work. Examples include
`parent::foo/descendant-or-self::document-node()` and `descendant-or-self::document-node()/attribute()`.

**Entry points:** `AxisType_19_1_1`, `AxisStep_19_8_8_9` and `Path_19_8_8_8` in
[ExpressionAnalysis.cs](../../src/Xsl3/Streaming/ExpressionAnalysis.cs); the same harness and tests as P7B-01.

**Done when:** every case has a source-type/axis derivation and all its expressions have been rechecked.
Any code fix is demonstrated by a minimal regression covering both the empty selection and its sweep.
Fixture corrections have the same provenance requirement as P7B-01. No expression-, case-ID- or
expected-label special cases enter production analysis.

**Dependency:** baseline inventory only.

### P7B-03 — Resolve the eight empty generated fixtures

**Scope:** consumingB017, B018, B034 and B047; motionlessC008, G008, H008 and H027, with their complete
IDs and files in the S1 inventory. Trace them to the pinned suite's `tests/sandp/_base-expressions.xml`
and `_create-sandp.xsl`.

**Entry points:** those upstream generator inputs and the empty-input guard in
[Runner.Streamability.cs](../../tests/Xsl3.W3C/Xslt30/Runner.Streamability.cs);
[StreamabilityHarnessTests.cs](../../tests/Xsl3.W3C/StreamabilityHarnessTests.cs).

**Done when:** recover intended expressions only where the generator/source establishes their provenance,
or document why the fixture cannot be recovered. An upstream fix or reviewed suite correction must be
reproducible and retain a mapping to the original cases. Preserve the harness rule that empty input fails;
negation, unsupported assertions and missing data must not create a pass. Do not invent expressions to
obtain the expected posture. Unrecoverable cases remain visible and may prevent the phase's exit gate.

**Dependency:** baseline inventory only.

### P7B-04 — Close the S2 exact-diagnostic inventory

**Scope:** the 153 streaming-required cases in the S2 inventory, plus preservation of its two ordinary
cases. Record actual outcome, actual error code, source location and diagnostic reason for each applicable
required-error case using the existing runner and diagnostic tests. The two XTSE3155 cases are
`error/error-3155a` and `su-shallow-descent/su-shallow-descent-901`.

Resolve the two known expectation conflicts explicitly: the sort exception for a direct fork child in
`si-fork/si-fork-953` ([§19.8.4.19](https://www.w3.org/TR/xslt-30/#streamability-xsl-for-each-group)),
and the compiler's peer-union rewrite in `sx-UnionExpr/sx-union-202`. A successful permitted rewrite
requires positive result coverage; it must not be relabelled an exact-error pass.

**Entry points:** existing `ConstructAnalysis` / `ConstructAnalysis.ControlFlow` / `ConstructAnalysis.Declarations`,
`ExpressionAnalysis.Expressions`, loader declaration checks, `Xslt30RunTests` and
`DeclarationDiagnosticTests`, `ConstructStreamabilityTests`, `StreamingDeclarationTests`.

**Done when:** all required-code cases have explicit matching-code evidence or a reviewed fixture
resolution, with original failures still reported separately. Wrong codes, not-run cases, generic exceptions
and unrelated earlier errors cannot satisfy the gate. Preserve `Strict`/`Fallback` and `Explain` behavior,
source-syntax assessment in `sandp`, and compiler-only safe rewrites. Fix only defects exposed by this
manifest; unrelated runtime failures in the 18-case baseline remain outside this ticket.

**Dependency:** baseline inventory; coordinates suite-correction provenance with P7B-01/02/03.

### P7B-05 — Resolve the documented ascent-rule authority

**Scope:** the existing `ascent` call rule in
[FunctionStreamability.cs](../../src/Xsl3/Streaming/FunctionStreamability.cs), its existing
[FunctionStreamabilityTests.cs](../../tests/Xsl3.Tests/FunctionStreamabilityTests.cs), and the pinned
`su-ascent` test set. The current rule follows proposed
[XT30.E31 / Bug 30289](https://github.com/w3c/qtspecs/blob/6cef5545618cd2edfd94f32af488a16bb19c598c/errata/xslt-30/errata.xml);
the original plan records that proposal as distinct from a normative amendment.

**Done when:** record the authority used and the behavior for node and atomic return types, first-argument
transmission, remaining operands and consuming/free-ranging arguments. If published text and the proposed
correction differ, document the selected interpretation as an explicit conformance difference. Change code
only if that decision establishes a defect; retain focused tests and affected `su-ascent` outcomes. Do not
expand into a new higher-order-function or runtime workstream.

**Dependency:** baseline inventory; finish before P7B-04's diagnostic conclusions are finalized.

### P7B-06 — Validate and close the phase

**Scope:** consolidate P7B-01 through P7B-05, their case dispositions, source/suite revisions and results.

**Done when:**

1. S1 reaches the unchanged **at least 99%** threshold, with zero not-run cases and complete expression
   assessment. For 919 cases this means **at least 910 passed and at most 9 failed**. Publish the original
   pinned result alongside any corrected-suite result. A suite correction must be reviewed and reproducible,
   cite its authority, map all original cases and identify changes to expressions/assertions. Do not silently
   shrink the denominator, remove failures, add a pass override or weaken the baseline. A changed population
   requires an explicit plan revision before claiming this gate.
2. S2's original 136+2 obligation is reconciled against the complete current manifest. Publish required
   exact-code results, permitted result alternatives, corrected fixtures and unresolved original expectations
   separately. Do not claim the original suite gate passed merely by reclassifying its failures.
3. Run affected focused tests and the analysis/unstreamed profiles after changes. For compiler/harness
   changes, complete the repository's required .NET 8/10, ordinary conformance, saved-assembly, compatibility,
   NativeAOT and ordinary performance checks as applicable. Compare ordinary failure IDs and unmatched-error
   counts with the accepted release checkpoint; investigate new regressions rather than refreshing them away.
4. Update this phase's outcome and relevant conformance/divergence documentation. Keep the feature claim
   `no`, normal tree execution and the separate streaming-development CI boundary. Record deferred obligations
   by linking P7A; do not start them as part of acceptance.

**Dependency:** P7B-01 through P7B-05. If provenance or a specification conflict prevents an honest gate
pass, issue a bounded reconciliation report, mark the affected gate unmet and stop this phase's work at that
decision. Do not call P7B complete or start runtime/benchmark work to compensate.

## Order, exclusions and stopping point

Investigate P7B-01/02/03 first; settle fixture provenance before choosing changes. P7B-04 can collect
diagnostic evidence alongside them; resolve P7B-05 before finalizing its conclusions. P7B-06 closes the phase.
The six tickets are the scope boundary, not six invitations to add more workstreams. New findings unrelated
to their case inventories or the ascent decision go to a later backlog.

Excluded: new streamed execution constructs or API paths, retained-model expansion, schema/atomic runtime
integration, memory and throughput optimization, interpreter/XSD 1.1/parallelism work, feature-claim changes,
package publication and changes to `bench/Xsl3.StreamingBenchmarks/`. The original memory/throughput targets
and [full P7 exits](p7-streaming.md#stages) remain unchanged and outside P7B.

**Stopping point:** the owner explicitly authorized the 1,000-case reviewed suite, retention of the original
919-case results and completion of bounded P7B. The six tickets close under that explicit acceptance revision.
Full P7 execution, memory, throughput and feature-claim work remains deferred.

## Implementation checkpoint

This section preserves the pre-revision checkpoint (`d780c83`). Its unresolved fixture decision has since
been resolved by the owner; its raw measurements remain unchanged and are retained below.

### P7B-01 / P7B-02: complete expression inspection

`XSL3_XSLT30_ASSESSMENTS` exports every assessed expression before assertion checking. The independent
`eng/audit-streamability.py` tool resolves the suite's generated two-axis paths against its canonical
`_base-expressions.xml` productions and applies §§19.1.1, 19.8.8.8–9 and 19.8.10. Production constraints
include the provably empty namespace-name selections; §19.1 permits more precise static types.
The tool rejects unknown syntax, missing/duplicate/changed/extra assessments and empty inventories.
It never edits a suite file or supplies an application result.

All **66,643** posture/sweep outcomes agree with that independent inspection. The
[S1 inventory](p7b-s1-cases.csv) now includes the complete groups and conflicting-expression counts for
each original failure:

| Disposition | Cases | Consequence |
|---|---:|---|
| Uniform expectation conflict | 65 | A provenance-backed assertion correction is possible |
| Mixed required outcomes within a single case | 56 | One assertion for every expression is insufficient; split/group fixtures with a full source-part mapping |
| Empty generator output | 8 | Invalid fixtures; no expressions can be recovered under their current constraints |

The 99/22 ticket assignment remains useful as the first-failure classification; it was not a statement
that all expressions in a case shared that cause. In particular, all 35 consumingA cases and 21 roamingC
cases are mixed. No S1 implementation defect was established by this inspection. The original **790/129/0**
baseline and all 919 case identities remain intact.

### P7B-03: empty-fixture provenance

All eight empty cases use a left production labelled `any-node` with the generator invariant
`node-type-unequal`. `_create-sandp.xsl` explicitly requires the left production's node type to differ from
`any-node` for that invariant; the candidate set is therefore empty. The affected base expressions are
`node()`, `child::node()`, `descendant::node()`, `descendant-or-self::node()`, `self::node()` (twice) and
`ancestor-or-self::node()` (twice). Inventing a right operand would contradict the generator constraint.
The empty-input rejection remains covered by the harness and the inspection tool's tests.

### P7B-04: exact static diagnostics and one compiler fix

The new `s2` development profile selects the applicable catalogue assertions directly, including the
ordinary mandatory case and the ordinary `any-of` case. It records the actual primary structured
diagnostic's expanded code, phase, module, line, column and reason. A dynamic exception, a message mentioning
the expected code or an unrelated later error does not satisfy an exact static-code check.

The 155-case result is **153 passed, 2 failed, 0 not run**: **152 matching static codes** (150 XTSE3430,
two XTSE3155), and **one permitted successful result**. The 153 streaming-required cases alone have
151 matching-code passes and two expectation conflicts. Both remaining failures are the previously documented
`si-fork/si-fork-953` and `sx-UnionExpr/sx-union-202`. They stay visible in a dedicated failure baseline;
matching that baseline does not close the original S2 gate. The [S2 inventory](p7b-s2-cases.csv) records each
case's outcome, code, line/column and reason. Full JSON reports retain module URIs.

The audit also identified an actual compiler defect in the already failing ordinary case
`streamable/streamable-141`. Construct analysis now applies §3.9.1's roaming/free-ranging override for
backwards-compatible instructions after assessing their operands, preserving nested diagnostics and
annotations. Strict compilation rejects even an otherwise motionless instruction; explicit Fallback and
ordinary non-streamable compatibility processing remain available. Only this verified fix is removed from
the ordinary failure baseline.

Three new regression cases cover direct/inherited/literal-element compatibility, Strict/Fallback and
version 2.0 acceptance. Six existing compatibility output cases previously requested guaranteed streaming
contrary to §3.9.1; they retain their typed-value/arithmetic assertions using explicit non-streamable sources
or modes. They no longer represent guaranteed-streaming coverage.

### P7B-05: ascent interpretation recorded

The pinned errata source marks E31 **proposed** on 2019-02-13. The implementation retains transmission of
the first argument and grounding for atomic return types, and documents the difference from the published
inspection rule in [divergences](../divergences.md#experimental-ascent-analysis). Eleven new cases cover
node/atomic results, first-argument postures and consuming/free-ranging remaining operands. The focused
function-category suite passes 33 cases. No new higher-order or runtime behavior was introduced.

### P7B-06: pre-revision acceptance boundary

The original S1 ≥99% and S2 all-required-diagnostics gates remain **unmet**. This is an implementation
checkpoint and bounded reconciliation outcome, not a claim that P7B or full P7 has met those exits.

A defensible fixture revision would split the 56 mixed cases into 145 uniform groups, retain the other
855 nonempty uniform cases, and preserve all 66,643 source expressions with original case/part mappings.
That yields **1,000 nonempty cases**, while the eight unsatisfiable generator combinations remain recorded
as invalid-source evidence and negative harness coverage. It also requires sourced assertion corrections
for the 65 uniform conflicts and positive result expectations for the two S2 fixture conflicts.
This is an explicit **proposed population revision**, not a silently altered 919-case denominator.
At this checkpoint no corrected population, fixture deletion or extra pass had been applied. The work
stopped at the decision whether to adopt a reviewed suite revision or obtain it upstream. The owner then
authorized the reviewed population described in the completion record. Memory, throughput and runtime
expansion remain outside scope.

The audit hooks, exact-code profile, source rule fix, regression tests and inspection tool are suitable for
review independently of that decision. CI retains raw baselines and adds the complete expression inspection
and exact-diagnostic audit. Five Python negative/coverage checks protect the inspection tool. Local evidence
is retained under `artifacts/p7b-implementation/`; validation results are recorded there and below.

| Validation | Result at this checkpoint |
|---|---|
| Release and Debug unit tests | 3,581 on .NET 10 and 3,430 on .NET 8, each configuration |
| Upstream regression tests | 1,654 applicable cases per framework |
| Byte-for-byte differential tests | 394 per framework |
| W3C harness, with saved-assembly sampling | 58 tests pass; ordinary XSLT 3.0 improves by the single verified case |
| Focused audit harness | 24 tests pass, including misleading-code, dynamic-error and primary-error/location regressions |
| Python inspection and process-controller tests | 18 pass, including five new inspection checks |
| Complete posture/sweep inspection | 66,643 outcomes; zero disagreements with the independent reference; raw 790/129/0 unchanged |
| S2 structured diagnostic audit | 152 exact codes with source locations/reasons; one permitted result; two fixture conflicts; zero not run |
| Unstreamed development profile | 2,693/18/1, with the existing one unmatched dynamic error code; unchanged |
| Globalized NativeAOT saved sample | macOS ARM64 publish/run pass, warnings treated as errors |
| Ordinary three-pair performance/allocation gate against `main` | Pass; 39 rows, time geometric mean 0.976 head/base; original thresholds unchanged |
| Source preservation | Original W3C suite and seven protected streaming benchmark files unchanged |

These are local validation results; hosted CI has been configured but has not been run for this branch.
The raw profile guards require the pinned 919/155 case populations, and S2 requires diagnostic source
location/reason as well as the exact primary code. Report capture retains S2 failures without changing the
test process's exit status. No streaming-memory or throughput acceptance claim is made by the ordinary gate.

## P7B completion

The owner explicitly approved proceeding with the **1,000-case corrected suite**, retaining the original
919-case results, and completing this bounded phase. This revises P7B-06's acceptance population and the
two conflicting S2 expectations. The S1 threshold remains at least 99%; the reviewed profiles enforce the
stronger current regression requirement of **zero failures and zero not run**. This is an Xsl3-reviewed
fixture revision, not a claim that the unchanged upstream suite's expectations now all pass.

| Gate | Original official-suite result, retained | Approved reviewed result |
|---|---|---|
| S1 posture/sweep | 790 passed / 129 failed / 0 not run, 919 cases | **1,000 passed / 0 failed / 0 not run** |
| S2 exact diagnostics/results | 153 passed / 2 failed / 0 not run, 155 cases | **155 passed / 0 failed / 0 not run** |
| S2 breakdown | 152 matching static codes, one permitted result, two expectation conflicts | **152 matching static codes and three checked positive results** |
| Source expression coverage | 66,643 XPath expressions | **The same 66,643 expressions, each mapped exactly once** |

The original [S1 failure inventory](p7b-s1-cases.csv), [S2 outcome inventory](p7b-s2-cases.csv), upstream
checkout and raw failure baselines are unchanged. CI retains complete raw and reviewed reports under
different names; the [result snapshot](p7b-results.json) records their populations and outcomes.

### Reproducible data correction

`eng/prepare-streamability-suite.py` prepares a fresh, separate fixture directory from the clean revision
pinned by `eng/fetch-w3c.sh`. It never consumes Xsl3 output or exported assessments. Posture/sweep expectations
come from the existing independent table-based reference and the canonical base productions. It retains
855 uniform nonempty cases, splits 56 mixed cases into 145 uniform groups, and records the eight impossible
generator combinations separately. Original expression text, context, namespace bindings and part IDs are
preserved. Historical descriptions are labelled as such alongside the corrected expectations.

`review.json` records all 919 original identities, all 1,000 reviewed identities and their original part
memberships, source/generated hashes, definition hashes and the eight invalid-source reasons. The existing
W3C catalogue loader has an explicit reviewed view that checks these hashes and verifies the full bijection.
Missing, duplicated or changed expressions, changed namespace/context bindings, omitted mappings and retiring
a nonempty source case are rejected. Each of the eight original empty cases is still executed as a negative
harness check before reviewed S1 acceptance; it must fail with the original empty-input diagnostic.

The two S2 corrections use ordinary XML-result assertions through the existing runner. Their expectations
are computed independently from the pinned `tests/strm/docs/books.xml` data:

- `si-fork/si-fork-953`: retain the original stylesheet. The direct-fork sort exception permits the construct;
  expected author groups follow first-appearance category order, with the position sort preserving that order.
- `sx-UnionExpr/sx-union-202`: the official stylesheet selects direct `BOOKLIST/ITEM` and `BOOKLIST/MAGAZINE`
  children that are absent from its input. The reviewed variant selects the actual `BOOKLIST/BOOKS/ITEM`
  rows in disjoint odd/even peer partitions, then their prices. Both arms must contribute. The assertion checks
  the complete nonempty XML result and document order. Original/reviewed selectors, source-data hashes and
  the fixture-shape repair are recorded explicitly; the official stylesheet is untouched.

No prices, author names or category keys are embedded as desired answers. Reviewed selection is opt-in via
`sandp-reviewed` / `s2-reviewed` and `XSL3_XSLT30_REVIEWED`; raw and ordinary profiles continue to use the
official catalogue. No reviewed expectation is consulted by application code.

### Diagnostic hardening discovered by the reviewed gates

Debug S2 exposed `streamable/streamable-136`: lowering a rejected `xsl:number` unit could assert in QIL
validation after source analysis had already recorded XTSE3430. The compiler now returns all recorded
source-analysis errors before lowering that rejected unit, with no QIL result. The load/save callers already
check compiler errors before consuming QIL; `Explain` now handles the same diagnostic-only result explicitly.
QIL checks remain enabled. Three regressions cover both debug-information settings, the public and legacy
compiler paths, preservation of both independent diagnostics, and Explain recompilation. This changes the
static error path; it does not introduce number execution support or a new runtime capability.

### Ticket closure

| Ticket | Disposition |
|---|---|
| P7B-01 | Closed: sourced attribute/namespace expectations corrected, with complete case/part traceability |
| P7B-02 | Closed: empty-selection outcomes separated from other expressions; prior consuming work preserved |
| P7B-03 | Closed: eight unsatisfiable generator outputs retained as invalid-source evidence and negative checks |
| P7B-04 | Closed: 152 exact static diagnostics and three positive results pass; raw discrepancies retained |
| P7B-05 | Closed: proposed E31 interpretation documented and covered by category regressions |
| P7B-06 | Closed under the explicitly approved fixture revision and bounded validation matrix |

CI checks the raw 919-case baseline, the reviewed 1,000-case gate, raw/reviewed S2 results, the complete
expression mapping and fixture-integrity tests. Debug reviewed gates and the targeted declaration regressions
also run, so the diagnostic-masking defect remains covered. Raw and reviewed failures retain the test process's
nonzero exit status. Hosted CI is configured; the validation recorded here was performed locally.

The pre-merge review also made the approved S2 balance an explicit gate: 152 exact static codes and three
positive XML results must remain covered. Debug S1 and S2 run in separate steps after a successful harness
build, so a declaration or S1 test failure cannot suppress the S2 audit or either generated Debug report.

| Final validation | Result |
|---|---|
| Reviewed S1, Release and Debug | 1,000/0/0 in each configuration; all 66,643 original expressions conserved |
| Reviewed S2, Release and Debug | 155/0/0 in each configuration; 152 exact static codes and three checked results |
| Raw S1 / S2 | Original 790/129/0 and 153/2/0 retained, with original baselines and inventories unchanged |
| Full Release and Debug units | 3,584 on .NET 10 and 3,433 on .NET 8, each configuration |
| Full W3C, with saved-assembly sampling | 66 tests pass; ordinary XSLT 3.0 remains 8,850/18/41; QT3 remains 21,884/0/99 |
| Fixture/harness integrity | 32 focused tests pass in Release/Debug; 26 Python tests pass |
| Upstream / differential | 1,654 / 394 per framework |
| Globalized NativeAOT saved sample | macOS ARM64 publish/run pass, warnings treated as errors |
| Ordinary three-pair performance gate | Pass: 39 gated rows; time geometric mean 0.998253, maximum allocation ratio 1.000718 |
| Preservation | Official W3C checkout and all seven protected streaming benchmark files unchanged |

Full P7 remains deferred and `supports-streaming` remains `no`. No memory/throughput target, runtime expansion,
benchmark controller, saved-format contract, package version or publication state is changed by this close-out.

## Reproduce the baseline

Build the W3C test project from the source revision being assessed, then run from the repository root
without `XSL3_STREAM_STRICT` or baseline-update settings:

```sh
dotnet build tests/Xsl3.W3C -c Release -f net10.0 -m:1 -p:UseSharedCompilation=false
XSL3_XSLT30_PROFILE=sandp dotnet test tests/Xsl3.W3C -c Release -f net10.0 --no-build --filter FullyQualifiedName~Xslt30Conformance
XSL3_XSLT30_PROFILE=strm dotnet test tests/Xsl3.W3C -c Release -f net10.0 --no-build --filter FullyQualifiedName~Xslt30Conformance
XSL3_XSLT30_PROFILE=s2 dotnet test tests/Xsl3.W3C -c Release -f net10.0 --no-build --filter FullyQualifiedName~Xslt30Conformance
```

Retain the `w3c-reports/xslt30-run-sandp.md` and `xslt30-run-strm.md` reports beside the test binaries.
For exact-code evidence, set `XSL3_XSLT30_CODES` to an **absolute** fresh log path; it appends unmatched
expected codes, so absence from that log alone does not establish a per-case pass. Compare the S1 inventory
with the exact baseline failure-ID set. For S2, inspect only active catalogue references, inherit set
dependencies unless the case overrides their kind, and preserve assertion structure (`error` versus `any-of`).
The inventories are planning evidence; application behavior must never consult their IDs or expected values.

To reproduce the independent inspection, export `sandp` with `XSL3_XSLT30_ASSESSMENTS` set to an absolute
JSONL path, then run:

```sh
python3 eng/audit-streamability.py --suite external/xslt30-test --assessments /absolute/path/assessments.jsonl --output /absolute/path/audit.json
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s eng/tests -p test_streamability_audit.py
```

An inspection tool exit of zero means complete agreement with its independent reference for these generated
paths. It does not convert the original 129 fixture failures into conformance passes.

For the approved reviewed gates, prepare a fresh output directory and run the normal harness:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 eng/prepare-streamability-suite.py --suite external/xslt30-test --output artifacts/p7b-reviewed
XSL3_XSLT30_PROFILE=sandp-reviewed XSL3_XSLT30_REVIEWED="$PWD/artifacts/p7b-reviewed" dotnet test tests/Xsl3.W3C -c Release --no-build --filter FullyQualifiedName~Xslt30Conformance
XSL3_XSLT30_PROFILE=s2-reviewed XSL3_XSLT30_REVIEWED="$PWD/artifacts/p7b-reviewed" dotnet test tests/Xsl3.W3C -c Release --no-build --filter FullyQualifiedName~Xslt30Conformance
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s eng/tests -p 'test_*streamability*.py'
```

Repeat the reviewed profile commands with `-c Debug` after building the Debug harness. The reviewed profiles
reject subset selection and cannot be relaxed by baseline updates. Their reports use
`xslt30-run-sandp-reviewed.md` and `xslt30-static-streamability-reviewed.{md,json}`. The original report names
remain reserved for the unmodified suite. Local final evidence, including full mappings and both sets of
assessments/reports, is retained under `artifacts/p7b-closeout/`.
