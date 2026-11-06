# P7 current-event stack cleanup control

This isolated control starts at diagnostic documentation head `2622fc4`; its runtime baseline is
`a4f730130728480748112af547aa999426bcfbff`. The [completed instruction capture](p7-source-instructions.md)
identifies the repeated open-stack count/top/end check as the largest sampled native block within
`ReadNextCore` (213 direct leaf samples). This motivates measurement, not a predicted speedup.

## Change and invariant

The current source event already identifies an element whose frame has just ended. Check that
event's kind and end flag before accessing the stack, then remove the top at the same next-demand
boundary as before. Keep the existing stack, root-count guard and stack-top end check. A Debug assertion verifies that
the frame being removed is the current element. No cached state, early removal, parser lookahead,
buffer policy or observer-order change is introduced.

The reviewed transitions are:

- Ordinary element starts push the current frame; empty starts also mark that frame ended.
- Ordinary end events and fork end events mark the stack top ended and make it current.
- A direct subtree skip requires the selected frame to be current. Grounding finishes through the
  navigator and restores that same completed frame as current.
- Projected discard leaves the frame in place until completion, then marks it ended and current.
  Its continuation still precedes stack cleanup.
- Text, comments and processing instructions leave their parent open. Text lookahead can update
  parent schema information but does not mark the parent ended.
- Replay/header-only documents retain their existing position guards and fork ownership. Ended
  document roots are rejected before cleanup, as before.

The previously rejected linked-ancestor replacement and `ReadNextCore` method extraction are excluded.
This runtime control changes only `StreamedDocument.cs`; canonical consumers, inputs, output oracles and
acceptance thresholds are unchanged.

## Reader-error review correction

The first candidate omitted the original stack-top end check. Review identified an error path:
after popping the previous element, a reader can throw before updating the current node. A retry
must not pop the still-open parent merely because the old current element is ended. That candidate's
validation was deliberately stopped before timing and retained under
`/private/tmp/xsl3-p7-current-event-stack-20261106/`; it is not qualified or adopted.

Two focused cases inject a read failure after an empty-element event or an explicit end-element
event, then retry and verify subsequent sibling/parent navigation. The exact same test binaries
pass both cases on the baseline and fail both on the withdrawn runtime on each framework (four
passes /four expected failures). The corrected runtime passes all four focused checks. The final
condition keeps the original stack-top end check behind the new current-event condition, preserving
this error-recovery behavior. These fixtures are test-only; no input recognition is added to runtime.

## Qualification and decision

Run existing streamed/streaming, schema-aware and accumulator contracts in Release and Debug on
.NET 8 and 10, with warnings treated as errors. Audit every TRX result and the frozen engine/test
PDB source checksums. These suites exercise source navigation, skips, copies, observation, replay,
schema/namespace handling, fork projection, cancellation, reader boundaries and retained memory.

Then compare immutable baseline/candidate engines using the same canonical consumer, exact 100 MB
inputs, three alternating rounds, both modes, original output validation, four effective processors,
disabled tiering and an actual 4 GiB managed-heap guard. Include identity, accumulator, merge, fork
and source-document (60 observations). Preserve all samples and reject inconclusive or regressing
results. Full compatibility and original hosted gates are required before final integration; a
targeted diagnostic cannot replace either memory matrix or the 126-observation throughput gate.

Corrected evidence directory: `/private/tmp/xsl3-p7-current-event-stack-corrected-20261106/`.
Qualification is in progress; the two new retry cases are included in the affected suites.
The corrected .NET 10 Release suite has passed all 4,344 selected checks, with no failures/skips.
The serial qualification process continues with .NET 8 Release, both Debug suites, source/binary
audits and then the 60-observation comparison. Process session `75871` is live; do not start a
second local qualification or timing run while it is active. Its orchestrator is `qualify.py` in
the corrected evidence directory. The withdrawn session `60454` is terminal (exit 143, deliberately
stopped after the review finding); the focused corrected and baseline/withdrawn control runs are terminal.
No adoption, performance improvement, supported-streaming or release claim is established.
