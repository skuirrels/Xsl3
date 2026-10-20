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
| 2026-10-20 | 21,743 | 141 | 99 | 99.4% | Focus-dependent function references with arguments (`fn:lang#1`, `fn:id#1`) keep the focus |
| 2026-10-20 | 21,746 | 138 | 99 | 99.4% | `map:merge`, `map:put` and `array:` functions copy the reused one-item lists the generated code passes them |
| 2026-10-21 | 21,796 | 88 | 99 | 99.6% | `fn:random-number-generator`; `fn:function-lookup` type-checks its arguments |

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
| 2026-10-20 | 7,549 | 386 | 86 | 95.1% | `fn:copy-of`/`fn:snapshot` copies keep each element's own namespace declaration; `xsl:namespace` raises XTDE0920, XTDE0925, XTDE0905 and XTDE0930, and the result tree XTDE0440 and XTDE0430, without relying on the serializer |
| 2026-10-21 | 7,606 | 299 | 49 | 96.2% | The `regex-classes` set no longer applies: it declares Unicode 6.0, and Xsl3's regex classes use .NET's newer tables (docs/divergences.md) |
| 2026-10-21 | 7,695 | 330 | 49 | 95.9% | Remaining scope (docs/plan/remaining-scope.md): initial functions with raw result delivery (37 tests run that didn't), `xsl:assert` (run with assertions on and off as each test asks), the xhtml default method on the direct result path, `current-output-uri()` absent outside templates; bug-1901 not run (it compares a tree the xhtml serialization can't match) |
| 2026-10-20 | 7,642 | 335 | 86 | 95.8% | P5 long tail: XTDE1480 for `xsl:result-document` in templates called in temporary output state (the tunnel frame marks it), XML declarations for nodes in adaptive output (Serialization 3.1 §10.1.6) |
| 2026-10-20 | 7,637 | 340 | 86 | 95.7% | P5 M2–M4: character maps (before normalization; not in CDATA, escaped URI attributes or namespace declarations; adaptive output), `parameter-document`, output declarations merged by import precedence, the xhtml `meta` element replacing the stylesheet's, cdata-section-elements written by Xsl3 (namespace declarations no longer CDATA), HTML5 prefix normalization, carriage returns as `&#xD;`, SERE0012, json-node-output-method html/xhtml, `disable-output-escaping` through sequences |
| 2026-10-20 | 7,593 | 384 | 86 | 95.2% | P5 (docs/plan/p5-evaluate.md) M1: `xsl:evaluate`, with `dynamic_evaluation` claimed (the 42 `evaluate` tests that apply, and `try-027`, `system-property-014a`, now run; `error-3175a` and `system-property-014b`, which need the feature absent, no longer apply); every unit with function items has a dispatcher |
| 2026-10-20 | 7,545 | 390 | 86 | 95.1% | P3 exit. `unparsed-entity-uri` and `unparsed-entity-public-id` (1 and 2 arguments, from the document's DTD), focus kept by `unparsed-entity-uri#1` and friends, attribute checks (XTSE0090 for `visibility`, `package-version`, `name` and `declared-modes` where they don't belong), `required="no"` on function parameters, rule-less accumulators, `xsl:fallback` order in `xsl:analyze-string`, `current-merge-group()`/`current-merge-key()` in patterns |
| 2026-10-19 | 7,512 | 423 | 86 | 94.7% | `copy-accumulators` and accumulator values of `fn:copy-of`/`fn:snapshot` copies, composite keys, `xsl:global-context-item`, XTMM9000, merged `xsl:decimal-format` declarations and `exponent-separator` |
| 2026-10-19 | 7,479 | 456 | 86 | 94.3% | Override checks (XTSE3060, XTSE3070), package-local accumulators, `fn:snapshot` of attributes, the built-in rule for arrays, declared signatures for references to stylesheet functions, CDATA sections and unencodable characters, SERE0015. The four tests that need the lowest package version are not applicable (Xsl3 picks the highest). |
| 2026-10-19 | 7,461 | 478 | 86 | 94.0% | Sequence normalization (zero-length text nodes from `xsl:text`, text value templates as simple content, the comment/PI separator, document copies in sequences, arrays flattened in content), `xsl:where-populated` emptiness, `xsl:next-match` from named templates, JSON character maps, empty doctype identifiers |
| 2026-10-19 | 7,404 | 535 | 86 | 93.3% | `xsl:number start-at`, required stylesheet parameters (XTDE0050), XTSE0270, XTSE1660 for `type` and strict/lax validation |
| 2026-10-19 | 7,389 | 550 | 86 | 93.1% | Packages: decimal formats, keys and namespace aliases are local to a package; abstract attribute sets (XTDE3052); what `xsl:override` may contain |
| 2026-10-19 | 7,365 | 574 | 86 | 92.8% | `fn:current-output-uri` and the base output URI, xhtml with HTML5, serialization parameter errors, indentation after the XML declaration, `xsl:analyze-string` regexes matching "", `validation="preserve"` |
| 2026-10-19 | 7,312 | 627 | 86 | 92.1% | Modes: `#current` and `xsl:next-match` follow the mode at run time, rules of several modes, `use-accumulators`; `xsl:copy` of document nodes in sequences, `document()` on sequences, json/adaptive principal results of `xsl:result-document` |
| 2026-10-19 | 7,255 | 684 | 86 | 91.4% | The html method's own serializer, `item-separator`, json/adaptive `xsl:result-document`; 3.0 pattern forms, errors in predicate patterns as non-matches |

### P5 exit sets

docs/plan/p5-evaluate.md defines the exit: these five sets ≥ 98% together, none below 95%, with `dynamic_evaluation`
claimed. Measured on 2026-10-20 (Release, .NET 10):

| Set | Passed | Failed | Pass rate | Before P5 |
|---|---:|---:|---:|---:|
| `evaluate` | 42 | 0 | 100.0% | not run |
| `output` | 218 | 3 | 98.6% | 89.6% |
| `character-map` | 29 | 0 | 100.0% | 82.8% |
| `result-document` | 140 | 0 | 100.0% | 90.7% |
| `disable-output-escaping` | 25 | 0 | 100.0% | 64.0% |
| **Together** | **454** | **3** | **99.3%** | 88.0% |

The three failures: `<a />` rather than `<a/>` (output-0285, -0311) and the xhtml default method on the direct
result path (output-0130), both from P4's fast path to .NET's writer. The 2.0 profile went from 96.3% to 96.7%
(4,439 passed, 151 failed).

| Suite | Feature | Pass | Fail | Not applicable |
|---|---|---|---|---|
| W3C `xslt30-test` | | | | |
| W3C `qt3tests` (XPath subset) | | | | |
