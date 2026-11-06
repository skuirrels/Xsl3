# P7 serialization annotation control

This isolated control starts from integration `402cbed`, whose runtime is byte-identical to
`32c845d` and `6c24a92`. It is not adopted into PR #40. Original full-P7 completion criteria,
canonical consumers, inputs, output oracles and gate thresholds remain unchanged.

## Hypothesis and implementation

The retained Linux identity framework-symbol diagnostic contains 356 inclusive samples for
`XmlQueryOutput.CopyAttributeAnnotation` among 26,343 CPU samples, with zero lost events.
That diagnostic disables ReadyToRun to resolve framework symbols and includes warmup; these
counts are not removable elapsed time or proof of a throughput benefit.

When an attribute's type is stripped, the existing method queries its ID/IDREF properties
before checking whether the output writer can retain those properties. Serialization cannot
retain that metadata. The control checks `AnnotatingWriters.Of(Writer)` first and uses that
same writer for both flags. The existing typed-copy branch is unchanged. Annotating temporary
trees, raw-result construction, XML ID normalization and dynamic construction checks retain
their existing paths. No compiler specialization or workload-dependent condition is added.

The change is confined to `src/Xsl3/Xsl/Runtime/XmlQueryOutput.cs`. No runtime domain literal,
fixture-derived value, source-reader policy or output oracle changes.

## Correctness evidence

All **7,698** selected checks pass: **2,184 .NET 10 /1,665 .NET 8** in each of Release and Debug,
with no failures or skips. The selection covers copied-attribute identity, direct/raw attribute
copying, serialization, saved typed nodes, schema contracts, streamed copies, validated
accumulator copies, secondary results and XSLT 2.0/3.0 contracts. Existing tests exercise
preserved and stripped ID/IDREF metadata, constructed trees, reader lifetime, namespace fixup,
XML ID normalization, duplicate attributes and invalid construction errors through public APIs
and saved styles. This is affected-contract coverage, not full compatibility qualification.

The audit checks every TRX result and unique execution ID, freezes all four engine/test binary
sets, and verifies the tracked source hashes in all eight PDBs, including 409 runtime sources
per engine. Raw logs, results, immutable source manifest, runtime patch, binaries and executable
auditors are retained under `/private/tmp/xsl3-p7-serialization-annotations-20261106/`.

## Performance decision

The local comparison completes all **60** observations: five workloads, tree and stream modes,
three alternating rounds per runtime. All original fingerprints match, all actual managed guards
are 4,294,967,296 bytes, and both engines' 409 tracked PDB source hashes verify. The frozen
consumer and dependencies are identical between variants. Host: .NET 10.0.14, macOS 27.0.1
ARM64, four reported processors, tiering disabled and normal ReadyToRun policy.

| Workload | Baseline stream median (ms) | Candidate stream median (ms) | Candidate / baseline | Winner |
|---|---:|---:|---:|---|
| Identity | 2,103.288166 | 2,087.447292 | 0.992469 | No meaningful winner |
| Accumulator | 2,053.676875 | 2,041.354625 | 0.994000 | No meaningful winner |
| Merge | 3,310.735542 | 3,307.058584 | 0.998889 | No meaningful winner |
| Fork | 1,523.261417 | 1,507.938000 | 0.989940 | No meaningful winner |
| Source-document | 2,108.532709 | 2,081.486917 | 0.987173 | No meaningful winner |

Every baseline/candidate sample range overlaps. Identity remains at 1.043438 stream/tree and
source-document at 1.025936 on this host. Their slightly lower medians do not establish a reliable
benefit. This control is **closed without adoption**; no Linux comparison or full gate rerun is
justified. Retain the source and complete evidence on this branch, and do not repeat the unchanged
control to obtain favorable medians.

Baseline engine SHA-256: `4a454adc501c00eace78bb495d62182ea45979ecb3df9bc730fc70ab40bc43d1`.
Candidate engine SHA-256: `7c160f3bff5fa93420521ea0f8615c1903631fc0425b9f352f0d94eb7fa4c0e3`.
See `validation-review.json`, `validation-coverage.json`, `comparison-review.json`, all raw child
outputs and the executable auditors under the evidence directory above. The historical Linux
profile remains at `/private/tmp/xsl3-p7-framework-cpu-20261105/`.

Integration and draft PR #40 remain at `402cbed`, excluding this runtime change. Original final-source
memory/throughput qualification, supported claims, review/merge and matching publication remain
required. A separate unchanged-source Linux merge CPU diagnostic is running at
[42160664076](https://github.com/skuirrels/Xsl3/actions/runs/42160664076), using integration `402cbed`;
that diagnostic is not an acceptance rerun or evidence for adopting this control.
