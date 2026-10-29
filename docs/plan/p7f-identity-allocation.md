# P7F: identity-streaming allocation reduction

Scope frozen on 2026-10-29. Branch: `feature/p7f-identity-allocation`.
Baseline: merged P7E audit `59a08e925badf116dfd79a7c1520986e16069935`; runtime equals release 1.1.3.
P7E PR #22 is merged after all four ordinary CI checks passed in run 40781096266.

**P7F is complete.** The [close-out](../p7f-identity-allocation-2026-10-29.md) and
[result snapshot](p7f-results.json) record the single-hotspot correction, all six passing comparisons,
full unchanged memory matrices, passing functional/performance gates and remaining limitations.
PR #23 contains the 1.1.4 candidate; release publication remains separate.

## Scope and completion criteria

Profile the existing identity workload, attribute transient allocation to a concrete shared runtime
hotspot, and optimize that one hotspot. Retain the original source, saved entry points, inputs,
independent output oracle, globalization, budgets and complete process lifetime measurements.
Diagnostics remain separate from canonical acceptance artifacts.

1. Preserve a reproducible baseline and allocation attribution before changing runtime code.
2. Reduce median transformation allocation per input byte by at least 20% at each existing input size
   (10,485,760; 209,715,200; 2,147,483,648 bytes), using three fresh processes per variant and platform
   (hosted Linux x64 and local macOS ARM64). Compare matching isolated instrumentation; exclude warmup
   and the oracle from transformation allocation. Retain every raw run and validate full input/output.
3. Verify uninstrumented throughput with three fresh processes per variant/size in alternating order;
   no more than 5% slower median at any size/platform, using the ordinary performance gate's tolerance.
4. Rerun both complete 16-workload memory matrices on both platforms with their original thresholds.
   Preserve failures explicitly; meeting Linux's separate 20 MiB target is not this phase's exit criterion.
5. Pass managed Release/Debug on .NET 8 and 10, upstream/differential/conformance gates, reviewed S1/S2,
   strict/unstreamed baselines, saved-style/NativeAOT checks and the ordinary performance gate.
6. Review the bounded diff, add focused behavioral regression coverage, update consumer documentation
   and publish a close-out with exact source/artifact provenance and before/after results.

## Stopping rule and release boundary

Stop after one confirmed hotspot and the criteria above. No new streaming construct, public API,
saved-format change, generalized pooling, GC-policy tuning, startup-footprint investigation or
`supports-streaming=yes` claim belongs here. Record unrelated findings for later work. If attribution
does not yield a safe qualifying optimization, stop with the evidence and explicit unmet criteria;
do not expand into another hotspot to manufacture a patch.

A validated consumer-facing improvement can become a 1.1.4 candidate. This phase does not authorize
tagging or publishing a release, and does not reopen full P7.
