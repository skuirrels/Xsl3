// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace Xsl3.Tests;

public sealed class SnapshotTraversalTests
{
    public static IEnumerable<object[]> Paths()
    {
        foreach (bool forward in new[] { false, true })
        foreach (bool debug in new[] { false, true })
        foreach (string api in new[] { "modern", "legacy", "saved" })
        {
#if !NET10_0_OR_GREATER
            if (api == "saved") continue;
#endif
            yield return new object[] { forward, debug, api };
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void MixedSubtreesKeepTheirAncestorsAndStopAtEachRecord(bool forward, bool debug, string api)
    {
        const string xml = """
            <doc xmlns:p="urn:snapshot-traversal" xml:base="https://example.test/root/" xml:lang="en"><record n="one">alpha<!--marker--><?probe value?><p:nested p:a="one &amp; two">left<empty/>right</p:nested>omega</record><record n="two"><inner xmlns="urn:snapshot-inner"><child>value</child><empty/></inner><tail/></record><record n="three" xml:space="preserve"> before <empty/> after </record></doc>
            """;
        Check(xml, document: false, forward, debug, api);
        Check(xml, document: true, forward, debug, api);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void DeepAndWideChildrenReturnToTheCorrectAncestor(bool forward, bool debug, string api)
    {
        foreach (int depth in new[] { 16, 256 })
        {
            var xml = new StringBuilder("<doc><record n='deep'>");
            for (int i = 0; i < depth; i++) xml.Append("<branch><left/>before");
            xml.Append("<!--bottom--><?probe bottom?><leaf/>");
            for (int i = 0; i < depth; i++) xml.Append("after<right/></branch>");
            xml.Append("<last/></record><record n='next'><sibling/></record></doc>");
            Check(xml.ToString(), document: false, forward, debug, api);
        }
    }

    private static void Check(string xml, bool document, bool forward, bool debug, string api)
    {
        string sheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode streamable="yes"/><xsl:output omit-xml-declaration="yes"/>
              <xsl:template match="/"><out><xsl:for-each select="{{(document ? "." : "doc/record")}}">
                <xsl:variable name="copy" select="snapshot(.)"/>
                <xsl:message select="count($copy/descendant-or-self::node())"/>
                <snap><xsl:copy-of select="root($copy)"/></snap>
              </xsl:for-each></out></xsl:template>
            </xsl:stylesheet>
            """;
        using var input = new TrackingReader(XmlReader.Create(new StringReader(xml), new XmlReaderSettings(), "https://example.test/source.xml"));
        using var output = new StringWriter();
        var observations = new List<(XmlNodeType Type, string Name, int Depth, int Records, bool Eof)>();
        void Observe() => observations.Add((input.NodeType, input.LocalName, input.Depth, input.Records, input.EOF));
        var settings = new XsltSettings { StreamedExecution = forward, IncludeDebugInformation = debug };
        if (api == "modern")
        {
            var transform = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
            transform.MessageHandler = _ => Observe();
            transform.ApplyTemplates(input, output);
        }
        else
        {
            var transform = new XslCompiledTransform(debug);
            using var source = XmlReader.Create(new StringReader(sheet));
#if NET10_0_OR_GREATER
            if (api == "saved")
            {
                using var image = new MemoryStream();
                transform.CompileToAssembly(source, settings, null, "SnapshotTraversal" + Guid.NewGuid().ToString("N"), "Saved.SnapshotTraversal", image);
                transform.Load(Assembly.Load(image.ToArray()).GetType("Saved.SnapshotTraversal", true)!, settings);
            }
            else
#endif
                transform.Load(source, settings, null);
            var arguments = new XsltArgumentList();
            arguments.XsltMessageEncountered += (_, _) => Observe();
            transform.Transform(input, arguments, output);
        }
        // Build the expected shape independently from LINQ to XML's parsed tree:
        // each selected record keeps only its own subtree and a shallow document element.
        XElement original = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        var expected = new XElement("out", document
            ? new[] { new XElement("snap", new XElement(original)) }
            : original.Elements("record").Select(record => new XElement("snap",
                new XElement(original.Name, original.Attributes(), new XElement(record)))));
        XElement actual = XElement.Parse(output.ToString(), LoadOptions.PreserveWhitespace);
        Assert.True(XNode.DeepEquals(expected, actual), output.ToString());
        Assert.Equal(document ? 1 : original.Elements("record").Count(), observations.Count);
        for (int i = 0; i < observations.Count; i++)
        {
            if (forward && !document)
                Assert.Equal((XmlNodeType.EndElement, "record", 1, i + 1, false), observations[i]);
            else
                Assert.True(observations[i].Eof);
        }
        Assert.True(input.EOF);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    private sealed class TrackingReader(XmlReader reader) : XmlWrappingReader(reader)
    {
        internal int Records;
        public override bool Read()
        {
            bool read = base.Read();
            if (read && NodeType == XmlNodeType.Element && Depth == 1 && LocalName == "record") Records++;
            return read;
        }
    }
}
