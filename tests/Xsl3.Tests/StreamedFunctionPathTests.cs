// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Xml;
using Xunit;
using static Xsl3.Tests.ForwardEvidence;

namespace Xsl3.Tests;

public class StreamedFunctionPathTests
{
    private const string Input = "<doc><section><section><p>a</p><p>b</p></section><section><p>c</p><p>d</p></section></section><section><section><p>e</p><p>f</p></section></section></doc>";

    [Theory]
    [InlineData("filter", "$node[self::section]", "node()", "doc/f:select(section[1])/section/p[2]", "bd")]
    [InlineData("filter", "$node[self::section]", "node()?", "doc/f:select(section[1])/section/p[2]", "bd")]
    [InlineData("shallow-descent", "$node/section", "node()*", "doc/f:select(section[1])/p[2]", "bd")]
    [InlineData("shallow-descent", "$node/section/section", "element()*", "doc/f:select(.)/p", "abcdef")]
    [InlineData("deep-descent", "<xsl:for-each select='$node'><xsl:sequence select='self::node()//section//p'/></xsl:for-each>", "node()*", "outermost(doc/f:select(section[1]))/text()", "abcd")]
    [InlineData("filter", "$node", "node()", "outermost(//section/section)/p[2]", "bdf")]
    public void TransmittedFunctionPathsConsumeEachNodeWhileCurrent(string category, string body, string result, string select, string expected)
    {
        string sheet = Sheet(category, body, result, select);
        foreach (bool forward in new[] { false, true })
        {
            var executable = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet));
            var transform = executable.CreateTransformer();
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(input)));
        }
    }

    [Fact]
    public void AShortCircuitConsumerEmitsBeforeTheRemainingFunctionPopulationIsRead()
    {
        string sheet = Sheet("shallow-descent", "$node/section", "node()*", "head(doc/f:select(section[1])/p[2])");
        string xml = "<doc><section><section><p>a</p><p>b</p></section>" + string.Concat(Enumerable.Repeat("<section><p>later</p><p>later</p></section>", 20000)) + "</section></doc>";
        using var source = new CountingReader(xml);
        using var reader = XmlReader.Create(source);
        using var output = new EarlyWriter(source);
        new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet))
            .CreateTransformer().ApplyTemplates(reader, output);
        Assert.Equal("b", output.ToString());
        AssertWrittenEarly(output.FirstWrite, xml.Length, forward: true);
    }

    [Theory]
    [InlineData("outermost(($tree/r[2], $tree/r[1]/r, $tree/r[1], $tree/r[1]))/@id", "ac")]
    [InlineData("outermost(($tree/r[2], $tree/r[1]/r, $tree/r[1], $tree/r[1]))/descendant-or-self::r/@id", "abc")]
    [InlineData("outermost($tree/r/..)/r/@id", "ac")]
    [InlineData("outermost(())", "")]
    public void GroundedArgumentsRetainOrderingIdentityAndAncestorRules(string select, string expected)
    {
        string sheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:template match="/"><xsl:variable name="tree" select="copy-of(doc)"/>
                <xsl:value-of select="{{select}}" separator=""/>
              </xsl:template>
            </xsl:stylesheet>
            """;
        foreach (bool forward in new[] { false, true })
        {
            using var reader = XmlReader.Create(new StringReader("<doc><r id='a'><r id='b'/></r><r id='c'/></doc>"));
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet)).CreateTransformer();
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(reader)));
        }
    }

    [Fact]
    public void FunctionCategoriesDoNotEstablishTheOrderOfMixedGroundedResults()
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:f="urn:test">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:variable name="fallback"><r><p>Z</p></r></xsl:variable>
              <xsl:function name="f:select" streamability="filter" as="node()">
                <xsl:param name="node" as="node()"/>
                <xsl:sequence select="if ($node/@keep) then $node else $fallback/r"/>
              </xsl:function>
              <xsl:template match="/">
                <xsl:variable name="tree" select="copy-of(doc)"/>
                <xsl:value-of select="$tree/r/f:select(.)/p" separator=""/>
              </xsl:template>
            </xsl:stylesheet>
            """;
        foreach (bool forward in new[] { false, true })
        {
            using var reader = XmlReader.Create(new StringReader("<doc><r keep='yes'><p>a</p></r><r/><r/><r keep='yes'><p>d</p></r></doc>"));
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet)).CreateTransformer();
            Assert.Contains(string.Concat(transform.ApplyTemplates(reader)), new[] { "Zad", "adZ" });
        }
    }

    [Theory]
    [InlineData("outermost(1)", "XPTY0004")]
    [InlineData("outermost((doc/r, 1))", "XPTY0004")]
    public void OutermostRetainsNonNodeArgumentErrors(string select, string code)
    {
        string sheet = Sheet("filter", "$node", "node()", select);
        using var reader = XmlReader.Create(new StringReader("<doc><r/></doc>"));
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        Assert.Contains(code, Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(reader)).Message);
    }

    // S4-21, S4-22, S4-24 and S4-25: a stylesheet function of each category, called on the streamed context node of a
    // streamable-mode template rule, through both public APIs. Forward execution writes the first row's result
    // before reading half of the input; tree execution, the control, only writes once the whole input is read.
    [Theory]
    [InlineData("inspection", "row", "local-name($node)", "xs:string", "f:select(.)", "row", false)]
    [InlineData("inspection", "row", "local-name($node)", "xs:string", "f:select(.)", "row", true)]
    [InlineData("ascent", "row", "$node/..", "element()?", "f:select(.)/@id", "s1", false)]
    [InlineData("ascent", "row", "$node/..", "element()?", "f:select(.)/@id", "s1", true)]
    [InlineData("deep-descent", "section", "$node//v", "element()*", "f:select(.)", "ab", false)]
    [InlineData("deep-descent", "section", "$node//v", "element()*", "f:select(.)", "ab", true)]
    [InlineData("filter", "section", "$node[@id]", "element()?", "f:select(.)//v", "ab", false)]
    [InlineData("filter", "section", "$node[@id]", "element()?", "f:select(.)//v", "ab", true)]
    public void FunctionCategoriesRunForwardInStreamableTemplateRules(string category, string match, string body, string result, string select, string first, bool legacy)
    {
        string sheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema"
                xmlns:f="urn:test" exclude-result-prefixes="xs f">
              <xsl:mode streamable="yes" on-no-match="shallow-skip"/><xsl:output method="text"/>
              <xsl:function name="f:select" streamability="{{category}}" as="{{result}}">
                <xsl:param name="node" as="node()"/><xsl:sequence select="{{body}}"/>
              </xsl:function>
              <xsl:template match="{{match}}"><xsl:value-of select="{{select}}" separator=""/>;</xsl:template>
            </xsl:stylesheet>
            """;
        string section = "<section id='s{0}'><row><v>{1}</v></row><row><v>b</v></row></section>";
        string xml = "<doc>" + string.Format(section, 1, "a") + string.Concat(Enumerable.Range(2, 19999).Select(i => string.Format(section, i, "c"))) + "</doc>";
        int rows = match == "row" ? 2 : 1;
        foreach (bool forward in new[] { false, true })
        {
            using var source = new CountingReader(xml);
            using var output = new EarlyWriter(source);
            Run(sheet, source, output, forward, legacy);
            string[] results = output.ToString().Split(';', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(20000 * rows, results.Length);
            Assert.Equal(first, results[0]);
            AssertWrittenEarly(output.FirstWrite, xml.Length, forward, category + ":");
        }
    }

    private static void Run(string sheet, CountingReader source, EarlyWriter output, bool forward, bool legacy)
    {
        var settings = new XsltSettings { StreamedExecution = forward };
        using var reader = XmlReader.Create(source);
        if (legacy)
        {
            var transform = new XslCompiledTransform();
            using var stylesheet = XmlReader.Create(new StringReader(sheet));
            transform.Load(stylesheet, settings, null);
            transform.Transform(reader, null, output);
        }
        else
        {
            new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(reader, output);
        }
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData("filter", "$node[self::section]", "node()", "doc/f:select(section[1])/section/p[2]", "bd")]
    [InlineData("shallow-descent", "$node/section/section", "element()*", "doc/f:select(.)/p", "abcdef")]
    [InlineData("deep-descent", "<xsl:for-each select='$node'><xsl:sequence select='self::node()//section//p'/></xsl:for-each>", "node()*", "outermost(doc/f:select(section[1]))/text()", "abcd")]
    [InlineData("filter", "$node", "node()", "outermost(//section/section)/p[2]", "bdf")]
    public void SavedAssembliesPreserveFunctionPathAndPruningContracts(string category, string body, string result, string select, string expected)
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        using var stylesheet = XmlReader.Create(new StringReader(Sheet(category, body, result, select)));
        new XslCompiledTransform().CompileToAssembly(stylesheet, settings, null, "Xsl3.Tests.FunctionPath", "Saved.FunctionPath", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.FunctionPath", true)!, settings);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal(expected, output.ToString());
    }
#endif

    private static string Sheet(string category, string body, string result, string select) => $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:f="urn:test">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:function name="f:select" streamability="{{category}}" as="{{result}}">
                <xsl:param name="node" as="node()"/>{{(body.StartsWith("<") ? body : "<xsl:sequence select=\"" + body + "\"/>")}}
              </xsl:function>
              <xsl:template match="/"><xsl:value-of select="{{select}}" separator=""/></xsl:template>
            </xsl:stylesheet>
            """;

}
