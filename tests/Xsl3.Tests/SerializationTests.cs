// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Text;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

/// <summary>Serialization parameters and character maps (docs/plan/p5-evaluate.md, M2–M4).</summary>
public class SerializationTests
{
    private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

    // The serialization of the principal result, as the stylesheet's output definition says.
    private static string Run(string body, string input = "<doc/>", string? directory = null)
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'>{body}</xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        string baseUri = directory == null ? "" : new System.Uri(Path.Combine(directory, "s.xsl")).AbsoluteUri;
        transform.Load(XmlReader.Create(new StringReader(stylesheet), null, baseUri), new XsltSettings(), new XmlUrlResolver());
        var bytes = new MemoryStream();
        transform.Transform(new System.Xml.XPath.XPathDocument(new StringReader(input)), null, bytes, null);
        return new UTF8Encoding(false).GetString(bytes.ToArray());
    }

    /// <summary>
    /// 2.0+ results of the xml method write empty elements as &lt;a/&gt;; comments, CDATA sections, processing
    /// instructions and attribute values keep what they contain.
    /// </summary>
    [Fact]
    public void EmptyElementsHaveNoSpaceBeforeTheSlash() =>
        Assert.Equal("<out a=\"x /&gt;\"><e/><e b=\"1\"/><!-- c /> --><?p q /> ?><c><![CDATA[ /> ]]></c>é<e/></out>", Run("""
            <xsl:output omit-xml-declaration="yes" cdata-section-elements="c"/>
            <xsl:template match="/"><out a="x />"><e/><e b="1"/><xsl:comment> c /> </xsl:comment><xsl:processing-instruction name="p">q /> </xsl:processing-instruction><c> /> </c>é<e/></out></xsl:template>
            """));

    /// <summary>The xhtml method keeps &lt;br /&gt; (XHTML's compatibility guidelines), and 1.0 stylesheets keep stock's form.</summary>
    [Fact]
    public void EmptyElementsKeepTheSpaceInXhtmlAndOneZero()
    {
        Assert.Equal("<html xmlns=\"http://www.w3.org/1999/xhtml\"><br /></html>", Run("""
            <xsl:output method="xhtml" indent="no" omit-xml-declaration="yes"/>
            <xsl:template match="/"><html xmlns="http://www.w3.org/1999/xhtml"><br/></html></xsl:template>
            """));
        string stylesheet = $"<xsl:stylesheet version='1.0' xmlns:xsl='{Xsl}'><xsl:output omit-xml-declaration='yes'/><xsl:template match='/'><out><e/></out></xsl:template></xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var bytes = new MemoryStream();
        transform.Transform(new System.Xml.XPath.XPathDocument(new StringReader("<doc/>")), null, bytes, null);
        Assert.Equal("<out><e /></out>", new UTF8Encoding(false).GetString(bytes.ToArray()).TrimStart('\uFEFF'));
    }

    /// <summary>The characters to map are found before normalization; what they map to isn't normalized (Serialization 3.1 §11).</summary>
    [Fact]
    public void CharacterMapBeforeNormalization() =>
        Assert.Equal("<out>ab#ç#çde</out>", Run("""
            <xsl:character-map name="m"><xsl:output-character character="c" string="#ç#"/></xsl:character-map>
            <xsl:output omit-xml-declaration="yes" use-character-maps="m" normalization-form="NFD"/>
            <xsl:template match="/"><out>abcçde</out></xsl:template>
            """));

    /// <summary>Character maps don't apply to CDATA sections, nor to namespace declarations.</summary>
    [Fact]
    public void CharacterMapNotInCDataOrNamespaces() =>
        Assert.Equal("<out xmlns:p=\"urn:a\"><a>AAA</a><b><![CDATA[aaa]]></b></out>", Run("""
            <xsl:character-map name="m"><xsl:output-character character="a" string="A"/></xsl:character-map>
            <xsl:output omit-xml-declaration="yes" use-character-maps="m" cdata-section-elements="b"/>
            <xsl:template match="/"><out xmlns:p="urn:a"><a>aaa</a><b>aaa</b></out></xsl:template>
            """));

    /// <summary>A namespace declaration on a cdata-section-elements element is an attribute value, not CDATA.</summary>
    [Fact]
    public void NamespaceDeclarationOfCDataElement() =>
        Assert.Equal("<p:h xmlns:p=\"urn:p\"><![CDATA[a & b]]></p:h>", Run("""
            <xsl:output omit-xml-declaration="yes" cdata-section-elements="p:h" xmlns:p="urn:p"/>
            <xsl:template match="/"><p:h xmlns:p="urn:p">a &amp; b</p:h></xsl:template>
            """));

    /// <summary>Higher import precedence wins for each parameter, use-character-maps included.</summary>
    [Fact]
    public void OutputDeclarationsMergeByPrecedence()
    {
        string directory = Directory.CreateTempSubdirectory("xsl3-").FullName;
        File.WriteAllText(Path.Combine(directory, "imported.xsl"), $"""
            <xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'>
              <xsl:character-map name="low"><xsl:output-character character="#" string="LOW"/></xsl:character-map>
              <xsl:output use-character-maps="low" indent="yes"/>
            </xsl:stylesheet>
            """);
        Assert.Equal("<out>HIGH</out>", Run("""
            <xsl:import href="imported.xsl"/>
            <xsl:character-map name="high"><xsl:output-character character="#" string="HIGH"/></xsl:character-map>
            <xsl:output omit-xml-declaration="yes" use-character-maps="high" indent="no"/>
            <xsl:template match="/"><out>#</out></xsl:template>
            """, directory: directory));
    }

    /// <summary>A parameter document (Serialization 3.1 §3.1): its method and character map; the declaration's own attributes win.</summary>
    [Fact]
    public void ParameterDocument()
    {
        string directory = Directory.CreateTempSubdirectory("xsl3-").FullName;
        File.WriteAllText(Path.Combine(directory, "params.xml"), """
            <output:serialization-parameters xmlns:output="http://www.w3.org/2010/xslt-xquery-serialization">
              <output:method value="json"/>
              <output:indent value="yes"/>
              <output:use-character-maps><output:character-map character="a" map-string="AAA"/></output:use-character-maps>
            </output:serialization-parameters>
            """);
        Assert.Equal("{\"k\":\"AAA\"}", Run("""
            <xsl:output parameter-document="params.xml" indent="no"/>
            <xsl:template match="/"><xsl:sequence select="map{'k': 'a'}"/></xsl:template>
            """, directory: directory));
    }

    /// <summary>XHTML with include-content-type: the serializer's meta element replaces one the stylesheet writes.</summary>
    [Fact]
    public void XhtmlContentTypeMetaReplaced()
    {
        string result = Run("""
            <xsl:output method="xhtml" omit-xml-declaration="yes" indent="no"/>
            <xsl:template match="/"><html xmlns="http://www.w3.org/1999/xhtml"><head><meta http-equiv="Content-Type" content="text/plain"/><title/></head></html></xsl:template>
            """);
        Assert.Equal(1, result.Split("<meta").Length - 1);
        Assert.Contains("content=\"text/html; charset=UTF-8\"", result);
    }

    /// <summary>disable-output-escaping on a text node returned by a template reaches the result; xsl:copy-of drops it.</summary>
    [Fact]
    public void DisableOutputEscapingThroughSequences() =>
        Assert.Equal("<out>&lt;<b>&amp;lt;</b></out>", Run("""
            <xsl:output omit-xml-declaration="yes"/>
            <xsl:template match="/"><out><xsl:call-template name="t"/><xsl:variable name="v" as="text()"><xsl:call-template name="t"/></xsl:variable><b><xsl:copy-of select="$v"/></b></out></xsl:template>
            <xsl:template name="t" as="text()"><xsl:value-of select="'&amp;lt;'" disable-output-escaping="yes"/></xsl:template>
            """));

    /// <summary>A system identifier with a quotation mark is written in apostrophes; a carriage return in text is &amp;#xD;.</summary>
    [Fact]
    public void DoctypeQuotesAndCarriageReturns() =>
        Assert.Equal("<!DOCTYPE a SYSTEM 'x\"y'><a>&#xD;</a>", Run("""
            <xsl:output omit-xml-declaration="yes" doctype-system='x"y'/>
            <xsl:template match="/"><a><xsl:value-of select="codepoints-to-string(13)"/></a></xsl:template>
            """).Replace(" />", "/>"));

    /// <summary>xsl:result-document in a template called from a variable's content is in temporary output state (XTDE1480).</summary>
    [Fact]
    public void ResultDocumentInTemporaryOutputState()
    {
        System.Exception e = Assert.ThrowsAny<System.Exception>(() => Run("""
            <xsl:template match="/"><xsl:variable name="v"><xsl:call-template name="r"/></xsl:variable><out><xsl:sequence select="$v"/></out></xsl:template>
            <xsl:template name="r"><xsl:result-document href="x.xml"><x/></xsl:result-document></xsl:template>
            """));
        Assert.Contains("XTDE1480: xsl:result-document can't be evaluated while a temporary tree", e.ToString());
    }

    /// <summary>The content of xsl:attribute isn't temporary output state in XSLT 3.0: the result document is written.</summary>
    [Fact]
    public void ResultDocumentFromAttributeContent()
    {
        var written = new System.Collections.Generic.Dictionary<string, StringWriter>();
        Xsl3.Runtime.XmlQueryRuntime.ResultDocumentHandler = (uri, method) => written[uri] = new StringWriter();
        try
        {
            Assert.Equal("<out v=\"r\"/>", Run("""
                <xsl:output omit-xml-declaration="yes"/>
                <xsl:template match="/"><out><xsl:attribute name="v"><xsl:call-template name="r"/></xsl:attribute></out></xsl:template>
                <xsl:template name="r"><xsl:result-document href="x.xml"><x/></xsl:result-document>r</xsl:template>
                """));
            Assert.Single(written);
        }
        finally
        {
            Xsl3.Runtime.XmlQueryRuntime.ResultDocumentHandler = null;
        }
    }

    /// <summary>The adaptive method writes a node with an XML declaration unless omit-xml-declaration="yes" (Serialization 3.1 §10.1.6).</summary>
    [Fact]
    public void AdaptiveNodeDeclaration()
    {
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?><e/>", Run("""
            <xsl:output method="adaptive"/>
            <xsl:template match="/"><e/></xsl:template>
            """));
        Assert.Equal("<e/>", Run("""
            <xsl:output method="adaptive" omit-xml-declaration="yes"/>
            <xsl:template match="/"><e/></xsl:template>
            """));
    }
}
