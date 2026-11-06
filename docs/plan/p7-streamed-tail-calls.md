# P7 streamed writer tail-call control

This isolated control starts from `18fc9a5`, whose runtime matches corrected discard `a8d322a`.
It excludes the separately tested short-polling control and the public-selection integration.
The original memory and throughput gates remain unchanged.

## Hypothesis and boundary

The corrected-discard framework-symbol CPU diagnostic records generated template-dispatch work
alongside the source-reader and serialization costs. Source inspection shows that the streamed
optimizer's conservative pattern set also disables `TailCallAnalyzer`, although that analyzer only
marks existing writer calls. It does not rewrite their operands or introduce a cache. This identifies
a possible unnecessary call-frame cost; the profile does not quantify a removable percentage.

The candidate enables that existing analysis for streamed graphs only when the caller requests
optimization. Debug stylesheet compilation keeps its frames. The existing analyzer still limits
eligibility to writer calls in tail position, excluding iterator-result calls and multi-item loops.
The normal argument evaluation, recursion-depth handling and IL emitter are unchanged. The Debug
streamed-read verifier still runs after the analysis. No inlining, hoisting, duplication, indexing,
new reader operation or public setting is enabled by this change.

## Required evidence

1. Existing optimizer-policy, recursive-function, public-entry and physical read-boundary contracts
   pass, followed by broader Release/Debug .NET 8/10 compatibility if the timing control is useful.
2. Verify eligible generated calls and preserve Debug behavior; do not infer effective generated-code
   changes merely from enabling an optimizer flag.
3. Compare through the unchanged canonical consumer: identity, accumulator, merge, fork and
   source-document, both modes, three alternating rounds, exact 100 MB and actual 4 GiB heap guards.
   Local timing alone cannot justify adoption or claim original hosted acceptance.
4. A useful candidate must retain all original memory, throughput, saved/native, conformance, final
   integration, review, CI and release requirements. Preserve any failing or inconclusive controls.

## Initial validation

All **172 .NET 10 Release** optimizer/verifier, recursive-function, initial-mode and read-boundary
cases pass, with every TRX result independently checked against its counters. The source patch,
immutable baseline and changed-file hash are retained under
`/private/tmp/xsl3-p7-streamed-tail-calls-20261106/`.

## Completed control: no adoption

All **60 local observations** validate, using the same frozen consumer, actual 4 GiB guards,
normal framework policy and four effective processors. The streaming medians are:

| Workload | Baseline (ms) | Tail-call control (ms) | Candidate / baseline | Winner |
|---|---:|---:|---:|---|
| identity | 2,127.043 | 2,129.453 | 1.001133 | No meaningful winner |
| accumulator | 2,077.981 | 2,073.855 | 0.998014 | No meaningful winner |
| merge | 3,398.047 | 3,389.500 | 0.997485 | No meaningful winner |
| fork | 1,533.590 | 1,529.670 | 0.997444 | No meaningful winner |
| source-document | 2,130.226 | 2,141.532 | 1.005307 | No meaningful winner |

A separate saved-IL diagnostic compiles the exact canonical stylesheet generator through both frozen
engines, decodes instruction boundaries and counts actual `tail.` prefixes. Baseline counts are zero.
The candidate emits two for identity, one for accumulator, one for merge, none for fork and three for
source-document. Every Debug compilation still emits zero. Thus the flag changes eligible generated
calls, but the paired measurement establishes no useful elapsed-time improvement.

This control is **not adopted** and does not justify a hosted performance or full qualification run.
The runtime patch remains isolated on `feature/p7-streamed-tail-calls`; broader compatibility and
footprint qualification are intentionally not claimed. Retain the source patch, focused TRX,
frozen comparison variants, raw reports, saved assemblies and `emission-{baseline,candidate}.json`
under the evidence directory above. Do not combine this change with public selection or polling.
