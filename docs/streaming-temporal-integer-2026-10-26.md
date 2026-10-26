# Deferred temporal context and integer parsing, 2026-10-26

This checkpoint continues Phase 7 on `feature/p7-streaming-analysis`. It improves runtime initialization and integer conversion. Full P7 remains incomplete and `supports-streaming` remains `no`.

## Runtime changes

Temporal state is resolved when an operation requires it. Numeric/string comparisons and arithmetic, numeric aggregates, grouping and merge keys, and explicit-offset date operations no longer initialize the local timezone just by obtaining their dynamic context. The existing configured timezone or actual local-time provider remains authoritative; missing providers and provider errors remain explicit. A thread-safe clock and timezone source are shared by derived static/collation contexts and fork runtimes. Context cloning reuses the clock without allocating a discarded replacement.

The current time is fixed on its first observation within the execution scope. This preserves stable repeated observations and host-supplied times. XPath allows an implementation-dependent observation point during processing; missing date/time offsets still use the implicit timezone. See the [XPath dynamic context](https://www.w3.org/TR/xpath-31/#id-xp-evaluation-context-components) and [date/time operators](https://www.w3.org/TR/xpath-functions-31/#func-subtract-dateTimes).

The XML integer lexer accumulates ASCII digits into an unsigned native magnitude, preserving signs and arbitrary leading zeros. Values beyond that magnitude use the existing arbitrary-precision parser after complete lexical validation. Invalid trailing characters are rejected even after magnitude overflow. Casting still applies its existing whitespace rules and derived-type facets. This uses actual input digits and numeric limits, with no workload constants, timezone fallback, disabled globalization or changed acceptance limits.

## Verification

There are 44 new focused cases per framework: temporal provider resolution, stable concurrent clocks, fixed host time, explicit/missing offsets, provider errors, native integer boundaries, invalid lexical forms, arbitrary precision and derived integer facets. Main and frozen Release/Debug pass **3,563 net10.0 / 3,416 net8.0**. W3C **55**, upstream **1,654 per framework**, differential **394 per framework**, and controller checks **13** pass. All nine conformance headings and failure-ID sets remain unchanged, including strict streaming **2,695/16/1** with zero unmatched expected codes and analysis **790/129**. No suite expectations or baselines were changed.

NativeAOT consumers pass with warnings as errors, retaining **279 byte-identical saved DLLs** and **327 saved-reference paths**, including older formats. All functional corpus consumers also pass on .NET 8 and .NET 10 through the production portability helper; the same two existing native-host assertions are adapted for managed execution. Fresh canonical probe compilation produces its actual 32 saved entry points.

An initial compatibility build overlapped a rebuild of the same temporary core and lost a reference assembly. That failed build is preserved in `publish-initial-shared-build-race.log`. The successful rerun uses a separate, byte-identical core snapshot. The source/consumer/artifact cohorts remain inspectable under `/tmp/xsl3-p7-temporal-integer-full`, `/tmp/xsl3-p7-temporal-integer-compat`, `/tmp/xsl3-p7-temporal-integer-corpus-audit`, and `/tmp/xsl3-p7-temporal-integer-canonical`.

## Exact memory measurements

The unchanged canonical controller measures lifetime absolute RSS with `wait4`, verifies independent output fingerprints, and does not subtract startup. Both the previous accepted source and this source use the same lean native host. The exact **209,715,200-byte input / 20,971,520-byte RSS and heap guard** matrix improves from **6/16** to **13/16** budget passes. Every current measured workload consumes the full requested input and validates its output. Old startup-budget failures did not execute the measured input; their RSS values must not be interpreted as completed transformations.

All table values are bytes, using the greater of internal and process-lifetime counters. A child reporting `ok` still fails when `wait4` exceeds the limit.

| Workload | Previous peak RSS | Current peak RSS | Current budget result |
|---|---:|---:|---|
| identity | 20,381,696 | 20,365,312 | pass |
| adjacent | 22,233,088 | 20,643,840 | pass |
| accumulator | 22,429,696 | 20,643,840 | pass |
| iterate | 22,953,984 | 23,265,280 | fail |
| merge | 22,659,072 | 21,184,512 | fail |
| fork | 22,708,224 | 23,052,288 | fail |
| source-document | 20,512,768 | 20,447,232 | pass |
| climbing-union | 22,396,928 | 20,807,680 | pass |
| climbing-intersection | 22,396,928 | 20,774,912 | pass |
| type-assertion | 20,414,464 | 20,398,080 | pass |
| type-inspection | 20,398,080 | 20,398,080 | pass |
| copy-select | 20,135,936 | 20,348,928 | pass |
| conditional-content | 20,168,704 | 20,398,080 | pass |
| template-parameter | 22,347,776 | 20,824,064 | pass |
| function-parameter | 22,396,928 | 20,889,600 | pass |
| overlap-values | 22,511,616 | 20,905,984 | pass |

The exact **2,147,483,648-byte input / 209,715,200-byte guard** matrix passes **16/16**. Its controller exit is **0**. The 200 MiB push controller exit is **1**. These are separate obligations; the larger guard does not replace the 20 MiB target.

Separate diagnostic-only phase snapshots located RSS growth in execution rather than the independent output oracle. Heap commitment grew similarly for identity and integer workloads, while integer-heavy paths also touched more runtime pages. The diagnostic host and read-only memory mappings are retained separately and are not acceptance measurements. The first timezone-only candidate passed 10/16; an intermediate integer candidate passed three of six previously failing measured workloads, with merge still failing the lifetime counter despite its internal `ok`. Final measurements above supersede those exploratory runs without overwriting them.

## Throughput and ordinary performance

The unchanged canonical benchmark runs **three alternating pairs at decimal 100 MB**, with the same **4 GiB** managed guard for both modes. All **42** trials validate their output; target pass is **False**, controller exit **1**. A ratio above 1 fails the exact stream-no-slower-than-tree requirement. The previous typed-static and current temporal/integer matrices ran separately; their before/after times are observations rather than a paired attribution test.

| Workload | Previous tree ms | Previous stream ms | Current tree ms | Current stream ms | Current stream/tree | Current observed winner |
|---|---:|---:|---:|---:|---:|---|
| identity | 2230.376 | 2829.661 | 2321.363 | 2997.405 | 1.291226 | Tree observed median |
| adjacent | 1760.285 | 1008.155 | 1809.232 | 1044.439 | 0.577283 | Stream observed median |
| accumulator | 6243.248 | 4382.437 | 6161.836 | 4368.209 | 0.708913 | Stream observed median |
| iterate | 1657.184 | 1403.413 | 1508.724 | 1300.017 | 0.861667 | Stream observed median |
| merge | 13789.986 | 3666.867 | 13704.111 | 3541.949 | 0.258459 | Stream observed median |
| fork | 1829.537 | 6160.125 | 1553.668 | 5770.483 | 3.714103 | Tree observed median |
| source-document | 2319.159 | 2957.453 | 2329.415 | 2939.299 | 1.261819 | Tree observed median |

The original ordinary three-pair gate against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` passes: time geometric mean **0.992096614330**, maximum allocation ratio **1.037339478088**, over **39** timed rows. Individual time ratios above 1.05 remain recorded: dispatch/compile 1.091903, grouping/compile 1.066619, sorting/compile 1.123903. This aggregate gate establishes no per-row win. Raw evidence is `/tmp/xsl3-p7-temporal-integer-final-perf-data/`.

Remaining obligations include the failed exact push/throughput rows, analysis-suite reconciliation, general bounded retained models, outstanding saved schema/atomic cases, and current-source Linux acceptance evidence. No commit, push, publication, or feature-completion claim is made.

## P7A release boundary

The owner subsequently froze this implementation as the [P7A foundations phase](plan/p7a-closeout.md) for a
1.1.0 candidate. Further P7 implementation is deferred. A completed follow-up on Docker Linux ARM64 uses the
same 418-file source snapshot and unmodified controllers: the exact 200 MiB / 20 MiB matrix fails all 16 budget
checks (some before measurement); the separate 2 GiB / 200 MiB matrix passes all 48 runs and 16 workloads.
These virtualized ARM64 results do not replace hosted Linux x64 CI or satisfy the 20 MiB obligation. Raw follow-up
evidence is `/tmp/xsl3-p7-current-linux/`. The release scope, unchanged experimental acceptance checks and package
validation are recorded in the phase close-out and [candidate release notes](releases/1.1.0.md).
