// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System.IO;
using System.Xml;
using System.Xml.Schema;
using Xunit;

namespace Xsl3.Tests;

public class StreamedSumTests
{
    private static string Sheet(string body) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/">{{body}}</xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string input, string body, bool forward)
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
            .Compile(new StringReader(Sheet(body))).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(input));
        return string.Concat(transform.ApplyTemplates(reader));
    }

    [Theory]
    [InlineData("<doc/>", "true|false|0")]
    [InlineData("<doc><value>1.25</value><value>2.75</value></doc>", "false|true|4")]
    [InlineData("<doc><value>1e16</value><value>1</value><value>-1e16</value></doc>", "false|true|0")]
    [InlineData("<doc><value>INF</value></doc>", "false|true|INF")]
    [InlineData("<doc><value>-INF</value></doc>", "false|true|-INF")]
    [InlineData("<doc><value>INF</value><value>-INF</value></doc>", "false|true|NaN")]
    [InlineData("<doc><value>NaN</value><value>2</value></doc>", "false|true|NaN")]
    public void SumPreservesEmptyTypeDoubleTypeAndAdditionOrder(string input, string expected)
    {
        const string body = "<xsl:value-of select='let $s := sum(doc/value) return ($s instance of xs:integer, $s instance of xs:double, string($s))' separator='|'/>";
        foreach (bool forward in new[] { false, true }) Assert.Equal(expected, Run(input, body, forward));
    }

    [Theory]
    [InlineData("<doc><value>-0</value></doc>", "-INF")]
    [InlineData("<doc><value>-0</value><value>-0</value></doc>", "-INF")]
    [InlineData("<doc><value>-0</value><value>0</value></doc>", "INF")]
    public void SumStartsWithTheFirstValueRatherThanPositiveZero(string input, string expected)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal(expected, Run(input, "<xsl:value-of select='1 div sum(doc/value)'/>", forward));
    }

    [Theory]
    [InlineData("<doc><value>invalid</value></doc>")]
    [InlineData("<doc><value>NaN</value><value>invalid</value></doc>")]
    [InlineData("<doc><value>INF</value><value>invalid</value></doc>")]
    public void EveryValueIsConvertedEvenAfterNaNOrInfinity(string input)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Contains("FORG0001", Assert.Throws<XsltDynamicException>(() => Run(input, "<xsl:value-of select='sum(doc/value)'/>", forward)).Message);
    }

    [Fact]
    public void ReenteredAggregatesDoNotRetainThePreviousGroupsState()
    {
        const string input = "<doc><group><value>2</value></group><group/><group><value>-0</value></group><group><value>5</value></group></doc>";
        const string body = "<xsl:for-each select='doc/group'><xsl:value-of select='let $s := sum(value) return ($s instance of xs:integer, string($s), string(1 div xs:double($s)))' separator='|'/><xsl:text>;</xsl:text></xsl:for-each>";
        foreach (bool forward in new[] { false, true })
            Assert.Equal("false|2|0.5;true|0|INF;false|-0|-INF;false|5|0.2;", Run(input, body, forward));
    }

    [Fact]
    public void AttributeValuesAreSummedWhileTheirRecordIsCurrent()
    {
        const string input = "<doc><record value='1.25'><discard>unused</discard></record><record value='2.75'/></doc>";
        foreach (bool forward in new[] { false, true })
            Assert.Equal("4", Run(input, "<xsl:value-of select='sum(doc/record/@value)'/>", forward));
    }

    [Fact]
    public void SchemaTypedIntegersKeepTheirExactArithmetic()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader("""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="doc"><xs:complexType><xs:sequence>
                <xs:element name="value" type="xs:integer" maxOccurs="unbounded"/>
              </xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """)));
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward, SchemaAware = true } }
                .Compile(new StringReader(Sheet("<xsl:value-of select='let $s := sum(doc/value) return ($s instance of xs:integer, string($s))' separator='|'/>"))).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader("<doc><value>9007199254740993</value><value>1</value></doc>"),
                new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas });
            Assert.Equal("true|9007199254740994", string.Concat(transform.ApplyTemplates(reader)));
        }
    }
#if NET9_0_OR_GREATER
    [Fact]
    public void SavedAssemblyUsesTheSameEmptyAndNonemptySumSemantics()
    {
        const string body = "<xsl:for-each select='doc/group'><xsl:value-of select='let $s := sum(value) return ($s instance of xs:integer, string($s))' separator='|'/><xsl:text>;</xsl:text></xsl:for-each>";
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(Sheet(body))), settings, null,
            "StreamedSum", "Saved.StreamedSum", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.StreamedSum", true)!, settings);
        using var input = XmlReader.Create(new StringReader("<doc><group><value>1.25</value><value>2.75</value></group><group/></doc>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("false|4;true|0;", output.ToString());
    }
#endif
}
