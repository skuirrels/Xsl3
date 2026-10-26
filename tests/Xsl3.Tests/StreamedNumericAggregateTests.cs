// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System.IO;
using System.Xml;
using System.Xml.Schema;
using Xunit;

namespace Xsl3.Tests;

public class StreamedNumericAggregateTests
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
    [InlineData("avg", "2")]
    [InlineData("min", "1.25")]
    [InlineData("max", "2.75")]
    public void UntypedNodesReturnDoubles(string function, string expected)
    {
        string body = $"<xsl:value-of select='let $r := {function}(doc/value) return (count($r), $r instance of xs:double, string($r))' separator='|'/>";
        foreach (bool forward in new[] { false, true })
            Assert.Equal("1|true|" + expected, Run("<doc><value>1.25</value><value>2.75</value></doc>", body, forward));
    }

    [Theory]
    [InlineData("avg", "doc/value")]
    [InlineData("min", "doc/value")]
    [InlineData("max", "doc/value")]
    [InlineData("avg", "()")]
    [InlineData("min", "()")]
    [InlineData("max", "()")]
    public void EmptyInputReturnsNoItem(string function, string argument)
    {
        string body = $"<xsl:value-of select='let $r := {function}({argument}) return (count($r), $r instance of xs:double, string($r))' separator='|'/>";
        foreach (bool forward in new[] { false, true }) Assert.Equal("0|false|", Run("<doc/>", body, forward));
    }

    [Theory]
    [InlineData("avg", "-0", "-0", "-INF")]
    [InlineData("avg", "0", "-0", "INF")]
    [InlineData("min", "-0", "0", "-INF")]
    [InlineData("min", "0", "-0", "INF")]
    [InlineData("max", "-0", "0", "-INF")]
    [InlineData("max", "0", "-0", "INF")]
    public void SignedZeroAndFirstEqualValueArePreserved(string function, string first, string second, string expected)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal(expected, Run($"<doc><value>{first}</value><value>{second}</value></doc>", $"<xsl:value-of select='1 div {function}(doc/value)'/>", forward));
    }

    [Theory]
    [InlineData("avg")]
    [InlineData("min")]
    [InlineData("max")]
    public void NaNIsStickyButDoesNotHideLaterConversionErrors(string function)
    {
        foreach (bool forward in new[] { false, true })
        {
            string body = $"<xsl:value-of select='{function}(doc/value)'/>";
            Assert.Equal("NaN", Run("<doc><value>NaN</value><value>2</value></doc>", body, forward));
            Assert.Equal("NaN", Run("<doc><value>2</value><value>NaN</value><value>3</value></doc>", body, forward));
            foreach (string first in new[] { "NaN", "INF", "-INF" })
                Assert.Contains("FORG0001", Assert.Throws<XsltDynamicException>(() => Run($"<doc><value>{first}</value><value>invalid</value></doc>", body, forward)).Message);
        }
    }

    [Theory]
    [InlineData("avg", "NaN")]
    [InlineData("min", "-INF")]
    [InlineData("max", "INF")]
    public void InfinitiesKeepTheirCanonicalResults(string function, string expected)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal(expected, Run("<doc><value>INF</value><value>-INF</value></doc>", $"<xsl:value-of select='{function}(doc/value)'/>", forward));
    }

    [Fact]
    public void AverageAddsInInputOrder()
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal("0", Run("<doc><value>1e16</value><value>1</value><value>-1e16</value></doc>", "<xsl:value-of select='avg(doc/value)'/>", forward));
    }

    [Theory]
    [InlineData("avg")]
    [InlineData("min")]
    [InlineData("max")]
    public void GroupsResetStateAndPreserveEmptyResults(string function)
    {
        string input = "<doc><group><value>2</value></group><group/><group><value>-0</value></group><group><value>5</value></group></doc>";
        string body = $"<xsl:for-each select='doc/group'><xsl:value-of select='let $r := {function}(value) return (count($r), string($r))' separator='|'/><xsl:text>;</xsl:text></xsl:for-each>";
        foreach (bool forward in new[] { false, true }) Assert.Equal("1|2;0|;1|-0;1|5;", Run(input, body, forward));
    }

    [Theory]
    [InlineData("min", "10")]
    [InlineData("max", "2")]
    public void CommentStringsKeepLexicalOrdering(string function, string expected)
    {
        foreach (bool forward in new[] { false, true })
        {
            Assert.Equal(expected, Run("<doc><!--2--><!--10--></doc>", $"<xsl:value-of select='{function}(doc/comment())'/>", forward));
            Assert.Equal(expected, Run("<doc><!--2--><!--10--></doc>", $"<xsl:value-of select='{function}(doc/comment(), &quot;http://www.w3.org/2005/xpath-functions/collation/codepoint&quot;)'/>", forward));
        }
    }

    [Theory]
    [InlineData("avg", "0.15")]
    [InlineData("min", "0.1")]
    [InlineData("max", "0.2")]
    public void SchemaTypedDecimalsKeepTheirExactTypeAndValue(string function, string expected)
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader("""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="doc"><xs:complexType><xs:sequence><xs:element name="value" type="xs:decimal" maxOccurs="unbounded"/></xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """)));
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward, SchemaAware = true } }
                .Compile(new StringReader(Sheet($"<xsl:value-of select='let $r := {function}(doc/value) return ($r instance of xs:decimal, string($r))' separator='|'/>"))).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader("<doc><value>0.1</value><value>0.2</value></doc>"), new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas });
            Assert.Equal("true|" + expected, string.Concat(transform.ApplyTemplates(reader)));
        }
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData("avg", "1|2;0|;")]
    [InlineData("min", "1|1.25;0|;")]
    [InlineData("max", "1|2.75;0|;")]
    public void SavedAssembliesKeepNonemptyAndEmptyResults(string function, string expected)
    {
        string body = $"<xsl:for-each select='doc/group'><xsl:value-of select='let $r := {function}(value) return (count($r), string($r))' separator='|'/><xsl:text>;</xsl:text></xsl:for-each>";
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(Sheet(body))), settings, null, "NumericAggregate", "Saved.NumericAggregate", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.NumericAggregate", true)!, settings);
        using var input = XmlReader.Create(new StringReader("<doc><group><value>1.25</value><value>2.75</value></group><group/></doc>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal(expected, output.ToString());
    }
#endif
}
