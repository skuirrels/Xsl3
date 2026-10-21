# Xsl3

An XSLT 3.0, XSLT 2.0 and XPath 3.1 processor for .NET, built on the engine behind
`System.Xml.Xsl.XslCompiledTransform`: stylesheets are compiled to IL, and XSLT 1.0 stylesheets behave exactly
as they do with stock .NET.

> **Alpha.** XSLT 1.0 and 2.0 are complete. XPath 3.1 is too, including maps, arrays and higher-order
> functions. Most of XSLT 3.0 works, including packages and `xsl:evaluate` (opt-in); schema awareness and streaming don't yet.
> The API may change before 1.0.

## Getting started

Xsl3 keeps `XslCompiledTransform`'s API, so switching from `System.Xml.Xsl` is a `using` change:

```csharp
using Xsl3;

var xslt = new XslCompiledTransform();
xslt.Load("style.xsl");
xslt.Transform("input.xml", "output.html");
```

For extra speed on XSLT 1.0, new in 0.4.0-alpha, set `UseCompactTree` before `Load`:

```csharp
var xslt = new XslCompiledTransform { UseCompactTree = true };
```

The input is then parsed into Xsl3's compact tree, which takes about 60% of `XPathDocument`'s memory. Transforms run
about 1.5× faster than stock. Output is the same, except for the strings `generate-id()` returns.

`XsltArgumentList`, `XsltSettings`, extension objects and parameters work as they do in `System.Xml.Xsl`.
The `TextWriter` and `Stream` overloads of `Transform` serialize the way the stylesheet's `xsl:output` says,
including XHTML, HTML5, JSON and the adaptive method.

Targets .NET 8 and .NET 10.

## What's supported

| | |
|---|---|
| XSLT 1.0 | Passes dotnet/runtime's `XslCompiledTransform` tests (1661/1661) and matches stock output byte for byte |
| XPath 3.1 | 99.6% of the W3C QT3 tests that apply |
| XSLT 2.0 / 3.0 | 7,738 of the 7,907 applicable W3C xslt30-test cases pass (97.9%); 98.7% of the XSLT 2.0 profile |
| Speed on XSLT 1.0 | 1.38× stock `XslCompiledTransform` (geometric mean, never slower); 1.49× on transforms from text with `UseCompactTree` |

XSLT 3.0 features include `xsl:try`, `xsl:iterate`, `xsl:merge`, accumulators, maps and arrays, text value
templates, `xsl:mode`, static parameters and shadow attributes, packages (`xsl:use-package`, visibility,
`xsl:override`), the JSON functions, and json and adaptive output.

Not yet: schema awareness and streaming.

## Secure by default

What a stylesheet can reach beyond its input is opt-in, through `XsltSettings`:

| Setting | Allows | Default |
|---|---|---|
| `EnableDocumentFunction` | `document()`, `doc()`, `unparsed-text()`, `json-doc()`, `xsl:source-document` | off |
| `EnableEvaluate` | `xsl:evaluate` (XTDE3175 when off) | off |
| `EnableResultDocument` | `xsl:result-document` files, only under `XslCompiledTransform.BaseOutputUri` | off |
| `EnableEnvironmentVariables` | `fn:environment-variable` | off |
| `EnableAssertions` | `xsl:assert` | off |
| `Timeout`, `XsltArgumentList.CancellationToken` | stop a transformation | none |
| `UseDedicatedThread`, `DedicatedThreadStackSize` | deep recursion on a large stack | off, 64 MB |

`XsltSettings.TrustedXslt` turns on the document functions, scripts and `xsl:evaluate`.

## Performance

Xsl3 is faster than stock `XslCompiledTransform` on XSLT 1.0, with byte-identical output. Set
`UseCompactTree = true` before `Load` to have a 1.0 stylesheet parse its input into Xsl3's compact tree. That tree
takes about 60% of `XPathDocument`'s memory. XSLT 2.0+ stylesheets always use it, for their input and for their
temporary trees. Details: https://github.com/skuirrels/Xsl3/blob/main/docs/performance.md

## Links

- Source, issues and roadmap: https://github.com/skuirrels/Xsl3
- Conformance details: https://github.com/skuirrels/Xsl3/blob/main/docs/conformance.md
- Differences from `System.Xml.Xsl`: https://github.com/skuirrels/Xsl3/blob/main/docs/divergences.md

MIT licensed. Xsl3 is an independent project and isn't affiliated with or endorsed by Microsoft or the
.NET Foundation.
