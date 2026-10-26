// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedFocusTests
{
    [Theory]
    [InlineData(false)]
#if NET10_0_OR_GREATER
    [InlineData(true)]
#endif
    public void PositionDoesNotRequireCollectingChildrenBeforeRecursiveCopy(bool saved)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode streamable="yes"/><xsl:output omit-xml-declaration="yes"/>
              <xsl:template match="*"><xsl:copy><xsl:copy-of select="@*"/>
                <xsl:attribute name="p" select="position()"/><xsl:apply-templates/>
              </xsl:copy></xsl:template>
            </xsl:stylesheet>
            """;
        using var input = XmlReader.Create(new StringReader("<doc id='r'><row id='a'>x</row><row id='b'>y</row></doc>"));
        using var output = new StringWriter();
        var settings = new XsltSettings { StreamedExecution = true };
        if (saved)
        {
#if NET10_0_OR_GREATER
            using var assembly = new MemoryStream();
            new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null,
                "Xsl3.Tests.StreamedFocus", "Saved.Focus", assembly);
            var transform = new XslCompiledTransform();
            transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Focus", true)!, settings);
            transform.Transform(input, null, output);
#else
            throw new System.PlatformNotSupportedException("Saved assembly generation requires .NET 10");
#endif
        }
        else
        {
            var transform = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
            transform.ApplyTemplates(input, output);
        }
        Assert.Equal("<doc id=\"r\" p=\"1\"><row id=\"a\" p=\"1\">x</row><row id=\"b\" p=\"2\">y</row></doc>", output.ToString());
    }

    [Fact]
    public void ActualLocalContextSizesRemainAvailableOnGroundedInputs()
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:template name="t"><xsl:for-each select="doc/row"><xsl:value-of select="concat(position(), '/', last(), ';')"/></xsl:for-each></xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        transform.GlobalContextItem = XdmNode.Load(new StringReader("<doc><row/><row/><row/></doc>"));
        Assert.Equal("1/3;2/3;3/3;", string.Concat(transform.CallTemplate(new QName("t"))));
    }

    [Fact]
    public void SortingCachesArePreservedWhenUnusedSizesAreRemoved()
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode name="s" streamable="yes"/><xsl:output method="text"/>
              <xsl:template name="t"><xsl:apply-templates select="doc/row"><xsl:sort select="@id"/></xsl:apply-templates></xsl:template>
              <xsl:template match="row"><xsl:value-of select="concat(position(), ':', @id, ';')"/></xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        transform.GlobalContextItem = XdmNode.Load(new StringReader("<doc><row id='2'/><row id='1'/></doc>"));
        Assert.Equal("1:1;2:2;", string.Concat(transform.CallTemplate(new QName("t"))));
    }
}
