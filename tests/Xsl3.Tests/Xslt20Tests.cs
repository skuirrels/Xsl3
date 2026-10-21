// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using Xsl3.Xslt;
using Xunit;

namespace Xsl3.Tests;

/// <summary>XSLT 2.0 stylesheets compiled by the 2.0 path of QilGenerator (docs/plan/p2-xslt20.md).</summary>
public class Xslt20Tests
{
    private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

    private static string Run(string templates, string input = "<doc/>", string extra = "", string output = "<xsl:output method='xml' omit-xml-declaration='yes'/>")
    {
        string stylesheet = $"<xsl:stylesheet version='2.0' xmlns:xsl='{Xsl}' xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:f='urn:f' exclude-result-prefixes='xs f' {extra}>" +
                            output + templates + "</xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var sw = new StringWriter();
        using (XmlWriter w = XmlWriter.Create(sw, transform.OutputSettings))
        {
            transform.Transform(XmlReader.Create(new StringReader(input)), w);
        }
        return sw.ToString();
    }

    [Fact]
    public void SequencesOfAtomicValuesBecomeSpaceSeparatedText() =>
        Assert.Equal("<out>1 2 3<b>x</b>4</out>", Run("<xsl:template match='/'><out><xsl:sequence select='1 to 3'/><b>x</b><xsl:sequence select='4'/></out></xsl:template>"));

    [Fact]
    public void ValueOfSeparator() =>
        Assert.Equal("<out>a,b,c</out>", Run("<xsl:template match='/'><out><xsl:value-of select=\"('a','b','c')\" separator=','/></out></xsl:template>"));

    [Fact]
    public void VariablesWithAsHoldSequences() =>
        Assert.Equal("<out>3:6</out>", Run(
            "<xsl:template match='/'><xsl:variable name='s' as='xs:integer*' select='1 to 3'/>" +
            "<out><xsl:value-of select=\"count($s), sum($s)\" separator=':'/></out></xsl:template>"));

    [Fact]
    public void TemporaryTreesAreNavigable() =>
        Assert.Equal("<out>2</out>", Run(
            "<xsl:template match='/'><xsl:variable name='t'><a/><a/></xsl:variable><out><xsl:value-of select='count($t/a)'/></out></xsl:template>"));

    [Fact]
    public void ForEachOverAtomicValues() =>
        Assert.Equal("<out><i>1</i><i>4</i><i>9</i></out>", Run(
            "<xsl:template match='/'><out><xsl:for-each select='1 to 3'><i><xsl:value-of select='. * .'/></i></xsl:for-each></out></xsl:template>"));

    [Fact]
    public void StylesheetFunctions() =>
        Assert.Equal("<out>120</out>", Run(
            "<xsl:function name='f:fact' as='xs:integer'><xsl:param name='n' as='xs:integer'/>" +
            "<xsl:sequence select='if ($n le 1) then 1 else $n * f:fact($n - 1)'/></xsl:function>" +
            "<xsl:template match='/'><out><xsl:value-of select='f:fact(5)'/></out></xsl:template>"));

    [Fact]
    public void ForEachGroupBy() =>
        Assert.Equal("<out><g k=\"a\">1 3</g><g k=\"b\">2</g></out>", Run(
            "<xsl:template match='/'><out><xsl:for-each-group select='*/i' group-by='@k'>" +
            "<g k='{current-grouping-key()}'><xsl:value-of select='current-group()'/></g></xsl:for-each-group></out></xsl:template>",
            "<doc><i k='a'>1</i><i k='b'>2</i><i k='a'>3</i></doc>"));

    [Fact]
    public void ForEachGroupStartingWith() =>
        Assert.Equal("<out><s>h1 p1 p2</s><s>h2 p3</s></out>", Run(
            "<xsl:template match='/'><out><xsl:for-each-group select='*/*' group-starting-with='h'>" +
            "<s><xsl:value-of select='current-group()'/></s></xsl:for-each-group></out></xsl:template>",
            "<doc><h>h1</h><p>p1</p><p>p2</p><h>h2</h><p>p3</p></doc>"));

    [Fact]
    public void AnalyzeString() =>
        Assert.Equal("<out>[12]-[345]x</out>", Run(
            "<xsl:template match='/'><out><xsl:analyze-string select=\"'12-345x'\" regex='[0-9]+'>" +
            "<xsl:matching-substring>[<xsl:value-of select='regex-group(0)'/>]</xsl:matching-substring>" +
            "<xsl:non-matching-substring><xsl:value-of select='.'/></xsl:non-matching-substring></xsl:analyze-string></out></xsl:template>"));

    [Fact]
    public void PerformSortAndTypedSortKeys() =>
        Assert.Equal("<out>2 10 33</out>", Run(
            "<xsl:template match='/'><out><xsl:perform-sort select='(33, 2, 10)'><xsl:sort select='.'/></xsl:perform-sort></out></xsl:template>"));

    [Fact]
    public void DescendingSort() =>
        Assert.Equal("<out>c b a</out>", Run(
            "<xsl:template match='/'><out><xsl:perform-sort select=\"('b', 'c', 'a')\"><xsl:sort select='.' order='descending'/></xsl:perform-sort></out></xsl:template>"));

    [Fact]
    public void TunnelParameters() =>
        Assert.Equal("<out>deep</out>", Run(
            "<xsl:template match='/'><out><xsl:apply-templates select='doc'><xsl:with-param name='t' select=\"'deep'\" tunnel='yes'/></xsl:apply-templates></out></xsl:template>" +
            "<xsl:template match='doc'><xsl:apply-templates select='a'/></xsl:template>" +
            "<xsl:template match='a'><xsl:param name='t' tunnel='yes'/><xsl:value-of select='$t'/></xsl:template>",
            "<doc><a/></doc>"));

    [Fact]
    public void TunnelParametersThroughCallTemplate() =>
        Assert.Equal("<out>deep</out>", Run(
            "<xsl:template match='/'><out><xsl:call-template name='t1'><xsl:with-param name='t' select=\"'deep'\" tunnel='yes'/></xsl:call-template></out></xsl:template>" +
            "<xsl:template name='t1'><xsl:call-template name='t2'/></xsl:template>" +
            "<xsl:template name='t2'><xsl:param name='t' tunnel='yes'/><xsl:value-of select='$t'/></xsl:template>"));

    [Fact]
    public void TunnelParameterDirect() =>
        Assert.Equal("<out>deep</out>", Run(
            "<xsl:template match='/'><out><xsl:call-template name='t2'><xsl:with-param name='t' select=\"'deep'\" tunnel='yes'/></xsl:call-template></out></xsl:template>" +
            "<xsl:template name='t2'><xsl:param name='t' tunnel='yes'/><xsl:value-of select='$t'/></xsl:template>"));

    [Fact]
    public void NextMatch() =>
        Assert.Equal("<out><outer><inner /></outer></out>", Run(
            "<xsl:template match='/'><out><xsl:apply-templates select='doc/a'/></out></xsl:template>" +
            "<xsl:template match='a' priority='2'><outer><xsl:next-match/></outer></xsl:template>" +
            "<xsl:template match='a'><inner/></xsl:template>",
            "<doc><a/></doc>"));

    [Fact]
    public void AttributeSelectAndSeparator() =>
        Assert.Equal("<out a=\"1-2-3\" />", Run(
            "<xsl:template match='/'><out><xsl:attribute name='a' select='1 to 3' separator='-'/></out></xsl:template>"));

    [Fact]
    public void XPathDefaultNamespace() =>
        Assert.Equal("<out>2</out>", Run(
            "<xsl:template match='/' xpath-default-namespace='urn:x'><out><xsl:value-of select='count(doc/a)'/></out></xsl:template>",
            "<doc xmlns='urn:x'><a/><a/></doc>"));

    [Fact]
    public void ShadowedLocalVariables() =>
        Assert.Equal("<out>2</out>", Run(
            "<xsl:template match='/'><xsl:variable name='v' select='1'/><xsl:variable name='v' select='$v + 1'/><out><xsl:value-of select='$v'/></out></xsl:template>"));

    [Fact]
    public void XPath31FunctionsInStylesheets() =>
        Assert.Equal("<out>2002-12-31 HELLO</out>", Run(
            "<xsl:template match='/'><out><xsl:value-of select=\"format-date(xs:date('2002-12-31'), '[Y]-[M01]-[D01]'), upper-case('hello')\"/></out></xsl:template>"));

    [Fact]
    public void PatternsWithVariablesAndCurrent() =>
        Assert.Equal("<out>[b][c]</out>", Run(
            "<xsl:variable name='skip' select=\"'a'\"/>" +
            "<xsl:template match='/'><out><xsl:apply-templates select='doc/*'/></out></xsl:template>" +
            "<xsl:template match='*[name() = $skip]'/>" +
            "<xsl:template match='*[name() = name(current())]' priority='-1'>[<xsl:value-of select='name()'/>]</xsl:template>",
            "<doc><a/><b/><c/></doc>"));

    [Fact]
    public void PositionalPatternPredicates() =>
        Assert.Equal("<out>first,last</out>", Run(
            "<xsl:template match='/'><out><xsl:apply-templates select='doc/i'/></out></xsl:template>" +
            "<xsl:template match='i[1]'>first,</xsl:template>" +
            "<xsl:template match='i[last()]'>last</xsl:template>" +
            "<xsl:template match='i'/>",
            "<doc><i/><i/><i/></doc>"));

    [Fact]
    public void KeysCompareTypedValues() =>
        Assert.Equal("<out>2|0</out>", Run(
            "<xsl:key name='k' match='i' use='xs:integer(@n)'/>" +
            "<xsl:template match='/'><out><xsl:value-of select=\"count(key('k', 1)), count(key('k', '1'))\" separator='|'/></out></xsl:template>",
            "<doc><i n='1'/><i n='01'/><i n='2'/></doc>"));

    [Fact]
    public void KeyWithContentAndThirdArgument() =>
        Assert.Equal("<out>1</out>", Run(
            "<xsl:key name='k' match='i'><xsl:sequence select='string(@n)'/></xsl:key>" +
            "<xsl:template match='/'><out><xsl:value-of select=\"count(key('k', 'x', doc/s[2]))\"/></out></xsl:template>",
            "<doc><s><i n='x'/></s><s><i n='x'/></s></doc>"));

    [Fact]
    public void UseWhenExcludesElements() =>
        Assert.Equal("<out>2.0+</out>", Run(
            "<xsl:template match='/'><out>" +
            "<xsl:value-of select=\"'1.0'\" use-when=\"system-property('xsl:version') = '1.0'\"/>" +
            "<xsl:value-of select=\"'2.0+'\" use-when=\"number(system-property('xsl:version')) ge 2\"/>" +
            "</out></xsl:template>"));

    [Fact]
    public void ModeCurrent() =>
        Assert.Equal("<out>m:b</out>", Run(
            "<xsl:template match='/'><out><xsl:apply-templates select='doc' mode='m'/></out></xsl:template>" +
            "<xsl:template match='doc' mode='m'><xsl:apply-templates mode='#current'/></xsl:template>" +
            "<xsl:template match='b' mode='m'>m:b</xsl:template>",
            "<doc><b/></doc>"));

    [Fact]
    public void NumberLevelAnyCountsFromTheStartWithoutAFromNode() =>
        Assert.Equal("<out>1 2 1</out>", Run(
            "<xsl:template match='/'><out><xsl:for-each select='//i'><xsl:if test='position() > 1'><xsl:text> </xsl:text></xsl:if><xsl:number level='any' from='s'/></xsl:for-each></out></xsl:template>",
            "<doc><i/><i/><s/><i/></doc>"));

    [Fact]
    public void XhtmlOutputMethod() =>
        Assert.Equal("<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\" /></head><body><br /><p></p></body></html>", Run(
            "<xsl:template match='/'><html xmlns='http://www.w3.org/1999/xhtml'><head/><body><br/><p/></body></html></xsl:template>",
            output: "<xsl:output method='xhtml' omit-xml-declaration='yes' indent='no'/>"));

    [Fact]
    public void CharacterMaps() =>
        Assert.Equal("<out>&lt;[nbsp]&gt;</out>", Run(
            "<xsl:character-map name='m'><xsl:output-character character='&#160;' string='[nbsp]'/></xsl:character-map>" +
            "<xsl:template match='/'><out>&lt;&#160;&gt;</out></xsl:template>",
            output: "<xsl:output method='xml' omit-xml-declaration='yes' use-character-maps='m'/>"));

    [Fact]
    public void DisableOutputEscapingStreamsThroughTemplates() =>
        Assert.Equal("<out><b/></out>", Run(
            "<xsl:template match='/'><out><xsl:apply-templates/></out></xsl:template>" +
            "<xsl:template match='doc'><xsl:value-of select=\"'&lt;b/&gt;'\" disable-output-escaping='yes'/></xsl:template>"));

    [Fact]
    public void TemplatesReturningAtomicValues() =>
        Assert.Equal("<out>6</out>", Run(
            "<xsl:template match='/'><xsl:variable name='v' as='xs:integer*'><xsl:apply-templates select='doc/i'/></xsl:variable><out><xsl:value-of select='sum($v)'/></out></xsl:template>" +
            "<xsl:template match='i'><xsl:sequence select='xs:integer(@n)'/></xsl:template>",
            "<doc><i n='1'/><i n='2'/><i n='3'/></doc>"));

    private static string RunToText(string templates)
    {
        string stylesheet = $"<xsl:stylesheet version='2.0' xmlns:xsl='{Xsl}'>" + templates + "</xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var sw = new StringWriter();
        transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, sw, null);
        return sw.ToString();
    }

    [Fact]
    public void ResultDocumentWithoutHrefConfiguresThePrincipalResult() =>
        Assert.Equal("<!DOCTYPE out SYSTEM \"out.dtd\"><out>hello</out>", RunToText(
            "<xsl:template match='/'><xsl:result-document omit-xml-declaration='yes' doctype-system='out.dtd'><out>hello</out></xsl:result-document></xsl:template>"));

    [Fact]
    public void OtherOutputBesidesAPrincipalResultDocumentIsAnError()
    {
        var e = Assert.ThrowsAny<System.Exception>(() => RunToText(
            "<xsl:template match='/'><xsl:result-document><a/></xsl:result-document><b/></xsl:template>"));
        Assert.Contains("XTDE1490", e.ToString());
    }

    [Fact]
    public void Utf8OutputHasNoByteOrderMarkAndNamesTheEncodingAsDeclared()
    {
        string stylesheet = $"<xsl:stylesheet version='2.0' xmlns:xsl='{Xsl}'><xsl:output encoding='UTF-8'/><xsl:template match='/'><out/></xsl:template></xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var bytes = new MemoryStream();
        transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, bytes);
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?><out/>", System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
    }
}
