# P7 fork throughput: rejected reclamation-lock control

**Rejected, 5 November 2026. Do not merge or adopt this runtime control.** The full P7 objective remains active.
This experiment starts from contract head `78815e8f6c85e7c8cc92e7f2d81751934383ff67` on
`feature/p7-tee-reclamation`. Its runtime change is isolated from PRs #34, #35 and #36.

## Motivation and implementation

A local .NET sampled-thread-time trace of the canonical 1 GB fork workload showed substantial sampled
time in monitor entry and publication waits. This includes waiting and GC pauses; it is not CPU-only
attribution or an acceptance throughput result. The traced output was correct, with the actual 4 GiB heap
guard, but instrumentation and default tiering distinguish this trace from the uninstrumented controller.

The control separated consumed-slot reclamation from the publication monitor. It retained immediate
prefix release, the original ring capacity, aliases, detachment, cancellation and caller-owned source reads.
Reclamation published cleared slots before allowing reuse. Space waiters and production requests registered
before rechecking availability, with full fences to prevent both sides missing the other's notification.

The first version used insufficient ordering and failed full-ring liveness controls. Its completed .NET 10
run had 362 passes / 3 failures; the subsequent .NET 8 run was stopped after failures because the corrected
version was ready. Failed logs and reconstructed first-version source are retained. After the correction,
all 14 targeted liveness/payload checks and all 693 affected Release fork/tee checks passed (.NET 10: 365;
.NET 8: 328). This is not full Debug, native, hosted or complete-suite qualification.

## Paired diagnostic

The baseline binaries were preserved before any runtime edit. The existing streaming benchmark controller
ran three rounds per variant/mode over the canonical 100 MB fork workload, with four effective processors,
tiering disabled and the actual 4,294,967,296-byte heap guard. Variant order alternated between rounds.
All twelve observations returned valid output with identical byte counts and hashes; no failures or outliers
were removed. The same controller and corpus were used for both variants.

| Measurement | Baseline | Reclamation control | Winner |
|---|---:|---:|---|
| Tree median | 1,496.128 ms | 1,492.401 ms | No meaningful difference |
| Stream median | 1,946.588 ms | 1,935.878 ms | No meaningful difference |
| Stream range | 1,937.063–1,954.867 ms | 1,906.486–1,944.065 ms | Overlapping ranges |
| Stream/tree ratio | 1.301083 | 1.297157 | Both miss the original target |

The observed stream improvement is 0.55%. It does not justify synchronization complexity, and the local
original throughput target remains unmet. No Linux or NativeAOT qualification was started for this rejected
control. A narrow local diagnostic does not replace the seven-workload, three-size acceptance matrix.

Runtime assembly SHA-256:

- Baseline: `60ef17ce25517b7f32d2a8f50df3a6477f03efea4e3e36123f972b7f054ed049`.
- Corrected control: `460786e8501874e3c9031861757d467d284456a70d049896c92822b98195ff52`.

Raw evidence: `/private/tmp/xsl3-p7-fork-profile-20261105/`, including source/runtime manifests,
the trace and stack summaries, both binary trees, failed and passing tests, the corrected patch,
six controller reports and `paired-review.json`. Temporary artifacts are not durable hosted acceptance
evidence. The isolated branch retains the corrected source and this rejection record.

Next investigate producer allocation/parsing costs with fresh evidence. The earlier parking-only control
was also rejected after a hosted slowdown (see the historical completion log); changing wait policy again
without stronger evidence is not the next step. Full P7's remaining evidence and original performance
requirements remain unchanged.
