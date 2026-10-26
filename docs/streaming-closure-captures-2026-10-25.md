# Deferred compiled closure captures, 2026-10-25

The [coerced function follow-up](streaming-coerced-captures-2026-10-25.md) extends this checkpoint;
its measurements below describe the preceding frozen core.

This continues the [accumulator phase checkpoint](streaming-accumulator-phases-2026-10-25.md) on
`feature/p7-streaming-analysis`. Compiled function captures now spill with the deferred header's XDM
value graph. Full Phase 7 remains active and `supports-streaming` remains `no`.

## Representation and limits

`StreamedXdmValueStore` records a compiled function's actual runtime owner, generated function ID and
compiled signature separately from its captured sequences. Code descriptors are interned; captures
are written to the existing source-owned scratch stream. Replaying constructs the closure before
reading its captures, so aliases and cycles crossing functions, maps and arrays survive. Traversal
is iterative, including 20,000 nested closures. Empty captures, actual type objects, names, argument
conversion, body errors and runtime dispatch retain their existing semantics. The saved-constant
format, public API and generated helper signatures do not change.

The descriptor table does not retain recorded closure objects or their scalar/container captures.
A regression writes 1,000 distinct closures and captured maps, reads them in reverse order, and proves
that all originals can be collected while the store remains alive. Code owners remain available
until the store clears; callers retaining a reconstructed closure keep its owner normally. The same
regressions against the preceding frozen core fail on both frameworks: it retains all 1,000 closures
and their maps, and returns the original object graph.

Descriptor storage is bounded for a fixed set of actual compiled code and runtime owners. Owners are
not merged merely because their generated function IDs agree: two runtimes sharing exactly the same
compiled code and signature still invoke their own global parameters. Fresh evaluated code units or
fresh runtime owners can grow this table. A semantically growing captured value also requires space
for that value when demanded. These limits remain explicit.

Nodes, `LibraryFunction`, `CoercedFunction`, native delegates and other opaque callables still retain
actual identity handles. A compiled capture containing one of these values can retain its reachable
graph. General bounded spill of such histories remains open. The fixed-owner compiled-closure matrix
below does not close that requirement, the separate 20 MiB push RSS criterion, canonical throughput
or the remaining semantic/analysis, exact error-code and Linux x64 gates.

## Verification

Focused Release tests pass **72 net10.0 / 64 net8.0**. Full Release and Debug each pass
**3,324 net10.0 / 3,212 net8.0**. The solution builds; ordinary W3C checks pass **46**, upstream
**1,654 per framework**, differential **394 per framework**, and the controller suite **13**.
The existing `Xslt30ApiTests.cs:47` xUnit2013 warning is unchanged.

Coverage includes cyclic and shared capture graphs, deep nesting, collection and code-owner cleanup,
typed map/array captures, partial applications, dynamic argument/arity/body errors, normal/debug and
saved execution, cross-runtime globals, and projection of reconstructed closures onto each fork's
reader. Coercion wrappers in the fork invocation test are applied after decoding; that test does not
claim scratch serialization of the wrapper itself.

All nine fresh conformance report headings and failure-ID sets match the preceding accumulator phase
checkpoint. Strict execution still reports **2,695 passed / 16 failed / 1 not run**, with **7** expected
errors lacking the matching code and its original failing process status. Posture/sweep remains
**790 passed / 129 failed**. No suite files, failure baselines, thresholds or feature claims change.
Complete profile comparisons are in `/tmp/xsl3-p7-closure-captures-final-reports/profile-comparison.json`.

The final NativeAOT consumer passes six new saved normal/debug styles (typed captures, partial
applications and same-code/different-owner calls), plus all **111** saved DLLs retained from the
preceding checkpoint. It also checks conversion errors and recursive function aliases directly.
Both native publications have no warnings. The main artifact manifest contains **120** files
(**117** saved DLLs plus three native publish files); the scale manifest contains **5** files.
All **413** frozen source/configuration files, **111** retained saved-DLL hashes and the separately
protected v2 fork consumer match. Raw evidence is under
`/tmp/xsl3-p7-closure-captures-final-native/` and
`/tmp/xsl3-p7-closure-captures-final-scale-native/`.

The preserved initial comparison fixture lacked a QIL namespace import; its corrected run reproduces
the retention defect. Initial scale generation lost an `xs` namespace declaration during fixture XML
rewriting, and the first native fixture used a trimming-unsafe `Load(Type)` overload. Both fixture
errors were corrected before acceptance; their original logs remain available. The final native
fixture uses the existing trimming-safe `Load(Type, settings)` overload. No warning suppression or
substitute fingerprints were introduced.

## Ordinary performance

The isolated original three-pair gate against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` passes
with **39** timed rows (cold start excluded), time geometric mean **0.986027868023** and
maximum allocation ratio **1.037238403996**. Individual time outliers above 5% remain
recorded; no threshold changes or advisory override are used.

| Workload | Size | Head/base time ratio |
|---|---|---:|
| dispatch | 1mb | 1.085278467542 |
| identity | 1mb | 1.054677702533 |
| maps | 1mb | 1.133224554595 |
| regex | 1kb | 1.160000000000 |

All regression jobs and native publications finished before the ordinary timing gate; large native
children started afterward. Raw three-pair data, comparison and summary are in
`/tmp/xsl3-p7-closure-captures-final-perf-data/`.


## Generated-input closure memory matrix

The XML generator and independent expected-output algorithm are byte-for-byte unchanged from the
preceding accepted harness. Each source row has a 20-digit ID. Its start rule creates a compiled
closure capturing a map containing an array of the actual numeric source ID:

```xml
<xsl:accumulator name="n" initial-value="function(){0}" streamable="yes">
  <xsl:accumulator-rule match="row"
    select="let $captured := map{'value': [xs:integer(@id)]}
            return function(){$captured?value?1}"/>
</xsl:accumulator>
```

A streamable `xsl:source-document` selects
`doc/row/value/ancestor::row except doc/row[number(@id) mod 2 = 0]/text/ancestor::row`.
Each emitted row writes its source ID, a colon and `accumulator-before('n')()`. The expected digest
is computed independently from odd record indexes. Each child must consume the exact full source,
verify the digest, emit before EOF, close its owned source and leave the borrowed initial reader open.

All four terminal statuses are **0**.

| Saved style | Exact input bytes | First output after input bytes | Child elapsed ms | Lifetime peak RSS bytes |
|---|---:|---:|---:|---:|
| normal | 209715200 | 57344 | 78027.8435 | 31293440 |
| normal | 2147483648 | 57344 | 1087954.5666 | 31260672 |
| debug | 209715200 | 57344 | 118210.4763 | 31637504 |
| debug | 2147483648 | 57344 | 1194962.5532 | 31653888 |


Lifetime RSS is the maximum of controller-observed and child-reported peaks, without subtraction.
The **209,715,200-byte** heap/RSS guard, ordinary ICU support and exact input sizes remain unchanged.
Compilation of an isolated follow-on candidate overlaps part of this matrix. Child elapsed times are
diagnostic observations, not comparative throughput measurements. These are
actual deferred-set and fixed-owner compiled-closure runs; the limits above remain open.

The smaller 10 MiB native diagnostic also passes: first output at **57,344** input bytes, peak RSS
**31,211,520** bytes and **770,880** live managed bytes after collection. It is separate from the exact
large-input matrix. Raw outputs, measurements, source/artifact manifests and final hash checks are
kept with the local evidence roots above.
