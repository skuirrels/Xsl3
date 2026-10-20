# Performance

Phase 4's 1.0 exit criterion (SPEC §19.6): Xsl3 at least 1.2× stock `XslCompiledTransform` on the XSLT 1.0
benchmark corpus and never slower, with 1.5× as the target.

Run on 2026-10-20 · .NET 10.0.13 · Windows 11 · Intel Core i7-8700 · BenchmarkDotNet 0.15.8 default job ·
`dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- --filter '*Xslt10Transform*' '*Xslt10Compile*'`.
Full reports: [Transform](bench/p4-2026-10-20-Xslt10Transform.md), [Compile](bench/p4-2026-10-20-Xslt10Compile.md).
The Phase 0 baseline from the same machine is in [bench/baseline-2026-10-20-*](bench/).

The output is byte-for-byte the same as stock's. The quick comparison checks it, and so does the differential
suite (394 cases on .NET 8 and .NET 10).

## Steady-state transform (pre-parsed input, output to `Stream.Null`)

Speedup is stock mean time / Xsl3 mean time.

| Stylesheet | Orders | Stock | Xsl3 | Speedup | Stock alloc | Xsl3 alloc |
|---|---:|---:|---:|---:|---:|---:|
| identity | 2,000 | 4.14 ms | 3.33 ms | 1.25× | 1,959 KB | 1,373 KB |
| identity | 20,000 | 40.5 ms | 33.0 ms | 1.23× | 18,850 KB | 13,197 KB |
| sort | 2,000 | 3.62 ms | 1.61 ms | 2.24× | 656 KB | 393 KB |
| sort | 20,000 | 57.8 ms | 32.9 ms | 1.76× | 6,819 KB | 4,226 KB |
| muenchian | 2,000 | 1.18 ms | 1.04 ms | 1.13× | 306 KB | 202 KB |
| muenchian | 20,000 | 18.7 ms | 16.9 ms | 1.11× | 2,938 KB | 1,930 KB |
| string-recursion | 2,000 | 1.52 ms | 1.23 ms | 1.23× | 1,542 KB | 1,055 KB |
| string-recursion | 20,000 | 16.1 ms | 13.3 ms | 1.21× | 15,142 KB | 10,341 KB |
| key-join | 2,000 | 4.05 ms | 2.76 ms | 1.47× | 951 KB | 330 KB |
| key-join | 20,000 | 42.6 ms | 27.2 ms | 1.57× | 7,991 KB | 1,729 KB |
| html-report | 2,000 | 54.1 ms | 2.47 ms | 21.9× | 1,706 KB | 1,564 KB |
| html-report | 20,000 | 5,195 ms | 25.2 ms | 206× | 17,031 KB | 15,624 KB |

- **Geometric mean speedup:**
  - 1.38× over the ten rows other than html-report;
  - 2.6× with html-report included.
- **Slowest row:** muenchian at 20,000 orders, 1.11×.
- **Exit criterion:** met. The geometric mean is at least 1.2× and no row is slower than stock.
- **Target:** 1.5× is reached only with html-report included. Without it, the mean is 1.38×.

## Compile (`Load`)

| Stylesheet | Stock | Xsl3 | Ratio |
|---|---:|---:|---:|
| identity | 492 µs | 502 µs | 1.02 |
| muenchian | 694 µs | 708 µs | 1.03 |
| html-report | 656 µs | 725 µs | 1.11 (RatioSD 0.21) |

Compile time is within noise of stock. The 2.0/3.0 front end (V2 loader and generator) is not on this path.

## What made the difference

All changes keep stock's output. Each fast path either is exact by construction or is checked against the
general algorithm by randomized tests, millions of values in all.

| Change | Where | Effect |
|---|---|---|
| `xsl:number` (level single, default count, no from) counts on from the sibling numbered last, so a row-by-row numbering is linear rather than quadratic | `XsltLibrary.X31SiblingNumber` | html-report 206× |
| A sort reuses collation keys for repeated strings within the sort | `XmlSortKeyAccumulator` | sort |
| Sort orders (key, item) struct pairs with a typed comparer, not two object arrays through `IComparable` | `XmlQuerySequence.SortByKeys` | sort |
| A literal result element whose attributes cannot clash (no duplicates and no namespace declarations, by the construct analyzer) writes its attributes straight to the serializer, skipping the duplicate-detecting cache | `XmlILVisitor`, `XmlQueryOutput.WriteStartElementUncached` | sort, key-join |
| `@*\|node()` compiles to one walk of the content axis, not a document-order merge of two | QIL optimizer, union of filters over one axis | identity |
| String → number: digits with an optional point and at most 15 digits convert as m / 10^k, which is correctly rounded | `XPathConvert.TryParseShortDecimal` | all with numbers |
| Number → string: a decimal of at most 15 digits is found as n / 10^k for the least k that gives the double back | `XPathConvert.TryShortDecimalToString` | muenchian |
| `format-number` with a plain picture (`#,##0.00`): exact 15-digit significand from `UInt128` arithmetic, then .NET's half-up rounding, with no custom-format parse | `DecimalFormatter.TryFormatSimple` | key-join |
| UTF-8 serializer: runs of plain ASCII are found with `SearchValues` and narrowed with `Ascii.FromUtf16` | `XmlUtf8RawTextWriter.CopyAsciiRun` | identity, string-recursion |
| `translate()` maps ASCII through a per-map table, and returns the argument when nothing maps | `XsltFunctions.Translate` | string-recursion |
| Per-element prefix bookkeeping is a reused list, not a cleared dictionary | `XmlQueryOutput.UsedPrefixes` | identity |
| `key()` finds its document's index without cloning a navigator per call; index inserts probe the table once | `XmlQueryRuntime.FindIndex`, `XmlILIndex.Add` | key-join, muenchian |
| `DocOrderDistinct` checks in one pass whether a sequence is already in order before sorting it | `XmlQueryNodeSequence` | key lookups |

Tried and dropped: emitting stylesheets into a collectible dynamic assembly so that tiered PGO applies (no
measurable gain over `DynamicMethod`s).

## Where the rest of the time goes

The remaining cost is mostly shared with stock and outside Xsl3's code:

- **Navigating `XPathDocument`**, in System.Private.Xml. On a 20,000-order document the node pages and strings
  don't fit in cache, so child steps and string values are memory-bound.
- **ICU sort-key generation** for culture-aware sorts.
- **The writer pipeline** of `XmlQueryOutput`.

muenchian is the clearest case. Its key lookups visit each customer's orders across the whole document. The
compact tree with name pools (SPEC §14, §19) is the next step for these.

## Measuring

`dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- quick [orders] [stylesheet...]` runs the
two engines interleaved, checks their output is identical, and prints the median and minimum ratios. Set
`XSL3_QUICK_MS` to change the time per engine (default 3000). BenchmarkDotNet is the record. CPU profiles:
`--filter '*Xsl3Profile*' --job short` writes speedscope traces to the artifacts folder.
