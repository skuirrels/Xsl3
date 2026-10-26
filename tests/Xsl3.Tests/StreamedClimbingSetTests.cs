// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using Xsl3.Xdm;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedClimbingSetTests
{
    private const string Input = "<doc id='R'><rows id='G'><item id='a'><x/></item><item id='b'><y/></item><item id='c'><x/><y/></item></rows></doc>";
    private static string Sheet(string expression) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/"><xsl:value-of select="{{expression}}" separator="|"/></xsl:template>
        </xsl:stylesheet>
        """;

    [Theory]
    [InlineData("(doc/rows/item/x/ancestor::* union doc/rows/item/y/ancestor::*) ! name()", "doc|rows|item|item|item")]
    [InlineData("(doc/rows/item/x/ancestor::* intersect doc/rows/item/y/ancestor::*) ! name()", "doc|rows|item")]
    [InlineData("doc/rows/item/x/ancestor::*/@id union doc/rows/item/y/ancestor::*/@id", "R|G|a|b|c")]
    [InlineData("doc/rows/item/x/ancestor::*/@id intersect doc/rows/item/y/ancestor::*/@id", "R|G|c")]
    [InlineData("doc/rows/item/x/ancestor::*/@id union doc/rows/item/x/ancestor::*/@id", "R|G|a|c")]
    [InlineData("(doc/rows/item/x/ancestor::* union doc/rows/item/y/ancestor::*)/@id", "R|G|a|b|c")]
    [InlineData("(doc/rows/item/x/ancestor::* intersect doc/rows/item/y/ancestor::*)/@id", "R|G|c")]
    [InlineData("(doc/rows/item/x/ancestor::*/@id union doc/rows/item/y/ancestor::*/@id) intersect doc/rows/item/*/ancestor::*/@id", "R|G|a|b|c")]
    [InlineData("doc/rows/item/missing/ancestor::*/@id intersect doc/rows/item/x/ancestor::*/@id", "")]
    [InlineData("doc/rows/item/x/ancestor-or-self::*/@id union doc/rows/item/y/ancestor-or-self::*/@id", "R|G|a|b|c")]
    [InlineData("doc/rows/item/x/ancestor::*[@id != 'G']/@id intersect doc/rows/item/y/ancestor::*[@id != 'G']/@id", "R|c")]
    [InlineData("doc/rows/item/x/ancestor::*/@id except doc/rows/item/y/ancestor::*/@id", "a")]
    [InlineData("doc/rows/item/y/ancestor::*/@id except doc/rows/item/x/ancestor::*/@id", "b")]
    [InlineData("(doc/rows/item/x/ancestor::* except doc/rows/item/y/ancestor::*) ! name()", "item")]
    [InlineData("doc/rows/item/x/ancestor::*/@id except doc/rows/item/missing/ancestor::*/@id", "R|G|a|c")]
    [InlineData("doc/rows/item/missing/ancestor::*/@id except doc/rows/item/x/ancestor::*/@id", "")]
    [InlineData("(doc/rows/item/x/ancestor::*/@id union doc/rows/item/y/ancestor::*/@id) except doc/rows/item[@id='c']/ancestor-or-self::*/@id", "a|b")]
    [InlineData("doc/rows/item/x/ancestor::*/@id except (doc/rows/item/y/ancestor::*/@id except doc/rows/item[@id='c']/ancestor-or-self::*/@id)", "R|G|a|c")]
    [InlineData("(doc/rows/item/x/ancestor::item union doc/rows/item/y/ancestor::doc)/@id", "R|a|c")]
    [InlineData("doc/rows/item/x/ancestor::*[1]/@id except doc/rows/item/y/ancestor::*[1]/@id", "a")]
    public void ClimbingOperandsPreserveCanonicalHeaderSets(string expression, string expected)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression))).CreateTransformer();
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(input)));
        }
    }
    [Theory]
    [InlineData("(doc/rows/item/x/ancestor::item union doc/rows/item/y/ancestor::doc)/@id", "R|a|c")]
    [InlineData("doc/rows/item/x/ancestor::*[@id != 'R']/@id union doc/rows/item/y/ancestor::*[@id = 'R']/@id", "R|G|a|c")]
    public void DifferentProjectionsDoNotAssumeIncrementalDocumentOrder(string expression, string expected)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression))).CreateTransformer();
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(XdmNode.Load(new StringReader(Input)))));
        }
    }

    [Theory]
    [InlineData("let $tree := copy-of(doc) return let $roots := reverse($tree/rows/item) return ($roots/x/ancestor::*/@id union $roots/y/ancestor::*/@id)", "R|G|a|b|c")]
    [InlineData("let $tree := copy-of(doc) return let $roots := ($tree/rows/item[2], $tree/rows, $tree/rows/item[1], $tree/rows/item[3]) return ($roots/x/ancestor::*/@id intersect $roots/y/ancestor::*/@id)", "R|G|c")]
    [InlineData("doc/rows/item/x/ancestor::*[1]/@id union doc/rows/item/y/ancestor::*[1]/@id", "a|b|c")]
    public void UnprovenRootsAndPositionalAncestorsRetainGroundedSemantics(string expression, string expected)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression))).CreateTransformer();
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(XdmNode.Load(new StringReader(Input)))));
        }
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData("doc/rows/item/x/ancestor::*/@id union doc/rows/item/y/ancestor::*/@id", "R|G|a|b|c")]
    [InlineData("doc/rows/item/x/ancestor::*/@id intersect doc/rows/item/y/ancestor::*/@id", "R|G|c")]
    public void SavedAssembliesRetainClimbingMembership(string expression, string expected)
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        using var stylesheet = XmlReader.Create(new StringReader(Sheet(expression)));
        new XslCompiledTransform().CompileToAssembly(stylesheet, settings, null, "Xsl3.Tests.Climbing", "Saved.Climbing", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Climbing", true)!, settings);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal(expected, output.ToString());
    }
#endif

}
