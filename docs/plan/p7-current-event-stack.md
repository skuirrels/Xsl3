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
The serial qualification is terminal and successful (session `75871`): all **15,834 affected checks**
pass, 4,344 .NET 10 /3,573 .NET 8 in each configuration, with no failures/skips. All eight instances
of the new retry cases pass. Every TRX row and unique execution ID, frozen binary hash and all
409 runtime /200 test tracked PDB source checksums per configuration/framework verify. The same
source audit verifies the baseline/withdrawn retry controls; all four withdrawn failures occur at
the attempted sibling retry. No tests or source changed during corrected qualification.

The withdrawn session `60454` is terminal (exit 143, deliberately stopped after the review finding);
the focused corrected and baseline/withdrawn control runs are also terminal. `qualify.py`,
`validation-review.json`, `retry-source-review.json` and `comparison-review.json` retain the evidence.

## Local paired result

All **60 observations** validate through the original controller and independent audit, including
original hosted output fingerprints, raw stdout, actual 4 GiB guards, four processors, disabled tiering,
unchanged consumer/dependencies and 409 tracked PDB sources per engine. Runtime: .NET 10.0.14,
macOS 27.0.1 ARM64. The measured runtime source is `1ef7c32`.

| Workload | Baseline stream median ms | Candidate stream median ms | Candidate / baseline | Winner |
|---|---:|---:|---:|---|
| Identity | 2130.641 | 2117.210 | 0.993696 | Candidate in this local cohort; 0.63% |
| Accumulator | 2067.166 | 2091.647 | 1.011842 | No meaningful winner; ranges overlap |
| Merge | 3220.539 | 3216.855 | 0.998856 | No meaningful winner; ranges overlap |
| Fork | 1521.847 | 1524.016 | 1.001425 | No meaningful winner; ranges overlap |
| Source-document | 2117.592 | 2118.635 | 1.000492 | No meaningful winner; ranges overlap |

Identity baseline samples are `[2130.640958, 2140.7335, 2124.620667]` ms; candidate samples are
`[2117.20975, 2122.25625, 2116.900458]` ms. Every candidate observation is below every baseline
observation. Accumulator's higher median is retained explicitly, not dismissed because this control
targets source reading. Candidate stream/tree ratios still fail locally for identity (1.062414),
fork (1.023316) and source-document (1.058327).

Engine SHA-256: baseline `e0c2e9fa1fefeee5bd02e0ca0964b63b1dc36c53b48a7e1337aebbc1346807bb`;
candidate `74a0ebaa553c017716bb1c6b8f6e069a3f0b8664d8c02f0db21e2947119130af`.

The small separated identity result and the original Linux instruction evidence justify one paired
Linux comparison of all five workloads. They do not justify integration or a full original-gate retry.
No supported-streaming or release claim is established; all original Phase 7 requirements remain.

## Linux paired result

[Run 42192860169](https://github.com/skuirrels/Xsl3/actions/runs/42192860169) completed successfully
on `7e8f842471f9c70d681db0bdace9caa292e99f2d` (runtime/test source `1ef7c32`), against unchanged
baseline `a4f730130728480748112af547aa999426bcfbff`. Independent review verifies all **60 observations**,
exact inputs, original fingerprints, raw output/report agreement, actual 4 GiB guards, four effective
processors, disabled tiering, frozen binaries/dependencies and all **409 runtime sources per variant**
plus **14 canonical consumer PDB sources**. No observation is excluded.

| Workload | Linux baseline stream median ms | Candidate stream median ms | Candidate / baseline | Winner |
|---|---:|---:|---:|---|
| Identity | 2950.392 | 2832.727 | 0.960119 | Candidate in this cohort; 3.99% |
| Accumulator | 2878.774 | 2767.944 | 0.961501 | No meaningful winner; ranges overlap |
| Merge | 4569.274 | 4526.194 | 0.990572 | No meaningful winner; ranges overlap |
| Fork | 2302.998 | 2161.891 | 0.938729 | No meaningful winner; ranges overlap |
| Source-document | 2854.644 | 2869.600 | 1.005239 | No meaningful winner; ranges overlap |

Identity baseline samples are `[2957.824053, 2950.392057, 2869.199818]` ms; candidate samples are
`[2795.405997, 2866.004929, 2832.727396]` ms. All three candidate samples are lower than all three
baseline samples. Source-document's 0.52% higher median remains explicit. The local accumulator
increase does not persist in this cohort; its Linux ranges still overlap, so no accumulator gain is
claimed. Candidate stream/tree ratios remain above one for identity **1.072698**, fork **1.016207**
and source-document **1.092342**. This comparison does not pass the full original throughput gate.

**Decision:** the repeated separated identity improvement justifies integrating this small runtime
change and its retry contracts for full compatibility qualification. Keep PR #40 draft. Full units,
reviewed-head hosted CI and every original final-source acceptance requirement remain necessary;
neither a supported-streaming claim nor release readiness follows from this result.

Artifact **13491415280** expires 4 February 2027. Verified ZIP SHA-256:
`98e0240c704d0eef824832f8ccd40fbd74ffa654845e038645a765b33c1a5edd`.
Engine hashes: baseline `36202f1aba6aae933c773f26d2e4dae59aebf4288c85849e3eb9a3c9f52010cf`;
candidate `efb6a094b9a59fa2d58d18a3d5dc2ef08e7f5f3fb3f6bd33df0dcf3a35ddce76`.
Evidence directory: `/private/tmp/xsl3-p7-current-event-stack-linux-20261106/`, including the raw archive,
reports, both binary sets, source manifests, `audit.py`, `audit-consumer.py`, `review.json` and
`consumer-review.json`. The run and all local qualification processes are terminal.
