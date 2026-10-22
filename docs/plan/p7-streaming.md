# P7: streaming

docs/plan/remaining-scope.md, workstream 7 (items 7.1 to 7.5): XSLT 3.0 streaming, the streaming feature of XSLT 3.0
§27.5. SPEC §17, §22 (P7), §19.6 (the streaming row). A 1.x release, after 1.0 (owner's decision, 2026-10-22).

This file is the plan that item 7.1 asks for. It covers the requirements, what the code does today, the design, the
order of work, and the approved owner decisions. Section numbers are those of the XSLT 3.0 Recommendation
(8 June 2017).

## Requirements

### What the spec asks of a streaming processor

- **Streamed processing where it is requested and guaranteed** (§27.5, §19.10). Streaming is requested by
  `streamable="yes"` on `xsl:mode`, `xsl:source-document` or `xsl:merge-source`. When the construct is guaranteed
  streamable and the input is supplied in streamable form, the input must be processed by streaming. An input
  supplied as a tree in memory needn't be streamed, but the analysis must still run.
- **Streamability analysis exactly as §19 defines it.** A construct is guaranteed streamable when it is
  declared streamable and the §19 rules say streaming is possible. The constructs that can be declared streamable are:
  - template rules of a mode with `streamable="yes"` (§6.6.4);
  - `xsl:source-document streamable="yes"` (§18.1);
  - `xsl:merge-source streamable="yes"` (§15.4);
  - `xsl:accumulator streamable="yes"` (§18.2.9);
  - `xsl:attribute-set streamable="yes"` (§10.2.3);
  - `xsl:function` with a `streamability` category other than `unclassified` (§10.3.5, §19.8.5).
    The categories are `absorbing`, `inspection`, `filter`, `shallow-descent`, `deep-descent` and `ascent`.
- **XTSE3430, at the user's option** (§19.10). A construct that is declared streamable but not guaranteed
  streamable must be handled in one of three ways, chosen by the user:
  - raise XTSE3430;
  - run the construct as a non-streaming processor would;
  - stream it where the processor can, and raise XTSE3430 where it can't.
- **The same results as unstreamed evaluation.** Only memory use may differ.
- **`system-property('xsl:supports-streaming')` returns `"yes"`** (§27.5). `fn:stream-available` (§18.1.3)
  keeps its meaning.
- **Memory.** §27.5 defines streamed processing as the ability to "process arbitrarily large input documents
  without ever-increasing memory requirements". `xsl:fork` explicitly permits buffered output, even unbounded
  buffering (§16.1).

### What Xsl3 adds

- **SPEC §17's memory target.** A guaranteed-streamable stylesheet runs in constant memory with respect to document
  size. CI checks this on a 2 GB generated document with a 200 MB limit.
- **SPEC §22's P7 exit.** The streaming test sets pass at 90% or more, and the constant-memory test passes.
- **No cost to stylesheets that don't stream.** As with P6, the analysis and the streamed runtime paths run only
  when a stylesheet declares something streamable. The micro suite and the perf gate (`eng/perf-gate.sh`) check this.
- **The usual gates are unchanged** (remaining-scope.md, "Gates before every push to main"). They are the upstream
  tests, the differential tests, the W3C baselines, NativeAOT and the saved-assembly round trip.

### How the suite tests it

Counted from `external/xslt30-test/catalog.xml` on 2026-10-22, with a script over every test case's and test set's
dependencies:

- **2,900 test cases need the `streaming` feature.** Of these, 2,542 are in `tests/strm` and 358 elsewhere
  (`attr`, `insn`, `decl`, `fn`, `misc`). 152 of them expect XTSE3430.
- **919 test cases, all in `tests/sandp`, need `sweep_and_posture`** (`supports-sweep-and-posture-assessments`).
  Each one gives XPath expressions, a context posture and an expected posture and sweep
  (`assert-posture-and-sweep`). They test the analysis alone, with no execution.
- **2 test cases need `streaming-fallback`.**

**Baseline, measured 2026-10-22 on `perf-fixed-cost` (0c4fac0).** A temporary runner profile claimed `streaming`
on top of the 3.0 profile and selected only the tests that need it, as `XSL3_XSLT30_PROFILE=sa` does for schemas.
It was reverted afterwards.

| Result | Tests |
|---|---:|
| Passed (unstreamed) | 2,544 |
| Failed | 167 |
| Not run | 1 |
| Pass rate | 93.8% |

The 167 failures:

- **136 expect XTSE3430 and get a result.** These need the analysis. The largest groups are `streamable` (26),
  `si-fork` (10), `su-absorbing` (7), `si-for-each-group` (6) and `accumulator` (6).
- **31 are other errors.** Of these:
  - 9 are XTDE2220 in `merge-070` to `merge-084`: streamed merge inputs read in the wrong order;
  - 3 are XTTE1510 for streamed validation in `streamable-021`, `-042` and `-043`;
  - 2 are XTSE3155, a function with no parameters and a streamability category, in `error-3155a` and `su-shallow-descent-901`;
  - 2 are `system-property` and `stream-013` expecting `supports-streaming` to be `yes`;
  - the rest are single cases: `sf-reverse-001`, `si-fork-809`, `si-value-of-102`, `streamable-035`, `-036`,
    `-116` and `-147`, `accumulator-061`, `mode-1506`, `error-0730a` and `si-result-document-024`.

**The suite can't tell streamed from unstreamed execution.** 94% of the streaming tests already pass on today's
tree-building runtime. So the pass rate alone doesn't show that P7 works. Two checks are needed besides it:

- **the strict streamed mode** in the runner (stage S3);
- **the constant-memory test** (stage S7).

## What happens today

These facts were read from the code on 2026-10-22.

- **Every streaming attribute is validated, then ignored.**
  - `streamable` on `xsl:source-document`, `xsl:merge-source`, `xsl:accumulator`, `xsl:mode` and
    `xsl:attribute-set` goes through `ParseYesNoAttribute`, at `XsltLoader.V2.cs:577`, `:342`, `:413` and `:1455`
    and `XsltLoader.cs:1696`.
  - `xsl:mode` also records its value, but only for the XTSE0545 conflict check (`Compiler.cs:573`).
  - `xsl:function/@streamability` is declared (`XsltLoader.V2.cs:1257`) but never read, so XTSE3155 is never raised.
  - XTSE3430 is raised nowhere, and `supports-streaming` is `"no"` (`XsltNames.cs:119`).
- **Every input becomes a tree before it is used.**
  - The principal source is built eagerly in `XmlQueryContext`'s constructor (`ConstructDocument`, `BuildTree`).
  - `doc()` and `xsl:source-document` go through `GetDataSource`, which caches by URI. `xsl:source-document` is
    `doc()` plus a focus (`XsltLoader.V2.cs:561–596`).
- **The 3.0 constructs that streaming relies on all materialise their input.**
  - **Accumulators** walk the whole tree the first time a value is asked for, then cache every node's values
    (`XsltLibrary.Accumulators.cs`).
  - **`X31Iterate`** takes an `IList<XPathItem>` (`XsltLibrary.Iterate.cs:51`).
  - **`X31Merge`** collects every item of every input, with its key, into one list (`XsltLibrary.Merge.cs`).
  - **`xsl:fork`** is the concatenation of its prongs, a `List` (`XsltLoader.V2.cs:965`; its comment cites §18.2.4,
    but `xsl:fork` is §16.1).
- **Generated code works on `XPathNavigator`s throughout.** Patterns match from the last step upwards through the
  parent and ancestor axes (`Patterns.cs`). Template dispatch is nested `Conditional`s in priority order
  (`MatcherBuilder`).
- **Output is already push-based.** `XmlQueryOutput` drives an `XmlRawWriter` chain, which buffers only in these
  cases:
  - nested sequence construction (`XmlCachedSequenceWriter`);
  - `item*` templates;
  - json and adaptive output;
  - attribute caching.
- **No pass analyses the XPath 3.1 AST statically.** Expressions are kept as strings on `XslNode` and parsed when
  QIL is generated (`QilGenerator.V2.cs:612`). Static types exist only as QIL `XmlType`s.
  - `XslAstAnalyzer` computes focus flags with the 1.0 parser, and 2.0+ overrides them (`QilGenerator.cs:155`).
  - `QilGenerator.Streaming.cs` has nothing to do with XSLT streaming. It decides whether a template's result is
    `item*` or `node*`.
- **QIL annotations are open-ended.** `QilNode.Annotation` is one `object?` slot. `XmlILAnnotation.Write` keeps an
  existing annotation as its predecessor, so an analysis result attached before IL generation survives it.
- **Reusable pieces:**
  - the compact tree's `Builder`, with push-style `StartElement`, `Attribute`, `Text` and `EndElement`, and its
    `small` mode for temporary trees;
  - `CompactDocument.Writer`, an `XmlRawWriter` sink;
  - `NodeValidator`, which runs `XmlSchemaValidator` over events;
  - the child-process memory probe in `CompactTreeTests.cs:308` (`tests/Xsl3.Tests/Program.cs`, `memory-probe`).

## Design

### 1. Streamability analysis

**Where.** A new `src/Xsl3/Streaming/` folder (namespace `Xsl3.Streaming`), run from `QilGenerator` after loading
and before QIL is generated. It runs only if the stylesheet declares at least one construct streamable.

**Input.**
- The XSL AST.
- The XPath 3.1 AST of every expression, pattern and value template in a declared-streamable construct, and in
  everything those constructs reach: called templates, functions, attribute sets and accumulators.
- Expressions are parsed once. The AST is kept on `XslNodeV2` and reused by `CompileXPath31`, so that streaming
  stylesheets don't parse twice. Other stylesheets keep today's path.

**What it computes.** Each rule below is one method, named after its section, so that a reviewer can check it
against the spec:

| Section | What |
|---|---|
| §19.1 | Static types as U-types (sets of node kinds and atomic or function types), including axis steps (§19.1.1), `current()` (§19.1.2) and schema types (§19.1.3) |
| §19.2 | Context item types |
| §19.3 | Operand roles and usages: absorption, inspection, transmission, navigation |
| §19.4 | Postures: grounded, climbing, striding, crawling, roaming |
| §19.5 | Context postures |
| §19.6 | Sweeps: motionless, consuming, free-ranging |
| §19.7 | Grounded consuming constructs |
| §19.8.1 | The general rules |
| §19.8.3 | Sequence constructors |
| §19.8.4 | Every instruction (42 rules) |
| §19.8.5 | Stylesheet functions, by category |
| §19.8.6 | Attribute sets |
| §19.8.7 | Value templates |
| §19.8.8 | Every expression form (18 rules) |
| §19.8.9 | The built-in functions with their own rules (18) |
| §19.8.10 | Patterns |
| §6.6.4, §15.4, §18.1, §18.2.9 | The declaration-level rules for templates, merge sources, source documents and accumulators |

**Output.**
- A `Streamability` record (posture, sweep and the rule that decided them) for each construct, in a side table
  keyed by AST node.
- For each declared-streamable construct, a verdict: guaranteed, or not guaranteed with the chain of rules that
  failed.
- Verdicts are attached to QIL as an annotation on the nodes that start streamed evaluation.

**Errors.**
- **XTSE3430** names the construct, its line, and the operand that made it free-ranging. One example:
  "the second consuming operand of `xsl:sequence` at line 12; `count(*)` was already consuming".
- **XTSE3155** for an `xsl:function` with no parameters whose `streamability` isn't `unclassified` (§10.3.5).
- The other rules that are static errors in a streaming processor only, for example where `accumulator-before` and
  `accumulator-after` may appear.

**The user's option** (§19.10) is a new setting, `XsltSettings.Streamability`:

| Value | What happens to a construct declared streamable but not guaranteed |
|---|---|
| `Strict` | XTSE3430 |
| `Fallback` | The construct runs unstreamed, as today |

The approved default is `Strict` (owner decision 3, 2026-10-22). Compiler analysis accepts the semantics-preserving
rewrites documented in the S2 progress section below; the analysis-only harness retains the source syntax rules.

**Diagnostics.**
- `XsltExecutable.Explain` and `xsl3 explain` print each declared-streamable construct with its posture, sweep and
  verdict.
- A test hook evaluates the posture and sweep of one XPath expression for a context posture. The runner's
  `assert-posture-and-sweep` uses it, so that the 919 `sandp` tests check the expression rules before anything runs.

### 2. Streamed execution: a forward-only navigator over `XmlReader`

SPEC §17 now records the approved pull-based streamed navigator (decision 1), replacing the original push-based
event pipeline. Push is kept where the spec needs fan-out.

**Why not push.**
- Every piece of generated code, every iterator in `ContentIterators` and `TreeIterators`, and every runtime helper
  takes an `XPathNavigator`.
- A push pipeline would need a second code generator. Template rules would become event handlers, and every
  consuming function (`sum`, `string-join`, `count`, `string(.)`) a reducer.
- That doubles the code generation and its testing for every construct.

**Why pull works.**
- Within a guaranteed-streamable construct, the §19 rules already ensure that streamed nodes are visited in document
  order with at most one downward pass.
- A navigator that only moves forward can therefore serve the existing generated code unchanged.
- Each place where this doesn't hold is a design item below: fork, accumulators, merge, iterate, grouping and
  grounding.

**`StreamedDocument` and `StreamedNavigator`** (`src/Xsl3/Streaming/`):

- **One reader per streamed document.** One `XmlReader` per streamed document is the cursor, and every navigator
  over that document shares it.
- **The reader chain** is the same as today's input. It has the whitespace-stripping reader, and the validating
  reader when validation is asked for: `XmlSchemaValidator` streams, so P6's typed input works streamed.
- **A navigator is a position: a node number and a depth.** The document keeps an ancestor stack. Each open element
  has these entries:
  - its name;
  - its in-scope namespaces;
  - its attributes, read whole at the start tag;
  - its base URI;
  - its type annotation;
  - its node number;
  - its child counters;
  - its accumulator values.
- **Climbing navigation reads only the stack.** Moves to the parent, ancestors, attributes and namespaces read the
  ancestor stack and never move the reader.
- **Downward navigation advances the reader.** The supported moves are `MoveToFirstChild` and `MoveToNext` on the
  child axis, and descendant iteration in document order. Content that a step passes over is skipped with
  `XmlReader.Skip`.
- **A string value consumes its node.** The string value of an element or document reads to the node's end tag.
- **Identity, `generate-id` and document order** come from node numbers assigned as the reader advances. Node
  numbers are unique within the transformation, as in the compact tree.
- **A move the stream has passed is an error.** If a navigator is asked to move from a position the stream has
  already left, it raises `Q{http://xsl3/err}stream-position`. That error is an internal one: it means the analysis
  or the compiler is wrong, and it is never wrong output. The strict streamed mode (S3) makes every such case a
  test failure.

**Grounding** (§19.7, §18.3, §18.4):
- `copy-of()`, `xsl:copy-of` and an `as` type that atomizes read the current subtree into a small compact tree.
  This uses the existing `Builder` in `small` mode, fed from `XmlReader.ReadSubtree()`.
- `snapshot()` adds copies of the ancestors from the stack, with their attributes. This is the existing
  `X31Snapshot` model with a different source.
- Copied accumulator values travel as they do today (`_accumulatorCopies`).

**Where streamed evaluation starts.** Each entry point opens a `StreamedDocument` instead of building a tree:

| Entry point | What |
|---|---|
| `xsl:source-document streamable="yes"` | The instruction's focus is the streamed document node. The document isn't cached by URI, and a second read opens it again (§18.1) |
| A streamable initial mode, with the source supplied as a reader, stream or URI | The initial match selection is the streamed document node (decision 5 for the API) |
| `xsl:merge-source streamable="yes"` | One streamed document per input, read in step (see below) |

Within these, every streamed-node operand that the analysis marked consuming compiles to its usual QIL. The
annotation decides only the runtime helper used where a materialising one exists today.

**The QIL optimizer must not break the single pass.** These rewrites can add a second pass over a consuming
operand:
- hoisting;
- common-subexpression elimination;
- variable inlining into two uses;
- `DocOrderDistinct` that materialises;
- caching iterators.

Rewrites in a QIL subtree annotated as streamed are restricted to those that keep order and evaluate an operand at
most once. A QIL verifier in debug builds checks every streamed subtree after optimisation. The runtime error above
catches anything both miss.

### 3. Constructs that need their own runtime

| Construct | Today | Streamed |
|---|---|---|
| Accumulators (§18.2) | A whole-tree walk on first use, values cached per node | **Observers on the reader.** Pre-descent rules run at each start tag and post-descent rules at each end tag, on a navigator at that node (§18.2.9 makes them motionless). Before values live on the ancestor stack, after values until the parent's end. Only accumulators named in `use-accumulators` run |
| `xsl:iterate` (§7.2) | `IList<XPathItem>`, size known | **An iterator variant**, with no size (`last()` isn't streamable) |
| `xsl:for-each-group` (§19.8.4.19) | Materialised groups | **`group-adjacent`, `group-starting-with` and `group-ending-with` stream.** `current-group()` is a lazy iterator that stops at the next group's first item. The keys are motionless, so the next start tag decides the boundary. `group-by` and `xsl:sort` are streamable only inside `xsl:fork`, where they buffer |
| `xsl:merge` (§15.4) | Every item of every input in one list | **A k-way merge with one item of lookahead per source.** Each streamed source has its own reader. The merge keys are motionless, so they are computed at the start tag. `current-merge-group()` per source is that source's run of equal keys. XTDE2220 is raised when a source goes out of order |
| `xsl:fork` (§16.1, §19.8.4.20) | The prongs concatenated | **Fan-out** (below) |
| Streamable functions (§19.8.5) | Ordinary functions | Unchanged code. The first argument is a streamed node, and the category is checked at compile time |
| `xsl:try` (§19.8.4.39) | Output buffered for rollback | Unchanged. Buffered output is allowed |
| `xsl:result-document` in a streamed template | Pushes output | Unchanged. Output is already push-based |

**`xsl:fork`.** Each prong consumes the same input independently, so a single pull cursor can't serve them. The
spec itself suggests one reader feeding events to one thread per prong (§16.1), and it allows the later prongs'
output to be buffered.

- **The tee.** A `StreamTee` reads the input once into a bounded ring buffer of events.
- **The prongs.** Each prong runs on its own thread over a `StreamedNavigator` reading from the tee.
- **The bound.** The tee drops an event once every prong has passed it. A prong that gets too far ahead waits for
  the slowest one. Memory is bounded by the ring buffer plus the buffered output of prongs 2 to n.
- **The results.** They are concatenated in prong order.
- **Errors.** The first error in prong order wins, and the other prongs are cancelled.
- **Cancellation and limits.** `CancellationToken`, `Timeout` and `MaxRecursionDepth` apply to every prong.
- **Decision 4.** These are threads for the semantics, not for speed, against the 1.0 decision of no parallelism.

### 4. Settings, API and packaging

- **`XsltSettings.Streamability`** (above). `system-property('xsl:supports-streaming')` becomes `"yes"` when S7
  claims the feature.
- **New overloads on the 3.0 API.** A streamed initial match selection needs these on `Xslt30Transformer`
  (decision 5):

  ```
  ApplyTemplates(XmlReader source, Destination, QName? mode = null, …)
  ApplyTemplates(Stream source, Uri? baseUri, Destination, QName? mode = null, …)
  ApplyTemplates(Uri source, Destination, QName? mode = null, …)
  ```

  - They stream when the mode is streamable and build a tree otherwise.
  - `ApplyTemplates(XdmValue)` never streams, because the tree is already in memory (§19.10's note).
  - The legacy `XslCompiledTransform.Transform(XmlReader, …)` streams for a 2.0+ stylesheet whose default mode is
    streamable.
  - The global context item for a streamed initial match selection follows the spec's rules. They are to be read in
    full when S4 starts.
  - The new members go in `PublicAPI.Unshipped.txt`.
- **Saved assemblies (Sink B).** The analysis runs at compile time. The static data records each streamed entry
  point and the accumulators' rule kinds, so the format version goes up. The round trip (`XSL3_W3C_SAVED`) covers
  the streaming tests.
- **NativeAOT.** Nothing new: the streamed runtime is ordinary code, and the fork threads are `Thread`s.
- **Packaging** (decision 2). SPEC §6.3 lists an `Xsl3.Streaming` package. This plan keeps streaming in the
  `Xsl3` assembly, as P6 did with schemas:
  - the analysis must run inside the compiler;
  - the runtime hooks sit in `XmlQueryRuntime` and `XsltLibrary`;
  - a separate assembly would need public extension points for both.

  Stylesheets that don't stream pay nothing, which the perf gate checks.

## Stages

Sizes follow remaining-scope.md: S is days, M is one to three weeks, L is a month or more. The total is SPEC §22's
6 to 12 person-months.

| Stage | What | Size | Exit |
|---|---|---|---|
| S0 | This plan; the owner's decisions below (approved 2026-10-22) | S | Complete: decisions recorded here and in SPEC §17 and §6.3 where they change them |
| S1 | **The `strm` profile** in the runner (`XSL3_XSLT30_PROFILE=strm`: the 3.0 profile plus `streaming`, selecting only tests that need it; its own report, `xslt30-run-strm.md`), and the expression analysis: U-types, postures, sweeps, §19.1 to §19.8.1, §19.8.8 and §19.8.9. The runner's `assert-posture-and-sweep` and the `sweep_and_posture` dependency, through the test hook | L | `sandp` (919 tests) at 99% or more. The baseline above reproduced |
| S2 | **The construct analysis**: §19.8.3 to §19.8.7, §19.8.10, and the declaration rules of §6.6.4, §10.2.3, §10.3.5, §15.4, §18.1 and §18.2.9. XTSE3430 with reasons, XTSE3155, `XsltSettings.Streamability`, `Explain` | L | The 136 XTSE3430 tests and the 2 XTSE3155 tests pass. No regression among the 2,544, which still run unstreamed. The perf gate shows no cost for stylesheets that don't stream |
| S3 | **`StreamedDocument` and `StreamedNavigator`**, grounding (`copy-of`, `snapshot`), `xsl:source-document streamable="yes"`, and the optimizer restrictions and debug verifier. The runner's **strict streamed mode** (`XSL3_STREAM_STRICT=1`): a streamed input can't be built as a tree, so a guaranteed-streamable construct that falls back fails its test | L | In strict mode: `source-document`, `sf-*`, `sx-*` and the `si-*` sets other than fork, merge, iterate and for-each-group pass at the levels of the baseline |
| S4 | **Streamable modes**: the initial match selection streamed, the new API overloads, the legacy `Transform(XmlReader)` path, the global context item rules, and `xsl:attribute-set` and streamable functions in streamed templates | M | `si-apply-templates`, `si-next-match`, `si-apply-imports`, `su-*` and `streamable` in strict mode |
| S5 | **Streamed accumulators, `xsl:iterate`, `xsl:for-each-group` and `xsl:merge`** (section 3) | L | `accumulator`, `si-iterate`, `si-for-each-group`, `si-merge`, `merge` (the 9 XTDE2220 cases included) in strict mode |
| S6 | **`xsl:fork`**: the tee, the prong threads, output buffering, `group-by` and `xsl:sort` inside fork, and errors and cancellation across prongs | M | `si-fork` in strict mode; a test that a failing prong cancels the others; the tee's memory bound measured |
| S7 | **The constant-memory test, benchmarks and the claim** (below). Claim `streaming` in `Profile.cs`, make `supports-streaming` `"yes"`, cover the saved-assembly round trip, and write the docs: getting-started, migration, divergences and conformance | M | The P7 exit: the streaming tests at 90% or more **in strict mode**, the 2 GB test passing in CI |

S1 and S2 can start alone. S3 needs S2's annotations. S4 to S6 can run in parallel once S3 is in, since they touch
separate runtime helpers. S7 is last.

## The constant-memory test and benchmarks

- **Where it runs.** A new verb `stream-probe` in `tests/Xsl3.Tests/Program.cs`, run in a child process like the
  memory probe.
- **The limit.** The child runs with `DOTNET_GCHeapHardLimit=0xC800000` (200 MB). An out-of-memory error fails the
  test, and so does a peak working set over the limit.
- **The input.** A `Stream` generates the document as it is read, so no 2 GB file is written to disk. It is a
  repeating record structure with nesting, attributes, text and mixed content, sized by a byte count.
- **The stylesheets.** They are guaranteed streamable, one for each runtime path:
  - a streamed identity copy with a change;
  - `group-adjacent` totals;
  - an accumulator sum read at the end;
  - `xsl:iterate` with a running balance;
  - a merge of two generated streams;
  - `xsl:fork` writing two result documents;
  - `xsl:source-document` from inside a non-streamed transformation.
- **Checking output.** The output goes to a counting sink that hashes it. The hash is compared with an unstreamed
  run on a 10 MB document of the same shape, which the test also runs.
- **CI.** Each push runs a 200 MB document under a 20 MB limit, the same ratio. The nightly workflow
  (`perf-nightly.yml`) runs 2 GB under 200 MB (decision 6).
- **Benchmarks.** A `streaming` set joins the micro suite (`bench/corpus/xslt30/micro`) at 1 MB, 100 MB and 1 GB.
  It times streamed runs against building the tree and transforming it. The target for 1.x is a streamed run no
  slower than tree-then-transform at 100 MB, with time to first output byte independent of size. The SaxonCS-HE
  comparison doesn't apply, since HE doesn't stream.

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| A QIL rewrite or a runtime helper adds a second pass over a streamed operand, and the run fails at run time with `stream-position` | High | The restricted rule set in streamed subtrees, the debug verifier, strict mode over the whole suite, and the error rather than wrong output |
| A helper materialises silently: sequence caching, `item*` template results, `DocOrderDistinct`, `last()`. The output is right but memory grows | High | The constant-memory test for each runtime path. The strict mode can't see this; only the memory test can |
| The §19 rules are long, and a misread rule accepts a construct that can't stream, or refuses one that can | Medium | One method per rule, named after its section; `sandp` for expressions; the XTSE3430 tests for constructs; the strict mode for false positives |
| `XmlReader` details: attributes are only available at the start tag, entity expansion, `xml:base` and DTD-defaulted attributes | Medium | The reader chain is today's, unchanged; differential tests against unstreamed runs on the W3C sources |
| Fork threads: deadlock, exceptions across threads, `UseDedicatedThread` and stack size, deterministic error choice | Medium | The bounded tee lets the slowest prong always proceed; errors are taken in prong order; tests for each case |
| A pull navigator is slower than the compact tree for small documents | Medium | Small documents can still take the tree: §19.10 allows a tree when streaming conveys no benefit, so an input under a size threshold builds one. The analysis still runs |
| A stylesheet that runs today with `streamable="yes"` stops compiling (XTSE3430) in a 1.x minor release | Certain, if the default is `Strict` | Decision 3 |

## Owner decisions

**Approved by the owner, 2026-10-22: all six recommended choices below.** The execution model and packaging
decisions are recorded in SPEC §17 and §6.3. These are implementation requirements, not claims that the runtime
or verification gates are already complete.

1. **The execution model.** Use a pull-based streamed navigator over `XmlReader`, with push fan-out only for
   `xsl:fork`, as section 2 describes, instead of SPEC §17's push-based event pipeline. *Recommended: the navigator.
   It reuses all of the existing code generation; SPEC §17 is updated to match.*
2. **Packaging.** Keep streaming in the `Xsl3` assembly, as P6 did, or ship a separate `Xsl3.Streaming` package as
   SPEC §6.3 lists. *Recommended: in `Xsl3`; SPEC §6.3 drops the package.*
3. **The default for XTSE3430** (§19.10, the user's option).
   - `Strict` is what the spec's error describes, and what a streaming processor usually does.
   - `Fallback` keeps every stylesheet that compiles today compiling. 136 of the suite's tests compile today but
     would raise XTSE3430 under `Strict`.

   *Recommended: `Strict`. `streamable="yes"` is an explicit request, and running unstreamed defeats it without
   saying so. The release notes must name the change.*
4. **Threads for `xsl:fork`**, one per prong, against the 1.0 decision of no parallelism. The alternative is to
   build the fork's input as a tree, which is correct but not constant memory, and to record that as a divergence.
   *Recommended: threads. The spec suggests the model itself (§16.1), and they are for memory, not for speed.*
5. **The API for a streamed initial match selection**: the three `ApplyTemplates` overloads in section 4, and the
   legacy `Transform(XmlReader)` streaming for a streamable default mode. *Recommended: both.*
6. **CI budget**: the 2 GB test nightly, and a 200 MB test under a 20 MB limit on each push. *Recommended: as
   stated.*

## Progress

| Date | `XSL3_XSLT30_PROFILE=strm` | Strict streamed mode | `sandp` |
|---|---|---|---|
| 2026-10-22, plan (unstreamed, temporary profile) | 2,544 passed, 167 failed, 1 not run (93.8%) | not built | not run |
| 2026-10-22, S2 first increment (unstreamed) | 2,546 passed, 165 failed, 1 not run (93.9%); both XTSE3155 cases fixed | not built | 790 passed, 129 documented discrepancies, 0 not run |
| 2026-10-22, S2 compiler checks (unstreamed) | 2,681 passed, 30 failed, 1 not run (98.9%); 135 additional fixes, no new failures | not built | 790 passed, 129 documented discrepancies, 0 not run |

### S1 first increment

Implemented on `feature/p7-streaming-analysis`, 2026-10-22. This starts S1; it does not complete S1 or change the
six decisions above. No runtime streaming feature is claimed.

- `src/Xsl3/Streaming/Streamability.cs`: the 28 fundamental U-types (§19.1), postures, sweeps and analysis context.
  Atomic types reuse the existing XDM hierarchy; integers map to decimal and duration subtypes to duration.
- `ExpressionAnalysis.cs`: axis-step type inference (§19.1.1), the axis transition table (§19.8.8.9), paths
  (§19.8.8.8), predicate-free scanning paths (§19.8.10), simple maps (§19.8.8.7), literals and context items. Each
  assessment names its rule and is retained against its AST node. The existing parser and its compilation cache
  are reused. The stylesheet compiler does not call the analysis yet.
- `XSL3_XSLT30_PROFILE=sandp`: loads the analysis sets from their actual files under `tests/sandp` because their
  entries in the suite catalogue are commented out. Runs analysis before looking for a principal stylesheet,
  uses the catalogue's context type (default `item()`), and checks each XPath part independently. An unsupported
  form is reported as not run; the profile independently rejects lost analysis coverage. An empty analysis test fails.
- `XSL3_XSLT30_PROFILE=strm`: the 3.0 profile plus `streaming`, selecting only positive streaming dependencies.
  This is **unstreamed execution**, labelled as such in `xslt30-run-strm.md`, and checked against a separate
  `xslt30-run-strm.txt` baseline. The normal profile and its baseline are unchanged.
- CI runs `sandp` and unstreamed `strm` against their separate baselines; the conformance report includes both development profiles when
  their reports exist. A filter selecting no tests fails instead of producing an empty success report.

Run the profiles with:

```sh
XSL3_XSLT30_PROFILE=sandp dotnet test tests/Xsl3.W3C -c Release --filter FullyQualifiedName~Xslt30Conformance
XSL3_XSLT30_PROFILE=strm dotnet test tests/Xsl3.W3C -c Release --filter FullyQualifiedName~Xslt30Conformance
```

**Analysis result:** 790 passed, 129 failed, none not run (86.0%) out of 919 cases containing 66,643 expressions,
Release, .NET 10. Every expression is assessed before assertions; a case reports its first mismatch. The S1 ≥99% gate is **not met**. The checked-in baseline
keeps the failures visible; it does not make these tests conformant. The discrepancies are:

| Cases | Evidence |
|---:|---|
| 99 | Old attribute/namespace posture expectations. For example, `foo/attribute()` expects climbing/consuming, but the final Recommendation's §19.8.8.9 table makes the attribute step striding/motionless; §19.8.8.8 retains the left operand's consuming sweep. Likewise `attribute()/self::node()` is striding/motionless, rather than climbing/motionless. |
| 22 | Expectations involving statically empty selections. For example, `parent::foo/descendant-or-self::document-node()` expects roaming/free-ranging, but the second step cannot select a document node from an element, so §19.1.1 and §19.8.8.9 give grounded/motionless. `descendant-or-self::document-node()/attribute()` is also empty. |
| 8 | Generated cases have no `xpath` or `xslt` children. They fail rather than pass vacuously. |

Rules are taken from the [XSLT 3.0 Recommendation](https://www.w3.org/TR/xslt-30/#streamability-of-axis-steps),
not from test IDs or expected labels. No suite files are changed. Resolve these older test expectations upstream
or establish a reviewed suite correction before using the original 99% target as an exit gate.

**After the first increment, still in S1:** general operand usage and choice rules, predicates, bindings and variables, the remaining
expression forms and built-in functions, SequenceType/union mapping and `current()` context tracking. Unsupported
forms raise an explicit analysis gap. S2's declaration/instruction analysis, XTSE3430/XTSE3155, compiler/QIL
integration and settings, and S3–S7's streamed runtime and memory checks remain outstanding.

**Validation of this increment:** Release solution build; unit tests on .NET 10 (839) and .NET 8 (835);
upstream regression gate (1,654 per framework); differential comparisons (394 per framework); the normal W3C
baselines (QT3 21,884/21,884 and XSLT 3.0 8,844 passed, 23 known failures, 42 not run); the analysis baseline.
The permanent `strm` profile reproduces the planning baseline of 2,544 passed, 167 failed and one not run.

### S1 expression rules

Continued on the same branch, 2026-10-22. The remaining expression rules are implemented; this does not close
the original S1 exit gate, because the 129 suite discrepancies above remain. The `sandp` baseline is unchanged.

- **One general analysis** (§19.8.1): adjusted usages and sweeps, potentially consuming operands, choice groups,
  combined postures and higher-order operands. Arithmetic, comparisons, comma, conditionals, arrays, casts and
  type tests use it; union/intersect/except use their own rule.
- **Types** (§19.1): SequenceTypes map through the existing XDM resolver, including schema restrictions and
  flattened pure unions. Ancillary function/map/array signatures survive variable binding, type assertions,
  dynamic calls and transmission functions. Unbound variables and namespace prefixes retain their static errors.
- **Bindings and predicates** (§19.8.8): let/for/quantified scopes, variable shadowing, predicate focus,
  focus-independent numeric singleton detection, non-positional scanning predicates and absolute-path expansion.
- **Functions** (§19.8.8.11–18, §19.8.9): static and dynamic calls, arrows, partial applications, named references,
  inline functions and streamed-variable capture checks; all registered built-ins have operand metadata, checked
  against the canonical registry. Code generation and analysis share the existing compiled higher-order signatures.
  Variadic references retain a lazy parameter list, so a large arity does not allocate an arity-sized array.
- **Special rules** (§19.8.9.1–18): accumulators, `current()`, grouping/merge context, folds and function mapping,
  function lookup, innermost/outermost, position/last, reverse and root. `current()` keeps the outer expression's
  type and tracks higher-order boundaries. Speculative scanning-pattern checks do not overwrite actual-context
  assessments in the AST side table.
- **Host inputs for S2**: explicit namespace/type resolution, declared variables and streaming-parameter
  assessments, extension/stylesheet function signatures and usages, pattern context, group selection, accumulator
  phase and sequence-constructor ordering. Unknown function policy is an explicit analysis gap. No stylesheet
  declaration or extension is assumed streamable merely because a signature exists.

The short `fn:error` equivalences printed in XSLT §19.8.9 are malformed (they show impossible arities). The
analysis uses the supplied-argument prefix of the three-argument roles; Xsl3's runtime does not retain streamed
nodes in an omitted error object. This is consistent with the implementation-dependent error object allowed by
[F&O 3.1 §2.3.1](https://www.w3.org/TR/xpath-functions-31/#func-error).

**Scope boundary:** the stylesheet compiler still does not invoke the analysis. S2 must supply its declaration
context, enforce function categories and declared-streamable constructs, attach QIL annotations, and implement the
settings and XTSE3430/XTSE3155 diagnostics. Streamed execution remains S3–S7. The analysis suite's original ≥99%
exit gate remains open pending resolution of the documented older test expectations and empty cases.

**Validation:** Release solution build; 165 focused analysis tests on each framework (including a metadata
coverage check over every registered built-in); full unit suites, 943 on .NET 10 and 939 on .NET 8; upstream
regression gate, 1,654 per framework; differential comparisons, 394 per framework; normal W3C suites against
unchanged baselines (QT3 21,884 passed and none failed; XSLT 3.0 8,844 passed and 23 known failures); 18 analysis
profile/harness checks. The 919-case analysis profile remains 790 passed and 129 documented failures.

### S1 code review and corrections

Reviewed the branch's analysis, shared signature changes, harness, development profiles and documentation on
2026-10-22. Added 19 regression cases, including failures reproduced before their fixes.

| Finding | Correction |
|---|---|
| Arrow calls escaped positional and focus-dependency checks, allowing unsafe scanning/singleton verdicts. | Inspect the expanded arrow function name and its effective arity in both checks. Scanning also requires a striding or crawling context; it cannot erase a free-ranging climbing/roaming context. |
| Nested `root()` calls reanalysed their operands exponentially (255 visits for eight nested calls). | Reuse the assessed operand and assess only the synthetic ancestor step; eight nested calls now visit eight function operands. |
| Grounded/roaming/empty axis steps, some partial applications and `current-merge-group()` skipped supplied expressions. | Assess predicates and supplied arguments before applying the construct's verdict, retaining static errors and AST assessments. |
| Known empty map/array members and callback results became `item()*`. Castable targets were never resolved. | Preserve the sequence type separately from its nullable item type, and resolve castable targets through the canonical resolver. |
| Host policies could change between repeated resolver calls; malformed signatures/roles could escape validation. Shared built-in roles were mutable. | Cache and validate host policies/signatures per analysis, snapshot host collections and expose read-only built-in roles. |
| Assertion negation could make malformed or unsupported analysis pass; namespaces were ignored and assertions repeated analysis. | Assess the complete input once before assertions, use each expression's in-scope namespaces, and propagate unsupported outcomes through assertion alternatives/negation. |
| An ID-only failure baseline could hide a failing case becoming not run; missing suites could pass without coverage. | Require zero not-run cases independently of the `sandp` baseline, and require the suite checkout for explicitly requested development profiles. Both development baselines now run in CI. |
| The package README contradicted its schema-awareness support and carried obsolete conformance counts. | Align the support statement and counts with the current verified profiles. |

**Validation:** Release solution build; 180 focused analysis cases per framework; full units, 958 on .NET 10
and 954 on .NET 8; upstream regressions, 1,654 per framework; differential comparisons, 394 per framework;
33 normal W3C harness tests with `XSL3_W3C_SAVED=10` (including saved-assembly round trips); 22 analysis
profile/harness tests. QT3 remains 21,884 passed, zero failed, 99 not run. XSLT 3.0 remains 8,844 passed,
23 known failures, 42 not run. No baseline was rewritten to conceal a finding. The analysis profile remains
790 passed, 129 documented suite discrepancies and zero not run; its original exit gate remains open.
The unstreamed `strm` baseline also remains unchanged: 2,544 passed, 167 known failures, one not run.
The build's existing upstream nullable/rethrow warnings and unit collection-assertion warning are outside these
branch changes; no new compiler warnings were introduced by the analysis corrections.

### S2 construct analysis: first increment

Continued on the same branch, 2026-10-22. The owner approved all six design recommendations; S0 is complete.
This starts the S2 construct pass. It does not close S1's suite gate, complete S2, or claim streamed execution.

- The XSL AST now retains function streamability categories, per-declaration attribute-set flags, accumulator
  and merge-source flags, the original merge source expression, source-document href and fork identity.
  Source syntax for `xsl:copy select`, `xsl:evaluate` and enabled `xsl:assert` survives their existing lowering.
  Their source operand roles differ from those of the generated temporary bindings and calls.
- The loader raises **XTSE3155**, with the function's source location, when a zero-parameter function declares
  any category other than `unclassified`. Unknown extension categories remain unclassified for analysis, as
  §19.8.5 requires; their lexical EQNames are validated.
- `ConstructAnalysis` and its control-flow partial operate on the XSL AST, using the existing compiler scope
  manager for namespaces and variable bindings and S1's shared general rules for operands. Covered combinations
  include sequence constructors, value templates, constructors, choices, variable initializers, template calls,
  apply-templates, sorting, grouping, iterate and next-iteration, analyze-string, try/catch, maps, fork branches,
  numbering, source documents, evaluate, assertions and result documents. Declaration checks remain separate work.
- `FunctionStreamability` describes all seven categories, their streaming parameters, body/signature constraints
  and call rules. The construct hook resolves actual loaded stylesheet function declarations, with package
  component resolution, and delegates their argument analysis to these policies. Node return-type conversion
  preserves the result's posture; atomic return types can ground it through atomization.
- **Published-text ambiguity:** the `ascent` call rule follows the correction proposed in W3C's
  [XT30.E31 / Bug 30289](https://github.com/w3c/qtspecs/blob/6cef5545618cd2edfd94f32af488a16bb19c598c/errata/xslt-30/errata.xml),
  using transmission for the first argument and grounding a node-free declared return type. This erratum is a
  draft proposal, not a normative amendment. The original inspection wording loses the streamed-node posture.
  The result-type conversion interpretation must also be checked with the declaration-level suite before S2 exits.
- Parsed XPath and value-template ASTs are retained on their XSL nodes. QIL generation can reuse them for
  expressions, patterns and value templates, supplying its own static context. Reassessment keeps the same
  syntax object while changing the context posture; a regression test passes those assessed nodes into QIL generation.

**Measured progress:** the unstreamed development profile is 2,546 passed, 165 failed and one not run.
Only the fixed IDs `error/error-3155a` and `su-shallow-descent/su-shallow-descent-901` were removed from its failure
baseline. The original 2,544 passing cases remain passing. The analysis baseline remains unchanged.

**Still required for S2:** invoke the construct pass for declared-streamable components and reachable code;
finish declaration-level checks for templates, functions, attribute sets, accumulators, source documents and merge
sources; integrate strict/fallback settings, XTSE3430 reasons, QIL entry annotations and Explain output; check the
remaining edge cases against the streaming error suite and the performance gate. The ordinary stylesheet compiler
does not invoke the construct analyser yet. S3–S7, including streamed execution and the memory gates, remain required.

**Validation:** Release solution build; 281 focused analysis/declaration cases per framework (101 added in this
increment); full units, 1,059 on .NET 10 and 1,055 on .NET 8; upstream regressions, 1,654 per framework;
differential comparisons, 394 per framework; 33 normal W3C harness tests with saved-assembly sampling enabled;
22 analysis profile/harness tests and the updated unstreamed development baseline. QT3 remains 21,884 passed,
zero failed, 99 not run; normal XSLT 3.0 remains 8,844 passed, 23 known failures, 42 not run. NativeAOT publish
and execution passed on macOS arm64 without new trim/AOT warnings. The three-round performance/allocation gate
against `0f2ddcb` passed: time geometric mean 0.981 over 39 rows; largest reported allocation ratio 1.007.


### S2 compiler enforcement

**Implemented, 2026-10-22.** Compiler analysis now runs after package binding and before XSL AST rewriting,
only when the loader has seen a streaming declaration. It validates streamable template rules (including match
patterns and parameter defaults), function signatures/bodies, merged attribute sets, accumulators, source
bodies and merge selections. XTSE0730 checks attribute-set references even in fallback mode. A named entry's
compilation unit checks its reachable named templates; unused named entries are checked when requested by
the 3.0 API. Ordinary XPath compilation still checks their other static errors.

- `XsltSettings.Streamability` defaults to `XsltStreamabilityMode.Strict`; failed checks report XTSE3430 with
  posture, sweep, rule and declaration location. `Fallback` preserves tree execution. Settings are cloned with
  the executable, and undefined enum values are rejected. These controls do not enable streamed execution.
- Verdicts are kept in a compiler side table, on QIL entry functions/source instructions, and at the QIL root so
  optimized `Explain` retains declarations removed as unreachable. Parameter annotations keep their existing
  binding meaning. Parsed expression/value-template syntax is reused by QIL compilation.
- Review fixes cover package-visible global bindings and template/attribute-set/mode references; message
  termination AVTs (the original AST argument is a boolean); pattern `current()` using the matched type;
  accumulator return-type adjustment; literal attributes not counting as preceding sibling instructions;
  consuming ancestor siblings for `accumulator-after()`; and captured streaming parameters in repeated operands.
  Invalid fork child combinations now report XTSE0010 rather than reaching a single-child assertion.
- Compiler analysis accepts sound rewrites: predicate-free `E//child::N` to `E/descendant::N`; a leading dot as
  `self::node()` for scanning paths; unions of children of the same peer selection; removing the current item
  from a peer selection; motionless, focus-independent quantified predicates; a forward copy/select loop with
  a grounded body and no streaming-parameter capture; and a fork with one consuming streamed result and only
  grounded motionless siblings. Grounded groups remain available after focus changes. The analysis-only
  `sandp` profile keeps the source syntax rules; these optimizations are not alterations to its expected labels.

**Measured profile:** 2,681 passed, 30 failed and one not run, still **UNSTREAMED**. No new failures were added
relative to the prior profile. Only the 135 verified fixed IDs were removed from its failure baseline.
Two expected XTSE3430 cases remain: `si-fork/si-fork-953` sorts groups inside a fork, permitted by the published
§19.8.4.19 rules; `sx-UnionExpr/sx-union-202` unites child selections sharing a parent, accepted by the peer-union
rewrite. These cases need a documented suite/spec resolution before claiming S2's exact error-suite exit.
The other runtime/schema/result failures remain visible in the baseline.

**Validation:** 318 focused analysis/declaration cases per framework; full units, 1,096 on .NET 10 and
1,092 on .NET 8; upstream regressions, 1,654 per framework; differential comparisons, 394 per framework;
33 normal W3C harness tests with saved-assembly sampling; 22 analysis profile/harness tests; and the updated
streaming development baseline. Normal QT3 remains 21,884 passed, zero failed and 99 not run; normal XSLT 3.0
remains 8,844 passed, 23 known failures and 42 not run. The streaming profile's six mismatched expected error
codes are runtime validation/URI/map errors; none is an XTSE3430 mismatch. NativeAOT publish and execution
passed on macOS arm64 without new trim/AOT warnings. The three-round gate against `0f2ddcb` passed: time
geometric mean 0.989 over 39 rows; maximum reported allocation ratio 1.007. The final package-entry and XML
comment-formatting checks add focused regression coverage and do not change the ordinary stylesheet path.

**Remaining for S2:** residual declaration/higher-order review and the two suite expectations before claiming
the exact exit. Package override contracts are implemented in the following increment. S1's 99% suite gate and
S3–S7 remain open.


### S2 package compatibility and S3 forward navigator foundation

**Implemented, 2026-10-22.** Package compatibility now includes the streaming contracts: a streamable attribute
set cannot be replaced by a non-streamable one (§3.5.3.3), and overriding functions must retain their category
(§19.8.5). Incompatible overrides report XTSE3070, including in fallback mode. Extension categories are retained
as expanded names, so two prefixes for one URI match, while different namespaces do not.

The first S3 runtime increment is a shared `XmlReader` document and an `XPathNavigator` over it. It retains the
open ancestor stack and one current node, with node numbers, depth, attributes, namespace declarations, base URI,
language and schema metadata. Clones share the reader; ancestor/attribute/namespace inspection does not advance
it. Child/sibling traversal and element/document string values advance it. Attempts to revisit consumed content
raise `Q{http://xsl3/err}stream-position`, rather than returning missing or incorrect results.

- Adjacent text, CDATA and expanded entities are coalesced. Schema whitespace and `xml:id` normalization reuse
  the existing schema/lexical rules. Positioned readers retain the namespace context of their input scope.
- Grounding uses the existing compact builder in small mode over `ReadSubtree()`. The builder now accepts root
  namespace context, preserving inherited bindings that are not physical attributes. A header already read by
  `HasChildren` can be replayed through the navigator reader without a second input pass. Reader type annotations
  survive grounding. Copied documents keep their base URI and clear their document URI, as ordinary copies do.
  Grounding keeps the next sibling available for forward traversal.
- Node identity and runtime document indexing recognize the new navigator. Existing `copy-of()` uses its direct
  grounding path. Tests run compiled `count`, `sum`, per-record `copy-of()` and `snapshot()` through the public
  transformer API using an internally supplied forward navigator; these consume the actual reader.
- Reader ownership and cancellation are explicit. Malformed XML propagates an error. Traversals over 100 and
  10,000 records retain at most four cursor frames in the tested shape; this is an internal frame bound, not a
  process-memory or S7 exit result.

**Still required for S3:** wire streamed source-document entry points and their resource scopes; apply the same
whitespace/validation reader chain as ordinary input; restrict optimizer rewrites and add the verifier; fuse
materializing operand helpers where required; and run the strict streamed development profile. The existing
accumulator-copy registry retains copied result trees and must be changed before a constant-memory claim.
Typed snapshots and streamed accumulator values also need their S3/S5 integration. S4–S7 remain required.
Ordinary entry points still build trees, and the normal profile still reports no streaming support.


**Validation for this increment:** 12 new package-override cases and 30 forward-navigator cases per framework;
full units, 1,138 on .NET 10 and 1,134 on .NET 8; upstream regressions, 1,654 per framework; differential
comparisons, 394 per framework; 33 normal W3C tests with saved-assembly sampling; 22 analysis profile/harness
tests; and the unchanged unstreamed streaming baseline (2,681 passed, 30 failed, one not run). NativeAOT publish
and execution passed on macOS arm64 without new trim/AOT warnings. The three-round performance/allocation gate
against `0f2ddcb` passed: time geometric mean 1.003 over 39 rows, maximum reported allocation ratio 1.007.

### S3 source-document entry integration

**Implemented in development, 2026-10-22.** `XSL3_STREAM_STRICT=1` now compiles guaranteed-streamable
`xsl:source-document streamable="yes"` instructions to forward inputs. The normal execution path remains
unchanged. A per-compiler internal override lets tests select the development path without changing process
environment variables.

- Source inputs are opened through the configured document resolver without entering the tree cache. Streams
  opened by the transformation close at source exit, on parsing/body/output errors and on cancellation. A
  source unwound by an enclosing `xsl:try` closes before its catch runs. Resolver-supplied readers retain the
  existing caller-owned policy. Nested sources have independent readers.
- The reader chain reuses whitespace rules and DTD settings. Skipping goes through that chain and checks
  cancellation instead of bypassing whitespace-reader state. DTD element-content whitespace matches ordinary
  input. Explicit annotation stripping/preservation and `input-type-annotations` are applied to already-typed
  readers; a known streamed type is visible at its start tag, before final validity is established. Grounded
  copies are marked as constructed trees.
- Document access settings and read/write URI conflicts remain enforced. A source with no applicable
  accumulators reports XTDE3362 on accumulator access. Requested streamed validation or accumulator observers
  currently raise `Q{http://xsl3/err}streaming-unimplemented`; they do not silently build a tree.
- QIL containing forward sources uses a conservative optimizer policy: required construction analysis and
  forward-axis/loop ordering facts remain, with redundant document ordering removed. Rewrites that inline,
  hoist or duplicate expressions are disabled. Runtime document-order IDs use weak document keys, so IDs stay
  stable for live documents without retaining closed sources.

**Measured limits:** the strict source-entry probe runs 47 applicable `source-document` cases: 34 pass and
13 fail, with three expected-error code mismatches. The same subset in unstreamed development is 45 passed,
two failed. Strict probes have a separate `xslt30-run-strm-strict.md` report and retain their failures; this is
not the S3 exit. Other streaming entry points still need integration. The debug verifier, forward validation,
fused operand helpers, weak accumulator-copy provenance, typed snapshots and streamed observers remain open.
The complete strict-profile gate and memory gates have not passed. S4–S7 remain required; the normal profile
continues to report no streaming support.

**Validation:** 23 source-document cases and 31 forward-navigator cases per framework (24 new cases in this
increment); full units: 1,162 on .NET 10 and 1,158 on .NET 8. The ordinary upstream and differential suites pass
1,654 and 394 cases respectively per framework. The normal W3C harness passes 33 tests with saved-assembly
sampling; the unstreamed streaming profile and harness pass 22 tests with the unchanged 2,681/30/1 baseline.
NativeAOT publish and execution pass on macOS arm64 without new trim/AOT warnings. The three-round gate against
`0f2ddcb` passes: time geometric mean 1.000 over 39 rows and maximum reported allocation ratio 1.009. The final
optimizer policy also excludes index construction over forward inputs. An additional source-error regression
verifies that a provider's close failure cannot mask its read error; full unit suites pass after these fixes.
These measurements concern ordinary stylesheet paths, not streaming throughput or process-memory budgets.

### S3 fused operands and copy lifetimes

**Implemented in development, 2026-10-22.** Forward compilation now evaluates the operand of `copy-of()`,
`snapshot()` and the copying instruction helpers one item at a time. A selected node is copied before the
cursor advances. `data()` also atomizes each item before advancing; paths with statically atomic results
avoid the mixed node/atomic result collector. Simple `A//child::N` paths lower directly to `A/descendant::N`.
The shortcut excludes predicates, preserving their sibling focus, and forward compilation disables automatic
document indexes.

The copy provenance registry now uses weak document keys. Live copies retain their original position and
validation applicability, including through navigator clones. Unreferenced copies and their origins can be
collected; the registry no longer holds every emitted copy until the transformation ends. Garbage-collection
regressions check both sides of this contract. This is not a process-memory-budget result.

Schema-aware snapshots reuse the typed compact builder: selected subtrees and attributes retain their types,
while copied ancestors have `xs:anyType`, with `nilled` and ID/IDREF flags cleared as §20.3.2 requires. Attribute
and namespace snapshots copy their parents shallowly, retaining the other attributes and namespaces without
copying parent children. Snapshot roots retain the original ancestor root's base URI. The ordinary W3C
`snapshot-0102a` now passes against the specification's equivalent implementation. Only that verified fixed ID
was removed from the normal baseline; no new failures were added. The generated per-set report was refreshed.

**Current limits:** the strict source-entry subset is now 36 passed, 11 failed, with three expected-error code
mismatches (previously 34/13). Source cases `stream-205` and `stream-500` now pass. Post-consumption descendant
traversal, predicate paths, further operand fusion (including indirect function calls), copy flag combinations,
forward validation, the debug verifier and other entry points still need work. An overlapping-selection guard
requires complete output or an explicit `stream-position` error; it does not count an unsupported case as a
streaming success. S3's full strict gate, S1/S2's remaining gates and S4–S7 remain open.

**Validation:** 23 new unit cases per framework, including the overlapping-selection guard and parentless
namespace snapshots; 1,185 units on .NET 10 and 1,181 on .NET 8; upstream 1,654 and differential 394 per
framework. The normal W3C harness passes
33 tests with saved-assembly sampling: QT3 is unchanged at 21,884/0/99; XSLT 3.0 is 8,845 passed, 22 failed,
42 not run (99.8%). Both development-profile/harness runs pass 22 tests each; their baselines remain 790/129
for analysis and 2,681/30/1 for unstreamed execution. NativeAOT publish and execution pass on macOS arm64 without
new trim/AOT warnings. The three-round ordinary-path gate against `0f2ddcb` passes: time geometric mean 1.043
over 39 rows, maximum allocation ratio 1.009. No streaming memory or throughput exit is claimed.

### S3 forward source validation

**Implemented in development, 2026-10-22.** Guaranteed-streamable source-document instructions with
`validation="strict"`, `validation="lax"` or `type` now use schema validation in the forward reader chain.
Imported schemas travel with compiled and saved stylesheets. The partial-type validating-reader constructor
is isolated in `SchemaReaderBridge`; .NET 8, .NET 10 and NativeAOT execution have been checked. An unavailable
runtime constructor produces an explicit unsupported-streaming error.

- Start-tag type information, default attributes, default element content and typed values are available through
  the forward navigator. Explicit source validation overrides annotation stripping. Input schema-location hints
  cannot fetch additional schemas. Lax assessment below undeclared roots uses `xs:anyType` and recursively
  assesses children and attributes, as §25.4.1.1 requires.
- Source exit finishes requested validation, including when the body does not consume the input. Validation
  errors bypass catches and message recovery inside that source; a surrounding try can catch them after source
  cleanup. Caller-owned readers remain open, and already-positioned readers validate only the supplied subtree.
- Function argument conversion atomizes selected nodes before the cursor advances. This covers library calls
  such as `string-length()` over a streamed element, without retaining stale selected nodes.
- Shared tree validation now recursively assesses undeclared lax roots. `xs:untypedAtomic` annotations map to
  the canonical atomic type. Duplicate schema keys use the element-validation error; only additional document-wide
  ID/IDREF constraints use `XTTE1555`. Strict validation of an undeclared root raises `XTTE1512`, including when
  an `xsi:type` attribute is present.

**Current limits:** lax validation of an undeclared streamed root carrying `xsi:type` or `xsi:nil` still raises
an explicit `streaming-unimplemented` error. Streamed accumulator observers, other entry points, remaining
operand fusion and traversal cases, the debug verifier and the full strict/memory gates remain open. Validation
can retain schema identity-constraint state; the passing functional checks do not establish a memory budget.
The normal profile continues to report no streaming support. S1/S2 gates and S4–S7 remain open.

**Validation:** 38 new unit cases per framework; full units pass 1,223 on .NET 10 and 1,219 on .NET 8.
Upstream and differential checks pass 1,654 and 394 cases per framework. The schema-aware strict source subset
passes all 12 cases, including saved-assembly round trips, with no wrong error codes. These cases are selected
separately because their explicit schema-awareness dependencies replace the suite's inherited streaming
dependency. The basic strict streaming source subset remains 36 passed and 11 failed; these functional probes
do not constitute the S3 exit. A dedicated NativeAOT probe exercises both lax and stipulated-type forward
validation with dynamic code disabled; the ordinary NativeAOT sample also publishes and runs successfully.

The normal W3C harness passes 33 tests against the tracked baseline: QT3 remains 21,884/0/99, and XSLT remains
8,845/22/42. The normal XSLT report records 34 error-code mismatches, compared with 32 on unchanged `HEAD`.
The two additional IDs, `attribute-1506` and `attribute-1507`, have undeclared roots under strict validation;
they now report the required `XTTE1512` before reaching the ID errors expected by those cases. No new baseline
IDs were added. Running every applicable XSLT case through a saved assembly gives 8,832/35/42 versus
8,831/36/42 on unchanged `HEAD`: all 13 failures beyond the ordinary baseline also occur on `HEAD`, and the
only removed failure is the previously verified snapshot fix. This exhaustive diagnostic is not a passing
saved-assembly gate. Analysis and unstreamed development-profile/harness runs each pass 22 tests against their
unchanged baselines (790/129 and 2,681/30/1 respectively).

The three-round ordinary-path performance/allocation gate against `0f2ddcb` passes: time geometric mean 0.982
over 39 rows and maximum reported allocation ratio 1.009. Individual compile timings vary; this gate does not
measure streaming throughput or establish the streaming memory budget.
