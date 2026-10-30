# Changelog

## Unreleased

- Retain actual primary compiler/runtime diagnostics and initial-source declarations in optional
  conformance exports. Require complete evaluation observations in strict CI audits and document
  every case without forwarding evidence while preserving raw outcomes and the original inventory.
- Verify physical input reads and actual sink emission for all seven canonical streaming workloads
  through both APIs and saved execution. Guard incremental reads against eager consumption, preserve
  complete-input accumulator/iterate totals, and validate original independent output fingerprints.
- Reuse each output writer's removal callback when reattaching its attribute cache. Preserve
  nested and pooled output ownership, duplicate attributes, saved execution and XML/HTML selection.
- Reject process-probe reports whose live managed heap exceeds the configured cap even when
  their status claims success; preserve the original memory limits and retained measurements.
- Capture actual forward/tree source representations during optional strict-suite audits,
  including failed source openings and nested, dedicated-thread and fork execution. Separate
  preparation, compilation, stylesheet execution/replay and assertion observations. Require
  every frozen forwarding case in hosted strict audits while preserving raw conformance results.
- Publish tee reader counts atomically so source-batch checks avoid taking the ring lock.
  Preserve reader detachment, payload release, configured capacity and cancellation.
- Reject function items, maps and arrays in atomic type checks without dereferencing absent
  schema annotations; general sequence checks consistently recognize them as items.
- Reuse immutable fork attribute and namespace arrays when prong atomization leaves their
  references unchanged. Preserve private name-table atoms and independent schema snapshots.
- Copy mutable schema snapshots for fork boundary and ancestor headers as well as events,
  so a prong cannot alter another prong's node or attribute validation metadata.
- Reject ambiguous, out-of-range or non-SDK assembly-version metadata and use the authoritative
  assembly header instead. Emit SDK provenance only alongside an SDK-generated version attribute;
  verify actual .NET 8/10 headers for standard, normalized, disabled and manual configurations.
- Hash benchmark artifacts with bounded reads so Linux child memory observations are not
  inflated by whole-image allocations in the controller before process creation. Retain tested
  benchmark and native binaries alongside hosted acceptance reports for independent verification.
- Preserve only the XML reader reflection members used by the bridge during NativeAOT trimming.
  Shared unit/native contracts verify resolver identity, explicit null policies, whitespace and external DTD reads.
- Write the native streaming probe's JSON protocol as explicit UTF-8 without a BOM, avoiding
  host console charset discovery while preserving full-lifetime memory measurement.
- Use the BCL's read-only invariant numeric data for XDM numbers and climbing-set plans,
  avoiding culture initialization for numeric-only work. Reuse typed captured numbers and
  accumulator priorities while preserving float lexical rounding, NaN normalization and errors.
- Read SDK-derived assembly-version metadata when it is canonical and preserve the original
  saved-stylesheet version comparison for differing versions. Short, padded, wildcard and
  metadata-disabled builds retain the assembly header's normalized version.
- Avoid observer-triggered culture initialization in the NativeAOT streaming probe: directly
  bind the same Unix RSS counter and retain round-trip numeric JSON reports. Both internal
  and external full-lifetime counters, original inputs, oracles and budgets remain in force.
- Keep isolated loader diagnostics compatible with both version-initialization paths without
  replacing their expressions or forcing the historical assembly-name lookup.
- Claim published fork events atomically to reduce shared ring-lock traffic. Preserve the
  configured capacity, caller-owned input, ordered faults, cancellation and payload release.
- Fix argument forwarding in the optional identity-allocation comparison and retain failed child
  reports instead of stopping before measurement.
- Store the first streamed accumulator phase directly on its frame, allocating a name dictionary
  only for additional accumulators. Skip empty rule phases while preserving initialization,
  publication, expanded-name matching, replay/copy values, fork ownership and cancellation.
- Extend isolated streaming-memory diagnostics with saved-stylesheet initialization/loading
  checkpoints and validation of their source hashes, complete phase inventory and legal ordering.
- Avoid temporary text trees for singleton literal simple content with a constant separator.
  Preserve streamed cancellation checkpoints, text-node results and the existing dynamic-content path.
- Preserve direct streamed emptiness tests when optimizing side-effecting expressions, fixing a
  Debug preservation-verifier failure in nested predicates.
- Use the existing type-code path for broad atomic checks, with correct exclusion of maps,
  arrays, functions and internal control items.
- Project normalized fork events directly into independent prong views and skip unselected
  buffered subtrees in batches while retaining bounded buffering, source ownership and error ordering.
- Pump requested fork source batches directly on the invoking thread, avoiding a synchronous queued
  callback per refill. Preserve the configured buffer capacity, callback ownership and cancellation.
- Reduce redundant wakeups during fork input consumption while preserving progress whenever buffer
  space is available, cancellation and caller-owned I/O.
- Reduce compact-tree short-value cache storage by retaining hashes and value-array indices instead
  of duplicate string-key references. Preserve complete ordinal interning, collision handling and
  the existing pooling boundary; full P7 memory and throughput qualification remains open.
- Store grounded compact-tree accumulator values at traversal changes in segmented storage,
  avoiding per-node string-key entries and large timeline resizes. Preserve exact before/after
  phases, partial failures, recursive-demand errors, cancellation, copy origins and fork sharing.
- Grow full compact-tree columns more densely to reduce excess capacity and transient resize
  memory. Preserve temporary-tree growth, node order, values, namespaces and attribute metadata.
- Append fixed-size node-column pages for large source trees to avoid whole-column resize peaks.
  Preserve linear node identities, navigation, attributes, namespaces and temporary-tree flat storage.
- Process merge snapshots incrementally for streamable-declared sources even when input is
  tree-backed. Preserve explicit tree selection, snapshots, group focus, sorting and source ownership.
- Store grounded unsorted adjacent groups as boundaries and materialize each action frame on demand.
  Preserve total context size, key evaluation/comparison, member identity, retained closures and
  cancellation. Untyped string keys use the canonical collation without per-row atom caches.
- Emit streamed atomization through the canonical atomizer without generic built-in dispatch.
  Saved static-data format 6 records whether numeric built-in IDs require fingerprint validation;
  older saved formats retain their compatibility checks.
- Use a direct streamed `string-join` path when its separator is a known string, sharing lexical
  conversion with the ordinary function while preserving overlapping values and schema atomization.
- Write streamed stripped attribute copies directly into constructed content. Raw sequences keep
  fresh parentless copies; schema stripping, ID/IDREF properties and namespace fixup are preserved.
- Avoid initializing default cultures when fork workers already inherit the captured execution
  context. Preserve explicit cultures, suppressed-flow behavior and worker state, and release
  captured ambient objects when a prong finishes.
- Full [Phase 7 qualification](docs/plan/p7-completion.md) remains in progress. Streaming support
  is not yet claimed; original memory, throughput and release gates remain required.

## [1.1.6](https://github.com/skuirrels/Xsl3/releases/tag/v1.1.6) — 2026-10-29

- Give bare XPath 3.1 variable references separate instruction source locations. Repeated
  `xsl:sequence select="$value"` uses retain their shared binding without overwriting its metadata
  or triggering the Debug compiler's source-line assertion.
- Cover the original six copied-attribute contracts with bare-variable expressions, declaration/use
  locations, once-only evaluation and error diagnostics. Generate normal/debug saved styles with
  Release and Debug compilers for .NET 8/10 and NativeAOT consumers.
- Retain public APIs, saved formats, identity behavior and all streaming-support boundaries.
  The stylesheet-reader reserialization finding remains [deferred](docs/plan/p7h-deferred.md).

Update all three packages together. See the [release notes](docs/releases/1.1.6.md) and
[bounded plan](docs/plan/p7h-debug-source-line-contracts.md).

## [1.1.5](https://github.com/skuirrels/Xsl3/releases/tag/v1.1.5) — 2026-10-29

- Preserve DTD `ID`, `IDREF` and `IDREFS` identity when XDM/XSLT 2.0+ input has ATTLIST-only
  declarations or the supplied reader has already passed its DOCTYPE. Carry the declarations into
  experimental forward execution and scoped/grounded reader views.
- Preserve IDREF/IDREFS metadata on parentless attributes, including copies made with
  `validation="strip"`. Reinserting copies into a document retains `id()`/`idref()` behavior under
  both preserve and strip construction policies. Names, values, independent node identity, `xml:id`
  normalization and borrowed-reader ownership remain covered.
- Add six bounded contracts, both public source APIs, normal/debug saved styles, .NET 8/10 and
  NativeAOT regressions. XSLT 1.0 compatibility, public APIs, saved formats and the P7F ordinary
  attribute optimization are retained. Unrelated compiler-debug work remains [deferred](docs/plan/p7g-deferred.md).
- Review: carry typed identity facts independently of annotations through intermediate/positioned reader views; register overlapping identity metadata once.

Update all three packages together. See the [release notes](docs/releases/1.1.5.md).

## [1.1.4](https://github.com/skuirrels/Xsl3/releases/tag/v1.1.4) — 2026-10-29

- Reduce allocation for ordinary untyped native attribute copies by constructing a minimal compact
  fragment. Preserve distinct parentless node identity and existing typed, DTD identity-sensitive and
  `xml:id` copy policies. No public API or saved-format change.
- Close [bounded P7F](docs/p7f-identity-allocation-2026-10-29.md): identity allocation falls **23.55%**
  across the three original sizes on Linux/macOS, with no measured throughput regression. Full memory
  matrices retain the unmet 20 MiB target; full P7 and supported streaming remain deferred.
- Add paired allocation/throughput diagnostics and an opt-in manual workflow mode using unchanged
  canonical memory jobs. Preserve all raw failures and the unrelated deferred DTD contract finding.

### Developer diagnostics (P7E audit)

- Add isolated, explicitly selected identity memory diagnostics with source/artifact provenance,
  bounded launcher hashing and invalid-evidence safeguards. Preserve scheduled/default acceptance jobs.
- Close the [P7E audit](docs/p7e-memory-diagnosis-2026-10-28.md) with complete 1.1.3 memory matrices
  and repeated Linux/macOS NativeAOT identity measurements. That audit introduced no runtime correction;
  its 1.1.3 baseline and original failures remain retained. Memory limits and unsupported-streaming status are unchanged.

Update `Xsl3`, `Xsl3.Build` and `Xsl3.Cli` together to 1.1.4 and regenerate saved stylesheets with matching
compiler/runtime packages. See the [release notes](docs/releases/1.1.4.md).

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
