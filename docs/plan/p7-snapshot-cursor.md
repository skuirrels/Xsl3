# P7 single-cursor snapshot control

This isolated control starts at profiling head `1b2e3e4`, whose runtime is identical to integration
`402cbed`. It is **closed without adoption**. The unchanged canonical consumer and original P7
requirements are retained; no Linux timing comparison or full acceptance rerun is justified.

## Change and review

The [audited merge profile](p7-merge-snapshot-profile.md) motivated reducing navigation overhead
inside snapshot construction. `XsltLibrary.Snapshot.CopyChildren` uses one cloned navigator for
its subtree and returns to ancestors with `MoveToParent`, replacing recursive child cursors.
It emits the existing shallow element headers and full end-element events. Leaf handling,
ancestor reconstruction, typed/parentless paths, the writer implementation and provenance are
unchanged. Source consumption still uses child/sibling navigation; returning to a parent does not
read the input. The caller's original navigator is not moved.

The shared helper's callers were reviewed. `CopyWithoutNamespaces` handles elements and roots in
its separate path and delegates only other node kinds to `CopyDeep`; it does not start this new
element traversal. No fixture-derived runtime value, reader bypass or policy threshold is added.

## Correctness evidence

All **7,976** affected checks pass: **2,220 .NET 10 /1,768 .NET 8** per Release/Debug configuration,
with no failures/skips. This includes **80** new `SnapshotTraversalTests` cases. Independently
constructed LINQ-to-XML trees check mixed text/comment/PI/element content, inherited names and
attributes, whole documents, selected subtrees, deep/wide nesting (16 and 256 levels), and following
siblings. Public modern/legacy and .NET 10 saved styles run in both compiler modes. Message callbacks
verify exact end-element/depth/record-count positions after each streamed snapshot, with EOF controls
for tree execution. Reader ownership is also asserted.

Additional frozen-binary controls verify:

- **160 baseline passes:** the new cases under default and forced-compact settings in all four builds.
- **80 additional candidate passes:** the same cases under forced-compact settings.
- **80 baseline and 80 candidate failures** under `XSL3_COMPACT_TREE=0`. Every case fails with the
  same `InvalidOperationException: A generated copy must belong to a compact document` at
  `CopyOriginRegistry.Set`. These failures are retained and are not counted as passes.

The forced-noncompact failure occurs on the unchanged baseline before expected-shape assertions.
It is an existing diagnostic-policy/provenance incompatibility, deferred from this performance
control; it does not justify changing the traversal, altering expected output or waiving an original
gate. The initial stopped control run and all raw failures remain available. The continued audit
reuses those completed initial results and verifies exact candidate/baseline failure and case parity.
Successful traversal under that forced setting is not claimed.

Every affected TRX row/counter/execution ID, frozen engine/test binary and tracked source hash is
audited. Each engine PDB covers 409 tracked sources; each candidate test PDB covers 201. Four frozen
baseline PDBs independently match the original integration source. There are **8,056 positive
candidate checks** including the additional forced-compact controls, plus the separate baseline
controls and retained diagnostic failures. The initial 40-case smoke run is not added to these totals.

## Paired performance outcome

All **60** observations complete on .NET 10.0.14 /macOS 27.0.1 ARM64: five workloads, two execution
modes, three alternating rounds per runtime, exactly 100,000,000 input bytes, four reported processors,
the actual 4,294,967,296-byte managed guard, disabled tiering and normal ReadyToRun policy.
Both engines' 409 tracked PDB sources, frozen binaries, unchanged consumer/dependencies and all
original output fingerprints verify.

| Workload | Baseline stream median (ms) | Candidate stream median (ms) | Candidate / baseline | Winner |
|---|---:|---:|---:|---|
| Identity | 2,083.442916 | 2,083.292792 | 0.999928 | No meaningful winner |
| Accumulator | 2,030.316792 | 2,013.306916 | 0.991622 | No meaningful winner |
| Merge | 3,326.871334 | 3,268.373583 | 0.982417 | No meaningful winner |
| Fork | 1,520.123208 | 1,524.079041 | 1.002602 | No meaningful winner |
| Source-document | 2,106.823917 | 2,102.700958 | 0.998043 | No meaningful winner |

Every baseline/candidate timing range overlaps. Merge baseline samples are
`3261.413625, 3326.871334, 3346.415250` ms; candidate samples are
`3260.247208, 3268.373583, 3322.523875` ms. The lower median is insufficient to establish a reliable
benefit. Do not integrate this control or repeat it unchanged to obtain favorable measurements.

Baseline engine SHA-256: `4a454adc501c00eace78bb495d62182ea45979ecb3df9bc730fc70ab40bc43d1`.
Candidate engine SHA-256: `39d51ba1c5a0b37858ea83ce5b9b0be2d0210dcc1d8edf4afe10e2f7e6091ed3`.
All source/binary snapshots, raw observations, initial failures and executable audits are retained at
`/private/tmp/xsl3-p7-snapshot-cursor-20261106/`, including `validation-review.json`,
`semantic-controls-initial.json`, `semantic-controls-complete.json`, `semantic-controls-review.json`
and `comparison-review.json`.

## Next evidence boundary

The ordinary merge profile retains 7,568 transform-entry samples with unresolved leaf symbols.
Resolve the framework portion with the existing supplemental framework-JIT diagnostic before
selecting another control; its elapsed time cannot qualify throughput. Source inspection already
shows that replay buffers are conditional on a live overlapping descendant scan or accumulator
observer. Do not propose disabling unconditional replay: that optimization is already present.
The same profile contains only 100 inclusive `CopyOriginRegistry.Set` samples and 56 in
`BeginReplay`, so neither supports a large removable-cost claim.

Integration PR #40 remains unchanged at `402cbed`. All original memory, throughput, physical I/O,
compatibility, supported-claim, review/merge and publication requirements remain outstanding until
proved on final integrated source.
