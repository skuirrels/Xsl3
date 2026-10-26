// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedOutermostTests
{
    private const string Input = "<doc><book id='a'><book id='nested'>1</book>2</book><group><book id='b'>3</book></group><after/></doc>";
    private static string Sheet(string select, string body) => "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>"
        + "<xsl:mode streamable='yes'/><xsl:output method='text'/><xsl:template match='/'>"
        + $"<xsl:for-each select='{select.Replace("'", "&apos;")}'>{body}</xsl:for-each></xsl:template></xsl:stylesheet>";

    [Theory]
    [InlineData("outermost(doc/*/descendant-or-self::book)", "123")]
    [InlineData("outermost(doc/*/descendant::book)", "13")]
    [InlineData("outermost(doc/*//book)", "13")]
    public void DisjointAnchorScansRetainPruningUnderBothOptimizerPolicies(string select, string expected)
    {
        string sheet = Sheet(select, "<xsl:value-of select='.'/>");
        var compiler = new XsltCompiler { Settings = new() { StreamedExecution = true } };
        var apply = compiler.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal(expected, string.Concat(apply.ApplyTemplates(reader)));
        var call = compiler.Compile(new StringReader(sheet.Replace("<xsl:template match='/'>", "<xsl:template name='t'>"))).CreateTransformer();
        call.GlobalContextItem = XdmNode.Load(new StringReader(Input));
        Assert.Equal(expected, string.Concat(call.CallTemplate(new QName("t"))));
    }

    [Theory]
    [InlineData("outermost(doc/book)", "12")]
    [InlineData("outermost(doc/book[@id])", "12")]
    [InlineData("outermost(doc/*:book)", "12")]
    [InlineData("outermost(/doc)", "123")]
    public void DisjointSelectionsStayIncremental(string select, string expected)
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }
            .Compile(new StringReader(Sheet(select, "<xsl:value-of select='.'/>"))).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal(expected, string.Concat(transform.ApplyTemplates(reader)));
    }

    [Theory]
    [InlineData("outermost(.//book)")]
    [InlineData("outermost(//book)")]
    [InlineData("outermost(doc//book)")]
    [InlineData("outermost(/doc/descendant::book)")]
    [InlineData("outermost(descendant::book)")]
    public void EachAcceptedSubtreeIsConsumedBeforeTheScanContinues(string select)
    {
        string sheet = Sheet(select, "<xsl:value-of select='.'/>");
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet)).CreateTransformer();
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal("123", string.Concat(transform.ApplyTemplates(input)));
            // The same forward-compiled iterator must also work over grounded XDM inputs.
            Assert.Equal("123", string.Concat(transform.ApplyTemplates(XdmNode.Load(new StringReader(Input)))));
        }
    }

    [Theory]
    [InlineData("outermost(doc/descendant-or-self::*)", "doc")]
    [InlineData("outermost(doc/book/descendant-or-self::book)", "book")]
    [InlineData("outermost(doc//book)", "bookbook")]
    public void PruningDoesNotRequireTheBodyToConsumeItsSelection(string select, string expected)
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }
            .Compile(new StringReader(Sheet(select, "<xsl:value-of select='name()'/>"))).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal(expected, string.Concat(transform.ApplyTemplates(reader)));
        Assert.Equal(expected, string.Concat(transform.ApplyTemplates(XdmNode.Load(new StringReader(Input)))));
    }

    [Theory]
    [InlineData("outermost(.//*:book)", "ab")]
    [InlineData("outermost(.//book[@id = 'nested'])", "nested")]
    [InlineData("outermost(.//Q{urn:p}*)", "p")]
    public void RejectedOuterCandidatesMustNotHideMatchingDescendants(string select, string expected)
    {
        string xml = Input.Replace("<after/>", "<other><p:item xmlns:p='urn:p' id='p'/></other>");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }
            .Compile(new StringReader(Sheet(select, "<xsl:value-of select='@id'/>"))).CreateTransformer();
        Assert.Equal(expected, string.Concat(transform.ApplyTemplates(XdmNode.Load(new StringReader(xml)))));
    }

    [Fact]
    public void CopiesRemainAvailableAfterTheSourceIsClosed()
    {
        string sheet = Sheet("outermost(.//book)", "<xsl:copy-of select='.'/>");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        XdmValue result;
        using (var reader = XmlReader.Create(new StringReader(Input))) result = transform.ApplyTemplates(reader);
        Assert.Equal(2, result.Count);
        var first = Assert.IsType<XdmNode>(result[0]);
        var second = Assert.IsType<XdmNode>(result[1]);
        Assert.Equal("a", first.Navigator.GetAttribute("id", ""));
        Assert.Equal("12", first.StringValue);
        Assert.Equal("1", first.Navigator.SelectSingleNode("book")!.Value);
        Assert.Equal("b", second.Navigator.GetAttribute("id", ""));
        Assert.Equal("3", second.StringValue);
    }

#if NET9_0_OR_GREATER
    [Fact]
    public void SavedAssemblyPreservesThePrunedScan()
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(Sheet("outermost(.//book)", "<xsl:value-of select='.'/>"))),
            settings, null, "Xsl3.Tests.Outermost", "Saved.Outermost", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Outermost", throwOnError: true)!, settings);
        using var reader = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(reader, null, output);
        Assert.Equal("123", output.ToString());
    }
#endif
}
