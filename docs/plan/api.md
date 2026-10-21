# Plan: the XSLT 3.0 API

docs/plan/remaining-scope.md, workstream 4.1: the public API for what `XslCompiledTransform` can't express, checked
against SPEC §7.2 and against what the engine does today. 4.2 implements it.

## What the engine has

These facts come from the code as of 2026-10-21.

- **One compiled unit per `Load`.** `XslCompiledTransform.Load` compiles to QIL and IL, and keeps an `XmlILCommand`
  (static data, the generated delegates). The command is immutable, and `Transform` is thread-safe after `Load`,
  as upstream. Per-run state lives in an `XmlQueryRuntime` that each `Transform` call creates.
- **Entry points are chosen at compile time.** The root expression compiled into the unit decides the entry
  point:
  - an initial mode or a match;
  - an initial named template;
  - an initial function (`QilGenerator.CompileRootExpression`).

  `InitialTemplate`, `InitialMode` and `InitialFunction` are internal properties set before `Load`, which the W3C
  runner and `tools/Xsl3.Dev` use.
- **Results:**
  - Serialized through an `XmlWriter`. 2.0+ stylesheets use `PrincipalResult` or a direct raw writer.
  - As raw items, through `XmlILCommand.Evaluate` and the internal `TransformToItems` and `RawResult`.
  - Secondary results go through the thread-static `XmlQueryRuntime.ResultDocumentHandler`, or through files
    under `BaseOutputUri` with `EnableResultDocument`.
- **Parameters:**
  - Stylesheet parameters come from `XsltArgumentList`. 2.0+ stylesheets map CLR values to XDM types (`ClrValues`).
  - Static parameters are internal: `StaticParameters`, a dictionary of `XmlQualifiedName` to `IList<XPathItem>`.
  - Initial-function arguments are passed as parameters with reserved names
    (`XslCompiledTransform.InitialFunctionArgument`).
- **Packages:** `xsl:use-package` resolves against the internal `Packages` list, compiled with the stylesheet into
  one unit.
- **Run-time settings:** `XsltSettings` holds them, copied at `Load`:
  - cancellation and `Timeout`;
  - `MaxRecursionDepth` and `MaxOutputBytes`;
  - the dedicated thread;
  - the document, result-document, environment, extension, assertion and evaluation switches.
- **XDM values:** the types (`AtomicValue`, `XdmMap`, `XdmArray`, `XdmFunction`) are internal, and only `QName`
  is public. Nodes are `XPathNavigator`s. Documents parsed by 2.0+ stylesheets become compact trees (`CompactDocument`).

## Decisions

1. **Keep `XslCompiledTransform` as the compatibility surface** (SPEC §7.1), unchanged except for additions. The
   3.0 API is a second, separate surface, and it shares the engine.

2. **`XsltCompiler` and `XsltExecutable`.**
   - `XsltCompiler.Compile(Uri | XmlReader | TextReader)` returns an `XsltExecutable`. The executable is immutable,
     thread-safe and meant to be cached.
   - `XsltCompilerOptions` carries the compile-time inputs:
     - the `XsltSettings`;
     - the resolver;
     - static parameters, as `XdmValue`;
     - a package library;
     - an error callback that receives every static error, with module, line and column (4.5).
   - **One unit per entry point.** The engine fixes the entry point at compile time, so an executable keeps a small
     cache of compiled units keyed by entry point: match or mode, named template, or function name and arity.
     - The first `Compile` builds the principal unit, for the default mode.
     - Another entry point compiles once more from the same parsed stylesheet. The AST is kept, so this needs no
       re-reading.
     - This is the cost of not changing the root expression into a dispatcher. A dispatcher that chooses the entry
       point at run time is the better long-term shape, and is recorded as 4.2 follow-up work.

3. **`Xslt30Transformer`** is created by `XsltExecutable.CreateTransformer()`. It is cheap to create and not
   thread-safe. Its members:
   - **Parameters:**
     - `SetStylesheetParameters(IDictionary<QName, XdmValue>)`;
     - `GlobalContextItem`.
   - **Handlers:**
     - `MessageHandler`, which receives the message, `terminate` and the error code;
     - `ResultDocumentHandler`, which receives the URI and the output properties and returns a destination;
     - `TraceHandler`.
   - **Run state:** `BaseOutputUri` and `CancellationToken`, per transformer and so per run. This fixes the shared
     `XslCompiledTransform.BaseOutputUri` that the audit flagged.
   - **Entry points:**
     - `ApplyTemplates(XdmValue selection, Destination, QName? mode = null)`;
     - `CallTemplate(QName? name, Destination)`, where null means `xsl:initial-template`;
     - `CallFunction(QName name, XdmValue[] arguments)`, which returns an `XdmValue`;
     - the same three with a raw result: `ApplyTemplatesRaw` and `CallTemplateRaw`.
   - **Initial template and mode parameters**, with tunnel ones (XSLT 3.0 §2.3.4): a dictionary on each call.
     - These need the root expression to read them, which today's engine can't (fixlist: initial-template-002/003,
       initial-mode-004).
     - The unit for an entry point compiles its root call with a with-param for every parameter that the target
       template, or the mode's rules, declare. Each with-param reads a reserved run-time slot, falling back to the
       declared default when it's empty.

4. **Data model types (public):**
   - `XdmValue`, `XdmItem`, `XdmNode`, `XdmAtomicValue`, `XdmMap`, `XdmArray`, `XdmFunctionItem`, `XdmEmptySequence`,
     and the existing `QName`.
   - They are thin public wrappers over the internal types, with no copying: an `XdmValue` holds an
     `IList<XPathItem>`.
   - `XdmNode.Load(Stream | XmlReader | TextReader, XdmNodeLoadOptions)` builds a compact tree.
     `XdmNode.Wrap(XPathNavigator)` keeps the caller's tree.
   - `XmlNode` and `XNode` wrapping (SPEC §7.2) is deferred until someone asks for it.

5. **Destinations:**
   - `SerializedDestination` writes to a `Stream`, `TextWriter` or `XmlWriter`, with `OutputProperties` that override
     `xsl:output`.
   - `XdmDestination` collects a tree.
   - `RawDestination` collects the sequence.

   They map onto `PrincipalResult`, the compact tree writer and `RawResultWriter`.

6. **Errors (4.5):**
   - `XsltDynamicException` carries the error `QName`, the error value, and a stylesheet stack trace of template and
     function frames with locations.
   - `XsltStaticException` lists every static error.
   - Run-time errors raised today as `XslTransformException`, `XdmException` or `XmlException` are wrapped at the
     API boundary and never leak.

7. **Extension functions.** Today only 1.0 stylesheets call extension objects: in 2.0+ an unknown function is
   XPST0017 at compile time, before any object exists.
   - The 3.0 API registers them on `XsltCompilerOptions`: `RegisterExtensionFunction(QName, signature, delegate)`.
   - The compiler then knows them, and calls them like library functions.
   - `XsltSettings.EnableExtensionFunctions` still turns them off.

8. **Packages:**
   - `PackageLibrary.Add(Uri | XmlReader)` reads packages by name and version.
   - The library is compiled with the principal stylesheet, as `xsl:use-package` is today: one unit.
   - Separately compiled packages are Sink B work (docs/plan/p4-sink-b.md).

## In 1.0

- Decisions 2 to 6 and 8. Decision 7 too, if 4.2 finishes in time; until then, extension objects work for 1.0
  stylesheets only.
- The W3C runner moves onto the public API. Today it uses `InitialTemplate`, `InitialMode`, `InitialFunction`,
  `RawResult`, `Packages`, `StaticParameters`, `ResultDocumentHandler` and `BaseOutputUri`, and all of them map to
  the decisions above.
  - It keeps internal access only for diagnostics: the counts of expected-error codes in its report.

## Order of work (4.2)

1. `XdmValue` and its subtypes, and `QName` conversions. Add unit tests over the existing internal types.
2. `XsltCompiler` and `XsltExecutable` over `Compiler` and `XmlILCommand`, with one unit per entry point.
3. `Xslt30Transformer` with the destinations and handlers; per-run `BaseOutputUri`.
4. Initial template and mode parameters (decision 3), which closes initial-template-002/003 and initial-mode-004.
5. Errors (decision 6).
6. The W3C runner on the public API.
7. Registered extension functions (decision 7).

## Status (2026-10-21)

Steps 1 to 5 are done, in `src/Xsl3/Xdm/XdmValue.cs`, `src/Xsl3/Xslt/XsltCompiler.cs` and `src/Xsl3/Xslt/XsltErrors.cs`,
with unit tests in `tests/Xsl3.Tests/Xslt30ApiTests.cs`. Where the result differs from the decisions above:

- **Units** are compiled again from the stylesheet's source (kept as text, or re-read from its URI) rather than
  from a kept AST; each is cached by entry point, raw or serialized delivery, and the names of the initial
  template's or mode's parameters.
- **Destinations** are overloads taking a `Stream`, a `TextWriter` or an `XmlWriter` (serialized), or none (the raw
  result as an `XdmValue`), rather than `Destination` classes. A tree comes back as the raw result's nodes.
- **The global context item** is an `XdmNode`. Applying templates to a single node makes it the global context
  item too, as `XslCompiledTransform` does; any other selection is passed as the initial match selection.
- **Messages** reach `MessageHandler` as text; a terminating message is an `XsltDynamicException` (XTMM9000 or its
  error code).
- **Maps** and arrays give their size as `Size`: `Count` is the number of items, 1.
- The internal `XdmMap`, `XdmArray` and `XdmFunction` became `MapItem`, `ArrayItem` and `FunctionItem`, so the public
  types could take the names.

Step 7 is done too: `XsltCompiler.RegisterExtensionFunction(name, arity, implementation)` registers a function of
`XdmValue[]` for 2.0+ stylesheets. The compiler resolves calls to it (and `function-available` reports it); a .NET
exception it throws is `Q{http://xsl3/err}extension-error`; `EnableExtensionFunctions = false` turns it off. A
stylesheet that calls one can't be saved as an assembly (the delegate isn't serializable).

Step 6 is done: the W3C runner compiles with `XsltCompiler` and runs each xslt30-test case through an
`Xslt30Transformer` (apply-templates, call-template, call-function, raw or serialized, with stylesheet, static,
template and tunnel parameters, handlers and the base output URI). It measures three more passes than the internal
entry points did (7,962 against 7,959). `Xslt30Transformer.UnnamedMode` was added for `#unnamed` initial modes.
Internal access is left for diagnostics and for the saved-assembly round trip (`XSL3_W3C_SAVED`).

## Open questions for the owner

- **Namespace.** The new types could go in `Xsl3`, next to `XslCompiledTransform`, or in `Xsl3.S9`, which mirrors
  Saxon's API names for familiarity. This plan uses `Xsl3`.
- **Backends.** `OptimisationLevel` and `CodeGenBackend` (SPEC §7.2) depend on whether Sink C ships in 1.0
  (remaining-scope "Decisions" 1).
