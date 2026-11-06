# P7 agent handoff after 1.1.7

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
