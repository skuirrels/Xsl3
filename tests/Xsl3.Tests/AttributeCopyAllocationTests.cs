// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class AttributeCopyAllocationTests
{
    public static IEnumerable<object[]> Paths()
    {
        foreach (bool forward in new[] { false, true })
        foreach (string validation in new[] { "preserve", "strip" })
            yield return new object[] { forward, validation };
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void AttributeCopiesRetainNamesValuesAndIndependentIdentityAfterSourceCloses(bool forward, string validation)
    {
        string sheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode streamable="yes"/>
              <xsl:template match="/"><xsl:for-each select="doc/row">
                <xsl:copy-of select="@*" validation="{{validation}}"/>
                <xsl:copy-of select="@*" validation="{{validation}}"/>
              </xsl:for-each></xsl:template>
            </xsl:stylesheet>
            """;
        var compiler = new XsltCompiler { Settings = new() { StreamedExecution = forward } };
        XdmValue result;
        using (var input = XmlReader.Create(new StringReader("<doc xmlns:p='urn:attribute-copy'><row p:key='one &amp; two' empty=''/><row p:key='three' empty=''/></doc>"),
            new XmlReaderSettings(), "urn:attribute-copy:source"))
            result = compiler.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(input);

        Assert.Equal(8, result.Count);
        for (int i = 0; i < result.Count; i++)
        {
            var node = Assert.IsType<XdmNode>(result[i]);
            XPathNavigator position = node.Navigator;
            Assert.Equal(XPathNodeType.Attribute, position.NodeType);
            Assert.Equal(i % 2 == 0 ? "p:key" : "empty", position.Name);
            Assert.Equal(i % 2 == 0 ? "urn:attribute-copy" : "", position.NamespaceURI);
            Assert.Equal(i % 2 != 0 ? "" : i < 4 ? "one & two" : "three", position.Value);
            Assert.Null(position.SchemaInfo);
            Assert.Equal("", position.BaseURI);
            Assert.False(position.MoveToParent());
            Assert.False(position.MoveToFirstChild());
            Assert.False(position.MoveToNext());
            XPathNavigator clone = position.Clone();
            clone.MoveToRoot();
            Assert.True(position.IsSamePosition(clone));
            for (int j = 0; j < i; j++)
                Assert.False(position.IsSamePosition(Assert.IsType<XdmNode>(result[j]).Navigator));
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void CopiesKeepXmlIdNormalizationAndDtdAttributeValues(bool forward, string validation)
    {
        string sheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:template match="/"><xsl:for-each select="doc/row">
                <xsl:variable name="attributes" as="attribute()*"><xsl:copy-of select="@*" validation="{{validation}}"/></xsl:variable>
                <xsl:value-of select="string-join($attributes/string(), '|')"/>
              </xsl:for-each></xsl:template>
            </xsl:stylesheet>
            """;
        var compiler = new XsltCompiler { Settings = new() { StreamedExecution = forward } };
        using var input = XmlReader.Create(new StringReader("<!DOCTYPE doc [<!ATTLIST row key ID #IMPLIED ref IDREF #IMPLIED>]><doc><row xml:id='  normalized  ' key='target' ref='target'/></doc>"),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse });
        using var output = new StringWriter();
        compiler.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(input, output);
        Assert.Equal("normalized|target|target", output.ToString());
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SavedCopiesPreserveParentlessnessAndNamespaceNames(bool forward, bool debug)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:p="urn:attribute-copy">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:template match="/"><xsl:for-each select="doc/row">
                <xsl:variable name="copies" as="attribute()*"><xsl:copy-of select="@*"/></xsl:variable>
                <xsl:value-of select="concat(name($copies[1]), '|', namespace-uri($copies[1]), '|', count($copies/..), '|', $copies[1] is @p:key, '|', string($copies[1]), ';')"/>
              </xsl:for-each></xsl:template>
            </xsl:stylesheet>
            """;
        var settings = new XsltSettings { StreamedExecution = forward };
        using var image = new MemoryStream();
        using (var source = XmlReader.Create(new StringReader(sheet)))
            new XslCompiledTransform(debug).CompileToAssembly(source, settings, null,
                "AttributeCopy" + Guid.NewGuid().ToString("N"), "Saved.AttributeCopy", image);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(image.ToArray()).GetType("Saved.AttributeCopy", true)!, settings);
        using var input = XmlReader.Create(new StringReader("<doc xmlns:p='urn:attribute-copy'><row p:key='first'/><row p:key='second'/></doc>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("p:key|urn:attribute-copy|0|false|first;p:key|urn:attribute-copy|0|false|second;", output.ToString());
    }
#endif
}
