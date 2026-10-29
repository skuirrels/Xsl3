# P7G: copied-attribute identity contracts

Scope frozen on 2026-10-29. Branch: `feature/p7g-attribute-identity-contracts`.
Released runtime baseline: 1.1.4, `09af4ab84766dffc058a2c20c58c2d26795db843`.
Branch base: `0fa19cdc3a4e048b7664effe15ba987f57687ad5` (README erratum only).

**Bounded implementation and local validation are complete.** All six contracts have conclusive
dispositions and the full functional, saved/native and performance checks pass. See the
[close-out](../p7g-attribute-identity-2026-10-29.md), [case inventory](p7g-identity-cases.csv) and
[result snapshot](p7g-results.json). [PR #25](https://github.com/skuirrels/Xsl3/pull/25) carries the
1.1.5 release; its [required CI checks](https://github.com/skuirrels/Xsl3/pull/25/checks) gate merging.
Stop implementation here. Release tagging/publication remain separate.

## Six contracts

| Case | DTD declaration | Attribute-copy validation |
|---|---|---|
| ID-preserve | ID | preserve |
| ID-strip | ID | strip |
| IDREF-preserve | IDREF | preserve |
| IDREF-strip | IDREF | strip |
| IDREFS-preserve | IDREFS | preserve |
| IDREFS-strip | IDREFS | strip |

Begin with [P7F D1](p7f-deferred.md). Establish specification-backed expectations before correcting
runtime behavior. Check source metadata and lookups, detached copies, and copies inserted into new
documents with explicit construction validation. Keep `xml:id`, names/values, parentlessness,
independent identity, ordinary attributes and borrowed-reader ownership as regression controls.
Use fully declared and ATTLIST-only DTD inputs to distinguish source-reader metadata from copying.

## Completion criteria

1. Preserve an independent 1.1.4 baseline and all original failures. Give each of the six contracts a
   conclusive, sourced disposition with positive controls at every stage.
2. Correct only the shared metadata-transfer paths needed by these contracts. No fixture-dependent
   runtime behavior, public API or saved-format changes.
3. Cover both public APIs, grounded/forward execution, normal/debug source and saved styles,
   .NET 8/10 consumers, and NativeAOT saved consumers. Existing `xml:id`, ordinary attribute and
   reader-ownership controls must pass.
4. Pass managed Release/Debug, upstream/differential/conformance, reviewed S1/S2, strict/unstreamed
   baselines and the ordinary performance gate. Preserve unrelated failure sets and original fixtures.
   Compare the existing P7F identity workload against 1.1.4 using three fresh processes at each original
   size (10,485,760; 209,715,200; 2,147,483,648 bytes): no more than 5% median transformation allocation
   growth. Retain raw runs and independently verify full input/output; keep the 20 MiB heap guard and
   diagnostic RSS policy unchanged. This is an allocation regression check, not a memory acceptance claim.
5. Review the bounded diff, update README/changelog/candidate release notes, and publish a close-out
   with exact source/artifact provenance and six-case results.

## Stopping rule and release boundary

Stop when all six dispositions and the gates above have evidence. Record unrelated findings separately.
Do not expand into startup footprint, the 20 MiB memory target, new streaming constructs, broad schema
work, other performance hotspots or full P7. `supports-streaming` remains `no`.

A verified consumer fix can become a 1.1.5 candidate. If the six contracts expose only fixture issues,
close as an audit without manufacturing a runtime patch. Tagging/publication remain a separate release
step; this phase does not authorize publishing packages.
