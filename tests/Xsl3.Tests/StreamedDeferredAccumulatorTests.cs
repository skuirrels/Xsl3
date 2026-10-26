// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Reflection;
using System.Xml;
using Xsl3.Xdm;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class StreamedDeferredAccumulatorTests
{
    private const string Input = "<doc><row id='a'><x/></row><row id='b'><y/></row><row id='c'><x/><y/></row></doc>";
    private const string Selection = "doc/row/x/ancestor::row except doc/row/y/ancestor::row";
    private const string Counter = """
        <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="yes">
          <xsl:accumulator-rule match="row" select="$value + 1"/>
          <xsl:accumulator-rule match="row" phase="end" select="$value + 10"/>
        </xsl:accumulator>
        """;

    private static string Sheet(string expression, string declarations) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xsl:mode streamable="yes" use-accumulators="#all"/><xsl:output method="text"/>
          {{declarations}}
          <xsl:template match="/"><xsl:value-of select="{{expression}}" separator="|"/></xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string expression, string declarations, bool forward, bool debug, bool saved = false, XsltArgumentList? arguments = null)
    {
        var transform = new XslCompiledTransform(debug);
        var settings = new XsltSettings { StreamedExecution = forward };
        using var sheet = XmlReader.Create(new StringReader(Sheet(expression, declarations)));
        if (saved)
        {
#if NET10_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(sheet, settings, null, "DeferredAccumulators", "Saved.DeferredAccumulators", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.DeferredAccumulators", true)!, settings);
#else
            throw new InvalidOperationException("Saved compilation requires .NET 10");
#endif
        }
        else transform.Load(sheet, settings, null);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(input, arguments, output);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }

    [Theory]
    [InlineData("(doc/row/x/ancestor::row except doc/row/y/ancestor::row) ! concat(@id, ':', accumulator-before('n'))", "a:1")]
    [InlineData("(doc/row/x/ancestor::row union doc/row/y/ancestor::doc) ! concat(name(), ':', accumulator-before('n'))", "doc:0|row:1|row:23")]
    [InlineData("(doc/row[1]/x/ancestor::doc except doc/row[1]/missing/ancestor::doc) ! accumulator-before('n')", "0")]
    public void DeferredHeadersKeepPublishedBeforeValues(string expression, string expected)
    {
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
            Assert.Equal(expected, Run(expression, Counter, forward, debug));
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression, Counter))).CreateTransformer();
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(input)));
            Assert.NotEqual(ReadState.Closed, input.ReadState);
        }
    }

    [Theory]
    [InlineData("map{'v': [0, QName('urn:types', 't:q')]}", "map{'v': [$value?v?1 + 1, QName('urn:types', 't:q')]}", "concat(accumulator-before('v')?v?1, ':', prefix-from-QName(accumulator-before('v')?v?2))", "1:t")]
    [InlineData("function(){''}", "let $id := string(@id) return function(){$id}", "accumulator-before('v')()", "a")]
    [InlineData("function($n as xs:integer) as xs:string {string($n)}", "let $captured := map{'id': [string(@id)]} return function($n as xs:integer) as xs:string {concat($captured?id?1, ':', $n)}", "accumulator-before('v')(7)", "a:7")]
    [InlineData("concat('', ?)", "concat(string(@id), ?)", "accumulator-before('v')(':suffix')", "a:suffix")]
    [InlineData("()", "parse-xml(concat('&lt;saved id=\"', @id, '\"/&gt;'))", "accumulator-before('v')/saved/@id", "a")]
    public void ContainerFunctionAndGroundedNodeValuesRetainTheirActualSemantics(string initial, string rule, string body, string expected)
    {
        string declarations = $"<xsl:accumulator name='v' initial-value=\"{initial.Replace("\"", "&quot;")}\" streamable='yes'>"
            + $"<xsl:accumulator-rule match='row' select=\"{rule.Replace("\"", "&quot;")}\"/></xsl:accumulator>";
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
            Assert.Equal(expected, Run($"({Selection}) ! ({body})", declarations, forward, debug));
#if NET10_0_OR_GREATER
        foreach (bool debug in new[] { false, true })
            Assert.Equal(expected, Run($"({Selection}) ! ({body})", declarations, true, debug, saved: true));
#endif
    }

    [Theory]
    [InlineData("accumulator-before('v')('bad')", "XPTY0004")]
    [InlineData("accumulator-before('v')(1, 2)", "XPTY0004")]
    [InlineData("accumulator-before('v')(0)", "FOER0000")]
    public void ReplayedClosureCallsKeepArgumentChecksAndBodyErrors(string body, string code)
    {
        const string declarations = """
            <xsl:accumulator name="v" initial-value="function($n as xs:integer){$n}" streamable="yes">
              <xsl:accumulator-rule match="row" select="let $id := string(@id) return function($n as xs:integer) {if ($n = 0) then error() else concat($id, $n)}"/>
            </xsl:accumulator>
            """;
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
            Assert.Equal(code, Assert.Throws<XdmException>(() => Run($"({Selection}) ! ({body})", declarations, forward, debug)).Code);
    }

    [Fact]
    public void DeferredSnapshotsDoNotEagerlyDemandAFailingAccumulator()
    {
        const string bad = "<xsl:accumulator name='bad' initial-value='0' streamable='yes'><xsl:accumulator-rule match='row' select='error()'/></xsl:accumulator>";
        Assert.Equal("1", Run($"({Selection}) ! accumulator-before('n')", Counter + bad, true, false));
        Assert.Equal("FOER0000", Assert.Throws<XdmException>(() => Run($"({Selection}) ! accumulator-before('bad')", Counter + bad, true, false)).Code);
    }

    [Theory]
    [InlineData("function() as xs:string", "function(){''}", "let $capture := map{'id': [string(@id)]} return function(){$capture?id?1}", "accumulator-before('v')()", "a|b|c")]
    [InlineData("function(xs:integer) as xs:string", "function($n){string($n)}", "let $id := string(@id) return function($n){concat($id, ':', $n)}", "accumulator-before('v')(xs:untypedAtomic('7'))", "a:7|b:7|c:7")]
    [InlineData("function(xs:string) as xs:string", "map{'id': ''}", "map{'id': string(@id)}", "accumulator-before('v')('id')", "a|b|c")]
    [InlineData("function(xs:integer) as xs:string", "[xs:untypedAtomic('')]", "[xs:untypedAtomic(string(@id))]", "concat(accumulator-before('v')(1), ':', accumulator-before('v')(1) instance of xs:string)", "a:true|b:true|c:true")]
    [InlineData("function(xs:integer) as xs:double", "abs#1", "abs#1", "concat(local-name-from-QName(function-name(accumulator-before('v'))), ':', accumulator-before('v')(-7) instance of xs:double)", "abs:true|abs:true|abs:true")]
    public void TypedAccumulatorClosuresRetainTheirCoercionWrappers(string type, string initial, string rule, string body, string expected)
    {
        string declarations = $"<xsl:accumulator name='v' as='{type}' initial-value=\"{initial}\" streamable='yes'><xsl:accumulator-rule match='row' select=\"{rule}\"/></xsl:accumulator>";
        string expression = $"(doc/row/x/ancestor::row union doc/row/y/ancestor::row) ! ({body})";
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true }) Assert.Equal(expected, Run(expression, declarations, forward, debug));
#if NET10_0_OR_GREATER
        foreach (bool debug in new[] { false, true }) Assert.Equal(expected, Run(expression, declarations, true, debug, saved: true));
#endif
    }

    [Theory]
    [InlineData("function() as xs:integer", "function(){0}", "let $id := string(@id) return function(){$id}", "accumulator-before('v')()")]
    [InlineData("function(xs:integer) as xs:string", "function($n){string($n)}", "let $id := string(@id) return function($n){concat($id, $n)}", "accumulator-before('v')('wrong')")]
    public void TypedAccumulatorClosuresKeepArgumentAndResultCoercionErrors(string type, string initial, string rule, string body)
    {
        string declarations = $"<xsl:accumulator name='v' as='{type}' initial-value=\"{initial}\" streamable='yes'><xsl:accumulator-rule match='row' select=\"{rule}\"/></xsl:accumulator>";
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
            Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => Run($"({Selection}) ! ({body})", declarations, forward, debug)).Code);
    }

    [Theory]
    [InlineData("xs:integer", "'wrong'", "0")]
    [InlineData("xs:integer", "0", "'wrong'")]
    [InlineData("xs:integer", "()", "0")]
    [InlineData("xs:integer", "(1, 2)", "0")]
    [InlineData("function() as xs:string", "function($n){$n}", "function(){''}")]
    public void AccumulatorConversionFailuresUseTheStandardTypeError(string type, string initial, string rule)
    {
        string declarations = $"<xsl:accumulator name='v' as='{type}' initial-value=\"{initial}\" streamable='yes'><xsl:accumulator-rule match='row' select=\"{rule}\"/></xsl:accumulator>";
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
            Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => Run($"({Selection}) ! accumulator-before('v')", declarations, forward, debug)).Code);
    }

    [Theory]
    [InlineData("concat", 3, "accumulator-before('v')(string(@id), ':', string(@id))", "a:a|b:b|c:c", "")]
    [InlineData("compare", 2, "concat(accumulator-before('v')('a', 'A'), ':', compare('a', 'A'))", "0:1|0:1|0:1", "default-collation='" + Xsl3.XPath31.Functions.Collation.HtmlAsciiCaseInsensitiveUri + "'")]
    [InlineData("static-base-uri", 0, "string(accumulator-before('v')())", "https://lookup.example/static/|https://lookup.example/static/|https://lookup.example/static/", "xml:base='https://lookup.example/static/'")]
    [InlineData("current-dateTime", 0, "accumulator-before('v')() eq current-dateTime()", "true|true|true", "")]
    public void ComputedLibraryLookupsKeepTheContextWhereTheFunctionWasCreated(string lookup, int arity, string body, string expected, string attributes)
    {
        string rule = $"function-lookup(QName('{QName.Ns.Fn}', $lookup), {arity})";
        string declarations = $"<xsl:param name='lookup' as='xs:string' required='yes'/><xsl:accumulator name='v' as='function(*)' initial-value=\"{rule}\" streamable='yes' {attributes}><xsl:accumulator-rule match='row' select=\"{rule}\"/></xsl:accumulator>";
        string expression = $"(doc/row/x/ancestor::row union doc/row/y/ancestor::row) ! ({body})";
        var arguments = new XsltArgumentList(); arguments.AddParam("lookup", "", lookup);
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true }) Assert.Equal(expected, Run(expression, declarations, forward, debug, arguments: arguments));
#if NET10_0_OR_GREATER
        foreach (bool debug in new[] { false, true }) Assert.Equal(expected, Run(expression, declarations, true, debug, saved: true, arguments: arguments));
#endif
        foreach (bool forward in new[] { false, true })
        {
            var transformer = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression, declarations))).CreateTransformer();
            transformer.StylesheetParameters[new QName("lookup")] = new XdmAtomicValue(lookup);
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal(expected, string.Concat(transformer.ApplyTemplates(input)));
            Assert.NotEqual(ReadState.Closed, input.ReadState);
        }
    }

    [Theory]
    [InlineData("map(*)", "random-number-generator(0)", "random-number-generator(string(@id))", "concat(accumulator-before('v')?number, ':', accumulator-before('v')?next()?number, ':', string-join(accumulator-before('v')?permute(1 to 10) ! string(.), ','))")]
    [InlineData("function(*)", "random-number-generator(0)?next", "random-number-generator(string(@id))?next", "concat(accumulator-before('v')()?number, ':', accumulator-before('v')()?next()?number)")]
    [InlineData("function(*)", "random-number-generator(0)?permute", "random-number-generator(string(@id))?permute", "string-join(accumulator-before('v')(1 to 10) ! string(.), ',')")]
    public void DeferredRandomGeneratorsPreserveSeededNextAndPermutationState(string type, string initial, string rule, string body)
    {
        string declarations = $"<xsl:accumulator name='v' as='{type}' initial-value=\"{initial}\" streamable='yes'><xsl:accumulator-rule match='row' select=\"{rule}\"/></xsl:accumulator>";
        string expression = $"(doc/row/x/ancestor::row except doc/row/missing/ancestor::row) ! concat(@id, ':', {body})";
        string expected = Run(expression, declarations, false, false);
        Assert.StartsWith("a:", expected); Assert.Contains("|c:", expected);
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true }) Assert.Equal(expected, Run(expression, declarations, forward, debug));
#if NET10_0_OR_GREATER
        foreach (bool debug in new[] { false, true }) Assert.Equal(expected, Run(expression, declarations, true, debug, saved: true));
#endif
        foreach (bool forward in new[] { false, true })
        {
            var transformer = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression, declarations))).CreateTransformer();
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal(expected, string.Concat(transformer.ApplyTemplates(input)));
            Assert.NotEqual(ReadState.Closed, input.ReadState);
        }
    }

    [Fact]
    public void ClosedScratchHeadersRetainBothPhasesAfterTheSourceHasAdvanced()
    {
        using var source = new StreamedForkAccumulatorTests.Source(Input);
        var node = source.Focus;
        var spool = node.Document.HeaderSpool;
        var root = spool.AddOwner(node.PositionNode);
        Assert.True(node.MoveToFirstChild()); var doc = node.PositionNode;
        var parent = spool.AddOwner(doc, root);
        Assert.True(node.MoveToFirstChild()); var row = node.PositionNode;
        var owner = spool.AddOwner(row, parent);
        Assert.Equal("11", source.Value(node, after: true));
        spool.UpdateOwner(owner, row);
        while (node.MoveToNext()) { }
        spool.UpdateOwner(parent, doc);
        var restored = spool.Read(owner);
        Assert.Equal("1", source.Value(restored)); Assert.Equal("11", source.Value(restored, after: true));
        Assert.True(restored.MoveToParent());
        Assert.Equal("0", source.Value(restored)); Assert.Equal("33", source.Value(restored, after: true));
    }

    [Fact]
    public void AnUnpublishedEndPhaseFinishesTheLiveAncestorInsteadOfTheScratchHeader()
    {
        using var source = new StreamedForkAccumulatorTests.Source(Input);
        var node = source.Focus; var spool = node.Document.HeaderSpool;
        var root = spool.AddOwner(node.PositionNode);
        Assert.True(node.MoveToFirstChild()); var parent = spool.AddOwner(node.PositionNode, root);
        var restored = spool.Read(parent);
        Assert.Equal("0", source.Value(restored));
        Assert.Equal("33", source.Value(restored, after: true));
        Assert.Equal("0", source.Value(restored));
        Assert.Equal(3, source.Starts); Assert.Equal(3, source.Ends);
    }

    [Fact]
    public void AReadSnapshotReceivesItsEndPhaseBeforeTheSourceCloses()
    {
        using var source = new StreamedForkAccumulatorTests.Source(Input);
        var node = source.Focus; var spool = node.Document.HeaderSpool;
        var root = spool.AddOwner(node.PositionNode);
        Assert.True(node.MoveToFirstChild()); var parent = spool.AddOwner(node.PositionNode, root);
        var original = node.Clone(); var restored = spool.Read(parent);
        Assert.Equal("0", source.Value(restored));
        Assert.Equal("33", source.Value(original, after: true));
        source.Close();
        Assert.Equal("33", source.Value(restored, after: true));
    }

    [Fact]
    public void OpenFramesDoNotRetainDiscardedSnapshotValues()
    {
        using var source = new StreamedForkAccumulatorTests.Source(Input);
        var node = source.Focus; var spool = node.Document.HeaderSpool;
        var root = spool.AddOwner(node.PositionNode);
        Assert.True(node.MoveToFirstChild()); var parent = spool.AddOwner(node.PositionNode, root);
        WeakReference discarded = ReadSnapshot(source, spool, parent);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(discarded.IsAlive); GC.KeepAlive(source);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ReadSnapshot(StreamedForkAccumulatorTests.Source source, StreamedHeaderSpool spool, StreamedHeaderSpool.Owner parent)
        => new(source.Items(spool.Read(parent)));

    [Fact]
    public void ForkScratchHeadersReadOriginalPublishedPhasesWithoutRerunningRules()
    {
        using var source = new StreamedForkAccumulatorTests.Source(Input);
        source.Run(source.Focus, Check, Check);
        Assert.Equal(3, source.Starts); Assert.Equal(3, source.Ends);

        void Check(StreamedNavigator node)
        {
            var spool = node.Document.HeaderSpool; var root = spool.AddOwner(node.PositionNode);
            Assert.True(node.MoveToFirstChild()); var parent = spool.AddOwner(node.PositionNode, root);
            Assert.True(node.MoveToFirstChild()); var row = node.PositionNode; var owner = spool.AddOwner(row, parent);
            Assert.Equal("11", source.Value(node, after: true)); spool.UpdateOwner(owner, row);
            var restored = spool.Read(owner);
            Assert.Equal("1", source.Value(restored)); Assert.Equal("11", source.Value(restored, after: true));
            while (node.MoveToNext()) source.Value(node, after: true);
        }
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedConsumersReadDeferredAccumulatorPhases(bool debug)
    {
        Assert.Equal("a:1", Run($"({Selection}) ! concat(@id, ':', accumulator-before('n'))", Counter, true, debug, saved: true));
    }
#endif
}
