# P7 built-in text dispatch control

This isolated control starts from integration `32c845d`, on
`feature/p7-builtin-text-dispatch`. It does not include the rejected fork-name cache.
**Closed without adoption, 6 November 2026.** Full P7 remains open. Do not integrate this runtime control.

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
An independent rerun of the same inspection tool with each frozen engine also confirms that both
optimized tree graphs are byte-for-byte identical (`qil-proof/review.json`). The shortcut is present
only in the streaming graphs; the inspection tool invokes the real optimizer with CheckOnly unset.

## Target-platform comparison

[Linux run 42156324228](https://github.com/skuirrels/Xsl3/actions/runs/42156324228) compares
candidate `76174b918e64e3595869404a27d00bd24afcfcbe` against integration
`32c845daf89818aeb4950e7d549a34fa3cef18e9`. The run completed successfully; its artifact and all 60 observations are independently audited. Its evidence destination is `/private/tmp/xsl3-p7-builtin-text-dispatch-linux-20261106/`.
Artifact `13474983014` (expires 4 February 2027) has verified ZIP SHA-256
`03da0d5a20016666fb64b39d91b4530a5d68d5b2432d33a8e980ba2fe65bb827`.
Both revisions, all retained binary hashes, 409 tracked PDB sources per engine, all raw result rows,
exact population, actual 4 GiB guard and original output fingerprints verify. The unchanged consumer
and dependencies are identical between variants. Final audit: `review.json`, reproduced by `audit.py`.

| Linux workload | Baseline median ms | Candidate median ms | Candidate / baseline | Candidate stream / tree |
| --- | ---: | ---: | ---: | ---: |
| identity | 5016.955 | 5193.729 | 1.035235 | 1.212366 |
| accumulator | 5454.685 | 5204.764 | 0.954182 | 0.976800 |
| merge | 9800.002 | 9890.198 | 1.009204 | 1.012731 |
| fork | 4270.114 | 4941.784 | 1.157296 | 1.481634 |
| source-document | 5078.075 | 5024.154 | 0.989382 | 1.127661 |

Every workload's three-sample ranges overlap. Identity baseline samples are
5448.534085, 4944.256918, 5016.955112; candidate samples 5228.578784, 4915.004810, 5193.729274.
Source-document baseline samples are 5037.610408, 5260.575953, 5078.075102;
candidate samples 4859.312891, 5024.153665, 5367.489406. No observation is excluded.
The local improvement did not reproduce reliably; neither target meets the original throughput
requirement here. The evidence does not justify adoption or another run of this unchanged control.

Linux engine hashes: baseline `695ba1b01ea913142d79bc96be7bfcf06bef4d7fab93e241df04a365f00fd720`,
candidate `8877b6de9c48d2d5fed5a3316dfe408a604a7c1d6a7a410b8a0355f5a03cefde`.
Integration `32c845d` and draft PR #40 remain unchanged.

## Decision criteria

Inspect all TRX rows and PDB source hashes, then all 60 paired observations, binary hashes, raw
outputs and original fingerprints. Adopt nothing based on generated code or correctness alone.
Only a useful repeatable local gain justifies a target-platform comparison; otherwise close this
control without adoption. A useful target-platform result would still require final-source original
memory/throughput qualification and complete integration review. All original P7 obligations remain.
