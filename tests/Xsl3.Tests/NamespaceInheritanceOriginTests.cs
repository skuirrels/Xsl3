// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class NamespaceInheritanceOriginTests
{
    [Theory]
    [InlineData("<xsl:element name='middle' namespace='urn:middle' inherit-namespaces='no'><xsl:element name='leaf'/></xsl:element>", "true|false")]
    [InlineData("<xsl:element name='a:middle' namespace='urn:shadow' inherit-namespaces='no'><xsl:element name='leaf'/></xsl:element>", "false|false")]
    [InlineData("<xsl:element name='a:middle' namespace='urn:outer' inherit-namespaces='no'><xsl:element name='leaf'/></xsl:element>", "false|false")]
    [InlineData("<xsl:element name='middle' inherit-namespaces='no'><xsl:namespace name='a' select='\"urn:outer\"'/><xsl:element name='leaf'/></xsl:element>", "false|false")]
    public void AnOuterConstructorsNamespacesCrossAnInnerNoInheritBoundaryUnlessShadowed(string middle, string expected)
    {
        string stylesheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:template match="/">
                <xsl:variable name="tree"><out xmlns:a="urn:outer">{{middle}}</out></xsl:variable>
                <xsl:variable name="copy" select="copy-of($tree)"/>
                <xsl:value-of select="for $r in ($tree, $copy) return exists($r/out/*/*/namespace::*[. = 'urn:outer']) || '|' || exists($r/out/*/*/namespace::*[. = ('urn:middle','urn:shadow')])" separator=";"/>
              </xsl:template>
            </xsl:stylesheet>
            """;
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XslCompiledTransform();
            transform.Load(XmlReader.Create(new StringReader(stylesheet)), new XsltSettings { StreamedExecution = forward }, null);
            using var input = XmlReader.Create(new StringReader("<doc/>"));
            using var output = new StringWriter();
            transform.Transform(input, null, output);
            Assert.Equal(expected + ";" + expected, output.ToString());
        }
    }
}
