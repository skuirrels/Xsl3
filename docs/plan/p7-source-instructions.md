# P7 source-event instruction diagnostic

## Why this control is needed

The completed original integration gate on `a4f7301` passes both memory matrices and four throughput
targets. Identity, fork and source-document still fail. The [same-host CPU capture](p7-tree-stream-cpu.md)
localizes 1,301 direct leaf samples to `StreamedDocument.ReadNextCore`, but does not identify the
costly instructions. A reparse conserves all 26,388 CPU samples and maps those 1,301 addresses inside
the same process's 4,109-byte JIT method. No IL offsets are present (`-1`), so a source-line conclusion
cannot be inferred from that trace alone.

This isolated branch adds optional same-child JIT disassembly to the existing CPU diagnostic and
optional leaf instruction addresses to its report. The filter is an explicit workflow input, empty
by default. Disassembly settings apply only to the benchmark child, so collector/probe processes
cannot truncate its output file. The controller records the selected settings and inherited
ReadyToRun settings, preserves the emitted file/hash and fails visibly if requested output is missing.
Normal JSON output remains separate. The report accounts for samples without a leaf explicitly.

The runtime, tests, canonical consumers, original inputs/oracles and acceptance gates are unchanged.
The diagnostic builds immutable source `a4f730130728480748112af547aa999426bcfbff`. No supported
feature claim or runtime optimization follows from adding this tool.

The selected disassembly flags are documented in the [.NET runtime JIT guide](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/jit/viewing-jit-dumps.md).
They emit the method's native instruction bytes and a separate output file. No code-generation,
ReadyToRun, processor-count or tiering override is introduced beyond the existing diagnostic policy.

## Local validation

- Python syntax, workflow parsing and changed shell-step syntax pass. Removing only the new optional
  input/arguments restores the prior workflow structurally; every acceptance job remains unchanged.
- The modified CPU reporter builds in Release with warnings treated as errors, zero warnings/errors.
  On the retained trace, its default report is identical to the old report. Enabling instruction
  addresses preserves every original field and accounts for all 26,388 samples. Its 1,301 selected
  method samples exactly match an independent address-only parser and the original method-level count.
- A 2 MiB ARM64 routing smoke retains the original valid result and emits the requested method's
  disassembly separately. This proves local flag/output routing, not Linux throughput or memory acceptance.

Local evidence: `/private/tmp/xsl3-p7-source-instructions-20261106/`, including both workflow versions,
controller/reporter reviews, the independent parser and its source/build, prior native offsets,
local disassembly and original child output. Runtime code and canonical outputs were not changed.

## Linux completion criteria

1. Capture the unchanged canonical 1 GB streaming identity child with the actual source/controller
   revisions recorded. Validate its original outputs, exact input size, effective heap guard and
   compilation/CPU settings; verify the archive digest, binaries and tracked PDB source checksums.
2. Verify zero lost CPU events and conserve every original report count. Match native sample addresses
   to this same child's perf-map method interval and emitted disassembly; account for all selected
   samples and any unresolved addresses. Never combine offsets from different JIT bodies/processes.
3. Inspect the sampled instruction regions and identify a concrete source change, or explicitly record
   that attribution remains insufficient. Samples can skid or cover warmup; they are not an elapsed-time
   improvement or a removable percentage of execution.
4. Any runtime change still needs semantic review, regression validation and independent paired timing
   before integration, followed by every unchanged original final-source gate and release requirement.

```sh
gh workflow run streaming-development.yml --repo skuirrels/Xsl3 \
  --ref feature/p7-source-instructions \
  -f cpu_profile=true -f cpu_workload=identity \
  -f cpu_jit_disasm=Xsl3.Streaming.StreamedDocument:ReadNextCore \
  -f source_ref=a4f730130728480748112af547aa999426bcfbff
```

Leave the other diagnostic switches false.

## Completed Linux capture

[Run 42179533851](https://github.com/skuirrels/Xsl3/actions/runs/42179533851) completed successfully
with controller `4b4219877aa4f3407e1c51ba9394bc8823801c8e` and unchanged runtime `a4f7301`.
Artifact `13485061490` has verified archive SHA-256
`2d05d8a333f4a5cf3bcdfb960c15636bd1eab6ee40978eaa9d44799da26b2f74`.
The original 1 GB output, actual 4 GiB heap guard, four processors, disabled tiering, retained
engine/consumer hashes and all 409 engine /14 consumer tracked PDB sources verify. Both inherited
ReadyToRun variables are unset. No CPU events were lost; all **26,315** CPU samples are conserved.

The independent instruction audit maps **all 1,305 direct `ReadNextCore` samples** to instruction
starts in this same process's 4,109-byte method. Its emitted body contains 958 decoded instructions.
There are no unmatched addresses or interior-instruction addresses. All IL offsets remain `-1`;
these are native-instruction observations, not automatically recovered source lines. Disassembly
SHA-256 is `d477045ae26d8f77d7dff93c83c9f7d229ed750b977557a65d28cc2fef7f0756`.

| Sampled native block | Direct leaf samples | Source operation identified by emitted code |
|---|---:|---|
| `IG05` | 213 | Read open-stack count/top, test `Ended`, remove the closed frame |
| `IG69` | 96 | Construct element and read name/namespace/prefix/attribute metadata |
| `IG01` | 95 | Method prologue and stack-frame initialization |
| `IG140` | 87 | Method epilogue |
| `IG10` | 85 | Reader readiness and node-kind dispatch |

Sampling skid, return addresses and the capture window prevent interpreting these counts as exact
instruction time or a removable share of elapsed time. The next source investigation is avoiding
the repeated stack-top lookup when the current event cannot have closed an element. It must retain
the existing removal boundary, fork and replay ownership, reader advancement, validation and
observer ordering. The previously rejected linked-ancestor rewrite and method-splitting control
are not adopted or repeated. No runtime change or performance benefit is established by this capture.

`review.json` and `instruction-review.json` in the evidence directory record the completed capture
and instruction audits; `audit-capture.py`, `audit-instructions.py`, raw trace, same-child perf map,
emitted instructions and original child output are retained alongside them.
