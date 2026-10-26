// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.IO;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Schema;
using Xsl3.Tree;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class SavedTypedNodeTests
{
    private static (XmlSchemaSet, XPathNavigator) Input()
    {
        var schemas = new XmlSchemaSet();
        using (var schema = XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema' targetNamespace='urn:typed' xmlns:t='urn:typed' elementFormDefault='qualified'><xs:simpleType name='small'><xs:restriction base='xs:integer'><xs:minInclusive value='1'/><xs:maxInclusive value='3'/></xs:restriction></xs:simpleType><xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='n' type='t:small'/><xs:element name='nil' type='xs:string' nillable='true'/></xs:sequence><xs:attribute name='id' type='xs:ID'/><xs:attribute name='refs' type='xs:IDREFS'/><xs:attribute name='q' type='xs:QName'/></xs:complexType></xs:element></xs:schema>"))) schemas.Add(null, schema);
        schemas.Compile();
        using var reader = XmlReader.Create(new StringReader("<t:doc xmlns:t='urn:typed' xmlns:v='urn:value' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' id='r' refs='r' q='v:name'><t:n>2</t:n><t:nil xsi:nil='true'/></t:doc>"), new() { ValidationType = ValidationType.Schema, Schemas = schemas }, "file:///typed/input.xml");
        return (schemas, CompactDocument.Load(reader, XmlSpace.Preserve, typed: true).CreateNavigator());
    }

    private static ConstantTable Save(ConstantTable table)
    {
        using var buffer = new MemoryStream();
        using var writer = new XmlQueryDataWriter(buffer);
        ConstantSerializer.Write(writer, table);
        writer.Flush();
        using var input = new MemoryStream(buffer.ToArray());
        using var reader = new XmlQueryDataReader(input);
        return ConstantSerializer.Read(reader);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedTreesKeepAnnotationsNilledAttributesAndSharedNodes(bool constructed)
    {
        var (schemas, node) = Input();
        ((CompactNavigator)node).Document._constructed = constructed;
        ((CompactNavigator)node).Document.NoDocumentUri = constructed;
        var root = node.Clone();
        var element = node.Clone();
        Assert.True(element.MoveToFirstChild());
        var attribute = element.Clone();
        Assert.True(attribute.MoveToAttribute("q", ""));
        var table = new ConstantTable { SchemaContext = schemas };
        table.Add(new XPathItem[] { root, element, attribute, element.Clone() });
        table.Add(schemas);
        ConstantTable restored = Save(table);
        var values = Assert.IsAssignableFrom<IList<XPathItem>>(restored.Get(0));
        var r = (XPathNavigator)values[0];
        var e = (XPathNavigator)values[1];
        var a = (XPathNavigator)values[2];
        Assert.True(e.IsSamePosition((XPathNavigator)values[3]));
        var parent = a.Clone();
        Assert.True(parent.MoveToParent());
        Assert.True(parent.IsSamePosition(e));
        parent = e.Clone();
        Assert.True(parent.MoveToParent());
        Assert.True(parent.IsSamePosition(r));
        Assert.Equal("file:///typed/input.xml", a.BaseURI);
        Assert.Equal("urn:value", a.LookupNamespace("v"));
        Assert.Equal(XmlTypeCode.QName, a.SchemaInfo!.SchemaType!.TypeCode);
        var savedSchemas = Assert.IsType<XmlSchemaSet>(restored.Get(1));
        Assert.Same(((XmlSchemaElement)savedSchemas.GlobalElements[new XmlQualifiedName("doc", "urn:typed")]!).ElementSchemaType, e.SchemaInfo!.SchemaType);
        var n = e.Clone();
        Assert.True(n.MoveToFirstChild());
        Assert.Same(savedSchemas.GlobalTypes[new XmlQualifiedName("small", "urn:typed")], n.SchemaInfo!.SchemaType);
        Assert.Equal("2", n.Value);
        Assert.True(n.MoveToNext());
        Assert.True(n.SchemaInfo!.IsNil);
        Assert.True(r.MoveToId("r"));
        Assert.True(r.IsSamePosition(e));
        Assert.Equal(constructed, ((CompactNavigator)e).Document._constructed);
        Assert.Equal(constructed, ((CompactNavigator)e).Document.NoDocumentUri);
        var refs = e.Clone();
        Assert.True(refs.MoveToAttribute("refs", ""));
        Assert.True(((CompactNavigator)refs).IsIdrefAttribute);
    }

    [Fact]
    public void TypedNodesShareTheirTreeAcrossNestedValuesAndTableEntries()
    {
        var (schemas, node) = Input();
        var element = node.Clone();
        element.MoveToFirstChild();
        var table = new ConstantTable { SchemaContext = schemas };
        table.Add(new XPathItem[] { node });
        table.Add(new XPathItem[] { MapItem.Of(new[] { new KeyValuePair<AtomicValue, IList<XPathItem>>(AtomicValue.OfString("node"), new XPathItem[] { ArrayItem.Of(new IList<XPathItem>[] { new XPathItem[] { element } }) }) }) });
        table.Add(new XPathItem[] { element });
        var saved = Save(table);
        var r = (XPathNavigator)((IList<XPathItem>)saved.Get(0)!)[0];
        var e = (XPathNavigator)((IList<XPathItem>)saved.Get(2)!)[0];
        var parent = e.Clone();
        Assert.True(parent.MoveToParent());
        Assert.True(parent.IsSamePosition(r));
        var map = (MapItem)((IList<XPathItem>)saved.Get(1)!)[0];
        var array = (ArrayItem)System.Linq.Enumerable.Single(map.Entries).Value[0];
        Assert.True(e.IsSamePosition((XPathNavigator)array.Members[0][0]));
    }

    [Fact]
    public void ParentlessTypedAttributesNeedNoImportedSchema()
    {
        var builder = new CompactDocument.Builder(new NameTable(), "file:///attribute.xml", fragment: true, small: true);
        builder.Attribute("", "id", "", "identity");
        builder.AnnotateAttribute(XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.Id), "identity");
        var node = builder.Finish().CreateNavigator();
        var table = new ConstantTable();
        table.Add(new XPathItem[] { node, node.Clone() });
        var values = (IList<XPathItem>)Save(table).Get(0)!;
        var first = (XPathNavigator)values[0];
        Assert.Equal(XPathNodeType.Attribute, first.NodeType);
        Assert.Equal(XmlTypeCode.Id, first.SchemaInfo!.SchemaType!.TypeCode);
        Assert.True(((CompactNavigator)first).IsIdAttribute);
        Assert.False(first.MoveToParent());
        Assert.True(first.IsSamePosition((XPathNavigator)values[1]));
    }

    [Fact]
    public void NilledSimpleTypedValuesAreEmptyBeforeSerialization()
    {
        var (_, node) = Input();
        Assert.True(node.MoveToFirstChild());
        Assert.True(node.MoveToFirstChild());
        Assert.True(node.MoveToNext());
        Assert.True(node.SchemaInfo!.IsNil);
        Assert.Empty(SchemaTypes.TypedValue(node, node.SchemaInfo.SchemaType!));
        Assert.Empty(Sequences.Atomize(new XPathItem[] { node }));
        Assert.Null(Sequences.AtomizeOptional(new XPathItem[] { node }, "optional value"));
    }

    [Fact]
    public void OptionalAtomizationChecksCardinalityAfterRemovingNilledValues()
    {
        var (_, node) = Input();
        Assert.True(node.MoveToFirstChild());
        Assert.True(node.MoveToFirstChild());
        XPathNavigator number = node.Clone();
        Assert.True(node.MoveToNext());
        Assert.Equal("2", Sequences.AtomizeOptional(new XPathItem[] { node, number }, "a value comparison")!.Value);
        Assert.Null(Sequences.AtomizeOptional(new XPathItem[] { node, node.Clone() }, "a value comparison"));
        Assert.Throws<XdmException>(() => Sequences.AtomizeOptional(new XPathItem[] { number, number.Clone() }, "a value comparison"));
    }

    [Fact]
    public void MixedTypedTreesKeepIndependentIdFactsNamespacesAndRawText()
    {
        var builder = new CompactDocument.Builder(new NameTable(), "file:///mixed.xml", small: true);
        builder.StartElement("", "doc", "", "file:///mixed.xml");
        builder.AnnotateElement(SchemaTypes.AnyType, false);
        builder.NamespaceDeclaration("v", "urn:value");
        builder.PreserveIdLookup(new[] { "provider-only" });
        builder.Attribute("", "id", "", "r");
        builder.MarkAttributeId("r");
        builder.Attribute("", "ref", "", "r");
        builder.MarkAttributeIdref();
        builder.Text("<raw/>", XPathNodeType.Text, raw: true);
        builder.EndElement(false);
        var root = builder.Finish().CreateNavigator();
        var ns = root.Clone();
        Assert.True(ns.MoveToFirstChild());
        Assert.True(ns.MoveToNamespace("v"));
        var table = new ConstantTable();
        table.Add(new XPathItem[] { root, ns });
        var values = (IList<XPathItem>)Save(table).Get(0)!;
        var document = (XPathNavigator)values[0];
        Assert.True(document.MoveToId("provider-only"));
        var e = document.Clone();
        Assert.True(document.MoveToId("r"));
        Assert.True(document.IsSamePosition(e));
        var attribute = e.Clone();
        Assert.True(attribute.MoveToAttribute("ref", ""));
        Assert.True(((CompactNavigator)attribute).IsIdrefAttribute);
        Assert.True(attribute.MoveToParent());
        Assert.True(attribute.MoveToAttribute("id", ""));
        Assert.True(((CompactNavigator)attribute).IsIdAttribute);
        Assert.Null(attribute.SchemaInfo);
        var text = e.Clone();
        Assert.True(text.MoveToFirstChild());
        Assert.Equal("<raw/>", text.Value);
        Assert.Equal((0, 6), Assert.Single(((CompactNavigator)text).RawText!));
        Assert.Equal("urn:value", ((XPathNavigator)values[1]).Value);
        var owner = ((XPathNavigator)values[1]).Clone();
        Assert.True(owner.MoveToParent());
        Assert.True(owner.IsSamePosition(e));
    }

    [Fact]
    public void ForeignValidatedTreesRetainTheOwningSchemaTypes()
    {
        var (schemas, source) = Input();
        var document = new XmlDocument();
        using (var reader = XmlReader.Create(new StringReader(source.OuterXml), new() { ValidationType = ValidationType.Schema, Schemas = schemas })) document.Load(reader);
        var original = document.CreateNavigator()!;
        var table = new ConstantTable { SchemaContext = schemas };
        table.Add(new XPathItem[] { original });
        table.Add(schemas);
        var saved = Save(table);
        var restored = (XPathNavigator)((IList<XPathItem>)saved.Get(0)!)[0];
        Assert.True(restored.MoveToFirstChild());
        Assert.Same(((XmlSchemaElement)((XmlSchemaSet)saved.Get(1)!).GlobalElements[new XmlQualifiedName("doc", "urn:typed")]!).ElementSchemaType, restored.SchemaInfo!.SchemaType);
        Assert.True(restored.MoveToFirstChild());
        Assert.True(restored.MoveToNext());
        Assert.True(restored.SchemaInfo!.IsNil);
        Assert.Empty(Sequences.Atomize(new XPathItem[] { restored }));
    }

    [Fact]
    public void InvalidForeignAnnotationsAreRejectedRatherThanRelabeledValid()
    {
        var (schemas, source) = Input();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas };
        settings.ValidationEventHandler += (_, _) => { };
        using var reader = XmlReader.Create(new StringReader(source.OuterXml.Replace(">2<", ">4<", StringComparison.Ordinal)), settings);
        var document = new XmlDocument();
        document.Load(reader);
        var table = new ConstantTable { SchemaContext = schemas };
        table.Add(new XPathItem[] { document.CreateNavigator()! });
        Assert.Contains("invalid", Assert.Throws<NotSupportedException>(() => Save(table)).Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MalformedTypedNodeRecordsRaiseInvalidData(int corruption)
    {
        var (schemas, node) = Input(); Assert.True(node.MoveToFirstChild()); var table = new ConstantTable { SchemaContext = schemas }; table.Add(new XPathItem[] { node });
        using var buffer = new MemoryStream(); using (var writer = new XmlQueryDataWriter(buffer)) ConstantSerializer.Write(writer, table);
        byte[] bytes = buffer.ToArray();
        if (corruption == 0) BitConverter.GetBytes(5).CopyTo(bytes, 0);
        // The final path selects child zero: its trailing fields are kind, index and an empty prefix.
        else if (corruption == 1) BitConverter.GetBytes(-1).CopyTo(bytes, bytes.Length - 5);
        else bytes[bytes.Length - 6] = 3;
        using var input = new MemoryStream(bytes); using var reader = new XmlQueryDataReader(input);
        Assert.Throws<InvalidDataException>(() => ConstantSerializer.Read(reader));
    }
#if NET9_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedStaticParametersKeepTheirActualSchemaSemantics(bool debug)
    {
        var (schemas, node) = Input();
        const string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:t='urn:typed'><xsl:import-schema namespace='urn:typed'/><xsl:param name='p' static='yes' as='document-node()'/><xsl:output method='text'/><xsl:template match='/'><xsl:value-of select=\"($p/t:doc/t:n instance of element(t:n,t:small), data($p/t:doc/@q) eq QName('urn:value','v:name'), nilled($p/t:doc/t:nil), count(data($p/t:doc/t:nil)), exists(id('r',$p)), ($p/t:doc/t:nil,$p/t:doc/t:n) eq 2)\"/></xsl:template></xsl:stylesheet>";
        var settings = new XsltSettings { SchemaAware = true, Schemas = schemas };
        var compiler = new XslCompiledTransform(debug) { StaticParameters = new() { [new XmlQualifiedName("p")] = new XPathItem[] { node } } };
        using var image = new MemoryStream();
        using (var source = XmlReader.Create(new StringReader(sheet))) compiler.CompileToAssembly(source, settings, null, "TypedStatic" + Guid.NewGuid().ToString("N"), "Saved.Typed", image);
        var context = new System.Runtime.Loader.AssemblyLoadContext("typed-static", isCollectible: true);
        try
        {
            image.Position = 0;
            var assembly = context.LoadFromStream(image);
            var transform = new XslCompiledTransform();
            transform.Load(assembly.GetType("Saved.Typed", true)!, settings);
            using var input = XmlReader.Create(new StringReader("<input/>"));
            using var output = new StringWriter();
            transform.Transform(input, null, output);
            Assert.Equal("true true true 0 true true", output.ToString());
        }
        finally
        {
            context.Unload();
        }
    }
#endif

}
