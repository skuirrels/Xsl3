# Annotation stripping before streamed whitespace filtering, 2026-10-26

This follows the [source-validation negotiation checkpoint](streaming-source-validation-negotiation-2026-10-26.md) on `feature/p7-streaming-analysis`. Full Phase 7 remains active and `supports-streaming` remains `no`.

## Reproduced issue and correction

A schema-validating input reader exposes the declared type at each element header. Whitespace filtering used that type to preserve simple-content whitespace before the outer streamed reader hid annotations. With `input-type-annotations="strip"` and `xsl:strip-space elements="*"`, a source `<doc><text>  </text></doc>` therefore exposed an untyped two-character value instead of the empty value required after stripping.

The shared whitespace reader now receives the effective annotation-stripping policy from the streamed source opener and initial-source path. Its whitespace decision ignores the simple-content preservation exception when annotations are stripped. Validation still reads the original input in its existing position in the chain. No extra reader wrapper or materialized tree is added.

This follows the preprocessing order in [XSLT 3.0 §4.4 and §4.4.1](https://www.w3.org/TR/xslt-30/#stripping-annotations). Explicit `validation` or `type` on [xsl:source-document](https://www.w3.org/TR/xslt-30/#element-source-document) overrides the package's input-annotation policy; explicit `preserve` still preserves typed simple-content whitespace. `xml:space="preserve"` also remains effective. Three existing internal core files change; helper signatures, template ABI and saved-constant formats stay unchanged.

Eight managed cases cover both streamed entry paths, implicit/explicit stripping, explicit preserving, typed-input preserving and `xml:space`. Against unchanged core, **three fail and five pass per framework**. All eight pass after the correction. The initial fixture emitted several adjacent result items, introducing spaces through XDM sequence formatting; that unsuccessful fixture and log are retained separately before the corrected single-string red/green comparison.

## Functional and profile verification

The actual branch and isolated frozen cohort both pass Release and Debug **3,457 net10.0 / 3,339 net8.0**. W3C **55**, upstream **1,654 per framework**, differential **394 per framework** and controller checks **13** pass. All nine profile headings and failure-ID sets remain unchanged. Strict streaming stays **2,695 passed / 16 failed / 1 not run**, with **zero** unmatched expected codes; QT3 stays **21,884/0/99**, with **25** unmatched-code notes.

No baseline, suite assertion, feature flag or error-tolerance policy changes. Existing unresolved suite expectations remain visible. For example, the reverse/snapshot path case expects reverse node order even though the final [XPath 3.1 path rule](https://www.w3.org/TR/xpath-31/#id-path-expressions) sorts a node-valued path into document order. The source-validation annotation case expecting stripped values despite explicit strict validation conflicts with the final source-document override rule. These observations do not silently convert suite failures into passes.

## Saved and native verification

NativeAOT passes **24** validation-backed checks across **12 new normal/debug saved stylesheets**, together with **225 retained saved stylesheets** and the existing compatibility helpers. The new stylesheets compile schema-aware using built-in types and receive real schema-validating readers at execution. They import no schema. Consequently, these saved tests do not establish persistence of imported schemas: the existing refusal to save a stylesheet with imported schemas remains in place.

The first native fixture omitted the schema-aware compilation option and included its new probe twice. Its terminal failure, original styles, DLLs, source and publication warning are preserved under `/tmp/xsl3-p7-annotation-whitespace-before-schema-aware-option/`. The corrected fixture matches managed compilation settings, contains one source inclusion, and publishes with warnings as errors and zero warnings. Product source does not change for this fixture repair.

The final cohort freezes **414** core/configuration files, **240** artifacts, **278** consumer files and two producer files. All **285** actual saved-reference paths are separately hashed. Final checks are `/tmp/xsl3-p7-annotation-whitespace-native/final-check.json`.

## Ordinary performance and remaining limits

The original ordinary three-pair gate passes against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`: time geometric mean **0.999821546864** over **39** timed rows, maximum allocation ratio **1.037310200701**, exit **0**. Raw rounds and comparison remain in `/tmp/xsl3-p7-annotation-whitespace-final-perf-data/`.

Individual timing ratios above 1.05 remain recorded: join/compile **1.066138**, regex/1kb **1.170213**. An aggregate pass establishes no per-row win. All live/frozen source hashes, one auxiliary hash, retained/fresh saved references, protected version-2 fork and artifact/consumer/producer manifests match after the gate.

This reader-policy correction adds no new exact-size memory claim. Earlier matrices remain evidence for their own frozen source. Exact 20 MiB push RSS, canonical throughput, analysis-suite reconciliation, general retained models, imported-schema saved compilation and current-source Linux evidence remain open.

## Follow-up: saved imported-schema validation

The later [saved-schema checkpoint](streaming-saved-schema-2026-10-26.md) opens supported saved source-validation paths using captured module graphs. General schema-aware metadata and framework portability remain incomplete. This report is evidence for its preceding frozen source.
