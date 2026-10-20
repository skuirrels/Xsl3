// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

/// <summary>xsl:evaluate and XsltSettings.EnableEvaluate (docs/plan/p5-evaluate.md).</summary>
public class EvaluateTests
{
    private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

    private static string Run(string body, XsltSettings? settings, string input = "<doc/>")
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:xs='http://www.w3.org/2001/XMLSchema' " +
                            "xmlns:f='urn:f' exclude-result-prefixes='xs f'>" +
                            "<xsl:output method='xml' omit-xml-declaration='yes'/>" + body + "</xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)), settings, null);
        var sw = new StringWriter();
        using (XmlWriter w = XmlWriter.Create(sw, transform.OutputSettings))
        {
            transform.Transform(XmlReader.Create(new StringReader(input)), w);
        }
        return sw.ToString();
    }

    private static XsltSettings Enabled => new() { EnableEvaluate = true };

    [Fact]
    public void DisabledByDefault()
    {
        Assert.False(new XsltSettings().EnableEvaluate);
        Assert.False(XsltSettings.Default.EnableEvaluate);
        Assert.True(XsltSettings.TrustedXslt.EnableEvaluate);
    }

    [Fact]
    public void DisabledRaisesXTDE3175()
    {
        Exception e = Assert.ThrowsAny<Exception>(() => Run("<xsl:template match='/'><o><xsl:evaluate xpath=\"'1 + 1'\"/></o></xsl:template>", null));
        Assert.Contains("XTDE3175", e.ToString());
    }

    [Fact]
    public void DisabledCanBeCaught() =>
        Assert.Equal("<o>XTDE3175</o>", Run("""
            <xsl:template match="/"><o><xsl:try><xsl:evaluate xpath="'1 + 1'"/>
              <xsl:catch errors="*" select="local-name-from-QName($Q{http://www.w3.org/2005/xqt-errors}code)"/></xsl:try></o></xsl:template>
            """, null));

    [Fact]
    public void DisabledUsesFallback() =>
        Assert.Equal("<o>fallback no false</o>", Run("""
            <xsl:template match="/"><o><xsl:evaluate xpath="'1 + 1'"><xsl:fallback>fallback</xsl:fallback></xsl:evaluate>
              <xsl:value-of select="'', system-property('xsl:supports-dynamic-evaluation'), element-available('xsl:evaluate')"/></o></xsl:template>
            """, null));

    [Fact]
    public void EnabledProperties() =>
        Assert.Equal("<o>yes true</o>", Run("""
            <xsl:template match="/"><o><xsl:value-of select="system-property('xsl:supports-dynamic-evaluation'), element-available('xsl:evaluate')"/></o></xsl:template>
            """, Enabled));

    [Fact]
    public void ContextItemParametersAndType() =>
        Assert.Equal("<o>12 3 9</o>", Run("""
            <xsl:template match="/"><o><xsl:variable name="e" select="'xs:integer(sum(*/@n)) + $k'"/>
              <xsl:evaluate xpath="$e" context-item="r" as="xs:integer"><xsl:with-param name="k" select="2"/></xsl:evaluate>
              <xsl:text> </xsl:text>
              <xsl:evaluate xpath="'$a + $b'" with-params="map{xs:QName('a'): 1, xs:QName('b'): 2}"/>
              <xsl:text> </xsl:text>
              <!-- with-params takes precedence over xsl:with-param -->
              <xsl:evaluate xpath="'$a * 3'" with-params="map{xs:QName('a'): 3}"><xsl:with-param name="a" select="100"/></xsl:evaluate></o></xsl:template>
            """, Enabled, "<r><p n='4'/><p n='6'/></r>"));

    [Fact]
    public void StaticErrorIsXTDE3160()
    {
        Exception e = Assert.ThrowsAny<Exception>(() => Run("<xsl:template match='/'><o><xsl:evaluate xpath=\"'1 +'\"/></o></xsl:template>", Enabled));
        Assert.Contains("XTDE3160", e.ToString());
    }

    [Fact]
    public void AbsentContextItemIsXPDY0002()
    {
        Exception e = Assert.ThrowsAny<Exception>(() => Run("<xsl:template match='/'><o><xsl:evaluate xpath=\"'name(.)'\"/></o></xsl:template>", Enabled));
        Assert.Contains("XPDY0002", e.ToString());
    }

    [Fact]
    public void ContextItemOfSeveralItemsIsXTTE3210()
    {
        Exception e = Assert.ThrowsAny<Exception>(() => Run("<xsl:template match='/'><o><xsl:evaluate xpath=\"'.'\" context-item='1 to 2'/></o></xsl:template>", Enabled));
        Assert.Contains("XTTE3210", e.ToString());
    }

    /// <summary>Public stylesheet functions are available to the target expression; private ones aren't (XSLT 3.0 §10.4.1).</summary>
    [Fact]
    public void StylesheetFunctions()
    {
        const string functions = """
            <xsl:function name="f:pub" visibility="public"><xsl:param name="x"/><xsl:sequence select="$x * 10"/></xsl:function>
            <xsl:function name="f:priv"><xsl:param name="x"/><xsl:sequence select="$x"/></xsl:function>
            """;
        Assert.Equal("<o>70</o>", Run(functions + "<xsl:template match='/'><o><xsl:evaluate xpath=\"'f:pub(7)'\"/></o></xsl:template>", Enabled));
        Exception e = Assert.ThrowsAny<Exception>(() => Run(functions + "<xsl:template match='/'><o><xsl:evaluate xpath=\"'f:priv(7)'\"/></o></xsl:template>", Enabled));
        Assert.Contains("XTDE3160", e.ToString());
    }

    /// <summary>Function items cross between the stylesheet and the target expression, both ways.</summary>
    [Fact]
    public void FunctionItemsAcrossUnits() =>
        Assert.Equal("<o>60 11</o>", Run("""
            <xsl:template match="/"><o>
              <xsl:variable name="g" as="function(*)"><xsl:evaluate xpath="'function($x) { $x + $y }'"><xsl:with-param name="y" select="10"/></xsl:evaluate></xsl:variable>
              <xsl:evaluate xpath="'$h(6)'"><xsl:with-param name="h" select="function($x) { $x * 10 }"/></xsl:evaluate>
              <xsl:value-of select="' ', $g(1)" separator=""/></o></xsl:template>
            """, Enabled));

    /// <summary>The same target expression in a loop is compiled once, and differing namespace contexts are kept apart.</summary>
    [Fact]
    public void CachedPerStaticContext() =>
        Assert.Equal("<o>a:1 b:1 a:2 b:2 a:3 b:3</o>", Run("""
            <xsl:template match="/"><o><xsl:variable name="root" select="/"/><xsl:for-each select="1 to 3"><xsl:variable name="i" select="."/>
              <xsl:for-each select="$root//*[@x]"><xsl:if test="position() > 1 or $i > 1"><xsl:text> </xsl:text></xsl:if>
                <xsl:evaluate xpath="'string(p:x) || '':'' || $i'" namespace-context="." context-item="."><xsl:with-param name="i" select="$i"/></xsl:evaluate>
              </xsl:for-each></xsl:for-each></o></xsl:template>
            """, Enabled, "<r><a x='1' xmlns:p='urn:a'><p:x xmlns:p='urn:a'>a</p:x></a><b x='1' xmlns:p='urn:b'><p:x xmlns:p='urn:b'>b</p:x></b></r>"));

    /// <summary>fn:current-dateTime() in the target expression is the transformation's (one execution scope).</summary>
    [Fact]
    public void OneExecutionScope() =>
        Assert.Equal("<o>true</o>", Run("""
            <xsl:template match="/"><o><xsl:variable name="t" as="xs:dateTime"><xsl:evaluate xpath="'current-dateTime()'"/></xsl:variable>
              <xsl:value-of select="$t eq current-dateTime()"/></o></xsl:template>
            """, Enabled));
}
