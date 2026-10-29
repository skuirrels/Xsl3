# Changelog

## Unreleased — developer diagnostics (P7E audit)

- Add isolated, explicitly selected identity memory diagnostics with source/artifact provenance,
  bounded launcher hashing and invalid-evidence safeguards. Preserve scheduled/default acceptance jobs.
- Close the [P7E audit](docs/p7e-memory-diagnosis-2026-10-28.md) with complete 1.1.3 memory matrices
  and repeated Linux/macOS NativeAOT identity measurements. No runtime or package change; 1.1.3 remains
  the current release. Existing memory limits and unsupported-streaming status are unchanged.

## [1.1.3](https://github.com/skuirrels/Xsl3/releases/tag/v1.1.3) — 2026-10-28

- Treat `validation="lax"` as annotation stripping with `SchemaAware=false`, as required for basic
  XSLT processing. Source and result documents now compile through both public APIs.
- Apply explicit annotation stripping to grounded source documents, including basic lax/preserve
  normalization, instead of retaining types from a validating reader. Apply whitespace rules to the
  stripped content, respect `xml:space`, and preserve namespaces, IDs/IDREFs and source URI metadata.
  Positioned 2.0+ readers enter filtering through the shared scoped view. Experimental forward execution
  retains the same policy and borrowed-reader ownership.
- Close the six-case [P7D contract investigation](docs/plan/p7d-validation-contracts.md) with public-path,
  saved-style and NativeAOT regressions. Preserve the raw fixtures, 16-case strict-forward inventory,
  schema-aware lax `xs:anyType` behavior and strict missing-import errors. No public API or saved-format change.

Update `Xsl3`, `Xsl3.Build` and `Xsl3.Cli` together to 1.1.3 and regenerate affected saved stylesheets
with matching compiler/runtime packages. See the [release notes](docs/releases/1.1.3.md).

## [1.1.2](https://github.com/skuirrels/Xsl3/releases/tag/v1.1.2) — 2026-10-27

P7C closes the bounded positioned source-reader correctness phase. Normal execution remains tree-based
and `supports-streaming` remains `no`.

- P7C: restrict positioned resolver readers to the selected element during experimental
  `xsl:source-document` execution. Following siblings are excluded; borrowed readers stay open
  at the selected boundary after success, early exit or a body error.
- Preserve inherited namespace bindings, including prefixes used only in QName values,
  when scoping positioned source and initial readers. Keep validation and annotation policies.
- Add normal/debug, two-API and saved-style regression coverage and retain the complete
  strict-forward failure inventory in CI. The gate also rejects lost cases, changed skips and
  unmatched expected-error codes. Streaming remains unsupported; no public API or
  saved-format change. See the [bounded P7C plan](docs/plan/p7c-runtime-correctness.md).

Update `Xsl3`, `Xsl3.Build` and `Xsl3.Cli` together to 1.1.2 and regenerate saved stylesheets with matching
compiler/runtime packages. See the [release notes](docs/releases/1.1.2.md).

## [1.1.1](https://github.com/skuirrels/Xsl3/releases/tag/v1.1.1) — 2026-10-27

P7B closes the approved analysis phase. Normal execution remains tree-based and `supports-streaming` remains `no`.

- Reject instructions processed with XSLT 1.0 behavior in declared-streamable content with `XTSE3430`,
  as required by §3.9.1. Explicit `Fallback` and ordinary non-streamable compatibility processing remain available.
- Complete bounded P7B with the owner-approved 1,000-case reviewed analysis suite and all 155 reviewed
  static-diagnostic/result cases, preserving original case/part mappings and unmodified suite results.
  Expected outputs come from specification rules and canonical fixture data; streaming remains unsupported.
- Preserve recorded streamability diagnostics before lowering rejected units, including Debug `xsl:number`
  cases and Explain recompilation. Developer `qil`/`qilopt` commands retain the diagnostics and exit cleanly
  when a rejected compilation produces no QIL.
- Add independent fixture preparation, complete expression mapping and integrity checks, exact-code diagnostics,
  and separate Release/Debug CI gates and reports. The original upstream 919-case results and raw S2 discrepancies remain visible.

No public API or saved-assembly format changes. Update all three packages together to 1.1.1 and regenerate saved
stylesheets with matching compiler/runtime packages. Backwards-compatible instructions in declared-streamable
content can now raise `XTSE3430` under Strict analysis; explicit Fallback permits ordinary tree execution.
See the [release notes](docs/releases/1.1.1.md).

## [1.1.0](https://github.com/skuirrels/Xsl3/releases/tag/v1.1.0) — 2026-10-26

P7A closes the implemented streaming foundations. Streaming remains experimental and unsupported;
`supports-streaming` continues to return `no`. Normal execution remains tree-based.

### Added

- Streamability analysis and `XsltSettings.Streamability` with Strict/Fallback policies.
- Reader/stream entry points for the XSLT 3.0 API.
- Per-invocation secondary results through `XsltArgumentList.ResultDocumentHandler`, including saved stylesheets
  under NativeAOT. Xsl3 owns and disposes the returned writers.
- Experimental forward execution for implemented initial-mode/source-document, iterate, grouping, accumulator,
  merge, fork and explicit-grounding cases, with separate conformance and process measurements.

### Fixed and improved

- Focus, parameter, shared document/global/key resources, fork ownership and result lifetimes.
- Source validation and annotation/whitespace handling.
- Saved stylesheet portability across .NET 8/10, persisted schema graphs and typed static nodes, with explicit
  errors for unsupported metadata and compatibility coverage for retained formats.
- Preserve the built-in type of schema-qualified attribute patterns when saving stylesheets.
- Temporal context initialization is deferred until needed; integer lexical parsing preserves arbitrary precision.

### Upgrade notes

Streamability declarations are now checked with Strict analysis by default and can raise `XTSE3430`.
Select `XsltStreamabilityMode.Fallback` explicitly to permit ordinary tree execution when analysis rejects a
declaration. Update `Xsl3`, `Xsl3.Build` and `Xsl3.Cli` together and regenerate saved stylesheets with the matching
compiler/runtime versions. See the [release notes](docs/releases/1.1.0.md) and [migration guide](docs/migration.md).

### Deferred

Full P7 analysis conformance, general retained-model bounds, the 20 MiB absolute memory target and several
streaming throughput targets remain unfinished. Their unchanged acceptance checks run separately from the
supported release CI. See the [phase close-out](docs/plan/p7a-closeout.md).

## [1.0.2](https://github.com/skuirrels/Xsl3/releases/tag/v1.0.2) — 2026-10-22

- Reuse output-writer buffers per thread, reducing allocation for small transformations.
- No public API or output changes; conformance is unchanged from 1.0.1.

## [1.0.1](https://github.com/skuirrels/Xsl3/releases/tag/v1.0.1) — 2026-10-22

- Improve regular expressions, `xsl:iterate`, dynamic function calls and homogeneous numeric `min`/`max`.
- No public API or output changes; conformance is unchanged from 1.0.0.

Earlier releases are recorded in the [GitHub release history](https://github.com/skuirrels/Xsl3/releases).
