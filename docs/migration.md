# Moving from System.Xml.Xsl

Xsl3 keeps the API of `System.Xml.Xsl.XslCompiledTransform`. For XSLT 1.0 it produces the same output as stock .NET,
byte for byte: `tests/Xsl3.Differential` checks that across the 1.0 corpus on every change.

## The change

```diff
- using System.Xml.Xsl;
+ using Xsl3;
```

`XslCompiledTransform`, `XsltArgumentList`, `XsltSettings`, `XsltException`, `XsltCompileException` and
`XsltMessageEncounteredEventArgs` have the same members. Also change:

- `catch (System.Xml.Xsl.XsltException)` to `catch (Xsl3.XsltException)`: Xsl3's exceptions are its own types.
- `XslTransform` (obsolete since .NET 2.0) isn't provided; move to `XslCompiledTransform`.
- `System.Xml.Xsl.XsltContext` stays where it is: it belongs to `System.Xml`'s XPath 1.0 engine, not to XSLT.

`msxsl:script` throws `PlatformNotSupportedException`, as it does on .NET (Core). Extension objects
(`XsltArgumentList.AddExtensionObject`) work as before; `XsltSettings.EnableExtensionFunctions = false` turns them off.

[docs/divergences.md](divergences.md) lists every known difference from stock; there are a few, all fixes of stock
bugs.

## What you gain without changing stylesheets

- **Speed.** XSLT 1.0 runs about 1.4× faster than stock on average ([docs/performance.md](performance.md)).
  `UseCompactTree = true`, set before `Load`, parses inputs into a tree that takes about 60% of `XPathDocument`'s memory;
  the only visible difference is the strings `generate-id()` returns.
- **Limits.** `XsltSettings.Timeout`, `MaxRecursionDepth`, `MaxOutputBytes` and `XsltArgumentList.CancellationToken`
  stop runaway transformations with an error instead of hanging or crashing the process; `UseDedicatedThread` gives
  deeply recursive stylesheets a large stack.

## Moving stylesheets to XSLT 3.0

Change `version="1.0"` to `version="3.0"` and the stylesheet runs on the XSLT 3.0 processor. Things to know:

- **XPath 2.0+ is stricter about types.** `'1' = 1` is a type error in 2.0 (it was true in 1.0); an empty
  sequence in arithmetic gives an empty sequence, not NaN; `xsl:value-of` outputs every item of a sequence, separated
  by spaces, not just the first. Regions marked `version="1.0"` keep the 1.0 rules (backwards-compatible mode), and
  `XsltSettings.VersionMode = XsltVersionMode.BackwardsCompatible` runs a whole `version="1.0"` stylesheet on the 3.0
  processor that way.
- **Parameters keep their .NET types.** In a 2.0+ stylesheet an `int` passed with `AddParam` is an `xs:integer`, a
  `DateTime` an `xs:dateTime`, a dictionary a map ([README](../README.md#parameters-of-xslt-20-and-30-stylesheets)). In
  1.0 every number was a double and a `DateTime` a string.
- **Reaching outside is opt-in.** `doc()`, `unparsed-text()` and `json-doc()` need `EnableDocumentFunction`, like
  `document()`; `xsl:result-document` writing files needs `EnableResultDocument` and a `BaseOutputUri`;
  `xsl:evaluate` needs `EnableEvaluate`.
- **Result trees are sequences.** `TransformToItems` is internal; to get the result as values rather than markup, use
  the 3.0 API ([docs/getting-started.md](getting-started.md)), whose `CallTemplate`, `ApplyTemplates` and
  `CallFunction` return `XdmValue`s.
- **Grouping, sorting and keys.** `xsl:for-each-group` replaces Muenchian grouping, and `xsl:sort` sorts by code
  point unless a collation is named; the 3.0 rewrites of the classic 1.0 benchmarks run faster on Xsl3 than the 1.0
  originals do on stock.

## Ahead-of-time compilation

Stock `xsltc.exe` and `Load(Type)` exist for .NET Framework only. Xsl3's `CompileToAssembly` (on .NET 9+) and the
`Xsl3.Build` MSBuild targets save compiled stylesheets as assemblies that `Load(Type)` loads without compiling, also
under NativeAOT.
