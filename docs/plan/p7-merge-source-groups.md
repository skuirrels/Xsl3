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

The next variant will lower source-group access at each actual named-group use rather than create
an unconditional binding. Runtime caching preserves repeated lookup and the existing XDM array
representation. It needs fresh affected tests, source/binary verification and a separate comparison;
retain the initial results and do not repeat the unchanged runtime-only variant.

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

A fresh 60-observation comparison is running under
`/private/tmp/xsl3-p7-merge-source-lowering-corrected-20261106/`; no benefit or adoption is established.
