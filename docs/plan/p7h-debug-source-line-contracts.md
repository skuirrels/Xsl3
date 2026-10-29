# P7H: Debug compiler source-line contracts

Scope frozen on 2026-10-29. Branch: `feature/p7h-debug-source-line-contracts`.
Baseline: released 1.1.5, `54df5c5ba8b34fc382dc3e7f6850fe3a6f4e8ac6`.

**Bounded implementation and local close-out are complete.** All six dispositions and local gates
have evidence in the [close-out](../p7h-debug-source-lines-2026-10-29.md),
[case inventory](p7h-debug-source-line-cases.csv) and [snapshot](p7h-results.json).
Stop implementation here. [PR #26](https://github.com/skuirrels/Xsl3/pull/26) is merged after all
four required CI checks passed. Release version: 1.1.6; see the [release notes](../releases/1.1.6.md).

Resolve [P7G D1](p7g-deferred.md): a bare variable returned by `xsl:sequence`
must compile with stylesheet debugging enabled when the library is built in Debug.
Retain the original reproduction and all assertions. An instruction's sequence point
must not overwrite the declaration's source location or change variable identity/evaluation.

## Frozen contracts

Reuse all six P7G contracts (DTD ID/IDREF/IDREFS × preserve/strip), with the original
`select="$second"` expression restored in a separate fixture variant. Retain the existing
`$second/self::attribute()` variant as an independent regression control. Do not change
the expected identity properties, constructor policies, inputs, or P7G inventories.

## Completion criteria

1. Reproduce the assertion on unchanged 1.1.5 compiler source in Debug; retain the failure
   and a Release positive control. Establish the source-location ownership defect before fixing it.
2. Correct only the compiler metadata path needed for bare-variable instructions. Preserve
   assertions, declaration locations, per-instruction locations, and once-only variable evaluation.
   No public API, saved-format, runtime identity, or unsupported-feature changes.
3. Pass the six contracts through both public source APIs, normal/debug styles, grounded/forward
   execution, positioned/initial readers and both DTD forms, in Release/Debug on .NET 8/10.
   Verify source-location ownership directly and externally visible error line reporting.
4. Generate normal/debug saved styles with both Release and Debug compilers. Run them on .NET 8/10
   and full-globalization NativeAOT, with independently checked outputs and execution modes.
5. Pass full managed Release/Debug, upstream/differential/conformance, reviewed S1/S2,
   retained raw/strict/unstreamed baselines, and the ordinary performance gate. Preserve all
   unrelated failure inventories and thresholds. Required hosted CI must pass before merging.
6. Review the bounded diff and record exact source/artifact provenance, results, documentation
   and a close-out. Stop implementation when these criteria are satisfied.

## Release boundary

The verified correction is included in the 1.1.6 release. The bounded implementation is closed;
publication and public-package verification remain distinct release evidence. Defer unrelated findings.
This phase does not resume full P7, the 20 MiB memory target, startup/performance
optimization, new streaming constructs or broad compiler refactoring. Streaming remains unsupported.
