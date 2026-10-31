// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xunit;

namespace Xsl3.Tests;

public class WriterReplacementTests
{
    private static XmlQueryRuntime Runtime()
    {
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager())
            { Xslt2 = true, Constants = new ConstantTable() };
        return new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null,
            new XmlCachedSequenceWriter(compact: true));
    }

    private static void StartRow(XmlQueryOutput output, string value)
    {
        output.WriteStartElement("", "row", "");
        output.WriteAttributeString("", "n", "", "replaced");
        output.WriteAttributeString("", "n", "", value);
    }

    private static void Row(XmlQueryOutput output, string value)
    {
        StartRow(output, value);
        output.WriteString(value);
        output.WriteEndElement();
    }

    private static void AssertRow(XPathNavigator node, string value)
    {
        Assert.Equal("row", node.LocalName);
        Assert.Equal(value, node.GetAttribute("n", ""));
        Assert.Equal(value, node.Value);
    }

    [Fact]
    public void NestedAttributeCachesRemainBoundToTheirOwnOutput()
    {
        XmlQueryRuntime runtime = Runtime();
        foreach (string value in new[] { "first", "second", "third" })
        {
            runtime.StartSequenceConstruction(out XmlQueryOutput outer);
            StartRow(outer, value);
            runtime.StartSequenceConstruction(out XmlQueryOutput inner);
            Row(inner, "nested");
            var nested = runtime.EndSequenceConstruction(out outer);
            outer.WriteString(value);
            outer.WriteEndElement();
            var result = runtime.EndSequenceConstruction(out _);
            AssertRow(Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(nested)), "nested");
            AssertRow(Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(result)), value);
        }
    }

    [Fact]
    public void PooledOutputsCanAlternateSequenceAndEventConstruction()
    {
        XmlQueryRuntime runtime = Runtime();
        foreach (string value in new[] { "first", "second", "third" })
        {
            runtime.StartSequenceConstruction(out XmlQueryOutput output);
            Row(output, value);
            var sequence = runtime.EndSequenceConstruction(out _);
            runtime.StartRtfConstruction("", out output);
            Row(output, "events");
            XPathNavigator events = runtime.EndRtfConstruction(out _);
            AssertRow(Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(sequence)), value);
            AssertRow(Assert.IsAssignableFrom<RtfNavigator>(events).ToNavigator().SelectSingleNode("row")!, "events");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AutomaticOutputSelectionSurvivesRepeatedCacheReplacement(bool debug, bool forward)
    {
        foreach (string root in new[] { "html", "doc" })
        {
            string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>"
                + "<xsl:mode streamable='yes'/><xsl:output omit-xml-declaration='yes' indent='no'/>"
                + "<xsl:template match='/'><xsl:element name='" + root + "'><xsl:for-each select='doc/row'>"
                + "<xsl:element name='row'><xsl:attribute name='n'>replaced</xsl:attribute>"
                + "<xsl:attribute name='n' select='@n'/><xsl:value-of select='.'/></xsl:element>"
                + "</xsl:for-each><br/></xsl:element></xsl:template></xsl:stylesheet>";
            int deliveries = 2;
#if NET10_0_OR_GREATER
            deliveries = 3;
#endif
            for (int delivery = 0; delivery < deliveries; delivery++)
            {
                var settings = new XsltSettings { StreamedExecution = forward, IncludeDebugInformation = debug };
                using var input = XmlReader.Create(new StringReader("<doc><row n='one'>first</row><row n='two'>second</row></doc>"));
                using var output = new StringWriter();
                if (delivery == 0)
                    new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(input, output);
                else
                {
                    var transform = new XslCompiledTransform(debug);
#if NET10_0_OR_GREATER
                    if (delivery == 2)
                    {
                        using var image = new MemoryStream();
                        transform.CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null,
                            "WriterReplacement" + Guid.NewGuid().ToString("N"), "Saved.WriterReplacement", image);
                        transform.Load(System.Reflection.Assembly.Load(image.ToArray()).GetType("Saved.WriterReplacement", true)!, settings);
                    }
                    else
#endif
                        transform.Load(XmlReader.Create(new StringReader(sheet)), settings, null);
                    transform.Transform(input, null, output);
                }
                string actual = output.ToString();
                if (root == "html")
                    Assert.Equal("<html><row n=\"one\">first</row><row n=\"two\">second</row><br></html>", actual);
                else
                {
                    XPathNavigator tree = new XPathDocument(new StringReader(actual)).CreateNavigator();
                    Assert.Equal("one", tree.SelectSingleNode("/doc/row[1]/@n")!.Value);
                    Assert.Equal("first", tree.SelectSingleNode("/doc/row[1]")!.Value);
                    Assert.Equal("two", tree.SelectSingleNode("/doc/row[2]/@n")!.Value);
                    Assert.Equal("second", tree.SelectSingleNode("/doc/row[2]")!.Value);
                    Assert.Equal(3, tree.Select("/doc/*").Count);
                    Assert.NotNull(tree.SelectSingleNode("/doc/br"));
                }
                Assert.NotEqual(ReadState.Closed, input.ReadState);
            }
        }
    }
}
