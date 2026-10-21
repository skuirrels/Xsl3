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

    /// <summary>Hrefs that would leave the base output directory: absolute, a sibling with the same prefix, a symbolic link.</summary>
    [Theory]
    [InlineData("abs")]
    [InlineData("sibling")]
    [InlineData("link")]
    public void ResultDocumentsCantEscape(string kind)
    {
        string root = Directory.CreateTempSubdirectory("xsl3-").FullName;
        string directory = Path.Combine(root, "out"), outside = Path.Combine(root, "out-evil");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(outside);
        string href = kind switch
        {
            "abs" => new Uri(Path.Combine(outside, "a.xml")).AbsoluteUri,
            "sibling" => "../out-evil/a.xml",
            _ => "link/a.xml",
        };
        if (kind == "link")
        {
            Directory.CreateSymbolicLink(Path.Combine(directory, "link"), outside);
        }
        XslCompiledTransform transform = Load("<xsl:template match='/'><xsl:result-document href='" + href + "'><x/></xsl:result-document></xsl:template>",
            new XsltSettings { EnableResultDocument = true });
        transform.BaseOutputUri = new Uri(Path.Combine(directory, "main.xml"));
        Exception e = Assert.ThrowsAny<Exception>(() => Run(transform));
        Assert.Contains("XTDE1480", e.ToString());
        Assert.False(File.Exists(Path.Combine(outside, "a.xml")));
    }

    /// <summary>A base output URI naming a directory (no file name) confines results to that directory, not its parent.</summary>
    [Fact]
    public void ResultDocumentsUnderABaseDirectory()
    {
        string root = Directory.CreateTempSubdirectory("xsl3-").FullName;
        string directory = Path.Combine(root, "out");
        Directory.CreateDirectory(directory);
        XslCompiledTransform transform = Load("<xsl:template match='/'><xsl:result-document href='../escaped.xml'><x/></xsl:result-document></xsl:template>",
            new XsltSettings { EnableResultDocument = true });
        transform.BaseOutputUri = new Uri(directory);
        Assert.ThrowsAny<Exception>(() => Run(transform));
        Assert.False(File.Exists(Path.Combine(root, "escaped.xml")));
    }

    /// <summary>Settings changed after Load don't change the loaded transform; invalid values are refused when set.</summary>
    [Fact]
    public void SettingsAreTakenAtLoad()
    {
        var settings = new XsltSettings();
        XslCompiledTransform transform = Load("<xsl:template match='/'><out><xsl:value-of select=\"environment-variable('XSL3_SETTINGS_TEST')\"/></out></xsl:template>", settings);
        Environment.SetEnvironmentVariable("XSL3_SETTINGS_TEST", "visible");
        settings.EnableEnvironmentVariables = true;
        Assert.Equal("<out></out>", Run(transform));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Timeout = TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.DedicatedThreadStackSize = -1);
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

    /// <summary>An initial function (XSLT 3.0 §2.3.4) with raw result delivery: the typed values come back as they are.</summary>
    [Fact]
    public void InitialFunctionRawResult()
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:f='urn:f' xmlns:xs='http://www.w3.org/2001/XMLSchema'>" +
                            "<xsl:function name='f:square' as='xs:integer' visibility='public'><xsl:param name='n' as='xs:integer'/><xsl:sequence select='$n * $n'/></xsl:function>" +
                            "<xsl:function name='f:hidden'><xsl:sequence select='1'/></xsl:function></xsl:stylesheet>";
        var transform = new XslCompiledTransform { InitialFunction = new XmlQualifiedName("square", "urn:f"), InitialFunctionArity = 1, RawResult = true };
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var arguments = new XsltArgumentList();
        XmlQualifiedName argument = XslCompiledTransform.InitialFunctionArgument(1);
        arguments.AddParam(argument.Name, argument.Namespace, new System.Xml.XPath.XPathItem[] { Xsl3.Xdm.AtomicValue.OfInteger(12) });
        var items = transform.TransformToItems(new System.Xml.XPath.XPathDocument(new StringReader("<doc/>")).CreateNavigator(), arguments, null);
        Assert.Equal("144", Assert.Single(items).Value);
        Assert.IsAssignableFrom<Xsl3.Xdm.IntegerValue>(items[0]);

        // A private function isn't an entry point (XTDE0041).
        var hidden = new XslCompiledTransform { InitialFunction = new XmlQualifiedName("hidden", "urn:f"), InitialFunctionArity = 0, RawResult = true };
        hidden.Load(XmlReader.Create(new StringReader(stylesheet)));
        Exception e = Assert.ThrowsAny<Exception>(() => hidden.TransformToItems(new System.Xml.XPath.XPathDocument(new StringReader("<doc/>")).CreateNavigator(), null, null));
        Assert.Contains("XTDE0041", e.ToString());
    }

    /// <summary>CLR parameter values become XDM values of their own types in a 2.0+ stylesheet (SPEC §7.1).</summary>
    [Fact]
    public void ClrParametersMapToXdm()
    {
        XslCompiledTransform transform = Load("""
            <xsl:param name="i"/><xsl:param name="d"/><xsl:param name="t"/><xsl:param name="o"/><xsl:param name="s"/><xsl:param name="m"/><xsl:param name="l"/><xsl:param name="u"/><xsl:param name="b"/>
            <xsl:template match="/" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs"><out><xsl:value-of select="
              $i instance of xs:integer, $d instance of xs:decimal, $t instance of xs:dateTime, timezone-from-dateTime($o),
              $s instance of xs:dayTimeDuration, $m?a + $m?b, count($l), $u instance of xs:anyURI, $b instance of xs:base64Binary"/></out></xsl:template>
            """);
        var arguments = new XsltArgumentList();
        arguments.AddParam("i", "", 42);
        arguments.AddParam("d", "", 1.5m);
        arguments.AddParam("t", "", new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc));
        arguments.AddParam("o", "", new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.FromHours(2)));
        arguments.AddParam("s", "", TimeSpan.FromMinutes(90));
        arguments.AddParam("m", "", new System.Collections.Generic.Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        arguments.AddParam("l", "", new[] { 1, 2, 3 });
        arguments.AddParam("u", "", new Uri("http://example.com/"));
        arguments.AddParam("b", "", new byte[] { 1, 2 });
        Assert.Equal("<out>true true true PT2H true 3 3 true true</out>", Run(transform, arguments));
    }

    /// <summary>A parameter value that contains itself is refused, not followed until the stack overflows.</summary>
    [Fact]
    public void CyclicParameterValuesAreRefused()
    {
        var list = new System.Collections.ArrayList();
        list.Add(list);
        var arguments = new XsltArgumentList();
        arguments.AddParam("p", "", list);
        Exception e = Assert.ThrowsAny<Exception>(() => Run(Load("""<xsl:param name="p"/><xsl:template match="/"><out><xsl:value-of select="count($p)"/></out></xsl:template>"""), arguments));
        Assert.Contains("XPTY0004", e.ToString());
    }

    private const string Nested = """
        <xsl:template match="/"><out><xsl:value-of select="f:depth(500)" xmlns:f="urn:f"/></out></xsl:template>
        <xsl:function name="f:depth" xmlns:f="urn:f"><xsl:param name="n"/><xsl:sequence select="if ($n eq 0) then 0 else 1 + f:depth($n - 1)"/></xsl:function>
        """;

    /// <summary>MaxRecursionDepth counts nested calls (SPEC §21.2); tail calls don't nest.</summary>
    [Fact]
    public void MaxRecursionDepth()
    {
        Assert.Equal("<out>500</out>", Run(Load(Nested, new XsltSettings { MaxRecursionDepth = 1000 })));
        Exception e = Assert.ThrowsAny<Exception>(() => Run(Load(Nested, new XsltSettings { MaxRecursionDepth = 100 })));
        Assert.Contains("recursion-limit", e.ToString());

        const string tail = """
            <xsl:template match="/"><out><xsl:call-template name="loop"><xsl:with-param name="n" select="5000"/></xsl:call-template></out></xsl:template>
            <xsl:template name="loop"><xsl:param name="n"/><xsl:choose><xsl:when test="$n eq 0">done</xsl:when>
              <xsl:otherwise><xsl:call-template name="loop"><xsl:with-param name="n" select="$n - 1"/></xsl:call-template></xsl:otherwise></xsl:choose></xsl:template>
            """;
        Assert.Equal("<out>done</out>", Run(Load(tail, new XsltSettings { MaxRecursionDepth = 100 })));
    }

    /// <summary>A depth reached inside xsl:try and abandoned by an error is given back.</summary>
    [Fact]
    public void MaxRecursionDepthAfterACaughtError() =>
        Assert.Equal("<out>caught caught caught</out>", Run(Load("""
            <xsl:template match="/"><out><xsl:value-of select="for $i in 1 to 3 return f:try()" xmlns:f="urn:f"/></out></xsl:template>
            <xsl:function name="f:try" xmlns:f="urn:f"><xsl:try><xsl:sequence select="f:fail(40)"/><xsl:catch select="'caught'"/></xsl:try></xsl:function>
            <xsl:function name="f:fail" xmlns:f="urn:f"><xsl:param name="n"/><xsl:sequence select="if ($n eq 0) then error() else f:fail($n - 1)"/></xsl:function>
            """, new XsltSettings { MaxRecursionDepth = 100 })));

    [Fact]
    public void MaxOutputBytes()
    {
        const string body = """<xsl:template match="/"><out><xsl:value-of select="string-join(for $i in 1 to 1000 return 'abcdefghij')"/></out></xsl:template>""";
        Assert.Equal(10011, Run(Load(body, new XsltSettings { MaxOutputBytes = 20000 })).Length);
        Exception e = Assert.ThrowsAny<Exception>(() => Run(Load(body, new XsltSettings { MaxOutputBytes = 5000 })));
        Assert.Contains("MaxOutputBytes", e.ToString());
        var stream = new MemoryStream();
        Assert.ThrowsAny<Exception>(() => Load(body, new XsltSettings { MaxOutputBytes = 5000 }).Transform(XmlReader.Create(new StringReader("<doc/>")), null, stream));
        Assert.True(stream.Length <= 5000);
    }

    public sealed class Extension
    {
        public string Hello() => "hi";
    }

    /// <summary>
    /// Extension objects are called unless EnableExtensionFunctions is off. (Only 1.0 stylesheets call them: in 2.0+ an
    /// unknown function is XPST0017 when compiled, before any extension object is known.)
    /// </summary>
    [Fact]
    public void ExtensionFunctionsCanBeTurnedOff()
    {
        string stylesheet = $"<xsl:stylesheet version='1.0' xmlns:xsl='{Xsl}' xmlns:e='urn:e' exclude-result-prefixes='e'><xsl:output omit-xml-declaration='yes'/>" +
                            "<xsl:template match='/'><out><xsl:choose><xsl:when test=\"function-available('e:Hello')\"><xsl:value-of select='e:Hello()'/></xsl:when>" +
                            "<xsl:otherwise>none</xsl:otherwise></xsl:choose></out></xsl:template></xsl:stylesheet>";
        var arguments = new XsltArgumentList();
        arguments.AddExtensionObject("urn:e", new Extension());
        foreach (bool enabled in new[] { true, false })
        {
            var transform = new XslCompiledTransform();
            transform.Load(XmlReader.Create(new StringReader(stylesheet)), new XsltSettings { EnableExtensionFunctions = enabled }, null);
            Assert.Equal(enabled ? "<out>hi</out>" : "<out>none</out>", Run(transform, arguments));
        }
    }

    /// <summary>fn:trace reaches XsltArgumentList.TraceHandler with its label and value.</summary>
    [Fact]
    public void TraceHandler()
    {
        var traces = new System.Collections.Generic.List<string>();
        var arguments = new XsltArgumentList { TraceHandler = (label, value) => traces.Add(label + "=" + value) };
        Assert.Equal("<out>3</out>", Run(Load("""<xsl:template match="/"><out><xsl:value-of select="count(trace((1, 'a', doc), 'items'))"/></out></xsl:template>"""), arguments));
        Assert.Equal(new[] { "items=1, a, <doc />" }, traces);
    }

    /// <summary>fn:transform (F&amp;O 3.1 §16.4.1) runs only with EnableEvaluate (FOXT0004 otherwise).</summary>
    [Fact]
    public void FnTransform()
    {
        const string body = """
            <xsl:variable name="inner">&lt;xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'&gt;&lt;xsl:template match='.'&gt;&lt;xsl:sequence select='. * 2'/&gt;&lt;/xsl:template&gt;&lt;/xsl:stylesheet&gt;</xsl:variable>
            <xsl:template match="/"><out><xsl:value-of select="transform(map { 'stylesheet-text': string($inner), 'initial-match-selection': 21, 'delivery-format': 'raw' })?output"/></out></xsl:template>
            """;
        Exception e = Assert.ThrowsAny<Exception>(() => Run(Load(body)));
        Assert.Contains("FOXT0004", e.ToString());
        Assert.Equal("<out>42</out>", Run(Load(body, new XsltSettings { EnableEvaluate = true })));
    }
}
