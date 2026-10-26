// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Reflection;
using System.Security;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedNodeSetTests
{
    private const string Input = "<doc><row><a>1</a><b>2</b></row><row><a>3</a><b>4</b></row></doc>";
    private const string Extra = "<xsl:variable name='extra' as='element(extra)'><extra><other><a>x</a></other><other><a>y</a></other></extra></xsl:variable>";

    [Theory]
    [InlineData("doc/row/a union $extra/*/a", "1|3|x|y", "x|y|1|3")]
    [InlineData("$extra/*/a | doc/row/a", "1|3|x|y", "x|y|1|3")]
    [InlineData("doc/row/a except $extra/*/a", "1|3", "1|3")]
    [InlineData("doc/row/a intersect $extra/*/a", "", "")]
    [InlineData("$extra/*/a except doc/row/a", "x|y", "x|y")]
    [InlineData("$extra/*/a intersect doc/row/a", "", "")]
    [InlineData("(doc/row | $extra/*)/a", "1|3|x|y", "x|y|1|3")]
    [InlineData("(doc/row except $extra/*)/a", "1|3", "1|3")]
    [InlineData("(doc/row intersect $extra/*)/a", "", "")]
    [InlineData("((doc/row | $extra/*) except $extra/*)/a", "1|3", "1|3")]
    [InlineData("let $g := reverse($extra/*) return (doc/row | ($g,$g))/a", "1|3|x|y", "x|y|1|3")]
    [InlineData("let $g := copy-of(doc/row) return ($g | $g)/a", "1|3", "1|3")]
    public void GroundedAndStreamedPopulationsKeepCanonicalSets(string expression, string first, string second)
    {
        Assert.Contains(Run(expression, false), new[] { first, second });
        Assert.Contains(Run(expression, true), new[] { first, second });
    }

    [Theory]
    [InlineData("doc/row ! (a | b)", "1|2|3|4")]
    [InlineData("doc/row ! (* except b)", "1|3")]
    [InlineData("doc/row ! (* intersect a)", "1|3")]
    [InlineData("doc/row ! ((a | b) except b)", "1|3")]
    [InlineData("doc/row ! (a intersect a)", "1|3")]
    [InlineData("doc/row ! count((a | a) intersect *)", "1|1")]
    [InlineData("doc/row ! (* except b[local-name()='b'])", "1|3")]
    [InlineData("doc/row ! (* intersect a[not(@skip)])", "1|3")]
    [InlineData("doc//a except doc/row[not(@skip)]/*", "")]
    [InlineData("doc//a intersect doc/row[not(@skip)]/*", "1|3")]
    [InlineData("doc//a union doc/row/b", "1|2|3|4")]
    [InlineData("(doc//row except doc/row[not(@skip)])/a", "")]
    [InlineData("(doc//row intersect doc/row[not(@skip)])/a", "1|3")]
    [InlineData("doc/row ! (descendant::a intersect child::a)", "1|3")]
    [InlineData("string-join(doc/row ! (child::text() | a/text()), '|')", "1|3")]
    [InlineData("doc/row ! (@* except @missing)", "")]
    [InlineData("doc/row/self::row/a except doc/row/b", "1|3")]
    [InlineData("count(doc/row/descendant-or-self::row except doc/missing)", "2")]
    [InlineData("doc/row[1]/a except doc/row/b", "1")]
    [InlineData("doc/row[2]/a | doc/row[1]/b", "2|3")]
    [InlineData("doc/row[position()=1 to 2]/* intersect doc/row[2]/*", "3|4")]
    [InlineData("doc/row/*[self::a or self::b][2] except doc/row/a", "2|4")]
    [InlineData("doc/row/*[position() mod 2 = 0] except doc/row/a", "2|4")]
    [InlineData("doc/row ! (a[1] except b)", "1|3")]
    [InlineData("doc/row/*[position() gt 1][1] except doc/row/a", "2|4")]
    [InlineData("doc/row/self::row[1]/a except doc/row/b", "1|3")]
    [InlineData("doc/row[0]/a union doc/row[2]/a", "3")]
    [InlineData("doc/row[1.5]/a union doc/row[2]/a", "3")]
    [InlineData("doc/row[xs:double('NaN')]/a union doc/row[2]/a", "3")]
    [InlineData("count(doc/row ! (node()[1] except missing))", "2")]
    [InlineData("doc/row[1] | doc/row[2]", "12|34")]
    [InlineData("doc/row/self::row[2] | doc/row/self::row[1]", "12|34")]
    [InlineData("doc/row[2] except doc/row[1]", "34")]
    public void CommonSelectionsUseOneScan(string expression, string expected)
    {
        Assert.Equal(expected, Run(expression, false));
        Assert.Equal(expected, Run(expression, true));
    }

    [Theory]
    [InlineData("1 | doc/row", "XPTY0004")]
    [InlineData("(doc/row, 1) | $extra/*", "XPTY0004")]
    [InlineData("doc/missing intersect ($extra/*, 1)", "XPTY0004")]
    [InlineData("($extra/*, 1) except doc/missing", "XPTY0004")]
    [InlineData("(1 | doc/row)/missing", "XPTY0004")]
    [InlineData("doc/row | error(QName('', 'TEST0001'))", "TEST0001")]
    public void SetErrorsKeepTheirCanonicalCode(string expression, string code)
    {
        Assert.Equal(code, Assert.Throws<XdmException>(() => Run(expression, false)).Code);
        Assert.Equal(code, Assert.Throws<XdmException>(() => Run(expression, true)).Code);
    }

    [Fact]
    public void TheFirstStreamedHeaderReachesItsConsumerBeforeItsContentIsRead()
    {
        var transform = Load("<xsl:for-each select='doc/row | $extra/*'><xsl:message select='local-name(.)'/></xsl:for-each>", true);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        int rows = 0;
        var arguments = new XsltArgumentList();
        arguments.XsltMessageEncountered += (_, message) => {
            if (message.Message != "row" || rows++ != 0) return;
            Assert.Equal(XmlNodeType.Element, input.NodeType);
            Assert.Equal("row", input.LocalName);
            Assert.False(input.EOF);
        };
        transform.Transform(input, arguments, output);
        Assert.Equal(2, rows);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void GroundedOperandExpressionsAreEvaluatedOnce()
    {
        int calls = 0;
        var arguments = new XsltArgumentList { TraceHandler = (label, _) => { Assert.Equal("extra", label); calls++; } };
        string result = Run("(doc/row | trace($extra/*, 'extra'))/a", true, arguments);
        Assert.Contains(result, new[] { "1|3|x|y", "x|y|1|3" });
        Assert.Equal(1, calls);
    }

    [Fact]
    public void PrefixPredicatesRunOnceEvenWithSeveralOrNoMatchingDescendants()
    {
        const string xml = "<doc><row><a>1</a><a>2</a><b>3</b></row><row><b>4</b></row></doc>";
        const string expression = "doc/row[trace(true(), 'prefix')]/a except doc/row/b";
        foreach (bool forward in new[] { false, true })
        {
            int calls = 0;
            var arguments = new XsltArgumentList { TraceHandler = (label, value) => { Assert.Equal("prefix", label); Assert.Equal("true", value); calls++; } };
            Assert.Equal("1|2", Run(expression, forward, arguments, xml: xml));
            Assert.Equal(2, calls);
        }
    }

    [Fact]
    public void AFailingPrefixPredicateIsCheckedWithoutMatchingDescendants()
    {
        const string expression = "doc/row[xs:integer(@id) gt 0]/missing except doc/row/b";
        foreach (bool forward in new[] { false, true })
            Assert.Equal("FORG0001", Assert.Throws<XdmException>(() => Run(expression, forward, xml: "<doc><row id='bad'><b>1</b></row></doc>")).Code);
    }

    [Fact]
    public void BothSharedSelectionsEvaluateTheirEligiblePredicates()
    {
        foreach (bool forward in new[] { false, true })
        {
            int left = 0, right = 0;
            var arguments = new XsltArgumentList { TraceHandler = (label, _) => { if (label == "left") left++; else if (label == "right") right++; else Assert.Fail(label); } };
            Assert.Equal("12|34", Run("doc/row[trace(true(), 'left')] | doc/row[trace(true(), 'right')]", forward, arguments));
            Assert.Equal(2, left); Assert.Equal(2, right);
        }
    }

    [Fact]
    public void ASharedUnionDoesNotHideTheOtherSelectionsPredicateError()
    {
        const string expression = "doc/row[true()] | doc/row[xs:integer(@id) gt 0]";
        foreach (bool forward in new[] { false, true })
            Assert.Equal("FORG0001", Assert.Throws<XdmException>(() => Run(expression, forward, xml: "<doc><row id='bad'><a>1</a></row></doc>")).Code);
    }

    [Fact]
    public void PositionalPredicateErrorsAreNotHiddenByUnionMembership()
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal("FORG0006", Assert.Throws<XdmException>(() => Run("doc/row | doc/row[(1,2)]", forward)).Code);
    }

    [Fact]
    public void PositionDiagnosticsUseTheirOwnPredicateStage()
    {
        foreach (bool forward in new[] { false, true })
        {
            var positions = new System.Collections.Generic.List<string>();
            var arguments = new XsltArgumentList { TraceHandler = (_, value) => positions.Add(value) };
            Assert.Equal("2|4", Run("doc/row/*[position() gt 1][trace(position(), 'stage')] except doc/row/a", forward, arguments));
            Assert.Equal(new[] { "1", "1" }, positions);
        }
    }

    [Fact]
    public void PositionalWholeElementSelectionsDeliverTheirFirstHeaderBeforeContent()
    {
        var transform = Load("<xsl:for-each select='doc/row[1] | doc/row[2]'><xsl:message select='name()'/><xsl:value-of select='.'/></xsl:for-each>", true);
        using var reader = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter(); int calls = 0;
        var arguments = new XsltArgumentList();
        arguments.XsltMessageEncountered += (_, _) => {
            if (calls++ == 0) { Assert.Equal(XmlNodeType.Element, reader.NodeType); Assert.Equal("row", reader.LocalName); }
        };
        transform.Transform(reader, arguments, output);
        Assert.Equal("1234", output.ToString()); Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("doc/row/@*[1] | doc/row/@*[2]", "1|2|3|4")]
    [InlineData("doc/row ! (@*[2] except @*[1])", "2|4")]
    [InlineData("doc/row ! (@*[position() gt 1][1] intersect @b)", "2|4")]
    public void AttributePositionsCountEachParentsFilteredPopulation(string expression, string expected)
    {
        const string xml = "<doc><row a='1' b='2'/><row a='3' b='4'/></doc>";
        Assert.Equal(expected, Run(expression, false, xml: xml));
        Assert.Equal(expected, Run(expression, true, xml: xml));
    }

    [Fact]
    public void StreamingFunctionParametersAreNotMistakenForGroundedVariables()
    {
        string declarations = """
            <xsl:function name="f:values" streamability="absorbing"><xsl:param name="nodes" as="node()*"/>
            <xsl:sequence select="string-join(($nodes except $extra/*)/a, '|')"/></xsl:function>
            """;
        var transform = Load("<xsl:value-of select='f:values(doc/row)'/>", true, declarations: declarations);
        using var input = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("1|3", output.ToString());
    }

    [Fact]
    public void TheExecutableApiUsesTheNativeSetCursor()
    {
        string sheet = Sheet("<xsl:value-of select='(doc/row[1] | doc/row[2])/a' separator='|'/>");
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader(Input));
        Assert.Equal("1|3", string.Concat(transformer.ApplyTemplates(input)));
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData("(doc/row except $extra/*)/a", "1|3")]
    [InlineData("doc/row ! (* intersect a)", "1|3")]
    [InlineData("doc/row[2]/a | doc/row[1]/b", "2|3")]
    public void NativeSetsSurviveSavedCompilation(string expression, string expected) => Assert.Equal(expected, Run(expression, true, saved: true));
#endif

    private static string Sheet(string body, string declarations = "") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:f="urn:test:functions" xmlns:xs="http://www.w3.org/2001/XMLSchema">
        <xsl:mode streamable="yes"/><xsl:output method="text"/>
        {{Extra}}{{declarations}}<xsl:template match="/">{{body}}</xsl:template></xsl:stylesheet>
        """;

    private static XslCompiledTransform Load(string body, bool forward, bool saved = false, string declarations = "")
    {
        var transform = new XslCompiledTransform();
        using var stylesheet = XmlReader.Create(new StringReader(Sheet(body, declarations)));
        var settings = new XsltSettings { StreamedExecution = forward, StreamingBufferCapacity = 1 };
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(stylesheet, settings, null, "Sets", "Saved.Sets", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.Sets", true)!);
#endif
        }
        else transform.Load(stylesheet, settings, null);
        return transform;
    }

    private static string Run(string expression, bool forward, XsltArgumentList? arguments = null, bool saved = false, string xml = Input)
    {
        var transform = Load($"<xsl:value-of select=\"{SecurityElement.Escape(expression)}\" separator='|'/>", forward, saved);
        using var input = XmlReader.Create(new StringReader(xml)); using var output = new StringWriter();
        try { transform.Transform(input, arguments, output); return output.ToString(); }
        finally { Assert.NotEqual(ReadState.Closed, input.ReadState); }
    }
}
