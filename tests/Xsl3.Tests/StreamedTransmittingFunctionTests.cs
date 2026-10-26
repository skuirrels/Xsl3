// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedTransmittingFunctionTests
{
    private static string ChildrenSheet(string resultType) => $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:f="urn:test:functions">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:function name="f:children" streamability="shallow-descent" as="{{resultType}}">
                <xsl:param name="input" as="element()"/><xsl:sequence select="$input/*"/>
              </xsl:function>
              <xsl:template match="/"><xsl:for-each select="doc/row"><xsl:for-each select="f:children(.)">
                <xsl:value-of select="."/>
              </xsl:for-each></xsl:for-each></xsl:template>
            </xsl:stylesheet>
            """;

    [Theory]
    [InlineData("node()*")]
    [InlineData("element()*")]
    [InlineData("element(value)*")]
    public void ShallowDescentResultsAreConsumedBeforeTheNextChild(string resultType)
    {
        string sheet = ChildrenSheet(resultType);
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader("<doc><row><value>first</value><value>second</value></row><row/><row><value>third</value></row></doc>"));
        Assert.Equal("firstsecondthird", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Theory]
    [InlineData("element(value)*")]
    [InlineData("attribute()*")]
    public void IncrementalResultChecksRetainTheirTypeErrors(string resultType)
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(ChildrenSheet(resultType))).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader("<doc><row><wrong>unexpected</wrong></row></doc>"));
        Assert.Contains("XTTE0780", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(reader)).Message);
    }

    [Theory]
    [InlineData("node()", "$input")]
    [InlineData("node()?", "$input[@keep]")]
    public void FilterResultsCanBeNavigatedBeforeTheNextInput(string resultType, string body)
    {
        string sheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:f="urn:test:functions">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:function name="f:keep" streamability="filter" as="{{resultType}}">
                <xsl:param name="input" as="node()"/><xsl:sequence select="{{body}}"/>
              </xsl:function>
              <xsl:template match="/"><xsl:value-of select="doc/row ! f:keep(.) / value"/></xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader("<doc><row keep='yes'><value>first</value></row><row keep='yes'><value>second</value></row></doc>"));
        Assert.Equal("first second", string.Concat(transform.ApplyTemplates(reader)));
    }

    private static string SelectedChildrenSheet(string parameter = "element()", string result = "node()*", string extra = "") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:f="urn:test:functions">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:function name="f:children" streamability="shallow-descent" as="{{result}}">
            <xsl:param name="input" as="{{parameter}}"/>
            <xsl:sequence select="$input/*[position() mod 2 = 1]"/>{{extra}}
          </xsl:function>
          <xsl:template match="/"><xsl:value-of select="f:children(doc/row)" separator="|"/></xsl:template>
        </xsl:stylesheet>
        """;

    [Theory]
    [InlineData("node()*")]
    [InlineData("element(value)*")]
    public void APathSelectedSingletonCanTransmitChildrenBeforeCheckingForAnotherInput(string result)
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }
            .Compile(new StringReader(SelectedChildrenSheet(result: result))).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><row><value>first</value><value>second</value><value>third</value></row></doc>"));
        Assert.Equal("first|third", string.Concat(transform.ApplyTemplates(input)));
    }

    [Theory]
    [InlineData("element()", "<doc/>", "XPTY0004")]
    [InlineData("element()?", "<doc/>", null)]
    [InlineData("element()", "<doc><row/><row/></doc>", "XPTY0004")]
    [InlineData("element()?", "<doc><row/><row/></doc>", "XPTY0004")]
    [InlineData("element(other)", "<doc><row/></doc>", "XPTY0004")]
    public void IncrementalArgumentChecksPreserveEmptyMultipleAndNamedNodeContracts(string parameter, string xml, string? code)
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }
            .Compile(new StringReader(SelectedChildrenSheet(parameter))).CreateTransformer();
        using var input = XmlReader.Create(new StringReader(xml));
        if (code == null) Assert.Empty(string.Concat(transform.ApplyTemplates(input)));
        else Assert.Contains(code, Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(input)).Message);
    }

    [Fact]
    public void AnOptionalEmptyArgumentStillEvaluatesTheBody()
    {
        string sheet = SelectedChildrenSheet("element()?", extra: "<made>empty</made>");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        Assert.Equal("empty", string.Concat(transform.ApplyTemplates(input)));
    }

    [Fact]
    public void CardinalityStateIsLocalToEachInvocation()
    {
        string sheet = SelectedChildrenSheet().Replace("f:children(doc/row)", "doc/group ! f:children(row)");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><group><row><value>first</value></row></group><group><row><value>second</value></row></group></doc>"));
        Assert.Equal("first|second", string.Concat(transform.ApplyTemplates(input)));
    }

    [Fact]
    public void ATransmittedResultStillHasItsDeclaredName()
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }
            .Compile(new StringReader(SelectedChildrenSheet(result: "element(value)*"))).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><row><wrong>unexpected</wrong></row></doc>"));
        Assert.Contains("XTTE0780", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(input)).Message);
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void SavedAssembliesKeepIncrementalTransmittingArgumentChecks()
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(SelectedChildrenSheet())), settings, null,
            "Xsl3.Tests.SelectedChildren", "Saved.SelectedChildren", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.SelectedChildren", true)!, settings);
        using var input = XmlReader.Create(new StringReader("<doc><row><value>first</value><value>second</value><value>third</value></row></doc>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("first|third", output.ToString());
    }
#endif
}
