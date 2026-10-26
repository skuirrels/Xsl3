// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedComplexContentTests
{
    private static string Sheet(string content) => "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:mode streamable='yes'/>"
        + "<xsl:output omit-xml-declaration='yes'/><xsl:template match='/'>" + content + "</xsl:template></xsl:stylesheet>";

    [Theory]
    [InlineData("<out><xsl:sequence select='1, [2, (), [3], 4], 5'/></out>", "<out>1 2 3 4 5</out>")]
    [InlineData("<out><xsl:sequence select='&quot;&quot;, 1, &quot;&quot;'/></out>", "<out> 1 </out>")]
    [InlineData("<xsl:variable name='empty' as='document-node()'><xsl:document/></xsl:variable><out><xsl:sequence select='1, $empty, 2'/></out>", "<out>12</out>")]
    [InlineData("<xsl:variable name='empty' as='text()'><xsl:value-of select='&quot;&quot;'/></xsl:variable><out><xsl:sequence select='1, $empty, 2'/></out>", "<out>12</out>")]
    [InlineData("<out><xsl:sequence select='[1, parse-xml(&quot;&lt;a>node&lt;/a>&quot;)/a, [2, &quot;&quot;]]'/></out>", "<out>1<a>node</a>2 </out>")]
    [InlineData("<out><xsl:sequence select='.'/></out>", "<out><doc><row id=\"a\">one</row><row id=\"b\">two</row></doc></out>")]
    [InlineData("<xsl:sequence select='data(doc/row/@id)'/>", "a b")]
    public void AtomicRunsArraysAndDocumentChildrenKeepComplexContentSemantics(string content, string expected)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal(expected, Run(Sheet(content), "<doc><row id='a'>one</row><row id='b'>two</row></doc>", forward));
    }

    [Theory]
    [InlineData("map{}")]
    [InlineData("[1, map{}]")]
    [InlineData("function(){1}")]
    public void FunctionsStillRaiseTheCanonicalComplexContentCode(string select)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Contains("XTDE0450", Assert.Throws<XsltDynamicException>(() => Run(Sheet("<out><xsl:sequence select='" + select + "'/></out>"), "<doc/>", forward)).Message);
    }

    [Fact]
    public void LateOrdinaryErrorsDoNotChooseOnEmptyAndTryStillRollsBack()
    {
        string sheet = Sheet("<xsl:try><out><xsl:sequence><xsl:sequence select='doc/row'/><xsl:sequence select='error()'/></xsl:sequence><xsl:on-empty><wrong/></xsl:on-empty></out>"
            + "<xsl:catch><caught/></xsl:catch></xsl:try>");
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<caught/>", Run(sheet, "<doc><row>one</row><row>two</row></doc>", forward));
    }

    [Theory]
    [InlineData("<value xsl:validation='preserve'><xsl:value-of select='.'/></value>")]
    [InlineData("<xsl:element name='value' validation='preserve'><xsl:value-of select='.'/></xsl:element>")]
    [InlineData("<xsl:copy copy-namespaces='no' validation='preserve'><xsl:value-of select='.'/></xsl:copy>")]
    public void PreserveScopesDoNotEscapeWhileNodesAreCopiedIntoTheOuterResult(string constructor)
    {
        string sheet = Sheet("<out><xsl:source-document streamable='yes' href='urn:rows'><xsl:for-each select='doc/row'>" + constructor
            + "</xsl:for-each></xsl:source-document></out>");
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward, EnableDocumentFunction = true, SchemaAware = true } }.Compile(new StringReader(sheet)).CreateTransformer();
            transform.DocumentResolver = new RowsResolver();
            using var input = XmlReader.Create(new StringReader("<launch/>"));
            var (writer, result) = Xsl3.Tree.CompactDocument.TemporaryTree(new NameTable(), "", fragment: false, noDocumentUri: true, compact: true);
            using (writer) transform.ApplyTemplates(input, writer);
            var child = result().SelectSingleNode("out/*"); Assert.NotNull(child);
            Assert.Null(Xsl3.Schema.SchemaTypes.TypeOf(child));
        }
    }

    [Fact]
    public void ContentContinuationsDoNotForceGlobalsBeforeTheirSourceIsOpened()
    {
        string sheet = Sheet("<out><xsl:source-document streamable='yes' href='urn:rows'><xsl:fork><xsl:for-each-group select='($extra, /doc/row)' group-by='&quot;g&quot;'>"
            + "<g><xsl:value-of select='current-group()/value' separator='|'/></g></xsl:for-each-group></xsl:fork></xsl:source-document></out>")
            .Replace("<xsl:template", "<xsl:variable name='extra' as='element(row)'><row><value>extra</value></row></xsl:variable><xsl:template");
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward, EnableDocumentFunction = true } }.Compile(new StringReader(sheet)).CreateTransformer();
            transform.DocumentResolver = new RowsResolver();
            using var input = XmlReader.Create(new StringReader("<launch/>")); using var output = new StringWriter();
            transform.ApplyTemplates(input, output);
            Assert.Equal("<out><g>source|extra</g></out>", output.ToString());
        }
    }

    private sealed class RowsResolver : XmlResolver
    {
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(System.Uri absoluteUri, string? role, System.Type? ofObjectToReturn) =>
            new MemoryStream(Encoding.UTF8.GetBytes("<doc><row><value>source</value></row></doc>"));
    }

    private static string Run(string sheet, string xml, bool forward)
    {
        var executable = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet));
        using var input = XmlReader.Create(new StringReader(xml)); using var output = new StringWriter();
        executable.CreateTransformer().ApplyTemplates(input, output);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }
}
