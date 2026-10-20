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

## The compact tree (SPEC §14.1)

`Xsl3.Tree.CompactDocument` is an immutable tree in columnar layout. Each node is an index in document order into
parallel arrays: kind, parent, subtree end, name and one data slot (about 17 bytes a node). Attributes and namespace
declarations have arrays of their own, names are pooled per document, and each distinct short value (32 characters or
fewer) is stored once.

Because a subtree is a contiguous range, several operations become integer arithmetic:

- document order is an integer comparison;
- a node's next sibling is the index just past its subtree;
- a descendant scan is one pass over a range;
- name tests compare integer expanded-name ids.

Its navigator reads exactly as `XPathDocument`'s does:

- the same text merging and whitespace rules;
- namespace nodes in the same order (including `xmlns=""` and the copies made when a prefix is overridden);
- the same base URIs and DTD IDs.

A test walks both trees in step over every node to check this. The one visible difference is the string
`generate-id()` returns.

Values are kept as pooled strings rather than slices of a shared `char[]` (a departure from the SPEC table). That way,
reading a value never allocates.

**Where it is used:** for the documents a transform parses itself, that is an input given as a URI or `XmlReader`,
`doc()` and `document()`.

- 2.0+ stylesheets always use it.
- A 1.0 stylesheet uses it when `XslCompiledTransform.UseCompactTree` is set before `Load`. Otherwise it keeps
  `XPathDocument`, so its output stays byte-identical to stock.
- A caller's own `XPathDocument` is used as given.

**Temporary trees (SPEC §14.4):** in 2.0+ stylesheets, these are compact trees too. That covers variables with
content, `xsl:document`, nodes built in sequences, and the copies `copy-of()` and `snapshot()` make. They're built
from raw-writer events by `CompactDocument.Writer`, exactly as `XPathDocumentBuilder` builds them:

- fragments whose root is a text, attribute or namespace node;
- empty base URIs on elements and PIs;
- shortcut tags only after `WriteEndElement`;
- the parentless `xml` namespace quirk.

Temporary trees share one name pool per name table (SPEC §14.2), so a small tree builds no name tables of its own.
1.0 result tree fragments stay `XPathDocument`'s.

Sorting nodes from many trees into document order was quadratic: each comparison between documents scanned every
root seen so far. Xsl3's own trees are now looked up by their document object, so the order between trees is a hash
lookup. The numbering, and so `generate-id()`, is unchanged.

On `bench/corpus/xslt30/temporary-trees.xsl` (a temporary tree per order, then grouping; 20,000 orders; Xsl3 alone,
`quick30` mode):

| | Time per transform | Allocated |
|---|---:|---:|
| Before (`XPathDocument` temporary trees, quadratic sort) | 5,640 ms | 400 MB |
| `XPathDocument` temporary trees | 652 ms | 396 MB |
| Compact temporary trees with the name pool | 287 ms | 243 MB |

**Memory:**

- 20,000 orders: 8.3 MB retained, against 14.0 MB for `XPathDocument` (59%; the target is ≤ 75%).
- A unit test checks the 75% bound.

**Building the tree** ([report](bench/p4-2026-10-20-InputParse.md)):

| Orders | XPathDocument | CompactDocument | Ratio |
|---|---:|---:|---:|
| 2,000 | 6.15 ms | 5.71 ms | 0.94 |
| 20,000 | 75.9 ms | 68.9 ms | 0.94 |

Building allocates more than it keeps: 22 MB, against 14 MB for `XPathDocument`. The arrays grow by doubling, and the
value-sharing table is discarded once the tree is built.

**Transforms from text, each engine building its own tree** ([report](bench/p4-2026-10-20-Xslt10EndToEnd.md);
20,000 orders):

| Stylesheet | Stock | Xsl3 (XPathDocument) | Xsl3 (`UseCompactTree`) | Speedup with the compact tree |
|---|---:|---:|---:|---:|
| identity | 195.7 ms | 171.6 ms | 136.2 ms | 1.44× |
| sort | 172.1 ms | 145.0 ms | 109.6 ms | 1.57× |
| muenchian | 150.4 ms | 128.6 ms | 106.3 ms | 1.41× |
| string-recursion | 154.6 ms | 135.5 ms | 100.8 ms | 1.53× |
| key-join | 164.5 ms | 135.3 ms | 109.3 ms | 1.51× |

Geometric mean 1.49× stock with the compact tree, and 1.17× with `XPathDocument`. On a tree that has already been
built, the transform runs about as fast on either tree. The end-to-end gain comes from the faster build and from less
garbage-collector work on the smaller retained tree.

## Where the rest of the time goes

The remaining cost is mostly shared with stock and outside Xsl3's code:

- **Navigating `XPathDocument`**, in System.Private.Xml. On a 20,000-order document the node pages and strings
  don't fit in cache, so child steps and string values are memory-bound.
- **ICU sort-key generation** for culture-aware sorts.
- **The writer pipeline** of `XmlQueryOutput`.

muenchian is the clearest case. Its key lookups visit each customer's orders across the whole document. The
compact tree (above) helps when the transform builds the tree itself.

## Measuring

`dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- quick30 [orders] [stylesheet]` times an XSLT
3.0 stylesheet from `bench/corpus/xslt30` on Xsl3 alone; `XSL3_COMPACT_TREE=0` gives it XPathDocument trees for
comparison.
`dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- quick [orders] [stylesheet...]` runs the
two engines interleaved, checks their output is identical, and prints the median and minimum ratios. Set
`XSL3_QUICK_MS` to change the time per engine (default 3000). BenchmarkDotNet is the record. CPU profiles:
`--filter '*Xsl3Profile*' --job short` writes speedscope traces to the artifacts folder.
