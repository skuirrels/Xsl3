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
| Debugger-only `XmlQueryRuntime.DebugSetGlobalValue` | Converts any value with the `item*` converter | Accepts only `XPathItem` values and sequences of them | The converter is internal to System.Private.Xml |

Xsl3 is forked from dotnet/runtime `main`, so it also carries upstream fixes that haven't shipped in a .NET
release yet; each one that changes output is listed above. No other output difference is known.
`tests/Xsl3.Differential` compares stock and Xsl3
byte for byte across the 1.0 corpus, including when the caller supplies its own `XmlWriter` (where Xsl3 writes
through the public `XmlWriter` API instead of stock's internal raw writer; see docs/internals.md).

## From the W3C specifications

None yet: Xsl3 implements no XSLT 2.0/3.0 features so far.
