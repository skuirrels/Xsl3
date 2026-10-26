// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Schema;
using Xunit;

namespace Xsl3.Tests;

public class SchemaSetSerializationTests
{
    private const string Head = "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'";
    private const string Location = "https://schemas.example.test/main.xsd";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncludedModulesKeepFacetsAndCanContainAnIncludeCycle(bool cycle)
    {
        string main = Head + " targetNamespace='urn:include' xmlns:t='urn:include'><xs:include schemaLocation='common.xsd'/><xs:element name='row' type='t:Code'/></xs:schema>";
        string common = Head + " targetNamespace='urn:include'>" + (cycle ? "<xs:include schemaLocation='main.xsd'/>" : "")
            + "<xs:simpleType name='Code'><xs:restriction base='xs:integer'><xs:minInclusive value='2'/></xs:restriction></xs:simpleType></xs:schema>";
        var resolver = new Modules(new() { [Location] = main, ["https://schemas.example.test/common.xsd"] = common });
        XmlSchemaSet original = Build(main, resolver);
        XmlSchemaSet restored = RoundTrip(original);
        AssertValid(restored, "<row xmlns='urn:include'>2</row>", true);
        AssertValid(restored, "<row xmlns='urn:include'>1</row>", false);
        Assert.Equal(original.Schemas().Cast<XmlSchema>().Select(s => s.SourceUri), restored.Schemas().Cast<XmlSchema>().Select(s => s.SourceUri));
    }

    [Fact]
    public void ImportsKeepNamespaceTypesAndDefaultAttributes()
    {
        string main = Head + " targetNamespace='urn:main' xmlns:t='urn:types'><xs:import namespace='urn:types' schemaLocation='types.xsd'/><xs:element name='row'><xs:complexType><xs:attribute name='code' type='t:Code' default='ABC'/></xs:complexType></xs:element></xs:schema>";
        string types = Head + " targetNamespace='urn:types'><xs:simpleType name='Code'><xs:restriction base='xs:string'><xs:pattern value='[A-Z]+'/></xs:restriction></xs:simpleType></xs:schema>";
        XmlSchemaSet restored = RoundTrip(Build(main, new Modules(new() { ["https://schemas.example.test/types.xsd"] = types })));
        AssertValid(restored, "<row xmlns='urn:main' code='abc'/>", false);
        using XmlReader reader = XmlReader.Create(new StringReader("<row xmlns='urn:main'/>"), new() { ValidationType = ValidationType.Schema, Schemas = restored });
        Assert.True(reader.Read());
        Assert.Equal("ABC", reader.GetAttribute("code"));
        Assert.True(reader.MoveToAttribute("code"));
        Assert.True(reader.IsDefault);
    }

    [Fact]
    public void ChameleonIncludesRetainTheirAdoptedNamespace()
    {
        string main = Head + " targetNamespace='urn:adopt' xmlns:t='urn:adopt'><xs:include schemaLocation='common.xsd'/><xs:element name='row' type='t:Code'/></xs:schema>";
        string common = Head + "><xs:simpleType name='Code'><xs:restriction base='xs:integer'><xs:maxInclusive value='3'/></xs:restriction></xs:simpleType></xs:schema>";
        XmlSchemaSet restored = RoundTrip(Build(main, new Modules(new() { ["https://schemas.example.test/common.xsd"] = common })));
        AssertValid(restored, "<row xmlns='urn:adopt'>3</row>", true);
        AssertValid(restored, "<row xmlns='urn:adopt'>4</row>", false);
    }

    [Fact]
    public void RedefinitionsRetainBothBaseAndRestrictingFacets()
    {
        string main = Head + " targetNamespace='urn:redefine' xmlns:t='urn:redefine'><xs:redefine schemaLocation='common.xsd'><xs:simpleType name='Code'><xs:restriction base='t:Code'><xs:pattern value='[A-Z]+'/></xs:restriction></xs:simpleType></xs:redefine><xs:element name='row' type='t:Code'/></xs:schema>";
        string common = Head + " targetNamespace='urn:redefine'><xs:simpleType name='Code'><xs:restriction base='xs:string'><xs:maxLength value='3'/></xs:restriction></xs:simpleType></xs:schema>";
        XmlSchemaSet restored = RoundTrip(Build(main, new Modules(new() { ["https://schemas.example.test/common.xsd"] = common })));
        AssertValid(restored, "<row xmlns='urn:redefine'>ABC</row>", true);
        AssertValid(restored, "<row xmlns='urn:redefine'>ABCD</row>", false);
        AssertValid(restored, "<row xmlns='urn:redefine'>abc</row>", false);
    }

    [Fact]
    public void DuplicateIncludesKeepOneSharedModule()
    {
        string main = Head + "><xs:include schemaLocation='common.xsd'/><xs:include schemaLocation='common.xsd'/><xs:element name='row' type='Code'/></xs:schema>";
        string common = Head + "><xs:simpleType name='Code'><xs:restriction base='xs:integer'/></xs:simpleType></xs:schema>";
        XmlSchema root = RoundTrip(Build(main, new Modules(new() { ["https://schemas.example.test/common.xsd"] = common }))).Schemas().Cast<XmlSchema>().Single();
        Assert.NotNull(((XmlSchemaExternal)root.Includes[0]).Schema);
        Assert.Same(((XmlSchemaExternal)root.Includes[0]).Schema, ((XmlSchemaExternal)root.Includes[1]).Schema);
    }

    [Fact]
    public void EmptyCompiledSetsRetainTheirCompilationPolicy()
    {
        var schemas = new XmlSchemaSet();
        schemas.CompilationSettings.EnableUpaCheck = false;
        schemas.Compile();
        XmlSchemaSet restored = RoundTrip(schemas);
        Assert.True(restored.IsCompiled);
        Assert.False(restored.CompilationSettings.EnableUpaCheck);
        Assert.Empty(restored.Schemas());
    }

    [Fact]
    public void UncompiledSetsAreNotSilentlyChangedBySaving()
    {
        var schemas = new XmlSchemaSet();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        Assert.Throws<NotSupportedException>(() => SchemaSetSerializer.Write(writer, schemas));
        Assert.False(schemas.IsCompiled);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(2, 1)]
    [InlineData(0, 1)]
    public void InvalidGraphSizesAreRejected(int roots, int count)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        { writer.Write(1); writer.Write(true); writer.Write(roots); writer.Write(count); }
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.Throws<InvalidDataException>(() => SchemaSetSerializer.Read(reader));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedStaticNodesKeepBuiltInAnnotationsWithoutImportedSchemas(bool element)
    {
        var schemas = new XmlSchemaSet();
        using (XmlReader schema = XmlReader.Create(new StringReader(Head + "><xs:element name='row' type='xs:integer'/></xs:schema>")))
            schemas.Add(null, schema);
        schemas.Compile();
        using XmlReader reader = XmlReader.Create(new StringReader("<row>7</row>"), new() { ValidationType = ValidationType.Schema, Schemas = schemas });
        var node = Xsl3.Tree.CompactDocument.Load(reader, XmlSpace.Preserve, typed: true).CreateNavigator();
        if (element) Assert.True(node.MoveToFirstChild());
        var constants = new Xsl3.XPath31.ConstantTable();
        constants.Add(new System.Xml.XPath.XPathItem[] { node });
        using var stream = new MemoryStream();
        using var writer = new Xsl3.Runtime.XmlQueryDataWriter(stream);
        Xsl3.XPath31.ConstantSerializer.Write(writer, constants); writer.Flush();
        using var saved = new MemoryStream(stream.ToArray()); using var savedReader = new Xsl3.Runtime.XmlQueryDataReader(saved);
        var restored = Xsl3.XPath31.ConstantSerializer.Read(savedReader);
        var value = (System.Xml.XPath.XPathNavigator)((System.Collections.Generic.IList<System.Xml.XPath.XPathItem>)restored.Get(0)!)[0];
        Assert.Equal(node.NodeType, value.NodeType);
        if (!element) Assert.True(value.MoveToFirstChild());
        Assert.Equal(XmlTypeCode.Integer, value.SchemaInfo!.SchemaType!.TypeCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnownedSchemaMetadataIsRejectedWithoutAnInScopeSchemaSet(bool map)
    {
        XmlSchemaSet schemas = Build(Head + "><xs:simpleType name='Code'><xs:restriction base='xs:integer'/></xs:simpleType></xs:schema>", new Modules(new()));
        Xsl3.Xdm.AtomicType type = SchemaTypes.AtomicTypeOf((XmlSchemaType)schemas.GlobalTypes[new XmlQualifiedName("Code")]!)!;
        var constants = new Xsl3.XPath31.ConstantTable();
        constants.Add(map ? new Xsl3.Xdm.SequenceTypeTest(new Xsl3.Xdm.MapTest(type, Xsl3.Xdm.SequenceTypeTest.AnySequence), Xsl3.Xdm.Cardinality.ExactlyOne)
            : Xsl3.Xdm.Casting.Cast(Xsl3.Xdm.AtomicValue.OfString("2"), type));
        using var stream = new MemoryStream();
        using var writer = new Xsl3.Runtime.XmlQueryDataWriter(stream);
        NotSupportedException error = Assert.Throws<NotSupportedException>(() => Xsl3.XPath31.ConstantSerializer.Write(writer, constants));
        Assert.Contains("compilation's schema contexts", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MismatchedOrOutOfRangeModuleEdgesAreRejected(bool invalidTarget)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1); writer.Write(true); writer.Write(1); writer.Write(1); writer.Write(Location);
            writer.Write(Head + ">" + (invalidTarget ? "<xs:include schemaLocation='other.xsd'/>" : "") + "</xs:schema>");
            writer.Write(1); writer.Write(1);
        }
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.Throws<InvalidDataException>(() => SchemaSetSerializer.Read(reader));
    }

    private static XmlSchemaSet Build(string main, Modules resolver)
    {
        var set = new XmlSchemaSet { XmlResolver = resolver };
        using XmlReader reader = XmlReader.Create(new StringReader(main), new XmlReaderSettings { XmlResolver = null }, Location);
        set.Add(null, reader);
        set.Compile();
        return set;
    }

    private static XmlSchemaSet RoundTrip(XmlSchemaSet original)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) SchemaSetSerializer.Write(writer, original);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        XmlSchemaSet restored = SchemaSetSerializer.Read(reader);
        Assert.Equal(stream.Length, stream.Position);
        Assert.True(original.IsCompiled);
        Assert.Equal(original.GlobalElements.Count, restored.GlobalElements.Count);
        Assert.Equal(original.GlobalTypes.Count, restored.GlobalTypes.Count);
        return restored;
    }

    private static void AssertValid(XmlSchemaSet schemas, string xml, bool expected)
    {
        bool valid = true;
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas, XmlResolver = null };
        settings.ValidationEventHandler += (_, e) => { if (e.Severity == XmlSeverityType.Error) valid = false; };
        using XmlReader reader = XmlReader.Create(new StringReader(xml), settings);
        while (reader.Read()) { }
        Assert.Equal(expected, valid);
    }

    private sealed class Modules(Dictionary<string, string> sources) : XmlResolver
    {
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri uri, string? role, Type? type) =>
            new MemoryStream(Encoding.UTF8.GetBytes(sources[uri.AbsoluteUri]));
    }
}
