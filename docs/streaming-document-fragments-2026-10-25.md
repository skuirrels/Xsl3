# Document fragments and source views, 2026-10-25

This follow-up continues the [recursive-function checkpoint](streaming-recursive-functions-2026-10-25.md)
on `feature/p7-streaming-analysis`. The full Phase 7 goal remains active; `supports-streaming` stays `no`.
The later [navigator ID-provenance review](streaming-id-provenance-2026-10-25.md) corrects the
navigator metadata inference described by this historical checkpoint and supplies newer evidence.

## Policy and implementation

XSLT's source-document instruction uses the document retrieval model of `fn:doc`; fragment interpretation
is implementation-defined. Xsl3 now interprets a shorthand fragment as an ID and constructs a document
view whose sole element child is the selected element. The resource URI is retrieved without the fragment.
See [XSLT 3.0 §18.1](https://www.w3.org/TR/xslt-30/#element-source-document) and
[Functions and Operators §14.6.1](https://www.w3.org/TR/xpath-functions-31/#func-doc).

| Entry point | Meaning of a shorthand fragment |
|---|---|
| `fn:doc` | A stable selected document view, cached separately from the complete resource and other fragments |
| `document()` | An element in the stable complete resource, preserving its original ancestors and root identity |
| `xsl:source-document` | A selected document view; forward execution scans to the ID and exposes its subtree |
| Initial URI input | The same selected document-view policy |

An empty fragment aliases the complete resource. A missing ID or unsupported selector syntax raises
`FODC0002` for a document view. `document()` returns an empty selection for a missing shorthand ID and
reports `XTDE1160` for unsupported fragment syntax. XPath-style XPointer expressions are not implemented.
A percent-encoded ID is decoded before matching, using ordinal ID comparison.

IDs come from `xml:id`, DTD declarations, or schema ID metadata supplied by an already-validating reader.
An ordinary attribute named `id` does not acquire an ID property. Provider metadata remains authoritative;
the implementation does not infer missing types or substitute known fixture values. Raw stream selection
uses the parser's DTD information; recognizing a schema ID before selection requires a validating reader
from the configured provider.

`DocumentFragment` shares parsing and selection across the runtime entry points. A forward selection
keeps the open XML Base chain and DTD declarations, scans without retaining the prefix, then returns a
subtree reader. The selected document has its fragment URI; element base URIs retain inherited XML Base,
and in-scope namespaces remain available. Owned inputs close with the view; closing a borrowed reader's
view leaves its input reader open. Prefix scanning and navigator metadata scans observe cancellation.
The navigator path creates its subtree reader only after completing its metadata scan.

The document cache and its concurrent loading table use fragment-sensitive keys because .NET's ordinary
`Uri` equality ignores fragments. Physical read/write conflict tracking still follows the resource URI.
`document()` uses the complete-resource cache and returns original selected nodes. This preserves the
required shared root identity for different fragments of the same resource, independently of the
selected-document views used by `fn:doc`.

DTD ID and IDREF facts survive selected-view loading, copying and grounding without being replaced by
invented schema annotations. The existing DTD bridge exposes ATTLIST declarations for the selector,
including declarations without a separate ELEMENT declaration. That extension is explicit at the
selector call; the normal compact-tree loading contract retains its prior parity. The compact builder
records an ID property's attribute flag as well as its lookup index, so copies retain the property.
Streamed copies carry the selected view's immutable identity metadata into their grounded trees.

## Functional and NativeAOT verification

The solution builds. Release and Debug each pass **3,237 net10.0 / 3,134 net8.0** unit tests, including
**45 net10.0 / 43 net8.0** new fragment cases. The final unchanged-source compatibility driver passes
normal W3C **46**, upstream **1,654** and differential **394** on each framework, plus all **13** streaming
controller tests. The strict audit retains its nonzero status; it is not called green.

The checkpoint copied eight execution reports and found no new failure IDs. A later evidence review
found that its 2.0 and posture/sweep files were copied without fresh runs. The table preserves that
historical snapshot; the [follow-up](streaming-id-provenance-2026-10-25.md) explicitly reruns both and
supplies the current eight-profile comparison:

| Profile | Passed | Failed | Not run | Unmatched expected codes | Newly passing cases |
|---|---:|---:|---:|---:|---|
| XSLT 3.0 | 8849 | 19 | 41 | 34 | `docbook-004`, `id-001` |
| XSLT 2.0 | 4588 | 9 | 11 | 7 | none |
| Unstreamed development | 2693 | 18 | 1 | 5 | `docbook-003` |
| Strict forward inputs | 2690 | 21 | 1 | 7 | `docbook-003` |
| Posture and sweep | 790 | 129 | 0 | 0 | none |
| Schema-aware | 843 | 1 | 2 | 6 | none |
| Schema-aware strict | 842 | 2 | 2 | 6 | none |
| QT3 | 21884 | 0 | 99 | 67 | none |

Focused regressions cover both APIs, stream/reader/navigable providers, parser and schema IDs, distinct
and repeated fragment identities, empty and Unicode fragments, inherited namespaces and XML Base,
ID/IDREF properties through grounding, missing IDs and unsupported syntax, cancellation, borrowed/owned
inputs, and saved normal/debug compilation. The compact-tree parity and shared-resource checks pass.

The saved NativeAOT consumer passes **14** new normal/debug fragment stylesheets plus all retained
consumers, including the legacy materialized-merge and immutable v2 fork assemblies. Its final publish
has no warnings. Four additional normal/debug checks reuse the saved DTD styles with an already-positioned
borrowed reader. At this checkpoint the frozen **406** source/configuration files matched the live source. Final native
logs are `/tmp/xsl3-p7-fragments-positioned-native/publish.log` and `run.log`;
both terminal statuses are **0**. Source hashes and artifacts are retained in that directory.

The final solution, unit, profile and compatibility logs are under
`/tmp/xsl3-p7-fragments-positioned-verified-*`,
with reports and the failure-ID comparison in `/tmp/xsl3-p7-fragments-positioned-reports/`.

The full audit caught a `document-1503` identity regression in the first shared-loader candidate;
`document()` and `fn:doc()` now use distinct selection semantics over the shared retrieval path. A public
API regression asserts element values, shared resource identity and missing-ID behavior. An earlier
compact-tree parity failure prompted the explicit opt-in DTD metadata extension. Native DTD cases use
a reader configured with `DtdProcessing.Parse`; the initial prohibited-DTD run remains preserved as test
configuration evidence.

The review also reproduced six failures with resolvers returning an `XmlReader` already positioned on
its document element. Its DOCTYPE had been consumed, so the selector never initialized its DTD facts.
The selector now reads the parser's existing declarations before inspecting an interactive reader,
sharing the same metadata routine as the ordinary DOCTYPE path and checking cancellation first.
The six cases now pass across both public APIs, tree/forward source-document execution and `fn:doc`,
including ID/IDREF retention after copying and borrowed-reader ownership. The failing before-fix log
is `/tmp/xsl3-p7-fragments-positioned-before.log`; focused after-fix counts are **45/43**.

The ordinary baseline removes only the verified passing `docbook-004` and `id-001` cases. The unstreamed
streaming-profile baseline removes only `docbook-003`, verified independently before the edit. Other
baselines are unchanged. No commit or push is performed. The exact 20 MiB push RSS requirement,
canonical throughput target, remaining semantic cases and remote Linux x64 evidence remain open.

## Ordinary performance gate

The isolated three-pair gate against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` passes (exit **0**).
The head/base timing geometric mean is **0.990513413537** across **39** timed rows; maximum allocation
ratio is **1.037238717165**. Both sides use the same ordinary micro harness. This compares the accumulated
branch with that baseline; it does not isolate this increment. Individual timing regressions remain,
including grouping compilation (**1.085**); aggregate success does not establish a pass for every row.
Cold start is reported separately. Raw rounds **1–3**, `compare.txt` and an exact `summary.json` are
retained in `/tmp/xsl3-p7-fragments-positioned-perf-data/`. The archive contains only these three pairs;
stale rounds from the shared artifact directory are excluded. No other task-owned CPU job ran during
this timing gate. Log: `/tmp/xsl3-p7-fragments-positioned-perf.log`.

## Generated-input native fragment probe

The focused NativeAOT probe reads a generated prefix taking approximately half the requested input,
then selects `#chosen` through the public resolver and source-document path. It writes selected row
values incrementally to a digest sink. All four children consume exactly the requested bytes, validate
against an independently calculated expected digest, emit before EOF, and close the owned input.
The resolver rejects any fragment in a retrieval URI. Normal and debug saved styles are tested.

| Saved style | Exact input bytes | First output after input bytes | Child elapsed ms | Lifetime peak RSS bytes |
|---|---:|---:|---:|---:|
| normal | 209715200 | 105213952 | 1154.0322 | 24985600 |
| normal | 2147483648 | 1074098176 | 11580.3056 | 24576000 |
| debug | 209715200 | 105213952 | 1133.0956 | 27377664 |
| debug | 2147483648 | 1074098176 | 11569.7274 | 26902528 |

RSS is the maximum of controller-observed and child-reported full-lifetime peaks, with no baseline
subtraction. Each child uses the unchanged **209,715,200-byte** heap/RSS guard and ordinary ICU support.
This demonstrates bounded prefix scanning at 200 MiB and 2 GiB; it is a focused probe and does not
satisfy the distinct **20,971,520-byte** push RSS limit or the canonical throughput target. Elapsed
figures are observations from one child per style/size, not comparative throughput evidence.

The probe directory is `/tmp/xsl3-p7-fragments-positioned-scale-native/`; `report.json`, `run.log`,
`run-exit.txt`, `driver-exit.txt`, `provenance.json` and `artifact-hashes.json` retain the results.
All terminal statuses are **0**. Both native consumers use the same frozen core. After execution,
`source-check.json` verifies all **406** live and frozen source/configuration hashes without a mismatch.
The fragment consumer records **90** artifact hashes (**87** saved DLLs plus three native publish
artifacts); the focused scale consumer records **5** (**2** saved DLLs plus three publish artifacts).
The protected legacy v2 fork consumer remains unmodified.

The original accepted fragment checkpoint remains archived under
`/tmp/xsl3-p7-fragments-verified-native/`, `/tmp/xsl3-p7-fragments-verified-scale-native/` and
`/tmp/xsl3-p7-fragments-accepted-perf-data/`. Its ordinary gate passed at **0.983685565594** before the
positioned-reader review fix. Current evidence above comes from fresh native publications and process
runs against the final frozen source. Previously emitted saved DLLs are intentionally reused in the
final native consumer: this review fix changes runtime metadata loading, not stylesheet compilation.
`saved-consumer-provenance.json` records all reused saved-assembly hashes.
