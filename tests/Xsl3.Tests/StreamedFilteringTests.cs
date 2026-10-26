// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedFilteringTests
{
    private static string Sheet(string expression) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/"><xsl:value-of select="{{expression}}" separator="|"/></xsl:template>
        </xsl:stylesheet>
        """;

    [Theory]
    [InlineData("doc/record[(@id + 1) mod 3 = 0]/@id", "2|5")]
    [InlineData("doc/record[(number(@id) mod 5) eq 0]/@id", "5|10")]
    [InlineData("doc/record[(xs:integer(@id) idiv 3) = 1]/@id", "5")]
    [InlineData("doc/record[(xs:decimal(@id) div 2) gt 2]/@id", "5|10")]
    [InlineData("doc/record[@id = '5']/@id", "5")]
    [InlineData("concat('x', string(5))", "x5")]
    [InlineData("codepoints-to-string(65)", "A")]
    [InlineData("doc/record[(@missing + 1) = 0]/@id", "")]
    [InlineData("doc/record[(@id mod 0) = 0]/@id", "")]
    [InlineData("(xs:date('2000-01-01') + xs:dayTimeDuration('P1D')) eq xs:date('2000-01-02')", "true")]
    public void AtomicArithmeticKeepsItsTypesAndEmptySequenceBehavior(string expression, string expected)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression))).CreateTransformer();
            using var input = XmlReader.Create(new StringReader("<doc><record id='1'/><record id='2'/><record id='5'/><record id='10'/></doc>"));
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(input)));
        }
    }

    [Theory]
    [InlineData("doc/record[(xs:integer(@id) idiv 0) = 1]/@id", "FOAR0001")]
    [InlineData("('not a number' + 1) eq 2", "XPTY0004")]
    public void ArithmeticErrorsAreStillReported(string expression, string code)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression))).CreateTransformer();
            using var input = XmlReader.Create(new StringReader("<doc><record id='1'/></doc>"));
            Assert.Contains(code, Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(input)).Message);
        }
    }
}
