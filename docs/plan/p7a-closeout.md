# P7A: streaming foundations release close-out

**Scope frozen on 2026-10-26. Release version: 1.1.0.** This phase closes the implemented foundations and
correctness work on `feature/p7-streaming-analysis`. It does not complete P7 or claim streaming support.
The owner requested a bounded release instead of continuing the full streaming goal. Further P7 implementation
and optimization are deferred; the original [P7 requirements and exit criteria](p7-streaming.md) remain unchanged.

## Included in this phase

- Streamability expression/construct analysis, retained declarations, Strict/Fallback settings and separate
  posture/sweep and streaming development baselines.
- Experimental forward inputs, streamed navigation, grouping, accumulators, iterate, merge, fork, explicit
  grounding/replay and reader entry points, with regression coverage for their implemented cases.
- Correctness fixes found during that work: focus and parameter lifetimes, shared document/global/key resources,
  cancellation and fork ownership, source validation and annotation/whitespace handling, and secondary-result delivery.
- Saved stylesheet portability across .NET 8/10, persisted schema graphs and typed static nodes, compatibility
  with retained saved formats, and explicit rejection of unsupported metadata.
- Deferred temporal-context initialization and integer lexical parsing improvements, validated against ordinary
  performance and compatibility gates.

Normal execution continues to build trees. `XsltSettings.Streamability` controls analysis; the internal development
switch `XSL3_STREAM_STRICT=1` enables experimental forward execution. `supports-streaming` remains `no`.
See [release notes and migration guidance](../releases/1.1.0.md).

The next proposed phase is [P7B: static streamability analysis close-out](p7b-analysis-closeout.md).
Its six-ticket plan addresses analysis reconciliation and static diagnostic evidence only; the full-P7
memory, throughput and execution obligations below remain deferred.

## Exit evidence

The frozen implementation has 418 source/configuration files, recorded by SHA-256 in the local release evidence.
The close-out initially changed documentation and CI scope only. Release CI then exposed test assumptions about
Windows newlines/thread scheduling and one saved schema-attribute pattern regression. Release finalization fixes
those test assumptions and preserves the built-in attribute type in saved metadata, with four new .NET 10 cases.
The original frozen measurements below retain their provenance; finalized Release tests pass 3,567 on .NET 10
and 3,416 on .NET 8. The targeted saved `match` conformance set passes 286/286 with no baseline changes.

| Check | Result at the frozen checkpoint |
|---|---|
| Unit tests, Release and Debug | 3,563 on .NET 10 and 3,416 on .NET 8, each configuration |
| Upstream XSLT 1.0 | 1,654 applicable tests per framework |
| Byte-for-byte differential | 394 per framework |
| W3C harness / Python controllers | 55 / 13 pass |
| QT3 evaluation | 21,884 passed, 0 failed, 99 not run; 25 unmatched expected-error codes |
| XSLT 2.0 profile | 4,590 passed, 8 failed, 10 not run; 7 unmatched expected-error codes |
| XSLT 3.0 profile | 8,849 passed, 19 failed, 41 not run; 34 unmatched expected-error codes |
| Nine conformance profiles | Headings and failure-ID sets unchanged from the preceding accepted checkpoint |
| Saved compatibility | 279 retained DLLs unchanged; 327 saved-reference paths; functional consumers pass on .NET 8/10 and NativeAOT |
| Ordinary three-pair performance gate | Pass: time geometric mean 0.992097; maximum allocation ratio 1.037339; 39 rows |

Expected-error code mismatches are tracked separately from the harness pass count. The ordinary performance gate
is aggregate evidence, not a claim that every timed row improved. Detailed results and provenance are in the
[final implementation checkpoint](../streaming-temporal-integer-2026-10-26.md).

The local package and clean-consumer evidence is recorded in the [release notes](../releases/1.1.0.md).
All three 1.1.0 packages build; isolated consumers pass on .NET 8/10, the packaged CLI passes both frameworks and
local tool installation, and the .NET 10 macOS ARM64 NativeAOT package consumer passes with warnings as errors
and globalization enabled. The local review bundle is `artifacts/releases/1.1.0/`.
Hosted Linux x64/Windows CI must still run against the integrated release commit before tagging.

## Deferred full-P7 obligations

These remain failures or unfinished work, rather than release claims:

| Original P7 obligation | Current evidence |
|---|---|
| S1 exact analysis conformance (at least 99%) | Posture/sweep profile: 790 passed, 129 failed; reconciliation remains open |
| 200 MiB input within 20 MiB absolute RSS/heap guard | macOS ARM64: 13/16 budget passes; virtualized Linux ARM64: 0/16 |
| 2 GiB input within 200 MiB absolute RSS/heap guard | 16/16 on macOS ARM64 and virtualized Linux ARM64; does not satisfy the separate 20 MiB target |
| 100 MB streaming no slower than tree | Fails for identity (1.291×), fork (3.714×) and source-document (1.262×); all 42 paired trials validate output |
| General streaming support | Remaining retained-model bounds, unsupported schema/atomic cases, complete static/runtime integration and hosted cross-platform acceptance remain future work |

The Linux experiment uses the same frozen source and unmodified controllers, NativeAOT, actual independent
output checks and full lifetime RSS. Its 20 MiB matrix includes startup failures; those rows are not completed
transforms. Its 2 GiB matrix completes all 48 runs with valid output. Docker Linux ARM64 on macOS is not hosted
Linux x64 CI evidence. Globalization was not disabled and startup RSS was not subtracted.

## CI and publication boundary

Required release CI retains unit/upstream/differential/W3C checks, analysis development baselines, process
controller tests, NativeAOT samples and probe smoke, the JIT-host guard, and the ordinary performance gate.
The exact P7 memory and throughput acceptance checks move to
[`streaming-development.yml`](../../.github/workflows/streaming-development.yml), scheduled daily and manually
dispatchable. Their input sizes, limits, paired rounds and failure behavior are unchanged. Failed targets fail
that workflow; there is no acceptance waiver. This separates the unsupported future feature from this phase's
release scope. Ordinary nightly benchmarks remain in `perf-nightly.yml`.

Version 1.1.0 packages `Xsl3`, `Xsl3.Cli` and `Xsl3.Build` together. Project version placeholders remain unchanged
because the release workflow derives versions from the tag. The 1.1.0 public API inventory is finalized in
`PublicAPI.Shipped.txt`; the unshipped inventory is cleared for subsequent work.

The owner authorized the 1.1.0 release on 2026-10-26. Release tags must point at a commit integrated into `main`,
with the required CI passing. The protected `nuget` environment retains its required reviewer; deployment
approvals remain the owner's to give in GitHub. This authorization does not complete or resume full P7.

Working evidence is under `/tmp/xsl3-phase-closeout-2026-10-26/`. The local review bundle at
`artifacts/releases/1.1.0/` copies the packages, package/source hashes, frozen source, build/consumer logs and
retained conformance/performance/Linux reports. Archive this ignored artifact folder with the release review;
it is not published automatically.
