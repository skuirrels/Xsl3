// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Xml;
using Xunit;

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
        Assert.InRange(output.FirstWriteCharacters, 1, xml.Length / 2);
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

    private sealed class CountingReader(string xml) : TextReader
    {
        private readonly StringReader _source = new(xml);
        internal int Characters;
        public override int Peek() => _source.Peek();
        public override int Read() { int value = _source.Read(); if (value >= 0) Characters++; return value; }
        public override int Read(char[] buffer, int index, int count) { int read = _source.Read(buffer, index, count); Characters += read; return read; }
        public override int Read(Span<char> buffer) { int read = _source.Read(buffer); Characters += read; return read; }
        protected override void Dispose(bool disposing) { if (disposing) _source.Dispose(); base.Dispose(disposing); }
    }

    private sealed class EarlyWriter(CountingReader reader) : StringWriter
    {
        internal int FirstWriteCharacters = -1;
        private void Capture() { if (FirstWriteCharacters < 0) FirstWriteCharacters = reader.Characters; }
        public override void Write(char value) { Capture(); base.Write(value); }
        public override void Write(string? value) { if (!string.IsNullOrEmpty(value)) Capture(); base.Write(value); }
        public override void Write(char[] buffer, int index, int count) { if (count != 0) Capture(); base.Write(buffer, index, count); }
        public override void Write(ReadOnlySpan<char> buffer) { if (!buffer.IsEmpty) Capture(); base.Write(buffer); }
    }

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
