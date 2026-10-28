// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xunit;

namespace Xsl3.Tests;

public class SchemaValidationContractTests
{
    private const string Schema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="doc"><xs:complexType><xs:sequence>
            <xs:element name="record" type="xs:integer"/>
          </xs:sequence></xs:complexType></xs:element>
        </xs:schema>
        """;

    public static IEnumerable<object[]> Paths()
    {
        foreach (bool legacy in new[] { false, true })
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
            yield return new object[] { legacy, debug, forward };
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void BasicSourcePoliciesStripSuppliedAnnotations(bool legacy, bool debug, bool forward)
    {
        foreach (string policy in new[] { "lax", "strip", "preserve" })
        {
            using var reader = Source(validated: true);
            Assert.NotNull(reader.SchemaInfo?.SchemaType);
            var settings = Settings(debug, forward, schemaAware: false);
            Assert.Equal("true", Run(SourceSheet(policy), settings, reader, legacy));
            if (forward) Assert.NotEqual(ReadState.Closed, reader.ReadState);
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void SchemaAwareLaxSourceWithoutDeclarationsUsesAnyType(bool legacy, bool debug, bool forward)
    {
        using var reader = Source(validated: false);
        Assert.Equal("false", Run(SourceSheet("lax"), Settings(debug, forward, schemaAware: true), reader, legacy));
        if (forward) Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void SuppliedAnnotationsDoNotNeedAnImportWhenPreserved(bool legacy, bool debug, bool forward)
    {
        using var reader = Source(validated: true);
        Assert.Equal("true", Run(SourceSheet("preserve", untyped: false), Settings(debug, forward, schemaAware: true), reader, legacy));
        if (forward) Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void StrippedSourcesApplyWhitespaceRulesToUntypedContent(bool legacy, bool debug, bool forward)
    {
        var schemas = new XmlSchemaSet();
        using (var schema = XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc' type='xs:string'/></xs:schema>")))
            schemas.Add(null, schema);
        schemas.Compile();
        string sheet = Sheet("""
            <xsl:source-document href="urn:p7d:source" streamable="yes" validation="strip">
              <xsl:value-of select="count(doc/text())"/>
            </xsl:source-document>
            """).Replace("<xsl:output", "<xsl:strip-space elements='*'/><xsl:output", StringComparison.Ordinal);
        foreach (bool preserveSpace in new[] { false, true })
        {
            using var reader = XmlReader.Create(new StringReader(preserveSpace ? "<doc xml:space='preserve'> </doc>" : "<doc> </doc>"),
                new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas,
                    ValidationFlags = XmlSchemaValidationFlags.ProcessIdentityConstraints | XmlSchemaValidationFlags.AllowXmlAttributes });
            reader.MoveToContent();
            Assert.Equal(preserveSpace ? "1" : "0", Run(sheet, Settings(debug, forward, schemaAware: true), reader, legacy));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GroundedStrippingPreservesSourceIdentityMetadata(bool legacy, bool debug)
    {
        var schemas = new XmlSchemaSet();
        using (var schema = XmlReader.Create(new StringReader("""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="doc"><xs:complexType><xs:simpleContent><xs:extension base="xs:string">
                <xs:attribute name="id" type="xs:ID"/><xs:attribute name="ref" type="xs:IDREF"/>
                <xs:attribute name="q" type="xs:QName"/>
              </xs:extension></xs:simpleContent></xs:complexType></xs:element>
            </xs:schema>
            """))) schemas.Add(null, schema);
        schemas.Compile();
        using var reader = XmlReader.Create(new StringReader("<doc xmlns:p='urn:p7d:namespace' id='selected' ref='selected' q='p:item'> </doc>"),
            new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas }, "urn:p7d:source");
        reader.MoveToContent();
        string sheet = Sheet("""
            <xsl:source-document href="urn:p7d:source" validation="strip">
              <xsl:for-each select="doc">
                <xsl:value-of select="concat(count(text()), '|', namespace-uri-from-QName(resolve-QName(string(@q), .)), '|', count(id(string(@id))), '|', count(idref(string(@ref))), '|', document-uri(root(.)))"/>
              </xsl:for-each>
            </xsl:source-document>
            """).Replace("<xsl:output", "<xsl:strip-space elements='*'/><xsl:output", StringComparison.Ordinal);
        Assert.Equal("0|urn:p7d:namespace|1|1|urn:p7d:source", Run(sheet, Settings(debug, forward: false, schemaAware: true), reader, legacy));
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void StrictValidationNeedsDeclarationsImportedFromTheConfiguredCatalog(bool legacy, bool debug, bool forward)
    {
        var settings = Settings(debug, forward, schemaAware: true);
        settings.Schemas = Schemas();
        using var missing = Source(validated: true);
        if (legacy) Assert.Equal("XTTE1512", Assert.Throws<Xdm.XdmException>(() => Run(SourceSheet("strict"), settings, missing, legacy)).Code);
        else Assert.Equal("XTTE1512", Assert.Throws<XsltDynamicException>(() => Run(SourceSheet("strict"), settings, missing, legacy)).Code.LocalName);

        using var declared = Source(validated: false);
        Assert.Equal("true", Run(SourceSheet("strict", import: true, untyped: false), settings, declared, legacy));
        if (forward)
        {
            Assert.NotEqual(ReadState.Closed, missing.ReadState);
            Assert.NotEqual(ReadState.Closed, declared.ReadState);
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void ResultLaxValidationFollowsTheProcessorsSchemaAwareness(bool legacy, bool debug, bool forward)
    {
        foreach (bool aware in new[] { false, true })
        {
            XPathNavigator? result = null;
            var previous = XmlQueryRuntime.ResultTreeHandler;
            try
            {
                XmlQueryRuntime.ResultTreeHandler = (_, tree) => result = tree;
                using var reader = Source(validated: false);
                Run(Sheet("""
                    <xsl:source-document href="urn:p7d:source" streamable="yes">
                      <xsl:result-document href="urn:p7d:result" method="xml" validation="lax">
                        <out><xsl:copy-of select="doc/record"/></out>
                      </xsl:result-document>
                    </xsl:source-document>
                    """), Settings(debug, forward, aware), reader, legacy);
                Assert.NotNull(result);
                XPathNavigator record = result.SelectSingleNode("/out/record")!;
                Assert.NotNull(record);
                if (aware) Assert.Equal(XmlTypeCode.Item, record.SchemaInfo!.SchemaType!.TypeCode); // xs:anyType
                else Assert.Null(record.SchemaInfo?.SchemaType); // xs:untyped
            }
            finally { XmlQueryRuntime.ResultTreeHandler = previous; }
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void BasicStrictValidationStillRequiresSchemaAwareness(bool legacy, bool debug, bool forward)
    {
        using var reader = Source(validated: false);
        var settings = Settings(debug, forward, schemaAware: false);
        if (legacy) Assert.Contains("XTSE1660", Assert.ThrowsAny<XsltException>(() => Run(SourceSheet("strict"), settings, reader, legacy)).Message);
        else Assert.Contains("XTSE1660", Assert.Throws<XsltStaticException>(() => Run(SourceSheet("strict"), settings, reader, legacy)).Message);
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SavedBasicLaxSourcesRetainTheStrippingContract(bool debug, bool forward)
    {
        using var reader = Source(validated: true);
        Assert.Equal("true", Run(SourceSheet("lax"), Settings(debug, forward, schemaAware: false), reader, legacy: true, saved: true));
        if (forward) Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }
#endif

    private static string SourceSheet(string validation, bool import = false, bool untyped = true) => Sheet($$"""
        <xsl:source-document href="urn:p7d:source" streamable="yes" validation="{{validation}}">
          <xsl:value-of select="doc/record instance of element(*, xs:{{(untyped ? "untyped" : "integer")}})"/>
        </xsl:source-document>
        """, import);

    private static string Sheet(string body, bool import = false) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
          {{(import ? "<xsl:import-schema/>" : "")}}
          <xsl:output method="text"/>
          <xsl:template match="/" name="xsl:initial-template">{{body}}</xsl:template>
        </xsl:stylesheet>
        """;

    private static XsltSettings Settings(bool debug, bool forward, bool schemaAware) => new()
    {
        IncludeDebugInformation = debug, StreamedExecution = forward,
        EnableDocumentFunction = true, SchemaAware = schemaAware,
    };

    private static XmlSchemaSet Schemas()
    {
        var schemas = new XmlSchemaSet();
        using var reader = XmlReader.Create(new StringReader(Schema));
        schemas.Add(null, reader);
        schemas.Compile();
        return schemas;
    }

    private static XmlReader Source(bool validated)
    {
        var settings = new XmlReaderSettings();
        if (validated)
        {
            settings.ValidationType = ValidationType.Schema;
            settings.Schemas = Schemas();
        }
        XmlReader reader = XmlReader.Create(new StringReader("<doc><record>7</record></doc>"), settings);
        reader.MoveToContent();
        return reader;
    }

    private static string Run(string sheet, XsltSettings settings, XmlReader reader, bool legacy, bool saved = false)
    {
        var resolver = new ReaderResolver(reader);
        using var output = new StringWriter();
        using var secondary = new StringWriter();
        if (legacy)
        {
            var transform = new XslCompiledTransform(settings.IncludeDebugInformation);
            using var stylesheet = XmlReader.Create(new StringReader(sheet));
            if (saved)
            {
#if NET9_0_OR_GREATER
                using var image = new MemoryStream();
                transform.CompileToAssembly(stylesheet, settings, null, "P7D" + Guid.NewGuid().ToString("N"), "Saved.Contracts", image);
                transform.Load(Assembly.Load(image.ToArray()).GetType("Saved.Contracts", true)!, settings);
#else
                throw new NotSupportedException("Saved emission requires .NET 9 or later.");
#endif
            }
            else transform.Load(stylesheet, settings, null);
            using var input = XmlReader.Create(new StringReader("<unused/>"));
            transform.Transform(input, new XsltArgumentList { ResultDocumentHandler = _ => secondary }, output, resolver);
        }
        else
        {
            var transform = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
            transform.DocumentResolver = resolver;
            transform.ResultDocumentHandler = _ => secondary;
            transform.CallTemplate(null, output);
        }
        return output.ToString();
    }

    private sealed class ReaderResolver(XmlReader reader) : XmlResolver
    {
        public override bool SupportsType(Uri absoluteUri, Type? type) => type == null || type == typeof(XmlReader);

        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            Assert.Equal("urn:p7d:source", absoluteUri.AbsoluteUri);
            Assert.True(ofObjectToReturn == null || ofObjectToReturn == typeof(XmlReader));
            return reader;
        }
    }
}
