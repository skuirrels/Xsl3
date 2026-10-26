# Optional focus capture and accumulator errors, 2026-10-26

This extends the [random-generator checkpoint](streaming-random-captures-2026-10-25.md) on
`feature/p7-streaming-analysis`. Focus-dependent function references now preserve an absent focus
without demanding it during creation. Full Phase 7 remains active; `supports-streaming` remains `no`.

## Correctness and representation

Creating a named or partial function reference does not call the function. The compiler now captures
the optional context item as a sequence, including its actual absence. A lifted focus retains that
optional sequence so an intrinsic's explicit missing-context contract can be applied when invoked.
XSLT and dynamic XPath host environments provide the optional item without triggering a generic
`XPDY0002` check first. Ordinary context-item evaluation keeps its existing checked path.

Accumulator calls report `XTDE3350` for absent context. Non-node, attribute and namespace contexts
continue to report `XTTE3360`, as prescribed by the
[XSLT 3.0 Recommendation](https://www.w3.org/TR/2017/REC-xslt-30-20170608/#err-XTDE3350).
The existing specific `key()` missing-context contract (`XTDE1270`) now survives captured and
runtime focus checks too. Ordinary `string()` calls still report `XPDY0002` when invoked without
context; creating `string#0` without context is valid. Actual present items remain early-bound,
including a node used after the caller changes focus.

The change is confined to five core files and a 24-case behavioral fixture. It adds no public API,
runtime helper entry point, generated calling convention or saved-constant format. Existing helpers
implement the optional check, and absence is not replaced by a fabricated item or an empty string.

The preserved preceding core reproduces all eleven initial regression failures on both frameworks.
The expanded tests cover both public API paths, direct/inline/dynamic/partial calls, local and global
reference creation, ordinary absent-focus behavior, invalid item kinds and early-bound real nodes.
Legacy tests include debug and saved compilation with the actual initial-template entry point.
The isolated broader suite passes **165 net10.0 / 156 net8.0**.

## Functional and native evidence

Full Release and Debug each pass **3,380 net10.0 / 3,268 net8.0**. The solution builds; ordinary W3C
checks pass **46**, upstream **1,654 per framework**, differential **394 per framework**, and controller
checks **13**. All nine fresh conformance failure-ID sets remain unchanged. Strict remains
**2,695 passed / 16 failed / 1 not run**, while its unmatched expected error codes improve **6 to 5**:
the dynamic accumulator call now reports the prescribed absent-context code. All other report
headings remain unchanged. No suite files, baselines or acceptance thresholds are changed.

The optimized current-source NativeAOT consumer passes twelve newly compiled normal/debug focus
styles and all **165** retained saved DLLs: 153 preceding RNG-checkpoint DLLs and the twelve focus
styles compiled before the allocation optimization. Compatibility helpers pass too. Both native
publications have no warnings. The main manifest contains **180** artifacts (**177** saved DLLs plus
three publish files), and the scale manifest contains **5**. Consumer-source manifests cover **205**
and **9** files, including three preceding-core expected-output files. Frozen source/configuration
contains **413** files.

An initial isolated native fixture selected its template only after compilation and produced no
result; selecting the actual initial template before `CompileToAssembly` corrected the fixture.
The original log is preserved. Initial extended XML fixtures also double-escaped their XML string
arguments; correcting the test inputs required no engine changes.

## Ordinary performance

The first ordinary performance gate passed the time geometric mean but failed `keys/compile`
allocations at **+5.7%**. Its six raw round files, comparison, logs and failed exit status are preserved
under `/tmp/xsl3-p7-absent-focus-first-allocation-failed/`; that failure was not attributed to timing
noise or accepted by relaxing a threshold.

The compiler now uses its existing match/loop focus guarantee to avoid generating an absent-item
check in scopes where focus is known to exist. A statically singleton optional item also bypasses
the redundant zero-or-one check. Global, named-template and lifted optional-focus paths retain the
checks and all missing-focus semantics. The same preserved benchmark harness, with only its core
DLL changed, measured **205,347** versus **199,859** compile bytes in both ABBA pairs, a reduction of
**5,488** bytes per compile. The isolated regression suite remains **165 / 156** passing.

The corrected frozen source passes the original alternating three-pair gate against
`0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`: time geometric mean **1.030587374330**
over **39** timed rows, maximum allocation ratio **1.037309886931**, exit **0**.
The original five-percent thresholds remain unchanged. Raw rounds and the comparison remain under
`/tmp/xsl3-p7-absent-focus-optimized-perf-data/`.

Individual timing ratios above 1.05 remain recorded: grouping/compile **1.0573**, join/compile
**1.0597**, keys/compile **2.0301**, regex/1kb **1.1702**, sorting/compile **1.2220**. The aggregate
pass does not establish an improvement for each row or close the separate throughput gate.

## Actual absent-context function values under spill

The stylesheet explicitly declares the global context absent and creates `accumulator-before#1`
as a global parameter. A streamable `function(*)` accumulator produces a fresh `$f(?)` partial
function for each source row. Its captured global reference has the actual absent context.
Deferred climbing `except` selects odd rows. Each recorded function is invoked with `n`; only an
`err:XTDE3350` catch produces the text derived from that row's 20-digit ID and numeric ID.
Premature reference-creation errors, a different error code or an unexpected successful result fail
the independent full-output check. The canonical XML generator and ID-based expected-output
algorithm remain unchanged.

### Preserved mixed-result buffering failure

The first 10 MiB probe placed text construction inside a per-row `xsl:try`, leaving its result statically
mixed. It consumed the full source and produced the correct digest, but first output occurred only
at **10,485,760** input bytes, with peak RSS **113,934,336** and live managed memory **857,776** bytes.
Its process status was **1** because the early-output check failed. The full source fixtures, saved
DLLs, native publication, raw result and both matching original manifests are preserved under
`/tmp/xsl3-p7-absent-focus-first-mixed-try-scale/`. This remains an open incremental-output/memory
issue; it is not counted as a successful bounded case.

The accepted scalar fixture makes each record's text constructor explicit around the try expression.
Its catch returns the same source-derived string. At the same exact 10 MiB input it independently
verifies the identical digest, emits at **57,344** input bytes and peaks at **31,637,504** RSS bytes,
with **853,424** live managed bytes after collection. The error-code assertion, input, expected-output
algorithm and budget remain unchanged. This does not establish bounded behavior for mixed-result try
loops. The original focus assemblies and failed allocation trial remain unchanged. The optimized source
was frozen separately, and its twelve new focus assemblies and two scale assemblies were generated
and published separately before their final manifests were recorded.

| Debug | Input bytes | Elapsed ms | Lifetime peak RSS bytes | Live managed bytes |
|---|---:|---:|---:|---:|
| no | 209,715,200 | 77750.098 | 31,653,888 | 862,176 |
| no | 2,147,483,648 | 875660.8551 | 31,637,504 | 853,976 |
| yes | 209,715,200 | 76187.9167 | 31,670,272 | 867,840 |
| yes | 2,147,483,648 | 835592.5575 | 31,735,808 | 859,640 |

All four exact-size children pass with full input consumption, independently verified output,
first output at **57,344** input bytes, native execution and owned-source cleanup. The normal and
debug results have identical digests for each input size. Commands, raw results and controller
measurements remain in `/tmp/xsl3-p7-absent-focus-optimized-scale-native/`. Additional isolated JSON
compilation/testing overlapped parts of this memory matrix; elapsed times are diagnostic.

The **209,715,200-byte** heap/RSS guard and ordinary ICU support remain unchanged. Lifetime RSS is
the maximum of child and controller measurements, without subtraction. Elapsed times are diagnostic;
canonical throughput remains a separate gate. Owned source streams close and borrowed initial
readers remain open.

After all four children completed, all **413** live/frozen source hashes, **165** retained saved
DLL hashes, twelve new optimized focus DLLs, protected version-2 fork, generator-source hashes and
both artifact/consumer-source manifests match. The main publication contains **180** artifacts and
**205** consumer files; scale contains **5** artifacts and **9** consumer files. The canonical XML
generator remains byte-for-byte unchanged. The final hash result is
`/tmp/xsl3-p7-absent-focus-optimized-native/final-check.json`.

The mixed-result try buffering case, XML-to-JSON continuation for filtered descendant/child paths,
general grounded-node histories, arbitrary opaque callables and fresh retained code/context models
remain open. Exact **20,971,520-byte** push RSS, canonical throughput, remaining conformance/analysis
and exact-code gates, and current-source Linux x64 evidence also remain open. This increment does
not complete an S stage or the full Phase 7 plan.


The later [XML-to-JSON continuation checkpoint](streaming-json-header-continuation-2026-10-26.md) repairs eligible header-path continuations and adds its own source/native/performance evidence. This optional-focus checkpoint remains the historical result for its separately frozen source; unsupported paths and the mixed-result try issue remain open.

## Follow-up: eligible mixed template results

The later [mixed-template checkpoint](streaming-mixed-template-results-2026-10-26.md) addresses the eligible generic try-result buffering reproduced here. The failed trial above remains historical evidence; excluded shapes and the full Phase 7 exit remain open.
