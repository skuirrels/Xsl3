// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class ImportedSchemaSaveTests
{
#if NET9_0_OR_GREATER
    private const string Schema = "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='record' type='xs:integer'/></xs:sequence></xs:complexType></xs:element></xs:schema>";

    [Theory]
    [InlineData("string", false)]
    [InlineData("string", true)]
    [InlineData("token", false)]
    [InlineData("token", true)]
    public void SavedSchemaAttributePatternsKeepTheirBuiltInType(string type, bool debug)
    {
        string schema = "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema' targetNamespace='urn:attribute-save' xmlns:s='urn:attribute-save'>"
            + "<xs:attribute name='code' type='xs:" + type + "'/><xs:element name='doc'><xs:complexType><xs:attribute ref='s:code'/></xs:complexType></xs:element></xs:schema>";
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:s='urn:attribute-save'>"
            + "<xsl:import-schema namespace='urn:attribute-save'>" + schema + "</xsl:import-schema><xsl:output method='text'/>"
            + "<xsl:template match='/'><xsl:apply-templates select='/*/@*'/></xsl:template>"
            + "<xsl:template match='schema-attribute(s:code)'><xsl:value-of select='.'/></xsl:template>"
            + "<xsl:template match='@*'>unmatched</xsl:template></xsl:stylesheet>";
        var schemas = new System.Xml.Schema.XmlSchemaSet();
        using (var schemaReader = XmlReader.Create(new StringReader(schema))) schemas.Add(null, schemaReader);
        schemas.Compile();
        var settings = new XsltSettings { SchemaAware = true };
        using var saved = new MemoryStream();
        using (var stylesheet = XmlReader.Create(new StringReader(sheet)))
            new XslCompiledTransform(debug).CompileToAssembly(stylesheet, settings, null,
                "AttributeSave" + Guid.NewGuid().ToString("N"), "Saved.Attribute", saved);
        var transform = new XslCompiledTransform();
        transform.Load(Assembly.Load(saved.ToArray()).GetType("Saved.Attribute", throwOnError: true)!, settings);
        const string input = "<s:doc xmlns:s='urn:attribute-save' s:code='retained'/>";
        using var reader = XmlReader.Create(new StringReader(input), new() { ValidationType = ValidationType.Schema, Schemas = schemas });
        using var output = new StringWriter();
        transform.Transform(reader, null, output);
        Assert.Equal("retained", output.ToString());
    }

    [Theory]
    [InlineData(false, false, "strict", "<doc><record>7</record></doc>", "7")]
    [InlineData(false, true, "strict", "<doc><record>7</record></doc>", "7")]
    [InlineData(true, false, "strict", "<doc><record>7</record></doc>", "7")]
    [InlineData(true, true, "strict", "<doc><record>7</record></doc>", "7")]
    [InlineData(true, false, "strict", "<doc><record>bad</record></doc>", "XTTE1510")]
    [InlineData(true, true, "lax", "<doc><record>bad</record></doc>", "XTTE1515")]
    public void SavedSourceValidationUsesItsImportedSchemas(bool streamed, bool debug, string validation, string xml, string expected)
    {
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:err='http://www.w3.org/2005/xqt-errors'>"
            + "<xsl:import-schema>" + Schema + "</xsl:import-schema><xsl:output method='text'/><xsl:template match='/'>"
            + "<xsl:try><xsl:source-document streamable='yes' href='urn:fixture:source' validation='" + validation
            + "'><xsl:value-of select='doc/record'/></xsl:source-document><xsl:catch><xsl:value-of select='local-name-from-QName($err:code)'/></xsl:catch></xsl:try>"
            + "</xsl:template></xsl:stylesheet>";
        var settings = new XsltSettings { SchemaAware = true, EnableDocumentFunction = true, StreamedExecution = streamed };
        RunSaved(sheet, settings, debug, new SourceResolver(xml), expected);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SavedValidationKeepsIncludesAfterOriginalResourcesAreUnavailable(bool streamed, bool debug)
    {
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:t='urn:portable'>"
            + "<xsl:import-schema namespace='urn:portable' schema-location='https://schemas.example.test/main.xsd'/><xsl:output method='text'/><xsl:template match='/'>"
            + "<xsl:source-document href='urn:fixture:source' streamable='yes' validation='strict'><xsl:value-of select='t:row'/></xsl:source-document></xsl:template></xsl:stylesheet>";
        var resolver = new SourceResolver("<row xmlns='urn:portable'>2</row>");
        resolver.Modules["https://schemas.example.test/main.xsd"] = "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema' targetNamespace='urn:portable' xmlns:t='urn:portable'><xs:include schemaLocation='common.xsd'/><xs:element name='row' type='t:Code'/></xs:schema>";
        resolver.Modules["https://schemas.example.test/common.xsd"] = "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema' targetNamespace='urn:portable'><xs:simpleType name='Code'><xs:restriction base='xs:integer'><xs:minInclusive value='2'/></xs:restriction></xs:simpleType></xs:schema>";
        RunSaved(sheet, new XsltSettings { SchemaAware = true, EnableDocumentFunction = true, StreamedExecution = streamed }, debug, resolver, "2");
        Assert.Equal(2, resolver.SchemaUris.Count);
    }

    private static void RunSaved(string sheet, XsltSettings settings, bool debug, SourceResolver resolver, string expected)
    {
        using var saved = new MemoryStream();
        using (XmlReader source = XmlReader.Create(new StringReader(sheet)))
            new XslCompiledTransform(debug).CompileToAssembly(source, settings, resolver, "SavedSchema" + Guid.NewGuid().ToString("N"), "Saved.Style", saved);
        int schemaOpens = resolver.SchemaOpens;
        resolver.Modules.Clear();
        saved.Position = 0;
        var context = new AssemblyLoadContext("schema-save-test", isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromStream(saved);
            var transform = new XslCompiledTransform();
            transform.Load(assembly.GetType("Saved.Style", throwOnError: true)!, settings);
            using XmlReader input = XmlReader.Create(new StringReader("<start/>"));
            using var output = new StringWriter();
            transform.Transform(input, null, output, resolver);
            Assert.Equal(expected, output.ToString());
            Assert.Equal(1, resolver.Opens);
            Assert.Equal(schemaOpens, resolver.SchemaOpens);
            Assert.NotEqual(ReadState.Closed, input.ReadState);
        }
        finally { context.Unload(); }
    }

    private sealed class SourceResolver(string xml) : XmlResolver
    {
        internal int Opens, SchemaOpens;
        internal readonly Dictionary<string, string> Modules = new();
        internal readonly HashSet<string> SchemaUris = new();
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri uri, string? role, Type? type)
        {
            if (Modules.TryGetValue(uri.AbsoluteUri, out string? schema))
            {
                SchemaOpens++;
                SchemaUris.Add(uri.AbsoluteUri);
                return new MemoryStream(Encoding.UTF8.GetBytes(schema));
            }
            Assert.Equal("urn:fixture:source", uri.AbsoluteUri);
            Opens++;
            return new MemoryStream(Encoding.UTF8.GetBytes(xml));
        }
    }
#endif
}
