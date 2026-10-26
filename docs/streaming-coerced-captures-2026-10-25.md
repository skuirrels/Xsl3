# Deferred coerced functions and declared value conversion, 2026-10-25

The [library function follow-up](streaming-library-captures-2026-10-25.md) extends this checkpoint;
its measurements below describe the preceding frozen core.

This extends the [compiled closure checkpoint](streaming-closure-captures-2026-10-25.md) on
`feature/p7-streaming-analysis`. Coercion wrappers now spill their target graphs, and function
conversion applies consistently through the shared converter. Full Phase 7 remains active;
`supports-streaming` remains `no`.

## Storage and semantic corrections

`StreamedXdmValueStore` records a coercion wrapper's parameter/result types separately from its target.
Metadata vectors compare the actual compiled `SequenceTypeTest` objects, so fresh per-wrapper
parameter arrays do not become a retained history. The target is serialized through the existing
map/array/compiled-function graph. Wrapper IDs are reserved before their targets are read; immutable
wrappers are published when their targets resolve. This preserves aliases and dependency cycles
through outer wrappers, containers and compiled captures without introducing mutable runtime
function objects. Reading and writing 20,000-level wrapper chains remain iterative.

The shared `Coercion.CoerceTo` path previously accepted coercible function items without installing
the wrapper. Only the variable conversion helper installed one, so typed accumulator values and
nested callbacks could miss argument/result checks. Wrapper creation now has one implementation,
including map and array targets. Coerced functions preserve their original QName; name and description
access walk wrapper chains iteratively. These behaviors follow the
[XPath function conversion rules](https://www.w3.org/TR/xpath-31/#id-function-conversion-rules) and
[function coercion rules](https://www.w3.org/TR/xpath-31/#id-function-coercion).

Accumulator initial/rule values use those conversion rules for their declared `as` type, as required
by [XSLT accumulator declarations](https://www.w3.org/TR/xslt-30/#accumulators). Immediate scalar,
cardinality and function-arity mismatches now report `XPTY0004`, replacing the erroneous
`XTTE3400` literal. Converted function argument/result failures remain delayed until invocation.
Recording snapshots does not call function bodies or demand an unused accumulator.

The preserved preceding core reproduces two missing-conversion failures and five wrong-code failures
on each framework, including tree execution. Separate regressions reproduce retained coercion
wrappers and capture graphs. Evidence is under `/tmp/xsl3-p7-coerced-captures-before/`. Initial isolated
fixture compilation errors and superseded native builds are retained in the candidate directory;
they are not final acceptance results.

The core public API, generated helper signatures, saved-constant format and value-replay layout are
unchanged. Actual code owners and compiled type models remain authoritative. No runtime domain
values, fallback results, suite expectations or memory thresholds are substituted.

## Functional and native evidence

Focused checkout tests pass **103 net10.0 / 95 net8.0**. Full Release and Debug each pass
**3,341 net10.0 / 3,229 net8.0**. The solution builds; ordinary W3C checks pass **46**, upstream
**1,654 per framework**, differential **394 per framework**, and controller checks **13**.
The existing xUnit collection-size warning is corrected using `Assert.Empty`.

Tests cover collection of 1,000 wrappers, targets, captures and fresh parameter arrays while the
scratch store remains alive; recursive and shared targets; 20,000-level chains; actual typed
accumulators; maps/arrays as functions; numeric promotion; original function names; argument/result
errors; and wrapper replay before real fork projection. Compiled/debug/saved invocation retains each
runtime owner's globals and enforces nested callback conversions.

All nine final conformance failure-ID sets match the preceding checkpoint. Strict execution remains
**2,695 passed / 16 failed / 1 not run**, with its failing process status retained, but unmatched expected
error codes improve **7 to 6**. Unstreamed execution remains **2,693 / 18 / 1**, with unmatched codes
improving **5 to 4**. Posture/sweep remains **790 passed / 129 failed**, so its original gate remains open.
The remaining profile counts are unchanged; no failure baselines are edited.

An initial 2.0 run additionally failed `unparsed-text-2003` when the live W3C mission URL was reported
unavailable. Its report/log are preserved under `/tmp/xsl3-p7-coerced-captures-network-failure/`.
Independent preceding/current-core public-API probes then both read **30,636** characters from the
same URL; a fresh full 2.0 run restores **4,590 passed / 8 failed / 10 not run**. The first HTTP response
was not captured, so no particular remote-server cause is asserted. No source or baseline changes
were made for this retry. Final comparisons and reports are under
`/tmp/xsl3-p7-coerced-captures-final-reports/`; generated conformance tables are refreshed from them.

The final NativeAOT consumer passes sixteen new saved normal/debug styles and all **117** retained
saved DLLs. It checks typed function/map/array values, names, promotions, argument/result/scalar
errors and graph reconstruction. Both native publications have no warnings. The main artifact
manifest contains **136** files (**133** saved DLLs plus three publish files); the scale manifest
contains **5** files. After all runs, all **413** live/frozen source/configuration hashes,
**117** retained saved-DLL hashes, protected v2 fork and artifact hashes match. Evidence roots are
`/tmp/xsl3-p7-coerced-captures-final-native/` and
`/tmp/xsl3-p7-coerced-captures-final-scale-native/`.

## Ordinary timing investigation and accepted gate

The first original three-pair gate fails with time geometric mean **1.525545851788**, while allocation
ratios remain largely unchanged. Raw measurements show a timing shift in the unchanged base as
well: its 1 MiB fold medians are **2.9952 / 2.8457 / 4.4977 ms**, versus head
**2.8909 / 4.11745 / 4.1098 ms**. All six files, comparison, failed status and log are preserved under
`/tmp/xsl3-p7-coerced-captures-final-perf-data-first-failed/`. No specific thermal/scheduling cause is
confirmed by the available system diagnostics.

An attribution run uses identical benchmark binaries and corpus, swapping only the accepted preceding
and new frozen cores in reversed order. Its **39** timed rows give a diagnostic new/preceding GM
**1.017690794821**; 1 MiB fold is **0.980814387389**, identity **0.996400449944** and regex
**0.979914331550**. The large penalty is not reproduced. These shorter measurements are diagnostic,
not a replacement acceptance gate; raw data and hashes are in
`/tmp/xsl3-p7-coerced-perf-attribution/`.

With source, harness and thresholds unchanged, a fresh original three-pair gate against
`0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` passes over **39** timed rows, cold start excluded:
time GM **1.035572890616**, maximum allocation ratio **1.037242608875**.
Individual timing outliers remain recorded:

| Workload | Size | Head/base time ratio |
|---|---|---:|
| dispatch | compile | 1.179842238600 |
| grouping | compile | 1.106432931601 |
| join | compile | 1.059000000000 |
| keys | compile | 1.732230478060 |
| regex | 1kb | 1.170212765957 |
| sorting | compile | 1.371158392435 |

All verification jobs and the checkpoint's two native publications finish before ordinary timing;
the large native children start afterward. Raw accepted rounds/comparison/summary are under
`/tmp/xsl3-p7-coerced-captures-final-perf-data/`. No hot-method refactor or threshold relaxation was
used to obtain the passing retry.

## Actual typed-wrapper memory matrix

The accumulator is declared `as="function() as xs:integer"`. Its start rule creates an inline
function capturing a map containing an array of the actual numeric source ID:

```xml
<xsl:accumulator name="n" as="function() as xs:integer"
    initial-value="function(){0}" streamable="yes">
  <xsl:accumulator-rule match="row"
    select="let $captured := map{'value': [xs:integer(@id)]}
            return function(){$captured?value?1}"/>
</xsl:accumulator>
```

The inline function has no declared result type, so its generated signature is `item()*`; the
accumulator's declared function type requires the engine-created coercion wrapper. A streamable
source-document evaluates
`doc/row/value/ancestor::row except doc/row[number(@id) mod 2 = 0]/text/ancestor::row` and emits the
20-digit ID plus `accumulator-before('n')()`. The XML generator and independent expected-output
algorithm remain byte-for-byte unchanged from the preceding accepted harness.

All four exact-size children finish with status **0**, consume the complete source, verify the
independent output digest, emit before EOF, close the owned input and preserve the borrowed reader:

| Saved style | Exact input bytes | First output after input bytes | Child elapsed ms | Lifetime peak RSS bytes |
|---|---:|---:|---:|---:|
| normal | 209715200 | 57344 | 100285.4952 | 30212096 |
| normal | 2147483648 | 57344 | 833816.1503 | 31244288 |
| debug | 209715200 | 57344 | 79098.0946 | 31834112 |
| debug | 2147483648 | 57344 | 814021.1125 | 31817728 |

Lifetime RSS is the maximum of controller-observed and child-reported peaks, without subtraction.
The **209,715,200-byte** heap/RSS guard and ordinary ICU support remain unchanged. Isolated follow-on
prototype work overlaps part of this matrix, so child elapsed times are diagnostic rather than
comparative throughput measurements. The separate 10 MiB diagnostic also passes: first output
**57,344** bytes, peak **31,424,512** bytes and **771,912** live managed bytes after collection.

## Remaining requirements

The bound assumes fixed actual compiled type/code/owner sets. Fresh evaluated code units or runtime
owners can grow metadata, and a growing captured value needs space when demanded. `LibraryFunction`,
native/custom callables and nodes still use actual identity handles; a coercion target containing
one can retain its reachable graph. General bounded storage for those histories remains open.
These results do not meet the separate **20,971,520-byte** push RSS criterion or prove canonical
throughput. The remaining semantic/analysis, exact-code and current-source Linux x64 gates stay open.
No full S stage or Phase 7 completion is claimed.
