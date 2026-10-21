# Getting started with Xsl3

Xsl3 runs XSLT 1.0, 2.0 and 3.0 stylesheets and XPath 3.1 on .NET 8 and .NET 10. It has two APIs:

- **`XslCompiledTransform`**, the API of `System.Xml.Xsl`. Existing code moves over with a `using` change, and XSLT 1.0
  stylesheets give byte-identical output ([docs/migration.md](migration.md)).
- **The XSLT 3.0 API**: `XsltCompiler`, `XsltExecutable` and `Xslt30Transformer`, with XDM values. Use it to start a
  stylesheet at a named template, a mode or a function, to pass and receive maps, arrays and sequences, and to handle
  messages and secondary results.

```
dotnet add package Xsl3 --prerelease
```

## Compile once, run many times

```csharp
using Xsl3;
using Xsl3.Xdm;

var compiler = new XsltCompiler();
XsltExecutable report = compiler.Compile(new Uri("file:///styles/report.xsl"));
```

An `XsltExecutable` is immutable and thread-safe: compile it once and keep it. A compile error is an
`XsltStaticException`; its `Errors` list every error with its code, module, line and column.

Each run uses an `Xslt30Transformer`. Transformers are cheap; use one per thread.

```csharp
Xslt30Transformer transformer = report.CreateTransformer();
transformer.StylesheetParameters[new QName("title")] = new XdmAtomicValue("Quarterly report");

using FileStream input = File.OpenRead("orders.xml");
XdmNode orders = XdmNode.Load(input, "file:///data/orders.xml");
using FileStream output = File.Create("report.html");
transformer.ApplyTemplates(orders, output); // serialized as the stylesheet's xsl:output says
```

## Entry points

| Call | Starts with |
|---|---|
| `ApplyTemplates(selection, …)` | templates applied to any sequence, in the default mode or a named one (`Xslt30Transformer.UnnamedMode` for `#unnamed`) |
| `CallTemplate(name, …)` | a named template; `null` is `xsl:initial-template` |
| `CallFunction(name, args)` | a public stylesheet function |

`ApplyTemplates` and `CallTemplate` write to a `Stream`, `TextWriter` or `XmlWriter`, or return the result sequence as
an `XdmValue` when called without a destination. Initial template and mode parameters, tunnel ones too, are
dictionaries of `QName` to `XdmValue`:

```csharp
XdmValue rows = transformer.CallTemplate(new QName("rows"),
    parameters: new Dictionary<QName, XdmValue> { [new QName("limit")] = new XdmAtomicValue(10L) },
    tunnelParameters: new Dictionary<QName, XdmValue> { [new QName("currency")] = new XdmAtomicValue("EUR") });

var totals = (XdmMap)transformer.CallFunction(new QName("urn:report", "totals"), rows);
foreach (var (key, value) in totals.Entries)
{
    Console.WriteLine($"{key}: {value}");
}
```

`GlobalContextItem` sets the context item of global variables, and of a named template or function the run starts
with. Applying templates to a single node makes that node the global context item too.

## Values

| Type | Holds |
|---|---|
| `XdmValue` | a sequence of items; `XdmValue.MakeValue(object)` converts .NET values |
| `XdmNode` | a node; `XdmNode.Load` parses into Xsl3's compact tree, `new XdmNode(navigator)` wraps any `XPathNavigator` |
| `XdmAtomicValue` | an atomic value, with its type (`TypeName`) and .NET form (`Value`); `new XdmAtomicValue("2026-10-21", xs:date)` constructs any built-in type |
| `XdmMap`, `XdmArray` | maps and arrays, immutable |
| `XdmFunctionItem` | a function item (its name and arity) |

## Handlers and settings

```csharp
transformer.MessageHandler = text => logger.LogInformation("{Message}", text);
transformer.TraceHandler = (value, label) => logger.LogDebug("{Label}: {Value}", label, value);
transformer.BaseOutputUri = new Uri("file:///out/report/main.html");
transformer.ResultDocumentHandler = uri => new StreamWriter(Path.Combine("out", Path.GetFileName(uri.LocalPath)));
transformer.CancellationToken = cancellation.Token;
```

What a stylesheet can reach is off unless `XsltSettings` turns it on: `document()` and `doc()`
(`EnableDocumentFunction`), `xsl:evaluate` (`EnableEvaluate`), secondary result files (`EnableResultDocument`, confined
to the base output URI's directory), environment variables (`EnableEnvironmentVariables`). `Timeout`,
`MaxRecursionDepth` and `MaxOutputBytes` bound a run. Set them on `XsltCompiler.Settings`.

## Errors

A dynamic error is an `XsltDynamicException` with the error's `Code` (a `QName`: `err:XTDE0640`, a stylesheet's own
from `fn:error`, or `Q{http://xsl3/err}cancelled`, `recursion-limit`, `output-limit`, `extension-error`), its
`Description` and `ErrorObject`. Its `StylesheetStack` lists the templates and functions that were running, innermost
first. Without tracing it is read from the .NET stack and leaves out functions that were inlined or tail-called; with
`XsltSettings.EnableTracing` it is complete, and an `IXsltTraceListener` sees each start and end.

## Extension functions

```csharp
compiler.RegisterExtensionFunction(new QName("urn:acme", "vat"), 1,
    args => new XdmAtomicValue((decimal)((XdmAtomicValue)args[0][0]).Value * 1.2m));
```

A stylesheet calls it as `acme:vat($price)`, and `function-available('acme:vat', 1)` is true. An exception it throws is
`Q{http://xsl3/err}extension-error`, which `xsl:try` can catch.

## Packages and static parameters

```csharp
compiler.Packages.Add(new Uri("file:///styles/common.xsl")); // its xsl:package name and package-version
compiler.StaticParameters[new QName("debug")] = new XdmAtomicValue(false);
```

## Ahead of time and NativeAOT

`XslCompiledTransform.CompileToAssembly` saves a compiled stylesheet as an assembly, and `Load(Type, XsltSettings)` loads
it without compiling, which works under NativeAOT. The `Xsl3.Build` targets do this at build time for
`<XsltStylesheet Include="report.xsl" TypeName="Stylesheets.Report" />` items; see `src/Xsl3.Build/README.md` and
`samples/Xsl3.AotSample`. A compiled stylesheet runs only on the version of Xsl3 that compiled it; `Load(Type)` refuses
another.

## The command line

`src/Xsl3.Cli` is the `xsl3` tool: `xsl3 transform`, `xsl3 compile` and `xsl3 explain` (the optimized QIL a
stylesheet compiles to).
