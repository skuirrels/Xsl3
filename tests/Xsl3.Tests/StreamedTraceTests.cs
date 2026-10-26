// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedTraceTests
{
    private const string Input = "<doc><row><value>1</value><value>2</value></row><row><value>3</value></row></doc>";

    [Theory]
    [InlineData("trace(doc/row/value, 'items')", "1|2|3")]
    [InlineData("trace(doc/row, 'rows')/value", "1|2|3")]
    [InlineData("doc/row ! trace(value, 'each')", "1|2|3")]
    [InlineData("trace(doc/row/value, 'items')[position() mod 2 = 1]", "1|3")]
    [InlineData("trace((1, 'a', map{'n':1}), 'items') ! (if (. instance of map(*)) then ?n else .)", "1|a|1")]
    [InlineData("trace((), 'empty')", "")]
    [InlineData("trace(count(doc/row), 'count') instance of xs:integer", "true")]
    [InlineData("trace(doc/row)/value", "1|2|3")]
    [InlineData("let $rows := trace(doc/row) return string-join($rows/value, ',')", "1,2,3")]
    [InlineData("doc/row ! (trace(., 'node') is .)", "true|true")]
    public void TraceReturnsOriginalValuesWithoutCollectingLiveNodes(string expression, string expected)
    {
        Assert.Equal(expected, Run(expression, false).Output);
        Assert.Equal(expected, Run(expression, true).Output);
    }

    [Theory]
    [InlineData("trace(doc/row, 'rows')/value")]
    [InlineData("trace((1, 'a', doc/row), 'items')")]
    [InlineData("trace((), 'empty')")]
    [InlineData("trace(doc/row)/value")]
    [InlineData("doc/row ! (trace(., 'node') is .)")]
    [InlineData("head(trace(doc/row, 'all rows'))")]
    [InlineData("trace(map{'n':1}, 'map') ? n")]
    public void HandlerReceivesTheCanonicalWholeCallValue(string expression)
    {
        var expected = Run(expression, false, handler: true);
        var actual = Run(expression, true, handler: true);
        Assert.Equal(expected.Output, actual.Output);
        Assert.Equal(expected.Traces, actual.Traces);
        Assert.NotEmpty(actual.Traces);
    }

    [Theory]
    [InlineData("trace((), ())")]
    [InlineData("trace((), (1, 2))")]
    [InlineData("trace(doc/row, 1)")]
    public void LabelConversionIsCheckedWithoutAHandler(string expression)
    {
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => Run(expression, false)).Code);
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => Run(expression, true)).Code);
    }

    [Fact]
    public void WithoutAHandlerTheFirstHeaderIsDeliveredBeforeReadingItsContent()
    {
        const string body = "<xsl:for-each select=\"trace(doc/row, 'rows')\"><xsl:message select='name(.)'/></xsl:for-each>";
        var transform = Load(body, true);
        using var input = XmlReader.Create(new StringReader(Input));
        bool observed = false;
        var arguments = new XsltArgumentList();
        arguments.XsltMessageEncountered += (_, message) => {
            Assert.Equal("row", message.Message);
            if (observed) return;
            Assert.Equal(XmlNodeType.Element, input.NodeType);
            Assert.Equal("row", input.LocalName);
            Assert.False(input.EOF);
            observed = true;
        };
        using var output = new StringWriter();
        transform.Transform(input, arguments, output);
        Assert.True(observed);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandlerFaultsPropagateAndLeaveBorrowedInputOpen(bool forward)
    {
        var transform = Load("<xsl:value-of select=\"trace(doc/row, 'rows')\"/>", forward);
        using var input = XmlReader.Create(new StringReader(Input));
        var error = new XdmException("TEST0001", "trace callback failed");
        int calls = 0;
        var arguments = new XsltArgumentList { TraceHandler = (_, _) => { calls++; throw error; } };
        using var output = new StringWriter();
        Assert.Same(error, Assert.Throws<XdmException>(() => transform.Transform(input, arguments, output)));
        Assert.Equal(1, calls);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void ForkTraceHandlersRunOnTheInvokingThread()
    {
        const string body = """
            <xsl:fork>
              <xsl:sequence><xsl:value-of select="count(trace(doc/row, 'left'))"/><xsl:text>|</xsl:text></xsl:sequence>
              <xsl:sequence><xsl:value-of select="sum(trace(doc/row/value, 'right'))"/></xsl:sequence>
            </xsl:fork>
            """;
        int owner = Environment.CurrentManagedThreadId;
        var labels = new List<string>();
        var arguments = new XsltArgumentList { TraceHandler = (label, _) => {
            Assert.Equal(owner, Environment.CurrentManagedThreadId);
            labels.Add(label);
        } };
        var transform = Load(body, true);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(input, arguments, output);
        Assert.Equal("2|6", output.ToString());
        labels.Sort(StringComparer.Ordinal);
        Assert.Equal(new[] { "left", "right" }, labels);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeeplyNestedTraceCallsCompileAndEvaluateEachSourceOnce(bool handler)
    {
        string expression = "doc/row";
        const int depth = 24;
        for (int i = 0; i < depth; i++) expression = $"trace({expression}, 'level{i}')";
        var result = Run(expression + "/value", true, handler);
        Assert.Equal("1|2|3", result.Output);
        Assert.Equal(handler ? depth : 0, result.Traces.Count);
        if (handler)
            for (int i = 0; i < depth; i++) Assert.Equal($"level{i}", result.Traces[i].Label);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TraceReplayRetainsAccumulatorPhases(bool handler)
    {
        string sheet = Sheet("""
            <xsl:for-each select="trace(doc/row, 'rows')">
              <xsl:value-of select="accumulator-before('total') || ':' || accumulator-after('total')"/>
              <xsl:if test="position()=1"><xsl:text>|</xsl:text></xsl:if>
            </xsl:for-each>
            """).Replace("<xsl:mode streamable=\"yes\"/>", """
            <xsl:mode streamable="yes" use-accumulators="total"/>
            <xsl:accumulator name="total" as="xs:integer" initial-value="0" streamable="yes">
              <xsl:accumulator-rule match="value" phase="end" select="$value + 1"/>
            </xsl:accumulator>
            """);
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(sheet)), new() { StreamedExecution = true }, null);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        int calls = 0;
        transform.Transform(input, new XsltArgumentList { TraceHandler = handler ? (_, _) => calls++ : null }, output);
        Assert.Equal("0:2|2:3", output.ToString());
        Assert.Equal(handler ? 1 : 0, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceAndLabelAreEachEvaluatedOnce(bool handler)
    {
        string sheet = Sheet("<xsl:value-of select=\"trace(f:source(), f:label())\" separator='|'/>")
            .Replace("<xsl:template", """
            <xsl:function name="f:source"><xsl:message>source</xsl:message><xsl:sequence select="(1, 2)"/></xsl:function>
            <xsl:function name="f:label"><xsl:message>label</xsl:message><xsl:sequence select="'items'"/></xsl:function>
            <xsl:template
            """).Replace("xmlns:xs=", "xmlns:f='urn:test:functions' xmlns:xs=");
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(sheet)), new() { StreamedExecution = true }, null);
        var messages = new List<string>();
        int calls = 0;
        var arguments = new XsltArgumentList { TraceHandler = handler ? (label, value) => {
            Assert.Equal("items", label); Assert.Equal("1, 2", value); calls++;
        } : null };
        arguments.XsltMessageEncountered += (_, message) => messages.Add(message.Message);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(input, arguments, output);
        Assert.Equal("1|2", output.ToString());
        messages.Sort(StringComparer.Ordinal);
        Assert.Equal(new[] { "label", "source" }, messages);
        Assert.Equal(handler ? 1 : 0, calls);
    }

    [Fact]
    public void ForkCallbackFaultReturnsToTheCallerWithoutClosingItsReader()
    {
        var transform = Load("""
            <xsl:fork><xsl:sequence select="trace(count(doc/row), 'left')"/>
            <xsl:sequence select="trace(sum(doc/row/value), 'right')"/></xsl:fork>
            """, true);
        var error = new XdmException("TEST0001", "trace callback failed");
        int owner = Environment.CurrentManagedThreadId;
        using var cancellation = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
        var arguments = new XsltArgumentList { CancellationToken = cancellation.Token, TraceHandler = (_, _) => {
            Assert.Equal(owner, Environment.CurrentManagedThreadId); throw error;
        } };
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        Assert.Same(error, Assert.Throws<XdmException>(() => transform.Transform(input, arguments, output)));
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void TheExecutableApiRetainsTheTraceHandlerContract()
    {
        string sheet = Sheet("<xsl:value-of select=\"trace(doc/row, 'rows')/value\" separator='|'/>");
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        var traces = new List<string>(); transformer.TraceHandler = (label, value) => traces.Add(label + "=" + value);
        using var input = XmlReader.Create(new StringReader(Input));
        Assert.Equal("1|2|3", string.Concat(transformer.ApplyTemplates(input)));
        Assert.Single(traces);
        Assert.StartsWith("rows=", traces[0]);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TraceSurvivesSavedCompilation(bool handler) =>
        Assert.Equal("1|2|3", Run("trace(doc/row, 'rows')/value", true, handler, saved: true).Output);
#endif

    private static string Sheet(string body) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/">{{body}}</xsl:template>
        </xsl:stylesheet>
        """;

    private static XslCompiledTransform Load(string body, bool forward)
    {
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(Sheet(body))), new() { StreamedExecution = forward, StreamingBufferCapacity = 1 }, null);
        return transform;
    }

    private static (string Output, List<(string Label, string Value)> Traces) Run(string expression, bool forward, bool handler = false, bool saved = false)
    {
        string sheet = Sheet($"<xsl:value-of select=\"{SecurityElement.Escape(expression)}\" separator='|'/>");
        var transform = new XslCompiledTransform();
        var settings = new XsltSettings { StreamedExecution = forward, StreamingBufferCapacity = 1 };
        using var stylesheet = XmlReader.Create(new StringReader(sheet));
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(stylesheet, settings, null, "Trace", "Saved.Trace", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.Trace", true)!);
#endif
        }
        else transform.Load(stylesheet, settings, null);
        var traces = new List<(string, string)>();
        var arguments = new XsltArgumentList { TraceHandler = handler ? (label, value) => traces.Add((label, value)) : null };
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(input, arguments, output);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return (output.ToString(), traces);
    }
}
