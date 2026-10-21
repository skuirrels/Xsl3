# P6: schema-awareness

Remaining-scope workstream 6 (SPEC §18). Started 2026-10-21 at the owner's request.

## Decisions

These are the plan's two open questions; the defaults below are what the work assumes. The owner can overrule them.

1. **XSD 1.0, with .NET's `System.Xml.Schema`.** `XmlSchemaSet` compiles the schemas and `XmlSchemaValidator`
   validates. Both are in the base class library, so schema-awareness adds no dependency. XSD 1.1 (assertions,
   conditional types) isn't in .NET; tests that need it (`XSD_1.1`, 8 of them) stay unclaimed. An XSD 1.0 processor
   is conformant (XSLT 3.0 §3.14).
2. **In the Xsl3 assembly, zero cost when unused.** SPEC §6.3 suggested a separate `Xsl3.Schema` package. As the
   schema classes are already in the BCL, the split would only move code. What it was for, that a stylesheet without
   `xsl:import-schema` pays nothing, is kept by making every schema path conditional on a schema being imported or
   validation being asked for. The micro suite and the perf gate check this.

## How the suite is measured

`XSL3_XSLT30_PROFILE=sa` runs the tests that need `schema_aware` (with the 3.0 profile's other features). The
baseline isn't checked for it until the feature is claimed in `Profile.cs`, which happens when these tests reach 95%.

## Stages

| Stage | What | State |
|---|---|---|
| 6.1 | This plan; the `sa` profile in the runner | Done |
| 6.2 | `xsl:import-schema` (a `schema-location`, an inline `xs:schema`, or a namespace the processor knows: `XsltSettings.Schemas`, and the XML and fn namespaces' schemas, built in) into one `XmlSchemaSet`; schema types in sequence types: user-defined atomic, list and union types (`as`, casts, `castable as`, constructor functions), `schema-element()`, `schema-attribute()`, `element(N, T)`, `attribute(N, T)`; `type-available`, `function-available` and `system-property('xsl:is-schema-aware')` | Done, but per stylesheet rather than per package |
| 6.3 | Validation of input: the API's (a validating `XmlReader` into a compact tree keeps the annotations), `validation` on `xsl:source-document`, `doc()`/`document()` by `default-validation`; a type-annotation store in the compact tree; typed values from annotations (`data()`, atomization, comparisons), list and union types, schema normalized values; `type` and `nilled` accessors | Done except `doc()` and the accessors |
| 6.4 | Validation of output: `validation` and `type` on literal result elements, `xsl:element`, `xsl:attribute`, `xsl:copy`, `xsl:copy-of`, `xsl:document`, `xsl:result-document`; `default-validation`; XTTE1510–XTTE1555 | Done except `validation="preserve"` on `xsl:copy`, and `default-validation` per module (one value per stylesheet here; constructed elements stay untyped rather than xs:anyType). Temporary trees keep their children's annotations (XSLT 3.0 §9.4) |
| 6.5 | Patterns with schema types (`match="element(*, T)"`, `schema-element()`), `xsl:mode typed`, `input-type-annotations="strip"` | Done |
| 6.6 | Claim `schema_aware` in `Profile.cs` at ≥ 95%; benchmarks show no cost without schemas | Done (2026-10-21): claimed in the 3.0 profile at 95.0%; the micro suite's geometric mean is within 4% of the commit before P6. `XsltSettings.SchemaAware` stays off by default (the owner's call) |

## How it works

- `XsltSettings.SchemaAware` turns it on (off by default until 6.6). Without it, `xsl:import-schema` is XTSE1650 as before.
- The compact tree keeps type annotations in two dictionaries (elements, attributes) and a set of nilled elements, null in
  an untyped tree. The builder fills them from a validating reader's `SchemaInfo`; nothing else changes for untyped trees.
- A schema's simple types become `AtomicType`s (Schema/SchemaTypes.cs), one per schema type; casting checks the
  built-in base type first, then the schema's facets through `XmlSchemaDatatype.ParseValue`.
- .NET's built-in datatypes are narrower than XSD's (years outside 1 to 9999, decimals beyond `System.Decimal`). Input
  validation accepts such a value when Xsl3's own casting does (Schema/Validation.cs).
- Constructed nodes are validated by walking them through `XmlSchemaValidator` into a new annotated tree
  (Schema/NodeValidator.cs); `type="T"` initialises the validator with the type. The validated node is the instruction's
  result, so its annotations survive where the result is the item itself (a variable with `as`, `xsl:sequence`), and are
  stripped when it is copied into an untyped parent, as `validation="strip"` asks.
- A stylesheet that uses schemas can't be saved (`CompileToAssembly`): the schemas aren't saved with it.

## Progress

| Date | `XSL3_XSLT30_PROFILE=sa` |
|---|---|
| 2026-10-21, start | 79 passed, 70 failed, 697 not run |
| 2026-10-21, 6.2–6.5 in part | 707 passed, 137 failed, 2 not run (83.8%) |
| 2026-10-21, second part | 802 passed, 42 failed, 2 not run (95.0%) |
