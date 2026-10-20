# Phase 5 plan: dynamic evaluation and the rest of serialization

SPEC §22, P5: `xsl:evaluate` (SPEC §16.4), the `json` and `adaptive` methods, character maps, the remaining
serialization parameters (SPEC §15). **Exit criterion: the matching xslt30-test sets ≥ 98%.**

This file is the working plan; tick items off as they land and keep the decisions section current.

## Where P3 and P4 leave us

- The json and adaptive output methods, `fn:serialize` with a map, character maps and `item-separator` are in
  (P3). The html method has its own serializer (`Runtime.HtmlSerializer`); xml, xhtml and text use the vendored
  raw writers.
- `xsl:evaluate` is not implemented. The harness doesn't claim the `dynamic_evaluation` feature, so the
  `evaluate` set doesn't run; `system-property('xsl:supports-dynamic-evaluation')` is `no`.
- An XPath 3.1 expression can already be compiled on its own at run time: `XPath31Compiler.CompileHosted`
  compiles an AST with a host environment (`IXPath31Environment`) to its own `XmlILCommand`, as `use-when` does
  at load time.
- Function items (`CompiledFunction`) carry the id of a lifted function in the unit that made them, and every
  unit's dispatcher assumes the ids are its own.

Measured on 2026-10-20 (Release, .NET 10, suites at the pinned commits), before P5:

| Set | Passed | Failed | Pass rate |
|---|---:|---:|---:|
| `evaluate` | not run (feature not claimed) | | |
| `output` | 198 | 23 | 89.6% |
| `character-map` | 24 | 5 | 82.8% |
| `result-document` | 127 | 13 | 90.7% |
| `disable-output-escaping` | 16 | 9 | 64.0% |

## Which sets count toward the exit

The exit criterion is measured over five xslt30-test sets, by the harness as it runs the 3.0 profile with
`dynamic_evaluation` claimed:

| Set | Why it counts |
|---|---|
| `evaluate` | `xsl:evaluate` itself |
| `output` | `xsl:output`: every serialization parameter, every method |
| `character-map` | `xsl:character-map` and `use-character-maps` |
| `result-document` | serialization parameters on secondary results, json/adaptive results, result-document errors |
| `disable-output-escaping` | the serializer's side of `disable-output-escaping` (the only other set that depends on the `serialization` feature alone) |

- **Exit:** the five sets together ≥ 98% passed of those run, and none of the five below 95%. Per-set
  numbers go in docs/conformance.md.
- The tests in other sets that depend on `dynamic_evaluation` (`try-027`, `system-property-014a`; and, with it
  unclaimed, `error-3175a`, `system-property-014b`) are covered by the overall baseline, not the exit number.
- Tests that need schema-awareness (`evaluate-009`…`-014`, `-032`…`-040`) or streaming (`evaluate-046`)
  don't apply (P6, P7).
- The harness passes an expected error on any error (docs/conformance.md); the report also counts how many of
  the exit sets' error tests raised the code they name, so the error codes can be checked separately.

## Decisions

1. **`xsl:evaluate` is lowered by the loader.** `XsltLoader.V2` turns `<xsl:evaluate>` into an `xsl:sequence`
   whose content binds each `xsl:with-param` to a variable with an internal name, then calls an internal
   function, `Q{urn:xsl3:evaluate}evaluate(…)`, with the target expression's string, the context item, the
   namespace context node, the base URI, the `with-params` map and a map of the `xsl:with-param` values. The
   result is converted to the `as` type by the code that converts `xsl:variable` values (the function conversion
   rules), so `xsl:evaluate` adds no QIL node type. The 2.0+ path compiles the internal function into a call of
   `XsltLibrary.X31Evaluate` with a static-context id.
2. **The static context is a constant.** At compile time each `xsl:evaluate` gets an `EvaluateContext` in the
   constant pool: the in-scope namespaces of the instruction (without the default namespace), its
   `xpath-default-namespace`, default collation, static base URI, the decimal formats in scope, the `as` types of
   the `xsl:with-param` children, and the stylesheet functions the target expression may call (decision 4).
3. **The target expression compiles at run time, through Sink A, cached.** `X31Evaluate` builds an
   `XPathStaticContext` (namespaces from the `namespace-context` node, else the constant ones; the variables are the
   `xsl:with-param` names and the `with-params` keys) and compiles the expression with `XPath31Compiler` and an
   `IXPath31Environment` for evaluation: the context item, position and size are supplied (a singleton focus) or
   absent (XPDY0002); variables are external parameters; stylesheet functions are dynamic calls of function items
   passed in as hidden parameters. Compiled expressions are cached per `EvaluateContext` in a bounded LRU keyed by
   the expression text, the namespace bindings, the base URI, the variable names and whether there is a context
   item. There is no interpreter yet (SPEC §12.3, Sink C), so the "seen twice" rule of §16.4 doesn't apply:
   every expression is compiled to IL once and reused.
4. **Stylesheet functions reach the target expression as function items.** XSLT 3.0 §10.4.1: the target expression
   may call the user-defined functions of the containing package that aren't private or hidden. The loader
   passes named function references to them (`Q{ns}f#n`, one per function and arity) as a sequence argument of the
   internal call, so they're compiled in the stylesheet's own unit. XSLT-defined functions in the `fn` namespace
   (`current`, `key`, `system-property`, …) aren't available (XPST0017, so XTDE3160).
5. **Function items belong to their unit.** A `CompiledFunction` records the runtime that created it; a dynamic
   call of a function item from another compiled unit (a stylesheet function called from a target expression, an
   inline function returned by one and called by the stylesheet) goes to the run-time library, which calls the
   owner's dispatcher with the owner's runtime.
6. **One execution scope.** The target expression runs in a child `XmlQueryRuntime` whose `fn:current-dateTime`,
   implicit timezone and document resolver are the transformation's (XSLT 3.0 §10.4.3). The child's documents are
   not shared with the transformation's document cache yet.
7. **Security: statically disabled by default.** `XsltSettings.EnableEvaluate` defaults to `false` (SPEC §16.4,
   §21.1); `XsltSettings.TrustedXslt` enables it. With it off, the feature is *statically disabled* (XSLT 3.0
   §10.4.4): `element-available('xsl:evaluate')` is false and `system-property('xsl:supports-dynamic-evaluation')`
   is `no` everywhere, an `xsl:fallback` child is used, and otherwise evaluating the instruction raises XTDE3175.
8. **Errors.** A static error in the target expression is XTDE3160 (the XPath code in the message); a dynamic
   error keeps its own code; more than one context item is XTTE3210; a `namespace-context` that isn't one node is
   XTTE3170; a `with-params` value that isn't a `map(xs:QName, item()*)` is XPTY0004.

## Milestones

### M1: `xsl:evaluate` (decisions 1–8)

- [x] `XsltSettings.EnableEvaluate`; `element-available`, `system-property`, `xsl:fallback` and XTDE3175 follow it.
- [x] Loader: attributes (`xpath`, `as`, `base-uri`, `with-params`, `context-item`, `namespace-context`,
      `schema-aware`), `xsl:with-param` and `xsl:fallback` children, static checks (XTSE0010, XTSE0090, XTSE0670).
- [x] Generator and runtime: `X31Evaluate`, `EvaluateContext`, the LRU cache, the child runtime.
- [x] Stylesheet functions and function items across units (every unit with function items now has a dispatcher).
- [x] Harness: claim `dynamic_evaluation` and enable it for the run; unit tests (`tests/Xsl3.Tests/EvaluateTests.cs`).
- [x] `evaluate` ≥ 98% of the tests that apply: 42 / 42.

### M2: character maps and the parameters around them

- [ ] Characters produced by a character map are not normalized (`normalization-form`) or escaped again.
- [ ] Character maps and `cdata-section-elements`; character maps and URI escaping in html/xhtml; maps are not
      applied to namespace declarations.
- [ ] `character-map` 100%.

### M3: the remaining serialization parameters

- [ ] `include-content-type` for xhtml (the `meta` element) and html5; `escape-uri-attributes` for xhtml.
- [ ] Doctype identifiers that contain both kinds of quote; `\r` in comments and processing instructions.
- [ ] `parameter-document` (Serialization 3.1 §3.1), for `xsl:output` and `xsl:result-document`.
- [ ] json and adaptive output of maps through `xsl:result-document` and `xsl:output`; SERE0012/SESU0011 checks.
- [ ] `output` and `result-document` ≥ 98%.

### M4: `disable-output-escaping` and the long tail

- [ ] `disable-output-escaping` on text nodes that reach the result through a sequence or a temporary tree.
- [ ] The rest of the five sets' failures, by count.
- [ ] Exit: the five sets ≥ 98% together; docs/conformance.md, README roadmap row.

## Status

2026-10-20: plan written. Before P5 the five sets stand at 365 passed of 415 run (88.0%), `evaluate` not run.

2026-10-20: M1 done. `evaluate` 42 / 42; `try-027` and `system-property-014a` pass with the feature claimed, and
`higher-order-functions-073` and `collations-0128` pass in passing (a function item escaping a unit without
dynamic calls now has a dispatcher). The five sets: 407 passed of 457 run (89.1%). xslt30-test 3.0 profile:
7,593 passed, 384 failed, 86 not run (95.2%); 2.0 profile unchanged at 96.3%.
