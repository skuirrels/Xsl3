// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedSingletonFunctionTests
{
    // Reduced from W3C function-5014: nested filters also occur inside a predicate.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedSingletonFiltersKeepTheirOriginalEvaluationScope(bool debug)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:f="urn:filter">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:function name="f:filter" streamability="filter" as="node()">
                <xsl:param name="node" as="node()"/>
                <xsl:sequence select="$node[local-name() = 'section']"/>
              </xsl:function>
              <xsl:template match="/">
                <xsl:copy-of select="doc/f:filter(section[1])/f:filter(section)[f:filter(self::section)]/p[2]"/>
              </xsl:template>
            </xsl:stylesheet>
            """;
        var compiler = new XsltCompiler { Settings = new() { StreamedExecution = true, IncludeDebugInformation = debug } };
        var transform = compiler.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader("<doc><section><section><p>first</p><p>kept</p><p>last</p></section></section></doc>"));
        using var output = new StringWriter();
        transform.ApplyTemplates(reader, output);
        Assert.Equal("kept", output.ToString());
    }

    private const string Input = "<doc><row><a/><b/></row></doc>";
    private static string Sheet(string body, string call = "f:consume(doc/row)", string parameter = "node()", string result = "xs:integer", string parameters = "", string attributes = "", string prologue = "") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            xmlns:f="urn:test:functions" xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:function name="f:consume" streamability="absorbing" as="{{result}}" {{attributes}}>
            <xsl:param name="input" as="{{parameter}}"/>{{parameters}}
            {{prologue}}
            <xsl:sequence select="{{body}}"/>
          </xsl:function>
          <xsl:template match="/"><xsl:value-of select="{{call}}"/></xsl:template>
        </xsl:stylesheet>
        """;

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 16, false)]
    [InlineData(false, 0, true)]
    public void TheFirstNodeIsConsumedWithinTheOriginalFunctionCall(bool tracing, int depth, bool cached)
    {
        string sheet = Sheet("count($input/descendant::*)", attributes: cached ? "new-each-time='no'" : "");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true, EnableTracing = tracing, MaxRecursionDepth = depth } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal("2", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Fact]
    public void RecursiveSingletonCallsCanReceiveANodeSelection()
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }
            .Compile(new StringReader(Sheet("1 + sum($input/* ! f:consume(.))"))).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal("3", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Theory]
    [InlineData("node()", "<doc/>", null)]
    [InlineData("node()?", "<doc/>", "7")]
    [InlineData("node()?", Input, "2")]
    [InlineData("node()", "<doc><row/><row/></doc>", null)]
    [InlineData("node()?", "<doc><row/><row/></doc>", null)]
    public void TheCompleteSelectionStillHasToMatchTheRequiredCardinality(string parameter, string input, string? expected)
    {
        string sheet = Sheet("if (empty($input)) then 7 else count($input/descendant::*)", parameter: parameter);
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(input));
        if (expected == null) Assert.Contains("XPTY0004", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(reader)).Message);
        else Assert.Equal(expected, string.Concat(transform.ApplyTemplates(reader)));
    }

    [Fact]
    public void AnEmptyFunctionResultDoesNotLookLikeAMissingArgument()
    {
        string sheet = Sheet("()", "count(f:consume(doc/row))", result: "empty-sequence()");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal("0", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Fact]
    public void OtherArgumentsRetainTheCallerPosition()
    {
        string sheet = Sheet("count($input/descendant::*) + $offset", "doc/group ! f:consume(row, position())",
            parameters: "<xsl:param name='offset' as='xs:integer'/>");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader("<doc><group><row><a/><b/></row></group><group><row><c/></row></group></doc>"));
        Assert.Equal("3 3", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Fact]
    public void EmptyResultsStillInvokeTheFunctionExactlyOnce()
    {
        string sheet = Sheet("()", "count(f:consume(doc/row))", result: "empty-sequence()", prologue: "<xsl:message>called</xsl:message>");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        int calls = 0;
        transform.MessageHandler = _ => calls++;
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal("0", string.Concat(transform.ApplyTemplates(reader)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FunctionResultsAreNotExposedBeforeTheCardinalityCheck()
    {
        string sheet = Sheet("()", result: "element(made)", prologue: "<made><xsl:value-of select='$input'/></made>")
            .Replace("<xsl:value-of select=\"f:consume(doc/row)\"/>", "<xsl:sequence select='f:consume(doc/row)'/>");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader("<doc><row>first</row><row>second</row></doc>"));
        using var output = new StringWriter();
        Assert.Contains("XPTY0004", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(reader, output)).Message);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void TracingEntersTheRealFunctionWhileTheReaderIsCurrent()
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true, EnableTracing = true } }
            .Compile(new StringReader(Sheet("count($input/descendant::*)"))).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        int calls = 0;
        transform.TraceListener = new Listener(frame =>
        {
            if (frame.Kind != "function" || frame.Name?.LocalName != "consume") return;
            Assert.False(reader.EOF);
            calls++;
        });
        Assert.Equal("2", string.Concat(transform.ApplyTemplates(reader)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void RecursionDepthLimitsStillApplyToScalarCalls()
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true, MaxRecursionDepth = 4 } }
            .Compile(new StringReader(Sheet("1 + sum($input/* ! f:consume(.))"))).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader("<doc><row><a><b><c/></b></a></row></doc>"));
        Assert.Contains("recursion-limit", Assert.ThrowsAny<Exception>(() => transform.ApplyTemplates(reader)).ToString());
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void SavedAssembliesRetainScalarArgumentConsumption()
    {
        string sheet = Sheet("1 + sum($input/* ! f:consume(.))");
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null,
            "Xsl3.Tests.StreamedScalar", "Saved.Scalar", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Scalar", true)!, settings);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("3", output.ToString());
    }
#endif

    private sealed class Listener(Action<XsltFrame> enter) : IXsltTraceListener
    {
        public void Enter(XsltFrame frame) => enter(frame);
        public void Leave(XsltFrame frame) { }
    }
}
