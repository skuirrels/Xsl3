# Phase 2 plan: XSLT 2.0

SPEC §22, P2: loader changes, the 2.0 instructions (`for-each-group`, `analyze-string`, `sequence`,
`function`, `result-document`, `next-match`, `perform-sort`, `namespace`, `document`), 2.0 patterns, the
`xml`, `html`, `xhtml` and `text` serialization methods, and backwards-compatible mode. **Exit criterion:
the `xslt30-test` XSLT 2.0-applicable tests pass at ≥ 95%.**

This file is the working plan; tick items off as they land and keep the decisions section current.

## Where P1 leaves us

- The XPath 3.1 front end (`Xsl3.XPath31`) compiles standalone expressions to QIL: every value is boxed as
  `item`/`item*` (`AtomicValue` or `XPathNavigator`), structure is native QIL, atomic semantics are
  early-bound calls into `XsltLibrary.XPath31.cs`. QT3 XPath 3.1: 95.9%.
- The XSLT compiler is upstream's: `XsltLoader` (stylesheet → `XslAst`), `XslAstAnalyzer`, `QilGenerator`
  (AST → QIL, compiling XPath through upstream's 1.0 `XPathBuilder`), `MatcherBuilder` (template dispatch),
  `KeyMatchBuilder`, `InvokeGenerator`. The loader still carries upstream's dormant `#if XSLT2` scaffolding
  (2.0 attributes parsed, instructions reported as not implemented).

## What QIL already gives XSLT 2.0

QIL came from XQuery, so node constructors are values, not just output side effects: `XmlILConstructAnalyzer`
chooses per constructor between writing to the output (`Writer`), building a cached sequence and iterating
it (`WriterThenIterator`), and the reverse. A 2.0 sequence constructor is therefore a QIL `Sequence` of
items and constructors whose value can be bound to a variable, returned from a function or written out.

What it lacks is the 2.0 *rules*: atomic values in content become text nodes with single spaces between
adjacent values (§5.7.2), temporary trees are real documents (no RTF restrictions), `xsl:value-of` joins
with a separator, and every instruction is typed by sequence types rather than XPath 1.0's four types.

## Decisions

1. **Extend upstream's generator, don't fork a second one.** `QilGenerator` becomes version-aware per
   instruction (each `XslNode` already carries its `XslVersion`). Instructions in 2.0+ modules compile their
   expressions with the XPath 3.1 front end and use 2.0 rules; 1.0 modules keep the existing code paths, so
   legacy mode (SPEC §4.3, G3) stays byte-identical and the upstream test gate keeps passing. Template
   dispatch, keys, numbering, output and the invocation machinery are shared.
2. **The XPath 3.1 compiler embeds.** `XPath31Compiler` takes the stylesheet's `QilFactory` and an
   environment (`IXPath31Environment`) that supplies the focus (context item, position, last), in-scope
   variables, the XSLT functions (`current`, `key`, `document`, `format-number` with `xsl:decimal-format`,
   `system-property`, `function-available`, `element-available`, `type-available`, `unparsed-entity-uri`,
   `unparsed-entity-public-id`, `generate-id`, `regex-group`, `current-group`, `current-grouping-key`) and
   user `xsl:function`s. Values stay boxed (`item*`); typed lowering is P4.
3. **Values crossing the 1.0/2.0 boundary are converted explicitly.** A 2.0 variable is `item*`. When a
   1.0-typed QIL value (string, double, boolean, node-set, RTF) reaches a 2.0 expression it is boxed, and a
   2.0 value reaching 1.0 code (only in backwards-compatible mode) is converted with 1.0 rules.
4. **Atomic values in content.** Content `item*` goes through `X31ContentItems`, which merges adjacent
   atomic values into single space-separated strings, then a QIL loop writes nodes with `XsltCopyOf` and
   strings with `TextCtor`. No output side effects inside library calls.
5. **Temporary trees are documents.** `xsl:variable` with content and no `as` builds a document node
   (upstream's RTF constructor, then the node-set conversion that `msxsl:node-set` uses), so 2.0 code can
   navigate it.
6. **Entry points.** Compilation takes an initial template or initial mode (the W3C suite starts 6,300 tests
   from a named template). Internal options first; the public `XsltCompiler`/`Xslt30Transformer` API of
   SPEC §7.2 follows once the semantics are in place.
7. **The suite is fetched, not committed.** `eng/fetch-w3c.sh` pins `w3c/xslt30-test` like `qt3tests`; the
   harness skips when it is absent; known failures are a baseline.

## Steps

- [ ] **xslt30-test harness** (`tests/Xsl3.W3C/Xslt30`): catalogue, environments (sources by file or
      content, roles, params, static params), test entry (stylesheet, initial template or mode, params),
      dependency filtering (spec `XSLT10+`/`XSLT20+`/`XSLT20`, features), assertions (`assert-xml`,
      `assert`, `assert-string-value`, `serialization-matches`, `assert-serialization`, `error`,
      `assert-message`, `assert-result-document`, `any-of`/`all-of`/`not`), baseline, report.
- [ ] **Entry points**: initial template (`xsl:initial-template` default), initial mode, global context item.
- [ ] **Embedding the XPath 3.1 compiler**: shared factory, environment, focus, variables, XSLT functions.
- [ ] **2.0 core instructions**: sequence constructors and content rules, `xsl:sequence`, `xsl:value-of`
      separators, `xsl:variable`/`xsl:param` with `as`, temporary trees, `xsl:if`/`xsl:choose`,
      `xsl:for-each` over items, `xsl:copy-of`, `xsl:copy`, literal result elements and AVTs, `xsl:element`,
      `xsl:attribute` (`select`, `separator`), `xsl:comment`/`xsl:processing-instruction` (`select`),
      `xsl:namespace`, `xsl:text`, `xsl:message`.
- [ ] **Templates and modes**: 2.0 patterns (parser and matcher), `mode="#all"`/lists, `#current`,
      `xsl:next-match`, `xsl:apply-imports` with params, tunnel parameters, required parameters, `as` on
      templates.
- [ ] **`xsl:function`**, including recursion and `as` coercion.
- [ ] **`xsl:for-each-group`** (all four grouping kinds, `current-group()`, `current-grouping-key()`,
      collations), **`xsl:perform-sort`** and 2.0 `xsl:sort` (data types, collations, stable).
- [ ] **`xsl:analyze-string`** and `regex-group()`.
- [ ] **`xsl:result-document`** and `xsl:document`.
- [ ] **`xsl:number`** 2.0 (`select`, `format` through the format-integer engine, `ordinal`).
- [ ] **Keys** 2.0 (any `use` expression, collations), **`xsl:import-schema`** rejected cleanly (P6).
- [ ] **Serialization**: `xml`, `html`, `xhtml`, `text` methods with the 2.0 output parameters, character
      maps.
- [ ] **Backwards-compatible mode** for 1.0 modules inside 2.0+ stylesheets.
- [ ] **Exit**: ≥ 95% of the applicable xslt30-test tests; upstream gate still 100%.
