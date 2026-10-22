# Xsl3

An XSLT 3.0, XSLT 2.0 and XPath 3.1 processor for .NET, built on the engine behind
`System.Xml.Xsl.XslCompiledTransform`: stylesheets are compiled to IL, and XSLT 1.0 stylesheets behave exactly
as they do with stock .NET.

> **1.0.1.** XSLT 1.0 and 2.0 are complete. XPath 3.1 is too, including maps, arrays and
> higher-order functions. XSLT 3.0 works apart from streaming, including packages, `xsl:evaluate` (opt-in) and schema
> awareness (opt-in, `XsltSettings.SchemaAware`).

Streaming development has begun with static analysis, strict/fallback compiler checks and W3C development profiles. Streamed execution
is still outstanding; this does not change the package's supported features.

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

### The XSLT 3.0 API

`XsltCompiler`, `XsltExecutable` and `Xslt30Transformer` start a stylesheet at a mode, a named template or a function,
take and return XDM values (`XdmNode`, `XdmAtomicValue`, `XdmMap`, `XdmArray`), and give per-run handlers for
messages and secondary results:

```csharp
XsltExecutable executable = new XsltCompiler().Compile(new Uri("file:///styles/report.xsl"));
XdmValue rows = executable.CreateTransformer().CallTemplate(new QName("rows"));
```

Guides: [getting started](https://github.com/skuirrels/Xsl3/blob/main/docs/getting-started.md) and
[moving from System.Xml.Xsl](https://github.com/skuirrels/Xsl3/blob/main/docs/migration.md).

## What's supported

| | |
|---|---|
| XSLT 1.0 | Passes all 1,654 applicable dotnet/runtime `XslCompiledTransform` tests on each target framework and matches stock output byte for byte |
| XPath 3.1 | 21,884 W3C QT3 cases pass, none fail, 99 are not run |
| XSLT 3.0 | 8,845 W3C xslt30-test cases pass, 22 known failures, 42 not run (99.8% of those run) |
| Speed on XSLT 1.0 | 1.38× stock `XslCompiledTransform` (geometric mean, never slower); 1.49× on transforms from text with `UseCompactTree` |

XSLT 3.0 features include `xsl:try`, `xsl:iterate`, `xsl:merge`, accumulators, maps and arrays, text value
templates, `xsl:mode`, static parameters and shadow attributes, packages (`xsl:use-package`, visibility,
`xsl:override`), the JSON functions, and json and adaptive output.

Schema awareness is opt-in through `XsltSettings.SchemaAware`. Streamed execution is not yet implemented.

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
