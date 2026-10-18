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

## XSLT 2.0 / 3.0, XPath 3.1 evaluation

Not implemented yet.

| Suite | Feature | Pass | Fail | Not applicable |
|---|---|---|---|---|
| W3C `xslt30-test` | | | | |
| W3C `qt3tests` (XPath subset) | | | | |
