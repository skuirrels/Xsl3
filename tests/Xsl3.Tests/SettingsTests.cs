// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

/// <summary>The run-time settings of SPEC §21.1–2: secure by default, each one opt-in (docs/plan/remaining-scope.md 4.4).</summary>
public class SettingsTests
{
    private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

    private static XslCompiledTransform Load(string body, XsltSettings? settings = null)
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'><xsl:output omit-xml-declaration='yes'/>{body}</xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)), settings, null);
        return transform;
    }

    private static string Run(XslCompiledTransform transform, XsltArgumentList? arguments = null)
    {
        var sw = new StringWriter();
        transform.Transform(XmlReader.Create(new StringReader("<doc/>")), arguments, sw);
        return sw.ToString();
    }

    private const string WritesSecondary = """
        <xsl:template match="/"><xsl:result-document href="sub/second.xml"><second/></xsl:result-document><main/></xsl:template>
        """;

    [Fact]
    public void ResultDocumentsOffByDefault()
    {
        Exception e = Assert.ThrowsAny<Exception>(() => Run(Load(WritesSecondary)));
        Assert.Contains("EnableResultDocument", e.ToString());
    }

    [Fact]
    public void ResultDocumentsUnderTheBaseOutputUri()
    {
        string directory = Directory.CreateTempSubdirectory("xsl3-").FullName;
        XslCompiledTransform transform = Load(WritesSecondary, new XsltSettings { EnableResultDocument = true });
        transform.BaseOutputUri = new Uri(Path.Combine(directory, "main.xml"));
        Assert.Equal("<main />", Run(transform));
        Assert.Equal("<second />", File.ReadAllText(Path.Combine(directory, "sub", "second.xml")).Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", ""));
    }

    [Fact]
    public void ResultDocumentsNextToTheResultsFile()
    {
        string directory = Directory.CreateTempSubdirectory("xsl3-").FullName;
        string input = Path.Combine(directory, "in.xml");
        File.WriteAllText(input, "<doc/>");
        XslCompiledTransform transform = Load(WritesSecondary, new XsltSettings { EnableResultDocument = true });
        transform.Transform(input, Path.Combine(directory, "out.xml"));
        Assert.True(File.Exists(Path.Combine(directory, "sub", "second.xml")));
    }

    [Fact]
    public void ResultDocumentsNotOutsideTheBaseOutputUri()
    {
        string directory = Directory.CreateTempSubdirectory("xsl3-").FullName;
        XslCompiledTransform transform = Load("""<xsl:template match="/"><xsl:result-document href="../escaped.xml"><x/></xsl:result-document></xsl:template>""",
            new XsltSettings { EnableResultDocument = true });
        transform.BaseOutputUri = new Uri(Path.Combine(directory, "main.xml"));
        Exception e = Assert.ThrowsAny<Exception>(() => Run(transform));
        Assert.Contains("only under", e.ToString());
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(directory)!, "escaped.xml")));
    }

    [Fact]
    public void EnvironmentVariablesOnlyWhenEnabled()
    {
        Environment.SetEnvironmentVariable("XSL3_SETTINGS_TEST", "visible");
        const string body = """<xsl:template match="/"><out><xsl:value-of select="environment-variable('XSL3_SETTINGS_TEST')"/></out></xsl:template>""";
        Assert.Equal("<out></out>", Run(Load(body)));
        Assert.Equal("<out>visible</out>", Run(Load(body, new XsltSettings { EnableEnvironmentVariables = true })));
    }

    private const string Endless = """
        <xsl:template match="/"><xsl:call-template name="loop"><xsl:with-param name="n" select="0"/></xsl:call-template></xsl:template>
        <xsl:template name="loop"><xsl:param name="n"/><xsl:if test="$n ge 0"><xsl:call-template name="loop"><xsl:with-param name="n" select="($n + 1) mod 1000"/></xsl:call-template></xsl:if></xsl:template>
        """;

    [Fact]
    public void Timeout()
    {
        XslCompiledTransform transform = Load(Endless, new XsltSettings { Timeout = TimeSpan.FromMilliseconds(200) });
        Exception e = Assert.ThrowsAny<Exception>(() => Run(transform));
        Assert.Contains("cancelled", e.ToString());
    }

    [Fact]
    public void CancellationToken()
    {
        using var cancel = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        Exception e = Assert.ThrowsAny<Exception>(() => Run(Load(Endless), new XsltArgumentList { CancellationToken = cancel.Token }));
        Assert.Contains("cancelled", e.ToString());
    }

    /// <summary>Deep recursion that isn't a tail call runs on a dedicated thread with a large stack.</summary>
    [Fact]
    public void DedicatedThread()
    {
        const string body = """
            <xsl:template match="/"><out><xsl:value-of select="f:depth(200000)" xmlns:f="urn:f"/></out></xsl:template>
            <xsl:function name="f:depth" xmlns:f="urn:f"><xsl:param name="n"/><xsl:sequence select="if ($n eq 0) then 0 else 1 + f:depth($n - 1)"/></xsl:function>
            """;
        Assert.Equal("<out>200000</out>", Run(Load(body, new XsltSettings { UseDedicatedThread = true, DedicatedThreadStackSize = 512 * 1024 * 1024 })));
    }

    private const string Asserts = """
        <xsl:param name="p" select="2"/>
        <xsl:template match="/"><out><xsl:assert test="$p eq 3">p is <xsl:value-of select="$p"/></xsl:assert></out></xsl:template>
        """;

    /// <summary>xsl:assert is ignored unless assertions are enabled (XSLT 3.0 §23.2).</summary>
    [Fact]
    public void AssertionsOffByDefault() => Assert.Equal("<out />", Run(Load(Asserts)));

    [Fact]
    public void FailedAssertionRaisesXTMM9001()
    {
        Exception e = Assert.ThrowsAny<Exception>(() => Run(Load(Asserts, new XsltSettings { EnableAssertions = true })));
        Assert.Contains("XTMM9001", e.ToString());
        Assert.Contains("p is 2", e.ToString());
    }

    [Fact]
    public void FailedAssertionCanBeCaught() =>
        Assert.Equal("<out>ABCD9999</out>", Run(Load("""
            <xsl:template match="/"><out><xsl:try><xsl:assert test="false()" error-code="my:ABCD9999" xmlns:my="urn:my" select="'no'"/>
              <xsl:catch errors="*" select="local-name-from-QName($Q{http://www.w3.org/2005/xqt-errors}code)"/></xsl:try></out></xsl:template>
            """, new XsltSettings { EnableAssertions = true })));

    /// <summary>Xsl3 isn't schema-aware yet: xsl:import-schema is XTSE1650, the error a basic processor raises.</summary>
    [Fact]
    public void ImportSchemaIsXTSE1650()
    {
        Exception e = Assert.ThrowsAny<Exception>(() => Load("""<xsl:import-schema namespace="urn:x"/><xsl:template match="/"/>"""));
        Assert.Contains("XTSE1650", e.ToString());
    }
}
