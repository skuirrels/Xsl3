// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedCopySelectTests
{
    [Theory]
    [InlineData(false, "strip", "false")]
    [InlineData(true, "strip", "false")]
    [InlineData(false, "preserve", "false")]
    [InlineData(true, "preserve", "false")]
    [InlineData(false, "strict", "true")]
    [InlineData(true, "strict", "true")]
    [InlineData(false, "lax", "true")]
    [InlineData(true, "lax", "true")]
    [InlineData(false, "type='rowType'", "true")]
    [InlineData(true, "type='rowType'", "true")]
    public void SelectedElementCanBeCopiedAndValidatedBeforeItsDescendantsAreConsumed(bool forward, string validation, string expected)
    {
        string option = validation.StartsWith("type=", System.StringComparison.Ordinal) ? validation : "validation='" + validation + "'";
        string sheet = Header + "<xsl:import-schema><xs:schema><xs:complexType name='rowType'><xs:sequence><xs:element name='value' type='xs:integer'/></xs:sequence></xs:complexType><xs:element name='row' type='rowType'/></xs:schema></xsl:import-schema>"
            + "<xsl:template match='/'><xsl:variable name='copy' as='node()'><xsl:copy select='*' " + option + "><xsl:copy-of select='node()'/></xsl:copy></xsl:variable>"
            + "<xsl:value-of select='$copy instance of element(*, rowType)'/></xsl:template></xsl:stylesheet>";
        Assert.Equal(expected, Run(sheet, "<row><value>42</value></row>", forward, schema: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundedCopySelectionHasSingletonFocusAndRestoresNestedFocus(bool forward)
    {
        string sheet = Header + "<xsl:template match='/'><xsl:copy select='copy-of(*)'><xsl:attribute name='focus' select='concat(position(), &quot;/&quot;, last())'/>"
            + "<xsl:for-each select='@*'><part p='{position()}' size='{last()}'/></xsl:for-each><xsl:copy-of select='node()'/></xsl:copy></xsl:template></xsl:stylesheet>";
        Assert.Equal("<row focus=\"1/1\"><part p=\"1\" size=\"2\"/><part p=\"2\" size=\"2\"/><value>42</value></row>", Run(sheet, "<row a='one' b='two'><value>42</value></row>", forward));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptySelectionDoesNotEvaluateTheCopyBody(bool forward)
    {
        string sheet = Header + "<xsl:template match='/'><xsl:copy select='missing'><xsl:sequence select='error()'/></xsl:copy><ok/></xsl:template></xsl:stylesheet>";
        Assert.Equal("<ok/>", Run(sheet, "<row/>", forward));
    }

    [Theory]
    [InlineData(false, "row/value")]
    [InlineData(true, "row/value")]
    [InlineData(false, "(1, 2)")]
    [InlineData(true, "(1, 2)")]
    public void MultipleItemsRaiseTheCopyCardinalityCodeAndRollBackTryOutput(bool forward, string select)
    {
        string sheet = Header + "<xsl:template match='/'><xsl:try><xsl:copy select='" + select + "'><xsl:copy-of select='node()'/></xsl:copy>"
            + "<xsl:catch errors='err:XTTE3180'><caught/></xsl:catch></xsl:try></xsl:template></xsl:stylesheet>";
        Assert.Equal("<caught/>", Run(sheet, "<row><value>one</value><value>two</value></row>", forward));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtomicSelectedValuesKeepTheirItemResultType(bool forward)
    {
        string sheet = Header + "<xsl:template match='/'><xsl:variable name='value' as='item()*'><xsl:copy select='41'><xsl:sequence select='error()'/></xsl:copy></xsl:variable>"
            + "<xsl:value-of select='$value instance of xs:integer, $value' separator='|'/></xsl:template></xsl:stylesheet>";
        Assert.Equal("true|41", Run(sheet, "<row/>", forward));
    }

    private const string Header = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:err='http://www.w3.org/2005/xqt-errors' exclude-result-prefixes='xs err'><xsl:mode streamable='yes'/><xsl:output omit-xml-declaration='yes'/>";
    private static string Run(string sheet, string xml, bool forward, bool schema = false)
    {
        var executable = new XsltCompiler { Settings = new() { StreamedExecution = forward, SchemaAware = schema } }.Compile(new StringReader(sheet));
        using var input = XmlReader.Create(new StringReader(xml)); using var output = new StringWriter();
        executable.CreateTransformer().ApplyTemplates(input, output);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }
}
