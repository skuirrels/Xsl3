# Phase 3 plan: XSLT 3.0 basic, XPath 3.1, higher-order functions

SPEC §22, P3: maps, arrays, function items and closures, `try`/`catch`, `iterate`, accumulators (unstreamed),
`merge`, `xsl:mode`, text value templates, static processing, packages, JSON, the rest of F&O 3.1.
**Exit criterion: `xslt30-test` basic + XPath 3.1 + HOF ≥ 95%; QT3 XPath 3.1 ≥ 95%.**

This file is the working plan; tick items off as they land and keep the decisions section current.

## Status

2026-10-19: maps, arrays, function items (inline functions, named references, partial application, dynamic
calls), the higher-order functions and the JSON functions are in. QT3 with `higherOrderFunctions` claimed:
99.1%. Then the first XSLT 3.0 instructions: static variables and parameters, shadow attributes,
`xsl:where-populated`, `xsl:on-empty`/`xsl:on-non-empty`, `xsl:try`/`xsl:catch`, `xsl:iterate`,
`xsl:fork` (unstreamed), template rules for items that aren't nodes, `fn:copy-of`, `fn:snapshot`,
`xsl:source-document` (unstreamed), `xsl:copy select`, `default-mode`, `xsl:context-item`, HTML5
serialization. xslt30-test 3.0 profile: 89.6%. Then the json and adaptive output methods (the principal
result is delivered raw and serialized by `Runtime.ItemSerializer`), `fn:serialize` with a map,
`xsl:map`/`xsl:map-entry`, accumulators, `xsl:merge`, `on-multiple-match="fail"`, single-package stylesheets.
Then `xsl:use-package` (the package is loaded like an import, from a package library; `xsl:override` declarations
belong to the using package), so the package tests run: 7,058 passed of 7,939 run (88.9%).
Then package visibility (decision 9): 7,193 passed (90.6%). Then the html method's own serializer
(`Runtime.HtmlSerializer`, over a text-method writer), `item-separator`, json and adaptive results of
`xsl:result-document`, and the XSLT 3.0 pattern forms: parenthesized unions as steps are distributed
(`x/(a|b)` is `x/a | x/b`), `intersect`/`except` take the difference per context node as §5.5.3 defines
unless both sides pin the same context, and other valid patterns are evaluated from each ancestor-or-self
with child-or-top; an error in a predicate pattern is a non-match. 7,255 passed (91.4%). Then modes:
where a template's mode isn't known when it's compiled (named templates, rules of several modes or of all
modes), the apply-templates functions record their mode in the tunnel frame and `#current` and
`xsl:next-match` dispatch on it; `xsl:copy` of a document node in a sequence makes a document node;
`document()` takes sequences. 7,312 passed (92.1%). Then `fn:current-output-uri` (the result document's URI
travels in the tunnel frame like the mode; absent in functions, patterns and variables), the base output URI
(internal for now), xhtml with HTML5, the serialization parameter errors, and the XML declaration written by
the writer itself (raw text before the document element stopped its indentation). 7,365 passed (92.8%).
Then decimal formats, keys and namespace aliases local to their package (their names carry the package id
like components'), abstract attribute sets: 7,389 passed (93.1%).
Next: initial functions, `xsl:evaluate`, modes and `xsl:copy` details, error-code coverage.

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
4. **Dynamic calls dispatch in generated code.** Each compiled unit has one generated dispatcher: a QIL
   function that takes a pending call (the function item and its arguments, already converted to the declared
   types), compares the item's id with every lifted function and invokes it with the captured values and the
   arguments. Maps, arrays and built-in functions found at run time go to the run-time library.
5. **Higher-order library functions compile to QIL**, or run in the library: the generated dispatcher is also
   exposed to the run-time library as a delegate (`XmlQueryStaticData.Dispatcher`), so folds are C# loops
   rather than recursion. The other higher-order functions when they take function items (`fn:for-each`,
   `fn:filter`, `fn:fold-left`, `fn:fold-right`, `fn:for-each-pair`, `fn:sort#3`, `fn:apply`,
   `map:for-each`, `array:for-each`, `array:filter`, `array:for-each-pair`, `array:sort#3`) compile to QIL
   loops whose calls go through the dispatcher.
6. **XSLT 3.0 is the current version.** Once the 3.0 instructions land, `version="3.0"` stops being
   forwards-compatible.
7. **Instructions that need control flow QIL lacks are lifted.** `xsl:try` and `xsl:iterate` compile their
   sequence constructors into QIL functions of the variables, parameters and focus iterators they use (found
   by a free-reference walk; global variables are captured too, so their errors aren't caught). The run-time
   library calls them through the dispatcher: `X31Try` inside a C# try/catch, with the value materialized
   first so no partial result escapes; `X31Iterate` in a loop, the body's value ending in a control item for
   `xsl:next-iteration` (the new parameter values) or `xsl:break`. The positions of captured focus iterators
   are captured separately, since a captured iterator is a `Let`.
9. **Packages share one compiled program.** Every component of a used package is registered under a name in a
   namespace of its own (`PackageComponents.Mangle`); each package has a table of the components its code can
   see, by plain name, built from the visibility rules (`xsl:expose`, `xsl:accept`, defaults), with overridden
   components redirected to their overrides. The compiler resolves template, function, variable, attribute-set
   and mode names through the table of the package whose code it is compiling.
8. **Template rules match any item.** A template rule whose pattern is a predicate pattern (`.`, `.[…]`) takes
   an `item()` context; `xsl:apply-templates` over items that aren't nodes goes to a second matcher per mode
   that tries only those rules, then the built-in rules for items (text-only-copy: the value as text).

## Steps

- [x] XDM: `XdmMap`, `XdmArray`, `XdmFunction`; `deep-equal`, atomization and string value rules; sequence
      type tests `map(K, V)`, `array(T)`, `function(…) as T`, function coercion.
- [x] XPath 3.1 syntax: map and array constructors, lookups (`?key`, `?*`, unary `?`), inline functions,
      named function references, dynamic function calls, partial application, the arrow operator with
      function expressions.
- [x] Function library: `map:*`, `array:*`, the higher-order `fn:*` functions, `fn:function-lookup`,
      `fn:function-name`, `fn:function-arity`. (Missing: `fn:random-number-generator`.)
- [x] JSON: `fn:parse-json`, `fn:json-doc`, `fn:json-to-xml`, `fn:xml-to-json` (the `fallback` option
      isn't called), the `json` and `adaptive` output methods (principal result only).
- [ ] XSLT 3.0 instructions: text value templates, `xsl:iterate`, `xsl:try`/`xsl:catch`, `xsl:where-populated`,
      `xsl:on-empty`/`xsl:on-non-empty`, `xsl:fork`, static parameters and shadow attributes, `xsl:import`
      anywhere, predicate patterns and template rules for items, `xsl:mode` (`on-no-match`,
      `on-multiple-match`), `default-mode`, `xsl:merge`, accumulators, `xsl:map`/`xsl:map-entry` (done);
      `xsl:evaluate`, `xsl:assert`, packages with `xsl:use-package`. Known gaps: `err:line-number`/`err:column-number`, static variables
      from modules imported after a reference, dynamic errors in patterns as non-matches.
- [x] XSLT 3.0 functions: `fn:copy-of`, `fn:snapshot`.
- [x] Profile: claim XSLT 3.0 basic, XPath 3.1 and higher-order functions in the xslt30-test harness.
- [ ] Exit: ≥ 95% of that profile; QT3 ≥ 95% with higher-order functions claimed.
