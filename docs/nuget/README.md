# Xsl3

An XSLT 3.0, XSLT 2.0 and XPath 3.1 processor for .NET, built on the engine behind
`System.Xml.Xsl.XslCompiledTransform`: stylesheets are compiled to IL, and XSLT 1.0 stylesheets behave exactly
as they do with stock .NET.

> **1.3.0: named streamability extensions.** XSLT 1.0 and 2.0 and XPath 3.1 are implemented.
> XSLT 3.0 works apart from supported streaming. Schema awareness remains opt-in through `XsltSettings.SchemaAware`.

Version **1.3.0** adds `XsltSettings.StreamabilityExtensions` (default `true`), which names the five
extensions to the XSLT 3.0 streamability rules that strict analysis applies and turns them off for the
specification's analysis exactly (§19.10). Behaviour is unchanged by default. 1.2.0's union change remains:
a union of two striding operands is crawling (§19.8.8.4), so select `Fallback` or write the selection as one
striding step where a construct relied on the former extension.

Streaming remains experimental and `supports-streaming` remains `no`. `Streamability = Strict` controls
static checking and does not enable forward execution by itself. Published default execution stays
tree-based; forward execution requires the explicit development opt-in. The original streaming memory
and throughput gates remain unchanged and unmet; this release does not complete full P7.

An unreleased P7 integration candidate selects forward execution under default/explicit `Strict`
for declared-streamable reader, stream and URI inputs through both APIs. `Fallback` remains tree-based;
existing XDM trees and saved execution modes are preserved. This behavior is under qualification on
`feature/p7-public-streaming-selection` and is not part of the published 1.3.0 packages.

See the [1.3.0 release notes](https://github.com/skuirrels/Xsl3/blob/v1.3.0/docs/releases/1.3.0.md) and
[checkpoint boundary](https://github.com/skuirrels/Xsl3/blob/v1.3.0/docs/plan/p7-runtime-checkpoint.md).
Strict analysis rejects XSLT 1.0 instructions in declared-streamable content with `XTSE3430`, including
motionless instructions. Select `XsltStreamabilityMode.Fallback` when strict static checking is not intended.
Keep `Xsl3`, `Xsl3.Build` and `Xsl3.Cli` at version 1.3.0 and regenerate saved stylesheets with matching packages.

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

Conformance checked on 2026-10-29; Linux and Windows CI passed on .NET 8 and .NET 10. Performance figures below retain their original benchmark dates.

## What's supported

| | |
|---|---|
| XSLT 1.0 | Passes 1,654 applicable dotnet/runtime `XslCompiledTransform` tests on Linux and 1,661 on Windows per target framework and matches stock output byte for byte |
| XPath 3.1 | 21,884 W3C QT3 cases pass, none fail, 99 are not run |
| XSLT 3.0 | 8,850 W3C xslt30-test cases pass, 18 known failures, 41 not run (99.8% of those run) |
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
