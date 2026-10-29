# P7G copied-attribute identity contracts, 2026-10-29

P7G implements the [six frozen contracts](plan/p7g-attribute-identity-contracts.md). The consumer fix
is a 1.1.5 candidate. All local completion gates pass; the [result snapshot](plan/p7g-results.json)
retains the evidence. [Required PR checks](https://github.com/skuirrels/Xsl3/pull/25/checks) gate merging;
publication remains a separate step. Implementation stops at this boundary.

## Contract and cause

[XSLT 3.0 §4.5](https://www.w3.org/TR/xslt-30/#dtd-validation) makes DTD ID/IDREF/IDREFS declarations
identity properties, independently of schema type annotations. Both preserve and strip keep these
properties when copying; see [§25.4.1.1](https://www.w3.org/TR/xslt-30/#validating-using-validation-attribute) and
[§5.7.1](https://www.w3.org/TR/xslt-30/#constructing-complex-content). Constructing a new parent
implicitly copies supplied attributes. The original P7F D1 expectation was correct, but multiple
metadata losses obscured where the failure began.

- ATTLIST-only declarations were omitted by stock-compatible compact loading. Positioned readers
  could also pass their DOCTYPE before the loader saw its event, then lose access to the parser's
  declarations behind a subtree reader. XDM/XSLT 2.0+ loading now explicitly requests complete identity
  metadata and captures it before scoping. Stock-compatible loading and XSLT 1.0 retain their policy.
- Forward input only carried declarations supplied by selected-fragment views. It now also reads
  ordinary DTD declarations and shares those immutable name tables with copies/grounding.
- IDREF/IDREFS flags were recorded only on attributes with an owner element. Detached attributes now
  retain those flags, just as detached IDs already did, including when annotations are stripped.

No behavior depends on a fixture ID or expected output. Metadata comes from the supplied reader,
existing DTD bridge, schema annotations or existing identity sidecars. No public API, saved format,
canonical benchmark, official fixture, memory limit or globalization setting changes. P7F's ordinary
untyped attribute factory and guard remain unchanged.

## Six-case dispositions

All six are confirmed runtime defects with shared corrections. Each case has two DTD forms,
source/attribute/document controls, normal/debug paths, both public source APIs and initial/positioned
readers. Both explicit document/element construction policies are checked independently of copy policy.

| Copy contract | 1.1.4 .NET 10 contract rows, pass/fail | Candidate pass/fail |
|---|---:|---:|
| ID / preserve | 9 / 46 | 55 / 0 |
| ID / strip | 9 / 46 | 55 / 0 |
| IDREF / preserve | 1 / 54 | 55 / 0 |
| IDREF / strip | 1 / 54 | 55 / 0 |
| IDREFS / preserve | 1 / 54 | 55 / 0 |
| IDREFS / strip | 1 / 54 | 55 / 0 |

The [case inventory](plan/p7g-identity-cases.csv) retains each baseline outcome and sourced oracle.
The complete unchanged 1.1.4 matrix has **308 failures / 22 passes** on .NET 10 and **216 failures /
18 passes** on .NET 8. The candidate passes all **330 / 234** rows in both Release and Debug.
These include source lookup positives and typed-identity regression controls; they are not 330
independent feature contracts. Correctly working fully declared source lookups remain positive controls.

Copies retain names, namespace URIs, normalized values, untyped/type policy, parentlessness and distinct
identity after their source closes. Reattachment retains `id()`/`idref()` lookup, multiple IDREFS token
matching and deduplication. Ordinary CDATA values do not acquire identity merely by matching an ID.
`xml:id` normalization and caller ownership of readers remain checked.

## Validation

- Full Release/Debug units: **4,040 / 3,783** on .NET 10 / .NET 8. Upstream **1,654** applicable
  tests and differential **394/394** pass on both frameworks. The W3C harness passes all **71** tests.
- QT3 remains **21,884/0/99**; ordinary XSLT 2.0 **4,590/8/10**, XSLT 3.0 **8,850/18/41**.
  Existing permissive expected-error accounting remains visible (25, 7 and 34 mismatched-code passes).
- Reviewed S1 **1,000/0/0** and S2 **155/0/0** pass in Release/Debug. Original raw S1 **790/129/0**
  and S2 **153/2/0** remain unchanged. Strict-forward stays **2,695/16/1** with zero unmatched error
  codes; unstreamed stays **2,693/18/1**, including its one unmatched error code.
- **24** fresh normal/debug grounded/forward saved styles pass **192** checks per .NET 8, .NET 10
  and macOS ARM64 NativeAOT consumer, across two DTD forms, two reader positions and two value sets.
  Both construction policies are checked in each result. The runtime decoder verifies the actual
  serialized execution mode. A deliberately missing binding is rejected, preventing partial-matrix passes.
- Native publication retains globalization and treats final trim/AOT/compiler warnings as errors.
  The generator uses the existing experimental switch only in its compiler process; consumers use
  the AOT-safe public `Load(Type, settings)` overload. CI adds the same saved consumer on Linux x64.
- All **194** recorded official-fixture/inventory hashes and **eight** P7F protected fixture/controller
  hashes remain unchanged. Existing baselines and public API inventories are retained.

The fresh allocation regression comparison uses the original saved identity workload, identical
phase instrumentation, three processes per variant/size and alternating order. All **18** final
processes consume full input and pass independent output verification. Transformation counters exclude
warmup and the oracle. The heap guard remains 20,971,520 bytes; diagnostic RSS rejection is disabled.
These are allocation results, not memory-budget acceptance or throughput measurements.

| Exact input bytes | 1.1.4 median bytes allocated | Candidate median bytes allocated | Ratio |
|---:|---:|---:|---:|
| 10,485,760 | 568,261,800 | 568,261,864 | 1.000000113 |
| 209,715,200 | 11,364,765,208 | 11,364,765,608 | 1.000000035 |
| 2,147,483,648 | 116,374,962,760 | 116,374,962,824 | 1.000000001 |

All three pass the frozen maximum **5% growth** criterion. P7F's allocation gain is retained.
The separate unchanged three-round ordinary performance gate also passes: the 39-row time geometric
mean is **0.970** (rounded), maximum allocation ratio **1.001139**, and no local row exceeds the 5%
slowdown tolerance. Individual rows and all three paired rounds remain in the retained comparison.

## Review and deferred work

Review confined complete DTD loading to XDM/XSLT 2.0+ after the unchanged stock-compatibility controls
detected a 1.0 behavior change. It also removed a duplicate declaration-table read and added explicit
saved-mode and missing-binding safeguards. No existing passing assertion was relaxed.

The initial fixture's bare `xsl:sequence select="$second"` triggers a pre-existing Debug compiler
source-line assertion with stylesheet debugging enabled, reproduced on unchanged 1.1.4. The final
fixture uses the equivalent attribute-axis selection on its `attribute()*` variable, keeping implicit
copying, both validation policies and all expected results. The original source and failures remain
[deferred](plan/p7g-deferred.md). Initial harness setup failures (socket sandbox, existing output directory,
an incorrectly streamable source-only control and unsuitable AOT-loading/build settings) are retained
separately and corrected; they are not consumer fixes or successful gate results.

Startup footprint, the 20 MiB target, other performance hotspots, new streaming constructs and broad
schema work remain deferred. Streaming remains unsupported and `supports-streaming` remains `no`.

## Provenance and reproduction

Released baseline: `09af4ab84766dffc058a2c20c58c2d26795db843` (1.1.4). Branch base:
`0fa19cdc3a4e048b7664effe15ba987f57687ad5` (README erratum). Implementation:
`816e6f564d26c2b558a0454816223e55881a9dc7`; [PR #25](https://github.com/skuirrels/Xsl3/pull/25).
The snapshot records runtime hashes, exact report/driver hashes, saved-entrypoint hashes and hosted CI.
Local evidence is under `artifacts/p7g-identity/`, including the independent `git archive v1.1.4`
baseline with only the new contract tests added. Original failing sources/logs and both initial/final
allocation cohorts remain retained. Ignored evidence is not automatically published.

```sh
dotnet test tests/Xsl3.Tests -c Release --filter FullyQualifiedName~CopiedAttributeIdentityTests
dotnet test tests/Xsl3.Tests -c Debug --filter FullyQualifiedName~CopiedAttributeIdentityTests
bash eng/verify-attribute-identity.sh osx-arm64 artifacts/new-identity-verification
bash eng/verify-attribute-identity.sh linux-x64 artifacts/new-linux-identity-verification
bash eng/perf-gate.sh v1.1.4 3
```

Allocation reproduction uses `eng/diagnose-streaming-memory.py prepare` on separate baseline/candidate
snapshots, the same original saved entrypoints, then `measure --instrumented` with three rounds at each
original size, the 20 MiB heap guard and fresh output directories. Compare the medians of transformed
minus collected cumulative allocation against the 1.05 ratio bound. The exact alternating driver and
all raw point arrays are retained and hashed in the snapshot.
