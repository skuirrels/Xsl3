// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.Schema;
using Xsl3.Xdm;

namespace Xsl3.XPath31;

/// <summary>A saved table's schema sets and type identities; never process-wide serialization state.</summary>
internal sealed class SchemaConstantContext
{
    private readonly List<XmlSchemaSet> _sets = new();
    private readonly Dictionary<XmlSchemaSet, int> _setIds = new(ReferenceEqualityComparer.Instance);
    private readonly List<List<XmlSchemaType>> _types = new();
    private readonly Dictionary<XmlSchemaType, (int Set, int Type)> _typeIds = new(ReferenceEqualityComparer.Instance);
    internal bool Required { get; private set; }
    internal bool NodeAnnotations { get; private set; }
    private readonly List<XPathNavigator> _nodeRoots = new();
    internal XmlSchemaSet? Primary { get; private set; }

    internal static SchemaConstantContext Collect(ConstantTable table)
    {
        var context = new SchemaConstantContext { Primary = table.SchemaContext };
        context.Add(table.SchemaContext);
        for (int i = 0; i < table.Count; i++) context.Visit(table.Get(i));
        context.IndexTypes();
        return context;
    }

    private void Add(XmlSchemaSet? schemas)
    {
        if (schemas != null && _setIds.TryAdd(schemas, _sets.Count)) _sets.Add(schemas);
    }

    private void Atomic(AtomicType type)
    {
        if (!type.IsUserDefined) return;
        if (type.UserSchemaType!.QualifiedName.IsEmpty)
            throw new NotSupportedException("Saving an anonymous schema atomic type is not supported yet");
        Required = true;
    }

    private void Visit(object? value)
    {
        switch (value)
        {
            case AtomicValue atomic: Atomic(atomic.Type); break;
            case XPathNavigator node: VisitNode(node); break;
            case CastTarget cast: Atomic(cast.Type); Required |= cast.Schema != null; break;
            case XmlSchemaSet schemas: Add(schemas); break;
            case XsltNameContext names: Add(names.Schemas); Required |= names.Schemas != null || names.SchemaAware || names.Static; break;
            case EvaluateContext evaluate: Add(evaluate.Schemas); Required |= evaluate.Schemas != null; break;
            case SequenceTypeTest type: Visit(type.Item); break;
            case AtomicTypeTest atomic: Atomic(atomic.Type); break;
            case NodeKindTest node:
                Required |= node.Names != null || node.SchemaType != null && !BuiltIn(node.SchemaType);
                Visit(node.DocumentElement); break;
            case FunctionSignature function:
                foreach (SequenceTypeTest parameter in function.Parameters) Visit(parameter);
                Visit(function.Return); break;
            case FunctionItemTest function:
                foreach (SequenceTypeTest parameter in function.Parameters ?? Array.Empty<SequenceTypeTest>()) Visit(parameter);
                Visit(function.ReturnType); break;
            case MapTest map:
                if (map.KeyType != null) Atomic(map.KeyType);
                Visit(map.ValueType); break;
            case ArrayTest array: Visit(array.MemberType); break;
            case Dictionary<string, AccumulatorInfo> accumulators:
                foreach (AccumulatorInfo info in accumulators.Values) Visit(info.Type);
                break;
            case IList<XPathItem> items: foreach (XPathItem item in items) Visit(item); break;
            case MapItem map:
                foreach (var (key, items) in map.Entries) { Visit(key); Visit(items); }
                break;
            case ArrayItem array: foreach (IList<XPathItem> items in array.Members) Visit(items); break;
        }
    }

    private void VisitNode(XPathNavigator node)
    {
        XPathNavigator root = node.Clone(); root.MoveToRoot();
        if (_nodeRoots.Any(previous => previous.IsSamePosition(root))) return;
        _nodeRoots.Add(root);
        void Annotation(XPathNavigator value)
        {
            if (value.SchemaInfo?.SchemaType != null)
            {
                if (SchemaTypes.TypeOf(value) == null)
                    throw new NotSupportedException("Saving incomplete or invalid schema node annotations is not supported");
                Required = NodeAnnotations = true;
            }
        }
        Annotation(root);
        XPathNodeIterator elements = root.SelectDescendants(XPathNodeType.Element, matchSelf: true);
        while (elements.MoveNext())
        {
            XPathNavigator element = elements.Current!; Annotation(element);
            XPathNavigator attribute = element.Clone();
            if (attribute.MoveToFirstAttribute()) do { Annotation(attribute); } while (attribute.MoveToNextAttribute());
        }
    }

    internal void Write(BinaryWriter writer)
    {
        SchemaSetSerializer.WriteBundle(writer, _sets);
        writer.Write(Primary == null ? -1 : _setIds[Primary]);
    }

    internal static SchemaConstantContext Read(BinaryReader reader)
    {
        var context = new SchemaConstantContext { Required = true };
        foreach (XmlSchemaSet set in SchemaSetSerializer.ReadBundle(reader)) context.Add(set);
        int primary = reader.ReadInt32();
        if (primary < -1 || primary >= context._sets.Count) throw new InvalidDataException("Invalid primary schema context");
        if (primary >= 0) context.Primary = context._sets[primary];
        context.IndexTypes();
        return context;
    }

    internal void WriteSet(BinaryWriter writer, XmlSchemaSet? schemas) => writer.Write(schemas == null ? -1 : _setIds[schemas]);
    internal XmlSchemaSet? ReadSet(BinaryReader reader)
    {
        int id = reader.ReadInt32();
        if (id < -1 || id >= _sets.Count) throw new InvalidDataException("Invalid schema-set reference");
        return id < 0 ? null : _sets[id];
    }

    private static bool BuiltIn(XmlSchemaType type) => SchemaTypes.IsBuiltIn(type) || ReferenceEquals(type, SchemaTypes.UntypedAtomic);

    internal void WriteType(BinaryWriter writer, XmlSchemaType? type)
    {
        if (type == null) { writer.Write((byte)0); return; }
        if (ReferenceEquals(type, SchemaTypes.UntypedAtomic)) { writer.Write((byte)3); return; }
        if (SchemaTypes.IsBuiltIn(type))
        {
            writer.Write((byte)1); writer.Write(type.QualifiedName.Namespace); writer.Write(type.QualifiedName.Name); return;
        }
        if (!_typeIds.TryGetValue(type, out var id))
            throw new NotSupportedException("A saved schema type must belong to the compilation's schema contexts");
        writer.Write((byte)2); writer.Write(id.Set); writer.Write(id.Type);
    }

    internal XmlSchemaType? ReadType(BinaryReader reader)
    {
        switch (reader.ReadByte())
        {
            case 0: return null;
            case 3: return SchemaTypes.UntypedAtomic;
            case 1:
                string ns = reader.ReadString(), local = reader.ReadString();
                var name = new XmlQualifiedName(local, ns);
                return (XmlSchemaType?)XmlSchemaType.GetBuiltInSimpleType(name) ?? XmlSchemaType.GetBuiltInComplexType(name)
                    ?? throw new InvalidDataException("Unknown saved built-in schema type");
            case 2:
                int set = reader.ReadInt32(), type = reader.ReadInt32();
                if (set < 0 || set >= _types.Count || type < 0 || type >= _types[set].Count)
                    throw new InvalidDataException("Invalid saved schema-type reference");
                return _types[set][type];
            default: throw new InvalidDataException("Unknown saved schema-type reference kind");
        }
    }

    private void IndexTypes()
    {
        for (int setId = 0; setId < _sets.Count; setId++)
        {
            var types = new List<XmlSchemaType>();
            var seen = new HashSet<XmlSchemaType>(ReferenceEqualityComparer.Instance);
            void Type(XmlSchemaType? type)
            {
                if (type == null || BuiltIn(type) || !seen.Add(type)) return;
                _typeIds.TryAdd(type, (setId, types.Count)); types.Add(type);
                Type(type.BaseXmlSchemaType);
                if (type is XmlSchemaComplexType complex)
                {
                    Particle(complex.ContentTypeParticle);
                    foreach (XmlSchemaAttribute attribute in Sorted(complex.AttributeUses)) Type(attribute.AttributeSchemaType);
                }
                if (type is XmlSchemaSimpleType { Content: XmlSchemaSimpleTypeList list }) Type(list.BaseItemType);
                if (type is XmlSchemaSimpleType { Content: XmlSchemaSimpleTypeUnion union })
                    foreach (XmlSchemaSimpleType member in union.BaseMemberTypes ?? Array.Empty<XmlSchemaSimpleType>()) Type(member);
            }
            void Particle(XmlSchemaParticle? particle)
            {
                switch (particle)
                {
                    case XmlSchemaElement element: Type(element.ElementSchemaType); break;
                    case XmlSchemaGroupBase group:
                        foreach (XmlSchemaParticle child in group.Items) Particle(child);
                        break;
                    case XmlSchemaGroupRef group: Particle(group.Particle); break;
                }
            }
            foreach (XmlSchemaType type in Sorted(_sets[setId].GlobalTypes)) Type(type);
            foreach (XmlSchemaElement element in Sorted(_sets[setId].GlobalElements)) Type(element.ElementSchemaType);
            foreach (XmlSchemaAttribute attribute in Sorted(_sets[setId].GlobalAttributes)) Type(attribute.AttributeSchemaType);
            _types.Add(types);
        }
    }

    private static IEnumerable<XmlSchemaObject> Sorted(XmlSchemaObjectTable table) => table.Names.Cast<XmlQualifiedName>()
        .OrderBy(name => name.Namespace, StringComparer.Ordinal).ThenBy(name => name.Name, StringComparer.Ordinal).Select(name => table[name]!);
}

internal sealed class SchemaConstantWriter(XmlQueryDataWriter writer, SchemaConstantContext context) : BinaryWriter(writer.BaseStream, Encoding.UTF8, leaveOpen: true)
{
    internal SchemaConstantContext Context { get; } = context;
    internal XmlQueryDataWriter Original { get; } = writer;
    internal List<XPathNavigator> NodeRoots { get; } = new();
}

internal sealed class SchemaConstantReader(XmlQueryDataReader reader, SchemaConstantContext context, int version) : BinaryReader(reader.BaseStream, Encoding.UTF8, leaveOpen: true)
{
    internal SchemaConstantContext Context { get; } = context;
    internal XmlQueryDataReader Original { get; } = reader;
    internal int Version { get; } = version;
    internal List<XPathNavigator> NodeRoots { get; } = new();
}
