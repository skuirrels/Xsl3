// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Reflection;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedSequenceParameterTests
{
    private const string Input = "<doc><row><a/><b/></row><row><c><d/></c></row></doc>";
    private static string Sheet(string body, string argument = "doc/row", string type = "node()*", string category = "absorbing", string extra = "") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
          xmlns:f="urn:test" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="f xs">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:function name="f:consume" streamability="{{category}}" as="xs:integer">
            <xsl:param name="input" as="{{type}}"/>
            {{body}}
          </xsl:function>
          {{extra}}
          <xsl:template match="/"><xsl:value-of select="f:consume({{argument}})"/></xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string sheet, bool forward = true, bool saved = false, string input = Input)
    {
        var transform = new XslCompiledTransform();
        var settings = new XsltSettings { StreamedExecution = forward, StreamingBufferCapacity = 1 };
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null, "SequenceParameter", "Saved.SequenceParameter", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.SequenceParameter", true)!);
#endif
        }
        else transform.Load(XmlReader.Create(new StringReader(sheet)), settings, null);
        using var reader = XmlReader.Create(new StringReader(input));
        using var output = new StringWriter();
        transform.Transform(reader, null, output);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
        return output.ToString();
    }

    [Theory]
    [InlineData(false)]
#if NET9_0_OR_GREATER
    [InlineData(true)]
#endif
    public void RecursiveSequenceParametersConsumeEachNodeBeforeItsSuccessor(bool saved)
    {
        string sheet = Sheet("<xsl:sequence select='sum($input ! (1 + f:consume(*)))'/>");
        Assert.Equal("6", Run(sheet, false));
        Assert.Equal("6", Run(sheet, saved: saved));
    }

    [Fact]
    public void MutuallyRecursiveFunctionsKeepTheirCallBoundaries()
    {
        string sheet = Sheet("<xsl:sequence select='sum($input ! (1 + f:inner(*)))'/>", extra: """
          <xsl:function name="f:inner" streamability="absorbing" as="xs:integer">
            <xsl:param name="input" as="node()*"/>
            <xsl:sequence select="sum($input ! (1 + f:consume(*)))"/>
          </xsl:function>
          """);
        Assert.Equal("6", Run(sheet));
    }

    [Theory]
    [InlineData(false)]
#if NET9_0_OR_GREATER
    [InlineData(true)]
#endif
    public void ChildPathsConsumeProvenDisjointParametersBeforeAdvancing(bool saved)
    {
        string sheet = Sheet("<xsl:sequence select='count($input/a/text()[xs:integer(.) gt 0])'/>");
        const string input = "<doc><row><a>1</a></row><row><a>2</a></row></doc>";
        Assert.Equal("2", Run(sheet, false, input: input));
        Assert.Equal("2", Run(sheet, saved: saved, input: input));
    }

    [Fact]
    public void ChildPathPredicatesRetainTheirDynamicErrors()
    {
        string sheet = Sheet("<xsl:sequence select='count($input/a/text()[300 div (xs:integer(.) - 390) = 5])'/>");
        const string input = "<doc><row><a>390</a></row><row><a>450</a></row></doc>";
        Assert.Equal("FOAR0001", Assert.Throws<XdmException>(() => Run(sheet, false, input: input)).Code);
        Assert.Equal("FOAR0001", Assert.Throws<XdmException>(() => Run(sheet, input: input)).Code);
    }

    [Fact]
    public void ForwardedParametersCarryTheirChildOrderContract()
    {
        string sheet = Sheet("<xsl:sequence select='f:inner($input)'/>", extra: """
          <xsl:function name="f:inner" streamability="absorbing" as="xs:integer">
            <xsl:param name="input" as="node()*"/>
            <xsl:sequence select="count($input/a/text())"/>
          </xsl:function>
          """);
        Assert.Equal("2", Run(sheet, input: "<doc><row><a>1</a></row><row><a>2</a></row></doc>"));
    }

    [Fact]
    public void RecursiveChildPathsRetainTheirCallBoundaries()
    {
        string sheet = Sheet("<xsl:sequence select='sum($input/* ! (1 + f:consume(.)))'/>", "doc");
        Assert.Equal("6", Run(sheet, false));
        Assert.Equal("6", Run(sheet));
    }

    [Fact]
    public void LongChildPathsKeepTheirIteratorChain()
    {
        const int depth = 32;
        string path = string.Concat(System.Linq.Enumerable.Repeat("/a", depth));
        string input = "<doc><row>" + string.Concat(System.Linq.Enumerable.Repeat("<a>", depth))
            + "value" + string.Concat(System.Linq.Enumerable.Repeat("</a>", depth)) + "</row></doc>";
        Assert.Equal("1", Run(Sheet("<xsl:sequence select='count($input" + path + "/text())'/>"), input: input));
    }

    [Theory]
    [InlineData("($g/doc/row[2], $g/doc/row[1], $g/doc/row[1])")]
    [InlineData("($g/doc/row[2], $g/doc, $g/doc/row[1], $g/doc/row[1])")]
    public void ArbitraryGroundedArgumentsKeepCanonicalPathOrderAndDeduplication(string argument)
    {
        string sheet = Sheet("<xsl:sequence select='xs:integer(string-join($input/*/text()))'/>", argument,
            extra: "<xsl:variable name='g'><doc><row><a>1</a></row><row><a>2</a></row></doc></xsl:variable>");
        Assert.Equal("12", Run(sheet, false));
        Assert.Equal("12", Run(sheet));
    }

    [Fact]
    public void UnsupportedInspectionAndConsumptionRetainsItsStaticRejection()
    {
        string sheet = Sheet("<xsl:choose><xsl:when test='exists($input)'><xsl:sequence select='count($input/*)'/></xsl:when><xsl:otherwise><xsl:sequence select='0'/></xsl:otherwise></xsl:choose>");
        Assert.Contains("XTSE3430", Assert.Throws<XslLoadException>(() => Run(sheet, false)).Message);
        Assert.Contains("XTSE3430", Assert.Throws<XslLoadException>(() => Run(sheet)).Message);
    }

    [Theory]
    [InlineData("copy-of(doc/row)", Input)]
    [InlineData("doc/@*", "<doc a='1' b='2'/>")]
    [InlineData("doc/row", Input)]
    public void RepeatedParameterUseRetainsTheDeclaredFunctionsStaticRejection(string argument, string input)
    {
        string sheet = Sheet("<xsl:sequence select='count($input) + count($input)'/>", argument);
        Assert.Contains("XTSE3430", Assert.Throws<XslLoadException>(() => Run(sheet, false, input: input)).Message);
        Assert.Contains("XTSE3430", Assert.Throws<XslLoadException>(() => Run(sheet, input: input)).Message);
    }

    [Theory]
    [InlineData("node()*", "()", "0")]
    [InlineData("node()*", "doc/missing", "0")]
    [InlineData("node()+", "doc/row", "2")]
    public void SequenceCardinalityIsCheckedAtTheFunctionBoundary(string type, string argument, string expected)
    {
        string sheet = Sheet("<xsl:sequence select='count($input)'/>", argument, type);
        Assert.Equal(expected, Run(sheet));
    }

    [Theory]
    [InlineData("node()+", "()")]
    [InlineData("node()*", "1")]
    [InlineData("element(other)+", "doc/row")]
    public void IncorrectArgumentsRaiseFunctionConversionErrorsEvenWhenUnused(string type, string argument)
    {
        string sheet = Sheet("<xsl:sequence select='0'/>", argument, type);
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => Run(sheet)).Code);
    }

    [Fact]
    public void AFunctionBodyCannotSwallowItsDeferredArgumentConversionFailure()
    {
        string sheet = Sheet("<xsl:try><xsl:sequence select='count($input)'/><xsl:catch errors='err:XPTY0004' xmlns:err='http://www.w3.org/2005/xqt-errors'><xsl:sequence select='0'/></xsl:catch></xsl:try>", "doc/*", "element(row)*");
        const string input = "<doc><row/><other/></doc>";
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => Run(sheet, false, input: input)).Code);
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => Run(sheet, input: input)).Code);
    }
}
