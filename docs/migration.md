# Moving from System.Xml.Xsl

Xsl3 keeps the API of `System.Xml.Xsl.XslCompiledTransform`. For XSLT 1.0 it produces the same output as stock .NET,
byte for byte: `tests/Xsl3.Differential` checks that across the 1.0 corpus on every change.

## The change

```diff
- using System.Xml.Xsl;
+ using Xsl3;
```

`XslCompiledTransform`, `XsltArgumentList`, `XsltSettings`, `XsltException`, `XsltCompileException` and
`XsltMessageEncounteredEventArgs` have the same members. Also change:

- `catch (System.Xml.Xsl.XsltException)` to `catch (Xsl3.XsltException)`: Xsl3's exceptions are its own types.
- `XslTransform` (obsolete since .NET 2.0) isn't provided; move to `XslCompiledTransform`.
- `System.Xml.Xsl.XsltContext` stays where it is: it belongs to `System.Xml`'s XPath 1.0 engine, not to XSLT.

`msxsl:script` throws `PlatformNotSupportedException`, as it does on .NET (Core). Extension objects
(`XsltArgumentList.AddExtensionObject`) work as before; `XsltSettings.EnableExtensionFunctions = false` turns them off.

[docs/divergences.md](divergences.md) lists every known difference from stock; there are a few, all fixes of stock
bugs.

## What you gain without changing stylesheets

- **Speed.** XSLT 1.0 runs about 1.4× faster than stock on average ([docs/performance.md](performance.md)).
  `UseCompactTree = true`, set before `Load`, parses inputs into a tree that takes about 60% of `XPathDocument`'s memory;
  the visible differences are the strings `generate-id()` returns and the order between nodes from different
  documents (compact trees come in the order they were built, so the input comes before `document()`'s).
- **Limits.** `XsltSettings.Timeout`, `MaxRecursionDepth`, `MaxOutputBytes` and `XsltArgumentList.CancellationToken`
  stop runaway transformations with an error instead of hanging or crashing the process; `UseDedicatedThread` gives
  deeply recursive stylesheets a large stack.

## 1.1.0: streamability declarations

Version 1.1.0 checks streamability declarations with `XsltSettings.Streamability = XsltStreamabilityMode.Strict`
by default. Rejected declarations can raise `XTSE3430`. To permit ordinary tree execution when analysis cannot
establish streamability, explicitly select `XsltStreamabilityMode.Fallback`. This setting controls analysis;
normal execution remains tree-based and streaming remains unsupported. See [release notes](releases/1.1.0.md).

## 1.1.1: backwards-compatible instructions in streamable content

Version 1.1.1 additionally applies §3.9.1 to instructions processed with XSLT 1.0 behavior inside declared-streamable
content, including otherwise motionless instructions. Strict analysis reports `XTSE3430`; remove the streamability
claim or explicitly select Fallback for tree execution, or update the instruction's version when the 2.0/3.0 semantics
are appropriate. See [1.1.1 release notes](releases/1.1.1.md). Ordinary non-streamable compatibility behavior is retained.

## Moving stylesheets to XSLT 3.0

Change `version="1.0"` to `version="3.0"` and the stylesheet runs on the XSLT 3.0 processor. Things to know:

- **XPath 2.0+ is stricter about types.** `'1' = 1` is a type error in 2.0 (it was true in 1.0); an empty
  sequence in arithmetic gives an empty sequence, not NaN; `xsl:value-of` outputs every item of a sequence, separated
  by spaces, not just the first. Regions marked `version="1.0"` keep the 1.0 rules (backwards-compatible mode), and
  `XsltSettings.VersionMode = XsltVersionMode.BackwardsCompatible` runs a whole `version="1.0"` stylesheet on the 3.0
  processor that way.
- **Parameters keep their .NET types.** In a 2.0+ stylesheet an `int` passed with `AddParam` is an `xs:integer`, a
  `DateTime` an `xs:dateTime`, a dictionary a map ([README](../README.md#parameters-of-xslt-20-and-30-stylesheets)). In
  1.0 every number was a double and a `DateTime` a string.
- **Reaching outside is opt-in.** `doc()`, `unparsed-text()` and `json-doc()` need `EnableDocumentFunction`, like
  `document()`; `xsl:result-document` writing files needs `EnableResultDocument` and a `BaseOutputUri`;
  `xsl:evaluate` needs `EnableEvaluate`.
- **Secondary result delivery.** Set `XsltArgumentList.ResultDocumentHandler` to receive serialized results
  from legacy `Transform` calls, including saved stylesheets under NativeAOT. It has the same URI/writer contract
  as `Xslt30Transformer.ResultDocumentHandler`: Xsl3 owns and disposes each returned writer on completion,
  errors and cancellation. A relative target can remain relative without a base output URI. Forward execution
  can write partial output before a later error. The handler is scoped to that invocation.
- **Result trees are sequences.** `TransformToItems` is internal; to get the result as values rather than markup, use
  the 3.0 API ([docs/getting-started.md](getting-started.md)), whose `CallTemplate`, `ApplyTemplates` and
  `CallFunction` return `XdmValue`s.
- **Grouping, sorting and keys.** `xsl:for-each-group` replaces Muenchian grouping, and `xsl:sort` sorts by code
  point unless a collation is named; the 3.0 rewrites of the classic 1.0 benchmarks run faster on Xsl3 than the 1.0
  originals do on stock.

## Ahead-of-time compilation

Stock `xsltc.exe` and `Load(Type)` exist for .NET Framework only. Xsl3's `CompileToAssembly` (on .NET 9+) and the
`Xsl3.Build` MSBuild targets save compiled stylesheets as assemblies that `Load(Type)` loads without compiling, also
under NativeAOT.


During P7 development, recompile saved stylesheets to receive the node-only `xsl:copy-of` content fix.
It preserves the compiler's node-only result information so a containing constructor can emit incrementally
instead of buffering its complete content. Existing saved assemblies remain load-compatible and keep their
previous emitted code; the namespace-scope runtime cache applies independently. This does not enable a
streaming-support claim or change the development switch and memory budgets. See
[copy-content evidence](streaming-copy-content-2026-10-24.md).

Overlapping descendant value consumption in development streamed execution can use temporary storage.
The source document owns two delete-on-close spill files and reuses them between sequential windows.
The OS temporary directory must be writable; Unix files are created owner-only. Cancellation and
source failures release the files with the document. Plain forward navigation retains its existing
position checks, and explicit `copy-of()`/`snapshot()` keeps its own grounding behavior. See
[value replay, bounds and remaining streaming requirements](streaming-value-replay-2026-10-25.md).
