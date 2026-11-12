# P7 source-production overlap control — rejected

This control starts from merged runtime `a5e89ac`, with diagnostic tooling through `c1787cc`.
When consumers release at least half of the ring, it requests a caller-thread refill while they
consume the published tail. Staged plus published events remain within the configured capacity;
source ownership, prefix-before-error delivery and cancellation mechanics are unchanged.

The first 899 affected Release checks passed. After adding deferred-error coverage and removing a
duplicate notification, 473 .NET 10 / 432 .NET 8 checks passed. Twelve paired local macOS ARM64
100 MB observations, four effective processors, disabled tiering and the actual 4 GiB heap guard,
all validated their independent output fingerprints:

| Variant | Tree median (ms) | Streaming median (ms) | Streaming observations (ms) |
|---|---:|---:|---|
| Merged baseline | 1,488.302 | 1,833.736 | 1,860.528; 1,829.443; 1,833.736 |
| Earlier refill | 1,481.426 | 1,474.116 | 1,474.116; 1,492.175; 1,458.561 |

The local elapsed improvement is about 19.6%, but **the control violates the demand contract**.
The streaming plan's S6 lifecycle states that the invoking-thread pump does not read ahead when
no prong requests input. Capacity becoming free after completed requests is not new demand.
`CallerOwnedSourceDoesNotReadAheadAfterBufferedRequestsComplete` exposes this at capacities three,
four and seven: all three cases fail on the control because `ProduceRequested()` performs source I/O
after both readers have returned and neither has requested another item.

The runtime change is rejected and must not be merged, despite its timing result. Preserve it only
on `feature/p7-fork-production-overlap`. No hosted or NativeAOT acceptance pass is claimed. Preserve
the new no-read-ahead regression on the unchanged runtime; the six experimental overlap assertions
describe the rejected behavior and must not be promoted with it.

Raw TRX (including the deliberate three-case failure), binaries, hashes, paired controller and all
observations are retained under `/private/tmp/xsl3-p7-fork-overlap-20261105/`. The existing preserved
baseline is `/private/tmp/xsl3-p7-fork-profile-20261105/baseline/`.
