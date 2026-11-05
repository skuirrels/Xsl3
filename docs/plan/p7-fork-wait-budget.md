# P7 fork wait-budget control — rejected

This isolated control starts from merged runtime `a5e89ac`, with the diagnostic tooling from
`23a8035`. It ends `ForkCaller.SpinBudget` when the BCL spin would first yield, then uses the
existing monitor wait and publication checks. Thread count, ring capacity, ownership, cancellation
and all output contracts stay unchanged. The first Linux kernel CPU trace showed substantial
native spin/yield cost, motivating this specific early-parking control.

All 1,786 affected contracts passed: Release and Debug each ran 467 .NET 10 / 426 .NET 8 checks,
selected by `FullyQualifiedName~Fork|FullyQualifiedName~StreamTee`.

Twelve local macOS ARM64 paired 100 MB observations (three per variant/mode), with four effective
processors, disabled tiering and the original 4 GiB heap allowance, all validated their output.

| Variant | Tree median (ms) | Streaming median (ms) | Streaming observations (ms) |
|---|---:|---:|---|
| Merged baseline | 1,580.140 | 2,035.443 | 2,035.443; 2,792.886; 1,964.510 |
| Early parking | 1,879.151 | 4,408.109 | 9,119.702; 4,408.109; 3,882.105 |

Tree controls show host drift, but all candidate streaming observations are slower than every
baseline observation. This is a clear regression and the runtime change is rejected. Preserve it
only on `feature/p7-fork-wait-budget`; do not merge or compose it. No hosted acceptance pass or
NativeAOT qualification is claimed. Reducing spin CPU is not itself an elapsed-time improvement.

Raw TRX, immutable candidate binaries, binary hashes, comparison driver, all child outputs and
reports are under `/private/tmp/xsl3-p7-fork-wait-20261105/`. The baseline binary is retained under
`/private/tmp/xsl3-p7-fork-profile-20261105/baseline/` and was verified runtime-source-equivalent to
merged `a5e89ac` before the experiment.
