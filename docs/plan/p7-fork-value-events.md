# P7 fork string-value event control

Branch: `feature/p7-fork-value-events`, based on integrated head `6c24a92`.
This isolated control is **closed without adoption**. It is not part of draft PR #40 and is not
approved for integration or release.
All original P7 requirements remain in force.

## Fresh allocation evidence

A new `gc-verbose` trace runs the unchanged canonical 1,000,000,000-byte fork consumer on
`6c24a92`: .NET 10.0.14, macOS ARM64, four effective processors, tiering disabled, ordinary
ReadyToRun policy and an actual 4,294,967,296-byte managed heap guard. All 8,695,652 records
and three output fingerprints match all six retained tree/stream observations at that size from
the original corrected-discard qualification. The trace reports zero lost events. All 1,400 captured
source hashes match the immutable revision; all 409 tracked runtime PDB sources match.

Sampled allocations total 23,914,204,088 bytes. These estimates include startup, warmup and the
output oracle; they are not exact per-type totals, timed-phase measurements or acceptance results.

| Allocation site | Sampled bytes |
|---|---:|
| Source `Child` frames | 5,208,257,784 |
| Fork-view `CreateFresh` frames through `Next` | 3,268,139,776 |
| Fork-view frames through `FirstChild` | 1,822,793,432 |
| Fork-view frames through `StringValue` | 1,822,792,800 |
| `ForkEvent` | 3,359,672,976 |

The corrected discard changes the attribution from the older producer investigation: current
view-frame allocations exceed producer-frame allocations. Allocation reduction alone still does
not justify adoption. Earlier producer, simple-content state and scalar-cast controls did not
establish sufficient throughput benefit.

Evidence: `/private/tmp/xsl3-p7-integrated-allocation-20261106/` contains the immutable binaries,
source manifest, raw output/trace, parser source, allocation report and executable audit. Trace
SHA-256: `4844e54a632ebb8daa5a176b23f9fba01928aa6fb8a19d66183c340c937fb9ed`.
Engine SHA-256: `4a454adc501c00eace78bb495d62182ea45979ecb3df9bc730fc70ab40bc43d1`.
The initial sandbox-denied socket attempt is retained separately and ran no workload.

## Isolated implementation

An unopened element's string-value consumer reads normalized fork events directly, retaining its
element header, logical numbering, first-child position and closing schema annotation. It avoids
creating descendant navigation frames only when no observer, replay, consumption proof, header
observer, descendant cursor or value consumer requires them. Peeked elements and ordinary readers
retain the existing path. Each event uses the ordinary tee read/reclamation protocol and checks
the individual prong's cancellation. No source demand, buffer capacity, thread policy or public ABI changes.

The earlier October string-value batching prototype was rejected after mixed one/two-CPU native
results. This control does not append text inside a locked or retryable predicate: it retains one
ordinary read per event and measures the current four-CPU corrected-discard foundation. Do not
adopt it merely because the older implementation or its tests were correct.

## Evidence and completion boundary

All 1,852 selected contracts pass: 502 .NET 10 /424 .NET 8 in each of Release and Debug, including
all 57 existing fork-value contracts in every configuration. Individual TRX outcomes, source hashes
and frozen engine/test binaries were audited. A sandbox-denied VSTest attempt ran zero tests and
is retained separately. Evidence: `/private/tmp/xsl3-p7-fork-value-events-20261106/`.

All 60 paired five-workload observations validate. The independent audit verifies the unchanged
consumer, all frozen binary hashes, 409 tracked runtime PDB source hashes per variant, original
fingerprints, exact inputs, four effective processors and actual 4 GiB heap guards. Every workload
has three tree and three streaming observations per variant, with alternating variant order.

| Workload | Baseline stream median ms | Candidate stream median ms | Candidate / baseline | Winner |
|---|---:|---:|---:|---|
| Identity | 2,111.941 | 2,103.214 | 0.995868 | No meaningful winner |
| Accumulator | 2,039.533 | 2,045.884 | 1.003114 | No meaningful winner |
| Merge | 3,356.804 | 3,358.520 | 1.000511 | No meaningful winner |
| Fork | 1,513.553 | 1,494.983 | 0.987731 | No meaningful winner |
| Source-document | 2,096.092 | 2,114.669 | 1.008862 | No meaningful winner |

All streaming ranges overlap. Fork baseline samples are 1,524.134 /1,513.185 /1,513.553 ms;
candidate samples are 1,494.983 /1,527.864 /1,490.065 ms. Its 1.23% lower median accompanies a
0.86% lower tree-control median; candidate stream/tree remains 1.018400. This does not establish
a useful gain sufficient to promote the extra runtime path. No Linux/full qualification rerun is
warranted for this control on these results.

A separate 1 GB allocation capture verifies that the implementation was active: the previous
`StringValue` descendant-frame allocation site has no sampled ticks. Total sampled allocations
are 22,280,313,608 bytes versus 23,914,204,088 baseline; zero events are lost, all output fingerprints
match and the actual heap guard remains 4 GiB. Allocation sampling redistributes type/site weights;
these totals do not establish exact savings or an elapsed-time gain. Candidate trace SHA-256:
`86544ed9d0115c92144f93588727a7d83ec71e38e992a25489842eff65710ae4`.
Candidate engine SHA-256: `1a7f904c0beae7ce776bfa49286726d7d79bd1e960a977cd41617e21ed044268`.
Both traces use the retained parser, which handles managed and kernel method-name formats; replaying
the baseline preserves every original type total. The raw trace, parser hashes and audit are in
`profile/`; `comparison-review.json` retains all paired observations and source verification.

The control remains only on this branch. Resume from integrated head `6c24a92`, retaining this
negative result so frame allocation alone does not trigger another equivalent experiment. Final-source
compatibility, both original memory matrices, all 126 throughput observations and every original
seven-workload target remain required, then review/CI/merge and matching NuGet/GitHub publication.
No result here completes P7.
