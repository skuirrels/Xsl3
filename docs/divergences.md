# Divergences

Every intentional difference between Xsl3 and stock `System.Xml.Xsl.XslCompiledTransform` (for XSLT 1.0), or
between Xsl3 and the W3C specifications (for 2.0/3.0), is listed here (SPEC §6.2). An unlisted difference is
a bug.

## From stock XslCompiledTransform

| Area | Stock | Xsl3 | Why |
|---|---|---|---|
| Namespace | `System.Xml.Xsl` | `Xsl3` | Callers change their `using`. Exceptions are `Xsl3.XsltException` and friends, so `catch (System.Xml.Xsl.XsltException)` must change too. |
| `XslTransform` | Present, obsolete | Not provided | Obsolete since .NET 2.0 (SPEC §2.2) |
| `XsltContext` | `System.Xml.Xsl.XsltContext` | Not provided | Only used with the stock XPath 1.0 engine's `XPathExpression.SetContext`; keep using `System.Xml.Xsl.XsltContext` for that |
| `msxsl:script` | Throws `PlatformNotSupportedException` | Same | Unchanged; listed for completeness (SPEC §2.2) |
| `ms:format-date` / `ms:format-time` with an empty picture string | .NET 8 and 10 use the culture's general format, so `format-date` also prints the time | The culture's short date (`d`) or long time (`T`) format | Inherited from upstream `main`, which fixed this after .NET 10 shipped (dotnet/runtime#115455); matches .NET 11. Allowlisted in `tests/Xsl3.Differential`. |
| A namespace declaration on an element named in `cdata-section-elements` | Its value is written as a CDATA section (`xmlns:p="<![CDATA[urn:p]]>"`), which isn't well-formed | Written as an attribute value | A bug in the stock writer's handling of namespace declarations written in chunks (`QueryOutputWriter`) |
| `doctype-system` containing a quotation mark | Written in quotation marks, so the doctype isn't well-formed | Written in apostrophes | A fix in the raw writers |
| Debugger-only `XmlQueryRuntime.DebugSetGlobalValue` | Converts any value with the `item*` converter | Accepts only `XPathItem` values and sequences of them | The converter is internal to System.Private.Xml |

Xsl3 is forked from dotnet/runtime `main`, so it also carries upstream fixes that haven't shipped in a .NET
release yet; each one that changes output is listed above, with the fixes Xsl3 makes itself. No other output
difference is known.
`tests/Xsl3.Differential` compares stock and Xsl3
byte for byte across the 1.0 corpus, including when the caller supplies its own `XmlWriter` (where Xsl3 writes
through the public `XmlWriter` API instead of stock's internal raw writer; see docs/internals.md).

## From the W3C specifications

Xsl3 is a basic XSLT 3.0 processor (XSLT 3.0 §27) with the serialization, higher-order functions and dynamic
evaluation features, and XPath 3.1. This section lists where it departs from the specifications, the optional
features it lacks, and its implementation-defined choices. `tests/Xsl3.W3C/Xslt30/Profile.cs` encodes the same
choices for the W3C suites.

### Features not provided

| Feature | Behaviour | Plan |
|---|---|---|
| Schema-awareness (XSLT 3.0 §27.3) | `xsl:import-schema` raises XTSE1650; `validation="strict"`/`"lax"` and `type` raise XTSE1660; `schema-element()` and `schema-attribute()` raise XPST0008 | P6 |
| Streaming (§27.4) | `streamable="yes"` is checked and otherwise ignored: the input is read into a tree | P7 |
| Backwards-compatible behaviour (§3.9) | claimed: `version="1.0"` regions of 2.0+ stylesheets run in backwards-compatible mode, and whole 1.0 stylesheets do with `XsltSettings.VersionMode = BackwardsCompatible`; by default (`Legacy`) a 1.0 stylesheet runs on the XSLT 1.0 engine, byte for byte as stock. Gaps: an `xsl:key` defined in both modes (backwards-043), `exsl:document` | remaining-scope 4.6 |
| `fn:transform` | not available (XPST0017) | remaining-scope 5 |
| `fn:random-number-generator` | the unseeded generator is seeded once per transformation, so its numbers differ between runs | |
| XML 1.1 | input and output are XML 1.0 (.NET's `XmlReader` doesn't read XML 1.1) | not planned |
| `xsl:stylesheet` found through an `<?xml-stylesheet?>` processing instruction | not supported | remaining-scope 4.6 |

### Implementation-defined choices

| Area | Xsl3's choice |
|---|---|
| `xsl:use-package` with a version range (§3.5.2) | the highest version in range |
| Template conflicts (`on-multiple-match` absent or `use-last`) | recovery: the rule last in document order |
| `xsl:evaluate` (§10.4.4) | disabled unless `XsltSettings.EnableEvaluate`; then statically enabled; target expressions run in the transformation's execution scope but parse documents again (node identity differs from the stylesheet's) |
| Secondary results (`xsl:result-document`, §25) | refused unless `XsltSettings.EnableResultDocument`; then written as files only under the base output URI |
| `fn:environment-variable` | no variables unless `XsltSettings.EnableEnvironmentVariables` |
| `xsl:assert` | ignored unless `XsltSettings.EnableAssertions`; a computed `error-code` isn't supported (XTSE0020) |
| Default language, numbering languages | `en` (also `de` month and day names); others fall back to English |
| Calendars | AD and ISO |
| Year range | above 9999 and negative years, up to ±(2^31 − 1) (FODT0001 beyond); no year zero (XSD 1.0) |
| Errors known statically | an unknown literal `collation` on `xsl:sort` (XTDE1035) is reported when the stylesheet is compiled, as XSLT 3.0 §2.14 allows |
| Document access (`fn:doc`, `fn:unparsed-text`, `fn:json-doc`, `xsl:source-document`) | refused unless `XsltSettings.EnableDocumentFunction`, as `document()` is; then through the transformation's resolver |
| XSD version | 1.0 |
| `xs:decimal` | .NET `decimal`: 28–29 significant digits; `xs:integer` is unbounded |
| Implicit timezone | the local timezone |
| Collations | the codepoint collation, the HTML ASCII case-insensitive collation, and UCA collations (`http://www.w3.org/2013/collation/UCA?…`) mapped onto .NET's culture-aware comparison; other URIs raise FOCH0002 |
| Regular expression character classes | .NET's Unicode tables, newer than the versions the suite's `regex-classes` (Unicode 6.0) and `unicode-90` (Unicode 9.0) sets expect |
| Unicode normalization forms | NFC, NFD, NFKC, NFKD, `fully-normalized` (as NFC, with SERE0012 for text starting with a combining character), `none` |
| Empty elements, xml and xhtml methods | written `<a />` (.NET's writer), not `<a/>`; five tests match the serialization against `<a/>` exactly (attribute-1101, namespace-2608, output-0285, output-0311, QT3 serialize-xml-120b) |
| Quotation marks in xhtml attribute values | `&#34;` |
| Adaptive method | each node with an XML declaration unless `omit-xml-declaration="yes"` (Serialization 3.1 §10.1.6); `fn:serialize` defaults to `yes` |
| Default output method | `html` for a first element `html` in no namespace; `xhtml` for XHTML's `html` element; otherwise `xml` |
| `disable-output-escaping` | honoured for text that reaches a result by sequence normalization; dropped by `xsl:copy-of` and in temporary trees and attributes |
| `doc()` and `document()` | DTDs are refused unless the input reader allows them |
| Deep recursion | a stack probe raises `Q{http://xsl3/err}recursion-limit` before the stack overflows; `XsltSettings.UseDedicatedThread` gives a larger stack |
