// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;

namespace Xsl3.Streaming;

/// <summary>Intrinsic source headers and finite schema metadata, without descendant retention.</summary>
internal sealed class StreamedHeaderCodec(XmlNameTable _names)
{
    private readonly List<IXmlSchemaInfo?> _schemas = new() { null };
    private readonly Dictionary<SchemaKey, int> _schemaIds = new();
    internal IXmlSchemaInfo? Info(int id) => _schemas[id];
    internal void Reset() { _schemas.Clear(); _schemas.Add(null); _schemaIds.Clear(); }

    private readonly record struct SchemaKey(XmlSchemaType? Type, XmlSchemaSimpleType? Member,
        XmlSchemaElement? Element, XmlSchemaAttribute? Attribute, XmlSchemaValidity Validity, bool Nil, bool Default);

    internal int Schema(IXmlSchemaInfo? info)
    {
        if (info == null) return 0;
        var key = new SchemaKey(info.SchemaType, info.MemberType, info.SchemaElement, info.SchemaAttribute,
            info.Validity, info.IsNil, info.IsDefault);
        if (_schemaIds.TryGetValue(key, out int id)) return id;
        id = _schemas.Count;
        _schemas.Add(new XmlSchemaInfo
        {
            SchemaType = key.Type, MemberType = key.Member, SchemaElement = key.Element,
            SchemaAttribute = key.Attribute, Validity = key.Validity, IsNil = key.Nil, IsDefault = key.Default,
        });
        _schemaIds.Add(key, id);
        return id;
    }

    internal void Write(BinaryWriter _writer, StreamedDocument.Node node)
    {
        _writer.Write(node.Number); _writer.Write((int)node.Kind);
        _writer.Write(node.LocalName); _writer.Write(node.Namespace); _writer.Write(node.Prefix);
        _writer.Write(node.Text); _writer.Write(node.BaseUri); _writer.Write(node.Language);
        _writer.Write(node.EmptyElement); _writer.Write(node.FirstChild); _writer.Write(Schema(node.Schema));
        _writer.Write(node.Attributes.Length);
        foreach (var a in node.Attributes)
        {
            _writer.Write(a.LocalName); _writer.Write(a.Namespace); _writer.Write(a.Prefix); _writer.Write(a.Value); _writer.Write(Schema(a.Schema));
        }
        _writer.Write(node.Namespaces.Length);
        foreach (var n in node.Namespaces) { _writer.Write(n.Prefix); _writer.Write(n.Uri); _writer.Write(n.Owner); }
    }

    internal StreamedDocument.Node Read(BinaryReader _reader, StreamedDocument.Node? parent)
    {
        var node = new StreamedDocument.Node(_reader.ReadInt64(), (XPathNodeType)_reader.ReadInt32(), parent)
        {
            LocalName = ReadName(_reader), Namespace = ReadName(_reader), Prefix = ReadName(_reader),
            Text = _reader.ReadString(), BaseUri = _reader.ReadString(), Language = _reader.ReadString(),
            EmptyElement = _reader.ReadBoolean(), FirstChild = _reader.ReadInt64(), Schema = _schemas[_reader.ReadInt32()],
            Ended = true, ChildrenStarted = true, Consumed = true,
        };
        node.Attributes = new StreamedDocument.Attribute[_reader.ReadInt32()];
        for (int i = 0; i < node.Attributes.Length; i++)
            node.Attributes[i] = new(ReadName(_reader), ReadName(_reader), ReadName(_reader), _reader.ReadString(), _schemas[_reader.ReadInt32()]);
        node.Namespaces = new StreamedDocument.Namespace[_reader.ReadInt32()];
        for (int i = 0; i < node.Namespaces.Length; i++)
            node.Namespaces[i] = new(ReadName(_reader), ReadName(_reader), _reader.ReadInt64());
        return node;
    }

    // XPathNavigator promises atomized names. Generated QName tests compare names
    // by reference when the navigator advertises the query's NameTable.
    private string ReadName(BinaryReader _reader) => _names.Add(_reader.ReadString());
}
