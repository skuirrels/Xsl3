# Portable saved framework references, 2026-10-26

This follows the [saved-schema checkpoint](streaming-saved-schema-2026-10-26.md) on `feature/p7-streaming-analysis`. It addresses the saved-DLL framework-reference failure found there. General schema-dependent constant metadata and the full Phase 7 exit remain incomplete; `supports-streaming` remains `no`.

## Failure and emitted reference contract

DLLs emitted on .NET 10 referenced `System.Private.Xml, Version=10.0.0.0`. The .NET 8 consumer failed at delegate creation or when an emitted method was first executed. Both the new imported-schema cases and two byte-identical retained non-schema styles reproduced the failure. A successful load-only check did not establish execution compatibility.

Normal and debug `CompileToAssembly` output now receives a narrow framework-reference normalization before it is written to the caller's stream. The target comes from the core project's single minimum-target property, also used in `TargetFrameworks`; its generated assembly metadata currently specifies the .NET 8 BCL contract. The helper recognizes the actual core/XML assembly names, culture and full public key or token. It changes only newer version fields of those references. Xsl3 and unrelated assembly identities remain unchanged. It adds no package or runtime dependency and installs no loader binding redirect.

The implementation uses public PE/metadata readers to locate AssemblyRef rows. ECMA-335 II.22.5 places four UInt16 version fields at each row's start; the remaining columns, heap offsets, IL, initialized data and embedded PDB bytes retain their positions. This is a controlled emission step for the engine's common supported API, not a general retargeter for arbitrary third-party code. Both core targets continue to compile. Microsoft's [PersistedAssemblyBuilder documentation](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.persistedassemblybuilder?view=net-10.0) distinguishes current-runtime emission from cross-runtime reference contracts; the previous direct runtime references did not satisfy this project's .NET 8 execution promise.

A Cecil-based experiment first isolated the reference problem in separate copies, using actual installed .NET 8 assembly identities. The applied implementation uses no Cecil dependency. Its initial identity check skipped full-public-key references; that failure is preserved and corrected by comparing the appropriate key representation. Two fixture assumptions were also corrected: existing PDB documents use local file paths, and the synthetic class has a generated default constructor. No functional assertion or reference identity check was relaxed.

## Managed and native verification

Four new unit cases verify normal/debug framework references, embedded source mapping/sequence points, unchanged method bodies and every byte outside the reference-version columns, plus preservation of an unrelated assembly's declared version.

The actual branch and isolated frozen cohort pass Release and Debug **3,490 net10.0 / 3,358 net8.0**. W3C **55**, upstream **1,654 per framework**, differential **394 per framework** and controller checks **13** pass. All nine profile headings and failure-ID sets remain unchanged: strict streaming **2,695/16/1**, zero unmatched expected codes; QT3 **21,884/0/99**, with **25** unmatched-code notes. No baseline, suite assertion, error-tolerance policy or feature flag changes.

The actual compiler emits **14 fresh DLLs**: twelve normal/debug imported-schema styles and the two ordinary shapes that previously failed. The .NET 8 consumer passes all **30** validation, facet, error, ownership, whitespace and URI checks against those actual emissions. NativeAOT passes the same fresh cases plus **249 retained saved stylesheets** and compatibility helpers; publication retains warnings as errors and reports none.

A broader managed corpus audit uses the production normalization helper to create separate copies of every **311 actual saved-reference paths**, including older constant formats and the version-2 compatibility case. All functional probes pass on both .NET 8 and .NET 10. Only two host assertions are adapted to require managed execution instead of native execution; result/error/identity/ownership assertions remain intact. Original native consumers and their host assertions remain unchanged and pass independently. The audit is preserved under `/tmp/xsl3-p7-portable-corpus-audit/` with source, projects, path mapping, copies, logs and exit statuses.

Original retained DLLs are never rewritten. Previously emitted files carrying the producer's framework references need regeneration with the corrected compiler for .NET 8 execution; loading an old Type does not repair its IL or references. The older failed consumers remain historical evidence.

## Frozen source, performance and remaining scope

The cohort freezes **416** core/configuration files, **266** artifacts, **307** consumer files and two producer files. All **311** actual native saved-reference paths and the separate cross-runtime audit are hashed. One auxiliary test-source hash records the applied fixture. Final checks are `/tmp/xsl3-p7-portable-emitter-native/final-check.json`.

The original ordinary three-pair gate passes against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`: time geometric mean **0.988417566779** over **39** timed rows, maximum allocation ratio **1.037310200701**, exit **0**. Raw rounds and comparison remain in `/tmp/xsl3-p7-portable-emitter-final-perf-data/`.

Individual timing ratios above 1.05 remain recorded: dispatch/1kb **1.082192**, regex/1kb **1.170213**. An aggregate pass establishes no per-row win. All live/frozen source hashes, one auxiliary hash, retained/fresh saved references, protected version-2 fork and artifact/consumer/producer manifests match after the gate.

This emission correction adds no new exact-size memory claim. Earlier matrices remain evidence for their own frozen source. Full schema-dependent constant metadata, typed static-node persistence, exact 20 MiB push RSS, canonical throughput, analysis-suite reconciliation, general retained models and current-source Linux evidence remain open.

## Follow-up: schema-dependent metadata

The later [schema-metadata checkpoint](streaming-schema-metadata-2026-10-26.md) adds registered schema contexts, named atomic/cast metadata and recursive sequence/function/node tests. Typed static-node persistence and anonymous atomic metadata remain open. Earlier measurements and failed consumers remain historical evidence.
