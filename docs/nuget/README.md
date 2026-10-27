# Xsl3

An XSLT 3.0, XSLT 2.0 and XPath 3.1 processor for .NET, built on the engine behind
`System.Xml.Xsl.XslCompiledTransform`: stylesheets are compiled to IL, and XSLT 1.0 stylesheets behave exactly
as they do with stock .NET.

> **1.1.1: P7B analysis close-out.** XSLT 1.0 and 2.0 are complete. XPath 3.1 is too, including maps, arrays and
> higher-order functions. XSLT 3.0 works apart from streaming, including packages, `xsl:evaluate` (opt-in) and schema
> awareness (opt-in, `XsltSettings.SchemaAware`).

Streaming remains in development, with static analysis, forward execution and process probes. It is not
claimed as a supported package feature; normal execution remains tree-based and `supports-streaming` remains `no`.
P7B completes the approved reviewed analysis gates (1,000 posture/sweep cases and 155 static-diagnostic/result cases),
while retaining the original upstream results. Full runtime, memory and throughput acceptance remain deferred.
See the [1.1.1 release notes](https://github.com/skuirrels/Xsl3/blob/v1.1.1/docs/releases/1.1.1.md).

Strict analysis now rejects XSLT 1.0 instructions in declared-streamable content with `XTSE3430`, including
motionless instructions. Select `XsltStreamabilityMode.Fallback` explicitly when ordinary tree execution is intended.
Keep `Xsl3`, `Xsl3.Build` and `Xsl3.Cli` at version 1.1.1 and regenerate saved stylesheets with matching packages.

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
about 1.5× faster than stock. Output is the same, except for the strings `generate-id()` returns and the order between
nodes from different documents: compact trees come in the order they were built, so the input comes before the
documents `document()` loads.

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

Conformance checked on 2026-10-26. Performance figures below retain their original benchmark dates.

## What's supported

| | |
|---|---|
| XSLT 1.0 | Passes all 1,654 applicable dotnet/runtime `XslCompiledTransform` tests on each target framework and matches stock output byte for byte |
| XPath 3.1 | 21,884 W3C QT3 cases pass, none fail, 99 are not run |
| XSLT 3.0 | 8,849 W3C xslt30-test cases pass, 19 known failures, 41 not run (99.8% of those run) |
| Speed on XSLT 1.0 | 1.38× stock `XslCompiledTransform` (geometric mean, never slower); 1.49× on transforms from text with `UseCompactTree` |

XSLT 3.0 features include `xsl:try`, `xsl:iterate`, `xsl:merge`, accumulators, maps and arrays, text value
templates, `xsl:mode`, static parameters and shadow attributes, packages (`xsl:use-package`, visibility,
`xsl:override`), the JSON functions, and json and adaptive output.

Schema awareness is opt-in through `XsltSettings.SchemaAware`. Streaming is not yet a supported feature.

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
