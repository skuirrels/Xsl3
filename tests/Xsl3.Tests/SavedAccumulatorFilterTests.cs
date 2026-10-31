// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.Tree;
using Xsl3.XPath31;
using Xunit;

namespace Xsl3.Tests;

public class SavedAccumulatorFilterTests
{
    private static Dictionary<string, AccumulatorInfo> Rules(bool filtered) => new() {
        ["Q{}n"] = new(0, new[] { false, true }, null, Streamable: true,
            Filters: filtered ? new AccumulatorNodeFilter?[] { new("røw", "urn:counter"), null } : null),
    };

    private static byte[] Save(ConstantTable table)
    {
        using var stream = new MemoryStream();
        using var writer = new XmlQueryDataWriter(stream);
        ConstantSerializer.Write(writer, table);
        writer.Flush();
        return stream.ToArray();
    }

    private static ConstantTable Read(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new XmlQueryDataReader(stream);
        return ConstantSerializer.Read(reader);
    }

    private static ConstantTable Table(bool filtered)
    {
        var table = new ConstantTable();
        table.Add(Rules(filtered));
        return table;
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void FiltersPreserveEachLegacyPayloadAndItsSchemaIdentity(int schemaMode, bool filtered)
    {
        var table = Table(filtered);
        if (schemaMode != 0)
        {
            var schemas = new XmlSchemaSet();
            using (var schema = XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='row' type='xs:integer'/></xs:schema>")))
                schemas.Add(null, schema);
            schemas.Compile();
            table.SchemaContext = schemas;
            table.Add(new XsltNameContext(new Dictionary<string, string>(), new HashSet<(string, string, int)>()) {
                Schemas = schemas, SchemaAware = true,
            });
            if (schemaMode == 2)
            {
                using var input = XmlReader.Create(new StringReader("<row>7</row>"), new XmlReaderSettings {
                    ValidationType = ValidationType.Schema, Schemas = schemas,
                });
                table.Add(new XPathItem[] { CompactDocument.Load(input, XmlSpace.Preserve, typed: true).CreateNavigator() });
            }
        }
        byte[] data = Save(table);
        using (var stream = new MemoryStream(data))
        using (var reader = new BinaryReader(stream))
        {
            int payload = schemaMode == 0 ? 3 : schemaMode == 1 ? 5 : 6;
            Assert.Equal(filtered ? 7 : payload, reader.ReadInt32());
            if (filtered) Assert.Equal(payload, reader.ReadInt32());
        }
        ConstantTable restored = Read(data);
        var rules = Assert.IsType<Dictionary<string, AccumulatorInfo>>(restored.Get(0));
        Assert.Equal(new[] { false, true }, rules["Q{}n"].EndPhases);
        Assert.True(rules["Q{}n"].Streamable);
        if (filtered) Assert.Equal(Rules(true)["Q{}n"].Filters, rules["Q{}n"].Filters);
        else Assert.Null(rules["Q{}n"].Filters);
        if (schemaMode == 0) Assert.Null(restored.SchemaContext);
        else Assert.Same(restored.SchemaContext, Assert.IsType<XsltNameContext>(restored.Get(1)).Schemas);
        if (schemaMode == 2)
        {
            var node = Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(Assert.IsAssignableFrom<IList<XPathItem>>(restored.Get(2))));
            Assert.True(node.MoveToFirstChild());
            Assert.Equal("integer", node.SchemaInfo!.SchemaType!.QualifiedName.Name);
            Assert.Equal("7", node.Value);
        }
    }

    [Fact]
    public void AllUnknownRulesKeepTheLegacyFormatAndGeneralMatcher()
    {
        var table = new ConstantTable();
        table.Add(new Dictionary<string, AccumulatorInfo> {
            ["Q{}n"] = new(0, new[] { false, true }, null, Filters: new AccumulatorNodeFilter?[] { null, null }),
        });
        byte[] data = Save(table);
        Assert.Equal(3, BitConverter.ToInt32(data, 0));
        Assert.Null(Assert.IsType<Dictionary<string, AccumulatorInfo>>(Read(data).Get(0))["Q{}n"].Filters);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void MalformedFilterCountsFailBeforeAllocation(int length)
    {
        byte[] data = Save(Table(true));
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        Assert.Equal(7, reader.ReadInt32());
        Assert.Equal(3, reader.ReadInt32());
        Assert.Equal(1, reader.ReadInt32());
        reader.ReadByte(); // filtered table kind
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal("Q{}n", reader.ReadString());
        Assert.Equal(0, reader.ReadInt32());
        int phases = reader.ReadInt32();
        for (int i = 0; i < phases; i++) reader.ReadBoolean();
        Assert.False(reader.ReadBoolean()); // no declared result type
        reader.ReadBoolean(); // principal selection
        reader.ReadBoolean(); // streamability
        Assert.True(reader.ReadBoolean()); // per-rule filter array present
        BitConverter.GetBytes(length).CopyTo(data, checked((int)stream.Position));
        Assert.Throws<InvalidDataException>(() => Read(data));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(99)]
    public void UnknownWrappedPayloadsAreRejected(int payload)
    {
        byte[] data = Save(Table(true));
        BitConverter.GetBytes(payload).CopyTo(data, sizeof(int));
        Assert.Throws<InvalidDataException>(() => Read(data));
    }

    [Fact]
    public void AFilteredRecordCannotMasqueradeAsLegacyData()
    {
        byte[] data = Save(Table(true));
        var legacy = new byte[data.Length - sizeof(int)];
        BitConverter.GetBytes(3).CopyTo(legacy, 0);
        Array.Copy(data, 2 * sizeof(int), legacy, sizeof(int), data.Length - 2 * sizeof(int));
        Assert.Throws<NotSupportedException>(() => Read(legacy));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void SavingRequiresOneFilterSlotPerRule(int length)
    {
        var table = new ConstantTable();
        table.Add(new Dictionary<string, AccumulatorInfo> {
            ["Q{}n"] = new(0, new[] { false, true }, null, Filters: new AccumulatorNodeFilter?[length]),
        });
        Assert.Throws<InvalidDataException>(() => Save(table));
    }
}
