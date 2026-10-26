// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class SourceValidationNegotiationTests
{
    private const string Schema = "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='record' type='xs:integer'/></xs:sequence></xs:complexType></xs:element></xs:schema>";

    [Theory]
    [InlineData("validation='strict'", "<doc><record>bad</record></doc>", "XTTE1510")]
    [InlineData("validation='lax'", "<doc><record>bad</record></doc>", "XTTE1515")]
    [InlineData("type='xs:integer'", "<value>bad</value>", "XTTE1540")]
    public void ExplicitValidationRequestsBytesBeforeTheProviderValidates(string attributes, string xml, string code)
    {
        using var resolver = new NegotiatingResolver(xml, supportsStream: true);
        var transform = Transformer(attributes);
        transform.DocumentResolver = resolver;
        XsltDynamicException error = Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null));
        Assert.Equal(code, error.Code.LocalName);
        Assert.Equal(typeof(Stream), resolver.RequestedType);
        Assert.Equal(0, resolver.ProviderValidations);
        Assert.Single(resolver.Streams);
        Assert.True(resolver.Streams[0].Closed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AValidSourceKeepsItsResultForEitherProviderCapability(bool supportsStream)
    {
        using var resolver = new NegotiatingResolver("<doc><record>7</record></doc>", supportsStream);
        var transform = Transformer("validation='strict'");
        transform.DocumentResolver = resolver;
        Assert.Equal("2", transform.CallTemplate(null).ToString());
        Assert.Equal(supportsStream ? typeof(Stream) : null, resolver.RequestedType);
        Assert.Equal(supportsStream ? 0 : 1, resolver.ProviderValidations);
        if (supportsStream) Assert.True(Assert.Single(resolver.Streams).Closed);
        else Assert.NotEqual(ReadState.Closed, Assert.Single(resolver.Readers).ReadState);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "validation='strict'")]
    public void OtherProviderValidationErrorsKeepTheirIdentity(bool supportsStream, string attributes)
    {
        using var resolver = new NegotiatingResolver("<doc><record>bad</record></doc>", supportsStream);
        var transform = Transformer(attributes);
        transform.DocumentResolver = resolver;
        XsltDynamicException error = Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null));
        Assert.Equal("XTTE0590", error.Code.LocalName);
        Assert.Null(resolver.RequestedType);
        Assert.Equal(1, resolver.ProviderValidations);
    }

    private static Xslt30Transformer Transformer(string attributes)
    {
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema'>"
            + "<xsl:import-schema>" + Schema + "</xsl:import-schema><xsl:template name='xsl:initial-template'>"
            + "<xsl:source-document streamable='yes' href='urn:fixture:source' " + attributes
            + "><xsl:sequence select='count(//*)'/></xsl:source-document></xsl:template></xsl:stylesheet>";
        return new XsltCompiler { Settings = new() { SchemaAware = true, EnableDocumentFunction = true, StreamedExecution = true } }
            .Compile(new StringReader(sheet)).CreateTransformer();
    }

    private sealed class NegotiatingResolver(string xml, bool supportsStream) : XmlResolver, IDisposable
    {
        internal Type? RequestedType;
        internal int ProviderValidations;
        internal readonly List<TrackedStream> Streams = new();
        internal readonly List<XmlReader> Readers = new();
        public override ICredentials? Credentials { set { } }
        public override bool SupportsType(Uri absoluteUri, Type? type) => type == typeof(Stream) ? supportsStream : type == null || type == typeof(XmlReader);
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            Assert.Equal("urn:fixture:source", absoluteUri.AbsoluteUri);
            RequestedType = ofObjectToReturn;
            if (ofObjectToReturn == typeof(Stream))
            {
                Assert.True(supportsStream);
                var stream = new TrackedStream(System.Text.Encoding.UTF8.GetBytes(xml));
                Streams.Add(stream);
                return stream;
            }
            ProviderValidations++;
            var schemas = new XmlSchemaSet();
            using (var source = XmlReader.Create(new StringReader(Schema))) schemas.Add(null, source);
            schemas.Compile();
            XmlReaderSettings settings = Xsl3.Schema.Validation.ReaderSettings(schemas, strict: true, baseSettings: null,
                message => new XdmException("XTTE0590", "Provider validation: " + message));
            XmlReader reader = XmlReader.Create(new StringReader(xml), settings, absoluteUri.AbsoluteUri);
            Readers.Add(reader);
            return reader;
        }
        public void Dispose() { foreach (XmlReader reader in Readers) reader.Dispose(); foreach (TrackedStream stream in Streams) stream.Dispose(); }
    }

    private sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal bool Closed;
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }
}
