// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

/// <summary>The XSLT 3.0 API: XsltCompiler, XsltExecutable, Xslt30Transformer and the XDM values (docs/plan/api.md).</summary>
public class Xslt30ApiTests
{
    private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

    private static XsltExecutable Compile(string body, XsltCompiler? compiler = null) =>
        (compiler ?? new XsltCompiler()).Compile(new StringReader(
            $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:f='urn:f' exclude-result-prefixes='xs f'>" +
            "<xsl:output omit-xml-declaration='yes'/>" + body + "</xsl:stylesheet>"));

    private static XdmNode Parse(string xml) => XdmNode.Load(new StringReader(xml));

    [Fact]
    public void AtomicValuesKeepTheirTypes()
    {
        Assert.Equal(new QName("http://www.w3.org/2001/XMLSchema", "integer"), new XdmAtomicValue(42L).TypeName);
        Assert.Equal(42L, new XdmAtomicValue(42L).Value);
        Assert.Equal(1.5m, new XdmAtomicValue(1.5m).Value);
        Assert.Equal("2026-10-21", new XdmAtomicValue("2026-10-21", new QName("http://www.w3.org/2001/XMLSchema", "date")).StringValue);
        Assert.Throws<ArgumentException>(() => new XdmAtomicValue("21/10/2026", new QName("http://www.w3.org/2001/XMLSchema", "date")));
        Assert.True(new XdmAtomicValue("a").Equals(XdmValue.MakeValue("a")));
    }

    [Fact]
    public void MapsAndArraysAreImmutableValues()
    {
        var map = new XdmMap(new[] { new KeyValuePair<XdmAtomicValue, XdmValue>(new XdmAtomicValue("a"), new XdmAtomicValue(1L)) });
        XdmMap bigger = map.Put(new XdmAtomicValue("b"), XdmValue.MakeValue(new[] { 2, 3 }));
        Assert.Equal(1, map.Size);
        Assert.Equal(2, bigger.Size);
        Assert.Equal("2 3", bigger.Get("b")!.ToString());
        Assert.Null(map.Get("b"));
        var array = new XdmArray(new XdmValue[] { new XdmAtomicValue(1L), XdmValue.Empty });
        Assert.Equal(2, array.Size);
        Assert.Equal(0, array.Get(1).Count);
        Assert.Throws<InvalidOperationException>(() => array.StringValue);
    }

    [Fact]
    public void ApplyTemplatesToANode()
    {
        XsltExecutable executable = Compile("<xsl:template match='doc'><out><xsl:value-of select='count(*)'/></out></xsl:template>");
        var text = new StringWriter();
        executable.CreateTransformer().ApplyTemplates(Parse("<doc><a/><a/></doc>"), text);
        Assert.Equal("<out>2</out>", text.ToString());
    }

    [Fact]
    public void ApplyTemplatesToAtomicValuesInAMode()
    {
        XsltExecutable executable = Compile("<xsl:template match='.[. instance of xs:integer]' mode='m'><xsl:sequence select='. * 10'/></xsl:template>");
        XdmValue result = executable.CreateTransformer().ApplyTemplates(XdmValue.MakeValue(new[] { 1, 2, 3 }), new QName("m"));
        Assert.Equal(new object[] { 10L, 20L, 30L }, result.Select(i => ((XdmAtomicValue)i).Value));
    }

    [Fact]
    public void CallTemplateWithParametersAndATunnel()
    {
        XsltExecutable executable = Compile(
            "<xsl:param name='g' select='1'/>" +
            "<xsl:template name='t'><xsl:param name='p' required='yes'/><xsl:param name='q' tunnel='yes'/><out><xsl:value-of select='$p, $q, $g'/></out></xsl:template>");
        Xslt30Transformer transformer = executable.CreateTransformer();
        transformer.StylesheetParameters[new QName("g")] = new XdmAtomicValue(7L);
        var text = new StringWriter();
        transformer.CallTemplate(new QName("t"), text,
            new Dictionary<QName, XdmValue> { [new QName("p")] = new XdmAtomicValue("x") },
            new Dictionary<QName, XdmValue> { [new QName("q")] = new XdmAtomicValue("y") });
        Assert.Equal("<out>x y 7</out>", text.ToString());
        // A required parameter not given: XTDE0700, as an XsltDynamicException.
        XsltDynamicException e = Assert.Throws<XsltDynamicException>(() => transformer.CallTemplate(new QName("t")));
        Assert.Equal("XTDE0700", e.Code.LocalName);
    }

    [Fact]
    public void CallTemplateDefaultsToTheInitialTemplate()
    {
        XsltExecutable executable = Compile("<xsl:template name='xsl:initial-template'><xsl:sequence select='1 to 3'/></xsl:template>");
        Assert.Equal("1 2 3", executable.CreateTransformer().CallTemplate(null).ToString());
    }

    [Fact]
    public void CallFunctionReturnsItsValue()
    {
        XsltExecutable executable = Compile(
            "<xsl:function name='f:add' visibility='public'><xsl:param name='a'/><xsl:param name='b'/><xsl:sequence select='$a + $b'/></xsl:function>");
        XdmValue sum = executable.CreateTransformer().CallFunction(new QName("urn:f", "add"), new XdmAtomicValue(2L), new XdmAtomicValue(3L));
        Assert.Equal(5L, ((XdmAtomicValue)sum).Value);
        XdmValue map = executable.CreateTransformer().CallFunction(new QName("urn:f", "add"), new XdmAtomicValue(1.5m), new XdmAtomicValue(1L));
        Assert.Equal(2.5m, ((XdmAtomicValue)map).Value);
    }

    [Fact]
    public void ResultsCanBeMaps()
    {
        XsltExecutable executable = Compile("<xsl:template name='xsl:initial-template'><xsl:sequence select=\"map{'a': (1, 2), 'b': [3]}\"/></xsl:template>");
        var map = (XdmMap)executable.CreateTransformer().CallTemplate(null);
        Assert.Equal("1 2", map.Get("a")!.ToString());
        Assert.Equal(3L, ((XdmAtomicValue)((XdmArray)map.Get("b")!).Get(0)).Value);
    }

    [Fact]
    public void DynamicErrorsHaveTheirCodes()
    {
        XsltExecutable executable = Compile(
            "<xsl:template name='xsl:initial-template'><xsl:sequence select=\"error(QName('urn:mine', 'oops'), 'failed', 42)\"/></xsl:template>");
        XsltDynamicException e = Assert.Throws<XsltDynamicException>(() => executable.CreateTransformer().CallTemplate(null));
        Assert.Equal(new QName("urn:mine", "oops"), e.Code);
        Assert.Equal("failed", e.Description);
        Assert.Equal("42", e.ErrorObject.ToString());
    }

    [Fact]
    public void TerminatingMessagesAreDynamicErrors()
    {
        XsltExecutable executable = Compile(
            "<xsl:template name='xsl:initial-template'><xsl:message terminate='yes' error-code='m:stop' xmlns:m='urn:mine'>stopping</xsl:message></xsl:template>");
        var messages = new List<string>();
        Xslt30Transformer transformer = executable.CreateTransformer();
        transformer.MessageHandler = messages.Add;
        XsltDynamicException e = Assert.Throws<XsltDynamicException>(() => transformer.CallTemplate(null));
        Assert.Equal(new QName("urn:mine", "stop"), e.Code);
    }

    [Fact]
    public void StaticErrorsAreListed()
    {
        XsltStaticException e = Assert.Throws<XsltStaticException>(() => Compile("<xsl:template match='/'><xsl:value-of select='$undeclared'/></xsl:template>"));
        Assert.Contains(e.Errors, error => error.Code?.LocalName == "XPST0008" && error.LineNumber > 0);
    }

    [Fact]
    public void StaticParametersAndMessages()
    {
        var compiler = new XsltCompiler();
        compiler.StaticParameters[new QName("debug")] = new XdmAtomicValue(true);
        XsltExecutable executable = Compile(
            "<xsl:param name='debug' static='yes' as='xs:boolean' select='false()'/>" +
            "<xsl:template name='xsl:initial-template'><xsl:message use-when='$debug'>debugging</xsl:message><done/></xsl:template>", compiler);
        var messages = new List<string>();
        Xslt30Transformer transformer = executable.CreateTransformer();
        transformer.MessageHandler = messages.Add;
        Assert.Equal("<done />", transformer.CallTemplate(null).ToString());
        Assert.Equal(new[] { "debugging" }, messages);
    }

    [Fact]
    public void SecondaryResultsGoToTheHandlerUnderThisRunsBaseOutputUri()
    {
        XsltExecutable executable = Compile(
            "<xsl:template name='xsl:initial-template'><xsl:result-document href='part.xml'><part/></xsl:result-document><main/></xsl:template>");
        var parts = new Dictionary<Uri, StringWriter>();
        Xslt30Transformer transformer = executable.CreateTransformer();
        transformer.BaseOutputUri = new Uri("file:///out/main.xml");
        transformer.ResultDocumentHandler = uri => parts[uri] = new StringWriter();
        var text = new StringWriter();
        transformer.CallTemplate(null, text);
        Assert.Equal("<main />", text.ToString());
        Assert.Contains("<part", parts[new Uri("file:///out/part.xml")].ToString());
    }

    [Fact]
    public void AnExecutableServesManyTransformersAndEntryPoints()
    {
        XsltExecutable executable = Compile(
            "<xsl:template match='/'><a/></xsl:template><xsl:template name='b'><b/></xsl:template>");
        XdmNode input = Parse("<doc/>");
        string[] results = Enumerable.Range(0, 8).AsParallel().Select(i =>
        {
            Xslt30Transformer t = executable.CreateTransformer();
            return i % 2 == 0 ? t.ApplyTemplates(input).ToString() : t.CallTemplate(new QName("b")).ToString();
        }).ToArray();
        Assert.All(results.Where((_, i) => i % 2 == 0), r => Assert.Equal("<a />", r));
        Assert.All(results.Where((_, i) => i % 2 == 1), r => Assert.Equal("<b />", r));
    }
}
