# XML-to-JSON header-path continuation, 2026-10-26

This extends the [optional-focus checkpoint](streaming-optional-focus-2026-10-26.md) on
`feature/p7-streaming-analysis`. Full Phase 7 remains active and `supports-streaming` remains `no`.

## Reproduced problem and implementation

A filtered descendant followed by a child step, for example
`xml-to-json(//*[@key='selected']/*)`, could revisit a parent iterator after conversion absorbed its
first selected child's subtree. Both valid single-result input and actual multiple-result input
raised `stream-position`. Four isolated regressions reproduced this on both frameworks; the
preceding 49 net10.0 / 48 net8.0 JSON tests still passed.

The compiler now uses a shared header-path parser and prefix-match builder for eligible paths.
A lifted predicate evaluates actual motionless header facts once per visited node, including empty
prefixes and headers read while conversion absorbs a selected subtree. An invocation-scoped
observation records additional selected headers as cardinality facts. It does not synthesize nodes,
rename a navigation failure, retain consumed subtrees or implicitly ground the input.

The input contract is an optional node and the result is an optional string, as specified by
[F&O 3.1, xml-to-json](https://www.w3.org/TR/xpath-functions-31/#func-xml-to-json).
Valid single selections now finish; actual multiple selections report XPTY0004 before any result
escapes. Existing JSON validation and argument-error precedence remain. A predicate fault retains
its identity even when it uses a FOJS code that could otherwise be mistaken for a pending body fault.
An already-consumed nonempty source remains a `stream-position` error unless actual replay exists;
attribute headers and explicit replay retain their legitimate empty selections.

The observation rejects recursive input reads and releases its callback/root on disposal. Existing
weak per-frame prefix facts avoid a document-wide history. The lifted node is bound once before
its iterator reference is shared, preserving the QIL tree invariant. The change adds one runtime
helper, recorded in `PublicAPI.Unshipped.txt`; existing helper signatures, capture/dispatcher ABI
and saved-constant format remain unchanged. Eight existing core/API files change, with one extended
JSON fixture and one new runtime fixture.

Eligibility uses the existing header-test and predicate proofs, singleton context/root anchors and
a filtered descendant/child path. Variable-dependent or otherwise unsupported paths receive no
new replay capability. Roaming expressions retain their static rejection. This is not a general
solution for arbitrary consuming populations or grounded-node histories.

## Functional and saved-assembly verification

The actual checkout's solution builds. Full Release and Debug each pass **3,404 net10.0 / 3,290
net8.0**. The solution recompilation reports 28 CA2200/CS8632 warnings in four unchanged ported
upstream test files; no warning source is changed by this increment. Native publications build with
warnings treated as errors and report none.

Coverage includes valid/indented/empty selections, nested and multiple matches, empty-prefix errors,
predicate frequency, error identity/precedence, observation disposal/GC, consumed-source rejection,
explicit replay, attribute headers, legacy normal/debug/saved execution and modern grounded/reader
APIs. The modern reader tests check wrapped XPTY0004, no partial output and borrowed-reader ownership.

The full isolated verification of the same 413 core/configuration files also passes ordinary W3C
checks **46**, upstream **1,654 per framework**, differential **394 per framework**, and controller
checks **13**. All nine fresh conformance failure-ID sets remain unchanged. Strict stays
**2,695 passed / 16 failed / 1 not run**; unmatched expected error codes improve **5 to 4**, because
`sf-xml-to-json-004` now reports its actual cardinality error. No suites, baselines or thresholds change.

The combined current-source NativeAOT consumer passes all **177** retained saved DLLs and compatibility
helpers plus **20** newly generated normal/debug JSON stylesheets. These include valid, nested,
multiple, empty, relative and predicate-error cases. The main manifest has **200** artifacts and
**226** consumer files; scale has **5** artifacts and **9** consumer files. Producer sources are also
hashed. The final saved DLLs are copied byte-for-byte from the verified corrected generator output.

## Preserved failed trials

An initial scale trial combined a crawling deferred population with a consuming body and was
correctly rejected by strict analysis; the rejected fixtures/logs are preserved. The accepted stress
case uses a striding row population. Analysis was not weakened to force a pass.

A runtime regression first demonstrated that an already-consumed source could incorrectly become an
empty argument; the explicit guard fixes it, with separate replay/attribute cases. The pre-guard source,
publications and small result remain under `/tmp/xsl3-p7-json-headers-before-consumed-guard/`.

A full Debug build caught a duplicate non-reference QIL node despite successful Release/native runs.
Binding the lifted node once fixes it; focused Debug then passes 70 / 67 before full suites. The failing
source/logs and successful pre-binding native publications/memory matrix remain separately preserved
under `/tmp/xsl3-p7-json-headers-before-header-binding/`. Those historical memory results are not used
as the final-source result below.

Temporary fixture compilation first used an inaccessible internal setting, then an incorrect test
assembly identity for internal error assertions. The fixtures now use the supported strict-execution
environment setting and the established test assembly identity. Temporary missing reference/test
execution outputs were repaired by rebuilding the temporary projects. These logs remain preserved;
none required suppressing a validator, warning or failing test.

## Ordinary performance

The actual applied source passes the original three-pair gate against
`0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`: time geometric mean **1.024100231667** over **39** timed
rows, maximum allocation ratio **1.037301320326**, exit **0**. The gate ran after
all other tool-run builds/tests/memory jobs completed, with its original thresholds. Raw rounds,
comparison and summary remain in `/tmp/xsl3-p7-json-headers-final-perf-data/`.

Individual timing ratios above 1.05 remain recorded: dispatch/compile **1.6857**, keys/compile **1.0962**, recursion/1mb **1.0558**, regex/1kb **1.2128**, sorting/compile **1.1992**. The aggregate pass does not establish a per-row win or close canonical throughput.

## Actual JSON conversion at exact input sizes

The additional test generator emits rows containing real JSON XML in the W3C function namespace,
with the row's actual 20-digit ID in the JSON string content. A striding row loop calls
`xml-to-json(descendant::j:array[@key='selected']/*)`. There is no error catch or fallback output.
An independent byte hasher builds expected JSON syntax and formatted ID values without using the
engine's JSON implementation or a literal expected digest. This new fixture does not replace the
canonical benchmark generator and its timings are not a comparison with the preceding workload.

| Debug | Input bytes | Elapsed ms | Lifetime peak RSS bytes | Live managed bytes |
|---|---:|---:|---:|---:|
| no | 209,715,200 | 5032.1728 | 30,310,400 | 737,528 |
| no | 2,147,483,648 | 51748.992 | 30,294,016 | 728,792 |
| yes | 209,715,200 | 5172.336 | 30,949,376 | 852,200 |
| yes | 2,147,483,648 | 53918.7234 | 30,572,544 | 843,464 |

All four final-publication children pass with full requested input consumed, independently verified
complete output, first output at **28,672** input bytes, native execution and owned-source cleanup.
Borrowed initial readers remain open. The **209,715,200-byte** managed/RSS guard, full-lifetime maximum
of controller/child RSS and ordinary ICU support remain unchanged. Elapsed values are diagnostic.
The final 10 MiB precheck also passes with the same independent oracle. Raw commands, results and
controller measurements remain in `/tmp/xsl3-p7-json-headers-final-scale-native/`.

After all four runs, all **413** live/frozen source hashes, **177** retained saved hashes, **20** new
saved hashes, protected version-2 fork, producer hashes and both artifact/consumer manifests match.
The final checks are `/tmp/xsl3-p7-json-headers-final-native/final-check.json`; the actual test-source
hashes are recorded separately. Existing canonical input data remains unchanged.

Generic mixed-result try buffering, arbitrary unsupported path/consuming-population continuations,
general grounded-node identity histories, arbitrary opaque/fresh retained models, exact
**20,971,520-byte** push RSS, canonical throughput, remaining conformance/analysis/error-code gates
and current-source Linux evidence remain open. This checkpoint completes no S stage or full P7 claim.

## Follow-up: eligible mixed template results

The later [mixed-template checkpoint](streaming-mixed-template-results-2026-10-26.md) addresses the eligible generic try-result buffering reproduced here. The failed trial above remains historical evidence; excluded shapes and the full Phase 7 exit remain open.
