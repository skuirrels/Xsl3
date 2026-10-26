# Changelog

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
