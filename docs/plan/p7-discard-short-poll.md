# P7 bounded polling on corrected discard

This isolated control starts from the corrected-discard runtime `a8d322a`, with the paired diagnostic
controller and evidence on `feature/p7-discard-host-comparison`. That foundation passes both original
memory matrices and shows a 14.4% lower fork median than its 256-event predecessor in the audited
[same-host comparison](https://github.com/skuirrels/Xsl3/actions/runs/42131056101). The original full
qualification still fails four throughput targets; its results are not replaced by that diagnostic.

## Reason for this control

The source-pinned [Linux CPU capture](https://github.com/skuirrels/Xsl3/actions/runs/42129815135)
records 71,109 kernel CPU samples, zero lost events, 11,254 `ThreadNative_SpinWait` leaves and substantial
scheduler/yield work. Most spin leaves lack an unwound engine caller; resolved scheduler/yield stacks
include publication waits and the caller pump. This identifies waiting policy as a candidate for a
controlled comparison, not a removable percentage of elapsed time.

The earlier `81e2abd` short-poll control showed a 7.3% paired hosted fork reduction on its older source,
but remained isolated while its memory/throughput qualification was incomplete. A later startup
composition showed a smaller, overlapping gain. Those results are historical and are not inherited
here. The now-passing corrected-discard memory foundation and its reduced producer work justify one
fresh composed comparison; they do not prove that polling will help or stay within budget.

`225260f` reapplies only the five-line `ForkCaller.SpinBudget` change. It checks publication/completion
between single processor waits instead of the BCL's growing backoff. The existing one-millisecond
deadline, spare-processor eligibility, protected-state recheck and monitor parking remain. No source
work becomes speculative, no consumed payload is retained longer, and the default capacity remains 256.
The polling unit is an implementation operation count, not a workload-specific value or a new budget.

## Completion criteria

1. Existing fork, caller/tee concurrency, source ownership, read-boundary, cancellation, schema,
   replay, identity and retained-memory contracts pass in Release/Debug on .NET 8/10.
2. Low-processor controls retain the existing parking behavior. Tests and performance comparisons
   run serially locally; frozen variants use the same canonical consumer and output oracles.
3. A paired comparison includes identity, accumulator, merge, fork and source-document, both modes,
   three alternating rounds, exact 100 MB, actual 4 GiB guards and normal framework compilation.
   Local results alone cannot establish a hosted benefit.
4. Only a useful composed improvement with preserved contracts proceeds to original hosted memory
   and all 126 throughput observations. Every original target remains required on final integrated
   source, together with full compatibility, public integration, review, CI and release.

## Contract validation

The affected Release and Debug suites each pass **1,609 .NET 10 /1,325 .NET 8** cases:
**5,868 checks**, with zero failures or skips. The independent TRX audit verifies every individual
result against its counters and retains source and result-file hashes in `contract-review.json`.
The only runtime difference from `a8d322a` remains the bounded polling change above; tests are unchanged.

The existing caller, tee and prong contracts also pass **75 cases with one processor and 75 with two**
on .NET 10 Release. These controls set `DOTNET_PROCESSOR_COUNT` for the child test process, exercising
the original spare-processor guard and parking path. Logs, commands and checked TRX counters are in
`low-cpu-review.json`.

## Local paired comparison

All **60 observations** pass the original output/measurement validator: five workloads, both modes,
three alternating rounds, exact 100 MB inputs, actual 4 GiB heap guards and four effective processors.
The independent audit verifies raw output, all frozen binary hashes, the unchanged consumer,
409 tracked PDB source hashes per engine and agreement with the original hosted output fingerprints.
The framework compilation policy remains unchanged. This is a macOS diagnostic, not hosted acceptance.

| Workload | Corrected discard streaming median (ms) | With short polling (ms) | Candidate / baseline | Winner |
|---|---:|---:|---:|---|
| identity | 2,187.557 | 2,166.218 | 0.990245 | No meaningful winner; ranges overlap |
| accumulator | 2,120.219 | 2,122.532 | 1.001091 | No meaningful winner; ranges overlap |
| merge | 3,502.638 | 3,481.405 | 0.993938 | No meaningful winner; ranges overlap |
| fork | 1,616.042 | 1,581.638 | 0.978711 | No meaningful winner; ranges overlap |
| source-document | 2,158.602 | 2,185.923 | 1.012657 | No meaningful winner; ranges overlap |

The 2.1% lower fork median is inconclusive locally: baseline samples are
`[1602.710667, 1655.856708, 1616.042125]` ms and candidate samples are
`[1556.7925, 1581.638458, 1625.239917]` ms. No adoption follows from these measurements.
The fresh Linux profile identifies the waiting cost on the target platform, so one paired hosted
comparison will assess this composition there. No original full qualification rerun is justified yet.

Evidence is retained under `/private/tmp/xsl3-p7-discard-short-poll-20261105/`, including
`audit-paired.py`, `paired-review.json`, both PDB source inventories, frozen consumers and raw child
reports. The baseline engine SHA-256 is
`91f491ffa4676894b2adba8d3932dd4b0047df0d8fe3471dfe18e4d562eb2226`;
the candidate engine is `033c369475a488135c2fc2c920cc9cebfeaa4c1e40730f83083b810bed5aa23b`.
No footprint result or release readiness is claimed for this composition.

## First Linux comparison: benefit unresolved

[Run 42133558420](https://github.com/skuirrels/Xsl3/actions/runs/42133558420) completes the same
60-observation comparison on Linux x64, using baseline `a8d322a` and candidate `a6f018a` (runtime
`225260f`). The independent audit verifies all observations, raw outputs, effective 4 GiB guards,
four processors, normal framework policy, all retained binary hashes and 409 tracked PDB source
hashes per engine. The five fingerprints also match original full qualification.

| Workload | Baseline streaming median (ms) | Candidate (ms) | Candidate / baseline | Winner |
|---|---:|---:|---:|---|
| identity | 5,117.614 | 5,164.414 | 1.009145 | No meaningful winner; ranges overlap |
| accumulator | 5,278.497 | 5,465.815 | 1.035487 | Baseline in this cohort |
| merge | 9,776.384 | 10,002.113 | 1.023089 | Baseline in this cohort |
| fork | 4,869.497 | 3,676.227 | 0.754950 | Unresolved; large overlapping variation |
| source-document | 5,188.687 | 5,191.880 | 1.000615 | No meaningful winner; ranges overlap |

The 24.5% lower fork median is not a robust adoption result. Its baseline samples are
`[4869.496567, 3577.826089, 4960.172207]` ms; the candidate samples are
`[4631.818031, 3676.226872, 3612.24001]` ms. Ranges overlap widely, one paired round is slower,
and accumulator/merge have non-overlapping regressions. The candidate fork stream/tree ratio
is still **1.090997**. None of these observations replaces original throughput acceptance.

The follow-up is bounded to one independent, longer **1 GB fork-only** paired comparison: three
alternating rounds, both modes, twelve observations, unchanged engines and canonical consumer.
The diagnostic workflow accepts an explicit size/scope; its defaults retain the original five-workload
100 MB comparison, and original full memory/throughput jobs are unchanged. Longer samples test whether
the apparent fork reduction persists beyond the wide short-cohort variation. If the result remains
mixed, do not adopt this control or repeat it until a green median appears. Even a clear longer-run
benefit still requires non-fork regression resolution and every original final-source gate.

Artifact `13463999562` expires on 4 February 2027. Archive SHA-256:
`a26d2594f5a519d03569349e143aae5e65cf05ee321505a7eb876e3f4f410b43`.
Raw files, PDB inventories and executable audit are retained under
`/private/tmp/xsl3-p7-discard-short-poll-linux-20261106/`.

## Longer Linux comparison: close without adoption

[Run 42135439005](https://github.com/skuirrels/Xsl3/actions/runs/42135439005) completes all **12**
fork observations at exact **1,000,000,000 bytes**, with unchanged canonical output, actual 4 GiB
guards and normal framework compilation. The independent audit verifies the archive, every raw
measurement, retained binary hashes and 409 tracked source hashes per engine. Candidate `a525d50`
has the same runtime as the preceding short comparison; only diagnostic options/documentation changed.

Baseline streaming samples are `[26292.574870, 23145.997515, 23182.011531]` ms; candidate samples
are `[21892.526943, 23753.155012, 20899.668056]` ms. Medians are **23,182.012 /21,892.527 ms**,
a candidate/baseline ratio of **0.944376** (5.6% lower). The ranges still overlap and the second
paired round is slower. Candidate stream/tree remains **1.075937**. The smaller longer-run median
reduction does not establish the large short-cohort gain, and it does not resolve the non-fork
regressions from the first Linux comparison.

As specified before this follow-up, the result remains mixed: **do not adopt this control or rerun
it looking for a passing median**. No original full qualification is dispatched for it. Keep the
source/evidence on `feature/p7-discard-short-poll` and exclude its runtime change from integration.
Winner: no established overall winner across the retained cohorts.

Artifact `13465516542` expires on 4 February 2027. Archive SHA-256:
`bcec747d98927f856e26dee471b49756d4ec9ebb5f0717e51bb28331997a304d`.
Raw reports, frozen engines, PDB inventories and executable audit are retained under
`/private/tmp/xsl3-p7-discard-short-poll-long-linux-20261106/`.
