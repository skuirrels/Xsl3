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
