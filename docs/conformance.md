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

| Date | Passed | Failed | Not run | Pass rate |
|---|---:|---:|---:|---:|
| 2026-10-18 | 16,354 | 3,694 | 99 | 81.6% |
| 2026-10-18 | 18,524 | 1,524 | 99 | 92.4% |
| 2026-10-19 | 19,077 | 969 | 99 | 95.2% |
| 2026-10-19 | 19,222 | 822 | 99 | 95.9% |

The failures include the XPath 3.1 maps and arrays, which arrive in P3 with higher-order functions.

## XSLT 2.0 / 3.0

Not implemented yet.

| Suite | Feature | Pass | Fail | Not applicable |
|---|---|---|---|---|
| W3C `xslt30-test` | | | | |
| W3C `qt3tests` (XPath subset) | | | | |
