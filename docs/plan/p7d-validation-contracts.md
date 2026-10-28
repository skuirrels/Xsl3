# P7D: schema-validation contracts

**Scope frozen on 2026-10-27. Branch: `feature/p7d-validation-contracts`.**
Base: release `v1.1.2` (`e98cc9fb9b49cb456b197253b4537382c2fb86ea`).
Implementation scope is closed. All local gates below have evidence; [PR #20's hosted checks](https://github.com/skuirrels/Xsl3/pull/20/checks)
(Linux, Windows, NativeAOT and performance) must also be green before merge or release.
The patch candidate is 1.1.3; publication remains separate. P7C remains closed.

## Fixed scope

Resolve these six entries from the unchanged [P7C deferred inventory](p7c-runtime-deferred.csv):

| Family | Cases |
|---|---|
| Lax source validation | `source-document/stream-013`, `source-document/non-stream-013` |
| Source schema availability | `streamable/streamable-021`, `streamable/streamable-042`, `streamable/streamable-043` |
| Lax result validation | `si-result-document/si-result-document-024` |

The acceptance contract is XSLT 3.0 schema-aware processing and validation (§2.11,
§18.1, §25.4, §27.1). Distinguish schema components imported by the stylesheet from type
annotations supplied by an already validating reader, and distinguish schema-aware
and non-schema-aware evaluation. A retained raw failure is not automatically an engine defect.

## Completion criteria

1. Reproduce all six cases against 1.1.2 and preserve a full strict-forward baseline,
   original fixtures, raw reports and the original deferred inventory. Give every
   selected case a conclusive runtime, harness or fixture disposition with specification
   references. Fix confirmed defects; do not change product behavior to force fixture results.
2. Add public-path regressions for the affected contracts: schema-aware settings,
   strict/lax validation, schema availability, supplied type annotations, exact error
   codes and reader ownership. Cover both public APIs and affected normal/debug saved
   stylesheets on .NET 8, .NET 10 and NativeAOT.
3. Pass managed Release/Debug, upstream, byte-for-byte differential, ordinary conformance,
   strict-forward coverage and P7B raw/reviewed gates. Preserve the 2,712-case strict
   population, its one existing collection skip and zero unmatched expected-error codes.
   Retain the ordinary performance gate and obtain Linux, Windows and NativeAOT CI evidence.
4. Review the bounded diff and update README, changelog, conformance documentation and
   this close-out with exact evidence and dispositions. If consumer-facing fixes result,
   prepare 1.1.3; publishing matching packages on NuGet and GitHub remains a separate,
   owner-authorized release step with public-feed consumer verification.

## Stopping rule and exclusions

These six cases and necessary regression coverage are the entire implementation scope.
New unrelated findings are recorded separately and deferred. Once the criteria have
evidence, stop implementation and close P7D; do not continue into full P7.

No target of reducing raw strict failures from 16 to 10 is imposed. Proven fixture
conflicts remain visible. If the investigation finds only fixture issues, close the
phase as an audit rather than manufacturing a runtime patch release.

New streaming constructs, general retained-model bounds, 20 MiB memory and throughput
acceptance, XSD 1.1, new public APIs and saved-format changes are excluded. Normal
execution remains tree-based and `supports-streaming` remains `no`.

## Six-case disposition and fixes

The [case inventory](p7d-validation-cases.csv) gives every selected case a conclusive disposition.
The original six failures remain in the unchanged 16-case raw strict-forward report:

- `stream-013`, `non-stream-013` and `si-result-document-024` expect basic, untyped results.
  The regular harness is schema-aware, where lax validation without declarations produces `xs:anyType`
  (§25.4.1.1). Those expectations conflict with the profile. Running these same unedited cases in basic
  mode exposes a real defect: 1.1.2 rejects lax validation with `XTSE1660`, although §27.1 requires it
  to behave as strip. All three original fixtures pass in basic mode after the correction.
- `streamable-021`, `-042` and `-043` request strict validation but do not import the declarations used by
  their source provider. In Xsl3, `XsltSettings.Schemas` is the documented import catalog, not an implicit
  import. `XTTE1512` is correct for this configuration. Positive controls import from that catalog and
  validate successfully; separate controls preserve supplied annotations without needing an import.

Basic lax/preserve normalization now happens in the shared attribute parser. Grounded source documents
also apply explicit stripping through the existing validation helper and node-copy implementation;
previously that path retained a validating reader's types. The forward path retains its existing strip
policy and borrowed-reader ownership. Strict validation in basic mode remains `XTSE1660`.

No runtime policy depends on a fixture ID or expected output. No public API, helper signature,
saved-format, official fixture, old result snapshot, existing baseline or protected benchmark changes.
The six core files contain only the shared contract correction, reader/copy support and comments.

## Code review corrections

Review reproduced a grounded/forward mismatch: with a validating simple-content reader,
`validation="strip"` and `xsl:strip-space`, grounded execution retained one whitespace-only text node
while forward execution removed it. Four of eight API/debug/path combinations failed per framework
on the pre-review head `5e4a56e12ae75e546b8dd8fe1e317986438f2642`.

Grounded stripping now reapplies the existing whitespace rules to the untyped copy without changing
the cached source. It reuses the selected-reader namespace and ID/IDREF sidecars and preserves document
URI metadata. Debug checks also exposed an uninitialized whitespace stack when a provider was already
positioned on an element. The 2.0+ grounded loader now uses the existing P7C scoped-reader view before
filtering; the legacy 1.0 path is unchanged. `xml:space="preserve"` remains effective.

Twelve review regression cases extend the two-API normal/debug matrix and metadata controls. Eighteen
additional normal/debug saved styles cover strip/basic lax, preserve, xml:space and identity metadata:
36 checks per .NET 8, .NET 10 and NativeAOT consumer across two whitespace inputs. The original P7D
and P7C saved styles remain unchanged and pass against the reviewed runtime. Review evidence and initial
failed runs are retained separately in `artifacts/p7d-review/`.

The review's unchanged three-round ordinary gate also passes: timing GM **1.002463** over 39 rows,
maximum allocation ratio **1.002233** against 1.1.2. The entry measurement below retains its own source
and paired rounds. All full functional, profile and native gates were rerun after the corrections.

## Exit evidence

| Gate | Local result |
|---|---|
| Fresh independent 1.1.2 baseline | Full strict-forward 2,695/16/1, zero unmatched codes; exact 16 failure IDs retained |
| Original P7D contract matrix on 1.1.2 | 20 failures / 32 passes on .NET 10; 16 failures / 32 passes on .NET 8; original evidence retained |
| Original three lax fixtures, basic mode | Fail with XTSE1660 on 1.1.2; all three pass after correction |
| Full Release and Debug units after review | 3,698 .NET 10 / 3,541 .NET 8 in each configuration; 64 / 60 new cases |
| Upstream / byte-for-byte differential | 1,654 / 394 per framework |
| Full W3C harness, saved round trips enabled | 71 passed |
| Ordinary XSLT 3.0 / QT3 | 8,850/18/41 and 21,884/0/99, existing failure sets retained |
| Full strict-forward / unstreamed development | 2,695/16/1 with zero unmatched codes; 2,693/18/1 with the existing one unmatched code |
| Original raw S1 / S2 | 790/129/0 and 153/2/0, unchanged |
| Reviewed S1 / S2, Release and Debug | 1,000/0/0 and 155/0/0 |
| Fresh saved normal/debug grounded/forward styles | 28 DLLs, 56 checks per .NET 8 / .NET 10 / macOS ARM64 NativeAOT consumer across two inputs |
| Retained P7C saved styles | 16 unchanged DLLs, 52 checks per managed/native consumer |
| Review saved whitespace/metadata controls | 18 additional DLLs, 36 checks per managed/native consumer |
| Entry three-round ordinary gate vs v1.1.2 | Pass; timing GM 1.008822 over 39 rows, maximum allocation ratio 1.001578 |
| Review three-round ordinary gate vs v1.1.2 | Pass; timing GM 1.002463 over 39 rows, maximum allocation ratio 1.002233 |

Native publication retains globalization and treats AOT, trimming and publish warnings as errors.
The independent baseline is a `git archive v1.1.2` snapshot with only the final regression test file added;
baseline runtime code is unchanged. The released W3C runner is also used with that rebuilt baseline
core for the original-fixture before/after comparison. All 194 recorded official fixture/schema/catalog
and original-inventory hashes remain unchanged. The complete strict population and single collection
skip remain guarded by the existing P7C coverage checks.

Evidence is retained in `artifacts/p7d-validation/`: independent baseline source and binaries, exact
red/green logs, original-fixture outcomes in both configurations, full functional/profile checks,
fresh saved consumers, retained P7C consumers, native publications, and the six paired performance rounds.
The [result snapshot](p7d-results.json) records the source hashes and gates. Hosted checks are tracked on
[PR #20](https://github.com/skuirrels/Xsl3/pull/20/checks); they do not replace the local saved-style and original-fixture evidence.

## Deferred findings

The ordinary gate is an aggregate pass, not a per-row win. Keys compilation has a measured median
head/base ratio of **1.511849** in the entry run. The review run retains ratios of **1.171825** for sorting
compilation, **1.130688** for dispatch compilation and **1.090923** for keys compilation. All paired
rounds are retained; causal performance
investigation is deferred rather than expanding this correctness phase. No timing threshold is relaxed.
The remaining P7C inventory, source-schema API policy changes, general retained-model bounds, original
memory/throughput acceptance and streaming feature claim stay outside P7D.

## Reproduce

```sh
dotnet build Xsl3.slnx -c Release -m:1
dotnet test tests/Xsl3.Tests -c Release --no-build
dotnet test tests/Xsl3.Tests -c Debug -m:1
dotnet test tests/Xsl3.Upstream.Tests -c Release --no-build --filter 'category!=failing'
dotnet test tests/Xsl3.Differential -c Release --no-build
XSL3_W3C_SAVED=10 dotnet test tests/Xsl3.W3C -c Release --no-build
XSL3_STREAM_STRICT=1 XSL3_XSLT30_PROFILE=strm dotnet test tests/Xsl3.W3C -c Release --no-build --filter FullyQualifiedName~Xslt30Conformance
eng/perf-gate.sh v1.1.2 3
```

Use the unchanged [P7B reproduction commands](p7b-analysis-closeout.md#reproduce-the-baseline) for raw
and reviewed gates. The retained phase producer emits saved styles from the actual contract fixtures;
its consumers load compiled types through the public API and do not compile stylesheets under NativeAOT.

Implementation stops here. Merge and release require green hosted checks; publishing 1.1.3 to NuGet
and GitHub requires separate owner authorization and public-feed consumer verification.
