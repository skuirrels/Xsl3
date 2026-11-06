# P7 tree and streaming CPU comparison

## Question and bounded scope

The navigator node-kind and attribute-cache controls passed correctness checks but did not establish
useful elapsed-time gains. The next diagnostic compares where the canonical tree and streamed identity
executions spend CPU on one host. This can distinguish shared serialization/hash/parser costs from
extra source traversal, node construction or template dispatch work before selecting another runtime change.

The isolated `feature/p7-tree-stream-cpu` branch starts at integration documentation head `4024e19`.
Runtime, tests and canonical consumers remain identical to `a4f7301`. The existing profiler already
supports `--mode tree`; the workflow adds only an optional `cpu_tree_control` switch, default false.
When enabled with `cpu_profile`, it profiles the existing stream child and then the tree control on
the same runner, using the same build and original 1,000,000,000-byte input. Both raw traces, results,
JIT maps and CPU reports are retained separately. Original acceptance jobs and their configurations
are unchanged. No benchmark/runtime/test source or feature claim changes.

## Evidence required

1. Pin the actual source to `a4f730130728480748112af547aa999426bcfbff` and record the separate
   diagnostic-controller revision. Verify the downloaded artifact digest and retained engine/consumer
   binaries and PDB source checksums against that exact source.
2. Verify both canonical results, original output fingerprints, exact input size, four-CPU host,
   disabled tiering and effective 4 GiB managed allowance. Confirm no processor override or altered
   input/oracle. Both modes use normal ReadyToRun policy.
3. Inspect actual kernel CPU samples, collector/probe exits, trace loss and per-thread/full-stack
   attribution. Preserve unresolved frames explicitly. Reports include warmup/collection coverage;
   sample counts are diagnostic evidence, not a removable fraction of measured elapsed time.
4. Identify a concrete difference that can support the next runtime change. Do not infer a useful
   speedup from allocation reductions or an instrumented timing. Any candidate still requires semantic
   review, regression tests, independent comparisons and all unchanged original acceptance gates.

The local full Debug integration suites (session 60509) continue on the already-built unchanged
runtime/test source. The original full throughput run 42173435783 remains separate; both of its
memory matrices have passed and been audited. The profiler is not a replacement or retry of that run.

## Dispatch

```sh
gh workflow run streaming-development.yml --repo skuirrels/Xsl3 \
  --ref feature/p7-tree-stream-cpu \
  -f cpu_profile=true -f cpu_tree_control=true -f cpu_workload=identity \
  -f source_ref=a4f730130728480748112af547aa999426bcfbff
```

Leave the other diagnostic switches false. [Run 42176230838](https://github.com/skuirrels/Xsl3/actions/runs/42176230838)
completed successfully on controller `1364f77`, with the source pinned above. Both CPU children
ran sequentially and all other jobs were skipped. The artifact and full-stack audits below are complete, with explicit coverage limits.

## Local controller review

The workflow parses with Ruby/Psych 5.3.1. Both added shell steps pass `bash -n`. A structural
comparison removes only the new default-false input and its two guarded steps, then requires the
entire workflow to equal `4024e19`. All existing acceptance/diagnostic jobs, commands, thresholds,
artifacts and conditions are unchanged. Runtime, tests, benchmark consumers and Python controllers
also remain byte-identical to `a4f7301`. Evidence is retained under
`/private/tmp/xsl3-p7-tree-stream-cpu-20261106/`, including both workflow files and `workflow-review.json`.

## Verified paired capture

Artifact **13483167694** expires 4 February 2027. Its GitHub ZIP digest is
`a73e4fe799b7c79308d004600e47a4e405ce5379399c96ae6c9770b9539d21b5`.
The exact controller/source refs, both probes/collectors/children, original outputs and raw stdout
agreement verify. Both inputs are exactly 1,000,000,000 bytes with effective 4,294,967,296-byte heap
allowances, four host CPUs, no processor override and disabled tiering. The same retained engine
and consumer are used by both modes; all 409 engine and 14 consumer PDB source checksums match
`a4f7301`. Generated PDB documents are listed separately. The other 24 dependency hashes in each
child report are not independently verified because those dependency files were not retained.

| Capture evidence | Stream | Tree |
|---|---:|---:|
| Kernel CPU samples | 26,388 | 28,704 |
| Transform-entry stack region | 23,313 | 24,627 |
| Output-oracle stack region | 2,941 | 3,733 |
| Unattributed stack region | 134 | 344 |
| Lost events | 0 | 0 |
| Input consumed at first output | 8,192 bytes | 1,000,000,000 bytes |

The complete-stack reparse preserves every original site count, region, inclusive count and event
inventory. These are independent collection windows, not matched measured-transform intervals.
They can include different warmup/oracle coverage; the raw counts cannot prove that one mode uses
less CPU or identify a removable share of elapsed time. The workflow/controller request no
ReadyToRun override, but inherited ReadyToRun environment values are not captured by this controller.
Unresolved framework frames remain visible rather than being assigned to an assumed method.

The stream trace contains 11,477 transform-entry samples whose complete stack includes
`StreamedDocument.ReadNext` or `ReadNextCore`; 6,314 also include `StreamedInputReader.Read`.
The remaining 5,163 samples exclude that reader-call path, including **1,301 direct leaf samples** in
`ReadNextCore`. Tree construction has 13,210 inclusive `XmlQueryContext.BuildTree` samples.
Shared output copying remains substantial in both modes (`StartCopy` inclusive counts 6,578 /6,489).
These inclusive counts overlap and must not be summed.

This localizes the next investigation to the normal source-event path and its generated instructions;
it does not justify retrying the rejected reader-metadata, token-kind, navigator-kind or lazy-header
controls. No new runtime change or throughput gain is established. First inspect the hot instruction
regions inside `ReadNextCore` and preserve the physical-read, validation, ownership and retained-header
contracts when selecting a change.

Retained engine SHA-256: `36202f1aba6aae933c773f26d2e4dae59aebf4288c85849e3eb9a3c9f52010cf`.
Retained consumer SHA-256: `4aab60c169a5f960d486081f11016a5e47a2bd49dfd29b67f91edb4732c0c135`.
Evidence root above contains the archive/metadata, both raw traces, maps, reports and binaries,
`audit.py`, `review.json`, and the two full-stack reparses. No Linux capture remains live for this
control; do not restart it. Original acceptance 42173435783 is a different, still-active throughput run.
