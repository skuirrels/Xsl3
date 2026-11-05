# P7 Linux CPU diagnostic

The compact producer, tee reclamation and simple-content value-state controls remain unaccepted:
their paired timings did not establish a meaningful throughput benefit. Their allocation reductions
are not evidence that the original seven-workload throughput gate passes.

The earlier `dotnet-sampled-thread-time` trace includes waiting and GC-polling stacks even when its
events are classified as `Managed`. This cannot isolate time spent running on a CPU.

This diagnostic uses .NET 10 `dotnet-trace collect-linux` with `dotnet-common,cpu-sampling,thread-time`.
The kernel emits `Universal.Events/cpu` and context-switch events. It needs Linux 6.4+, USER_EVENTS,
mounted tracefs and root privileges; see the [official profiler documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace#dotnet-trace-collect-linux).
The workflow checks prerequisites and downloads native runtime symbols. Missing prerequisites fail
the diagnostic visibly; it does not silently substitute sampled-thread-time.

## Reproduction and acceptance boundary

Dispatch `streaming-development.yml` at `feature/p7-linux-cpu-profile` with `cpu_profile=true`,
`memory_diagnosis=false`, `allocation_comparison=false`, and
`source_ref=a5e89ac7f6fb630e47872a2bcc297cb010ad36f6`.

The new job builds that immutable source and profiles only its own canonical 1 GB fork streaming
child. Collection is limited to that PID and 30 seconds; child/collector timeouts are bounded.
The benchmark retains the original 4 GiB heap guard, disabled tiering and independent output checks.
No processor-count override is added. Raw command, PID, source revisions, tool/runtime versions,
artifact hashes, CPU configuration, stdout, stderr and the original trace are retained.

`eng/profile-streaming-cpu.py` reports successful capture only when the collector succeeds and the
canonical child's result passes existing input/output validation and the effective heap guard.
Trace existence alone does not prove usable CPU coverage: inspect event counts, loss, symbols and
per-thread stacks before drawing conclusions. Instrumented timing is never throughput acceptance.

Default and scheduled runs retain all original memory and throughput requirements. The CPU job runs
only on explicit dispatch; selecting it with another diagnostic mode is rejected. No runtime code,
feature support claim, expected output, memory budget or elapsed-time target changes here.

## Evidence

The [first hosted capture](https://github.com/skuirrels/Xsl3/actions/runs/42105701535), diagnostic
`5694871` / source `a5e89ac`, succeeded and validated all three outputs over 1,000,000,000 input bytes,
with the actual 4,294,967,296-byte heap allowance. The runner has four logical CPUs / two physical cores.
Parsing found 84,819 `Universal.Events/cpu` samples and no lost events. Native inclusive stacks include
18,951 samples in `ThreadNative_SpinWait` and 21,757 in `__sched_yield`; these are diagnostic counts,
not benchmark improvements. Managed symbol records and context-switch events are absent, so the
capture does not establish attribution to individual managed methods or off-CPU durations.

Artifact `13450953350` (`p7-linux-cpu-profile`) expires 3 February 2027; GitHub reports archive digest
`sha256:29fd06f83fb6417ed197737fd858f58a78dc3df0d80776ea78a603dad9036f1e`.
Local raw trace and parser evidence are under `/private/tmp/xsl3-p7-linux-cpu-20261105/`.
A follow-up retains a capability probe and explicitly enables/preserves JIT perf maps, using the
standard `/tmp` diagnostic directory to avoid an unverified custom-directory discovery path.

Main `a5e89ac` has the same runtime and benchmark source as reviewed contract head `78815e8`; all three
contract PRs passed their own CI before merging. Neither source contains the unaccepted producer,
tee reclamation or simple-content value-state controls.
