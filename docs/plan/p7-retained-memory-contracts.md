# P7 S3-04: retained managed-byte contract

This increment on `feature/p7-retained-memory-contracts` follows [group read positions](p7-group-read-positions.md).
The existing navigator tests count open frames but cannot establish a byte bound: a frame or an external
reference can retain a large graph. The new isolated process measurement checks live managed bytes while
traversing the canonical generated input. No runtime code or original acceptance budget changes.

## Measurement and controls

`StreamedNavigatorMemoryTests` starts a fresh child of the test assembly with the original P7 push input
size (209,715,200 bytes) and managed heap guard (20,971,520 bytes). It uses the existing generated corpus;
there is no materialized input string, retained output, or new sample-only data path. The navigator reads
every row, checks each ordinal and text value, and verifies the final record and input-byte counts.

At 0, 100, 1,000, 10,000, 100,000, 1,000,000 and all records, the child forces collection and records
`GC.GetTotalMemory`, actual `TotalAvailableMemoryBytes`, read position and retained frame count. The
report is written to xUnit output, so ordinary CI's TRX artifacts retain the samples. Tests run in the existing
nonparallel process-state collection; output/error pipes are drained concurrently and the child has a deadline.

The negative control deliberately keeps each past navigator clone. Its heap guard is larger **only so it can
report a measured violation instead of crashing**; its acceptance bound remains 20,971,520 bytes. The child
must report failure when live bytes exceed that bound, even though the source's frame counter stays small.
This is a diagnostic control, never an accepted memory-gate observation or a runtime fallback.

The bound applies to the canonical fixed-depth, fixed-vocabulary, bounded-record-payload input without
caller-retained past positions. Arbitrarily large attributes/text, growing XML name vocabularies, caller-owned
reader caches, explicit grounding and retained navigator clones have their own storage costs. The earlier
depth/frame tests and the NativeAOT whole-process memory matrix remain complementary evidence.
Live managed bytes here are neither peak process RSS nor an allocation count; the original absolute RSS
and actual heap-guard gates on all sixteen workloads at both sizes remain required on final reviewed source.

## Local validation, 5 November 2026

| Configuration/framework | Largest sampled live managed bytes | Records completed | Negative-control live bytes at 100,000 records |
|---|---:|---:|---:|
| Release .NET 10 | 318,880 | 1,823,610 | 107,565,184 |
| Release .NET 8 | 363,136 | 1,823,610 | 105,643,144 |
| Debug .NET 10 | 311,544 | 1,823,610 | 107,370,944 |
| Debug .NET 8 | 369,184 | 1,823,610 | 102,093,760 |

All positive children observed the actual 20,971,520-byte managed guard and consumed exactly 209,715,200
bytes. The negative controls returned the expected budget-failure status, with only three source-owned
frames still counted. All raw samples are retained in the TRX files under
`/private/tmp/xsl3-p7-retained-memory-20261105/`; this temporary path is not durable hosted evidence.

Release: four new checks passed across both frameworks. Debug: 104 checks passed across both frameworks,
including all navigator regressions and the four new checks. The initial probe walker treated the corpus's
trailing whitespace padding as another record; it was corrected to traverse element siblings. That failed
run is preserved under `release/`, followed by successful `release-final/` and `debug/` runs. No library
behavior or input bytes changed. Hosted CI is pending for this increment.

Next full-P7 work includes internal-grounding detection, fork grouping/sorting retention, the five unmet
throughput targets, final full-source qualification and supported-streaming integration and release.
