// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;
using Xunit;

namespace Xsl3.Tests;

public class StreamedSourceBoundaryTests
{
    private const string Schema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="outer" type="xs:anyType"/>
          <xs:element name="doc"><xs:complexType><xs:sequence>
            <xs:element name="record" type="xs:integer" minOccurs="0" maxOccurs="unbounded"/>
          </xs:sequence><xs:attribute name="q" type="xs:QName"/></xs:complexType></xs:element>
        </xs:schema>
        """;

    private static string Sheet(string attributes = "", string body = "<xsl:value-of select='count(doc/record)'/>", bool initial = false) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xsl:import-schema>{{Schema}}</xsl:import-schema>
          {{(initial ? "<xsl:mode streamable='yes'/>" : "")}}
          <xsl:output method="text"/>
          <xsl:template name="xsl:initial-template" match="/">
            {{(initial ? body : $"<xsl:source-document href='urn:source' streamable='yes' {attributes}>{body}</xsl:source-document>")}}
          </xsl:template>
        </xsl:stylesheet>
        """;

    public static IEnumerable<object[]> Boundaries()
    {
        foreach (bool legacy in new[] { false, true })
        foreach (bool debug in new[] { false, true })
        foreach (bool empty in new[] { false, true })
        foreach (string attributes in new[] { "", "validation='preserve'", "validation='strip'", "validation='strict'" })
            yield return new object[] { legacy, debug, empty, attributes };
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void PositionedResolverReadersCannotExposeFollowingSiblings(bool legacy, bool debug, bool empty, string attributes)
    {
        string subtree = empty ? "<doc/>" : "<doc><record>1</record></doc>";
        using var reader = XmlReader.Create(new StringReader("<outer>" + subtree + "<doc><record>2</record></doc></outer>"));
        Assert.True(reader.ReadToFollowing("doc"));
        Assert.Equal(empty ? "0" : "1", Run(Sheet(attributes), reader, legacy, debug));
        AssertBoundary(reader, empty);
        Assert.True(reader.Read());
        Assert.Equal("doc", reader.LocalName);
        Assert.Equal(XmlNodeType.Element, reader.NodeType);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AnUnreadOrFailedBodyStillReleasesOnlyTheSelectedSubtree(bool legacy, bool debug)
    {
        foreach (bool fail in new[] { false, true })
        {
            using var reader = XmlReader.Create(new StringReader("<outer><doc><record>1</record></doc><after/></outer>"));
            Assert.True(reader.ReadToFollowing("doc"));
            string body = fail ? "<xsl:sequence select=\"error(QName('', 'BOUNDARY0001'))\"/>" : "<xsl:text>done</xsl:text>";
            if (fail) AssertError("BOUNDARY0001", legacy, () => Run(Sheet(body: body), reader, legacy, debug));
            else Assert.Equal("done", Run(Sheet(body: body), reader, legacy, debug));
            AssertBoundary(reader, false);
            Assert.True(reader.Read());
            Assert.Equal("after", reader.LocalName);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InvalidContentOutsideTheSelectionIsNotRead(bool legacy, bool debug)
    {
        using var reader = XmlReader.Create(new StringReader("<outer><doc><record>1</record></doc><broken></outer>"));
        Assert.True(reader.ReadToFollowing("doc"));
        Assert.Equal("1", Run(Sheet(), reader, legacy, debug));
        AssertBoundary(reader, false);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InvalidContentInsideTheSelectionKeepsItsReadError(bool legacy, bool debug)
    {
        using var reader = XmlReader.Create(new StringReader("<outer><doc><record></doc></outer>"));
        Assert.True(reader.ReadToFollowing("doc"));
        AssertError("FODC0002", legacy, () => Run(Sheet(), reader, legacy, debug));
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PositionedTypedReadersRetainInheritedNamespacesAndAnnotationPolicy(bool legacy, bool debug)
    {
        var schemas = new XmlSchemaSet();
        using (var schema = XmlReader.Create(new StringReader(Schema))) schemas.Add(null, schema);
        foreach (bool strip in new[] { false, true })
        foreach (bool initial in new[] { false, true })
        foreach (bool wrapped in new[] { false, true })
        {
            using var reader = XmlReader.Create(new StringReader("<outer xmlns:p='urn:inherited'><doc q='p:item'><record>1</record></doc><after/></outer>"),
                new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas });
            Assert.True(reader.ReadToFollowing("doc"));
            string body = "<xsl:for-each select='doc'><xsl:value-of select=\"namespace-uri-from-QName(resolve-QName('p:item', .)) || '|' || (data(@q) instance of xs:QName)\"/></xsl:for-each>";
            string sheet = Sheet(strip ? "validation='strip'" : "validation='preserve'", body, initial);
            if (initial && strip) sheet = sheet.Replace("version=\"3.0\"", "version=\"3.0\" input-type-annotations='strip'", StringComparison.Ordinal);
            XmlReader supplied = wrapped ? new XmlWrappingReader(reader) : reader;
            Assert.Equal("urn:inherited|" + (strip ? "false" : "true"), Run(sheet, supplied, legacy, debug, initial: initial));
            AssertBoundary(reader, false);
        }
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedNormalAndDebugStylesheetsKeepTheReaderBoundary(bool debug)
    {
        foreach (bool empty in new[] { false, true })
        {
            using var reader = XmlReader.Create(new StringReader(empty ? "<outer><doc/><doc><record>2</record></doc></outer>"
                : "<outer><doc><record>1</record></doc><doc><record>2</record></doc></outer>"));
            Assert.True(reader.ReadToFollowing("doc"));
            Assert.Equal(empty ? "0" : "1", Run(Sheet(), reader, legacy: true, debug, saved: true));
            AssertBoundary(reader, empty);
        }
    }
#endif

    private static void AssertError(string code, bool legacy, Action run)
    {
        if (legacy) Assert.Equal(code, Assert.Throws<Xdm.XdmException>(run).Code);
        else Assert.Equal(code, Assert.Throws<XsltDynamicException>(run).Code.LocalName);
    }

    private static string Run(string sheet, XmlReader reader, bool legacy, bool debug, bool saved = false, bool initial = false)
    {
        var resolver = new ReaderResolver(reader);
        using var output = new StringWriter();
        var settings = new XsltSettings { StreamedExecution = true, EnableDocumentFunction = true, SchemaAware = true, IncludeDebugInformation = debug };
        if (legacy)
        {
            var transform = new XslCompiledTransform(debug);
            using var source = XmlReader.Create(new StringReader(sheet));
            if (saved)
            {
#if NET9_0_OR_GREATER
                using var image = new MemoryStream();
                transform.CompileToAssembly(source, settings, null, "ReaderBoundary" + Guid.NewGuid().ToString("N"), "Saved.Boundary", image);
                transform.Load(Assembly.Load(image.ToArray()).GetType("Saved.Boundary", true)!, settings);
#endif
            }
            else transform.Load(source, settings, null);
            using var input = XmlReader.Create(new StringReader("<unused/>"));
            transform.Transform(initial ? reader : input, null, output, resolver);
        }
        else
        {
            var transform = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
            transform.DocumentResolver = resolver;
            if (initial) transform.ApplyTemplates(reader, output);
            else transform.CallTemplate(null, output);
        }
        return output.ToString();
    }

    private static void AssertBoundary(XmlReader reader, bool empty)
    {
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
        Assert.Equal("doc", reader.LocalName);
        Assert.Equal(empty ? XmlNodeType.Element : XmlNodeType.EndElement, reader.NodeType);
    }

    private sealed class ReaderResolver(XmlReader reader) : XmlResolver
    {
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn) => reader;
    }
}
