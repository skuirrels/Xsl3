# Deferred library function values, 2026-10-25

The [random-generator follow-up](streaming-random-captures-2026-10-25.md) extends this checkpoint;
its measurements below describe the preceding frozen core.

This extends the [coerced function checkpoint](streaming-coerced-captures-2026-10-25.md) on
`feature/p7-streaming-analysis`. Library-function values now spill through the source-owned XDM
scratch codec. Full Phase 7 remains active and `supports-streaming` remains `no`.

## Representation and lifetime

The codec records the actual `FunctionDefinition` and `FunctionContext` in separate reference-identity
metadata tables. A value contains those two metadata indexes plus its actual arity. Variadic arity is
recorded per value, so changing caller-supplied arities do not create metadata history. Replay builds
a new `LibraryFunction` using the original model objects; aliases within a graph remain shared.
Definitions are not replaced by their numeric registry ID or QName: separate provider definitions
with identical IDs and names retain their distinct implementations.

Retaining the actual context preserves static base URI, default collation, decimal formats, implicit
timezone, date/time state, comparison delegate and owning runtime. Serialization neither computes
current time nor recreates an approximation of the context. The existing runtime still rebinds the
context when a decoded callable is projected into a compatible fork worker. The normal lookup path
uses canonical registry definitions and `X31ContextFor(sc)` caches contexts by static-context ID.

A collection regression writes 1,000 fresh function objects with 1,000 distinct variadic arities,
reads them in reverse order and proves the originals can be collected while the store remains
alive. Context/owner models stay available until the store clears, then become collectible. The
preceding accepted core retains all recorded function objects; its two regression failures on each
framework are preserved under `/tmp/xsl3-p7-library-captures-before/`.

The bound assumes a fixed set of actual definitions and contexts. Fresh evaluated runtimes, fresh
contexts, or provider models retaining arbitrary state can still grow memory. Native/custom callable
values and nodes continue to retain identity handles; general spill for those histories remains open.
No public API, generated helper signature, saved-constant format or value-replay layout changes.

## Functional and native evidence

Focused tests pass **111 net10.0 / 103 net8.0**. Full Release and Debug each pass
**3,349 net10.0 / 3,237 net8.0**. The solution builds; ordinary W3C checks pass **46**, upstream
**1,654 per framework**, differential **394 per framework**, and controller checks **13**.
All nine fresh conformance report headings and failure-ID sets match the preceding accepted core.
Strict remains **2,695 / 16 / 1** with **6** unmatched expected error codes; unstreamed remains
**2,693 / 18 / 1** with **4**. Posture/sweep remains **790 / 129**, and the original gates stay open.
No suite files, baselines or thresholds are changed.

Real computed `function-lookup()` accumulators are tested through both public API paths, tree and
forward execution, debug and saved compilation. The lookup name is a required global parameter
supplied by the caller; it cannot become a compile-time literal function reference. Tests cover
variadic `concat`, a creation-site collation differing from the caller's, lexical static base URI and
stable current date/time. Fork tests decode the function before projecting its context into a worker.

The final NativeAOT consumer passes eight new saved normal/debug lookup styles and all **133**
retained saved DLLs, plus direct library/coerced graph and variadic-arity invocation checks. Both
native publications have no warnings. The main artifact manifest has **144** files (**141** saved
DLLs plus three publish files); the scale manifest has **5** files. Frozen source/configuration has
**413** files. Local evidence is under `/tmp/xsl3-p7-library-captures-final-native/` and
`/tmp/xsl3-p7-library-captures-final-scale-native/`.

Initial isolated fixture builds omitted the generator's friend assembly name and used a C# 14
extension property in an out-of-tree C# 12 test project. Those fixture errors were corrected and the
original logs preserved. The final fixtures use the established friend assembly and actual runtime
comparison provider, without warning suppressions or fake static data.

## Ordinary performance

The original three-pair gate against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` passes on its first attempt.
Across **39** timed rows (cold rows excluded), the time geometric mean is
**0.994364503290** and maximum allocation ratio **1.037238717165**.
Individual timing ratios above five percent are retained: dispatch/1kb **1.055555555556** and
regex/1kb **1.148936170213**. All six raw files, comparison, exit status and logs are preserved under
`/tmp/xsl3-p7-library-captures-final-perf-data/`. No harness, source or acceptance threshold changes
were made during the trial. Regression and native-build jobs finished before these timings.


## Actual computed-lookup memory matrix

The stylesheet declares a required global string parameter, `lookup`, which the public caller supplies
as `concat`. Initial and row-rule values call
`function-lookup(QName('http://www.w3.org/2005/xpath-functions', $lookup), 3)`. The accumulator is typed
`function(*)`; each rule produces the actual library-function value through the runtime lookup path.

A streamable source-document evaluates
`doc/row/value/ancestor::row except doc/row[number(@id) mod 2 = 0]/text/ancestor::row`.
Each selected row invokes its recorded function with the 20-digit source ID, a colon and the numeric
source ID. The canonical XML generator and independent expected-output algorithm remain unchanged;
no precomputed desired fingerprint replaces the output calculation.

| Debug | Exact input bytes | Child elapsed ms | Lifetime peak RSS bytes | Live managed bytes |
|---|---:|---:|---:|---:|
| no | 209,715,200 | 68624.3145 | 31,866,880 | 868,728 |
| no | 2,147,483,648 | 702570.0960 | 31,834,112 | 860,528 |
| yes | 209,715,200 | 67434.0645 | 31,752,192 | 874,232 |
| yes | 2,147,483,648 | 734656.6597 | 31,801,344 | 866,032 |

All four exact-size native children pass with full source consumption, independently verified output,
first output at **57,344** input bytes and owned-source cleanup. Raw child results, commands,
controller measurements and statuses are preserved in the scale evidence root. Isolated follow-on
prototype work overlaps part of the memory matrix; its elapsed times remain diagnostic.


The **209,715,200-byte** heap/RSS guard and ordinary ICU support remain unchanged. RSS is the maximum
of full-lifetime controller and child measurements, without subtraction. Child elapsed times are
diagnostic observations, not canonical throughput acceptance. The separate 10 MiB native diagnostic
passes with first output at **57,344** input bytes, peak **31,817,728** bytes and **876,944** live managed
bytes after collection. Owned inputs close and borrowed initial readers remain open.

After all runs, all **413** live/frozen source hashes, both artifact manifests (**144** and **5** files),
all **133** retained saved DLLs, consumer-source hashes and the protected v2 fork match. The canonical
XML generator is unchanged.
General opaque/native/node histories, exact **20,971,520-byte** push RSS, canonical throughput,
remaining semantic/analysis and exact-code gates, and current-source Linux x64 evidence remain open.
This increment does not complete an S stage or the full Phase 7 plan.
