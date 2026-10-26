# Recursive transmitting functions, 2026-10-25

This follow-up continues the [comma/content checkpoint](streaming-comma-content-2026-10-25.md) on
`feature/p7-streaming-analysis`. The full Phase 7 goal remains active; `supports-streaming` stays `no`.

## Findings and implementation

The reduced `f:odd(doc, 1)` and `f:peek(doc)` cases in
`/tmp/xsl3-p7-shallow-continuation-repro/` succeed on tree input but fail on the first function-body
navigation on forward input. The list argument ABI collects a path-selected singleton before the
function can navigate it. Recursive transmitting results also need suspension: collecting the result
list advances past returned nodes before their caller can copy or navigate them.

`StreamedFunctionLowering` now registers resumable implementations for eligible functions whose first
parameter is a singleton node and whose declared result is `node()*` or another node type with `*`
cardinality. It snapshots original definitions, registers each producer before rewriting its body,
and uses the existing pull-function continuation machinery for self and mutual recursion. Arguments
are captured once; each returned node reaches its consumer before the producer resumes. The existing
singleton guard checks the whole argument selection without retaining its nodes. Parameter and result
checks retain `XPTY0004` and `XTTE0780`. The original function ABI remains available, and the dispatcher
is completed after these producers have registered. No public or runtime helper ABI is added.

Body cloning also preserves the `StreamedSimpleContent` annotation, which previously could be lost
when a function body was expanded. A QIL regression verifies that the incremental text-writing policy
survives cloning.

This follows the current lowering eligibility: tracing, configured recursion-depth limits, and memoized
functions retain their existing call path. Narrow node return cardinalities also retain their current
path because a consumer may stop after the first result, before a suspended producer could complete
its result-cardinality check. Dynamic calls, many-item transmitting parameters, and other remaining
continuation cases are not claimed as solved by this change.

## Verification

The focused Release and Debug regressions pass on both frameworks: **75 net10.0 / 71 net8.0**.
New coverage exercises self and mutual recursion, live inspection followed by transmission, both public
APIs, argument and result errors, saved normal/debug compilation, and 5,000-record early delivery with
bounded open source headers. The fixture permits at most ten retained headers for its five node levels
and one lookahead chain; this is not a process RSS result. Borrowed readers remain open.

The full solution builds. Debug and Release each pass **3,192 net10.0 / 3,091 net8.0** unit tests.
The normal W3C harness passes all **46** tests; upstream passes **1,654** and differential passes **394**
on each framework. All **13** streaming-controller tests pass. The full validation driver exits **0**,
while retaining the strict audit's expected nonzero status. No baseline is edited. The eight-profile
comparison against the comma/content checkpoint reports no new failure IDs. Strict forward execution
now passes `su-shallow-descent-002` and `su-shallow-descent-004`, moving from **2,687/24/1** to
**2,689/22/1**, with seven unmatched expected codes. Other profile counts are unchanged.

| Profile | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| XSLT 3.0 | 8847 | 21 | 41 | 34 |
| XSLT 2.0 | 4588 | 9 | 11 | 7 |
| Unstreamed development | 2692 | 19 | 1 | 5 |
| Strict forward inputs | 2689 | 22 | 1 | 7 |
| Posture and sweep | 790 | 129 | 0 | 0 |
| Schema-aware | 843 | 1 | 2 | 6 |
| Schema-aware strict | 842 | 2 | 2 | 6 |
| QT3 | 21884 | 0 | 99 | 67 |

The frozen NativeAOT consumer passes the ten new saved normal/debug stylesheets: recursive copy,
recursive text values, inspection/transmission, argument errors and result errors, plus all retained
saved consumers, the prior materialized-merge consumer and immutable v2 fork consumer. The final
publish has no warnings. `/tmp/xsl3-p7-recursive-native/` records **405** source/configuration hashes
and **76** saved/native artifact hashes. Both the frozen source and the live source matched the snapshot
when verified. The native expected-error harness checks missing errors outside its catch filter.

Commands and logs are retained under `/tmp/xsl3-p7-recursive-*`: focused tests, the full regression
driver, the frozen native source and generator, publish output and executed-consumer log. No baseline,
commit or push is changed by this follow-up.

## Generated-input recursive NativeAOT proof

All four normal/debug runs pass with exact **209,715,200** and **2,147,483,648** byte inputs, normal
ICU, dynamic-code support disabled and no trusted-platform-assembly list. The effective managed heap
guard and this focused absolute RSS guard are **209,715,200** bytes. This is a focused 200 MiB proof,
not the required 20 MiB push gate. It runs after the functional driver finishes; timings remain
harness diagnostics rather than a throughput comparison.

The stylesheet recursively selects odd-position children and writes their values. The harness uses
the existing generated XML fixture, hashes output as it is written, and independently derives the
expected digest from the fixture's record count and value. It retains no complete output buffer.
Every run validates the digest, exact consumed byte count, early output, effective heap limit,
native flags, borrowed-reader ownership and the maximum of child and outer lifetime RSS.

| Build | Input bytes | Records | Output bytes | Lifetime RSS bytes | Live managed bytes | Input at first output |
|---|---:|---:|---:|---:|---:|---:|
| normal | 209715200 | 1823610 | 3647219 | 30130176 | 727624 | 356352 |
| normal | 2147483648 | 18673770 | 37347539 | 29655040 | 727856 | 356352 |
| debug | 209715200 | 1823610 | 3647219 | 29966336 | 843208 | 356352 |
| debug | 2147483648 | 18673770 | 37347539 | 29949952 | 843440 | 356352 |

Source provenance, saved stylesheets, artifact hashes, compiler logs, per-child stdout/stderr and the
controller report are retained under `/tmp/xsl3-p7-recursive-scale-native/`. The core assembly comes
from the same frozen **405**-file snapshot as the saved-consumer matrix. The native publish has no
warnings; the controller exits **0**. RSS stays near 30 MB from 200 MiB to 2 GiB, and first output arrives
after **356,352** input bytes in every run. Arbitrarily large atomic or materialized results are not
covered by this bounded-result fixture.

## Completed prior-candidate scaling sweep

The already running sweep used the unchanged comma/content frozen process host. It completed with
exit **1**: **84 attempted trials, 72 valid, 12 failed**, using equal 4 GiB heap guards and three rounds
per mode. All 42 trials at 1 MB passed. At 1 GB all 21 streamed trials passed; all three tree trials each
for adjacent, accumulator, merge and fork failed with `System.OutOfMemoryException`. Identity, iterate
and source-document passed in both modes. All **83** process-host artifacts still match their hashes.

Together with the prior candidate's completed 100 MB trials, all three planned sizes were attempted.
The four failed large-input tree groups have no valid speed comparison. The 100 MB canonical throughput
target remains failed on identity, fork and source-document. These measurements belong to the frozen
comma/content candidate, before the recursive-function change. Raw trials and their derived summary
are under `/tmp/xsl3-p7-comma-content-scaling-data/`.

## Remaining work

The latest exact native push evidence still has sixteen RSS failures at the required 20 MiB limit;
the nightly 200 MiB gate passed at the previous checkpoint. The throughput target, remaining strict
semantic failures, general function continuations, non-monotonic ancestor difference, source validation,
URI fragments, typed/serialization cases and remote Linux x64 evidence remain open. Large atomic or
materialized results can still grow. The full Phase 7 goal remains active.


The [document-fragment follow-up](streaming-document-fragments-2026-10-25.md) extends the same branch with selected source views,
resource identity and DTD metadata preservation. Its evidence applies to the later frozen source.
