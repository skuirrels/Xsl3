// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedFunctionTests
{
    private const string Input = "<doc><row><a/><b/></row><row><c><d/></c></row></doc>";

    private static string Sheet(string body, string call = "f:consume(doc/row)", string extra = "") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            xmlns:f="urn:test:functions" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="f xs">
          <xsl:output method="text"/><xsl:mode streamable="yes"/>
          <xsl:function name="f:consume" streamability="absorbing" as="xs:integer">
            <xsl:param name="input" as="node()*"/>
            {{body}}
          </xsl:function>
          {{extra}}
          <xsl:template match="/"><xsl:value-of select="{{call}}"/></xsl:template>
        </xsl:stylesheet>
        """;

    [Theory]
    [InlineData("count($input ! descendant::*)")]
    [InlineData("count($input/descendant::*)")]
    [InlineData("count($input/*)")]
    [InlineData("count($input ! outermost(descendant::*))")]
    public void AbsorbingSequenceArgumentsAreConsumedBeforeAdvancing(string expression)
    {
        string sheet = Sheet($"<xsl:sequence select='{expression}'/>");
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet)).CreateTransformer();
            string expected = expression is "count($input/*)" or "count($input ! outermost(descendant::*))" ? "3" : "4";
            using var reader = XmlReader.Create(new StringReader(Input));
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(reader)));
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(XdmNode.Load(new StringReader(Input)))));
        }
    }

    [Fact]
    public void ForwardArgumentsCanPassThroughAFunctionChain()
    {
        string sheet = Sheet("<xsl:sequence select='f:inner($input)'/>", extra: """
            <xsl:function name="f:inner" streamability="absorbing" as="xs:integer">
              <xsl:param name="nodes" as="node()*"/>
              <xsl:sequence select="count($nodes ! descendant::*)"/>
            </xsl:function>
            """);
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal("4", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Theory]
    [InlineData("node()")]
    [InlineData("node()?")]
    public void RecursiveSingletonFunctionsConsumeChildrenIncrementally(string parameterType)
    {
        string sheet = Sheet("<xsl:sequence select='1 + sum($input/* ! f:consume(.))'/>", "doc/row ! f:consume(.)")
            .Replace("as=\"node()*\"", $"as=\"{parameterType}\"");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal("3 3", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Theory]
    [InlineData("node()", "()", true)]
    [InlineData("node()?", "()", false)]
    [InlineData("node()", "doc/row", true)]
    [InlineData("node()?", "doc/row", true)]
    public void SingletonTypeFactsRetainRuntimeCardinalityChecks(string parameterType, string argument, bool error)
    {
        string sheet = Sheet("<xsl:sequence select='count($input/descendant::*)'/>", $"f:consume({argument})")
            .Replace("as=\"node()*\"", $"as=\"{parameterType}\"");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        if (error) Assert.Contains("XPTY0004", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(reader)).Message);
        else Assert.Equal("0", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Fact]
    public void GroundedArgumentsRetainCallerFocusAndCanBeUsedMoreThanOnce()
    {
        string sheet = Sheet("<xsl:param name='offset' as='xs:integer'/><xsl:sequence select='count($input/descendant::*) + $offset + $offset'/>",
            "f:consume(doc/row, position())");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal("6", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Fact]
    public void ARepeatedGroundedArgumentKeepsItsNodeIdentity()
    {
        string sheet = Sheet("<xsl:param name='marker' as='element()'/><xsl:sequence select='count($input/descendant::*) + (if ($marker is $marker) then 1 else 0)'/>",
            "f:consume(doc/row, f:marker())", """
            <xsl:function name="f:marker" as="element()" new-each-time="yes"><marker/></xsl:function>
            """);
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal("5", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Fact]
    public void AnIncorrectArgumentStillRaisesTheFunctionConversionError()
    {
        string sheet = Sheet("<xsl:sequence select='count($input/descendant::*)'/>", "f:consume((1, 2))");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Contains("XPTY0004", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(reader)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbsorbingFunctionsCanConstructTheirResultWhileDispatchingInput(bool templates)
    {
        string body = templates ? "<data><xsl:apply-templates select='$input' mode='rows'/></data>"
            : "<data><xsl:for-each select='$input'><xsl:value-of select='.'/></xsl:for-each></data>";
        string extra = templates ? "<xsl:mode name='rows' streamable='yes'/><xsl:template match='row' mode='rows'><xsl:value-of select='string(value)'/></xsl:template>" : "";
        string sheet = Sheet(body, extra: extra).Replace("as=\"xs:integer\"", "as=\"element(data)\"");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader("<doc><row><value>first</value></row><row><value>second</value></row></doc>"));
        Assert.Equal("firstsecond", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Theory]
    [InlineData("string(doc/row)", "XPTY0004")]
    [InlineData("string((function() {1}, function() {2}))", "XPTY0004")]
    [InlineData("string(function() {1})", "FOTY0014")]
    public void StringConversionKeepsItsCanonicalErrors(string select, string code)
    {
        string sheet = Sheet("<xsl:sequence select='count($input)'/>", select);
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Contains(code, Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(reader)).Message);
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void SavedAssemblyKeepsTheArgumentIterator()
    {
        string sheet = Sheet("<xsl:sequence select='count($input/descendant::*)'/>");
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null,
            "Xsl3.Tests.StreamedFunction", "Saved.Function", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Function", true)!, settings);
        using var reader = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(reader, null, output);
        Assert.Equal("4", output.ToString());
    }
#endif
}
