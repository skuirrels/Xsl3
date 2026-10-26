# Saved schema-dependent metadata, 2026-10-26

This follows the [portable-emission checkpoint](streaming-portable-emitter-2026-10-26.md) on `feature/p7-streaming-analysis`. It adds persistence for schema-dependent constant metadata. Typed static nodes, anonymous atomic values and types outside the compilation's registered schema contexts remain explicitly unsupported; full Phase 7 remains incomplete and `supports-streaming` remains `no`.

## Behavior and identity

Saved stylesheets can now retain named user-defined atomic values, cast targets, list and union constructors, recursive sequence/function/map/array type tests, schema element tests, substitution names, and schema-aware name and dynamic-evaluation contexts. Named types resolve to the restored schema's canonical type object, so casts, signatures, type tests and values share identity. Floating-point atomic values retain their binary representation, including negative zero.

Constant format **5** records table-scoped schema references. The compiler attaches its actual imported schema context to the constant table. Distinct schema sets use one resolved module graph, preserving shared modules; restored type references use deterministic indices through the compiled graph. Existing formats **1–4** remain readable. Ordinary non-schema tables still emit format 3, and schema-set-only tables can retain format 4. There is no process-wide serialization registry, new package dependency or runtime fallback.

The existing single-schema graph version-1 wire layout is preserved. Graph bundles reuse that module codec, reconnect resolved include/import/redefine edges before compilation, and retain UPA and compiled/empty-set state. Reading uses no external resolver and prohibits DTDs. A missing compilation schema context raises an explicit error instead of replacing a user type with a built-in type.

The new reader/writer adapters retain the original query-data reader/writer for output-settings serialization. The first broad regression exposed three failures where output settings cast the adapter to the original concrete writer. That failed cohort and its artifacts are preserved under `/tmp/xsl3-p7-schema-metadata-before-writer-adapter/`; the corrected adapter passes the same cases.

## Verification

Before implementation, all nine initial public saved-schema cases failed the existing unsupported-metadata guard. Permanent coverage now includes **13** public normal/debug schema-metadata cases, **2** shared-identity cases on both frameworks and the existing **18** schema-graph regressions. These verify actual facet rejection, nested type identity, anonymous element types and substitution groups rather than substituting runtime fixture values.

Two fixture assumptions were corrected with their failed logs retained: dynamic evaluation requires explicit `schema-aware='yes'`, and the anonymous-node fixture's local element must be qualified. Runtime validation and evaluation policy were preserved.

The main branch and frozen source pass Release and Debug **3,505 net10.0 / 3,360 net8.0**. W3C **55**, upstream **1,654 per framework**, differential **394 per framework** and controller checks **13** pass. All nine profile headings and failure-ID sets remain unchanged: strict streaming **2,695/16/1**, zero unmatched expected codes; QT3 **21,884/0/99**, with **25** unmatched-code notes. No baseline, suite expectation or feature claim changes.

The producer emits **14 fresh normal/debug DLLs** covering casts, facets, list/union constructors, name availability, higher-order/function signatures, anonymous element/substitution tests and schema-aware evaluation. NativeAOT publication passes with warnings as errors, alongside **263 byte-identical retained saved stylesheets** and the existing compatibility helpers. Dynamic evaluation retains its existing NativeAOT dynamic-code restriction; managed consumers execute the expression and verify its result.

A managed corpus audit creates separate portable copies using the production framework-reference helper for all **325 actual saved-reference paths**. All functional consumers pass on .NET 8 and .NET 10. Exactly two host assertions are adapted to require managed execution; functional assertions and the originals' native guards are preserved. Original retained DLLs are never rewritten. Audit source, projects, path mapping, copies, logs and statuses are preserved under `/tmp/xsl3-p7-schema-metadata-corpus-audit/`.

## Evidence, performance and remaining work

The frozen cohort contains **417** core/configuration files, three auxiliary test-source hashes, **280** artifacts, **322** consumer files and two producer files. All actual native saved-reference paths and separate managed audit evidence are hashed. Final checks are `/tmp/xsl3-p7-schema-metadata-native/final-check.json`.

The original ordinary three-pair gate passes against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`: time geometric mean **0.984465502519** over **39** timed rows, maximum allocation ratio **1.037343840011**, exit **0**. Raw rounds and comparison remain in `/tmp/xsl3-p7-schema-metadata-final-perf-data/`.

Individual timing ratios above 1.05 remain recorded: dispatch/1kb **1.055556**, dispatch/compile **1.065941**, grouping/compile **1.112750**. The aggregate pass establishes no per-row win. Current main/frozen/full source, three auxiliary test hashes, retained/fresh saved references, the protected version-2 fork and artifact/consumer/producer manifests match after the gate.

This increment adds no new memory or canonical-throughput claim. Typed static-node persistence, anonymous atomic metadata, schema types without an owning compilation context, exact 20 MiB push RSS, canonical throughput, analysis-suite reconciliation, general retained models and current-source Linux evidence remain open.

## Follow-up: typed static nodes

The later [typed static-node checkpoint](streaming-typed-static-nodes-2026-10-26.md) adds annotated tree persistence with shared node identity and repairs nilled simple-type/optional atomization. Its new format 6 retains format-5 read compatibility. Anonymous atomic constants and unowned schema contexts remain open.
