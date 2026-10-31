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
| Accumulators | `StreamedAccumulatorTests`: start/end values, skipped subtrees, dependency cycles, applicability, copies and saved bindings. `StreamedAccumulatorStorageTests`: independent counter slots, deferred headers, fork consumers and cancellation after output. `StreamedAccumulatorLifetimeTests`: measured scalar and bounded rolling-string state across two generated input sizes | Complete mapping of original S5 requirements, general growing/opaque/node-valued state and explicit copies |
| Grouping | `StreamedGroupingTests`: consume members before advancing, drain unused members, nested groups, repeatable grounded copies, key cardinality/errors and saved execution | Complete original construct/applicability mapping |
| Merge | `StreamedMergeTests`: sorted inputs, stable groups, raw key values, context-size rules, XTDE errors, ownership, retained snapshots and saved execution. `StreamedMergeLifetimeTests`: measured snapshot/key retention, explicit compiled closures, source/action failures, cancellation and resource ownership through both APIs and supported saved loading | Complete original construct/applicability mapping and original absolute process-memory qualification |
| Tee | `StreamTeeTests`: ordered single-source reads, bounded high-water mark, partial reservations, source-fault prefix, detach/dispose/cancellation, collectible events and concurrent reader aliases. `StreamTeePayloadTests`: measured live payload bytes with explicit payload assumptions and an isolated retention mutation | Complete full S6 acceptance mapping and original process-memory qualification |
| Fork callers | `ForkCallerTests`: input/output ownership, lost-wakeup protection, full-ring output progress, partial-ring refill and active callback lifetime. `ForkProngsTests`: declaration-order failures, cancellation and joining | Original absolute RSS and throughput targets still fail |
| Nested fork frames | `StreamedForkInputTests.NestedEventsDoNotRetainConsumedProngFrameState`: after the source advances past an intermediate prong frame, a slow nested reader may retain its published event without retaining that frame's private runtime state | A focused lifetime contract; complete variable-payload, ancestor and replay accounting remains required |
| Observer and matcher cursors | `StreamedObservationTests.ObserverCallbacksKeepIndependentPositionsAfterTheSourceAdvances` retains ordinary callback positions through completion. `StreamedForkAccumulatorTests.MatcherCursorMutationDoesNotChangeTheRuleBodyOrCallerFocus` moves each matcher cursor while checking body and caller focus; omitting its required clone fails the isolated regression | Cursor ownership evidence; complete construct/state accounting and original performance qualification remain required |

A tee capacity counts events, not bytes. The ring and unpublished staged payloads share its capacity;
each reader can additionally retain its current event. Payload sizes, ancestor/scope metadata, reader
buffers, explicitly retained copies and prong output must also be accounted for. Unbounded text or
attribute payloads cannot acquire a constant byte bound merely from a passing ring-count assertion.
XSLT's permitted fork output buffering is distinct from the shared input bound.

### Measured retention with explicit payload assumptions

`StreamTeePayloadTests` measures reachable payload arrays at controlled checkpoints after every reader's
`Read` call has returned. Each reader retains only its current event, and at a checkpoint all readers
are at the same event. A weak-reference recorder observes live payloads after collection; its own
growing list is measurement instrumentation and is not part of a process-memory acceptance claim.

For capacity `C` and fixed payload length `P`, live payload bytes at each such checkpoint must equal
`(BufferedCount + 1) * P` and be no greater than `(C + 1) * P`. The extra event is the shared current
event already released from the ring. This checkpoint bound is narrower than the general allowance
of a separate current event per reader and does not describe every in-flight machine instruction.

| Capacity | Readers | Payload bytes | Events in short /long input | Checkpoint byte bound |
|---:|---:|---:|---:|---:|
| 1 | 2 | 1,024 | 112 /448 | 2,048 |
| 7 | 3 | 8,192 | 496 /1,984 | 65,536 |
| 64 | 4 | 1,024 | 4,144 /16,576 | 66,560 |

Every run measures 16 or 64 checkpoints, verifies the ring high-water mark and requires no live
payloads after the readers finish and their current references are released. Release and Debug pass
all six cases on .NET 8 and 10. A separate source-verified copy with consumed-slot clearing disabled
fails all six cases; restoring and rebuilding that copy passes all six again. The mutation changes
retention without changing the supplied event sequence and is never adopted in the working branch.

`StreamedAccumulatorLifetimeTests` exercises the actual initial-source observer with the existing
native-rule fixture, a generated non-seekable input and both before/after values. At 65,536 bytes
(569 records) and 1,048,576 bytes (9,117 records), sampled live per-row state remains at most one row
frame, one observer-metadata object and two tracked scalar phase lists. Moving the focus to the root
and closing the source releases all tracked row frames/metadata; the final root after-value may
legitimately retain one scalar list. The caller-owned generated stream remains open.

The same actual initial-source observer now also runs a bounded rolling-string program. Its initial
value is empty; the start rule forwards the prior value and the end rule appends a fresh string derived
from the current row's actual `@id`, retaining the last `W` items. Explicit test parameters control
the repetitions in each string. A conditional weak table deduplicates observed atomic objects without
rooting them; the weak-reference list and its storage are measurement instrumentation.

At a checkpoint, the current before/after windows overlap except for their entering/leaving items.
The tests require at most `W + 1` live atomic objects and `(W + 1) * P` logical payload characters,
where `P` is derived from the actual source IDs and configured repetitions. They also require at most
one current row frame, one row metadata object and two nonempty phase arrays. The initial empty
sequence is checked by identity against the runtime's shared `Sequences.Empty`; its static lifetime
is accounted for separately. After moving to the root and closing, row frames/metadata must collect,
while the root may retain one final phase array containing up to `W` atomic values.

| Window `W` | Maximum item characters `P` | Measured maximum live values | Measured maximum logical payload characters |
|---:|---:|---:|---:|
| 1 | 20 | 2 | 40 |
| 7 | 163,840 | 8 | 1,310,720 |
| 64 | 2,560 | 65 | 166,400 |

Each row is verified at 65,536 and 1,048,576 input bytes (569 and 9,117 records), with respectively
five and 72 checkpoints, on .NET 8/10 in Release and Debug. All 24 TRX measurements show the same
maximum value/character counts between short and long inputs. Character counts describe logical
payload, not physical string allocation, array/object overhead or process RSS. An isolated source
mutation retaining previous accumulator values fails all six window cases on both frameworks;
restoring the source and rebuilding passes all six. Canonical native images remain unchanged.

These measurements cover fixed-size scalar state, the specified rolling-string program and tee
payload model. Explicitly retained copies, general growing/opaque/node-valued state, XML ancestor/header
metadata, parser buffers, reference-array
and object overhead, and buffered prong output need their own accounting. They do not replace the
full-lifetime managed-heap/RSS counters or the original sixteen-workload acceptance matrices. The
streamed merge also retains its current equal-key group, as the original design specifies; a bound
for that path must include the group size and snapshot payload rather than only one lookahead per input.

Evidence: `artifacts/p7-completion/state-bounds-final-{release,debug}` TRX files retain each measurement.
The isolated mutation/control and source hashes are in `artifacts/p7-completion/tee-payload-f8915f6`.
Rolling-window measurements, the initial shared-empty-sequence accounting failure, the retained-state
mutation/control and frozen engines are in `artifacts/p7-completion/window-state-bd89eae`.

### Merge groups, lookahead and explicit retained values

`StreamedMergeLifetimeTests` observes the actual compact snapshots and atomic keys through a function
parameter called by compiled merge-key and merge-action expressions. Execution capture verifies that
each external merge input actually uses a forward source. Every program runs through both APIs, with
normal/debug stylesheet compilation and, on .NET 10, saved assembly loading. The input fixtures and
weak-reference recorders are test instrumentation; these tests make no process-RSS claim.

Let `N` be the number of active inputs, `W` the rows contributed by each input to an equal-key group,
and `G = N * W`. At sampled action checkpoints without explicitly retained earlier results, live snapshot
documents stay within `G + N`: the current group and at most one lookahead per input. At most `N + 1`
observed atomic key values remain live. Row text/attribute characters and copied ancestor-attribute
characters are counted separately, bounded by the permitted snapshot count times the largest observed
per-snapshot character count. Names, namespaces, object/array overhead, parser buffers, source provenance
and the test fixture's own storage are not included in that logical-character measurement.

| Inputs `N` | Rows per input/group `W` | Row text characters | Ancestor depth | Maximum live snapshots | Maximum live observed keys |
|---:|---:|---:|---:|---:|---:|
| 2 | 1 | 32 | 1 | 4 | 3 |
| 3 | 7 | 256 | 1 | 24 | 4 |
| 2 | 3 | 8,192 | 8 | 8 | 3 |

Each shape runs for 16 and 128 groups, with two and sixteen collection checkpoints respectively.
Snapshot/key maxima remain unchanged between those input lengths. Logical character maxima can differ
because generated IDs and keys acquire another decimal digit; the bound uses the actual observed
values, not a fixed expected payload. Source order, within-source order, row contents and each copied
ancestor are checked against the generator's explicit inputs.

Another program retains the first two groups as explicit node sequences and compiled closures over
an explicit `$nodes` variable. Its bound includes those additional snapshots. All twelve retained
snapshot documents survive source closure, preserve identity/content when the closures run later,
and collect after their owner releases both sequences and closures. No implicit merge context is
captured: XSLT 3.0 [15.6.1](https://www.w3.org/TR/xslt-30/#func-current-merge-group) and
[15.6.2](https://www.w3.org/TR/xslt-30/#func-current-merge-key) require function values to have absent
current merge group/key. Six inline/named-reference regression forms check the specified
`XTDE3480` / `XTDE3510` errors and restoration of the surrounding merge action's context.

Cleanup cases cover full completion, explicit terminating messages, action and key failures, input
read failures, and cancellation during key/action processing. Merge-owned non-seekable streams close;
lent readers and the principal input stay open. Cancellation preserves the original token. Lent-reader
read failures follow the existing `FODC0002` projection; unwrapped stream failures retain their original
exception. All unretained observed snapshots and keys collect after execution returns.

A source-verified isolated mutation that retains completed groups, without changing their contents,
fails all 36 .NET 10 and 24 .NET 8 measurement cases. Restoring and rebuilding passes them all.
Raw measurements, controls, initial fixture failures, source restoration checks and tested-engine
checksums are retained in `artifacts/p7-completion/merge-state-ad659a9`.

A grounded variable containing the complete merge result can legitimately materialize that result;
applying `head()` afterward does not establish an early-termination contract for that variable. The
initial eager-result observation is retained. These contracts cover current-group/lookahead retention
and explicit termination, not arbitrary lazy merge-result consumption or the original RSS/throughput gates.

## Process-wide collection isolation

`StreamedAccumulatorLifetimeTests`, `StreamedMergeLifetimeTests` and `StreamTeePayloadTests`
force full process collections to measure retained state. They use the existing nonparallel
`Process-wide state` test collection alongside `ConstantPoolTests`. This keeps their collection
checkpoints apart from unrelated tests' cancellation deadlines while retaining every case,
retention assertion and original timeout.

A source-verified .NET 8 full-suite diagnostic records 2,927 induced collections and 77.353 seconds
of paired runtime suspension/resumption intervals. The failing 4 MiB legacy fork read-boundary
case overlaps 1,279 such intervals: 28.525 seconds of its 30.319-second wall time. The same frozen
engine passes 100 standalone repetitions of that case and 1,000 accumulator/fork repetitions.
The full-suite failures and stack/GC traces remain retained; isolated repetitions alone did not
establish a runtime remedy.

Evidence: `artifacts/p7-completion/fork-projection-79cb441/full-net8-gc-diagnostic/`, including
`gc-pause-audit.json`, the original trace and TRX; repaired current-branch suites are retained in
`artifacts/p7-completion/suite-gc-isolation-79cb441/`. Native memory and throughput qualification
continues under its original independent processes, inputs and limits.

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

## Partial-prefix reclamation before another read

`StreamTeeTests.AliasesReleaseAPartialPrefixBeforeAnyFurtherReadIsRequested` covers one and four
aliases of the same logical reader. Every alias reads 13 events, then all aliases stop at a barrier.
At each of 128 checkpoints, a 64-event tee must report exactly `produced - consumed` buffered events
before any alias can request another read. This checks an incomplete batch, not just EOF or a full-ring
refill. The existing exactly-once and payload-byte tests remain separate contracts.

Both cases pass against the unchanged production tee. An isolated mutation omitting fast-read prefix
reclamation fails both cases at their first checkpoint: expected occupancy 51 /12, observed 64 /64.
Restoring the production source passes both again. The mutation's actual engine/test PDBs match the
50 runtime source documents and changed test document; failed and restored runs remain retained in
`artifacts/p7-completion/tee-prefix-contract-ad6a998`. No timeout, payload assumption, capacity or runtime
policy changes. This evidence extends S6's ownership contracts without claiming the original process
memory or throughput gates have passed.

## Copied accumulator graphs and error ownership

`StreamedAccumulatorGraphLifetimeTests` uses an ordinary strict compiled stylesheet, both APIs and
.NET 10 saved loading, with normal/debug compilation. Its accumulator calls a test-supplied function
with grounded string/value operands. The function creates either a seven-item window of constructed
payload nodes, a seven-item window of opaque native functions capturing those nodes, or a map that
intentionally accumulates every processed row. Actual execution captures require forwarded input.

The 16/128-row cases exercise `xsl:copy-of`, `copy-of()`, `snapshot()`, a copy of a copy, explicit
annotation stripping and lax validation. A host either discards every copy or explicitly retains the
first two. Every before/after value, node payload and opaque callable identity is checked. A compiled
reader function checks the copied values after closing the original source, before invocation cleanup;
the recorded phases remain readable after the invocation returns. Releasing their owners collects all
observed payload documents, returned copies, map versions and opaque functions.

Let `W` be the window width and `R` the distinct payloads explicitly retained by the early copies.
At sampled callbacks the node/opaque windows retain at most `W + 1 + R` payload documents; the extra
item accounts for overlapping before/after windows. The growing map legitimately retains `N` payloads
after `N` rows, but at most two current map versions plus the explicitly retained earlier versions.
Logical payload characters count node text and its `id` attribute separately from map keys, object/array
overhead, name tables, parser buffers and fixture/probe storage. These are graph-lifetime measurements,
not heap/RSS acceptance or a constant-byte claim for arbitrary user-owned opaque graphs.

The first regression retains two early copies and observes 128 payload documents instead of two.
The fix stores corresponding recorded phases on the copy, with no source navigator or execution link.
Capture requires an immutable recorded subtree and corresponding content-node names, kinds and relative
depths. Copies made during an open enclosing replay use the deferred handoff described below.
Structurally changed copies keep the existing live provenance path; their complete lifetime
qualification remains required. Fork-copy and structural-mismatch guards are separate
regressions. Attribute/namespace nodes remain outside accumulator applicability.

Cycle invalidation uses a small shared notification token. Complete phases do not subscribe to future
ordinary errors or their optional error payloads. Missing phases preserve the error known when copied.
A later error carrying a 127-node map initially exposes another retention bug in the prototype: early
copies keep that unrelated payload. The corrected contract retains only their two requested payloads.
Conversely, explicitly retaining a copy with an unavailable end phase must retain and return its
127-node error payload, including its identity. Original error codes and applicability errors survive
source closure. This required error graph is intentional state, not an omitted memory cost.

The initial completed-copy candidate passes all four full suites: 5,285 .NET 10 /4,699 .NET 8 checks
in Release and Debug. Actual
engine/test DLLs and PDBs are frozen before execution; source hashes match 51 runtime /seven test
documents and remain unchanged afterward. An isolated mutation reconnecting snapshots to the source
fails all six selected API/debug/saved controls with 128 retained payloads instead of two; restoration
passes all six. Raw failures, source snapshots and engines are retained in
`artifacts/p7-completion/accumulator-graphs-138fcee`.

A separate NativeAOT development consumer extracts the exact tested stylesheet text and compiles twelve
saved styles through the public compiler. Its 72 cases independently use 32/256 rows, three-item
windows and 128-character payloads. All verify actual forwarded execution, values after closing the
source, two retained early payloads and collection after releasing their owners. It uses the existing
`Xsl3.Dev` friend assembly for graph observation, with trimming/AOT warnings treated as errors. Its
startup check rejects JIT hosting. These functional checks do not alter the 32 canonical native styles,
original process-memory budgets or the complete P7 throughput requirements.

## Copies made during an enclosing replay

A completed row can be copied while an explicitly grounded ancestor is still being recorded. On
`cddc819`, retaining only two early row copies then keeps all 16 /128 later payload documents even
after source closure. Two source-verified regressions preserve this original failure.

Such copies now temporarily own a deferred provenance handle. The replay registers only a weak
completion delegate, with dead-reader pruning; discarded copies can collect while the capture is
open. At capture end, the handle atomically receives the corresponding recorded phases and releases
its source/observer links. Reading the replay dictionary waits until it is immutable. Completion
publication does not hold the registration lock across callbacks, source I/O or user code.

A sealed interrupted capture remains incomplete and is not made replayable. Its already completed,
corresponding child subtrees can nevertheless transfer their recorded phases. Skipped or structurally
mismatched subtrees retain their previous provenance behavior. Tests close the source both before and
after capture disposal, stop before EOF, and exercise ordinary errors carrying later map payloads,
cycle invalidation and cancellation. Early copies keep only their requested payloads; releasing the
copies collects all observed graphs. An explicit outer grounding legitimately owns earlier source
phases while it is live; those cases do not assert the rolling-window bound during that grounding.

The compiled graph matrix adds 144 .NET 10 /96 .NET 8 cases through both APIs, normal/debug compilation
and supported saved loading. The interrupted fixture actually raises a caught early stop with unread
input; simply omitting a final read had reached EOF and is retained as a failed fixture control. The
same post-capture owner bounds apply to node windows, growing maps and opaque function graphs.
A separate strictly trimmed NativeAOT consumer passes 96 cases from sixteen saved styles with
independent 32/256-row inputs and actual forwarding. Its startup rejects JIT hosting.

All four full suites pass: 5,441 .NET 10 /4,807 .NET 8 checks in Release and Debug. Frozen DLL/PDB hashes
remain unchanged after execution, with 51 runtime /eight test source documents verified. All 1,313
tracked source/test files were compared with `cddc819`; only the five intended candidate files differ.
Omitting completion notification causes 11 retention failures /one discarded-copy pass; restoring the
reviewed source passes all twelve controls. Raw results, source snapshots and frozen engines remain
under `artifacts/p7-completion/accumulator-open-replay-cddc819`.

The unchanged original native push matrix validates all 48 outputs and retains 13/16 budget passes.
This ownership repair does not close process-memory, throughput, pending-copy validation/fork lifetime,
structurally changed copy applicability or the remaining full P7 qualification requirements.

## Pending copy chains, validation and fork views

The same `a2998a2` runtime passes additional contracts without production changes. Completed and
interrupted enclosing captures now exercise a copy of a pending copy and lax validation of a pending
copy. Both APIs, normal/debug compilation, supported saved loading, node/opaque windows, growing maps,
retained early owners and discarded copies retain the existing value/identity and collection checks.

Three additional forms validate against an imported schema that supplies default text to empty rows:
ordinary, completed enclosing replay and interrupted enclosing replay. The schema defines the added
content; tests verify the actual text node and its type annotation. The copied parent's before/after
values still come from its corresponding source node, consistent with
[XSLT 3.0 section 18.2.10](https://www.w3.org/TR/xslt-30/#copying-accumulators), rather than running its
accumulator rules again on the validated tree. These assertions concern the copied parent, not an
invented corresponding source for newly created text nodes.

One- and two-prong runtime cases consume 16/128 rows while each prong owns an enclosing replay.
Retaining its first two copies preserves values after source closure but only two distinct payload
graphs, shared across prongs. Releasing the copies collects every observed payload. Rules execute on
the original caller thread exactly once; the existing fork thread model and tee capacity are unchanged.

The new checks pass in all four Release/Debug configurations: 508 .NET 10 /340 .NET 8 checks per
configuration. Earlier full runtime verification remains 5,441 /4,807; these are additional focused
contracts, not a claim that a new full suite has run. Engine hashes and 51 runtime /two changed test
PDB documents are verified. Comparing all 1,314 tracked source/test files finds only the two intended
test edits. An isolated omitted-notification mutation causes 40 ownership failures, while 48 controls
still pass; restoring source passes all 88. Raw sources, failed controls and frozen engines are retained
under `artifacts/p7-completion/accumulator-pending-forms-a2998a2`.

A separate NativeAOT consumer passes 180 saved-style cases from thirty styles and four supplemental
runtime-level fork graph cases, using 32/256 rows. The saved styles come from the exact tested fixture;
inputs, window width and payload size remain independent. Startup rejects JIT hosting. The supplemental
manual metadata builder checks that early-bound types are absent and narrowly documents that
precondition for trimming analysis; production annotations and global trimming/AOT error policies are
unchanged. Source PDB checks cover 51 runtime /three consumer documents. This is functional ownership
qualification, not an absolute process-memory or throughput pass.

Arbitrary structurally mismatched provenance and accumulator applicability for schema-created nodes
without copied counterparts still need explicit disposition. These cases do not declare all S3–S6
contracts complete or change the original P7 acceptance gates.
