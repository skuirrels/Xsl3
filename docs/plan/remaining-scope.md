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
| W3C QT3 (XPath 3.1, F&O 3.1, higher-order functions claimed) | 21,810 passed, 74 failed (99.7%) | ≥ 99% on claimed features (Hardening, met) |
| W3C xslt30-test, 3.0 profile | 7,962 passed, 74 failed, 49 not run (99.1%) | ≥ 99% on claimed features (Hardening, met) |
| W3C xslt30-test, 2.0 profile | 4,561 passed, 36 failed (99.2%) | ≥ 99% (met) |
| P5 sets (`evaluate`, `output`, `character-map`, `result-document`, `disable-output-escaping`) | 454 / 457 (99.3%) | ≥ 98% (met) |
| XSLT 1.0 throughput against stock | 1.38× geometric mean, never slower | ≥ 1.2× (met), target 1.5× |
| 3.0 rewrites on Xsl3 against the 1.0 originals on stock (time ratio) | identity 0.86, sort 0.71, muenchian 0.97, string-recursion 0.77, key-join 0.95 (medians of three runs, 2026-10-21) | ≤ 1.0 on every pair (§19.6, met) |
| Compact tree memory | 59% of `XPathDocument` | ≤ 75% (met) |
| NuGet | 0.7.0-alpha (published 2026-10-21) | 1.0 after P4 (§22 release plan) |

The xslt30-test catalogue has about 14,600 test cases. The 3.0 profile runs about 8,000 of them. Most of the
rest depend on features Xsl3 doesn't claim yet:

| Feature | Tests that need it | Workstream |
|---|---:|---|
| `streaming` | 2,983 | 7 (P7) |
| `schema_aware` | 933 | 6 (P6) |
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
     "expected errors passed without a matching code". When a change makes such a test produce a result
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
   under AOT. Decide whether 1.0 ships without it.

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
   - XML 1.1 input: .NET's `XmlReader` doesn't support it, so probably declare it unsupported in
     docs/conformance.md and the runner profile.

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

**State.** Not started. 933 xslt30-test cases need `schema_aware`. What happens today:
- `xsl:import-schema` is rejected as an unexpected element (`Xslt_UnexpectedElement`, XsltLoader.cs), not
  with the error a non-schema-aware processor should raise. Fix this first: it's small.
- `validation="strict|lax"` and `type=` raise XTSE1660 in 2.0+ stylesheets; `strip` and `preserve` are accepted.
- `schema-element()` and `schema-attribute()` parse, then raise XPST0008 ("no schema is imported").

6.1 **Plan** (S): `docs/plan/p6-schema.md`.
   - Decide XSD 1.1 (§18 options a/b/c; open question 3). XSD 1.0 only is conformant.
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

9.1 **Decide what 1.0 means** with the owner. Likely: workstreams 1, 2.2–2.3, 3, 4 and 5 done, and 6–7
   later. Open questions in §25 still to settle:
   - 4: a .NET 10-only release, or .NET 8 too, given Sink B needs a .NET 10 build host;
   - 5: parallelism in 1.0;
   - 6: the licence and contribution terms.
9.2 **API freeze**: run a public API analyzer (`Microsoft.CodeAnalysis.PublicApiAnalyzers`) from the first
   release candidate.
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

1. Whether 1.0 needs Sink C (an interpreter), or ships with `xsl:evaluate` unavailable under NativeAOT.
2. Which SaxonCS edition to compare against, given its licence (3.1).
3. The shape of the public 3.0 API (4.1): follow §7.2 as written, or a smaller first version.
4. XSD 1.1 (6.1): options a, b or c.
5. XML 1.1 input: declare it unsupported?
6. §25 questions 4–6: framework floor, parallelism in 1.0, licence and contribution terms.

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
| 4.5 Error model | Extension function exceptions are `Q{http://xsl3/err}extension-error` (catchable). The 3.0 API raises `XsltDynamicException` (code, description, error object, and the stylesheet stack of templates and functions with their locations when compiled with `EnableTracing`) and `XsltStaticException` (every static error with module, line and column). Left: a stack trace without tracing compiled in |
| 8.1 Fuzzing | `tests/Xsl3.Tests/RobustnessTests.cs`: seeded mutations of XPath expressions, JSON texts and a stylesheet must end in Xsl3's own errors (static, dynamic, XML), never another exception. 1,500 mutations each in the unit suite; `XSL3_FUZZ_MUTATIONS` and `XSL3_FUZZ_SEED` run longer campaigns. Fixed: a missing required expression attribute reached the XPath parser as null (now XPST0003). Four campaigns of 10,000 mutations (seeds 0, 100, 200, 300) are clean. Left: coverage-guided fuzzing (SharpFuzz) on a CI schedule |
| 8.2 Property tests | `tests/Xsl3.Tests/PropertyTests.cs`: seeded generated values check cast round trips through xs:string, `castable as` against `cast`, value comparison as an order, map persistence against a dictionary model (old versions unchanged, lookup tables included), numeric map keys by value, and collation consistency (codepoint, HTML case-blind, UCA). Written without a framework (no new dependency); CsCheck could replace the generators |
| 9.3 Docs for users | docs/getting-started.md (the 3.0 API: entry points, values, handlers, settings, errors, extension functions, packages, ahead-of-time compilation) and docs/migration.md (from `System.Xml.Xsl`, and from 1.0 to 3.0 stylesheets); the divergences list is current |
| 4.6 Missing 3.0 pieces | `xsl:assert`, initial functions (with raw result delivery), memo functions (`new-each-time="no"`, `cache="yes"`), `copy-namespaces="no"` on `xsl:copy` and `xsl:copy-of`, `inherit-namespaces="no"`, `fn:transform`, initial template and mode parameters, the `xsl-stylesheet-processing-instruction` feature (`XsltCompiler.GetAssociatedStylesheet`, `CompileAssociatedStylesheet`), backwards compatibility (`version="1.0"` regions in compat mode; `XsltSettings.VersionMode = BackwardsCompatible` for whole 1.0 stylesheets, SPEC §4.3) done. `xsl:apply-imports` in named templates (2.0+), `exsl:node-set` and `msxsl:node-set` as the identity. Left in backwards compatibility: keys defined in both modes, `exsl:document`, and a 1.0 principal module that includes a 2.0+ one (SPEC §4.3 says the 3.0 processor; today it is legacy mode) |
| 4.7 Diagnostics | `XsltArgumentList.TraceHandler` receives `fn:trace`. `XsltSettings.EnableTracing` compiles calls to an `IXsltTraceListener` (`XsltArgumentList.TraceListener`, `Xslt30Transformer.TraceListener`) around every 2.0+ template and function. `XsltExecutable.Explain` and the `xsl3` CLI (`src/Xsl3.Cli`: `transform`, `explain`, `--trace`) are done; publishing `Xsl3.Cli` to nuget.org is the owner's call (the release workflow packs only `Xsl3`). Debug PDBs: embedded in saved assemblies compiled with debug information (Sink B). Dynamic (`DynamicMethod`) units still have no symbols |
| 5.1 xslt30-test | 3.0 profile 95.8% → 97.9%, 2.0 profile 96.7% → 98.7%. Fixed: `regex-group()` in called templates (through the tunnel frame), `xsl:sort` attributes (lang, collation, case-order, current-group hidden), non-terminating `xsl:message` that holds maps or fails, rounding of doubles on their exact value, `xsl:mode` and `xsl:decimal-format` attributes merged by precedence (non-BMP decimal-format characters), XTDE0045, XTDE0050, XTDE0420, XTSE0085, XTTE0990, `deep-equal` and grouping collations, big integers in `xsl:number`, attribute sets and `xsl:next-match`. Left: next-match between union alternatives, copy-namespaces/inherit-namespaces, initial template and mode parameters, absent context items, accumulator applicability, `fn:transform`, DTD-validated inputs (element-content whitespace), package-scoped outputs |
| 5.1 xslt30-test, second batch | 3.0 profile 97.9% → 99.0% (Hardening target met), 2.0 profile 98.7% → 99.2%. Fixed: `fn:transform`, `inherit-namespaces="no"`, absent initial context items, initial template and mode parameters, `$err:line-number`, accumulator applicability, package checks (XTSE3040, XTSE3051, XTSE3080), DTDs and external-entity base URIs in 2.0+ documents, collections. Left (76): next-match between union alternatives, namespace fixup that re-prefixes instead of raising XTDE0430, the static pre-scan (`static-021`, `use-when-0138`), `global-context-item` errors, backwards-compatible processing, DTD element-content whitespace, PI base URIs, errors in node-pattern predicates as non-matches, `<a/>` in serialization regexes |
| 5.2 QT3 | 99.4% → 99.7%: `fn:random-number-generator` (seeded once per transformation), `fn:function-lookup` argument checks, collections |
| Pre-release audit (2026-10-21) | Four review passes over this branch, then fixes: result documents confined to the base output directory (a directory URI, case-insensitive comparison on Windows and macOS, no symbolic links, never a file system root, a nested transform's own opener), settings taken at `Load` and validated when set, `doc()`/`unparsed-text()`/`json-doc()` gated by `EnableDocumentFunction` (SPEC §21.1), cyclic CLR parameter values refused, secondary result files always closed, `function-available` with impossible arities, rounding overflow, `xsl:assert` EQName error codes, errors raised by the stylesheet inside `xsl:message`, the release workflow split into build and publish jobs with pinned actions. Open: same-precedence conflicts below a higher-precedence `xsl:mode`/`xsl:decimal-format` aren't reported (XTSE0545, XTSE1290); `Timeout` isn't checked inside long regular expressions or pure XPath loops |
| 5.5 Divergences | Done: the W3C list in docs/divergences.md |
| 6 `xsl:import-schema` | Raises XTSE1650 |
| 8.4 Memory test | Fixed: it runs in a child process |
| 8.6 DNS flake | Fixed: network failures are compared by type |
