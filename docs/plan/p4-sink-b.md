# Plan: Sink B (`Xsl3.Build`) and NativeAOT

docs/plan/remaining-scope.md, workstream 2 (items 2.1 to 2.4): stylesheets compiled ahead of time into an assembly,
then run under NativeAOT. SPEC §12.2, §6.3, goal G7.

This file is the plan the brief asks for in 2.1. It records what blocks NativeAOT today, how the compiled form survives
being saved and loaded, and the order of work.

## What happens today

These facts were measured on 2026-10-21 with .NET 10 on osx-arm64. A probe app references `src/Xsl3` with
`PublishAot=true` and runs a one-template XSLT 1.0 stylesheet.

- **Publishing succeeds with 14 trim/AOT warnings from 8 sources:**

  | Warning | Where | Why |
  |---|---|---|
  | IL3050 | `XslCompiledTransform()` and its callers | the constructor is marked `RequiresDynamicCode` |
  | IL3050 | `XPath31Compiler.CompileHosted` | compiles to IL (use-when, `xsl:evaluate`) |
  | IL3050 | `QilGenerator.CompileInitialFunction31` | through the generator |
  | IL2026 | `XPath31Compiler..cctor` | the `XsltLibrary` methods are found by reflection (`GetMethods`) |
  | IL2075 | `QilGenerator.MentionsPositionOrLast` | reflection over QIL node fields |
  | IL2080 × 6 | `Bridges/DtdInfoBridge.GetIdAttributes` | `Type.GetProperty` on System.Private.Xml internals |
  | IL2080 × 2 | `Bridges/XmlReaderInternals..cctor` | the same |

- **The AOT binary fails at start-up.** `XmlILModule`'s static constructor calls `CreateLREModule`, which needs
  Reflection.Emit and throws `PlatformNotSupportedException`. A precompiled stylesheet would hit it too, before any
  compilation is needed.
- **IL is emitted as `DynamicMethod`s** in release builds (`XmlILModule`, one shared module). Debug builds use a
  non-collectible `AssemblyBuilder` holding one type of static methods, which is close to what Sink B needs.
- **Static data serializes, but only part of it.** Upstream's `Load(Type)` and `Load(MethodInfo, byte[], Type[])` work
  through `XmlQueryStaticData.GetObjectData`: names, filters, prefix mappings, types, collations, whitespace rules
  and early-bound types become a byte array. Xsl3's additions are not in that byte array:
  - `OutputExtras`
  - `ResultDocuments`
  - `Xslt2`
  - `CompactTree`
  - `Dispatcher`
  - the 2.0+ constants
- **2.0+ constants live in a process-wide `ConstantPool`.** Generated IL loads them by integer id, as a double literal
  passed to `XsltLibrary.X31*` calls. The 24 call sites put these kinds in the pool:
  - `SequenceTypeTest` and `CastTarget`
  - `StaticInfo` (base URI, default collation, decimal formats)
  - `FunctionSignature`
  - `XsltNameContext`
  - `EvaluateContext`
  - `ResultFormat` and its named-format dictionaries
  - accumulator tables
  - error-name lists
  - string arrays
  - atomic values (literals)
  - static parameter values

  The ids are only meaningful inside the process that compiled the stylesheet.

## Decisions

1. **Sink A stays `DynamicMethod`.** A collectible `AssemblyBuilder` was tried for tiered PGO and gave no measurable
   gain (docs/performance.md). `DynamicMethod`s are already collected with their owner. Function items need no
   generated types: they are runtime objects plus a dispatcher function (docs/plan/p3-xslt30.md, decision 4). So
   SPEC §12.1's closure classes aren't needed.
2. **Sink B emits through `PersistedAssemblyBuilder`** (.NET 9+). It uses the same `XmlILModule` abstraction as the
   debug `AssemblyBuilder` path: one public type per stylesheet, holding the static methods, the static-data byte
   array and the early-bound types, which is the shape upstream's `Load(Type)` expects. The build task runs on the
   .NET 10 SDK even for .NET 8 targets.
3. **Constants move into the compiled unit.** Each compilation gets its own constant table, which becomes part of
   `XmlQueryStaticData`. Generated code reads it through the runtime (`XmlQueryRuntime.Constants[id]`) rather than
   the global pool:
   - Sink A keeps the objects as they are.
   - Sink B serializes the table with a versioned binary format covering the closed set of constant types above.
   - A unit test round-trips every constant type, and a W3C sample round-trips through a saved assembly (2.3).

   This change also stops the process-wide pool growing without bound, which matters for long-running hosts that
   compile many stylesheets. That pool growth is an audit finding in its own right.
4. **The rest of the static data is serialized too**: `OutputExtras`, `Xslt2`, `CompactTree`, `ResultDocuments`.
   The dispatcher is found by its method name in the loaded type.
5. **Nothing is compiled at start-up.** `XmlILModule` creates its module lazily, on the first compilation. A
   precompiled stylesheet never touches Reflection.Emit.
6. **Reflection becomes AOT-safe**:
   - `XPath31Compiler`'s method table becomes a source-generated or hand-written dictionary of `MethodInfo`s, with
     `DynamicDependency` attributes as a stopgap.
   - The bridges switch to `UnsafeAccessor` (as `XmlWriterSettingsInternals` already does) or get
     `DynamicallyAccessedMembers` annotations.
   - `MentionsPositionOrLast` walks QIL with the node API rather than reflection.
7. **Without Sink C, dynamic compilation fails clearly under AOT.** Loading a stylesheet from source, `xsl:evaluate`
   and use-when's run-time compilation raise a `PlatformNotSupportedException` that names the remedy (precompile
   with `Xsl3.Build`). Use-when is evaluated at build time, so a precompiled stylesheet never needs it.
   Whether 1.0 ships Sink C (the interpreter) is the owner's decision (remaining-scope "Decisions" 1).

## Milestones

### B1: AOT-safe runtime (done, 2026-10-21)

- [x] Lazy `XmlILModule` (decision 5).
- [x] Reflection fixes (decision 6): the bridges' type lookups are annotated with `DynamicallyAccessedMembers`, the
  `XsltLibrary` method table and `MentionsPositionOrLast` carry justified suppressions.
- [x] `RequiresDynamicCode` only on what really emits: the `Load` overloads that compile from source, and
  `XPath31Compiler.Compile`/`CompileHosted`. `xsl:evaluate` checks `RuntimeFeature.IsDynamicCodeSupported` and
  raises `PlatformNotSupportedException` without it. The probe app now publishes with no warnings from `Xsl3`; its
  own `Load` from source is the one warning left, as it should be.

### B2: unit-local constants (done, 2026-10-21)

- [x] Decision 3, Sink A first: each `Load` opens a `ConstantTable` that every unit compiled for the stylesheet
  shares (its use-when expressions and `xsl:evaluate` targets refer to its constants by number). The static data
  carries the table; `XsltLibrary` reads constants through the runtime. XPath compiled outside a stylesheet still
  uses the process-wide table. A test checks that loading stylesheets doesn't add to it.
- [x] The gates pass unchanged, and the pairs show no slowdown: identity 0.82, sort 0.70, muenchian 1.18,
  string-recursion 0.75, key-join 1.10 (main: 0.82, 0.71, 1.14, 0.76, 1.11).

### B3: Sink B

- [x] `PersistedAssemblyBuilder` behind `XmlILModule`: `XslCompiledTransform.CompileToAssembly` (.NET 9+) defines one
  public type, with `GeneratedCodeAttribute`, through upstream's `TypeBuilder` path of `XmlILGenerator`.
- [x] Static data and constants serialized: the static data format's minor version 1 adds `Xslt2`, `CompactTree`,
  `ResultDocuments`, `OutputExtras`, the constant table (`XPath31.ConstantSerializer`, every kind the compiler adds)
  and the dispatcher's method name, which `Load(MethodInfo, …)` binds. `Load(Type, XsltSettings)` supplies the run-time
  settings. With `XSL3_W3C_SAVED=1` every xslt30-test case compiles into a saved assembly, loads from it in its own
  load context, and runs: the results equal the from-source run (7,959 passed, 77 failed, 2026-10-21).
- [ ] `Xsl3.Build` MSBuild task (`<XsltStylesheet Include="…"/>`) and a sample.
- [ ] `XslCompiledTransform.Load(Type)` loads it with no compilation.
- [ ] Portable PDBs mapping IL to stylesheet lines (SPEC §21.3), so `GenerateHelper.MarkSequencePoint` comes back.

### B4: NativeAOT in CI

- [ ] A sample using `Xsl3.Build` is published with `PublishAot=true` and run in CI.
- [ ] A sampled W3C subset (about 10%) runs through the saved-assembly round trip.

Exit (remaining-scope 2): the sample runs under NativeAOT with no warnings; the round trip passes; `Xsl3.Build` is
ready to publish, with the owner's go-ahead.
