// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedSequenceWindowTests
{
    private static string Sheet(string select) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/"><xsl:value-of select="{{select}}" separator=""/></xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string select, string xml, bool forward)
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(Sheet(select))).CreateTransformer();
        using var input = XmlReader.Create(new StringReader(xml));
        return string.Concat(transform.ApplyTemplates(input));
    }

    [Theory]
    [InlineData("head(doc/row)", "a")]
    [InlineData("tail(doc/row)", "bcd")]
    [InlineData("subsequence(doc/row, 2)", "bcd")]
    [InlineData("subsequence(doc/row, 2, 2)", "bc")]
    [InlineData("subsequence(doc/row, 1.5, 1.5)", "bc")]
    [InlineData("subsequence(doc/row, 0, 3)", "ab")]
    [InlineData("subsequence(doc/row, -1, 3)", "a")]
    [InlineData("subsequence(doc/row, 1, 0)", "")]
    [InlineData("subsequence(doc/row, 1, -2)", "")]
    [InlineData("subsequence(doc/row, xs:double('NaN'))", "")]
    [InlineData("subsequence(doc/row, xs:double('INF'))", "")]
    [InlineData("subsequence(doc/row, xs:double('-INF'))", "abcd")]
    [InlineData("subsequence(doc/row, xs:double('-INF'), xs:double('INF'))", "")]
    [InlineData("subsequence(doc/row, 2, xs:double('INF'))", "bcd")]
    [InlineData("subsequence(doc/row, 1, xs:double('NaN'))", "")]
    [InlineData("subsequence(tail(doc/row), 2, 1)", "c")]
    public void WindowBoundsMatchOrdinarySequenceSemantics(string select, string expected)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal(expected, Run(select, "<doc><row>a</row><row>b</row><row>c</row><row>d</row></doc>", forward));
    }

    [Theory]
    [InlineData("head(doc/row ! (if (@bad) then error() else .))")]
    [InlineData("subsequence(doc/row ! (if (@bad) then error() else .), 1, 1)")]
    public void ACompletedWindowDoesNotFetchAnotherItem(string select) =>
        Assert.Equal("first", Run(select, "<doc><row>first</row><row bad='yes'/></doc>", true));

    [Theory]
    [InlineData("head(doc/missing)")]
    [InlineData("tail(doc/missing)")]
    public void EmptyInputsRemainEmpty(string select) => Assert.Empty(Run(select, "<doc/>", true));

    [Theory]
    [InlineData("subsequence(doc/row, ())")]
    [InlineData("subsequence(doc/row, '2')")]
    [InlineData("subsequence(doc/row, 1, ())")]
    public void BoundsKeepCanonicalArgumentTypeErrors(string select) =>
        Assert.Contains("XPTY0004", Assert.Throws<XsltDynamicException>(() => Run(select, "<doc><row>a</row></doc>", true)).Message);

    [Fact]
    public void WindowStateIsLocalToEachInvocation() =>
        Assert.Equal("bd", Run("doc/group ! subsequence(row, 2, 1)", "<doc><group><row>a</row><row>b</row></group><group><row>c</row><row>d</row></group></doc>", true));

    [Fact]
    public void AHeadResultCanBeNavigatedWhileCurrent() =>
        Assert.Equal("first", Run("head(doc/row)/value/string()", "<doc><row><value>first</value></row><row><value>second</value></row></doc>", true));
}
