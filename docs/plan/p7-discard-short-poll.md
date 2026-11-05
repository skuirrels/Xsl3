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
