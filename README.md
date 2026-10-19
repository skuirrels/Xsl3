# Xsl3

**An XSLT 2.0 / 3.0 processor for .NET, forked from `System.Xml.Xsl`.**

Xsl3 starts from the engine behind .NET's `XslCompiledTransform` (stylesheets compiled to IL through the QIL
intermediate representation) and is growing it into a conformant XSLT 3.0 / XPath 3.1 processor that stays
at least as fast as stock on XSLT 1.0 and aims to be clearly faster than SaxonCS on 2.0/3.0.
The full design is in [SPEC.md](SPEC.md).

> **Status: Phase 3 (XSLT 3.0) in progress.** XSLT 1.0 behaves exactly as stock `System.Xml.Xsl`; XSLT 2.0
> is complete; XPath 3.1 with maps, arrays and higher-order functions is in; the XSLT 3.0 instructions are
> partly done. Not yet released to NuGet. See [what works](#what-works) and [what's outstanding](#whats-outstanding).

## What works

Measured on 2026-10-19 (Release build, .NET 10); details in [docs/conformance.md](docs/conformance.md).

| Gate | Result |
|---|---|
| Upstream `XslCompiledTransform` tests (the XSLT 1.0 regression gate) | 1661 / 1661 on .NET 8 and .NET 10 |
| Differential: byte-for-byte against stock `System.Xml.Xsl` over the 1.0 corpus | 394 / 394 on .NET 8 and .NET 10 |
| W3C QT3 (XPath 3.1 / F&O 3.1, higher-order functions claimed) | 21,685 passed, 199 failed (99.1%) |
| W3C xslt30-test, XSLT 2.0 profile (P2 exit: ≥ 95%) | 95.2% |
| W3C xslt30-test, XSLT 3.0 profile (P3 exit target: ≥ 95%) | 6,791 passed, 790 failed, 444 not run (89.6%) |
| XSLT 1.0 speed against stock | parity (±3%, [docs/performance.md](docs/performance.md)) |

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
- **XSLT 3.0**: text value templates (`expand-text`), `xsl:mode` (`on-no-match`), `[xsl:]default-mode`,
  static variables and parameters, shadow attributes, `xsl:try`/`xsl:catch`, `xsl:iterate` (with
  `xsl:next-iteration`, `xsl:break`, `xsl:on-completion`), `xsl:where-populated`, `xsl:on-empty`,
  `xsl:on-non-empty`, `xsl:fork` and `xsl:source-document` (without streaming), `xsl:copy select`,
  `xsl:sequence` with content, `xsl:context-item`, predicate patterns and template rules for atomic values,
  maps and arrays, `xsl:import` anywhere, `fn:copy-of`, `fn:snapshot`, `fn:available-system-properties`,
  HTML5 serialization (`html-version`).

## What's outstanding

- **XSLT 3.0 instructions**: `xsl:merge`, accumulators, `xsl:map`/`xsl:map-entry`/`xsl:array`,
  `xsl:evaluate`, `xsl:assert`, `xsl:mode on-multiple-match`, packages (`xsl:package`, `xsl:use-package`,
  visibility) and initial functions, `fn:current-output-uri` and the base output URI.
- **Serialization**: the `json` and `adaptive` methods, `err:line-number`/`err:column-number` in
  `xsl:catch`.
- **Known gaps**: the HTML5-only void elements (`wbr`, `embed`, `source`, `track`) get end tags with the html method, dynamic errors in patterns treated as non-matches, static variables declared in modules
  imported after their use, regex character classes follow .NET's Unicode version rather than the suite's.
- **Later phases**: performance work (P4), schema-awareness (P6) and streaming (P7) haven't started.
- **Packaging**: no NuGet release yet; the public API is still `XslCompiledTransform`-shaped.

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

Targets .NET 8 and .NET 10.

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

Benchmark baseline against stock: [docs/performance.md](docs/performance.md) (parity on XSLT 1.0, as Phase 0 requires).

How the fork was taken, what was vendored, and how Xsl3 reaches the System.Xml internals it can't vendor:
[docs/internals.md](docs/internals.md).

## Roadmap

From [SPEC.md §22](SPEC.md):

| Phase | Scope | State |
|---|---|---|
| P0 | Fork, baseline, upstream tests, benchmark harness | Done |
| P1 | XDM type system and XPath 2.0 core, core F&O | Done ([plan](docs/plan/p1-xdm-xpath.md)) |
| P2 | XSLT 2.0 | Done: xslt30-test 2.0 profile 95.2% ([plan](docs/plan/p2-xslt20.md)) |
| P3 | XSLT 3.0 basic, XPath 3.1, higher-order functions | In progress: QT3 99.1%, xslt30-test 3.0 profile 89.6% ([plan](docs/plan/p3-xslt30.md)) |
| P4 | Performance: compact tree, dispatch tables, build-time AOT | Not started |
| P5 | `xsl:evaluate`, JSON and adaptive serialization | Not started (the JSON functions are in) |
| P6 | Schema-awareness | Not started |
| P7 | Streaming | Not started |

## Licence

MIT, the same as upstream. Files derived from dotnet/runtime keep their .NET Foundation headers; see
[LICENSE.TXT](LICENSE.TXT) and [THIRD-PARTY-NOTICES.TXT](THIRD-PARTY-NOTICES.TXT).

Xsl3 is an independent project and is not affiliated with or endorsed by Microsoft or the .NET Foundation.
