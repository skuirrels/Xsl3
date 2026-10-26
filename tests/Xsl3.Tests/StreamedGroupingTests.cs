// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Text;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedGroupingTests
{
    private const string Input = "<doc><row id='1' k='a' start='yes'>one</row><row id='2' k='a' end='yes'>two</row><row id='3' k='b' start='yes'>three</row><row id='4' k='b' end='yes'>four</row></doc>";

    private static string Sheet(string body, string declarations = "", bool streamable = true) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
          <xsl:mode streamable="{{(streamable ? "yes" : "no")}}"/><xsl:output omit-xml-declaration="yes"/>
          {{declarations}}<xsl:template match="doc"><out>{{body}}</out></xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string sheet, string xml = Input, bool forward = true, bool legacy = false)
    {
        using var input = XmlReader.Create(new StringReader(xml));
        using var output = new StringWriter();
        if (legacy)
        {
            var transform = new XslCompiledTransform();
            transform.Load(XmlReader.Create(new StringReader(sheet)), new() { StreamedExecution = forward }, null);
            transform.Transform(input, null, output);
        }
        else
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet)).CreateTransformer();
            transform.ApplyTemplates(input, output);
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }

    [Theory]
    [InlineData("group-adjacent='@k'", false)]
    [InlineData("group-starting-with='row[@start]'", false)]
    [InlineData("group-ending-with='row[@end]'", false)]
    [InlineData("group-adjacent='@k'", true)]
    [InlineData("group-starting-with='row[@start]'", true)]
    [InlineData("group-ending-with='row[@end]'", true)]
    public void MembersAreConsumedBeforeThePopulationAdvances(string grouping, bool legacy)
    {
        string sheet = Sheet($$"""
            <xsl:for-each-group select="row" {{grouping}}><g first="{@id}" p="{position()}">
              <xsl:for-each select="current-group()"><r id="{@id}"><xsl:value-of select="."/></r></xsl:for-each>
            </g></xsl:for-each-group>
            """);
        const string expected = "<out><g first=\"1\" p=\"1\"><r id=\"1\">one</r><r id=\"2\">two</r></g><g first=\"3\" p=\"2\"><r id=\"3\">three</r><r id=\"4\">four</r></g></out>";
        Assert.Equal(expected, Run(sheet, forward: false, legacy: legacy));
        Assert.Equal(expected, Run(sheet, legacy: legacy));
    }

    [Theory]
    [InlineData("group-adjacent='@k'")]
    [InlineData("group-starting-with='row[@start]'")]
    [InlineData("group-ending-with='row[@end]'")]
    public void CountingMembersUsesTheEntireGroup(string grouping)
    {
        string sheet = Sheet($"<xsl:for-each-group select='row' {grouping}><n v='{{count(current-group())}}'/></xsl:for-each-group>");
        Assert.Equal("<out><n v=\"2\"/><n v=\"2\"/></out>", Run(sheet));
        Assert.Equal("<out/>", Run(sheet, "<doc/>"));
    }

    [Fact]
    public void MemberAtomizationAndGroupingKeysKeepTheirValues()
    {
        string sheet = Sheet("<xsl:for-each-group select='row' group-adjacent='@k'><g k='{current-grouping-key()}' s='{sum(current-group())}'/></xsl:for-each-group>");
        const string xml = "<doc><row k='a'>1</row><row k='a'>2</row><row k='b'>3</row></doc>";
        Assert.Equal("<out><g k=\"a\" s=\"3\"/><g k=\"b\" s=\"3\"/></out>", Run(sheet, xml));
    }

    [Fact]
    public void AnUnstreamedCallerKeepsTheGroupWhenTheCalleeHasNoContextItem()
    {
        string sheet = Sheet("<xsl:for-each-group select='row' group-adjacent='@k'><xsl:call-template name='members'/></xsl:for-each-group>", """
            <xsl:template name="members"><xsl:context-item use="absent"/><g key="{current-grouping-key()}"><xsl:for-each select="current-group()"><xsl:value-of select="."/></xsl:for-each></g></xsl:template>
            <xsl:function name="f:absorb" xmlns:f="urn:functions" streamability="absorbing" as="xs:string"><xsl:param name="n" as="node()"/><xsl:sequence select="string($n)"/></xsl:function>
            """, streamable: false);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><g key=\"a\">onetwo</g><g key=\"b\">threefour</g></out>", Run(sheet, forward: forward));
    }

    [Theory]
    [InlineData("count(current-group())", "XTDE1061")]
    [InlineData("current-grouping-key()", "XTDE1071")]
    public void ADeclaredStreamableCallerClearsTheGroupInCalledTemplates(string query, string code)
    {
        string sheet = Sheet("<xsl:for-each-group select='row' group-adjacent='@k'><xsl:call-template name='members'/></xsl:for-each-group>", $$"""
            <xsl:template name="members"><xsl:context-item use="absent"/><xsl:try><xsl:value-of select="{{query}}"/><xsl:catch><absent/></xsl:catch></xsl:try></xsl:template>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><absent/><absent/></out>", Run(sheet, forward: forward));
        string uncaught = sheet.Replace("<xsl:try>", "").Replace("<xsl:catch><absent/></xsl:catch></xsl:try>", "");
        Assert.Contains(code, Assert.Throws<XsltDynamicException>(() => Run(uncaught)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryAppliedTemplatesInheritTheirCallersGroup(bool forward)
    {
        string sheet = Sheet("<xsl:for-each-group select='row' group-adjacent='@k'><xsl:apply-templates select='current-group()' mode='members'/></xsl:for-each-group>", """
            <xsl:mode name="members"/>
            <xsl:template match="row" mode="members"><r key="{current-grouping-key()}" n="{count(current-group())}"/></xsl:template>
            <xsl:function name="f:absorb" xmlns:f="urn:functions" streamability="absorbing" as="xs:string"><xsl:param name="n" as="node()"/><xsl:sequence select="string($n)"/></xsl:function>
            """, streamable: false);
        Assert.Equal("<out><r key=\"a\" n=\"2\"/><r key=\"a\" n=\"2\"/><r key=\"b\" n=\"2\"/><r key=\"b\" n=\"2\"/></out>", Run(sheet, forward: forward));
    }

    [Fact]
    public void OrdinaryAttributeSetsInheritTheGroup()
    {
        string sheet = Sheet("<xsl:for-each-group select='row' group-adjacent='@k'><g xsl:use-attribute-sets='group'><xsl:value-of select='current-group()'/></g></xsl:for-each-group>", """
            <xsl:attribute-set name="group"><xsl:attribute name="n" select="count(current-group())"/><xsl:attribute name="key" select="current-grouping-key()"/></xsl:attribute-set>
            <xsl:function name="f:absorb" xmlns:f="urn:functions" streamability="absorbing" as="xs:string"><xsl:param name="n" as="node()"/><xsl:sequence select="string($n)"/></xsl:function>
            """, streamable: false);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><g n=\"2\" key=\"a\">one two</g><g n=\"2\" key=\"b\">three four</g></out>", Run(sheet, forward: forward));
    }

    [Theory]
    [InlineData("group-adjacent='@k'")]
    [InlineData("group-starting-with='row[@start]'")]
    [InlineData("group-ending-with='row[@end]'")]
    public void UnusedMembersAreDrainedBeforeTheNextGroup(string grouping)
    {
        string sheet = Sheet($"<xsl:for-each-group select='row' {grouping}><g first='{{@id}}'/></xsl:for-each-group>");
        Assert.Equal("<out><g first=\"1\"/><g first=\"3\"/></out>", Run(sheet));
    }

    [Fact]
    public void ATruncatedMemberSelectionDoesNotLoseTheNextGroup()
    {
        string sheet = Sheet("<xsl:for-each-group select='row' group-adjacent='@k'><g><xsl:value-of select='string(head(current-group()))'/></g></xsl:for-each-group>");
        Assert.Equal("<out><g>one</g><g>three</g></out>", Run(sheet));
    }

    [Fact]
    public void NestedGroupingCanConsumeTheOuterCurrentGroup()
    {
        string sheet = Sheet("""
            <xsl:for-each-group select="row" group-adjacent="@k"><g key="{current-grouping-key()}">
              <xsl:for-each-group select="current-group()" group-adjacent="@id"><r><xsl:value-of select="current-group()"/></r></xsl:for-each-group>
            </g></xsl:for-each-group>
            """);
        Assert.Equal("<out><g key=\"a\"><r>one</r><r>two</r></g><g key=\"b\"><r>three</r><r>four</r></g></out>", Run(sheet));
    }

    [Fact]
    public void GroundedGroupsCanBeReadMoreThanOnce()
    {
        string sheet = Sheet("""
            <xsl:for-each-group select="1 to 4" group-adjacent=". idiv 3"><g n="{count(current-group())}" size="{last()}"><xsl:value-of select="current-group()"/></g></xsl:for-each-group>
            """);
        Assert.Equal("<out><g n=\"2\" size=\"2\">1 2</g><g n=\"2\" size=\"2\">3 4</g></out>", Run(sheet));
    }

    [Theory]
    [InlineData("()", true, "<out><g n=\"4\"/></out>")]
    [InlineData("(@k, @start)", true, "<out><g n=\"1\"/><g n=\"1\"/><g n=\"1\"/><g n=\"1\"/></out>")]
    [InlineData("position() idiv 3", false, "<out><g n=\"2\"/><g n=\"2\"/></out>")]
    public void CompositeKeysAndPopulationPositionsSurviveSuspension(string key, bool composite, string expected)
    {
        string sheet = Sheet($"<xsl:for-each-group select='row' group-adjacent='{key}' composite='{(composite ? "yes" : "no")}'><g n='{{count(current-group())}}'/></xsl:for-each-group>");
        Assert.Equal(expected, Run(sheet, forward: false));
        Assert.Equal(expected, Run(sheet));
    }

    [Theory]
    [InlineData("()")]
    [InlineData("(@k, @id)")]
    public void NonCompositeKeysRequireExactlyOneAtomicValue(string key)
    {
        string sheet = Sheet($"<xsl:for-each-group select='row' group-adjacent='{key}'><xsl:value-of select='count(current-group())'/></xsl:for-each-group>");
        Assert.Contains("XTTE1100", Assert.Throws<XsltDynamicException>(() => Run(sheet)).Message);
    }

    [Fact]
    public void ExplicitlyCopiedMembersRemainRepeatable()
    {
        string sheet = Sheet("<xsl:for-each-group select='copy-of(row)' group-adjacent='@k'><g n='{count(current-group())}'><xsl:value-of select='current-group()'/></g></xsl:for-each-group>");
        Assert.Equal("<out><g n=\"2\">one two</g><g n=\"2\">three four</g></out>", Run(sheet));
    }

    [Fact]
    public void HeaderAttributeGroupsRemainRepeatable()
    {
        string sheet = Sheet("<xsl:for-each-group select='@*' group-adjacent='true()'><g n='{count(current-group())}'><xsl:value-of select='current-group()'/></g></xsl:for-each-group>");
        const string xml = "<doc a='one' b='two'/>";
        Assert.Equal("<out><g n=\"2\">one two</g></out>", Run(sheet, xml, forward: false));
        Assert.Equal("<out><g n=\"2\">one two</g></out>", Run(sheet, xml));
    }

    [Fact]
    public void HeaderNamespaceGroupsRemainRepeatable()
    {
        string sheet = Sheet("<xsl:for-each-group select='namespace::*' group-adjacent='true()'><g first='{count(current-group())}' second='{count(current-group())}'/></xsl:for-each-group>");
        const string xml = "<doc xmlns:p='urn:example'/>";
        Assert.Equal("<out><g first=\"2\" second=\"2\"/></out>", Run(sheet, xml, forward: false));
        Assert.Equal("<out><g first=\"2\" second=\"2\"/></out>", Run(sheet, xml));
    }

    [Fact]
    public void AKeyFailureKeepsItsErrorCode()
    {
        string sheet = Sheet("<xsl:for-each-group select='row' group-adjacent='if (@id = 2) then error() else @k'><xsl:value-of select='current-group()'/></xsl:for-each-group>");
        Assert.Contains("FOER0000", Assert.Throws<XsltDynamicException>(() => Run(sheet)).Message);
    }

    [Fact]
    public void TheFirstGroupBodyStartsBeforeItsMembersAreRead()
    {
        var xml = new StringBuilder("<doc>");
        for (int i = 0; i < 1000; i++) xml.Append("<row k='same'>value</row>");
        xml.Append("</doc>");
        string sheet = Sheet("<xsl:for-each-group select='row' group-adjacent='@k'><xsl:message select='current-grouping-key()'/><g><xsl:value-of select='current-group()'/></g></xsl:for-each-group>");
        using var input = new TrackingReader(XmlReader.Create(new StringReader(xml.ToString())));
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true, MaxRecursionDepth = 32 } }.Compile(new StringReader(sheet)).CreateTransformer();
        int? rowsAtMessage = null;
        transform.MessageHandler = _ => rowsAtMessage ??= input.Rows;
        using var output = new StringWriter();
        transform.ApplyTemplates(input, output);
        Assert.Equal(1, rowsAtMessage);
        Assert.Equal(1000, input.Rows);
    }

    private sealed class TrackingReader(XmlReader reader) : XmlWrappingReader(reader)
    {
        internal int Rows;
        public override bool Read()
        {
            bool read = base.Read();
            if (read && NodeType == XmlNodeType.Element && LocalName == "row") Rows++;
            return read;
        }
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void SavedAssembliesResumeGroupingPopulations()
    {
        string sheet = Sheet("<xsl:for-each-group select='row' group-adjacent='@k'><g><xsl:value-of select='current-group()'/></g></xsl:for-each-group>");
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null,
            "Xsl3.Tests.StreamedGrouping", "Saved.Grouping", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Grouping", true)!, settings);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("<out><g>one two</g><g>three four</g></out>", output.ToString());
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }
#endif
}
