# XSL3: an XSLT 2.0 / 3.0 fork of System.Xml.Xsl

**Status:** draft specification, version 0.1 (18 October 2026)
**Working name:** XSL3. This is a placeholder. The final name must not start with `System.` (Microsoft reserves that NuGet prefix) and must not use "Microsoft" or "Saxon".

---

## 1. Summary

XSL3 is a fork of the XSLT engine in `dotnet/runtime` (`System.Private.Xml/src/System/Xml/Xsl`), which is MIT-licensed. Its two jobs are:

1. **Run XSLT 2.0 and 3.0 stylesheets conformantly.** The underlying languages are XPath 3.1, XDM 3.1, Functions and Operators (F&O) 3.1 and Serialization 3.1.
2. **Be fast.** It must never be slower than stock `XslCompiledTransform` on XSLT 1.0 stylesheets. On 2.0/3.0 stylesheets it must be clearly faster than SaxonCS-HE.

It keeps the property that makes `XslCompiledTransform` fast: stylesheets are compiled to IL through the QIL intermediate representation. Around that core it adds:

- a new XPath 3.1 front end
- a full XDM type system with unboxed value storage
- a compact tree model
- new code-generation targets (collectible JIT, build-time AOT, interpreter)
- a modernised serializer

The public API is a superset of today's `XslCompiledTransform`, so existing callers can switch with a `using` change. A new compiler/executable/transformer API exposes the 3.0 features that the old API cannot express.

---

## 2. Goals and non-goals

### 2.1 Goals

| # | Goal | Measured by |
|---|------|-------------|
| G1 | Conformant XSLT 3.0 processor, including the optional features listed in §4 | Pass rates on the W3C `xslt30-test` and `qt3tests` suites (§20) |
| G2 | XSLT 2.0 stylesheets run correctly | The XSLT 2.0-tagged tests in `xslt30-test` |
| G3 | XSLT 1.0 stylesheets behave exactly as on stock `XslCompiledTransform` | The upstream XSLT test suite, plus differential tests against the stock engine |
| G4 | No performance regression on 1.0 | Throughput ≥ 1.0× stock, target 1.2–1.5× (§19.6) |
| G5 | Fast on 2.0/3.0 | Faster than SaxonCS-HE 13 on every benchmark, median ≥ 2× (§19.6) |
| G6 | Drop-in API compatibility | Every public member of `XslCompiledTransform`, `XsltArgumentList` and `XsltSettings` keeps its signature and behaviour |
| G7 | Works with NativeAOT and trimming | Stylesheets compiled at build time run under NativeAOT with no `RequiresDynamicCode` warnings |
| G8 | Secure by default | No file, network or result-document access unless enabled (§21) |

### 2.2 Non-goals

- **XSLT 4.0.** It is still a Community Group draft. The design must not block it (§24 lists the extension points), but none of it is built now.
- **XQuery.** The QIL backend could host XQuery 3.1 later. `fn:load-xquery-module` raises `FOQM0006`, which the spec allows.
- **The legacy `XslTransform` class** (upstream `XsltOld/`). It is obsolete and is not carried over.
- **`msxsl:script`.** .NET Core already throws `PlatformNotSupportedException` for it, and XSL3 does the same.
- **.NET Framework.** The minimum target is .NET 8 (LTS), and .NET 10 (LTS) is the primary target.
- **A new XML parser.** Input is parsed with `XmlReader`. A faster UTF-8 parser is a possible later optimisation (§19.4), not part of this spec.

---

## 3. What upstream provides, and what it lacks

### 3.1 Upstream layout (dotnet/runtime `main`, October 2026)

All paths below are under `src/libraries/System.Private.Xml/src/System/Xml/`.

| Area | Files | What it does | What happens to it in XSL3 |
|------|-------|--------------|------------------|
| `Xslt/` | `XslCompiledTransform.cs`, `XsltArgumentList.cs`, `XsltSettings.cs`, `XsltContext.cs`, `XsltException.cs`, `XslTransform.cs` | Public API | Kept and extended. `XslTransform` is dropped. |
| `Xsl/Xslt/` | `XsltLoader.cs` (135 KB), `QilGenerator.cs` (119 KB), `XslAstAnalyzer.cs`, `MatcherBuilder.cs`, `XPathPatternParser.cs`, `KeyMatchBuilder.cs`, `Focus.cs`, … | Stylesheet loading, the XSLT abstract syntax tree (AST), and lowering to QIL | Heavily extended for 3.0 |
| `Xsl/XPath/` | `XPathParser.cs`, `XPathScanner.cs`, `XPathBuilder.cs`, `XPathQilFactory.cs` | XPath 1.0 to QIL | Kept unchanged for 1.0 mode. A new XPath 3.1 front end is added alongside it. |
| `Xsl/QIL/` | `QilNodeType.cs`, `QilFactory.cs`, `QilTypeChecker.cs`, `QilPatternFactory.cs`, visitors | The QIL intermediate representation | Extended with new node types (§10) |
| `Xsl/` | `XmlQueryType.cs`, `XmlQueryTypeFactory.cs`, `XmlQueryCardinality.cs` | Static type system. It already covers every `XmlTypeCode` up to `DayTimeDuration`, plus cardinalities. | Extended with function, map and array types and union types (§8.3) |
| `Xsl/IlGen/` | `XmlILOptimizerVisitor.cs` (281 KB), `XmlIlVisitor.cs` (211 KB), `GenerateHelper.cs`, `IteratorDescriptor.cs`, `XmlILConstructAnalyzer.cs`, `TailCallAnalyzer.cs`, `XmlILModule.cs`, `*.p` | Optimiser and IL emitter | Extended. New sinks are added (§12). |
| `Xsl/Runtime/` | `XmlQueryRuntime.cs`, `XmlQueryOutput.cs`, `XsltLibrary.cs`, `XsltFunctions.cs`, `XmlCollation*.cs`, `XmlSortKey.cs`, iterators, `RtfNavigator.cs` | Runtime library | Extended with the F&O 3.1 library, maps and arrays |
| `Cache/` | `XPathDocumentBuilder.cs`, `XPathNode.cs`, `XPathDocumentNavigator.cs` | `XPathDocument` tree | Vendored for the 1.0 path. A new compact tree is added (§14). |
| `Core/` | `XmlUtf8RawTextWriter.tt/.cs`, `XmlEncodedRawTextWriter.*`, `HtmlUtf8RawTextWriter.*`, `TextUtf8RawTextWriter.*`, `QueryOutputWriter*.cs`, `XmlRawWriter.cs` | Serializer back end (internal types) | Vendored and modernised (§15) |

### 3.2 Facts about upstream that shape the design

1. **The optimiser has no working generator.** The `*.p` files (`Annotate.p`, `ConstantFold.p`, `Normalize.p`, `Macros.p`, `XmlILOptimizer.p`) are a pattern language, and `XmlILOptimizerVisitor.cs` was originally generated from them. No generator exists in the repository, so the 281 KB C# file is effectively the source. Decision: treat the C# as authoritative and write new rules in C#. A small rule DSL can come later if rule count makes it worthwhile (§11.3).
2. **IL is emitted as `DynamicMethod`s.** `XmlILModule` does this in release builds, and only uses an `AssemblyBuilder` with `AssemblyBuilderAccess.Run` in debug builds. A `DynamicMethod` cannot define types, which closures, function items, struct iterators and maps need. That forces a new code-generation target (§12).
3. **Compiled stylesheets can't be saved on .NET Core.** `Load(Type)` and `Load(MethodInfo, byte[], Type[])` exist, but `xsltc.exe` only runs on .NET Framework, so every process recompiles every stylesheet. .NET 9's `PersistedAssemblyBuilder` makes it possible to fix this (§12.2).
4. **The engine depends on internal `System.Private.Xml` types:** raw writers, `XmlCharType`, `ValidateNames`, `XsdDateTime`, `XsdDuration`, `XPathDocument` internals, `SR` resource strings, `BitStack` and others. All of them must be vendored or replaced. Phase 0 (§22) produces the complete list.
5. **The raw writers are generated from T4 templates** (`.tt`/`.ttinclude`). Either use `Mono.TextTemplating` in the build or commit the generated C# and edit it directly. Decision: commit the generated C# and drop the T4 templates. The writers are rewritten in §15 anyway.
6. **The type system was designed with XQuery in mind.** `XmlQueryType` already models all XSD primitive atomic types and occurrence cardinalities, so QIL can represent XPath 2.0+ semantics without being redesigned.

---

## 4. Conformance targets

### 4.1 Specifications

| Spec | Version | Notes |
|------|---------|-------|
| XSLT | 3.0 (W3C Rec, 8 June 2017) | Plus the 2.0 behaviour that 3.0 subsumes, and 1.0 behaviour via the compatibility feature |
| XPath | 3.1 (W3C Rec, 21 March 2017) | |
| XQuery and XPath Data Model (XDM) | 3.1 | |
| XPath and XQuery Functions and Operators | 3.1 | The whole function library, including `map:`, `array:`, `math:` and JSON |
| XSLT and XQuery Serialization | 3.1 | All methods, including `json` and `adaptive` |
| XML Schema | 1.0 required, 1.1 optional | Only used for the schema-awareness feature (§18) |

### 4.2 XSLT 3.0 conformance features (spec §27)

| Feature | Target | Phase |
|---------|--------|-------|
| Basic XSLT 3.0 processor | Required | P2–P3 |
| XPath 3.1 feature | Required | P3 |
| Higher-order functions feature | Required | P3 |
| Serialization feature | Required | P2–P5 |
| Compatibility feature (backwards-compatible processing for `version="1.0"`) | Required | P2 |
| Dynamic evaluation feature (`xsl:evaluate`) | Required | P5 |
| Schema-awareness feature | Required with XSD 1.0. XSD 1.1 is a stretch goal. | P6 |
| Streaming feature | Required | P7 |

"Full support" means all eight features. The phases are ordered so that a useful, fast processor ships long before the last two features, which are also the most expensive.

### 4.3 Version handling

| Stylesheet `version` | Default behaviour | Option |
|----------------------|------------------|--------|
| `1.0` | **Legacy 1.0 mode:** compiled through the upstream XPath 1.0 front end, giving byte-identical results to stock `XslCompiledTransform`, including `msxsl:node-set`, MSXSL extension functions and UTF-16 code-unit string semantics | `XsltVersionMode.BackwardsCompatible` runs the stylesheet on the 3.0 processor in spec-defined backwards-compatible mode instead |
| `2.0` | 3.0 processor. XSLT 3.0 Appendix "Incompatibilities with XSLT 2.0" lists the behaviour differences, which follow the 3.0 rules. | none |
| `3.0` | 3.0 processor | none |
| `> 3.0` | Forwards-compatible processing, as the spec requires | none |

A module whose `version` differs from the module that includes it compiles in the mode its own `version` implies, as spec §3.9 requires. Legacy 1.0 mode applies only when the *principal* stylesheet module is 1.0 and no 2.0+ module is included or imported. Otherwise the 1.0 modules compile in backwards-compatible mode.

---

## 5. Architecture

```
                                     ┌──────────────────────── compile time ────────────────────────┐
 stylesheet modules / packages
   │
   ▼
 XsltLoader 3.0 ──► XSL AST ──► Static analysis ──► QilGenerator 3.0 ──► QIL ──► Optimiser ──► Annotator ──► IL emitter
   │ use-when,        │ modes,        │ type inference,      │ XPath 3.1          │ rewrite       │ storage   │
   │ static params,   │ accumulators, │ focus analysis,      │ front end          │ rules +       │ & iterator│
   │ shadow attrs,    │ packages,     │ streamability,       │ (or XPath 1.0      │ typed passes  │ selection │
   │ TVTs             │ overrides     │ closure capture      │ for legacy mode)   │               │           │
                                                                                                             ▼
                                                                                 ┌──────────── code-generation sinks ────────────┐
                                                                                 │ A. RunAndCollect assembly (JIT, default)      │
                                                                                 │ B. PersistedAssemblyBuilder (build-time AOT)  │
                                                                                 │ C. QIL interpreter (no IL emit at all)        │
                                                                                 └───────────────────────────────────────────────┘
                                     └──────────────────────────────────────────────────────────────┘
                                     ┌──────────────────────── run time ────────────────────────────┐
 input (XmlReader / Stream / XdmNode / XPathNavigator)
   │
   ▼
 CompactTree builder ──► compiled stylesheet ──► XmlQueryOutput 3.0 ──► Serializer (xml, html, xhtml, text, json, adaptive)
   (whitespace strip,      (runtime library:          │                     or tree / raw-sequence destination
    name pool, indexes)     F&O 3.1, maps, arrays,     └──► xsl:result-document handler
                            collations, regex)
                                     └──────────────────────────────────────────────────────────────┘
```

**One compiler, two XPath front ends.** Everything after QIL is shared between legacy 1.0 mode and the 3.0 processor. That way performance work benefits both, and legacy 1.0 mode can never fall behind stock.

---

## 6. Fork and repository strategy

### 6.1 Extraction

1. Copy `Xsl/` (without `XsltOld/`), `Xslt/` (without `XslTransform.cs`) and the dependencies listed in Phase 0 into `src/Xsl3/`.
2. Rename namespaces from `System.Xml.Xsl.*` to `Xsl3.*`.
3. Replace every use of `System.Private.Xml` internals:
   - **Vendor** where XSL3 owns the behaviour: raw writers, `XmlCharType`, `ValidateNames`, `XsdDateTime`, `XsdDuration`, `Cache/*`, `BitStack`, `SR` strings, which move to `.resx`.
   - **Use the public equivalent** where one exists: `XmlQualifiedName`, `XmlSchemaType`, `XPathNavigator`, `XmlReader`.
4. Get the forked code building and passing the upstream XSLT tests from `src/libraries/System.Private.Xml/tests/Xslt/` **before changing any behaviour**. That baseline is what G3 and G4 are measured against.

### 6.2 Repository layout

```
/src
  Xsl3/                     core: API, compiler, QIL, IL gen, runtime
  Xsl3.Schema/              schema-awareness (P6), separate so it can be trimmed
  Xsl3.Streaming/           streaming (P7)
  Xsl3.Build/               MSBuild task + targets for build-time compilation (§12.2)
  Xsl3.Cli/                 `xsl3` command-line tool (transform, compile, test, bench)
/tests
  Xsl3.Tests/               unit tests
  Xsl3.Upstream.Tests/      ported dotnet/runtime XSLT tests (1.0 regression gate)
  Xsl3.W3C/                 harness for xslt30-test and qt3tests (§20)
  Xsl3.Differential/        differential and fuzz testing against stock engine and Saxon
/bench
  Xsl3.Benchmarks/          BenchmarkDotNet suites (§19.5)
  corpus/                   benchmark stylesheets and generated documents (licences checked)
/docs
  internals.md              upstream internals that were vendored or replaced, and why
  conformance.md            published pass rates per feature
  divergences.md            every intentional behaviour difference from stock or spec
```

### 6.3 Packages

| Package | Contents |
|---------|----------|
| `Xsl3` | Core engine, all target frameworks |
| `Xsl3.Schema` | Schema-awareness |
| `Xsl3.Streaming` | Streaming |
| `Xsl3.Build` | Build-time compilation (development dependency) |

### 6.4 Licence and upstream sync

- XSL3 is licensed MIT, the same as upstream. Every file derived from upstream keeps its `.NET Foundation` header, and the repository carries upstream's `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT`.
- Upstream rarely changes this code: mostly security fixes and analyser clean-ups. To keep up:
  - Add a CI job that diffs the upstream paths weekly against the fork-point commit recorded in `docs/internals.md` and opens an issue when anything changes.
  - Port fixes by hand.

---

## 7. Public API

### 7.1 Compatibility surface (unchanged signatures)

`Xsl3.XslCompiledTransform`, `Xsl3.XsltArgumentList`, `Xsl3.XsltSettings` and `Xsl3.XsltException` mirror the upstream public members exactly. Callers switch by changing `using System.Xml.Xsl;` to `using Xsl3;`, or with a `using` alias. The following changes are additive only:

- `Load(...)` accepts 2.0/3.0 stylesheets and picks the version mode as described in §4.3.
- `XsltArgumentList.AddParam` accepts more CLR types than before. It maps them to XDM values as follows. Unsupported types throw, as they do today.

  | CLR type | XDM type |
  |----------|----------|
  | `long`, `int`, `BigInteger` | `xs:integer` |
  | `decimal` | `xs:decimal` |
  | `float` | `xs:float` |
  | `DateTime`, `DateTimeOffset` | `xs:dateTime` |
  | `DateOnly` | `xs:date` |
  | `TimeOnly` | `xs:time` |
  | `TimeSpan` | `xs:dayTimeDuration` |
  | `Uri` | `xs:anyURI` |
  | `byte[]` | `xs:base64Binary` |
  | `IReadOnlyDictionary<,>` | `map(*)` |
  | `IEnumerable` of supported types | sequence |
  | `XdmValue` | passed through |

- `XsltSettings` gains the options in §21. Its defaults are unchanged.
- `Load(Type)` also loads stylesheets compiled by `Xsl3.Build` (§12.2).

### 7.2 Full 3.0 API

```csharp
// Compile once. XsltExecutable is immutable, thread-safe and meant to be cached.
var compiler = new XsltCompiler(new XsltCompilerOptions
{
    Resolver          = myResolver,          // module, package, doc(), unparsed-text, collection()
    StaticParameters  = { [new QName("debug")] = XdmValue.From(false) },
    PackageLibrary    = packages,            // resolves xsl:use-package
    Optimisation      = OptimisationLevel.Full,   // None | Fast | Full
    Backend           = CodeGenBackend.Auto,      // Auto | Jit | Interpreter
    Errors            = diagnostics => { ... },
});
XsltExecutable exe = compiler.Compile(new Uri("file:///styles/main.xsl"));

// Run many times. Creating a transformer is cheap; each one is not thread-safe.
Xslt30Transformer t = exe.CreateTransformer();
t.SetStylesheetParameters(new Dictionary<QName, XdmValue> { ... });
t.GlobalContextItem = doc;
t.MessageHandler    = (msg, terminate, location) => { ... };
t.ResultDocumentHandler = (uri, outputProps) => new StreamDestination(File.Create(uri.LocalPath));
t.CancellationToken = ct;

t.ApplyTemplates(doc, new SerializedDestination(stream));          // initial match selection
t.ApplyTemplates(XdmValue.Of(items), mode: new QName("m"), dest);   // any sequence, any mode
t.CallTemplate(new QName("main"), dest);                            // initial named template (xsl:initial-template if null)
XdmValue r = t.CallFunction(new QName("urn:f", "sum"), [a, b]);    // initial function (public functions only)
XdmValue raw = t.ApplyTemplatesRaw(doc);                            // raw result delivery, no serialization
```

**Data model API:** `XdmValue`, `XdmItem`, `XdmNode`, `XdmAtomicValue`, `XdmMap`, `XdmArray`, `XdmFunctionItem`, `XdmEmptySequence`, `QName`.

**Creating `XdmNode`s:** `XdmNode.Load(Stream|XmlReader|TextReader, options)`, `XdmNode.Wrap(XPathNavigator)`, `XdmNode.Wrap(XmlNode)` and `XdmNode.Wrap(XNode)`. The `Wrap` methods keep the original tree, which is slower. `Load` builds a compact tree (§14).

**Destinations:** `SerializedDestination` (Stream, TextWriter or XmlWriter, with `OutputProperties`), `XdmDestination` (tree), `RawDestination` (sequence) and `XmlWriterDestination`.

---

## 8. Data model and runtime values

### 8.1 Items

| XDM item | Runtime representation | Notes |
|----------|------------------------|-------|
| Node (compact tree) | `struct NodeRef { CompactTree Tree; int Index; }` | Unboxed in generated code. Boxed to `XdmNode` only at API boundaries. |
| Node (foreign tree) | `XPathNavigator` | Wraps `XmlDocument`, `XDocument` or user navigators |
| Atomic value | See §8.2 | |
| Map | `XdmMap`: a persistent hash trie (CHAMP) keyed by atomic values using the same-key rules (`op:same-key`) | O(1) average lookup. `map:put` is O(log₃₂ n) and shares structure with the original. |
| Array | `XdmArray`: a persistent vector, implemented as a 32-way trie with a tail buffer | `array:append` is amortised O(1). Arrays under about 32 members use a flat array. |
| Function item | `XdmFunctionItem`: an abstract class with `Arity`, a `Signature` and an `Invoke(ReadOnlySpan<XdmValue>)` method | The compiler emits one sealed subclass per inline function and per named function reference. Arities up to 4 get typed `Invoke` overloads. |

**Sequences** are `XdmValue`, with these specialised forms: empty, singleton, array-backed, lazy (iterator plus memo) and a node range in document order. Generated code works on **iterators**, not materialised sequences, wherever static analysis allows (§12.4).

### 8.2 Atomic types

All built-in atomic types in XPath 3.1 §2.7 must be supported. Generated code keeps singleton values in **unboxed storage** wherever the static type is known:

| XDM type | Storage | Notes |
|----------|---------|-------|
| `xs:string`, `xs:anyURI`, `xs:untypedAtomic`, `xs:normalizedString` and its derived types | `string`, plus a type tag when it's not `xs:string` | See §13.5 for the code point rules |
| `xs:boolean` | `bool` | |
| `xs:integer` and its derived types | `long` fast path. Overflow is checked and promotes to `BigInteger`. | Derived types (`xs:int`, `xs:byte`, …) carry a type tag and are range-checked on cast. |
| `xs:decimal` | `decimal`, which has 28–29 significant digits. The spec's minimum is 18. | Overflow raises `FOAR0002` |
| `xs:double`, `xs:float` | `double`, `float` | Cast to string follows XPath 3.1's rules, not .NET's `ToString` (§13.6) |
| `xs:dateTime`, `xs:date`, `xs:time`, `xs:g*` | `XsDateTime` struct: ticks, an optional timezone and a year beyond the `DateTime` range. Based on the vendored upstream `XsdDateTime`. | Values are compared using the implicit timezone |
| `xs:duration`, `xs:yearMonthDuration`, `xs:dayTimeDuration` | `XsDuration` struct: months plus seconds and nanoseconds | |
| `xs:QName`, `xs:NOTATION` | `XsQName` struct: name-pool ids plus a prefix | Two QNames compare equal when their ids are equal (§14.2) |
| `xs:hexBinary`, `xs:base64Binary` | `ReadOnlyMemory<byte>` plus a tag | |
| `xs:dateTimeStamp` | Through the XSD 1.1 option | |

A boxed atomic value (`XdmAtomicValue`) appears only when the static type is `xs:anyAtomicType` or wider. Upstream boxes through `XmlAtomicValue`, which is slow. XSL3 replaces it on every path except the legacy API.

### 8.3 Static type system

`XmlQueryType` is extended with:

- `function(T1, …, Tn) as R` and `function(*)`
- `map(K, V)`, `map(*)`, `array(T)` and `array(*)`
- union types: XSD unions, plus the generalised atomic types used by casting
- `element(N, T)`, `attribute(N, T)`, `schema-element(N)` and `schema-attribute(N)`, which only appear in schema-aware compilation
- `document-node(element(N))`
- `empty-sequence()`, `item()` and occurrence indicators (the existing `XmlQueryCardinality` covers these)

**Subtype checks** follow XPath 3.1 §2.5.6. They are cached per type pair, because function coercion and `instance of` call them heavily.

---

## 9. Front end

### 9.1 Stylesheet loader (`XsltLoader` 3.0)

The loader is extended to handle:

- **Static processing** (spec §3.13):
  - `use-when`
  - static parameters and variables (`static="yes"`)
  - shadow attributes (`_name="{...}"`)
  - evaluation through the interpreter (§12.3) at compile time

  These must run **before** the AST is built, because they can remove or create declarations.
- **New declarations:**
  - `xsl:package`, `xsl:use-package`, `xsl:expose`, `xsl:accept` and `xsl:override`
  - `xsl:mode`, `xsl:accumulator` and `xsl:global-context-item`
  - `xsl:import-schema` (P6)
  - `xsl:function` with its new attributes: `visibility`, `streamability`, `override-extension-function`, `new-each-time` and `cache`
- **New instructions:** see the table in §16.
- **Text value templates** (`expand-text`): split into literal and expression parts once, at load time.
- **`xsl:default-collation`, `[xsl:]xpath-default-namespace` and `default-mode`**: these feed the static context.
- **Line and column information:** kept on every AST node and carried through QIL, for error messages and debug mapping (§21.3).

### 9.2 XPath 3.1 front end

The XPath 3.1 front end lives in a new `Xsl3.XPath31` namespace:

- **Scanner and parser:** a hand-written recursive-descent parser over the XPath 3.1 EBNF, producing an XPath AST. Upstream's 1.0 parser is not extended, because the grammar differences are too large: `for`/`let`/`some`/`every`, `if`, `!`, `||`, `=>`, `?` lookup, inline functions, `#` named function references, `?` placeholders, sequence types and `instance of`/`cast`/`castable`/`treat`.
- **Static analysis of the XPath AST:**
  - name resolution (variables, functions with arity, types)
  - **focus analysis**: does the expression use the context item, `position()` or `last()`? Upstream's `Focus.cs` is generalised for this.
  - **type inference**: bottom-up, using the function signatures from F&O 3.1
  - **free-variable capture analysis** for inline functions
- **Lowering:** an `XPath31QilBuilder` lowers the AST to QIL, filling the same role as `XPathBuilder.cs`. General comparisons, atomisation and function coercion are lowered *explicitly*, as `Atomize`, `Convert` and `TypeAssert` nodes, so the optimiser can remove them when the types allow (§11.2).

### 9.3 Patterns

XSLT 3.0 patterns (spec §5.5) are extended in three ways. Upstream's `XPathPatternParser` and `XPathPatternBuilder` are reworked to support them.

- **Predicate patterns** (`.[…]`) can match atomic values as well as nodes.
- **Patterns may contain variable references, `doc()`, `id()`, `key()` and `element-with-id()`** as the root of a path.
- **Unions, intersections and differences** of patterns are allowed.

Patterns compile to the dispatch tables described in §19.2.

---

## 10. Extending QIL

QIL already has `For`, `Let`, `Filter`, `Sort`, `Loop`, `Conditional`, `Choice`, `Invoke`, `Function`, `TypeAssert`, `IsType`, `DocOrderDistinct`, constructors and the XSLT-specific nodes. XSL3 adds:

| New node | Purpose |
|----------|---------|
| `Range` | `a to b`. Lazy integer range, never materialised. |
| `SimpleMap` | `a ! b`. A loop *without* the document-order sort. |
| `ValueCompare` (`Eq`/`Ne`/`Lt`/… with an atomic type) | Value comparisons. The general comparisons are lowered to an existential loop over these. |
| `Atomize` | Explicit atomisation. Removed when the input is already atomic. |
| `Cast`, `Castable`, `Treat`, `InstanceOf` | Sequence-type operations. `InstanceOf` generalises the existing `IsType`. |
| `FunctionItem` | Builds a closure: target function plus captured variables |
| `DynamicCall` | Calls a function item: `$f(…)`, `?` lookups on functions |
| `PartialApply` | `f(?, 1)` |
| `MapCtor`, `MapLookup`, `ArrayCtor`, `ArrayLookup` | Maps and arrays. Lookups with a literal key lower to a direct call. |
| `Try` | `xsl:try`/`xsl:catch`, with the error variables (`$err:code` and so on) bound |
| `Iterate` | `xsl:iterate`: a loop with typed parameters, `on-completion`, `Break` and `NextIteration`. Lowered to a plain IL loop, never to recursion. |
| `GroupBy`, `GroupAdjacent`, `GroupStartingWith`, `GroupEndingWith` | `xsl:for-each-group`, with composite keys and collations |
| `Merge` | `xsl:merge` (an n-way merge of pre-sorted sources) |
| `AccumulatorBefore`, `AccumulatorAfter` | Accumulator reads (§16.3) |
| `AnalyzeString` | Iterates over regex matches and non-matches |
| `ResultDocument` | Switches the output destination |
| `NextMatch`, `ApplyImports` with parameters | Generalises the upstream equivalents |
| `Evaluate` | `xsl:evaluate`: compiles and runs XPath at run time (§16.4) |
| `SourceDocument` | Streamed and unstreamed `xsl:source-document` |
| `Assert` | `xsl:assert`. Compiled out unless assertions are enabled. |
| `ErrorQName` | `fn:error` with a QName code, a description and an error object |

**Type checking:** `QilTypeChecker` is extended for every new node. `QilValidationVisitor` gains invariants for closures: captured variables must be in scope, and there must be no reference to an iterator outside its loop.

---

## 11. Optimiser

### 11.1 Keep what works

All existing rule groups stay active: constant folding, normalisation, document-order elimination, `position()`/`last()` elimination, tail-call analysis and construction analysis. In legacy 1.0 mode the rule set is **identical** to upstream, which is how G3 is guaranteed.

### 11.2 New typed passes (3.0 processor)

These passes run in order, and each is driven by a fixed-point loop:

1. **Type-driven simplification.**
   - Remove `Atomize`, `Convert`, `TypeAssert` and `Treat` when inference proves them redundant.
   - Turn general comparisons into value comparisons when both sides are singletons of known type.
   - Turn `string(.) = 'x'` on untyped nodes into a direct string comparison with no atomised `xs:untypedAtomic` value.
2. **Inlining.**
   - Inline small `xsl:function`s and named templates (by a size heuristic, never when recursive).
   - Inline function items called with a statically known target, so `$f(x)` becomes a direct call when `$f` is a literal function reference or an inline function bound by `let`.
3. **Variable handling.**
   - Inline variables used once.
   - Promote variables used more than once to lazy evaluation when they are only used on some paths (upstream already makes this choice for some variables).
4. **Loop-invariant code motion** out of `for-each`, `for`, `!`, predicates and `xsl:iterate`.
5. **Common subexpression elimination** for pure expressions within a scope. A purity flag goes on every QIL node: `doc()`, `generate-id()` on new nodes, `random-number-generator`, `current-dateTime` and constructors with `new-each-time` affect it.
6. **Automatic indexing** (§19.3).
7. **Closure conversion**: decide whether each captured variable is copied by value or captured as a lazy cell.
8. **Removing the document-order sort** for path expressions whose steps are known to stay in document order. This extends upstream's `DocOrderDistinct` elimination to the new `SimpleMap`, `Range` and `for` forms.
9. **Streamability analysis** (P7, §17). This runs as an analysis, not as a rewrite.

### 11.3 Rule infrastructure

Rules are written as C# methods on `QilPatternVisitor`, the same shape as upstream's generated rules, and grouped by `XmlILOptimization` flags so groups can be switched off for debugging. If the number of rules passes about 300, a source generator can take a rule DSL instead. Until then, plain C# is easier to debug.

---

## 12. Code generation

### 12.1 Sink A: collectible JIT assembly (default)

The IL emitter writes into an `AssemblyBuilder.DefineDynamicAssembly(…, AssemblyBuilderAccess.RunAndCollect)`, replacing the upstream `DynamicMethod`s. This allows:

- one sealed type per stylesheet, with static readonly fields for literals, names and compiled regexes
- closure classes and function-item subclasses
- `struct` iterators (no allocation per step)
- unloading the compiled stylesheet when its `XsltExecutable` is collected

### 12.2 Sink B: build-time AOT (`PersistedAssemblyBuilder`, .NET 9+)

The **same emitter** writes to a `PersistedAssemblyBuilder`. That has two uses:

- **`Xsl3.Build` MSBuild task.** It compiles the `<XsltStylesheet Include="…"/>` items in a project into an assembly that is referenced and shipped like any other. At run time, `XsltExecutable.FromCompiledType(typeof(MyStylesheet))` or `XslCompiledTransform.Load(typeof(…))` loads it with **zero** compile cost.
- **NativeAOT.** A persisted assembly is ordinary IL, so ILC compiles it like any other code. This is what satisfies G7.

Portable PDBs are written with a line mapping from IL back to the stylesheet, so a stylesheet can be stepped through in Visual Studio or Rider (§21.3).

On .NET 8, which has no `PersistedAssemblyBuilder`, the build task runs on the .NET 10 SDK, and its output assembly targets .NET 8.

### 12.3 Sink C: QIL interpreter

The interpreter is a tree-walking interpreter over optimised QIL. It is used for:

- compile-time static evaluation (`use-when`, static parameters)
- `xsl:evaluate` and runtime-compiled stylesheets under NativeAOT, where IL can't be emitted
- `Backend = Interpreter` for one-shot transforms of small stylesheets, where JIT cost dominates
- **tiered execution** (optional, P4): a stylesheet starts interpreted and moves to Sink A after N transforms or T milliseconds of total run time

The interpreter must pass the same conformance suites. CI runs the whole W3C harness on both Sink A and Sink C.

### 12.4 Emission strategy

- **Iterator selection.** Upstream's `IteratorDescriptor` and `XmlILConstructAnalyzer` already choose between *push* (writing directly to output), *pull* (iterators) and *materialised* storage. XSL3 extends that choice to the new nodes. The default is push, which avoids building sequences for content construction.
- **Storage types.** `XmlIlTypeHelper` maps static types to CLR storage (§8.2). Upstream already uses typed storage, but not all of it fits XPath 2.0+:
  - **Kept:** `string`, `bool`, `decimal`, `float`, `double` and `byte[]`.
  - **Replaced:** `DateTime` for every date and time type, because it can't represent an optional timezone or years outside 1–9999. Plain `string` for durations. `XmlQualifiedName` for QNames.

  XSL3 replaces those with the `XsDateTime`, `XsDuration` and `XsQName` structs, and adds `NodeRef`.
- **Calls.**
  - Calls with statically known targets are direct `call`s.
  - Dynamic calls go through a virtual `Invoke`, with arity-specialised overloads (§8.1).
  - Tail calls are emitted with the `tail.` prefix where `TailCallAnalyzer` proves them safe, and as loops for self-recursion.
- **Recursion depth.** A recursion-depth counter is emitted at every template and function entry (§21.2).

---

## 13. Runtime library

### 13.1 Function library

The whole F&O 3.1 library is in scope. That's roughly 230 functions across the `fn:`, `math:`, `map:` and `array:` namespaces, plus the XSLT-specific ones:

- `current`, `current-group`, `current-grouping-key`, `current-merge-group` and `current-merge-key`
- `regex-group`, `key`, `document`, `unparsed-entity-uri` and `unparsed-entity-public-id`
- `system-property`, `function-available`, `element-available`, `type-available` and `available-system-properties`
- `accumulator-before`, `accumulator-after`, `copy-of`, `snapshot` and `stream-available`

Each function is implemented as a static C# method with a typed signature. The compiler binds calls statically and skips argument coercion when the types already match. Functions that need context (focus, collation, base URI) take an explicit `ref XsltContext3` parameter.

Some functions get compile-time specialisation:

- `fn:concat` and string templates compile to `StringConcat` with exact-size preallocation.
- `fn:count`, `fn:exists` and `fn:empty` on paths stop at the first item when that's enough.
- `fn:string-join` with a literal separator gets a pooled builder.
- `fn:sort` and `fn:sort#3` precompute their keys (§13.3).
- `fn:transform` compiles and caches the target stylesheet through the owning compiler.

### 13.2 Collations

- **Unicode codepoint collation** (the default) uses ordinal comparison, with surrogate-aware ordering (§13.5).
- **UCA collations** (`http://www.w3.org/2013/collation/UCA?…`): the URI parameters are parsed and mapped to `CompareInfo` and `CompareOptions`. That's ICU on Linux and macOS and NLS or ICU on Windows. Parameters that can't be honoured fall back as the spec allows, and each fallback is documented in `docs/divergences.md`.
- **HTML ASCII case-insensitive collation.**
- **Collation keys** (`fn:collation-key`, sorting and grouping) use `CompareInfo.GetSortKey`, cached per item for the duration of a sort.

### 13.3 Sorting and grouping

- Sort keys are computed **once** per item into a parallel key array, then sorted stably with a typed comparer. That avoids recomputing keys on every comparison. Upstream's `XmlSortKeyAccumulator` provides the base.
- `group-by` uses a hash table keyed by collation key, or by the typed value with the same-key rules.
- `group-adjacent`, `group-starting-with` and `group-ending-with` run in a single forward pass.

### 13.4 Regular expressions

- XPath/XSD regular expressions are translated into .NET `Regex` syntax:
  - `\i`, `\c`, `\I` and `\C` expand to the XML name-character classes.
  - Flag `q` escapes the whole pattern.
  - Flag `x` strips whitespace as XPath defines it, which differs from .NET's `IgnorePatternWhitespace`.
  - `$` and `.` follow XPath semantics.
  - Back-references are validated.
  - Character class subtraction and `\p{IsBlock}` are already supported by .NET.
- Literal patterns compile once to `RegexOptions.Compiled` and are stored in static fields. Under Sink B they are emitted with the .NET regex source generator's output, so NativeAOT doesn't need the regex interpreter.
- Dynamic patterns are cached in an LRU keyed by pattern plus flags.

### 13.5 Strings and code points

XPath counts strings in **Unicode code points**, but .NET strings are UTF-16. For `string-length`, `substring`, `translate`, `string-to-codepoints`, `codepoints-to-string`, `tokenize` on `""`, comparisons and so on:

- **Fast path:** a vectorised scan for surrogates using `IndexOfAnyInRange('\uD800', '\uDFFF')`. If there are none, which covers nearly every real string, the function uses UTF-16 indexing directly.
- **Slow path:** code-point-aware implementations.
- In legacy 1.0 mode, upstream's UTF-16 behaviour is kept.

Normalisation (`fn:normalize-unicode`) uses `string.Normalize`. NFC, NFD, NFKC and NFKD are required. `FULLY-NORMALIZED` is optional, and if unsupported raises `FOCH0003`.

### 13.6 Number, date and string formatting

- **Casting `xs:double`/`xs:float` to string:** use the shortest round-trip digits (`"R"` semantics), then reformat to XPath 3.1's rules: decimal notation for absolute values from 1e-6 up to (but not including) 1e6, exponential notation outside that range, and canonical `INF`, `-INF`, `NaN` and `-0`.
- **`format-number`:** XSLT 3.0's picture-string grammar and `xsl:decimal-format`, with exponent separators. Upstream's `DecimalFormatter` is extended.
- **`format-integer`, `format-date`, `format-time`, `format-dateTime`:** the full picture syntax. Words and ordinals come from `CultureInfo` for month and day names, plus small per-language tables for ordinals and numbers in words, starting with English. Languages without a table fall back to English and signal this through the `[Language: …]` prefix the spec allows.
- **`xsl:number`:** upstream's `XslNumber` is extended with `select`, `start-at` and the 3.0 level rules.

### 13.7 JSON

- **`parse-json` and `json-doc`:** `Utf8JsonReader` builds maps and arrays directly, without an intermediate DOM. All of the spec's options are supported (`liberal`, `duplicates`, `escape` and `fallback`).
- **`json-to-xml`, `xml-to-json`:** these follow the spec's XML representation of JSON.
- **The JSON serialization method:** see §15.

### 13.8 Resources and I/O

- `doc`, `document`, `doc-available`, `collection`, `uri-collection`, `unparsed-text`, `unparsed-text-lines`, `unparsed-text-available`, `json-doc` and `environment-variable` all go through the configured resolver, and are gated by `XsltSettings` (§21.1).
- Documents are cached per transformation by absolute URI, which gives the stable `doc()` identity the spec requires.

---

## 14. Trees

### 14.1 Compact tree

A new immutable tree in columnar (structure-of-arrays) layout is the default for input documents and temporary trees. Saxon's TinyTree is the prior art.

| Array | Element type | Meaning |
|-------|--------------|---------|
| `kind` | `byte` | Node kind |
| `depth` | `ushort` | Depth in the tree |
| `next` | `int` | Next sibling, or the parent shifted negative for the last child |
| `nameCode` | `int` | Name-pool id |
| `firstAttr`, `firstNs` | `int` | Index into the attribute and namespace arrays |
| `textOffset`, `textLength` | `int` | Index into one shared `char[]` text buffer |
| `typeAnnotation` | `int` | Schema-aware trees only |

- **Document order is the array index.** That makes node comparisons (`<<`, `>>`, `is`) and sorting into document order integer operations.
- **Descendant axes are contiguous index ranges:** a node's descendants are the following indexes whose depth is greater than the node's. Name tests on `//x` scan a `nameCode` array, which is cache-friendly.
- **A per-document name index** (from `nameCode` to the sorted node indexes) is built lazily the first time `//name` or `key()` needs it.
- **Whitespace stripping** (`xsl:strip-space`) is applied while the tree is built.
- **Typed values** are computed on demand and memoised for schema-aware trees only.
- **`XPathNavigator` adapter:** for compatibility with APIs that need a navigator.

**Memory target:** at most 75% of `XPathDocument` for the same document, measured on the benchmark corpus (§19.5).

### 14.2 Name pool

Each `XsltExecutable` gets a name pool (QName to `int`) that is shared, lock-free for reads, and seeded with every name the stylesheet uses. Trees built for that executable intern their names into it, so every name test compiles to an `int` comparison. Trees built under a different pool are re-mapped through a translation table when they're first used.

### 14.3 Foreign trees

`XmlDocument`, `XDocument` and arbitrary `XPathNavigator`s are accessed through the navigator path. That's slower but correct, and the API docs recommend `XdmNode.Load` for performance. Legacy 1.0 mode keeps using `XPathDocument` by default, so its behaviour stays identical. Using the compact tree there is an opt-in.

### 14.4 Temporary trees

Variables with content, `xsl:document`, `copy-of` and so on build compact trees through a builder that receives push events directly from `XmlQueryOutput`. Upstream builds these as `RtfNavigator`/`XPathDocument` result tree fragments. In the 3.0 processor every temporary tree is a full document node, as XSLT 2.0+ requires.

---

## 15. Serialization

The vendored raw writers are kept and modernised:

- **All Serialization 3.1 methods:** `xml`, `xhtml`, `html` (with `html-version="5.0"` rules), `text`, `json` and `adaptive`.
- **All parameters:**
  - `byte-order-mark`, `cdata-section-elements`, `doctype-public`/`-system` and `encoding`
  - `escape-uri-attributes`, `html-version`, `include-content-type` and `indent`
  - `item-separator`, `media-type`, `method` and `normalization-form`
  - `omit-xml-declaration`, `standalone`, `suppress-indentation` and `undeclare-prefixes`
  - `use-character-maps` and `json-node-output-method`
  - `allow-duplicate-names`, and `build-tree` for result delivery
- **Escaping fast path:** scan for the next character that needs escaping with `SearchValues<char>` and `IndexOfAny`, which are vectorised, then copy whole runs of clean text with `Span` copies. Upstream does a character-class table lookup for every character.
- **UTF-8 direct:** for the UTF-8 encoding, write to an `IBufferWriter<byte>` or `Stream` with `Utf8.FromUtf16`, without going through a `TextWriter`.
- **Character maps** use a perfect hash per map, and are skipped entirely on runs that contain no mapped character.
- **`xsl:result-document`:** each secondary result gets its own serializer instance from a pool. Destinations are chosen through `ResultDocumentHandler`, and the spec's rules on URI conflicts and writing to an already-read URI (`XTDE1490`, `XTRE1500`) are enforced.

---

## 16. XSLT 3.0 feature implementation

### 16.1 Instructions

| Instruction | How it lowers / is implemented |
|-------------|------------------------|
| `xsl:sequence` | Returns the select value as is. Template result types allow arbitrary items. |
| `xsl:for-each-group` | `GroupBy`/`GroupAdjacent`/… nodes (§13.3). `current-group()` is a loop-scoped variable. |
| `xsl:analyze-string` | `AnalyzeString` over a compiled regex. `regex-group()` reads match state. |
| `xsl:iterate`, `xsl:break`, `xsl:next-iteration`, `xsl:on-completion` | The `Iterate` node, lowered to an IL loop with locals for the parameters |
| `xsl:try`, `xsl:catch` | The `Try` node. Errors are .NET exceptions that carry an `XsltError` (QName, description, value, location). Output written inside the `try` is buffered and discarded if an error is caught, as `rollback-output` requires. With `rollback-output="no"`, output is written directly. |
| `xsl:merge`, `xsl:merge-source`, `xsl:merge-key`, `xsl:merge-action` | A k-way merge using a heap of cursors. Sources are checked to be in order, raising `XTDE2220` otherwise. |
| `xsl:fork` | Sequential evaluation, which is conformant. Parallel evaluation is opt-in (§19.4). |
| `xsl:map`, `xsl:map-entry` | `MapCtor`, with duplicate-key detection (`XTDE3365`) |
| `xsl:on-empty`, `xsl:on-non-empty`, `xsl:where-populated` | A *conditional content* buffer. Content is held in a small event buffer until the first non-empty item decides the outcome, then flushed. When the content is known not to be empty, the buffer is skipped entirely. |
| `xsl:assert` | `Assert`, enabled by `XsltCompilerOptions.EnableAssertions` |
| `xsl:evaluate` | §16.4 |
| `xsl:source-document` | Unstreamed: `doc()` plus a focus. Streamed: §17. |
| `xsl:result-document` | `ResultDocument` (§15) |
| `xsl:perform-sort` | `Sort` with sequence input |
| `xsl:namespace`, `xsl:document` | Constructors |
| `xsl:next-match`, `xsl:apply-imports` with `xsl:with-param` | Generalised dispatch (§19.2) |
| `xsl:context-item`, `xsl:global-context-item` | Type checks on the initial focus and template focus |
| `xsl:copy select=` and `xsl:copy-of` `copy-namespaces`/`copy-accumulators`/`validation` | Constructors with the new options |
| `xsl:message` with `error-code` and `terminate` | Calls `MessageHandler`. `terminate="yes"` raises the given error code. |

### 16.2 Modes and template rules

- **`xsl:mode` declarations:**
  - `on-no-match`: `text-only-copy`, `shallow-copy`, `deep-copy`, `shallow-skip`, `deep-skip` and `fail`
  - `on-multiple-match`
  - `warning-on-no-match` and `warning-on-multiple-match`
  - `typed`, `visibility`, `use-accumulators` and `streamable`
- **Built-in template behaviour** is compiled per mode into the dispatch table (§19.2). Built-in rules are never interpreted at run time.
- **`#current`, `#unnamed`, `#default` and `#all`** are resolved at compile time.

### 16.3 Accumulators

- **Unstreamed:** a one-pass pre-computation over the document on first use. `before`/`after` values are stored in two arrays indexed by node number, which is possible because of the compact tree. Only the accumulators named in `use-accumulators` and actually referenced are computed.
- **Streamed:** values are computed on the fly as the stream advances (§17).

### 16.4 Dynamic evaluation (`xsl:evaluate`)

1. Build the static context from the instruction's attributes: namespace context, `base-uri`, `with-params` and `context-item`.
2. Compile the expression through the XPath 3.1 front end and the optimiser, then:
   - **Sink A**, when IL emission is available and the expression has been seen at least twice
   - **Sink C** otherwise
3. Cache by expression text plus a static-context hash, in a bounded LRU per executable.
4. `XsltSettings.EnableEvaluate` defaults to `false` for `XslCompiledTransform`, matching the secure-by-default stance, and to `true` for `XsltCompiler`. When it is disabled, evaluating `xsl:evaluate` raises `XTDE3175`.

### 16.5 Packages

- **`xsl:package` compilation unit:** each package compiles to its own QIL module, with `visibility` (`public`, `private`, `final` and `abstract`) enforced at link time.
- **Linking:** `xsl:use-package` binds components through a component table. `xsl:override` replaces entries in that table. Overridden components are bound by indirection, a virtual slot, **only** if they are overridable. Final and private components bind directly and can be inlined.
- **Package library:** compiled packages are cached by name and version, and can be compiled ahead of time with `Xsl3.Build`, so a large library such as DocBook xslTNG is compiled once.

### 16.6 Higher-order functions

- **Inline functions** become closure classes (§12.1). Captured variables become fields.
- **Named function references** to built-in functions get one singleton `XdmFunctionItem` per function and arity, created at load time.
- **Function coercion** (XPath 3.1 §3.1.5.3) wraps the item in a coercing proxy only when the declared and actual signatures differ.
- **`fn:apply`, `fn:function-lookup`, `fn:for-each`, `fn:filter`, `fn:fold-left`, `fn:fold-right`, `fn:for-each-pair`, `fn:sort#3`, `map:for-each` and `array:*`:** when the function argument is statically known, the call is inlined. That's the common case, as in `fold-left(…, function($a,$b){…})`.

### 16.7 Error handling

- **Error codes:** every error has its spec code (`XTSE*`, `XTDE*`, `XTTE*`, `XTRE*`, `XPST*`, `XPDY*`, `XPTY*`, `FO*` and `SE*`).
- **Static errors** are collected: the compiler reports all of them, not just the first, each with its module URI, line and column.
- **Dynamic errors** are `XsltDynamicException` (a subclass of `XsltException`), carrying the error QName, description, error object and a stylesheet stack trace (template and function frames with locations).
- **`xsl:catch` scope:** it catches XSL3 errors only. .NET exceptions thrown by extension functions are wrapped as an implementation-defined error (`Q{http://xsl3/err}extension-error`) and can be caught. `OutOfMemoryException` and `OperationCanceledException` are never caught.

---

## 17. Streaming (P7)

Streaming is the most expensive feature in this spec. It builds on everything earlier and does not affect the earlier phases' design, beyond keeping QIL annotations open-ended.

- **Streamability analysis:** implement spec §19 exactly. That covers posture, sweep (`motionless`, `consuming`, `free-ranging`), the general streamability rules, and the per-construct rules for every instruction and function. The analysis runs over the XSL AST plus XPath ASTs, because the spec's rules are defined on constructs, not on QIL. Results are attached to QIL. A non-streamable construct in a streamable context is a static error (`XTSE3430`).
- **Execution model:** a push-based event pipeline over `XmlReader`, with a "streamed node" proxy that is valid only while it's being read. Grounded subtrees (`copy-of()`, `snapshot()`) are materialised into small compact trees.
- **Constructs to cover:**
  - `xsl:source-document streamable="yes"`
  - streamable modes
  - `xsl:merge` with streamable sources
  - streamed accumulators
  - `xsl:fork` with several consuming branches, implemented by fanning events out to each branch
  - streamable functions (`streamability` attribute) and `xsl:iterate` over streamed input
- **Memory target:** constant memory with respect to document size for a guaranteed-streamable stylesheet. CI checks this on a 2 GB generated document with the memory limit set to 200 MB.

---

## 18. Schema-awareness (P6)

- **`xsl:import-schema`:** load schemas into a `XmlSchemaSet`, using System.Xml's XSD 1.0 support.
- **Validation** of input and output (`validation`, `type` on constructors, `xsl:result-document`, `xsl:copy-of` and so on): validate with `XmlSchemaValidator` while building the tree, and store the type annotations in the compact tree's `typeAnnotation` column.
- **Typed values:** derive them from the annotations, including list and union types.
- **Schema types in sequence types and patterns:** `schema-element(N)`, `element(*, T)`, `attribute(*, T)` and `match="element(*, T)"`.
- **XSD 1.1 (stretch goal):** System.Xml has no XSD 1.1 validator. Options, to decide in P6:
  - (a) implement the 1.1 additions (assertions, conditional type assignment, `xs:dateTimeStamp`) on top of System.Xml's 1.0 validator
  - (b) offer an optional integration with an external validator
  - (c) declare XSD 1.0 only

  1.0 only is conformant: the spec lets processors choose their XSD version.
- Schema-aware compilation is only active when the stylesheet uses `xsl:import-schema`. Without it, nothing in the type system or trees costs anything extra.

---

## 19. Performance plan

### 19.1 Principles

1. **Compile, don't interpret.** All template dispatch, focus handling, type checks and built-in rules are resolved to IL at compile time.
2. **Unboxed and allocation-free on the hot path.** Values use typed storage, iterators are structs and nodes are `NodeRef`. CI fails if the steady-state allocation of a microbenchmark rises by more than 5%.
3. **Push by default.** Content goes straight from instructions to the serializer, without intermediate sequences.
4. **Laziness where it pays.** Variables used on some paths only, `exists()`/`empty()`, `[1]` and `head()` stop early.
5. **Indexes, built lazily.** Keys, automatic indexes, name indexes and accumulators are all built on first use, for each document.
6. **Measure every phase separately**: parse, compile, transform and serialize (§19.5).

### 19.2 Template dispatch

Each mode compiles to a **dispatch table** keyed first by node kind, then by name code:

- Element and attribute rules with a plain name test go in a hash map from name code to a priority-ordered rule list. A frozen `int`-keyed dictionary is used, and a perfect hash when there are fewer than 64 names.
- Rules with wildcard or predicate patterns go in a fallback list, evaluated in priority order after the exact-name rules.
- Pattern predicates compile to inlined boolean IL. They are tested only after the cheaper kind and name checks pass.
- The common `match="node()|@*"` identity template and `on-no-match="shallow-copy"` become a specialised copy loop.

This replaces upstream's `MatcherBuilder` strategy, which is a sequence of conditional tests ordered by priority.

### 19.3 Automatic indexing

Pattern: a filter `E[p]` evaluated repeatedly inside a loop, where:

- `E` is a path expression rooted at a document node and loop-invariant
- `p` is `K = V`, where `K` is a path relative to the filtered node and `V` is loop-variant

Rewrite: on first evaluation, build a hash index from `K` to nodes for that `E`, then look up `V`. This turns accidental O(n²) joins, which are common in real stylesheets, into O(n). It's guarded by a size threshold and can be switched off with `XsltCompilerOptions.AutoIndex`.

### 19.4 Other optimisations

**Input:**
- `XmlReaderSettings` tuned for speed: `DtdProcessing.Prohibit` by default, `IgnoreComments` when the stylesheet never matches or selects comments, and the same for `IgnoreProcessingInstructions`.
- Text is read in bulk into the compact tree's buffer.
- **Stretch goal:** a UTF-8 parser using `SearchValues`, for well-formed input without a DTD. It would only be adopted if it beats `XmlReader` by more than 1.5× on the corpus, and it falls back to `XmlReader` for anything it doesn't handle.

**Strings:**
- `StringBuilder` pooling
- concatenation sized exactly when the parts are known
- `string.Create` for `translate`, `upper-case` and `lower-case`
- interned literals

**Numbers:**
- `long` fast paths for `xs:integer` arithmetic, with checked overflow
- `double` arithmetic with no boxing
- `sum()`/`avg()` over typed sequences accumulate unboxed

**Calls:**
- avoid allocating for parameters by passing them in typed locals
- tunnel parameters go in a per-transform parameter stack, not dictionaries

**Compilation:**
- `OptimisationLevel.Fast` skips CSE, inlining and automatic indexing, for faster compiles of one-shot stylesheets
- compiled executables are cached by a hash of stylesheet content and options (`XsltCompiler.Cache`)
- Sink B removes compile cost entirely

**Parallelism (opt-in):**
- the extension attribute `xsl3:threads="N"` on `xsl:for-each` (the order of results is preserved)
- parallel `xsl:fork` branches
- trees are immutable, so concurrent reads are safe
- the default stays sequential, so output is deterministic and cost predictable

### 19.5 Benchmarking

**Harness:** BenchmarkDotNet. Each scenario reports:
- compile time
- first-transform (cold) time
- steady-state transform time
- allocation per transform
- peak working set

Parse, transform and serialize are timed separately using a pre-parsed input and a null serializer.

**Comparison engines:**

| Engine | Used for |
|--------|---------|
| Stock `XslCompiledTransform` (.NET 10) | 1.0 corpus. Gate for G4. |
| SaxonCS-HE 13.x | 2.0/3.0 corpus. Gate for G5. |
| SaxonCS-EE 13.x (evaluation licence) | Reference for optimiser features |
| SaxonJ-HE/EE 12.x+ on HotSpot | Reference for JIT steady state |
| SaxonC-HE 12.x | Reference for native start-up |

**Corpus.** Licences must be checked before anything is committed. Large generated files are produced by a script, not committed.

1. **Synthetic micro:** one stylesheet per feature (grouping, sorting, keys, regex, string functions, maps, `fold-left`, `xsl:iterate`, identity transform, deep recursion), each at 1 KB / 1 MB / 100 MB of input.
2. **XMark:** auction documents from the XMark generator at 1 MB, 10 MB, 100 MB and 1 GB, with the XMark queries ported to XSLT 3.0, and to XSLT 1.0 where possible.
3. **Real stylesheets, 1.0:**
   - DocBook XSL 1.79.x (HTML and FO output) over the DocBook guide
   - a large DITA-OT-style 1.0 preprocess, if one with a usable licence is available
4. **Real stylesheets, 2.0/3.0:**
   - DocBook xslTNG
   - TEI Stylesheets (TEI P5 to HTML)
   - Schematron through SchXslt (compiling and validating)
   - XSpec self-tests
5. **Rewritten pairs:** the same task written in idiomatic 1.0 and idiomatic 3.0, to measure the gains described in the earlier discussion: string recursion versus `tokenize`, Muenchian grouping versus `for-each-group`, and `node-set()` workarounds versus sequences.

**Regression gate:** every pull request runs the micro suite. A slowdown of more than 5% fails CI unless it's justified in the PR. The full corpus runs nightly and publishes its results.

### 19.6 Performance targets

| Scenario | Target |
|----------|--------|
| 1.0 stylesheets, legacy mode, steady state | ≥ 1.0× stock throughput (gate until P4). P4 exit: ≥ 1.2× stock on the benchmark corpus and never slower, target 1.5×, from span-based serialization and string handling, pooled buffers, the compact tree and dispatch. |
| 1.0 stylesheets, compile time | ≤ stock. With Sink B, effectively zero. |
| 2.0/3.0 stylesheets, steady state | ≥ 2× SaxonCS-HE median, never slower on any benchmark |
| 2.0/3.0 stylesheets, cold start (compile + first transform) | ≥ 2× SaxonCS-HE. With Sink B, ≥ 10×. |
| Rewritten pairs | The 3.0 version on XSL3 is at least as fast as the 1.0 version on stock, on every pair |
| Memory per input node | ≤ 75% of `XPathDocument` |
| Streaming | Constant memory (§17) |

---

## 20. Testing and conformance

| Suite | Source | Use |
|-------|--------|-----|
| W3C XSLT 3.0 test suite | `github.com/w3c/xslt30-test` (roughly 14,000+ test cases) | Main conformance measure. Tests are filtered by catalogue dependencies (`spec`, `feature`, `xsd-version`, …) against the features XSL3 claims. |
| W3C QT3 test suite | `github.com/w3c/qt3tests` (roughly 30,000 test cases) | XPath 3.1 and F&O 3.1. Run the XPath subset; skip the XQuery-only tests. |
| Upstream XSLT tests | `dotnet/runtime` `System.Private.Xml/tests/Xslt` | Legacy 1.0 regression gate. 100% pass is required. |
| Differential testing against stock | `Xsl3.Differential` | Every 1.0 corpus stylesheet and generated 1.0 XPath expressions. Output must be byte-identical in legacy mode. |
| Differential testing against Saxon | `Xsl3.Differential` | Generated XPath 3.1 expressions and real stylesheets. Differences are triaged against the spec, and Saxon is not treated as the oracle. |
| Property tests | `Xsl3.Tests` | Atomic type round-trips, cast tables, comparison laws, map/array persistence, collation consistency |
| Fuzzing | SharpFuzz on the XPath parser, stylesheet loader and JSON parser | Inputs must never crash the process |

**Conformance reporting:** `docs/conformance.md` is regenerated by CI, with pass, fail and not-applicable counts per feature and test set. Every known failure gets an issue.

**Both backends:** the full W3C run executes on Sink A and Sink C. Sink B runs a sampled subset, about 10%, through a compiled-assembly round trip.

---

## 21. Security, limits and diagnostics

### 21.1 Settings (secure by default)

| Setting | `XslCompiledTransform` default | `XsltCompiler` default |
|---------|------------------------------|------------------------|
| `EnableDocumentFunction` (`doc`, `document`, `collection`, `unparsed-text*`, `json-doc`) | `false`, as upstream | `false` |
| `EnableResultDocument` (writing secondary results) | `false` | `false`. When enabled, the default handler writes only under `BaseOutputUri`. |
| `EnableEvaluate` | `false` | `true` |
| `EnableExtensionFunctions` | `true` for objects added with `AddExtensionObject`, as upstream | `true` for registered functions |
| `EnableEnvironmentVariables` | `false` | `false` |
| `DtdProcessing` for inputs | `Prohibit` | `Prohibit` |
| Resolver | Same as upstream | None unless supplied |
| `MaxRecursionDepth` | None, as upstream (opt-in) | 10,000 |
| `MaxOutputBytes`, `Timeout`, `CancellationToken` | Unlimited / none | Unlimited / none |

### 21.2 Stack overflow protection

.NET cannot catch `StackOverflowException`. Deep recursion in a stylesheet kills the process, and that's true upstream today. XSL3 mitigates it in three ways:

1. **Recursion-depth counting** at every template and function entry, raising a catchable `Q{http://xsl3/err}recursion-limit` error.
2. **Recursion removal:** `xsl:iterate`, self tail calls and tail-recursive templates become loops.
3. **A large stack:** `XsltCompilerOptions.UseDedicatedThread` runs the transform on a thread with a configurable stack size (64 MB by default) for stylesheets that need deep, non-tail recursion.

### 21.3 Diagnostics

- Every compile and run-time error includes the module URI, line and column.
- **Debug mode** (`XsltCompilerOptions.Debug` or `new XslCompiledTransform(enableDebug: true)`): emits portable PDBs that map IL to stylesheet lines (Sinks A and B), turns off inlining, and keeps variables visible to the debugger.
- **Tracing:** `fn:trace` and `xsl:message` go to handlers. An optional `ITraceListener` receives template enter and exit events. It's compiled in only when requested, so it costs nothing otherwise.
- **`xsl3 explain main.xsl`:** dumps the optimised QIL for a stylesheet, using upstream's `QilXmlWriter`, and the chosen dispatch tables and indexes.

---

## 22. Roadmap

Effort is in **person-months for one senior .NET engineer** who knows the XSLT specs. These are planning estimates, not commitments. Phases P4 to P7 can overlap once P3 is stable.

| Phase | Scope | Exit criteria | Effort |
|-------|-------|---------------|--------|
| **P0: fork and baseline** | Extraction (§6.1), build, port the upstream tests, set up the benchmark harness and corpus, write `docs/internals.md` | Upstream XSLT tests 100%. Benchmark parity with stock within ±3%. | 1–2 |
| **P1: XDM and XPath 2.0 core** | Atomic types and storage (§8), extended type system, XPath 3.1 parser (the 2.0 subset first), lowering, core F&O (strings, numbers, dates, durations, QNames, sequences, aggregates, regex, collations) | QT3 XPath 2.0 F&O subset ≥ 90% | 5–7 |
| **P2: XSLT 2.0** | Loader changes, the 2.0 instructions (`for-each-group`, `analyze-string`, `sequence`, `function`, `result-document`, `next-match`, `perform-sort`, `namespace`, `document`), 2.0 patterns, the `xml`, `html`, `xhtml` and `text` serialization methods, backwards-compatible mode | `xslt30-test` XSLT 2.0-applicable tests ≥ 95% | 3–4 |
| **P3: XSLT 3.0 basic + XPath 3.1 + HOF** | Maps, arrays, function items and closures (Sink A required), `try`/`catch`, `iterate`, accumulators (unstreamed), `merge`, `xsl:mode`, TVTs, static processing, packages, JSON, the rest of F&O 3.1 | `xslt30-test` basic + XPath 3.1 + HOF ≥ 95%. QT3 XPath 3.1 ≥ 95%. | 7–10 |
| **P4: performance** | Beat stock `XslCompiledTransform` with modern .NET: `Span<T>`/`ReadOnlySpan<char>` and `SearchValues` in the serializer, parsing and string functions, pooled buffers (`ArrayPool`) instead of per-node allocation, struct enumerators and unboxed codegen instead of `item*` boxing, a compact tree and name pool, dispatch tables, automatic indexing, Sink B (`Xsl3.Build`) and NativeAOT, optional tiered interpreter | 1.0 stylesheets measurably faster than stock (≥ 1.2× steady state on the benchmark corpus, never slower), and every other §19.6 target except streaming | 4–6 |
| **P5: dynamic evaluation + serialization completion** | `xsl:evaluate`, `json` and `adaptive` methods, character maps, the remaining serialization parameters | The matching test sets ≥ 98% | 1–2 |
| **P6: schema-awareness** | §18 with XSD 1.0. Decide on XSD 1.1. | Schema-aware test sets ≥ 95% (XSD 1.0) | 3–5 |
| **P7: streaming** | §17 | Streaming test sets ≥ 90%. Constant-memory test passes. | 6–12 |
| **Hardening** (continuous) | Fuzzing, the long tail of conformance, documentation, divergence log | Overall pass ≥ 99% on claimed features | Ongoing |

**Totals:**
- A fast, useful XSLT 3.0 processor (P0–P5): roughly **21–31 person-months**.
- Full conformance including schema-awareness and streaming (P0–P7): roughly **30–48 person-months**.

Two engineers working in parallel shorten the calendar time, but not by half: the front end and type system (P1–P3) are hard to split.

**Release plan:**
- `0.x` previews after P2 (XSLT 2.0) and after P3
- `1.0` after P4 (basic 3.0, fast)
- schema-awareness and streaming ship in `1.x` minor releases

---

## 23. Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Spec long tail (edge cases in casting, dates, regex, serialization) takes longer than planned | High | Schedule | Test-driven from the W3C suites from day one. Publish pass rates. Keep a divergence log rather than blocking releases on rare edge cases. |
| Legacy 1.0 behaviour drifts from stock | Medium | Breaks G3 | Separate front end, identical optimiser rule set in legacy mode, byte-for-byte differential tests in CI |
| QIL can't express a 3.0 construct cleanly (for example streaming, `try` with rollback) | Medium | Design rework | §10 adds node types up front. Streaming analysis runs on the AST, not QIL. Prototype `Try` and `Iterate` during P1. |
| Moving from `DynamicMethod` to `RunAndCollect` assemblies slows compilation or uses more memory | Low–Medium | G4 compile-time target | Benchmark it in P0. Keep `DynamicMethod` for legacy mode if needed. |
| Unboxed codegen multiplies emitter complexity | Medium | Bugs | Run the W3C suite on the interpreter too, so emitter bugs show up as differences between the two backends |
| SaxonCS-HE improves, so G5 becomes a moving target | Medium | Positioning | G5 is measured against a pinned version per release, and the reference version is stated in the release notes |
| One-maintainer risk | High | Project continuity | Keep the docs current, CI fully automated and the spec mapping in code comments (spec section numbers on implementations) |
| Licence problems in the benchmark corpus | Low | Can't publish benchmarks | Check every corpus item's licence before committing it. Generate synthetic data with scripts. |

---

## 24. Extension points for XSLT 4.0 (not built now)

The design must leave room for these. Nothing here is implemented in this spec.

- **New XPath 4.0 syntax** (the `=!>` mapping arrow, `otherwise`, string templates, the `fn { }` function shorthand): the parser is table-driven by version, with a `LanguageLevel` flag.
- **Records and new types:** `XmlQueryType` gains a record type slot.
- **New instructions** (`xsl:array`, `xsl:switch` and others in the current draft): the loader's instruction table is keyed by name and minimum version.
- **The `fn:` and `array:` additions:** the function library is registered per language level.

---

## 25. Open questions

1. **Final name and namespace.** It must avoid the `System.` prefix and trademarks (§1).
2. **Legacy 1.0 defaults.** Should `XslCompiledTransform.Load` of a 1.0 stylesheet use the compact tree by default after P4? That would be faster but no longer byte-identical in edge cases such as `generate-id()` strings and whitespace-only text node handling. The current proposal is opt-in. *Implemented that way (October 2026): `XslCompiledTransform.UseCompactTree`, off by default for 1.0 and always on for 2.0+. The trees read alike except for `generate-id()` strings.*
3. **XSD 1.1.** Choose between options (a), (b) and (c) in §18 during P6.
4. **Minimum framework.** .NET 8 needs a .NET 10 build host for Sink B. Is a .NET 10-only release simpler?
5. **Parallelism.** Should `xsl3:threads` ship in 1.0, or wait until there's feedback?
6. **Project licence.** MIT matches upstream. Is a dual licence or a commercial support offering wanted? That affects contribution terms (a CLA or the DCO).

---

## Appendix A. Upstream files and what happens to each

| Upstream (`System/Xml/…`) | Fate |
|---------------------------|------|
| `Xslt/XslCompiledTransform.cs`, `XsltArgumentList.cs`, `XsltSettings.cs`, `XsltContext.cs`, `XsltException.cs` | Kept. API extended additively (§7.1). |
| `Xslt/XslTransform.cs`, `Xsl/XsltOld/*` | Dropped |
| `Xsl/Xslt/XsltLoader.cs`, `XsltInput.cs`, `Keywords.cs` | Extended for 3.0 (§9.1) |
| `Xsl/Xslt/QilGenerator.cs`, `QilGeneratorEnv.cs`, `XsltQilFactory.cs`, `InvokeGenerator.cs` | Extended. 3.0 lowering added. |
| `Xsl/Xslt/XslAst.cs`, `XslAstAnalyzer.cs`, `XslVisitor.cs`, `XslFlags.cs`, `Focus.cs` | Extended (new AST nodes, focus and type analysis) |
| `Xsl/Xslt/MatcherBuilder.cs`, `KeyMatchBuilder.cs`, `XPathPatternParser.cs`, `XPathPatternBuilder.cs` | Replaced for the 3.0 processor (§9.3, §19.2). Kept as they are for legacy mode. |
| `Xsl/Xslt/Scripts.cs` | Kept as a stub that throws, as upstream does |
| `Xsl/XPath/*` | Kept unchanged for legacy 1.0 mode |
| (new) `Xsl3/XPath31/*` | New XPath 3.1 front end (§9.2) |
| `Xsl/QIL/*` | Extended with new nodes (§10) |
| `Xsl/XmlQueryType*.cs`, `XmlQueryCardinality.cs` | Extended (§8.3) |
| `Xsl/IlGen/XmlILOptimizerVisitor.cs`, `*.p` | C# is now the source of truth. The `.p` files are kept as documentation only. New rules added (§11). |
| `Xsl/IlGen/XmlIlVisitor.cs`, `GenerateHelper.cs`, `IteratorDescriptor.cs`, `XmlIlTypeHelper.cs`, `XmlILConstructAnalyzer.cs`, `TailCallAnalyzer.cs`, `StaticDataManager.cs` | Extended (§12.4) |
| `Xsl/IlGen/XmlILModule.cs` | Replaced by the sink abstraction (§12.1–12.3) |
| `Xsl/Runtime/*` | Extended. `XsltLibrary` and `XsltFunctions` gain the F&O 3.1 library (§13). `RtfNavigator` is used in legacy mode only. |
| `Cache/*` (`XPathDocument` internals) | Vendored for legacy mode. The compact tree is added (§14). |
| `Core/*RawTextWriter*`, `QueryOutputWriter*.cs`, `XmlRawWriter.cs` | Vendored with the generated C# committed. Modernised (§15). |
| `XmlCharType.cs`, `ValidateNames.cs`, `Schema/XsdDateTime.cs`, `Schema/XsdDuration.cs`, `BitStack.cs` and the `SR` strings | Vendored |
