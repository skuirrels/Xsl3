// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;
using Xsl3.Xslt;
using Xunit;

namespace Xsl3.Tests;

/// <summary>XSLT 3.0 instructions and functions (docs/plan/p3-xslt30.md).</summary>
public class Xslt30Tests
{
    private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

    private static string Run(string templates, string input = "<doc/>", Dictionary<XmlQualifiedName, IList<XPathItem>>? staticParameters = null)
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:xs='http://www.w3.org/2001/XMLSchema' " +
                            "xmlns:err='http://www.w3.org/2005/xqt-errors' exclude-result-prefixes='xs err'>" +
                            "<xsl:output method='xml' omit-xml-declaration='yes'/>" + templates + "</xsl:stylesheet>";
        var transform = new XslCompiledTransform { StaticParameters = staticParameters };
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var sw = new StringWriter();
        using (XmlWriter w = XmlWriter.Create(sw, transform.OutputSettings))
        {
            transform.Transform(XmlReader.Create(new StringReader(input)), w);
        }
        return sw.ToString();
    }

    [Fact]
    public void TryCatchesDynamicErrorsWithTheirCode() =>
        Assert.Equal("<out><a v=\"-10\" /><caught pos=\"2\" code=\"err:FOAR0001\" /><a v=\"10\" /></out>", Run(
            "<xsl:template match='/'><out><xsl:for-each select='1 to 3'><xsl:try><a v='{10 idiv (. - 2)}'/>" +
            "<xsl:catch errors='err:FOAR0001'><caught pos='{position()}' code='{$err:code}'/></xsl:catch></xsl:try></xsl:for-each></out></xsl:template>"));

    [Fact]
    public void TryChoosesTheCatchMatchingTheErrorAndSeesOuterVariables() =>
        Assert.Equal("<out>3.14 boom</out>", Run(
            "<xsl:template match='/'><xsl:variable name='pi' select='3.14'/><out><xsl:try select=\"error(QName('urn:x', 'x:E'), 'boom')\">" +
            "<xsl:catch errors='err:*' select=\"'wrong'\"/><xsl:catch errors='*:E' select='$pi, $err:description'/></xsl:try></out></xsl:template>"));

    [Fact]
    public void ErrorsNotMatchedByAnyCatchPropagate() =>
        Assert.ThrowsAny<System.Exception>(() => Run(
            "<xsl:template match='/'><xsl:try select='1 idiv 0'><xsl:catch errors='err:XPTY0004'/></xsl:try></xsl:template>"));

    [Fact]
    public void ApplyTemplatesToAtomicValuesWithPredicatePatterns() =>
        Assert.Equal("<out><i>1</i><d>1.5</d>x</out>", Run(
            "<xsl:template match='/'><out><xsl:apply-templates select=\"1, 1.5, 'x'\"/></out></xsl:template>" +
            "<xsl:template match='.[. instance of xs:integer]' priority='2'><i><xsl:value-of select='.'/></i></xsl:template>" +
            "<xsl:template match='.[. instance of xs:decimal]'><d><xsl:value-of select='.'/></d></xsl:template>"));

    [Fact]
    public void NextMatchWithAtomicContextItems() =>
        Assert.Equal("<out><a><b>7</b></a></out>", Run(
            "<xsl:template match='/'><out><xsl:apply-templates select='7'/></out></xsl:template>" +
            "<xsl:template match='.[. instance of xs:integer]' priority='3'><a><xsl:next-match/></a></xsl:template>" +
            "<xsl:template match='.'><b><xsl:value-of select='.'/></b></xsl:template>"));

    [Fact]
    public void SnapshotKeepsAncestors() =>
        Assert.Equal("<out>c b a</out>", Run(
            "<xsl:template match='/'><out><xsl:value-of select='snapshot(//c)/ancestor-or-self::*/name() => reverse()'/></out></xsl:template>",
            "<a><b><c/></b><z/></a>"));

    [Fact]
    public void CopyOfMakesNewNodes() =>
        Assert.Equal("<out>false 1</out>", Run(
            "<xsl:template match='/'><out><xsl:value-of select='copy-of(/a) is /a, count(copy-of(/a)/b)'/></out></xsl:template>",
            "<a><b/></a>"));

    [Fact]
    public void StaticParametersFeedUseWhenAndShadowAttributes() =>
        Assert.Equal("<out>on:3</out>", Run(
            "<xsl:param name='mode' static='yes' as='xs:string' select=\"'off'\"/><xsl:variable name='n' static='yes' select='3'/>" +
            "<xsl:template match='/'><out><xsl:value-of use-when=\"$mode = 'on'\" _select=\"'{$mode}:{$n}'\"/></out></xsl:template>",
            staticParameters: new() { [new XmlQualifiedName("mode")] = new XPathItem[] { AtomicValue.OfString("on") } }));

    [Fact]
    public void OnEmptyAndOnNonEmpty() =>
        Assert.Equal("<out><x>none</x><y><h />1</y></out>", Run(
            "<xsl:template match='/'><out><x><xsl:sequence select='()'/><xsl:on-empty>none</xsl:on-empty></x>" +
            "<y><xsl:on-non-empty><h/></xsl:on-non-empty><xsl:sequence select='1'/><xsl:on-empty>none</xsl:on-empty></y></out></xsl:template>"));

    [Fact]
    public void WherePopulatedDropsEmptyElements() =>
        Assert.Equal("<out><b>1</b></out>", Run(
            "<xsl:template match='/'><out><xsl:where-populated><a/><b>1</b></xsl:where-populated></out></xsl:template>"));

    [Fact]
    public void IterateWithParametersBreakAndCompletion() =>
        Assert.Equal("<out><i p=\"1\">1</i><i p=\"2\">3</i><stop sum=\"3\" /></out><out>2 4 6</out>", Run(
            "<xsl:template match='/'><out><xsl:iterate select='1 to 10'><xsl:param name='sum' as='xs:integer' select='0'/>" +
            "<xsl:on-completion><total/></xsl:on-completion>" +
            "<xsl:choose><xsl:when test='. gt 2'><xsl:break><stop sum='{$sum}'/></xsl:break></xsl:when>" +
            "<xsl:otherwise><i p='{position()}'><xsl:value-of select='. + $sum'/></i>" +
            "<xsl:next-iteration><xsl:with-param name='sum' select='$sum + .'/></xsl:next-iteration></xsl:otherwise></xsl:choose></xsl:iterate></out>" +
            "<out><xsl:iterate select='1 to 3'><xsl:param name='acc' select='()'/><xsl:on-completion select='$acc'/>" +
            "<xsl:next-iteration><xsl:with-param name='acc' select='$acc, . * 2'/></xsl:next-iteration></xsl:iterate></out></xsl:template>"));

    [Fact]
    public void NextIterationMustBeInATailPosition() =>
        Assert.ThrowsAny<System.Exception>(() => Run(
            "<xsl:template match='/'><xsl:iterate select='1 to 3'><xsl:if test='. = 2'><xsl:break/></xsl:if><x/></xsl:iterate></xsl:template>"));

    [Fact]
    public void CopySelectCopiesTheSelectedItem() =>
        Assert.Equal("<out><b x=\"1\">y</b></out>", Run(
            "<xsl:template match='/'><out><xsl:copy select='/a/b'><xsl:copy-of select='@x'/>y</xsl:copy></out></xsl:template>",
            "<a><b x='1'><c/></b></a>"));

    [Fact]
    public void DefaultModeAppliesToTemplatesAndApplyTemplates() =>
        Assert.Equal("<m>x</m>", Run(
            "<xsl:template match='/' mode='#unnamed'><xsl:apply-templates select='*' default-mode='m'/></xsl:template>" +
            "<xsl:template match='a' default-mode='m'><m><xsl:value-of select='.'/></m></xsl:template>" +
            "<xsl:template match='a'>wrong</xsl:template>",
            "<a>x</a>"));

    [Fact]
    public void SequenceWithContent() =>
        Assert.Equal("<out>1|2</out>", Run(
            "<xsl:template match='/'><out><xsl:sequence><xsl:value-of select='1'/><xsl:text>|</xsl:text><xsl:value-of select='2'/></xsl:sequence></out></xsl:template>"));

    [Fact]
    public void Html5Serialization()
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'><xsl:output method='html' html-version='5'/>" +
                            "<xsl:template match='/'><html><body><wbr/><br/><p/></body></html></xsl:template></xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var sw = new StringWriter();
        transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, sw);
        string html = sw.ToString();
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<br><p></p>", html);
    }

    [Fact]
    public void WhitespaceIsStrippedFromLoadedDocuments()
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'><xsl:strip-space elements='*'/><xsl:output method='text'/>" +
                            "<xsl:template match='/'><xsl:value-of select='count(/a/node())'/></xsl:template></xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var doc = new System.Xml.XPath.XPathDocument(new StringReader("<a> <b/> <c/> </a>"));
        var sw = new StringWriter();
        transform.Transform(doc, null, sw);
        Assert.Equal("2", sw.ToString());
    }

    private static string RunText(string stylesheetBody, string input = "<doc/>")
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:xs='http://www.w3.org/2001/XMLSchema' exclude-result-prefixes='xs'>{stylesheetBody}</xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var sw = new StringWriter();
        transform.Transform(XmlReader.Create(new StringReader(input)), null, sw);
        return sw.ToString();
    }

    [Fact]
    public void JsonOutputOfMapsAndArrays() =>
        Assert.Equal("{\"a\":[1,\"x\\/y\"]}", RunText(
            "<xsl:output method='json'/><xsl:template match='/'><xsl:map><xsl:map-entry key=\"'a'\" select=\"[1, 'x/y']\"/></xsl:map></xsl:template>"));

    [Fact]
    public void AdaptiveOutput() =>
        Assert.Equal("\"a\"\n1\ntrue()\n<e/>", RunText(
            "<xsl:output method='adaptive'/><xsl:template match='/'><xsl:sequence select=\"'a', 1, true()\"/><e/></xsl:template>"));

    [Fact]
    public void SerializeWithAMapOfParameters() =>
        Assert.Equal("<out>[1,2]</out>", Run(
            "<xsl:template match='/'><out><xsl:value-of select=\"serialize([1, 2], map{'method': 'json'})\"/></out></xsl:template>"));

    [Fact]
    public void AccumulatorsCountElementsInDocumentOrder() =>
        Assert.Equal("<out><n b=\"0\" a=\"2\" /><n b=\"2\" a=\"3\" /></out>", Run(
            "<xsl:accumulator name='count' as='xs:integer' initial-value='0'><xsl:accumulator-rule match='i' select='$value + 1'/></xsl:accumulator>" +
            "<xsl:template match='/'><out><xsl:for-each select='//g'><n b=\"{accumulator-before('count')}\" a=\"{accumulator-after('count')}\"/></xsl:for-each></out></xsl:template>",
            "<doc><g><i/><i/></g><g><i/></g></doc>"));

    [Fact]
    public void PackagesHidePrivateComponentsAndHonourOverrides()
    {
        string dir = Path.Combine(Path.GetTempPath(), "xsl3-packages-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string used = Path.Combine(dir, "used.xsl");
            File.WriteAllText(used,
                $"<xsl:package name='urn:used' package-version='1.2.0' version='3.0' xmlns:xsl='{Xsl}' xmlns:u='urn:u'>" +
                "<xsl:function name='u:secret' visibility='private'><xsl:sequence select=\"'secret'\"/></xsl:function>" +
                "<xsl:function name='u:greet' visibility='public'><xsl:param name='n'/><xsl:sequence select=\"u:prefix() || $n || u:secret()\"/></xsl:function>" +
                "<xsl:function name='u:prefix' visibility='public'><xsl:sequence select=\"'hello '\"/></xsl:function>" +
                "</xsl:package>");
            string principal =
                $"<xsl:package name='urn:main' version='3.0' xmlns:xsl='{Xsl}' xmlns:u='urn:u' exclude-result-prefixes='u'>" +
                "<xsl:use-package name='urn:used' package-version='1.*'><xsl:override>" +
                "<xsl:function name='u:prefix' visibility='public'><xsl:sequence select=\"'[' || xsl:original() || ']'\"/></xsl:function>" +
                "</xsl:override></xsl:use-package>" +
                "<xsl:template name='xsl:initial-template' visibility='public'><out><xsl:value-of select=\"u:greet('you'), function-available('u:secret')\"/></out></xsl:template>" +
                "</xsl:package>";
            var transform = new XslCompiledTransform
            {
                InitialTemplate = new XmlQualifiedName("initial-template", Xsl),
                Packages = new List<(string, string, System.Uri)> { ("urn:used", "1.2.0", new System.Uri(used)) },
            };
            transform.Load(XmlReader.Create(new StringReader(principal)), new XsltSettings(), new XmlUrlResolver());
            var sw = new StringWriter();
            transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, sw);
            // The override replaces u:prefix everywhere, even in the used package; u:secret stays private to it.
            Assert.EndsWith("<out>[hello ]yousecret false</out>", sw.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
