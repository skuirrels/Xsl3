// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Xsl3.Xdm;
using Xsl3.W3C.Qt3;
using Catalog = Xsl3.W3C.Xslt30.Catalog;
using Runner = Xsl3.W3C.Xslt30.Runner;
using Xunit;

namespace Xsl3.W3C;

[Collection("Streaming harness settings")]
public class ResultTreeHarnessTests
{
    [Fact]
    public void TreeReplayIsCachedAndDoesNotReplaceOriginalObservations()
    {
        var messages = new List<string> { "original" };
        using var original = new StringWriter();
        using var replacement = new StringWriter();
        var secondary = new Dictionary<string, StringWriter> { ["original"] = original };
        int calls = 0;
        var tree = Runner.ReplayResultTree(writer => {
            calls++;
            messages.Add("replay");
            secondary.Clear();
            secondary.Add("replay", replacement);
            writer.WriteStartElement("out"); writer.WriteString("value"); writer.WriteEndElement();
        }, messages, secondary);
        var first = tree();
        var second = tree();
        Assert.Equal(1, calls);
        Assert.True(first.IsSamePosition(second));
        Assert.True(first.MoveToFirstChild());
        Assert.Equal(System.Xml.XPath.XPathNodeType.Root, second.NodeType);
        Assert.Equal(new[] { "original" }, messages);
        Assert.Equal("original", Assert.Single(secondary).Key);
        Assert.Same(original, secondary["original"]);
    }

    [Fact]
    public void ReplayFailuresAreRetainedAndRestoreOriginalObservations()
    {
        var messages = new List<string> { "original" };
        using var original = new StringWriter();
        var secondary = new Dictionary<string, StringWriter> { ["original"] = original };
        int calls = 0;
        var tree = Runner.ReplayResultTree(_ => {
            calls++;
            messages.Clear();
            secondary.Clear();
            throw new XdmException("FOER0000", "test replay failure");
        }, messages, secondary);
        Assert.Equal("FOER0000", Assert.Throws<XdmException>(() => tree()).Code);
        Assert.Equal("FOER0000", Assert.Throws<XdmException>(() => tree()).Code);
        Assert.Equal(1, calls);
        Assert.Equal(new[] { "original" }, messages);
        Assert.Same(original, Assert.Single(secondary).Value);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("xml")]
    [InlineData("html")]
    public void TreeAssertionsUseTheConstructedForkResult(string method)
    {
        WithCase($$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode streamable="yes"/><xsl:output method="{{method}}" omit-xml-declaration="yes"/>
              <xsl:template match="/"><out><xsl:fork>
                <xsl:for-each-group select="doc/row" group-by="@k">
                  <xsl:value-of select="current-group()/@v"/><br/>
                </xsl:for-each-group>
              </xsl:fork></out></xsl:template>
            </xsl:stylesheet>
            """, """
            <all-of>
              <assert>count(/out/text()) = 2</assert>
              <assert>/out/text()[1] = '1 2' and /out/text()[2] = '3'</assert>
              <assert-xml><![CDATA[<out>1 2<br/>3<br/></out>]]></assert-xml>
              <assert-string-value>1 23</assert-string-value>
            </all-of>
            """);
    }

    [Fact]
    public void TextThatLooksLikeXmlRemainsATextNodeInTheResultTree()
    {
        WithCase("""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="text"/>
              <xsl:template match="/"><out>&lt;fake/&gt;</out></xsl:template>
            </xsl:stylesheet>
            """, """
            <all-of>
              <assert>/out/text() = '&lt;fake/&gt;' and empty(/fake)</assert>
              <assert-string-value>&lt;fake/&gt;</assert-string-value>
              <assert-serialization>&lt;fake/&gt;</assert-serialization>
            </all-of>
            """);
    }

    [Fact]
    public void SerializationAddedHtmlMetadataDoesNotEnterTheResultTree()
    {
        WithCase("""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="html"/>
              <xsl:template match="/"><html><head/><body>value</body></html></xsl:template>
            </xsl:stylesheet>
            """, """
            <all-of>
              <assert>empty(/html/head/meta) and /html/body = 'value'</assert>
              <serialization-matches>&lt;[mM][eE][tT][aA]</serialization-matches>
            </all-of>
            """);
    }

    [Fact]
    public void ExplicitSerializationMakesTreeAssertionsReadTheSerializedResult()
    {
        WithCase("""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="html"/>
              <xsl:template match="/"><html><head/><body>value</body></html></xsl:template>
            </xsl:stylesheet>
            """, "<assert>count(/html/head/meta) = 1</assert>", "<output serialize='yes'/>");
    }

    [Fact]
    public void JsonSerializationAssertionsDoNotRequireAnXmlTree()
    {
        WithCase("""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="json" indent="no"/>
              <xsl:template match="/"><xsl:sequence select="map{'value': 1}"/></xsl:template>
            </xsl:stylesheet>
            """, """
            <assert-serialization>{"value":1}</assert-serialization>
            """);
    }

    [Fact]
    public void ExplicitPrincipalTreesRetainTypeAnnotations()
    {
        WithCase("""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
              <xsl:template match="/"><xsl:result-document validation="preserve">
                <typed xsl:type="xs:integer">7</typed>
              </xsl:result-document></xsl:template>
            </xsl:stylesheet>
            """, """
            <assert xmlns:xs="http://www.w3.org/2001/XMLSchema">/typed instance of element(*, xs:integer) and data(/typed) instance of xs:integer</assert>
            """);
    }

    [Fact]
    public void SecondaryTreesRetainTheirTypesAndUseTheOutputUriForXmlBase()
    {
        WithCase("""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
              <xsl:template match="/"><out/><xsl:result-document href="child/one.xml" validation="preserve">
                <root xml:base="relative/" xsl:validation="preserve"><typed xsl:type="xs:integer">7</typed></root>
              </xsl:result-document></xsl:template>
            </xsl:stylesheet>
            """, """
            <all-of>
              <assert-xml>&lt;out/&gt;</assert-xml>
              <assert-result-document uri="child/one.xml"><all-of>
                <assert>ends-with(base-uri(/), '/child/one.xml') and ends-with(base-uri(/root), '/child/relative/')</assert>
                <assert xmlns:xs="http://www.w3.org/2001/XMLSchema">/root/typed instance of element(*, xs:integer) and data(/root/typed) instance of xs:integer</assert>
              </all-of></assert-result-document>
            </all-of>
            """);
    }

    private static void WithCase(string stylesheet, string assertions, string output = "")
    {
        string directory = Path.Combine(Path.GetTempPath(), "xsl3-result-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? strict = Environment.GetEnvironmentVariable("XSL3_STREAM_STRICT");
        try
        {
            Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", "1");
            File.WriteAllText(Path.Combine(directory, "catalog.xml"), $"""
                <catalog xmlns="{Catalog.Ns}"><test-set name="result-tree" file="set.xml"/></catalog>
                """);
            File.WriteAllText(Path.Combine(directory, "set.xml"), $$"""
                <test-set xmlns="{{Catalog.Ns}}" name="result-tree"><test-case name="constructed-result">
                  <environment><source role="." file="source.xml" streaming="true"/></environment>
                  <test><stylesheet file="input.xsl"/>{{output}}</test><result>{{assertions}}</result>
                </test-case></test-set>
                """);
            File.WriteAllText(Path.Combine(directory, "source.xml"), "<doc><row k='a' v='1'/><row k='a' v='2'/><row k='b' v='3'/></doc>");
            File.WriteAllText(Path.Combine(directory, "input.xsl"), stylesheet);
            Outcome outcome = new Runner().Run(Catalog.Load(directory).TestCases.Single());
            Assert.True(outcome.Kind == OutcomeKind.Pass, outcome.Detail);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", strict);
            Directory.Delete(directory, recursive: true);
        }
    }
}
