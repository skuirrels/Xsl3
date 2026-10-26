# Navigator ID provenance, 2026-10-25

This continues the [document-fragment checkpoint](streaming-document-fragments-2026-10-25.md) on
`feature/p7-streaming-analysis`. Full Phase 7 remains active and `supports-streaming` remains `no`.

## Bug and resulting behavior

The navigator fragment loader used `MoveToId(attribute.Value)` to infer an ID attribute declaration,
then applied that declaration to every element with the same qualified name. A lookup identifies an
element; it does not identify which attribute supplied its ID. With `child key='first' alias='not-an-id'`
and a later `child key='second' alias='second'`, the loader could replace the real `key` declaration
with `alias`. After loading and copying the selected view, `id('first')` was empty and
`id('not-an-id')` returned a node. The corrected public reproducer fails in all four combinations of
legacy/compact navigator providers and legacy/modern transformation APIs before the fix; its log is
`/tmp/xsl3-p7-id-provenance-before-valid.log`.

The loader now keeps two distinct kinds of provider facts:

- Known ID/IDREF attribute properties belong to a particular element and expanded attribute name.
  Schema annotations continue through the existing typed-reader path; untyped compact-tree identity
  flags use the same element-local metadata channel.
- A provider exposing only an ID lookup contributes the exact value-to-element association it
  confirms. This creates an ID index entry, without assigning an ID type or attribute flag to a
  matching ordinary attribute.

`Schema/NodeIdentity.cs` defines the internal reader metadata contract. The selected reader supplies
sparse element-ordinal facts to the compact builder, so elements sharing a name do not share inferred
identity declarations. The compact tree stores imported lookup-only IDs separately from attribute
flags. The common element-copy routine preserves those lookups through repeated copying, including
annotation stripping. Neither a copied alias nor another same-named element gains an ID or IDREF
property merely because its value matches.

The metadata is collected only while selecting an already-materialized navigator subtree. It scales
with that explicitly grounded subtree. Forward stream/reader selection continues to use authoritative
parser declarations; it does not acquire this navigator index or retain the streamed prefix. The
reader/tree metadata boundary avoids adding resolver policy to the tree builder or duplicating copy
logic in individual functions. No public API or saved-helper signature changes.

A legacy `XPathDocument` does not expose its DTD attribute flags or IDREF metadata. Its confirmed
ID lookups are retained; additional attribute properties are not fabricated. Providers requiring
those properties must supply the corresponding reader/schema/compact metadata. This does not claim
that missing provider metadata has been recovered.

## Functional and saved-native verification

Release and Debug each pass **3,249 net10.0 / 3,146 net8.0** unit tests. The focused fragment, compact-tree,
bridge and shared-resource checks pass **123/119**. The fragment class now contains **57/55** cases,
including **12** additional cases for this review. They cover both APIs, `fn:doc` and source-document,
DTD and XML IDs, repeated copies, and same-named attributes with different local schema types before
and after annotation stripping. Expected values follow the input declarations and actual ID/IDREF
properties; fixtures do not supply runtime metadata.

Eight new normal/debug saved stylesheets pass **12** native provider checks, plus all retained saved
consumers, including the legacy materialized merge and immutable version-2 fork consumer. The native
publish has no warnings. Its directory is `/tmp/xsl3-p7-id-provenance-native/`; `generate.log`,
`publish.log`, `run.log` and their terminal status files retain the evidence. The frozen snapshot has
**407** source/configuration files. Previously saved consumers are retained with their original hashes;
the eight new styles are generated against this snapshot.

The evidence review found that the earlier convenience driver copied the existing XSLT 2.0 and
posture/sweep report files without rerunning those profiles. Both were explicitly rerun for this
increment (`id-provenance-verified-p2.log` and `id-provenance-verified-sandp.log`). The fresh 2.0 result
is **4,590/8/10**, with `id/id-001` passing and one previously unrun result-tree comparison now covered;
posture/sweep remains **790/129/0**. These 2.0 differences supersede the stale report. They are not
attributed specifically to this metadata fix without an isolated source comparison. No new failure
IDs appear in either fresh profile.

The final compatibility driver passes ordinary W3C **46**, upstream **1,654** and differential **394**
on each framework, and all **13** streaming controller checks. The strict audit retains exit **1**;
its failures are not described as a passing suite. No baselines, expected-error classifications,
memory limits, public feature claims, commit or push are changed by this increment.

## Fresh conformance audit

All eight execution profiles ran against this source. Nine report files, including parse coverage,
are compared with the preceding checkpoint in `/tmp/xsl3-p7-id-provenance-reports/profile-comparison.json`.
There are **no new failure IDs**. QT3 parse coverage is **22,359/22,359**.

| Profile | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| XSLT 3.0 | 8849 | 19 | 41 | 34 |
| XSLT 2.0 | 4590 | 8 | 10 | 7 |
| Unstreamed development | 2693 | 18 | 1 | 5 |
| Strict forward inputs | 2690 | 21 | 1 | 7 |
| Posture and sweep | 790 | 129 | 0 | 0 |
| Schema-aware | 843 | 1 | 2 | 6 |
| Schema-aware strict | 842 | 2 | 2 | 6 |
| QT3 | 21884 | 0 | 99 | 67 |

Logs are `/tmp/xsl3-p7-id-provenance-verified-*`; the separate explicit-profile statuses are in
`/tmp/xsl3-p7-id-provenance-extra-profiles-exit.txt` (**0 0**). Every report was produced by a current
profile run rather than reused as evidence merely because it existed in the shared output directory.

## Ordinary performance gate

The isolated three-pair gate against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` passes (exit **0**).
Across **39** timed rows, the head/base time geometric mean is **0.977000839111**, and the maximum allocation
ratio is **1.037247283614**. This compares the accumulated branch with the baseline using the same harness;
it does not isolate this review fix. Individual slower rows remain: join compilation (**1.059**) and
regex 1 KB (**1.170**). Aggregate success does not establish a pass for every row.

No other task-owned CPU job ran during the gate. Only rounds **1–3** are archived in
`/tmp/xsl3-p7-id-provenance-perf-data/`, alongside `compare.txt` and the exact `summary.json`.
Log: `/tmp/xsl3-p7-id-provenance-perf.log`.

## Generated-input native scale probe

All four focused normal/debug children pass at exact **209,715,200** and **2,147,483,648** input bytes.
They consume every requested byte, validate independently calculated output digests, emit before EOF,
and close the owned input. The source is a forward reader with a large prefix before the selected ID;
it exercises the unchanged streamed declaration path alongside the new metadata-aware compact builder.

| Saved style | Exact input bytes | First output after input bytes | Child elapsed ms | Lifetime peak RSS bytes |
|---|---:|---:|---:|---:|

| normal | 209715200 | 105213952 | 1159.6083 | 25116672 |
| normal | 2147483648 | 1074098176 | 11830.6380 | 24805376 |
| debug | 209715200 | 105213952 | 1142.6700 | 27475968 |
| debug | 2147483648 | 1074098176 | 11749.6252 | 26935296 |

RSS is the maximum of controller-observed and child-reported full-lifetime peaks. There is no baseline
subtraction. Ordinary ICU support and the **209,715,200-byte** heap/RSS guard remain in place. These
focused probes do not satisfy the separate **20,971,520-byte** push RSS requirement or the canonical
throughput target. Their single-child elapsed times are observations, not comparative benchmarks.

The scale directory is `/tmp/xsl3-p7-id-provenance-scale-native/`; `report.json`, `run.log`,
`run-exit.txt`, `driver-exit.txt` and `provenance.json` retain the evidence. Both native publications
have no warnings. After all execution, `source-check.json` verifies **407** live and frozen source/config
hashes with no mismatch. The main consumer's `artifact-hashes.json` records **98** artifacts (**95**
saved DLLs plus three native publish artifacts); the scale consumer records **5** (**2** saved DLLs plus
three publish artifacts). All **87** retained saved DLL hashes are unchanged, as is the protected v2
fork consumer. Final functional, native, publish, performance and scale terminal statuses are **0**;
the strict conformance audit separately retains its expected nonzero status.

Full P7 remains active: the non-monotonic climbing-set work, other semantic cases, S1/S2 suite gates,
exact push RSS, canonical throughput and current-source remote Linux x64 evidence remain open.


The [deferred-header storage follow-up](streaming-deferred-headers-2026-10-25.md) prepares bounded scratch storage for the
remaining non-monotonic climbing sets. Its compiler integration remains unfinished.
