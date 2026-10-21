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
> [releases](https://github.com/skuirrels/Xsl3/releases)). See [what works](#what-works) and
> [what's outstanding](#whats-outstanding).

## What works

Measured on 2026-10-21 (Release build, .NET 10); details in [docs/conformance.md](docs/conformance.md).

| Gate | Result |
|---|---|
| Upstream `XslCompiledTransform` tests (the XSLT 1.0 regression gate) | 1661 / 1661 on .NET 8 and .NET 10 |
| Differential: byte-for-byte against stock `System.Xml.Xsl` over the 1.0 corpus | 394 / 394 on .NET 8 and .NET 10 |
| W3C QT3 (XPath 3.1 / F&O 3.1, higher-order functions claimed) | 21,796 passed, 88 failed (99.6%) |
| W3C xslt30-test, XSLT 2.0 profile (P2 exit: ≥ 95%) | 4,415 passed, 57 failed (98.7%) |
| W3C xslt30-test, XSLT 3.0 profile (P3 exit: ≥ 95%) | 7,729 passed, 178 failed, 49 not run (97.7%) |
| xslt30-test sets for `xsl:evaluate` and serialization (P5 exit: ≥ 98%) | 454 of 457 (99.3%): `evaluate` 42 / 42, `output` 218 / 221, `character-map` 29 / 29, `result-document` 140 / 140, `disable-output-escaping` 25 / 25 |
| XSLT 1.0 speed against stock (P4 exit: ≥ 1.2×, never slower) | 1.38× geometric mean, 1.11× to 2.2× per benchmark, html-report 206× ([docs/performance.md](docs/performance.md)) |

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

- **XSLT 3.0**:
  `inherit-namespaces="no"` (the tree model inherits namespaces from ancestors), unparsed entities of
  temporary trees (a document's own are read from its DTD), memoized functions (`new-each-time="no"`,
  `cache`).
- **API**: the XSLT 3.0 API of SPEC §7.2 (initial templates, modes and functions, the package library, raw
  results); for now `XslCompiledTransform` plus the settings above. `MaxRecursionDepth` and `MaxOutputBytes`.
- **Known gaps**:
  - `xsl:next-match` from one alternative of a union pattern to another of the same rule.
  - Dynamic errors in patterns are non-matches only for predicate patterns.
  - Static variables declared in modules imported after their use.
  - `doc()` and `document()` refuse DTDs unless the input reader allows them.
  - An expression of `xsl:evaluate` parses documents again rather than sharing the transformation's, so node
    identity across the two differs.
  - Implementation-defined choices (Unicode version of regex classes, `<a />` for empty elements, and the rest) are in
    [docs/divergences.md](docs/divergences.md).
- **Performance (P4)**: the 1.0 throughput goal is met, and the compact tree is in use.
  - 2.0+ stylesheets use it for the documents they parse and for their temporary trees.
  - 1.0 stylesheets opt in with `UseCompactTree`, which brings transforms from text to 1.49× stock.
  - XSLT 2.0/3.0 has nearly caught up with 1.0 on stock. The 3.0 rewrites of the 1.0 benchmarks ran 1.9–4.3×
    slower than the 1.0 versions on stock, because the 2.0+ code boxed its values. Typed codegen and a direct result
    writer have brought that to 0.68–1.15× of stock's time (2026-10-21: muenchian and key-join still above 1×), and it
    continues until every pair is at or under 1×.
  - Still to do: Sink B (`Xsl3.Build`) and NativeAOT, and the SaxonCS-HE comparison.
- **Later phases**: schema-awareness (P6) and streaming (P7) haven't started.
- **Packaging**: alpha prereleases only; the public API is still `XslCompiledTransform`-shaped and may change
  before 1.0.

## Using it

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

Limits:
- `XsltSettings.Timeout` stops a transformation that runs too long.
- `XsltArgumentList.CancellationToken` cancels one from outside.
- `XsltSettings.UseDedicatedThread` (with `DedicatedThreadStackSize`, 64 MB by default) runs each transformation on a
  thread with a large stack, for stylesheets that recurse deeply.

A stopped transformation raises `Q{http://xsl3/err}cancelled`; one that runs out of stack raises
`Q{http://xsl3/err}recursion-limit` instead of crashing the process.

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
| P2 | XSLT 2.0 | Done: xslt30-test 2.0 profile 95.2% ([plan](docs/plan/p2-xslt20.md)) |
| P3 | XSLT 3.0 basic, XPath 3.1, higher-order functions | Done: QT3 99.4%, xslt30-test 3.0 profile 95.1% ([plan](docs/plan/p3-xslt30.md)) |
| P4 | Performance: beat stock `XslCompiledTransform` (≥ 1.2× on 1.0, never slower) with modern .NET: `Span<T>`, `SearchValues`, pooled buffers, unboxed codegen, a compact tree, dispatch tables, build-time AOT | In progress: 1.0 throughput 1.38× stock (exit ≥ 1.2× met); compact trees for parsed documents (59% of `XPathDocument`'s memory, 1.49× stock end to end) and 2.0+ temporary trees, with a name pool; 3.0 rewrites of the 1.0 benchmarks now at 0.68–1.15× of stock's time (target ≤ 1×; typed codegen and a direct result writer); Sink B (started: lazy IL module), NativeAOT and the SaxonCS comparison to do |
| P5 | `xsl:evaluate`, JSON and adaptive serialization, character maps, the remaining serialization parameters | Exit met ([plan](docs/plan/p5-evaluate.md)): the five matching xslt30-test sets at 99.3% (454 / 457; `evaluate` 42 / 42); merged |
| P6 | Schema-awareness | Not started |
| P7 | Streaming | Not started |

## Licence

MIT, the same as upstream. Files derived from dotnet/runtime keep their .NET Foundation headers; see
[LICENSE.TXT](LICENSE.TXT) and [THIRD-PARTY-NOTICES.TXT](THIRD-PARTY-NOTICES.TXT).

Xsl3 is an independent project and is not affiliated with or endorsed by Microsoft or the .NET Foundation.
