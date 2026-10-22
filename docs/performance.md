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

Run on 2026-10-22 · .NET 10.0.11 · macOS 27 · Apple M4 Pro · Xsl3 at 18d2ba6 (the code of 1.0.0, less one unused
method) · `dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -p:Saxon=true -- saxon --sizes 1kb,1mb
--cold-runs 5`, three times; each figure is the median of the three runs. Both engines read the same document, parsed
once into each engine's own tree (whitespace-only text left out, as `XPathDocument` and Xsl3's compact tree do), write
to `Stream.Null`, and gave identical output for all 13 tests.

### Run speed, 1 MB input

Xsl3 wins 11 of 13, Saxon 2.

| Test | Xsl3 ms | Saxon ms | Faster |
|---|---|---|---|
| join | 2.55 | 72.45 | Xsl3, 28.5× faster |
| identity | 2.04 | 5.24 | Xsl3, 2.6× faster |
| dispatch | 1.49 | 3.37 | Xsl3, 2.3× faster |
| keys | 3.87 | 5.32 | Xsl3, 38% faster |
| json | 9.90 | 13.60 | Xsl3, 37% faster |
| maps | 4.60 | 5.83 | Xsl3, 27% faster |
| grouping | 0.93 | 1.14 | Xsl3, 22% faster |
| recursion | 1.31 | 1.57 | Xsl3, 20% faster |
| strings | 3.07 | 3.53 | Xsl3, 15% faster |
| sorting | 2.78 | 3.09 | Xsl3, 11% faster |
| fold-left | 3.38 | 3.70 | Xsl3, 9% faster |
| iterate | 2.09 | 1.62 | **Saxon**, 29% faster |
| regex | 28.77 | 14.88 | **Saxon**, 93% faster |

### Run speed, 1 KB input

Xsl3 wins 8 of 13, Saxon 5. A run at this size takes microseconds, so these rows are the noisiest.

| Test | Xsl3 ms | Saxon ms | Faster |
|---|---|---|---|
| identity | 0.004 | 0.009 | Xsl3, 2.2× faster |
| grouping | 0.012 | 0.019 | Xsl3, 59% faster |
| join | 0.005 | 0.008 | Xsl3, 54% faster |
| fold-left | 0.006 | 0.009 | Xsl3, 52% faster |
| json | 0.008 | 0.011 | Xsl3, 32% faster |
| sorting | 0.006 | 0.007 | Xsl3, 29% faster |
| keys | 0.007 | 0.008 | Xsl3, 26% faster |
| maps | 0.012 | 0.014 | Xsl3, 13% faster |
| strings | 0.006 | 0.006 | **Saxon**, 3% faster |
| iterate | 0.006 | 0.006 | **Saxon**, 9% faster |
| recursion | 0.005 | 0.005 | **Saxon**, 15% faster |
| dispatch | 0.009 | 0.007 | **Saxon**, 32% faster |
| regex | 0.019 | 0.011 | **Saxon**, 72% faster |

### Start-up

Loading the stylesheet and running it once on the 1 MB input, in a new process, with the input already parsed;
the median of five processes per run. "Precompiled" is Xsl3 loading the stylesheet from an assembly saved by
`CompileToAssembly`, as Xsl3.Build does at build time. Xsl3 wins 13 of 13 from source, and 13 of 13 precompiled.

| Test | Xsl3 ms | Xsl3 precompiled ms | Saxon ms | Faster: Xsl3 or Saxon | Faster: Xsl3 precompiled or Saxon |
|---|---|---|---|---|---|
| join | 138 | 58 | 411 | Xsl3, 3.0× faster | Xsl3, 7.1× faster |
| dispatch | 123 | 44 | 167 | Xsl3, 36% faster | Xsl3, 3.8× faster |
| json | 156 | 81 | 191 | Xsl3, 22% faster | Xsl3, 2.4× faster |
| identity | 133 | 56 | 162 | Xsl3, 21% faster | Xsl3, 2.9× faster |
| keys | 151 | 62 | 179 | Xsl3, 18% faster | Xsl3, 2.9× faster |
| recursion | 143 | 54 | 168 | Xsl3, 17% faster | Xsl3, 3.1× faster |
| strings | 142 | 61 | 166 | Xsl3, 17% faster | Xsl3, 2.7× faster |
| regex | 199 | 129 | 227 | Xsl3, 14% faster | Xsl3, 75% faster |
| fold-left | 159 | 65 | 176 | Xsl3, 11% faster | Xsl3, 2.7× faster |
| maps | 180 | 85 | 197 | Xsl3, 10% faster | Xsl3, 2.3× faster |
| grouping | 144 | 62 | 156 | Xsl3, 9% faster | Xsl3, 2.5× faster |
| sorting | 156 | 63 | 166 | Xsl3, 7% faster | Xsl3, 2.6× faster |
| iterate | 152 | 60 | 162 | Xsl3, 7% faster | Xsl3, 2.7× faster |
| **median** | **151** | **62** | **168** | **Xsl3, 11% faster** | **Xsl3, 2.7× faster** |

### Compile and memory

- Compiling a stylesheet, warm, in one process: Xsl3 wins 10 of 13, Saxon 3 (dispatch, fold-left, iterate). Median 0.13 ms against 0.16 ms.
- Memory allocated per 1 MB run: Xsl3 allocates 49% of Saxon's at the median (join 2%, iterate 101%).
- Peak memory of the start-up process: Xsl3 uses 7% less.

### Against the targets

SPEC §19.6 asks for 2.0/3.0 stylesheets to run at least twice as fast as on SaxonCS-HE and never slower, and to start
at least twice as fast, ten times precompiled. 1.0.0 doesn't meet them: run speed at 1 MB is 1.22× Saxon's at the
median, with regex and iterate slower; start-up is 1.11× from source and 2.72× precompiled. regex and
iterate are the first 1.x speed work.

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
