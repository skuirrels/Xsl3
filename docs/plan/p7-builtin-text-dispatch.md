# P7 built-in text dispatch control

This isolated control starts from integration `32c845d`, on
`feature/p7-builtin-text-dispatch`. It does not include the rejected fork-name cache.
Full P7 remains open; this is a performance experiment, not release qualification.

## Hypothesis and boundary

The canonical identity and source-document stylesheets repeatedly enter a mode dispatcher
for text even though their compiled template patterns can match only other node kinds.
The existing Linux identity CPU diagnostic attributes samples to apply-templates and copy
operations. The control tests eliminating that dispatch work without changing input navigation.

For streamed compilation, the existing matcher collects effective patterns, including imports,
all-mode rules and next-match restrictions. Only a shallow-copy mode with neither text patterns
nor heterogeneous patterns qualifies. A conditional at eligible call sites uses the existing
`XsltCopy` operation for text and ordinary dispatch for every other node. It deliberately retains
copy semantics: replacing this with text construction would lose disable-output-escaping metadata
on grounded temporary text. Explicit ordinary parameters, debugging, tracing and nonzero recursion
limits retain normal dispatch. Computed implicit arguments are bound outside the conditional,
including tunnel frames, to preserve evaluation.

No stylesheet names, input values, benchmark workload identifiers, output fingerprints, thresholds,
reader behavior or gate settings are embedded in the optimization. The implementation belongs to
the compiler and uses the existing matcher's classification rather than parsing patterns again.

## Evidence collected on 6 November 2026

Local evidence root: `/private/tmp/xsl3-p7-builtin-text-dispatch-20261106/`.

- The first over-conservative prototype passed 140 new cases but emitted no shortcut for either
  canonical workload: implicit tunnel-clear calls failed its argument guard. Its results and QIL
  are retained under `candidate-focused` and `initial-qil`. They establish no performance effect.
- The revised candidate emits text-copy branches in optimized streaming QIL: identity grows from
  140 to 167 graph nodes; source-document from 218 to 238. Corresponding tree graphs remain 124
  and 210 nodes. More graph nodes are not a speed claim; paired execution decides adoption.
- Affected Release and Debug runs pass 725 .NET 10 and 672 .NET 8 checks each, 2,794 total.
  These include 320 instances of eight new scenarios across API, saved-loading, debug and forward/tree
  combinations: mixed content, text rules, heterogeneous predicates, all-mode rules, current mode,
  raw text, ordinary parameters and tunnel parameters. Existing dispatch-position, serialization,
  source-document, initial-mode, template-parameter and optimizer tests are included.
- Frozen assemblies, source manifest, runtime patch, raw TRX and validation scripts are retained.
  The initial sandbox run could not bind test-host sockets and was stopped; its log is preserved.
  The successful runs used the local test-host permission already required by the repository tests.
- The unchanged canonical consumer completed the five-workload paired comparison: three rounds,
  both engines/modes, 100,000,000-byte inputs, four processors, tiering off, ordinary ReadyToRun and
  the actual 4 GiB managed heap guard. All 60 outputs match the original qualification fingerprints. Both engines have 409 matching tracked PDB sources; consumer/dependency binaries are identical between variants.

The validation audit checks every TRX row and execution ID, all four frozen engine/test sets and
all tracked PDB documents. All 320 new semantic cases also pass against the frozen integration
engines in both configurations/frameworks. These tests preserve existing behavior; they do not
claim an existing correctness defect.

Local paired streaming results (milliseconds):

| Workload | Baseline median | Candidate median | Candidate / baseline | Sample ranges overlap |
| --- | ---: | ---: | ---: | --- |
| identity | 2086.595 | 2052.368 | 0.983597 | No; every candidate faster |
| accumulator | 2042.900 | 2030.704 | 0.994030 | Yes |
| merge | 3334.520 | 3278.543 | 0.983213 | Yes |
| fork | 1517.498 | 1518.310 | 1.000535 | Yes |
| source-document | 2097.107 | 2056.598 | 0.980684 | No; every candidate faster |

Identity baseline samples: 2086.419875, 2102.605292, 2086.594542;
candidate: 2051.659500, 2053.083792, 2052.367875.
Source-document baseline: 2097.106958, 2101.844291, 2087.453708;
candidate: 2052.500458, 2056.598208, 2064.991750.
The candidate's identity/source-document stream/tree ratios remain 1.030554/1.002658.
Other workloads show no meaningful winner. The local result justifies one paired Linux comparison,
not adoption or an original full-gate rerun.

Baseline engine SHA-256: `4a454adc501c00eace78bb495d62182ea45979ecb3df9bc730fc70ab40bc43d1`.
Candidate: `65285fd3386b251f14ceb55535a080d7c55f3f5bdeeee9aa33fa6088c6b82968`.
Audits: `validation-review.json`, `baseline-contracts-review.json`, `comparison-review.json`.
Executable audit scripts and the frozen QIL inspection engine are retained in the same evidence root.

## Decision criteria

Inspect all TRX rows and PDB source hashes, then all 60 paired observations, binary hashes, raw
outputs and original fingerprints. Adopt nothing based on generated code or correctness alone.
Only a useful repeatable local gain justifies a target-platform comparison; otherwise close this
control without adoption. A useful target-platform result would still require final-source original
memory/throughput qualification and complete integration review. All original P7 obligations remain.
