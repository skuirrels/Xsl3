# Remaining scope: everything after P5

This is the brief for whoever carries Xsl3 from here: what is left of the SPEC.md roadmap once Phase 5
(`xsl:evaluate` and serialization completion, merged in skuirrels/Xsl3#1) is done. It covers:

- the end of Phase 4 (performance and code generation);
- the public API and settings the spec promises;
- the conformance long tail;
- Phases 6 (schema-awareness) and 7 (streaming);
- the test and release infrastructure, and the road to 1.0.

Read SPEC.md first. Section numbers below (§n) are SPEC.md's.

Each workstream says what exists today, what is left, how to know it's done, and what it touches, so that
several sessions can work in parallel without editing the same files. Measured numbers are from
2026-10-20/21 (Release, .NET 10). Re-measure before quoting them.

## Where things stand

| Gate | Now | Target |
|---|---|---|
| Upstream `XslCompiledTransform` tests (1.0 regression gate) | 1661 / 1661, .NET 8 and 10 | 100%, always |
| Differential, byte-for-byte against stock on the 1.0 corpus | 394 / 394 | 100%, always |
| W3C QT3 (XPath 3.1, F&O 3.1, higher-order functions claimed) | 21,884 passed, 0 failed (100%) | ≥ 99% on claimed features (Hardening, met) |
| W3C xslt30-test, 3.0 profile | 8,844 passed, 23 failed, 42 not run (99.7%), `schema_aware` claimed | ≥ 99% on claimed features (Hardening, met) |
| W3C xslt30-test, 2.0 profile | 4,588 passed, 9 failed (99.8%) | ≥ 99% (met) |
| P5 sets (`evaluate`, `output`, `character-map`, `result-document`, `disable-output-escaping`) | 454 / 457 (99.3%) | ≥ 98% (met) |
| XSLT 1.0 throughput against stock | 1.38× geometric mean, never slower | ≥ 1.2× (met), target 1.5× |
| 3.0 rewrites on Xsl3 against the 1.0 originals on stock (time ratio) | identity 0.86, sort 0.71, muenchian 0.97, string-recursion 0.77, key-join 0.95 (medians of three runs, 2026-10-21) | ≤ 1.0 on every pair (§19.6, met) |
| Compact tree memory | 59% of `XPathDocument` | ≤ 75% (met) |
| NuGet | 1.0.0-rc.1 (tagged 2026-10-21; 0.8.0-alpha before it) | 1.0 after P4 (§22 release plan) |

The xslt30-test catalogue has about 14,600 test cases. The 3.0 profile runs about 8,000 of them. Most of the
rest depend on features Xsl3 doesn't claim yet:

| Feature | Tests that need it | Workstream |
|---|---:|---|
| `streaming` | 2,983 | 7 (P7) |
| `schema_aware` | 933 | 6 (claimed 2026-10-21 with `XsltSettings.SchemaAware`) |
| `backwards_compatibility` | 122 | 4 (claimed 2026-10-21) |
| `XML_1.1` | 65 | 4 (decide, probably declare unsupported) |
| `xsl-stylesheet-processing-instruction` | 18 | 4 (claimed 2026-10-21) |
| `built_in_derived_types` | 10 | 6 |
| `XSD_1.1` | 8 | 6 (decision) |
| `streaming-fallback` | 7 | 7 |
| `enable_assertions` (`xsl:assert`), `sweep_and_posture` | (treated as unsatisfied by the runner) | 4, 7 |

`tests/Xsl3.W3C/Xslt30/Profile.cs` decides what is claimed. Claim a feature only when its tests pass.

## How to work

### Setup

- .NET 8 and .NET 10 SDKs (see `global.json`).
- `eng/fetch-w3c.sh` fetches the W3C suites into `external/`.
- Python 3 is handy for editing scripts; the earlier phases used it.

### Gates before every push to main

1. `dotnet build Xsl3.slnx -c Release`
2. `dotnet test tests/Xsl3.Tests -c Release`: net8.0 and net10.0.
   - `CompactTreeTests.TakesLessMemoryThanXPathDocument` is flaky under parallel runs (`GC.GetTotalMemory`).
     Fixing it is item 8.4.
3. `dotnet test tests/Xsl3.Upstream.Tests -c Release --filter "category!=failing"`: all pass.
4. `XSL3_UPDATE_BASELINES=1 dotnet test tests/Xsl3.W3C -c Release`, then `git diff tests/Xsl3.W3C/baselines`.
   - The diff may only remove lines. A test that newly fails is a regression, even if the pass count rose.
   - Some tests "pass" by raising an unexpected exception where an error is expected; the reports count
     "expected errors passed without a matching code" (`XSL3_XSLT30_CODES=file` lists them). When a change makes such a test produce a result
     instead, it is a real regression. P4 found twelve that had been passing only because .NET's
     `XmlWellFormedWriter` threw.
5. `XSL3_XSLT30_PROFILE=2.0 dotnet test tests/Xsl3.W3C -c Release -f net10.0 --filter "FullyQualifiedName~Xslt30Run"`:
   the 2.0 profile must not drop.
6. `dotnet test tests/Xsl3.Differential -c Release`: 394 / 394 (about 20 minutes per framework).

When iterating, `XSL3_XSLT30_SETS=set1,set2` and `XSL3_QT3_SETS=...` run chosen sets; `eng/xslt30-sets.sh`
wraps the first.

### Conventions

- Commit as the repository owner's git identity. No attribution lines in commit messages or PR descriptions.
- Push to main at milestones, or work on a branch and open a PR when another session is active in the same
  files.
- Ask the owner before force pushes, history rewrites, releases, tags or NuGet publishing. Deployment
  approvals for the Release workflow are the owner's to give; never approve them through the API.
- Report measured numbers, honestly, with how they were measured. The bench machine is noisy: quote medians
  of three or more runs and the spread.
- Every change to code inherited from upstream carries an `// Xsl3:` comment saying why (docs/internals.md).
  Performance changes are marked `Xsl3 (P4)`.
- Keep README.md (gates table, What's outstanding, Roadmap), docs/conformance.md (history rows),
  docs/performance.md and docs/nuget/README.md current with every milestone.
- XSLT 1.0 output must stay byte-identical to stock. Anything that could change it must keep 1.0 on the
  existing path, or be gated by `_qil.Xslt2`, `XmlQueryRuntime.IsXslt2` or similar.

### Tools

- `tools/Xsl3.Dev`:
  - `run <xsl> <xml>` transforms.
  - `XSL3_DEV_TIME=n` times n runs and prints the median.
  - `qil` and `qilopt` dump the QIL before and after the optimizer.
- `bench/Xsl3.Benchmarks`:
  - `pairs [orders] [names]` compares the 3.0 rewrites with stock on the 1.0 originals (`XSL3_PAIRS_DUMP=dir`
    keeps the outputs).
  - `quick`, `quick30` and `corpus <orders> <file>` are quicker checks.
  - BenchmarkDotNet suites: `Xslt10Transform`, `Xslt10Compile`, `InputParse`, `Xslt10EndToEnd`, and
    `Xsl3Profile`/`Xsl3Profile30` for EventPipe CPU sampling to speedscope files.

## Workstreams

The numbering is for reference, not order; the last section suggests an order. Sizes are rough: S is days,
M is one to three weeks, L is a month or more, XL is several months (SPEC §22's person-month estimates).

### 1. Phase 4 close-out: 2.0/3.0 speed

**State.** Unboxed ("typed") code generation for the hot XPath functions, the direct result writer,
one-pass shallow-copy, string-keyed grouping, native number sort keys and map lookup tables are done.
docs/performance.md has the history and the reasons. Four of the five rewritten pairs are under 1×.

1.1 **key-join under 1× reliably** (S). It measures 0.94–1.07×.
   - Profile with `Xsl3Profile30` (pairs/key-join).
   - What's left is the dynamic call `$products(string(@sku))` (`X31IsMap`/`X31MapLookup`), the
     `$product/@name` path over an `item*` variable (`X31Nodes` + `DocOrderDistinct`), and
     `format-number`.
   - A variable bound to a single map, or a single node, could keep a typed Let instead of `item*`.
     That is the general fix and also helps every other stylesheet.

1.2 **Direct writer differences** (S). P5 left three `output` failures that come from the 2.0+ direct
   result path (P4: `XslCompiledTransform.DirectPrincipalSettings`, which writes through Xsl3's raw writer
   without `PrincipalResult`):
   - `<a />` instead of `<a/>` (output-0285, output-0311);
   - the xhtml default method (output-0130).

   Fix them in the direct path or its conditions. The byte-comparison test
   `DirectPrincipalResultMatchesThePrincipalResultWriter` must stay green.

1.3 **More 2.0/3.0 benchmarks** (M). The rewritten pairs are only the five 1.0 benchmarks. §19.5 asks for:
   - micro stylesheets per feature (grouping, sorting, keys, regex, string functions, maps, `fold-left`,
     `xsl:iterate`, identity, deep recursion), at 1 KB, 1 MB and 100 MB;
   - XMark at several sizes;
   - real 2.0/3.0 stylesheets (DocBook xslTNG, TEI, SchXslt, XSpec). Check each licence first, and fetch
     them by script rather than committing them.

   §19.6 says the 3.0 versions must never be slower than stock 1.0 on any pair. Add pairs as they are
   written.

1.4 **Template dispatch tables** (§19.2, M). The 3.0 processor still uses upstream's `MatcherBuilder`
   (priority-ordered conditionals).
   - Build per-mode tables keyed by node kind and name: frozen dictionaries, or a perfect hash under 64
     names.
   - Put the wildcard and predicate rules in a fallback list.
   - This matters for stylesheets with many rules (DocBook, TEI); the five pairs barely exercise it.
   - Never for legacy 1.0 mode unless the differential tests stay byte-identical.

1.5 **Automatic indexing** (§19.3, M). Rewrite `E[K = V]` inside loops, where E is loop-invariant and rooted
   at a document, to a lazily built hash index. Guard it with a size threshold and an option. Add a pair or
   micro benchmark that shows the O(n²) → O(n) change.

1.6 **2.0/3.0 compile time and cold start** (S to M). Only 1.0 compile time is measured (`Xslt10Compile`,
   within noise of stock). Add 2.0/3.0 compile and first-transform benchmarks; §19.6 targets them against
   SaxonCS. The V2 loader and generator have never been profiled. The QIL optimizer's
   `inLoopCountsMany` change and the typed paths may have moved compile time.

1.7 **Performance and allocation gates in CI** (§19.1 item 2, §19.5; M).
   - A micro suite on every PR that fails on more than 5% slowdown or more than 5% more allocation.
   - The full corpus nightly.
   - GitHub-hosted runners are noisy, so this needs either a self-hosted runner or relative measurements
     (stock against Xsl3 in the same job).

1.8 **Other §19.4 items, as profiles justify them**:
   - `long` fast paths for `xs:integer` arithmetic. `IntegerValue` (`src/Xsl3/Xdm/AtomicValue.cs`) always
     holds a `BigInteger`, and its comment defers the fast path to P4;
   - `StringBuilder` pooling;
   - `IgnoreComments`/`IgnoreProcessingInstructions` when the stylesheet can't see them;
   - a per-document name index for `//name` (§14.1). There is none; descendant scans are range scans over
     expanded-name ids;
   - the name pool is per `XmlNameTable` (`src/Xsl3/Tree/NamePool.cs`), not per executable as §14.2 says.
     Only change it if profiles show re-mapping costs;
   - opt-in parallelism (`xsl3:threads`, parallel `xsl:fork`): open question 5 in §25.

**Touches.** `src/Xsl3/XPath31/XPath31Compiler.cs`, `src/Xsl3/Xsl/Xslt/QilGenerator.V2.cs`,
`src/Xsl3/Xsl/Runtime/XsltLibrary.*.cs`, `src/Xsl3/Xsl/IlGen/*`, `bench/`.

**Done when.** Every rewritten pair (the five, plus any added in 1.3) is ≤ 1.0× as a median of three runs,
and the gates in 1.7 run in CI.

### 2. Code generation sinks: Sink A, Sink B (`Xsl3.Build`), NativeAOT, Sink C

**State.**
- IL is still emitted as upstream's `DynamicMethod`s, in one shared module (`src/Xsl3/Xsl/IlGen/XmlILModule.cs`,
  marked `[RequiresDynamicCode]`; `XmlIlGenerator.cs` picks it in release builds). Debug builds use an
  `AssemblyBuilder` with `AssemblyBuilderAccess.Run` (not collectible) holding one type of static methods.
- A collectible assembly for tiered PGO was tried and dropped: it gave no measurable gain (docs/performance.md).
- Debug line mapping is a stub: the body of `GenerateHelper.MarkSequencePoint` is commented out, and
  `XsltSettings.IncludeDebugInformation` is internal.
- Function items, closures and maps work without new types: they are runtime-library objects and a compiled
  dispatcher function (docs/plan/p3-xslt30.md, decision 4).
- 2.0+ code reads its constants (cast targets, sequence types, collations, function definitions) from a
  process-wide `ConstantPool` by integer id, filled at compile time.
- There is no `PersistedAssemblyBuilder` use, no `Xsl3.Build` project and no interpreter.
- `Load(Type)` and `Load(MethodInfo, byte[], Type[])` exist from upstream.

2.1 **Plan first** (S). Write `docs/plan/p4-sink-b.md`. A proposal for this already exists as a session card
   ("Plan Sink B (Xsl3.Build) and NativeAOT"). The plan must answer:
   - How the `ConstantPool` and `XmlQueryStaticData` (`OutputExtras`, `ResultDocuments`, `Xslt2`,
     `CompactTree`, `Dispatcher`) survive being saved and loaded in another process. The pool's ids are
     process-local today.
   - Whether to keep `DynamicMethod` for the JIT path (Sink A as SPEC §12.1 describes it may no longer be
     needed, given decision 4) or move to a collectible `AssemblyBuilder`. Measure compile time and memory
     either way (§23 risk 4).
   - Every AOT and trim blocker. Publish a small app with `PublishAot=true` and list the warnings:
     `src/Xsl3/Bridges` uses `UnsafeAccessor` into System.Private.Xml, and there is reflection in the
     runtime library and the early-bound call machinery.

2.2 **Sink B: `Xsl3.Build`** (§12.2, L).
   - Emit to `PersistedAssemblyBuilder` (.NET 9+). For .NET 8 targets, the build task runs on the .NET 10 SDK.
   - Ship an MSBuild task for `<XsltStylesheet Include="…"/>` items.
   - At run time, `XslCompiledTransform.Load(typeof(...))` (and later `XsltExecutable.FromCompiledType`) loads
     the result with zero compile cost.
   - Package it as `Xsl3.Build`, a development dependency (§6.3).
   - Portable PDBs mapping IL to stylesheet lines (§21.3) belong here too.

2.3 **NativeAOT** (G7, L). Precompiled stylesheets run under NativeAOT with no `RequiresDynamicCode` or trim
   warnings. Add a CI job that publishes and runs an AOT sample, and a sampled W3C subset (about 10%, §20)
   through a compiled-assembly round trip.

2.4 **Sink C: QIL interpreter** (§12.3, XL, optional for 1.0). It is needed for:
   - `xsl:evaluate` under NativeAOT (P5 compiles `xsl:evaluate` through the JIT path);
   - runtime-compiled stylesheets under AOT;
   - `Backend = Interpreter` for one-shot transforms;
   - tiered execution, which is optional.

   It must pass the same W3C suites. Without it, `xsl:evaluate` and `Load(string)` must raise a clear error
   under AOT. *Decided (owner, 2026-10-22): 1.0 ships without it; the interpreter is a 1.x to-do (see "After 1.0").*

**Touches.** `src/Xsl3/Xsl/IlGen/XmlILModule.cs`, `XmlIlGenerator.cs`, `src/Xsl3/Xsl/Runtime/ConstantPool.cs`,
`XmlQueryStaticData.cs`, `src/Xsl3/Xslt/XslCompiledTransform.cs`, new `src/Xsl3.Build/`. Mostly separate from
workstream 1's files, except the IL generator.

**Done when.** A sample app using `Xsl3.Build` runs under NativeAOT with no warnings; the sampled W3C
round trip passes; the `Xsl3.Build` package is ready to publish (with the owner's go-ahead).

### 3. The SaxonCS comparison (G5, §19.5–19.6)

**State.** Not started. G5 asks for 2.0/3.0 to be faster than SaxonCS-HE on every benchmark, with a median
of 2× or more, and cold start 2× or more (10× with Sink B).

3.1 **Licence check first** (S). Confirm what licence the SaxonCS edition used carries, and whether it
   allows benchmarking and publishing results. Saxonica's .NET product has had commercial editions; check the
   current terms, record the version, and get the owner's agreement before installing anything.
3.2 **Harness** (M). Add a comparison engine to `bench/Xsl3.Benchmarks`, behind a build flag so that the
   default build doesn't need Saxon. Measure compile, first transform, steady state, allocation and peak
   working set, separately (§19.5).
3.3 **Report** (S). Write a docs/performance.md section with the pinned Saxon version, and keep it current
   per release (§23 risk 6).

Depends on 1.3 (the benchmarks) and, for cold start, on 2.2.

### 4. Public API, settings and limits

**State.** The public API is still `XslCompiledTransform`, `XsltArgumentList`, `XsltSettings` and
`XsltException`, plus:
- `XslCompiledTransform.UseCompactTree`;
- `XsltSettings.EnableEvaluate` (P5).

The test harness and `tools/Xsl3.Dev` reach the rest through `InternalsVisibleTo`: `InitialTemplate`,
`InitialMode`, `Packages`, `StaticParameters`, `XmlQueryRuntime.ResultDocumentHandler`, the base output URI.
None of the §7.2 API exists: `XsltCompiler`, `XsltExecutable`, `Xslt30Transformer`, `XdmValue` and friends,
the destinations. The only public XDM type is `Xsl3.Xdm.QName`.

Some of the machinery exists internally, as thread-statics on `XmlQueryRuntime` that only the W3C runner sets:
- **Cancellation:** `XmlQueryRuntime.Cancellation`, checked at function entry and mapped to
  `Q{http://xsl3/err}cancelled`.
- **Secondary results:** `XmlQueryRuntime.ResultDocumentHandler`. Without a handler, `xsl:result-document`
  with an `href` raises XTDE1480 ("Secondary results are not enabled"), which is secure by default but can't
  be turned on publicly.
- **Base output URI:** `XmlQueryRuntime.BaseOutputUri`.
- **Recursion guard:** at every function entry, `XmlQueryRuntime.EnterFunction` calls
  `RuntimeHelpers.EnsureSufficientExecutionStack`, and `XmlILCommand.Execute` maps the exception to
  `Q{http://xsl3/err}recursion-limit`. It is a stack probe, not a depth limit.

4.1 **Design the 3.0 API** (M). Write `docs/plan/api.md`. Start from §7.2, but check it against what the
   engine does today:
   - one `XmlILCommand` per load;
   - thread-safety of `XslCompiledTransform` after `Load`;
   - how 2.0+ results are delivered: `XmlILCommand.Evaluate` already returns raw items.

   Decide what is in 1.0. At least:
   - initial template, mode, function, and match selection (including none, so XTDE0044 can be raised);
   - stylesheet and static parameters as XDM values;
   - a package library;
   - result-document and message handlers;
   - a base output URI;
   - raw result delivery;
   - `XdmNode.Load` building a compact tree.

4.2 **Implement it** (L), with unit tests and the W3C runner moved onto the public API where it can be.
   The runner exercising the public API is the best test of it.

4.3 **`AddParam` CLR mappings** (§7.1, S to M).
   - Today every stylesheet, 2.0+ included, gets upstream's conversion (`XsltConvert.InferXsltType`,
     `XmlQueryRuntime.ChangeTypeXsltResult`):
     - every number type becomes `xs:double`, so an `int` parameter isn't an `xs:integer`;
     - `DateTime` becomes a string;
     - `BigInteger`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Uri`, `byte[]`, dictionaries and
       general `IEnumerable` are rejected.
   - For 2.0+ stylesheets, map them as §7.1 says: `long`/`int`/`BigInteger` to `xs:integer`, `decimal`,
     `float`, `DateTime`/`DateTimeOffset` to `xs:dateTime`, `DateOnly`, `TimeOnly`, `TimeSpan` to
     `xs:dayTimeDuration`, `Uri`, `byte[]`, `IReadOnlyDictionary<,>` to maps, `IEnumerable` to sequences, and
     `XdmValue` as is.
   - 1.0 stylesheets keep stock's conversions exactly.
   - The W3C runner converts every parameter to double, boolean, string or nodes before `AddParam`; switch it
     to typed values once they work.

4.4 **Settings, secure by default** (§21.1, M). Only `EnableDocumentFunction`, `EnableScript` and (since P5)
   `EnableEvaluate` exist. Add:
   - `EnableResultDocument`. It is off by default, so check what the Stream and URI overloads do with
     `xsl:result-document` today, and the default handler writing only under `BaseOutputUri`.
   - `EnableEnvironmentVariables`. Today `fn:environment-variable` always returns the empty sequence and
     `available-environment-variables` returns nothing (`MiscFunctions.cs`), which is safe but can't be enabled.
   - `EnableExtensionFunctions`. Extension objects are always honoured today, as upstream.
   - `MaxRecursionDepth`, a real depth counter next to the existing stack probe (§21.2).
   - A public `CancellationToken`, over the internal one; then `Timeout` and `MaxOutputBytes`.
   - `UseDedicatedThread`, a large-stack thread (§21.2).

   Each needs a test and a docs/divergences.md entry when it changes stock behaviour (for 1.0, defaults
   must equal stock).
4.5 **Error model** (§16.7, M):
   - `XsltDynamicException` with the error QName, value and a stylesheet stack trace (template and
     function frames with locations);
   - static errors collected and all reported, each with module, line and column;
   - .NET exceptions from extension functions wrapped as `Q{http://xsl3/err}extension-error` and catchable;
   - `OperationCanceledException` and `OutOfMemoryException` never caught.

4.6 **Missing XSLT 3.0 pieces** (README "What's outstanding" and the unclaimed features above). Each is S
   to M:
   - `xsl:assert` (`enable_assertions`, behind an option);
   - initial functions;
   - the `backwards_compatibility` feature: 122 tests. The compatibility feature is required (§4.2), and
     SPEC §4.3 describes `XsltVersionMode.BackwardsCompatible`;
   - `inherit-namespaces="no"`;
   - unparsed entities of temporary trees;
   - memoized functions (`new-each-time="no"`, `cache`);
   - the `xsl-stylesheet-processing-instruction` feature;
   - XML 1.1 input: .NET's `XmlReader` doesn't support it. *Decided (owner, 2026-10-22): unsupported for now
     (docs/divergences.md).*

4.7 **Diagnostics** (§21.3, M):
   - debug mode with PDBs (shared with 2.2);
   - an optional `ITraceListener` for template enter and exit, compiled in only when asked for;
   - an `fn:trace` handler: today `fn:trace` returns its value and drops the label;
   - `xsl:message` already reaches `XsltArgumentList.XsltMessageEncountered`, as upstream; the 3.0 API
     needs its own handler, with `terminate` and the error code;
   - an `xsl3 explain` CLI that dumps optimized QIL and dispatch tables. `tools/Xsl3.Dev qilopt` is the
     seed of it, and §21.3 also implies a packaged CLI.

**Touches.** `src/Xsl3/Xslt/*`, new public types, `src/Xsl3/Xsl/XmlILCommand.cs`, `XmlQueryRuntime.cs`, the
W3C runner, `tools/`. 4.6 touches the loader and `QilGenerator.V2.cs`; coordinate it with workstream 1.

**Done when.** The 1.0 API surface is decided in docs/plan/api.md and implemented. The W3C runner uses no
internal entry points the public API could serve. The settings and limits exist, with tests, and the README's
"API" outstanding line is empty.

### 5. Conformance long tail (Hardening, claimed features)

**State.** The 3.0 profile is at 99.0% and QT3 at 99.7% (2026-10-21), meeting the Hardening exit (§22) of ≥ 99% on
claimed features. `tests/Xsl3.W3C/baselines/xslt30-run.txt` and `qt3-run.txt` list every failing test; re-count them
after P5.

5.1 **xslt30-test.** Before P5, the failures by set were:

   | Failures | Sets |
   |---:|---|
   | 30 | regex-classes |
   | 20 | error |
   | 19 | copy |
   | 12 | mode |
   | 9 each | base-uri, id, package |
   | 7 each | accumulator, function, math, namespace, next-match, transform, try |
   | 6 each | analyze-string, context-item, higher-order-functions, param, stream-available |

   P5 cleared `output`, `result-document`, `character-map` and `disable-output-escaping`.

   Work set by set, highest count first. Give each fix a unit test when the cause is general. Two known
   decisions:
   - regex-classes follow .NET's Unicode version rather than the suite's. Either map the Unicode 9 classes,
     or document the divergence and exclude the tests with a reason.
   - `fn:transform` (7 failures) is unimplemented.
5.2 **QT3.** The 138 failures: `fn-random-number-generator` 41, `fn-function-lookup` 19, `fn-idref` 14, the
   JSON functions about 20 (`parse-json` 8, `json-to-xml` 6, `xml-to-json` 3, `json-doc` 3),
   `op-same-key` 6, `prod-NamedFunctionRef` 6, `fn-serialize` 5, and a few in sorting and maps.
5.3 **Known gaps** listed in the README:
   - `xsl:next-match` between alternatives of one union pattern;
   - dynamic errors in non-predicate patterns;
   - XTDE1480 where it can only be seen dynamically (P5 covered templates in temporary output state);
   - static variables declared in modules imported after their use;
   - `doc()`/`document()` refusing DTDs;
   - `xsl:evaluate` re-parses documents it is given, so their node identity differs (P5's known gap).
5.4 **Errors without a matching code.** About 1,500 expected-error tests pass with a different error code
   or an exception. The spec requires the right code; work towards it set by set. The runner reports the
   count, and could report the list.
5.5 **docs/divergences.md, "From the W3C specifications"**, still says no 2.0/3.0 features are implemented.
   Write the real list: every intentional difference, including implementation-defined choices
   (`xsl:use-package` picks the highest version, `on-multiple-match` recovery and the like, which
   `Profile.cs` encodes).

**Touches.** All over the compiler and runtime: the main source of conflicts with workstream 1. Split by
area, and rebase often.

**Done when.** The 3.0 profile, the 2.0 profile and QT3 are each ≥ 99% on claimed features, and
docs/divergences.md is complete.

### 6. Phase 6: schema-awareness (§18; 3–5 person-months)

**State.** In progress since 2026-10-21 (owner's choice; docs/plan/p6-schema.md). `XsltSettings.SchemaAware` turns it
on; off, nothing changes (`xsl:import-schema` is XTSE1650). With it, `XSL3_XSLT30_PROFILE=sa` measures the tests that
need `schema_aware`: 827 passed, 17 failed (98.0%), from 79 passed and 697 not run. Done: `xsl:import-schema`
(location, inline, by namespace, built-in xml and fn schemas, XSD 1.1 conditional inclusion); schema atomic, list and
union types in sequence types, casts and constructor functions; `schema-element()`, `schema-attribute()`,
`element(N, T)` and `attribute(N, T)` in expressions and patterns; type annotations in the compact tree from a
validating reader, typed values, schema-normalized values; validation of constructed nodes (`validation`, `type` on
the constructors, `xsl:copy-of`, `xsl:document`, `xsl:result-document`, `xsl:source-document`), default attributes,
XTTE1510/1540/1550/1555/0950; `validation="preserve"` on `xsl:copy-of`; temporary trees keep their children's
annotations; `xsl:mode typed` (XTTE3100, XTTE3110); `input-type-annotations="strip"`; `default-validation="preserve"`;
json-to-xml `validate`; `nilled()`, typed IDs and IDREFs (`id()`, `element-with-id()`, `idref()`); typed grouping,
comparison and `xsl:value-of`; `xsl:evaluate schema-aware`; schema locations as hints; `[xsl:]default-validation` on
any element; the is-id property kept when annotations are stripped; the runner asserts on the result tree the engine
builds. `XsltSettings.SchemaAware` stays off by default (owner's decision, 2026-10-21). Open: schemas per package (one
compiled set serves all the packages of a compilation; no test depends on it, so it is left for 1.x), and the
remaining 2 failures, validation-0201 and -0202, whose expected XHTML is Saxon's serialization (no attributes the
XHTML schema defaults, no indentation before inline elements). Union types of the same members are taken as
identical across packages (override-v-005, override-f-031).

6.1 **Plan** (S): `docs/plan/p6-schema.md`.
   - Decide XSD 1.1 (§18 options a/b/c; open question 3). XSD 1.0 only is conformant. *Decided (owner,
     2026-10-22): option c, XSD 1.0 only for 1.0; XSD 1.1 is a 1.x to-do (see "After 1.0").*
   - Decide the package split: §6.3 has `Xsl3.Schema` as a separate package, so non-schema-aware users pay
     nothing.
6.2 **`xsl:import-schema`** into an `XmlSchemaSet`. Schema types in sequence types and patterns:
   `schema-element()`, `schema-attribute()`, `element(*, T)`, `attribute(*, T)`, `match="element(*, T)"`.
6.3 **Validation** of input and output: `validation` and `type` on constructors, `xsl:result-document`,
   `xsl:copy-of`, `xsl:copy`, `xsl:document`. Use `XmlSchemaValidator` while building the tree, and add a
   `typeAnnotation` column to the compact tree (`src/Xsl3/Tree`; §14.1 reserves it).
6.4 **Typed values** from annotations, including list and union types. The atomic type system
   (`src/Xsl3/Xdm`) already has every built-in type. The `built_in_derived_types` feature (10 tests) fits
   here.
6.5 **Zero cost without `xsl:import-schema`** (§18): check with the benchmarks that non-schema-aware
   stylesheets don't slow down.

**Done when.** The schema-aware test sets are ≥ 95% with XSD 1.0, and the feature is claimed in `Profile.cs`.

### 7. Phase 7: streaming (§17; 6–12 person-months)

**State.** Not started. 2,983 xslt30-test cases need `streaming`, and the runner marks posture-and-sweep
assertions "not run (P7)". `xsl:source-document`, `xsl:merge`, accumulators and `xsl:fork` work unstreamed; `streamable="yes"` is
validated and otherwise ignored.

7.1 **Plan** (M): `docs/plan/p7-streaming.md`.
7.2 **Streamability analysis**, exactly as XSLT 3.0 §19 defines it: posture, sweep, the general rules and
   the rules for every construct. Run it on the XSL and XPath ASTs, attach the results to QIL, and raise
   XTSE3430 for non-streamable constructs in streamable contexts.
7.3 **Execution**:
   - a push-based event pipeline over `XmlReader`, with streamed-node proxies;
   - grounded subtrees (`copy-of()`, `snapshot()`) built into small compact trees;
   - streamable modes, `xsl:source-document streamable="yes"`, streamable `xsl:merge`, streamed
     accumulators, `xsl:fork` with consuming branches (fan-out), and streamable functions and
     `xsl:iterate` over streamed input.
7.4 **Constant-memory test** in CI: a 2 GB generated document under a 200 MB limit.
7.5 **Packaging**: `Xsl3.Streaming` (§6.3).

**Done when.** The streaming test sets are ≥ 90%, the constant-memory test passes, and the feature is claimed.

### 8. Test and project infrastructure

8.1 **Fuzzing** (§20, M): SharpFuzz on the XPath 3.1 parser, the stylesheet loader and the JSON parser.
   Inputs must never crash the process. Run it in CI on a schedule.
8.2 **Property tests** (§20, M): atomic type round trips, the cast tables, comparison laws, map and array
   persistence, collation consistency. There are ad-hoc randomized tests (`DecimalFormatterTests`), but no
   property-testing framework (FsCheck or CsCheck).
8.3 **Differential testing against Saxon** (§20, M, after 3.1's licence check): generated XPath 3.1
   expressions and real stylesheets. Triage differences against the spec; Saxon isn't the oracle.
8.4 **The flaky memory test**: `CompactTreeTests.TakesLessMemoryThanXPathDocument` uses
   `GC.GetTotalMemory` and fails under parallel test runs. Isolate it (a collection with parallelization
   off, or measure allocated bytes for the current thread).
8.5 **docs/conformance.md generated by CI** (§20), with per-feature and per-set counts. Today it is updated
   by hand from the reports in `tests/Xsl3.W3C/bin/.../w3c-reports`.
   - CI (`ci.yml`) already runs the W3C suites against the baselines on Ubuntu, and the unit, upstream and
     differential tests on Ubuntu and Windows.
   - It has no benchmark or allocation step (1.7).
8.6 **CI flakes**: the differential test `XsltApiV2/XsltSettings14.xsl` loads `http://IDontExist/...`, and
   its DNS failure message differs between stock and Xsl3 on Linux runners (seen once on 2026-10-20).
   Normalise the message or allowlist it.
8.7 **Upstream sync**: `.github/workflows/upstream-sync.yml` diffs upstream against the fork point
   (docs/internals.md). Check that it still runs and opens issues, and port anything it finds.

### 9. Release 1.0

The §22 release plan puts 1.0 after P4 (a basic 3.0 processor, fast), with schema-awareness and streaming
in 1.x minor releases.

9.1 **Decide what 1.0 means** with the owner. Decided (owner, 2026-10-22), with SPEC §25 updated:
   - 4: .NET 8 and .NET 10, as now; `CompileToAssembly` (Sink B) stays .NET 10-only;
   - 5: no parallelism in 1.0;
   - 6: MIT, as upstream, with no further contribution terms;
   - streaming (P7) after 1.0, in a 1.x release; no SaxonCS comparison for 1.0 (the W3C suites stay the oracle);
   - the error-code long tail stops at 32 (5.4);
   - no interpreter (Sink C) in 1.0: `xsl:evaluate`, `fn:transform` and loading from source keep raising a clear
     error under NativeAOT; the interpreter is a 1.x to-do;
   - the 3.0 API stays in the `Xsl3` namespace, and `OptimisationLevel` and `CodeGenBackend` (SPEC §7.2) are left
     out of 1.0;
   - XSD 1.0 only (§18 option c); XSD 1.1 is a 1.x to-do;
   - XML 1.1 input is unsupported for now.
9.2 **API freeze**: run a public API analyzer (`Microsoft.CodeAnalysis.PublicApiAnalyzers`) from the first
   release candidate. Done for 1.0.0-rc.1 (owner's decision to cut it, 2026-10-21): `src/Xsl3/PublicAPI.Shipped.txt`
   records 955 members, and `PublicAPI/net10.0` the .NET 10-only `CompileToAssembly`.
9.3 **Docs for users**: a getting-started page for the 3.0 API, migration notes from `System.Xml.Xsl`, the
   divergences list, and the security defaults.
9.4 **Release mechanics** are in place: a tag `v*` triggers `.github/workflows/release.yml`, which publishes
   to nuget.org by trusted publishing after the owner approves the `nuget` environment deployment. Releases
   so far are 0.1.0-alpha to 0.7.0-alpha. Only the owner approves deployments.

## Suggested order and parallel tracks

These tracks touch mostly separate files, so they can run in parallel sessions:

| Track | Workstreams | Main files | Conflicts with |
|---|---|---|---|
| A: speed | 1 | XPath31Compiler, QilGenerator.V2, XsltLibrary, IlGen | C |
| B: build and AOT | 2.1–2.3, then 3 | XmlILModule, XmlIlGenerator, ConstantPool, StaticData, new `src/Xsl3.Build` | A (IL generator only) |
| C: conformance | 5, 4.6 | across the compiler and runtime | A |
| D: API and settings | 4.1–4.5, 4.7 | `src/Xsl3/Xslt`, new public types, XmlILCommand, runner | B (XslCompiledTransform.Load) |
| E: infrastructure | 8 | tests, eng, .github | none |
| F: schema (P6) | 6 | new `Xsl3.Schema`, Tree, Xdm, loader | C |
| G: streaming (P7) | 7 | new `Xsl3.Streaming`, a new AST analysis, runtime | C, F |

A sensible sequence:
1. A and B in parallel, with E alongside.
2. Then D and C.
3. Then 1.0.
4. Then F, then G (G is the largest single piece, and builds on everything else).

## Decisions the owner needs to make

1. Whether 1.0 needs Sink C (an interpreter), or ships with `xsl:evaluate` unavailable under NativeAOT. *Decided
   (owner, 2026-10-22): 1.0 ships without it; a 1.x to-do.*
2. Which SaxonCS edition to compare against, given its licence (3.1). *Deferred (owner, 2026-10-22): no SaxonCS work for 1.0.*
3. The shape of the public 3.0 API (4.1): follow §7.2 as written, or a smaller first version. *Decided (owner,
   2026-10-22): the `Xsl3` namespace, as shipped in the release candidates; no backend settings in 1.0.*
4. XSD 1.1 (6.1): options a, b or c. *Decided (owner, 2026-10-22): c, XSD 1.0 only; XSD 1.1 a 1.x to-do.*
5. XML 1.1 input: declare it unsupported? *Decided (owner, 2026-10-22): unsupported for now.*
6. §25 questions 4–6: framework floor, parallelism in 1.0, licence and contribution terms. *Decided (owner, 2026-10-22): .NET 8 and .NET 10, no parallelism in 1.0, MIT (9.1).*

## After 1.0 (1.x to-do)

Owner's decisions of 2026-10-22 leave these for 1.x releases:

- [ ] **Sink C: QIL interpreter** (2.4). `xsl:evaluate`, `fn:transform` and stylesheets loaded from source under
  NativeAOT, then `CodeGenBackend` (`Auto | Jit | Interpreter`) and `OptimisationLevel` in `XsltCompilerOptions`
  (SPEC §7.2). Must pass the same W3C suites.
- [ ] **XSD 1.1** (6.1, SPEC §18). Choose between option a (the 1.1 additions on System.Xml's 1.0 validator) and
  option b (an external validator); 8 `XSD_1.1` tests are unclaimed until then.
- [ ] **Streaming** (P7, section 7).
- [ ] **Parallelism** (`xsl3:threads`, SPEC §25 question 5).
- [ ] **Schemas per package** (section 6).

## Review and progress (2026-10-21)

### Corrections to this brief

- **Upstream tests.** The suite has 1661 tests. The gate filter `category!=failing` runs 1654, and all of them pass.
  The 7 left out are platform-specific or `ActiveIssue` tests; unfiltered, 1657 pass and the 4 that fail are among
  those 7. The table at the top means 1654 / 1654.
- **Differential tests** take about a minute per framework on the development machine, not 20 minutes.
- **`regex-classes`.** The set declares `unicode-version 6.0`, and the runner used to claim every unknown dependency.
  Xsl3's regular expressions use .NET's Unicode tables, which are newer, so the profile now says so and the 30 tests
  don't apply. That is the documented-divergence option in 5.1.
- **8.4** wasn't only parallel noise. In the test host the compact tree measured 85% of `XPathDocument`'s memory,
  because other tests' name pools are freed a collection late. In a clean process it is 45%.

### Status of the items

| Item | State |
|---|---|
| 1.1 key-join under 1× | Done, with muenchian: every pair is under 1× in three runs in a row (key-join 0.95–0.98, muenchian 0.95–0.98): scaled rounding in `format-number`, maps built in one go keep a hash table, string-keyed map lookups, string group-by keys. Left in workstream 1: more benchmarks (1.3), dispatch tables (1.4), automatic indexing (1.5), compile-time benchmarks (1.6), perf gates in CI (1.7) |
| 1.2 Direct writer differences | output-0130 fixed: a 2.0+ stylesheet with an XHTML `html` literal result element and no method takes the principal writer. `<a />` is recorded as implementation-defined (docs/divergences.md). |
| 2.1 Plan Sink B | Done: docs/plan/p4-sink-b.md, with the NativeAOT warnings measured |
| 2.2–2.3 Sink B, NativeAOT | B1 done: no NativeAOT warnings from Xsl3 (lazy IL module, annotated bridges, `RequiresDynamicCode` only on loading from source and on XPath compilation, `xsl:evaluate` refusing clearly without dynamic code). B2 done: constants belong to the stylesheet (`ConstantTable`), not the process. B3 done: `XslCompiledTransform.CompileToAssembly` (`PersistedAssemblyBuilder`, .NET 9+) with the static data and constants serialized, `Load(Type, XsltSettings)`, embedded portable PDBs, `Xsl3.Build` (MSBuild targets running `xsl3 compile`). B4 done: `samples/Xsl3.AotSample` published with NativeAOT (no warnings) and run in CI; one xslt30-test case in ten round-trips through a saved assembly in CI (all of them locally, unchanged). Publishing `Xsl3.Build` and `Xsl3.Cli` is the owner's call |
| 4.3 `AddParam` CLR mappings | Done for 2.0+ stylesheets |
| 4.1 Design the 3.0 API | Done: docs/plan/api.md (one compiled unit per entry point, per-run base output URI, initial template and mode parameters, registered extension functions); two questions for the owner there |
| 4.2 Implement the 3.0 API | Steps 1–5 done: public XDM values (`XdmValue`, `XdmNode`, `XdmAtomicValue`, `XdmMap`, `XdmArray`, `XdmFunctionItem`), `XsltCompiler` (settings, static parameters, package library), `XsltExecutable` (one unit per entry point, compiled on first use), `Xslt30Transformer` (apply-templates to any selection in any mode, call-template, call-function, raw or serialized, initial template and mode parameters, per-run base output URI and handlers), `XsltDynamicException` and `XsltStaticException` (4.5), registered extension functions (`XsltCompiler.RegisterExtensionFunction`), and the W3C runner on the API: xslt30-test runs through `XsltCompiler` and `Xslt30Transformer` (`XSL3_W3C_API=0` keeps the internal entry points, which the saved-assembly round trip uses) | The types follow the plan's default namespaces (`Xsl3`, and `Xsl3.Xdm` beside `QName`); the namespace question in docs/plan/api.md is still the owner's |
| 4.4 Settings | Done: `EnableResultDocument` (with `BaseOutputUri`), `EnableEnvironmentVariables`, `EnableAssertions`, `EnableExtensionFunctions`, `Timeout`, `MaxRecursionDepth` (a depth counter compiled in when set; tail calls don't count), `MaxOutputBytes`, `UseDedicatedThread`, `XsltArgumentList.CancellationToken` |
| 4.5 Error model | Extension function exceptions are `Q{http://xsl3/err}extension-error` (catchable). The 3.0 API raises `XsltDynamicException` (code, description, error object, and the stylesheet stack of templates and functions with their locations) and `XsltStaticException` (every static error with module, line and column). The stack is complete with `EnableTracing`; without it, it is read from the .NET stack by the names of the methods templates and functions compile to (a table in the static data, saved assemblies included), so inlined and tail-called frames are missing. Done |
| 1.3 More benchmarks | The XSLT 3.0 micro suite (`bench/corpus/xslt30/micro`, `micro` mode): grouping, sorting, keys, join, regex, strings, maps, fold-left, iterate, identity, dispatch, recursion, json, at about 1 KB, 1 MB and 100 MB (docs/performance.md). Real stylesheets: the TEI Stylesheets (licence checked: BSD-2 or CC BY-SA; fetched by `eng/fetch-bench.sh`) reproduce their test suite's expected HTML, LaTeX and text output (`TeiStylesheetTests`, in CI), and the `tei` mode times four conversions nightly. Left: XMark (a C generator to fetch and build), DocBook xslTNG and XSpec (MIT; large, with their own drivers) |
| 1.6 Compile time and cold start | Compile time and allocation per micro stylesheet, and the cold start, are rows of the suite. Profiling them found the compile-time reflection scan for `position()`/`last()` following module back-references (fixed: 9% less allocation). Left: a comparison with SaxonCS (owner) |
| 1.7 Perf gates in CI | `eng/perf-gate.sh` and CI's `perf` job: the micro suite on the branch against the base, alternating in one job; fails on more than 5% slower (geometric mean) or 5% more allocation in a benchmark. `perf-nightly.yml` runs all sizes and the pairs nightly |
| 1.4 Template dispatch tables | Measured, not built: 200 extra fixed-name rules cost 0.30 ms per 1 MB transform (about 17 ns per element; `dispatch` micro benchmark). A table would need a runtime name index and integer tests, since QIL's `Choice` is push-mode only. Left until a real stylesheet with hundreds of rules shows the cost |
| 1.5 Automatic indexing | Done: `/path[key = $value]` with a focus-free value is looked up in an index of the document from its second evaluation (join micro benchmark 288 ms to 2.3 ms). Guarded by its applicability (codepoint collation, no 1.0 mode, string or node values each time) and the second-use threshold; there is no option to turn it off |
| 1.8 Profile-led items | Recursion over sequences: focus-free predicates index directly, converted sequences pass on unchecked (recursion micro 101 ms to 1.15 ms) |
| 8.1 Fuzzing | `tests/Xsl3.Tests/RobustnessTests.cs`: seeded mutations of XPath expressions, JSON texts and a stylesheet must end in Xsl3's own errors (static, dynamic, XML), never another exception. 1,500 mutations each in the unit suite; `XSL3_FUZZ_MUTATIONS` and `XSL3_FUZZ_SEED` run longer campaigns. Fixed: a missing required expression attribute reached the XPath parser as null (now XPST0003). Four campaigns of 10,000 mutations (seeds 0, 100, 200, 300) are clean. Left: coverage-guided fuzzing (SharpFuzz) on a CI schedule |
| 8.5 Generated conformance doc | `eng/conformance-report.sh` assembles the harness reports into docs/conformance-sets.md: pass rates per test set and per spec and feature, for the 3.0 and 2.0 profiles and QT3. The runner writes per-feature counts and a report per profile; CI writes the page to the job summary and the test-results artifact. docs/conformance.md keeps the dated history by hand |
| 8.7 Upstream sync | Checked on 2026-10-21: the weekly workflow ran on 2026-10-19 and succeeded; dotnet/runtime has no commits on the forked paths since the fork point (`ec0059c`, 2026-10-18), so nothing to port |
| 8.2 Property tests | `tests/Xsl3.Tests/PropertyTests.cs`: seeded generated values check cast round trips through xs:string, `castable as` against `cast`, value comparison as an order, map persistence against a dictionary model (old versions unchanged, lookup tables included), numeric map keys by value, and collation consistency (codepoint, HTML case-blind, UCA). Written without a framework (no new dependency); CsCheck could replace the generators |
| 9.3 Docs for users | docs/getting-started.md (the 3.0 API: entry points, values, handlers, settings, errors, extension functions, packages, ahead-of-time compilation) and docs/migration.md (from `System.Xml.Xsl`, and from 1.0 to 3.0 stylesheets); the divergences list is current |
| 4.6 Missing 3.0 pieces | `xsl:assert`, initial functions (with raw result delivery), memo functions (`new-each-time="no"`, `cache="yes"`), `copy-namespaces="no"` on `xsl:copy` and `xsl:copy-of`, `inherit-namespaces="no"`, `fn:transform`, initial template and mode parameters, the `xsl-stylesheet-processing-instruction` feature (`XsltCompiler.GetAssociatedStylesheet`, `CompileAssociatedStylesheet`), backwards compatibility (`version="1.0"` regions in compat mode; `XsltSettings.VersionMode = BackwardsCompatible` for whole 1.0 stylesheets, SPEC §4.3) done. `xsl:apply-imports` in named templates (2.0+), `exsl:node-set` and `msxsl:node-set` as the identity. Left in backwards compatibility: keys defined in both modes, `exsl:document`, and a 1.0 principal module that includes a 2.0+ one (SPEC §4.3 says the 3.0 processor; today it is legacy mode) |
| 4.7 Diagnostics | `XsltArgumentList.TraceHandler` receives `fn:trace`. `XsltSettings.EnableTracing` compiles calls to an `IXsltTraceListener` (`XsltArgumentList.TraceListener`, `Xslt30Transformer.TraceListener`) around every 2.0+ template and function. `XsltExecutable.Explain` and the `xsl3` CLI (`src/Xsl3.Cli`: `transform`, `explain`, `--trace`) are done; publishing `Xsl3.Cli` to nuget.org is the owner's call (the release workflow packs only `Xsl3`). Debug PDBs: embedded in saved assemblies compiled with debug information (Sink B). Dynamic (`DynamicMethod`) units still have no symbols |
| 5.1 xslt30-test | 3.0 profile 95.8% → 97.9%, 2.0 profile 96.7% → 98.7%. Fixed: `regex-group()` in called templates (through the tunnel frame), `xsl:sort` attributes (lang, collation, case-order, current-group hidden), non-terminating `xsl:message` that holds maps or fails, rounding of doubles on their exact value, `xsl:mode` and `xsl:decimal-format` attributes merged by precedence (non-BMP decimal-format characters), XTDE0045, XTDE0050, XTDE0420, XTSE0085, XTTE0990, `deep-equal` and grouping collations, big integers in `xsl:number`, attribute sets and `xsl:next-match`. Left: next-match between union alternatives, copy-namespaces/inherit-namespaces, initial template and mode parameters, absent context items, accumulator applicability, `fn:transform`, DTD-validated inputs (element-content whitespace), package-scoped outputs |
| 5.1 xslt30-test, second batch | 3.0 profile 97.9% → 99.0% (Hardening target met), 2.0 profile 98.7% → 99.2%. Fixed: `fn:transform`, `inherit-namespaces="no"`, absent initial context items, initial template and mode parameters, `$err:line-number`, accumulator applicability, package checks (XTSE3040, XTSE3051, XTSE3080), DTDs and external-entity base URIs in 2.0+ documents, collections. Left (76): next-match between union alternatives, namespace fixup that re-prefixes instead of raising XTDE0430, the static pre-scan (`static-021`, `use-when-0138`), `global-context-item` errors, backwards-compatible processing, DTD element-content whitespace, PI base URIs, errors in node-pattern predicates as non-matches, `<a/>` in serialization regexes |
| 5.2 QT3 | 99.4% → 99.7%: `fn:random-number-generator` (seeded once per transformation), `fn:function-lookup` argument checks, collections |
| Pre-release audit (2026-10-21) | Four review passes over this branch, then fixes: result documents confined to the base output directory (a directory URI, case-insensitive comparison on Windows and macOS, no symbolic links, never a file system root, a nested transform's own opener), settings taken at `Load` and validated when set, `doc()`/`unparsed-text()`/`json-doc()` gated by `EnableDocumentFunction` (SPEC §21.1), cyclic CLR parameter values refused, secondary result files always closed, `function-available` with impossible arities, rounding overflow, `xsl:assert` EQName error codes, errors raised by the stylesheet inside `xsl:message`, the release workflow split into build and publish jobs with pinned actions. Open: same-precedence conflicts below a higher-precedence `xsl:mode`/`xsl:decimal-format` aren't reported (XTSE0545, XTSE1290); `Timeout` isn't checked inside pure XPath loops (a regular expression match stops at it: its match timeout is the transformation's, 2026-10-21) |
| 5.1 xslt30-test, third batch | `fn:idref` over DTD IDREF/IDREFS attributes (a bridge to the DTD's attribute types; QT3 +14, xslt30-test +5, 2.0 profile 99.3%). The harnesses load sources as Xsl3's own tree. XTDE3086 (`use="required"` global context item missing) and XTSE3450 (inconsistent static variables across import precedences): 3.0 profile 99.2%. Then: the global context item absent without an input or with `use="absent"`, and xsl:next-match from an alternative of a union pattern (one rule per alternative; 3.0 profile 99.3%). Left: static variables of an import visible after the `xsl:import` in the importing module (static-021; imports load after the module, which the loader relies on). Earlier note, now done: xsl:next-match from a union pattern whose alternatives have different default priorities (next-match-021, -023, -025, -026, -039) needs the alternative that matched at run time, or one template rule per alternative |
| 5.1 xslt30-test, fourth batch | 3.0 profile 99.3% → 99.5% (7,996 passed, 40 failed), 2.0 profile 99.5% → 99.6%. Fixed: large templates split into called templates no longer pass a shadowed local variable twice; `function-lookup` with a literal name in a used package finds the package's own function, not its override (an abstract one not at all); `function-available` in use-when leaves out the XSLT functions that need a dynamic context; the static base URI inside an external entity; whitespace in elements a DTD declares element-only is left out of the tree (XDM element content whitespace); XTDE1500 for a result document written to a URI the transformation read; a processing instruction's base URI from its external entity; `data-type="number"` on `xsl:merge-key`; disable-output-escaping text in a cdata-section element written outside the CDATA section; copies of document nodes and `xsl:copy-of` elements keep their original's base URI; XTSE3020 for an `xsl:expose` naming a function without its arity. Then 99.6% (8,001 passed, 35 failed; 2.0 profile 99.7%): `document('')` against the xml:base static base URI, string comparison for keys with a backwards compatible definition, the xml method for a 1.0 principal's XHTML `html`, and the harness reading expected serializations in their declared encoding. Then 99.7% (8,008 passed, 28 failed): XTSE0165 for imported packages, XTSE0020 for `xsl:mode name="#unnamed"`, an absent global context item for used packages' variables, package-local character maps, `doc()` in static expressions, XTDE2210 for disagreeing merge keys, use-when errors ignored on unknown elements in forwards compatible mode. Left, with reasons: `<a />` for empty elements (four tests, implementation-defined, docs/divergences.md); whitespace stripping per package (document-2401, -2402); errors in patterns as non-matches (import-1201, a try around every predicate); base URIs of parentless copies with a relative xml:base (base-uri-046); namespace nodes that don't inherit from a new parent (copy-1221); XInclude (base-uri-052) |
| 5.2 QT3, second batch | 99.9% → 99.99% (21,883 passed, 1 failed): `position#0` and `last#0` keep their focus, comparisons with a range use its bounds (no materialising), partial application checks the function's arity (XPTY0004), declared signatures for the compiled higher-order functions, `fn:load-xquery-module` raising FOQM0006, German ordinal endings (`o(-er)`), FOAR0002 for arities past `int`, and the harness maps relative resource URIs to their files. Left: `<f />` (implementation-defined) |
| 5.4 Errors without a matching code | The xslt30-test runner now compares codes (an error that names the expected code matches; it used to count every expected error) and `XSL3_XSLT30_CODES=file` lists the others: 573 in the 3.0 profile (254 in the `error` set), 343 in the 2.0 profile. Then the messages inherited from the 1.0 compiler start with their XSLT code in 2.0+ stylesheets (XTSE0010, XTSE0090, XTSE0020, XTSE0280, the element-specific select-and-content codes and others), and a module that can't be read is XTSE0165: 336 left in the 3.0 profile, 190 in the 2.0 profile. Then XTSE0210 for import cycles, SEPM0016 for xsl:output's yes/no values and XTDE0830/XTDE0860 for undeclared prefixes in computed names: 315 and 176. The runner's any-of now takes the alternative an error names (XTDE1350 or FOFD1350): 261 and 133. Then codes for more inherited messages (XTSE0630, XTSE0580, XTSE0550, XTSE1290, XTSE1130, XTSE1017, XTSE0500, XTSE1205, XTSE0130, XTSE0120) and XTDE0410 at run time: 230 and 102. Then XTSE0020 for attribute values that aren't QNames, XTSE0808/XTSE1430 for undeclared prefixes in prefix lists, XTSE0740, XTSE0770, XTSE1080, XTSE3440, XTDE0640 for a key index asked for while it is built and XTDE3400 for an accumulator that needs its own values: 196 and 83. QT3 has 72. With `schema_aware` claimed the 3.0 profile had 218; then XTTE1555 for document-level constraints (duplicate IDs, identity constraints) wherever they arise, XTTE1535 for an attribute given a complex type and XTTE1545 for a QName or NOTATION value whose prefix isn't in scope: 204. Then XTDE0820 and XTDE0850 for element and attribute names that aren't QNames (static ones too), and XTSE3025 before XTSE3080 for a component only xsl:expose makes abstract: 184 in the 3.0 profile, 73 in the 2.0 profile. Then XTDE0450 for a map or function in a node's content, and XTDE1480 for xsl:result-document in the content of a sort key, merge key, key or accumulator rule: 176 and 73. Then XTTE0510 for xsl:apply-templates without select on an item that isn't a node, XTTE1000 for an xsl:number select that isn't one node, XTSE1295 for a zero-digit that isn't a zero digit and XTDE3510 for current-merge-key() outside a merge action: 164 and 65. Then XTDE0700 for a parameter that isn't supplied when the empty sequence, its implicit default, isn't of its type: 162. Then XTSE0550 for a mode attribute token that isn't a mode name, and XPTY0004 (XSLT 3.0's code; 2.0's was XTTE0790) for a stylesheet function argument that doesn't convert to its parameter's type: 156 in the 3.0 profile, 62 in the 2.0 profile. Then xsl:message's error-code given as an EQName or as an attribute value template without expressions, and the runner comparing an expected EQName code with the error's expanded name: 151. Then XTSE0020 for an xsl:use-package package-version that isn't a list of version ranges: 147. Then codes for more inherited messages (XTSE0010 for xsl:choose's structure, XTSE0020, XTSE0125, XTSE0265, XTSE0809, XTSE1040, and XTDE0560 for xsl:apply-imports with no current template rule): 130 in the 3.0 profile, 47 in the 2.0 profile. Then XTTE0945 for xsl:copy in a function body (it was a NullReferenceException at compile time) and XTDE0420 for an attribute or namespace node in a document node: 125 and 43. Then XTSE0110 for a version attribute that isn't a number, XTSE0010 for xsl:analyze-string's child order, XTDE1260 for a key name with no xsl:key, and XTDE0045 for an initial mode that isn't public: 119 and 41. Then XTDE1450 for an unknown extension element without xsl:fallback, and XTDE1360, XTDE1061 and XTDE1071 for current(), current-group() and current-grouping-key() in a function body: 113 and 39. Then XTSE0170 and XTSE0190 for xsl:include and xsl:import below the top level, XTSE0350 for a value template's unmatched left curly bracket and XTSE0370 for an undoubled right one: 108 and 34. Then an untyped value that doesn't cast to an as type raises the conversion's XSLT code (XTTE0570, XTTE0505, ...) rather than FORG0001, and computed names raise XTDE0835, XTDE0855 and XTDE0865 for the xmlns prefix and name: 104 and 30. Then XTSE0020 for a fixed xsl:number start-at that isn't a list of integers: 102. Then XTSE3060 (not XTSE3058) for overriding a component that is private in the used package: 99. Then FODC0002 for a document() that can't be loaded, and XTTE2230 for incomparable keys of different merge sources (no longer wrapped in the sort's exception): 96. Then XTDE0050 for a supplied static parameter value and XTDE0700 for a static parameter's implicit default that don't match its as type: 95. Then XTDE0430 in 2.0+ for two namespace nodes binding one prefix to different URIs: 94. Then XTSE3088 (XTSE3089 is the global context item's) for xsl:context-item with use="absent" and an as attribute, and XTSE0620 for a static variable with both select and content: 92. Then XPTY0004 for xsl:evaluate's as conversion and XTTE3165 for with-params keys that aren't QNames, and the runner resolving a prefixed expected code (my:ABCD9999) against the test catalog's namespaces: 88. Then XTTE1510 for an attribute that xsl:copy-of or xsl:copy validates against a type attribute and that fails (a copied element or document stays XTTE1540): 86. Then XTDE0030, statically, for a fixed xsl:number lang that isn't a language code: 85. Then XTSE0165 for a module named with a URI scheme the resolver doesn't support: 83. (XTSE0150 for a document that isn't a stylesheet stays without a code: the upstream tests check stock's message, and the version isn't known yet.) Then XTTE0600 for a template parameter's explicit default value that doesn't match its as type: 82 in the 3.0 profile, 24 in the 2.0 profile. Then XTSE0690, statically, for xsl:call-template without a required non-tunnel parameter of the template: 80 and 22. Then XTSE3520 for an xsl:iterate parameter without an initial value whose type excludes the empty sequence: 78. Then XTDE1110 for an unknown xsl:for-each-group collation, XTTE1515 for a value invalid for its datatype under validation="lax" (other lax failures, an xsi:type, stay XTTE1510), and XTDE0890 for a fixed processing-instruction name that isn't an NCName: 75 and 21. Then a computed xsl:message error-code (e:{$code}), compiled as an XPath expression that resolves its prefix against the instruction's namespaces (XTMM9000 if it isn't a QName): 71. Then SENR0001 for a map or function in the principal result (not a node's XTDE0450): 68. Then FOXT0002 for an fn:transform stylesheet that can't be loaded, and XTDE0835 and XTDE0865 for computed names in the xmlns namespace, whichever way the namespace is given: 65 in the 3.0 profile, 19 in the 2.0 profile. Then XTSE0215 for an inline schema whose target namespace isn't xsl:import-schema's namespace: 63. Then XTDE0830 and XTDE0860 for an undeclared prefix in a fixed xsl:element or xsl:attribute name, and XTSE1520 for a type attribute that isn't a QName: 61. Then XTSE0710 for a use-attribute-sets token that isn't a QName and XTSE0812 for an undeclared xsl:namespace-alias prefix: 59. Then XTSE1530 for xsl:attribute with a complex type, and XTSE3105 (not XPST0008) for a typed="strict" pattern naming an undeclared element: 56. Then XTSE3055 (not XTSE0660) for a template in xsl:override with the name of another of the package's templates: 55. Then XTSE3460 for xsl:apply-imports in a template rule of xsl:override: 54. Then XTDE3052 for an abstract template or variable used without an override (raised before its parameters' defaults): 50. Then an xsl:accept wildcard whose visibility a component can't take doesn't match it (the default visibility stays; only a rule naming the component is XTSE3040): 46. Then XTDE1425 for an unknown extension function called in backwards-compatible mode: 44 in the 3.0 profile, 15 in the 2.0 profile. Then XTDE1360 (not XPST0017) for current() where there is no current item, a function body: 43. Then XTDE1270 for key() with two arguments where the context item is absent: 42. Then XTDE0890 in the 2.0+ compiler's xsl:processing-instruction too: 40. Then XTSE1080 alone (no XPST0003 from compiling a grouping key that isn't there) for xsl:for-each-group without a grouping attribute: 38. Then XTDE0640, instead of exhausting the stack, for a key whose match or use calls key() with the key's own name: 37. Then SEPM0004 for an xml-method result with standalone that has more than one top-level element or top-level text: 36 in the 3.0 profile, 7 in the 2.0 profile. Then XTDE0050 for a static parameter whose default, or supplied value, isn't of its type (implicitly required): 35. Then XTSE3085 for a top-level package whose default-mode names a mode it doesn't declare: 33. Then XTSE0020 for xsl:mode visibility="abstract": 32. Stopped here for 1.0 (owner's decision, 2026-10-22): 32 left in the 3.0 profile and 7 in the 2.0 profile, mostly tests that disagree with each other (iterate-006/-007, package-200/use-package-29x), tests from before errata (expose-911/-913), errors where several apply and the suite picks another, and two needing more run-time machinery (attribute-set cycles through xsl:override, document() without a base URI) |
| Saved stylesheets and versions | CI's NativeAOT job failed on 65e1fcd: `samples/Xsl3.AotSample/xsl3/Stylesheets.Report.dll` had been committed, so a fresh checkout ran a stylesheet compiled by an older Xsl3, whose library function numbers differed (a map reached `fn:load-xquery-module`). Fixed: the generated assembly is no longer tracked, Xsl3.Build writes under `obj/<Configuration>/xsl3/` and recompiles when `Xsl3CompilerInputs` change, and saved static data (format 3) records a fingerprint of the function library, so `Load(Type)` refuses a stylesheet compiled by another Xsl3 instead of calling the wrong function. The tracked assembly had also hidden that the sample's compile step couldn't find its compiler when published for a runtime identifier: the compiler's project reference now drops it (CI's NativeAOT job passes on d9e1844) |
| Empty elements (owner's decision, 2026-10-21) | 2.0+ results of the xml method write `<a/>`: Xsl3's raw writers write it directly (`XmlRawWriter.CompactEmptyTags`, no cost), and output that goes through System.Xml's writers (result documents, `fn:serialize`, serialization parameters beyond XmlWriterSettings) passes through `EmptyTagFilter`, which follows the markup so text, comments, CDATA and processing instructions are untouched. xhtml keeps `<br />`; 1.0 is unchanged. Five tests pass (QT3 100%); the micro suite's geometric mean is 0.954 of the previous commit's |
| 1.3 Real stylesheets: DocBook xslTNG | `eng/fetch-bench.sh` fetches the 2.7.1 release (owner's approval, 2026-10-21); `DocBookStylesheetTests` (CI) converts the sample article and a test book; `-- docbook` benchmarks it. It found five faults, all fixed: static variables of imported modules after the `xsl:import` (static-021, static-027 pass), next-match from a union rule passing on a parameter, namespace nodes after attributes, the current group in called templates, and an `xsl:evaluate` cache too small for DocBook's 151 localization patterns (15 s to 2.3 s for a 301 KB book; docs/performance.md). Allocation for that book reduced from 3.1 GB to 2.4 GB a run (docs/performance.md) |
| Publishing Xsl3.Build and Xsl3.Cli (owner's decision, 2026-10-21) | The release workflow packs both at the tag's version beside Xsl3 (symbols for Xsl3 only; the tool packages keep their PDBs). Checked locally: packed at 0.9.0-test, the `xsl3` tool installs from the package and transforms, and a project with `Xsl3` and `Xsl3.Build` package references compiles an `XsltStylesheet` item at build time and loads it with `Load(Type)`. Xsl3.Build carries the compiler without a native app host. Publishing stays with the owner: the next tag and the `nuget` environment approval; nuget.org's trusted-publishing policy must allow the two new package ids |
| 5.5 Divergences | Done: the W3C list in docs/divergences.md |
| 6 `xsl:import-schema` | Raises XTSE1650 without `XsltSettings.SchemaAware`; with it, P6 is in progress (99.8% of the schema-aware tests; section 6) |
| 8.4 Memory test | Fixed: it runs in a child process |
| 8.6 DNS flake | Fixed: network failures are compared by type |
