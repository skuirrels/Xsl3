// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedIterateTests
{
    private static string Sheet(string body, string declarations = "") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:f="urn:test:functions" exclude-result-prefixes="xs f">
          <xsl:mode streamable="yes"/><xsl:output omit-xml-declaration="yes"/>
          {{declarations}}
          <xsl:template match="/">{{body}}</xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string sheet, string xml, bool forward = true)
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader(xml));
        using var output = new StringWriter();
        transform.ApplyTemplates(input, output);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }

    [Fact]
    public void IterationResultsReachTheWriterBeforeTheInputEnds()
    {
        string sheet = Sheet("<out><xsl:iterate select='doc/row'><xsl:value-of select='.'/></xsl:iterate></out>");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><row>first</row><row>second</row></doc>"));
        using var text = new StringWriter();
        using var output = new ForwardOutputWriter(input, text);
        transform.ApplyTemplates(input, output);
        Assert.True(output.Observed);
        Assert.Equal("<out>firstsecond</out>", text.ToString());
    }

    private sealed class ForwardOutputWriter(XmlReader input, TextWriter text) : XmlTextWriter(text)
    {
        internal bool Observed;
        public override void WriteString(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                Assert.False(input.EOF);
                Observed = true;
            }
            base.WriteString(value);
        }
    }

    [Fact]
    public void ARunningBalanceConsumesEachRowBeforeAdvancing()
    {
        string sheet = Sheet("""
            <out><xsl:iterate select="doc/row">
              <xsl:param name="balance" as="xs:decimal" select="0"/>
              <xsl:on-completion><total><xsl:value-of select="$balance"/></total></xsl:on-completion>
              <row p="{position()}"><xsl:value-of select="value"/></row>
              <xsl:next-iteration><xsl:with-param name="balance" select="$balance + xs:decimal(@amount)"/></xsl:next-iteration>
            </xsl:iterate></out>
            """);
        const string xml = "<doc><row amount='1.25'><value>a</value></row><row amount='2.75'><value>b</value></row></doc>";
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><row p=\"1\">a</row><row p=\"2\">b</row><total>4</total></out>", Run(sheet, xml, forward));
    }

    [Theory]
    [InlineData("'done'", "done")]
    [InlineData("copy-of(.)", "<row>first</row>")]
    public void BreakDoesNotEvaluateTheNextSelectionItem(string select, string expected)
    {
        string sheet = Sheet($"<xsl:iterate select=\"doc/row ! (if (@bad) then error() else .)\"><xsl:on-completion>wrong</xsl:on-completion><xsl:break select=\"{select}\"/></xsl:iterate>");
        Assert.Equal(expected, Run(sheet, "<doc><row>first</row><row bad='yes'/></doc>"));
    }

    [Fact]
    public void EmptyInputCompletesWithInitialParameterValues()
    {
        string sheet = Sheet("<xsl:iterate select='doc/row'><xsl:param name='p' select='7'/><xsl:on-completion><v p='{$p}'/></xsl:on-completion></xsl:iterate>");
        Assert.Equal("<v p=\"7\"/>", Run(sheet, "<doc/>"));
    }

    [Fact]
    public void RealGroundedSizesAndBreakBothWork()
    {
        string sheet = Sheet("""
            <out><xsl:iterate select="1 to 3"><xsl:on-completion>wrong</xsl:on-completion>
              <v p="{position()}" size="{last()}"/>
              <xsl:if test=". = 2"><xsl:break/></xsl:if>
            </xsl:iterate></out>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><v p=\"1\" size=\"3\"/><v p=\"2\" size=\"3\"/></out>", Run(sheet, "<doc/>", forward));
    }

    [Fact]
    public void ParameterUpdatesAreSimultaneous()
    {
        string sheet = Sheet("""
            <out><xsl:iterate select="1 to 2"><xsl:param name="a" select="1"/><xsl:param name="b" select="2"/>
              <xsl:on-completion><v a="{$a}" b="{$b}"/></xsl:on-completion>
              <v a="{$a}" b="{$b}"/>
              <xsl:next-iteration><xsl:with-param name="a" select="$b"/><xsl:with-param name="b" select="$a"/></xsl:next-iteration>
            </xsl:iterate></out>
            """);
        Assert.Equal("<out><v a=\"1\" b=\"2\"/><v a=\"2\" b=\"1\"/><v a=\"1\" b=\"2\"/></out>", Run(sheet, "<doc/>"));
    }

    [Fact]
    public void ImplicitNextAndOmittedParametersKeepTheirValues()
    {
        string sheet = Sheet("""
            <out><xsl:iterate select="doc/row"><xsl:param name="a" select="1"/><xsl:param name="b" select="2"/>
              <xsl:on-completion><v a="{$a}" b="{$b}"/></xsl:on-completion>
              <xsl:if test="@next"><xsl:next-iteration><xsl:with-param name="a" select="$a+1"/></xsl:next-iteration></xsl:if>
            </xsl:iterate></out>
            """);
        Assert.Equal("<out><v a=\"2\" b=\"2\"/></out>", Run(sheet, "<doc><row next='yes'/><row/></doc>"));
    }

    [Fact]
    public void SourceResultsReachTheirConsumerWhileCurrent()
    {
        string sheet = Sheet("<out><xsl:iterate select='doc/row'><xsl:sequence select='.'/></xsl:iterate></out>");
        Assert.Equal("<out><row><v>a</v></row><row><v>b</v></row></out>", Run(sheet, "<doc><row><v>a</v></row><row><v>b</v></row></doc>"));
    }

    [Fact]
    public void NestedIterationsHaveIndependentBreakState()
    {
        string sheet = Sheet("""
            <out><xsl:iterate select="doc/group"><g><xsl:iterate select="row">
              <xsl:break select="copy-of(.)"/>
            </xsl:iterate></g></xsl:iterate></out>
            """);
        Assert.Equal("<out><g><row>a</row></g><g><row>c</row></g></out>", Run(sheet, "<doc><group><row>a</row><row>b</row></group><group><row>c</row><row>d</row></group></doc>"));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("position()")]
    [InlineData("last()")]
    public void CompletionHasNoFocus(string select)
    {
        string sheet = Sheet($"<xsl:iterate select='1'><xsl:on-completion select='{select}'/></xsl:iterate>");
        // Exercise the inline runtime on a grounded selection in an ordinary mode. Streamability
        // declaration checks may reject an absent-focus expression before this runtime check.
        sheet = sheet.Replace("<xsl:mode streamable=\"yes\"/>", "<xsl:mode name='s' streamable='yes'/>");
        Assert.Contains("XPDY0002", Assert.Throws<XsltDynamicException>(() => Run(sheet, "<doc/>")).Message);
    }

    [Fact]
    public void AnExpandedAbsorbingFunctionKeepsTheLoopGate()
    {
        string sheet = Sheet("<xsl:value-of select='f:total(doc/row)'/>", """
            <xsl:function name="f:total" streamability="absorbing" as="xs:decimal">
              <xsl:param name="rows" as="node()*"/>
              <xsl:iterate select="$rows"><xsl:param name="sum" select="0" as="xs:decimal"/>
                <xsl:on-completion select="$sum"/>
                <xsl:next-iteration><xsl:with-param name="sum" select="$sum + xs:decimal(.)"/></xsl:next-iteration>
              </xsl:iterate>
            </xsl:function>
            """);
        Assert.Equal("4", Run(sheet, "<doc><row>1.25</row><row>2.75</row></doc>"));
    }

    [Fact]
    public void AnExpandedFunctionBreakStillSkipsTheNextSelectionError()
    {
        string sheet = Sheet("<xsl:value-of select='f:first(doc/row)'/>", """
            <xsl:function name="f:first" streamability="absorbing" as="xs:decimal">
              <xsl:param name="rows" as="node()*"/>
              <xsl:iterate select="$rows ! (if (@bad) then error() else .)"><xsl:break select="xs:decimal(.)"/></xsl:iterate>
            </xsl:function>
            """);
        Assert.Equal("1.25", Run(sheet, "<doc><row>1.25</row><row bad='yes'/></doc>"));
    }

    [Fact]
    public void ParameterTypesKeepTheirDeclarationNamespaces()
    {
        string sheet = Sheet("""
            <xsl:iterate select="doc/row" xmlns:t="http://www.w3.org/2001/XMLSchema">
              <xsl:param name="sum" as="t:decimal" select="0"/>
              <xsl:on-completion select="$sum"/>
              <xsl:next-iteration xmlns:t="urn:caller"><xsl:with-param name="sum" select="$sum + xs:decimal(.)"/></xsl:next-iteration>
            </xsl:iterate>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("4", Run(sheet, "<doc><row>1.25</row><row>2.75</row></doc>", forward));
    }

    [Fact]
    public void AtomicStateValuesConsumeSelectedNodesBeforeAdvancement()
    {
        string sheet = Sheet("""
            <xsl:iterate select="doc/group"><xsl:param name="values" as="xs:integer*" select="()"/>
              <xsl:on-completion select="sum($values)"/>
              <xsl:next-iteration><xsl:with-param name="values" select="value"/></xsl:next-iteration>
            </xsl:iterate>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("9007199254740999", Run(sheet, "<doc><group><value>9007199254740993</value><value>6</value></group></doc>", forward));
    }

    [Fact]
    public void AFailedParameterConversionCanBeCaughtBeforeTheUpdateCommits()
    {
        string sheet = Sheet("""
            <xsl:iterate select="doc/row"><xsl:param name="sum" select="0" as="xs:decimal"/>
              <xsl:on-completion select="$sum"/>
              <xsl:try><xsl:next-iteration><xsl:with-param name="sum" select="$sum + xs:decimal(.)"/></xsl:next-iteration>
                <xsl:catch><xsl:next-iteration><xsl:with-param name="sum" select="$sum + 10"/></xsl:next-iteration></xsl:catch>
              </xsl:try>
            </xsl:iterate>
            """);
        Assert.Equal("14", Run(sheet, "<doc><row>1</row><row>bad</row><row>3</row></doc>"));
    }

    [Theory]
    [InlineData("next-match", false)]
    [InlineData("apply-imports", false)]
    [InlineData("next-match", true)]
    [InlineData("apply-imports", true)]
    public void IterationBodiesAndCompletionHaveNoCurrentTemplateRule(string instruction, bool completion)
    {
        string content = completion ? $"<xsl:on-completion><xsl:{instruction}/></xsl:on-completion>" : $"<xsl:{instruction}/>";
        string sheet = Sheet($"<xsl:iterate select='doc/row'>{content}</xsl:iterate>");
        sheet = sheet.Replace("<xsl:mode streamable=\"yes\"/>", "<xsl:mode name='s' streamable='yes'/>");
        foreach (bool forward in new[] { false, true })
            Assert.Contains("XTDE0560", Assert.Throws<XsltDynamicException>(() => Run(sheet, "<doc><row>x</row></doc>", forward)).Message);
    }

    [Fact]
    public void ATemplateCalledFromIterationDoesNotRecoverTheCallerRule()
    {
        string sheet = Sheet("<xsl:iterate select='doc/row'><xsl:call-template name='t'/></xsl:iterate>",
            "<xsl:template name='t'><xsl:next-match/></xsl:template>")
            .Replace("<xsl:mode streamable=\"yes\"/>", "<xsl:mode name='s' streamable='yes'/>");
        foreach (bool forward in new[] { false, true })
            Assert.Contains("XTDE0560", Assert.Throws<XsltDynamicException>(() => Run(sheet, "<doc><row>x</row></doc>", forward)).Message);
    }

    [Fact]
    public void InitialParametersUseTheOuterFocus()
    {
        string sheet = Sheet("<xsl:iterate select='row'><xsl:param name='seed' as='xs:string' select='string(@seed)'/><xsl:on-completion select='$seed'/></xsl:iterate>")
            .Replace("match=\"/\"", "match='doc'");
        foreach (bool forward in new[] { false, true })
            Assert.Equal("outer", Run(sheet, "<doc seed='outer'><row seed='inner'/></doc>", forward));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialParametersSeeEarlierBindingsWithoutLeakingThem(bool forward)
    {
        string sheet = Sheet("""
            <out><xsl:iterate select="doc/row"><xsl:param name="a" select="$a + 1"/>
              <xsl:param name="b" select="$a + 1"/>
              <xsl:on-completion><v a="{$a}" b="{$b}"/></xsl:on-completion>
            </xsl:iterate><after a="{$a}"/></out>
            """, "<xsl:variable name='a' select='100'/>");
        Assert.Equal("<out><v a=\"101\" b=\"102\"/><after a=\"100\"/></out>", Run(sheet, "<doc/>", forward));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("position()")]
    [InlineData("last()")]
    public void ATemplateCalledFromCompletionAlsoHasNoFocus(string select)
    {
        string sheet = Sheet("<xsl:iterate select='1'><xsl:on-completion><xsl:call-template name='t'/></xsl:on-completion></xsl:iterate>",
            $"<xsl:template name='t'><xsl:value-of select='{select}'/></xsl:template>")
            .Replace("<xsl:mode streamable=\"yes\"/>", "<xsl:mode name='s' streamable='yes'/>");
        foreach (bool forward in new[] { false, true })
            Assert.Contains("XPDY0002", Assert.Throws<XsltDynamicException>(() => Run(sheet, "<doc/>", forward)).Message);
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void SavedAssembliesKeepTheBeforeNextGateAndIterationState()
    {
        string sheet = Sheet("""
            <xsl:iterate select="doc/row ! (if (@bad) then error() else .)"><xsl:param name="sum" select="0" as="xs:decimal"/>
              <xsl:on-completion>wrong</xsl:on-completion>
              <xsl:choose><xsl:when test="position() = 2"><xsl:break select="$sum + xs:decimal(.)"/></xsl:when>
                <xsl:otherwise><xsl:next-iteration><xsl:with-param name="sum" select="xs:decimal(.)"/></xsl:next-iteration></xsl:otherwise>
              </xsl:choose>
            </xsl:iterate>
            """);
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null,
            "Xsl3.Tests.StreamedIterate", "Saved.Iterate", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Iterate", true)!, settings);
        using var input = XmlReader.Create(new StringReader("<doc><row>1.25</row><row>2.75</row><row bad='yes'/></doc>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("4", output.ToString());
    }
#endif
}
