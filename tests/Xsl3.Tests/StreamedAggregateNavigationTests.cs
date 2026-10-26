// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System.IO;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedAggregateNavigationTests
{
    private static string Sheet(string select) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/"><xsl:value-of select="{{select}}"/></xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string xml, string select, bool forward, bool grounded = false)
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
            .Compile(new StringReader(Sheet(select))).CreateTransformer();
        if (grounded) return string.Concat(transform.ApplyTemplates(XdmNode.Load(new StringReader(xml))));
        using var reader = XmlReader.Create(new StringReader(xml));
        return string.Concat(transform.ApplyTemplates(reader));
    }

    [Theory]
    [InlineData("min", "0.8")]
    [InlineData("max", "9.5")]
    public void CrawlingChildPathsArePrunedBeforeAtomization(string function, string expected)
    {
        const string xml = "<doc><ITEM><DIMENSIONS>2 0.8</DIMENSIONS></ITEM><ITEM><DIMENSIONS>9.5 3</DIMENSIONS></ITEM></doc>";
        string select = $"{function}(outermost(//ITEM/DIMENSIONS)!xs:NMTOKENS(.)!xs:decimal(.))";
        foreach (bool forward in new[] { false, true }) Assert.Equal(expected, Run(xml, select, forward));
    }

    [Theory]
    [InlineData("min", "P2D")]
    [InlineData("max", "P5D")]
    public void CastsAtomizeWhileChildNodesAreAvailable(string function, string expected)
    {
        const string xml = "<doc><ITEM><PUB-DATE>1970-01-03</PUB-DATE><other/></ITEM><ITEM><PUB-DATE>1970-01-06</PUB-DATE><other/></ITEM></doc>";
        string select = $"{function}(doc/ITEM ! (xs:date(PUB-DATE) - xs:date('1970-01-01')))";
        foreach (bool forward in new[] { false, true }) Assert.Equal(expected, Run(xml, select, forward));
    }

    [Theory]
    [InlineData("outermost(//P/C)")]
    [InlineData("outermost(.//P/C)")]
    [InlineData("outermost(doc//P/C)")]
    [InlineData("outermost(doc/group//P/C)")]
    public void OnlyAcceptedChildPathsPruneTheirDescendants(string path)
    {
        const string xml = "<doc><group><C><P><C>inner</C></P></C><P><C><P><C>nested</C></P>outer</C></P></group><group><P><C>last</C></P></group></doc>";
        foreach (bool forward in new[] { false, true })
        foreach (bool grounded in new[] { false, true })
        {
            Assert.Equal("inner|nestedouter|last", Run(xml, $"string-join({path}!string(.), '|')", forward, grounded));
            Assert.Equal("C|C|C", Run(xml, $"string-join({path}!name(.), '|')", forward, grounded));
        }
    }

    [Fact]
    public void ChildPathsUseExpandedNames()
    {
        const string xml = "<doc xmlns:p='urn:parent' xmlns:c='urn:child'><P><c:C>wrong-parent</c:C></P><p:P><C>wrong-child</C><c:C>right</c:C></p:P></doc>";
        const string select = "outermost(//Q{urn:parent}P/Q{urn:child}C)!string(.)";
        foreach (bool forward in new[] { false, true })
        foreach (bool grounded in new[] { false, true }) Assert.Equal("right", Run(xml, select, forward, grounded));
    }

    [Theory]
    [InlineData("doc/ITEM/PUB-DATE cast as xs:date", "1970-01-03")]
    [InlineData("doc/ITEM/PUB-DATE castable as xs:date", "true")]
    [InlineData("xs:NMTOKENS(doc/ITEM/CODES)", "a b")]
    public void CastSyntaxAndListConstructorsPreserveTheirContracts(string select, string expected)
    {
        const string xml = "<doc><ITEM><PUB-DATE>1970-01-03</PUB-DATE><CODES>a b</CODES><other/></ITEM></doc>";
        foreach (bool forward in new[] { false, true }) Assert.Equal(expected, Run(xml, select, forward));
    }

#if NET9_0_OR_GREATER
    [Fact]
    public void SavedAssemblyPreservesTheChildPathCursorAndCasts()
    {
        const string xml = "<doc><ITEM><DIMENSIONS>2 0.8</DIMENSIONS></ITEM><ITEM><DIMENSIONS>9.5 3</DIMENSIONS></ITEM></doc>";
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(Sheet("min(outermost(//ITEM/DIMENSIONS)!xs:NMTOKENS(.)!xs:decimal(.))"))),
            settings, null, "AggregateNavigation", "Saved.AggregateNavigation", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.AggregateNavigation", true)!, settings);
        using var input = XmlReader.Create(new StringReader(xml));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("0.8", output.ToString());
    }
#endif
}
