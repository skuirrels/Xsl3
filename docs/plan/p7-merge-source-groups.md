# P7 deferred merge source groups

## Scope and evidence

This control branches from `9a5948d` on `feature/p7-merge-source-groups`. Its runtime baseline is
integration `402cbed` (unchanged since `6c24a92`). The audited [merge framework profile](p7-merge-snapshot-profile.md)
attributes 302 inclusive samples to immutable-list construction under `X31MergeNextGroup`.
That is diagnostic evidence for investigating group construction, not an elapsed-time guarantee.

A forward merge currently constructs a list for every declared source and an immutable XDM array
for every group, even when the action only requests the combined group. The control records source
counts alongside the existing combined group. `MergeGroup.Sources` materializes the existing XDM
array representation when requested, caching it with atomic publication. Equal-key rows remain
contiguous by source because the existing heap breaks ties by source and then anchor.

Materialized merges retain their existing eager representation. Public helper signatures and saved
assembly calls are unchanged. Named-source materialization uses the same grounded node references;
it does not read or advance a producer, rebuild a snapshot, or consult benchmark-specific values.
No source-name, input-size or workload special case is introduced.

## Completion criteria

1. Preserve stable source/anchor order, empty sources, sort-before-merge, source-name errors,
   retained node identity, snapshots and closures, and all existing physical-reader boundaries.
2. Exercise the affected merge/snapshot/schema/ownership contracts in Release and Debug on .NET 8
   and .NET 10, including both APIs and saved styles. Verify the new cases against the unchanged
   engine to establish semantic preservation, and audit raw test rows and source/binary identity.
3. Run all 60 unchanged canonical paired observations with original output fingerprints and actual
   resource settings. A reliable local improvement requires a Linux comparison before adoption.
4. Record and close this control if the timing evidence is inconclusive or adverse. Do not treat
   lower allocation or green ordinary CI as full Phase 7 acceptance.

## Contract validation

The new regression covers multiple anchors, equal keys, empty sources before/between/after populated
sources, presorted and sort-before-merge inputs, static and dynamic source names, repeated lookup,
and named-group identities retained after subsequent groups. Expected ordering is independently
computed from parsed input fixtures, rather than another engine output.

The initial 40 cases failed on empty-element serialization whitespace in the string assertion.
The assertion now compares parsed XML structure; the initial results remain retained. Runtime code
was not changed to accommodate that test discrepancy.

All 7,976 affected checks pass: 2,220 .NET 10 and 1,768 .NET 8 in each of Release and Debug.
This includes 80 new named-source cases. The same 80 cases pass with the frozen, unchanged
baseline engines. Independent auditing checks every TRX result row, unique execution identifiers,
counters, frozen binary hashes and all 409 engine plus 200 test tracked PDB source checksums per
build. Baseline engines independently match 409 original source checksums each. The first audit
attempt had an incorrect test filename match in the auditor; the corrected audit and original
failure log are both retained. This did not change or rerun the passing affected tests.

All 60 original 100 MB paired observations validate against the original fingerprints, unchanged
consumer and dependencies, four reported processors, the actual 4 GiB heap and disabled tiering.
Every timing range overlaps. Candidate/baseline stream median ratios are identity 1.000655,
accumulator 0.995008, merge 1.005614, fork 1.004558 and source-document 0.993839. Merge is 0.56%
slower. This runtime-only variant is not adopted or sent to Linux.

A follow-up inspection compiles the unchanged canonical stylesheet through the actual QIL compiler
and optimizer. Both streamed and tree QIL retain an eager `X31MergeGroupSources` binding, despite
no named-group reference in the action. The runtime-only change therefore does not achieve the
intended demand behavior in this workload. The isolated inspection uses the frozen candidate engine
and byte-identical canonical stylesheet; its source, build log, XML and JSON are retained in `qil/`.

The follow-up below investigates compiler lowering so an action without any named-group reference
can avoid the source-array binding. Runtime caching preserves the existing XDM array representation.
It uses fresh affected tests, source/binary verification and a separate comparison. The initial
results remain retained; do not repeat the unchanged runtime-only variant.

Raw evidence is retained under `/private/tmp/xsl3-p7-merge-source-groups-20261106/`.
The original four failed throughput targets, both final-source memory matrices, all 126 throughput
observations, compatibility qualification, supported claims, review/merge and matching release
remain the full Phase 7 completion boundary.

## Compiler binding correction

The first compiler variant directly inlined the accessor. Its Release run failed 12 of 2,220
.NET 10 cases and eight of 1,768 .NET 8 cases, all in the new retained named-group regression.
The IL visitor encountered an unbound captured reference. The run stopped before Debug, and no
performance comparison was run for that variant. Its source manifest, patch, frozen Release
binaries and failed results remain in `/private/tmp/xsl3-p7-merge-source-lowering-20261106/`.

The corrected variant retains the existing source-group binding when the compiled action has a
free reference to it. It omits only an unused binding, using the existing `FreeReferences31`
analysis. This preserves named lookup and captured-value scope instead of introducing shared
inline expression nodes. Runtime source-count storage and atomic source-array caching are unchanged.
The corrected variant passes all 7,976 affected checks and a fresh 80-case baseline comparison.
Every result row and all four engine/test binary sets are audited, including 409 engine and 200
test source checksums per build. The rebuilt test assembly hashes differ from the first variant,
so the baseline controls were rerun against the new assemblies rather than reusing earlier results.

QIL inspection verifies zero `X31MergeGroupSources` calls for the original canonical action and
one for a separate named-group control, in both tree and streamed optimized QIL. The original
stylesheet file matches the baseline revision byte for byte. The first named diagnostic stylesheet
had an XML quote-escaping error; the corrected diagnostic changes only that control string. This
is outside the canonical benchmark and does not change the source or timings of the gate workloads.

All 60 corrected local observations validate with the original 100 MB inputs/output fingerprints,
unchanged consumer/dependencies, actual 4 GiB heap, four reported CPUs, disabled tiering and normal
ReadyToRun policy. Frozen baseline/candidate binaries and all 409 tracked engine source checksums
per variant verify. These are macOS ARM64 diagnostics, not Linux acceptance.

| Workload | Baseline stream median (ms) | Candidate stream median (ms) | Candidate / baseline | Ranges overlap |
|---|---:|---:|---:|---|
| Identity | 2,086.842 | 2,094.711 | 1.003771 | Yes |
| Accumulator | 2,052.204 | 2,031.734 | 0.990025 | Yes |
| Merge | 3,319.379 | 3,178.679 | 0.957613 | No |
| Fork | 1,510.591 | 1,532.197 | 1.014303 | Yes |
| Source-document | 2,107.220 | 2,079.426 | 0.986810 | Yes |

Merge's baseline samples are 3,278.941084 /3,403.668833 /3,319.378750 ms; candidate samples are
3,163.848292 /3,179.878833 /3,178.678708 ms. Every candidate sample is faster, and the median
improves 4.24%. This justifies a Linux comparison; it does not yet justify integration.

Baseline engine SHA-256: `4a454adc501c00eace78bb495d62182ea45979ecb3df9bc730fc70ab40bc43d1`.
Candidate engine SHA-256: `e0c2e9fa1fefeee5bd02e0ca0964b63b1dc36c53b48a7e1337aebbc1346807bb`.
The tested runtime/compiler source is committed at `370178a3965db6ac111f46df2649f358b7494c9f`.
Raw evidence and auditors are under `/private/tmp/xsl3-p7-merge-source-lowering-corrected-20261106/`.

[Linux comparison 42166241534](https://github.com/skuirrels/Xsl3/actions/runs/42166241534) completed
successfully on candidate `370178a`, using the unchanged controller against integration `402cbed`.
The independently audited outcome is recorded below.

## Additional named-closure review

The final test-only extension retains a named group's nodes in an explicit closure, invokes it after
later groups complete, and checks its content and identity against the combined group. All 80
expanded cases pass on the exact frozen corrected candidate engines, and all 80 pass on the exact
baseline engines. These are stronger reruns of the named-source cases, not 160 new distinct cases.

The auditor verifies all eight raw result sets, unique execution identifiers, engine/test hashes and
200 test-source PDB checksums per configuration/framework. Evidence is retained under
`/private/tmp/xsl3-p7-merge-source-closures-20261106/`. Runtime, compiler, consumer and controllers
remain byte-identical in source to `370178a`, the candidate in the completed Linux comparison.

## Linux outcome and integration decision

The independent audit verifies all 60 observations, original output fingerprints, actual 4 GiB heap,
four reported CPUs, disabled tiering, original controller hashes, unchanged consumer/dependencies,
retained binary hashes and 409 tracked PDB source checksums per engine. Merge improves 3.93% with
fully separated ranges. Its candidate stream/tree median ratio is 0.987767 in this paired diagnostic.

| Workload | Baseline stream median (ms) | Candidate stream median (ms) | Candidate / baseline | Candidate stream / tree | Ranges overlap |
|---|---:|---:|---:|---:|---|
| identity | 3,821.861 | 3,841.669 | 1.005183 | 1.041462 | Yes |
| accumulator | 3,918.903 | 3,920.444 | 1.000393 | 0.768590 | Yes |
| merge | 6,311.366 | 6,063.242 | 0.960686 | 0.987767 | No |
| fork | 3,006.628 | 3,167.272 | 1.053430 | 1.122432 | Yes |
| source-document | 3,823.673 | 3,847.913 | 1.006339 | 1.040296 | Yes |

Merge's baseline samples are 6,311.366358 /6,390.135956 /6,271.243029 ms; candidate samples are
6,072.067479 /6,021.593734 /6,063.241822 ms. Fork's median is 5.34% higher, with overlapping
ranges: baseline 3,006.627621 /3,562.504954 /2,916.556115 ms and candidate
3,353.877385 /2,935.911256 /3,167.272130 ms. No fork benefit or resolution is claimed.

The reproducible merge gain justifies carrying this bounded change into the draft integration
branch. This is not a full original gate pass: the latest complete 126-observation qualification
remains the earlier three-of-seven result. Identity, fork and source-document remain optimization
targets; all seven original targets and both memory matrices must be rerun and pass on final source,
along with complete compatibility, physical-I/O evidence, supported claims, review/merge and release.

Artifact `13479272141` expires on 2027-02-04. ZIP SHA-256:
`f7b3743f6a852681a6363539e40594faa54acf827e04f66bb22ec1a63a333bbf`.
Baseline engine SHA-256: `ba00584de9cd0ea454061c331dd0f674dab6fc9c7420504141ecd3aa436a97a4`.
Candidate engine SHA-256: `1b380f9e126524facef2927ed8fa9d1a05032938124a776b86ce619ed4ddf7cb`.
Raw evidence and `audit.py`/`review.json` are retained under
`/private/tmp/xsl3-p7-merge-source-groups-linux-20261106/`.
