# P7 source dispatch control

The independent Linux identity and accumulator CPU profiles both show substantial inclusive cost
in `StreamedDocument.ReadNextCore`; see [CPU evidence](p7-linux-cpu-profile.md). Many underlying
parser frames remain unresolved. This control tests method layout, not a claim that wrapper calls
account for all source-reading cost.

## Isolated change

Branch `feature/p7-source-dispatch` starts at `42d2329`, whose runtime and benchmark source equal
merged main `a5e89ac` / contract head `78815e8`. It extracts element-header construction and text
coalescing from the large `ReadNextCore` switch. It adds no state, caching, buffering or source
lookahead. Existing cancellation, schema normalization, namespace handling and entity order remain.
`ReadText` reports whether it produced a node or left another token ready after discarding whitespace;
the caller continues dispatch in the latter case, as before.

The statement-level extraction audit verifies both original bodies, allowing only return/continue
mapping at the method boundary. Existing reader, schema, observer, accumulator, fork and physical
read-boundary tests provide runtime verification. Release passes all 1,786 .NET 10 and 1,355 .NET 8 checks,
with no failures or skips. The Release benchmark build has zero warnings/errors. All 48 paired timing observations are valid. The control is rejected: no convincing elapsed-time
benefit is established.

## Measurement boundary

The planned paired diagnostic uses the unchanged original controller, independent outputs and actual
4 GiB heap guard. It compares immutable baseline/candidate binaries at 100 MB for identity,
accumulator, fork and source-document, three alternating rounds of both modes (48 observations),
with four effective processors on macOS ARM64. This is a targeted diagnostic, not the original
hosted seven-workload, three-size acceptance gate. Runtime/default buffer policy remains separate.

Source/binary hashes, exact extracted baseline, source comparison, build/test logs and timing driver
are retained under `/private/tmp/xsl3-p7-source-dispatch-20261105/`. The baseline runtime SHA-256 is
`60ef17ce25517b7f32d2a8f50df3a6477f03efea4e3e36123f972b7f054ed049`; its recorded source is `78815e8`.
Do not promote this control before reviewing its contracts and paired measurements.


## Result: rejected

| Workload | Candidate/baseline streaming median | Candidate stream/tree |
|---|---:|---:|
| Identity | 1.010991 | 1.082731 |
| Accumulator | 1.003603 | 0.843517 |
| Fork | 0.982846 | 1.224392 |
| Source-document | 1.004706 | 1.068998 |

All inputs, independent output fingerprints, actual heap guards, processor allowances and frozen
binary hashes validate. Identity is slightly slower; the other changes are small and the fork ranges
overlap. The extraction does not justify adoption or further qualification. Its source remains only
on this isolated branch; do not merge it. No threshold, fixture or public default was changed.
`paired-review.json` retains every sample and the test counts. Next qualify the independently measured
larger-buffer policy against the original memory and compatibility requirements, excluding this control.
