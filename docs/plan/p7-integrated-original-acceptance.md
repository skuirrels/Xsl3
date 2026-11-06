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
| Throughput: 126 observations, seven original 100 MB targets | Running; recheck the same run |

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

## Completion boundary

The active throughput job must finish and be independently audited. The passing memory matrices
do not complete P7. Final integrated source still requires all original gates, full compatibility,
physical-input/first-output evidence, supported claims, review/merge and matching NuGet/GitHub release.
The older three-of-seven throughput result applies only to its historical component cohort;
no throughput result is yet established for this integration head.
