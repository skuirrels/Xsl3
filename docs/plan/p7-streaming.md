# P7: streaming

> **2026-10-26 release boundary:** P7A closes the implemented foundations for the 1.1.0 release.
> Full P7 is deferred, streaming remains unsupported, and the original targets below are unchanged.
> Use the [phase close-out](p7a-closeout.md) for current scope, evidence and release steps; historical progress
> entries below do not expand this release. Release version: 1.1.0.
>
> **Next bounded phase:** [P7B analysis close-out](p7b-analysis-closeout.md) has six tickets and exact
> S1/S2 case inventories. It does not resume the full runtime or performance workstream.

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
| `xsl:merge` (§15.4) | Every item of every input in one list | **A k-way merge with one row of lookahead per input sequence.** Each streamed input has its own reader. Every selected row is snapshotted before its keys are evaluated (§15.4), so keys may navigate the copied subtree and ancestors. `current-merge-group()` retains the current equal-key run in source/input/item order. XTDE2220 is raised when an input goes out of order |
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

### S3 optimizer verification and descendant cursors

**Implemented in development, 2026-10-22.** For graphs with forward sources, Debug builds capture QIL before optimization and verify the
conservative optimizer's contract afterward. Read-capable operations, calls and buffering operators must remain
in order without insertion or duplication. Their function, binding, loop and branch scopes must be preserved;
changing a `for` binding into a cache or changing a read operation's kind is rejected. Nine mutation tests cover
these checks. Elimination is allowed. This verifies preservation by the optimizer; source analysis and runtime
position checks still establish separate parts of the single-pass contract. Release compilation does not run
the verifier.

Forward descendant iterators now use an ancestor boundary instead of seeking to a following sibling before
their scan starts. They can resume after a selected subtree is atomized, copied or snapshotted. Active scans
observe absorption and keep one bit recording whether a matching nested node was lost. Such overlapping
absorption still raises `stream-position` until replay is implemented. Non-matching nested elements do not
prevent continuation. Observer references are weak and pruned, so an abandoned cursor is not retained by the
source. Grounding reads through the navigator to preserve these observations and schema information.

QName and node-kind filter annotations are enabled in the conservative optimizer so the cursor observes the
actual selection test. `A//child::N[P]` can use a direct descendant step when compiling `P` does not access its
outer focus position or size. Numeric and positional predicates keep their original sibling focus; nested
focuses remain separate. `last()` and its named function reference recognize the function namespace through
aliases. Debug conformance also exposed shared QIL literal nodes in automatic indexing and tracing; both now
create independent literal nodes at each use.

**Measured progress:** the strict streaming source subset is 40 passed and seven failed (previously 36/11),
with three wrong expected error codes. `stream-107`, `stream-108`, `stream-109` and `stream-207` now pass.
Release and Debug runs, including saved-assembly round trips, have identical results. The separate schema-aware
strict subset remains 12/0/0 with matching error codes and saved-assembly coverage. The seven remaining basic
source failures are two old lax-type expectations, four accumulator-observer gaps and the template traversal
case `stream-211`. This does not complete S3 or the full strict runtime gate.

**Validation:** 27 additional unit cases per framework; full Release units pass 1,250 on .NET 10 and 1,246 on
.NET 8. Debug checks covering forward sources, the verifier, automatic indexing and the public API/tracing pass
166 and 162 cases respectively. The repository's upstream CI gate (`category!=failing`) passes 1,654 per
framework; differential checks pass 394 per framework. A NativeAOT probe checks validation and bounded
descendant grounding with dynamic code disabled; the official NativeAOT sample also publishes and runs.

An additional 1-in-17 saved-assembly diagnostic reports 8,843/24/42, against 8,842/25/42 on `0f2ddcb`.
`override-v-008` and `validation-1601` are beyond the ordinary baseline on both revisions. No new failure IDs
were added, and the only removed failure is the earlier snapshot fix. These saved-only issues remain recorded;
this diagnostic is not a passing gate.

The exact W3C CI setting (`XSL3_W3C_SAVED=10`) passes all 33 harness tests: QT3 remains 21,884/0/99 and XSLT
remains 8,845/22/42, with 34 wrong expected error codes. Both development-profile/harness runs pass 22 tests
against their unchanged analysis and unstreamed baselines. The generated per-set conformance report is refreshed.

The three-round ordinary-path performance/allocation gate against `0f2ddcb` passes: time geometric mean 1.004
over 39 rows and maximum reported allocation ratio 1.009. This does not establish streaming throughput or
the process-memory budget.

**Still required:** overlapping-selection replay, further predicate and operand paths, copy flag combinations,
the undeclared-lax-root instance-attribute case, other entry points and the full strict/memory gates. Streamed
accumulator observers and S4–S7 remain open, as do the recorded S1/S2 exit discrepancies. The normal profile
continues to report no streaming support.

### S3 copy options and lax root views

**Implemented in development, 2026-10-22.** Undeclared lax streamed roots now support `xsi:type` and `xsi:nil`.
As §25.4.1.1 requires, those attributes do not cause the undeclared root itself to be assessed. A bounded view
of its header excludes them from the validating reader's element assessment, then exposes the original attributes
to consumers. Other attributes keep their validation information; descendants receive normal assessment,
including their own instance attributes. The adapter preserves namespace resolution, attribute navigation,
typed attribute content, defaults, ownership and line information. It retains a header rather than a subtree.
The separation follows the [validating reader's element and attribute processing](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.Xml/src/System/Xml/Core/XsdValidatingReader.cs#L1891-L2010).

Instruction copies now apply `copy-namespaces`, `validation` and `copy-accumulators` independently. Previously,
requesting accumulator copies bypassed the other options. Forward copies use one type-aware small-tree builder
path, with an absorption scope for descendant cursors. Explicit validation is performed on each grounded item
before advancing the input, instead of collecting the selection's copies first. Namespace-sensitive values
still raise `XTTE0950` when annotations are preserved but their namespaces are excluded.

Annotation preservation alone no longer associates an instruction copy with its source's accumulators.
`copy-accumulators="no"` creates a temporary tree with its own accumulator applicability; `yes` retains the
source association. The `copy-of()` function shares the canonical copy path and keeps schema annotations when
compiled schema-aware. Copies have no document URI, while keeping their base URI. Attribute ID/IDREF properties
survive annotation stripping, as §25.4.1.1 requires. The W3C run caught a regression in these properties during
implementation; it was corrected and `copy-5034` passes again. No failure was added to a baseline.

**Validation:** 33 additional unit cases per framework; full Release units pass 1,283 on .NET 10 and 1,279 on
.NET 8. The Debug forward/verifier/API/indexing set passes 199 and 195 cases respectively. The option matrix
compares ordinary and forward execution for preservation, stripping and default validation, namespace copying
and accumulator copying. Further cases check namespace-sensitive content, copied accumulator applicability,
ID/IDREF properties, document URI, first-item validation ordering and typed root-attribute reading.

The exact W3C CI run (`XSL3_W3C_SAVED=10`) passes 33 harness tests, with unchanged QT3 21,884/0/99 and XSLT
8,845/22/42 results and 34 wrong expected XSLT error codes. Upstream and differential gates pass 1,654 and 394
per framework. Analysis and unstreamed development-profile/harness runs pass 22 tests each against unchanged
baselines. Strict source subsets with saved assemblies remain 40/7/0 for basic streaming and 12/0/0 for
schema-aware sources. The generated per-set report is refreshed.

NativeAOT probes exercise the lax-root adapter, schema validation and a saved stylesheet that performs actual
forward copies with accumulator copying enabled and namespaces excluded. They run with dynamic code disabled.
These functional results do not establish a streaming memory budget or complete S3. Overlapping-selection
replay, remaining operand/predicate and identity-metadata paths, other entry points, streamed observers and the
full strict/memory gates remain open. S1/S2 discrepancies and S4–S7 remain required; streaming is not claimed.

The first ordinary-path performance run failed at 1.073. An unchanged-binary three-pair control also failed at
1.063, exposing a repeatability problem in the harness: its three-call warmup and 10,000-sample cap could leave
short transforms measured before the requested duration. Warmup and measurement now each use the requested
duration, retaining their minimum call counts. The corrected identical-binary control passes at 1.007 with
maximum allocation ratio 1.000. Inputs, aggregation and acceptance thresholds are unchanged; both revisions
use the same harness. The original failed results are retained as diagnostic evidence.

With the corrected harness on both sides, the three-round gate against `0f2ddcb` passes: time geometric mean
1.005 over 39 rows and maximum reported allocation ratio 1.009. These are ordinary-path measurements;
streaming throughput and process-memory exits remain unverified.

### S4 initial-mode entry points

**Implemented in development, 2026-10-22.** `Xslt30Transformer.ApplyTemplates` now accepts readers, streams
(with an optional base URI), and URIs, with raw-result and all three serialized-destination overloads.
The selected mode receives a forward input when forward execution is enabled, it is declared streamable,
and the compilation has guaranteed-streamability verdicts. Other modes and explicit fallback compilations
with non-guaranteed verdicts build an ordinary tree. `XdmValue` inputs remain grounded. The existing `XslCompiledTransform.Transform(XmlReader, …)`
path also streams its selected initial mode. Normal execution and the `supports-streaming` claim remain
unchanged until the remaining gates are met.

The initial-source descriptor carries the input independently of `GlobalContextItem`. Opening is deferred
until execution, after required-global-context and type checks. The global context remains grounded; a
streamed legacy-reader entry has no implicit global context. Reader and stream arguments remain open,
including positioned readers restricted to their current subtree. URI inputs use the configured resolver
and close acquired streams or readers on success and failure. The primary input does not require
`EnableDocumentFunction`. Whitespace rules, base URIs, annotation stripping, cancellation, and read/write
URI conflicts use the existing runtime paths. XSLT 1.0 retains its ordinary tree behavior.

Saved static-data format minor version 4 persists the selected mode's forward-input flag; earlier formats
default to ordinary input. Initial modes also activate the single-pass optimizer policy. The implementation
review and execution tests found and corrected these related issues:

- `apply-templates` no longer caches an entire forward selection before dispatch; templates run while each
  selected node is still available.
- Wildcard attribute filters retain their attribute-only iterator annotation. Namespace/name filters are
  layered above it, preventing header inspection from consuming children.
- Whitespace stripping preserves the absent-input marker. Optional global context items are checked against
  their declared type when present, and global prerequisites retain push serialization of node results.
- Initial-mode accumulator applicability follows documents in the initial selection, including explicit XDM
  selections with a different global context. Independent global-context documents retain ordinary applicability.
  Requested streamed observers still report the explicit S5 gap before acquiring the input.

The strict W3C harness now retains each source's `streaming` attribute and supplies actual reader inputs for
streamed initial selections, including saved stylesheet runs and XML assertion reruns. It does not first load
them into the global context tree. A harness regression uses an unclosed initial source with a required global
context: `XTDE3086` must occur before the source is parsed. The report is labelled **STRICT FORWARD INPUTS**;
its results cover both source-document instructions and initial-mode inputs, so they are not interchangeable
with the earlier source-entry-only measurements.

**Validation:** 52 new unit cases on .NET 10 and 51 on .NET 8; full Release units pass 1,335 and 1,330.
The Debug forward/verifier/API/index/accumulator selection passes 258 and 253 cases respectively. Coverage
includes all 12 overloads, incremental output, ownership and positioned inputs, named modes and parameters,
separate/absent/typed global context, saved assemblies, accumulator applicability, built-in rules, recursive
copies, wildcard attributes, streamable functions and attribute sets. Upstream and differential gates pass
1,654 and 394 cases per framework. The exact W3C CI sampling run passes 34 harness tests with unchanged
QT3 21,884/0/99 and XSLT 8,845/22/42 results (34 wrong expected XSLT error codes). A NativeAOT consumer runs a
saved initial-mode stylesheet with dynamic code disabled, produces output before EOF, keeps its caller reader
open, and publishes through the annotated `Load(Type, XsltSettings)` overload without trim/AOT warnings.

**S4 is not complete.** Its strict subset (`si-apply-templates`, `si-next-match`, `si-apply-imports`, `su-*`,
`streamable`, with saved-assembly sampling) is 138 passed, 93 failed, none not run, with two wrong expected error
codes. `si-apply-templates` passes all 11 cases; outermost/overlapping-selection traversal, operand/predicate
helpers and other runtime paths still prevent the full exit gate. S3's remaining paths, exact S1/S2 errors,
S5–S7 and the retained-memory gates remain required. No strict failure has been added to a baseline, and no
streaming conformance claim is made.

The three-pair ordinary-path performance gate against `0f2ddcb` passes: time geometric mean **1.006** over
39 rows, maximum allocation ratio **1.026**. Analysis and unstreamed development-profile/harness runs pass
23 tests each against unchanged 790/129/0 and 2,681/30/1 baselines. These compatibility measurements do not
establish the retained-memory budget for forward execution.


### S4 outermost scans and typed operands

**Implemented in development, 2026-10-23.** Forward `outermost()` selections over complete descendant name
tests use a pruned descendant iterator. Once a matching subtree is accepted, the next scan begins after it,
within the original scan boundary. Both absorbing bodies and bodies that only inspect metadata work without
collecting navigator clones. Proven disjoint child-only selections retain their iterator directly. Grounded
inputs use the same semantics. The compiler preserves the pruning flag in IL, including saved assemblies;
the ordinary optimizer cannot replace a pruned scan with a non-pruning merge iterator, and the Debug verifier
rejects changes to the pruning policy.

This lowering deliberately excludes descendant predicates and partial name tests whose acceptance happens
after iterator filtering. Pruning before such a predicate accepted the node would lose valid nested matches.
Those remaining forms need a later acceptance stage rather than an unconditional prune. Streamed argument
sequences passed to classified functions also still need their full iterator dataflow.

New descendant scans reject already-consumed or skipped nonempty subtrees instead of reporting an empty
sequence. An expired element known to be empty still has no descendants. This corrects misleading zero counts
in the unsupported function-argument path while preserving valid ongoing scans after absorption.

The numeric `sum()` fast path now applies only where nodes have untypedAtomic values. Schema-aware values,
including list-valued attributes, use typed atomization and retain integer/decimal precision. Comment, PI and
namespace nodes have string values and correctly raise `FORG0006` rather than being parsed as doubles.
Attribute value templates likewise atomize typed elements and attributes before converting their values to
strings. Backwards-compatible AVTs retain the first atomic item after atomization, as §5.6.1 requires.
Per-item forward atomization preserves adjacent text-node merging, including whitespace and significant
whitespace. Aggregates still need bounded accumulation before the S7 memory gate can be claimed.

The strict W3C resolver now returns validating readers for catalog-bound validated sources instead of
prebuilding typed trees that forward source-document entries cannot consume. The test owns these readers
through execution and XML assertion reruns, then disposes them. The existing core policy for borrowed readers
is unchanged. A dedicated regression binds its catalog URI explicitly and forces a text-to-XML assertion rerun.

Only the two proven resolved unstreamed failures, `streamable-035` and `streamable-036`, were removed from the
development baseline. No strict failure is added to a baseline. Normal streaming claims remain unchanged.


**Validation:** 51 additional unit cases on .NET 10 and 50 on .NET 8; full Release units pass 1,386 and 1,380.
The Debug forward/verifier/API/index/accumulator selection passes 309 and 303 cases. The cases cover overlapping
names pruned by outermost, disjoint anchors under both optimizer policies, non-consuming bodies, saved IL,
stale/empty descendant scans, typed sum lists and exact arithmetic, string-valued node errors, typed AVTs,
backwards-compatible atomization order, and adjacent text/whitespace merging. Upstream and differential gates
pass 1,654 and 394 cases per framework. The exact W3C CI run (`XSL3_W3C_SAVED=10`) passes 35 harness tests with
unchanged QT3 21,884/0/99 and XSLT 8,845/22/42 results, including 34 wrong expected XSLT error codes. Analysis and
unstreamed development-profile/harness selections pass 24 tests each: analysis remains 790/129/0; unstreamed
execution improves to **2,683/28/1**.

The strict S4 subset with saved-assembly sampling improves from 138/93/0 to **181/50/0** (two wrong expected
error codes). All cases in `si-apply-templates` (11), `si-next-match` (2), and `si-apply-imports` (3) pass.
The remaining strict failures are recorded in the development report, including classified function arguments,
post-filter acceptance, and other source/type paths. S4 is still open; S3's remaining operand/replay paths,
exact S1/S2 errors, S5–S7, and forward retained-memory budgets are not complete.

NativeAOT probes run a saved pruned descendant scan and a schema-aware sum over a caller's validating reader,
with dynamic code disabled. The sum preserves `9007199254740993 + 1` exactly and the reader remains open.
Both probes publish and run without trim/AOT warnings. This uses the supported saved-stylesheet path without
stylesheet schema imports; P6's existing restriction on saving imported schemas is retained. An initial probe
with imports was rejected by that guard; it was not bypassed. These functional probes do not prove the
streaming retained-memory budget.

The three-pair ordinary-path performance gate against `0f2ddcb` passes: time geometric mean **1.000** across
39 rows, maximum allocation ratio **1.026**. The gate uses the same current harness on base and head;
its thresholds and inputs are unchanged. This is a compatibility measurement, not a forward-memory verdict.

### S4 absorbing function arguments and string values

Absorbing functions whose first node-sequence parameter has one non-repeated use can now consume that
sequence before the reader advances. A compiler lowering expands acyclic calls before the streamed QIL
optimizer's read-order contract is captured. The ordinary function ABI is retained. Other arguments retain
a single binding and their node identity, and are compiled in the caller's focus and namespace context.
Runtime conversion remains authoritative; redundant `node()*` and node-sequence wrappers can be removed
only when the actual operand is statically known to contain nodes. General sequence/name/type checks and
the remaining transmission categories still need the full iterator dataflow.

Recursive functions keep their call boundaries. Runtime-checked `node()` and `node()?` parameters now retain
their node/cardinality facts in QIL, allowing child paths to avoid unnecessary merge buffers. Empty and
multiple-item arguments still receive the canonical conversion checks and error codes. This fixes the
recursive singleton count cases without unrolling recursion.

`fn:string` now extracts a node's string value while that node is current, before the generic function's
argument collection. This also fixes `string(child)` inside a consuming template. It uses string values rather
than typed-value atomization, and preserves non-node items so the canonical function still enforces its
cardinality and function-item errors.

Review safeguards cover repeated loop bodies, direct and mutual recursion, cached functions, tracing and
explicit recursion-depth settings. The sequence-call lowering retains the existing call boundary for cached,
traced or depth-counted functions; their general sequence parameters remain an outstanding runtime path.
Expanded bodies have independent mutable code-generation annotations, preserving only the compiler's
subtree-pruning policy. All candidate bodies are snapshotted before rewriting definitions, and nested
expansion follows the streamed operand. Regression tests bound expression growth for an acyclic graph with
independent grounded calls in both declaration orders.

Verification adds **25 net10.0 / 24 net8.0** focused cases, including saved assemblies. Full Release units pass
**1,411 / 1,404**; the Debug streaming/API/index/accumulator subset passes **334 / 327**. The strict S4 subset
with saved-assembly sampling improves from 181/50/0 to **189/42/0**, with one wrong expected error code.
All sixteen template-dispatch cases still pass. No strict failures are added to a baseline.

The NativeAOT saved-stylesheet probe now combines a sequence-argument absorbing function and a recursive
singleton function. It returns the correct count with dynamic code disabled and preserves the borrowed
reader's lifetime, without trim/AOT warnings. Aggregates still buffer atomic operands; these functional
checks do not establish a retained-memory budget. S4, the remaining S3 paths and S5–S7 are still open.

Ordinary compatibility gates pass: upstream **1,654/framework**, differential **394/framework**, and all
**35** normal W3C harness tests with saved-assembly sampling. QT3 remains **21,884/0/99** and the normal
XSLT profile remains **8,845/22/42**, with their existing error-code mismatches unchanged. The analysis
profile remains **790/129/0** and the unstreamed development profile **2,683/28/1**. Baselines are unchanged
by this increment; the ordinary profile continues to report no streaming support.

The three-pair ordinary-path performance gate against `0f2ddcb` passes: geometric mean time **1.007** across
39 rows and maximum allocation ratio **1.026**. Inputs and thresholds are unchanged. The individual dispatch
compilation row measures **1.335** in this run (0.961 in the preceding increment's run); the passing aggregate
gate does not settle that compile-time variation. It remains a performance-audit item for S7, alongside the
required forward-memory and latency measurements.

### S4 function contracts and simple content

Stylesheet function signatures now resolve in their own declaration scopes, including namespaces declared
on individual parameters, and share that resolution with streamability analysis. A caller's rebindings cannot
change a declared type. The argument compiler receives the declared parameter type: nodes passed to atomic
parameters are atomized while current, before argument collection. Conversion to the required atomic type
and the complete argument's cardinality checks still use the canonical runtime conversion rules.

Dynamic calls use the runtime function signature for the same per-item atomization. Per-call buffers keep
empty arguments separate, preserve list-typed values and numeric precision, and support named references,
inline functions, typed dynamic lookup, maps and arrays. Mutable call operations have side-effect annotations
and survive body cloning; a grounded named-template test exercises the ordinary optimizer's loop behavior.
The two generated-code helper contracts are recorded in `PublicAPI.Unshipped.txt`. Completion counters use
the existing double-valued generated-call ABI, verified by Debug QIL validation.

Validated node return types retain their QIL node/cardinality facts. Iterator lowering now covers acyclic
classified functions beyond absorbing functions, allowing shallow-descent results to be consumed as they
are produced. Node-only star types retain canonical per-item checks for node kind, name and type; wrong
results still raise XTTE0780. Full iterator dataflow across recursive, cached, traced and depth-counted calls,
and cardinality-sensitive singleton argument selection, still needs work.

Simple-content constructors now atomize before advancement too. They preserve originally adjacent text and
group each non-text item's atoms, including empty groups. Thus an empty list value, nilled element or empty
array still separates text nodes that were not originally adjacent. This corrects both value-of and AVT
behavior without retaining the original non-text source nodes. `fn:string` continues to use node string
values rather than typed-value atomization.

Verification adds **33 net10.0 / 32 net8.0** cases: direct/arrow/dynamic calls, declaration namespaces, list
values, map/array conversion, argument state, canonical errors, shallow descent, result checks, empty-value
boundaries and saved dynamic calls. Full Release units pass **1,444 / 1,436**; the Debug
streaming/API/index/accumulator subset passes **367 / 359**. The strict S4 subset with saved-assembly sampling
improves from 189/42/0 to **203/28/0**, retaining one wrong expected error code. All filter-function cases
(10), unclassified-function cases (6), inspection cases (8), ascent cases (6) and template-dispatch cases (16)
pass. Absorbing, shallow-descent and other `streamable` cases still fail; S4 is not complete. No strict
failures are added to a baseline, and the ordinary profile still reports no streaming support.

Ordinary compatibility remains unchanged: upstream **1,654/framework**, differential **394/framework**, all
**35** normal W3C harness tests with saved-assembly sampling, QT3 **21,884/0/99**, normal XSLT
**8,845/22/42**, analysis **790/129/0**, and unstreamed development **2,683/28/1**. Existing wrong-error-code
counts are unchanged and no baseline entries change in this increment.

The NativeAOT saved-stylesheet probe combines named-node shallow-descent result checks, an expanded
absorbing function, a dynamic atomic call, declaration-local type prefixes and schema list input. It preserves
the exact result **9,007,199,254,740,999** and verifies text boundaries around empty/nilled list values, with
dynamic code disabled and caller readers left open. Generation, publication and execution pass without
trim/AOT warnings. The existing restriction on saving imported schemas remains unchanged; input schema
validation is supplied by the caller's validating reader.

Atomic argument/result sequences and complete simple-content strings can still require storage. The full
S3/S4 gates and S5–S7, including the retained-memory and latency budgets, remain outstanding.

The three-pair ordinary-path performance gate against `0f2ddcb` passes: geometric mean time **1.000** across
39 rows and maximum allocation ratio **1.027**. The dispatch compilation row measures **1.086** in this run;
the prior compile-time variation remains an S7 audit item. The same current harness runs on base and head,
with unchanged thresholds and inputs. This is an ordinary-path compatibility result, not a forward-memory
or first-output-latency measurement.

### S4 singleton argument flow and focus demand

Absorbing and inspection functions with a declared `node()` or `node()?` first parameter now consume a
path-selected first node within the original function invocation. Their grounded result is wrapped while
that node is current, and the argument selection is then checked for a second node before the result is
exposed. Empty required arguments still raise XPTY0004; optional empty arguments invoke the actual body.
Empty function results remain distinct from missing arguments. The original call boundary preserves tracing,
cached-function policy and recursion-depth checks. This evaluation order uses XPath's permitted lazy argument
evaluation and error ordering ([XPath 3.1 §3.1.5.1](https://www.w3.org/TR/xpath-31/#id-function-calls),
[§2.3.3–4](https://www.w3.org/TR/xpath-31/#id-errors-and-opt)).

Acyclic transmitting functions can also receive a path-selected singleton without collecting its nodes.
Their inlined body and its consumer run while the first node is current; a per-invocation seen flag detects
a second argument item. Complete consumption checks the whole argument's cardinality. An optional empty
argument takes the original grounded call, preserving body evaluation without duplicating nested expansions.
Declared argument names/types and result names/types retain their canonical checks and error codes. Results
can be delivered incrementally before a later argument error; retaining those source results to delay all
output would defeat their forward lifetime. Recursive, cached, traced and depth-counted transmitting calls
still need the full iterator ABI, as do cardinality-sensitive transmitted results. Expansion also preserves
any narrower native return signature at its existing call boundary. The three generated-code state helpers
are in `PublicAPI.Unshipped.txt`.

Template calls now carry a context size only when a reachable body reads it. A QIL demand pass follows the
implicit size parameter through call chains and cycles, removes unused actual/formal parameters, and removes
only compiler-marked caches created solely to count that selection. Sorting caches, actual `last()` users and
opaque focus captures retain their existing dependencies. Thus recursive copy templates can use `position()`
without first collecting all child nodes. No placeholder size is substituted. This pass runs before function
lowering and the streamed optimizer verifier; saved assemblies retain the resulting signatures.

Forward general and value comparisons atomize each node before advancing their operands, including range
comparison shortcuts. Canonical comparison routines still decide conversion, collation, cardinality and
errors. Regression tests include list-typed nodes and integers beyond double precision. Backwards-compatible
general comparisons keep their separate boolean/node semantics and still require further operand work.

The broader Debug review found an existing P7 attribute-set diagnostic issue: a merged set intentionally has
no code-generation source span, but streamability errors used that missing span. Assessment now reports a
constituent declaration's actual location, preserving the compiler's attribute-set invariant. The regression
checks XTSE3430 and its real line/column in both configurations.

This increment adds **64 net10.0 / 61 net8.0** cases covering scalar and transmitting selection, empty/multiple
arguments, side effects, caller focus, tracing, recursion limits, canonical type/comparison errors, optional
expansion growth, context-size demand/cycles, sorting and saved assemblies. Full Release units pass
**1,508 / 1,497**. An expanded Debug filter covering streaming analysis/declarations as well as runtime,
API/index/accumulator cases passes **759 / 748**. The strict S4 subset with saved-assembly sampling improves
from 203/28/0 to **220/11/0**, retaining one wrong expected error code. The remaining eleven failures cover
overlapping absorption (3), schema/catalog behavior (4), grouping (2) and recursive transmitting functions (2).
No strict failures are added to a baseline; S4 is still open.

Ordinary compatibility gates pass: upstream **1,654/framework**, differential **394/framework**, all **35**
normal W3C harness tests with saved-assembly sampling, QT3 **21,884/0/99**, normal XSLT **8,845/22/42**,
analysis **790/129/0**, and unstreamed development **2,683/28/1**. This unstreamed run records seven
wrong-error-code passes (six in the preceding increment); its pass/fail/not-run totals and failure IDs are
unchanged. Exact error-code conformance remains outstanding. Baselines are unchanged in this increment.

The NativeAOT saved-stylesheet probe now combines scalar recursion, incremental transmitting arguments,
optional empty input, recursive position-based copy, comparisons and cardinality errors. It runs with dynamic
code disabled, leaves borrowed readers open, and passes generation, publication and execution without trim
or AOT warnings. These are functional results; atomic buffers, output buffering and retained-memory/latency
budgets still need their S7 measurements. The remaining S1/S2 gates, S3/S4 paths and S5–S7 remain required,
and the normal profile still reports no streaming support.

The three-pair ordinary-path performance gate against `0f2ddcb` passes: geometric mean time **1.004** across
39 rows and maximum allocation ratio **1.027**. The dispatch compilation row measures **1.017** in this run;
the earlier compile-time variation remains an S7 audit item. Inputs, harness and thresholds are unchanged.
The final return-signature safeguard only affects the forward-function lowering path; ordinary-path behavior
and these benchmark paths are unchanged by that safeguard. Forward retained memory and first-output latency
remain unmeasured by this gate.

### S5 inline iterate and forward sequence windows

**Development increment, 2026-10-23.** Forward compilation now lowers `xsl:iterate` into an inline QIL loop,
so its selection and body results stay as iterators. A per-invocation control item stores the current parameter
values and break state. Initial parameter expressions use the outer focus; each body uses the selected item
and its actual position. Supplied next-iteration values are converted before the update commits, omitted
parameters retain their values, and implicit next-iteration leaves them unchanged. Completion uses the final
values with absent focus, and a break suppresses completion. The ordinary path retains its callback runtime.

A compiler-marked loop gate checks the break condition before fetching the next selection item, including
the first fetch. This matters when the next expression would raise an error or advance past the current
streamed result. The streamed optimizer preserves the gate, the Debug verifier detects its removal, and
function expansion preserves the annotation. Context-size pruning removes an unused counting cache through
the gate without losing it; a real grounded `last()` retains its count and cache. Source-node results are
consumed while current. A writer regression confirms that nonempty results arrive before input EOF; it does
not establish a quantitative latency or memory budget.

Direct forward calls to `head`, `tail` and `subsequence` now use position windows rather than collecting
the source sequence. Their per-invocation state retains no input nodes and checks the end bound before
fetching another item. Function argument conversion remains canonical, and both execution paths share the
existing subsequence bound calculation, including rounding, NaN and infinite bounds
([Functions and Operators 3.1](https://www.w3.org/TR/xpath-functions-31/#func-subsequence)). Dynamic function
references still need their own forward lowering.

The review and regression probes also corrected related context and type errors:

- Iterate parameter types resolve in their declaration scopes, so a namespace rebound on `next-iteration`
  cannot change a parameter's declared type. Construct analysis uses the same declaration context. Atomic
  sequence conversion reads each node while current and retains canonical cardinality/type errors and exact
  integer values. Initial expressions see earlier parameter bindings; a parameter's own initializer still
  sees the outer binding of its name, and local bindings do not leak after the instruction.
- Iterate bodies and completion clear the current template rule. A named template called from them also
  receives an absent rule in its tunnel frame, rather than recovering an outer rule. `next-match` and
  `apply-imports` raise XTDE0560 there. The original XSLT 1.0 compiler assertion remains limited to 1.0.
- Numeric focus access in a named template called with absent focus raises XPDY0002, including calls from
  completion. Global `position()` and `last()` likewise raise XPDY0002 when the independent global context
  item is absent, and return 1 when one is supplied. The initial streamed selection cannot supply that global
  focus. Context restoration uses `finally` around compilation without a current template rule.

These rules follow the [iterate dynamic context](https://www.w3.org/TR/xslt-30/#element-iterate) and
[current template rule](https://www.w3.org/TR/xslt-30/#current-template-rule) requirements. Tests exercise both
ordinary and forward compilation where the same rule applies.

This increment adds **58 net10.0 / 57 net8.0** unit cases. Full Release units pass **1,566 / 1,554**, and the
expanded Debug streaming/API/index/accumulator subset passes **817 / 805**. The strict `si-iterate` subset
improves from 17/17/0 to **33/1/0**. Its remaining case, `si-iterate-139`, needs overlapping snapshot replay
from S3. The combined S4 and iterate probe with saved-assembly sampling is **253/12/0**, retaining one wrong
expected error code; S4 itself remains **220/11/0**. No strict failures were added to a baseline.

Ordinary compatibility checks pass: upstream **1,654/framework**, differential **394/framework**, all **35**
normal W3C harness tests with saved-assembly sampling, QT3 **21,884/0/99** (67 wrong-error-code passes), normal
XSLT **8,845/22/42** (34), analysis **790/129/0**, and unstreamed development **2,683/28/1** (seven). These
development baselines and their known failures are unchanged. The saved NativeAOT stylesheet executes the
window/iterate loop, typed parameter updates, dependent initializers, completion, a break before an erroring
third item, and a real grounded context size. Generation, publication and execution pass without trim/AOT
warnings, with dynamic code disabled and borrowed readers left open.

The final three-pair ordinary-path performance gate against `0f2ddcb` passes: geometric mean time **1.019**
over 39 rows and maximum allocation ratio **1.028**. Its individual compilation rows include **1.061** for
dispatch and **1.749** for keys. Keys' head round medians range from 0.366 to 0.624 ms, versus 0.299 to
0.393 ms on base. These compilation outliers remain an investigation item alongside the earlier compile-time
variation; the aggregate pass does not resolve them. The same current harness, inputs and thresholds run on
both sides. This gate measures ordinary execution, not forward retained memory or first-output latency.

**S5 is not complete:** streamed accumulator observers, grouping and merge remain outstanding. S1/S2's
remaining static checks, S3 overlapping replay, S4's remaining function paths, S6 fork execution and S7's
retained-memory/latency gates also remain required. The normal profile still reports no streaming support.

### S3 replay for explicit grounding

**Development increment, 2026-10-23.** Explicit `snapshot` and `copy-of` grounding now records source events
when a live descendant scan may need matches inside the copied subtree. Copying an outer `a` in `//a` can
therefore finish its subtree, yield the independent compact copy, then resume the source selection at nested
`a` nodes before continuing with the following input. Copy instructions use the same mechanism for all
namespace and annotation options. This closes `si-iterate-139` without grounding the initial input as a tree.

The replay buffer attaches to the original source node frames, retaining their original document-order
numbers, parent chains, attributes, namespaces, language and schema annotations. Its child/sibling navigation
and string values use those captured events. New copies still get separate compact trees and identities.
No whole-document index owns the buffers: they become collectible when the last source position in that
subtree dies. Copy-accumulator provenance can retain such a position while its copy is live. Temporary
construction bookkeeping is released when capture completes. Replay of an inner node stays within its
subtree, and continuing past the buffered root resumes the actual reader.

Only explicit grounding requests recording; scalar consumption does not enable it. Pruned outermost scans
do not record excluded descendants. Interrupted copies retain the position-error guard rather than treating
their partial capture as a complete or empty subtree, even when the interruption precedes any nested match.
Reaching the root's end after skipping unread content also leaves capture incomplete. Existing tests still require `stream-position` for
unsupported overlapping scalar absorption. The buffer is proportional to the explicitly copied subtree;
retained-copy/provenance lifetimes and process-memory/latency budgets still require S7 measurements.

This increment adds **20 net10.0 / 19 net8.0** unit cases covering nested matches and subsequent input,
source identity versus copy identity, ancestors/language, child/sibling boundaries, empty elements, header
lookahead, final schema annotations and typed attributes, all copy-option paths, interrupted/unread capture,
subtree-only recording, collection after advancement, and saved assemblies. Release units pass
**1,586 / 1,573** and the expanded Debug streaming/API/index/accumulator subset passes **837 / 824**.

With saved-assembly sampling, strict `si-iterate` is now **34/0/0**, and the combined S4-plus-iterate subset
is **254/11/0**, retaining one wrong expected error code. A broader probe adding `source-document`, `sf-*`
and `sx-*` measures **1,617/236/0**, with thirteen wrong-error-code passes. Its source-document row is
**41/6/0**. This is a selected development probe, not the complete strict P7 exit. No strict failures were
added to a baseline. Remaining copy-function failures include descendant-text traversal (`sf-copy-of-009`,
`sf-snapshot-0309`) and a buffered `remove` operand (`sf-snapshot-0209`); replay alone does not lower those
other operand paths. S3 still has traversal/operand work beyond explicit grounding.

Ordinary gates pass: upstream **1,654/framework**, differential **394/framework**, all **35** normal W3C
harness tests with saved-assembly sampling, QT3 **21,884/0/99** (67 wrong-error-code passes), normal XSLT
**8,845/22/42** (34), analysis **790/129/0**, and unstreamed development **2,683/28/1** (seven). Their baselines
remain unchanged. The saved NativeAOT probe adds overlapping snapshots to the prior window/iterate checks;
generation, publication and execution pass without trim/AOT warnings, with dynamic code disabled and borrowed
readers left open. These results do not establish retained-memory or latency budgets. S1/S2's remaining
static checks, S4's remaining function paths, S5 accumulators/grouping/merge, S6 fork and S7 remain open;
normal conformance still reports no streaming support.

The final three-pair ordinary-path performance gate against `0f2ddcb` passes with geometric mean time
**1.018** over 39 timed rows. The largest reported allocation ratio is **1.028** (the cold-start row).
Individual compilation ratios include keys **1.833**, sorting **1.167** and dispatch **1.038**. Keys' recurring
compilation outlier and the other compile-time variation remain under investigation; the aggregate pass does
not resolve them. Both sides use the same current harness, inputs and thresholds. Forward retained-memory and
first-output latency remain unmeasured by this ordinary-path gate.

### S5 streamed accumulator observers

**Development increment, 2026-10-23.** Applicable streamable accumulators now run on source reader events.
Start rules run before descent and end rules after descent, including nodes skipped by template selection.
Callbacks can inspect headers and ancestors but cannot advance the reader. Requesting `accumulator-after`
finishes the context subtree's remaining events, including an ancestor whose earlier children were already
consumed. Replay does not run the observers again. Both API paths, forward source documents, input validation
and saved stylesheets use the compiled accumulator bindings.

Applicability follows the selected mode or source document. Named references must identify streamable
accumulators; malformed, duplicate or mixed `#all` lists are rejected. `#all` selects only streamable
accumulators visible in the owning package. Public imported modes resolve to that package before determining
applicability. Tests distinguish a package's counter from a same-named failing counter in its caller.
An independent grounded global context keeps its own accumulator values.

Rule evaluation shares the existing pattern-priority and type-conversion logic. Pattern errors are non-matches;
the internal source-position and missing-runtime-path guards remain errors. Ordinary initial-value or rule-value
errors are retained until a functionally dependent value is requested, preserving earlier valid values. End
rules can read their node's already-computed before value. Real dependency cycles raise XTDE3400 and cannot be
hidden by a previously cached root value; a user-raised error with the same QName retains normal deferred-error
semantics. These corrections also apply to tree execution and fix `evaluate-046`, whose end rule reads its
own before value. See [§18.2.5](https://www.w3.org/TR/xslt-30/#errors-in-accumulators).

Before/after values are weakly keyed by original source frames; no document-wide strong node index owns them.
Explicit copies record source frames when accumulator provenance needs them, even without an overlapping scan.
Copies and snapshots retain the original nodes' values after the input closes, including descendant values
and copies subsequently validated against a schema. Lookup uses recorded provenance rather than traversing a
closed reader. Observer disposal releases invocation functions and current values while leaving copied values
available. These lifetime properties still need S7 process-memory measurements; weak caches alone do not prove
the retained-memory budget. See [§18.2.10](https://www.w3.org/TR/xslt-30/#copying-accumulators).

Static-data minor version **5** records accumulator streamability using constants format **2**. The reader
accepts constants format 1 with a non-streamable default. Existing generated entry points retain their ABI and
their explicit guard when bindings are unavailable; new entry points carry bindings and package identity.
Saved imported schemas retain their existing unsupported-format restriction.

Focused new coverage comprises **40 net10.0 / 39 net8.0** cases: accumulator behavior, observer event order,
motionless callbacks, deferred errors and real cycles, applicability, package ownership, validated source events,
copy provenance after source closure, old constants and saved assemblies. The final validation and performance
results follow. Full Release units pass **1,659 / 1,643**, and the expanded Debug
streaming/API/index/accumulator subset passes **877 / 863** on .NET 10/8. These full-suite totals also include
concurrent compact-tree tests; the focused case count above describes this increment.

With saved-assembly sampling, the strict accumulator set is **36/0/0** and source-document is **45/2/0**.
The two remaining source-document cases require the streaming-support claim, which stays disabled. Iterate
remains **34/0/0** and S4 **220/11/0**. The broader probe covering these sets, `sf-*` and `sx-*` is
**1,657/232/0**, with fourteen wrong-error-code passes. This remains a selected development probe, rather
than the full S5 or P7 exit. No strict failures were added to a baseline.

Compatibility checks pass: upstream **1,654/framework**, differential **394/framework**, all **35** normal W3C
harness tests with saved-assembly sampling, QT3 **21,884/0/99** (67 wrong-error-code passes), normal XSLT
**8,845/22/42** (34), analysis **790/129/0**, and unstreamed development **2,684/27/1** (seven). Only the verified
`evaluate-046` fix was removed from the development baseline; normal baselines are unchanged by this increment.
The saved NativeAOT probe adds start/end accumulator values and a final total to the prior window/iterate and
overlapping-snapshot checks. Generation, publication and execution pass without trim/AOT warnings, with dynamic
code disabled and borrowed readers left open. These results do not establish memory or latency budgets.

The final three-pair ordinary-path performance gate against `0f2ddcb` passes: geometric mean time **1.002**
over 39 timed rows, with maximum reported allocation ratio **1.029** (cold start). Individual compilation
ratios include keys **1.225**, dispatch **1.009** and sorting **0.936**; the 1 MB strings execution row is
**1.084**. Keys' recurring compilation outlier and individual execution variation remain investigation items;
the aggregate pass does not resolve them. Both sides use the same current harness, inputs and thresholds.
This gate does not measure forward retained memory or first-output latency.

**S5 remains open:** grouping and merge still need forward runtime integration. Remaining S1/S2 static checks,
S3 traversal/operand work, S4 function paths, S6 fork execution and S7 memory/latency gates are also outstanding.
The normal profile continues to report no streaming support.

### S5 forward grouping

**Development increment, 2026-10-23.** `group-adjacent`, `group-starting-with` and `group-ending-with` now
consume a forward population through a resumable generated iterator. The first member starts the group body;
`current-group()` pulls members as the body consumes them. Adjacent and starting groups retain one boundary
item for the following group. Ending groups stop at their matching member without fetching the next group.
Unused or truncated groups are drained before the next group starts. Keys are evaluated once while each
population item is current, using the shared collation, composite-key and equality rules.

Compiler-created population functions have an explicit continuation argument. Generated IL saves and restores
iterator locals in an invocation-owned frame, then resumes the original population at its next-item label.
Completion and failure release its slots and function bindings. Nested grouping can therefore consume an outer
`current-group()` without caching its source nodes. A pull-operation annotation makes the IL loop over runtime
one-item results instead of treating them as a materialized list. The Debug verifier checks both that annotation
and the continuation argument. Saved assemblies and NativeAOT use the same emitted calls and state frames.

Grounded groups remain repeatable: atomic values, explicitly copied nodes, and attribute/namespace headers can
be cached as group members. The runtime does not cache streamed document, element or child-node members.
Grounded caches and objects referenced by continuation slots can still retain explicit results; these mechanics
do not prove a process-memory bound. A 1,000-member test verifies that the first group body starts with only its
first source member read, and that repeated suspension does not leak counted call depth. Quantitative first-byte
latency and S7's process-memory budgets remain unmeasured.

Current-group scope now follows [§14.2.1](https://www.w3.org/TR/xslt-30/#func-current-group), independently of
whether a callee has a context item. Invocations within declared-streamable constructs clear the group and its
key in the tunnel frame, even during tree execution. Ordinary calls preserve them through named and applied
templates and attribute sets. Focused regressions cover both rules; the two W3C fork-scope cases remain passes.

The strict `si-for-each-group` set improves from **82/32/0** to **108/6/0** with saved-assembly sampling. Its six
remaining failures (`si-group-055`, `-057`, `-058`, `-059`, `-059a` and `-204`) all require fork execution. No
strict failures were added to a baseline. The broader probe covering grouping, accumulators, iterate, S4,
source-document, `sf-*` and `sx-*` is **1,766/237/0**, with fourteen wrong-error-code passes. This selected
development probe does not establish the full P7 exit.

This increment adds **35 net10.0 / 34 net8.0** unit cases covering all three group kinds, draining and
truncation, nested populations, key cardinality/composite values and population positions, reductions, grounded
and copied members, repeatable headers, group scope, first-group evaluation and saved assemblies. It includes
two preservation-verifier regressions. Full Release units pass **1,694 / 1,677**, and the expanded Debug
streaming/API/index/accumulator subset passes **912 / 897**.

Compatibility checks pass: upstream **1,654/framework**, differential **394/framework**, all **35** normal W3C
harness tests with saved-assembly sampling, QT3 **21,884/0/99** (67 wrong-error-code passes), normal XSLT
**8,845/22/42** (34), analysis **790/129/0**, and unstreamed development **2,684/27/1** (seven). Their baselines
remain unchanged. The saved NativeAOT probe adds adjacent grouping and member values to the accumulator,
window/iterate and overlapping-snapshot checks. Generation, publication and execution pass without trim/AOT
warnings, with dynamic code disabled and borrowed readers left open. Memory and latency budgets remain open.

The final three-pair ordinary-path performance gate against `0f2ddcb` passes: geometric mean time **1.009**
over 39 timed rows and maximum reported allocation ratio **1.031** (cold start). Compilation ratios include
sorting **1.181**, dispatch **1.027**, grouping **1.014** and keys **0.961**. Sorting's outlier and the
previous increments' compilation variation remain investigation items; this aggregate pass does not resolve
them. Both sides use the same current harness, inputs and thresholds. This gate does not establish forward
throughput, retained memory or first-output latency.

S5 still requires merge runtime integration. `group-by` and sorted groups with streamed input remain part of
S6's fork work. Remaining S1/S2 checks, S3/S4 runtime paths and S7 memory/latency gates also remain open; the
normal profile continues to report no streaming support.


### S5 streamed merge

**Development increment, 2026-10-23.** Merge sources now use the same generated pull populations as forward
grouping. A priority queue keeps one head per input sequence; one merge source can select several input URIs.
Heads are ordered by key, then source declaration and anchor order. Each equal-key group is buffered before its
action runs, preserving repeatable `current-merge-group()` values and source/input/item order. Explicitly sorted
ordinary sources can participate, buffering their own inputs as requested by `sort-before-merge`.

The memory requirement is proportional to the input heads and the largest equal-key group, plus explicit
results and any requested ordinary-source sorting. This is not a constant-size bound for arbitrary groups.
A 1,000-row test verifies that the first action runs after only the first few rows from each reader, without
reading both documents completely. Quantitative latency and process-memory gates remain S7 work.

[§15.4](https://www.w3.org/TR/xslt-30/#streamable-merging) requires each selected row to be snapshotted before
its keys or action are evaluated, including when tree execution is selected. Keys may read children and copied
ancestors, and the copies preserve accumulator provenance and schema annotations. Both execution paths now
follow this rule. Key functions use singleton focus; source selections use anchor position and size; actions
use the first group member and the group position, with absent context size if any source is streamable.
Merge actions also clear the current template rule (§6.8).

Shared runtime options evaluate key attribute value templates in the containing merge's focus, compare their
effective values and explicit presence, resolve relative collation URIs, and apply language/case collations.
Comparison-only `data-type` conversions no longer change the raw values exposed by `current-merge-key()`.
Incomparable inputs of the same source now raise XTTE2230, and dynamic source names are matched exactly.
Repeated fixed-focus calls receive separate expression nodes, avoiding Debug QIL validation failures.
Numeric NaNs retain their sorting/grouping semantics without making non-numeric keys comparable.
Named attribute flags distinguish omitted attributes from explicitly empty values. Empty `data-type` raises
XTDE0030, as does empty `case-order` without an overriding collation. Empty collation references resolve against
the key's base URI, and an explicit collation ignores language and case hints as required by [§13.1.3](https://www.w3.org/TR/xslt-30/#collating-sequences).

Forward source opening reuses source-document validation, annotation policy and accumulator observers.
The input cursor closes owned sources at completion; transformation and try/catch scopes retain their existing
failure cleanup. Resolver-supplied readers remain borrowed. The generated runtime ABI adds opaque option,
input, row and group helpers while retaining the existing `X31Merge` entry point and static-data format.

The strict `merge` and `si-merge` probe changes from **23/10/1** to **28/5/1**, with no wrong-error-code passes.
Eight former failures now pass: `merge-070`, `-071`, `-076`, `-078`, `-079`, `-080`, `-083` and `-097s`.
All five remaining failures conflict with published XSLT 3.0:

- `merge-073`, `-081`, `-082` and `si-merge-004ns` combine `for-each-item` with `streamable="no"`.
  [XTSE3195 (§15.3)](https://www.w3.org/TR/xslt-30/#err-XTSE3195) excludes the streamable attribute regardless
  of its value. `merge-082` also combines `for-each-item` with `use-accumulators`.
- `merge-084` calls `last()` in an action with a streamable source.
  [§15.7](https://www.w3.org/TR/xslt-30/#merge-action) makes the context size absent even during tree execution.

The [official errata](https://www.w3.org/XML/2017/qt-errata/xslt-30-errata.html) contains no correction to these
rules. The full unstreamed development profile is **2,689/22/1**, with seven wrong-error-code passes. Its
reviewed baseline removes the eight verified fixes and adds the three newly exposed fixture conflicts
(`merge-073`, `-082`, `si-merge-004ns`); the other two were already failures. They remain visible failed cases,
with comments linking their normative basis. The normal conformance baseline is unchanged.

The expanded strict probe covering merge, grouping, accumulators, iterate, source-document, S4, `sf-*`, `sx-*`,
`su-*` and declaration checks is **1,794/242/1**, with fourteen wrong-error-code passes. Apart from the newly
included merge sets, its results are unchanged from the preceding grouping increment: accumulators **36/0/0**,
iterate **34/0/0**, grouping **108/6/0** and source-document **45/2/0**. This selected probe does not establish
P7's full strict-streaming exit.

The increment adds **60 net10.0 / 59 net8.0** unit cases: both API paths and tree/forward modes, stable ties,
multiple anchors, key and action focus, numeric/raw and multiple keys, AVTs and relative collations, empty
inputs, snapshots, validation defaults, copied accumulator values, ordering/type/cardinality errors, source
names, template-rule scope, ownership, URI conversion, NaNs, ordinary context sizes, empty attributes, collation precedence and early evaluation. Saved merge assemblies pass, and a saved NativeAOT
probe merges two owned input streams using child-valued keys and dynamic attributes with dynamic code disabled.
No trim/AOT warnings are reported. Full Release units pass **1,754 / 1,736**, and the expanded Debug subset
passes **972 / 956**. Upstream's documented CI gate passes **1,654/framework**, differential passes
**394/framework**, and all **35** normal W3C harness tests pass with saved-assembly sampling. The normal XSLT
profile remains **8,845/22/42** (34 wrong-error-code passes), and QT3 remains **21,884/0/99** (67).


The final three-pair ordinary-path gate against `0f2ddcb`, after the attribute and comparison fixes and all CPU
checks, passes: geometric mean head/base time **1.004** over 39 timed rows, maximum reported allocation ratio
**1.033** (cold start). Compilation ratios include keys **1.149**, dispatch **1.074**, grouping **1.020** and
sorting **0.932**. Keys/dispatch outliers and the earlier compilation variation remain investigation items;
the aggregate pass does not resolve them. Both sides use the same current harness and thresholds. This gate
does not measure forward retained memory, throughput or first-output latency.

S6 fork execution, remaining S1/S2 checks and S3/S4 paths, and S7's process-memory, latency and claim gates
remain outstanding. The normal profile continues to report no streaming support.


### S6 fork input and prong lifecycle

**Development foundation, 2026-10-23.** `StreamTee<T>` now shares immutable event objects through a bounded
ring. Each reader has its own position; events are released after every attached reader passes them. A reader
that reaches the ring limit waits for slower readers. Detaching a reader releases its unread references, and
cancellation/disposal wake blocked readers. The source is read once, and every reader receives its buffered
prefix before a source failure. Exception dispatch retains the original error across the transport boundary.

`ForkProngs` runs one thread per prong. A demand-driven pump reads the source on the invoking thread, preserving
the parser's and accumulator callbacks' thread context. It does not read ahead when no prong requests input.
Source I/O runs outside the tee lock, allowing readers to consume buffered events or detach during a read.
Nested fork coordinators can request events through an enclosing tee without adding producer threads.

An error cancels only following prongs. Earlier prongs finish so that a subsequently detected earlier error can
win according to declaration order. Linked cancellation tokens cover the prong bodies; every started thread is
joined before return or error propagation. Completed prongs detach their inputs. Cleanup callback failures do
not replace the original prong failure. Transport catches engine exceptions as well as dynamic errors, keeping
an exception such as `NullReferenceException` from escaping a worker thread and terminating the host process.
Normal completion leaves prong tokens uncancelled.

The **25 new tests per framework** cover shared event identity/order, single source evaluation, ring limits at
capacities one and seven, slow-reader backpressure, collection of consumed events, partial and early detach,
EOF/error prefixes, blocked-reader cancellation/disposal, source I/O outside the ring lock, recursive requests,
caller-thread source reads, independent prong threads, declaration-order errors, parent cancellation, callback
failures and nested fan-out. Full Release units pass **1,779 net10.0 / 1,761 net8.0**; the expanded Debug
streaming/API/index/accumulator subset, including both new test classes, passes **997 / 981**.

This measures the event-count bound and verifies reference release; it does not prove a retained-byte or
process-memory limit. The components are not yet called by stylesheet fork lowering, and this increment makes
no new strict-conformance, saved-assembly, NativeAOT or performance claim. The preceding merge increment's
measurements remain the last checks of those paths.

The remaining S6 integration must:

- Adapt logical streamed events to independent navigators while preserving original node identity, ancestor
  headers, schema annotations and accumulator provenance. Accumulator rules must run once on the source;
  advancing a prong must not bypass the tee to consume the original reader.
- Keep per-prong output stacks, call frames and source scopes independent while coordinating transformation-wide
  global bindings, document identity/caches and result-URI claims. Sharing the current mutable runtime directly
  would race; independently cloning global evaluation could change once-only evaluation and node identity.
- Preserve context position and absent-size behavior, captured function ownership, dynamic date/time, cancellation,
  recursion depth, annotation policy and other execution settings in each prong.
- Connect compiler lowering to push output from the first prong, buffer later prongs and concatenate their results
  in declaration order. Returning every prong as a materialized item list would not meet the streaming goal.
- Integrate group-by/sorted groups, add stylesheet-level failure/cancellation coverage, verify saved assemblies and
  NativeAOT, and measure the tee's memory bound before S6 can close.

S1/S2 conformance checks, remaining S3/S4 paths and S7's memory, latency, throughput and feature-claim gates also
remain open. The normal profile continues to report no streaming support.


### S6 streamed fork input views

**Development increment, 2026-10-23.** The tee/prong foundation now feeds independent `StreamedDocument`
views through logical node events. The source parser coalesces text and performs validation once. Events freeze
node metadata; each view builds its own navigation frames and name table without serializing XML or building a
whole-input tree. Ancestor headers are seeded from the current source position, including an already inspected
first-child header. The parent source can resume at the following sibling after an element fork finishes.

Views retain the original source's document identity and node numbers. Position equality, document order,
namespace/attribute positions and document-index lookup therefore recognize aliases of the same source node.
Source-node references are also retained for subsequent accumulator-provenance integration. Schema annotations
and default attributes survive event transport and explicit grounding. Only the original document runs source
observer callbacks, on the invoking thread; closing a view detaches its tee reader and leaves the original
reader/observer owned by its enclosing source scope.

Cancellation of the original source is linked to every view, including prongs waiting without requesting more
input. Early-completing prongs release their unread events. Source errors keep their original exception and do
not close a borrowed reader. The current adapter accepts fresh forward document/element positions and exposes
only their subtree plus ancestor headers. It rejects secondary positions instead of silently using their parent,
and reports out-of-scope consuming navigation. Completed replay inputs and wider context access still need their
appropriate execution paths during compiler integration.

**15 new unit cases per framework** cover document/element focus, mixed text/CDATA/comments/PIs, independent
traversal, node/attribute/namespace identity and order, separate name tables, ancestor language/base URI and
namespace headers, parent-source continuation, first-child lookahead, header-only bodies, nested forks, source
observer ownership, typed/defaulted copies, early completion, cancellation, malformed input and explicit scope
boundaries. All **40** fork input/lifecycle cases pass per framework. Full Release units pass
**1,794 net10.0 / 1,776 net8.0**; the expanded Debug subset passes **1,012 / 996**.

Compatibility checks pass: upstream **1,654/framework**, differential **394/framework**, and all **35** normal
W3C harness tests with saved-assembly sampling. Normal XSLT remains **8,845/22/42** (34 wrong-error-code passes),
QT3 **21,884/0/99** (67), unstreamed development **2,689/22/1** (seven), and the selected strict probe
**1,794/242/1** (fourteen). No conformance baseline changed. The saved grouping/merge/accumulator/iterate/replay
NativeAOT probe passes again, and a direct internal NativeAOT smoke test consumes two fork views and checks
shared identity with dynamic code disabled. Publication reports no trim/AOT warnings. This does not prove saved
stylesheet fork lowering, which is still outstanding.


The final three-pair ordinary-path gate against `0f2ddcb` passes with geometric mean time **0.997** over
39 timed rows and maximum reported allocation ratio **1.033** (cold start). Compilation ratios include sorting
**1.205**, grouping **1.068**, dispatch **1.026** and keys **0.914**. The sorting outlier and earlier compilation
variation remain investigation items despite the aggregate pass. Both sides use the same current harness and
thresholds. This does not measure fork throughput, retained bytes or first-output latency.

S6 still needs accumulator value lookup/copy-provenance routing for aliases, coordinated global/document caches,
isolated runtime stacks/settings and captured function bindings, compiler lowering, ordered push/buffered output,
group-by/sorted groups, and stylesheet-level error/cancellation and memory checks. S1/S2 and remaining S3/S4 work,
and S7's budgets and feature claim, also remain open. The normal profile continues to report no streaming support.


### S6 fork accumulator values and copy provenance

**Development increment, 2026-10-23.** Fork views now resolve accumulator applicability and values through the
original source's document identity and node frames. Rules still execute once, on the invoking thread. Workers
read published phases without invoking rules or creating cache entries. Cache access uses short critical
sections and volatile phase/fault references, with no lock across input I/O or function execution.

When a requested post-descent value is not yet available, the requesting view finishes its own subtree through
the tee. It never advances the original reader directly. Deferred rule failures preserve their original codes;
detected accumulator cycles retain XTDE3400 even when an earlier phase has a cached value. Ancestor headers
remain readable, but requesting an unavailable post-descent value outside the fork boundary raises the position
error rather than consuming input outside the prong's subtree.

Explicit copies and snapshots that inherit accumulator values retain the view's replay frames. Descendant
provenance resolves to original source frames after the view and original source close. Copies without
accumulator provenance do not enable replay merely because the source has an observer.

The **13 new cases per framework** cover independent before/after values, caller-thread and single rule
evaluation, nested forks, snapshots and both copy paths after closure, descendant text values, applicability,
header-only access without input reads, multiple accumulators, 1,000 nodes with prongs at different positions,
copying without replay, collection of consumed source frames while the observer remains live, ancestor boundaries,
deferred failures and cyclic dependencies. Full Release units pass
**1,807 net10.0 / 1,789 net8.0**; the expanded Debug subset passes **1,025 / 1,009**. A NativeAOT probe passes
the saved grouping/merge/accumulator/iterate/replay checks and directly exercises fork accumulator lookup and
copied descendant provenance after source closure with dynamic code disabled. Publication reports no trim/AOT
warnings. This still does not exercise saved stylesheet fork lowering.

Compatibility checks pass: upstream **1,654/framework**, differential **394/framework**, and all **35** normal
W3C harness tests with saved-assembly sampling. Normal XSLT remains **8,845/22/42** (34 wrong-error-code passes),
QT3 **21,884/0/99** (67), unstreamed development **2,689/22/1** (seven), and the selected strict probe
**1,794/242/1** (fourteen). No conformance baseline changed. These checks preserve the preceding results;
they do not establish the full strict-streaming exit or a retained-byte budget.

The final three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time **0.986**
over 39 rows, maximum reported allocation ratio **1.033** (cold start). Compilation ratios include dispatch
**1.065**, grouping **0.975**, sorting **0.865** and keys **0.652**. These differing compilation results do not
resolve the variation recorded in preceding increments. Both sides use the same current harness and thresholds.
This gate does not measure fork throughput, retained bytes or first-output latency.

S6 still needs coordinated global/document caches and copy registries, isolated runtime stacks/settings and
captured function bindings, compiler lowering, ordered push/buffered output, group-by/sorted groups, and
stylesheet-level error/cancellation and memory checks. S1/S2 and remaining S3/S4 work, and S7's budgets and
feature claim, also remain open. The normal profile continues to report no streaming support.


### S6 shared global bindings

**Development increment, 2026-10-23.** `XmlQueryRuntime.ShareGlobalBindings` now promotes the invoking runtime's
existing global values and failures into a shared coordinator before worker threads start. Separate runtimes
claim each unevaluated binding once and retain the same result object. Other prongs wait for its value or
original exception. Cache locks never span evaluation. Cancellation wakes waiters without releasing somebody
else's claim. A wait graph detects cycles involving multiple threads/globals; forks created during a global's
evaluation inherit that dependency instead of trying to evaluate it again or waiting for their own caller.

Claimed global evaluations use the transformation's cancellation token, restoring the requesting prong's token
on success or failure, including nested globals. Cancelling a following prong therefore cannot abort a shared
global needed by an earlier prong. Transformation cancellation still reaches the computation. Bindings already
being evaluated when sharing starts preserve the invoking thread's cancellation context and ownership.

Generated global methods now publish failures through `GlobalValueFailed` before rethrowing. Repeated demands
preserve the original error instead of reporting a spurious circular definition or stranding a waiter. This
generated-runtime ABI addition is tracked in `PublicAPI.Unshipped.txt`; the static-data format remains 5.
Ordinary execution keeps its per-runtime array path. Independent transformations retain separate bindings.

Broader Debug verification also exposed two issues that are fixed: union-pattern `next-match` dispatch reused
one literal QIL node across invocations, and loader error handling queried the stylesheet version after malformed
XML failed before version discovery. Existing union-dispatch and mutated-stylesheet tests now pass in Debug.

The new global-binding class has **17 net10.0 / 15 net8.0** cases covering once-only evaluation and identity,
promotion of values/failures and active dependencies, independent transformations, debugger access, incompatible
or used runtimes, two-/three-thread cycles, cancelled waiters, failure publication, owner-only completion,
transformation/prong cancellation restoration and saved generated methods. Full Debug and Release unit suites
both pass **1,834 / 1,813**. A NativeAOT probe directly invokes a saved failing global concurrently on shared
runtimes, verifies repeated/original error identity, and passes with dynamic code disabled and no trim/AOT
warnings. The preceding saved streaming and direct fork accumulator probes also pass.

Upstream passes **1,654/framework**, differential **394/framework**, and all **35** normal W3C harness tests pass
with saved-assembly sampling. Normal XSLT remains **8,845/22/42** (34 wrong-error-code passes), QT3 **21,884/0/99**
(67), and the selected strict-forward probe **1,794/242/1** (fourteen).

The full unstreamed development profile also remains **2,689/22/1**, with seven wrong-error-code passes.
No conformance baseline changed; the full strict-streaming exit remains unproven.

The final three-pair ordinary-path gate against `0f2ddcb` passes with geometric mean head/base time **1.010**
over 39 rows and maximum reported allocation ratio **1.033** (cold start). Compilation ratios include dispatch
**1.114**, sorting **1.137**, grouping **1.043** and keys **0.961**. Dispatch/sorting outliers and the variation
across preceding increments remain investigation items; the aggregate pass does not resolve them. Both sides
use the same current harness and thresholds. This does not establish fork memory, throughput or output latency.

The sharing hook does not yet construct or run complete fork runtimes. Document identity/caches and result URI
claims, shared copy registries, independent output/call/source stacks and execution policies, captured functions,
compiler lowering, ordered push/buffered output and group-by/sorted groups remain open. S1/S2, remaining S3/S4
paths and S7's conformance, memory, throughput, latency and feature-claim gates also remain outstanding.


### S6 shared document resources

**Development increment, 2026-10-23.** `DocumentResources` now owns document identity/order, the available-document
pool, copied-node provenance and URI read/write claims for a transformation. Fresh runtimes can attach through
`ShareDocuments`. Concurrent requests for one resolved URI load it once and receive independent navigator
positions over the same document. Document-index assignment is synchronized; Xsl3 document identities retain
weak keys. Independent transformations keep separate pools and claims.

Resolver/parser work runs outside the cache lock. Waiters remain cancellable, while a claimed shared load uses
transformation cancellation so cancelling a following prong does not abort a document an earlier prong needs.
Success/failure restores the caller's token. Recursive retrieval, including cross-thread cycles, raises FODC0002.
Forks created during retrieval inherit that active load attempt's identity; a later retry of a failed or absent
resource is not mistaken for the previous dependency. Completed dependencies no longer participate in cycle
detection. Failed loads wake their existing waiters and do not invent a cached document.

URI claims now make read/write checks atomic. A pending open blocks a result write; an established write blocks
reads before the provider is called, and duplicate result claims raise XTDE1490. Once the provider returns a
readable resource, the URI remains recorded even if parsing/decoding fails. Doc/source/initial, text, JSON and
availability functions participate. XTDE1500 and cancellation are preserved rather than hidden by text-read
error conversion. Failed attempts that never obtain a readable resource release their reservation. Relative
targets accepted by a host result-document handler retain their explicit identity when no base URI was supplied.

Copy-origin storage is shared between runtime instances. Copies and snapshots made in prongs retain their
streamed accumulator values when the parent queries them after source closure. Lookup follows copy chains,
including a new copy of a previously copied descendant. The origin registry still uses weak document keys.

Hosted `xsl:evaluate` now uses the host document pool and URI claims. Its focus travels through the existing
explicit context-item parameter; treating it as another principal input could replace an existing URI mapping
with a temporary context tree. Actual stylesheet tests verify both document identity and focus preservation,
and prevent an outer result-document write after a hosted document read.

The **34 new tests per framework** cover concurrent loads/IDs, independent positions, URI aliases, load/write
races, duplicate writes, unavailable and malformed resources, failure fan-out, recursive/finished/inherited load
dependencies, cancellation ownership, streamed/initial ownership and claims after closure, text/JSON/probe
functions, independent transformations, sharing guards, hosted evaluation and cross-runtime copy chains.
Full Debug and Release units pass **1,884 net10.0 / 1,863 net8.0**. A NativeAOT probe passes shared document
identity and URI-claim checks, parent lookup of prong-copy accumulator values and repeated-copy provenance,
alongside the prior saved streaming/global probes. Dynamic code is disabled and no trim/AOT warnings are reported.

Compatibility checks pass: upstream **1,654/framework**, differential **394/framework**, and all **35** normal
W3C harness tests with saved-assembly sampling. Normal XSLT remains **8,845/22/42** (34 wrong-error-code passes),
QT3 **21,884/0/99** (67), unstreamed development **2,689/22/1** (seven), and the selected strict-forward probe
**1,794/242/1** (fourteen). No conformance baseline changed; these results do not prove the full strict-streaming exit.

The final three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time **1.015**
over 39 rows and maximum reported allocation ratio **1.033** (cold start). Time outliers include cold grouping
**1.151**, 1 KB sorting **1.292** and 1 MB JSON **1.108**. Compilation ratios include keys **1.062**, dispatch
**1.023**, grouping **1.000** and sorting **0.998**. These outliers and preceding variation remain investigation
items despite the aggregate pass. Both sides use the same current harness and thresholds. This does not measure
fork throughput, retained bytes or first-output latency.

S6 still needs complete runtime construction and execution-policy propagation, external-entity resolver tracking
and provider callback policies, key/grounded-accumulator cache coordination, captured-function ownership,
compiler lowering, ordered push/buffered output, group-by/sorted groups and stylesheet-level failure/cancellation
checks. Remaining S1/S2 and S3/S4 paths, and S7's retained-memory, throughput, latency and feature-claim gates,
also remain open. The normal profile continues to report no streaming support.


### S6 external-entity resource tracking

**Development increment, 2026-10-23.** Processor-created XML readers now track actual external DTD/entity opens
through `ResourceResolver`. They wrap their effective configured resolver, preserving explicitly disabled,
ignored/prohibited and legacy reader policies. The new `XmlReaderSettings.GetXmlResolver_CheckConfig` bridge is
verified on .NET 8 and 10 and uses the platform's configuration/default-resolver rules. URI resolution,
capabilities and credentials delegate to the original provider. Wrappers can be reused within a transformation
or retargeted without retaining another transformation's claims.

An entity open reserves its URI before the provider runs and records a read when a readable resource is
returned. Internal entities, unused declarations and ignored DTDs do not invent external reads. Streams and
text readers remain parser-owned and are not cached. Read-time cancellation captures the requesting scope's
token; asynchronous reads combine it with their explicit caller token. Async entity opens retain their claim
across the provider await. Cancellation and XTDE1500 wrapped by the XML parser keep their original error rather
than being converted to FODC0002/FODC0006. Provider calls and entity I/O run outside the URI-claim lock, on the
requesting thread; no provider-serialization policy is added.

Tracking covers materialized document loads, forward source/initial readers, `parse-xml`, legacy unparsed-entity
metadata reads and serialization parameter documents. Caller-owned readers keep their supplied parsing behavior;
the processor does not rewrite their internal resolvers. Materialized loads now also close an owned stream if
reader construction fails and preserve a parse/control error when cleanup throws. A close failure after
successful parsing remains visible.

The increment adds **38 tests per framework**, including the bridge check: actual DTD/entity expansion with
streams/text readers; borrowed initial streams; configured, disabled and ignored resolver policies; absent
phantom reads; pending-read/write conflicts; parser-wrapped cancellation/errors; async opens and both cancellation
tokens; cleanup failures; retargeting; `parse-xml`, unparsed-entity lookup and parameter-document claims.
Full Debug units pass **1,922 net10.0 / 1,901 net8.0**; full Release units pass the same counts. A saved streamed
stylesheet expands an external DTD/entity under NativeAOT, checks stream closure, and verifies XTDE1500 through
the forward reader and `parse-xml`. The prior native probes also pass. Dynamic code is disabled and no trim/AOT
warnings are reported.

Compatibility checks pass: upstream **1,654/framework**, differential **394/framework**, and all **35** normal
W3C harness tests with saved-assembly sampling. Normal XSLT remains **8,845/22/42** (34 wrong-error-code passes),
QT3 **21,884/0/99** (67), unstreamed development **2,689/22/1** (seven), and the selected strict-forward probe
**1,794/242/1** (fourteen). No conformance baseline changed; the strict probe still reports known failures.

The final three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time **1.022**
over 39 rows and maximum reported allocation ratio **1.033** (cold start). Time outliers include 1 KB sorting
**1.292**, 1 MB regex **1.068** and cold grouping **1.066**. Compilation ratios include dispatch **1.046**,
keys **1.033**, grouping **1.021** and sorting **0.919**. The recurring small-input sorting outlier and prior
variation remain investigation items despite the aggregate pass. Both sides use the same current harness and
thresholds. This does not establish fork throughput, retained-memory limits or first-output latency.

This does not complete S6: runtime construction/policy propagation, key and grounded-accumulator coordination,
captured-function ownership, compiler/output lowering, group-by/sorted groups and stylesheet-level checks remain.
The legacy unparsed-entity URI re-read path is tracked but must be replaced by metadata captured from the original
input before that path can satisfy the streaming memory gate. Remaining S1/S2 and S3/S4 paths, and S7's memory,
throughput, latency and feature-claim gates, also remain outstanding.

### S6 isolated fork runtimes and execution scopes

**Development increment, 2026-10-23.** `CreateForkRuntime` constructs a prong runtime from the same compiled
static data and dispatcher. It shares transformation globals, document identity, URI claims and copy provenance;
output stacks, source ownership, mutable library caches, query name tables and extension-binding caches stay
local. The global context item keeps its original identity and gets an independent navigator position. Creating
a prong never reparses that item, reopens a pending initial source or registers another principal document.
Initial-selection identity remains available for accumulator applicability, but a prong cannot acquire the
parent's forward cursor through the initial-source entry point: its streamed focus must be passed through
the tee view. Prong cleanup closes its own sources, including on failure, without closing borrowed parent input.

`RunFork` applies a snapshot of the invoking execution scope and restores the worker's previous scope on
success, cancellation or failure. It carries document/extension/environment/dynamic-transform permissions,
result callbacks, base output URI, package library, regex timeout, current recursion depth, annotation stripping,
serializer thread state and cultures. Transformation and prong cancellation tokens both apply. The coordinator
must provide the prong's own principal writer; the parent's writer is not installed on a worker. Runtimes execute
once. Trace/try stacks start with the enclosing context and remain independent. Current date/time, implicit
timezone and the default random seed are stable across the transformation. Early-bound extension objects keep
one identity, with coordinated first construction; late-bound objects still come from the supplied argument list.
Global failures record their computing stack before publication. Weakly keyed synchronization protects shared
exception stack annotations, so waiting prongs preserve the original failure and its computing context.

Reader policies are copied with separate name tables and retain their configured resolver. The legacy
`XmlTextReader` policy now preserves `DtdProcessing.Ignore`, which previously became `Parse` when captured.
Ignored external DTDs are neither opened nor claimed, for both ordinary and forked reader settings.

Captured compiled functions from the same fork family dispatch in the calling prong's runtime. Captured built-in
functions preserve their static base URI, collation and time context while using that runtime's resource/order
services. Function coercion is represented by an explicit function item instead of a delegate closing over the
parent runtime; its target is called through the current library. Unrelated compiled units retain their own
dispatcher. Isolation of captured foreign-unit runtimes remains work before complete stylesheet fork lowering.
Captured source-node bindings also need mapping to each tee view when that lowering constructs its prong functions.

The increment adds **24 net10.0 / 23 net8.0** unit cases for runtime construction, parallel shared globals and
document loads, reader policies, policy/culture restoration, both cancellation tokens before and during entry,
recursion depth, principal-writer isolation, stable dynamic context, trace/error identity, source closure,
initial-selection ownership, captured/coerced functions, early-bound object identity and shared-exception
metadata publication (including an unavailable metadata getter). Full Debug and Release units pass
**1,946 net10.0 / 1,924 net8.0**. Actual compiled absorbing functions, including a captured inline
function, construct separate results from two bounded tee views while using the original global context and
one shared document. That probe passes dynamically on both frameworks, through a saved assembly on net10.0,
and under NativeAOT with dynamic code disabled. The earlier native streaming/resource probes also pass;
no trim/AOT warnings are reported. This exercises runtime composition, not stylesheet `xsl:fork` lowering.

Compatibility checks pass: upstream **1,654/framework**, differential **394/framework**, and all **35** normal
W3C harness tests with saved-assembly sampling. Normal XSLT remains **8,845/22/42** (34 wrong-error-code passes),
QT3 **21,884/0/99** (67), unstreamed development **2,689/22/1** (seven), and the selected strict-forward probe
**1,794/242/1** (fourteen). No conformance baseline changed; the strict probe's known failures remain.

The final three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time **0.994**
over 39 rows and maximum reported allocation ratio **1.033** (cold start). Time outliers include keys compilation
**1.256**, join compilation **1.109** and 1 MB grouping **1.073**. Other compilation ratios include dispatch
**1.017**, grouping **1.002** and sorting **1.011**; 1 KB sorting is **0.920** in this run. These outliers and
the preceding variation remain unresolved. Both sides use the same current harness and thresholds. The gate
does not establish fork throughput, retained-memory bounds or first-output latency.

This does not complete S6. Compiler/output coordination, grounded key/accumulator caches, foreign-unit function
ownership, provider callback concurrency, group-by/sorted groups and stylesheet-level error/cancellation checks
remain. Cross-prong catch locations and validation propagation still need coverage with that lowering. Original
input unparsed-entity metadata capture and the remaining S1/S2, S3/S4 and S7 gates also remain outstanding.

### S6 shared key indexes and cross-cache dependencies

**Development increment, 2026-10-23.** `CreateForkRuntime` now promotes key indexes from the invoking runtime:
completed indexes, failed builds and builds already in progress retain their identity/state. A key/document
pair is built once across prongs. Only a completed index is published; waiters never receive a partial index.
Build callbacks run outside the cache lock. The owner uses transformation cancellation until publication,
then restores its prong token; a cancelled waiter does not release or poison another prong's build.

Generated key-build IL now publishes errors through `IndexValueFailed` before rethrowing. Repeated demands
preserve the original error instead of mistaking an abandoned build for a circular definition. The helper is
tracked in `PublicAPI.Unshipped.txt` for saved assemblies. Build loops use locals rather than leaving call
arguments on the evaluation stack across the exception scope, and index ids are assigned before generating
nested index expressions. Failure-publication coverage applies to newly generated bodies; older saved bodies
retain their existing build code and need compatibility handling before foreign-unit fork ownership is enabled.

Published indexes are immutable. Each lookup receives its own sequence and navigator positions, so sorting,
sequence reuse or navigation in one prong cannot change another's result. Shared caches use the document-identity
mapping already used for document ordering. Xsl3 trees are weak keys with dependent values: even an index whose
nodes point back to its document does not keep an otherwise dead temporary tree alive. Promotion releases the
parent's old strong cache lists. Foreign navigator identities retain the comparer's existing retention behavior.
The runtime's single reusable index-root navigator may retain its most recent document.

`EvaluationWaitGraph` tracks dependencies across globals, document loads and keys. It detects cycles across
cache kinds as well as within one kind, and fork creation captures the invoking thread's active dependencies.
Completed attempts stop participating in the graph even before a notified waiter resumes. The graph never
acquires a cache lock or runs provider/user code. Cache monitors still own value publication and cancellation
wakeups. Global bindings use this shared graph rather than a separate cycle-detection loop.

The increment adds **17 net10.0 / 16 net8.0** tests: once-only compiled key callbacks; promotion; component/tree
boundaries; original failures; inherited active builds; waiter and owner cancellation; key/key, key/global,
key/document and global/document cycles; lookup isolation; and collection of completed temporary trees.
Generated failure recovery passes dynamically on both frameworks and through saved code on net10.0.
Full Debug and Release units pass **1,963 net10.0 / 1,940 net8.0**.

A NativeAOT probe runs saved key bodies in three isolated runtimes, checks publication and independent lookup
positions, and verifies that two caught key-build failures retain FORG0001. Direct lookup inspection uses
`X31KeyValues`, the generated code's canonical encoder. All preceding native probes also pass, with dynamic
code disabled and no trim/AOT warnings. This verifies runtime composition and the saved helper ABI, not
stylesheet `xsl:fork` lowering.

Compatibility checks pass: upstream **1,654/framework**, differential **394/framework**, and all **35** normal
W3C harness tests with saved-assembly sampling. Normal XSLT remains **8,845/22/42** (34 wrong-error-code passes),
QT3 **21,884/0/99** (67), unstreamed development **2,689/22/1** (seven), and the selected strict-forward probe
**1,794/242/1** (fourteen). No conformance baseline changed; the strict probe's known failures remain.

The final three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time **1.004**
over 39 rows and maximum reported allocation ratio **1.033** (cold start). Time outliers include 1 MB iterate
**1.166**, 1 KB regex **1.125**, 1 MB regex **1.099** and join compilation **1.061**. Other compilation ratios
include keys **0.895**, dispatch **1.027**, grouping **1.019** and sorting **0.999**; 1 MB keys is **1.006**.
These outliers and the preceding variation remain unresolved. Both sides use the same current harness and
thresholds. The gate does not establish fork throughput, retained-memory budgets or first-output latency.

S6 still needs grounded-accumulator coordination, captured foreign-unit/source-node ownership, provider callback
concurrency, compiler/output integration, group-by/sorted groups and stylesheet-level checks. Automatic path-index
retention also needs review. Original-input unparsed-entity metadata capture, remaining S1/S2 and S3/S4 paths,
and S7's memory, throughput, latency and feature-claim gates remain open. No full streaming claim is made.

### S6 shared grounded accumulator values

**Development increment, 2026-10-23.** `GroundedAccumulatorValues` replaces the separate phase, fault, active-build
and source-applicability dictionaries. Ordinary execution and fork runtimes use the same state model. Fork
creation promotes existing completed, failed and active computations without evaluating them again, and shares
subsequent source-document applicability updates. Initial-selection restrictions and copy/validation provenance
continue to apply before value lookup. Streamed inputs still use their source observer, with no hidden grounding.

Only one prong evaluates an accumulator over a grounded document. Callbacks run outside the cache monitor.
Unrelated prongs wait for completion; recursive/inherited demands may use a phase already recorded, but an
undefined self-dependency raises XTDE3400. Circular faults invalidate recorded phases; an ordinary later fault
preserves earlier defined phases and its original exception for undefined demands. Accumulator dependencies
participate in `EvaluationWaitGraph` with keys, globals and document loads. Promotion captures active computations
for child prongs, including a fork started during accumulator evaluation.

Shared evaluation uses transformation cancellation and restores the prong token afterward. Waiter cancellation
does not poison the owner. Cancelled/fatal computations remove their partial state and release waiters; a later
uncancelled request can evaluate afresh. Published values keep node identity with separate navigator positions.
Native document identities are weak keys with dependent values, including values that point back to their own
document. Applicability metadata has the same lifetime. Foreign trees retain the document comparer's existing
identity/retention behavior.

Traversal now uses explicit heap frames, preserving start/child/end order without recursive CLR calls per XML
level. Cancellation checks also cover accumulators with no matching rules. Evaluation restores construction
output/depth, annotation scopes, pending type state and trace/try bookkeeping, and closes only sources opened
inside that evaluation. This matters when a later rule fails but an earlier requested phase is valid: returning
that phase must not leave a partially constructed tree or an owned input behind.

The increment adds **31 net10.0 / 29 net8.0** tests covering once-only evaluation, promotion, partial/circular
errors, cancelled retries, both cancellation scopes, cross-cache cycles, inherited dependencies, applicability,
copy provenance, node positions, temporary-tree collection, construction/source cleanup and a 20,000-level tree.
Actual compiled rules share values across three isolated runtimes, both before and after parent evaluation;
registered callback counts verify once-only computation. Saved assemblies exercise the same paths on net10.0.
Current full Debug and Release suites pass **2,011 net10.0 / 1,985 net8.0**, including other workspace tests.

A NativeAOT probe executes the saved grounded rules across three runtimes with and without prior evaluation,
checks all before/after values, and passes alongside the earlier native streaming/resource probes. Dynamic code
is disabled and no trim/AOT warnings are reported. These checks verify runtime composition rather than compiled
stylesheet `xsl:fork` lowering or S7's quantitative memory/throughput/latency budgets.

Compatibility checks pass: upstream **1,654/framework**, differential **394/framework**, and all **35** normal
W3C harness tests with saved-assembly sampling. Normal XSLT remains **8,845/22/42** (34 wrong-error-code passes),
QT3 **21,884/0/99** (67), unstreamed development **2,689/22/1** (seven), and the selected strict-forward probe
**1,794/242/1** (fourteen). No conformance baseline changed; the strict probe's known failures remain.

The final three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time **0.999**
over 39 rows and maximum reported allocation ratio **1.033** (cold start). Time outliers include 1 KB regex
**1.146**, 1 MB iterate **1.130**, dispatch compilation **1.082**, iterate compilation **1.067** and strings
compilation **1.055**. Other compilation ratios include keys **0.628**, sorting **0.876** and grouping **1.043**.
These outliers and preceding variation remain unresolved. Both sides use the same current harness and thresholds.
This ordinary-path gate does not measure grounded-accumulator throughput or establish fork memory/latency budgets.

S6 still needs captured foreign-unit/source-node ownership, provider callback concurrency, compiler/output
integration, group-by/sorted groups and stylesheet-level checks, including cross-prong error locations and
validation propagation. Automatic path-index retention, original-input unparsed-entity metadata capture,
remaining S1/S2 and S3/S4 paths, and S7's final budgets and feature-claim gates remain open.

### S6 captured source binding projection

**Development increment, 2026-10-23.** `ProjectForkBindings` projects an explicitly supplied captured graph onto
one prong's logical input view before user code runs. It maps streamed node identities to local boundary and
ancestor headers, preserving attribute/namespace positions and annotations. An already-inspected first child
is obtained from that prong's tee, without navigating the parent reader. Header-only projection does not demand
input events. Past positions and separate live streamed documents fail with the existing stream-position error;
projection never copies an input subtree to make an unavailable position appear usable.

Maps, arrays, same-family compiled closures, coercion wrappers, built-in function resource contexts,
tunnel/deferred parameters and grounded group/merge-group bindings receive local values. Repeated function
references keep their aliasing inside the projected graph, including a closure captured through its own map.
Every copied navigator slot has a private cursor with the original node identity. The source graph is unchanged;
in particular, projection does not populate a parent's lazy grouping-key or member-node cache.

Sequence shells and queued work handle recursive closure graphs and deeply nested values without recursive CLR
calls. Cancellation covers empty bindings and graph traversal. There is no runtime projection cache: retained
state follows the explicitly captured value graph, rather than a document-wide node registry. Projection visits
boxed bindings; it does not evaluate a suspended population or invoke a captured function.

This is an internal runtime-composition primitive. The compiler does not yet call it for stylesheet `xsl:fork`.
Foreign-unit compiled functions and suspended streaming pipeline state are explicitly unsupported by this helper;
their dispatcher/resolver ownership and continuation rebinding still need integration. Explicit source replay
positions outside the local view also need handling; ordinary grounded copies already receive private navigators.
These limitations must be resolved before claiming complete fork capture support.

The increment adds **14 net10.0 / 13 net8.0** tests. They cover node/attribute/namespace identity, ancestor headers,
inspected-child projection, header-only input behavior, past/unrelated inputs, nested forks, recursive closures,
tunnel/deferred/group values, private grounded cursors, built-in static context, cancellation and 20,000-level
array/tunnel structures. Actual compiled and saved function bodies capture a streamed node through a map and
array; two prongs invoke plain/coerced projected closures and independently consume their tee views. Current full
Debug and Release unit suites pass **2,025 net10.0 / 1,998 net8.0**, including other workspace tests.

A NativeAOT probe runs the saved source-capturing body in two isolated runtimes, including its coercion wrapper,
and passes with dynamic code disabled alongside the earlier streaming/resource probes. No trim/AOT warnings are
reported. The Release solution build succeeds with 30 existing warnings. Compatibility checks pass upstream
**1,654/framework**, differential **394/framework**, and all **35** normal W3C harness tests with saved-assembly
sampling. Normal XSLT remains **8,845/22/42** (34 wrong-error-code passes) and QT3 **21,884/0/99** (67).
The selected strict-forward probe remains **1,794/242/1** (fourteen) and unstreamed development
**2,689/22/1** (seven). No conformance baseline changed in this increment; the strict probe's known failures remain.

The final three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time **0.973** over
39 rows, maximum reported allocation ratio **1.033** (cold start). Time outliers include 1 MB iterate **1.116**,
cold start **1.112** and dispatch compilation **1.076**. Keys compilation is **0.691**, sorting compilation
**0.844** and grouping compilation **1.022**. These outliers and preceding variation remain unresolved. Both
sides use the same current harness and thresholds. This gate does not measure binding-projection overhead,
fork throughput, retained-memory budgets or first-output latency.

Remaining S6 work includes foreign-unit and suspended-source ownership, provider callback concurrency, compiler
lowering/output coordination, group-by/sorted groups and stylesheet-level error-location/validation checks.
Automatic path-index retention, original-input unparsed-entity metadata, remaining S1/S2 and S3/S4 paths, and
S7's quantitative budgets and feature-claim gates also remain open. This increment does not complete Phase 7.

### S6 prepared binding snapshots and retained headers

**Development increment, 2026-10-23.** `PrepareForkBindings` copies a captured graph on the invoking thread before
prongs start. It freezes collection contents, compiled closure captures, cursor positions and built-in contexts.
Workers project that prepared graph onto their own runtime and input view. Live content positions continue to
use the tee. Retained attributes and namespaces use metadata-only positions when their source element has passed
or belongs to another/closed source; no parent reader is accessed or reopened.

The frozen data preserves node identity, attribute typing/defaults, namespace scope, base URI, language and
ancestor headers. Preparation shares ancestor snapshots within the graph. It keeps neither original source
frames nor their replay buffers. Worker header registries are also temporary: returned positions retain their
ancestor chain and a metadata-only document root, rather than a document-wide registry of captured nodes.
Header content axes and grounding fail with the existing position error; snapshots do not make unavailable
subtree content consumable. Ordinary grounded values retain their existing copy behavior.

The same graph visitor performs preparation and projection, preserving recursive closure/map relationships and
aliases while creating private navigator positions. Queued graph work and iterative ancestor traversal avoid CLR
recursion. Cancellation covers graph work, empty captures and ancestor traversal. Copied header names now use
each prong's own name table, including declared/inherited prefixes and the implicit `xml` namespace.

The increment adds **14 tests per framework** for closed and passed sources, caller mutations after preparation,
private positions, document identity, metadata-only navigation, typed default attributes, unsupported content,
20,000-level ancestor chains, source-frame collection, namespace atomization, early foreign-unit rejection and
cancellation. Existing recursive closure, tunnel/deferred/group, built-in context, 20,000-level container and
compiled/saved closure tests now exercise both preparation and projection. Full Debug and Release unit suites
pass **2,039 net10.0 / 2,012 net8.0**, including other workspace tests.

A NativeAOT probe executes the saved generic binder with prepared live content and retained closed-source
attributes inside a map/array closure, across two prong runtimes. It also checks namespaces, name atomization,
content rejection and that an unrelated prong input remains unread. All earlier native probes pass with dynamic
code disabled and no trim/AOT warnings. The Release solution build succeeds with 30 existing warnings.
Compatibility checks pass upstream **1,654/framework**, differential **394/framework**, and all **35** normal W3C
harness tests with saved-assembly sampling. Normal XSLT remains **8,845/22/42** (34 wrong-error-code passes), and
QT3 **21,884/0/99** (67).

The selected strict-forward probe remains **1,794/242/1** (fourteen wrong-error-code passes) and unstreamed
development **2,689/22/1** (seven). No conformance baseline changed in this increment. The strict profile's
known failures remain; these harness checks do not establish complete fork support.

This remains runtime composition, not stylesheet `xsl:fork` lowering. Retained ancestor accumulator phases outside
an available view, foreign compiled units, suspended streaming pipelines and provider callback concurrency still
need integration. Compiler lowering/output coordination, group-by/sorted groups, error-location/validation checks,
automatic path-index retention, original-input unparsed-entity metadata, remaining S1/S2 and S3/S4 paths, and S7's
quantitative budgets and feature-claim gates remain open.

The final three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time **0.996** over
39 rows and maximum reported allocation ratio **1.034** (cold start). Time outliers include 1 KB regex **1.167**
and dispatch compilation **1.072**. Keys compilation is **0.802**, sorting compilation **1.001** and grouping
compilation **0.991**. These outliers and preceding variation remain unresolved. Both sides use the same current
harness and thresholds. This gate does not measure preparation/projection throughput, header retention in bytes,
fork throughput or first-output latency; S7's quantitative budgets remain open.

### S6 compiled fork prongs and ordered output

**Development increment, 2026-10-23.** The streaming compiler now lowers `xsl:fork` into lifted prongs and calls
the fork runtime instead of concatenating prongs serially. The invoking thread prepares captured bindings and
constructs isolated runtimes; each worker executes one prong over its local tee view. Grounded/atomic/absent
focuses use the same isolated execution without a source tee. Frozen text/comment/PI values can be projected
without another input reader. Captured focus and position are preserved. Nested forks and saved assemblies use
the same generated/runtime contract.

`X31Fork` is a writer constructor in QIL, so a fork used as a variable value receives ordinary sequence-construction
caching rather than writing into the surrounding result. `X31ForkPush` lowers a prong body directly to its local
writer while the function dispatcher returns an empty sequence. Writer locals are initialized explicitly for
these otherwise pull-style functions. Atomic normalization remains at the normal enclosing expression boundary.
The generated ABI is recorded in `PublicAPI.Unshipped.txt`; saved IL records the configured buffer capacity.

`ForkCaller` serializes source reads and first-prong output operations on the invoking thread. Each worker has
one synchronous request outstanding. Callback failures return their original exception to the worker; ordered
fault selection, cancellation and joining remain in `ForkProngs`. Running callbacks finish before cancellation
or disposal can release a worker's reusable argument buffer. Nested requests on the invoking thread run inline.
The tee stops requesting input when no readers remain.

Result-document opener, text-handler and tree callbacks also run on the original invoking thread, inherited
through nested forks. This preserves thread-local host callback state and serializes callback invocation. It
fixed null references in the strict runner's result-tree registry, which was unset on worker threads. Resolver
and extension-callback ownership still needs a separate integration pass.

The first prong relays raw writer events, rather than collecting its whole result before publication. Later
prongs buffer output in their private sequence writers and replay it in declaration order. Streamed nodes sent
to an output sink are consumed on their worker: moving that navigation into a caller callback could deadlock
behind another worker's queued source request. Type annotations, IDs and IDREFs have relay operations, and typed
result-sequence nodes are verified. `XsltSettings.StreamingBufferCapacity` configures the positive event-ring
capacity (default 64); this bounds input event count, not event bytes or later-prong output.

The increment adds **24 net10.0 / 23 net8.0** tests covering compiled multiple-consuming prongs, first-output
progress and caller-thread affinity, variable values, atomic spacing/focus, text focus, nested/saved forks,
transmitting output, grounded input, absent context, typed result nodes, secondary result documents, explicit
unsupported-principal diagnostics, capacity validation, callback failures, cancellation, disposal and joins.
Current full Debug and Release suites pass **2,109 net10.0 / 2,077 net8.0**, including concurrent aggregate and
other workspace changes. Four aggregate-navigation cases failed during an earlier run, then passed after the
shared workspace changed; this increment did not edit those aggregate files.

A NativeAOT probe now executes an actual saved stylesheet containing two consuming fork prongs over a forward
reader. It checks ordered results and borrowed-reader ownership. It passes alongside all earlier native probes
with dynamic code disabled and no trim/AOT warnings. The Release solution build succeeds with 30 existing
warnings. Normal W3C harness tests pass **35/35** with saved-assembly sampling; normal XSLT remains
**8,845/22/42** (34 wrong-error-code passes) and QT3 **21,884/0/99** (67).

This does not complete S6. A principal `xsl:result-document` without href inside a prong currently raises the
explicit `streaming-unimplemented` control error; it must receive ordered principal-result coordination before
support can be claimed. Foreign units, suspended streaming pipeline captures, provider callback concurrency,
retained ancestor accumulator phases, group-by/sorted groups, output/error/validation edge cases and final
stylesheet coverage remain open. Nested-parent annotation-copy behavior also needs a separate audit: exploratory
cases lost annotations on both the ordinary and fork paths, while typed result-sequence nodes pass on both.
Automatic path-index retention, original-input unparsed-entity metadata, remaining S1/S2 and S3/S4 paths and
S7's quantitative memory/throughput/latency and feature-claim gates remain open.

Final strict-forward measurement: `si-fork` is **41 passed / 14 failed / 0 not run** (74.5%). The earlier
39/16 measurement had two null references from result-tree callbacks; both are resolved by invoking those
callbacks on the original thread. Remaining fork failures cover grouped populations/captures, source-position
constraints and text-node output semantics. The expanded strict selection (the earlier selected sets plus
`si-fork`) is **1,840/251/1**, with sixteen wrong-error-code passes. Its scope differs from the previous
1,794/242/1 selection. No conformance baseline was changed. Upstream remains **1,654/framework** and differential
**394/framework**. The final callback-coordination source passes full Debug and Release units and the native
real-stylesheet fork probe.
The unstreamed development reference remains **2,689/22/1** (seven wrong-error-code passes).

The final three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time **0.999** over
39 rows, maximum reported allocation ratio **1.034** (cold start). The largest timed ratio is grouping compilation
**1.032**; cold-start time is **1.034**. Dispatch compilation is **1.022**, keys compilation **0.963** and sorting
compilation **0.993**. No timed row exceeds 1.05 in this run; variation in earlier runs remains unresolved. Both
sides use the same current harness and thresholds. This gate does not measure fork throughput, event bytes,
retained memory or first-output latency. The first-output test is a functional ordering check, not S7's latency
measurement. Phase 7 remains incomplete.

### S6 fork grouping buffers and captured forward groups

**Development increment, 2026-10-23.** A non-forward grouping population inside a compiled fork now buffers each
streamed member before collecting grouping tuples. Group-by and sorted groups can subsequently navigate their
members. The buffer preserves original node identity, annotations, names and accumulator provenance; it does
not replace members with copied nodes. This buffering is specific to the explicitly permitted fork grouping
case. Ordinary forward grouping continues to pull one member at a time.

Fork preparation also supports a captured forward `current-group()`. It finishes that group on the invoking
thread, buffering each member before advancing to the next one and retaining the boundary lookahead for the
outer grouping iterator. Workers receive grounded group shells, never the parent's suspended grouping
continuation. Cursor coordinates are captured immediately, while replay metadata is frozen after all group
buffers have finished. Complete replay snapshots create separate documents and navigation state per prong.
Motionless captures that have no available content receive metadata-only positions; content requests still
raise `stream-position`.

A fork inside a stylesheet function now preserves absent dynamic context. Its input tee can instead use the
available streamed operand found in its captured graph. This fixes both a Debug compiler assertion and lost
function-operand navigation. Candidate source cursors are retained only for the current invocation; completed
buffers and unavailable positions are removed from that candidate list. Multiple independent captured forward
readers without an enclosing streamed context raise an explicit `streaming-unimplemented` diagnostic pending
coordinated input tees.

Mixed compact/streamed documents now share a native creation-order counter. Previously the comparer always
placed compact nodes before streamed nodes, reversing path-normalized members of mixed-document groups.
Replay and tee views inherit the source document's creation number. Ordinary compact-to-compact order and the
existing ordering of compact versus foreign trees are preserved.

New regression coverage checks sorted group-by, captured adjacent groups, functions with absent context,
multiple function prongs, saved assemblies, private replay cursors, original identity, typed/default attributes,
mixed-document order and published accumulator phases. The increment adds **14 net10.0 / 10 net8.0** tests.
Full Debug and Release unit suites pass **2,123 net10.0 / 2,087 net8.0**. The Release solution builds with
30 existing warnings. Three new NativeAOT probes execute saved grouped-function, captured-group and mixed-document stylesheets, alongside
all earlier probes, with dynamic code disabled and no trim/AOT warnings.

Strict `si-fork` coverage improves from **41/14/0 to 50/5/0** (90.9%). Remaining cases are recursive function
arguments whose source positions have already been lost before fork capture (`808`, `810`, `811`), adjacent
text-node result semantics (`809`) and an exact static-error gap (`953`). The latter expects
XTSE3430 for sorted groups; the runtime now returns a result where it previously raised an unrelated position
error that the runner counted as a wrong-error-code pass. No baseline or fixture was changed to hide it.

S6 remains incomplete: principal-result coordination, foreign units, suspended pipeline captures, resolver and
extension callback ownership, retained ancestor accumulator phases, output/error/validation edges and complete
stylesheet coverage remain open. S1/S2, remaining S3/S4 paths and S7's quantitative memory, throughput, latency
and feature-claim gates remain open. Recursive sequence-parameter lowering must be repaired before the three
remaining captured-group fixtures can stream successfully.

The final expanded strict selection is **1,858/233/1**, with fifteen wrong-error-code passes, compared with
1,840/251/1 before this increment. The earlier unstreamed reference remains **2,689/22/1** (seven wrong-error-code
passes); compact-to-compact ordering is unchanged. The full compatibility gates include the repository CI's
`category!=failing` upstream filter. An initial unfiltered run included four failing Windows-specific path
cases per framework on macOS; this increment does not change those existing platform tests.

Final compatibility results on the ordering fix: upstream **1,654/framework** with the CI filter, differential
**394/framework**, and normal W3C harness **35/35** with saved-assembly sampling. Normal XSLT remains
**8,845/22/42** (34 wrong-error-code passes) and QT3 **21,884/0/99** (67). The final NativeAOT binary passes all
probes, including grouped functions, captured groups and mixed-document order.

The final isolated three-pair ordinary-path performance gate against `0f2ddcb` passes: geometric mean
head/base time **1.018** over 39 timed rows; maximum reported allocation ratio **1.034** (cold start). Individual
timing regressions remain unresolved: keys compilation **1.215**, sorting compilation **1.075**, and 1 KB regex
execution **1.170**. Cold-start time is **1.011**, dispatch compilation **1.007**, and grouping compilation
**1.020**. All test and NativeAOT jobs had finished before the gate; both sides used the same current harness
and thresholds. The gate was not rerun. It measures the ordinary path, not fork throughput, replay-buffer bytes,
retained memory or first-output latency; S7's budgets remain unverified. Logs for this increment use the local
`/tmp/xsl3-p7-fork-groups-` prefix. Phase 7 remains incomplete.

### S6 principal result-document coordination

**Development increment, 2026-10-23.** `xsl:result-document` without href now works inside compiled fork prongs.
The real principal writer and its serialization configuration remain on the invoking thread. Each fork output
supplies a callback that projects prepared output values and applies the ordinary principal-result logic there;
nested forks relay through their parent callback. Prongs keep independent ordinary output writers. The previous
`streaming-unimplemented` guard has been removed.

The ordinary and fork paths share one principal-writing implementation. It retains the existing XTDE1490
checks for duplicate explicit claims and other principal output, and the dynamic temporary-output prohibition
still runs before a callback. Configurable text/stream destinations retain result-document settings; a supplied
XmlWriter keeps its fixed settings. When the API returns a raw XDM sequence with no serialization writer,
principal-document contents return through each prong's normal ordered result path. A writer failure returns
to the invoking prong and can be caught there. The callback uses the prong's linked cancellation token and
restores the caller's token on every exit.

Raw result-document items now buffer source content before the enclosing IList advances its population. This
also covers item-separated validation before document construction. Serialization preparation traverses maps
and arrays, freezes complete node replay data, and treats function values as descriptions rather than invoking
or rebinding their closures. The caller receives private replay positions, so principal serialization cannot
block the caller on a live worker cursor. The new generated helper `X31BufferResultItem` is recorded in the
unshipped ABI. These buffers hold result data explicitly; the bounded input tee remains separate.

The NativeAOT validation fixture exposed a saved-constant gap: a built-in-only validation instruction stores
an empty XmlSchemaSet. Constants format **3** now has an explicit empty-schema-set value. Nonempty schema sets
continue to raise the imported-schema rejection, with regression coverage. The reader retains version 1/2
support, and the final native probe executes an unchanged **format-2** saved fork alongside new format-3
stylesheets. Imported schemas remain outside the saved-assembly contract.

The increment adds **24 net10.0 / 20 net8.0** tests and replaces the old unsupported-principal test with the
ordinary conflict behavior. Coverage includes either prong position, nested forks, saved assemblies, XML and
JSON, item separators, adaptive function descriptions, raw XDM results, typed annotations, built-in validation,
caller-thread affinity, borrowed inputs/writers, caught writer errors, cancellation scope, structured values
and an early-output/remaining-input deadlock regression. Full Debug suites pass **2,147 net10.0 / 2,107 net8.0**.
The final NativeAOT binary passes all earlier probes plus saved principal XML, JSON and validated content,
without trim/AOT warnings.

Phase 7 remains incomplete. S6 still needs recursive sequence-parameter lowering, foreign-unit and suspended
pipeline captures, resolver/extension callback ownership, retained ancestor accumulator phases, remaining
output/error/validation edges and complete stylesheet coverage. Remaining S1/S2, S3/S4 and S7 quantitative
memory, throughput, latency and feature-claim gates remain open. This increment does not resolve the five
remaining strict fork fixtures or the earlier individual performance regressions.

Final Release units also pass **2,147 net10.0 / 2,107 net8.0**. The solution build succeeds with 30 existing
warnings. Upstream remains **1,654/framework** with `category!=failing`, differential **394/framework**, and
normal W3C harness **35/35** with saved-assembly sampling. Normal XSLT stays **8,845/22/42** (34 wrong-error-code
passes), QT3 **21,884/0/99** (67), and the expanded strict selection **1,858/233/1** (15). Strict `si-fork` remains
**50/5/0**. No baseline was changed. Principal claim conflicts caught across different prongs still need a
dedicated side-effect timing audit; the current tests establish duplicate rejection and delivery of writer
failures to the issuing prong. Result-document side effects in a failing try are implementation-dependent
([XSLT 3.0 §8.3](https://www.w3.org/TR/xslt-30/#element-try)).

The final isolated three-pair ordinary-path performance gate against `0f2ddcb` passes: geometric mean
head/base time **0.992** over 39 rows, maximum reported allocation ratio **1.035** (cold start). Individual
regressions remain unresolved: 1 MB iterate **1.166** and dispatch compilation **1.065**. Keys compilation is
**0.527** (base 0.615 ms, head 0.324 ms), with a substantially slower base compilation than earlier runs;
this variation is not evidence of an improvement. Sorting compilation is **0.983**, grouping compilation
**0.968**, and cold-start time **1.004**. All test and native jobs finished before the gate, both sides used the
same current harness and thresholds, and this final gate was not rerun. It does not measure principal/fork
throughput, replay-buffer bytes, retained memory or first-output latency. Logs use the local
`/tmp/xsl3-p7-fork-principal-` prefix. Phase 7 remains incomplete.

### S4 iterator-backed sequence parameters and recursive forks

**Development increment, 2026-10-23.** Direct calls to non-memoized absorbing stylesheet functions can now pass
node-sequence arguments through a suspended producer rather than collecting source positions before entry.
This applies to many-valued node-kind parameters whose type checks inspect metadata; document-element type
checks and other function categories retain their existing paths. The generated CLR function signature remains
IList-based; an invocation-local opaque argument carries the producer through that boundary.

Each parameter reference creates its own cursor. A forward argument retains one first header for lookahead,
not all input members; already-grounded values remain repeatable. Streamed attributes do not enter an unbounded metadata cache.
A cursor that tries to revisit unavailable source positions raises `stream-position`. Materialized inputs are
validated at entry. A producer checks its first item and minimum cardinality before the body, validates each
later item while pulled, and drains any remaining argument after constructing the grounded function result.
Conversion faults are retained so a catch inside the function cannot swallow a failed argument conversion.
Cancellation is checked at parameter boundaries and on each pull. Count/existence operations use parameter
metadata; compile-time weak annotations preserve lexical variable resolution.

A caller also carries a conservative proof when its argument is distinct, ordered and disjoint: singleton
nodes, child/attribute paths, existing disjoint populations, or a forwarded parameter retaining that proof.
Child paths rooted at such a parameter use a direct iterator chain. Other arguments retain document-order
normalization after each step, including reversed, duplicated and overlapping grounded sequences. The compiler
builds one complete chain for each branch rather than cloning a conditional chain at every step. Regression
coverage includes forwarding, recursive child navigation and a 32-step path. Arbitrary mixed or otherwise
unproven forward populations still need broader iterator integration; no ordering promise is inferred merely
from entering an absorbing function.

The error-code audit exposed a regression in `su-absorbing-910`: collecting intermediate child nodes raised
`stream-position` before its required `FOAR0001` division error. An isolated copy with the new parameter path
disabled reproduced the previous correct error. Carrying the ordering proof restores the expected error without
removing canonical sorting and deduplication from grounded calls.

Recursive and mutually recursive calls retain real function boundaries. The earlier finite-expansion lowering
continues to handle its remaining call shapes. No whole-input tree fallback was added. Generated helper
signatures are recorded in the unshipped ABI, and saved assemblies use the same runtime protocol.

The increment adds **26 net10.0 / 24 net8.0** regression cases for recursive/saved calls, mutual recursion,
empty and nonempty cardinalities, wrong arguments even when unused, deferred conversion faults caught by the
body, independent grounded readers, forward lookahead, explicit past-position rejection and a weak-frame
retention check on an attribute-only stream, child-path errors, forwarded ordering proofs, recursive child
navigation, deep paths and canonical grounded path results. Patterns rejected
by static analysis on both backends retain that rejection rather than being treated as runtime coverage.
Full Debug and Release suites pass **2,173 net10.0 / 2,131 net8.0**. NativeAOT executes saved recursive sequence parameters
and recursive grouped forks, plus child-path navigation through a sequence parameter, alongside all earlier
probes, with caller-owned inputs left open.

Strict `si-fork` now passes **53/55**: `808`, `810` and `811` no longer lose their argument nodes before group
capture. Their groups continue to use the explicit per-group replay buffers from S6. The remaining two fork
cases are `809` (adjacent text-node result semantics) and `953` (exact static error for sorted groups).

This does not complete S4 or S6. Other function categories, document-element type guards, dynamic calls and
memoization still need full iterator integration. Foreign-unit and suspended-pipeline captures across prongs,
provider callback ownership, retained ancestor accumulator phases and remaining output/error/validation work
remain open. S1/S2, remaining S3 paths and S7's memory, throughput, latency and feature-claim gates remain open.

Final compatibility gates pass: upstream **1,654/framework** with `category!=failing`, differential
**394/framework**, and the complete W3C harness **35/35** with saved-assembly sampling. The Release solution
build has zero errors and 30 existing warnings. Normal XSLT remains **8,845/22/42** with 34 wrong-error-code
passes; QT3 remains **21,884/0/99** with 67. The like-for-like strict selection is **1,862/229/1**, with fifteen
wrong-error-code passes, compared with **1,858/233/1** (15) before this increment. The original `su-absorbing-910`
error is again matched exactly. The selection uses `accumulator,merge,source-document,streamable,sf-,sx-,su-,`
plus `si-apply-imports,si-apply-templates,si-for-each-group,si-fork,si-iterate,si-merge,si-next-match`.

A broader strict probe that includes **all** `si-*` sets is **2,363/258/1**, with sixteen wrong-error-code
passes. Its additional unmatched code is `si-map-007`; this larger selection is a separate coverage measure.
Neither strict probe changes a conformance baseline or establishes the full streaming feature claim.

The final isolated three-pair ordinary-path performance gate against `0f2ddcb` passes: geometric mean
head/base time **1.010** over 39 rows, maximum allocation ratio **1.036** (cold start). Individual time
regressions remain unresolved: 1 KB regex **1.128** and dispatch compilation **1.059**. Keys compilation is
**0.944**, sorting compilation **0.927**, grouping compilation **1.004**, and cold-start time **1.010**.
All test and native jobs finished before measurement; both sides used the same current harness and thresholds,
and this final gate was not rerun. These ordinary-path measurements do not establish streamed parameter/fork
throughput, replay bytes, retained-memory budgets or first-output latency. Logs use the local
`/tmp/xsl3-p7-function-parameters-` prefix. Phase 7 remains incomplete.

### S1/S6 result-tree assertions, namespace origins and host map errors

**Development increment, 2026-10-23.** The fork audit found two distinct issues. `si-fork-809` constructed
its result correctly, but the harness parsed its text serialization as an XML fragment, losing the `out`,
`br` and separate text nodes. Tree assertions now use a cached annotated result tree, replayed through the same
entry point and source-reader factory. Explicit catalog `output/@serialize='yes'` keeps assertions on the
serialized representation; serialization assertions always inspect the original bytes. XML comparison avoids
`OuterXml` formatting and preserves attribute newlines. The per-test `bug-1901` skip and the type-expression
regular-expression heuristic were removed.

Tree replay returns independent navigator positions, retains failures and restores the original messages,
secondary-result observations and principal target. It runs once for all tree assertions in a case. Explicit
principal results remain attached to the replay tree, including principal writes nested inside secondary
results. The principal writer preserves annotations when its destination is a compact tree, using the existing
schema-aware node-copy implementation. Captured secondary trees retain their annotations and receive their
resolved output URI as their final document base, with `xml:base` resolved against it. This is result-tree
materialization, not a fallback that grounds the streamed input.

The broader assertion audit exposed a namespace-origin bug hidden by XML serialization: an inner
`inherit-namespaces='no'` element suppressed namespaces introduced by an outer constructor. Under
[XSLT 3.0 §5.7.1](https://www.w3.org/TR/xslt-30/#constructing-complex-content), an outer constructor propagates
its namespaces through descendants unless an intervening binding shadows that prefix. Compact scopes now
suppress the no-inherit parent's own bindings while retaining outer bindings. The writer records local origins
even when a required or explicit binding has the same prefix and URI as an ancestor, and explicit copies retain
the scope boundary. Regression coverage checks outer propagation, different and identical shadow bindings and
copied trees on ordinary and forward execution paths.

The error-code audit found that XPath map constructors inside XSLT still raised `XQDY0137`, preventing a catch
for `XTDE3365` from matching. [XSLT 3.0 §21.4](https://www.w3.org/TR/xslt-30/#map-constructors) specifies
`XTDE3365`; [standalone XPath 3.1](https://www.w3.org/TR/xpath-31/#id-map-constructors) retains `XQDY0137`.
An explicit host flag selects an XSLT map builder for stylesheet expressions, inline functions, static
expressions and `xsl:evaluate`. Existing generated helper signatures remain available; the new helper is
recorded in the unshipped ABI. Ordinary, forward, saved and native stylesheet paths retain the XSLT contract,
including failure propagation from fork prongs. This fixes the unmatched code in `si-fork-814`.

`si-fork-953` is a fixture conflict, correcting its earlier classification as a missing rejection:
[XSLT 3.0 §19.8.4.19](https://www.w3.org/TR/xslt-30/#streamability-xsl-for-each-group) explicitly exempts a direct
fork child from its sort prohibition, and §19.8.9.4 permits the fixture's single downward `current-group()` use.
The published errata page contains no correction to that rule. The fixture still expects `XTSE3430` and remains
a visible failure with a baseline comment; no rejection was added to match it. Static analysis and ordinary,
forward and saved execution have regression coverage for the supported sorted shape.

This increment adds **17 net10.0 / 15 net8.0** unit cases and **11** harness cases. Full Debug and Release unit
suites pass **2,190 / 2,146**. The native probe executes saved XSLT map errors, namespace origins and typed
principal-tree delivery, alongside all earlier probes, with dynamic code disabled and no trim/AOT warnings.
Strict fork coverage is **54/1/0**, with no unmatched error codes after the map fix. The like-for-like expanded
strict selection is **1,863/228/1**, with fourteen unmatched-code passes. The full unstreamed development
reference is **2,691/20/1**, with five unmatched-code passes; only the verified `si-fork-809` and
`si-value-of-102` fixes were removed from its baseline. Normal XSLT is **8,847/21/41** (34 unmatched codes),
including restored `bug-1901` coverage and corrected result-tree evaluation for `validation-0202`. Only `validation-0202` was
removed from the normal failure baseline. QT3 remains **21,884/0/99** (67 unmatched codes).

The harness still starts with a serialized execution, and logical-tree assertions use a second execution.
Serialization-only errors when no serialization was requested, non-repeatable external effects, raw-sequence
post-processing and complete secondary-result capture need further work. The tested replay preserves observed
messages and result dictionaries; it does not establish that arbitrary external effects are replay-safe.
S1/S2, remaining iterator and callback ownership paths, full error/validation coverage and S7's quantitative
memory, throughput, latency and feature-claim gates remain open. Phase 7 remains incomplete.

Final verification: the Release solution builds with zero errors and 30 existing warnings. Upstream passes
**1,654/framework** with `category!=failing`, differential **394/framework**, and the complete W3C harness
**46/46**. The eleven-case result-tree suite also passes with legacy invocation and saved assemblies enabled. The reviewed
normal and unstreamed development baselines pass with no newly failing IDs. NativeAOT executes all new and
previous probes, including the unchanged version-2 saved fork, without trim/AOT warnings. Borrowed inputs
remain open in the native checks.

The final isolated three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time
**1.027** over 39 rows, maximum allocation ratio **1.036** (cold start). Individual time regressions remain
unresolved: keys compilation **1.880** (base/head **0.343/0.645 ms**), sorting compilation **1.204**, 1 KB regex
**1.125**, and 1 KB dispatch **1.053**. Dispatch compilation is **1.046**, grouping compilation **1.048**, and
cold-start time **1.019**. All test and native jobs had finished; both sides used the same current harness and
thresholds, and the final gate was not rerun. This ordinary-path measurement does not establish streaming
throughput, retained-memory, replay-byte or first-output latency budgets. Logs and retained raw rounds use the
local `/tmp/xsl3-p7-result-tree-` prefix. Phase 7 remains incomplete.

The retained raw rounds show substantial compilation variation: keys base medians are
**0.3428 / 0.6364 / 0.3212 ms**, versus head **0.66135 / 0.64455 / 0.3490 ms**; sorting base medians are
**0.5158 / 0.6464 / 0.5264 ms**, versus head **0.6337 / 0.6436 / 0.5053 ms**. The cause and attribution remain
unresolved; the aggregate pass is not evidence that these individual regressions are fixed.


### S4 shared forward paths and incremental sequence inspection

**Development increment, 2026-10-24.** Compatible union branches now share one native QIL traversal of a
stable common anchor. Predicate-free child, attribute, self and descendant selections use one candidate scan
with axis and node-test masks; mixed attribute/content axes, independent constructors and predicated branches
retain their separate evaluation. Syntactic peer selections and iterator-parameter ordering proofs preserve
order without buffering. Unknown grounded anchors still sort and deduplicate, including reversed, repeated
and overlapping inputs. Lexical anchor equality alone does not establish ordering.

A descendant selection followed by fixed, predicate-free child steps now scans final candidates once and
matches their ancestor headers. Matching stops at the original anchor, including for descendant-or-self;
it cannot accidentally match ancestors outside the selection. Document-element tests, predicated steps and
unproved overlapping anchors retain their existing compilation. Failed child-chain shape analysis is cached
by AST identity to avoid rescanning every recursive prefix of a long child path. The cache is created only
when forward compilation needs it; ordinary compilation does not allocate it.

Forward node-sequence effective boolean values now use native emptiness inspection. General sequences use
an incremental state that stops after a node first item or inspects at most two atomic items, reusing canonical
EBV type/cardinality errors. `remove()` now filters a native iterator with an exact integer position counter
and the canonical function argument conversion. Zero, negative and arbitrarily large positions retain their
specified behavior. Neither helper grounds the whole input. Existing atomic-item proofs survive removal;
original nodes retain identity and source ownership.

The new suite has **50 net10.0 / 49 net8.0** cases covering union ordering and deduplication, independent
constructors, positional branches, context error codes, anchor boundaries, EBV errors and short circuiting,
exact removal positions, attributes followed by children, and saved compilation. An early-message assertion
observes a result before borrowed input reaches EOF. The saved NativeAOT probe executes union values,
removal/EBV errors, anchor boundaries and early output with dynamic code disabled; borrowed readers remain
open. No saved-constants format change is needed, and the earlier version-2 saved fork still executes.
Six generated runtime helper signatures are recorded in the unshipped ABI. Review also caught a self-only
union incorrectly using a descendant-or-self scan, consuming children needed by a later expression. Two
regression cases failed with `stream-position` before the correction; this shape now uses a self scan and
retains its peer ordering proof. The saved native probe checks the same behavior.

The final strict selection is **1,918/173/1**, with **9** unmatched-code passes, compared with
**1,863/228/1** and fourteen in an isolated control with these rewrites disabled. Failure-ID comparison finds
**56 fixes and one newly visible failure**, rather than establishing regression freedom from the net gain of
55. The additional failure is `sx-UnionExpr/sx-union-202`: its union of child selections sharing a parent now
executes instead of raising an unrelated runtime error. This is the peer-union rewrite already documented
under S2; the unstreamed baseline already contains the same fixture. The catalog explicitly describes it as
not guaranteed streamable while streamable in Saxon. The source-syntax analysis remains crawling. The exact
XTSE3430/extension-option contract under [§19.10](https://www.w3.org/TR/xslt-30/#streamability-guarantees) remains
part of S2's open exit; the result is kept visible and no baseline was relaxed.

The selected `sf-boolean`, `sf-not`, `sf-empty`, `sf-exists` and `sf-remove` sets all pass. `sx-UnionExpr` is
**40/34/0**, including the newly visible expected-error mismatch. Predicated/mixed union paths, remaining
function ownership and callback paths, complete S1/S2 enforcement and S7's throughput, memory, replay-byte and
first-output latency gates remain open. This increment does not establish bounded memory for arbitrary atomic
items, explicit result buffers or grounded results, and does not close S4 or Phase 7.


Final functional verification: the Release solution builds with **zero errors** (30 existing warnings in the
earlier full build; none in the final incremental build).
Full Debug and Release unit suites pass **2,240 net10.0 / 2,195 net8.0**; upstream passes **1,654/framework**,
and the full W3C harness **46/46**. Normal XSLT remains **8,847/21/41** (34 unmatched-code passes), QT3
**21,884/0/99** (67), and the full unstreamed development reference **2,691/20/1** (5). Normal and unstreamed
failure-ID comparisons show no additions or removals, and no baseline changed in this increment. The saved
NativeAOT build and execution complete with no trim/AOT warnings. Logs use `/tmp/xsl3-p7-inspection-final-`;
the isolated disabled-rewrite comparison uses `/tmp/xsl3-p7-inspection-disabled-`.


The final isolated three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time
**1.016** over 39 rows and maximum allocation ratio **1.037** (cold start). Individual time regressions remain
unresolved: **1 MB iterate 1.176**, **1 KB regex 1.149**, **grouping compilation 1.138** and
**sorting compilation 1.058**. Dispatch compilation is **0.994**, keys compilation **0.952** and cold-start
time **1.004**. Functional and native jobs had finished before measurement, both sides used the same current
harness and thresholds, and this final gate was not rerun. Earlier outliers are not considered fixed merely
because their medians changed. These ordinary-path results do not establish streaming throughput,
retained-memory, replay-byte or first-output latency budgets.

Retained round medians show variation. Iterate 1 MB is base **1.2602/1.2100/1.2461 ms**, head
**1.27575/1.46590/1.46535 ms**; regex 1 KB is base **0.0047/0.0046/0.0047 ms**, head
**0.0054/0.0056/0.0046 ms**. Grouping compilation is base **0.5985/0.6570/0.5824 ms**, head
**0.68105/0.69710/0.61990 ms**; sorting compilation is base **0.6065/0.5206/0.58725 ms**, head
**0.6212/0.6376/0.6012 ms**. Cause and attribution remain open. The log is
`/tmp/xsl3-p7-inspection-perf.log`; raw rounds are retained under `/tmp/xsl3-p7-inspection-perf-data/`.
Phase 7 remains incomplete and all branch changes remain uncommitted.


### S4 cardinality assertions and native sequence insertion

**Development increment, 2026-10-24.** Forward calls to `zero-or-one`, `one-or-more` and `exactly-one` now
transmit each item through a native iterator, checking the upper bound before a second item can escape and
the lower bound at completion. No source item is retained by the assertion state. Materialized calls and the
incremental helpers share the canonical [F&O cardinality checks](https://www.w3.org/TR/xpath-functions-31/#fns-seq-cardinality),
including unchanged FORG0003/FORG0004/FORG0005 messages. `unordered` preserves the original iterator and
identity. Syntactic disjoint child selections, existing same-depth annotations and dynamic iterator-parameter
ordering proofs survive cardinality wrappers; unknown grounded sequences retain canonical sorting and
deduplication when used by a path. No ordering proof is inferred from the function name alone.

`insert-before` now interleaves the target and insertion iterators at the requested exact integer position.
Either sequence operand may provide the forward population. A per-call integer counter handles non-positive
positions and append positions beyond the target, including integers beyond machine range, without collecting
the node populations. Position conversion uses the registered function contract; `remove` shares the same
conversion helper. The insertion expression executes once, either before a target item or after target
exhaustion. Non-empty insertions do not inherit the target's ordering proof: arbitrary inserted nodes can
change document order or introduce duplicates. These are the [F&O insertion semantics](https://www.w3.org/TR/xpath-functions-31/#func-insert-before).

A statically empty insertion retains argument validation and any operand effects while preserving the target's
identity and ordering proofs. Validation returns an opaque empty node iterator. A statically empty `void` call
allowed `FoldNone` to extract it from surrounding iterator bindings, which the Debug verifier correctly
rejected. The new lowering preserves those read regions; no verifier policy was relaxed. Region failures now
include the helper name and before/after region descriptions to make future violations actionable.

The new suite has **45 net10.0 / 43 net8.0** cases. It covers values and canonical errors, exact insertion
positions, either forward operand, repeated invocations, maps, identity, reversed/duplicate grounded anchors,
empty insertions followed by child paths, early message output, borrowed readers, both public API paths and
saved compilation. Before implementation, the initial suite had **18 failing / 16 passing** cases on net8.0.
Full Debug and Release units now pass **2,285 / 2,238**. The saved NativeAOT probe executes transmitted nodes,
empty and multiple cardinality errors, empty insertion and invalid position errors, alongside all previous
probes and the frozen version-2 fork. The final native build has no trim/AOT warnings and borrowed readers
remain open. Seven additive generated helper signatures are recorded in the unshipped ABI; no saved format
version changes.

The expanded strict selection is **1,951/140/1**, with **8** unmatched-code passes, compared with
**1,918/173/1** and nine in the previous recorded selection. Failure-ID comparison finds **33 fixes and no new
failures**. The selected `sf-exactly-one` (**8/0**), `sf-one-or-more` (**30/0**), `sf-zero-or-one` (**7/0**) and
`sf-unordered` (**22/0**) sets all pass. `sf-insert-before` is **48/4**. Its remaining -041/-043/-141/-143 cases
apply `/PRICE` after combining a grounded target with streamed insertions. The required canonical order across
those node populations still buffers nodes before their values can be consumed. This needs a correct ordered
merge or equivalent lowering; dropping the ordering check would change semantics. No baseline was relaxed.

Trace callback ownership, remaining mixed-node and predicated path shapes, other function/capture paths,
complete S1/S2 enforcement and S7's quantitative throughput, retained-memory, replay-byte and first-output
latency gates remain open. This increment adds no whole-input fallback, and does not establish bounded memory
for arbitrary atomic values, explicit buffers or results. S4 and Phase 7 remain incomplete.


Final functional verification: Release solution build **zero errors / 30 existing warnings**; upstream
**1,654/framework**; differential **394/framework**; W3C harness **46/46**. Normal XSLT remains
**8,847/21/41** (34 unmatched-code passes), QT3 **21,884/0/99** (67), and the unstreamed development reference
**2,691/20/1** (5). Normal and unstreamed failure-ID comparisons show no additions or removals. Final unit,
conformance and native logs use `/tmp/xsl3-p7-contract-final-`; the strict failure comparison snapshot is
`/tmp/xsl3-p7-contract-strict-report.md`.


The final isolated three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time
**1.005** over 39 rows and maximum allocation ratio **1.038** (cold start). Individual time regressions remain
unresolved: **1 MB iterate 1.166**, **1 KB regex 1.149**, **dispatch compilation 1.093** and
**sorting compilation 1.084**. Grouping compilation is **0.998**, keys compilation **0.945**, and cold-start
time **1.028**. Functional and native jobs had finished before measurement, both sides used the same current
harness and thresholds, and this final gate was not rerun. Changed medians do not establish a fix for earlier
outliers or prove S7's streaming budgets.

The retained rounds show iterate base **1.2350/1.2724/1.2457 ms**, head **1.1942/1.4529/1.4522 ms**;
regex 1 KB base **0.0047/0.0047/0.0047 ms**, head **0.0054/0.0054/0.0056 ms**. Dispatch compilation is
base **1.60935/1.66470/1.59285 ms**, head **1.6406/1.7595/1.8050 ms**; sorting compilation is base
**0.5412/0.4665/0.62585 ms**, head **0.5304/0.5882/0.58665 ms**. Cause and attribution remain unresolved.
The log is `/tmp/xsl3-p7-contract-perf.log`; raw rounds are retained in `/tmp/xsl3-p7-contract-perf-data/`.
Phase 7 remains incomplete and all branch changes remain uncommitted.


### S4 ordered mixed-node paths and exact position windows

**Development increment, 2026-10-24.** Fixed child paths following `insert-before` now select each operand's
node population separately and merge those populations in canonical document order. The native cursor uses
the runtime's existing document comparer, deduplicating by node identity rather than value. Proven ordered
populations retain one head each and advance only after their current node has been delivered to its consumer.
Unproved populations retain canonical materialized sorting; this does not make arbitrary unproved live
populations streamable. Nested insertions share one merge, and every position expression is converted exactly
once. The fixed node-only child steps make the canonical result set independent of the insertion index;
argument validation is retained. This follows the node-set semantics of
[XPath paths](https://www.w3.org/TR/xpath-31/#id-path-expressions) and its
[document ordering rules](https://www.w3.org/TR/xpath-31/#id-document-order).

The compiler reuses the host's existing closure capture and suspended-population ABI. Both public stylesheet
APIs therefore use the same lowering; saved and native compilation need no serialized AST or format-version
change. Fixed child chains are built linearly, including nested insertion leaves. Failed shape analyses cache
AST identities to avoid repeatedly scanning every prefix of an unrelated long path. Predicate-bearing fused
steps and document-element tests retain their existing compilation. No ordering proof is granted to general
mixed nodes merely because they pass through insertion.

Cursor completion and catchable faults release population captures, heads and sorting buffers. Faults are
retained so a subsequent request raises the same exception instead of resuming the producer. Ordered inputs
are checked for monotonic document order; a violated compiler proof fails visibly. The cursor owns no input
reader, and borrowed readers remain open.

Review found two additional proof/position issues. `exactly-one` and `zero-or-one` have at most one result,
so their successful node results are disjoint anchors even when the source was crawling. That stronger proof
now takes precedence over a conservative caller-order flag, including across iterator-backed stylesheet
function calls. The new function-call regression failed with `stream-position` before this correction.
Positive integer literal predicates now use an exact integer window and test its limit before advancing the
input again. This prevents a descendant cursor from re-entering children already consumed by the selected
node's body. The window retains integer precision beyond machine range. Per-parent predicates and a filter
on a whole descendant sequence keep their different meanings; position-use notification still prevents the
incorrect `A//N[1]` to `A/descendant::N[1]` rewrite. Other predicate forms retain their existing compilation.
Syntactic child-peer proofs remain visible when the new state wrappers hide ordinary optimizer axis patterns.

The increment adds **39 net10.0 / 38 net8.0** cases, including four direct cursor tests. Coverage includes
mixed document blocks, reversed and duplicated grounded inputs, aliased nodes and independent constructors,
nested insertions, position evaluation counts, node/position errors, adjacent text-node joining, static
crawling-path rejection, early delivery, both public APIs, saved compilation, exact literal windows and
cross-function proof precedence. A final isolated control disables only the ordered-path intercept while
retaining the other final fixes: **19 failures / 16 passes** in the 35-case net10.0 path suite, versus all 35
passing with the intercept enabled. Two bounded-input cases and the caller-proof case also have recorded
failing runs before their respective corrections. Full Debug and Release units pass **2,324 / 2,276**.

The expanded strict selection is **1,957/134/1**, with **8** unmatched-code passes, compared with
**1,951/140/1** and eight in the preceding selection. Failure-ID comparison finds **six fixes and no new
failures**: insertion -041/-043/-141/-143 and arithmetic -001/-002. `sf-insert-before` is now **52/0/0**.
No baseline changed. The earlier peer-union expected-error mismatch remains visible; this increment does not
close S1/S2's analysis and exact-code gates.

The final Release solution build has zero errors (zero warnings in the incremental build). Upstream passes
**1,654/framework**, differential **394/framework**, and the W3C harness **46/46**. Normal XSLT remains
**8,847/21/41** (34 unmatched-code passes), QT3 **21,884/0/99** (67), and the full unstreamed development
reference **2,691/20/1** (5). Normal and unstreamed failure-ID comparisons show no additions or removals.
Saved NativeAOT execution checks mixed and nested ordering, bounded crawling anchors, exact predicates,
cross-function proof precedence, node/position errors and early first streamed values, alongside all previous
probes and the unchanged version-2 saved fork. The native build has no trim/AOT warnings. Six additive
generated helper signatures are recorded in the unshipped ABI. Logs use `/tmp/xsl3-p7-ordered-final-`;
the disabled-rewrite control uses `/tmp/xsl3-p7-ordered-control-`.

Remaining work includes predicated mixed paths, other unproved native populations, descendant/callback and
function/capture ownership paths, complete S1/S2 enforcement, and S7's quantitative throughput,
retained-memory, replay-byte and first-output latency gates. Unproved sorting, explicit buffers and arbitrary
atomic values or results can still grow. This increment adds no whole-input tree fallback and does not close
S4 or Phase 7.


The final isolated three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time
**0.990** over 39 rows and maximum allocation ratio **1.039** (cold start). The 1 KB regex time remains an
unresolved outlier at **1.128**. Dispatch, grouping, keys and sorting compilation are
**1.002/1.041/0.699/0.988**; cold-start time is **0.992**. Functional and native jobs had finished before
measurement, both sides used the same current harness and thresholds, and this final gate was not rerun.
Changed medians do not establish a fix for earlier outliers or prove streaming throughput, retained-memory,
replay-byte or first-output latency budgets.

Retained regex round medians are base **0.0047/0.0047/0.0047 ms**, head **0.0053/0.0047/0.0054 ms**.
Cause and attribution remain unresolved. The log is `/tmp/xsl3-p7-ordered-perf.log`; raw rounds are retained in
`/tmp/xsl3-p7-ordered-perf-data/`. Phase 7 remains incomplete and all branch changes remain uncommitted.


### S4 streamed trace and invoking-thread diagnostics

**Development increment, 2026-10-24.** Direct `fn:trace` calls now preserve their original streamed node
identities and child-path ordering proofs. Without a handler, the two-argument form validates its label once
and transmits its input iterator without collecting or replaying nodes. A regression checks that the first
row header reaches its consumer before that row's content is read. The one-argument form retains the
published absorbing operand role: each selected subtree is explicitly replayable before it is delivered,
including when a variable retains those nodes for subsequent navigation. No whole-sequence cache is added
on that no-handler path. The existing built-in operand-usage table is unchanged. Hosted many-valued inputs
use one suspended population shared by the exclusive diagnostic/no-handler branches, avoiding exponential compiler graph
growth from nested calls. Empty and singleton inputs retain direct lowering. A 24-level nested-call
regression checks both handler paths and once-only diagnostic delivery.

An installed `TraceHandler` retains the existing whole-call contract: one `(label, formatted value)` callback
before returning the call's result, including `head(trace(...))` and an empty input. Selected streamed subtrees
are explicitly buffered with their original identities, namespaces and accumulator phases; serialization and
subsequent result navigation can both read them. The canonical function implementation still formats nodes,
atomic values and function items and enforces the label's declared type. The behavior follows the unchanged
result contract of [F&O fn:trace](https://www.w3.org/TR/xpath-functions-31/#func-trace); the diagnostic format
and whole-call callback are this implementation's established API contract.

In a fork prong, formatting runs where its streamed nodes are owned, then the immutable label/text pair is
relayed synchronously through the existing bounded caller queue. The handler therefore runs on the invoking
thread. Callback faults return through that queue with their original exception identity. This change covers
`fn:trace` callbacks; other listeners and callback ownership paths remain separate work. Borrowed input readers
remain open on success and failure. Three additive generated helper signatures are recorded in the unshipped
ABI; saved stylesheet format and the frozen version-2 fork probe are unchanged.

The increment adds **34 net10.0 / 32 net8.0** cases. Coverage includes direct and per-row values, mixed items,
empty inputs, node identity, child navigation, exact integer boxing, label errors with no handler, evaluation
counts, whole-call diagnostic values, first-header delivery, accumulator phases, callback faults and invoking
thread ownership, nested-call scalability, both stylesheet APIs and saved compilation. The first regression
run exposed 16/14 failures; one per framework was an invalid fork-prong fixture, corrected before validation. All final cases pass. Full
Debug and Release units pass **2,358 / 2,308**.

The same expanded strict selection improves from **1,957/134/1** to **1,964/127/1**, retaining eight
unmatched-code passes. Failure-ID comparison finds seven fixes and no additions: `sf-trace` -011 through
-016 and -018. The trace set is now **22/0/0**. No baseline was changed. Saved NativeAOT execution verifies
both arities and 24-level nested calls, with and without a handler, readable node results and invoking-thread
fork callbacks alongside all prior probes. The native build has no trim/AOT warnings. Logs use `/tmp/xsl3-p7-trace-final-`; the strict
snapshot is `/tmp/xsl3-p7-trace-strict-report.md`.

The final Release solution build passes with zero warnings and errors in the incremental build. Upstream
passes **1,654/framework** with the repository CI filter `category!=failing`; an additional unfiltered run
exposes four existing Windows-specific path-test failures per framework on macOS. Those cases are marked
Windows-only in the upstream source, and no test or filter was changed. Differential passes **394/framework**.
The full W3C harness passes **46/46**. Normal XSLT remains **8,847/21/41**, with 34 unmatched-code passes,
and QT3 **21,884/0/99**, with 67. The full unstreamed development reference remains **2,691/20/1**, with
five unmatched-code passes. Normal and unstreamed failure-ID comparisons match their unchanged baselines.

Explicit diagnostics can retain every traced subtree plus its formatted text and are unbounded. Likewise,
`trace#1` can buffer a large selected subtree because its operand is absorbing. These buffers are not evidence
of S7's bounded-memory or throughput gates. Dynamic function-item dispatch, remaining descendant/capture
ownership paths, complete S1/S2 enforcement and the other strict failures remain open. Phase 7 remains
incomplete, and the branch changes remain uncommitted.

The final isolated three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time
**1.008** over 39 rows and maximum allocation ratio **1.039** (cold start). Sorting compilation remains an
unresolved time outlier at **1.201**; keys compilation is **1.054**. Dispatch/grouping compilation are
**1.014/1.031** and cold-start time **1.020**. All functional and native jobs had finished before measurement;
both sides used the same current harness and thresholds, and the final gate was not rerun. This does not
establish S7's streaming throughput, retained-memory, replay-byte or first-output latency budgets, or resolve
outliers recorded in preceding increments.

Retained sorting compilation round medians are base **0.50895/0.5970/0.49125 ms**, head
**0.47755/0.6113/0.6131 ms**. Keys compilation is base **0.2774/0.2929/0.2897 ms**, head
**0.3052/0.6175/0.2545 ms**. Cause and attribution remain unresolved. The log is
`/tmp/xsl3-p7-trace-perf.log`; raw rounds and the comparison are retained in
`/tmp/xsl3-p7-trace-perf-data/`. Phase 7 remains incomplete and all branch changes remain uncommitted.


### S4 parallel maps and forked node-parameter factories

**Development increment, 2026-10-24.** XPath map constructors and direct `xsl:map-entry` children of
`xsl:map` now evaluate independent consuming entries through the existing bounded input tee and isolated
prong runtimes. This implements the parallel-entry exception in
[XSLT §19.8.8.17](https://www.w3.org/TR/xslt-30/#streamability-of-map-constructors) and
[§21.6](https://www.w3.org/TR/xslt-30/#maps-streaming): entries can select different children in any input
order during one pass. Dynamic instruction content retains its ordinary single-consumer lowering. Existing
analysis continues to reject live streamed node references as map values; this adds no input-tree fallback.

Single-entry expressions and maps with at most one potentially input-dependent entry keep direct lowering.
The input-free proof is deliberately limited to literal expression trees. Other variables and calls are
conservative candidates. At runtime, a map whose prepared graph contains no live source is evaluated serially;
constant and motionless maps do not acquire a consuming input pass. Computed keys are atomized while their
selected nodes remain readable, before the canonical key/cardinality checks. Maps merge in declaration order
through the existing builder and preserve the XPath/XSLT duplicate-key distinction. Worker fault selection,
cancellation, joining, caller-thread callbacks, runtime state and source ownership reuse the fork coordinator.
This adds thread/setup overhead to maps that need independent views; quantitative S7 throughput remains open.

Map forks derive their source boundary from the entry closures' captured positions. The enclosing instruction
focus can differ from a nested XPath `!` focus and must not supply the boundary. Each row can therefore finish
before the next row is consumed. A regression checks the underlying reader is still at that row's end tag at
its first output notification, rather than relying only on `EOF` being false.

Returned structured values are frozen before their private views close and projected back to the invoking
runtime. Function values must remain callable, so this uses a retained-result graph mode distinct from
serialization's function-description mode. A returned closure can invoke `fn:trace` after the entry workers
have joined, with its handler running on the invoking thread. Copied node values, aliases and same-family
function captures use the existing graph visitor. Foreign-runtime function integration remains open.

Fork binding projection now handles grounded node parameters and unstarted, proven-pure node-parameter
factories. The compiler supplies that proof only for predicate-free node-axis paths passed to unrestricted
`node()*` parameters. Such a factory has no evaluation side effects and no node-type/cardinality conversion
to perform at entry, so it can start independently in each view. Each prong completes its parameter cursor
before closing the view; after joining, cardinality agreement is checked and the invoking parameter receives
its completion summary and first-node header. Parent function completion does not replay the consumed source.
Nested forks update their immediate invoking parameter, preserving thread ownership. Faults clear projected
parameter tracking. The old generated argument helper and its eager header inspection remain available.
Already inspected parameters, typed/restricted parameters and stateful producers still require continuation
projection; they fail explicitly instead of being grounded or silently shared between workers.

The increment adds **33 net10.0 / 32 net8.0** cases, including reverse child selection, multiple values,
computed keys, nested maps, direct entry instructions, dynamic map content, motionless maps followed by input
consumption, shared globals, scalar captures, empty inputs, node parameters, nested and early parameter
consumers, returned function ownership, canonical errors and argument errors that survive a callee catch.
Both stylesheet APIs and saved compilation are covered. The first regression run had 13/12 failures; one per
framework was an unspaced QName/colon fixture corrected before validation. Full Debug and Release units pass
**2,391 / 2,340**. Two additive generated helper signatures are recorded in the unshipped ABI, with no saved
format-version change.

The expanded strict selection improves from **1,964/127/1** to **1,973/118/1**. All nine previous MapExpr
failures pass, with no new failure IDs; that set is now **15/0/0**. Expected errors passing without a matching
code drop from eight to six. No baseline changed. The strict snapshot is `/tmp/xsl3-p7-map-strict-report.md`;
logs use `/tmp/xsl3-p7-map-`.

The final Release solution build passes with zero warnings and errors in the incremental build. Upstream
passes **1,654/framework** with the repository CI filter, differential **394/framework**, and the full W3C
harness **46/46**. Normal XSLT remains **8,847/21/41** (34 unmatched-code passes), QT3 **21,884/0/99** (67),
and the full unstreamed development reference **2,691/20/1** (5). Normal and unstreamed failure-ID sets match
their unchanged baselines. Saved NativeAOT execution covers map expressions, instruction entries, computed
keys, duplicate-key errors, absent-focus node parameters, early and nested parameter consumers, returned
closures and first-record delivery, alongside all earlier probes and the frozen version-2 fork. The native
publish has no trim/AOT warnings. Final logs use `/tmp/xsl3-p7-map-final-`.

Map values and explicit copies can grow with the result being constructed. Neither bounded tee capacity nor
these functional checks establish S7's retained-memory, replay-byte, first-output latency or throughput gates.
Remaining work includes parameter continuation projection, foreign function ownership, other native expression
paths and complete S1/S2 enforcement. Phase 7 remains incomplete and all branch changes remain uncommitted.

The final isolated three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time
**1.015** over 39 rows and maximum allocation ratio **1.040** (cold start). Compilation time outliers remain
unresolved: sorting **1.259**, grouping **1.120**, keys **1.081** and dispatch **1.058**. Cold-start time is
**0.977**. Functional and native jobs had finished before measurement; both sides used the same current
harness and thresholds, and the final gate was not rerun. This does not resolve earlier outliers or prove
streaming throughput, retained-memory, replay-byte or first-output latency budgets.

Retained sorting compilation round medians are base **0.47615/0.5638/0.4785 ms**, head
**0.6308/0.60235/0.5100 ms**. Grouping is base **0.6056/0.5880/0.6261 ms**, head
**0.6840/0.6782/0.6445 ms**. Dispatch is base **1.5591/1.54405/1.5581 ms**, head
**1.7001/1.6263/1.6485 ms**. Keys is base **0.3000/0.57395/0.30095 ms**, head
**0.2762/0.32545/0.3797 ms**. Cause and attribution remain unresolved. The log is
`/tmp/xsl3-p7-map-perf.log`; raw rounds and the comparison are retained in `/tmp/xsl3-p7-map-perf-data/`.
Phase 7 remains incomplete and all branch changes remain uncommitted.


### S4 native node sets and shared header paths

**Development increment, 2026-10-24.** Forward compilation now lowers supported `union`, `intersect` and
`except` expressions into native node populations. Independent operands use a delayed-advance merge when
at least one operand is grounded. Grounded populations are validated and sorted before the live producer
advances. Returned source headers reach their consumer before that producer advances again. Identity,
document order, duplicate removal, empty results and late non-node errors use the canonical node contract;
set operands report `XPTY0004`. Completion validates the remaining population even when an intersection
cannot produce another result. Cancellation and producer failures close captured state and retain their
original exception without resuming the producer. Empty populations use an explicit empty producer.

An unproved live population cannot acquire a sorting buffer. The guard releases retained headers, validates
remaining item kinds without retaining positions, and reports `streaming-unimplemented` unless an operand
error occurs first. Grounded/replayable populations retain their existing explicit buffering contract.
Compiler facts distinguish grounded lexical bindings and host variables from streamed function parameters;
the first parameter of every streamable function is excluded from the host's grounded-variable proof.

Compatible selections of one source use a single traversal with Boolean membership for all three operators.
Fixed child chains after a set can distribute through its operands because each selected child has one parent
at that fixed depth. Longer child/self/descendant paths use header-only prefix facts, rather than executing two
competing live selections. Each prefix predicate runs once at its own source header, including prefixes with
no selected descendants. Every eligible operand evaluates its predicates before membership is combined, so a
successful left union member cannot hide a right predicate's conversion error or diagnostic call. Positional,
variable-dependent and other unproved predicates remain outside this lowering.

The longer-path matcher computes prefixes in source order and reads previously computed parent/ancestor/self
facts. It does not recursively emit all preceding paths for every prefix. Streamed facts use weak source-header
keys, with no strong document-wide node index; grounded identity maps belong to the current matcher. The scan
restricts its iterator to the node kinds any prefix can select. Reading an element's value therefore does not
turn irrelevant consumed text into a lost element match. Real lost matching descendants still fail explicitly;
there is no implicit replay or whole-input grounding. Fixed peer depths retain disjoint/order proofs, while
mixed depths remain crawling. Unproved outer populations use the same guarded canonical ordering path.

Six additive generated helper signatures are recorded in `PublicAPI.Unshipped.txt`, with no saved format-version
change. Runtime merging reuses the ordered-population reader and document comparer; ordinary node-test kind
classification is shared with the XPath compiler. No runtime fixture or domain-value fallback was introduced.

The increment adds **56 net10.0 / 54 net8.0** cases. Coverage includes mixed grounded/live operands in both
orders, canonical grounded sorting and identity, nested sets, fixed child paths, longer shared paths, self-axis
prefixes, header predicates, diagnostics, empty populations, first-header delivery, late type failures,
cancellation, sticky producer faults, both stylesheet APIs and saved compilation. Debug and Release units pass
**2,447 / 2,394**. Initial basic set cases failed 20/18 before implementation; later review caught seven
regressions per framework in broader paths and predicate evaluation, all corrected. A text-node fixture uses
explicit `string-join` to make its joining contract independent of simple-content construction. Manual long-path
probes at depths 8/32/128 return the correct result, with single observed compilation times of
**1.54/1.59/3.96 ms**. These are diagnostic observations, not a scaling or performance gate.

The expanded strict selection improves from **1,973/118/1** to **2,023/68/1**, retaining six expected-error
passes without a matching code. Exactly 50 previous failure IDs pass, with no new failures. ExceptExpr is
**56/10/0**, IntersectExpr **55/9/0**, and UnionExpr **65/9/0**. Except fixes are 011–016, 018, 302,
310, 311, 313, 314, 316, 320, 320a and 323; intersect fixes are 302, 310–314, 316, 320 and 323; union fixes
are 011–016, 018, 040–043, 111–116, 118, 140–143, 311, 320 and 323. No baseline changed. Remaining set
failures include climbing axes, other consumers and unproved predicate/selection shapes, alongside the existing
peer-union expected-error mismatch.
The strict snapshot is `/tmp/xsl3-p7-sets-strict-report.md`; final logs use `/tmp/xsl3-p7-sets-final-`.

Saved NativeAOT execution passes mixed sets, shared and longer header paths, predicate callback counts,
predicate/type errors, node parameters and first-header delivery, alongside all earlier probes and the frozen
version-2 fork. The publish emits no trim/AOT warnings. The final Release solution build passes with 30 existing
test-project warnings and no errors. The runtime changes and generated helpers introduce no new build warnings.
Remaining work includes other native expression paths, parameter continuation projection, foreign function
ownership, complete S1/S2 enforcement and all quantitative S7 streaming gates. Phase 7 remains incomplete and
all branch changes remain uncommitted.

Compatibility checks pass **1,654/framework** upstream, **394/framework** differential, and **46/46** for the
full W3C harness. Normal XSLT remains **8,847/21/41** (34 unmatched-code passes), QT3 **21,884/0/99** (67),
and the full unstreamed development reference **2,691/20/1** (5). Normal and unstreamed failure-ID sets exactly
match their unchanged baselines. Conformance tables were regenerated from these final reports. All functional
and native jobs completed before the final isolated performance gate began.

The final isolated three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time
**0.989** over 39 rows and maximum allocation ratio **1.041** (cold start). Cold-start time is **1.026**.
The individual 1 KB regex row is **1.128**, with retained round medians base **0.0047/0.0047/0.0047 ms** and
head **0.0047/0.0053/0.0054 ms**. Its cause and attribution remain unresolved. Compilation ratios in this run
are sorting **0.832**, keys **0.825**, dispatch **0.992** and grouping **1.016**; these measurements do not
resolve the earlier compilation outliers. Both sides used the same current harness and thresholds. The final
gate was not rerun. The log is `/tmp/xsl3-p7-sets-perf.log`; exactly the three raw pairs and comparison are
retained in `/tmp/xsl3-p7-sets-perf-data/`.

These ordinary-path measurements do not establish streaming throughput, retained-memory, replay-byte or
first-output latency budgets. Explicit grounded operands and constructed results may grow. Phase 7 remains
incomplete and all branch changes remain uncommitted.

Retained compilation round medians are sorting base **0.5838/0.5550/0.59665 ms**, head
**0.4445/0.59995/0.4855 ms**; keys base **0.2904/0.50075/0.3553 ms**, head
**0.2932/0.26475/0.3725 ms**; dispatch base **1.54095/1.77855/1.5057 ms**, head
**1.5219/1.5288/2.0525 ms**; and grouping base **0.6219/0.6211/0.6339 ms**, head
**0.6316/0.6663/0.62935 ms**. Cause and attribution of the earlier outliers remain unresolved.


### S4 positional header predicates and bounded scans

**Development increment, 2026-10-24.** Shared set selections now evaluate positional child, attribute and
self predicates without competing input traversals. Longer child/self paths can carry positions at their
prefixes. Each positional predicate has its own counter after the preceding predicate stages, scoped to the
parent whose children or attributes it filters. Self populations have position one. Rejected candidates do not
advance a later stage. The abbreviated `//` representation retains the original child-per-parent counter scope
when this lowering applies; the prescribed streamability analysis still runs and can reject an expression.
Explicit descendant-axis positional predicates, size-dependent predicates and other unproved shapes remain
outside this shared lowering. This introduces no exception to XTSE3430.

Counters use exact integers. A predicate's `position()` value remains an integer item through expression
compilation, and numeric-singleton predicates use the existing numeric comparison/promotion logic rather than
converting integer positions to binary floating point. Other predicate results retain the canonical effective
Boolean value rules, including errors for invalid sequences. Trace diagnostics see the position of their own
predicate stage. State uses weak streamed-parent keys and matcher-local grounded identities, without retaining
a strong document-wide node index. Grounded operands retain their explicit storage contract.

Fixed child/self header paths now annotate their maximum child depth. The descendant iterator honors that
bound on streamed and grounded trees, including zero-depth self populations. Consuming a selected element's
value cannot lose deeper headers that are outside the selection. Nodes still needed within the bound continue
to raise `stream-position` if consumed without replay. Exhaustion remains stable, and the bounded cursor stops
at its root boundary without seeking to an outside sibling. This is a structural bound derived from the path,
not a configured input-size limit or implicit subtree buffer. Existing unbounded and outermost entry points
retain their signatures. The shared cursor also now recognizes a self result as having started before its
consumer reads it: consuming that root is safe when no matching descendants were lost. Skipped subtrees and
consumption that predates cursor creation still fail. Two new regressions per framework reproduced the false
position error before correction; bounded and unbounded variants now pass.

Review exposed a node-valued pull function being classified as writer output because its selection had an
empty conditional branch. Construction annotation now leaves pull-function bodies as iterators, preserving
yielded values and node identity. The generated optimizer and its `Annotate.p` pattern source both carry the
correction. Descendant-method selection is shared by filtered and unfiltered IL generation. Two generated
predicate helpers and the additive `DescendantIterator.CreateBounded` signature are recorded in the unshipped
ABI, without a saved format-version change.

The increment adds **34 net10.0 / 33 net8.0** cases. They cover positional prefixes, filtered stage counts,
per-parent resets, self and attribute selections, zero/fractional/NaN predicates, exact integer comparison
above the binary floating-point integer boundary, canonical errors, trace positions, complete element values,
grounded identity, bounded cursor exhaustion and first-header delivery. Both stylesheet APIs and saved
compilation are covered. Debug and Release units pass **2,481 / 2,427**. The final incremental solution build
passes with zero warnings and errors in the final incremental build. Earlier rebuilds report only existing
test-project warnings.

The expanded strict selection improves from **2,023/68/1** to **2,035/56/1**, retaining six unmatched-code
passes. Twelve previous failure IDs pass with no new failures: except 303, 312, 317, 321 and 322; intersect
303, 317, 321 and 322; union 317, 321 and 322. ExceptExpr is **61/5/0**, IntersectExpr **59/5/0**, and
UnionExpr **68/6/0**. No baseline changed. The strict snapshot is
`/tmp/xsl3-p7-positional-strict-report.md`; final logs use `/tmp/xsl3-p7-positional-final-`.

Saved NativeAOT execution passes positional prefixes, chained filters, self and attribute positions, complete
element consumption, first-header delivery, grounded node identity, diagnostics and numeric predicate errors,
alongside all earlier probes and the frozen version-2 fork. Direct native iterator checks also cover safe
consumption after a bounded or unbounded self-root yield. The publish emits no trim/AOT warnings. Manual
long-path probes at depths 8/32/128 return the correct output, with single observed compilation times of
**1.61/1.66/4.25 ms**; these are diagnostic observations, not a scaling gate. Remaining work includes climbing
set paths, size/variable-dependent predicates, other native expression paths, parameter continuation projection,
foreign function ownership, complete S1/S2 enforcement and all quantitative S7 streaming gates. Phase 7
remains incomplete and all branch changes remain uncommitted.

Final compatibility checks pass **1,654/framework** upstream, **394/framework** differential, and **46/46**
for the full W3C harness. Normal XSLT remains **8,847/21/41** (34 unmatched-code passes), QT3 **21,884/0/99**
(67), and the full unstreamed development reference **2,691/20/1** (5). Their failure-ID sets exactly match
the unchanged baselines. Conformance tables were regenerated from the final reports. All functional and
native jobs completed before the final isolated performance gate began.

The final isolated three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time
**1.018** over 39 rows and maximum allocation ratio **1.041** (cold start). Cold-start time is **1.037**.
Individual time outliers remain unresolved: sorting compilation **1.250**, 1 MB iterate **1.196**, 1 KB
dispatch **1.056**, and iterate/join compilation **1.051/1.051**. Earlier outliers, including the 1 KB regex
measurement in the preceding increment, remain unresolved. These results do not establish streaming
throughput, retained-memory, replay-byte or first-output latency budgets.

Retained sorting compilation round medians are base **0.4437/0.4563/0.5013 ms**, head
**0.5706/0.5599/0.5944 ms**. The 1 MB iterate row is base **1.1904/1.1985/1.1890 ms**, head
**1.42375/1.18245/1.55325 ms**. The 1 KB dispatch row is base **0.0072/0.0070/0.0071 ms**, head
**0.0073/0.0075/0.0075 ms**. Iterate compilation is base **0.1423/0.1444/0.1454 ms**, head
**0.1590/0.1518/0.1484 ms**; join compilation is base **0.0890/0.0886/0.0897 ms**, head
**0.0935/0.0935/0.0907 ms**. Cause and attribution remain unresolved. Both sides used the same current
harness and thresholds, and the final gate was not rerun. The log is `/tmp/xsl3-p7-positional-perf.log`;
exactly three raw pairs and the comparison are retained in `/tmp/xsl3-p7-positional-perf-data/`. Explicit
grounded populations and constructed results may grow. Phase 7 remains incomplete and all branch changes
remain uncommitted.


### S4 forward XML-to-JSON conversion

**Development increment, 2026-10-24.** Direct and arrow calls to `fn:xml-to-json` now convert a selected
streamed node while it is readable. The canonical converter visits a document element immediately, then
finishes validating document structure; scalar values collect their text during the same child scan that
rejects element children. Comments and processing instructions retain their established treatment. This
removes the previous second traversal of the document or scalar subtree, without creating an input tree or
implicit replay buffer. Output strings and duplicate-key sets retain their normal, potentially growing storage.

Forward call lowering evaluates an optional-node argument incrementally. Conversion errors remain pending
until argument evaluation and cardinality checks finish, so a second argument item raises XPTY0004 and a
later source error retains its identity. No conversion result escapes before these checks. The canonical
function implementation still validates options, node kinds, JSON structure and escapes. Semantic conversion
errors finish reading only the selected subtree, preserving source validation and header observations.

For a predicate-free descendant selection with a proven exact expanded name or pure node-kind test, a scoped
observer records whether conversion consumed another matching descendant. That observed header proves a
second argument member even when the source cursor can no longer return it. The observer retains one root
header and a Boolean, unregisters on every exit, and stores no descendant positions or payload. Arbitrary
position failures are never translated into cardinality errors. More complex overlapping populations, including
additional predicates, wildcard/schema filters and variable or complex anchors, still need a stronger
membership/continuation proof and can fail explicitly with `stream-position`.

Four additive generated runtime signatures are recorded in the unshipped ABI. The saved format version is
unchanged. Function-item calls retain their prescribed analysis: a node-typed argument has type-determined
navigation usage under [XSLT 3.0 §19.8.8.11](https://www.w3.org/TR/xslt-30/#streamability-of-dynamic-function-calls).
The direct builtin's absorption rule does not bypass XTSE3430 for `xml-to-json#1(.)`.

Native-probe review also reproduced a compiler defect in large `xsl:try` bodies. The legacy AST rewriter used
32-bit instruction masks, causing newer instruction ordinals to alias older ones. A large try could be treated
as a refactorable `xsl:if` and have its catch moved into a generated template. Explicit instruction
classifications now preserve structural boundaries while retaining the established refactoring scopes. The new
regressions fail against the original compiler in ordinary and forward execution and saved compilation.

The increment adds **49 net10.0 / 48 net8.0** XML-to-JSON cases and **3/2** large-try cases. Coverage includes
document/element/scalar conversion, mixed text, empty input, options, structural and escape errors, deferred
argument cardinality, nested descendant members, exact names and pure node-kind selections, bounded per-record
delivery, observer cleanup, both stylesheet APIs and saved compilation. Debug and Release units pass
**2,533 / 2,477**. The final solution build passes; the only reported warnings are the existing test-project
xUnit collection-size assertions. Saved NativeAOT execution passes all previous probes, the frozen version-2
fork and 19 new JSON checks, including per-record reader position and errors, without trim/AOT warnings.
The native marker is `saved-forward-xml-to-json-validation-and-bounded-consumption-ok`; final functional and
native logs use `/tmp/xsl3-p7-json-final-`.

The final expanded strict selection improves from **2,035/56/1** to **2,041/50/1**, retaining six unmatched-code
passes. All six previous XML-to-JSON failures pass (001, 002, 003, 005, 006 and 007), bringing that set to
**7/7**, with no new failure IDs. No baseline changed. The strict snapshot is
`/tmp/xsl3-p7-json-strict-report.md`; comparison uses `/tmp/xsl3-p7-json-base-strict.md`.

Final compatibility checks pass **1,654/framework** upstream, **394/framework** differential, and **46/46**
for the W3C harness. Normal XSLT remains **8,847/21/41** (34 unmatched-code passes), QT3 **21,884/0/99**
(67), and the full unstreamed development reference **2,691/20/1** (5). Their failure-ID sets exactly match
the unchanged baselines. Conformance tables were regenerated from these final reports. All functional and
native jobs completed before the isolated ordinary-path performance gate began. Complex overlapping
populations, other native expression paths, parameter continuation projection, foreign function ownership,
complete S1/S2 enforcement and all quantitative S7 streaming gates remain open. Phase 7 remains incomplete
and all branch changes remain uncommitted.

The final isolated three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time
**0.987** over 39 rows and maximum allocation ratio **1.042** (cold start). Cold-start time is **1.005**.
Individual time outliers remain unresolved: dispatch compilation **1.070**, 1 KB regex **1.125**, and 1 MB
iterate **1.142**. Retained dispatch compilation round medians are base **1.6121/1.5565/1.5647 ms**, head
**1.7780/1.6742/1.6303 ms**. The 1 KB regex row is base **0.0046/0.0049/0.0048 ms**, head
**0.0055/0.0046/0.0054 ms**. The 1 MB iterate row is base **1.2576/1.24315/1.2147 ms**, head
**1.4612/1.4195/1.1902 ms**. Cause and attribution, including earlier outliers, remain unresolved. Both
sides used the same current harness and thresholds, and the final gate was not rerun. The log is
`/tmp/xsl3-p7-json-perf.log`; exactly the three raw pairs and comparison are retained in
`/tmp/xsl3-p7-json-perf-data/`. These results do not establish streaming throughput, retained-memory,
replay-byte or first-output latency budgets. Explicit grounded populations, JSON strings and duplicate-key
sets may grow. Phase 7 remains incomplete and all branch changes remain uncommitted.


### S4 forward deep equality and grounded descendant projection

**Development increment, 2026-10-24.** Direct `fn:deep-equal` calls with one proven grounded or locally retained attribute-header operand now
compare the other population one item at a time. Selected nodes remain readable during comparison. The
canonical node comparator also walks corresponding children incrementally, replacing its former child lists.
It retains the existing name, attribute, text, collation, map and array rules. Document and element comparisons
open consumption scopes; attribute and other leaf-header comparisons do not consume their owning element.
Regressions reproduced incorrect attribute-owner consumption and local-header/child comparison before correction.

Predicate-free descendant element selections with exact expanded names, `*` or plain `element()` have an
additional lowering over stable disjoint anchors. The source produces its outermost selected nodes. After a
whole source subtree compares equal to its grounded counterpart, the counterpart's matching element descendants
stand for the selected descendants that have already passed the reader. Those projected members are compared
with the remaining grounded sequence in document order. This is valid because the comparison proves the same
element structure and names under the unchanged comparison rules. No source subtree, source position list or
implicit replay buffer is created. Multiple disjoint anchor scopes retain their order. A failed comparison
remains false while argument iteration continues, including late source errors and function-item checks.

The grounded reference is explicit caller data and may grow. A runtime guard rejects live parent-node subtrees
inside that reference, including nested map/array values, rather than silently grounding them. More complex
overlapping populations, partial name/schema filters, descendant predicates and mixed node-kind projections
still require a stronger proof and can fail explicitly. Both independently live node operands and foreign
function ownership remain separate work. These changes do not relax the prescribed streamability analysis.

Review corrected the canonical FOTY0015 check when a non-map function is compared with another kind of item
([F&O 3.1 deep-equal](https://www.w3.org/TR/xpath-functions-31/#func-deep-equal)).
Uncompared nested map and array members retain their existing short-circuit behavior. Explicit and relative
collations use the canonical function-conversion and URI-resolution paths. The common pull runtime now checks
cancellation before entering any producer, including native producers; cancellation and source/comparison
faults retain their identity and cannot return a partial Boolean. Completed comparisons release their reference.
Four additive generated signatures are recorded in the unshipped ABI without changing the saved format version.
The descendant-axis closure proof is shared with forward XML-to-JSON lowering.

The increment adds **66 net10.0 / 65 net8.0** cases, including six runtime lifecycle checks. Initial public-API
regressions reproduced **25/24** failures before implementation; separate header regressions reproduced the
owner-consumption and errors when using a local attribute as the reference, before correction. Coverage includes both argument
orders, document/element/text/attribute comparisons, attributes in different orders, child structure, namespace
prefix independence, empty and unequal populations, multiple anchor scopes, nested selected descendants,
collations and relative URI resolution, canonical errors, uninterpreted map/array members, per-record reader
position, sticky faults, cancellation and reference cleanup. Both stylesheet APIs and saved compilation are
covered. Debug and Release units pass **2,599 / 2,542**. The solution build passes; earlier rebuilds report
only the existing test-project xUnit collection-size warnings.

Saved NativeAOT execution passes all previous probes, the frozen version-2 fork and 23 new comparison checks,
including local-header inspection, bounded per-record delivery and canonical errors. The publish emits no
trim/AOT warnings. The native marker is `saved-forward-deep-equal-descendants-collations-and-errors-ok`;
final functional and native logs use `/tmp/xsl3-p7-deep-equal-final-`.

The final expanded strict selection improves from **2,041/50/1** to **2,046/45/1**, retaining six unmatched-code
passes. All five previous deep-equal failures pass (008, 008a, 008d, 056 and 057), bringing that set to
**35/35**, with no new failure IDs. No baseline changed. The strict snapshot is
`/tmp/xsl3-p7-deep-equal-strict-report.md`; comparison uses `/tmp/xsl3-p7-deep-equal-base-strict.md`.

Final compatibility checks pass **1,654/framework** upstream, **394/framework** differential, and **46/46**
for the W3C harness. Normal XSLT remains **8,847/21/41** (34 unmatched-code passes), QT3 **21,884/0/99**
(67), and the full unstreamed development reference **2,691/20/1** (5). Their failure-ID sets exactly match
the unchanged baselines. Conformance tables were regenerated from the final reports. All functional and
native jobs completed before the isolated performance gate began. Complex selection projection, other native
expression paths, parameter continuation projection, foreign function ownership, complete S1/S2 enforcement
and all quantitative S7 streaming gates remain open. Phase 7 remains incomplete and all branch changes
remain uncommitted.

The final isolated three-pair ordinary-path gate against `0f2ddcb` passes: geometric mean head/base time
**1.032** over 39 rows and maximum allocation ratio **1.042** (cold start). Cold-start time is **1.037**.
Individual time outliers remain unresolved: keys compilation **2.044**, sorting compilation **1.139**, join
compilation **1.057**, 1 KB regex **1.170**, and 1 MB JSON **1.056**. The keys compilation measurement is a
material regression in this run despite the aggregate gate passing. Its cause and attribution remain unproven.

Retained keys compilation round medians are base **0.29295/0.41565/0.2818 ms**, head
**0.5988/0.3065/0.6162 ms**. Sorting compilation is base **0.4061/0.51165/0.5704 ms**, head
**0.59305/0.5171/0.58295 ms**. Join compilation is base **0.0882/0.0901/0.0915 ms**, head
**0.0952/0.0932/0.1001 ms**. The 1 KB regex row is base **0.0047/0.0047/0.0047 ms**, head
**0.0055/0.0055/0.0054 ms**. The 1 MB JSON row is base **9.09605/9.3639/9.0998 ms**, head
**9.7087/9.6057/9.1019 ms**. Earlier outliers remain unresolved. Both sides used the same current harness and
thresholds, and the final gate was not rerun. The log is `/tmp/xsl3-p7-deep-equal-perf.log`; exactly the three
raw pairs and comparison are retained in `/tmp/xsl3-p7-deep-equal-perf-data/`.

These results do not establish streaming throughput, retained-memory, replay-byte or first-output latency
budgets. Explicit grounded operands and constructed results may grow. Phase 7 remains incomplete and all
branch changes remain uncommitted.

### S7 generated-input process probe foundation

Added the `stream-probe` test-assembly verb and `eng/streaming-gate.py`. The seven workloads exercise the public
compiler/transformer, document resolver and secondary-result handler APIs: attribute-changing identity,
adjacent-group totals, end-of-document accumulator totals, iterate balance, two-source merge, two fork result
documents and source-document from a non-streamed principal transformation. Inputs are non-seekable generated
streams with exact byte counts, attributes, nesting and mixed content. Merge splits the requested bytes across
two sources, including odd totals and unequal record counts. Inputs larger than `Int32.MaxValue` need no corpus
file or whole-input allocation. Counting SHA-256 sinks and fixture-derived independent output oracles validate
record order, amounts and exact serialization; fixture values never supply engine runtime data.

The controller uses fresh children for each tree/stream 10 MiB baseline and each measured streamed run. It
retains stdout/stderr and atomically checkpoints results after every child. An incomplete matrix is marked
`complete: false`. A successful exit needs valid finite timing/counter fields, complete fingerprints, exact
input byte counts, matching baseline hashes, positive absolute peak counters and no budget violation. Missing,
malformed, failed or interrupted evidence cannot establish a pass. A child's claimed one-byte RSS does not
bypass the independent full-lifetime `wait4` counter. Heap limits are explicit hexadecimal
`DOTNET_GCHeapHardLimit` values; baseline children do not inherit the measured cap.

`Process.PeakWorkingSet64` returned zero on this macOS host. The probe now uses `getrusage(RUSAGE_SELF)` on
64-bit Linux/macOS, with KiB-to-byte conversion on Linux; nonpositive/missing counters fail. A macOS child
reported **87,769,088 bytes**, independently compared with **89,866,240 bytes** from `/usr/bin/time -l`, whose
lifetime includes report serialization and teardown. The controller therefore enforces the larger of the child
counter and the full-lifetime outer counter. Windows child metrics use the process counter; the full-lifetime
controller remains Linux/macOS-only. See `docs/streaming-probe.md` for the CLI and measurement contract.

The approved **200 MiB input / 20 MiB absolute RSS** attempt fails all seven measured children before input
processing: startup/compilation/warmup already reaches approximately **78–83 MiB** under that heap cap. Each
reports zero measured input and no measured timing/output, with `budget-exceeded-before-measurement` and exit 3.
This is a real failed gate, not a successful large-input run. Raw evidence is
`/tmp/xsl3-p7-probe-push-budget/`, with log `/tmp/xsl3-p7-probe-push-budget.log`.

A separate **200 MiB input / 200 MiB absolute RSS** diagnostic survey produces the following results. This
limit is not the push gate, and this size is not the 2 GiB nightly gate. Peak is the full-lifetime outer counter.
Timing is a single diagnostic sample and does not establish a throughput or latency gate.

| Workload | Output validation | Peak RSS bytes | Execution ms | Input bytes at first output |
| --- | --- | ---: | ---: | ---: |
| identity | passed | 91,471,872 | 5,827.36 | 8,192 |
| adjacent | passed | 96,681,984 | 1,568.82 | 356,352 |
| accumulator | passed; RSS budget failed | 356,859,904 | 27,936.20 | 209,715,200 |
| iterate | passed | 96,305,152 | 1,676.12 | 209,715,200 |
| merge | passed | 163,856,384 | 8,879.21 | 122,880 |
| fork | execution failed: managed heap exhausted | 238,206,976 | unavailable | unavailable |
| source-document | passed | 91,783,168 | 5,994.53 | 8,192 |

All seven 10 MiB tree/stream baseline pairs agree. The survey fails overall. Fork's retained stack trace points
to `CompactDocument.Builder.FlushText` while closing the secondary-result temporary tree constructed by
`QilGenerator.V2.cs`'s result-document lowering. Small fork runs also emit their first secondary bytes only at
end of input. Bounded input teeing alone therefore does not make this result-document workload bounded; direct
secondary serialization still needs implementation. No output cap, fake result, larger approved budget or
baseline subtraction was introduced. Evidence is `/tmp/xsl3-p7-probe-200m-survey/` and its matching `.log`.

The initial survey's managed-memory reading was uncollected allocation, not retained live memory. The final
probe uses `GC.GetTotalMemory(forceFullCollection: true)` after execution and oracle validation, outside the
timed interval. A separate 200 MiB accumulator diagnostic still fails absolute peak RSS (**402,194,432 bytes**)
while reporting **1,443,624 bytes** after collection and a correct hash. This distinguishes transient heap
pressure from retained data after execution; it does not prove a bounded in-flight peak or repair the failed
RSS budget. Evidence is `/tmp/xsl3-p7-probe-accumulator-retained.json` and its `.stderr`.

The final 21-child functional smoke matrix passes all seven workloads at 64 KiB baselines / 2 MiB measured
inputs, with the memory limit explicitly disabled. Its evidence is `/tmp/xsl3-p7-probe-final-smoke/`. CI now runs
that Linux functional smoke and the three Python controller tests (valid matrix; missing/corrupt/failed evidence;
independent outer-RSS enforcement). It does not substitute this smoke for either quantitative exit. Twenty new
.NET cases per framework cover generated input, huge-input prefix reads, all seven independent oracles,
unequal merge inputs, malformed CLI arguments, impossible-budget reporting and hash finalization. Full units
pass **2,619 on net10 / 2,562 on net8**, in both Debug and Release. The final Release solution build succeeds
with zero warnings/errors. A clean Debug test compilation reports the existing xUnit2013 warning in
`Xslt30ApiTests.cs:47`; no new warning was introduced. Logs are `/tmp/xsl3-p7-probe-units-debug.log`,
`/tmp/xsl3-p7-probe-units-release.log` and `/tmp/xsl3-p7-probe-build.log`. Three Python controller tests pass;
no remote CI run is claimed.

NativeAOT delivery of the probe, bounded secondary-result serialization, accumulator peak-allocation work,
Windows full-lifetime measurement, budget CI/nightly integration and the complete quantitative S7 gates remain
open. No library production source changed in this increment. Existing ordinary-path performance and native
execution evidence remains applicable to that source; the performance gate was not rerun.

The full S1 analysis audit remains **790/129/0**. Current W3C §19.8.8.9 axis rules agree with the implementation;
121 historical sandp expectations disagree with those rules, and eight fixtures supply empty posture/sweep
expectations. No analyzer rule or baseline was changed to hide those conflicts. Evidence is
`/tmp/xsl3-p7-analysis-audit.log` and the current W3C specification snapshot `/tmp/xsl3-xslt30-spec.html`.

The previously observed **2.044** keys-compilation ratio remains unresolved. Two targeted diagnostic pairs,
using the same current harness against archived `0f2ddcb` source, show warmup/tiering sensitivity:

| Keys compile diagnostic | Base ms | Head ms | Head/base | Winner |
| --- | ---: | ---: | ---: | --- |
| Default tiering, 3,000 ms measurement | 0.0898 | 0.0924 | 1.029 | No meaningful winner from one pair |
| Tiering disabled, 1,000 ms measurement | 0.1653 | 0.1678 | 1.015 | No meaningful winner from one pair |

Compile allocations are respectively **191,231 / 195,128 bytes** (base/head) and **192,630 / 196,526 bytes**.
These are diagnostics, not a replacement performance gate, causal attribution or a regression fix. No benchmark
or compiler optimization was made to force a passing result. Raw snapshots/build logs and both pairs are under
`/tmp/xsl3-p7-compile-audit/`. The runtime's background tiering configuration is documented in Microsoft's
[runtime compilation settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation).

Phase 7 remains incomplete. All branch changes remain uncommitted.

### S5/S7 frame-owned accumulator phases and incremental secondary results

The process probe exposed two implementation defects, now repaired in the development branch. Streamed
non-validated document results with an href lift a push body into `X31ResultDocumentPush` instead of building
one complete temporary result tree per secondary document. Each secondary result gets an independent output
scope. The existing format/serialization-parameter resolver, output-URI calculation, write-ownership checks,
handler/file opener and function dispatcher are shared with materialized results. The complete output stack
and call depth are restored even when body evaluation or serialization fails. Writers are disposed on success,
body/writer errors and cancellation. Result handlers still open on the invoking thread inside a fork.

Dynamic empty hrefs retain principal-result ownership. A consumer explicitly capturing result trees still gets
a materialized tree with its result URI and annotations. Raw sequence serialization and validated result
construction retain their existing paths; they are not claimed to be bounded by this increment. The first and
later fork prongs can now independently serialize secondary document results while consuming their tee views.
Later ordinary principal-prong output may still buffer, as the plan permits.

The text method previously wrote `doc.Value` directly, bypassing character maps and Unicode normalization.
It now uses the canonical serializer. The forward text adapter discards markup, comments, PIs and attributes
before character expansion, so NFC can compose across ignored markup and attributes never enter the result.
This follows [Serialization 3.1's serialization phases and text output parameters](https://www.w3.org/TR/xslt-xquery-serialization-31/).
Character-map replacements remain outside normalization. Between physical writes the serializer retains the
final normalized text element so following combining marks and Hangul jamo can compose. NFC/NFD/NFKC/NFKD
scalar-at-a-time checks agree with whole-string normalization. A combining sequence can itself grow; this is
not a promise of constant storage for arbitrary unbounded text elements or CDATA construction. Completion also
flushes private serialization wrappers around borrowed principal writers without closing the caller's writer.

Accumulator phase annotations now live on their source frames, not in a document-wide dependent-handle table.
The observer owns no history of discarded frames. Explicit replay/copy provenance still retains frames and
published before/after phases together. An observer identity token prevents another source's frame values from
being used; publication uses the existing lock and volatile phase fields. No lock spans rule evaluation or
input reads. Existing cycle, deferred-error, retained-copy, frame-collection and fork tests remain applicable.
No GC tuning, input-size switch, fixture lookup, forced collection during execution or changed budget hides the
original problem.

The repaired cases pass a fresh **200 MiB input / 200 MiB absolute RSS** diagnostic controller run, including
10 MiB tree/stream hash baselines. All six child runs validate and the controller exits zero. This remains a
diagnostic, not either approved S7 exit. Raw evidence is `/tmp/xsl3-p7-secondary-memory-final/`; its log is
`/tmp/xsl3-p7-secondary-memory-final.log`.

| Workload | Initial survey peak bytes | Repaired peak bytes | Repaired retained managed bytes | Diagnostic outcome |
| --- | ---: | ---: | ---: | --- |
| accumulator | 356,859,904 (RSS failure) | 94,142,464 | 1,469,616 | passed, exact hash |
| fork | 238,206,976 (managed heap exhausted) | 94,371,840 | 1,535,144 | passed, both exact secondary hashes |

The repaired accumulator runs in **4,836.17 ms** and emits its total at EOF, as required by the workload. The
repaired fork runs in **200,263.11 ms** and first writes after **35.94 ms / 36,864 input bytes**, versus whole-input
buffering in the old path. These are single diagnostic samples. They do not establish the 100 MiB throughput
target, the complete first-output latency gate or a statistically meaningful speed ratio. In particular fork
throughput still needs improvement and measurement against tree execution at the required sizes.

The managed startup floor still prevents the approved 200 MiB/20 MiB push gate from passing. The 2 GiB/200 MiB
nightly gate, NativeAOT delivery of the actual process probe, budget CI integration, the micro-suite streaming
set and the final streaming-support claim remain open. Native saved-assembly correctness probes are separate
from delivering or passing the NativeAOT memory gate. Full S1/S2 enforcement and other recorded runtime paths
also remain open; this increment does not complete Phase 7.

Review also reproduced two compiler-context defects in both tree and forward modes. A computed `format` on a
nested result document saw the new document's URI instead of its caller's, selecting the wrong serializer; a
caught missing output format leaked an unbound output frame into later instructions and caused an IL-generation
null reference. Result-document attributes now compile in the caller's context, and only the contained sequence
constructor temporarily installs the new output frame, with unconditional restoration. Four regression cases
failed before the fix and pass after it. The caller-context example independently produces `<inner/>` with
**Saxon-HE 10.3J / Java 26.0.1**; evidence is `/tmp/xsl3-p7-secondary-saxon-context/`. The before/after logs are
`/tmp/xsl3-p7-secondary-context-before.log` and `/tmp/xsl3-p7-secondary-context-after.log`.

There are **36 new net10 / 34 new net8** result-document and serialization regression cases. Full units pass
**2,655 / 2,596** in both Debug and Release. The full W3C harness outside its two run-profile facts passes
**44/44**; normal and QT3 conformance are run separately. Upstream remains **1,654/framework** and differential
**394/framework**. The expanded strict selection adds `si-result-document` to the prior selected sets and is
**2,063/46/1** with six unmatched expected-error codes. Its failure IDs exactly equal the prior 45 plus the
already-failing `si-result-document-024`, independently reproduced with the pre-change library using the same
current harness. This adds coverage; it does not fix or conceal that typed-result assertion. No baseline changed.
The selected strict snapshot is `/tmp/xsl3-p7-secondary-verified-strict-report.md`.

Saved-assembly NativeAOT execution passes all earlier probes plus seven new secondary-result scenarios: forked
text/XML, Unicode normalization, compatibility composition, dynamic empty href, body failure, caller output-URI
attribute context and a caught missing format. Publication has no trim/AOT warnings, and execution confirms
`RuntimeFeature.IsDynamicCodeSupported == false`. This run uses normal globalization
(`-p:InvariantGlobalization=false`) so the Unicode cases exercise the real normalization implementation. The
frozen old compiled-fork assembly is unchanged. Publish/run evidence is
`/tmp/xsl3-p7-secondary-verified-native-publish.log` and `/tmp/xsl3-p7-secondary-verified-native-run.log`; the
executable is `/tmp/xsl3-p7-secondary-verified-native/Xsl3.Tests`. These are correctness checks, not a NativeAOT
process-memory exit or a shipped memory-probe artifact.

Final unit/build/harness logs use `/tmp/xsl3-p7-secondary-verified-`. Solution builds succeed; a rebuilding
ported-test project reports its existing CS8632/CA2200 warnings, and unit compilation retains the existing
xUnit2013 warning. No new library warning is introduced. All branch changes remain uncommitted.

The final normal profile remains **8,847/21/41** (34 unmatched expected-error codes), QT3 **21,884/0/99**
(67), and the full unstreamed development profile **2,691/20/1** (5). Their failure-ID sets exactly match the
unchanged before snapshots. Conformance tables were regenerated. Final reports/logs use
`/tmp/xsl3-p7-secondary-verified-`. The controller's three failure-handling tests and its 21-child 2 MiB
functional smoke matrix also pass (`/tmp/xsl3-p7-secondary-smoke/`); that smoke explicitly disables memory
budget enforcement and is not a quantitative exit.

All task-owned functional, native and memory jobs were terminal before the single final three-pair
ordinary-path performance gate against `0f2ddcb` began. The gate passes: geometric mean head/base time
**0.983** over 39 rows; maximum allocation ratio **1.042438** (cold start). Its log is
`/tmp/xsl3-p7-secondary-perf.log`. Exactly the three base/head JSON pairs and comparison are retained under
`/tmp/xsl3-p7-secondary-perf-data/`; stale higher-numbered artifact rounds were not included.

| Measurement | Base `0f2ddcb` | Current head | Head/base | Winner |
| --- | ---: | ---: | ---: | --- |
| Geometric mean time (normalized) | 1.000 | 0.983 | 0.983 | No meaningful winner within the gate tolerance |
| Cold-start time, ms | 132.5451 | 140.8452 | 1.063 | Base in this run |
| Cold-start allocation, bytes | 1,597,240 | 1,665,024 | 1.042438 | Base uses fewer bytes; within the allocation budget |
| 1 MiB maps time, ms | 4.22575 | 4.821 | 1.141 | Base in this run |
| Keys compilation time, ms | 0.579 | 0.36895 | 0.637 | Head in this run; attribution remains unresolved |

Cold-start and maps timings are individual regressions in this run despite the aggregate gate passing.
Cold-start round values are base **131.7891/132.5451/134.4895 ms**, head
**137.2031/140.8452/145.3564 ms**. Maps at 1 MiB are base **4.2028/4.22575/4.2517 ms**, head
**4.1995/4.821/4.9161 ms**. Keys compilation is base **0.2937/0.579/0.5889 ms**, head
**0.5807/0.3129/0.36895 ms**. This reversal does not resolve the earlier **2.044** keys regression or establish
that it was fixed; its cause remains unproven. Sorting compilation is **0.897**, join compilation **0.989**,
1 KiB regex **0.979**, and 1 MiB JSON **0.992** in this gate. Earlier outliers remain audit items. The gate was
not rerun to obtain a preferred outcome, and no ordinary-path optimization was made in this increment.

The approved push/nightly budgets, streaming throughput/latency gates and remaining S1–S7 requirements are
still open. Phase 7 remains incomplete, support is not newly claimed, and all branch changes remain uncommitted.


### S7 NativeAOT probe, public legacy secondary delivery and bounded fork batching

The process probe now has a repository-owned NativeAOT delivery path. `eng/publish-stream-probe.sh` generates
14 saved stylesheets (tree and forward versions of the seven shared fixtures), generates static entry-point
references, and publishes `tests/Xsl3.StreamingProbe.Native`. It treats trim/AOT warnings as errors and keeps
normal globalization. The generator and consumer are built by the script rather than the solution because
the consumer requires generated inputs. The native executable rejects JIT execution;
`eng/streaming-gate.py --native` also requires `DynamicCodeSupported == false` in each report. Managed and native
hosts share their input generator, fixtures, output oracles, counters and source-generated JSON schema.

A missing public legacy API path prevented fork secondary results from being delivered by a saved native
consumer. `XsltArgumentList.ResultDocumentHandler` now provides the same `Func<Uri, TextWriter>` contract as the
3.0 transformer. The URI can remain relative without a base output URI. The existing invocation scope installs
and restores the handler, including errors; an explicit 3.0 transformer handler takes precedence. Both APIs
reuse one adapter and the canonical output/ownership path. Returned writers remain engine-owned and are
disposed on completion, errors and cancellation. Streamed output may be partial on a later error.

Five net10 / four net8 tests cover compiled/saved legacy delivery, repeat use, relative URIs, no handler leakage,
explicit-handler precedence and exception identity/ambient restoration. The actual native fork fixture also
checks that both result handlers open on the invoking thread. Fixture values never supply engine runtime data.

Fork input previously made a synchronous invoking-thread round trip for every source event. It now fills a
batch from space reserved in the existing tee ring. Staged plus published payloads together stay within the
configured event capacity; staging adds a reference array, cleared on publication/discard. Source I/O runs
outside the ring lock and on the invoking thread. Every reader receives an accepted prefix before its shared
source fault. Detachment, cancellation, declaration-order error selection and buffered-output semantics are
unchanged. Eight new cases cover reservations/event identity, partial-batch faults, malformed batches,
consumption during source I/O and detachment while a batch is staged. Existing fork/accumulator/nested/error
cases also pass. This does not promise that arbitrary fork output is bounded; the spec permits output buffering.

The targeted **2 GiB input / 200 MiB absolute RSS** native fork matrix passes all three children. The measured
child consumes **2,147,483,648 bytes**, validates both secondary fingerprints and empty principal output,
reports **30,625,792 bytes** full-lifetime RSS and **362,760 retained managed bytes**, and has dynamic code disabled.
Its first output is after **3.46 ms / 36,864 input bytes**; execution is **241,403.64 ms**. Evidence is
`/tmp/xsl3-p7-linux-probe/artifacts/nightly-batch-fork/`. A macOS 200 MiB/200 MiB diagnostic also passes all three
children at **30,801,920 bytes** RSS, **12,503.73 ms** execution and first output after **2.26 ms / 36,864 bytes**.
Its output fingerprints exactly equal the retained pre-batching 200 MiB run. Evidence is
`/tmp/xsl3-p7-batch-mac-fork/`. These are diagnostic timing samples, not a controlled throughput comparison.
On the same Linux native fixture the 10 MiB streamed baseline changes from **28,957.69 ms** before batching to
**1,235.94 ms** after it; the latter tree baseline is **139.30 ms**. The required 100 MiB throughput gate remains
unproven, and these samples still show a substantial fork cost relative to building the tree.

Every report now includes the runtime's effective managed-memory allowance. A capped result is valid only when
`AvailableManagedBytes` is positive and at most the requested limit. The independent full-lifetime RSS check
remains mandatory. The controller also has an explicit per-child wall-time guard; a timeout kills/reaps the child
with `wait4`, retains its lifetime RSS, marks `timedOut: true` and fails even if valid-looking JSON was printed.
It uses `os.kill` directly so a Popen polling/reaping race cannot discard the RSS counter. The default guard is
disabled; CI explicitly uses 900 seconds. This is a resource guard, not a throughput threshold. Six controller
tests cover malformed/failed evidence, ignored heap caps, JIT-as-native, independent RSS and timeout failures.

Both final 21-child 2 MiB zero-limit smoke matrices pass. CI is configured to publish the native probe, run its
smoke beside the existing native sample, and enforce the exact **200 MiB/20 MiB** push gate. The nightly workflow
now runs the exact **2 GiB/200 MiB** matrix. Logs and report/stdout/stderr artifacts survive failures. There is no
advisory waiver for these memory gates. They are configured development gates with known local failures;
no remote CI execution or passing complete budget matrix is claimed.

The test-only portable SHA-256 sink removes a measurement-only OpenSSL dependency on Linux. Its constants,
padding and compression follow FIPS 180-4, with ten standard-vector/chunk/padding/reset/disposal cases checked
against .NET SHA-256. It is confined to test/probe projects, not a cryptography API or a FIPS-validation claim.
Input/output bytes, fingerprint protocol, independent oracles and approved limits are unchanged. A 2 GiB native
identity run exactly matches the 2,352,895,118-byte fingerprint retained from the independent BCL hash run:
`1E622BC6E724D0ED1E59F61E1AA0FDB46F31C812E9EFF361915F279991222799`. This exercises a message
length beyond SHA-256's 32-bit bit-count boundary.

The final push attempts still fail overall. Both controllers complete all 21 children using the exact
200 MiB input, 20 MiB heap cap and absolute RSS limit. Zero measured bytes means warmup exceeded the limit.

| Workload | macOS arm64 peak bytes | macOS measured bytes | Linux arm64 peak bytes | Linux measured bytes |
| --- | ---: | ---: | ---: | ---: |
| identity | 20,611,072 | 209,715,200 | 25,333,760 | 209,715,200 |
| adjacent | 22,413,312 | 0 | 26,554,368 | 209,715,200 |
| accumulator | 22,511,616 | 0 | 26,578,944 | 209,715,200 |
| iterate | 22,937,600 | 0 | 22,298,624 | 0 |
| merge | 22,724,608 | 0 | 21,385,216 | 0 |
| fork | 22,773,760 | 0 | 21,381,120 | 0 |
| source-document | 20,692,992 | 209,715,200 | 25,919,488 | 209,715,200 |

Only the two macOS full-input rows pass this budget. The four full-input Linux rows validate output but exceed
absolute RSS. A separate explicit Linux `DOTNET_GCConserveMemory=9` diagnostic also fails (identity 24,719,360
bytes; iterate fails before measurement at 21,897,216 bytes). That policy is not configured in the deliverable
or CI. Size-oriented native code generation and portable hashing did not resolve the approved push gate.
Final push evidence is `/tmp/xsl3-p7-batch-push-mac/` and
`/tmp/xsl3-p7-linux-probe/artifacts/push-batch/`; earlier failed attempts remain preserved.

The preceding full 2 GiB/200 MiB native matrix ends **failed**, with all 21 children recorded. Identity, adjacent,
accumulator, iterate and source-document consume complete inputs with correct output under the limit. Merge
was still running when its resident set reached **250,421,248 bytes**, beyond **209,715,200**; only that task-owned
child was terminated after the confirmed violation. Complete merge input/output validation is unavailable.
The old measured fork child was separately interrupted to implement batching, and has no valid completed
measurement. Its replacement targeted matrix passes as described above; results across these different
binaries are not combined into a single complete-matrix pass. Evidence is
`/tmp/xsl3-p7-linux-probe/artifacts/nightly-portable/`; explicit termination records are
`/tmp/xsl3-p7-linux-probe/nightly-merge-termination.json` and `nightly-fork-termination.json`. Merge's root cause
and a complete passing nightly run remain open. No budget was relaxed, baseline-subtracted or replaced by
live managed heap. Linux uses the cached official SDK arm64 image, SDK 10.0.400 and normal ICU globalization;
only a disposable source snapshot/artifact directory is mounted, preserving host workspace build outputs.

The complete strict profile is **2,634/77/1 (97.2%)**, with seven expected errors accepted without matching codes.
Its failure-ID set is identical before and after batching. This is broader evidence than the previous selected
sets; it does not erase the 77 failures. Its xUnit fact exits nonzero because no accepted strict baseline exists;
none was added. The generated conformance table now includes this explicitly incomplete strict profile when its
report is present. Evidence is `/tmp/xsl3-p7-native-final-xslt30-run-strm-strict.md` and
`/tmp/xsl3-p7-batch-xslt30-run-strm-strict.md`. The earlier S1 analysis audit remains a separate unresolved exit.

Full Debug and Release units pass **2,678 / 2,618**. The normal W3C harness passes **46/46**, including QT3 and
normal conformance; upstream remains **1,654/framework**, differential **394/framework**. Normal conformance is
**8,847/21/41** (34 unmatched expected-error codes), QT3 **21,884/0/99** (67), and the full unstreamed development
profile **2,691/20/1** (5), with unchanged failure IDs. The solution builds with its existing ported-test
CS8632/CA2200 warnings and no new library warning; clean units retain xUnit2013. All retained NativeAOT
correctness probes, including the frozen version-2 fork assembly, pass against the batched runtime with normal
globalization and no trim/AOT warnings. Final functional logs use `/tmp/xsl3-p7-batch-`; the native deliverable is
`/tmp/xsl3-p7-batch-native/bin/Xsl3.StreamingProbe.Native`. All branch changes remain uncommitted. Phase 7's
remaining analysis, runtime, budget, throughput/latency, benchmark and feature-claim requirements remain open.


All task-owned native, memory and engine regression jobs were terminal before the single final three-pair
ordinary-path performance gate against `0f2ddcb`. It passes: head/base time geometric mean **0.993** over
39 timed rows, maximum allocation ratio **1.042453** (cold start). Exactly three base/head JSON pairs and the
comparison are retained in `/tmp/xsl3-p7-batch-perf-data/`; no stale higher-numbered rounds were included.
The gate log is `/tmp/xsl3-p7-batch-perf.log`. A mistyped base SHA stopped an initial command before any build
or measurements; that error is retained separately in `/tmp/xsl3-p7-batch-perf-invalid-base.log`.

| Measurement | Base `0f2ddcb` | Current head | Head/base | Winner |
| --- | ---: | ---: | ---: | --- |
| Geometric mean time (normalized) | 1.000 | 0.993 | 0.993 | No meaningful winner within the gate tolerance |
| Cold-start time, ms | 137.2348 | 140.2511 | 1.022 | No meaningful winner within the gate tolerance |
| Cold-start allocation, bytes | 1,597,240 | 1,665,048 | 1.042453 | Base uses fewer bytes; within the allocation budget |
| Keys compilation time, ms | 0.2846 | 0.3154 | 1.108 | Base in this run |
| Dispatch compilation time, ms | 1.5291 | 1.6280 | 1.065 | Base in this run |
| 1 KiB regex time, ms | 0.0048 | 0.0055 | 1.146 | Base in this run |
| 1 MiB maps time, ms | 4.1612 | 4.1527 | 0.998 | No meaningful winner within the gate tolerance |

The aggregate pass does not resolve the individual regressions or earlier outliers. Keys round values are
base **0.2846/0.2905/0.2615 ms**, head **0.6/0.3154/0.3065 ms**. Dispatch is base
**1.5291/1.4812/1.7325 ms**, head **1.564/1.8273/1.628 ms**. The 1 KiB regex is base
**0.0047/0.0048/0.0048 ms**, head **0.0052/0.0055/0.0056 ms**. Cold start is base
**137.2348/142.3545/134.2166 ms**, head **152.2499/139.0999/140.2511 ms**. Maps is base
**4.53645/4.0557/4.1612 ms**, head **4.1527/4.09985/5.0592 ms**. No attribution or fix is claimed for the
previous keys/maps/cold-start variation. No ordinary-path optimization or gate rerun was made to obtain a
preferred result. Phase 7 remains active and incomplete, with all changes uncommitted.


### S7 copy-owned streamed provenance and complete native memory matrix

The previous complete 2 GiB matrix exposed merge's process-memory failure. A scratch diagnostic now isolates
its cause: recording every short-lived streamed snapshot in the long-lived copy-origin weak table builds
collection pressure. At 256 MiB input the diagnostic's normal native run reaches **118,472,704 bytes** peak RSS,
with **57,323,840 bytes** reported managed allocation near the end of processing. A scratch-only full-collection
ablation at 16 MiB intervals reaches **41,742,336 bytes** and has the same output hash. The same storage behavior
appears under CoreCLR. This is diagnostic evidence, not a production GC policy or an approved budget pass.
Replacing only streamed-copy origin storage in the scratch build completes a full 2 GiB input with valid output
at **30,142,464 bytes** peak, without those extra collections. Scratch sources/logs are under
`/tmp/xsl3-p7-merge-diagnostic/`; its raw output/progress files use
`/tmp/xsl3-p7-linux-probe/artifacts/merge-diagnostic-`. None of that instrumentation or forced-collection policy
was copied into repository runtime/probe code.

`CopyOriginRegistry` now attaches streamed provenance to the compact copy through an opaque tree annotation.
An independent scope token identifies its invocation/fork family without retaining the registry itself. The
copy owns the origin payload and immutable scope links; the registry owns no history of discarded streamed
copies. Copies of streamed copies preserve this route. Compare/exchange publication supports independent
scopes annotating the same copy, and replacing one scope preserves the others. Payloads are composed with
storage links rather than inheriting from them: a borrowed payload cannot retain obsolete foreign-scope
links. A regression reproduced that lifetime edge in the first implementation and passes with this separation.

Ordinary grounded copies retain the existing invocation-owned weak table. This avoids a small returned
grounded copy retaining its whole original tree after the invocation is collected. A new lifetime assertion
passes against both the frozen pre-change library and the final library when the weak table's documented
two-phase container finalization is fully drained. The initial two-collection assertion failed on both and
was corrected as a test assumption, not attributed to this implementation. See the
[.NET 10.0.11 weak-table container implementation](https://github.com/dotnet/runtime/blob/v10.0.11/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/ConditionalWeakTable.cs).

Completed `StreamedDocument`s also release the reader reference. Their retained headers, replay and accumulator
phases remain available, while parser/input buffers can be collected. Borrowed readers are not closed; owned
readers keep their disposal behavior, including failure paths. The new reader-retention test fails against the
frozen previous library and passes after this fix. That isolated before check did not replace workspace binaries.
Eight new tests per framework cover scoped publication/replacement, concurrent independent scopes, copies of
copies, collection of discarded copies, registry lifetime, grounded-input lifetime and borrowed-reader release.
Existing source-close, accumulator, validation, retained-copy, fork and error cases remain applicable.

The finalized canonical NativeAOT probe now passes the **complete local 2 GiB / 200 MiB matrix**: all 21 children
pass, the report is complete and its failure list is empty. Every measured child consumes exactly
**2,147,483,648 input bytes**, including the combined size of merge's two sources. The independent large-input
oracles validate every principal/secondary fingerprint, and each separate 10 MiB tree/stream baseline pair
matches. All child and full-lifetime counters are positive and the heap allowance is exactly 209,715,200 bytes.
No budget was changed, baseline-subtracted or replaced by retained managed heap.

| Workload | Full-lifetime RSS bytes | Retained managed bytes | Execution ms | First-output ms | Input bytes at first output |
| --- | ---: | ---: | ---: | ---: | ---: |
| identity | 29,261,824 | 379,280 | 83,075.71 | 1.14 | 8,192 |
| adjacent | 27,078,656 | 357,960 | 14,196.53 | 4.63 | 356,352 |
| accumulator | 30,203,904 | 357,792 | 52,321.13 | 52,321.12 | 2,147,483,648 |
| iterate | 28,323,840 | 413,448 | 13,605.06 | 13,605.03 | 2,147,483,648 |
| merge | 30,638,080 | 401,752 | 58,575.05 | 6.38 | 122,880 |
| fork | 30,429,184 | 363,056 | 246,539.21 | 5.89 | 36,864 |
| source-document | 29,265,920 | 382,016 | 82,526.03 | 0.66 | 8,192 |

The raw final matrix is `/tmp/xsl3-p7-linux-probe/artifacts/nightly-copy-owned-final/`, log
`/tmp/xsl3-p7-linux-probe/copy-owned-final.log`, executable
`/tmp/xsl3-p7-linux-probe/artifacts/native-copy-owned-final/Xsl3.StreamingProbe.Native`. It uses normal ICU
globalization and strict warning-as-error publication in the same official SDK 10.0.400 arm64 container used
for previous evidence. This is local Linux arm64 evidence; the configured GitHub linux-x64 nightly job has not
run against these uncommitted changes. The timing rows are single diagnostics and do not establish the required
100 MiB throughput comparison or a complete latency gate. Accumulator and iterate totals intentionally emit at
EOF. The earlier failed matrix and termination records remain preserved.

Review also reproduced a native-proof gap. The SDK's managed intermediate has
`RuntimeFeature.IsDynamicCodeSupported == false` in its runtime configuration, so that flag alone cannot reject
CoreCLR execution. The native entry point now also rejects the trusted-platform-assembly host property, and
reports include mandatory `HasTrustedPlatformAssemblies` evidence. The controller requires false for both host
fields under `--native`. Fake-child tests reject a CoreCLR-like report even when its dynamic-code flag is false,
and reject missing host evidence. Actual managed-intermediate invocations on macOS and Linux exit 1 with the
explicit NativeAOT-required message before input processing. The SDK switch is visible in its
[NativeAOT build targets](https://github.com/dotnet/runtime/blob/v10.0.11/src/coreclr/nativeaot/BuildIntegration/Microsoft.NETCore.Native.targets).
CI now runs that real negative check and retains its stdout/stderr; no missing-library error can satisfy it.
All final native matrix reports have both host fields false. The previously published native executables were
run directly after native compilation; this strengthens the guard without reclassifying those actual native
runs as managed measurements.

The exact **200 MiB / 20 MiB** push recheck still fails overall, with complete 21-child reports on both hosts.
Only identity/source-document pass on macOS. Linux validates four complete inputs but exceeds RSS; its other
three and five macOS workloads exceed the limit during warmup. Zero input denotes an unexecuted measured run.

| Workload | macOS RSS bytes | macOS measured input | Linux RSS bytes | Linux measured input |
| --- | ---: | ---: | ---: | ---: |
| identity | 20,627,456 | 209,715,200 | 25,415,680 | 209,715,200 |
| adjacent | 22,396,928 | 0 | 26,357,760 | 209,715,200 |
| accumulator | 22,511,616 | 0 | 26,202,112 | 209,715,200 |
| iterate | 22,855,680 | 0 | 22,106,112 | 0 |
| merge | 22,691,840 | 0 | 21,168,128 | 0 |
| fork | 22,691,840 | 0 | 21,196,800 | 0 |
| source-document | 20,676,608 | 209,715,200 | 25,710,592 | 209,715,200 |

Final push evidence is `/tmp/xsl3-p7-copy-owned-final-push-mac/` and
`/tmp/xsl3-p7-linux-probe/artifacts/push-copy-owned-final/`. The push limit is unchanged and has no waiver.
The complete strict audit remains **2,634/77/1**, with seven unmatched expected-error codes and exactly the
same failure IDs. Normal and unstreamed development baselines and QT3 are unchanged. No strict baseline was
added and no feature support claim changed. Full Debug/Release units pass **2,686 net10.0 / 2,626 net8.0**;
the normal W3C harness passes **46/46**, upstream **1,654/framework**, differential **394/framework**. All
retained saved-assembly NativeAOT correctness probes, including the frozen version-2 fork assembly, pass with
normal globalization and no trim/AOT warnings. Six controller tests and the two 21-child functional smoke
matrices pass with the strengthened host-evidence schema. Final regression/native logs use
`/tmp/xsl3-p7-copy-owned-final-`. All changes remain uncommitted and Phase 7 remains incomplete.


After all task-owned native, memory and engine regression processes were terminal, the single final ordinary-path
three-pair gate against `0f2ddcb` passes: geometric mean head/base time **0.996** across 39 timed rows and maximum
allocation ratio **1.042463** (cold start). Exact three-pair JSON/comparison/summary evidence is retained in
`/tmp/xsl3-p7-copy-owned-final-perf-data/`; stale higher-numbered rounds were not included. The gate log is
`/tmp/xsl3-p7-copy-owned-final-perf.log`. The gate was not rerun to obtain a preferred result.

| Measurement | Base `0f2ddcb` | Current head | Head/base | Winner |
| --- | ---: | ---: | ---: | --- |
| Geometric mean time (normalized) | 1.000 | 0.996 | 0.996 | No meaningful winner within the gate tolerance |
| Cold-start time, ms | 132.4223 | 139.4305 | 1.053 | Base in this run |
| Cold-start allocation, bytes | 1,597,240 | 1,665,064 | 1.042463 | Base uses fewer bytes; within the allocation budget |
| Grouping compilation, ms | 0.57165 | 0.62885 | 1.100 | Base in this run |
| Dispatch compilation, ms | 1.5091 | 1.5941 | 1.056 | Base in this run |
| 1 KiB join, ms | 0.0035 | 0.0037 | 1.057 | Base in this run |
| 1 KiB regex, ms | 0.0047 | 0.0054 | 1.149 | Base in this run |
| 1 MiB maps, ms | 4.02225 | 4.8054 | 1.195 | Base in this run |
| 1 MiB JSON, ms | 9.3024 | 9.7780 | 1.051 | Base in this run |
| Keys compilation, ms | 0.49555 | 0.29875 | 0.603 | Head in this run; prior attribution remains unresolved |

These individual time regressions remain audit items despite the aggregate pass. Cold-start rounds are base
**135.3599/130.4556/132.4223 ms**, head **139.4305/142.419/137.4563 ms**. Dispatch is base
**1.5091/1.4951/1.5399 ms**, head **1.57145/1.624/1.5941 ms**. Maps is base
**4.02225/3.9743/4.18085 ms**, head **4.39085/4.8054/4.8421 ms**. The 1 KiB regex is base
**0.0047/0.0047/0.0046 ms**, head **0.0056/0.0054/0.0054 ms**. Keys is base
**0.4417/0.59645/0.49555 ms**, head **0.2794/0.3653/0.29875 ms**. This reversal does not establish a fix for the
earlier keys regression or resolve its cause. No ordinary-path optimization was introduced or attributed in
this increment. The 20 MiB push limit, remaining conformance/analysis/runtime requirements, streaming benchmark
set and throughput/latency exits, remote CI evidence and the final feature claim remain open. Phase 7 remains
active and all branch changes remain uncommitted.


### S4 singleton function child-path ordering

The strict `function/function-5002` failure was reduced to an absorbing function receiving a required or
optional singleton node and copying `$node/doc/section[1]/section/p[2]`. Tree execution passed; forward
execution failed in `StreamedDocument.FirstChild` while revisiting a selected element. The initial regression
and stack are retained in `/tmp/xsl3-p7-function-navigation-before.log`; the compiler explanation in
`/tmp/xsl3-p7-function-navigation-qil.xml` shows the intervening document-order sorts.

`XPath31Compiler.PathAsWritten` now retains the existing order/disjointness proof when a child or attribute
step starts from at most one node. Previously only syntactically recognized anchors and populations already
marked at the same depth retained it. A coerced singleton function parameter therefore lost the proof;
subsequent path prefixes could sort and exhaust the forward selection before its consumer ran. Ordinary
compilation keeps its existing condition. Arbitrary many-valued populations still need their existing
canonicalization. No replay buffer, implicit grounding, navigator relaxation or function-specific result is
introduced.

`StreamedFunctionNavigationTests` adds 12 net10.0 / 9 net8.0 cases. The fixture has multiple candidate siblings
and nested parents so `[1]` and per-parent `[2]` selections are independently observable. Required and optional
parameters, absent optional input, ordinary/tree comparison, retained calls under tracing/depth checks, both
public transformer paths and saved assemblies are covered. The focused existing function/ordered-path suites
also pass (82 net10.0 / 76 net8.0 cases). The isolated saved-stylesheet NativeAOT harness passes the new optional
singleton navigation case and all retained compatibility probes, including the frozen version-2 fork assembly,
with normal globalization and no trim/AOT warnings. Native generation/publish/run logs are
`/tmp/xsl3-p7-singleton-path-native-{generate,publish,run}.log`.

The complete strict profile is **2,635 passed / 76 failed / 1 not run (97.2%)**, with seven unmatched-code
passes. Only `function/function-5002` leaves the failure list; no new failure IDs appear. The strict fact
correctly still exits nonzero and no baseline was added. Reports are retained in
`/tmp/xsl3-p7-singleton-path-strict-report.md` and `/tmp/xsl3-p7-singleton-path-strict.log`. Full Debug and Release units
pass **2,698 net10.0 / 2,635 net8.0**. The previous local 2 GiB memory result is retained; this compiler-only
repair does not introduce new source consumption or ownership mechanisms and the large matrix was not repeated.
The remaining 76 strict failures, analysis rules, exact error-code contracts, 20 MiB push budget, streaming
benchmarks and throughput/latency exits remain open. Phase 7 remains active; all changes remain uncommitted.


Final regression checks for this increment: Debug/Release units **2,698 net10.0 / 2,635 net8.0**; normal W3C
harness **46/46**; the configured upstream CI selection **1,654/framework**; differential **394/framework**.
The initial unfiltered upstream invocation included the repository's pre-existing `category=failing` cases:
four Windows-only path expectations fail on macOS, while the CI selection excludes them. No source or trait
was changed to obtain the configured pass. Both logs are retained as
`/tmp/xsl3-p7-singleton-path-upstream{,-ci}.log`. Normal XSLT, unstreamed development and QT3 failure IDs are
unchanged: **8,847/21/41** (34 unmatched codes), **2,691/20/1** (five), **21,884/0/99** (67), respectively.
The solution Release build and `git diff --check` pass. Other regression logs use
`/tmp/xsl3-p7-singleton-path-{unit-debug,unit-release,w3c,differential,solution}.log`.


After all task-owned engine/native regression processes were terminal, the single final three-pair ordinary
performance/allocation gate against `0f2ddcb` passes: geometric mean **1.001167** across 39 timed rows;
maximum allocation ratio **1.042463**, at cold start. Exact three-pair JSON, comparison and summary evidence
is in `/tmp/xsl3-p7-singleton-path-perf-data/`; the log is `/tmp/xsl3-p7-singleton-path-perf.log`. Stale fourth
and fifth rounds in the shared artifacts folder were excluded. The temporary base worktree was removed by
the gate. No rerun was made to obtain a preferred ratio.

| Measurement | Base `0f2ddcb` | Current head | Head/base | Winner |
| --- | ---: | ---: | ---: | --- |
| Geometric mean time (normalized) | 1.000 | 1.001167 | 1.001167 | No meaningful winner within the gate tolerance |
| Cold-start time, ms | 137.269900 | 140.190800 | 1.021279 | Base in this run |
| Sorting compilation, ms | 0.497700 | 0.611500 | 1.228652 | Base in this run |
| Keys compilation, ms | 0.306000 | 0.323400 | 1.056863 | Base in this run |
| 1 KiB regex, ms | 0.004700 | 0.005300 | 1.127660 | Base in this run |
| Cold-start allocation, bytes | 1,597,240 | 1,665,064 | 1.042463 | Base uses fewer bytes; within the allocation budget |

The individual timing regressions remain open for attribution despite the aggregate pass. Sorting compilation
rounds are base **0.4977/0.6096/0.45245 ms**, head **0.6115/0.5960/0.6129 ms**. Keys compilation is base
**0.2996/0.3234/0.3060 ms**, head **0.3234/0.6051/0.2750 ms**. The 1 KiB regex is base
**0.0048/0.0046/0.0047 ms**, head **0.0053/0.0053/0.0054 ms**. This run does not establish fixes for earlier
individual regressions or explain their causes. No ordinary-path optimization is introduced or attributed by
the singleton ordering increment. Remaining S1/runtime rules, exact error codes, memory/throughput/latency exits
and remote CI evidence still prevent a Phase 7 completion claim.


### S4 function path order and population scoped pruning

The next audit repairs the eight remaining failures in the strict `function` set and the two remaining
`sf-outermost` cases. The complete strict profile is now **2,645 passed / 66 failed / 1 not run (97.6%)**,
with seven unmatched expected-error codes. The `function` selection is **16/16** and `sf-outermost` is **26/26**.
No new failure IDs appear. The strict fact still exits nonzero and no strict baseline was added.
The repaired IDs are `function-5004`, `function-5005`, `function-5006`, `function-5012`, `function-5014`,
`function-5015`, `function-5017`, `function-5018`, `sf-outermost-102` and `sf-outermost-103`.

The reduced filter/shallow-descent cases failed because document-order sorts remained around expanded
function results. `StreamedPathOrder` now derives order and disjointness from the actual QIL: one anchor,
child depth when fixed, filters, checked node conversions, conditional branches and one-time bindings.
It runs after streamed function lowering and before the optimizer's preservation contract is captured.
A descendant scan can be ordered without being disjoint; child navigation from overlapping populations
therefore retains its canonicalization. Repeated or unrelated grounded results also retain sorting and
identity deduplication. A function category alone supplies no order proof. Type assertions keep the native
node/order contract; runtime item, cardinality and error checks remain in the graph.

Nested child/descendant paths with header-safe predicates now reuse the shared header-prefix scanner.
This traverses candidates once rather than repeatedly descending overlapping ancestors. Descendant-count
and invalid-prefix caches keep repeated shape checks linear for long paths. Unknown host predicate calls
fall back to ordinary lowering instead of acquiring a built-in predicate policy.

General forward `outermost()` now uses a suspended, canonical node producer and retains only its last
accepted ancestor. It validates order, skips duplicate/nested candidates and retains producer failures.
Arbitrary grounded arguments still sort before filtering. Accepted streamed nodes prune only the descendant
cursors used by their own producer pull, before the consumer can navigate or absorb their content. Independent
cursors keep their missing-match checks; no document-wide pruning flag was introduced. Both the general cursor
and the existing specialized outermost child-path cursor carry a disjoint order proof for later child steps.

Pruning observation uses an exception-safe thread-local scope, including nested scopes and the initial
or-self yield. Review reproduced a retention bug in the first observer: a strong cursor list kept abandoned
cursors alive during a long pull. `DescendantPruningTests.ALongPullDoesNotOwnAbandonedDescendantCursors` fails
against that version and passes after weak observations plus finished/dead-entry removal. Scope disposal also
clears its observations. The before/after logs are `/tmp/xsl3-p7-pruning-retention-{before,after}.log`; the
before runtime is retained as `/tmp/xsl3-p7-pruning-strong-before.dll`. This adds no forced collection to engine
execution. The unit regression collects outside the engine to check actual ownership.

There are **25 new net10.0 / 21 new net8.0** regression cases: live filter and shallow/deep paths, mixed grounded
results, canonical order/identity, ancestor rules, non-node errors, early output measured against actual input
characters read, saved assemblies, independent cursor checks, nested exception restoration, or-self handling
and abandoned cursor release. Direct runtime cases also verify sticky producer failures, order-contract errors
and advancing only after the current accepted node is exposed. Full Debug/Release units pass
**2,723 net10.0 / 2,656 net8.0**. Four new saved
assemblies and all retained NativeAOT correctness probes, including the frozen version-2 fork assembly, pass
with normal globalization and no trim/AOT warnings. The two new runtime helper signatures are recorded in
`PublicAPI.Unshipped.txt`; the saved data format is unchanged. Native evidence is
`/tmp/xsl3-p7-function-path-final-native-{publish,run}.log`, with generation in
`/tmp/xsl3-p7-function-path-native-generate.log`. Strict evidence is
`/tmp/xsl3-p7-function-path-final-strict.log`; other final regression logs use the
`/tmp/xsl3-p7-function-path-final-` prefix.

The first Linux memory run was deliberately superseded after the observer-retention regression was found.
Its checkpoint is **complete: false, 17 completed children**; its task-owned container exited 137 when stopped.
That partial run is not a gate pass. The checkpoint, log and reason are preserved under
`/tmp/xsl3-p7-function-path-linux/`, including `superseded.json`. The corrected source has its own fresh build
and reports under `/tmp/xsl3-p7-function-path-final-linux/`.

These results establish the named cases and contracts, not complete function support across every tracing,
caching, recursion, higher-order or fork-continuation combination. Remaining strict failures, S1 rules, exact
error codes, the 20 MiB push budget, streaming benchmarks and throughput/latency exits still prevent a Phase 7
completion claim. The goal remains active and all changes remain uncommitted.


Final compatibility checks pass: normal W3C harness **46/46**, configured upstream CI selection
**1,654/framework**, differential **394/framework**. Normal/unstreamed development/QT3 failure IDs remain
unchanged at **8,847/21/41**, **2,691/20/1** and **21,884/0/99**, with 34/five/67 unmatched codes respectively.
The Release solution build passes with the existing 28 upstream-port warnings; the new library code introduces
no warnings. No baseline changed for this increment. Final normal evidence is
`/tmp/xsl3-p7-function-path-final-{w3c,upstream,differential,solution}.log`.

The fresh corrected-source Linux arm64 NativeAOT **2 GiB / 200 MiB** matrix passes all **21 children**,
including matching tree/stream 10 MiB baselines. Every measured stream consumes exactly **2,147,483,648 bytes**
(combined for merge), validates its independent oracle/fingerprint and has both native host-evidence flags
false with the effective 200 MiB GC cap. Full-lifetime peak RSS is below 30 MiB for the seven large-input
measured children; uncapped tree baselines are separate. The exact production snapshot matches the current
worktree (`source-check.json` contains no differences). Normal ICU globalization is retained, with SDK
10.0.400 / runtime 10.0.11 on the cached official arm64 SDK image. The managed intermediate is rejected with
the explicit NativeAOT-required message. No remote CI run is implied.

| Workload | Full-lifetime RSS bytes | Live managed bytes | Elapsed ms | First-output ms | Input bytes at first output |
| --- | ---: | ---: | ---: | ---: | ---: |
| identity | 29,286,400 | 379,208 | 87,550.743 | 0.933 | 8,192 |
| adjacent | 26,976,256 | 357,888 | 14,268.621 | 4.202 | 356,352 |
| accumulator | 30,023,680 | 357,720 | 54,002.761 | 54,002.720 | 2,147,483,648 |
| iterate | 28,094,464 | 413,376 | 14,758.694 | 14,758.677 | 2,147,483,648 |
| merge | 30,597,120 | 401,680 | 59,524.158 | 5.944 | 122,880 |
| fork | 30,420,992 | 362,984 | 197,135.191 | 4.001 | 36,864 |
| source-document | 29,327,360 | 381,944 | 87,607.238 | 0.592 | 8,192 |

These timings are diagnostics collected alongside other regression work, not a throughput comparison or
completion of the streaming throughput/latency exits. Raw current evidence is
`/tmp/xsl3-p7-function-path-final-linux/artifacts/nightly/report.json`, with build/execution log
`/tmp/xsl3-p7-function-path-final-linux/run.log` and native executable under `artifacts/native/bin/`.

The fresh exact **200 MiB / 20 MiB** push gate still fails all seven workloads. All 21 children complete;
five measured runs consume their complete input with valid output but exceed RSS, while iterate/fork exceed
the limit during warmup and measure zero input. No budget was relaxed or waived. Its controller exit code is
1; the enclosing Docker command also exits 1 for this failed gate after the passing nightly matrix.

| Workload | Full-lifetime RSS bytes | Measured input bytes | Status |
| --- | ---: | ---: | --- |
| identity | 25,546,752 | 209,715,200 | budget-exceeded |
| adjacent | 26,222,592 | 209,715,200 | budget-exceeded |
| accumulator | 26,120,192 | 209,715,200 | budget-exceeded |
| iterate | 22,102,016 | 0 | budget-exceeded-before-measurement |
| merge | 26,886,144 | 209,715,200 | budget-exceeded |
| fork | 21,311,488 | 0 | budget-exceeded-before-measurement |
| source-document | 25,780,224 | 209,715,200 | budget-exceeded |

Push evidence is `/tmp/xsl3-p7-function-path-final-linux/artifacts/push/report.json` and
`artifacts/push-exit-code.txt`. The process matrices, functional probes and conformance results cover their
specified workloads; broader function combinations, non-built-in header predicates/schema constructors and
all remaining P7 exits still need audit and implementation. Phase 7 remains incomplete.


The final isolated ordinary-path three-pair gate against `0f2ddcb` passes after all task-owned regression and
native-memory processes were terminal: geometric mean head/base **1.015593** over 39 timed rows; maximum
allocation ratio **1.042854** at cold start. The exact three-pair files, comparison and summary are retained in
`/tmp/xsl3-p7-function-path-final-perf-data/`, with log `/tmp/xsl3-p7-function-path-final-perf.log`. Only rounds
1–3 were used; stale rounds 4/5 were excluded. The gate removed its temporary base worktree. It was not rerun
to obtain a preferred ratio. Cold startup is reported separately and excluded from the configured geometric
mean; its regression remains an open item rather than being resolved by the aggregate pass.

| Measurement | Base `0f2ddcb` | Current head | Head/base | Winner |
| --- | ---: | ---: | ---: | --- |
| Geometric mean time (normalized) | 1.000 | 1.015593 | 1.015593 | No meaningful winner within the gate tolerance |
| Cold-start time, ms | 144.902400 | 187.418500 | 1.293412 | Base in this run |
| keys compile, ms | 0.315500 | 0.384100 | 1.217433 | Base in this run |
| join compile, ms | 0.094400 | 0.103000 | 1.091102 | Base in this run |
| dispatch compile, ms | 1.608950 | 1.723550 | 1.071227 | Base in this run |
| regex 1kb, ms | 0.004800 | 0.005500 | 1.145833 | Base in this run |
| sorting 1mb, ms | 2.106200 | 2.294800 | 1.089545 | Base in this run |
| maps 1mb, ms | 4.203100 | 4.932350 | 1.173503 | Base in this run |
| Cold-start allocation, bytes | 1,597,240 | 1,665,688 | 1.042854 | Base uses fewer bytes; within the allocation budget |

All individual timing regressions above remain open for attribution. Their retained rounds are:

- `grouping cold`: base **159.267000/144.902400/137.392700 ms**; head **240.072200/187.418500/166.734600 ms**.
- `keys compile`: base **0.313700/0.317100/0.315500 ms**; head **0.384100/0.351000/0.421200 ms**.
- `join compile`: base **0.097500/0.094400/0.094400 ms**; head **0.097900/0.103000/0.138100 ms**.
- `dispatch compile`: base **1.638050/1.608950/1.595350 ms**; head **1.645700/1.723550/3.877250 ms**.
- `regex 1kb`: base **0.004800/0.004700/0.004800 ms**; head **0.005600/0.005500/0.005500 ms**.
- `sorting 1mb`: base **2.106200/2.127800/2.092200 ms**; head **2.294800/2.124200/2.461600 ms**.
- `maps 1mb`: base **4.203100/4.264300/4.187600 ms**; head **5.009400/4.160600/4.932350 ms**.

No ordinary-path optimization is introduced or attributed by this increment. The observed cold/compile,
regex, sorting and map regressions, remaining strict/S1/error-code work, push memory budget and streaming
throughput/latency evidence remain part of the full Phase 7 objective. No source was committed or pushed.


### S4 bounded ancestor union and intersection

The complete strict development audit improves to **2,655 passed / 56 failed / 1 not run (97.9%)**,
with seven unmatched expected-error codes. Ten cases are repaired: `sx-union-331` through `sx-union-335`
and `sx-intersect-331` through `sx-intersect-335`. No new failure IDs appear. Intersection is **64/64**;
union is **73/74**, with the existing `sx-union-202` XTSE3430 mismatch still visible. The strict fact
still exits 1 and no strict baseline was added.

Matching ancestor projections now share one ordered header-prefix scan. Each active source frame owns
leaf membership and emitted flags; an event drains its eligible headers only after all operand updates,
in document order. Nested union/intersection plans remain monotonic. The compiler requires the same
header projection on every operand and proven ordered, disjoint scan roots. It excludes positional
ancestor predicates, differing projections and `except`. Ordered owner headers also preserve order when
projecting their attributes, without sorting an entire streamed population. Grounded inputs retain their
normal canonicalization when these proofs are absent.

Review reproduced an ordering bug in the first version: different ancestor tests could discover a root
only after another operand emitted a descendant. Two reduced grounded cases fail before the projection
compatibility guard and pass afterward. Their before log is
`/tmp/xsl3-p7-climbing-projection-before.log`. Reversed/overlapping grounded roots and nearest-ancestor
predicates are also covered, without treating them as proven incremental streamed selections.

`StreamedFrameFacts` attaches scoped payloads to actual source frames with weak owner references and
atomic registration. Header-prefix match facts and sibling position counters use the same ownership model.
There is no document-wide history of discarded frame keys. New registrations remove dead/disposed scope
entries; payloads do not point back to their owners. Tests check independent/concurrent scopes, abandoned
scope collection, completed payload removal and discarded source-frame collection.

A further review reproduced retention of foreign grounded documents by a completed climbing state's
ordering map. That map is now lazy and released on completion and terminal failure, together with pending
headers, membership and the previous output header. The regression keeps the completed state and runtime
alive while verifying that its foreign document is collected. Before evidence is
`/tmp/xsl3-p7-climbing-grounded-retention-before.log` and
`/tmp/xsl3-p7-climbing-grounded-retention-before.dll`; corrected focused evidence is
`/tmp/xsl3-p7-climbing-owned-focused.log`. The first memory run was superseded for this fix: its 20-child
checkpoint remains **complete: false**, the task-owned container exited 137 when stopped, and its reason
is preserved in `/tmp/xsl3-p7-climbing-final-linux/superseded.json`. It is not a gate pass.

The header-predicate guard also recognizes imported schema constructor functions. Ordinary and forward
execution retain actual schema facet validation, including FORG0001 for invalid values. Schema-aware saved
assemblies remain explicitly unsupported by the existing save API; no NativeAOT schema claim is inferred.
Two new saved ancestor-set assemblies and all retained native correctness probes, including the frozen
version-2 fork assembly, execute successfully with normal globalization. The helper ABI is recorded in
`PublicAPI.Unshipped.txt`; the saved data format is unchanged. Final native logs are
`/tmp/xsl3-p7-climbing-owned-native-{publish,run}.log`. An initial temporary consumer had the frozen fixture
under a filename different from its assembly name and failed to load it; the corrected consumer copies the
same frozen bytes as `CompiledFork.dll`, verified in its `frozen-fork-sha256.txt`. No engine compatibility
change was needed for that fixture-layout failure.

The process probe now has **nine workloads / eighteen saved fixtures**. New ancestor union/intersection
cases use differently filtered descendant populations, repeated ancestor attributes and independent output
oracles derived from generated row fields. Unit tests verify tree/stream agreement and emission before
half of a 4 MiB input is read. The corrected Linux arm64 NativeAOT 2 GiB measurements for these two workloads
already consume their complete exact inputs with valid fingerprints:

| Workload | Full-lifetime peak RSS, bytes | Live managed bytes | First output, ms | Input bytes at first output |
|---|---:|---:|---:|---:|
| Ancestor union | 30,392,320 | 359,160 | 1.908292 | 53,248 |
| Ancestor intersection | 30,523,392 | 359,168 | 16.360875 | 204,800 |

The associated 10 MiB tree/stream baselines agree. The effective measured GC cap is 209,715,200 bytes;
both native host-evidence flags are false. Timing is diagnostic because other regression processes were
running. The complete fresh matrix and exact push-budget results are recorded below.
These cases do not establish general ancestor `except`, differing projections, remaining S1/runtime/error
contracts, the throughput/latency exit or a streaming feature claim. The goal remains active.

Final corrected-source Debug/Release units pass **2,758 net10.0 / 2,689 net8.0**: **35/33** new cases
relative to the preceding function-path increment. Focused cases pass **55/53**. Normal W3C is **46/46**,
the configured upstream CI selection is **1,654/framework**, differential is **394/framework**, and the
Release solution build passes. Normal/unstreamed/QT3 counts and failure IDs remain unchanged at
**8,847/21/41**, **2,691/20/1** and **21,884/0/99**; unmatched error-code counts remain **34/five/67**.
The controller's six Python tests pass. New and changed source whitespace checks pass, and no runtime or
sample domain values were introduced. Generated XML values and expected fingerprints remain probe fixtures.
Regression evidence is `/tmp/xsl3-p7-climbing-owned-regressions.json` and its named `.log` files;
`/tmp/xsl3-p7-climbing-owned-strict-delta.json` records all ten repaired IDs and an empty new-failure list.
The first upstream invocation accidentally included known Windows-specific cases tagged `failing`; those
four failures per framework are retained in `/tmp/xsl3-p7-climbing-final-upstream.log`. The final command
uses the repository's exact `category!=failing` CI filter and passes. No baseline or category was changed.


The corrected-source Linux arm64 NativeAOT **2 GiB / 200 MiB** matrix passes all **27 children**: nine
10 MiB tree/stream baseline pairs and nine exact 2 GiB measured streams. All fingerprints validate;
every measured stream has the exact 209,715,200-byte effective GC cap and both native host-evidence flags
false. Peak full-lifetime RSS for the measured streams is **29.668 MiB or less**. This does not describe
the uncapped tree baselines, whose merge run peaks at 564,727,808 bytes.

| Workload | Measured peak RSS, bytes | Live managed bytes | Elapsed, ms | First output, ms | Input bytes at first output |
|---|---:|---:|---:|---:|---:|
| Identity | 29,847,552 | 381,104 | 85,260.124330 | 1.270416 | 8,192 |
| Adjacent groups | 27,344,896 | 359,784 | 14,044.934298 | 3.853709 | 356,352 |
| Accumulator | 30,326,784 | 359,616 | 53,074.162024 | 53,074.103441 | 2,147,483,648 |
| Iterate | 28,606,464 | 415,272 | 14,038.926423 | 14,038.905715 | 2,147,483,648 |
| Merge | 31,109,120 | 403,576 | 63,511.243987 | 6.370250 | 122,880 |
| Fork | 30,781,440 | 364,912 | 220,232.564184 | 3.252084 | 36,864 |
| Source document | 29,868,032 | 383,840 | 85,805.656290 | 0.466709 | 8,192 |
| Ancestor union | 30,392,320 | 359,160 | 48,791.833772 | 1.908292 | 53,248 |
| Ancestor intersection | 30,523,392 | 359,168 | 45,158.263687 | 16.360875 | 204,800 |

The accumulator and iterate results require end-of-input; they do not establish early emission. Timing
is diagnostic, not an isolated throughput comparison. Evidence is
`/tmp/xsl3-p7-climbing-owned-final-linux/artifacts/nightly/report.json` (**complete: true**, zero failures),
`nightly-measured-summary.json` and `run.log` in that task directory. The source hashes match the worktree
in `source-check.json` (empty differences). The managed intermediate fails with the required NativeAOT
message. The cached official SDK image is `70b5de4a1846`, SDK 10.0.400; normal ICU globalization is retained.
This is local arm64 evidence, with no remote linux-x64 CI run implied.

The separate exact **200 MiB / 20 MiB** native push matrix is **complete: true, 27 children, nine failures**.
Every measured workload exceeds the absolute full-lifetime RSS limit. Seven stop before measured input;
identity and source-document consume all 209,715,200 bytes and validate output before failing the budget.
No limit, GC cap, startup accounting or baseline subtraction was changed.

| Workload | Full-lifetime peak RSS, bytes | Measured input bytes | Result |
|---|---:|---:|---|
| Identity | 26,099,712 | 209,715,200 | Budget exceeded |
| Adjacent groups | 22,343,680 | 0 | Budget exceeded before measurement |
| Accumulator | 21,315,584 | 0 | Budget exceeded before measurement |
| Iterate | 22,384,640 | 0 | Budget exceeded before measurement |
| Merge | 21,594,112 | 0 | Budget exceeded before measurement |
| Fork | 21,372,928 | 0 | Budget exceeded before measurement |
| Source document | 26,193,920 | 209,715,200 | Budget exceeded |
| Ancestor union | 21,221,376 | 0 | Budget exceeded before measurement |
| Ancestor intersection | 21,221,376 | 0 | Budget exceeded before measurement |

Raw evidence is `/tmp/xsl3-p7-climbing-owned-final-linux/artifacts/push/report.json` and
`push-measured-summary.json` in that task directory. `push-exit-code.txt` is 1; the enclosing Docker command
also exits 1 for this failed gate after the passing nightly matrix. The exact push limit remains an open
P7 requirement, alongside remaining S1/runtime/error-code paths, streaming throughput/latency measurements,
remote CI verification and the feature claim. All changes remain uncommitted on `feature/p7-streaming-analysis`.

The final ordinary performance gate was run once after all other task-owned CPU jobs ended:
`eng/perf-gate.sh 0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021 3`. It **passes**, with geometric mean
**1.01815370884** over 39 timed rows and maximum allocation ratio **1.04331972653**. The same current
harness and corpus run on both versions. Exactly three base/head JSON pairs are retained in
`/tmp/xsl3-p7-climbing-owned-perf-data/`; stale rounds 4/5 in `artifacts/perf` are excluded. Raw log:
`/tmp/xsl3-p7-climbing-owned-perf.log`. `summary.json` retains every row, ratio and individual round.

| Metric | Base | Head | Head/base | Winner |
|---|---:|---:|---:|---|
| Aggregate timed ratio | 1.000000 | 1.018154 | 1.018154 | No meaningful winner within the gate's 5% band |
| Maximum allocation row, cold bytes | 1,597,240 | 1,666,432 | 1.043320 | No meaningful winner within the gate's 5% band |
| Cold start, ms | 144.685700 | 151.354000 | 1.046088 | No meaningful winner within the gate's 5% band |
| Grouping compile, ms | 0.644500 | 0.684700 | 1.062374 | Base observed median |
| Sorting compile, ms | 0.476000 | 0.630050 | 1.323634 | Base observed median |
| Keys compile, ms | 0.319500 | 0.345200 | 1.080438 | Base observed median |
| Regex 1 KiB, ms | 0.004800 | 0.005500 | 1.145833 | Base observed median |
| Recursion 1 MiB, ms | 1.215300 | 1.279000 | 1.052415 | Base observed median |

Cold start is excluded from the timed geometric mean, while its allocation remains part of the gate.
The band labels describe the gate, not statistical significance. Material individual regressions remain
open for attribution despite the aggregate pass. Their three timing rounds are:

- Grouping compile: base **0.626550/0.644500/0.647550 ms**, head **0.686000/0.668200/0.684700 ms**.
- Sorting compile: base **0.372000/0.476000/0.526300 ms**, head **0.632000/0.571200/0.630050 ms**.
- Keys compile: base **0.300400/0.319500/0.333850 ms**, head **0.345200/0.327700/0.639400 ms**.
- Regex 1 KiB: base **0.004800/0.004700/0.004800 ms**, head **0.005500/0.005400/0.005500 ms**.
- Recursion 1 MiB: base **1.214100/1.247500/1.215300 ms**, head **1.299150/1.268800/1.279000 ms**.

Cold rounds are base **144.685700/140.077000/146.228600 ms**, head **163.245100/150.247100/151.354000 ms**.
This run does not establish fixes for prior cold-start, map or compilation outliers or attribute any
ordinary-path optimization to the ancestor-set change. No extra run was selected to obtain a pass.
Remaining strict/S1/error-code work, exact push memory, streaming benchmarks, throughput/latency and the
feature claim still prevent a Phase 7 completion claim. The goal remains active; no commit or push was made.


### S4 incremental sequence type checks and node return contracts

The complete strict development audit is now **2,661 passed / 50 failed / 1 not run (98.2%)**, with seven
unmatched expected-error codes and no new failure IDs. The repaired cases are `sx-treat-002`, `sx-treat-013`,
`sx-treat-014`, `si-try-002`, `si-try-013` and `si-try-014`. The strict `sx-TreatExpr` and `si-try` sets now
pass **36/36** and **47/47**. The strict fact still exits 1; no accepted strict baseline was added.

`treat as` now transmits each original item while it is current and validates its type before exposure.
Completion checks the minimum cardinality. `instance of` inspects the same stream without collecting it;
a void inspection loop avoids a redundant total-length counter. The checker stores only a count saturated
at two, its match/completion flags, the required type, error policy and a sticky failure. It stores no source
node or user item. Matching uses the existing `ItemTest.Matches` implementation, including subtype and schema
checks, without function conversion or atomization. Fully consumed assertions retain XPDY0050. Inspection
continues to evaluate late producer errors even after a type mismatch.

The required node type refines an opaque `item()*` population for native path construction. Review also
reproduced collection at a streamable function's declared return-conversion boundary: `item()*`, `item()+`,
`node()+` and named-node return types could collect live results before the caller's type check. Expanded,
single-use functions now reuse the incremental checker for node/item return conversions, retaining the
original XTTE0780 error code. Universal star conversions remain identity operations. Ordinary function
calls, recursive boundaries and document-content conversion paths retain their existing lowering. A function
category alone still establishes no order/disjointness proof. Existing proven path properties are preserved.

The initial reduced type tests fail four cases before the change:
`/tmp/xsl3-p7-types-before.log`. Opaque item-star functions then exposed two further failures in
`/tmp/xsl3-p7-types-refinement-before.log`; positive return cardinalities exposed five failures in
`/tmp/xsl3-p7-types-return-cardinality-before.log`. The shared-checker version passes all focused tests in
`/tmp/xsl3-p7-types-verified-focused.log` (**137 net10.0 / 124 net8.0**, including existing function cases).
Tests cover original-node identity, child paths, mixed items, maps, subtype matching, empty and excessive
cardinality, wrong node names, no promotion/atomization, late producer errors, native path refinement, every
return cardinality, original conversion codes, saved assemblies, cancellation, terminal failures and discarded
source-frame collection while the checker remains alive. The new runtime helper signatures are recorded in
`PublicAPI.Unshipped.txt`; no saved-data format change was introduced.

Short consumers retain XPath's permitted lazy-error behavior; full consumption validates the complete type
contract. This follows [XPath 3.1 §2.3.4](https://www.w3.org/TR/xpath-31/#id-errors-and-opt), which permits
stopping when remaining evaluation can only preserve the result or raise an error. No source values are
substituted, promoted, fabricated or silently grounded by the checker.

Four new saved fixtures and all retained NativeAOT correctness probes, including the frozen version-2 fork
assembly, execute with normal globalization and no trim/AOT warnings. They include opaque item/named-node
positive returns, direct transmission, inspection and both XPDY0050/XTTE0780 failures on empty input. Logs
are `/tmp/xsl3-p7-types-native-{generate,publish,run}.log`. Debug/Release units pass
**2,808 net10.0 / 2,734 net8.0** (**50/45 new cases** relative to the preceding ancestor-set increment).
The controller's six Python tests pass.

The process matrix now covers **eleven workloads / twenty-two saved fixtures**, adding `type-assertion`
and `type-inspection`. Their independent oracles use the generated row IDs/count and declared fixture shape.
They use the same public execution paths as other workloads. Both new native streams already consume their
full exact **2,147,483,648-byte** input under the **209,715,200-byte** GC/RSS budget:

| Workload | Peak full-lifetime RSS, bytes | Live managed bytes | Elapsed, ms | First output, ms | Input bytes at first output |
|---|---:|---:|---:|---:|---:|
| Type assertion | 26,427,392 | 357,848 | 10,721.678839 | 0.252417 | 36,864 |
| Type inspection | 26,247,168 | 357,912 | 7,880.751296 | 7,880.716546 | 2,147,483,648 |

Their 10 MiB tree/stream baselines agree with the independent oracles. Both native host-evidence flags are
false and the effective measured cap is exact. Inspection needs the complete input to establish its result;
it does not establish early output. Timings are diagnostic because regression processes were running.
Complete matrix, exact push-budget and final ordinary performance evidence follow below. Remaining runtime,
S1/error-code, memory, benchmark, throughput/latency and feature-claim requirements remain open; the goal
is active and all changes remain uncommitted on `feature/p7-streaming-analysis`.


Final compatibility checks pass: normal W3C **46/46**, configured upstream CI selection **1,654/framework**,
differential **394/framework**, and the Release solution build. Normal/unstreamed/QT3 counts and failure IDs
remain **8,847/21/41**, **2,691/20/1** and **21,884/0/99**, with 34/five/67 unmatched codes respectively.
The full strict audit still has 50 failures and exits 1 as expected. Evidence is
`/tmp/xsl3-p7-types-final-regressions.json` and its named logs, plus `/tmp/xsl3-p7-types-strict-delta.json`.
Production snapshots match the worktree in `/tmp/xsl3-p7-types-final-linux/source-check.json`. No baselines,
thresholds or feature claims were changed. New source and `git diff --check` whitespace checks pass.


The fresh-source local Linux arm64 NativeAOT **2 GiB / 200 MiB** matrix passes **all 33 children**:
eleven 10 MiB tree/stream baseline pairs and eleven exact 2 GiB measured streams. Independent fingerprints
validate in every case. All measured runs have the exact 209,715,200-byte effective cap and both native
host-evidence flags false. Maximum full-lifetime measured RSS is **29.371 MiB**. Uncapped tree baselines
are separate; the merge tree baseline peaks at 560,914,432 bytes.

| Workload | Measured peak RSS, bytes | Live managed bytes | Elapsed, ms | First output, ms | Input bytes at first output |
|---|---:|---:|---:|---:|---:|
| Identity | 29,523,968 | 382,040 | 89,650.823666 | 0.633292 | 8,192 |
| Adjacent groups | 27,279,360 | 360,184 | 15,205.675215 | 8.195375 | 356,352 |
| Accumulator | 30,154,752 | 360,552 | 57,012.398275 | 57,012.347150 | 2,147,483,648 |
| Iterate | 28,508,160 | 416,208 | 14,255.662173 | 14,255.616548 | 2,147,483,648 |
| Merge | 30,797,824 | 404,512 | 61,564.223611 | 6.241375 | 122,880 |
| Fork | 30,470,144 | 365,816 | 241,417.881192 | 5.681375 | 36,864 |
| Source document | 29,585,408 | 384,776 | 84,529.939830 | 0.736875 | 8,192 |
| Ancestor union | 30,318,592 | 360,096 | 46,620.645396 | 2.076125 | 53,248 |
| Ancestor intersection | 30,371,840 | 360,104 | 42,785.273395 | 11.130583 | 204,800 |
| Type assertion | 26,427,392 | 357,848 | 10,721.678839 | 0.252417 | 36,864 |
| Type inspection | 26,247,168 | 357,912 | 7,880.751296 | 7,880.716546 | 2,147,483,648 |

The accumulator, iterate and type-inspection results require end-of-input. Timing is diagnostic because
other regression processes ran concurrently; it is not an isolated throughput comparison. Evidence is
`/tmp/xsl3-p7-types-final-linux/artifacts/nightly/report.json` (**complete: true**, zero failures),
`nightly-measured-summary.json` and `run.log` in that task directory. `source-check.json` has no differences.
Normal ICU globalization and the cached official SDK image `70b5de4a1846` / SDK 10.0.400 are retained.
The managed intermediate is rejected with the required NativeAOT message. No remote linux-x64 CI evidence
is implied by this local arm64 matrix.

The exact **200 MiB / 20 MiB** native push matrix remains red: **complete: true, 33 children, eleven failures**.
Four measured streams consume their full input and validate output before exceeding full-lifetime RSS;
seven exceed the limit during startup/warmup, consuming zero measured input. No limit, effective GC cap,
warmup accounting or baseline subtraction was changed.

| Workload | Full-lifetime peak RSS, bytes | Measured input bytes | Result |
|---|---:|---:|---|
| Identity | 25,899,008 | 209,715,200 | Budget exceeded |
| Adjacent groups | 22,097,920 | 0 | Budget exceeded before measurement |
| Accumulator | 21,217,280 | 0 | Budget exceeded before measurement |
| Iterate | 22,433,792 | 0 | Budget exceeded before measurement |
| Merge | 21,446,656 | 0 | Budget exceeded before measurement |
| Fork | 21,561,344 | 0 | Budget exceeded before measurement |
| Source document | 25,538,560 | 209,715,200 | Budget exceeded |
| Ancestor union | 21,348,352 | 0 | Budget exceeded before measurement |
| Ancestor intersection | 21,348,352 | 0 | Budget exceeded before measurement |
| Type assertion | 25,423,872 | 209,715,200 | Budget exceeded |
| Type inspection | 25,571,328 | 209,715,200 | Budget exceeded |

Evidence is `/tmp/xsl3-p7-types-final-linux/artifacts/push/report.json`, `push-measured-summary.json`
in the task directory and `artifacts/push-exit-code.txt` (1). The enclosing Docker command exits 1 for
this failed gate after the passing nightly matrix. This remains an open P7 requirement; the passing
200 MiB nightly budget does not establish the exact 20 MiB push budget.

The final ordinary performance gate passes after all other task-owned CPU jobs ended. It was run once:
`eng/perf-gate.sh 0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021 3`. The geometric mean over 39 timed rows is
**0.989463046803**, and the maximum allocation ratio is **1.04389572012**. The same current harness/corpus
runs on base and head. Exactly three pairs are retained in `/tmp/xsl3-p7-types-final-perf-data/`; stale
rounds 4/5 in `artifacts/perf` are excluded. Raw log: `/tmp/xsl3-p7-types-final-perf.log`.
`summary.json` retains every row and its individual observations.

| Metric | Base | Head | Head/base | Winner |
|---|---:|---:|---:|---|
| Aggregate timed ratio | 1.000000 | 0.989463 | 0.989463 | No meaningful winner within the gate's 5% band |
| Maximum allocation row, cold bytes | 1,597,240 | 1,667,352 | 1.043896 | No meaningful winner within the gate's 5% band |
| Cold start, ms | 140.522500 | 142.905800 | 1.016960 | No meaningful winner within the gate's 5% band |
| Dispatch compile, ms | 1.628300 | 1.802150 | 1.106768 | Base observed median |
| Regex 1 KiB, ms | 0.004700 | 0.005200 | 1.106383 | Base observed median |
| Identity 1 KiB, ms | 0.002800 | 0.003000 | 1.071429 | Base observed median |
| Dispatch 1 KiB, ms | 0.007300 | 0.007900 | 1.082192 | Base observed median |

Cold start is excluded from the timed geometric mean, while its allocation is included in the gate.
Band labels describe the gate, not statistical significance. Individual regressions remain open for
attribution. The timing rounds are:

- Dispatch compile: base **1.512200/1.629800/1.628300 ms**, head **2.180400/1.613850/1.802150 ms**.
- Regex 1 KiB: base **0.004700/0.004700/0.004700 ms**, head **0.005200/0.005400/0.004700 ms**.
- Identity 1 KiB: base **0.002800/0.002900/0.002800 ms**, head **0.003000/0.002900/0.003000 ms**.
- Dispatch 1 KiB: base **0.007300/0.007700/0.007200 ms**, head **0.007900/0.007200/0.007900 ms**.

Cold rounds are base **140.990900/138.494700/140.522500 ms**, head **141.625900/142.905800/143.407600 ms**.
This run does not attribute any ordinary-path optimization to type checking, establish fixes for prior
sorting/compilation/map/cold outliers or close the streaming throughput/latency exit. No extra run was
selected to obtain a pass. The 50 strict failures, remaining S1/exact-code work, exact 20 MiB push budget,
streaming benchmark set, throughput/latency, remote CI evidence and feature claim still prevent P7 completion.
The full goal remains active; no commit, push, package publication or deployment was made.


### S7 paired process benchmark and exact push RSS investigation

**Implemented, 2026-10-24.** The existing benchmark assembly now exposes a streaming set using the
same generated input, stylesheets, SHA-256 sinks, independent oracles and JSON contract as the memory
probe. Eleven checked-in stylesheets under `bench/corpus/xslt30/micro/streaming` cover the seven
canonical P7 workloads plus ancestor-set and type-check regressions. A unit test prevents fixture/corpus
drift. The benchmark has no test assembly dependency and compiles/executes through the public compiler,
transformer, resolver and secondary-result APIs.

`eng/streaming-benchmark.py` runs fresh paired processes at exact decimal 1 MB, 100 MB and 1 GB, alternating
mode order and retaining every observation. The timed interval includes source reading/parsing,
transformation, serialization and hashing. Compilation, warmup and the independent oracle are outside it.
Both modes receive the same explicit managed heap guard and stable JIT policy. Effective runtime allowances
must agree, exact input/output validation is required, and full-lifetime RSS is retained through `wait4`.
Atomic incomplete checkpoints, crashes, malformed/non-finite reports, timeouts, output disagreement,
heap-cap disagreement and invalid native evidence cannot yield a successful comparison. Failed/OOM tree
runs do not imply a streaming winner.

The 100 MB throughput target requires all seven canonical workloads, at least three valid observations
per mode and a stream/tree median ratio at most 1.0 for every workload. Descriptive 5% Winner labels do not
weaken that target. First-output milliseconds and consumed-input bytes are reported across all sizes;
end-of-input totals are identified explicitly. No unapproved latency threshold is invented. The full matrix
and exact throughput target are configured in a separate nightly job with evidence uploaded on failure.
CI also runs both controller suites. Remote execution is not claimed. The ordinary performance harness now
excludes the separate process host on both sides via `EnableStreamingProcessBenchmarks=false`, so
commits predating the reader API build the same current ordinary micro harness. Normal builds include
the process host. Failed builds retain their diagnostics in the gate log. The ordinary in-memory
micro suite's lifecycle is unchanged.

See [benchmark method and commands](../streaming-benchmark.md). The exact 20 MiB push investigation is
recorded separately in [RSS evidence](../streaming-rss-investigation-2026-10-24.md). GC conservation/region/
foreground settings did not reduce the unchanged native consumer below 20 MiB. Disabling AOT data
hydration worsened RSS to 27,037,696 bytes; a consumer rooting only the identity stylesheet pair still used
23,842,816 bytes. No diagnostic setting, split consumer, lower heap limit, disabled globalization or
baseline-subtracted accounting was adopted. The exact push gate remains red.


The isolated managed matrix is **complete: true, 126 trials**, on .NET 10.0.11 / macOS 27.0.1 / Arm64.
All **63 streamed trials** and **48 tree trials** consume exact input and validate output. Fifteen tree
trials fail with `System.OutOfMemoryException`: merge at 100 MB, and adjacent grouping, accumulators,
merge and fork at 1 GB, three trials each. Every valid child reports the same 4,294,967,296-byte effective
managed allowance. The report retains every failure; incomplete pairs have no ratio or winner.

At 100 MB the valid stream/tree median ratios are identity **2.001237**, adjacent grouping **0.641410**,
accumulators **0.677534**, iterate **0.826322**, fork **3.349502** and source-document **2.115013**.
Merge has no valid tree comparison. The exact throughput target is therefore incomplete, and three
of the valid comparisons already exceed 1.0. Incremental first-output input positions stay at 8,192 bytes
for identity/source-document, 356,352 for adjacent grouping, 122,880 for merge and 36,864 for fork across
all three measured sizes. End-of-input totals are not early-emission results. No latency exit is claimed.

[Full comparison and first-output tables](../streaming-benchmark-2026-10-24.md) retain the measurements and
limits. Raw frozen artifacts, all observations, source/fixture audits and logs are under
`/tmp/xsl3-p7-streaming-process-benchmark/`. At matrix completion, the source audit has no differences across its 400 files;
controller, benchmark and engine hashes match. Shared fixture hashes are retained separately and occur
in the frozen portable PDB. The additional audit was captured after launch. The original controller's
exit status was not captured; the completed report contains fifteen failed children. No successful matrix
or throughput gate is inferred from its terminal state.

Release solution build passes with zero warnings/errors. Debug and Release units pass **2,809 net10.0 /
2,735 net8.0**; the increase is the corpus-drift fact. Both Python controller suites pass **13 tests**,
including effective-heap disagreement, malformed/non-finite evidence, crashes, timeouts, missing coverage,
exact target semantics and native evidence. The .NET 8 public-API benchmark smoke passes **22/22 children**
across all eleven workloads. Verification is serial after benchmark completion. Logs are
`/tmp/xsl3-p7-process-benchmark-{solution,units-release,units-debug,python,net8-smoke}.log` and the net8
report directory at `/tmp/xsl3-p7-process-benchmark-net8-smoke/`.

No production compiler/runtime source was changed in this increment. The last full strict audit remains
2,661/50/1 with seven unmatched expected codes; it was not rerun for these harness-only changes. Native
saved-assembly correctness and the 33-child nightly memory matrix remain the preceding verified evidence,
not new executions. The 20 MiB push gate, remaining S1/runtime/error-code work, throughput/latency,
remote CI and feature claim remain open. The full goal remains active.


The first ordinary gate attempt stopped before timing because the base predates the reader-transformer
API. An isolated archive reproduces two CS1503 errors with the process host enabled and builds cleanly
with it disabled. The finalized `EnableStreamingProcessBenchmarks` property defaults to true; the gate
sets it false on both sides and now prints build diagnostics on failure. Both default framework builds
and fresh **22-child** smoke matrices pass. All **409** default benchmark method IL bodies are identical
to the frozen measured build; the engine/controller hashes still match. The post-compatibility audit records
the two changed benchmark files (Program.cs and its project) among the 400 audited files, with no
production differences. This distinguishes the measured artifact from the
later build-compatibility configuration, without rerunning identical default executable methods.


The final ordinary performance gate **passes** (exit 0), after all task-owned benchmark/verification CPU
jobs ended. The finalized harness runs exactly three base/head pairs against
`0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021`, with the process host excluded on both sides.
The 39-row timed geometric mean is **1.036264177419**, and maximum allocation ratio is
**1.043895720117** (cold allocation: 1,597,240 to 1,667,352 bytes).
The earlier attempt failed at build time and produced no timing observations; the complete fixed gate was
run once. The temporary base worktree was removed by its trap. Raw log and captured exit status are
`/tmp/xsl3-p7-process-benchmark-perf.log` and `-perf-exit.txt`. Exactly three pairs are copied to
`/tmp/xsl3-p7-process-benchmark-perf-data/`, with every row and observation in `summary.json`.
Stale rounds 4/5 in the shared artifacts directory are excluded.

| Ordinary metric | Base ms | Head ms | Head/base | Winner |
|---|---:|---:|---:|---|
| grouping cold | 148.623800 | 145.522900 | 0.979136 | No meaningful winner within 5% band |
| grouping compile | 0.607350 | 0.649600 | 1.069565 | Base observed median |
| sorting compile | 0.512700 | 0.597000 | 1.164424 | Base observed median |
| keys compile | 0.305500 | 0.482600 | 1.579705 | Base observed median |
| join compile | 0.093000 | 0.101800 | 1.094624 | Base observed median |
| regex compile | 0.081600 | 0.086500 | 1.060049 | Base observed median |
| dispatch compile | 1.617500 | 1.701750 | 1.052087 | Base observed median |
| regex 1kb | 0.004700 | 0.005500 | 1.170213 | Base observed median |
| sorting 1mb | 2.108350 | 2.215100 | 1.050632 | Base observed median |
| maps 1mb | 4.155250 | 4.622450 | 1.112436 | Base observed median |

Cold time is reported but excluded from the timed geometric mean; its allocation is included in the
gate. The 5% labels are descriptive, not statistical significance. Individual regressions remain open for
attribution, including the large key-compilation ratio. They are not declared fixed or removed because
of the aggregate pass. The exact observations are:

- grouping cold: base **131.799400/155.052000/148.623800 ms**, head **187.420700/141.009200/145.522900 ms**.
- grouping compile: base **0.607350/0.599500/0.630300 ms**, head **0.672900/0.649600/0.631400 ms**.
- sorting compile: base **0.452200/0.535450/0.512700 ms**, head **0.597000/0.478700/0.610850 ms**.
- keys compile: base **0.302100/0.433900/0.305500 ms**, head **0.554700/0.296900/0.482600 ms**.
- join compile: base **0.087800/0.093000/0.094000 ms**, head **0.106400/0.091100/0.101800 ms**.
- regex compile: base **0.081600/0.082100/0.081500 ms**, head **0.088900/0.083100/0.086500 ms**.
- dispatch compile: base **1.734250/1.556500/1.617500 ms**, head **1.779200/1.648850/1.701750 ms**.
- regex 1kb: base **0.004800/0.004700/0.004700 ms**, head **0.005500/0.005500/0.005500 ms**.
- sorting 1mb: base **2.222900/2.066950/2.108350 ms**, head **2.267400/2.131600/2.215100 ms**.
- maps 1mb: base **4.569450/4.092500/4.155250 ms**, head **4.374550/4.622450/5.004500 ms**.

This ordinary-suite pass does not establish a fix for preceding outliers, attribute an optimization
of ordinary paths to the benchmark tooling, or satisfy streaming throughput/latency and memory exits.
The full Phase 7 goal remains active. No commit, push, publication or deployment was performed.


### S4 streamed node-only content and frame-owned namespace scopes

**Implemented, 2026-10-24.** Proven node-only `xsl:copy-of` selections now retain a broad node-only QIL
result after the existing copy/validation helpers, without retaining the input's possibly changed schema
annotation. Previously the helpers' arbitrary-item return type sent the containing constructor through
`X31ContentItems`, buffering the complete content. A new 10,000-child row regression consumes all
180,031 bytes before first output in all four option variants before the fix; afterward it emits before
half the input is read while preserving complete output. Copied attributes remain parentless and distinct.
Atomic/mixed selections keep their existing conversion path. No copy, validation or accumulator helper
is bypassed, and the saved format/public API is unchanged. Newly compiled saved stylesheets receive the
QIL improvement; older saved assemblies retain compatible prior code.

Resolved namespace arrays now belong to source frames and reuse immutable ancestor scopes where no local
declaration changes them. Owner numbers, shadowing and lookup order are preserved; lookup is iterative and
publication uses compare/exchange. Fork views create fresh local frames and atomized declarations, without
copying the resolved cache from source frames. No discarded-frame history is added. The new warmed
namespace-axis test fails at 3,520,000 bytes for 10,000 enumerations before the fix and passes a less-than-
4,096-byte tolerance afterward, for all three scope modes. Local ownership/rebinding, readable old headers,
expired-frame collection and fork name-table checks pass.

Debug/Release units pass **2,820 net10.0 / 2,746 net8.0**, with eleven new cases. The .NET 10-only W3C
harness passes **46/46**; normal, unstreamed and strict failure IDs remain unchanged. Strict is still
**2,661/50/1** with seven unmatched expected codes. QT3 remains 21,884/0/99. Upstream and differential
checks pass **1,654** and **394 per framework**; both controller suites pass **13 tests**. The solution
build has zero errors and thirty existing warnings in unchanged upstream/API tests, with none in the
changed production files or finalized new tests.

A fresh exact schema comparison uses the same current harness and the frozen pre-change engine: ordinary
schema-aware is **843/1/2** on both, and full strict schema-aware is **835/9/2** on both, with identical
failure IDs. Older stored reports (842/2/2 ordinary, twelve strict subset cases) are insufficient for change
attribution. The nine full strict schema-aware failures remain open; none is hidden by the main streaming
profile. No baseline or feature claim changed.

A fresh genuine NativeAOT consumer passes four newly saved copy-content emission cases and all retained
correctness probes, including the immutable version-2 fork assembly. Its final publish has no trim/AOT
warnings. Initial temporary-project source globbing and missing private metadata roots in a type array
caused harness failures; logs are retained. The corrected project excludes the source subtree and uses
explicit annotated stylesheet type roots. No reflection/globalization feature was disabled or warning
suppressed. Managed compiler/transformer and public legacy saved-load/transform paths are exercised.

The frozen current Linux arm64 source passes the complete **33-child 2 GiB/200 MiB** matrix, maximum
measured full-lifetime RSS **30,945,280 bytes**; all eleven measured streams validate exact input and output.
The complete **33-child 200 MiB/20 MiB** push matrix still has **eleven failures** (four full-input runs,
seven startup/warmup failures). Both native-evidence flags are false and normal ICU is retained. The source
snapshot check has no differences. Native timings are diagnostic because functional regressions ran in
parallel; no isolated-throughput inference is made from them. Docker/controller exit 1 records the failed
push gate after the passing nightly matrix. This is local arm64 evidence, not remote linux-x64 CI.

See [full method, regression logs, native probes and memory tables](../streaming-copy-content-2026-10-24.md).
The two isolated targeted benchmark pilots pass 36 children each; combined fixes improve observed 100 MB
identity/source-document medians to 3,028.680416/3,158.651375 ms, still slower than their tree comparisons.
Fork remains unresolved. Targeted coverage cannot satisfy the canonical throughput exit. All original
S1/runtime/error-code, exact push memory, throughput/latency, remote CI and feature-claim work remains
required. The full goal stays active.


The final isolated process matrix completes all **126** planned children: **63** validated streams,
**48** validated tree trials and **15** failures. Captured controller exit is **1**; the exact 100 MB
target remains **incomplete coverage**. Raw evidence is `/tmp/xsl3-p7-copy-content-process-benchmark/`;
all 409 source/fixture/config hashes match. The same runtime/artifact, equal 4 GiB guard and three alternating
pairs are used per workload/size. No competing task-owned build/test/benchmark job ran. See the
[complete current/previous and first-output tables](../streaming-copy-content-2026-10-24.md#final-full-paired-process-matrix).

The final ordinary gate exits **0** after all other task-owned CPU jobs are terminal, run once with exactly
three pairs against `0f2ddcb`: geometric mean **1.009512334383**, maximum allocation ratio
**1.043895720117**. Every row/round and individual outlier is retained in
`/tmp/xsl3-p7-copy-content-final-perf-data/summary.json` and the linked report. Cold timing is reported but
excluded from the time mean, while its allocation is included. Stale extra artifact rounds are excluded.
Individual regressions remain attribution work; no prior outlier is declared fixed by this aggregate.
The 50 main strict failures, nine full strict schema-aware failures, remaining S1/error-code/runtime work,
20 MiB push, streaming throughput/latency, remote CI and feature claim remain open. The full goal remains
active. No commit/push/publication/deployment was performed.


### S4 incremental selected-copy cardinality and original-focus result inference

**Implemented, 2026-10-24.** Streamed `xsl:copy select` now compiles its original selection through the
existing incremental optional-item checker, retaining XTTE3180, instead of the loader's count()/let
rewrite that consumed selected descendants before the copy body. Ordinary lowering is preserved.
Template result inference also uses the original selection: the synthetic error() branch is not a
returned value and must not widen a proven node-only template to arbitrary items. Genuine atomic/mixed
selections keep their item result type. No helper/format/public API changes are introduced.

The corrected before-fix regression has five failures and thirteen passes. Twenty selected-copy cases
now cover validation, named types, grounded singleton/nested focus, empty and atomic selection,
multiple node/atomic cardinality, XTTE3180 rollback and borrowed-reader ownership. Two additional process
cases check the selected-copy oracle and emission before half the generated input is read. The early-output
case first failed after cardinality repair alone, then passed after result inference was corrected.
The invalid initial last() fixture was corrected to explicit grounding before failure attribution.

Full Debug/Release units pass **2,842 net10.0 / 2,768 net8.0**. The schema-aware copy set is **52/52**.
Main strict improves **2,661/50/1 to 2,664/47/1**, repairing si-copy-028/-047/-049 with no new failures.
Full strict schema-aware improves **835/9/2 to 842/2/2**, repairing si-copy-121/-122/-123/-124/-125/-127/-129,
also with no new failures. Ordinary schema-aware stays 843/1/2. Main strict code mismatches stay seven;
schema strict mismatches reduce to six. No accepted strict baseline or streaming-support claim changes.

The repository-owned process probe has twelve workloads and twenty-four generated saved entry points;
`copy-select` shares the identity oracle and generated input, without adding runtime data/fallbacks.
The new checked-in corpus file comes from the shared definitions. A fresh rooted NativeAOT consumer
passes selected-copy focus/empty/atomic/multiple rollback cases and all retained correctness probes,
with no trim/AOT publish warnings. Schema-aware saved assemblies remain explicitly unsupported, and
the version-2 fork fixture is retained without regeneration.

See [selected-copy implementation and evidence](../streaming-copy-select-2026-10-24.md). Exact memory
and isolated benchmark validation is retained separately; these repaired cases do not close remaining
S1/runtime/error-code, push RSS, throughput/latency, remote CI or feature-claim requirements. The full goal
remains active.

The fresh local Linux arm64 NativeAOT matrix completes **36 children with zero failures** at exact
2 GiB input / 200 MiB absolute RSS. Selected-copy peaks at **29,503,488 bytes**, validates its independent
identity hash and emits after 8,192 input bytes. Normal ICU and both native-evidence checks are retained.
The separate exact 200 MiB-input / 20 MiB RSS push matrix completes all 36 children with **twelve measured
failures**. Selected-copy consumes and validates its full input but peaks at **25,395,200 bytes**. Neither
startup/warmup nor a baseline is subtracted. Native timing is diagnostic because functional checks ran
concurrently. All 527 frozen source hashes still match; this is local arm64 evidence, not remote linux-x64
CI. The version-2 saved-fork fixture hash is unchanged.

The final isolated process matrix retains all **144 trials**: **72 valid streams**, **57 valid tree trials** and **15 tree OOM failures**. Controller exit is **1**, with the exact 100 MB target still incomplete. Selected-copy first-output medians are **0.239708/0.239292/0.233083 ms** at 1 MB/100 MB/1 GB, each after 8,192 input bytes; its 100 MB stream/tree median ratio is **1.140905**. Three canonical 100 MB comparisons remain slower and merge lacks a valid tree comparison. No ratio or winner is assigned to failed comparisons. All 422 frozen source hashes and artifact/controller/engine hashes match. See [all current/previous medians and emission observations](../streaming-copy-select-2026-10-24.md#final-full-paired-process-matrix). This is not a full throughput or latency pass.

The final ordinary gate exits **0** after all other task-owned CPU jobs finish, over exactly three pairs
against `0f2ddcb`: geometric mean **1.027029251298**, maximum allocation ratio **1.043895720117**.
Keys compilation (**1.766204**), regex 1 KiB (**1.170213**), sorting compilation (**1.093939**), join
compilation (**1.089056**) and dispatch compilation (**1.065657**) remain individual observed regressions
requiring attribution. All row/round/allocation evidence is retained in the linked selected-copy report;
no aggregate pass establishes a fix for earlier outliers. The gate ran once and removed its temporary
base checkout. All main/schema strict failures, error-code/S1 requirements, exact push memory, throughput/
latency, remote CI and feature-claim work remain open. The full goal remains active. No commit/push was
performed.


### S4 forward conditional sequences and complex content

**First increment implemented, 2026-10-24.** Streamed `xsl:on-empty` / `xsl:on-non-empty` now consume an ordinary
population and deferred bodies through resumable iterators, retaining conditional order, lexical values
and item types. `xsl:where-populated` filters each item while readable. Ordinary execution also preserves
vacuous items when no `xsl:on-empty` replaces them. Initial regressions reproduce eleven failures / three
passes per framework. No strict baseline is accepted.

Complex-content conversion now expands streamed document children and array members incrementally,
preserving atomic spacing and XTDE0450. Root compilation precedes dispatcher finalization. The first full
strict audit exposed three annotation-scope leaks and one eagerly captured global affecting fork order;
corrected pull cursors isolate annotation context and population adapters preserve lazy global evaluation.
Six context regressions cover those issues, including completion/fault restoration. The existing
accumulator context handling shares the stack-copy helper, and `xsl:try` retains global error boundaries.

Forty-four net10 / forty net8 new cases cover conditional/complex content, lifecycle, saved assemblies
and process emission. Full Debug/Release units pass **2,886 / 2,808**; the .NET 10 W3C harness passes
**46 facts**. Main strict improves **2,664/47/1 to 2,668/43/1**, repairing si-on-empty-022/-024 and
si-on-non-empty-022/-024 with no new failure IDs. Seven wrong expected codes remain. Ordinary XSLT,
unstreamed development and QT3 counts are unchanged. A corrected fresh saved NativeAOT consumer passes
seven new stylesheets plus every retained correctness/compatibility probe, with no trim/AOT warnings.

The probe adds a thirteenth workload and twenty-six saved entry points. The existing twelve corpus files
are unchanged. Its oracle uses generated records and explicit conditional header/footer output; fixtures
never become runtime data. An arbitrary undecided vacuous prefix and explicit grounded values can grow;
the selected-row process fixture does not prove every constant-memory requirement. See
[implementation, failures, context corrections and validation](../streaming-conditional-content-2026-10-24.md).
The exact native memory and isolated performance gates are recorded separately. The full goal remains
active, including remaining S1/runtime/error-code, exact push RSS, throughput/latency, remote CI and claim
requirements. No commit/push is performed.

At this checkpoint the strict report still failed `si-on-empty-012` and `si-where-populated/si-coco-012`,
both involving temporary document construction from a streamed selection. The next increment below
repairs these cases. The
successful selected-row workload does not prove every conditional/document-construction path or bound
an arbitrary vacuous prefix.

The corrected-source local Linux arm64 NativeAOT nightly matrix passes **39 children, zero failures**
at exact 2 GiB input / 200 MiB absolute RSS. Conditional-content validates all 2,147,483,648 input bytes,
emits after 8,192 bytes and peaks at **29,777,920 bytes** full-lifetime RSS, with **377,968 bytes** live
managed memory. Effective allowance is 209,715,200 bytes; both native-evidence flags are false. Normal ICU
is retained. These timings are diagnostic because functional jobs overlapped part of the matrix.

The corrected exact 200 MiB-input / 20 MiB push matrix is complete at **39 children / thirteen measured
failures**. Conditional-content consumes and validates its full input but peaks at **26,013,696 bytes**.
All 545 frozen Linux source hashes match; the corrected macOS consumer's 395 source hashes also match.
The version-2 saved-fork fixture is preserved. Neither startup/warmup nor a baseline is subtracted.

The final isolated process matrix is complete at **162 trials**: **81 validated streams**, **63 valid tree
trials** and **18 tree OOM failures**. Controller exit is **1**; the exact 100 MB target is incomplete.
Conditional-content medians are **22.829916/2,189.526208/21,985.462000 ms** at 1/100/1,000 MB; its 100 MB
stream/tree ratio is **1.108109**, and no valid 1 GB tree comparison exists. Its first-output medians are
**0.186167/0.185500/0.237125 ms**, all after 8,192 input bytes. These are observations, not a latency exit.
All 425 frozen source/fixture/controller hashes and the runtime artifacts match. See
[all current/previous comparisons and first output](../streaming-conditional-content-2026-10-24.md#final-full-paired-process-matrix).
The final ordinary gate exits **0** over exactly three pairs against `0f2ddcb`: timing geometric mean
**0.990893315951**, maximum allocation ratio **1.044596929704**. Regex 1 KiB remains an individual
observed regression (**1.127660**); every row/round and allocation is retained in the linked evidence.
The gate ran once, after the complete streaming matrix and all task-owned functional/native CPU jobs
finished. Cold time is reported but excluded from the timed mean; its allocation is included. No aggregate
pass establishes fixes for earlier outliers. The temporary base checkout is removed. The full goal remains
active, including 43 main strict / two schema strict failures, S1/error-code/runtime gaps, exact push RSS,
throughput/latency, remote CI and the feature claim. No commit/push was performed.

### S4 node-valued predicate analysis and prepared compiled pull calls

**Increment implemented; full P7 remains active.** Node-valued forward predicates now use the existing
node-existence operation instead of requesting an artificial numeric position. This preserves a single
descendant scan for `//row[@keep]`. Explicit positions, numeric/uncertain predicates and ordinary
compilation retain their rules. Eight temporary-document cases reproduced failures before the repair;
main strict improves **2,668/43/1 to 2,670/41/1**, fixing si-on-empty-012 and si-coco-012 with no new
failure IDs. Seven expected-code mismatches remain.

A compiled pull producer now prepares its converted opaque-state argument once and shares ordinary
compiled-dispatch selection. Native callbacks keep fresh mutable arguments. Completion/failure release
the cached call. Across 10,000 calls of the same identity body, ordinary calls allocate **2,560,000 bytes**
and resumptions **880,000 bytes** on each framework; generated result containers still allocate.

Twenty-seven net10 / twenty-four net8 new cases cover predicate existence, explicit positions, saved
assemblies, reusable temporary documents, ownership, early row emission and call lifecycle/conversion.
Full Debug/Release suites pass **2,913 / 2,832**. A fresh macOS NativeAOT consumer passes five new saved
predicate/document stylesheets and every retained compatibility probe without trim/AOT warnings.
Exact native memory and isolated process/performance results are recorded in
[node-predicate evidence](../streaming-node-predicates-2026-10-24.md). The preceding full three-size
matrix remains evidence for its own source; this increment's paired matrix measures 100 MB. Main/schema
strict failures, S1/error-code/runtime work, exact push RSS, throughput/latency, remote CI and the feature
claim remain required. No commit/push is performed.

The current-source Linux arm64 NativeAOT nightly matrix completes **39 children with zero failures**
at exact 2 GiB input / 200 MiB full-lifetime RSS. The exact 200 MiB / 20 MiB push matrix completes
**39 children with thirteen measured RSS failures**. Normal ICU and the original budgets are retained.
The isolated current-source 100 MB matrix completes **54 trials**: **27 valid streams**, **24 valid tree
trials** and **three tree merge OOM failures**, under equal 4 GiB managed limits. The canonical throughput
target remains incomplete. Conditional-content's stream/tree ratio is **1.047953**, within the controller's
5% no-meaningful-winner band; identity/source-document/fork remain slower and merge has no valid comparison.
All 395 native-core, 545 Linux-probe and 425 process source/fixture/controller hashes match, as do runtime
artifacts and the immutable version-2 fixture. See the linked evidence for every measured memory row,
throughput comparison and first-output result.

The final ordinary gate exits **0** over exactly three pairs against `0f2ddcb`: timing geometric mean
**0.982051247778**, maximum allocation ratio **1.044672059302**. Dispatch compilation remains an individual
observed regression (**1.098697598698**), with all rounds retained for attribution. Older individual
outliers are not declared fixed by absence here. The gate ran once after all task-owned native/functional
CPU jobs and the 54-trial matrix terminated; its base checkout is removed. Full P7 remains active,
including 41 main strict / two schema strict failures and the other previously listed requirements.

### S4 declaration-scoped atomic template arguments and debug focus

**Increment implemented; full P7 remains active.** Named-template headers now resolve parameter types
in each declaration's namespace scope. An atomic target lets its explicit non-tunnel caller atomize
source nodes while readable; the callee retains canonical conversion and cardinality errors. Explicit
node types on `xsl:with-param` use the existing incremental matcher before that atomic adaptation.
A declaration cache also retains deferred type errors in forward-compatible code, preventing an invalid
type from being resolved again against an outer namespace binding.

Streamed debug metadata no longer forces an eager context-size count. Position-only focus demand is
kept separate from `last()`. The node-change hook is guarded for non-node XDM context items, and the
runtime dispatcher keeps its iterator/list-returning ABI even during debug construction analysis.
The previous engine reproduces both debug IL/delegate failures. No public helper or saved-data format
is added. Merge-source attribute rejection and streamable merge-action XPDY0002 behavior match the
Recommendation and remain; five suite/specification conflicts stay in the raw reports.

Forty-three net10 / thirty-six net8 new cases include typed arguments, explicit argument checks,
namespace/error scope, both APIs, actual debug compilation, saved assemblies and process coverage.
Full Debug/Release units pass **2,956 / 2,868**. Main strict improves **2,670/41/1 to 2,671/40/1**, fixing
si-call-template-002 with no new failure IDs and seven expected-code mismatches. Other full conformance
profiles retain their previous counts. The .NET 10 W3C harness, upstream/differential and thirteen
controller tests pass. A fresh macOS NativeAOT consumer passes six new normal/debug/error stylesheets
and every retained compatibility probe with no trim/AOT warnings.

The shared probe adds a fourteenth workload and twenty-eight saved entry points; all thirteen prior
corpus files are unchanged. Its final-source exact Linux NativeAOT nightly matrix passes **42 children**
at 2 GiB / 200 MiB; the complete exact 200 MiB / 20 MiB push matrix retains **fourteen RSS failures**.
Earlier checkpoints are preserved separately; the final source includes the deferred-error repair.
See [implementation, reproduced failures and final evidence](../streaming-template-parameters-2026-10-24.md).

The recorded `price + (1,2)[last()]` diagnostic was subsequently isolated to the arithmetic operand's
atomization boundary, rather than the conservative template-focus policy. The
[arithmetic and XPath focus increment](../streaming-arithmetic-focus-2026-10-24.md) repairs that boundary
and separate local-focus defects. Other S1/runtime/error-code, push RSS, throughput/latency, remote CI,
schema-aware saved-assembly and feature-claim requirements remain.
No commit or push is performed.

The final-source isolated 100 MB matrix completes **60 trials**: **30 valid streams**, **27 valid trees**
and **three tree merge OOM failures** under equal 4 GiB heaps. The canonical throughput target remains
incomplete. Template-parameter medians are **1,960.900209 ms tree / 1,761.378541 ms stream** (ratio
**0.898250**, observed stream median winner); this is a current-snapshot comparison, not causal proof
against an earlier version. The full three-size matrix remains evidence for its earlier frozen source.
All 426 process source/fixture/controller hashes match, as do assembly/engine/controller hashes.
The final ordinary gate exits **0** over exactly three pairs against `0f2ddcb`: geometric mean
**0.998328693944**, maximum allocation ratio **1.045944253838**. Sorting compilation (**1.063126**) and
1 KiB regex (**1.191489**) remain observed individual regressions requiring attribution. Cold grouping
time (**1.058318**) is reported separately and excluded from the timed mean; cold allocation is included.
Every row/round/allocation is retained in the linked report. Earlier outliers are not declared fixed by
absence here. The temporary base checkout is removed. Full P7 remains active.

### S1/S4 scoped numeric focus and forward arithmetic

**Increment implemented; full P7 remains active.** Arithmetic absorbs selected nodes while live,
including unary and either-side binary operands. Compatibility mode converts only the first item,
preserves canonical atomization errors, and retains NaN for empty input without an unnecessary cursor
window. Shared XPath numeric-focus analysis respects local bindings and visits map/array/lookup and
function-reference operands. It is used by context-size binding, S1 scanning-predicate eligibility and
the corresponding forward header predicate check. Filtered children after descendant selections can
use the existing header scan; direct descendant selections retain their absorption-aware iterator.

Numeric named references capture their real template focus; `function-lookup#2` references retain
captured numeric function values. Absent-focus reference creation remains legal, with XPDY0002 on
calling the numeric function. The conservative template-focus policy is retained: an isolated
comparison disproved the earlier attribution of the scoped arithmetic failure to that policy.

Fifty-five net10 / fifty-three net8 new regression cases bring complete Debug/Release unit results to
**3,011 / 2,921**. Eight full conformance profiles have no new failure IDs. Main strict remains
**2,671/40/1**, with seven unmatched expected codes; posture/sweep remains **790/129/0**. The solution,
46 W3C harness facts, 1,654 upstream cases per framework, 394 differential cases per framework and
thirteen controller tests pass. Twelve new normal/debug saved NativeAOT fixtures and all retained
compatibility probes pass without trim/AOT warnings; all 396 current/frozen core hashes match.

The exact corrected native matrix passes all **42 nightly children** at 2 GiB / 200 MiB RSS; the
complete 200 MiB / 20 MiB push matrix retains **fourteen RSS failures**. All 547 current/frozen
source/controller/fixture hashes match. The one ordinary three-pair gate exits **0** at geometric
mean **0.994790425361** and maximum allocation ratio **1.046024392076**. The 1 KiB regex row remains
an observed regression (**1.170212765957**); earlier outliers remain open. Full rows/rounds and native
evidence are in [arithmetic/focus evidence](../streaming-arithmetic-focus-2026-10-24.md). Earlier
checkpoints are retained separately, including the compatibility and Debug empty-window failures.
No current throughput claim is inferred from the preceding engine's process measurements. Main
strict/S1/error-code, exact push RSS, throughput/latency, remote CI, schema-aware saved-assembly and
feature-claim requirements remain. No commit or push is performed.

### S4 generic absorbing-function parameters and debug output workers

Absorbing functions with `item()*` or omitted first-parameter types use incremental argument frames,
including mixed XDM items. Grounded arguments/results, forwarded fork origins and singleton child-order
proofs survive the call boundary. Inlining retains pull-call semantics, and generic node paths check
one yielded item at a time. The existing node-returning runtime helper and saved-data version remain.

Debug end-tag markers follow the fork outside its prongs. Fork and secondary-output closures retain
their iterator-return ABI so debug writer conversion cannot capture and discard worker output. Early
secondary-output tests cover direct and forked calls. Full Debug/Release units pass **3,040 / 2,948**;
ten new normal/debug saved NativeAOT fixtures and retained compatibility probes pass without trim/AOT
warnings. All 396 current/frozen core/configuration hashes match. Eight full profiles have no new
failure IDs; main strict improves to **2,672/39/1**, fixing `su-absorbing-301`, with seven unmatched
expected codes retained. See
[item-parameter implementation, evidence and remaining scope](../streaming-item-parameters-2026-10-24.md).
The full Phase 7 goal remains active.

The item-parameter final source completes both fifteen-workload native matrices: **45 nightly children
pass** at exact 2 GiB input / 200 MiB RSS; the complete **45-child** exact 200 MiB / 20 MiB push matrix
retains **fifteen RSS failures**. All 548 current/frozen source/controller/fixture hashes match.
Normal ICU and full-lifetime counters remain. See [native evidence](../streaming-item-parameters-2026-10-24.md#final-source-exact-native-memory-matrices).

The item-parameter ordinary three-pair gate exits 0 at timing geometric mean **0.994644134262**
and maximum allocation ratio **1.046490195587**. Individual timing outliers
remain recorded in [the evidence](../streaming-item-parameters-2026-10-24.md#ordinary-performance-gate);
this does not close the remaining streaming throughput/latency or exact push RSS requirements.

### S4 overlapping descendant values

Unfiltered value consumers preserve nested descendant matches in disk-backed header/character ranges.
Open matching nodes alone remain on the capture stack; the document reuses one spare store across
sequential windows. Canonical atomization retains final schema/list/union/nil/QName information. AVTs,
simple content, sums and string conversions use the same eligibility proof and additive runtime helper.
Unix spill files are owner-private and delete-on-close; 64-bit generations expire old ranges.

Full Debug/Release units pass **3,060 / 2,966**. Eight strict failures are fixed, improving main strict
to **2,680/31/1**, with seven unmatched expected codes and no new failure IDs in eight full profiles.
Four new normal/debug saved NativeAOT styles and retained probes pass without trim/AOT warnings; all
400 current/frozen core/configuration hashes match. The shared probe now has sixteen workloads and
thirty-two saved entry points; all fifteen preceding corpus stylesheets are unchanged. See
[value-replay evidence and limits](../streaming-value-replay-2026-10-25.md). Scalar/string result size,
deferred predicates, other consumers and the remaining full P7 requirements remain open.

The value-replay source completes both sixteen-workload native matrices: **48 nightly children pass**
at exact 2 GiB input / 200 MiB RSS; the complete **48-child** exact 200 MiB / 20 MiB push matrix
retains **sixteen RSS failures**. All 553 current/frozen source/controller/fixture hashes match.
Normal ICU and full-lifetime counters remain. See [native evidence](../streaming-value-replay-2026-10-25.md#final-source-exact-native-memory-matrices).

The corrected value-replay ordinary gate exits 0 after one isolated three-pair run: timing geometric
mean **1.029701487659**, maximum allocation ratio **1.046810748541**.
Individual outliers and raw rounds remain in [the evidence](../streaming-value-replay-2026-10-25.md#ordinary-performance-gate).
The following process matrix was intentionally stopped after 48 completed trials to fix a verified
quadratic tree intersection. Its partial results remain separate. See the
[node-set/mode follow-up](../streaming-set-merge-mode-2026-10-25.md) for the replacement comparison.
Full P7 remains active.

### S1 audit and mode/node-set follow-up

The node-set/mode follow-up replaces the grounded intersection/difference nested scan with a linear
merge after ordering, and fixes per-attribute mode precedence and effective typed-mode inheritance.
Main strict improves to **2,681/30/1**, with seven unmatched expected codes. Full Debug/Release units
pass **3,087/2,993**; retained saved NativeAOT probes pass. An independent restricted final-Recommendation
check verifies all **2,723 S1 discrepancies** in the prior exhaustive inventory. The raw S1 profile
remains **790/129/0**, including eight empty catalogue inputs. See
[the audit and validation](../streaming-set-merge-mode-2026-10-25.md). The full P7 goal and its remaining
memory, throughput/latency, execution and feature-claim requirements stay active.


The node-set/mode ordinary three-pair gate exits **0** at timing geometric mean **0.990166466732**
and maximum allocation ratio **1.046810748541**. Its complete **96-trial** 100 MB matrix has 48 valid
streams, 45 valid trees and three tree-merge OOMs under equal 4 GiB heap guards. The repaired tree
intersection completes all rounds at median **1,809.021667 ms**. Identity, fork and source-document
streaming remain slower; merge has no valid tree comparison, and overlap-values remains substantially
slower. See [all ratios and limits](../streaming-set-merge-mode-2026-10-25.md). Exact push RSS and full
throughput/latency requirements remain open.


### Bounded value cache and materialized merge output

The bounded-value/merge follow-up removes retained action results from newly compiled tree merges
while preserving group size/focus and old saved entry points. The fresh **96-trial** 100 MB matrix has
**zero failures**, including all tree-merge rounds that previously exhausted the 4 GiB heap guard.
Small value windows share a bounded 64 KiB serialized cache; larger captures retain private disk spill.
Package mode visibility inheritance and the on-multiple-match enumeration are fixed. Full Debug/Release
units pass **3,108/3,012**; four new saved NativeAOT styles, old materialized-merge and retained version-2
consumers pass. Eight full conformance profiles are unchanged: main strict **2,681/30/1**, seven unmatched
expected codes. All 402 core and 566 Linux source/controller/probe/test hashes match.

The exact **48-child nightly matrix passes** at 2 GiB input / 200 MiB absolute RSS. The complete exact
200 MiB / 20 MiB push matrix retains **sixteen RSS failures**. The isolated ordinary three-pair gate
passes at timing geometric mean **0.996520976223**, maximum allocation ratio **1.047265587827**.
Canonical throughput is fully evaluated and **fails**: identity, fork and source-document streaming
remain slower than tree. The current overlap-values stream/tree ratio is **2.375079889881**. Full P7
remains active; see [current evidence and limitations](../streaming-bounded-values-merge-2026-10-25.md).



### Relative value paths and deferred built-in signatures

The next follow-up adds value-only lowering for relative and compound descendant populations and fixes
non-atomized replay header names. Strict streaming improves to **2,683/28/1**, with seven unmatched
expected codes; `streamable-037` and `streamable-039` now pass. Other full profiles are unchanged.
Debug/Release units pass **3,125/3,029**; normal/debug saved NativeAOT memory/spill cases and retained
old consumers pass. Function metadata keeps computed IDs/arities/fingerprint while unused type
signatures initialize lazily. The measured registry startup allocation improvement does not establish
a pass for the absolute RSS gate. Full P7 remains active; see
[the implementation and evidence](../streaming-relative-values-signatures-2026-10-25.md).



The matching current native nightly matrix passes **48 children / zero failures** at exact 2 GiB input
and 200 MiB absolute RSS. The requested 200 MiB / 20 MiB push matrix retains **16 failures**: twelve full
input/output validations and four startup/warmup exits. The ordinary three-pair gate passes at timing
geometric mean **1.004693365582**, maximum allocation ratio **1.037238560580**. All **96** paired 100 MB
process trials validate under equal 4 GiB guards; canonical throughput still fails on identity, fork
and source-document. These requirements and the full Phase 7 goal remain open. See the
[current complete evidence](../streaming-relative-values-signatures-2026-10-25.md).


### Mixed node paths and incremental simple content

The comma/content follow-up shares ordered child-path merging between comma sequences and insertions,
and emits streamed simple content directly into one text constructor. It preserves captured/empty
text nodes, identity, rollback, canonical boundaries and raw escaping, and fixes debug choose source
locations. Strict streaming improves to **2,687/24/1**, with seven unmatched expected codes. Other full
profiles are unchanged. Debug/Release units pass **3,172/3,073**; new and retained saved NativeAOT
consumers pass. Normal/debug focused generated-input probes pass at exact 200 MiB and 2 GiB input,
with first output after 356,352 bytes and full-lifetime RSS around 25–28 MB. Full P7 remains active;
see [the implementation and evidence](../streaming-comma-content-2026-10-25.md).



The final comma/content native nightly matrix passes **48 children / zero failures** at exact 2 GiB
input / 200 MiB absolute RSS. The requested 200 MiB / 20 MiB push matrix retains **16 failures** (twelve
full-input validations and four startup/warmup exits). The ordinary three-pair gate passes at timing
geometric mean **1.024354047451**, maximum allocation ratio **1.037242922079**. All **96** paired 100 MB
trials validate; the canonical throughput target still fails on identity, fork and source-document.
The same-host 1 MB / 1 GB sweep has completed: **84 trials, 72 valid, 12 failed**. All streamed trials
pass; three tree trials each for adjacent, accumulator, merge and fork at 1 GB exhaust the unchanged
4 GiB heap guard. All three planned sizes have been attempted, with those four large-input comparisons
unavailable. Full P7 remains active. See
[current evidence](../streaming-comma-content-2026-10-25.md).


### Recursive transmitting functions

Resumable function implementations now deliver recursive shallow-descent node results before the
reader advances. Self/mutual recursion, live inspection/transmission, cardinality/name errors, both
APIs, debug and saved compilation are covered. Body cloning also retains incremental text-writing
annotations. Strict streaming improves to **2,689/22/1**, fixing `su-shallow-descent-002` and `-004`,
with seven unmatched expected codes and no new failure IDs across eight profiles. Debug/Release
units pass **3,192/3,091**, all compatibility checks pass, and the frozen saved NativeAOT consumer
passes with no publish warnings. Focused normal/debug 200 MiB and 2 GiB NativeAOT runs validate exact
input and output digests, early output and lifetime RSS near 30 MB under a 200 MiB guard; the 20 MiB
push requirement remains unmet. Tracing/depth-limited/memoized and narrow-return call paths, general
continuations and the remaining P7 gates remain open. See [the implementation and evidence](../streaming-recursive-functions-2026-10-25.md).


### Document fragments and stable resource identity

Shorthand-ID fragments now work in `fn:doc`, source-document and initial URI views. Streaming scans
the prefix and exposes the selected subtree, preserving namespaces, XML Base and provider-known
ID/IDREF facts, including already-positioned borrowed readers. `document()` selects original nodes from
the stable complete resource, so different fragments retain a common root. Cache/loading keys
distinguish selected views; I/O conflict tracking
uses the physical resource URI. Only three verified passing baseline entries are removed. Debug/Release
units pass **3,237/3,134**, all compatibility checks pass, and fourteen new saved normal/debug NativeAOT
cases plus the retained consumers pass without publish warnings. Strict streaming improves to
**2,690/21/1** with no new failure IDs across eight profiles. Full P7 remains active. See
[implementation and evidence](../streaming-document-fragments-2026-10-25.md).

The final isolated ordinary three-pair gate passes: time geometric mean **0.990513413537**, maximum
allocation ratio **1.037238717165**. Focused normal/debug native fragment probes validate exact 200 MiB
and 2 GiB inputs, independent output digests, early output and owned-source closure, with lifetime
RSS **24,576,000–27,377,664 bytes** under the 200 MiB guard. These do not satisfy the separate 20 MiB
push RSS requirement or canonical throughput target. Current source hashes and artifacts match;
full P7 remains active. See [fragment evidence](../streaming-document-fragments-2026-10-25.md).


### Navigator ID provenance

The fragment loader now preserves element-local ID/IDREF facts and provider-confirmed ID lookups
separately. Equal attribute values no longer turn ordinary attributes into ID declarations, and copied
views retain their real lookups. Twelve additional public API cases cover DTD/XML IDs, repeated copies,
and locally different schema types, including annotation stripping. Debug/Release units pass
**3,249/3,146**; eight new normal/debug saved styles pass twelve native checks, with retained consumers
and no publish warnings. All eight execution profiles were freshly run, with no new failure IDs;
strict remains **2,690/21/1**. Explicit 2.0 auditing supersedes a previously copied stale report at
**4,590/8/10**; posture/sweep remains **790/129/0**. This does not attribute the 2.0 changes to this fix.
The ordinary three-pair gate passes at time geometric mean **0.977000839111**, maximum allocation ratio
**1.037247283614**. Focused 200 MiB / 2 GiB native runs pass with lifetime RSS **24,805,376–27,475,968 bytes** under the
200 MiB guard. The separate 20 MiB push RSS, throughput, semantic and complete P7 gates remain open.
See [implementation and evidence](../streaming-id-provenance-2026-10-25.md).


### Deferred header storage foundation

Source-owned header/membership scratch storage now shares the existing replay cache and private-file
machinery. Offset links retain source identity and intrinsic metadata without retaining closed nodes;
incomplete child metadata is guarded rather than reported empty. Schema-info objects are snapshotted.
Seven new tests cover the primitive, and a native **100,000**-closed-header probe plus an incomplete
owner verifies metadata, membership, cache bounds and cleanup. Full Debug/Release units pass
**3,256/3,153**, retained saved consumers and all compatibility checks pass, and eight freshly run
execution profiles have no new failure IDs. Strict remains **2,690/21/1**. The final ordinary gate
passes at time geometric mean **1.005276705514**, maximum allocation ratio **1.037242922079**; retained focused
200 MiB / 2 GiB native runs pass under the 200 MiB guard.

At this historical storage checkpoint the compiler was not connected to the store.
The subsequent deferred climbing checkpoint below connects it and repairs those five failures.
Deferred observer/accumulator snapshots still need serialization and are explicitly rejected by the
primitive until supported. These are remaining requirements, together with the other semantic,
analysis, exact push RSS, throughput and full P7 gates. See [the implementation and evidence](../streaming-deferred-headers-2026-10-25.md).

### Deferred climbing set execution

The compiler now connects the source-owned header spool to climbing `except`, mixed nested set
operators, differing projections and nearest-ancestor predicates. Potential headers are registered
before matched predicates execute; leaf membership, source completion and invocation sealing are
tracked separately. Each resumable drain returns one result, including at EOF. Explicit replay keeps
its actual nodes and final canonical order; completion releases foreign-document roots. Existing
monotonic helper entry points retain their saved-consumer ABI.

Strict streaming improves to **2,695 passed / 16 failed / 1 not run**. All **66** `sx-ExceptExpr` cases
pass, fixing `sx-except-331` through `-335`, and none of the nine fresh reports adds a failure ID.
Release and Debug units each pass **3,285 net10.0 / 3,180 net8.0**. Both native publications have no
warnings, and the new saved normal/debug cases plus retained compatibility consumers pass. The
three-pair ordinary performance gate passes with time GM **1.027012847257** and maximum allocation
ratio **1.037238717165**; individual timing outliers are retained in the report.

Actual deferred-set normal/debug native runs also pass at exact **200 MiB** and **2 GiB**, with
verified output, early emission and owned-input cleanup under the unchanged **200 MiB** heap/RSS guard.
The separate push RSS and canonical throughput criteria remain open.

Accumulator snapshot serialization, remaining analysis/semantic failures and full P7 acceptance
remain open. `supports-streaming` remains `no`; no stage is declared complete. See
[the implementation and evidence](../streaming-deferred-climbing-2026-10-25.md).

### Deferred accumulator phase snapshots

Deferred headers now preserve applicable before/after phases using a registered observer codec.
Scalars and map/array structure spill through the existing source-owned scratch stream. Atomic types,
QName prefixes, container aliases and deep nesting retain their semantics. Live source frames publish
late phases to weak snapshot readers, including before source disposal; prong readers use the original
observer without repeating rules. Unknown observer metadata remains an explicit error.

Node and opaque function values currently retain actual identity handles. General bounded spill of
those histories remains required; scalar memory results do not close that obligation. Strict analysis
and all failure baselines are unchanged. Full Release and Debug pass **3,306 net10.0 / 3,199 net8.0**;
new saved normal/debug consumers, the native late-publication regression and retained compatibility
cases pass. All nine fresh conformance reports retain their previous failure IDs. The final ordinary
three-pair gate passes with time GM **0.989223741081** and maximum allocation ratio **1.037234042553**.
The actual deferred-set/scalar-counter matrix passes normal/debug native runs at exact **200 MiB**
and **2 GiB**, with verified output, early emission and owned-source cleanup under the unchanged
**200 MiB** heap/RSS guard. Identity-bearing histories, exact push RSS and canonical throughput remain
open. Full P7 remains active.
See [implementation, limits and current evidence](../streaming-accumulator-phases-2026-10-25.md).

### Deferred compiled closure captures

Compiled functions now spill their captured sequences through the source-owned XDM scratch codec.
Actual runtime/code/signature descriptors remain separate from captured values; aliases and cycles
crossing functions, maps and arrays survive iterative replay. Recorded scalar/container closure
histories can be collected while the source remains alive. Separate runtime owners retain their
actual globals even when their generated function IDs and signatures agree.

The descriptor bound assumes a fixed actual code/owner set. Fresh evaluated runtimes can grow it;
nodes and opaque callable kinds still retain identity. Full Release and Debug each pass **3,324
net10.0 / 3,212 net8.0**, saved normal/debug and native consumers pass, and all nine conformance report
headers/failure-ID sets match the preceding checkpoint. The original ordinary gate passes with time
GM **0.986027868023** and maximum allocation ratio **1.037238403996**. Actual fixed-owner compiled
closure normal/debug native runs pass exact **200 MiB** and **2 GiB** inputs under the unchanged
**200 MiB** heap/RSS guard, with full independent output verification and early output. General
identity-history spill, exact push RSS, canonical throughput and remaining P7 gates stay open.
See [implementation, limits and evidence](../streaming-closure-captures-2026-10-25.md).

### Deferred coerced function values

Coercion wrappers now spill typed target graphs through the source-owned XDM codec, including aliases,
outer-wrapper dependency cycles and deep chains. Shared function conversion now installs wrappers for
accumulators and callbacks, supports map/array targets, preserves original function names and reports
standard accumulator type errors. Fresh parameter arrays do not become retained value histories.

Full Release and Debug each pass **3,341 net10.0 / 3,229 net8.0**; sixteen new saved normal/debug
styles and retained native compatibility consumers pass without publication warnings. All nine final
failure-ID sets remain unchanged; unmatched error codes improve strict **7 to 6**, unstreamed **5 to 4**.
The original ordinary three-pair gate passes on an unchanged retry: GM **1.035572890616**, maximum
allocation ratio **1.037242608875**. The initial failed timing trial and reversed-order attribution are
retained in the report. Actual typed-wrapper normal/debug native runs pass exact **200 MiB** and
**2 GiB** inputs with independent full-output checks, early output and cleanup under the unchanged
**200 MiB** heap/RSS guard. General opaque/node history spill, exact push RSS, canonical throughput
and remaining Phase 7 gates stay open; `supports-streaming` remains `no`.
See [implementation, limits and evidence](../streaming-coerced-captures-2026-10-25.md).

### Deferred library function values

Computed library functions now spill their actual definition/context references and per-value arity
through the source-owned XDM codec. Separate metadata tables preserve provider definition authority
and all static/dynamic context state without retaining each function object or variadic-arity history.
Aliases and fork context projection retain their semantics. The bound assumes a fixed set of actual
definition/context models; arbitrary fresh models, opaque/native callbacks and nodes remain open.

Full Release and Debug each pass **3,349 net10.0 / 3,237 net8.0**. Eight new saved normal/debug
lookup styles and **133** retained saved DLLs pass NativeAOT without publication warnings. All nine
conformance report headings/failure-ID sets remain unchanged. The original ordinary three-pair gate
passes on its first attempt: GM **0.994364503290**, maximum allocation ratio **1.037238717165**.
Actual computed-lookup normal/debug native cases pass exact **200 MiB** and **2 GiB**, with full
independent output checks, early output and cleanup under the unchanged **200 MiB** heap/RSS guard.
Exact push RSS, canonical throughput and remaining Phase 7 gates stay open; `supports-streaming`
remains `no` and the full plan remains active.
See [implementation, limits and evidence](../streaming-library-captures-2026-10-25.md).

### Deferred random-generator state

Generator maps now spill their actual `next`/`permute` numeric state through the source-owned XDM
codec, without retaining each callable or a per-state metadata table. The actual generator factory
owns the descriptor; generic native/provider callbacks retain their identity. Seed hashing, next-state
transitions and permutation semantics remain unchanged, including shared targets and fork calls.

Full Release and Debug each pass **3,356 net10.0 / 3,244 net8.0**. Six preceding-core and six current
saved normal/debug random styles plus **141** retained saved DLLs pass NativeAOT without publication
warnings. All nine conformance report headings/failure-ID sets remain unchanged. The original ordinary
three-pair gate passes: GM **0.994987004211**, maximum allocation ratio **1.037239186928**.
Actual per-row seeded-generator normal/debug native cases pass exact **200 MiB** and **2 GiB**, with
independent seeded-permutation output checks, early output and cleanup under the unchanged
**200 MiB** heap/RSS guard. General grounded-node/opaque histories, exact push RSS, canonical
throughput and remaining Phase 7 gates stay open; `supports-streaming` remains `no`.
See [implementation, limits and evidence](../streaming-random-captures-2026-10-25.md).


### S1/S4 optional context capture and intrinsic error codes

Focus-dependent references now capture actual optional focus without demanding a missing item during creation. Accumulators report XTDE3350 and key reports XTDE1270 when invoked without context; ordinary string invocation retains XPDY0002. Known match/loop focus avoids redundant absence checks. Release/Debug pass **3,380 / 3,268**, all nine profile failure-ID sets are unchanged and strict unmatched error codes improve **6 to 5**. All retained and current saved native cases pass; the corrected original performance gate and four exact native 200 MiB/2 GiB cases pass. The failed allocation trial and mixed-result try buffering trial remain preserved. Generic mixed-result try, exact push RSS, throughput and other P7 requirements remain open. See [the implementation and evidence](../streaming-optional-focus-2026-10-26.md).


### XML-to-JSON header-path continuation, 2026-10-26

Eligible filtered descendant/child arguments now preserve continuation while conversion absorbs a selected subtree. Motionless prefix predicates run once, real additional matches produce XPTY0004, and predicate faults retain precedence. Consumed input still requires actual replay. Full Release/Debug pass **3,404 / 3,290**; all nine profile failure-ID sets are unchanged, with strict unmatched codes improving **5 to 4**. Retained/current native saved consumers, the original ordinary gate and four final normal/debug exact-size native JSON cases pass. Unsupported paths, mixed-result try, exact push RSS and full P7 requirements remain open. See [the complete checkpoint](../streaming-json-header-continuation-2026-10-26.md).

### Mixed template result streaming, 2026-10-26

Eligible mixed `item()*` template and mode-dispatch calls now use resumable companions. This fixes whole-population buffering for per-record try results while preserving rollback and principal normalization. Debug choices use iterator branches; context-free raw calls retain one stateful cursor. Actual Release/Debug pass **3,422 / 3,306** and all nine conformance headings/failure-ID sets are unchanged. Retained/current saved native cases pass. Final exact-size memory and ordinary gates are tracked in [the checkpoint](../streaming-mixed-template-results-2026-10-26.md). Excluded template shapes, exact push RSS and full P7 requirements remain open.

### Reference and diagnostic contracts, 2026-10-26

Document references now reject raw backslashes before provider normalization; specific declaration errors retain precedence. QT3 boolean assertions retain genuine code notes and recognize allowed alternatives correctly. Release/Debug pass **3,442 / 3,324**, W3C **55**, all nine failure-ID sets are unchanged, and strict unmatched codes improve **4 to 1**. Native retained/current saved consumers and the original performance gate pass. Full P7 and its remaining gates stay open. See [implementation and evidence](../streaming-reference-diagnostics-2026-10-26.md).

### Source validation negotiation, 2026-10-26

Explicit streamed validation now negotiates bytes from capable resolvers, preserving reader-only and provider-error contracts. Release/Debug pass **3,449 / 3,331**; strict unmatched codes improve **1 to 0**, with unchanged failure-ID sets. Native runtime validation, **225** retained saved styles and the original ordinary performance gate pass. Imported-schema saved compilation and full P7 gates remain open. See [implementation and evidence](../streaming-source-validation-negotiation-2026-10-26.md).

### Annotation and whitespace ordering, 2026-10-26

Streamed whitespace filtering now uses the effective annotation-stripping policy, while preserving explicit validation and xml:space behavior. Release/Debug pass **3,457 / 3,339**; all nine profile headings and failure-ID sets remain unchanged. Native checks pass **12** new normal/debug saved styles plus **225** retained styles. The original ordinary performance gate passes; imported-schema persistence and full P7 gates remain open. See [implementation and evidence](../streaming-annotation-whitespace-2026-10-26.md).

### Saved imported-schema validation, 2026-10-26

Resolved schema graphs now travel with supported saved source-validation paths. Release/Debug pass **3,486 / 3,358**, with unchanged profile headings and failure-ID sets. Native checks pass **12** new imported-schema saved styles plus **237** retained styles; the ordinary gate passes. General schema-dependent constant metadata, typed static nodes, .NET 10-to-8 saved-DLL portability and full P7 gates remain open. See [implementation and evidence](../streaming-saved-schema-2026-10-26.md).

### Portable saved framework references, 2026-10-26

Saved framework references now use the core project's minimum supported runtime contract. Release/Debug pass **3,490 / 3,358**, with unchanged profile headings and failure-ID sets. Fourteen fresh portable emissions pass on .NET 8 and NativeAOT alongside **249** retained native styles. The **311**-path portable-copy corpus passes on .NET 8 and .NET 10. The ordinary gate passes; general schema metadata, typed static nodes and full P7 gates remain open. See [implementation and evidence](../streaming-portable-emitter-2026-10-26.md).

### Saved schema-dependent metadata, 2026-10-26

Saved constants now retain registered schema contexts, named atomic/cast metadata and recursive sequence/function/node tests using table-scoped format 5. Release/Debug pass **3,505 / 3,360**, with unchanged conformance profile headings and failure-ID sets. Fourteen fresh saved styles pass with **263** retained styles under NativeAOT; the **325**-path portable-copy corpus passes on .NET 8 and .NET 10. The ordinary gate passes. Typed static nodes, anonymous atomic metadata and full P7 remain open. See [implementation and evidence](../streaming-schema-metadata-2026-10-26.md).

### Typed static-node persistence, 2026-10-26

Annotated static nodes now retain schema types, shared-tree identity, nilled state and node metadata through format 6. Nilled simple-type atomization and optional cardinality checks are corrected. Release/Debug pass **3,519 / 3,372**, with unchanged profile headings and failure-ID sets. Two fresh saved styles pass with **277** retained styles under NativeAOT; the **327**-path portable-copy corpus passes on .NET 8 and .NET 10. The ordinary gate passes. Anonymous atomic constants, unowned schema contexts and full P7 remain open. See [implementation and evidence](../streaming-typed-static-nodes-2026-10-26.md).

### Deferred temporal context and integer parsing, 2026-10-26

Dynamic contexts now share deferred temporal state, and XML integer parsing uses a native-magnitude fast path while retaining arbitrary precision. Release/Debug pass **3,563 / 3,416**, with unchanged profile headings and failures; retained native and managed saved consumers pass. The exact 200 MiB / 20 MiB matrix passes **13/16**, and the 2 GiB / 200 MiB matrix passes **16/16**. All 42 canonical timing trials validate output, with target pass **False**. The ordinary performance gate passes. Full P7 remains incomplete. See [implementation and evidence](../streaming-temporal-integer-2026-10-26.md).
