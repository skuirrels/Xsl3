# Performance

The bounded [P7D validation-contract phase](plan/p7d-validation-contracts.md) passes the unchanged
three-round ordinary gate against `v1.1.2`: timing GM **1.008822** over 39 rows, maximum allocation
ratio **1.001578**. Keys compilation has an individual median time ratio of **1.511849**; its paired
rounds remain recorded and causal investigation is deferred. This aggregate pass makes no streaming
memory/throughput or per-row performance claim.

The 2026-10-26 [P7A close-out](plan/p7a-closeout.md) freezes the foundations release. The ordinary performance
gate passes; full streaming memory/throughput acceptance remains deferred and keeps its original targets.

The 2026-10-27 [P7C local close-out](plan/p7c-runtime-correctness.md) passes the unchanged three-round
ordinary gate against `v1.1.1`: timing geometric mean **0.995962** over 39 rows, maximum allocation
ratio **1.000156**. Individual timing outliers remain recorded in the close-out and result snapshot.
This input-boundary correction makes no new streaming-memory or throughput claim.
The [P7C hosted gate](https://github.com/skuirrels/Xsl3/actions/runs/40522313206) also passes,
with rounded timing GM **1.010** against the base branch. The two measurements retain their separate provenance.

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

A test walks both trees in step over every node to check this. Within a tree, the one visible difference is the string
`generate-id()` returns. Between trees, the order differs:

- compact trees are ordered by when they were built, so a transform's input comes before the documents `document()`
  loads;
- `XPathDocument`s are ordered by when a comparison first meets them, as in stock.

A union of nodes from two documents, such as `document('b.xml')//item | //item`, therefore comes out in a different
order. XSLT 1.0 leaves this order to the implementation. `UseCompactTreeTests` checks both differences.

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

## XSLT 2.0/3.0: rewritten pairs (SPEC §19.6)

SPEC §19.6 asks that each 1.0 benchmark, rewritten in idiomatic 3.0, run on Xsl3 at least as fast as the 1.0
version runs on stock. The rewrites are in `bench/corpus/xslt30/pairs`:

| Benchmark | 3.0 rewrite |
|---|---|
| identity | `xsl:mode on-no-match="shallow-copy"` |
| sort | text value templates |
| muenchian | `xsl:for-each-group` |
| string-recursion | `tokenize()` and `upper-case()` |
| key-join | a map instead of `xsl:key` |
| html-report | `position()` instead of `xsl:number` |

Each rewrite produces the same output as its 1.0 version, apart from 2.0+ serializer conventions:

- no BOM, and `encoding="UTF-8"`;
- a lowercase `<meta>` element;
- one last digit of a float. XPath 1.0's number-to-string algorithm prints 9565.910000000001 for the double that 2.0+
  prints, as the shortest form, 9565.910000000002.

20,000 orders, medians of interleaved runs (`pairs` mode); this machine was also running other work:

| Pair | Stock, 1.0 | Xsl3, 1.0 | Xsl3, 3.0 rewrite | Rewrite / stock |
|---|---:|---:|---:|---:|
| identity | 39.5 ms | 39.6 ms | 75.0 ms | 1.90 |
| sort | 60.3 ms | 31.6 ms | 163.4 ms | 2.71 |
| muenchian | 23.6 ms | 19.0 ms | 67.8 ms | 2.88 |
| string-recursion | 16.6 ms | 12.6 ms | 68.7 ms | 4.15 |
| key-join | 47.6 ms | 30.2 ms | 202.5 ms | 4.25 |
| html-report | 5,629 ms | 24.1 ms | 120.1 ms | 0.02 |

**The target is not met.** The 2.0+ path runs the same work 1.9–4.3× slower than stock runs the 1.0 version, and
2–7× slower than Xsl3's own 1.0 path. html-report is the exception, and only because stock's `xsl:number` is
quadratic.

The profiles say why: garbage collection takes 50–70% of the time in string-recursion and key-join. The 2.0+ code
boxes every value into an `IList<XPathItem>` of `AtomicValue` objects, calls run-time helpers for typing and
conversion, and builds sequences where the 1.0 path pushes values straight to the output. Unboxed code generation
(SPEC §19.1, the next Phase 4 item) is the fix.

Problems that measuring turned up, fixed:

- **Variables recomputed in a loop.** The QIL optimizer substituted a variable referenced once (textually) even when
  the reference was inside a loop, so its value was computed for every iteration. The key-join rewrite rebuilt its
  product map for every line: 650× slower than stock at 200 orders, and not finishing at 2,000. In 2.0+ stylesheets,
  a reference in a loop body now counts as many. 1.0 stylesheets are unchanged, so their output stays identical to
  stock's, node identity included.
- **A correctness bug.** `map:entry`, `map:put`, `array:append`, `array:put` and `array:insert-before` kept the
  navigator they were given. When that was the caller's iterator, every value ended up on the last node, as in
  `map:merge(p ! map:entry(@k, .))`. They now clone it.
- **A lock on every constant read.** `ConstantPool.Get` took a process-wide lock on every literal, cast target and
  sequence type that compiled 2.0+ code reads. Reads are now lock-free.

An earlier version of this page said that 2.0+ `xsl:sort` used the culture-sensitive collation. That was wrong:
2.0+ sort keys are encoded in code point order (`X31SortKey`), and only the comparison of those encoded keys went
through a culture's collation keys, which order the key characters (`[0-9A-F]`) the same way. That comparison is
now ordinal (below).

### Typed code generation, first steps

| Pair | Before | After |
|---|---:|---:|
| identity | 1.90 | 1.24 |
| sort | 2.71 | 1.54 |
| muenchian | 2.88 | 2.22 |
| string-recursion | 4.15 | 1.69 |
| key-join | 4.25 | 2.64 |

Rewrite time over stock 1.0 time, medians, 20,000 orders, on a loaded machine. The figures move by about 0.2 from
run to run.

What changed:

- **String functions on native strings.** `upper-case`, `lower-case`, `normalize-space`, `string-length`,
  `translate`, `concat`, `contains`, `starts-with`, `ends-with`, `substring-before`, `substring-after`, `string` and
  `tokenize` with a literal pattern compile to direct calls. This applies when each argument is certainly one string
  or one node; the comparisons also need the default collation to be codepoint. The calls use the same library
  helpers, with no item lists around them. A call declared to return strings marks its items, so an iteration over
  `tokenize()` has a known-string context item. String literals are known to the compiler.
- **Value templates.** Attribute and text value templates take a native string directly when the expression gives
  one string or one node.
- **`tokenize()` with a literal pattern** (no flags, no regex syntax) splits by ordinal search, which is what the
  regex would match.
- **`$map(key)`** looks the key up directly when the called value is a map, instead of building a dynamic call.
- **Argument conversion.** `item()*` parameters skip conversion. Atomization and type checks loop by index, with no
  enumerator.
- **`format-number`** caches parsed pictures. It takes a double's digits from the short-decimal search when it has at
  most 15 digits, which are the same digits `"R"` gives (a test checks this).
- **Casts to `xs:double`** of short decimals use the 1.0 path's exact fast parse.
- **Grouping by a single key** avoids per-item allocation.
- **Sort.** The 2.0+ sort compares its encoded keys ordinally (a new ordinal string sort key), not through ICU keys of
  hex strings.
- **Focus.** A 2.0+ stylesheet in which no template mentions `position`, `last` or `function-lookup` passes only the
  current node to its templates. Until now, every 2.0+ template took the full focus, so every `apply-templates`
  counted its items for `last()`. The analyzer can't follow 2.0+ expressions, so this is decided from the
  stylesheet's text, conservatively.

### Typed code generation, second batch

| Pair | First batch | Second batch |
|---|---:|---:|
| identity | 1.24 | 1.15 |
| sort | 1.54 | 1.60 |
| muenchian | 2.22 | 1.15 |
| string-recursion | 1.69 | 1.64 |
| key-join | 2.64 | 1.38 |

Same measure as above, the median of three runs. The machine was loaded for both columns, so differences under
about 0.2 aren't meaningful: sort and string-recursion are unchanged.

What changed:

- **`count()`** compiles to the length of its sequence, as a native number.
- **`sum()` of nodes** adds their values as doubles in one call. The fold order is the same as the library's.
- **Arithmetic on two nodes** goes straight to double arithmetic, without the general operator dispatch.
- **`format-number`** with a literal picture is a direct call. Doubles take a long-arithmetic path when the
  picture has no exponent, percent, per-mille or fraction grouping. That covers doubles of up to 17 round-trip digits
  and integers that fit a long. The path uses the same digits and the same half-to-even rounding as the general
  (BigInteger) path, and a test compares the two over thousands of values and pictures.
- **`@name`** compiles to a direct attribute step, known to give at most one node. So `string(@k)`, string-function
  arguments and value templates take it as a string. Arguments declared `xs:string?` accept zero or one node
  generally.
- **Value templates over several elements or attributes** join their string values with spaces directly.
- **`group-by`** groups string, untyped and URI keys under the codepoint collation by their strings, in a plain
  dictionary. A tuple with one key atomizes it straight into its own array.

A bug found on the way: the generated code passes a native value to a list parameter in a one-item list that it
reuses. `map:entry`, `map:put` and the `array:` functions that keep their argument now copy such lists. Before,
`for $k in … return map:entry($k, count(…))` could give every entry the last value.

### Typed code generation, third batch

| Pair | Second batch | Third batch |
|---|---:|---:|
| identity | 1.15 | 1.18 |
| sort | 1.60 | 1.05 |
| muenchian | 1.15 | 1.11 |
| string-recursion | 1.64 | 0.92 |
| key-join | 1.38 | 1.34 |

Same measure, the median of three runs, on a loaded machine. This batch is about sort and string-recursion; the other
three pairs moved only within the noise.

What changed:

- **`upper-case` and `lower-case` of ASCII strings** map with `System.Text.Ascii`, and return the string itself when
  nothing changes. Unicode's case mappings of ASCII characters are ASCII's own; a test checks the fast path against
  the tables for every ASCII character. This was most of string-recursion's extra cost: the general path looked
  each character up in a table and built the result in a `StringBuilder`.
- **Sort keys under the codepoint collation** are the strings themselves, not their UTF-8 bytes in hex. The
  characters from U+D800 up are rearranged so that ordinal order is still code point order. A key that is one node
  skips atomization.
- **`number()`** of a string or of nodes is a direct call. With the short-decimal parse, it doesn't go through the
  library.
- **Node arguments of string functions.** An argument declared `xs:string?` or `xs:anyAtomicType?` takes a node
  sequence the compiler can't bound, such as a child step, as a string. The call checks the count when it runs and
  raises XPTY0004 for more than one node, as before. Value templates still join several nodes with spaces.
- **`tokenize()` with a literal pattern** counts its tokens first and fills an array of the right size.

### The 2.0+ result writer

| Pair | Third batch | Direct writer |
|---|---:|---:|
| identity | 1.18 | 0.78 |
| sort | 1.05 | 1.11 |
| muenchian | 1.11 | 1.12 |
| string-recursion | 0.92 | 0.88 |
| key-join | 1.34 | 1.08 |

Same measure, the median of three runs. Sort and muenchian moved only within the noise.

A 2.0+ stylesheet's principal result used to go through `PrincipalResult` (serves `xsl:result-document` without
`href`) and then .NET's `XmlWriter.Create`. That meant `XmlWellFormedWriter` over System.Private.Xml's raw writer,
which re-checks every name and namespace and lacks Xsl3's vectorized escaping. In identity, that layer was about a
quarter of the time. When the stylesheet has no `xsl:result-document`, no serialization parameters beyond
`XmlWriterSettings` and the xml method, the Stream and TextWriter overloads now write with Xsl3's raw writer
directly, as 1.0 does. The settings, and so the XML declaration and the encoding as the stylesheet names it, are
those `PrincipalResult` would use. A test compares the bytes of the two paths over several `xsl:output`
declarations.

Removing `XmlWellFormedWriter` showed what it had been covering for:

- **`copy-of()` and `snapshot()`** dropped each copied element's own namespace declaration (`p` on `p:subelem`). It
  was redeclared only when serializing. Four more W3C tests now pass.
- **`xsl:namespace`** now raises its errors itself: XTDE0920 (the name), XTDE0925 (`xml` and the XML namespace),
  XTDE0905 (the xmlns namespace), XTDE0930 (an empty value). So does the result tree, for 2.0+: XTDE0440 (a default
  namespace on an element in no namespace) and XTDE0430 (the element's own prefix bound elsewhere). Before, these
  tests passed only because `XmlWellFormedWriter` threw an `XmlException`.

The built-in shallow-copy and shallow-skip rules now apply templates to `@* | node()` in one pass over the content,
which holds the attributes first and then the children. That is how 1.0's `@*|node()` is compiled. As one sequence,
`position()` now runs across the attributes and children, as the spec defines the rule.

### Map lookups

| Pair | Direct writer | Map lookups |
|---|---:|---:|
| identity | 0.78 | 0.80 |
| sort | 1.11 | 1.04 |
| muenchian | 1.12 | 1.07 |
| string-recursion | 0.88 | 0.70 |
| key-join | 1.08 | 1.00 |

Same measure, the median of three runs. Only key-join's code changed; the other pairs moved within the noise.

An XDM map is immutable, so updates share structure: it is an `ImmutableDictionary`, which is a balanced tree.
Looking up a key walks the tree. A map that is read again and again, such as key-join's product table read once per
order line, now gets a plain hash table of its entries after 16 reads, if it has at least 8 entries. Maps that are
built, read a few times and updated don't pay for it. Since the map can't change, the table never goes stale.

### Groups and number sort keys

| Pair | Map lookups | Groups and sort keys |
|---|---:|---:|
| identity | 0.80 | 0.78 |
| sort | 1.04 | 0.83 |
| muenchian | 1.07 | 0.83 |
| string-recursion | 0.70 | 0.74 |
| key-join | 1.00 | 1.03 |

Same measure, the median of three runs.

- **Steps from `current-group()`.** A group's members are a subsequence of the population, in population order.
  When the population is a path of child steps (`order`, `a/b[@x]`), its nodes are in document order, distinct
  and at one depth, and so are the members of every group. `current-group()` then gives them as nodes marked as
  such. A child or attribute step from them compiles to a plain loop, without the merge into document order
  (`ContentMergeIterator`) and the sort that steps from arbitrary nodes need.
- **`sum()` of nodes** reads their values as a stream of native doubles (`IList<double>`). It no longer takes a
  list of cloned nodes. The values are added in order from the first, as before; no nodes is `xs:integer` 0. The
  IL generator now passes an empty sequence to an `IList<T>` parameter for any native `T`, not only for `XPathItem`.
- **Sort keys of `number(...)`** are native doubles, sorted as 1.0 sorts numbers. The value is always one
  `xs:double`, so no check is needed that the keys are comparable. NaN sorts least and -0 equals 0, as in the
  general string encoding; a test compares the two.

### Measured again (2026-10-21)

| Pair | main | remaining-scope |
|---|---:|---:|
| identity | 0.82 | 0.80 |
| sort | 0.71 | 0.68 |
| muenchian | 1.14 | 1.15 |
| string-recursion | 0.76 | 0.74 |
| key-join | 1.11 | 1.12 |

One `pairs 20000` run of each, back to back on the same machine. The conformance work on the remaining-scope
branch (docs/plan/remaining-scope.md) leaves the pairs where main has them. Muenchian and key-join measure above
the earlier table's 0.83 and 1.03 on both, so that difference is the machine and the run, not a code change; both
are still above the ≤ 1.0 target.

### Every pair under 1× (2026-10-21)

| Pair | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|
| identity | 0.87 | 0.86 | 0.85 |
| sort | 0.74 | 0.71 | 0.71 |
| muenchian | 0.97 | 0.95 | 0.98 |
| string-recursion | 0.78 | 0.77 | 0.75 |
| key-join | 0.95 | 0.95 | 0.98 |

Three `pairs 20000` runs in a row, rewrite time over stock 1.0 time. Before these changes muenchian measured
1.07–1.22 and key-join 1.08–1.13 on the same machine. The changes:

| Change | Where | Pair |
|---|---|---|
| `format-number` rounds a double by scaling when the scaled value is below 10^9 and more than 10^-6 from a half, instead of finding its shortest round-trip digits (Dragon4); near a half the exact digits still decide | `NumberFormatter.TryScaledDigits` | key-join |
| A map built in one go (`map:merge`, constructors, JSON) keeps a hash table, and makes the persistent dictionary only when it is first updated | `MapItem.Of` | key-join |
| `$map(string-expression)` looks the string up without making an atomic value for it | `XPath31Compiler.DynamicFunctionCall`, `X31MapLookupString` | key-join |
| A `group-by` key that is one node is kept as its string, and grouped by it under the codepoint collation without an atomic value and an array per item | `X31GroupTuple`, `X31Groups` | muenchian |

## The XSLT 3.0 micro suite (remaining-scope 1.3, 1.6, 1.7)

`bench/corpus/xslt30/micro` has one stylesheet per feature: grouping, sorting, keys, regex, string functions, maps,
`fold-left` and other higher-order functions, `xsl:iterate`, the identity transform by `on-no-match`, deep recursion,
and json output. `micro` runs each on the order book at about 1 KB and 1 MB (and 100 MB with `--sizes`). It also
times compiling each stylesheet and the cold start of the first, a compile and transform in a fresh process. Each row
has the median and minimum time and the bytes allocated per run.

Measured on 2026-10-21 (Apple M-series, .NET 10, Release, input parsed once into Xsl3's compact tree):

| Benchmark | Compile | KB | 1 KB input | 1 MB input | KB per run, 1 MB |
|---|---:|---:|---:|---:|---:|
| grouping | 0.65 ms | 207 | 0.010 ms | 0.91 ms | 1,947 |
| sorting | 0.51 ms | 193 | 0.009 ms | 2.26 ms | 4,007 |
| keys | 0.38 ms | 210 | 0.016 ms | 3.50 ms | 6,253 |
| regex | 0.12 ms | 172 | 0.029 ms | 25.8 ms | 14,838 |
| strings | 0.09 ms | 182 | 0.015 ms | 2.77 ms | 6,392 |
| maps | 0.17 ms | 313 | 0.034 ms | 4.48 ms | 13,664 |
| fold-left | 0.18 ms | 308 | 0.009 ms | 3.06 ms | 12,413 |
| iterate | 0.16 ms | 303 | 0.010 ms | 1.80 ms | 6,941 |
| identity | 0.05 ms | 98 | 0.008 ms | 1.90 ms | 3,293 |
| recursion | 0.13 ms | 287 | 0.005 ms | 1.15 ms | 3,494 |
| json | 0.07 ms | 178 | 0.020 ms | 9.14 ms | 18,073 |

The cold start (the first compile and transform in a process, Xsl3's own JIT included) is 129 ms. The first run of
the suite found two things, now fixed:

| Finding | Before | After | Change |
|---|---:|---:|---|
| recursion, 1 MB | 101 ms | 1.15 ms | `$items[$i]` with a predicate that can't depend on the focus is evaluated once and selects by index (`X31Subscript`); a sequence already converted to a parameter's type is passed on to it without being checked again (`CoercedSequence`) |
| Compile allocation | 106.8 KB (identity) | 97.6 KB | The scan for `position()` and `last()` stopped following a template's back-reference to its module, and caches each AST type's fields |

Later rounds, each measured on the same machine:

| Change | Benchmark | Before | After |
|---|---|---:|---:|
| Automatic indexing (remaining-scope 1.5): an absolute path whose last step filters on one attribute or child compared with a value that doesn't depend on the focus (`/orders/product[@sku = $l/@sku]`) builds an index of the document on its second evaluation and looks values up from then on. It applies where `=` compares strings (codepoint collation, no XPath 1.0 mode, string or node values, checked on each use); otherwise the path runs as written | join, 1 MB | 288 ms | 2.3 ms (the `xsl:key` version: 3.6 ms) |
| General comparisons between nodes and strings compare string values directly, without atomizing into new values | join with the index defeated, 1 MB | 273 ms, 753 MB allocated | 177 ms, 207 MB |
| A built-in function's argument coercion makes the message naming the argument only on an error, not as a closure per argument and call | allocation at 1 MB | | regex −14%, strings −16%, maps −15%, sorting −5%, identity −6% |

**Template dispatch (remaining-scope 1.4), measured and left.** The `dispatch` micro benchmark adds 200 template rules
for names that never occur ahead of the ones that do. They cost 0.30 ms per 1 MB transform (1.34 ms against 1.04 ms),
about 17 ns per element for 200 name tests. A name table would save part of that only in stylesheets with hundreds of
rules, so the linear test of fixed names stays until a real stylesheet shows it. QIL's `Choice` can't hold the
per-name matchers (it works in push mode only), so a table would mean a runtime name index and a tree of integer
tests.

regex is still the slowest per byte: `xsl:analyze-string` and `replace` per order allocate about 5.8 KB each, mostly
.NET's `Match` and `Group` objects.

**The CI gate.** `eng/perf-gate.sh <base>` builds this checkout's harness against the base commit's Xsl3 and against
this checkout's. It runs the suite alternately on each, three rounds in CI, so runner noise hits both. It fails when
the geometric mean of the time ratios grows by more than 5%, or when any benchmark allocates more than 5% (and 1 KB)
more. Allocation is near-deterministic, so it is the sharper test; cold start is reported but not gated. A deliberate
trade fails the gate until the owner labels the pull request `perf-accepted`. Automatic indexing was one: its first
version compiled three forms of an indexed path (the join stylesheet's compile allocation up 18% for a transform 100
times faster). Now the candidates are compiled once and one pass either filters them or builds the index, and a
compilation parses each XPath text once (1-12% less compile allocation across the suite); what is left is the index
plumbing, about 7% of that ten-line stylesheet's compile allocation. CI's `perf`
job runs it on every pull request against the target branch. `perf-nightly.yml` runs the suite at all three sizes
and the rewritten pairs every night, as an artifact. `micro --alloc-types` adds the runtime's allocation samples by
type to each row, which is how the compile allocation above was found.

**Measurement update, 2026-10-22.** Warmup now runs for the requested `--ms` duration (at least three calls),
followed by a measurement phase of that duration (at least five calls). The former 10,000-sample cap could end
small transforms well before the requested measurement duration. An identical-binary, three-pair control with
the old harness reported a 6.3% slowdown and failed the gate; the duration-based control reported 0.7% and passed,
with unchanged allocation. The gate's inputs, aggregation and thresholds are unchanged, and its base and head
always use the same harness. Earlier tables retain their original measurements; absolute timings should not be
compared across these harness versions.

## Real stylesheets: the TEI Stylesheets (remaining-scope 1.3)

`eng/fetch-bench.sh` fetches the TEI Stylesheets (BSD-2-Clause or CC BY-SA 3.0) at a pinned commit; they aren't
committed. `tests/Xsl3.W3C/TeiStylesheetTests` runs seven of their test suite's conversions (HTML of two documents,
LaTeX of four, text of one) against the expected results, made with Saxon, and they agree word for word apart from
the dates of the run. `dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- tei` times four
conversions of `Test/test14.xml` (550 KB); the nightly workflow runs it.

Measured on 2026-10-21, after a temporary tree stopped allocating a 256-entry value table it then replaced, and got
its attribute and namespace arrays only with its first attribute or namespace node (allocation before both in
brackets):

| Stylesheet | Compile | Transform | Allocated per transform |
|---|---:|---:|---:|
| html/html.xsl | 241 ms | 470 ms | 291 MB (426 MB) |
| latex/latex.xsl | 90 ms | 272 ms | 439 MB (557 MB) |
| fo/fo.xsl | 90 ms | 67 ms | 227 MB (356 MB) |
| txt/tei-to-text.xsl | 12 ms | 29 ms | 83 MB (97 MB) |

These stylesheets build many small temporary trees and copy navigators freely; `tei --alloc-types` shows where the
rest goes (item arrays, navigators, item sequences). XMark isn't run: its generator is a C program, to be fetched and
built separately.

## Real stylesheets: DocBook xslTNG (remaining-scope 1.3)

`eng/fetch-bench.sh` also fetches the DocBook xslTNG 2.7.1 release (MIT; the zip without Saxon's jars, checked against
its SHA-256). `tests/Xsl3.W3C/DocBookStylesheetTests` converts its sample article and `tests/Xsl3.W3C/docbook/book.xml`
(lists, sections, a table, a program listing, cross references, a footnote, an index) to HTML and checks the structure;
there is no expected output to compare with. `-- docbook [--chapters n] [--transform-only] [--alloc-types]` times the
compile and a book of the test book's chapters repeated.

Getting it to run found five faults, fixed on 2026-10-21: the static variables of an imported module weren't visible
after the `xsl:import` (static-021), `xsl:next-match` from a union pattern's rule broke when it passed on one of the
rule's parameters, a namespace node couldn't follow attributes, `xsl:call-template` lost the current group, and the
`xsl:evaluate` cache (64 expressions) was smaller than the 151 localization patterns DocBook evaluates in turn from one
instruction, so each was compiled again every time. Then the names pool got a per-thread cache in front of its lock.

| Measure (Release, .NET 10) | Before the fixes | After |
|---|---:|---:|
| Compile `docbook.xsl` (warm) | | 351 ms, 180 MB |
| Transform, 150 KB book | 6,599 ms, 1,958 MB | 1,247 ms, 1,554 MB |
| Transform, 301 KB book | 14,957 ms, 3,893 MB | 2,332 ms, 3,088 MB |

Then the allocation, on 2026-10-21, guided by `docbook --alloc-types`: a sequence allocates its item array on its first
item, four slots and growing, not sixteen up front (most sequences hold none or one); the run time keeps finished
`XmlQueryOutput` objects for the next temporary tree or sequence; `xsl:evaluate` builds the target's static context
only when the expression isn't cached; and the XTDE0430 check reads the pending namespace declarations instead of
copying the scope into a dictionary. 301 KB book: 3,088 MB to 2,426 MB a run (−21%), 2.3 to 2.5 s (within this
machine's noise). The micro benchmarks allocate up to 22% less (recursion 1 MB), with no time regression
(`eng/perf-gate.sh`, geometric mean 0.96). What is left at the top is navigators, the node arrays of the temporary
trees, and the trees themselves. Compiling: the XPath compiler makes its namespace resolver and its static-context
constant only when an expression needs them, and boxing a value shares one static context, so compiling `docbook.xsl`
allocates 139 MB, not 179 MB, and the micro benchmarks' compiles up to 23% less (geometric mean of time 0.94).

## Against SaxonCS-HE (remaining-scope 3, SPEC §19.6)

The XSLT 3.0 micro suite on Xsl3 and on SaxonCS-HE 13.1.0, "The Saxon XSLT and XQuery Processor from Saxonica
Limited" ([saxonica.com](https://www.saxonica.com/); MPL-2.0, the free .NET edition). Times are milliseconds, and lower
is better. "Faster" names the winner of each row; within 3% counts as about the same.

Run on 2026-10-22 · .NET 10.0.11 · macOS 27 · Apple M4 Pro · Xsl3 at f691818 (main after 1.0.0) · `dotnet run -c Release
--project bench/Xsl3.Benchmarks -f net10.0 -p:Saxon=true -- saxon --sizes 1kb,1mb --cold-runs 5`, three times; each
figure is the median of the three runs. The 1.0.0 column is the same command on 1.0.0's code. Both engines read the
same document, parsed once into each engine's own tree (whitespace-only text left out, as `XPathDocument` and Xsl3's
compact tree do), write to `Stream.Null`, and gave identical output for all 13 tests in every run.

### Run speed, 1 MB input

Xsl3 wins 13 of 13. At the median Xsl3 is 1.33× Saxon's speed; 1.0.0 was 1.22×, and lost 2 (iterate, regex).

| Test | Xsl3 1.0.0 ms | Xsl3 ms | Saxon ms | Faster |
|---|---|---|---|---|
| join | 2.55 | 2.31 | 68.53 | Xsl3, 29.6× faster |
| identity | 2.04 | 1.94 | 4.94 | Xsl3, 2.5× faster |
| regex | 28.77 | 5.95 | 13.99 | Xsl3, 2.4× faster |
| dispatch | 1.49 | 1.38 | 3.14 | Xsl3, 2.3× faster |
| maps | 4.60 | 4.21 | 5.63 | Xsl3, 34% faster |
| json | 9.90 | 9.50 | 12.64 | Xsl3, 33% faster |
| keys | 3.87 | 3.51 | 4.66 | Xsl3, 33% faster |
| fold-left | 3.38 | 2.72 | 3.58 | Xsl3, 32% faster |
| iterate | 2.09 | 1.17 | 1.53 | Xsl3, 32% faster |
| sorting | 2.78 | 2.13 | 2.77 | Xsl3, 30% faster |
| grouping | 0.93 | 0.84 | 1.02 | Xsl3, 22% faster |
| recursion | 1.31 | 1.24 | 1.47 | Xsl3, 18% faster |
| strings | 3.07 | 2.91 | 3.31 | Xsl3, 14% faster |

### Run speed, 1 KB input

A run at this size takes a few microseconds, so the order the engines run in matters: whichever runs second finds
the caches and the JIT in a different state. The first measurements here ran Xsl3 before Saxon on every row, and
their 1 KB results changed when a test ran on its own. These rows are measured again, with the writer change below and the order alternated: six
rounds per row, the engine that goes first swapped each round, three seconds per row (`--ms 3000`), three runs. Every
row had the same winner in all three runs (the last column).

Xsl3 wins 9 of 13, Saxon 4 (dispatch, grouping, iterate, recursion). Saxon's wins are by 0.3 to 1.8 µs a run.

| Test | Xsl3 ms | Saxon ms | Faster | Saxon's time / Xsl3's, per run |
|---|---|---|---|---|
| identity | 0.0038 | 0.0087 | Xsl3, 2.3× faster | 2.28, 2.29, 2.35 |
| regex | 0.0062 | 0.0101 | Xsl3, 63% faster | 1.63, 1.64, 1.51 |
| fold-left | 0.0056 | 0.0089 | Xsl3, 59% faster | 1.62, 1.56, 1.59 |
| join | 0.0048 | 0.0075 | Xsl3, 56% faster | 1.60, 1.56, 1.58 |
| keys | 0.0057 | 0.0079 | Xsl3, 39% faster | 1.40, 1.39, 1.34 |
| maps | 0.0083 | 0.0115 | Xsl3, 39% faster | 1.41, 1.39, 1.40 |
| sorting | 0.0040 | 0.0055 | Xsl3, 38% faster | 1.38, 1.50, 1.22 |
| json | 0.0072 | 0.0097 | Xsl3, 35% faster | 1.35, 1.35, 1.35 |
| strings | 0.0047 | 0.0059 | Xsl3, 26% faster | 1.31, 1.23, 1.28 |
| iterate | 0.0058 | 0.0055 | **Saxon**, 5% faster | 0.95, 0.93, 0.95 |
| recursion | 0.0048 | 0.0044 | **Saxon**, 9% faster | 0.94, 0.88, 0.89 |
| grouping | 0.0085 | 0.0074 | **Saxon**, 15% faster | 0.87, 0.96, 0.85 |
| dispatch | 0.0085 | 0.0067 | **Saxon**, 27% faster | 0.79, 0.79, 0.77 |

Every transformation to a stream or a `TextWriter` makes a writer, and its buffer, 6 KB of bytes for UTF-8 or 12 KB
of characters otherwise, was most of what a small transformation allocated: an almost empty stylesheet allocated
11,032 bytes a run, now 4,832. The writers now keep one buffer per thread for the next writer (`WriterBuffers`).
Allocation per 1 KB run fell by 6.1 KB at the median (json, which writes through its own serializer, is unchanged);
Xsl3 now allocates less than Saxon on every row but iterate (22.5 KB against 22.1 KB). It saves allocation, not
time: `eng/perf-gate.sh` against the commit before shows 18–36% less allocation on the 1 KB rows and no measurable
change in their time.

### Start-up

Loading the stylesheet and running it once on the 1 MB input, in a new process, with the input already parsed;
the median of five processes per run. "Precompiled" is Xsl3 loading the stylesheet from an assembly saved by
`CompileToAssembly`, as Xsl3.Build does at build time. Xsl3 wins 13 of 13 from source, and 13 of 13 precompiled.

| Test | Xsl3 ms | Xsl3 precompiled ms | Saxon ms | Faster: Xsl3 or Saxon | Faster: Xsl3 precompiled or Saxon |
|---|---|---|---|---|---|
| join | 125 | 50 | 415 | Xsl3, 3.3× faster | Xsl3, 8.3× faster |
| regex | 157 | 96 | 220 | Xsl3, 40% faster | Xsl3, 2.3× faster |
| dispatch | 111 | 41 | 153 | Xsl3, 38% faster | Xsl3, 3.7× faster |
| json | 144 | 75 | 183 | Xsl3, 27% faster | Xsl3, 2.4× faster |
| identity | 121 | 51 | 152 | Xsl3, 26% faster | Xsl3, 3.0× faster |
| keys | 135 | 57 | 165 | Xsl3, 22% faster | Xsl3, 2.9× faster |
| grouping | 126 | 48 | 148 | Xsl3, 18% faster | Xsl3, 3.1× faster |
| recursion | 131 | 51 | 152 | Xsl3, 16% faster | Xsl3, 3.0× faster |
| maps | 162 | 81 | 183 | Xsl3, 13% faster | Xsl3, 2.3× faster |
| strings | 138 | 59 | 155 | Xsl3, 12% faster | Xsl3, 2.6× faster |
| fold-left | 149 | 58 | 166 | Xsl3, 11% faster | Xsl3, 2.8× faster |
| iterate | 136 | 52 | 149 | Xsl3, 10% faster | Xsl3, 2.9× faster |
| sorting | 141 | 57 | 149 | Xsl3, 6% faster | Xsl3, 2.6× faster |
| **median** | **136** | **57** | **155** | **Xsl3, 14% faster** | **Xsl3, 2.7× faster** |

### Compile and memory

- Compiling a stylesheet, warm, in one process: Xsl3 wins 10 of 13, Saxon 3 (dispatch, fold-left, iterate). Median 0.13 ms against 0.15 ms.
- Memory allocated per 1 MB run: Xsl3 allocates 49% of Saxon's at the median (join 2%, fold-left 66%).
- Peak memory of the start-up process: Xsl3 uses 7% less.

### What changed since 1.0.0

- **regex:** XPath's `\w` (and `.` and every character class) was translated to a .NET pattern with an alternation for
  the supplementary planes; `(\w)(\w*)` became 28 KB, and .NET couldn't run it as a set loop. Each regex now also has
  a translation for strings without surrogates, a plain character class, which `XPathRegex.For` picks after a
  vectorised check of the input: `xsl:analyze-string` with `\w` is 20× faster.
- **iterate:** each step of `xsl:iterate` is a dynamic call of its body. A dynamic call no longer makes a closure
  per argument for its error message, nor copies arguments `xsl:iterate` has already copied; `fn:min` and `fn:max`
  of values of one numeric type take one pass without LINQ. fold-left and maps gain from the same dynamic-call change.

### Against the targets

SPEC §19.6 asks for 2.0/3.0 stylesheets to run at least twice as fast as on SaxonCS-HE and never slower, and to start
at least twice as fast, ten times precompiled. Xsl3 is never slower at 1 MB now, but the rest isn't met: run speed is
1.33× Saxon's at the median, start-up 1.14× from source and 2.71× precompiled.

## Where the rest of the time goes

The remaining cost is mostly shared with stock and outside Xsl3's code:

- **Navigating `XPathDocument`**, in System.Private.Xml. On a 20,000-order document the node pages and strings
  don't fit in cache, so child steps and string values are memory-bound.
- **ICU sort-key generation** for culture-aware sorts.
- **The writer pipeline** of `XmlQueryOutput`.

muenchian is the clearest case. Its key lookups visit each customer's orders across the whole document. The
compact tree (above) helps when the transform builds the tree itself.

## Measuring

`dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- pairs [orders] [name...]` runs the rewritten
pairs: stock and Xsl3 on each 1.0 benchmark and Xsl3 on its 3.0 rewrite, interleaved, checking the outputs agree
(`XSL3_PAIRS_DUMP=dir` writes them when they don't). `--filter '*Xsl3Profile30*'` profiles the 3.0 stylesheets.
`dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- quick30 [orders] [stylesheet]` times an XSLT
3.0 stylesheet from `bench/corpus/xslt30` on Xsl3 alone; `XSL3_COMPACT_TREE=0` gives it XPathDocument trees for
comparison.
`dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- quick [orders] [stylesheet...]` runs the
two engines interleaved, checks their output is identical, and prints the median and minimum ratios. Set
`XSL3_QUICK_MS` to change the time per engine (default 3000). BenchmarkDotNet is the record. CPU profiles:
`--filter '*Xsl3Profile*' --job short` writes speedscope traces to the artifacts folder.
`dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -p:Saxon=true -- saxon [--sizes 1kb,1mb] [--ms budget]
[--cold-runs n] [--json file] [name...]` runs the comparison with SaxonCS-HE; without `-p:Saxon=true` the project doesn't
reference Saxon.

## P7 analysis development

The streaming-expression analyser serves the W3C `sandp` profile and the compiler's declaration checks.
The default `strm` development profile still builds trees. Strict forward probes and runtime unit tests
exercise the development switch, but retained-memory and quantitative first-output latency measurements
remain outstanding. See [the P7 plan](plan/p7-streaming.md#s5-streamed-merge).

After streamed accumulator observers (2026-10-23), the final three-pair ordinary-path gate against `0f2ddcb`
passes with geometric mean head/base time **1.002** over 39 timed rows and maximum reported allocation ratio
**1.029**. Keys compilation remains **1.225** and 1 MB strings execution **1.084**; these individual outliers
remain investigation items despite the aggregate pass. This gate uses the same current harness and thresholds
on both sides and does not establish forward retained-memory or latency budgets.

After forward grouping, a fresh three-pair gate against the same base passes with geometric mean **1.009**
and maximum reported allocation ratio **1.031** (cold start). Sorting compilation is **1.181**, dispatch
**1.027**, grouping **1.014** and keys **0.961**. The sorting outlier and earlier compilation variation remain
investigation items. This ordinary-path gate does not measure the new forward runtime's throughput or its S7
memory and first-output latency targets.


After streamed merge and the key-option/comparison fixes, the final three-pair gate against `0f2ddcb` passes
with geometric mean **1.004** and maximum reported allocation ratio **1.033** (cold start). Keys compilation
is **1.149**, dispatch **1.074**, grouping **1.020** and sorting **0.932**. The keys/dispatch outliers and earlier
compilation variation remain investigation items. Both sides use the same current harness and thresholds.
Forward throughput, retained memory and quantitative first-output latency remain unmeasured. See
[merge runtime evidence and limits](plan/p7-streaming.md#s5-streamed-merge).

S2 compiler analysis runs only when the loader has seen a declared-streamable component. Ordinary stylesheets
skip declaration analysis and XPath syntax caching. QIL annotations carry declaration verdicts into `Explain`.
These analysis checks do not measure streamed execution. See
[S2 progress](plan/p7-streaming.md#s2-compiler-enforcement) for the measured performance gate.

The initial forward navigator is tested over 100 and 10,000 records with at most four cursor frames retained
for that shape. Compiled count/sum and per-record grounding tests consume its reader. These are functional
and structural checks; they do not establish the process-memory budgets or the strict streamed-suite exit.
See [S3 foundation](plan/p7-streaming.md#s2-package-compatibility-and-s3-forward-navigator-foundation).


After the S6 logical fork-input adapter, the final three-pair ordinary-path gate against `0f2ddcb` passes:
geometric mean **0.997**, maximum reported allocation ratio **1.033** (cold start). Sorting compilation is
**1.205**, grouping **1.068**, dispatch **1.026** and keys **0.914**. Sorting's outlier and the preceding
compilation variation remain investigation items. The current harness and thresholds are shared by both sides;
this gate does not establish fork throughput, memory or first-output latency. See
[adapter evidence and limits](plan/p7-streaming.md#s6-streamed-fork-input-views).

After fork accumulator/copy-provenance routing, the final three-pair ordinary-path gate against `0f2ddcb`
passes: geometric mean **0.986**, maximum reported allocation ratio **1.033** (cold start). Compilation ratios
include dispatch **1.065**, grouping **0.975**, sorting **0.865** and keys **0.652**. The variation across
increments remains unresolved; both sides use the same current harness and thresholds. This does not establish
fork throughput, retained-byte budgets or first-output latency. See
[accumulator routing evidence and limits](plan/p7-streaming.md#s6-fork-accumulator-values-and-copy-provenance).

After shared global bindings and generated failure publication, the final three-pair ordinary-path gate against
`0f2ddcb` passes: geometric mean **1.010**, maximum reported allocation ratio **1.033** (cold start). Compilation
ratios include dispatch **1.114**, sorting **1.137**, grouping **1.043** and keys **0.961**. Dispatch/sorting
outliers and variation across preceding runs remain investigation items. Both sides use the same current
harness and thresholds; this does not establish fork memory, throughput or output latency. See
[shared-global evidence and limits](plan/p7-streaming.md#s6-shared-global-bindings).

After shared document resources, the final three-pair ordinary-path gate against `0f2ddcb` passes: geometric
mean **1.015**, maximum reported allocation ratio **1.033** (cold start). Time outliers include cold grouping
**1.151**, 1 KB sorting **1.292** and 1 MB JSON **1.108**; compilation ratios include keys **1.062**, dispatch
**1.023**, grouping **1.000** and sorting **0.998**. Outliers and prior variation remain investigation items.
Both sides use the same current harness and thresholds. This does not establish fork throughput, memory or
first-output latency. See [shared-document evidence and limits](plan/p7-streaming.md#s6-shared-document-resources).

After external DTD/entity resource tracking and reader cleanup fixes, the final three-pair ordinary-path gate
against `0f2ddcb` passes: geometric mean **1.022** over 39 timed rows, maximum reported allocation ratio **1.033**
(cold start). Time outliers include 1 KB sorting **1.292**, 1 MB regex **1.068** and cold grouping **1.066**;
compilation ratios include dispatch **1.046**, keys **1.033**, grouping **1.021** and sorting **0.919**. The
recurring small-input sorting outlier and prior variation remain investigation items. Both sides use the same
current harness and thresholds. This does not establish fork throughput, retained memory or first-output
latency. See [entity tracking evidence and limits](plan/p7-streaming.md#s6-external-entity-resource-tracking).

After isolated fork runtimes, captured-function dispatch and shared-exception publication fixes, the final
three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean **0.994** over 39 timed rows, maximum
reported allocation ratio **1.033** (cold start). Time outliers include keys compilation **1.256**, join
compilation **1.109** and 1 MB grouping **1.073**; other compilation ratios include dispatch **1.017**, grouping
**1.002** and sorting **1.011**. The preceding small-input sorting outlier is **0.920** in this run. These
outliers and variation remain unresolved; the run does not attribute their cause. Both sides use the same
current harness and thresholds. This does not establish fork throughput, retained-memory bounds or first-output
latency. See [fork-runtime evidence and limits](plan/p7-streaming.md#s6-isolated-fork-runtimes-and-execution-scopes).

After shared key indexes, generated failure publication and cross-cache dependency tracking, the final three-pair
ordinary-path gate against `0f2ddcb` passes: geometric mean **1.004** over 39 timed rows, maximum reported
allocation ratio **1.033** (cold start). Time outliers include 1 MB iterate **1.166**, 1 KB regex **1.125**,
1 MB regex **1.099** and join compilation **1.061**. Other compilation ratios include keys **0.895**, dispatch
**1.027**, grouping **1.019** and sorting **0.999**; 1 MB keys is **1.006**. These outliers and preceding
variation remain unresolved. Both sides use the same current harness and thresholds. This does not establish
fork throughput, retained-memory budgets or first-output latency. See
[shared-key evidence and limits](plan/p7-streaming.md#s6-shared-key-indexes-and-cross-cache-dependencies).

After shared grounded accumulator values, iterative traversal and evaluation-scope cleanup, the final three-pair
ordinary-path gate against `0f2ddcb` passes: geometric mean **0.999** over 39 timed rows, maximum reported
allocation ratio **1.033** (cold start). Time outliers include 1 KB regex **1.146**, 1 MB iterate **1.130**,
dispatch compilation **1.082**, iterate compilation **1.067** and strings compilation **1.055**. Other compilation
ratios include keys **0.628**, sorting **0.876** and grouping **1.043**. These outliers and preceding variation
remain unresolved. Both sides use the same current harness and thresholds. This does not measure grounded
accumulator throughput or establish fork retained-memory/first-output latency budgets. See
[grounded accumulator evidence and limits](plan/p7-streaming.md#s6-shared-grounded-accumulator-values).

After captured source-binding projection, the final three-pair ordinary-path gate against `0f2ddcb` passes:
geometric mean **0.973** over 39 timed rows, maximum reported allocation ratio **1.033** (cold start). Time
outliers include 1 MB iterate **1.116**, cold start **1.112** and dispatch compilation **1.076**. Keys compilation
is **0.691**, sorting compilation **0.844** and grouping compilation **1.022**. These outliers and preceding
variation remain unresolved. Both sides use the same current harness and thresholds. This does not measure
binding-projection overhead or establish fork throughput, retained-memory or first-output latency budgets. See
[binding projection evidence and limits](plan/p7-streaming.md#s6-captured-source-binding-projection).

After caller-side prepared binding snapshots and retained header views, the final three-pair ordinary-path gate
against `0f2ddcb` passes: geometric mean **0.996** over 39 timed rows, maximum reported allocation ratio **1.034**
(cold start). Time outliers include 1 KB regex **1.167** and dispatch compilation **1.072**. Keys compilation is
**0.802**, sorting compilation **1.001** and grouping compilation **0.991**. These outliers and preceding variation
remain unresolved. Both sides use the same current harness and thresholds. This does not measure preparation or
projection throughput, header retention in bytes, fork throughput or first-output latency. See
[prepared binding evidence and limits](plan/p7-streaming.md#s6-prepared-binding-snapshots-and-retained-headers).

After compiled fork lowering, caller-thread output relay and result-document callback coordination, the final
three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean **0.999** over 39 timed rows, maximum
reported allocation ratio **1.034** (cold start). The largest timed ratio is grouping compilation **1.032**;
cold-start time is **1.034**. Dispatch compilation is **1.022**, keys compilation **0.963** and sorting compilation
**0.993**. No timed row exceeds 1.05 in this run; earlier variation remains unresolved. Both sides use the same
current harness and thresholds. This does not establish fork throughput, retained-memory or latency budgets.
See [compiled fork evidence and limits](plan/p7-streaming.md#s6-compiled-fork-prongs-and-ordered-output).

After fork grouping buffers, captured forward groups and shared native-document creation order, the final
isolated three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean **1.018** over 39 timed rows,
maximum reported allocation ratio **1.034** (cold start). Individual time regressions remain unresolved:
keys compilation **1.215**, sorting compilation **1.075**, and 1 KB regex execution **1.170**. Cold-start time is
**1.011**; dispatch and grouping compilation are **1.007** and **1.020**. Tests and native compilation had
finished before the measurement. Both sides use the same current harness and thresholds; this final gate was
not rerun. Fork throughput, replay-buffer bytes, retained memory and first-output latency remain unmeasured.
See [grouping evidence and limits](plan/p7-streaming.md#s6-fork-grouping-buffers-and-captured-forward-groups).

After principal fork coordination, raw result buffering and constants format 3, the final isolated three-pair
ordinary-path gate against `0f2ddcb` passes: geometric mean **0.992** over 39 rows and maximum allocation ratio
**1.035** (cold start). Individual time regressions remain unresolved: 1 MB iterate **1.166** and dispatch
compilation **1.065**. Keys compilation is **0.527**, with base/head times **0.615/0.324 ms** and substantial
variation in base compilation compared with previous runs; this does not establish an improvement. Sorting and
grouping compilation are **0.983/0.968**, and cold-start time **1.004**. Tests and NativeAOT jobs had finished;
both sides used the same current harness and thresholds. This final gate was not rerun. Principal/fork
throughput, replay bytes, retained memory and first-output latency remain unmeasured. See
[principal evidence and limits](plan/p7-streaming.md#s6-principal-result-document-coordination).

After iterator-backed sequence parameters, recursive grouped forks and conservative child-path ordering proofs,
the final isolated three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean **1.010** over 39 rows,
maximum allocation ratio **1.036** (cold start). Individual time regressions remain unresolved: 1 KB regex
**1.128** and dispatch compilation **1.059**. Keys, sorting and grouping compilation are **0.944/0.927/1.004**;
cold-start time is **1.010**. All test and NativeAOT jobs finished before measurement, both sides used the same
current harness and thresholds, and this final gate was not rerun. This does not establish streamed parameter
or fork throughput, replay bytes, retained-memory budgets or first-output latency. See
[parameter evidence and limits](plan/p7-streaming.md#s4-iterator-backed-sequence-parameters-and-recursive-forks).

After the result-tree audit, namespace-origin correction and XSLT map error contract, the final isolated
three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean **1.027** over 39 rows and maximum
allocation ratio **1.036** (cold start). Individual time regressions remain unresolved: keys compilation
**1.880** (base/head **0.343/0.645 ms**), sorting compilation **1.204**, 1 KB regex **1.125**, and 1 KB dispatch
**1.053**. Dispatch/grouping compilation are **1.046/1.048** and cold-start time is **1.019**. Tests and native
jobs had finished before measurement; both sides used the same current harness and thresholds. This final
gate was not rerun. These measurements do not establish streaming throughput, retained-memory, replay-byte or
latency budgets. See [audit evidence and limits](plan/p7-streaming.md#s1s6-result-tree-assertions-namespace-origins-and-host-map-errors).
Keys compilation varies across the retained rounds: base **0.3428/0.6364/0.3212 ms**, head
**0.66135/0.64455/0.3490 ms**. Sorting is base **0.5158/0.6464/0.5264 ms**, head
**0.6337/0.6436/0.5053 ms**. Their cause and attribution remain unresolved.


After shared forward union/descendant paths, incremental EBV, iterator removal and the self-only union
correction, the final isolated three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean **1.016**
over 39 rows and maximum allocation ratio **1.037** (cold start). Individual time regressions remain
unresolved: 1 MB iterate **1.176**, 1 KB regex **1.149**, grouping compilation **1.138** and sorting compilation
**1.058**. Dispatch/keys compilation are **0.994/0.952**; cold-start time is **1.004**. Functional and native
jobs had finished, both sides used the same current harness and thresholds, and the final gate was not rerun.
The changed medians do not resolve earlier outliers or establish streaming throughput, memory, replay-byte or
latency budgets. Retained rounds and limits are recorded in
[path and inspection evidence](plan/p7-streaming.md#s4-shared-forward-paths-and-incremental-sequence-inspection).


After native cardinality assertions and sequence insertion, the final isolated three-pair ordinary-path gate
against `0f2ddcb` passes: geometric mean **1.005** over 39 rows and maximum allocation ratio **1.038** (cold
start). Individual time regressions remain unresolved: 1 MB iterate **1.166**, 1 KB regex **1.149**, dispatch
compilation **1.093** and sorting compilation **1.084**. Grouping/keys compilation are **0.998/0.945**;
cold-start time is **1.028**. Functional and native jobs had finished, both sides used the same current
harness and thresholds, and this final gate was not rerun. This does not resolve earlier outliers or establish
streaming throughput, retained-memory, replay-byte or latency budgets. Raw rounds and limits are recorded in
[sequence contract evidence](plan/p7-streaming.md#s4-cardinality-assertions-and-native-sequence-insertion).


After ordered mixed-node paths, exact integer windows and caller-proof precedence, the final isolated
three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean **0.990** over 39 rows and maximum
allocation ratio **1.039** (cold start). The 1 KB regex time remains an unresolved outlier at **1.128**.
Dispatch/grouping/keys/sorting compilation are **1.002/1.041/0.699/0.988**; cold-start time is **0.992**.
Functional and native jobs had finished, both sides used the same current harness and thresholds, and the
final gate was not rerun. Changed medians do not resolve earlier outliers or establish streaming throughput,
retained-memory, replay-byte or latency budgets. Regex round medians are base **0.0047/0.0047/0.0047 ms**,
head **0.0053/0.0047/0.0054 ms**. Raw rounds and limits are recorded in
[ordered path evidence](plan/p7-streaming.md#s4-ordered-mixed-node-paths-and-exact-position-windows).


After streamed trace, invoking-thread diagnostics and nested-call population sharing, the final isolated
three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean **1.008** over 39 rows and maximum
allocation ratio **1.039** (cold start). Sorting compilation remains an unresolved outlier at **1.201**;
keys compilation is **1.054**. Dispatch/grouping compilation are **1.014/1.031**, and cold-start time is
**1.020**. Functional and native jobs had finished, both sides used the same harness and thresholds, and the
final gate was not rerun. These measurements do not establish streaming throughput, retained-memory,
replay-byte or latency budgets or resolve earlier outliers. Explicit trace diagnostics are unbounded;
trace#2 without a handler transmits nodes without buffering. Raw rounds and limits are recorded in
[trace evidence](plan/p7-streaming.md#s4-streamed-trace-and-invoking-thread-diagnostics).


After parallel map entries and pure node-parameter factory projection, the final isolated three-pair
ordinary-path gate against `0f2ddcb` passes: geometric mean **1.015** over 39 rows and maximum allocation
ratio **1.040** (cold start). Compilation time outliers remain unresolved: sorting **1.259**, grouping
**1.120**, keys **1.081** and dispatch **1.058**; cold-start time is **0.977**. Functional and native jobs had
finished, both sides used the same harness and thresholds, and the final gate was not rerun. These results
do not resolve earlier outliers or establish streaming throughput, retained-memory, replay-byte or latency
budgets. Maps requiring independent source views add thread/setup overhead, and constructed values can grow.
Raw rounds and limits are recorded in
[map and parameter evidence](plan/p7-streaming.md#s4-parallel-maps-and-forked-node-parameter-factories).


After native node sets and shared header paths, the final isolated three-pair ordinary-path gate against
`0f2ddcb` passes: geometric mean **0.989** over 39 rows and maximum allocation ratio **1.041** (cold start).
Cold-start time is **1.026**. The 1 KB regex row measures **1.128**; its cause and attribution remain
unresolved. Compilation ratios are sorting **0.832**, keys **0.825**, dispatch **0.992** and grouping
**1.016**. Earlier compilation outliers remain unresolved. Functional and native jobs finished before the
gate, both sides used the same harness and thresholds, and the final gate was not rerun. These results do not
establish streaming throughput, retained-memory, replay-byte or latency budgets. Explicit grounded operands
and results can grow. Raw rounds and limits are recorded in
[node-set evidence](plan/p7-streaming.md#s4-native-node-sets-and-shared-header-paths).


After positional header predicates and bounded scans, the final isolated three-pair ordinary-path gate against
`0f2ddcb` passes: geometric mean **1.018** over 39 rows and maximum allocation ratio **1.041** (cold start).
Cold-start time is **1.037**. Individual time outliers remain unresolved: sorting compilation **1.250**,
1 MB iterate **1.196**, 1 KB dispatch **1.056**, and iterate/join compilation **1.051/1.051**. Earlier outliers
remain unresolved. Functional and native jobs finished before the gate, both sides used the same harness and
thresholds, and the final gate was not rerun. These results do not establish streaming throughput,
retained-memory, replay-byte or latency budgets. Explicit grounded operands and results can grow. Raw rounds
and limits are recorded in
[positional evidence](plan/p7-streaming.md#s4-positional-header-predicates-and-bounded-scans).


After forward XML-to-JSON conversion and the large-try AST correction, the final isolated three-pair
ordinary-path gate against `0f2ddcb` passes: geometric mean **0.987** over 39 rows and maximum allocation ratio
**1.042** (cold start). Cold-start time is **1.005**. Individual time outliers remain unresolved: dispatch
compilation **1.070**, 1 KB regex **1.125**, and 1 MB iterate **1.142**. Earlier outliers remain unresolved.
Functional and native jobs finished before the gate, both sides used the same harness and thresholds, and the
final gate was not rerun. These results do not establish streaming throughput, retained-memory, replay-byte or
latency budgets. Output JSON strings and duplicate-key sets may grow. Raw rounds and limits are recorded in
[XML-to-JSON evidence](plan/p7-streaming.md#s4-forward-xml-to-json-conversion).


After forward deep equality and grounded descendant projection, the final isolated three-pair ordinary-path
gate against `0f2ddcb` passes: geometric mean **1.032** over 39 rows and maximum allocation ratio **1.042**
(cold start). Cold-start time is **1.037**. Individual time outliers remain unresolved: keys compilation
**2.044**, sorting compilation **1.139**, join compilation **1.057**, 1 KB regex **1.170**, and 1 MB JSON
**1.056**. Keys compilation is a material regression in this run despite the aggregate pass; its cause and
attribution remain unproven. Earlier outliers remain unresolved. Functional and native jobs finished before
the gate, both sides used the same harness and thresholds, and the final gate was not rerun. These results
do not establish streaming throughput, retained-memory, replay-byte or latency budgets. Explicit grounded
references and constructed results may grow. Raw rounds and limits are recorded in
[deep-equality evidence](plan/p7-streaming.md#s4-forward-deep-equality-and-grounded-descendant-projection).

The P7 bounded ancestor-set increment's final ordinary gate passes against `0f2ddcb` over three alternating
rounds after all other task-owned CPU jobs ended: timed geometric mean **1.018154**, maximum allocation
ratio **1.043320**. Individual observed regressions remain: sorting compilation **1.323634**, regex 1 KiB
**1.145833**, keys compilation **1.080438**, grouping compilation **1.062374** and recursion 1 MiB
**1.052415**. These remain attribution work; the aggregate pass does not establish a fix for earlier
outliers. Exact round pairs, method and limits are recorded in
[the ancestor-set evidence](plan/p7-streaming.md#s4-bounded-ancestor-union-and-intersection).

The incremental type-check/node-return-contract increment's final ordinary gate passes over three
alternating rounds against `0f2ddcb`, after all other task-owned CPU jobs ended: geometric mean **0.989463**,
maximum allocation ratio **1.043896**. Individual observed regressions remain: dispatch compilation
**1.106768**, regex 1 KiB **1.106383**, dispatch 1 KiB **1.082192** and identity 1 KiB **1.071429**.
This does not establish fixes for preceding outliers or attribute ordinary-path improvements to the change.
Exact round observations and limits are in
[the type-check evidence](plan/p7-streaming.md#s4-incremental-sequence-type-checks-and-node-return-contracts).


The S7 process benchmark now measures source parsing plus transformation at decimal 1 MB, 100 MB and
1 GB. Its complete local managed matrix retains **126 trials**: all 63 streamed and 48 tree trials validate
output, with fifteen tree OOM failures under the equal 4 GiB managed heap guard. At 100 MB, streaming
identity, fork and source-document are slower than tree; adjacent grouping, accumulators and iterate are
faster, and merge has no valid tree comparison. The exact throughput target remains incomplete. See
[full paired comparisons and first output](streaming-benchmark-2026-10-24.md) and
[method](streaming-benchmark.md). These results do not replace the exact NativeAOT memory gates.

After the finalized harness's old-base compatibility fix, the ordinary gate passes over exactly three pairs
against `0f2ddcb`: geometric mean **1.036264**, maximum allocation ratio **1.043896**. Individual regressions
remain, including key compilation **1.579705**, regex 1 KiB **1.170213**, sorting compilation **1.164424**
and maps 1 MiB **1.112436**. They remain attribution work; the aggregate pass does not establish a fix.
The initial attempt stopped before timing because the base lacks the streaming reader API; the fixed gate
excludes the separate process host equally on both sides and its default build retains identical streamed
method IL. The complete fixed gate was run once after other task-owned CPU jobs ended. All outliers,
round observations, provenance and limits are in
[the latest S7 evidence](plan/p7-streaming.md#s7-paired-process-benchmark-and-exact-push-rss-investigation).


The node-only copy-content increment fixes complete containing-row buffering and reuses frame-owned
namespace scopes. A new large-row emission regression and saved NativeAOT variants pass. The full current
1 MB/100 MB/1 GB process matrix retains 126 trials: 63 valid streams, 48 valid tree trials and
15 failures. The exact streaming target remains incomplete. Current/previous medians and first output
are in [the full report](streaming-copy-content-2026-10-24.md#final-full-paired-process-matrix).

Its final ordinary gate exits 0 over three pairs against `0f2ddcb`: geometric mean **1.009512**,
maximum allocation ratio **1.043896**. It ran once after all task-owned functional/native
and streaming benchmark CPU jobs ended. Individual outliers remain unassigned and open; the report
retains every observation and does not infer their resolution or streaming exits from an aggregate pass.


The selected-copy increment's [complete paired matrix](streaming-copy-select-2026-10-24.md#final-full-paired-process-matrix)
retains 144 trials: 72 validated streams, 57 validated tree trials and 15 tree OOM failures. Selected-copy
emits after 8,192 input bytes at all three sizes, while its 100 MB streaming median is 1.140905 times tree.
The exact streaming throughput target remains incomplete.

Its final ordinary gate exits 0 over exactly three pairs against `0f2ddcb`, after all other task-owned CPU
jobs finish: geometric mean **1.027029**, maximum allocation ratio **1.043896**. Individual regressions
remain: keys compilation **1.766204**, regex 1 KiB **1.170213**, sorting compilation **1.093939**, join
compilation **1.089056** and dispatch compilation **1.065657**. These remain attribution work; the
aggregate pass does not establish a fix for preceding outliers. Every round, row and allocation is retained
in [the full ordinary-gate evidence](streaming-copy-select-2026-10-24.md#final-ordinary-performance-gate).
This is not a streaming throughput, latency or exact push-memory pass.


The conditional/complex-content increment's [complete process matrix](streaming-conditional-content-2026-10-24.md#final-full-paired-process-matrix)
retains **162 trials**: **81 validated streams**, **63 valid tree trials** and **18 tree OOM failures**.
Conditional-content emits after 8,192 input bytes at all three sizes; its 100 MB stream/tree median ratio
is **1.108109**, and its 1 GB tree comparisons fail. The exact canonical throughput target stays incomplete.

The final ordinary gate passes once over exactly three pairs against `0f2ddcb`, after the full streaming
matrix and all task-owned functional/native CPU jobs finish: timing geometric mean **0.990893**,
maximum allocation ratio **1.044597**. Regex 1 KiB remains an individual observed regression
(**1.127660**). Earlier outlier attribution remains open; no aggregate pass establishes a fix or a streaming
exit. Every row/round, cold-start result and allocation is retained in
[the final gate evidence](streaming-conditional-content-2026-10-24.md#final-ordinary-performance-gate).

## Node predicates and prepared pull calls, 2026-10-24

The current branch's isolated 100 MB follow-up completes **54 trials** with 27 valid streams,
24 valid trees and three tree merge OOM failures under equal 4 GiB heaps. The canonical throughput
target remains incomplete. Conditional-content has stream/tree ratio **1.047953** (no meaningful winner
within the controller's 5% band). Identity, source-document and fork remain slower; merge has no valid
comparison. This follow-up covers 100 MB only; the prior full three-size matrix belongs to its frozen source.

The ordinary gate runs once after all functional/native/process jobs terminate, over exactly three pairs
against `0f2ddcb`: exit **0**, timing geometric mean **0.982051247778**, maximum allocation ratio
**1.044672059302** (grouping cold, 1,597,240/1,668,592 bytes). Dispatch compilation remains an individual
observed regression at **1.098697598698**; older outliers are not declared fixed by absence here. All
row medians, exact round times, allocations, first-output metrics and source/runtime hashes are retained
in [current evidence](streaming-node-predicates-2026-10-24.md). A controlled runtime probe reduces
10,000 identity-call allocations from 2,560,000 bytes for ordinary calls to 880,000 for resumptions;
this is not a claim about whole-transform allocation. The exact native nightly matrix passes 39 children;
the exact 20 MiB RSS push matrix still has thirteen failures. Full P7 remains active.

## Atomic template parameters and debug focus, 2026-10-24

The final repaired source completes an isolated **60-trial 100 MB** comparison: 30 valid streams,
27 valid trees and three tree merge OOM failures at equal 4 GiB managed limits. Canonical throughput
coverage remains incomplete. Template-parameter medians are **1,960.900209 ms tree / 1,761.378541 ms
stream**, ratio **0.898250** (observed stream median winner). Other workload medians, first-output
measurements, failed comparisons and all hashes are in
[final evidence](streaming-template-parameters-2026-10-24.md). These are current-snapshot comparisons;
the preceding full three-size matrix belongs to its own frozen source.

The ordinary gate runs once after all final functional/native/process CPU jobs terminate: exactly three
pairs against `0f2ddcb`, exit **0**, geometric mean **0.998328693944**, maximum allocation ratio
**1.045944253838** (grouping cold, 1,597,240/1,670,624 bytes). Sorting compilation (**1.063126**) and
1 KiB regex (**1.191489**) remain individual observed regressions requiring attribution. Cold grouping
time is **1.058318** and excluded from the timed mean; its allocation is included. Exact round data is
retained. No previous outlier is declared fixed by absence here. The exact fourteen-workload native
nightly matrix passes 42 children; the complete exact 20 MiB RSS push matrix retains fourteen failures.
The deferred declaration-error repair was completed before these final measurements; earlier snapshots
and their source differences are preserved separately. Full P7 remains active.

The arithmetic/focus increment's one ordinary three-pair gate exits **0** after all task-owned
functional/native CPU jobs terminate: timing geometric mean **0.994790425361**, maximum allocation
ratio **1.046024392076** (cold grouping, 1,597,240/1,670,752 bytes). The 1 KiB regex row remains an
observed regression (**1.170212765957**, 0.0047/0.0055 ms), with all three rounds retained. Cold grouping
time is **1.033200447423** (134.7271/139.2001 ms), excluded from the timed mean; cold allocation is
included. The temporary base worktree is removed. Earlier outliers are not declared fixed by absence.
The corrected fourteen-workload native nightly matrix passes 42 children, while the complete exact
20 MiB RSS push matrix retains fourteen failures; all 547 source/controller/fixture hashes match.
No fresh paired streaming process matrix was run for this source. The older 60-trial and three-size
matrices retain their own frozen-source scope. Full P7 remains active. See
[all rows, rounds and evidence](streaming-arithmetic-focus-2026-10-24.md).

## Generic function parameters and debug output workers, 2026-10-24

The item-parameter increment's one isolated three-pair ordinary gate exits **0**: timing geometric
mean **0.994644134262**, maximum allocation ratio **1.046490195587**.
It ran after task-owned functional/native CPU jobs finished, against `0f2ddcb`, using the same harness
on both versions. This compares the complete working tree; it does not isolate this increment's cost.
The **grouping compile** timing ratio **1.097596317763** remains an observed regression requiring attribution.
The **dispatch compile** timing ratio **1.473261186001** remains an observed regression requiring attribution.
The **regex 1kb** timing ratio **1.170212765957** remains an observed regression requiring attribution.
Earlier outliers remain open; no fresh paired streaming matrix was run. See [all medians, allocations,
rounds and evidence](streaming-item-parameters-2026-10-24.md#ordinary-performance-gate).

## Overlapping descendant values, 2026-10-25

The corrected value-replay source's isolated ordinary three-pair gate exits **0** against `0f2ddcb`: timing
geometric mean **1.029701487659**, maximum allocation ratio **1.046810748541**.
All functional/native CPU jobs were terminal before this comparison. The same ordinary harness runs
on both versions; this compares the full working tree rather than attributing cost to one increment.
Observed **sorting compile** timing ratio **1.125561413786** remains recorded for attribution.
Observed **keys compile** timing ratio **1.359108226330** remains recorded for attribution.
Observed **join compile** timing ratio **1.106312292359** remains recorded for attribution.
Observed **dispatch compile** timing ratio **1.479902200489** remains recorded for attribution.
Observed **regex 1kb** timing ratio **1.170212765957** remains recorded for attribution.
Earlier outliers stay open. See [all rows and rounds](streaming-value-replay-2026-10-25.md#ordinary-performance-gate).


## Node-set and mode follow-up, 2026-10-25

The ordinary three-pair gate exits **0** against `0f2ddcb`: timing geometric mean **0.990166466732**,
maximum allocation ratio **1.046810748541**. Dispatch compile (**1.188996763754**) and regex 1 KB
(**1.170212765957**) remain timing outliers. The fresh 100 MB process matrix completes **96 trials**:
48 valid streams, 45 valid trees and three tree-merge OOMs under equal 4 GiB heap guards.
Tree intersection now completes at median **1,809.021667 ms** after the linear merge repair.
Identity, fork and source-document streaming are slower; merge has no valid tree comparison.
The canonical throughput target remains incomplete. See [all rows, failures and provenance](streaming-set-merge-mode-2026-10-25.md).


## Bounded value cache and merge output, 2026-10-25

The isolated ordinary three-pair gate exits **0** at timing geometric mean **0.996520976223** and
maximum allocation ratio **1.047265587827**. Dispatch compile (**1.235022428975**), regex 1 KB
(**1.191489361702**) and dispatch 1 KB (**1.081081081081**) remain timing outliers. Earlier outliers
remain open. The fresh **96-trial** 100 MB process matrix has **zero failures**: tree merge succeeds
under the unchanged equal 4 GiB heap guard after action output stops accumulating. Canonical
throughput is evaluated and fails for identity, fork and source-document. Current overlap-values
stream/tree ratio is **2.375079889881**. See [all medians, allocations and provenance](streaming-bounded-values-merge-2026-10-25.md).


The matching current native nightly matrix passes **48 children / zero failures** at exact 2 GiB input
and 200 MiB absolute RSS. The requested 200 MiB / 20 MiB push matrix retains **16 failures**: twelve full
input/output validations and four startup/warmup exits. The ordinary three-pair gate passes at timing
geometric mean **1.004693365582**, maximum allocation ratio **1.037238560580**. All **96** paired 100 MB
process trials validate under equal 4 GiB guards; canonical throughput still fails on identity, fork
and source-document. These requirements and the full Phase 7 goal remain open. See the
[current complete evidence](streaming-relative-values-signatures-2026-10-25.md).


The final comma/content native nightly matrix passes **48 children / zero failures** at exact 2 GiB
input / 200 MiB absolute RSS. The requested 200 MiB / 20 MiB push matrix retains **16 failures** (twelve
full-input validations and four startup/warmup exits). The ordinary three-pair gate passes at timing
geometric mean **1.024354047451**, maximum allocation ratio **1.037242922079**. All **96** paired 100 MB
trials validate; the canonical throughput target still fails on identity, fork and source-document.
The same-host 1 MB / 1 GB sweep has completed: **84 trials, 72 valid, 12 failed**. All streamed trials
pass; three tree trials each for adjacent, accumulator, merge and fork at 1 GB exhaust the unchanged
4 GiB heap guard. All three planned sizes have been attempted, with those four large-input comparisons
unavailable. Full P7 remains active. See
[current evidence](streaming-comma-content-2026-10-25.md).


The recursive-function follow-up passes normal/debug focused NativeAOT runs at exact 200 MiB and
2 GiB input, with independently checked output digests, first output after 356,352 bytes and lifetime
RSS near 30 MB under a focused 200 MiB guard. This does not satisfy the 20 MiB push requirement. The
full goal and the existing throughput/RSS gates remain open. See
[recursive-function evidence](streaming-recursive-functions-2026-10-25.md).


### Document fragment checkpoint, 2026-10-25

The final isolated ordinary three-pair gate passes: time geometric mean **0.990513413537**, maximum
allocation ratio **1.037238717165**. Focused normal/debug native fragment probes validate exact 200 MiB
and 2 GiB inputs, independent output digests, early output and owned-source closure, with lifetime
RSS **24,576,000–27,377,664 bytes** under the 200 MiB guard. These do not satisfy the separate 20 MiB
push RSS requirement or canonical throughput target. Current source hashes and artifacts match;
full P7 remains active. See [fragment evidence](streaming-document-fragments-2026-10-25.md).


### Navigator ID provenance checkpoint, 2026-10-25

The current isolated ordinary gate passes: time geometric mean **0.977000839111**, maximum allocation ratio
**1.037247283614**. Four focused normal/debug native fragment runs validate exact 200 MiB and 2 GiB inputs,
independent output digests, early output and ownership, at lifetime RSS **24,805,376–27,475,968 bytes** under the
200 MiB guard. This does not satisfy the separate 20 MiB push requirement or canonical throughput target.
The frozen source/config manifest has **407** verified hashes. Full P7 remains active. See
[the current evidence](streaming-id-provenance-2026-10-25.md).


### Deferred header storage checkpoint, 2026-10-25

The final corrected-source ordinary gate passes: time geometric mean **1.005276705514**, maximum allocation
ratio **1.037242922079**. Retained normal/debug native fragment probes pass at exact 200 MiB
and 2 GiB input, with lifetime RSS **24,313,856–27,443,200 bytes** under the 200 MiB guard. The separate 20 MiB
push RSS and canonical throughput requirements remain open. These historical probes do not execute deferred
climbing except. Compiler integration and its memory proof were still required at that checkpoint;
the subsequent deferred climbing checkpoint below supplies those focused results. The native primitive
probe checks 100,000 closed headers, metadata, cache bounds and cleanup. All **411** source/configuration
hashes match. See [current evidence](streaming-deferred-headers-2026-10-25.md).

### Deferred climbing execution checkpoint, 2026-10-25

Actual deferred climbing `except` now passes normal/debug NativeAOT runs at exact **209,715,200**
and **2,147,483,648** input bytes. Every run consumes its full source, validates an independently
computed output digest, emits after **69,632** input bytes and closes its owned source. Lifetime
RSS, measured as the maximum of outer and child peaks without subtraction, ranges from **30048256**
to **30572544** bytes under the unchanged **209,715,200-byte** guard. ICU remains enabled.
This does not satisfy the separate push RSS or canonical throughput requirements.

The ordinary three-pair gate passes with time GM **1.027012847257** over **39** timed rows and maximum
allocation ratio **1.037238717165**. Individual timing outliers remain recorded. Both native publications
have no warnings; the new saved normal/debug cases and retained compatibility consumers pass. All
**412** live/frozen source hashes and **95** retained saved-DLL hashes match. Full P7 remains active.
See [the implementation and complete evidence](streaming-deferred-climbing-2026-10-25.md).

### Deferred accumulator phase checkpoint, 2026-10-25

Source-owned scratch storage now carries accumulator phase snapshots for deferred climbing headers,
including late publication before source close and fork readers. Scalars and map/array structure spill;
node/opaque function identities are retained, so general bounded storage for those histories remains open.

Actual deferred-set/scalar-counter normal/debug native children pass at exact **209,715,200** and
**2,147,483,648** input bytes. Each consumes the full source, independently verifies IDs plus counter
values, emits before EOF and closes its owned source. Lifetime peak RSS ranges from
**31309824** to **31621120** bytes, taking the maximum of outer and child peaks without
subtraction under the unchanged **209,715,200-byte** guard. ICU remains enabled. These measurements do
not satisfy the separate push RSS, canonical throughput or arbitrary identity-history requirements.

The isolated ordinary three-pair gate passes: **39** timed rows, time GM **0.989223741081**, maximum
allocation ratio **1.037234042553**, with individual outliers retained. Both native publications are
warning-free. All **413** frozen/live source hashes and **103** retained saved DLLs match after execution.
Full P7 remains active. See [complete evidence](streaming-accumulator-phases-2026-10-25.md).

### Compiled closure capture checkpoint, 2026-10-25

Actual deferred-set/compiled-closure normal/debug native runs pass exact **209,715,200** and
**2,147,483,648** input bytes. Each closure captures a map containing an array of the numeric source
ID; output verification independently checks each selected ID and invoked closure value. Lifetime
peak RSS ranges from **31,260,672** to **31,653,888** bytes, without subtraction, under the unchanged
**209,715,200-byte** heap/RSS guard with ordinary ICU support. First output follows **57,344** input
bytes in each case. These fixed-owner cases do not prove general opaque/node history spill, exact
push RSS or canonical throughput. Candidate compilation overlaps part of the matrix, so elapsed
times remain diagnostic.

The isolated original ordinary three-pair gate passes over **39** timed rows: time GM
**0.986027868023**, maximum allocation ratio **1.037238403996**. Individual timing outliers remain
recorded. Both native publications have no warnings; all **413** frozen/live source hashes,
**111** retained saved DLLs, protected v2 fork and native artifact hashes match after execution.
Full P7 remains active.
See [the complete checkpoint](streaming-closure-captures-2026-10-25.md).

### Coerced function capture checkpoint, 2026-10-25

Actual declared-function accumulator normal/debug native children pass exact **209,715,200** and
**2,147,483,648** input bytes. The engine creates a coercion wrapper around each map-capturing
compiled function. Each child consumes the full source, independently verifies output, emits at
**57,344** input bytes and closes its owned source. Lifetime peak RSS is **30,212,096–31,834,112** bytes,
without subtraction, under the unchanged **209,715,200-byte** guard with ordinary ICU support.
These fixed-model cases do not close opaque/node history, exact push RSS or canonical throughput.

The first timing gate fails during a change also visible in unchanged-base measurements; its raw
rounds remain preserved. Reversed-order frozen-core attribution does not reproduce the large penalty.
An unchanged original three-pair retry passes over **39** timed rows: GM **1.035572890616**, maximum
allocation ratio **1.037242608875**, with individual timing outliers retained. All native publications
are warning-free; all **413** live/frozen source hashes, **117** retained saved DLLs, protected v2 fork
and artifact hashes match after the matrix. Child times remain diagnostic because isolated follow-on
work overlaps part of the memory matrix. Full P7 remains active.
See [the complete checkpoint](streaming-coerced-captures-2026-10-25.md).

### Library function capture checkpoint, 2026-10-25

Actual computed `function-lookup()` accumulator normal/debug native children pass exact
**209,715,200** and **2,147,483,648** input bytes. The caller supplies the required lookup-name
parameter through the public API; each rule creates a library-function value. All children consume
the full source, independently verify output, emit at **57,344** input bytes and close their owned
source. Lifetime peak RSS is **31,752,192–31,866,880** bytes, without subtraction, under the
unchanged **209,715,200-byte** guard with ordinary ICU. Follow-on prototype work overlaps part of
the matrix, so elapsed times remain diagnostic. General opaque/native/node histories, exact push
RSS and canonical throughput remain open.

The unchanged original three-pair ordinary gate passes on its first attempt over **39** timed rows:
GM **0.994364503290**, maximum allocation ratio **1.037238717165**. Individual timing outliers and
all raw measurements remain in the report. Native publications have no warnings; all **413** source
hashes, **133** retained saved DLLs, the protected v2 fork and artifact/consumer-source manifests
match after the matrix. Full Phase 7 remains active.
See [the complete checkpoint](streaming-library-captures-2026-10-25.md).

### Random-generator capture checkpoint, 2026-10-25

Actual per-row seeded random-generator map accumulators pass normal/debug native runs at exact
**209,715,200** and **2,147,483,648** input bytes. Both callables carry a distinct actual numeric state
for each source ID. Recorded permutation determines the output token order, and an independent
algorithm computes the expected output from source IDs. That verifier also passes against the
preserved preceding core. All children consume the full source, independently verify output, emit
at **57,344** input bytes and close their owned source. Lifetime peak RSS is
**31,260,672–31,801,344** bytes, without subtraction, under the unchanged **209,715,200-byte**
guard with ordinary ICU. These results do not close exact push RSS or canonical throughput.

The original ordinary three-pair gate passes over **39** timed rows: GM
**0.994987004211**, maximum allocation ratio **1.037239186928**.
Raw rounds and individual outliers remain in the report. Native publications have no warnings;
all **413** source hashes, **141** retained and six preceding-core random saved DLLs, protected v2 fork,
generator-source and artifact/consumer-source manifests match after the matrix. Full P7 remains active.
See [the complete checkpoint](streaming-random-captures-2026-10-25.md).


### Optional-focus compile allocation, 2026-10-26

The initial keys/compile allocation regression failed the original gate and is preserved. Existing focus/cardinality proofs remove 5,488 redundant compile bytes in identical-harness ABBA attribution. The corrected original three-pair gate passes at time GM **1.030587374330**, maximum allocation ratio **1.037309886931**; individual timing outliers remain recorded. Actual absent-context function values pass normal/debug exact **200 MiB and 2 GiB** native cases under the unchanged **200 MiB** guard, including independent output verification. This does not close exact 20 MiB push RSS or canonical throughput. See [the complete checkpoint](streaming-optional-focus-2026-10-26.md).


### XML-to-JSON header-path continuation, 2026-10-26

Eligible filtered descendant/child arguments now preserve continuation while conversion absorbs a selected subtree. Motionless prefix predicates run once, real additional matches produce XPTY0004, and predicate faults retain precedence. Consumed input still requires actual replay. Full Release/Debug pass **3,404 / 3,290**; all nine profile failure-ID sets are unchanged, with strict unmatched codes improving **5 to 4**. Retained/current native saved consumers, the original ordinary gate and four final normal/debug exact-size native JSON cases pass. Unsupported paths, mixed-result try, exact push RSS and full P7 requirements remain open. See [the complete checkpoint](streaming-json-header-continuation-2026-10-26.md).

### Mixed template result streaming, 2026-10-26

Eligible mixed template results can now reach the writer before the population ends. The original 10 MiB try stress case emits after **57,344** input bytes with an independent complete-output check, in normal and Debug native runs. The final exact-size memory matrix and unchanged ordinary three-pair gate are recorded in [the checkpoint](streaming-mixed-template-results-2026-10-26.md). They do not replace the exact 20 MiB push RSS or canonical throughput requirements.

### Reference and diagnostic contracts, 2026-10-26

The URI/declaration/accounting checkpoint passes the original three-pair ordinary gate: time GM **1.022631015925**, maximum allocation ratio **1.037310200701**. Outliers remain recorded. It adds no new exact-size memory claim; exact push RSS and canonical throughput remain open. See [implementation and evidence](streaming-reference-diagnostics-2026-10-26.md).

### Source validation negotiation, 2026-10-26

Explicit streamed validation now negotiates bytes from capable resolvers, preserving reader-only and provider-error contracts. Release/Debug pass **3,449 / 3,331**; strict unmatched codes improve **1 to 0**, with unchanged failure-ID sets. Native runtime validation, **225** retained saved styles and the original ordinary performance gate pass. Imported-schema saved compilation and full P7 gates remain open. See [implementation and evidence](streaming-source-validation-negotiation-2026-10-26.md).

### Annotation and whitespace ordering, 2026-10-26

Streamed whitespace filtering now uses the effective annotation-stripping policy, while preserving explicit validation and xml:space behavior. Release/Debug pass **3,457 / 3,339**; all nine profile headings and failure-ID sets remain unchanged. Native checks pass **12** new normal/debug saved styles plus **225** retained styles. The original ordinary performance gate passes; imported-schema persistence and full P7 gates remain open. See [implementation and evidence](streaming-annotation-whitespace-2026-10-26.md).

### Saved imported-schema validation, 2026-10-26

Resolved schema graphs now travel with supported saved source-validation paths. Release/Debug pass **3,486 / 3,358**, with unchanged profile headings and failure-ID sets. Native checks pass **12** new imported-schema saved styles plus **237** retained styles; the ordinary gate passes. General schema-dependent constant metadata, typed static nodes, .NET 10-to-8 saved-DLL portability and full P7 gates remain open. See [implementation and evidence](streaming-saved-schema-2026-10-26.md).

### Portable saved framework references, 2026-10-26

Saved framework references now use the core project's minimum supported runtime contract. Release/Debug pass **3,490 / 3,358**, with unchanged profile headings and failure-ID sets. Fourteen fresh portable emissions pass on .NET 8 and NativeAOT alongside **249** retained native styles. The **311**-path portable-copy corpus passes on .NET 8 and .NET 10. The ordinary gate passes; general schema metadata, typed static nodes and full P7 gates remain open. See [implementation and evidence](streaming-portable-emitter-2026-10-26.md).

### Saved schema-dependent metadata, 2026-10-26

Saved constants now retain registered schema contexts, named atomic/cast metadata and recursive sequence/function/node tests using table-scoped format 5. Release/Debug pass **3,505 / 3,360**, with unchanged conformance profile headings and failure-ID sets. Fourteen fresh saved styles pass with **263** retained styles under NativeAOT; the **325**-path portable-copy corpus passes on .NET 8 and .NET 10. The ordinary gate passes. Typed static nodes, anonymous atomic metadata and full P7 remain open. See [implementation and evidence](streaming-schema-metadata-2026-10-26.md).

### Typed static-node persistence, 2026-10-26

Annotated static nodes now retain schema types, shared-tree identity, nilled state and node metadata through format 6. Nilled simple-type atomization and optional cardinality checks are corrected. Release/Debug pass **3,519 / 3,372**, with unchanged profile headings and failure-ID sets. Two fresh saved styles pass with **277** retained styles under NativeAOT; the **327**-path portable-copy corpus passes on .NET 8 and .NET 10. The ordinary gate passes. Anonymous atomic constants, unowned schema contexts and full P7 remain open. See [implementation and evidence](streaming-typed-static-nodes-2026-10-26.md).

### Deferred temporal context and integer parsing, 2026-10-26

Dynamic contexts now share deferred temporal state, and XML integer parsing uses a native-magnitude fast path while retaining arbitrary precision. Release/Debug pass **3,563 / 3,416**, with unchanged profile headings and failures; retained native and managed saved consumers pass. The exact 200 MiB / 20 MiB matrix passes **13/16**, and the 2 GiB / 200 MiB matrix passes **16/16**. All 42 canonical timing trials validate output, with target pass **False**. The ordinary performance gate passes. Full P7 remains incomplete. See [implementation and evidence](streaming-temporal-integer-2026-10-26.md).
