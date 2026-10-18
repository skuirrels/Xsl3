# Xsl3

**An XSLT 2.0 / 3.0 processor for .NET, forked from `System.Xml.Xsl`.**

Xsl3 starts from the engine behind .NET's `XslCompiledTransform` (stylesheets compiled to IL through the QIL
intermediate representation) and is growing it into a conformant XSLT 3.0 / XPath 3.1 processor that stays
at least as fast as stock on XSLT 1.0 and aims to be clearly faster than SaxonCS on 2.0/3.0.
The full design is in [SPEC.md](SPEC.md).

> **Status: Phase 0 (fork and baseline).** Today Xsl3 is the upstream XSLT 1.0 engine, extracted from
> `dotnet/runtime` and building on its own. It passes the whole upstream `XslCompiledTransform` test suite
> (1661/1661 on .NET 8 and 10), matches stock output byte for byte across the 1.0 corpus, and runs at the same speed.
> No XSLT 2.0/3.0 features exist yet. See the [roadmap](#roadmap).

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
| `tests/Xsl3.Differential` | Runs the 1.0 corpus through stock `System.Xml.Xsl` and through Xsl3 and requires byte-identical results |
| `bench/Xsl3.Benchmarks` | BenchmarkDotNet suites comparing Xsl3 with stock: `dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- --filter '*'` |

Benchmark baseline against stock: [docs/performance.md](docs/performance.md) (parity on XSLT 1.0, as Phase 0 requires).

How the fork was taken, what was vendored, and how Xsl3 reaches the System.Xml internals it can't vendor:
[docs/internals.md](docs/internals.md).

## Roadmap

From [SPEC.md §22](SPEC.md):

| Phase | Scope | State |
|---|---|---|
| P0 | Fork, baseline, upstream tests, benchmark harness | In progress |
| P1 | XDM type system and XPath 2.0 core, core F&O | |
| P2 | XSLT 2.0 | |
| P3 | XSLT 3.0 basic, XPath 3.1, higher-order functions | |
| P4 | Performance: compact tree, dispatch tables, build-time AOT | |
| P5 | `xsl:evaluate`, JSON and adaptive serialization | |
| P6 | Schema-awareness | |
| P7 | Streaming | |

## Licence

MIT, the same as upstream. Files derived from dotnet/runtime keep their .NET Foundation headers; see
[LICENSE.TXT](LICENSE.TXT) and [THIRD-PARTY-NOTICES.TXT](THIRD-PARTY-NOTICES.TXT).

Xsl3 is an independent project and is not affiliated with or endorsed by Microsoft or the .NET Foundation.
