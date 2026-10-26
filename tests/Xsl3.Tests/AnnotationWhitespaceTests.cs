// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Net;
using System.Xml;
using System.Xml.Schema;
using Xunit;

namespace Xsl3.Tests;

public class AnnotationWhitespaceTests
{
    private const string Schema = "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='text' type='xs:string'/></xs:sequence></xs:complexType></xs:element></xs:schema>";

    [Theory]
    [InlineData(true, "strip", "", false, "0|true")]
    [InlineData(true, "strip", "strip", false, "0|true")]
    [InlineData(true, "strip", "preserve", false, "2|false")]
    [InlineData(true, "preserve", "", false, "2|false")]
    [InlineData(true, "strip", "", true, "2|true")]
    [InlineData(false, "strip", "", false, "0|true")]
    [InlineData(false, "preserve", "", false, "2|false")]
    [InlineData(false, "strip", "", true, "2|true")]
    public void AnnotationStrippingPrecedesWhitespaceRules(bool sourceDocument, string annotations, string validation, bool preserveSpace, string expected)
    {
        string entry = sourceDocument ? "name='xsl:initial-template'" : "match='/'";
        string body = "<xsl:for-each select='doc/text'><xsl:variable name='v' select='data(.)'/><xsl:value-of select=\"concat(string-length($v), '|', $v instance of xs:untypedAtomic)\"/></xsl:for-each>";
        if (sourceDocument) body = "<xsl:source-document streamable='yes' href='urn:fixture:typed' "
            + (validation.Length == 0 ? "" : "validation='" + validation + "'") + ">" + body + "</xsl:source-document>";
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' input-type-annotations='" + annotations + "'>"
            + "<xsl:mode streamable='yes'/><xsl:output method='text'/><xsl:strip-space elements='*'/><xsl:template " + entry + ">" + body + "</xsl:template></xsl:stylesheet>";
        var transform = new XsltCompiler { Settings = new() { SchemaAware = true, EnableDocumentFunction = true, StreamedExecution = true } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using XmlReader reader = Input(preserveSpace);
        transform.DocumentResolver = new ReaderResolver(reader);
        string actual = sourceDocument ? transform.CallTemplate(null).ToString() : transform.ApplyTemplates(reader).ToString();
        Assert.Equal(expected, actual);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    private static XmlReader Input(bool preserveSpace)
    {
        var schemas = new XmlSchemaSet();
        using (XmlReader schema = XmlReader.Create(new StringReader(Schema))) schemas.Add(null, schema);
        schemas.Compile();
        return XmlReader.Create(new StringReader("<doc><text" + (preserveSpace ? " xml:space='preserve'" : "") + ">  </text></doc>"),
            new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas });
    }

    private sealed class ReaderResolver(XmlReader reader) : XmlResolver
    {
        public override ICredentials? Credentials { set { } }
        public override bool SupportsType(Uri uri, Type? type) => type == null || type == typeof(XmlReader);
        public override object GetEntity(Uri uri, string? role, Type? type) => reader;
    }
}
