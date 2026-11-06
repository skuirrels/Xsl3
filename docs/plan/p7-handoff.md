# P7 agent handoff after 1.1.7

**Isolated value-event control, 6 November:** `feature/p7-fork-value-events` is closed without
adoption. It starts at integrated `6c24a92` and changes only direct fork string-value event
consumption. All 1,852 selected contracts and 60 paired observations validate, but the 1.23% lower
fork median has overlapping ranges and accompanies a lower tree-control median. A fresh allocation
trace proves the targeted frame-allocation path disappears; that does not establish useful throughput
benefit. Do not merge this runtime or repeat an equivalent frame-only control from allocation evidence
alone. [The control record](p7-fork-value-events.md) retains the source, exact evidence and decision.
Resume runtime work from `feature/p7-integrated-qualification` at `6c24a92`.

Draft [PR #40](https://github.com/skuirrels/Xsl3/pull/40) is running its own
[CI 42141508372](https://github.com/skuirrels/Xsl3/actions/runs/42141508372) on that unchanged head.
The last verified snapshot has NativeAOT and ordinary performance successful, with Linux and Windows
unit-test steps still running. Recheck the same run; do not restart it or claim full compatibility yet.

**Integrated qualification preparation, 6 November 2026:** `feature/p7-integrated-qualification`
combines the public-selection head `5645098` with corrected-discard controller `18fc9a5`
(runtime `a8d322a`, 256-event default). It includes merged PRs #34–38 and excludes all rejected
runtime controls. This combined source has not yet completed compatibility or original qualification.
See [the integration record](p7-integrated-qualification.md). Full P7 remains active; no release claim.

The isolated short-polling control is closed after its 100 MB and 1 GB Linux comparisons; overlapping
fork ranges and other-workload regressions do not justify adoption. The tail-call control is also closed
without useful timing benefit. Lazy frame headers completed all 60 Linux observations in
[42136785894](https://github.com/skuirrels/Xsl3/actions/runs/42136785894), with verified binaries,
409 tracked PDB source hashes per engine and original outputs, but all timing ranges overlap.
It is closed without adoption. Their branches retain the source, audit records and decisions.
The earlier descriptions below are dated investigation history, not current approval to merge them.


Public-selection head `5645098` now passes all four jobs in
[CI 42133700130](https://github.com/skuirrels/Xsl3/actions/runs/42133700130). Draft PR #39 remains
unmerged under the full-P7 boundary. Combined candidate `409068e` passes 18,260 affected Release/Debug checks across .NET 8/10, with
every result, source hash and all four engine/test binary sets retained. Its own hosted CI and original
final-source qualification remain required. The [CPU stack-region audit](p7-cpu-stack-regions.md) preserves all original
samples while separating output-oracle work; it does not change a gate or establish a runtime gain.


**Public integration preparation, 6 November:** `feature/p7-public-streaming-selection` starts from
merged main `f453216` and prepares the original public Strict/Fallback execution requirement. It
excludes all unadopted performance controls and retains `supports-streaming=no`. Six public/saved
contracts fail on the unchanged policy; the candidate passes all 966 focused Release/Debug checks,
including actual representation and physical read boundaries. Full local unit compatibility also passes:
27,798 checks across Release/Debug and .NET 8/10, with matching source and engine hashes. Draft PR #39
has passed all four hosted CI jobs on `c889023` in
[42127805496](https://github.com/skuirrels/Xsl3/actions/runs/42127805496).
See [the selection record](p7-public-streaming-selection.md). It must not merge or claim release
readiness until final integrated source passes every original gate.

Separate performance evidence: corrected discard `a8d322a` now passes both original memory matrices
in [42123882563](https://github.com/skuirrels/Xsl3/actions/runs/42123882563): 32 budgets and 96 outputs,
independently audited; maximum nightly RSS 32,309,248 bytes. Its full throughput is now complete:
all 126 observations validate, with 409 tracked PDB source hashes matching, but only 3/7 targets pass.
Stream/tree medians at 100 MB are identity 1.064619, adjacent 0.745620, accumulator 0.861578,
iterate 0.798460, merge 1.052924, fork 1.447580 and source-document 1.066196. Do not infer a
before/after effect from separate hosts. The 60-observation paired Linux comparison completed at
[42131056101](https://github.com/skuirrels/Xsl3/actions/runs/42131056101), candidate `9fbd8e1`
(runtime identical to `a8d322a`) versus `deed3c1`, including merge and four other workloads.
All observations and 409 tracked PDB source hashes per engine verify. Fork's streaming median falls
from 3,218.088 to 2,755.886 ms (14.4%); every candidate fork sample is below every baseline sample.
Other workload ranges overlap, with no meaningful winner. The candidate fork stream/tree ratio is
still 1.132795. This establishes a useful discard improvement, not original throughput acceptance.

The isolated `feature/p7-discard-short-poll` control reapplies only bounded short polling to this
corrected-discard foundation. It passes 5,868 affected Release/Debug .NET 8/10 checks and 150 one/two-CPU
caller/tee/prong checks. All 60 local paired observations and source hashes validate, but the 2.1%
lower fork median has overlapping ranges; no local timing benefit or adoption is established.
The fresh target-platform wait profile below justifies one paired Linux comparison at
[42133558420](https://github.com/skuirrels/Xsl3/actions/runs/42133558420), baseline `a8d322a`, candidate
`a6f018a` (runtime `225260f`). The original one-millisecond wait bound, spare-processor guard, capacity,
payload reclamation and demand-driven source reads are unchanged. The comparison is running; no
full qualification rerun is justified yet. See the
[isolated control record](https://github.com/skuirrels/Xsl3/blob/feature/p7-discard-short-poll/docs/plan/p7-discard-short-poll.md).

The linked ancestor-stack control `7b24202` is rejected after the paired Linux diagnostic
[42126416005](https://github.com/skuirrels/Xsl3/actions/runs/42126416005): all 48 outputs and 409 source
hashes per variant verify, but timing is mixed. Fork's 8.2% lower median has overlapping ranges;
accumulator and source-document regress. Do not merge that control. Raw evidence and executable audit
are under `/private/tmp/xsl3-p7-linked-source-stack-linux-20261105/`.

A fresh Linux fork CPU diagnostic completed at
[42129815135](https://github.com/skuirrels/Xsl3/actions/runs/42129815135), using corrected discard
`a8d322a` and controller `5db06fb`. The original 1 GB output, actual 4 GiB heap guard, ordinary
ReadyToRun policy, artifact digest, four retained binary hashes and 409 tracked PDB sources verify.
It records 71,109 kernel CPU samples and zero lost events; 11,254 leaves are `ThreadNative_SpinWait`,
but most of those lack an unwound engine caller. Scheduler/yield stacks do resolve to publication
waits and the caller pump. Sample shares are not removable elapsed time. The artifact does not retain
the 24 dependency binaries whose hashes the report lists; no independent hash verification of those
files is claimed. Evidence and executable audit: `/private/tmp/xsl3-p7-fork-cpu-discard-20261105/`.

PR #38 is merged as `8bdec4c` after all four jobs passed at its unchanged `7ea0190` head in
[42123616078](https://github.com/skuirrels/Xsl3/actions/runs/42123616078), with no open review threads.
The public-selection branch now incorporates that test-only increment. Both dedicated Debug CI steps
and both contract suites are preserved. Combined head `9f0fed3` passes all 1,318 selected Release/Debug
checks across .NET 8/10, including every hidden-grounding, fork-retention and optimizer/verifier case.
The preceding `c889023` CI completed successfully before pushing the reconciled head; no live
compatibility job was cancelled. The combined head still requires its own CI.

**Merged contract increments:** [PR #37](https://github.com/skuirrels/Xsl3/pull/37) passed all four
jobs in [42113850460](https://github.com/skuirrels/Xsl3/actions/runs/42113850460) and merged as `f453216`.
Its [S6-11/12 payload-retention contracts](p7-fork-group-retention.md) and PR #38's
[S3-27 hidden-grounding contracts](p7-hidden-grounding-contracts.md) are both on main.

Separate runtime candidates remain unaccepted. The 256-event `deed3c1` passes both memory matrices
and four throughput targets; identity, fork and source-document fail. The 4,096-event `aebb3a2`
also passes four throughput targets but fails push memory at 22,941,696 bytes against 20,971,520.
The initial discard `fb6d4cd` failed source-position contracts and its qualification was cancelled;
corrected `a8d322a` passes all 5,536 affected Release/Debug checks, with hosted results above.
The source-dispatch control `3793df5` remains rejected. No performance control is part of PR #39.



**Full objective resumed, 5 November 2026:** the owner instructed “continue with phase 7 until it's complete”.
Start from merged main `a5e89ac` or a verified descendant. PRs #34–36 have merged
[S3-25 verifier coverage](p7-verifier-coverage.md), [S5-14 group read positions](p7-group-read-positions.md)
and [S3-04 retained managed bytes](p7-retained-memory-contracts.md). All original acceptance gates remain
required, including the five outstanding throughput targets. Earlier pause and bounded-stop instructions below
are historical and no longer limit the active objective.

The completed predecessor [S3-04 retained-memory increment](p7-retained-memory-contracts.md) is on
`feature/p7-retained-memory-contracts`, based on grouping head `051c58d`. Local managed-byte and negative
controls passed; its own hosted CI is still required. No runtime optimization has been adopted in these
three contract increments. Fork throughput profiling is next, alongside remaining internal-grounding and
fork-retention evidence.

**Throughput investigation:** the [tee reclamation control](p7-tee-reclamation-experiment.md) at `ce42373`
is rejected and remains only on `feature/p7-tee-reclamation`: all 693 affected Release checks passed after
a wake-ordering correction, but twelve valid paired local observations showed only 0.55% improvement
with overlapping ranges. Do not merge that branch. `feature/p7-producer-allocation` starts again from
validated contract head `78815e8`. Fresh
[allocation evidence and the candidate boundary](p7-producer-allocation.md) identify producer `Node`
frames as 12.84 GB of sampled allocations. An isolated compact producer is now under qualification;
its runtime changes have not yet passed the original hosted acceptance gates or been adopted into main.
Both producer variants pass the affected contracts, and the first removes producer `Node` allocation
ticks, but twelve paired observations per variant show no conclusive throughput benefit. The direct
metadata variant passes 1,588 affected Release/Debug checks. Keep both controls unadopted; next profile
consumer-frame and per-item output costs rather than claiming allocation reduction closes performance.
[PR #34](https://github.com/skuirrels/Xsl3/pull/34) passed all four required jobs in run `42094356435`
and merged at `66e078d`. [PR #35](https://github.com/skuirrels/Xsl3/pull/35) is retargeted to main;
it passed all four jobs in run `42095292062` and merged at `9a75be4`.
[PR #36](https://github.com/skuirrels/Xsl3/pull/36) passed all four jobs in run `42096941959`
and merged at `a5e89ac`. All three contract increments are merged; full P7 remains incomplete.

**Next isolated runtime control:** [simple-content value state](p7-simple-content-state.md) is on
`feature/p7-simple-content-value-state`, based on `78815e8` and excluding both unaccepted producer
variants. It removes the per-constructor heap wrapper from newly generated IL while keeping the old
saved-style runtime API and a shared state machine. All 1,346 affected Release/Debug checks and all
36 paired output observations passed, but the elapsed-time benefit remains inconclusive. It is not
promoted to a PR. The producer controls remain preserved on their own branch. Next obtain Linux
kernel CPU samples: the old sampled-thread-time trace classifies waits and GC polling as `Managed`,
so filtering that label does not establish CPU cost. Original acceptance gates remain unchanged.

**Current investigation:** `feature/p7-linux-cpu-profile` starts from merged main `a5e89ac`, with the
same runtime and benchmark sources as contract head `78815e8`. The opt-in
[Linux CPU diagnostic](p7-linux-cpu-profile.md) captures kernel CPU and context-switch events for one
canonical fork child. It excludes all three unaccepted runtime controls. A trace is useful only after
checking actual CPU event coverage and symbols; a successful capture does not satisfy throughput gates.
Runs `42105701535` and `42106401936` completed successfully. The latter has managed symbols and
70,016 kernel CPU samples with zero reported loss; `eng/StreamingCpuReport` reproduces the analysis.
The [early-parking wait control](p7-fork-wait-budget.md) at `91bd422` passed 1,786 contracts but clearly
regressed paired elapsed time and remains rejected on its own branch. Do not merge it. The current
profiling branch retains merged runtime behavior. Next investigate source-production overlap while
preserving the original capacity, demand, read-order and cancellation contracts; do not repeat the
rejected short-wait, no-yield or short-poll controls recorded in the completion history.

**Demand review and configuration investigation:** the [speculative refill control](p7-fork-production-overlap.md)
at `1b2ef47` gains about 19.6% locally but violates the original no-read-ahead-without-demand contract,
and is rejected. Its new negative control passes on the unchanged runtime. Current branch
`feature/p7-fork-buffer-policy` retains only that demand regression and
[public buffer-setting diagnostics](p7-fork-buffer-policy.md). All 24 local observations validate output;
256 configured events gains about 8.0% over 64, with diminishing gains at larger sizes. Every local ratio
still exceeds one. Hosted run `42109352467` also validates all 24 outputs; 4,096 events reduces the
fork streaming median by 27.9%, but its stream/tree ratio remains 1.255375. The default remains 64;
no capacity policy is adopted without the original memory and compatibility checks. Independent
identity/accumulator CPU runs `42109632792` / `42109637672` validate both 1 GB outputs and provide
26,875 / 25,889 CPU samples without reported loss. Source construction is a shared cost; underlying
parser frames remain partly unresolved. See the linked CPU evidence before choosing a runtime control.

**Current candidate:** `feature/p7-fork-capacity-qualification` independently raises the public default
from 64 to 4,096 logical events, based on the hosted comparison above. It starts at `42d2329` and
excludes every rejected runtime control. Explicit settings and old saved constants remain unchanged.
See [qualification criteria](p7-fork-capacity-qualification.md); the original memory/throughput gates
are required. The source-dispatch extraction at `3793df5` on `feature/p7-source-dispatch` is rejected:
3,141 Release contracts and 48 paired outputs pass, but it establishes no convincing timing gain.
Do not merge that control.

**Earlier bounded continuation, 5 November 2026:** the current published release is 1.3.0 at `f9708eb`.
Phase A accounting, the union-analysis correction, the extension setting and optimizer/function
evidence have shipped. The current bounded follow-up is [PR #33](https://github.com/skuirrels/Xsl3/pull/33):
forward-entry evidence and the HTTP-URI probe qualification. Read [phase A](p7-phase-a.md) and
[its review record](p7-forward-evidence-review.md) for current scope and remaining limitations.
The 1.1.7 baseline and historical release evidence below remain available; the earlier pause no longer
applies to this owner-authorized bounded continuation. Full P7 and supported streaming remain incomplete.

Verified 1 November 2026. The owner requested a stopping point and release of completed work.
The bounded runtime correctness and allocation checkpoint is closed and published as **1.1.7**.
Further P7 implementation was paused here. On 1 November 2026 the owner resumed it in bounded phases:
a fresh baseline of 1.1.7, then phase A (S3–S6 acceptance accounting); see [phase A](p7-phase-a.md).
Do not resume the earlier open-ended goal. Full streaming remains experimental and unsupported; `supports-streaming` is `no`.

## Released baseline

- Start future work from `v1.1.7`, commit `b0bf3bb3464e319048c230191577f86c326b5cdb`, or a
  newly verified descendant of main. [PR #27](https://github.com/skuirrels/Xsl3/pull/27) is merged.
  Its reviewed source `c9c622b677192bb2cf7caf619ce5494e6db51b62` has the same tree as the merge.
- [GitHub release](https://github.com/skuirrels/Xsl3/releases/tag/v1.1.7) is public with three
  coordinated packages, runtime symbols and `SHA256SUMS`. Public NuGet versions are
  [Xsl3](https://www.nuget.org/packages/Xsl3/1.1.7),
  [Xsl3.Build](https://www.nuget.org/packages/Xsl3.Build/1.1.7) and
  [Xsl3.Cli](https://www.nuget.org/packages/Xsl3.Cli/1.1.7).
- Default execution remains tree-based. `Streamability = Strict` selects static checking, not
  experimental forward execution. `XSL3_STREAM_STRICT=1` is a development opt-in; saved styles
  retain their recorded execution mode. Both public APIs and saved/native consumers remain covered.
- The released scope and exclusions are in [the checkpoint](p7-runtime-checkpoint.md),
  [release notes](../releases/1.1.7.md), [changelog](../../CHANGELOG.md) and [migration notes](../migration.md).

The release was prepared in `/Users/tyomidi/Source/Xsl3` on `feature/p7-completion` at the reviewed
source; the handoff documentation was added afterward. Do not assume the active branch is main. Preserve unrelated untracked
`docs/.DS_Store`, `eng/__pycache__/` and `eng/tests/__pycache__/`. Inspect status and refs before
changing branches. The older `p7-native-initialization` worktree is not the released baseline.

## Completed release verification

| Evidence | Result |
|---|---|
| [PR CI 41348273376](https://github.com/skuirrels/Xsl3/actions/runs/41348273376) | Linux, Windows, NativeAOT and ordinary performance all passed |
| [Main CI 41351971235](https://github.com/skuirrels/Xsl3/actions/runs/41351971235) | Linux, Windows and NativeAOT passed; performance skipped by push condition |
| [Tagged release 41352001262](https://github.com/skuirrels/Xsl3/actions/runs/41352001262) | Build, tests, pack and NuGet publication passed after owner deployment review |
| Public package identity | All three versions and repository commits verified; ZIP payload entries matched hosted packages, excluding NuGet's added signature |
| Fresh public package consumers | Isolated .NET 8/10, both public APIs, Xsl3.Build saved styles, secondary results, Strict/Fallback, CLI tool and real NativeAOT passed |
| Public saved-style contracts | Original bare-variable fixtures: 192 .NET 8 + 192 .NET 10 + 192 NativeAOT = 576 passed |
| Full local unit suites recorded before release | Release and Debug each: 7,339 .NET 10 / 6,229 .NET 8; 27,136 total, no failures or skips |

These are release compatibility checks. Ordinary CI success does not satisfy the separate experimental
streaming memory or throughput gates. Recheck live CI and package state before making a later release claim.
NuGet environment approval is reserved for the owner by the release workflow; do not self-approve it.

## Remaining full P7 work

Read the original [streaming plan](p7-streaming.md), the requirements table in
[P7 completion](p7-completion.md#requirements-and-evidence), and
[runtime contracts](p7-runtime-contracts.md). Older progress entries are historical evidence about
their named commits, not current completion claims.

1. Finish S3–S6 acceptance accounting: forward runtime, modes/functions, stateful constructs and fork
   ownership, cancellation, input/output lifetimes and actual buffer bounds. Reassess the strict runtime
   inventory from original inputs; its retained baseline is 2,695 pass / 16 fail / 1 not run.
2. Reverify S1/S2 against the reviewed 1,000-case / 155-case sets and retain the original raw inventories.
   Preserve the pinned suite, case population, fingerprints, error codes and independent oracles.
3. Pass the entire original 16-workload NativeAOT push matrix: **209,715,200 input bytes**,
   **20,971,520-byte absolute whole-process RSS**, with the actual managed-heap guard.
4. Pass the entire original nightly matrix: **2,147,483,648 input bytes**,
   **209,715,200-byte absolute RSS**, with its actual managed-heap guard.
5. Complete all **126** canonical benchmark observations (seven workloads, three decimal sizes,
   three rounds in each mode). All seven 100 MB streaming medians must be no greater than
   tree-plus-transform. Preserve the 4 GiB heap guard and actual first-output/input-consumption evidence.
6. Complete strict conformance, both public API paths, saved-style integration, compatibility and final
   reviewed-source qualification before changing any supported-streaming claim.

The checkpoint retains failures in merge, fork and source-document push RSS, plus throughput failures.
Exact ratios elsewhere belong to specific historical candidates/hosts; they are not measurements of
`v1.1.7`. There is no complete full-P7 qualification of the released tree. No startup subtraction,
disabled globalization, relaxed budget, omitted workload or altered expected output can establish a pass.

Private compositions such as `feature/p7-composed-compact` / `b2316fd` and `c7da793` were **not adopted**.
Local wins did not resolve all hosted failures. The final private combined benchmark was cancelled at
the owner's stop; partial output cannot qualify acceptance. Do not resume that run or cherry-pick its
experiments as if they had been released. Re-review and freshly qualify any proposed adoption.

## Evidence availability and reproduction

The ignored local `artifacts/` directory disappeared during release verification. The development log
still records historical paths and measurements, but those paths are not portable evidence and their
files may be absent. Some private refs, worktrees and raw controls may also be unavailable to another
agent. GitHub Actions artifacts have retention limits. If raw evidence cannot be recovered from a linked
run, state that gap and rerun the original check; do not reconstruct passing evidence from prose.

Release packages and checksums remain on the public GitHub release. Recovered public-package checks,
their logs and verifier scripts currently live at `/private/tmp/xsl3-release-1.1.7-recovery/` on the
release machine (`final-audit.json`, `consumers-public/`, `saved-public/`). This is temporary local
storage, not checked-in tooling or a portable prerequisite. The original full local unit logs were
lost; hosted CI is the durable linked verification, subject to GitHub retention.

Use checked-in tooling for new work, with fresh output directories and no competing heavy local jobs:

- [Process-probe instructions](../streaming-probe.md): publish with `eng/publish-stream-probe.sh`,
  then run `eng/streaming-gate.py` at both original size/limit pairs above. Use real NativeAOT;
  preserve full globalization. The internal Linux probe needs LLD and glibc 2.36+.
- [Benchmark instructions](../streaming-benchmark.md): `eng/streaming-benchmark.py`,
  `--sizes 1mb 100mb 1gb --rounds 3 --heap-limit 4294967296 --require-throughput-target`.
  Keep managed and NativeAOT cohorts separate; preserve all raw outcomes.
- [Runtime inventory instructions](p7-completion.md#runtime-inventory-gate-2026-10-29): the
  `strm` profile, explicit `XSL3_STREAM_STRICT=1`, exports and `eng/audit-streaming-runtime.py`.
  The pinned inventory is `p7-runtime-inventory.json`; do not regenerate it to hide regressions.
- [Ordinary CI](../../.github/workflows/ci.yml) and
  [experimental streaming workflow](../../.github/workflows/streaming-development.yml) are separate.
  The latter remains scheduled and its original acceptance may still fail. Its `source_ref` input
  selects diagnostic controls, not the ordinary acceptance checkout; inspect the workflow before dispatch.

For work after the current bounded phase A follow-up, first agree the next phase and its completion
criteria, then capture a fresh baseline on the intended immutable source. Do not restart open-ended optimization
solely because older entries say the full goal is active. Do not restart a live measurement because
an observation wait times out.
