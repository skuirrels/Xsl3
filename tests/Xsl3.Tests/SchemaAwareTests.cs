// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

/// <summary>P6, schema-awareness (docs/plan/p6-schema.md): xsl:import-schema, schema types, validation.</summary>
public class SchemaAwareTests
{
    private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

    private const string Schema =
        "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema' targetNamespace='urn:s' xmlns:s='urn:s' elementFormDefault='qualified'>" +
        "  <xs:simpleType name='size'><xs:restriction base='xs:integer'><xs:minInclusive value='1'/><xs:maxInclusive value='10'/></xs:restriction></xs:simpleType>" +
        "  <xs:simpleType name='sizes'><xs:list itemType='s:size'/></xs:simpleType>" +
        "  <xs:element name='e' type='s:size'/>" +
        "  <xs:element name='list' type='s:sizes'/>" +
        "  <xs:element name='item'><xs:complexType><xs:sequence/><xs:attribute name='n' type='xs:integer'/><xs:attribute name='d' type='xs:string' default='x'/></xs:complexType></xs:element>" +
        "</xs:schema>";

    private static string Run(string body, bool schemaAware = true)
    {
        var compiler = new XsltCompiler { Settings = new XsltSettings { SchemaAware = schemaAware } };
        XsltExecutable executable = compiler.Compile(new StringReader(
            $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:s='urn:s' exclude-result-prefixes='xs s'>" +
            "<xsl:output omit-xml-declaration='yes'/>" +
            $"<xsl:import-schema namespace='urn:s'>{Schema}</xsl:import-schema>" + body + "</xsl:stylesheet>"));
        var text = new StringWriter();
        executable.CreateTransformer().CallTemplate(null, text);
        return text.ToString();
    }

    [Fact]
    public void ImportSchemaNeedsASchemaAwareProcessor()
    {
        XsltStaticException e = Assert.Throws<XsltStaticException>(() => Run("<xsl:template name='xsl:initial-template'/>", schemaAware: false));
        Assert.Contains("XTSE1650", e.Message);
    }

    [Fact]
    public void SchemaTypesCastAndMatch()
    {
        Assert.Equal("<out>5 true false s:size</out>", Run(
            "<xsl:template name='xsl:initial-template'><out>" +
            "<xsl:value-of select=\"s:size('5'), 5 cast as s:size instance of s:size, 11 castable as s:size\"/><xsl:text> </xsl:text>" +
            "<xsl:value-of select=\"if (s:size(3) instance of xs:integer) then 's:size' else ''\"/></out></xsl:template>"));
        XsltDynamicException e = Assert.Throws<XsltDynamicException>(() => Run(
            "<xsl:template name='xsl:initial-template'><xsl:sequence select=\"s:size('0')\"/></xsl:template>"));
        Assert.Equal("FORG0001", e.Code.LocalName);
    }

    [Fact]
    public void ListTypesCastToSequences()
    {
        Assert.Equal("3", Run("<xsl:template name='xsl:initial-template'><xsl:value-of select=\"count(s:sizes('1 2 3'))\"/></xsl:template>"));
    }

    [Fact]
    public void ValidationAnnotatesConstructedElements()
    {
        Assert.Equal("<out>true true 7 true</out>", Run(
            "<xsl:template name='xsl:initial-template'>" +
            "<xsl:variable name='e' as='element()'><s:e xsl:validation='strict'>7</s:e></xsl:variable>" +
            "<xsl:variable name='l' as='element()'><s:list xsl:validation='strict'>1 2</s:list></xsl:variable>" +
            "<out><xsl:value-of select=\"$e instance of element(*, s:size), $e instance of schema-element(s:e), data($e) + 0, count(data($l)) = 2\"/></out>" +
            "</xsl:template>"));
    }

    [Fact]
    public void ValidationAddsDefaultAttributes()
    {
        Assert.Equal("<out>x true</out>", Run(
            "<xsl:template name='xsl:initial-template'>" +
            "<xsl:variable name='i' as='element()'><s:item n='3' xsl:validation='strict'/></xsl:variable>" +
            "<out><xsl:value-of select=\"$i/@d, $i/@n instance of attribute(*, xs:integer)\"/></out>" +
            "</xsl:template>"));
    }

    [Fact]
    public void InvalidContentIsATypeError()
    {
        XsltDynamicException e = Assert.Throws<XsltDynamicException>(() => Run(
            "<xsl:template name='xsl:initial-template'><out><s:e xsl:validation='strict'>11</s:e></out></xsl:template>"));
        Assert.Equal("XTTE1510", e.Code.LocalName);
        e = Assert.Throws<XsltDynamicException>(() => Run(
            "<xsl:template name='xsl:initial-template'><out><xsl:element name='n' type='s:size'>zero</xsl:element></out></xsl:template>"));
        Assert.Equal("XTTE1540", e.Code.LocalName);
    }

    [Fact]
    public void ValidatedInputIsTyped()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add("urn:s", XmlReader.Create(new StringReader(Schema)));
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas };
        using XmlReader reader = XmlReader.Create(new StringReader("<list xmlns='urn:s'>1 2 3</list>"), settings);
        var doc = Tree.CompactDocument.Load(reader);
        var compiler = new XsltCompiler { Settings = new XsltSettings { SchemaAware = true } };
        XsltExecutable executable = compiler.Compile(new StringReader(
            $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:xs='http://www.w3.org/2001/XMLSchema' exclude-result-prefixes='xs'>" +
            "<xsl:output omit-xml-declaration='yes'/>" +
            "<xsl:template match='/'><out><xsl:value-of select='count(data(*)), sum(data(*)), * instance of element(*, xs:untyped)'/></out></xsl:template>" +
            "</xsl:stylesheet>"));
        var text = new StringWriter();
        executable.CreateTransformer().ApplyTemplates(new XdmNode(doc.CreateNavigator()), text);
        Assert.Equal("<out>3 6 false</out>", text.ToString());
    }
}
