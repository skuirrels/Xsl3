// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Net;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class DocumentReferenceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InvalidSourceReferencesAreRejectedBeforeResolution(bool forward, bool debug)
    {
        foreach (string reference in new[] { @"c:\my\doc\books.xml", @"folder\input.xml", @"https://example.test\input.xml" })
        {
            var resolver = new FixtureResolver();
            string body = $"<xsl:source-document streamable='{(forward ? "yes" : "no")}' href='{reference}'><xsl:sequence select='name(/*)'/></xsl:source-document>";
            Assert.Equal("FODC0005", Run(Catch(body), resolver, forward, debug));
            Assert.Equal(0, resolver.Resolutions);
            Assert.Equal(0, resolver.Opens);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DocReferencesUseTheSameLexicalPolicy(bool debug)
    {
        var resolver = new FixtureResolver();
        Assert.Equal("FODC0005", Run(Catch(@"<xsl:sequence select='name(doc(&quot;folder\input.xml&quot;)/*)'/>"), resolver, false, debug));
        Assert.Equal(0, resolver.Resolutions);
        Assert.Equal(0, resolver.Opens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AvailableFunctionsRejectInvalidReferencesWithoutOpening(bool forward)
    {
        var resolver = new FixtureResolver();
        Assert.Equal("false false", Run(@"<xsl:sequence select='doc-available(&quot;folder\input.xml&quot;), stream-available(&quot;folder\input.xml&quot;)'/>", resolver, forward, false));
        Assert.Equal(0, resolver.Resolutions);
        Assert.Equal(0, resolver.Opens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncodedCharactersAndOrdinaryReferencesReachTheConfiguredResolver(bool forward)
    {
        foreach (string reference in new[] { "relative/input.xml", "relative/encoded%5Cvalue.xml", "urn:fixture:encoded%5Cvalue", "https://example.test/space%20name.xml", "relative/a b.xml", "relative/café.xml" })
        {
            var resolver = new FixtureResolver();
            string body = $"<xsl:source-document streamable='{(forward ? "yes" : "no")}' href='{reference}'><xsl:sequence select='name(/*)'/></xsl:source-document>";
            Assert.Equal("doc", Run(body, resolver, forward, false));
            Assert.Equal(1, resolver.Opens);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetrievalAndXmlErrorsRemainDocumentFailures(bool forward)
    {
        foreach (bool malformed in new[] { false, true })
        {
            var resolver = new FixtureResolver { Fail = !malformed, Xml = malformed ? "<unclosed>" : "<doc/>" };
            string body = $"<xsl:source-document streamable='{(forward ? "yes" : "no")}' href='urn:fixture:input'><xsl:sequence select='count(//*)'/></xsl:source-document>";
            Assert.Equal("FODC0002", Run(Catch(body), resolver, forward, false));
            Assert.Equal(1, resolver.Opens);
        }
    }

    [Fact]
    public void LegacyDocumentResolutionKeepsItsProviderPathPolicy()
    {
        var resolver = new FixtureResolver();
        Assert.Equal("doc", Run(@"<xsl:sequence select='name(document(&quot;folder\input.xml&quot;)/*)'/>", resolver, false, false));
        Assert.Equal(1, resolver.Opens);
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedSourceDocumentsKeepTheirUriCheck(bool debug)
    {
        const string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:err='http://www.w3.org/2005/xqt-errors'>"
            + "<xsl:output method='text'/><xsl:template match='/'><xsl:try><xsl:source-document streamable='yes' href='folder\\input.xml'>"
            + "<xsl:value-of select='name(/*)'/></xsl:source-document><xsl:catch><xsl:value-of select='local-name-from-QName($err:code)'/>"
            + "</xsl:catch></xsl:try></xsl:template></xsl:stylesheet>";
        using var source = XmlReader.Create(new StringReader(sheet));
        using var assembly = new MemoryStream();
        var transform = new XslCompiledTransform(debug);
        var settings = new XsltSettings { EnableDocumentFunction = true, StreamedExecution = true };
        transform.CompileToAssembly(source, settings, null, "DocumentReference" + Guid.NewGuid().ToString("N"), "Saved.DocumentReference", assembly);
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.DocumentReference", true)!, settings);
        using var input = XmlReader.Create(new StringReader("<start/>"));
        using var output = new StringWriter();
        var resolver = new FixtureResolver();
        transform.Transform(input, null, output, resolver);
        Assert.Equal("FODC0005", output.ToString());
        Assert.Equal(0, resolver.Resolutions);
        Assert.Equal(0, resolver.Opens);
    }
#endif

    private static string Catch(string body) => "<xsl:try>" + body + "<xsl:catch><xsl:sequence select='local-name-from-QName($err:code)'/></xsl:catch></xsl:try>";

    private static string Run(string body, FixtureResolver resolver, bool forward, bool debug)
    {
        const string head = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:err='http://www.w3.org/2005/xqt-errors'><xsl:output method='text'/><xsl:template match='/'>";
        var transform = new XslCompiledTransform(debug);
        using var stylesheet = XmlReader.Create(new StringReader(head + body + "</xsl:template></xsl:stylesheet>"), new XmlReaderSettings(), "file:///styles/main.xsl");
        transform.Load(stylesheet, new() { EnableDocumentFunction = true, StreamedExecution = forward }, null);
        using var input = XmlReader.Create(new StringReader("<start/>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output, resolver);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }

    private sealed class FixtureResolver : XmlResolver
    {
        internal int Resolutions;
        internal int Opens;
        internal bool Fail;
        internal string Xml = "<doc/>";
        public override ICredentials? Credentials { set { } }
        public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
        {
            Resolutions++;
            return baseUri == null ? new Uri(relativeUri!, UriKind.Absolute) : new Uri(baseUri, relativeUri);
        }
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            Opens++;
            if (Fail) throw new IOException("Fixture retrieval failure");
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Xml));
        }
    }
}
