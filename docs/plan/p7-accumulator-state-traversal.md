# P7 accumulator state traversal control

**Rejected, 5 November 2026. Do not merge or adopt this runtime control.**

This isolated control starts from merged main `a5e89ac7f6fb630e47872a2bcc297cb010ad36f6` on
`feature/p7-accumulator-state-traversal`. It excludes the rejected source child-header change and all
buffer-capacity candidates; the default capacity remains 64. The candidate is rejected after the paired comparisons below.

## Motivation and change

The retained Linux accumulator CPU trace attributes 11,473 inclusive samples to `Visit`, 8,107 to
`After`, 1,818 to `Before` and 1,493 to `Values`. These overlapping inclusive samples do not measure
removable loop overhead. Source inspection shows that the observer's applicability dictionary is
constructed once and never mutated afterward, but `Visit` enumerates its values at every node boundary.

The control captures that dictionary's actual enumeration order in a private array after construction.
Only `Visit` uses the array. Expanded-name lookups, rule order, applicability, phase publication,
cycle/error behavior, cancellation, snapshot serialization, copies and disposal retain their original
paths. The array references the same State objects already owned by the dictionary; it introduces no
per-node collection, new rule selection, fixture assumption, domain literal or public configuration.

Existing accumulator storage contracts exercise multiple expanded names, mixed phases, dependent values,
additional-counter faults, fork consumers, deferred copies and cancellation. Accumulator lifetime and
replay contracts cover closure and retained copies. No implementation-shaped array test is added.

## Qualification

Affected Release tests pass: 1,480 .NET 10 and 1,072 .NET 8, all 2,552 without failures or skips.
The first paired local diagnostic completed all 12 observations with matching independent outputs,
exact inputs and actual guards. Its streaming median is 2,052.668 ms versus 2,083.047 ms baseline
(1.46% lower; ranges do not overlap). However, tree medians also shifted from 2,465.197 to
2,539.404 ms despite unchanged tree execution. No causal stream/tree improvement is claimed.
The second complete paired run validates all 12 further observations. Its streaming median improves
only 0.86%, while the unchanged tree path again measures slower. Both runs remain retained.
It uses the byte-identical original benchmark consumer, changes only the frozen engine DLL/PDB/XML,
alternates baseline/candidate order over three rounds, and runs both tree and stream modes on the
canonical 100,000,000-byte accumulator workload. All 12 observations retain the actual 4 GiB heap guard,
four effective processors, disabled tiering, independent output oracle and original input/first-output
checks. This isolated diagnostic cannot replace any original P7 acceptance gate.

Evidence directory: `/private/tmp/xsl3-p7-accumulator-state-traversal-20261105/` (source patch/manifest,
frozen candidate, raw TRX, paired scripts and subsequent reports). Baseline engine SHA-256:
`60ef17ce25517b7f32d2a8f50df3a6477f03efea4e3e36123f972b7f054ed049`, built from runtime source `78815e8`,
which matches main `a5e89ac`. Candidate engine SHA-256:
`b1ed5424a223dc2d9fc7a55def6c0bfe4c8614c59654ffa32626a6ef8166e502`.

Adoption requires a useful measured benefit and appropriate Debug, saved/native and hosted qualification.
Full Phase 7 still requires every original correctness, memory, throughput, final-integration and release
obligation; neither a local timing result nor ordinary CI alone completes it.

## Repeated diagnostic and decision

| Combined six observations per mode | Baseline | Candidate | Assessment |
|---|---:|---:|---|
| Stream median | 2,066.973 ms | 2,049.646 ms | 0.84% lower |
| Stream range | 2,062.438–2,096.509 ms | 2,025.690–2,062.848 ms | Ranges overlap |
| Tree median | 2,456.678 ms | 2,541.316 ms | Candidate cohort 3.45% higher |

The small aggregate streaming difference does not establish a useful improvement for adoption. The tree
shift is retained rather than interpreted as improved streaming efficiency; its cause was not isolated.
No observations were discarded and no original gate was relaxed. No Debug, native or hosted qualification
was started for this rejected control. `repeat-paired-review.json` and `combined-review.json` preserve the
second cohort and all 24 observations alongside the original report.
