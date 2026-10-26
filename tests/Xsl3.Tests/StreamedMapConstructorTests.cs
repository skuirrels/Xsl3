// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedMapConstructorTests
{
    private const string Input = "<doc><row id='a'><first>one</first><last>two</last><value>1</value><value>2</value></row><row id='b'><first>three</first><last>four</last><value>3</value></row></doc>";

    [Theory]
    [InlineData("doc/row ! (let $m := map{'last':string(last), 'first':string(first)} return $m?first || ':' || $m?last)", "one:two|three:four")]
    [InlineData("let $m := map{'sum':sum(doc/row/value), 'count':count(doc/row/value)} return $m?sum || ':' || $m?count", "6:3")]
    [InlineData("let $m := map{'first':doc/row/first/string(), 'last':doc/row/last/string()} return string-join($m?first, ',') || ':' || string-join($m?last, ',')", "one,three:two,four")]
    [InlineData("doc/row ! (let $m := map{first : true(), last : true()} return count(map:keys($m)))", "2|2")]
    [InlineData("doc/row ! (let $m := map{'nested':map{'last':string(last), 'first':string(first)}, 'sum':sum(value)} return $m?nested?first || ':' || $m?sum)", "one:3|three:3")]
    [InlineData("doc/row ! (let $factor := 2 return let $m := map{'sum':sum(value)*$factor, 'text':string(first)} return $m?text || ':' || $m?sum)", "one:6|three:6")]
    [InlineData("doc/row ! (let $m := map{'missing':min(missing), 'other':max(other)} return count($m?missing) + count($m?other))", "0|0")]
    [InlineData("doc/row ! (let $m := map{'first':string(first)} return $m?first)", "one|three")]
    [InlineData("doc/row ! (let $m := map{first:1} return $m?*)", "1|1")]
    public void IndependentEntriesConsumeTheSameSource(string expression, string expected)
    {
        Assert.Equal(expected, Run(expression, false));
        Assert.Equal(expected, Run(expression, true));
    }

    [Theory]
    [InlineData("map{'key':sum(doc/row/value), 'key':count(doc/row)}", "XTDE3365")]
    [InlineData("map{doc/row/first:1, 'count':count(doc/row)}", "XPTY0004")]
    [InlineData("map{():1, 'count':count(doc/row)}", "XPTY0004")]
    [InlineData("map{map{}:1, 'count':count(doc/row)}", "FOTY0013")]
    public void EntryFailuresKeepTheCanonicalError(string expression, string code)
    {
        Assert.Equal(code, Assert.Throws<XdmException>(() => Run(expression, false)).Code);
        Assert.Equal(code, Assert.Throws<XdmException>(() => Run(expression, true)).Code);
    }

    [Theory]
    [InlineData("map{'constant':1, 'other':2}")]
    [InlineData("map{'constant':true(), 'other':false()}")]
    [InlineData("map{'first':string(@id), 'other':string(@id)}")]
    public void MotionlessMapsLeaveTheInputAvailable(string expression)
    {
        string body = $"<xsl:for-each select='doc/row'><xsl:variable name='m' select=\"{SecurityElement.Escape(expression)}\"/><xsl:value-of select=\"count(map:keys($m)) || ':' || sum(value)\"/><xsl:text>|</xsl:text></xsl:for-each>";
        Assert.Equal("2:3|2:3|", Execute(Sheet(body), true));
    }

    [Theory]
    [InlineData("<xsl:map-entry key=\"'last'\" select='string(last)'/><xsl:map-entry key=\"'first'\" select='string(first)'/>", "$m?first || ':' || $m?last", "one:two|three:four|")]
    [InlineData("<xsl:map-entry key='first' select='1'/><xsl:map-entry key='last' select='2'/>", "count(map:keys($m))", "2|2|")]
    [InlineData("<xsl:map-entry key=\"'sum'\" select='sum(value)'/><xsl:map-entry key=\"'text'\"><xsl:value-of select='string(first)'/></xsl:map-entry>", "$m?text || ':' || $m?sum", "one:3|three:3|")]
    public void DirectMapEntryInstructionsUseIndependentViews(string entries, string result, string expected)
    {
        string sheet = Sheet($"<xsl:for-each select='doc/row'><xsl:variable name='m' as='map(*)'><xsl:map>{entries}</xsl:map></xsl:variable><xsl:value-of select=\"{result}\"/><xsl:text>|</xsl:text></xsl:for-each>");
        Assert.Equal(expected, Execute(sheet, false));
        Assert.Equal(expected, Execute(sheet, true));
    }

    [Fact]
    public void DynamicMapContentRetainsItsSingleConsumer()
    {
        string sheet = Sheet("""
            <xsl:variable name="m" as="map(*)"><xsl:map><xsl:if test="true()"><xsl:map-entry key="'sum'" select="sum(doc/row/value)"/></xsl:if>
            <xsl:map-entry key="'constant'" select="2"/></xsl:map></xsl:variable>
            <xsl:value-of select="$m?sum || ':' || $m?constant"/>
            """);
        Assert.Equal("6:2", Execute(sheet, false));
        Assert.Equal("6:2", Execute(sheet, true));
    }

    [Theory]
    [InlineData(Input, "6:3")]
    [InlineData("<doc/>", "0:0")]
    public void AFunctionWithAnAbsentFocusCanForkCapturedNodeParameters(string input, string expected)
    {
        string sheet = Sheet("<xsl:value-of select='f:summary(doc/row/value)'/>").Replace("<xsl:template", """
            <xsl:function name="f:summary" streamability="absorbing">
              <xsl:param name="nodes" as="node()*"/>
              <xsl:variable name="m" select="map{'sum':sum($nodes), 'count':count($nodes)}"/>
              <xsl:sequence select="$m?sum || ':' || $m?count"/>
            </xsl:function><xsl:template
            """).Replace("xmlns:map=", "xmlns:f='urn:test:functions' xmlns:map=");
        Assert.Equal(expected, Execute(sheet, false, input: input));
        Assert.Equal(expected, Execute(sheet, true, input: input));
    }

    [Theory]
    [InlineData("map{'one':name(head($nodes)), 'two':name(head($nodes))}", "$m?one || ':' || $m?two", "value:value")]
    [InlineData("map{'nested':map{'sum':sum($nodes), 'count':count($nodes)}, 'other':count($nodes)}", "$m?nested?sum || ':' || $m?other", "6:3")]
    public void ParameterCompletionWorksForEarlyAndNestedEntryConsumers(string map, string result, string expected)
    {
        string sheet = Sheet("<xsl:value-of select='f:summary(doc/row/value)'/>").Replace("<xsl:template", $"<xsl:function name='f:summary' streamability='absorbing'><xsl:param name='nodes' as='node()*'/><xsl:variable name='m' select=\"{SecurityElement.Escape(map)}\"/><xsl:sequence select=\"{result}\"/></xsl:function><xsl:template")
            .Replace("xmlns:map=", "xmlns:f='urn:test:functions' xmlns:map=");
        Assert.Equal(expected, Execute(sheet, false));
        Assert.Equal(expected, Execute(sheet, true));
    }

    [Theory]
    [InlineData("first", "XPTY0020")]
    [InlineData("./first", "XPTY0019")]
    public void ABodyCatchCannotHideItsArgumentFactoryFailure(string source, string code)
    {
        string sheet = Sheet($"<xsl:value-of select='1 ! f:summary({source})'/>").Replace("<xsl:template", """
            <xsl:function name="f:summary" streamability="absorbing"><xsl:param name="nodes" as="node()*"/>
              <xsl:try><xsl:variable name="m" select="map{'sum':sum($nodes), 'count':count($nodes)}"/>
              <xsl:sequence select="$m?count"/><xsl:catch errors="*"><xsl:sequence select="0"/></xsl:catch></xsl:try>
            </xsl:function><xsl:template
            """).Replace("xmlns:map=", "xmlns:f='urn:test:functions' xmlns:map=");
        Assert.Equal(code, Assert.Throws<XdmException>(() => Execute(sheet, false)).Code);
        Assert.Equal(code, Assert.Throws<XdmException>(() => Execute(sheet, true)).Code);
    }

    [Fact]
    public void ReturnedFunctionsUseTheCallerAfterTheEntryWorkersFinish()
    {
        int owner = Environment.CurrentManagedThreadId;
        int calls = 0;
        var arguments = new XsltArgumentList { TraceHandler = (label, value) => {
            Assert.Equal(owner, Environment.CurrentManagedThreadId);
            Assert.Equal("returned", label); Assert.Equal("ok", value); calls++;
        } };
        Assert.Equal("ok:6", Run("let $m := map{'getter':function(){trace('ok','returned')}, 'sum':sum(doc/row/value)} return $m?getter() || ':' || $m?sum", true, arguments));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GlobalValuesAreSharedAcrossEntryWorkers()
    {
        string sheet = Sheet("<xsl:value-of select=\"let $m := map{'sum':sum(doc/row/value)*$g, 'count':count(doc/row/value)*$g} return $m?sum || ':' || $m?count\"/>")
            .Replace("<xsl:template", "<xsl:variable name='g' select=\"trace(2,'global')\"/><xsl:template");
        int calls = 0;
        var arguments = new XsltArgumentList { TraceHandler = (label, value) => { Assert.Equal("global", label); Assert.Equal("2", value); calls++; } };
        Assert.Equal("12:6", Execute(sheet, true, arguments));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ParallelEntryTraceCallbacksKeepTheirInvokingThread()
    {
        int owner = Environment.CurrentManagedThreadId;
        var labels = new List<string>();
        var arguments = new XsltArgumentList { TraceHandler = (label, _) => {
            Assert.Equal(owner, Environment.CurrentManagedThreadId); labels.Add(label);
        } };
        Assert.Equal("6:3", Run("let $m := map{'sum':trace(sum(doc/row/value),'sum'), 'count':trace(count(doc/row/value),'count')} return $m?sum || ':' || $m?count", true, arguments));
        labels.Sort(StringComparer.Ordinal);
        Assert.Equal(new[] { "count", "sum" }, labels);
    }

    [Fact]
    public void EachRecordCanFinishBeforeTheWholeInput()
    {
        string sheet = Sheet("""
            <xsl:for-each select="doc/row"><xsl:variable name="m" select="map{'last':string(last), 'first':string(first)}"/>
            <xsl:message select="$m?first || ':' || $m?last"/></xsl:for-each>
            """);
        var transform = Load(sheet, true);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        int calls = 0;
        var arguments = new XsltArgumentList();
        arguments.XsltMessageEncountered += (_, message) => {
            if (calls++ == 0) { Assert.Equal("one:two", message.Message); Assert.False(input.EOF);
                Assert.Equal(XmlNodeType.EndElement, input.NodeType); Assert.Equal("row", input.LocalName); }
        };
        transform.Transform(input, arguments, output);
        Assert.Equal(2, calls);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void CallbackFaultCancelsAndJoinsEntryWorkers()
    {
        var error = new XdmException("TEST0001", "map entry callback failed");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var arguments = new XsltArgumentList { CancellationToken = cancellation.Token, TraceHandler = (_, _) => throw error };
        Assert.Same(error, Assert.Throws<XdmException>(() => Run("map{'sum':trace(sum(doc/row/value),'sum'), 'count':count(doc/row/value)}", true, arguments)));
    }

    [Fact]
    public void TheExecutableApiUsesTheSameParallelEntries()
    {
        string sheet = Sheet("<xsl:value-of select=\"let $m := map{'sum':sum(doc/row/value), 'count':count(doc/row/value)} return $m?sum || ':' || $m?count\"/>");
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader(Input));
        Assert.Equal("6:3", string.Concat(transformer.ApplyTemplates(input)));
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

#if NET9_0_OR_GREATER
    [Fact]
    public void ParallelEntriesSurviveSavedCompilation() =>
        Assert.Equal("6:3", Run("let $m := map{'sum':sum(doc/row/value), 'count':count(doc/row/value)} return $m?sum || ':' || $m?count", true, saved: true));
#endif

    private static string Sheet(string body) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:map="http://www.w3.org/2005/xpath-functions/map">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/">{{body}}</xsl:template>
        </xsl:stylesheet>
        """;

    private static XslCompiledTransform Load(string sheet, bool forward, bool saved = false)
    {
        var transform = new XslCompiledTransform();
        var settings = new XsltSettings { StreamedExecution = forward, StreamingBufferCapacity = 1 };
        using var stylesheet = XmlReader.Create(new StringReader(sheet));
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(stylesheet, settings, null, "ParallelMap", "Saved.ParallelMap", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.ParallelMap", true)!);
#endif
        }
        else transform.Load(stylesheet, settings, null);
        return transform;
    }

    private static string Run(string expression, bool forward, XsltArgumentList? arguments = null, bool saved = false) =>
        Execute(Sheet($"<xsl:value-of select=\"{SecurityElement.Escape(expression)}\" separator='|'/>"), forward, arguments, saved);

    private static string Execute(string sheet, bool forward, XsltArgumentList? arguments = null, bool saved = false, string input = Input)
    {
        var transform = Load(sheet, forward, saved);
        using var reader = XmlReader.Create(new StringReader(input));
        using var output = new StringWriter();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        arguments ??= new XsltArgumentList { CancellationToken = cancellation.Token };
        try { transform.Transform(reader, arguments, output); return output.ToString(); }
        finally { Assert.NotEqual(ReadState.Closed, reader.ReadState); }
    }
}
