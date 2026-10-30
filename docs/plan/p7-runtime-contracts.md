# P7 runtime contract evidence

This records implementation-level acceptance evidence for the resumed [full P7 objective](p7-completion.md).
It does not change the original population, applicability rules, memory budgets or throughput targets.

## Physical reads and first output

`tests/Xsl3.Tests/StreamingReadBoundaryTests.cs` uses the existing canonical stylesheet generator,
generated input and independent output fingerprints. It adds a non-seekable wrapper around each actual
input stream. Before the first nonempty sink write, the wrapper refuses to supply bytes past half of
that source's length. Reads crossing the boundary are clipped; the next attempted read throws. This
observes bytes supplied to the XML parser, including parser read-ahead, rather than navigator positions.

| Canonical workload | Required observation |
|---|---|
| Identity | Principal output after no more than half of the principal input has been supplied |
| Adjacent grouping | Group output after no more than half of the principal input has been supplied |
| Source-document | Principal output after no more than half of the external source has been supplied |
| Merge | Merge output before either external source crosses its individual halfway boundary |
| Fork | Actual secondary-sink output after no more than half of the shared principal input has been supplied |
| Accumulator | The final total first reaches the sink after the complete input has been supplied |
| Iterate | The final total first reaches the sink after the complete input has been supplied |

Every workload runs with exact 1 MiB and 4 MiB generated inputs through both public APIs. On .NET 10,
the tests also compile, load and execute a saved stylesheet through `XslCompiledTransform.Load(Type, settings)`.
This is the existing saved entry point; the tests do not invent a saved-loader path for `XsltExecutable`.

For each incremental workload, a tree-mode control must fail at the same physical boundary with no
observed sink write. This proves the guard detects eager input consumption. Successful forward runs
must consume the exact complete input, match the original independent output fingerprints, and capture
the expected actual forward source representations during stylesheet execution. Fork checks both
secondary outputs as well as its empty principal result. Principal writes and secondary-result opening
remain on the invoking thread; independent secondary sinks may be written by their prongs.

The final-total cases run successfully in both modes, verify the full output fingerprint, and require
first output at end of input. Their guards deliberately permit complete input because those results
depend on every record. These cases do not claim early output.

These tests establish read/emission contracts for these shapes. They do not alone prove an arbitrary
stylesheet's static applicability, bounded retained state, byte-level memory usage or performance.
The original exact-size NativeAOT matrices and paired benchmarks remain separate requirements.

## Existing state and ownership contracts

| Area | Concrete test evidence | Remaining qualification |
|---|---|---|
| Accumulators | `StreamedAccumulatorTests`: start/end values, skipped subtrees, dependency cycles, applicability, copies and saved bindings. `StreamedAccumulatorStorageTests`: independent counter slots, deferred headers, fork consumers and cancellation after output | Complete mapping of original S5 requirements and retained-state bounds |
| Grouping | `StreamedGroupingTests`: consume members before advancing, drain unused members, nested groups, repeatable grounded copies, key cardinality/errors and saved execution | Complete original construct/applicability mapping |
| Merge | `StreamedMergeTests`: sorted inputs, stable groups, raw key values, context-size rules, XTDE errors, ownership, retained snapshots and saved execution | Complete original construct/applicability mapping |
| Tee | `StreamTeeTests`: ordered single-source reads, bounded high-water mark, partial reservations, source-fault prefix, detach/dispose/cancellation, collectible events and concurrent reader aliases | Relate event bounds to payload assumptions and complete full S6 acceptance mapping |
| Fork callers | `ForkCallerTests`: input/output ownership, lost-wakeup protection, full-ring output progress, partial-ring refill and active callback lifetime. `ForkProngsTests`: declaration-order failures, cancellation and joining | Original absolute RSS and throughput targets still fail |

A tee capacity counts events, not bytes. The ring and unpublished staged payloads share its capacity;
each reader can additionally retain its current event. Payload sizes, ancestor/scope metadata, reader
buffers, explicitly retained copies and prong output must also be accounted for. Unbounded text or
attribute payloads cannot acquire a constant byte bound merely from a passing ring-count assertion.
XSLT's permitted fork output buffering is distinct from the shared input bound.

## Non-forwarding case review

The retained runtime inventory still contains all 2,712 cases. The four latest strict captures preserve
the same 2,459 cases with stylesheet-stage forward sources. Of the other 253 cases, 144 have no
stylesheet-stage command and 109 have stylesheet commands without an observed forward source. Their
outcomes are 246 pass, six fail and one not run. These are observation categories, not exclusions.

The [253-case review](p7-runtime-nonforward-review.csv) now records each identity, original definition
fingerprint, actual primary diagnostic, outcome and disposition. Fresh Release/Debug API and legacy
captures preserve all underlying case records and forwarding observations. They add the evaluation
result before assertions and the initial-source declarations read by the ordinary runner.

| Disposition | Cases | Evidence |
|---|---:|---|
| Expected compilation error | 156 | Actual primary compiler code matches a pinned error assertion |
| Expected dynamic error | 8 | Actual primary runtime code matches a pinned error assertion |
| Tree or absent principal input | 76 | Stylesheet commands receive tree/absent inputs and no streaming principal source is declared |
| Explicit nonstreamed initial mode | 6 | Supplied readers are paired with a nonstreamed invoked mode; exact declarations/static parameters are recorded per case |
| Retained failure | 6 | Four compilation failures, the pending streaming feature claim and a schema-validation mismatch remain failures |
| Retained collection skip | 1 | The existing unavailable collection environment remains not run |

The six reader controls are accumulator-033s, -036, -044s and -045s, si-group-057-ns and si-fork-812ns.
Their modes specify `streamable="no"`, omit the attribute, or use the test's explicit false/no static
parameter. This agrees with [XSLT 3.0 modes](https://www.w3.org/TR/xslt-30/#modes). Tree-supplied inputs
need not be forwarded, while static streamability analysis still applies
([streamability guarantees](https://www.w3.org/TR/xslt-30/#streamability-guarantees)).

The review does not remove cases, change assertions, change the denominator or waive a failure.
It accounts for the missing forwarding observations; it does not establish every original S3–S6
construct's static/runtime obligations or retained-state bounds. Those mappings remain open alongside
the original memory and throughput targets.

`RuntimeCaseReport`'s optional evaluation observer belongs to one runner invocation, including if a
worker finishes late after timeout. The export keeps `Returned`, `Error` and `NotObserved` distinct.
It reads the actual primary compiler diagnostic and reuses the public API's dynamic-error translation
for legacy exceptions; expected assertion codes never supply a diagnostic. A returned evaluation can
still fail an assertion. Deferred replay/assertion failures remain visible in the case outcome and
forwarding capture; this field describes the evaluation before assertion checking.

CI now requires `--require-evaluation`. Missing or malformed observations, undeclared capture data,
coerced source flags and a passing runtime case with no observed evaluation fail the audit. The
frozen forwarding minimum still independently protects every previously forwarding case.

Source evidence: `artifacts/p7-completion/evaluation-observation-de179f8/{release,debug}-{api,legacy}.jsonl`,
their audits and `comparison.json`. Original fixtures and `p7-runtime-inventory.json` are unchanged.
