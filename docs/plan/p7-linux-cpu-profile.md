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

Pending the first hosted capture. Main `a5e89ac` has the same runtime and benchmark source as reviewed
contract head `78815e8`; all three contract PRs passed their own CI before merging. Neither source
contains the unaccepted producer, tee reclamation or simple-content value-state controls.
