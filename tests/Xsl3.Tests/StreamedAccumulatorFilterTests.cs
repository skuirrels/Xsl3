// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xunit;

namespace Xsl3.Tests;

public class StreamedAccumulatorFilterTests
{
    private const string Input = "<doc xmlns:p='urn:p' xmlns:q='urn:q'><row active='yes'/><p:row/><q:row/><other/><row/></doc>";

    private sealed record Case(string Pattern, string Query, string DefaultNamespace = "", bool? Filter = false, string Namespace = "");

    private static readonly Case[] Cases = {
        new("row", "count(//row)", Filter: true),
        new("p:row", "count(//p:row)", Filter: true, Namespace: "urn:p"),
        new("Q{urn:p}row", "count(//p:row)", Filter: true, Namespace: "urn:p"),
        new("row", "count(//q:row)", DefaultNamespace: "urn:q", Filter: true, Namespace: "urn:q"),
        new("row[true()]", "count(//row)", Filter: null),
        new("row[@active]", "count(//row[@active])"),
        new("row | p:row", "count(//row | //p:row)"),
        new("*:row", "count(//*[local-name()='row'])"),
        new("p:*", "count(//p:*)"),
        new("*", "count(//*)"),
    };

    private static string DefaultNamespaceAttribute(Case item) => item.DefaultNamespace.Length == 0
        ? "" : "xpath-default-namespace=\"" + item.DefaultNamespace + "\"";

    private static string Sheet(Case item) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:p="urn:p" xmlns:q="urn:q"
            {{DefaultNamespaceAttribute(item)}}>
          <xsl:mode streamable="yes" use-accumulators="n"/><xsl:output method="text"/>
          <xsl:accumulator name="n" initial-value="0" streamable="yes">
            <xsl:accumulator-rule match="{{item.Pattern}}" phase="end" select="$value + 1"/>
          </xsl:accumulator>
          <xsl:template match="/"><xsl:value-of select="accumulator-after('n')"/></xsl:template>
        </xsl:stylesheet>
        """;

    private static void VerifyProof(XslCompiledTransform transform, Case item)
    {
        var command = (XmlILCommand)typeof(XslCompiledTransform).GetField("_command", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transform)!;
        var constants = command.StaticData.Constants!;
        var info = Enumerable.Range(0, constants.Count).Select(constants.Get).OfType<Dictionary<string, AccumulatorInfo>>().Single()["Q{}n"];
        AccumulatorNodeFilter? filter = info.Filters?.Single();
        if (item.Filter is bool required) Assert.Equal(required, filter != null);
        if (filter != null)
        {
            Assert.Equal("row", filter.LocalName);
            Assert.Equal(item.Namespace, filter.Namespace);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolvedNameProofsKeepActualPatternSemanticsAcrossDeliveryPaths(bool debug)
    {
        using var referenceInput = XmlReader.Create(new StringReader(Input));
        var reference = new XPathDocument(referenceInput).CreateNavigator();
        var namespaces = new XmlNamespaceManager(reference.NameTable);
        namespaces.AddNamespace("p", "urn:p");
        namespaces.AddNamespace("q", "urn:q");
        foreach (Case item in Cases)
        foreach (bool forward in new[] { false, true })
        for (int delivery = 0; delivery <
#if NET10_0_OR_GREATER
            3;
#else
            2;
#endif
            delivery++)
        {
            string expected = ((double)reference.Evaluate(item.Query, namespaces)).ToString(CultureInfo.InvariantCulture);
            var settings = new XsltSettings { StreamedExecution = forward, IncludeDebugInformation = debug };
            using var input = XmlReader.Create(new StringReader(Input));
            using var sink = new StringWriter(CultureInfo.InvariantCulture);
            if (delivery == 1)
            {
                var transformer = new XsltCompiler { Settings = settings }.Compile(new StringReader(Sheet(item))).CreateTransformer();
                transformer.ApplyTemplates(input, sink);
            }
            else
            {
                var transform = new XslCompiledTransform(debug);
                using var stylesheet = XmlReader.Create(new StringReader(Sheet(item)));
#if NET10_0_OR_GREATER
                if (delivery == 2)
                {
                    using var image = new MemoryStream();
                    transform.CompileToAssembly(stylesheet, settings, null, "AccumulatorFilter" + Guid.NewGuid().ToString("N"), "Saved.Filter", image);
                    transform.Load(Assembly.Load(image.ToArray()).GetType("Saved.Filter", true)!, settings);
                }
                else
#endif
                    transform.Load(stylesheet, settings, null);
                VerifyProof(transform, item);
                transform.Transform(input, null, sink);
            }
            Assert.Equal(expected, sink.ToString());
            Assert.NotEqual(ReadState.Closed, input.ReadState);
        }
    }
}
