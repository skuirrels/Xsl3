# P7 simple-content value-state investigation

Branch: `feature/p7-simple-content-value-state`, based on contract head `78815e8`.
This experiment excludes the unaccepted compact producer and tee reclamation controls. The full P7
objective and every original acceptance requirement remain active.

## Measured premise and implementation

The original 1 GB fork allocation trace attributed 1,872,555,472 sampled bytes to `SimpleContentState`
(5.96% of total sampled allocations; estimates, not exact per-type object counts). See the preserved
[producer investigation](p7-producer-allocation.md) for the trace, source and sampling limitations.

The streamed text-constructor emitter creates one `FunctionItem` state object before evaluating its
items. The candidate instead stores a `SimpleContentWriter` value in a generated IL local and calls
its methods by reference. This follows the existing runtime iterator pattern. The state machine is
shared with the shipped `X31SimpleContentNew/Accept/End` API, whose wrapper remains available to
previously saved stylesheets. The new helper type is public for saved-assembly access and hidden from
normal editor browsing. No consumer feature switch or benchmark-specific behavior is added.

Initialization still precedes item evaluation. Separator evaluation, empty text-node creation,
adjacent-text merging, array and schema atomization, raw output, cancellation checkpoints, terminal
error identity and text-constructor completion retain their existing order. Reinitialization clears
the previous constructor's flags and fault. Tests exercise both the shipped wrapper and value state
against the canonical simple-content function, including reuse after success and failure.

## Qualification

Affected Release and Debug checks each passed: 356 .NET 10 and 317 .NET 8 (1,346 total), including saved streamed simple content,
early output, rollback, schema/list values, value replay, lowering and fork cases. Evidence is under
`/private/tmp/xsl3-p7-simple-content-state-20261105/`; the original baseline binary remains in
`/private/tmp/xsl3-p7-fork-profile-20261105/baseline/`.

An allocation decrease alone does not prove throughput or retained-memory acceptance. Qualification
must preserve the exact original workload inputs, independent output oracles, full observations,
actual heap guards and absolute memory limits. Full compatibility, both APIs, saved/native consumers,
the remaining runtime evidence and the supported-streaming claim/release are still required.

## Paired local result: not promoted

All 36 observations passed independent output, exact input, actual 4 GiB heap-guard and process-exit
checks. Each mode/variant/workload has three observations, with alternating variant order. The
candidate runtime SHA-256 is `f5e871a1234814474217e6b86bc44ea939dd27325fd46261989739e6e8ba44dc`.
The precise source snapshots/hashes, binaries and raw controller reports are retained beside
`paired-review.json` and `comparison-manifest.json`.

| Workload | Baseline tree / stream median ms | Value-state tree / stream median ms | Stream change |
|---|---:|---:|---:|
| Fork | 1,621.087 / 2,214.729 | 1,586.286 / 2,194.669 | 0.91% faster |
| Group-adjacent | 1,483.275 / 1,078.034 | 1,457.965 / 1,087.522 | 0.88% slower |
| Merge | 4,170.530 / 3,677.648 | 4,085.847 / 3,590.467 | 2.37% faster |

No convincing throughput improvement is established: fork and grouping ranges overlap, while merge's
improvement accompanies a similar tree-control shift. Fork remains slower than tree-then-transform.
These macOS ARM64 observations are diagnostic, not the original full hosted seven-workload gate.
The candidate is **unaccepted and not promoted to a PR**. Native/hosted qualification has not been run
for this control; do not mistake passing correctness checks for approval to adopt its new runtime API.
Next separate managed execution from thread waiting in the baseline profile before choosing another
runtime change. The producer and this output-state experiment both reduced allocation mechanisms
without establishing a useful elapsed-time improvement.
