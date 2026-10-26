// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class ModeDeclarationTests
{
    private static string Sheet(string body) => "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>" + body + "</xsl:stylesheet>";

    private static XslCompiledTransform Compile(string principal, params (string Name, string Body)[] modules)
    {
        var resolver = new Modules();
        foreach (var (name, body) in modules) resolver.Sheets.Add(new Uri("https://styles.test/" + name), Sheet(body));
        var transform = new XslCompiledTransform();
        using var reader = XmlReader.Create(new StringReader(Sheet(principal)), new XmlReaderSettings(), "https://styles.test/main.xsl");
        transform.Load(reader, new XsltSettings(), resolver);
        return transform;
    }

    [Theory]
    [InlineData("")]
    [InlineData("name='m'")]
    public void HigherDeclarationOfAnotherAttributeDoesNotResolveConflicts(string name)
    {
        var error = Assert.ThrowsAny<XsltException>(() => Compile("<xsl:import href='lower.xsl'/><xsl:mode " + name + " on-no-match='deep-copy'/>",
            ("lower.xsl", "<xsl:include href='first.xsl'/><xsl:include href='second.xsl'/>"),
            ("first.xsl", "<xsl:mode " + name + " streamable='no'/>"),
            ("second.xsl", "<xsl:mode " + name + " streamable='yes'/>")));
        Assert.Contains("XTSE0545", error.Message);
    }

    [Theory]
    [InlineData("no")]
    [InlineData("yes")]
    public void HigherDefinitionOfTheSameAttributeResolvesConflicts(string streamable)
    {
        Compile("<xsl:import href='lower.xsl'/><xsl:mode streamable='" + streamable + "'/>",
            ("lower.xsl", "<xsl:mode streamable='no'/><xsl:mode streamable='yes'/>"));
    }

    [Theory]
    [InlineData("streamable", "yes", "true")]
    [InlineData("streamable", "true", "1")]
    [InlineData("streamable", "no", "0")]
    [InlineData("warning-on-no-match", "yes", "1")]
    [InlineData("warning-on-multiple-match", "no", "false")]
    [InlineData("typed", "yes", "true")]
    [InlineData("typed", "false", "0")]
    public void BooleanLexicalAliasesHaveTheSameValue(string attribute, string first, string second)
    {
        Compile("<xsl:mode " + attribute + "='" + first + "'/><xsl:mode " + attribute + "='" + second + "'/>");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EffectiveTypedAttributeIsInheritedIndependently(bool imported)
    {
        string declaration = "<xsl:mode typed='strict'/>";
        string other = "<xsl:mode on-no-match='shallow-copy'/>";
        string templates = "<xsl:template match='/'><xsl:apply-templates select='doc'/></xsl:template>";
        var transform = imported ? Compile("<xsl:import href='lower.xsl'/>" + other + templates, ("lower.xsl", declaration))
            : Compile(other + declaration + templates);
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var output = new StringWriter();
        var error = Assert.ThrowsAny<XsltException>(() => transform.Transform(input, null, output));
        Assert.Contains("XTTE3100", error.Message);
    }

    [Fact]
    public void IndependentAttributesRetainTheirEffectiveValues()
    {
        var transform = Compile("<xsl:import href='lower.xsl'/><xsl:mode on-multiple-match='fail'/>",
            ("lower.xsl", "<xsl:mode on-no-match='deep-copy'/><xsl:mode typed='no'/>"));
        using var input = XmlReader.Create(new StringReader("<doc a='x'><child/></doc>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Contains("<doc a=\"x\"><child", output.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackageModeVisibilityIsInheritedIndependently(bool imported)
    {
        var resolver = new Modules();
        resolver.Sheets.Add(new Uri("https://styles.test/lower.xsl"), Sheet("<xsl:mode name='m' visibility='public'/>"));
        string modes = imported ? "<xsl:import href='lower.xsl'/><xsl:mode name='m' on-no-match='deep-copy'/>"
            : "<xsl:mode name='m' on-no-match='deep-copy'/><xsl:mode name='m' visibility='public'/>";
        string package = "<xsl:package version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' name='urn:mode-test' package-version='1.0'>"
            + modes + "<xsl:output omit-xml-declaration='yes'/></xsl:package>";
        var transform = new XslCompiledTransform { InitialMode = new XmlQualifiedName("m") };
        using var stylesheet = XmlReader.Create(new StringReader(package), new XmlReaderSettings(), "https://styles.test/main.xsl");
        transform.Load(stylesheet, new XsltSettings(), resolver);
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("<doc/>", output.ToString());
    }

    [Fact]
    public void ExplicitHigherPrivateModeIsNotMadePublicByAnImport()
    {
        var resolver = new Modules();
        resolver.Sheets.Add(new Uri("https://styles.test/lower.xsl"), Sheet("<xsl:mode name='m' visibility='public'/>"));
        string package = "<xsl:package version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' name='urn:mode-test' package-version='1.0'>"
            + "<xsl:import href='lower.xsl'/><xsl:mode name='m' visibility='private'/></xsl:package>";
        var transform = new XslCompiledTransform { InitialMode = new XmlQualifiedName("m") };
        using var stylesheet = XmlReader.Create(new StringReader(package), new XmlReaderSettings(), "https://styles.test/main.xsl");
        Assert.Contains("XTDE0045", Assert.ThrowsAny<XsltException>(() => transform.Load(stylesheet, new XsltSettings(), resolver)).Message);
    }

    [Fact]
    public void InvalidMultipleMatchPolicyIsRejected()
    {
        Assert.Contains("XTSE0020", Assert.ThrowsAny<XsltException>(() => Compile("<xsl:mode on-multiple-match='invalid'/>")).Message);
    }

    private sealed class Modules : XmlResolver
    {
        public Dictionary<Uri, string> Sheets { get; } = new();
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn) =>
            new MemoryStream(Encoding.UTF8.GetBytes(Sheets[absoluteUri]));
    }
}
