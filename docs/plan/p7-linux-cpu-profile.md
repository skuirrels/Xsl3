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
The `cpu_workload` choice defaults to `fork` and can select any of the seven canonical workloads.
Each run still profiles exactly one child. Identity and accumulator can therefore be investigated
independently of fork scheduling, with their original input and output validation.

The new job builds that immutable source and profiles only its own selected canonical 1 GB streaming
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

The [follow-up capture](https://github.com/skuirrels/Xsl3/actions/runs/42106401936), diagnostic `23a8035`
and the same source, also passes all output checks. Its capability probe reports `supportsCollectLinux=true`.
The older reader bundled with dotnet-trace fails on label kind 10; the pinned TraceEvent 4.0.0 reader
in `eng/StreamingCpuReport` reads the retained trace successfully. It reports 70,016 kernel CPU samples,
zero lost events, and 13,799 Xsl3 symbol records. Inclusive sample counts include:

| Frame | CPU samples |
|---|---:|
| `StreamTee.WaitForPublication` | 19,699 |
| `__sched_yield` | 18,652 |
| `ThreadNative_SpinWait` | 11,227 |
| `StreamedInputReader.Read` | 7,043 |
| `ForkHeader.Atomize` | 1,803 |

Inclusive counts overlap and must not be summed. Some stacks remain unresolved. No context-switch
events were present in the reader's event inventory, so off-CPU duration is still unproven. The CPU
evidence identifies source-production and publication waits as useful follow-up areas. The separate
[early-parking control](p7-fork-wait-budget.md) clearly regresses elapsed time and is rejected.

The second artifact is `13451777590`, expires 3 February 2027, and has GitHub-reported archive digest
`sha256:96b889d29fd9fcb03e61dd0b26a1452e2e15057b2707d51265654a5845ed112f`.
It includes the raw trace, JIT maps, probe, child output and hashes. Local parsed evidence is
`/private/tmp/xsl3-p7-linux-cpu-20261105/qualified-cpu-sites.json`. Reproduce with:

```sh
dotnet run --project eng/StreamingCpuReport -c Release -- path/to/cpu.nettrace > cpu-sites.json
```

Main `a5e89ac` has the same runtime and benchmark source as reviewed contract head `78815e8`; all three
contract PRs passed their own CI before merging. Neither source contains the unaccepted producer,
tee reclamation or simple-content value-state controls.


## Independent identity and accumulator profiles

[Identity run 42109632792](https://github.com/skuirrels/Xsl3/actions/runs/42109632792) and
[accumulator run 42109637672](https://github.com/skuirrels/Xsl3/actions/runs/42109637672) both
succeed at controller `5a7760f`, immutable source `a5e89ac`. Each validates the original 1 GB
input and output, with four host CPUs, no processor override, disabled tiering and the actual
4 GiB heap allowance. Both use runtime SHA-256
`68394e63c2f2e27c7fae6f23df54a0a3149e7ed055715a028718b50eae9b67c9`.

| Evidence | Identity | Accumulator |
|---|---:|---:|
| Kernel CPU samples | 26,875 | 25,889 |
| Samples with an Xsl3 frame | 26,827 | 25,854 |
| Reported lost events | 0 | 0 |
| Inclusive `StreamedDocument.ReadNextCore` | 11,211 | 10,886 |
| Inclusive `StreamedInputReader.Read` | 7,303 | 6,000 |
| Inclusive `StreamedDocument.Child` | 1,151 | 933 |

Identity also spends 7,292 inclusive samples in `XmlQueryOutput.StartCopy`; accumulator spends
11,473 in `StreamedAccumulators.Visit`. Inclusive counts overlap. Many framework leaf frames
remain unresolved, so reader-wrapper attribution includes its underlying parser and does not
prove that wrapper dispatch itself is the bottleneck. Instrumented elapsed times are not acceptance.
The older metadata bypass and token-kind caching controls in the completion history already failed
to establish reliable gains; these profiles are not a reason to repeat them unchanged.

Artifacts `13454560274` (identity) and `13454325230` (accumulator) expire 3 February 2027.
Their GitHub archive SHA-256 digests are, respectively:

- `b94b5cf030f8c3b6a057da9b8d949b4ee97cb6670a0d7b5ef22e9c7c180b2216`
- `8f79cf0a513a36154e684d54d7f6f9235e923184df97cb68b644a581a856d6fb`

Each includes the raw trace, JIT maps, source revisions, child report and CPU analysis. Local copies
are under `/private/tmp/xsl3-p7-linux-cpu-20261105/{identity,accumulator}/`.

## Supplemental framework-symbol capture

The identity trace from run `42109632792` has 5,877 unnamed-leaf samples under the source reader's
first-child/next paths. Its retained perf map names only one `System.Private.Xml.XmlTextReaderImpl`
method (`GetResolver`), so it does not identify the parser operations behind those samples. This is
a symbol-coverage gap, not evidence that wrapper dispatch accounts for that time.

The optional `cpu_jit_framework=true` workflow input maps to `--jit-framework` in the CPU diagnostic
only. It sets `DOTNET_ReadyToRun=0` for the measured child, removes a conflicting `COMPlus_ReadyToRun`
value and records the effective variables in the report. The collector retains its ordinary environment.
Without this option, the child environment is unchanged. Normal memory and throughput jobs never
receive this override; their original runtime policy and requirements remain unchanged.

The runtime's [framework-symbol guidance](https://github.com/dotnet/runtime/blob/main/docs/project/linux-performance-tracing.md#resolving-framework-symbols)
distinguishes precompiled framework code from JIT-generated maps. Microsoft's
[JIT explanation](https://devblogs.microsoft.com/dotnet/performance_improvements_in_net_7/) documents
`DOTNET_ReadyToRun=0` as a way to compile framework methods rather than use their precompiled code.
This supplemental capture can expose method names, but changes framework compilation and startup.
Its CPU proportions must not be treated as identical to the original policy, and its elapsed time
cannot be used as acceptance or claimed as an application optimization. Any hypothesis still needs
controlled measurement under the unchanged original benchmark policy.

The controller is isolated on `feature/p7-framework-cpu-symbols`. The first requested runtime source
is corrected discard candidate `a8d322ac6bfd094c65a61477a519d0a426059c77`, workload `identity`.
Require a valid original 1 GB output, actual heap guard, successful collection, recorded loss count
and visible parser/framework symbols before relying on the capture. Python parsing, CLI help and
workflow YAML parsing pass locally.

[Run 42124527290](https://github.com/skuirrels/Xsl3/actions/runs/42124527290) completes successfully
at controller `e6317411e80f72238f827b52b0315b7901b4e8b0`, with that exact runtime source. The independent
audit validates the original 1 GB output, raw stdout, actual 4 GiB heap allowance, all four retained
binary hashes and 409 tracked library PDB source hashes (three generated documents excluded).
The report records four host CPUs, no processor override, disabled tiering and the requested
framework-JIT policy. It has **26,343 CPU samples**, 26,173 with engine frames and zero reported loss.
There are **213 `XmlTextReaderImpl` symbol records**, resolving the previous framework-name gap.

Aggregated leaf samples include 1,517 in `StreamedDocument.ReadNextCore`, 511 in `NameTable.Add`,
376 in `XmlTextReaderImpl.ParseAttributes`, and 303 in its parameterized `ParseText`. Under the
reader path, parsing, name interning and input generation account for substantial resolved work;
the wrapper's inclusive count does not measure dispatch overhead alone. The unchanged output hash
sink also contributes 3,858 leaf samples. These counts describe this instrumented compilation policy,
not removable elapsed time or an acceptance improvement. Existing rejected metadata bypass,
token-kind caching and source-dispatch extraction controls are not validated by this capture.

Artifact `13460154609` expires 3 February 2027. Its archive SHA-256
`0def64170f62360214cf7340c6949c8fa08cf9884d92bd9e895e7948f53d17d7` was independently verified.
Runtime SHA-256 is `48104685f3530ed28ba5f66304e410221e39e1d27019de5ebeecf7a8aba923ea`.
Raw trace, perf map, child report, original outputs and independent `review.json` are retained under
`/private/tmp/xsl3-p7-framework-cpu-20261105/`. The original memory and throughput gates remain separate.
