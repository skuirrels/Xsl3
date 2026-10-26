# Mixed template result streaming, 2026-10-26

This continues the [XML-to-JSON header checkpoint](streaming-json-header-continuation-2026-10-26.md) on `feature/p7-streaming-analysis`. Full Phase 7 remains active and `supports-streaming` remains `no`.

## Reproduced problem and implementation

A template with an unconstrained mixed `item()*` result used the ordinary function result cache. A per-record `xsl:try` returning either an integer or catch text therefore collected the entire population before the principal writer received output. Six original regressions across the supported frameworks and public writer API paths reproduced correct complete output but first output only at the final row. A preserved native 10 MiB case likewise produced its first bytes at EOF, with 113,934,336-byte peak RSS.

Eligible template and mode-dispatch calls now use memoized pull companions. Explicit internal metadata identifies templates; compilation does not infer eligibility from debug names. Original definitions are snapshotted before rewriting. The companion is registered before its body is visited, so recursive calls retain call boundaries. Already evaluated arguments are boxed/captured and unboxed through the existing typed conversion path, and the stylesheet frame identity is copied.

The ordinary template calling convention, runtime helper signatures and saved constant format remain intact. Four existing core files and one new partial implement the change. Existing `DepthLimit` metadata and its maximum-depth argument survive body cloning. Tracing-enabled compilation, an explicit maximum recursion depth, constrained return types and original formal defaults retain the existing paths. Constructing a single indivisible large result node can still require memory for that node.

Debug compilation retained a writer-first `Choice` cache. Only choices in cloned pull companions become a let-bound selector and balanced iterator conditionals. Each branch occurs once, indexed/default selection is retained, and even a one-branch choice evaluates its selector. The IL backend is unchanged.

A broader regression found that a context-free raw-result call could recreate its cursor on each resume after let inlining. The cursor allocation is now marked as stateful. Managed stacks and the interrupted failed full run are preserved. A cancellation-bounded before/after test reproduces and fixes the nontermination; the independent raw function-kind check passed before and after. No test or validator was disabled.

Complex-content normalization still spans the complete item sequence. Arrays, empty document/text boundaries, adjacent atomics, raw atomic/function kinds and constructed-node identity are covered. Each per-record try remains transactional: a late error discards its partial sequence before catch output. Typed/default/tunnel parameters evaluate once, and named/mutually recursive templates and 128-rule mode dispatch retain their results.

## Functional and saved execution evidence

The actual solution builds. Full Release and Debug each pass **3,422 net10.0 / 3,306 net8.0**. The isolated same-source suites also pass ordinary W3C **46**, upstream **1,654 per framework**, differential **394 per framework** and controller checks **13**. All nine fresh profile headings and failure-ID sets remain unchanged. Strict streaming stays **2,695 passed / 16 failed / 1 not run**, with four unmatched expected error codes. No suite, baseline or threshold has changed.

The corrected NativeAOT consumer passes **197 retained checkpoint saved DLLs**, **16 fresh normal/debug styles** and the existing compatibility helpers. Fresh cases include mixed success/catch values, rollback, normalization, parameters, recursion, mode dispatch and raw fork/function results. All **261** actual referenced saved DLL paths are separately hashed, including older references outside the checkpoint set. There are **216** main artifacts, **243** main consumer files and two producer files; scale has five artifacts, nine consumer files and two producer files. The corrected cohort freezes **414** core/configuration source files.

Temporary producer/consumer compilation failures are retained: missing shared reference-build outputs, an inaccessible internal execution setting, a missing trimming annotation and a fixture type namespace. Native and regression build intermediates are isolated; producers use the public strict environment setting, raw saved fixtures use the established test friend assembly, and the Load(Type) annotation is propagated. Native publication keeps warnings as errors.

## Native memory and ordinary performance gates

The exact original failing mixed-try source and canonical generated input are reused without an outer `value-of` workaround. The output oracle independently formats actual row IDs and hashes the complete expected byte sequence. The corrected 10 MiB normal/debug prechecks both pass, with first output after **57,344** input bytes and full input/output, native execution, owned-source closure and borrowed-reader ownership checks.

The corrected cohort passes all four final exact-size cases:

| Debug | Input bytes | Elapsed ms | Lifetime peak RSS bytes | Live managed bytes |
|---|---:|---:|---:|---:|
| no | 209,715,200 | 99177.7574 | 31,948,800 | 867,696 |
| no | 2,147,483,648 | 1021032.8728 | 32,030,720 | 859,496 |
| yes | 209,715,200 | 100462.1849 | 32,030,720 | 868,256 |
| yes | 2,147,483,648 | 807476.8219 | 32,079,872 | 860,056 |

Every child verifies full input, complete independently hashed output, first output at **57,344** bytes, native execution and owned-source cleanup; the hashed consumer also verifies borrowed initial readers remain open. Stress timings overlap isolated follow-on work and are diagnostic. Earlier pre-cursor-fix publications and memory trials remain historical evidence.

The original ordinary three-pair gate against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` passes with time geometric mean **1.018739215186** over **39** timed rows and maximum allocation ratio **1.037305995787**, exit **0**. The gate runs after the active memory and regression jobs close, with its original thresholds. Raw rounds, comparison and summary remain in `/tmp/xsl3-p7-mixed-template-final-perf-data/`.

Individual time ratios above 1.05 remain recorded: grouping/compile **1.066841**, keys/compile **1.423111**, regex/1kb **1.170213**, sorting/compile **1.075252**. An aggregate pass does not establish a per-row win or canonical throughput.

After all four native cases and the ordinary gate, all **414** live/frozen source hashes, **197** retained and **16** new checkpoint DLL hashes, all **261** actual saved-reference paths, the protected version-2 fork, canonical input, actual test source and both artifact/consumer/producer manifests match. The final checks are `/tmp/xsl3-p7-mixed-template-stateful-native/final-check.json`.

The heap/RSS guard remains **209,715,200 bytes**, using the full lifetime maximum of the controller and child with ordinary ICU support. No RSS baseline is subtracted. These stress timings do not establish canonical throughput or the exact **20,971,520-byte** push RSS requirement.

General grounded-node histories, arbitrary retained/opaque models, excluded template shapes, exact push RSS, canonical throughput, remaining conformance/analysis/error-code gates and current-source Linux evidence remain open. This increment completes no full S stage or P7 claim.

## Follow-up: reference and diagnostic contracts

The later [diagnostic checkpoint](streaming-reference-diagnostics-2026-10-26.md) extends this source with URI checks, specific declaration diagnostics and corrected QT3 accounting. This report and its exact-size matrix remain evidence for the frozen mixed-template cohort.
