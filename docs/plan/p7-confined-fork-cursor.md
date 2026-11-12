# P7 private fork cursor reassessment

Branch `feature/p7-confined-fork-cursor` starts from integration `32c845d` and excludes all rejected
runtime controls. **Closed without adoption, 6 November 2026.** Full P7, its original gates and draft PR #40 remain open.

## Why reassess this control

The 1 November private-reader record reported lower fork elapsed time but higher native RSS when
the original push budget was already failing. Corrected discard now has audited passing original
memory matrices, providing a materially different footprint baseline. This justifies reassessing the
private-reader design; it does not reuse the old timing or memory result as current evidence.

The old artifact directory is absent. The original patch, tests and wakeup correction were recovered
from this chat's recorded tool calls of 1 November (09:44, 09:46 and 09:48 UTC). The two synchronization
files, `StreamTee.cs` and `ForkProngs.cs`, are byte-identical between historical base `c7da793` and
integration `32c845d`. The recovered adapter change is applied separately to the current fork source,
preserving corrected projection discard. Reconstruction commands and hashes are retained locally.

Do not repeat the different generic position-based or split-reclamation-lock experiments. Their
negative results remain in the completion record. This control keeps the existing generic protocol
and its concurrent-reader alias support; only the private fork-event adapter selects confined readers.

## Correctness finding and correction

The historical fast path published its cursor but deferred consumed-slot reclamation until another
read or producer request. All five recovered ownership/progress/source-fault cases still pass, but new
prefix-release controls demonstrate the gap: all four private variants fail in both .NET 8 and 10,
while the four generic variants pass. The cases use one/two readers and capacities one/seven, force
collection before another read, and check that the consumed payload is no longer retained by the tee.
The eight failures and their frozen binaries remain in the evidence root.

The corrected path releases consumed prefixes in `finally` before returning, including a predicate
exception. The existing monitor orders cursor publication with producer demand and handles full-ring
wakeups. It replaces the historical separate wakeup hint; no delayed cleanup or weaker retention
contract is accepted to obtain a faster result.

Private cursors bind to their first consuming thread. Another thread may detach them but cannot
consume them. Publication uses release stores for reused slots; readers acquire the slot before
rechecking attachment, preventing a detached reader from accepting a later ring epoch. Generic
atomic claims and remaining-reader counts are unchanged. Source/callback ownership, thread count,
ring capacity, input projection, cancellation and error ordering remain unchanged.

## Validation and decision boundary

Evidence root: `/private/tmp/xsl3-p7-confined-fork-cursor-20261106/`.

- The corrected candidate passes 624 .NET 10 /543 .NET 8 affected fork/tee cases in each of Release
  and Debug, 2,334 checks total.
- A concurrent-detachment contract additionally verifies all 10,000 original event numbers in the
  surviving reader across eight detach races. All 14 private-reader contracts pass on both frameworks
  and both build configurations with one, two and four effective CPUs: 168 checks.
- All historical failures, source snapshots, reconstruction records, frozen engine/test binaries and
  raw TRX are retained. The final detach test changes no runtime source.
- The canonical five-workload paired comparison is complete: unchanged consumer, three rounds,
  100,000,000-byte inputs, tree/stream modes, four effective CPUs, tiering off, normal ReadyToRun and
  an actual 4 GiB managed guard. All 60 raw outputs and their original qualification fingerprints verify.
  All retained binary hashes, unchanged consumer/dependencies and 409 tracked PDB sources per engine verify.

Only a useful measured improvement can justify a target-platform comparison. Any adopted version
must pass final-source original memory and throughput gates, all compatibility/semantic obligations,
review and release publication. Thread confinement and passing narrow tests alone establish none
of those outcomes.

## Audited outcome

The final audit checks all raw TRX rows and execution IDs, all 20 frozen PDBs (historical negative
control, affected candidate and final test-only addition), and every relevant source hash. The eight
historical failures are precisely the private prefix-release cases. The final test addition leaves
all four runtime binaries byte-identical to the affected-test and comparison engines.
Audit records: `validation-review.json`, `historical-contract-review.json`, `comparison-review.json`;
reproduction scripts are beside them in the evidence root.

| Workload | Baseline stream median ms | Candidate stream median ms | Candidate / baseline | Ranges overlap |
| --- | ---: | ---: | ---: | --- |
| identity | 2097.843 | 2106.006 | 1.003891 | Yes |
| accumulator | 2048.416 | 2049.168 | 1.000367 | Yes |
| merge | 3341.079 | 3313.955 | 0.991882 | Yes |
| fork | 1526.240 | 1562.392 | 1.023687 | No; every candidate slower |
| source-document | 2104.488 | 2095.879 | 0.995909 | Yes |

Fork baseline samples: 1526.240209, 1510.722083, 1527.513875;
candidate samples: 1554.850125, 1562.391708, 1566.871541 milliseconds.
The candidate fork stream/tree ratio is 1.026936. Every observation is retained.
The corrected ownership protocol has no measured benefit and does not justify target-platform
qualification. Do not revive the faster historical variant by weakening the consumed-prefix contract.

Baseline engine SHA-256: `4a454adc501c00eace78bb495d62182ea45979ecb3df9bc730fc70ab40bc43d1`.
Candidate: `09a885b529bf4a66c06a6ab6c3d14dea247d3f55fd15baa513b3fa99646ecf02`.
No Linux/native run, merge or release is claimed for this control. Integration remains `32c845d`.
