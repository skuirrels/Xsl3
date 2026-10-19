# Xsl3

An XSLT 3.0, XSLT 2.0 and XPath 3.1 processor for .NET, built on the engine behind
`System.Xml.Xsl.XslCompiledTransform`: stylesheets are compiled to IL, and XSLT 1.0 stylesheets behave exactly
as they do with stock .NET.

> **Alpha.** XSLT 1.0 and 2.0 are complete. XPath 3.1 is too, including maps, arrays and higher-order
> functions. Most of XSLT 3.0 works, including packages; `xsl:evaluate`, schema awareness and streaming don't yet.
> The API may change before 1.0.

## Getting started

Xsl3 keeps `XslCompiledTransform`'s API, so switching from `System.Xml.Xsl` is a `using` change:

```csharp
using Xsl3;

var xslt = new XslCompiledTransform();
xslt.Load("style.xsl");
xslt.Transform("input.xml", "output.html");
```

`XsltArgumentList`, `XsltSettings`, extension objects and parameters work as they do in `System.Xml.Xsl`.
The `TextWriter` and `Stream` overloads of `Transform` serialize the way the stylesheet's `xsl:output` says,
including XHTML, HTML5, JSON and the adaptive method.

Targets .NET 8 and .NET 10.

## What's supported

| | |
|---|---|
| XSLT 1.0 | Passes dotnet/runtime's `XslCompiledTransform` tests (1661/1661) and matches stock output byte for byte |
| XPath 3.1 | 99.3% of the W3C QT3 tests that apply |
| XSLT 2.0 / 3.0 | 7,389 of the 7,939 applicable W3C xslt30-test cases pass (93.1%) |

XSLT 3.0 features include `xsl:try`, `xsl:iterate`, `xsl:merge`, accumulators, maps and arrays, text value
templates, `xsl:mode`, static parameters and shadow attributes, packages (`xsl:use-package`, visibility,
`xsl:override`), the JSON functions, and json and adaptive output.

Not yet: `xsl:evaluate`, schema awareness and streaming.

## Links

- Source, issues and roadmap: https://github.com/skuirrels/Xsl3
- Conformance details: https://github.com/skuirrels/Xsl3/blob/main/docs/conformance.md
- Differences from `System.Xml.Xsl`: https://github.com/skuirrels/Xsl3/blob/main/docs/divergences.md

MIT licensed. Xsl3 is an independent project and isn't affiliated with or endorsed by Microsoft or the
.NET Foundation.
