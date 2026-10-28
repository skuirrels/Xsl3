# P7E memory-budget diagnosis, 2026-10-28

P7E is closed as a diagnostic audit of release **1.1.3**
(`7bf3dd67a71fe0d77dc2fca1f0264ff8b343c4bf`). Runtime and packaging source are unchanged. No limited,
generally correct product fix was demonstrated, and no 1.1.4 release is proposed. Streaming remains
unsupported and `supports-streaming` remains `no`.

The [frozen phase](plan/p7e-memory-budget-diagnosis.md) limits investigation to identity. Other workloads
are regression controls, not additional optimization tasks. Original inputs, limits, globalization,
output oracles, fixture stylesheets, generated entry points and canonical controllers are unchanged.

## Complete canonical baselines

| Platform | Exact input / absolute RSS and heap guard | Measured workload budget passes | Full measured inputs with valid output |
|---|---|---:|---:|
| Hosted Linux x64 | 209,715,200 / 20,971,520 bytes | 0/16 | 6/16; 10 rejected before measurement |
| macOS ARM64 | 209,715,200 / 20,971,520 bytes | 13/16 | 16/16 |
| Hosted Linux x64 | 2,147,483,648 / 209,715,200 bytes | 16/16 | 16/16 |
| macOS ARM64 | 2,147,483,648 / 209,715,200 bytes | 16/16 | 16/16 |

Every matrix contains all 48 child results, including its 10 MiB tree/stream output-fingerprint controls.
All those baseline controls pass. A startup-budget rejection has zero measured input and does not count
as a completed transformation. The Linux source is the immutable release tag in
[run 40774600310](https://github.com/skuirrels/Xsl3/actions/runs/40774600310); local source was extracted
with `git archive v1.1.3`. Both use the .NET 10.0.403 SDK and 10.0.14 runtime/toolchain.

The local failures remain iterate, merge and fork. Merge reports internal `ok` at 20,873,216 bytes but
its full-lifetime `wait4` peak is **21,118,976**, above **20,971,520**. The controller correctly rejects it.
The older [scheduled Linux run](https://github.com/skuirrels/Xsl3/actions/runs/40625350353) is retained
separately; its different startup dispositions are not overwritten by this reproduction.

## Separate identity diagnostics

Each platform has three fresh processes per exact input size for each of two images: unchanged control
and instrumented copy. All **36** final processes consume the requested input and validate independent
output fingerprints. The managed heap cap remains **20,971,520 bytes**. The probe's RSS guard argument is
explicitly **0** so diagnostics can finish beyond that budget. These runs are **not acceptance passes**.

The instrumented image adds snapshots at entry, saved-style load, warmup, post-warmup collection,
transformation, oracle and result creation. It records lifetime peak, cumulative allocation,
`GC.GetTotalMemory(false)`, last-GC heap/commit/fragmentation observations, and Linux `/proc/self/smaps_rollup`.
The canonical source is copied into an isolated directory; only probe instrumentation and its build inputs
are changed there. Acceptance images and runtime source are not instrumented.

| Platform | Exact input bytes | Control median lifetime peak | Instrumented median lifetime peak |
|---|---:|---:|---:|
| Linux x64 | 10,485,760 | 25,989,120 | 26,017,792 |
| Linux x64 | 209,715,200 | 25,952,256 | 26,267,648 |
| Linux x64 | 2,147,483,648 | 27,566,080 | 27,611,136 |
| macOS ARM64 | 10,485,760 | 20,398,080 | 20,414,464 |
| macOS ARM64 | 209,715,200 | 20,119,552 | 20,086,784 |
| macOS ARM64 | 2,147,483,648 | 20,119,552 | 20,119,552 |

The corrected [Linux diagnostic run](https://github.com/skuirrels/Xsl3/actions/runs/40778934175) passes
its measurement/output-validation job. That green diagnostic job does not waive the red 20 MiB gate.
The [result snapshot](plan/p7e-results.json) retains min/max peaks, all phase medians and launcher peaks.

## What the observations support

- Growing input from 10 MiB to 2 GiB (204.8 times) does not produce proportional RSS growth in identity.
  Linux control medians grow about 6%; macOS remains in the same narrow range. This is evidence for this
  input family and execution path, not proof of bounded memory for every stylesheet or input shape.
- After result creation and the canonical forced collection, the managed estimate stays near **0.41 MB**
  on Linux and **0.71 MB** on macOS. Last-GC commitment is roughly **4.7–6.2 MB** and **3.6 MB**, respectively.
  These last-GC observations are not simultaneous decompositions of a historical process peak. Zero
  commitment observations before the first collection must not be interpreted as zero allocated heap.
- Linux's instrumented live RSS already exceeds 20 MiB after warmup collection: about **21.4 MB**.
  At the end of transformation it is about **25.6 MB** at 200 MiB input and **27.0 MB** at 2 GiB.
  Its larger file-backed/anonymous process footprint is visible in the retained rollup data.
- Identity allocates substantial transient managed data: about **14.9 GB** cumulatively for 200 MiB input
  and **152 GB** for 2 GiB, while the post-collection estimate remains small. The interval containing the
  independent oracle and diagnostic bookkeeping adds only kilobytes of allocation and does not raise
  the sampled peak. Large allocation traffic is not the same measurement as retained input.

The [.NET heap hard limit](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#heap-hard-limit)
bounds GC heap/bookkeeping commitment, not the entire process RSS. Native code, runtime support, mappings
and stacks also contribute. The measurements distinguish those budgets; they do not establish an
irreducible platform minimum or identify one safe product change that would meet the remaining limit.
Full-lifetime counters, independent output checks and the original budgets therefore remain authoritative.

## Excluded first diagnostic cohort and tooling correction

The first Linux diagnostic cohort used whole-file hashing of the native image before child launch.
It reported an invariant **33,497,088-byte** control peak already present at managed entry, while
simultaneous live RSS was far smaller. That pre-exec launcher memory contaminated native peak attribution.
Linux [preserves resource usage across `execve`](https://man7.org/linux/man-pages/man2/getrusage.2.html).

The diagnostic launcher now hashes with a bounded buffer and records its own peak before its first child.
The corrected Linux launcher peak is **19,513,344 bytes**; later native transformation peaks exceed it.
Both platforms' complete identity cohorts were repeated. The original run
[40775718775](https://github.com/skuirrels/Xsl3/actions/runs/40775718775), its valid functional outputs and
all initial macOS measurements remain retained, but their peaks do not feed the final attribution table.
No canonical-controller source was changed and no peak was baseline-subtracted.

The tool also rejects duplicate sizes, non-finite timeouts, malformed/incomplete phase evidence and
backwards cumulative counters, and cleans up children if interrupted. Twenty local controller/diagnostic
tests pass; the seven diagnostic tests also pass in Linux CI. Full managed/conformance/performance gates
were not rerun as product-change gates because runtime, packaging, canonical fixtures and those baselines
are byte-identical to released 1.1.3. Both platforms' actual NativeAOT controls and instrumented runs passed.

## Deferred work and stopping rule

No runtime allocation/initialization policy was changed. A broader startup/image/GC footprint investigation,
call-site attribution of transient allocation, remaining workloads' retained models and throughput
optimization remain deferred. In particular, no globalization change, tuned GC-policy override, reduced
input, relaxed limit, specialized acceptance image or streaming feature claim is introduced.

All P7E completion criteria have evidence, so implementation stops here. Version **1.1.3 remains the current
package release**. Future runtime changes require their own bounded scope and full regression evidence.

## Evidence and reproduction

Raw local source/artifact hashes, both full matrices, all identity rounds, excluded cohorts and logs are
under `artifacts/p7e-memory/`. This ignored directory is not automatically published. GitHub artifacts are
retained under the linked runs with their recorded expiry; archive the raw bundle if longer retention
is needed. The tracked result snapshot records report hashes, source identity and artifact archive digests.

Publish an unchanged source archive with `eng/publish-stream-probe.sh`, then run each canonical command
with a fresh output directory (the 20 MiB failure is retained as exit 1):

```sh
python3 eng/streaming-gate.py --native <canonical-native> --bytes 209715200 --limit 20971520 --timeout-seconds 900 --output <new-push-directory>
python3 eng/streaming-gate.py --native <canonical-native> --bytes 2147483648 --limit 209715200 --timeout-seconds 900 --output <new-nightly-directory>
python3 eng/diagnose-streaming-memory.py prepare --source <released-source-archive> --saved <canonical-stylesheets> --rid <linux-x64-or-osx-arm64> --output <new-diagnostic-directory>
python3 eng/diagnose-streaming-memory.py measure --native <canonical-native> --output <new-control-directory> --sizes 10485760 209715200 2147483648 --rounds 3 --heap-limit 20971520
python3 eng/diagnose-streaming-memory.py measure --native <diagnostic-native> --output <new-instrumented-directory> --sizes 10485760 209715200 2147483648 --rounds 3 --heap-limit 20971520 --instrumented
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s eng/tests -p 'test_streaming_*.py'
```

The optional manual workflow inputs `memory_diagnosis=true`, `source_ref=v1.1.3` select diagnostics only.
Scheduled/default jobs retain their original definitions and limits. Instrumentation overhead and image
layout differ from the control, so the two images' peak difference is not a product performance claim.
