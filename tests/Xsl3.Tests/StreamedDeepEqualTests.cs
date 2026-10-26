// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Reflection;
using System.Security;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedDeepEqualTests
{
    private const string Nested = "<n><n>1</n><n>2</n></n>";
    private const string NestedSequence = Nested + "<n>1</n><n>2</n>";

    [Theory]
    [InlineData("doc/row", "<doc><row>one</row><row>two</row></doc>", "<row>one</row><row>two</row>", true)]
    [InlineData("doc/row", "<doc><row>one</row><row>two</row></doc>", "<row>one</row><row>three</row>", false)]
    [InlineData("doc/row", "<doc><row>one</row><row>two</row></doc>", "<row>one</row>", false)]
    [InlineData("doc/row", "<doc><row>one</row></doc>", "<row>one</row><row>two</row>", false)]
    [InlineData("doc/row", "<doc/>", "", true)]
    [InlineData("doc/row", "<doc/>", "<row/>", false)]
    [InlineData("doc/row", "<doc><row/></doc>", "", false)]
    [InlineData("doc/row", "<doc><row a='1' b='2'><a>one</a><b>two</b></row></doc>", "<row b='2' a='1'><a>one</a><b>two</b></row>", true)]
    [InlineData("doc/row", "<doc><row a='1'>one</row></doc>", "<row a='2'>one</row>", false)]
    [InlineData("doc/row", "<doc><row a='1'>one</row></doc>", "<row>one</row>", false)]
    [InlineData("doc/row", "<doc><row>one<!--ignored--><?p ignored?><n>two</n></row></doc>", "<row>one<n>two</n><!--different--></row>", true)]
    [InlineData("doc/row", "<doc><row><n>one</n>tail</row></doc>", "<row><n>one</n>different</row>", false)]
    [InlineData("doc/row", "<doc><row><n>one</n></row></doc>", "<row><other>one</other></row>", false)]
    [InlineData("doc/row", "<doc><row><n/></row></doc>", "<row><n/><n/></row>", false)]
    [InlineData("doc/row", "<doc><row><n/><n/></row></doc>", "<row><n/></row>", false)]
    [InlineData("doc/row", "<doc><row><p:n xmlns:p='urn:test'>one</p:n></row></doc>", "<row><q:n xmlns:q='urn:test'>one</q:n></row>", true)]
    [InlineData("doc/row/text()", "<doc><row>one<!--split-->two</row></doc>", "<xsl:sequence select=\"parse-xml('&lt;row&gt;one&lt;!--split--&gt;two&lt;/row&gt;')/row/text()\"/>", true)]
    [InlineData("doc/row/@a", "<doc><row a='one'/><row a='two'/></doc>", "<xsl:sequence select=\"parse-xml('&lt;row a=&quot;one&quot;/&gt;')/row/@a, parse-xml('&lt;row a=&quot;two&quot;/&gt;')/row/@a\"/>", true)]
    [InlineData("doc/row/xs:integer(.)", "<doc><row>1</row><row>2</row></doc>", "<xsl:sequence select='1,2'/>", true)]
    [InlineData("//n", Nested, NestedSequence, true)]
    [InlineData("descendant::n", Nested, NestedSequence, true)]
    [InlineData("descendant-or-self::n", Nested, NestedSequence, true)]
    [InlineData("doc//n", "<doc>" + Nested + "</doc>", NestedSequence, true)]
    [InlineData("doc/descendant::n", "<doc>" + Nested + "</doc>", NestedSequence, true)]
    [InlineData("//n", Nested, Nested + "<n>1</n><n>wrong</n>", false)]
    [InlineData("//n", Nested, Nested + "<n>1</n>", false)]
    [InlineData("//n", Nested, NestedSequence + "<n>extra</n>", false)]
    [InlineData("//n", "<n><n>wrong</n><n>2</n></n>", NestedSequence, false)]
    [InlineData("//n", "<doc>" + Nested + Nested + "</doc>", NestedSequence + NestedSequence, true)]
    [InlineData("//*", Nested, NestedSequence, true)]
    [InlineData("//element()", Nested, NestedSequence, true)]
    [InlineData("//n", "<n><!--ignored--><n>1</n><?p ignored?><n>2</n></n>", NestedSequence, true)]
    [InlineData(".", Nested, "<xsl:document>" + Nested + "</xsl:document>", true)]
    [InlineData("doc/row//n", "<doc><row>" + Nested + "</row><row>" + Nested + "</row></doc>", NestedSequence + NestedSequence, true)]
    [InlineData("//p:n", "<p:n xmlns:p='urn:test'><p:n>1</p:n></p:n>", "<q:n xmlns:q='urn:test'><q:n>1</q:n></q:n><q:n xmlns:q='urn:test'>1</q:n>", true)]
    public void NodesAreComparedBeforeThePopulationAdvances(string source, string xml, string reference, bool expected)
    {
        foreach (bool forward in new[] { false, true })
            foreach (bool reversed in new[] { false, true })
                Assert.Equal(expected ? "true" : "false", Run(reversed ? $"deep-equal($expected, {source})" : $"deep-equal({source}, $expected)", xml, reference, forward));
    }

    [Theory]
    [InlineData("deep-equal(doc/row, $expected, 'http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive')", "<doc><row a='ONE'>Text</row></doc>", "<row a='one'>text</row>", "true")]
    [InlineData("deep-equal(doc/row, $expected, 'http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive')", "<doc><row>one</row></doc>", "<ROW>ONE</ROW>", "false")]
    [InlineData("deep-equal(//n, $expected, 'http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive')", "<n><n>ONE</n></n>", "<n><n>one</n></n><n>One</n>", "true")]
    public void CollationsApplyToValuesAtEveryLevel(string select, string xml, string reference, string expected)
    {
        Assert.Equal(expected, Run(select, xml, reference, false));
        Assert.Equal(expected, Run(select, xml, reference, true));
    }

    [Theory]
    [InlineData("deep-equal(doc/row, $expected, 'urn:unknown-collation')", "FOCH0002")]
    [InlineData("deep-equal(doc/row, $expected, ())", "XPTY0004")]
    [InlineData("deep-equal(doc/row, $expected, ('one','two'))", "XPTY0004")]
    [InlineData("deep-equal((doc/row, error(QName('', 'TEST0001'))), $expected)", "TEST0001")]
    [InlineData("deep-equal($expected, (doc/row, error(QName('', 'TEST0001'))))", "TEST0001")]
    public void ArgumentErrorsKeepTheirCanonicalIdentity(string select, string code)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal(code, Assert.Throws<XdmException>(() => Run(select, "<doc><row>one</row></doc>", "<row>one</row>", forward)).Code);
    }

    [Theory]
    [InlineData("deep-equal((doc/row, true#0), ($expected, 1))")]
    [InlineData("deep-equal(($expected, 1), (doc/row, true#0))")]
    [InlineData("deep-equal(doc/row, ($expected, true#0))")]
    [InlineData("deep-equal((doc/row, true#0), ())")]
    [InlineData("deep-equal(true#0, 1)")]
    [InlineData("deep-equal((doc/row, map{'a':true#0}), ($expected, map{'a':true#0}))")]
    public void ComparingANonMapFunctionWithAnyItemRaisesTheCanonicalError(string select)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal("FOTY0015", Assert.Throws<XdmException>(() => Run(select, "<doc><row>one</row></doc>", "<row>one</row>", forward)).Code);
    }

    [Theory]
    [InlineData("deep-equal((doc/row, map{'a':true#0}), ($expected, 42))")]
    [InlineData("deep-equal((doc/row, 42), ($expected, map{'a':true#0}))")]
    [InlineData("deep-equal((doc/row, 42), ($expected, [true#0]))")]
    [InlineData("deep-equal((doc/row, map{'a':true#0}), ($expected, map{'b':true#0}))")]
    public void UncomparedMapAndArrayMembersDoNotRaiseFunctionErrors(string select)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal("false", Run(select, "<doc><row>one</row></doc>", "<row>one</row>", forward));
    }

    [Fact]
    public void RelativeCollationsUseTheExpressionStaticBaseUri()
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal("true", Run("deep-equal(doc/row, $expected, 'html-ascii-case-insensitive')", "<doc><row>ONE</row></doc>", "<row>one</row>", forward,
                staticBase: "http://www.w3.org/2005/xpath-functions/collation/"));
    }

    [Fact]
    public void ComparingAnAttributeDoesNotConsumeItsOwningElement()
    {
        const string select = "string-join(doc/row ! (if (deep-equal(@a, $expected/@a)) then string(.) else ''), '|')";
        foreach (bool forward in new[] { false, true })
            Assert.Equal("one|two", Run(select, "<doc><row a='yes'>one</row><row a='yes'>two</row></doc>", "<r a='yes'/>", forward));
    }

    [Theory]
    [InlineData("deep-equal(@a, n)")]
    [InlineData("deep-equal(n, @a)")]
    public void ALocalAttributeCanBeTheStableOperandOfAChildComparison(string comparison)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal("false false", Run("doc/row ! (" + comparison + ")", "<doc><row a='one'><n>one</n></row><row a='two'><n>two</n></row></doc>", "", forward));
    }

    [Fact]
    public void EachRecordIsComparedBeforeTheNextRecordIsRead()
    {
        string body = "<xsl:for-each select='doc/row'><xsl:message select='deep-equal(n, $expected)'/></xsl:for-each>";
        var transform = new XslCompiledTransform();
        using var stylesheet = XmlReader.Create(new StringReader(Sheet("", "<n><a>one</a><b>two</b></n>", body)));
        transform.Load(stylesheet, new() { StreamedExecution = true }, null);
        using var input = XmlReader.Create(new StringReader("<doc><row><n><a>one</a><b>two</b></n></row><row><n><a>one</a><b>two</b></n></row></doc>"));
        using var output = new StringWriter(); int calls = 0;
        var arguments = new XsltArgumentList();
        arguments.XsltMessageEncountered += (_, message) => {
            Assert.Equal("true", message.Message);
            if (calls++ == 0) { Assert.Equal("row", input.LocalName); Assert.Equal(XmlNodeType.EndElement, input.NodeType); }
        };
        transform.Transform(input, arguments, output);
        Assert.Equal(2, calls); Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void TheExecutableApiComparesTheStreamedDocument()
    {
        var executable = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(Sheet("deep-equal(/*, $expected)", Nested)));
        using var input = XmlReader.Create(new StringReader(Nested));
        Assert.Equal("true", string.Concat(executable.CreateTransformer().ApplyTemplates(input)));
    }

#if NET9_0_OR_GREATER
    [Fact]
    public void ComparisonSurvivesSavedCompilation() => Assert.Equal("true", Run("deep-equal(//n, $expected)", Nested, NestedSequence, true, saved: true));
#endif

    private static string Sheet(string select, string reference, string? body = null) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:p="urn:test">
        <xsl:mode streamable="yes"/><xsl:output method="text"/>
        <xsl:template match="/"><xsl:variable name="expected" as="item()*">{{reference}}</xsl:variable>{{body ?? $"<xsl:value-of select='{SecurityElement.Escape(select)}'/>"}}</xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string select, string xml, string reference, bool forward, bool saved = false, string? staticBase = null)
    {
        var transform = new XslCompiledTransform();
        using var stylesheet = XmlReader.Create(new StringReader(Sheet(select, reference)), new XmlReaderSettings(), staticBase ?? "");
        var settings = new XsltSettings { StreamedExecution = forward };
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(stylesheet, settings, null, "DeepEqual", "Saved.DeepEqual", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.DeepEqual", true)!);
#endif
        }
        else transform.Load(stylesheet, settings, null);
        using var input = XmlReader.Create(new StringReader(xml)); using var output = new StringWriter();
        try { transform.Transform(input, null, output); return output.ToString(); }
        finally { Assert.NotEqual(ReadState.Closed, input.ReadState); }
    }
}
