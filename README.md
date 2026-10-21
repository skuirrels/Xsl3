# Xsl3

[![NuGet](https://img.shields.io/nuget/vpre/Xsl3.svg)](https://www.nuget.org/packages/Xsl3)
[![NuGet downloads](https://img.shields.io/nuget/dt/Xsl3.svg)](https://www.nuget.org/packages/Xsl3)

**An XSLT 2.0 / 3.0 processor for .NET, forked from `System.Xml.Xsl`.**

Xsl3 starts from the engine behind .NET's `XslCompiledTransform` (stylesheets compiled to IL through the QIL
intermediate representation) and is growing it into a conformant XSLT 3.0 / XPath 3.1 processor that stays
faster than stock on XSLT 1.0 and aims to be clearly faster than SaxonCS on 2.0/3.0.
The full design is in [SPEC.md](SPEC.md).

> **Status: alpha.** XSLT 1.0 behaves exactly as stock `System.Xml.Xsl`; XSLT 2.0 is complete; XPath 3.1 with maps,
> arrays and higher-order functions is in; XSLT 3.0 works apart from schema awareness and streaming.
> Preview builds are published to NuGet as prereleases of [Xsl3](https://www.nuget.org/packages/Xsl3) (see the
> [releases](https://github.com/skuirrels/Xsl3/releases)); the latest is 0.8.0-alpha. See [what works](#what-works) and
> [what's outstanding](#whats-outstanding).

## What works

Measured on 2026-10-21 (Release build, .NET 10); details in [docs/conformance.md](docs/conformance.md).

| Gate | Result |
|---|---|
| Upstream `XslCompiledTransform` tests (the XSLT 1.0 regression gate) | 1654 / 1654 on .NET 8 and .NET 10 (of 1661: 7 platform-specific or known upstream issues are filtered) |
| Differential: byte-for-byte against stock `System.Xml.Xsl` over the 1.0 corpus | 394 / 394 on .NET 8 and .NET 10 |
| W3C QT3 (XPath 3.1 / F&O 3.1, higher-order functions claimed) | 21,884 passed, 0 failed (100%) |
| W3C xslt30-test, XSLT 2.0 profile (P2 exit: ≥ 95%) | 4,588 passed, 9 failed (99.8%) |
| W3C xslt30-test, XSLT 3.0 profile (P3 exit: ≥ 95%; Hardening: ≥ 99%) | 8,012 passed, 24 failed, 49 not run (99.7%) |
| xslt30-test sets for `xsl:evaluate` and serialization (P5 exit: ≥ 98%) | 454 of 457 (99.3%): `evaluate` 42 / 42, `output` 218 / 221, `character-map` 29 / 29, `result-document` 140 / 140, `disable-output-escaping` 25 / 25 |
| XSLT 1.0 speed against stock (P4 exit: ≥ 1.2×, never slower) | 1.38× geometric mean, 1.11× to 2.2× per benchmark, html-report 206× ([docs/performance.md](docs/performance.md)) |
| XSLT 3.0 rewrites of the 1.0 benchmarks against the 1.0 originals on stock (target ≤ 1×) | 0.71× to 0.96× ([docs/performance.md](docs/performance.md)) |
| Real stylesheets: TEI Stylesheets (`eng/fetch-bench.sh`) against the TEI test suite's expected output | HTML (2 documents), LaTeX (4) and text (1): the same text, generation dates aside |

- **XSLT 1.0**: the upstream engine, unchanged in behaviour.
- **XPath 3.1 and F&O 3.1**: the XDM type system, all atomic types and casts, sequences, `for`/`let`/
  quantified expressions, the function library (strings, regex, dates and times, formatting, collations,
  QNames, URIs, math), maps, arrays, inline functions, named function references, partial application,
  dynamic calls, the arrow and simple map operators, lookups, the higher-order functions, and the JSON
  functions (`parse-json`, `json-doc`, `json-to-xml`, `xml-to-json`).
- **XSLT 2.0**: temporary trees, `as` types on variables, parameters and functions, `xsl:function`,
  `xsl:for-each-group`, `xsl:analyze-string`, `xsl:perform-sort`, `xsl:sequence`, `xsl:next-match`,
  `xsl:result-document` with serialization parameters, character maps, the `xhtml` method, tunnel
  parameters, multiple modes, keys with collations, `use-when`.
- **XSLT 3.0 instructions and declarations**: text value templates (`expand-text`), `xsl:mode`
  (`on-no-match`, `on-multiple-match`, `use-accumulators`), `[xsl:]default-mode`, static variables and
  parameters, shadow attributes, `xsl:try`/`xsl:catch`, `xsl:iterate` (with `xsl:next-iteration`,
  `xsl:break`, `xsl:on-completion`), `xsl:where-populated`, `xsl:on-empty`, `xsl:on-non-empty`, `xsl:fork`
  and `xsl:source-document` (without streaming), `xsl:copy select`, `xsl:sequence` with content,
  `xsl:context-item`, `xsl:import` anywhere, accumulators (`accumulator-before`/`accumulator-after`),
  `xsl:merge` (without streaming), `xsl:map` and `xsl:map-entry`, `xsl:number start-at`, `copy-accumulators`,
  composite keys, `xsl:global-context-item`, `xsl:message` with err:XTMM9000 and `error-code`,
  `xsl:decimal-format` declarations that merge (and `exponent-separator`), and
  `xsl:analyze-string` with regexes that match the empty string.
- **Dynamic evaluation**: `xsl:evaluate` (off unless `XsltSettings.EnableEvaluate` is set, or `XsltSettings.TrustedXslt`
  is used; then the target expression is compiled once per static context and cached, and may call the stylesheet's
  public functions).
- **XSLT 3.0 functions**: `fn:copy-of`, `fn:snapshot`, `fn:available-system-properties`,
  `fn:current-output-uri`.
- **Patterns and modes**: predicate patterns and template rules for atomic values, maps and arrays (also in
  `group-starting-with`/`group-ending-with`); the 3.0 pattern forms (`x/(a|b)`, `intersect`/`except` per
  context node, `(…)[n]`, `id()` with two arguments); `mode="#current"` and `xsl:next-match` in named
  templates and in rules of several modes, `xsl:next-match` in a named template called from a rule;
  EQNames in QName-valued attributes.
- **Packages**: `xsl:package`, `xsl:use-package` with version ranges, visibility (`xsl:expose`,
  `xsl:accept`, public/private/final/abstract), `xsl:override` and `xsl:original`, declared modes, and
  decimal formats, keys, namespace aliases and accumulators local to their package; `xsl:use-package`
  picks the highest version in range.
- **Serialization**: XML, HTML (its own serializer: HTML4 and HTML5 void elements, boolean attributes, URI
  escaping, the `meta` element, HTML5 prefix normalization of SVG and MathML), XHTML (including HTML5: void
  elements, prefix normalization; the serializer's `meta` element replaces the stylesheet's), text, JSON and
  adaptive (also for `xsl:result-document`; nodes in JSON with `json-node-output-method` html or xhtml),
  `item-separator`, character maps (found before normalization, not applied to CDATA sections, escaped URI
  attributes or namespace declarations), Unicode normalization (with SERE0012 for `fully-normalized`),
  `parameter-document`, output declarations merged by import precedence, `fn:serialize` with an element or a map
  of parameters, and the serialization parameter errors (SEPM, SESU).

## What's outstanding

- **XSLT 3.0**: unparsed entities of temporary trees (a document's own are read from its DTD).
- **Known gaps**:
  - Dynamic errors in patterns are non-matches only for predicate patterns.
  - Static variables of an imported module aren't visible after the `xsl:import` in the importing module (imports
    are loaded after the module that imports them).
  - `doc()` and `document()` refuse DTDs unless the input reader allows them.
  - Whitespace stripping is the stylesheet's, not each package's, so a document read in two packages is stripped once.
  - An expression of `xsl:evaluate` parses documents again rather than sharing the transformation's, so node
    identity across the two differs.
  - Implementation-defined choices (Unicode version of regex classes and the rest) are in
    [docs/divergences.md](docs/divergences.md).
- **Performance (P4)**: the 1.0 throughput goal is met, and the compact tree is in use.
  - 2.0+ stylesheets use it for the documents they parse and for their temporary trees.
  - 1.0 stylesheets opt in with `UseCompactTree`, which brings transforms from text to 1.49× stock.
  - XSLT 2.0/3.0 has caught up with 1.0 on stock. The 3.0 rewrites of the 1.0 benchmarks ran 1.9–4.3× slower than
    the 1.0 versions on stock, because the 2.0+ code boxed its values. Typed codegen, a direct result writer and
    cheaper maps, grouping and number formatting have brought every pair under stock's time: 0.71–0.96×
    (2026-10-21, [docs/performance.md](docs/performance.md)).
  - Automatic indexing of `/path[key = $value]` joins, and recursion over sequences that no longer re-checks them.
  - The XSLT 3.0 micro suite (13 stylesheets at 1 KB, 1 MB and 100 MB, compile and cold start) runs on every pull
    request against its base, and fails on 5% more time or allocation.
  - Sink B (`Xsl3.Build`, saved assemblies) and NativeAOT are in; still to do: the SaxonCS-HE comparison, and
    template dispatch tables if a stylesheet with hundreds of rules shows the need (measured: 17 ns per element for
    200 extra rules).
- **Later phases**: schema-awareness (P6) and streaming (P7) haven't started.
- **Packaging**: alpha prereleases only (0.8.0-alpha is the latest). The XSLT 3.0 API (`XsltCompiler`,
  `Xslt30Transformer`) is in since 0.8.0-alpha, beside `XslCompiledTransform`; both may change before 1.0, and what
  1.0 means is still to be decided (docs/plan/remaining-scope.md §9). `Xsl3.Build` and `Xsl3.Cli` aren't published
  yet.

## Using it

Guides: [getting started with the XSLT 3.0 API](docs/getting-started.md) and [moving from System.Xml.Xsl](docs/migration.md).

Xsl3 keeps `XslCompiledTransform`'s API, so switching is a `using` change:

```diff
- using System.Xml.Xsl;
+ using Xsl3;
```

```csharp
using Xsl3;

var xslt = new XslCompiledTransform();
xslt.Load("style.xsl");
xslt.Transform("input.xml", "output.html");
```

`XsltArgumentList`, `XsltSettings`, `XsltException` and the extension-object and parameter APIs work as
they do in `System.Xml.Xsl`. Intentional differences are listed in [docs/divergences.md](docs/divergences.md).

### New: `UseCompactTree` for extra speed on XSLT 1.0

Set `UseCompactTree` before `Load`. An XSLT 1.0 stylesheet then parses its input into Xsl3's compact tree instead
of `XPathDocument`:

```csharp
var xslt = new XslCompiledTransform { UseCompactTree = true }; // new in 0.4.0-alpha
xslt.Load("style.xsl");
xslt.Transform("input.xml", "output.html");
```

- **Speed:** transforms from a file, URI, stream or `XmlReader` run about 1.5× faster than stock, against about 1.2×
  without it.
- **Memory:** the tree takes about 60% of `XPathDocument`'s memory.
- **What it covers:** the input and the documents `document()` loads. An `XPathDocument` you pass in yourself is used
  as it is.
- **Output:** the same as stock, except that `generate-id()` returns different (still unique) strings. That's why
  it's off by default for 1.0.
- **XSLT 2.0 and 3.0 stylesheets** always use the compact tree.

See [docs/performance.md](docs/performance.md) for the measurements.

Targets .NET 8 and .NET 10.

### The XSLT 3.0 API

`XsltCompiler`, `XsltExecutable` and `Xslt30Transformer` start a stylesheet the ways XSLT 3.0 allows (§2.3): templates
applied to any sequence in any mode, a named template, or a stylesheet function. Values are XDM values (`XdmValue`,
`XdmNode`, `XdmAtomicValue`, `XdmMap`, `XdmArray` in `Xsl3.Xdm`), and a raw result comes back as one:

```csharp
using Xsl3;
using Xsl3.Xdm;

XsltExecutable executable = new XsltCompiler().Compile(new Uri("file:///style.xsl")); // cache and share this
Xslt30Transformer transformer = executable.CreateTransformer();                       // one per thread
transformer.StylesheetParameters[new QName("title")] = new XdmAtomicValue("Report");

transformer.ApplyTemplates(XdmNode.Load(File.OpenRead("input.xml")), Console.Out);   // serialized as xsl:output says
XdmValue rows = transformer.CallTemplate(new QName("rows"),
    parameters: new Dictionary<QName, XdmValue> { [new QName("max")] = new XdmAtomicValue(10L) });
XdmValue total = transformer.CallFunction(new QName("urn:report", "total"), rows);
```

- A dynamic error is an `XsltDynamicException` with the error's `Code`, `Description` and `ErrorObject`; a stylesheet
  that doesn't compile raises an `XsltStaticException` listing every error with its module, line and column.
- Each transformer has its own `BaseOutputUri`, `MessageHandler`, `TraceHandler`, `ResultDocumentHandler`,
  `DocumentResolver`, `CancellationToken` and `GlobalContextItem`.
- `XsltCompiler` takes the `XsltSettings`, static parameters and the package library (`Packages.Add(uri)`), and
  registers extension functions for 2.0+ stylesheets (`RegisterExtensionFunction(name, arity, args => …)`).
- An executable compiles the stylesheet once per entry point it is started at, the first time; the design is in
  [docs/plan/api.md](docs/plan/api.md).
- An `XsltDynamicException` carries the `StylesheetStack` of templates and functions that was running. With
  `XsltSettings.EnableTracing` it is complete, and a transformer's `TraceListener` (an `IXsltTraceListener`) sees
  every template and function start and end; without it, the stack is read from the .NET one and leaves out
  functions that were inlined or tail-called.
  `XsltExecutable.Explain` writes the optimized QIL the stylesheet compiles to.
- `XsltCompiler.CompileAssociatedStylesheet(document)` compiles the stylesheet a document names in its
  `<?xml-stylesheet?>` processing instruction, embedded in it (`href="#id"`) or not.

### Parameters of XSLT 2.0 and 3.0 stylesheets

`XsltArgumentList.AddParam` takes more CLR types for a 2.0+ stylesheet, and gives each its XDM type (SPEC §7.1):

| CLR value | XDM value |
|---|---|
| `int`, `long`, `BigInteger` and the other integer types | `xs:integer` |
| `decimal`, `double`, `float` | `xs:decimal`, `xs:double`, `xs:float` |
| `DateTime`, `DateTimeOffset` | `xs:dateTime` (with a timezone unless the `DateTime` is unspecified) |
| `DateOnly`, `TimeOnly`, `TimeSpan` | `xs:date`, `xs:time`, `xs:dayTimeDuration` |
| `Uri`, `byte[]` | `xs:anyURI`, `xs:base64Binary` |
| a dictionary | a map |
| any other `IEnumerable` | a sequence |

XSLT 1.0 stylesheets convert exactly as stock does: every number becomes a double, and a `DateTime` becomes a string.

### Settings: secure by default

What a stylesheet can reach beyond its input and output is off unless you turn it on (SPEC §21.1):

| Setting | Default | What it allows |
|---|---|---|
| `XsltSettings.EnableDocumentFunction` | off, as stock | `document()`, `doc()` and friends |
| `XsltSettings.EnableEvaluate` | off | `xsl:evaluate` (XSLT 3.0 §10.4); `TrustedXslt` turns it on |
| `XsltSettings.EnableResultDocument` | off | `xsl:result-document` writing files, only under `XslCompiledTransform.BaseOutputUri` (the results file for `Transform(string, string)`) |
| `XsltSettings.EnableEnvironmentVariables` | off | `fn:environment-variable` and `fn:available-environment-variables` |
| `XsltSettings.EnableAssertions` | off | `xsl:assert` (ignored when off) |
| `XsltSettings.EnableExtensionFunctions` | on, as stock | calls to `XsltArgumentList.AddExtensionObject` objects (1.0 stylesheets) |

Limits:
- `XsltSettings.Timeout` stops a transformation that runs too long.
- `XsltArgumentList.CancellationToken` cancels one from outside.
- `XsltSettings.MaxRecursionDepth` caps the nesting of template and function calls (tail calls don't count).
- `XsltSettings.MaxOutputBytes` caps the principal result written to a `Stream` or `TextWriter`.
- `XsltSettings.UseDedicatedThread` (with `DedicatedThreadStackSize`, 64 MB by default) runs each transformation on a
  thread with a large stack, for stylesheets that recurse deeply.

A stopped transformation raises `Q{http://xsl3/err}cancelled`; one that runs out of stack, or past
`MaxRecursionDepth`, raises `Q{http://xsl3/err}recursion-limit` instead of crashing the process; one that writes too
much raises `Q{http://xsl3/err}output-limit`. An extension function's .NET exception is
`Q{http://xsl3/err}extension-error`. `XsltArgumentList.TraceHandler` receives `fn:trace` calls.

### The `xsl3` command line

`src/Xsl3.Cli` is a .NET tool, `xsl3` (not on nuget.org yet; see [its readme](src/Xsl3.Cli/README.md)):

```bash
xsl3 transform -xsl:style.xsl -s:input.xml -o:output.html title=Report --trace
```

`xsl3 explain -xsl:style.xsl` prints the optimized QIL the stylesheet compiles to.

### Stylesheets compiled ahead of time, and NativeAOT

`XslCompiledTransform.CompileToAssembly` (on .NET 9 or later) saves a compiled stylesheet as an assembly holding one
type; `Load(Type, XsltSettings)` loads it without compiling, so it runs under NativeAOT. The `Xsl3.Build` targets
(`src/Xsl3.Build`, not on nuget.org yet) do it at build time:

```xml
<XsltStylesheet Include="Report.xsl" TypeName="Stylesheets.Report" />
```

```csharp
var transform = new XslCompiledTransform();
transform.Load(typeof(Stylesheets.Report), new XsltSettings());
```

`samples/Xsl3.AotSample` is published with NativeAOT in CI.

### XSLT 1.0 stylesheets on the 3.0 processor

A stylesheet whose principal module says `version="1.0"` runs on stock's 1.0 engine, with byte-identical results.
`XsltSettings.VersionMode = XsltVersionMode.BackwardsCompatible` runs it on the 3.0 processor instead, in the
backwards-compatible mode XSLT 3.0 §3.9 defines: XPath 2.0+ syntax and functions, with XPath 1.0's conversions in
comparisons, arithmetic and function arguments, and the first item of `xsl:value-of` and attribute value templates.
Regions marked `version="1.0"` inside 2.0+ stylesheets always run that way.

## Building and testing

Requires the .NET 10 SDK.

```bash
dotnet build Xsl3.slnx
```

```bash
dotnet test Xsl3.slnx
```

| Project | What it is |
|---|---|
| `src/Xsl3` | The engine and public API |
| `tests/Xsl3.Upstream.Tests` | dotnet/runtime's `XslCompiledTransform` tests, ported by `eng/port-upstream-tests.py`. The XSLT 1.0 regression gate: must pass 100%. |
| `tests/Xsl3.Tests` | Unit tests, including checks that every bridge to System.Private.Xml internals still binds on the running .NET |
| `tests/Xsl3.W3C` | W3C conformance harness: QT3 (XPath 3.1) and xslt30-test. Fetch the suites with `eng/fetch-w3c.sh`; known failures are tracked in `tests/Xsl3.W3C/baselines`. `eng/xslt30-sets.sh` runs chosen xslt30-test sets. |
| `tests/Xsl3.Differential` | Runs the 1.0 corpus through stock `System.Xml.Xsl` and through Xsl3 and requires byte-identical results |
| `bench/Xsl3.Benchmarks` | BenchmarkDotNet suites comparing Xsl3 with stock: `dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- --filter '*'` |

Benchmarks against stock: [docs/performance.md](docs/performance.md) (1.38× geometric mean on XSLT 1.0, never slower).

The NuGet package page uses [docs/nuget/README.md](docs/nuget/README.md) (absolute links only); update its
status and numbers along with this file.

How the fork was taken, what was vendored, and how Xsl3 reaches the System.Xml internals it can't vendor:
[docs/internals.md](docs/internals.md).

## Roadmap

From [SPEC.md §22](SPEC.md):

| Phase | Scope | State |
|---|---|---|
| P0 | Fork, baseline, upstream tests, benchmark harness | Done |
| P1 | XDM type system and XPath 2.0 core, core F&O | Done ([plan](docs/plan/p1-xdm-xpath.md)) |
| P2 | XSLT 2.0 | Done: xslt30-test 2.0 profile 95.2% at exit, 99.8% now ([plan](docs/plan/p2-xslt20.md)) |
| P3 | XSLT 3.0 basic, XPath 3.1, higher-order functions | Done: QT3 99.4%, xslt30-test 3.0 profile 95.1% at exit; 100% and 99.7% now ([plan](docs/plan/p3-xslt30.md)) |
| P4 | Performance: beat stock `XslCompiledTransform` (≥ 1.2× on 1.0, never slower) with modern .NET: `Span<T>`, `SearchValues`, pooled buffers, unboxed codegen, a compact tree, dispatch tables, build-time AOT | In progress: 1.0 throughput 1.38× stock (exit ≥ 1.2× met); compact trees for parsed documents (59% of `XPathDocument`'s memory, 1.49× stock end to end) and 2.0+ temporary trees, with a name pool; 3.0 rewrites of the 1.0 benchmarks now at 0.71–0.96× of stock's time (target ≤ 1× met); automatic indexing; a micro suite and a performance gate in CI; Sink B (saved assemblies, `Xsl3.Build`) and NativeAOT done; the SaxonCS comparison to do |
| P5 | `xsl:evaluate`, JSON and adaptive serialization, character maps, the remaining serialization parameters | Exit met ([plan](docs/plan/p5-evaluate.md)): the five matching xslt30-test sets at 99.3% (454 / 457; `evaluate` 42 / 42); merged |
| P6 | Schema-awareness | Not started |
| P7 | Streaming | Not started |

## Licence

MIT, the same as upstream. Files derived from dotnet/runtime keep their .NET Foundation headers; see
[LICENSE.TXT](LICENSE.TXT) and [THIRD-PARTY-NOTICES.TXT](THIRD-PARTY-NOTICES.TXT).

Xsl3 is an independent project and is not affiliated with or endorsed by Microsoft or the .NET Foundation.
