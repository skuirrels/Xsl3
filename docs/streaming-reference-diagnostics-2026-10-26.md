# Streaming reference and diagnostic contracts, 2026-10-26

This follows the [mixed-template checkpoint](streaming-mixed-template-results-2026-10-26.md) on `feature/p7-streaming-analysis`. Full P7 remains active and `supports-streaming` remains `no`.

## Document references and specific errors

A shared internal reference check rejects literal backslashes before .NET or a configured resolver can normalize them as file paths. `fn:doc` and both streamed/grounded `xsl:source-document` paths report FODC0005; availability functions return false without resolving or opening the invalid reference. Encoded backslashes, ordinary relative/absolute references, spaces and Unicode retain their existing provider paths. Legacy `document()` retains its provider path policy. Resource retrieval and XML parsing failures remain FODC0002.

This elects the optional invalid-reference diagnostic described in [F&O 3.1, fn:doc](https://www.w3.org/TR/xpath-functions-31/#func-doc). It neither fabricates a document nor renames a retrieval failure after trying to open it. Callers naming a literal backslash in a resource must use percent-encoding in its URI.

A streamable attribute set referencing a nonstreamable set already reported XTSE0730 at the actual use span, then added a generic XTSE3430 at an earlier enclosing position. Source ordering exposed the generic code first. An assessment now retains its computed verdict and complete body analysis while avoiding a redundant generic error when that assessment has already emitted a specific nonwarning diagnostic. Errors from earlier components do not suppress later diagnostics. Strict/fallback reference errors, independent XPST0008 and independent XTSE3430 are covered.

Four existing core files change. No public helper signature, ordinary template ABI or saved-constant format changes.

## QT3 diagnostic accounting

The first full run showed QT3 unmatched-code notes increasing from 67 to 69. A controlled per-case comparison isolated three Windows-path cases that explicitly allow both FODC0002 and FODC0005, plus a malformed-URI case with the alternatives in the opposite order. The harness selected the first tolerated wrong-code pass even when a later alternative matched exactly.

`any-of` now prefers an exact or wildcard pass before falling back to a tolerated wrong-code pass. `all-of` retains wrong-code notes instead of discarding them when all conjuncts pass under the existing error tolerance. Pass/fail tolerance itself is unchanged. Nine real-runner regressions cover ordering, wildcard and nested cases, plus genuinely unmatched codes. The unchanged runner fails five of the nine; the corrected runner passes all nine.

With identical corrected harness sources and isolated verified core binaries, both the preceding and URI-policy kernels report **25** unmatched QT3 codes, no failures, and exactly the same per-case unmatched-code details across **21,983** applicable cases. The 67/69 counts included misleading accounting; the corrected count is not a claim that the engine fixed 42 runtime errors. No suite assertions or baselines change.

## Functional, profile and native checks

The isolated final cohort passes Release and Debug **3,442 net10.0 / 3,324 net8.0**, W3C **55**, upstream **1,654 per framework**, differential **394 per framework**, and controller checks **13**. All nine profile failure-ID sets remain unchanged. Strict streaming stays **2,695 passed / 16 failed / 1 not run**; unmatched expected codes improve **4 to 1**, covering the two source-document URI cases and the specific attribute-set error. The remaining strict mismatch is the separate provider-prevalidation/source-validation issue.

The actual applied branch passes the solution build, both unit configurations at **3,442 / 3,324**, and all nine accounting regressions. The solution retains 28 previously attributed warnings in unchanged ported upstream test files. Native publications keep warnings as errors and report none.

The current-source native consumer passes **213** retained checkpoint saved DLLs, **12** fresh normal/debug URI styles and the existing compatibility helpers. Producer checks also verify specific declaration errors and independent diagnostics. The frozen cohort contains **414** core/configuration files, **228** artifacts, **256** consumer files and two producer files. All **273** actual saved-reference paths are separately hashed. The applied four core files, one runner file and three new fixtures are recorded in the application manifest.

Temporary reference/dependency output failures were preserved and repaired with fresh isolated builds or exact locked runtime assets. The controlled accounting comparison uses the actual four suite-runner sources and verified kernels. No compiler check, validator, warning policy or assertion was suppressed to obtain the results.

## Ordinary performance and remaining limits

The applied cohort passes the original ordinary three-pair gate against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`: time geometric mean **1.022631015925** over **39** timed rows, maximum allocation ratio **1.037310200701**, exit **0**. Raw rounds, comparison and summary remain in `/tmp/xsl3-p7-streaming-diagnostics-final-perf-data/`.

Individual timing ratios above 1.05 remain recorded: dispatch/compile **1.142958**, keys/compile **1.580645**, regex/1kb **1.170213**, sorting/compile **1.093987**. The aggregate pass establishes no per-row win.

After the gate, all **414** live/frozen core/configuration hashes, four auxiliary source hashes, **213** retained and **12** fresh checkpoint DLL hashes, all **273** actual saved-reference paths, the protected version-2 fork and artifact/consumer/producer manifests match. Final checks are `/tmp/xsl3-p7-streaming-diagnostics-native/final-check.json`.

This URI/diagnostic change adds no new exact-size memory claim. The preceding mixed-template cohort's four verified native memory cases remain evidence for that frozen source. Exact **20 MiB** push RSS, canonical throughput, remaining conformance/analysis gates, general retained models and current-source Linux evidence remain open.

## Follow-up: source validation negotiation

The later [source-validation checkpoint](streaming-source-validation-negotiation-2026-10-26.md) resolves the remaining strict unmatched-code case through resolver negotiation. Its native runtime validation coverage is separate from the unsupported schema-aware saved round trip. This report remains evidence for the preceding frozen source.
