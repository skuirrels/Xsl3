// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedCurrentModeLoweringTests
{
    private const string Input = "<doc><row n='1'/><row n='2'/></doc>";

    private static string Sheet(string relay, bool multiple)
    {
        string children = "<xsl:apply-templates select='row' mode='#current'/>";
        string body = relay switch {
            // Named templates receive a grounded context; the caller's current mode
            // must still survive the focus change and the named-template call.
            "named" => "<xsl:for-each select='copy-of(.)'><xsl:call-template name='relay'/></xsl:for-each>",
            "next" => "<xsl:next-match/>",
            _ => children,
        };
        return "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>"
            + "<xsl:mode streamable='yes'/><xsl:output method='text'/>"
            + (multiple ? "<xsl:mode name='m' streamable='yes'/><xsl:mode name='n' streamable='yes'/>" : "")
            + "<xsl:template match='/' mode='#all'><xsl:apply-templates select='doc' mode='#current'/></xsl:template>"
            + "<xsl:template match='doc' mode='#all' priority='2'>" + body + "</xsl:template>"
            + (relay == "named" ? "<xsl:template name='relay'>" + children + "</xsl:template>" : "")
            + (relay == "next" ? "<xsl:template match='doc' mode='#all' priority='1'>" + children + "</xsl:template>" : "")
            + "<xsl:template match='row'><xsl:text>U</xsl:text><xsl:value-of select='@n'/></xsl:template>"
            + (multiple ? "<xsl:template match='row' mode='m'><xsl:text>M</xsl:text><xsl:value-of select='@n'/></xsl:template>"
                + "<xsl:template match='row' mode='n'><xsl:text>N</xsl:text><xsl:value-of select='@n'/></xsl:template>" : "")
            + "</xsl:stylesheet>";
    }

    public static IEnumerable<object[]> DispatchCases()
    {
        foreach (string relay in new[] { "direct", "named", "next" })
        foreach (bool multiple in new[] { false, true })
        foreach (bool forward in new[] { false, true })
        foreach (bool debug in new[] { false, true })
        foreach (bool legacy in new[] { false, true })
            yield return new object[] { relay, multiple, forward, debug, legacy };
    }

    [Theory]
    [MemberData(nameof(DispatchCases))]
    public void CurrentModeSurvivesDirectNamedAndNextMatchCalls(string relay, bool multiple, bool forward, bool debug, bool legacy)
    {
        string mode = multiple ? relay == "named" ? "n" : "m" : "";
        var settings = new XsltSettings { StreamedExecution = forward, IncludeDebugInformation = debug };
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        if (legacy)
        {
            var transform = new XslCompiledTransform(debug) { InitialMode = new XmlQualifiedName(mode) };
            using var stylesheet = XmlReader.Create(new StringReader(Sheet(relay, multiple)));
            transform.Load(stylesheet, settings, null);
            transform.Transform(input, null, output);
        }
        else
        {
            var executable = new XsltCompiler { Settings = settings }.Compile(new StringReader(Sheet(relay, multiple)));
            executable.CreateTransformer().ApplyTemplates(input, output, mode: mode.Length == 0 ? null : new QName("", mode));
        }
        string label = mode.Length == 0 ? "U" : mode.ToUpperInvariant();
        Assert.Equal(label + "1" + label + "2", output.ToString());
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACurrentModeLookupIsGeneratedOnlyWhenDispatchHasAlternatives(bool optimized)
    {
        foreach (bool multiple in new[] { false, true })
        {
            var executable = new XsltCompiler { Settings = new() { StreamedExecution = true } }
                .Compile(new StringReader(Sheet("direct", multiple)));
            using var plan = new StringWriter();
            executable.Explain(plan, optimized);
            bool lookup = XDocument.Parse(plan.ToString()).Descendants("LiteralQName")
                .Any(n => (string?)n.Attribute("name") == "X31TunnelMode");
            Assert.Equal(multiple, lookup);
        }
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData("direct", false)]
    [InlineData("named", false)]
    [InlineData("next", false)]
    [InlineData("direct", true)]
    [InlineData("named", true)]
    [InlineData("next", true)]
    public void SavedDebugAndReleaseDispatchPreserveTheSelectedMode(string relay, bool multiple)
    {
        foreach (bool debug in new[] { false, true })
        {
            string mode = multiple ? "m" : "";
            var transform = new XslCompiledTransform(debug) { InitialMode = new XmlQualifiedName(mode) };
            var settings = new XsltSettings { StreamedExecution = true };
            using var stylesheet = XmlReader.Create(new StringReader(Sheet(relay, multiple)));
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(stylesheet, settings, null, "CurrentMode", "Saved.CurrentMode", assembly);
            var saved = new XslCompiledTransform();
            saved.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.CurrentMode", throwOnError: true)!);
            using var input = XmlReader.Create(new StringReader(Input));
            using var output = new StringWriter();
            saved.Transform(input, null, output);
            string label = multiple ? "M" : "U";
            Assert.Equal(label + "1" + label + "2", output.ToString());
            Assert.NotEqual(ReadState.Closed, input.ReadState);
        }
    }
#endif
}
