# Conformance

Pass rates per feature and test set (SPEC §20). CI will regenerate this file once the W3C harness
(`tests/Xsl3.W3C`, phase P1 onwards) exists; until then it records the XSLT 1.0 baseline by hand.

## XSLT 1.0 (legacy mode)

| Suite | net8.0 | net10.0 |
|---|---|---|
| dotnet/runtime `XslCompiledTransform` tests (`tests/Xsl3.Upstream.Tests`) | 1661 / 1661 | 1661 / 1661 |

## XPath 3.1 parsing

| Suite | Result |
|---|---|
| QT3 parse coverage (`Qt3ParseCoverageTests`): XPath 3.1-applicable tests parse, or are rejected with XPST0003 when that is the only acceptable outcome | 22,364 / 22,364 (253 must-reject) |

## XPath 3.1 evaluation (QT3)

`Qt3RunTests` runs every QT3 test that applies to what Xsl3 claims: XPath 3.1, the languages `en` and `de`
(month and day names; other languages fall back to English), plus the `namespace-axis`
and `infoset-dtd` features. Not claimed yet: `higherOrderFunctions` (P3), `schemaImport` and
`schemaValidation` (P6), `staticTyping` (never). Known failures are listed in
`tests/Xsl3.W3C/baselines/qt3-run.txt`; a test that expected an error passes on any error, and passes that
raised a different code are counted separately.

| Date | Passed | Failed | Not run | Pass rate | Notes |
|---|---:|---:|---:|---:|---|
| 2026-10-18 | 16,354 | 3,694 | 99 | 81.6% |
| 2026-10-18 | 18,524 | 1,524 | 99 | 92.4% |
| 2026-10-19 | 19,077 | 969 | 99 | 95.2% |
| 2026-10-19 | 19,222 | 822 | 99 | 95.9% |
| 2026-10-19 | 21,685 | 199 | 99 | 99.1% | Maps, arrays, function items, JSON; `higherOrderFunctions` claimed (1,840 more tests apply) |
| 2026-10-19 | 21,731 | 153 | 99 | 99.3% | `fn:serialize` with a map (json, adaptive); maps keep insertion order |

The remaining failures are mostly fn:serialize details (html doctype, CDATA, character maps),
fn:random-number-generator and fn:function-lookup corner cases.

## XSLT 2.0 / 3.0 (xslt30-test)

`Xslt30RunTests` runs the xslt30-test cases that apply to the current profile (P2: tests for "XSLT 1.0 or
later" and "XSLT 2.0 or later", without the schema-aware, streaming and higher-order-function features), at
the commit `eng/fetch-w3c.sh` pins. Known failures are listed in `tests/Xsl3.W3C/baselines/xslt30-run.txt`.
Expected errors pass on any error for now (608 of those passes raised a different code than the test names).
The baseline is generated from a Release build, as CI runs it.
The `unicode-90` sets, which loop over every Unicode character, run only on request (`XSL3_XSLT30_SETS`), so
the later rows count about 1,460 fewer tests.

| Date | Passed | Failed | Not run | Pass rate | Notes |
|---|---:|---:|---:|---:|---|
| 2026-10-19 | 2,437 | 3,429 | 194 | 41.5% | Upstream's 1.0 engine; 2.0 stylesheets run in forwards-compatible mode |
| 2026-10-19 | 4,031 | 391 | 178 | 91.2% | The 2.0 path on by default (`XSL3_XSLT2=0` restores the row above); without `unicode-90` |
| 2026-10-19 | 4,171 | 251 | 178 | 94.3% | Static checks, base URIs, `xsl:result-document` serialization |
| 2026-10-19 | 4,208 | 214 | 178 | 95.2% | Key collations, `use-when` on modules, 2.0 `xsl:strip-space` names; P2 exit criterion met |

From P3 the harness runs the XSLT 3.0 profile: tests for "XSLT 3.0 or later" too, with higher-order functions
and XPath 3.1 claimed (`XSL3_XSLT30_PROFILE=2.0` still runs the 2.0 one). Stylesheets with `version="3.0"` are
no longer compiled in forwards-compatible mode. Tests that need packages, initial functions, collections or
a schema aren't run yet (the report's "Not run" line counts them by reason).

| Date | Passed | Failed | Not run | Pass rate | Notes |
|---|---:|---:|---:|---:|---|
| 2026-10-19 | 4,881 | 1,077 | 2,067 | 81.9% | XSLT 3.0 profile, first run |
| 2026-10-19 | 5,023 | 935 | 2,067 | 84.3% | Text value templates, `xsl:mode` |
| 2026-10-19 | 6,198 | 1,201 | 626 | 83.8% | The harness runs tests whose stylesheet comes from their environment |
| 2026-10-19 | 6,494 | 1,087 | 444 | 85.7% | Static parameters, shadow attributes, `xsl:where-populated`, `xsl:on-empty`, `xsl:try`, predicate patterns |
| 2026-10-19 | 6,516 | 1,065 | 444 | 86.0% | `xsl:iterate` |
| 2026-10-19 | 6,670 | 911 | 444 | 88.0% | Whitespace stripping of loaded documents, HTML5 serialization, `fn:available-system-properties` |
| 2026-10-19 | 6,791 | 790 | 444 | 89.6% | `xsl:source-document`, `xsl:copy select`, `default-mode`, `xsl:sequence` content, `xsl:context-item` |
| 2026-10-19 | 6,880 | 701 | 444 | 90.8% | json and adaptive output, `xsl:map`, accumulators, `xsl:merge`, `on-multiple-match`, packages used alone |
| 2026-10-19 | 7,058 | 881 | 86 | 88.9% | Package tests run (358 more): `xsl:use-package` from the test's package library, shadow attributes on the stylesheet element |
| 2026-10-19 | 7,193 | 746 | 86 | 90.6% | Package visibility: per-package component tables, `xsl:expose`, `xsl:accept`, `xsl:override`, `xsl:original`, declared modes |
| 2026-10-19 | 7,389 | 550 | 86 | 93.1% | Packages: decimal formats, keys and namespace aliases are local to a package; abstract attribute sets (XTDE3052); what `xsl:override` may contain |
| 2026-10-19 | 7,365 | 574 | 86 | 92.8% | `fn:current-output-uri` and the base output URI, xhtml with HTML5, serialization parameter errors, indentation after the XML declaration, `xsl:analyze-string` regexes matching "", `validation="preserve"` |
| 2026-10-19 | 7,312 | 627 | 86 | 92.1% | Modes: `#current` and `xsl:next-match` follow the mode at run time, rules of several modes, `use-accumulators`; `xsl:copy` of document nodes in sequences, `document()` on sequences, json/adaptive principal results of `xsl:result-document` |
| 2026-10-19 | 7,255 | 684 | 86 | 91.4% | The html method's own serializer, `item-separator`, json/adaptive `xsl:result-document`; 3.0 pattern forms, errors in predicate patterns as non-matches |

| Suite | Feature | Pass | Fail | Not applicable |
|---|---|---|---|---|
| W3C `xslt30-test` | | | | |
| W3C `qt3tests` (XPath subset) | | | | |
