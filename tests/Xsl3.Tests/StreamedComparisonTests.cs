// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using System.Xml.Schema;
using Xunit;

namespace Xsl3.Tests;

public class StreamedComparisonTests
{
    private static string Sheet(string select) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/"><xsl:value-of select="{{select}}"/></xsl:template>
        </xsl:stylesheet>
        """;

    [Theory]
    [InlineData("doc/value = 4", "true")]
    [InlineData("4 = doc/value", "true")]
    [InlineData("doc/value != 4", "true")]
    [InlineData("doc/value &lt; 4", "true")]
    [InlineData("doc/value &lt;= 4", "true")]
    [InlineData("doc/value > 4", "false")]
    [InlineData("doc/value >= 4", "true")]
    [InlineData("doc/single eq '4'", "true")]
    [InlineData("doc/single ne '4'", "false")]
    [InlineData("doc/single lt '5'", "true")]
    [InlineData("doc/single le '4'", "true")]
    [InlineData("doc/single gt '3'", "true")]
    [InlineData("doc/single ge '4'", "true")]
    [InlineData("doc/value = (1 to 3)", "true")]
    [InlineData("(1 to 3) = doc/value", "true")]
    [InlineData("doc/value > (1 to 3)", "true")]
    [InlineData("(1 to 3) &lt; doc/value", "true")]
    [InlineData("empty(doc/missing eq '4')", "true")]
    [InlineData("doc/value ! (. is .)", "true true")]
    public void ComparisonOperandsConsumeEachNodeWhileCurrent(string select, string expected)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(Sheet(select))).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader("<doc><value>1</value><value>4</value><single>4</single></doc>"));
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(reader)));
        }
    }

    [Theory]
    [InlineData("doc/value eq '4'", "XPTY0004")]
    [InlineData("doc/value = function() { 1 }", "FOTY0013")]
    public void CanonicalComparisonErrorsArePreserved(string select, string code)
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(Sheet(select))).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader("<doc><value>1</value><value>4</value></doc>"));
        Assert.Contains(code, Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(reader)).Message);
    }

    [Theory]
    [InlineData("doc/value = 9007199254740993", "true")]
    [InlineData("doc/value = 9007199254740992", "false")]
    [InlineData("doc/value eq 9007199254740993", null)]
    public void ListTypedValuesKeepTheirItemsAndIntegerPrecision(string select, string? expected)
    {
        const string schema = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:simpleType name="integers"><xs:list itemType="xs:integer"/></xs:simpleType>
              <xs:element name="doc"><xs:complexType><xs:sequence>
                <xs:element name="value" type="integers" maxOccurs="unbounded"/>
              </xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """;
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader(schema)));
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward, SchemaAware = true } }.Compile(new StringReader(Sheet(select))).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader("<doc><value>9007199254740993 1</value><value>2</value></doc>"),
                new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas });
            if (expected == null) Assert.Contains("XPTY0004", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(reader)).Message);
            else Assert.Equal(expected, string.Concat(transform.ApplyTemplates(reader)));
        }
    }
}
