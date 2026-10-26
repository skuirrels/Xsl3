// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Schema;
using Xsl3.XPath31;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class SchemaConstantIdentityTests
{
    private static XmlSchemaSet Schemas()
    {
        var schemas = new XmlSchemaSet();
        using XmlReader reader = XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:simpleType name='Code'><xs:restriction base='xs:double'><xs:minInclusive value='-10'/><xs:maxInclusive value='10'/></xs:restriction></xs:simpleType><xs:element name='row' type='Code'/></xs:schema>"));
        schemas.Add(null, reader); schemas.Compile(); return schemas;
    }

    [Fact]
    public void SharedModulesKeepTypeIdentityAcrossDistinctSchemaSets()
    {
        XmlSchemaSet first = Schemas();
        var second = new XmlSchemaSet();
        second.Add(first.Schemas().Cast<XmlSchema>().Single()); second.Compile();
        Assert.Same(first.GlobalTypes[new XmlQualifiedName("Code")], second.GlobalTypes[new XmlQualifiedName("Code")]);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) SchemaSetSerializer.WriteBundle(writer, [first, second]);
        stream.Position = 0; using var reader = new BinaryReader(stream);
        XmlSchemaSet[] restored = SchemaSetSerializer.ReadBundle(reader);
        Assert.Same(restored[0].GlobalTypes[new XmlQualifiedName("Code")], restored[1].GlobalTypes[new XmlQualifiedName("Code")]);
        Assert.Same(first.GlobalTypes[new XmlQualifiedName("Code")], second.GlobalTypes[new XmlQualifiedName("Code")]);
    }

    [Fact]
    public void AtomicValuesCastsAndNestedTypeTestsShareOneRestoredType()
    {
        XmlSchemaSet schemas = Schemas();
        AtomicType atomic = SchemaTypes.AtomicTypeOf((XmlSchemaType)schemas.GlobalTypes[new XmlQualifiedName("Code")]!)!;
        var constants = new ConstantTable { SchemaContext = schemas };
        constants.Add(new CastTarget(atomic, null));
        constants.Add(new SequenceTypeTest(new MapTest(atomic, new SequenceTypeTest(new ArrayTest(new SequenceTypeTest(new AtomicTypeTest(atomic), Cardinality.ExactlyOne)), Cardinality.ExactlyOne)), Cardinality.ExactlyOne));
        constants.Add(AtomicValue.OfDouble(-0.0).WithType(atomic));
        constants.Add(schemas); constants.Add(schemas);
        using var stream = new MemoryStream();
        using (var writer = new Xsl3.Runtime.XmlQueryDataWriter(stream))
        {
            ConstantSerializer.Write(writer, constants); writer.Flush();
            byte[] bytes = stream.ToArray();
            using var saved = new MemoryStream(bytes); using var reader = new Xsl3.Runtime.XmlQueryDataReader(saved);
            ConstantTable restored = ConstantSerializer.Read(reader);
            var cast = Assert.IsType<CastTarget>(restored.Get(0));
            var map = Assert.IsType<MapTest>(Assert.IsType<SequenceTypeTest>(restored.Get(1)).Item);
            var value = Assert.IsType<DoubleValue>(restored.Get(2));
            var set = Assert.IsType<XmlSchemaSet>(restored.Get(3));
            Assert.Same(set, restored.Get(4));
            Assert.Same(cast.Type, map.KeyType); Assert.Same(cast.Type, value.Type);
            Assert.Same(cast.Type, SchemaTypes.AtomicTypeOf((XmlSchemaType)set.GlobalTypes[new XmlQualifiedName("Code")]!));
            Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(value.Double));
            Assert.Throws<XdmException>(() => cast.Apply(AtomicValue.OfString("11")));
        }
    }
}
