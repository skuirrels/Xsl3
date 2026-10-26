# Streamed source validation negotiation, 2026-10-26

This follows the [reference and diagnostic checkpoint](streaming-reference-diagnostics-2026-10-26.md) on `feature/p7-streaming-analysis`. Full Phase 7 remains active; `supports-streaming` remains `no`.

## Validation and resolver contracts

An explicitly validating `xsl:source-document` could receive an already validating reader from its resolver. The provider's validator then reported its own error before the instruction's validator ran. This affected strict streaming case `streamable-149`: the expected instruction error XTTE1510 was obscured by provider validation and retrieval wrapping.

The streamed opener now requests `Stream` when it must apply explicit validation and the resolver advertises support for streams. Reader-only resolvers retain their reader contract. Without instruction validation, provider validation retains its original identity. The W3C resolver honors the explicit stream request and returns the actual configured resource bytes. Null requests continue to receive the environment's configured validating reader. This uses the existing [XmlResolver.SupportsType](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlresolver.supportstype?view=net-10.0) and [GetEntity](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlresolver.getentity?view=net-10.0) negotiation contract.

One existing core file, one harness file and seven new unit cases change. No helper signature, ordinary template ABI or saved-constant format changes. The tests exercise real strict/lax/type validation, valid inputs through both resolver capabilities, provider-error preservation, owned-stream closure and borrowed-reader ownership. Before the correction, four of seven fail on each framework; all seven pass afterward.

## Functional and conformance evidence

The isolated full cohort passes Release and Debug **3,449 net10.0 / 3,331 net8.0**, W3C **55**, upstream **1,654 per framework**, differential **394 per framework**, and controller checks **13**. All nine profile failure-ID sets are unchanged. Strict streaming stays **2,695 passed / 16 failed / 1 not run**, with unmatched expected codes improving **1 to 0**. QT3 stays **21,884 passed / 0 failed / 99 not run**, with **25** unmatched-code notes. No baseline, assertion or feature flag changes.

The first applied-branch incremental build reused an older compiled library: the copied source retained a timestamp older than the existing output. Four tests reproduced the pre-correction behavior. That terminal failure and binary hashes are retained under `/tmp/xsl3-p7-source-validation-negotiation-stale-live-output/`. Updating only the changed source timestamps forces normal compilation; the actual branch then passes the solution build and both unit configurations at **3,449 / 3,331**. Source hashes are unchanged by this rebuild repair.

The grounded development profile retains one unmatched source-provider validation error. This streamed opener change does not alter grounded document caching or `fn:doc` retrieval.

## Native evidence and saved-schema limitation

The final native consumer passes real runtime schema negotiation and error checks plus **225 retained saved stylesheets** and the compatibility helpers. Native publication retains warnings as errors and reports none. The runtime checks use an existing saved stylesheet's real static data, a real `XmlQueryRuntime`, actual configured input and schema validators; they verify instruction errors, valid typed values, provider-error identity and ownership.

**Saving stylesheets that import schemas remains unsupported.** The initial attempt to save schema-importing stylesheets terminated at the existing explicit guard because imported schemas are not persisted. The failed producer, source, logs and artifacts remain under `/tmp/xsl3-p7-source-validation-negotiation-before-schema-save-check/`. The guard is unchanged. The final native runtime probe is not a successful schema-aware saved round trip. No new saved stylesheet is claimed in this checkpoint; all 225 retained DLLs are byte-identical to the preceding accepted checkpoint.

An intermediate native assertion inspected the BCL schema type before validation completion. The final test checks the project's existing start-header type policy, consumes the value, then checks the completed BCL type. Both the unsuccessful probe and final successful run remain preserved.

## Ordinary performance and remaining gates

The original ordinary three-pair gate passes against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`: time geometric mean **1.020324699197** over **39** timed rows, maximum allocation ratio **1.037120979577**, exit **0**. Raw rounds and comparison remain in `/tmp/xsl3-p7-source-validation-negotiation-final-perf-data/`.

Individual timing ratios above 1.05 remain recorded: dispatch/compile **1.114789**, grouping/compile **1.073763**, identity/1kb **1.071429**, join/compile **1.062771**, keys/compile **1.121669**, regex/1kb **1.170213**, sorting/compile **1.271159**. An aggregate pass establishes no per-row win. All live/frozen source hashes, two auxiliary hashes, retained saved references, protected version-2 fork and artifact/consumer/producer manifests match after the gate.

The frozen cohort contains **414** core/configuration files, **228** artifacts, **265** consumer files and two producer files. All **273** actual saved-reference paths are separately hashed. Final checks are recorded in `/tmp/xsl3-p7-source-validation-negotiation-native/final-check.json`.

This peripheral change adds no new exact-size memory claim. Earlier memory matrices remain evidence for their own frozen source. Exact 20 MiB push RSS, canonical throughput, analysis-suite reconciliation, general retained models, imported-schema saved compilation and current-source Linux evidence remain open.

## Follow-up: annotation and whitespace ordering

The later [reader-policy checkpoint](streaming-annotation-whitespace-2026-10-26.md) corrects preprocessing order for stripped streamed inputs, with new normal/debug saved styles using supplied validators. The imported-schema persistence limitation remains. This report is evidence for its preceding frozen source.
