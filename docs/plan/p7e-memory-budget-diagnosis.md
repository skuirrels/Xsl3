# P7E: streaming memory-budget diagnosis

**Scope frozen on 2026-10-28. Branch: `feature/p7e-memory-budget-diagnosis`.**
Baseline: release `v1.1.3` (`7bf3dd67a71fe0d77dc2fca1f0264ff8b343c4bf`).
P7D is closed. This phase does not resume full P7 or claim supported streaming.

## Fixed scope

Reproduce the existing 16-workload NativeAOT memory matrices on the released source:

- 209,715,200 input bytes / 20,971,520-byte absolute RSS and managed-heap guard.
- 2,147,483,648 input bytes / 209,715,200-byte absolute RSS and managed-heap guard.

Use the unchanged canonical controller, source generator, output oracle, generated saved entry points,
globalization and full-lifetime process counters. Preserve original raw logs, source/artifact hashes,
all failed runs, timeouts and cases rejected before measurement. A startup failure is not a completed
transformation. Neither startup subtraction nor a diagnostic run can satisfy an acceptance gate.

**Identity is the only investigation/optimization target.** Distinguish fixed runtime overhead from
memory increasing with input using fresh processes, phase snapshots and multiple input sizes. At most
one evidenced shared allocation/initialization cause may be corrected. The complete 16-workload matrices
remain regression controls; their other failures do not expand implementation scope.

## Completion criteria

1. Obtain complete, inspectable v1.1.3 baseline matrices on hosted Linux x64 and local macOS ARM64;
   identify input completion, independent output validation, absolute peak RSS, managed memory,
   process exit status and measurement provenance for every case. Retain the earlier scheduled run
   separately: [40625350353](https://github.com/skuirrels/Xsl3/actions/runs/40625350353).
2. Produce a reproducible identity diagnosis separating initialization, transformation and output-oracle
   effects, including live/committed managed memory and native/process footprint where available.
   Keep instrumentation separate from acceptance artifacts and record its measurement effects.
3. If there is a generally correct fix to one confirmed shared cause, implement only that correction,
   preserve behavior and rerun both complete matrices, managed Release/Debug, upstream/differential,
   ordinary conformance, saved-style/NativeAOT and ordinary performance gates. Preserve original
   failures and thresholds. Otherwise, conclude with a sourced diagnostic report and explicit blockers.
4. Review the bounded diff and publish a concise close-out/result snapshot with exact provenance,
   before/after measurements when applicable, limitations and deferred work. Update the plan's current
   status. Record all newly discovered unrelated findings for later phases.

## Stopping rule and release boundary

Stop when the baseline, identity diagnosis and applicable regression evidence are complete. There is
no requirement to make every P7 memory case green to close this diagnostic phase, and no permission
to weaken an acceptance limit. An audit-only outcome closes P7E without manufacturing a patch release.
A useful consumer-facing runtime correction may become a 1.1.4 candidate after review and CI; release,
tagging and publication remain separately authorized actions.

Throughput optimization, new streaming constructs, general retained-model repair, schema/API expansion,
public API or saved-format changes and `supports-streaming=yes` remain outside this phase. Existing
throughput measurements from the unchanged experimental workflow are retained as deferred evidence.

## Initial execution

A fresh unchanged experimental workflow was dispatched on the immutable release tag:
[40774600310](https://github.com/skuirrels/Xsl3/actions/runs/40774600310). Its two memory jobs provide
hosted Linux x64 baseline evidence. The workflow's separate throughput job does not expand P7E scope.
Local evidence is retained under `artifacts/p7e-memory/`; it is not automatically published.
