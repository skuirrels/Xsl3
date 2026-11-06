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
| Nightly: 2 GiB input, 200 MiB absolute RSS and managed guard, 16 workloads | Running; recheck the same run |
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

## Completion boundary

The other two active jobs must finish and be independently audited. A passing push matrix alone
does not complete P7. Final integrated source still requires all original gates, full compatibility,
physical-input/first-output evidence, supported claims, review/merge and matching NuGet/GitHub release.
The older three-of-seven throughput result applies only to its historical component cohort;
no throughput result is yet established for this integration head.
