# Phase 3 plan: XSLT 3.0 basic, XPath 3.1, higher-order functions

SPEC §22, P3: maps, arrays, function items and closures, `try`/`catch`, `iterate`, accumulators (unstreamed),
`merge`, `xsl:mode`, text value templates, static processing, packages, JSON, the rest of F&O 3.1.
**Exit criterion: `xslt30-test` basic + XPath 3.1 + HOF ≥ 95%; QT3 XPath 3.1 ≥ 95%.**

This file is the working plan; tick items off as they land and keep the decisions section current.

## Status

2026-10-19: maps, arrays, function items (inline functions, named references, partial application, dynamic
calls), the higher-order functions and the JSON functions are in. QT3 with `higherOrderFunctions` claimed:
99.1%. Next: the XSLT 3.0 instructions.

## Where P2 leaves us

- Every XPath value is boxed as `item`/`item*`: `AtomicValue` subclasses and `XPathNavigator`s, both
  `XPathItem`s. QT3 XPath 3.1 is at 95.9% without maps, arrays and function items (about 800 tests fail on them).
- XSLT 2.0 stylesheets compile through the 2.0 path of `QilGenerator`; the xslt30-test 2.0 profile is at 95.2%.
- Stylesheets with `version="3.0"` are compiled by the 2.0 path in forwards-compatible mode.

## Decisions

1. **Maps, arrays and function items are `XPathItem`s.** `XdmMap`, `XdmArray` and `XdmFunction` derive from
   `XPathItem` like `AtomicValue`, so they travel through the boxed `item*` representation, variables, template
   parameters and sequences unchanged. Their `Value` raises FOTY0014 where a string value is asked for, and
   atomization raises FOTY0013.
2. **Maps** are persistent hash tries (CHAMP) keyed by atomic values with `op:same-key` equality; **arrays** are
   persistent vectors. Both are immutable; `map:put` and `array:append` share structure.
3. **Inline functions are lambda-lifted.** Each inline function expression becomes a QIL function whose
   parameters are its captured variables followed by its declared parameters. Evaluating the expression
   builds an `XdmFunction` that holds the function's id and the captured values.
4. **Dynamic calls dispatch in generated code.** Compiled code can't hand delegates to the runtime library, so
   each arity that is called dynamically gets a generated dispatcher: a QIL function that compares the item's
   id with every lifted function of that arity and invokes it with the captured values and the arguments;
   other function items (named references to library functions, partial applications, map and array
   functions) go to the runtime library.
5. **Higher-order library functions compile to QIL**, or run in the library: the generated dispatcher is also
   exposed to the run-time library as a delegate (`XmlQueryStaticData.Dispatcher`), so folds are C# loops
   rather than recursion. The other higher-order functions when they take function items (`fn:for-each`,
   `fn:filter`, `fn:fold-left`, `fn:fold-right`, `fn:for-each-pair`, `fn:sort#3`, `fn:apply`,
   `map:for-each`, `array:for-each`, `array:filter`, `array:for-each-pair`, `array:sort#3`) compile to QIL
   loops whose calls go through the dispatcher.
6. **XSLT 3.0 is the current version.** Once the 3.0 instructions land, `version="3.0"` stops being
   forwards-compatible.

## Steps

- [x] XDM: `XdmMap`, `XdmArray`, `XdmFunction`; `deep-equal`, atomization and string value rules; sequence
      type tests `map(K, V)`, `array(T)`, `function(…) as T`, function coercion.
- [x] XPath 3.1 syntax: map and array constructors, lookups (`?key`, `?*`, unary `?`), inline functions,
      named function references, dynamic function calls, partial application, the arrow operator with
      function expressions.
- [x] Function library: `map:*`, `array:*`, the higher-order `fn:*` functions, `fn:function-lookup`,
      `fn:function-name`, `fn:function-arity`. (Missing: `fn:random-number-generator`.)
- [ ] JSON: `fn:parse-json`, `fn:json-doc`, `fn:json-to-xml`, `fn:xml-to-json` (done; the `fallback` option
      isn't called), the `json` and `adaptive` serialization methods.
- [ ] XSLT 3.0 instructions: text value templates, `xsl:iterate`, `xsl:try`/`xsl:catch`, `xsl:mode`
      (`on-no-match`, `on-multiple-match`), `xsl:merge`, accumulators, `xsl:evaluate`, `xsl:assert`,
      `xsl:where-populated`, `xsl:on-empty`/`xsl:on-non-empty`, `xsl:map`/`xsl:array`, static parameters and
      shadow attributes, packages.
- [ ] Profile: claim XSLT 3.0 basic, XPath 3.1 and higher-order functions in the xslt30-test harness.
- [ ] Exit: ≥ 95% of that profile; QT3 ≥ 95% with higher-order functions claimed.
