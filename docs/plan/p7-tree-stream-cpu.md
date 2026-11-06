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

Leave the other diagnostic switches false. Execution and artifact audit are pending.

## Local controller review

The workflow parses with Ruby/Psych 5.3.1. Both added shell steps pass `bash -n`. A structural
comparison removes only the new default-false input and its two guarded steps, then requires the
entire workflow to equal `4024e19`. All existing acceptance/diagnostic jobs, commands, thresholds,
artifacts and conditions are unchanged. Runtime, tests, benchmark consumers and Python controllers
also remain byte-identical to `a4f7301`. Evidence is retained under
`/private/tmp/xsl3-p7-tree-stream-cpu-20261106/`, including both workflow files and `workflow-review.json`.
