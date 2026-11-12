# P7 original integration acceptance

## Source and scope

[Run 42173435783](https://github.com/skuirrels/Xsl3/actions/runs/42173435783) qualifies immutable
`a4f730130728480748112af547aa999426bcfbff`, the reviewed integration of public selection,
corrected fork discard and deferred merge source groups. Its ordinary CI 42167378456 and
[artifact review](p7-integrated-evidence-review.md) pass. Neither the rejected attribute-cache
nor navigator node-kind experiment is included.

The dispatch uses no diagnostic switches. Original input sizes, all workloads, output fingerprints,
absolute RSS limits, heap guards and seven throughput targets are unchanged. Current results:

| Original gate | State on 6 November 2026 |
|---|---|
| Push: 200 MiB input, 20 MiB absolute RSS and managed guard, 16 workloads | Passed and independently audited: 48 valid observations, all 16 budgets |
| Nightly: 2 GiB input, 200 MiB absolute RSS and managed guard, 16 workloads | Passed and independently audited: 48 valid observations, all 16 budgets |
| Throughput: 126 observations, seven original 100 MB targets | All 126 observations valid; four targets pass, three fail |

## Push memory audit

All 48 baseline/measured child outputs match their retained stdout and original fingerprints. Each
measured child uses exactly 209,715,200 input bytes and reports the required 20,971,520-byte managed
allowance. The maximum of the child's whole-lifetime RSS and the outer `wait4` RSS is below that
absolute limit for every workload. No startup or baseline subtraction is used. The largest observed
peak is **17,498,112 bytes**, for fork.

The retained 32 saved stylesheets match the original cohort byte for byte. The native executable
is an ELF image; its retained hash and all 409 tracked engine PDB document checksums verify against
the immutable integration source. Generated assembly/framework/regex source entries are recorded
separately and are not claimed as tracked-source comparisons.

First-output input counts are retained for every child. Identity and source-document emit after
8,192 bytes, merge after 122,880 bytes and fork after 36,864 bytes. Accumulator, iterate and type
inspection emit their document-wide result after EOF. This preserves the distinction between early
output and operations whose result inherently needs the whole input.

- Artifact: `13482515854` (`streaming-memory-push`), expires 4 February 2027.
- GitHub ZIP SHA-256: `b88464ec48ccce78063f9ab62e2fa8832feb316c915d7d57cf3c281657ecb817`.
- Retained native SHA-256: `e439f796d2194b9114ee03636ddd34312f9b2191d70d5d8de35abf143fd485ce`.
- Report SHA-256: `232bbe624437548fc5af3abfe611304466d062c8081a9f8da71a8dbccb874a77`.

Raw ZIP, metadata, frozen gate/workflow controllers, exact source manifest, extracted artifact and
`audit-original.py` / `push-review.json` are retained under
`/private/tmp/xsl3-p7-integrated-original-20261106/`. The audit verifies artifact SHA before content,
exact run/head, all original observations, output equality, effective limits and source/binary identity.

## Nightly memory audit

The nightly artifact also independently passes all **48** original observations and **16** budgets.
Measured children consume exactly 2,147,483,648 bytes, with the required 209,715,200-byte managed
allowance. Maximum absolute whole-lifetime RSS is **32,305,152 bytes**, for fork. All original output
fingerprints, stdout agreement, 32 saved stylesheets and 409 tracked PDB source checksums verify.
First-output input positions match the push cohort for all early-output workloads; accumulator,
iterate and type inspection retain their EOF-dependent results at the full nightly size.

- Artifact: `13482897413` (`streaming-memory-nightly`), expires 4 February 2027.
- GitHub ZIP SHA-256: `ba0f701d1cb9173d66a4af180d838ebde97bd42087213ce2564d6f1820f5b54e`.
- Retained native SHA-256: `3a441fdc0727040c90b60e79eec54b39a8b58020053e5335a83ea0f2ff7fbd98`.
- Report SHA-256: `02ecded03f629f8d26a3432e4c1f073b7e5762ba2ffe22af1cd9e7d03fefe29d`.

`nightly-review.json` and its executable audit are retained alongside the push evidence. Both
matrix jobs are terminal. Together they prove **32/32 original budgets and 96 valid outputs** on
this integrated source; neither matrix needs an unchanged-source retry.

## Completed original throughput audit

All **126** original observations validate, with no child failure, timeout or incorrect output.
The independent audit verifies every size/workload/mode/round, raw stdout equality, original output
fingerprints, four reported processors, disabled tiering, the effective 4 GiB allowance, retained
engine/consumer hashes, original controller hash and all 409/14 tracked PDB source checksums.
The workflow fails because **three original 100 MB median targets are unmet**, not because outputs
or observations were discarded. All original requirements remain unchanged.

| Workload | Tree median (ms) | Stream median (ms) | Stream/tree | Original target |
|---|---:|---:|---:|---|
| identity | 4345.374 | 5127.982 | 1.180101 | Fail |
| adjacent | 2978.729 | 2570.166 | 0.862840 | Pass |
| accumulator | 5231.135 | 5203.924 | 0.994798 | Pass |
| iterate | 3015.402 | 2819.365 | 0.934988 | Pass |
| merge | 9724.763 | 9607.438 | 0.987935 | Pass |
| fork | 3302.519 | 5008.933 | 1.516701 | Fail |
| source-document | 4445.760 | 5089.340 | 1.144763 | Fail |

Merge now passes in this complete integration cohort. Identity, fork and source-document remain
open; accumulator is close to its unchanged threshold. The preceding component cohort remains
historical evidence. Differences between separate hosts/cohorts are not paired speedup estimates.

- Artifact: `13483644827` (`streaming-benchmark-nightly`), expires 4 February 2027.
- ZIP SHA-256: `3918e5c362a291efe044decb95040d0578393de52981c86519d4b5dd95794a86`.
- Report SHA-256: `4dcd53e532f3514f0ca08fe22b642229d1a26353ab355669e1e3c37d30ce929b`.
- Engine SHA-256: `6e304ffcd90b7c63a0be1364fb0704e8d227f14e391adb9e2c271fe29caeea7b`.
- Consumer SHA-256: `1df68e8bfabea6dffc7c6e26656ab031c5e399ff7fc7ea980d61e00bc51af7a4`.

`throughput-review.json`, its executable audit and all 126 raw outputs are retained in the evidence
root above. All jobs in 42173435783 are terminal. Do not restart this unchanged-source gate to seek
favorable medians. Qualify changed final runtime source against the same complete requirements.

## Completion boundary

Both memory matrices pass, but only four of seven throughput targets pass. Full Phase 7 remains
incomplete. Resolve identity, fork and source-document throughput, then qualify final reviewed
source against every original gate, full compatibility and physical-input/first-output contracts.
Supported claims, final review/CI/merge and matching NuGet/GitHub publication remain required.
