# Phase 1 plan: XDM and the XPath 2.0 core

SPEC §22, P1: atomic types and storage (§8), the extended type system, the XPath 3.1 parser (2.0 subset of
semantics first), lowering to QIL, and the core F&O library. **Exit criterion: the QT3 XPath 2.0 F&O subset
passes at ≥ 90%.**

This file is the working plan; tick items off as they land and keep the decisions section current.

## What upstream gives us

QIL was designed with XQuery 1.0 in mind (SPEC §3.2.6) and already covers most of the XPath 2.0 core:

| XPath 2.0 | Existing QIL |
|---|---|
| `for $x in E return R`, `some`/`every` | `Loop`, `Filter` |
| `if (C) then A else B` | `Conditional` |
| Value comparisons, arithmetic on numerics | `Eq`/`Ne`/`Lt`/…, `Add`/`Subtract`/…, typed by `XmlQueryType` |
| Sequences, `,`, `union`/`intersect`/`except` | `Sequence`, `Union`, `Intersection`, `Difference` |
| `instance of`, `treat as` | `IsType`, `TypeAssert` |
| Casts to native storage types | `XsltConvert` |
| Function library | `XsltInvokeEarlyBound` into static C# methods |
| Sequence results | `XmlCachedSequenceWriter` → `IList<XPathItem>` |

What it lacks is storage. `XmlIlTypeHelper` maps all date/time types to `DateTime` (no optional timezone,
years 1–9999 only), durations to `string`, QNames to `XmlQualifiedName`, and every other atomic type to a
boxed `XPathItem`.

## Decisions

1. **One XDM value library, three users.** `Xsl3.Xdm` (public: `XdmValue`, `XdmItem`, `XdmAtomicValue`,
   `XdmNode`, `QName`, …) plus internal `XsDateTime`, `XsDuration` and `XsDecimal`/integer helpers. The
   compiled code (through early-bound helpers), the future QIL interpreter (Sink C) and the public API all
   use the same casting, comparison and formatting code. This is where most of the spec long tail lives, so
   it is written once, test-first against QT3.
2. **`XdmAtomicValue : XPathItem`.** A boxed atomic value is an `XPathItem`, so it fits the existing
   `item`/`IList<XPathItem>` storage without ILGen changes. Phase 1 lowers every atomic type that has no
   native storage as `item` and lets these objects flow through. Unboxed storage for dates, durations and
   QNames (SPEC §12.4) is a P4 performance task, measured, not a P1 correctness task.
3. **Full XPath 3.1 grammar from the start.** The parser and AST cover all of XPath 3.1 now; constructs
   whose semantics belong to later phases (maps, arrays, function items, `=>`, `!`) lower to a "not yet
   supported" static error (`XPST0017`-style, with a clear message) until then. Writing the grammar once
   avoids reworking precedence later.
4. **Separate front end, shared back end.** `Xsl3.XPath31` never touches upstream's XPath 1.0 front end
   (`Xsl/XPath`), which stays byte-for-byte for legacy mode (SPEC §9.2, G3).
5. **Standalone XPath first.** QT3 tests are standalone expressions, so P1 adds a small `XPathCompiler` →
   `XPathExecutable` → `XPathSelector` API (compiling to a QIL expression whose root delivers a sequence).
   XSLT 2.0 integration of the new front end is P2.
6. **The test suites are fetched, not committed.** `eng/fetch-w3c.sh` clones `w3c/qt3tests` (and later
   `xslt30-test`) into the git-ignored `external/`. The harness skips cleanly when they are absent.

## Steps

- [x] **QT3 harness** (`tests/Xsl3.W3C`): catalogue and test-set parsing, dependency filtering (spec
      `XP20`/`XP30`/`XP31`, features, `xsd-version`), environments (sources, namespaces, params), and every
      assertion kind. First mode: *parse coverage*, so the parser is measured before anything executes.
      Done: catalogue, dependency filtering, baselines; environments and assertions arrive with execution.
- [x] **XPath 3.1 lexer, parser, AST** (`src/Xsl3/XPath31`): recursive descent over the 3.1 EBNF,
      including the lexical subtleties (keyword vs name, `/` disambiguation, comments, `xml-version` char
      rules, `?` lookups, string templates are 4.0 so excluded). Exit: every QT3 XPath expression that
      should parse does, and every `XPST0003` test fails to parse. Done: 22,364 / 22,364 applicable
      tests (253 of them must-reject).
- [x] **Static context and analysis** (name resolution, static errors; type inference is P4): namespaces, default element/function namespaces, in-scope
      variables, function signatures with arity, focus analysis, type inference (bottom-up).
- [x] **XDM atomic types** (`src/Xsl3/Xdm`): the type hierarchy of XPath 3.1 §2.7, lexical-space parsing,
      canonical serialisation (double/float formatting per §19.1.2.2 of F&O), casting table (F&O §19), equality and
      ordering, arithmetic with overflow (`FOAR0002`), `XsDateTime`/`XsDuration` with timezone arithmetic.
      Done, with unit tests from the F&O worked examples (`XdmAtomicTests`). Measuring against QT3's
      `prod-CastExpr` and `op-*` sets waits for the execution path.
- [x] **Lowering to QIL** (`XPath31Compiler`): explicit atomisation, general comparison as an
      existential loop over value comparisons, effective boolean value, path expressions with document-order
      handling, `for`/`let`/`some`/`every`/`if`, `instance of`/`cast`/`castable`/`treat`, function calls.
- [x] **Execution path**: `XPath31Compiler`/`XPath31Executable` (internal for now), sequence delivery
      through `XmlCachedSequenceWriter` (`XmlILCommand.Evaluate`), QT3 running for real (`Qt3RunTests`):
      81.6% on the first run.
- [ ] **Core F&O**: strings (code-point semantics, SPEC §13.5), numerics, booleans, dates and durations,
      QNames, node functions, sequences, aggregates, regex (SPEC §13.4), collations (SPEC §13.2).
- [ ] **Conformance report**: `docs/conformance.md` generated from the harness, per test set.

## Decisions made along the way

- **xs:integer is unbounded** (`BigInteger`); the `long` fast path of SPEC §8.2 is a P4 optimisation.
- **Fractional seconds are exact decimals** in both `XsDateTime` and `XsDuration`, and all date arithmetic
  goes through an exact decimal timeline, so nothing is rounded to ticks or milliseconds.
- **XSD 1.0 year semantics**: no year 0, and -0001 is 1 BCE (SPEC §18 claims XSD 1.0). Lexical double
  forms follow XSD 1.1 in accepting `+INF`, as F&O 3.1 does.
- **double → decimal uses the shortest round-trip digits**, so `xs:decimal(0.1e0)` is `0.1`.

## Risks noted at the start

- The upstream optimiser's rewrite rules were written for XSLT 1.0 and XQuery-draft semantics. Some may
  be unsound for XPath 2.0 typed values (e.g. numeric promotion, NaN, empty sequences). Mitigation: QT3
  runs with optimisation on and off; any difference is a bug in a rule, which gets restricted to legacy mode.
- `XmlQueryTypeFactory`'s built-in types are keyed by `XmlTypeCode`, which has no code for some XPath 3.1
  types (`xs:dateTimeStamp`, `xs:error`). Those need a type-system extension (SPEC §8.3).
