# P7 merge CPU attribution and next control

The isolated `feature/p7-merge-snapshot-profile` starts from integration `402cbed`.
Its runtime, tests, benchmark consumer, controllers and workflows remain unchanged. The separate
[serialization annotation control](p7-serialization-annotations.md) is closed without adoption at
`be9ba9e`; only its record is carried here. Full Phase 7 remains active.

## Verified diagnostic

[Run 42160664076](https://github.com/skuirrels/Xsl3/actions/runs/42160664076) completed successfully
on 6 November 2026. Both the controller and benchmark source are
`402cbed8fe00f86b2e6fe5eb8a76bc7398130db3`. The canonical merge child processes exactly
1,000,000,000 input bytes with an actual 4,294,967,296-byte managed limit, four host processors and
disabled tiering. All six original 1 GB merge output controls match its record count and output
fingerprint. Its instrumented elapsed time and RSS are diagnostic values, not acceptance results.

The independent audit verifies:

- Artifact `13476487596`, expiring 4 February 2027, SHA-256
  `7cc882b27ce5a7e8140e31778fd4b6e5f323dc4fc26e664677a4b6438bd20d89`.
- Trace SHA-256 `901bcc9d3acbbbf7cc16c8a136aeb50aed5fc146d6938307e68acc1b3e5daf48`.
- All four retained engine/consumer DLL/PDB hashes; 409 tracked engine and 14 tracked consumer
  source hashes against the immutable Git revision. Generated PDB sources are excluded explicitly.
- All 25,253 kernel CPU samples belong to the child. There are zero lost events, 25,126 samples
  with an engine frame, 24,619 in transform-entry stacks and 634 unattributed.
- The controller reports 24 dependency binaries whose files are absent from the archive; those
  reported hashes are not independently verified.

The workflow requests no ReadyToRun override. This controller version does not record inherited
ReadyToRun environment variables, so the audit does not claim independent verification of their
values. The 30-second collection is a window within a longer child run; it can include warmup and
does not cover every operation or define removable elapsed-time shares. Unresolved framework leaves
remain visible.

## Snapshot attribution

`X31Snapshot` appears in 10,048 inclusive samples. A diagnostic reporter retaining every stack
frame separates those samples using these ordered, mutually exclusive rules:

| Snapshot stack classification | Samples |
|---|---:|
| Contains `StreamedInputReader.Read` | 1,932 |
| Otherwise contains `StreamedDocument.ReadNext*` | 2,521 |
| Otherwise contains a `CompactDocument.Builder` method | 1,971 |
| Other snapshot work | 3,624 |
| Total | 10,048 |

Reading/parsing is therefore part of the snapshot count. Do not present all 10,048 samples as
removable copying overhead. The remaining stacks include snapshot traversal, navigator cloning,
name/namespace access, writer calls, allocation and GC barriers. In the final category, resolved
leaves include `WriteStartShallow` (153), `CopyDeep` (137) and `StreamedNavigator.Clone` (128).
The grouping preserves every original site count, inclusive count, region count and event count.

The fuller reporter changes only diagnostic display grouping. Its source SHA-256 is
`7f5e67785070efd4bad9cd99ad9968b4cc111f895e7850a3680cf12a57a27e7a`.
The local build succeeds using pinned TraceEvent 4.0.0; its log retains a vulnerability-feed lookup
warning from the restricted network. No engine or gate is rebuilt for this analysis.

Evidence, source manifests, binaries, metadata, raw child output, full stacks, reporter source/build
log and auditors are retained under `/private/tmp/xsl3-p7-merge-cpu-20261106/`, particularly
`review.json`, `verified-source-hashes.json` and `full-stack-conservation.json`.

## Original bounded implementation (closed below)

Test a single-cursor snapshot subtree walk that reduces per-node navigator clones while retaining
the existing writer events and shallow ancestor reconstruction. This was the proposed control; the completed outcome is recorded below. Inspect the shared `CopyChildren` callers, including copy-without-namespaces,
before deciding whether the optimization belongs in a shared helper or a snapshot-only path.

Completion criteria for the control:

1. Preserve subtree contents, shallow ancestors, attribute/namespace positions, base/document URIs,
   XML language and space, node kinds and independent copied identity. Keep existing typed and
   parentless copy paths and the compact-tree override behavior.
2. Preserve accumulator/copy provenance, replay, retained snapshots/closures, cancellation, errors,
   ownership and exact physical read positions. Do not bypass the navigator or validating reader to
   obtain speed. Review recursive traversal and ancestor restoration with arbitrary-depth inputs.
3. Pass affected tree/forward, public API and saved-style contracts in Release/Debug on .NET 8/10;
   add independent mixed-content/deep-tree controls where existing coverage does not establish the
   changed traversal invariant. Freeze source and binaries and audit the actual results.
4. Compare the unchanged canonical consumer in all 60 paired observations and verify original
   fingerprints, source identity and actual resource configuration. A useful local result requires
   a target-platform comparison; allocation reduction alone is insufficient for adoption.
5. Record the outcome and close the control. Reject it if it produces no reliable timing benefit or
   cannot preserve the contracts. Do not repeat unchanged measurements to obtain favorable results.

This control cannot close P7 by itself. Identity, merge, fork and source-document remain the four
failed original throughput targets. Final integrated source still needs both original memory
matrices, all 126 original throughput observations with seven passing 100 MB targets, complete
compatibility/physical-I/O qualification, supported claims, review/merge and matching publication.

## Subsequent control and supplemental diagnostic

The [single-cursor control](p7-snapshot-cursor.md) is now closed at `6eb42dd` without adoption.
Its affected/default and forced-compact contracts pass, with explicit baseline/candidate failures
under forced noncompact mode. All 60 observations verify, but every timing range overlaps.
This profiling branch carries only that record; its runtime/tests remain identical to integration.

[Framework-symbol diagnostic 42163291085](https://github.com/skuirrels/Xsl3/actions/runs/42163291085)
completed successfully against unchanged source `402cbed`, using controller
`5db06fbcd023302e70fb06bfb5fc354478c10403`. It explicitly disables ReadyToRun to improve
framework symbol resolution. The altered code-generation policy makes its elapsed time unsuitable
for throughput acceptance.

Independent audit verifies all six original 1 GB merge output controls, the actual 4 GiB heap and
four host CPUs, disabled tiering, all four retained engine/consumer binary hashes, and 409 engine
plus 14 consumer tracked source checksums. The 24 unretained dependency hashes remain unverified.
The profile contains 24,395 CPU samples, 24,130 with engine frames, and zero reported lost events.
The full-stack report preserves every original sample, inclusive count and event count.

Artifact `13478121304` expires on 2027-02-04. ZIP SHA-256:
`321f7a08cf3f0d3a5e3be82c49d688be9eb8cf65a3ed6ad741b82fb6c908785c`. Trace SHA-256:
`a8c8f3c9cbe42cd2423046a47236b188c725f014bbeeb245494574a2a2b13088`. Auditors and raw evidence are retained under
`/private/tmp/xsl3-p7-merge-framework-20261106/`, including `review.json`,
`verified-source-hashes.json` and `full-stack-conservation.json`.

Immutable-list construction appears in 581 inclusive samples: 302 nearest `X31MergeNextGroup`,
277 nearest `X31MergeRowNew` and two elsewhere. This supports inspecting per-group construction;
it does not establish a removable elapsed-time percentage. The next distinct control will defer
named-source group materialization using the merge's existing stable source order, preserving
public helper signatures, saved styles, named-group contents and retained node identity. Its
adoption still requires contract checks and the unchanged canonical paired measurements.
