# P7 current-mode compilation control

This isolated branch starts from integrated qualification head
`6c24a9270e5e1ca589a73e867beb381d9ff8950e`. Full P7 remains active. This control does
not change any original gate, supported-feature claim, canonical consumer or output oracle.

## Finding and change

The real optimized QIL for the canonical streamed identity workload contains an unused
`X31TunnelMode` call and binding. Its tree counterpart removes them. Streamed plans deliberately
disable general iterator elimination because moving expressions can reorder forward reads.

`ApplyInCurrentMode31` now creates the current-mode binding only when an alternative dispatch
target actually exists. With one target it returns that invocation directly. With multiple
targets it retains the lookup and conditional dispatch. Target discovery, fallback selection,
argument copying, next-match handling and the streaming optimizer policy are unchanged.
The helper reads an internal tunnel frame; it does not read the source or perform a cancellation
checkpoint. Nested function generation restores the caller's tunnel before returning.

The canonical identity tree plan remains 124 nodes; its streamed plan falls from 140 to 134
and contains no mode lookup. Source-document still needs multiple-mode dispatch: its tree/stream
plans remain 210/218 nodes, with the lookup retained. These are actual compilation and optimization
results, not `CheckOnly` plans or evidence of an elapsed-time improvement.

## Regression evidence

The new contracts cover direct application, named-template calls, next-match, unnamed and named
modes, both public APIs, tree/forward inputs and Release/Debug compilation. Saved .NET 10 assemblies
cover both compiler configurations. The named-template fixture explicitly copies its small context
before calling the template, preserving a valid streamability contract while testing current-mode
propagation through both the focus change and the call.

On unchanged runtime source, the corrected fixtures pass all 102 behavior checks and fail the four
expected plan assertions. The initial invalid named-template fixture and its failures are retained
separately; they are not attributed to the runtime. After the change, all 106 focused checks pass.

Broader current-mode, initial-mode, XSLT 2/3, mode-declaration, optimizer, forward-execution,
streamability-declaration, iterate and source-document regressions pass 584 .NET 10 /570 .NET 8
checks in each of Release and Debug: **2,308 passed, none skipped**. Every result row and 1,594
source hashes were checked. All four engine/test binary sets are frozen; each engine PDB verifies
409 tracked runtime source hashes against the source manifest.

Evidence root: `/private/tmp/xsl3-p7-current-mode-lowering-20261106/`. It contains baseline and
candidate TRXs/logs, `source-manifest.json`, `runtime.patch`, `validation-review.json`,
`pdb-review.json`, `frozen/`, and the canonical `qil/` reports. Release .NET 10 engine SHA-256:
`565808065cc8f4eab0c3e7f222848912d3178dc5bfc06ad163935e0a4179153d`.

## Timing and decision

The paired local diagnostic completed against frozen integrated baseline engine
`4a454adc501c00eace78bb495d62182ea45979ecb3df9bc730fc70ab40bc43d1`, using the unchanged canonical
consumer. It measures identity, accumulator, merge, fork and source-document at 100 MB, three
rounds, both execution modes and both engines. The actual heap guard is 4 GiB, processor count is
four, tiering is disabled, and ordinary framework ReadyToRun is retained. All 60 raw observations,
original output fingerprints, consumer/dependency hashes and 409 tracked PDB source hashes per
engine independently verify in `comparison-review.json` using the retained `audit-comparison.py`.

| Workload | Baseline streaming median, ms | Candidate streaming median, ms | Candidate /baseline | Winner |
|---|---:|---:|---:|---|
| Identity | 2,101.864 | 2,080.139 | 0.989664 | No meaningful winner; ranges overlap |
| Accumulator | 2,051.122 | 2,037.322 | 0.993272 | No meaningful winner; ranges overlap |
| Merge | 3,317.032 | 3,319.081 | 1.000618 | No meaningful winner; ranges overlap |
| Fork | 1,516.593 | 1,533.694 | 1.011276 | No meaningful winner; ranges overlap |
| Source-document | 2,092.973 | 2,100.901 | 1.003788 | No meaningful winner; ranges overlap |

The lookup is demonstrably removed, but no useful elapsed-time improvement is established.
Candidate identity, fork and source-document still exceed their tree medians in this diagnostic.
**Close this control without adoption.** Retain its source, tests and evidence on
`feature/p7-current-mode-lowering`; do not add it to the integrated candidate or repeat a hosted
comparison/full gate run on the strength of this result. A compiler simplification alone does not
resolve the remaining original throughput failures. No original acceptance requirement is waived.

The earlier fork value-event control is closed without adoption on
[`feature/p7-fork-value-events`](https://github.com/skuirrels/Xsl3/blob/feature/p7-fork-value-events/docs/plan/p7-fork-value-events.md),
head `e0ad958`. Its runtime is excluded here. See the
[integrated qualification criteria](p7-integrated-qualification.md) for the full completion boundary.
