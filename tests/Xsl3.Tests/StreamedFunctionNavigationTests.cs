// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedFunctionNavigationTests
{
    private const string Input = "<doc><section><section><p>first</p><p>second</p><p>third</p></section><section><p>fourth</p><p>fifth</p></section></section><section><section><p>other</p><p>ignored</p></section></section></doc>";

    private static string Sheet(string parameter, bool empty = false) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:f="urn:test" exclude-result-prefixes="f">
          <xsl:mode streamable="yes"/><xsl:output omit-xml-declaration="yes"/>
          <xsl:function name="f:consume" streamability="absorbing">
            <xsl:param name="node" as="{{parameter}}"/>
            <xsl:copy-of select="$node/doc/section[1]/section/p[2]"/>
          </xsl:function>
          <xsl:template match="/"><out><xsl:copy-of select="f:consume({{(empty ? "()" : ".")}})"/></out></xsl:template>
        </xsl:stylesheet>
        """;

    [Theory]
    [InlineData(false, "node()", false)]
    [InlineData(false, "node()?", false)]
    [InlineData(false, "node()?", true)]
    [InlineData(true, "node()", false)]
    [InlineData(true, "node()?", false)]
    [InlineData(true, "node()?", true)]
    public void AbsorbingFunctionCopiesPositionallySelectedDescendants(bool forward, string parameter, bool empty)
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
            .Compile(new StringReader(Sheet(parameter, empty))).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.ApplyTemplates(reader, output);
        AssertOutput(output.ToString(), empty);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 16)]
    public void BothExpandedAndRetainedCallsConsumeThePathWhileCurrent(bool tracing, int depth)
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true, EnableTracing = tracing, MaxRecursionDepth = depth } }
            .Compile(new StringReader(Sheet("node()?"))).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.ApplyTemplates(reader, output);
        AssertOutput(output.ToString());
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData("node()", false)]
    [InlineData("node()?", false)]
    [InlineData("node()?", true)]
    public void SavedAssembliesPreserveSingletonChildPathOrder(string parameter, bool empty)
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        using var stylesheet = XmlReader.Create(new StringReader(Sheet(parameter, empty)));
        new XslCompiledTransform().CompileToAssembly(stylesheet, settings, null,
            "Xsl3.Tests.StreamedFunctionNavigation", "Saved.Navigation", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Navigation", true)!, settings);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        AssertOutput(output.ToString(), empty);
    }
#endif

    private static void AssertOutput(string output, bool empty = false)
    {
        var document = new XmlDocument();
        document.LoadXml(output);
        Assert.Equal(empty ? "<out />" : "<out><p>second</p><p>fifth</p></out>", document.DocumentElement!.OuterXml);
    }
}
