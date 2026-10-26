# Typed static-node persistence, 2026-10-26

This follows the [schema-metadata checkpoint](streaming-schema-metadata-2026-10-26.md) on `feature/p7-streaming-analysis`. Saved tables can now preserve typed static nodes whose user-defined types belong to registered compilation schema contexts. Full Phase 7 remains incomplete; `supports-streaming` remains `no`.

## Saved nodes and runtime behavior

Constant format **6** adds annotated tree records and table-scoped shared tree references. Repeated nodes retain identity across table entries, nested maps and arrays; descendants and attributes retain their relationship to the same document. The codec reuses the existing tree traversal and node-path representation. It restores actual schema type objects, including anonymous element types, nilled state, namespace bindings, ID/IDREF and provider lookup facts, raw text ranges and compact-tree construction/document-URI flags. Parentless attributes with built-in annotations need no imported schema.

User-defined type references resolve through the existing schema graph registry. The serializer does not replace unknown types with built-in types or revalidate a node against a different schema. Foreign annotations that are invalid or incomplete are explicitly rejected. Types without an owning registered context and anonymous atomic constants remain explicit limitations.

Formats **1–5** remain readable. Non-schema tables continue to emit format 3, schema-set-only tables can use format 4, and schema metadata without annotated nodes retains format 5. Annotated nodes select format 6. New tree references and node paths reject invalid indices or unknown records instead of silently selecting another node. Reader and writer state is scoped to one table.

The new saved-node tests exposed an existing atomization defect: a nilled element with a simple schema type produced one empty string instead of an empty typed value. The existing nilled check covered only complex types. It now applies to nilled elements before the simple/complex split. Optional atomization checks cardinality after atomization, including sequences where nilled nodes disappear; two remaining atomic values still raise XPTY0004. Scalar untyped-node conversion is shared with ordinary atomization, retaining the scalar fast path.

## Reproduction and tests

The initial three typed-node persistence cases failed on both frameworks at the previous unsupported-metadata guard. The expanded normal/debug save tests then found the nilled simple-type issue without changing their expected result. A direct pre-serialization test reproduced it on both frameworks. A further optional-cardinality case reproduced XPTY0004 before the helper correction; the corrected helper passes nilled-plus-value, all-nilled and genuinely multiple-valued inputs.

Temporary harness failures are preserved separately: stale reference/runtime artifacts after rebuilding, and an incorrect fixture property name. A fresh build directory produced the actual optional-cardinality red/green results. An attribute-navigation fixture was also corrected to move back to the parent before selecting another attribute. None of these failures is counted as a product regression or a passing functional test.

Permanent coverage adds **14 net10.0 / 12 net8.0** cases. It exercises constructed/input trees, shared roots across nested values, parentless typed attributes, QName namespaces, nilled values, independent ID provenance, raw text, foreign valid/invalid annotations, malformed records and actual normal/debug saved assemblies. The former two built-in typed-node rejection cases now assert that annotations survive. Final targeted coverage passes **47 / 32** alongside the retained schema metadata and graph tests.

The actual main solution and frozen cohort pass Release and Debug **3,519 net10.0 / 3,372 net8.0**. W3C **55**, upstream **1,654 per framework**, differential **394 per framework** and controller checks **13** pass. All nine profile headings and failure-ID sets remain unchanged: strict streaming **2,695/16/1**, zero unmatched expected codes; QT3 **21,884/0/99**, with **25** unmatched-code notes. Baselines and feature claims are unchanged.

## Native and managed execution

Two fresh normal/debug saved styles execute real schema values, nilled empty typed values, QName atomization, ID/IDREF lookup, schema-element tests and node identity across several static parameters and an array. They also compare a nilled-plus-non-nilled node sequence after atomization. The tests and producer inject static parameters through the existing test friend-assembly setup; consumers use the actual public saved-Type load and transform methods. This adds no public export API. NativeAOT publication passes with warnings as errors, alongside **277 byte-identical retained saved stylesheets** and the existing compatibility helpers.

The first complete typed-node cohort is retained under `/tmp/xsl3-p7-typed-static-before-optional/`, including source, artifacts, consumers, producers, tests, reports and the managed corpus audit. The final cohort regenerates the two new styles against the corrected helper. Original retained binaries are unchanged.

The production portable-reference helper creates separate copies for all **327 actual saved-reference paths**. All functional consumers pass on .NET 8 and .NET 10. Exactly two native-host assertions are adapted for managed execution; functional checks and the originals' native guards remain intact. The final audit is `/tmp/xsl3-p7-typed-static-final-corpus-audit/`. Older producer-runtime-bound originals still need regeneration for .NET 8; these audit copies do not modify them.

## Evidence, performance and remaining scope

The final frozen cohort contains **417** core/configuration files, four changed core files, two auxiliary test-source hashes, **282** artifacts, **325** consumer files and two producer files. Final checks are `/tmp/xsl3-p7-typed-static-native/final-check.json`.

The original ordinary three-pair gate passes against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`: time geometric mean **1.006812505740** over **39** timed rows, maximum allocation ratio **1.037142004146**, exit **0**. Raw rounds and comparison remain in `/tmp/xsl3-p7-typed-static-final-perf-data/`.

Individual timing ratios above 1.05 remain recorded: dispatch/compile **1.136251**, grouping/compile **1.051936**, maps/1mb **1.116550**, sorting/compile **1.203225**. This aggregate pass establishes no per-row win. Current main/frozen/full source, both auxiliary tests, retained/fresh saved references, the protected version-2 fork and artifact/consumer/producer manifests match after the gate.

No new exact-size memory or canonical-throughput claim is made. Anonymous atomic constants, schema types outside registered compilation contexts, exact 20 MiB push RSS, canonical throughput, analysis-suite reconciliation, general retained models and current-source Linux evidence remain open.
