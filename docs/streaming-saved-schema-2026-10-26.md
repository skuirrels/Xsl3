# Saved imported-schema validation graphs, 2026-10-26

This follows the [annotation/whitespace checkpoint](streaming-annotation-whitespace-2026-10-26.md) on `feature/p7-streaming-analysis`. It opens saved source-validation paths that previously failed at the blanket schema-import guard. General schema-aware saved compilation and the full Phase 7 exit remain incomplete; `supports-streaming` remains `no`.

## Resolved schema modules travel with saved validation

The constant serializer can now persist a compiled `XmlSchemaSet` as a module graph. It saves the actual schema documents, source URIs, resolved include/import/redefine links, shared identities and compilation policy. Loading reconnects those links before preprocessing and compilation. XML parsing prohibits DTDs, and the restored set has a null resolver: original schema locations remain provenance rather than runtime fetches.

This uses the BCL's [resolved schema reference property](https://learn.microsoft.com/en-us/dotnet/api/system.xml.schema.xmlschemaexternal.schema?view=net-10.0) and [schema-set collection](https://learn.microsoft.com/en-us/dotnet/api/system.xml.schema.xmlschemaset.schemas?view=net-10.0), including indirectly imported schemas. The graph also traverses included modules that are not separate entries in that collection. Tests cover cycles, sharing, imports, chameleon namespaces, redefine facets, defaults and malformed graph sizes/edges. An uncompiled input set is rejected without being silently compiled or changed.

A nonempty schema-set constant selects format **4**. Tables without one continue to use format **3**; existing formats **1–3** remain readable. The new kind is appended without renumbering old tags. Saved validation reconstructs real validators and type annotations from the captured definitions. No runtime helper signature, template ABI or public API changes.

The unconditional `CompileToAssembly` schema-import guard is removed. Unsupported schema-dependent constant kinds retain explicit rejection: user-defined cast/sequence/function metadata, schema name/evaluation contexts and typed static nodes are not silently written in an untyped or builtin-only representation. Atomic values and map key types also reject unsupported user-defined metadata. These are remaining implementation work, not a claim of complete schema-aware saving.

## Managed and native evidence

Six public save/load cases fail against the unchanged kernel at the old guard. The corrected implementation passes normal/debug and tree/streamed validation, including real strict/lax errors. Four additional public cases remove the original included schema resources before loading and executing. The fixture checks distinct source resources and no new schema retrieval after compilation; it does not assume a particular count of compiler reads.

The targeted cohort passes **28 net10.0 / 18 net8.0** checks, plus updated schema constant round-trip and legacy-format checks in the full suite. Actual branch and isolated full Release/Debug each pass **3,486 net10.0 / 3,358 net8.0**. W3C **55**, upstream **1,654 per framework**, differential **394 per framework** and controller checks **13** pass. All nine profile headings and failure-ID sets remain unchanged: strict streaming **2,695/16/1**, zero unmatched codes; QT3 **21,884/0/99**, with **25** unmatched-code notes. No baseline, suite assertion or error-tolerance policy changes.

NativeAOT passes **28** real validation/facet/error/ownership checks across **12 new normal/debug saved stylesheets that import schemas**, plus **237 retained saved stylesheets** and the compatibility helpers. This is actual saved execution, extending the preceding checkpoint's internal runtime-only schema checks. Included schema resources exist only in the producer's configured resolver; the consumer rejects any attempt to fetch them. The final native publication has warnings as errors and zero warnings. Its original compatibility probe marker was updated to refer to this separate saved-validation coverage.

The frozen cohort contains **415** core/configuration files, **252** artifacts, **291** consumer files and two producer files. All **297** actual saved-reference paths are separately hashed. Three auxiliary test source hashes record the applied fixtures. Final checks are `/tmp/xsl3-p7-saved-schema-native/final-check.json`.

## Cross-framework saved-DLL portability remains open

The new saved-DLL consumer on .NET 8 fails with `FileNotFoundException` for `System.Private.Xml, Version=10.0.0.0`. A load-only check of an older saved stylesheet initially passed; executing it then exposed the same problem. A second retained URI stylesheet fails at delegate creation. Both retained DLLs are byte-identical to their preceding accepted checkpoints. The failure occurs in generated framework references before the new schema payload is decoded.

Therefore, codec tests passing on .NET 8 do **not** establish that DLLs emitted on .NET 10 run on .NET 8. This broader portability gap also contradicts the existing general API comment promising that cross-runtime execution; it remains an explicit item to fix. No assembly binding redirect, substituted framework DLL or runtime resolver workaround is installed. Original failed consumers, the retained-style attribution checks, source/projects, logs and exit statuses remain under `/tmp/xsl3-p7-saved-schema-net8-consumer/` and `/tmp/xsl3-p7-saved-schema-net8-baseline/`, separately hashed by `cross-framework-evidence-sha256.json`.

## Ordinary performance and remaining requirements

The original ordinary three-pair gate passes against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`: time geometric mean **0.974702126523** over **39** timed rows, maximum allocation ratio **1.037305995787**, exit **0**. Raw rounds and comparison remain in `/tmp/xsl3-p7-saved-schema-final-perf-data/`.

Individual timing ratios above 1.05 remain recorded: regex/1kb **1.170213**. An aggregate pass establishes no per-row win. All live/frozen source hashes, three auxiliary hashes, retained/fresh saved references, protected version-2 fork and artifact/consumer/producer manifests match after the gate.

This change adds no new exact-size memory claim. Earlier matrices remain evidence for their own frozen source. Full schema-dependent constant metadata, typed static-node persistence, saved-DLL framework portability, exact 20 MiB push RSS, canonical throughput, analysis-suite reconciliation, general retained models and current-source Linux evidence remain open.

## Follow-up: portable framework references

The later [portable-emission checkpoint](streaming-portable-emitter-2026-10-26.md) fixes framework references for newly emitted styles and verifies actual execution on .NET 8. The failed consumers above remain historical evidence; old DLLs are not rewritten. General schema metadata remains incomplete.

## Follow-up: schema-dependent metadata

The later [schema-metadata checkpoint](streaming-schema-metadata-2026-10-26.md) adds registered schema contexts, named atomic/cast metadata and recursive sequence/function/node tests. Typed static-node persistence and anonymous atomic metadata remain open. Earlier measurements and failed consumers remain historical evidence.
