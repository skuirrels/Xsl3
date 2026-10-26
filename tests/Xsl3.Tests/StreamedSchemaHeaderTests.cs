// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedSchemaHeaderTests
{
    private static string Sheet(string expression) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:s="urn:test:types">
          <xsl:import-schema namespace="urn:test:types">
            <xs:schema targetNamespace="urn:test:types">
              <xs:simpleType name="positive"><xs:restriction base="xs:integer"><xs:minInclusive value="1"/></xs:restriction></xs:simpleType>
            </xs:schema>
          </xsl:import-schema>
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/"><xsl:value-of select="{{expression}}" separator="|"/></xsl:template>
        </xsl:stylesheet>
        """;

    [Theory]
    [InlineData("doc/row[s:positive(@n) gt 1]/a | doc/row/b", "y1|x2|y2")]
    [InlineData("doc//row[s:positive(@n) gt 1]//a", "x2")]
    public void SchemaConstructorsRetainTheirHeaderPredicateSemantics(string expression, string expected)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { SchemaAware = true, StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression))).CreateTransformer();
            using var input = XmlReader.Create(new StringReader("<doc><row n='1'><a>x1</a><b>y1</b></row><row n='2'><a>x2</a><b>y2</b></row></doc>"));
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(input)));
        }
    }
    [Theory]
    [InlineData("doc/row[s:positive(@n) gt 1]/a | doc/row/b")]
    [InlineData("doc//row[s:positive(@n) gt 1]//a")]
    public void InvalidSchemaConstructorValuesKeepTheirErrorCode(string expression)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { SchemaAware = true, StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression))).CreateTransformer();
            using var input = XmlReader.Create(new StringReader("<doc><row n='0'><a>x</a><b>y</b></row></doc>"));
            Assert.Equal("FORG0001", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(input)).Code.LocalName);
        }
    }

}
