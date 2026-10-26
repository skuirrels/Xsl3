// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.XPath31
{
    /// <summary>
    /// Saves a stylesheet's constant table with its compiled form, and reads it back (Sink B, docs/plan/p4-sink-b.md
    /// decision 3): a versioned binary format over the closed set of kinds the compiler puts in the table. A kind it
    /// doesn't know, or a value that can't be saved (an inline function item among static parameter values), raises
    /// <see cref="System.NotSupportedException"/> when saving.
    /// </summary>
    internal static class ConstantSerializer
    {
        // Older constant formats have no table-scoped schema references.
        private static void NoSchema(bool usesSchemas)
        {
            if (usesSchemas)
            {
                throw new System.NotSupportedException("Saving schema-dependent type or expression metadata is not supported yet");
            }
        }

        private const int FormatVersion = 6;

        private enum Kind : byte
        {
            Null,
            Atomic,
            QName,
            StaticInfo,
            CastTarget,
            SequenceType,
            NameContext,
            FunctionSignature,
            ResultFormat,
            ResultFormats,
            MergeSpec,
            ErrorLists,
            Strings,
            EvaluateContext,
            Accumulators,
            Sequence,
            Frame,
            EmptySchemaSet,
            SchemaSet,
            SchemaSetReference,
        }

        public static void Write(XmlQueryDataWriter w, ConstantTable table)
        {
            SchemaConstantContext context = SchemaConstantContext.Collect(table);
            int version = context.NodeAnnotations ? FormatVersion : context.Required ? 5 : 3;
            if (!context.Required)
                for (int i = 0; i < table.Count; i++)
                    if (table.Get(i) is System.Xml.Schema.XmlSchemaSet { Count: > 0 }) version = 4;
            w.Write(version);
            w.Write(table.Count);
            if (context.Required) context.Write(w);
            using var scoped = context.Required ? new SchemaConstantWriter(w, context) : null;
            BinaryWriter values = scoped == null ? w : scoped;
            for (int i = 0; i < table.Count; i++) WriteValue(values, table.Get(i));
        }

        public static ConstantTable Read(XmlQueryDataReader r)
        {
            int version = r.ReadInt32();
            if (version < 1 || version > FormatVersion)
                throw new System.NotSupportedException("The compiled stylesheet's constants were saved in a format this version of Xsl3 doesn't read");
            int count = r.ReadInt32();
            SchemaConstantContext? context = version >= 5 ? SchemaConstantContext.Read(r) : null;
            using var scoped = context == null ? null : new SchemaConstantReader(r, context, version);
            BinaryReader values = scoped == null ? r : scoped;
            var table = new ConstantTable { SchemaContext = context?.Primary };
            for (int i = 0; i < count; i++) table.Add(ReadValue(values, version)!);
            return table;
        }

        // ------------------------------------------------------------ values

        private static void WriteValue(BinaryWriter w, object? value)
        {
            switch (value)
            {
                case null:
                    w.Write((byte)Kind.Null);
                    break;
                case QNameValue q:
                    w.Write((byte)Kind.QName);
                    WriteAtomicType(w, q.Type);
                    WriteName(w, q.QName);
                    break;
                case AtomicValue a:
                    w.Write((byte)Kind.Atomic);
                    WriteAtomic(w, a);
                    break;
                case StaticInfo s:
                    w.Write((byte)Kind.StaticInfo);
                    WriteNullable(w, s.BaseUri);
                    w.Write(s.DefaultCollation);
                    WriteDecimalFormats(w, s.DecimalFormats);
                    WriteResolver(w, s.Namespaces);
                    break;
                case CastTarget c:
                    if (w is not SchemaConstantWriter) NoSchema(c.Kind == CastKind.Schema || c.Type.IsUserDefined);
                    w.Write((byte)Kind.CastTarget);
                    WriteAtomicType(w, c.Type);
                    WriteResolver(w, c.Namespaces);
                    w.Write((byte)c.Kind);
                    if (w is SchemaConstantWriter castWriter) castWriter.Context.WriteType(w, c.Schema);
                    break;
                case SequenceTypeTest t:
                    w.Write((byte)Kind.SequenceType);
                    WriteSequenceType(w, t);
                    break;
                case XsltNameContext n:
                    if (w is not SchemaConstantWriter) NoSchema(n.Schemas != null);
                    w.Write((byte)Kind.NameContext);
                    WriteStringMap(w, n.Namespaces);
                    w.Write(n.StylesheetFunctions.Count);
                    foreach (var (ns, local, arity) in n.StylesheetFunctions)
                    {
                        w.Write(ns);
                        w.Write(local);
                        w.Write(arity);
                    }
                    w.Write(n.EvaluateEnabled);
                    if (w is SchemaConstantWriter namesWriter)
                    {
                        w.Write(n.SchemaAware); w.Write(n.Static); namesWriter.Context.WriteSet(w, n.Schemas);
                    }
                    break;
                case FunctionSignature f:
                    w.Write((byte)Kind.FunctionSignature);
                    WriteOptionalName(w, f.Name);
                    w.Write(f.Parameters.Length);
                    foreach (SequenceTypeTest p in f.Parameters)
                    {
                        WriteSequenceType(w, p);
                    }
                    WriteSequenceType(w, f.Return);
                    break;
                case ResultFormat format:
                    w.Write((byte)Kind.ResultFormat);
                    WriteResultFormat(w, format);
                    break;
                case Dictionary<(string, string), ResultFormat> formats:
                    w.Write((byte)Kind.ResultFormats);
                    w.Write(formats.Count);
                    foreach (var ((uri, local), format) in formats)
                    {
                        w.Write(uri);
                        w.Write(local);
                        WriteResultFormat(w, format);
                    }
                    break;
                case MergeSpec spec:
                    w.Write((byte)Kind.MergeSpec);
                    w.Write(spec.Descending.Count);
                    spec.Descending.ForEach(w.Write);
                    w.Write(spec.Collations.Count);
                    spec.Collations.ForEach(w.Write);
                    w.Write(spec.SortBeforeMerge.Count);
                    spec.SortBeforeMerge.ForEach(w.Write);
                    break;
                case List<List<string>> errors:
                    w.Write((byte)Kind.ErrorLists);
                    w.Write(errors.Count);
                    foreach (List<string> list in errors)
                    {
                        w.Write(list.Count);
                        list.ForEach(w.Write);
                    }
                    break;
                case string?[] strings:
                    w.Write((byte)Kind.Strings);
                    w.Write(strings.Length);
                    foreach (string? s in strings)
                    {
                        WriteNullable(w, s);
                    }
                    break;
                case EvaluateContext e:
                    w.Write((byte)Kind.EvaluateContext);
                    WriteStringMap(w, e.Namespaces);
                    w.Write(e.DefaultElementNamespace);
                    WriteNullable(w, e.BaseUri);
                    w.Write(e.DefaultCollation);
                    WriteDecimalFormat(w, e.DefaultDecimalFormat);
                    w.Write(e.DecimalFormats.Count);
                    foreach (var (name, format) in e.DecimalFormats)
                    {
                        WriteName(w, name);
                        WriteDecimalFormat(w, format);
                    }
                    if (w is SchemaConstantWriter evaluateWriter) evaluateWriter.Context.WriteSet(w, e.Schemas);
                    else NoSchema(e.Schemas != null);
                    break;
                case Dictionary<string, AccumulatorInfo> accumulators:
                    w.Write((byte)Kind.Accumulators);
                    w.Write(accumulators.Count);
                    foreach (var (name, info) in accumulators)
                    {
                        w.Write(name);
                        w.Write(info.Offset);
                        w.Write(info.EndPhases.Length);
                        System.Array.ForEach(info.EndPhases, w.Write);
                        w.Write(info.Type != null);
                        if (info.Type != null)
                        {
                            WriteSequenceType(w, info.Type);
                        }
                        w.Write(info.OnPrincipal);
                        w.Write(info.Streamable);
                    }
                    break;
                case IList<XPathItem> items:
                    w.Write((byte)Kind.Sequence);
                    w.Write(items.Count);
                    foreach (XPathItem item in items)
                    {
                        WriteItem(w, item);
                    }
                    break;
                case XsltFrame frame:
                    w.Write((byte)Kind.Frame);
                    w.Write(frame.Kind);
                    WriteOptionalName(w, frame.Name);
                    WriteNullable(w, frame.Match);
                    WriteOptionalName(w, frame.Mode);
                    WriteNullable(w, frame.ModuleUri);
                    w.Write(frame.LineNumber);
                    w.Write(frame.ColumnNumber);
                    break;
                case System.Xml.Schema.XmlSchemaSet schemas:
                    if (w is SchemaConstantWriter schemaWriter)
                    {
                        w.Write((byte)Kind.SchemaSetReference); schemaWriter.Context.WriteSet(w, schemas);
                    }
                    else if (schemas.Count == 0) w.Write((byte)Kind.EmptySchemaSet);
                    else
                    {
                        w.Write((byte)Kind.SchemaSet);
                        Xsl3.Schema.SchemaSetSerializer.Write(w, schemas);
                    }
                    break;
                default:
                    throw new System.NotSupportedException($"A compiled stylesheet can't be saved with a constant of type {value.GetType().Name}");
            }
        }

        private static object? ReadValue(BinaryReader r, int version = FormatVersion)
        {
            var kind = (Kind)r.ReadByte();
            switch (kind)
            {
                case Kind.Null:
                    return null;
                case Kind.EmptySchemaSet:
                    return new System.Xml.Schema.XmlSchemaSet { XmlResolver = null };
                case Kind.SchemaSet when version >= 4:
                    return Xsl3.Schema.SchemaSetSerializer.Read(r);
                case Kind.SchemaSetReference when r is SchemaConstantReader schemaReader:
                    return schemaReader.Context.ReadSet(r) ?? throw new InvalidDataException("Missing saved schema set");
                case Kind.QName:
                    return new QNameValue(ReadAtomicType(r), ReadName(r));
                case Kind.Atomic:
                    return ReadAtomic(r);
                case Kind.StaticInfo:
                    return new StaticInfo(ReadNullable(r), r.ReadString(), ReadDecimalFormats(r)) { Namespaces = ReadResolver(r) };
                case Kind.CastTarget:
                {
                    AtomicType type = ReadAtomicType(r);
                    var namespaces = ReadResolver(r);
                    var castKind = (CastKind)r.ReadByte();
                    return new CastTarget(type, namespaces, castKind)
                    { Schema = r is SchemaConstantReader castReader ? (System.Xml.Schema.XmlSchemaSimpleType?)castReader.Context.ReadType(r) : null };
                }
                case Kind.SequenceType:
                    return ReadSequenceType(r);
                case Kind.NameContext:
                {
                    Dictionary<string, string> namespaces = ReadStringMap(r);
                    var functions = new HashSet<(string, string, int)>();
                    for (int i = r.ReadInt32(); i > 0; i--)
                    {
                        functions.Add((r.ReadString(), r.ReadString(), r.ReadInt32()));
                    }
                    bool evaluate = r.ReadBoolean();
                    if (r is SchemaConstantReader namesReader)
                        return new XsltNameContext(namespaces, functions) { EvaluateEnabled = evaluate, SchemaAware = r.ReadBoolean(), Static = r.ReadBoolean(), Schemas = namesReader.Context.ReadSet(r) };
                    return new XsltNameContext(namespaces, functions) { EvaluateEnabled = evaluate };
                }
                case Kind.FunctionSignature:
                {
                    QName? name = ReadOptionalName(r);
                    var parameters = new SequenceTypeTest[r.ReadInt32()];
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        parameters[i] = ReadSequenceType(r);
                    }
                    return new FunctionSignature(name, parameters, ReadSequenceType(r));
                }
                case Kind.ResultFormat:
                    return ReadResultFormat(r);
                case Kind.ResultFormats:
                {
                    var formats = new Dictionary<(string, string), ResultFormat>();
                    for (int i = r.ReadInt32(); i > 0; i--)
                    {
                        formats[(r.ReadString(), r.ReadString())] = ReadResultFormat(r);
                    }
                    return formats;
                }
                case Kind.MergeSpec:
                {
                    var spec = new MergeSpec();
                    for (int i = r.ReadInt32(); i > 0; i--)
                    {
                        spec.Descending.Add(r.ReadBoolean());
                    }
                    for (int i = r.ReadInt32(); i > 0; i--)
                    {
                        spec.Collations.Add(r.ReadString());
                    }
                    for (int i = r.ReadInt32(); i > 0; i--)
                    {
                        spec.SortBeforeMerge.Add(r.ReadBoolean());
                    }
                    return spec;
                }
                case Kind.ErrorLists:
                {
                    var errors = new List<List<string>>();
                    for (int i = r.ReadInt32(); i > 0; i--)
                    {
                        var list = new List<string>();
                        for (int j = r.ReadInt32(); j > 0; j--)
                        {
                            list.Add(r.ReadString());
                        }
                        errors.Add(list);
                    }
                    return errors;
                }
                case Kind.Strings:
                {
                    var strings = new string?[r.ReadInt32()];
                    for (int i = 0; i < strings.Length; i++)
                    {
                        strings[i] = ReadNullable(r);
                    }
                    return strings;
                }
                case Kind.EvaluateContext:
                {
                    Dictionary<string, string> namespaces = ReadStringMap(r);
                    string defaultElement = r.ReadString();
                    string? baseUri = ReadNullable(r);
                    string collation = r.ReadString();
                    XPathDecimalFormat unnamed = ReadDecimalFormat(r);
                    var named = new Dictionary<QName, XPathDecimalFormat>();
                    for (int i = r.ReadInt32(); i > 0; i--)
                    {
                        named[ReadName(r)] = ReadDecimalFormat(r);
                    }
                    return new EvaluateContext(namespaces, defaultElement, baseUri, collation, unnamed, named)
                    { Schemas = r is SchemaConstantReader evaluateReader ? evaluateReader.Context.ReadSet(r) : null };
                }
                case Kind.Accumulators:
                {
                    var accumulators = new Dictionary<string, AccumulatorInfo>();
                    for (int i = r.ReadInt32(); i > 0; i--)
                    {
                        string name = r.ReadString();
                        int offset = r.ReadInt32();
                        var phases = new bool[r.ReadInt32()];
                        for (int j = 0; j < phases.Length; j++)
                        {
                            phases[j] = r.ReadBoolean();
                        }
                        SequenceTypeTest? type = r.ReadBoolean() ? ReadSequenceType(r) : null;
                        bool principal = r.ReadBoolean();
                        accumulators[name] = new AccumulatorInfo(offset, phases, type, principal, version >= 2 && r.ReadBoolean());
                    }
                    return accumulators;
                }
                case Kind.Sequence:
                {
                    var items = new XPathItem[r.ReadInt32()];
                    for (int i = 0; i < items.Length; i++)
                    {
                        items[i] = ReadItem(r);
                    }
                    return items;
                }
                case Kind.Frame:
                    return new XsltFrame(r.ReadString(), ReadOptionalName(r), ReadNullable(r), ReadOptionalName(r), ReadNullable(r), r.ReadInt32(), r.ReadInt32());
                default:
                    throw new System.NotSupportedException($"Unknown constant kind {kind} in a compiled stylesheet");
            }
        }

        // ------------------------------------------------------------ items

        private static void WriteItem(BinaryWriter w, XPathItem item)
        {
            switch (item)
            {
                case AtomicValue:
                    w.Write((byte)0);
                    WriteValue(w, item);
                    break;
                case XPathNavigator node when w is SchemaConstantWriter schemaWriter && schemaWriter.Context.NodeAnnotations:
                    w.Write((byte)6);
                    WriteSharedNode(schemaWriter, node);
                    break;
                case XPathNavigator { NodeType: XPathNodeType.Root } document:
                    // A document is saved as its markup, and parsed again into a compact tree.
                    NoSchemaAnnotations(document);
                    w.Write((byte)1);
                    WriteNullable(w, document.BaseURI);
                    w.Write(document.InnerXml);
                    break;
                case XPathNavigator node:
                    // Any other node: its tree, from the root (a document or a parentless node), and the way down to it.
                    w.Write((byte)2);
                    WriteNode(w, node);
                    break;
                case MapItem map:
                    w.Write((byte)3);
                    w.Write(map.Count);
                    foreach (var (key, value) in map.Entries)
                    {
                        WriteAtomic(w, key);
                        WriteItems(w, value);
                    }
                    break;
                case ArrayItem array:
                    w.Write((byte)4);
                    w.Write(array.Members.Count);
                    foreach (IList<XPathItem> member in array.Members)
                    {
                        WriteItems(w, member);
                    }
                    break;
                case FunctionItem { Name: QName name } function when FunctionLibrary.Find(name.NamespaceUri, name.LocalName, function.Arity) != null &&
                    function is LibraryFunction or Runtime.CompiledFunction { Captured.Count: 0 }:
                    // A reference to a built-in function (xs:date#1): found again by its name and arity.
                    w.Write((byte)5);
                    WriteName(w, name);
                    w.Write(function.Arity);
                    break;
                default:
                    throw new System.NotSupportedException($"A compiled stylesheet can't be saved with {(item is FunctionItem f ? f.Describe() : item.ToString())} as a static value");
            }
        }

        private static void WriteItems(BinaryWriter w, IList<XPathItem> items)
        {
            w.Write(items.Count);
            foreach (XPathItem item in items)
            {
                WriteItem(w, item);
            }
        }

        private static IList<XPathItem> ReadItems(BinaryReader r)
        {
            var items = new XPathItem[r.ReadInt32()];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = ReadItem(r);
            }
            return items;
        }

        private static XPathItem ReadItem(BinaryReader r)
        {
            switch (r.ReadByte())
            {
                case 1:
                {
                    string? baseUri = ReadNullable(r);
                    using XmlReader reader = XmlReader.Create(new StringReader(r.ReadString()),
                        new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment, DtdProcessing = DtdProcessing.Ignore }, baseUri);
                    return Tree.CompactDocument.Load(reader, XmlSpace.Preserve).CreateNavigator();
                }
                case 2:
                    return ReadNode(r);
                case 6 when r is SchemaConstantReader { Version: >= 6 } schemaReader:
                    return ReadSharedNode(schemaReader);
                case 3:
                {
                    var entries = new List<KeyValuePair<AtomicValue, IList<XPathItem>>>();
                    for (int i = r.ReadInt32(); i > 0; i--)
                    {
                        entries.Add(new(ReadAtomic(r), ReadItems(r)));
                    }
                    return MapItem.Of(entries);
                }
                case 4:
                {
                    var members = new List<IList<XPathItem>>();
                    for (int i = r.ReadInt32(); i > 0; i--)
                    {
                        members.Add(ReadItems(r));
                    }
                    return ArrayItem.Of(members);
                }
                case 5:
                {
                    QName name = ReadName(r);
                    int arity = r.ReadInt32();
                    return new LibraryFunction(FunctionLibrary.Find(name.NamespaceUri, name.LocalName, arity)!, arity,
                        new FunctionContext(ImplicitTimezone.Local(), (a, b) => a.ComparePosition(b) switch
                        {
                            XmlNodeOrder.Before => -1,
                            XmlNodeOrder.After => 1,
                            _ => 0,
                        }));
                }
                case 0:
                    return (XPathItem)ReadValue(r)!;
                default:
                    throw new InvalidDataException("Unknown saved item record");
            }
        }

        // The untyped tree record cannot silently discard annotations from a typed static parameter.
        private static void NoSchemaAnnotations(XPathNavigator node)
        {
            XPathNavigator root = node.Clone();
            root.MoveToRoot();
            NoSchema(root.SchemaInfo?.SchemaType != null);
            XPathNodeIterator elements = root.SelectDescendants(XPathNodeType.Element, matchSelf: true);
            while (elements.MoveNext())
            {
                XPathNavigator element = elements.Current!;
                NoSchema(element.SchemaInfo?.SchemaType != null);
                XPathNavigator attribute = element.Clone();
                if (attribute.MoveToFirstAttribute())
                    do { NoSchema(attribute.SchemaInfo?.SchemaType != null); } while (attribute.MoveToNextAttribute());
            }
        }

        // A node's tree: element, attribute, namespace, text, comment and PI records, from the root down.
        private const byte TreeElement = 1, TreeEnd = 2, TreeAttribute = 3, TreeNamespace = 4, TreeText = 5, TreeComment = 6, TreePI = 7, TreeDone = 0;

        private static void WriteNode(BinaryWriter w, XPathNavigator node, bool annotations = false)
        {
            if (!annotations) NoSchemaAnnotations(node);
            XPathNavigator root = node.Clone();
            root.MoveToRoot();
            w.Write(root.NodeType == XPathNodeType.Root);
            WriteNullable(w, root.BaseURI);
            if (annotations)
            {
                byte flags = root is Tree.CompactNavigator native
                    ? (byte)((native.Document._constructed ? 1 : 0) | (native.Document.NoDocumentUri ? 2 : 0) | (native.Document._writerBuilt ? 4 : 0))
                    : (byte)(root is MS.Internal.Xml.Cache.XPathDocumentNavigator { OwnerDocument.NoDocumentUri: true } ? 2 : 0);
                w.Write(flags);
            }
            WriteTree(w, root, annotations);
            w.Write(TreeDone);
            WriteNodePath(w, node);
        }

        private static void WriteNodePath(BinaryWriter w, XPathNavigator node)
        {
            // The way from the root to the node: child positions, attribute positions, namespace prefixes.
            var steps = new List<(byte, int, string)>();
            XPathNavigator at = node.Clone();
            while (true)
            {
                XPathNavigator here = at.Clone();
                if (!at.MoveToParent())
                {
                    break;
                }
                switch (here.NodeType)
                {
                    case XPathNodeType.Attribute:
                        steps.Add((1, Position(at.Clone(), here), ""));
                        break;
                    case XPathNodeType.Namespace:
                        steps.Add((2, 0, here.LocalName));
                        break;
                    default:
                        int index = 0;
                        while (here.MoveToPrevious())
                        {
                            index++;
                        }
                        steps.Add((0, index, ""));
                        break;
                }
            }
            steps.Reverse();
            w.Write(steps.Count);
            foreach (var (kind, index, prefix) in steps)
            {
                w.Write(kind);
                w.Write(index);
                w.Write(prefix);
            }
        }

        private static int Position(XPathNavigator element, XPathNavigator attribute)
        {
            int index = 0;
            for (bool more = element.MoveToFirstAttribute(); more && !element.IsSamePosition(attribute); more = element.MoveToNextAttribute())
            {
                index++;
            }
            return index;
        }

        private static void WriteTree(BinaryWriter w, XPathNavigator node, bool annotations = false)
        {
            switch (node.NodeType)
            {
                case XPathNodeType.Root:
                    WriteChildren(w, node, annotations);
                    break;
                case XPathNodeType.Element:
                    w.Write(TreeElement);
                    w.Write(node.Prefix);
                    w.Write(node.LocalName);
                    w.Write(node.NamespaceURI);
                    w.Write(node.BaseURI);
                    if (annotations)
                    {
                        ((SchemaConstantWriter)w).Context.WriteType(w, node.SchemaInfo?.SchemaType);
                        w.Write(node.SchemaInfo?.IsNil == true);
                        var ids = (node as Tree.CompactNavigator)?.LookupIds;
                        w.Write(ids?.Count ?? 0);
                        if (ids != null) foreach (string id in ids) w.Write(id);
                    }
                    if (node.MoveToFirstNamespace(XPathNamespaceScope.Local))
                    {
                        do
                        {
                            WriteTree(w, node, annotations);
                        }
                        while (node.MoveToNextNamespace(XPathNamespaceScope.Local));
                        node.MoveToParent();
                    }
                    if (node.MoveToFirstAttribute())
                    {
                        do
                        {
                            WriteTree(w, node, annotations);
                        }
                        while (node.MoveToNextAttribute());
                        node.MoveToParent();
                    }
                    WriteChildren(w, node, annotations);
                    w.Write(TreeEnd);
                    break;
                case XPathNodeType.Attribute:
                    w.Write(TreeAttribute);
                    w.Write(node.Prefix);
                    w.Write(node.LocalName);
                    w.Write(node.NamespaceURI);
                    w.Write(node.Value);
                    if (annotations)
                    {
                        ((SchemaConstantWriter)w).Context.WriteType(w, node.SchemaInfo?.SchemaType);
                        w.Write(node is Tree.CompactNavigator { IsIdAttribute: true });
                        w.Write(node is Tree.CompactNavigator { IsIdrefAttribute: true });
                    }
                    break;
                case XPathNodeType.Namespace:
                    w.Write(TreeNamespace);
                    w.Write(node.LocalName);
                    w.Write(node.Value);
                    break;
                case XPathNodeType.Comment:
                    w.Write(TreeComment);
                    w.Write(node.Value);
                    break;
                case XPathNodeType.ProcessingInstruction:
                    w.Write(TreePI);
                    w.Write(node.LocalName);
                    w.Write(node.Value);
                    break;
                default:
                    w.Write(TreeText);
                    w.Write((byte)node.NodeType);
                    w.Write(node.Value);
                    if (annotations)
                    {
                        var raw = (node as Tree.CompactNavigator)?.RawText;
                        w.Write(raw?.Count ?? 0);
                        if (raw != null) foreach (var part in raw) { w.Write(part.Start); w.Write(part.Length); }
                    }
                    break;
            }
        }

        private static void WriteChildren(BinaryWriter w, XPathNavigator node, bool annotations = false)
        {
            if (node.MoveToFirstChild())
            {
                do
                {
                    WriteTree(w, node, annotations);
                }
                while (node.MoveToNext());
                node.MoveToParent();
            }
        }

        private static XPathNavigator ReadNode(BinaryReader r, bool annotations = false)
        {
            bool document = r.ReadBoolean();
            string baseUri = ReadNullable(r) ?? "";
            byte flags = annotations ? r.ReadByte() : (byte)0;
            if ((flags & ~7) != 0) throw new InvalidDataException("Invalid saved node flags");
            var builder = new Tree.CompactDocument.Builder(new NameTable(), baseUri, fragment: !document, small: true);
            for (byte record = r.ReadByte(); record != TreeDone; record = r.ReadByte())
            {
                switch (record)
                {
                    case TreeElement:
                        builder.StartElement(r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString());
                        if (annotations)
                        {
                            var type = ((SchemaConstantReader)r).Context.ReadType(r); bool nilled = r.ReadBoolean();
                            if (type != null) builder.AnnotateElement(type, nilled);
                            else if (nilled) throw new InvalidDataException("A nilled saved node must have a schema type");
                            int count = r.ReadInt32();
                            if (count < 0) throw new InvalidDataException("Invalid saved ID lookup count");
                            var ids = new string[count]; for (int i = 0; i < count; i++) ids[i] = r.ReadString();
                            if (count != 0) builder.PreserveIdLookup(ids);
                        }
                        break;
                    case TreeEnd:
                        builder.EndElement(emptyTag: false);
                        break;
                    case TreeAttribute:
                    {
                        string prefix = r.ReadString(), local = r.ReadString(), ns = r.ReadString(), value = r.ReadString();
                        builder.Attribute(prefix, local, ns, value);
                        if (annotations)
                        {
                            var type = ((SchemaConstantReader)r).Context.ReadType(r);
                            if (type != null) builder.AnnotateAttribute(type, value);
                            bool id = r.ReadBoolean(), idref = r.ReadBoolean();
                            if (id && (type == null || !Xsl3.Schema.SchemaTypes.IsId(type))) builder.MarkAttributeId(value);
                            if (idref && (type == null || !Xsl3.Schema.SchemaTypes.IsIdref(type))) builder.MarkAttributeIdref();
                        }
                        break;
                    }
                    case TreeNamespace:
                        builder.NamespaceDeclaration(r.ReadString(), r.ReadString());
                        break;
                    case TreeComment:
                        builder.Comment(r.ReadString());
                        break;
                    case TreePI:
                        builder.ProcessingInstruction(r.ReadString(), r.ReadString());
                        break;
                    case TreeText:
                    {
                        var type = (XPathNodeType)r.ReadByte();
                        string value = r.ReadString();
                        int count = annotations ? r.ReadInt32() : 0;
                        if (count < 0) throw new InvalidDataException("Invalid saved raw text count");
                        int offset = 0;
                        for (int i = 0; i < count; i++)
                        {
                            int start = r.ReadInt32(), length = r.ReadInt32();
                            if (start < offset || length < 0 || start > value.Length - length) throw new InvalidDataException("Invalid saved raw text range");
                            builder.Text(value.Substring(offset, start - offset), type);
                            builder.Text(value.Substring(start, length), type, raw: true); offset = start + length;
                        }
                        builder.Text(value.Substring(offset), type);
                        break;
                    }
                    default: throw new InvalidDataException("Unknown saved tree record");
                }
            }
            var tree = builder.Finish();
            tree._constructed = (flags & 1) != 0; tree.NoDocumentUri = (flags & 2) != 0; tree._writerBuilt = (flags & 4) != 0;
            XPathNavigator node = tree.CreateNavigator();
            ReadNodePath(r, node);
            return node;
        }

        private static void ReadNodePath(BinaryReader reader, XPathNavigator node)
        {
            int steps = reader.ReadInt32();
            if (steps < 0) throw new InvalidDataException("Invalid saved node path length");
            for (int i = 0; i < steps; i++)
            {
                byte kind = reader.ReadByte(); int index = reader.ReadInt32(); string prefix = reader.ReadString();
                if (index < 0) throw new InvalidDataException("Invalid saved node path index");
                bool moved;
                switch (kind)
                {
                    case 0:
                        moved = node.MoveToFirstChild();
                        for (int n = 0; moved && n < index; n++) moved = node.MoveToNext();
                        break;
                    case 1:
                        moved = node.MoveToFirstAttribute();
                        for (int n = 0; moved && n < index; n++) moved = node.MoveToNextAttribute();
                        break;
                    case 2: moved = index == 0 && node.MoveToNamespace(prefix); break;
                    default: throw new InvalidDataException("Unknown saved node path step");
                }
                if (!moved) throw new InvalidDataException("Saved node path is outside its tree");
            }
        }

        private static void WriteSharedNode(SchemaConstantWriter writer, XPathNavigator node)
        {
            XPathNavigator root = node.Clone(); root.MoveToRoot();
            int id = writer.NodeRoots.FindIndex(previous => previous.IsSamePosition(root));
            if (id < 0)
            {
                id = writer.NodeRoots.Count; writer.NodeRoots.Add(root.Clone());
                writer.Write(id); WriteNode(writer, root, annotations: true);
            }
            else writer.Write(id);
            WriteNodePath(writer, node);
        }

        private static XPathNavigator ReadSharedNode(SchemaConstantReader reader)
        {
            int id = reader.ReadInt32();
            if (id < 0 || id > reader.NodeRoots.Count) throw new InvalidDataException("Invalid saved node-tree reference");
            if (id == reader.NodeRoots.Count) reader.NodeRoots.Add(ReadNode(reader, annotations: true));
            XPathNavigator node = reader.NodeRoots[id].Clone(); ReadNodePath(reader, node); return node;
        }

        private static void WriteAtomic(BinaryWriter w, AtomicValue a)
        {
            WriteAtomicType(w, a.Type);
            switch (a)
            {
                case DoubleValue d:
                    w.Write((byte)1);
                    w.Write(d.Double);
                    break;
                case FloatValue f:
                    w.Write((byte)2);
                    w.Write(f.Float);
                    break;
                case StringValue s:
                    w.Write((byte)3);
                    w.Write(s.String);
                    break;
                default:
                    w.Write((byte)0);
                    w.Write(a.Value);
                    break;
            }
        }

        private static AtomicValue ReadAtomic(BinaryReader r)
        {
            AtomicType type = ReadAtomicType(r);
            return r.ReadByte() switch
            {
                1 => WithSavedType(AtomicValue.OfDouble(r.ReadDouble()), type),
                2 => WithSavedType(AtomicValue.OfFloat(r.ReadSingle()), type),
                3 => new StringValue(type, r.ReadString()),
                _ => Casting.Cast(AtomicValue.OfUntyped(r.ReadString()), type),
            };
        }

        // ------------------------------------------------------------ sequence types

        private enum TestKind : byte { None, Any, Atomic, Numeric, Node, Function, Map, Array }

        private static void WriteSequenceType(BinaryWriter w, SequenceTypeTest t)
        {
            w.Write((byte)t.Cardinality);
            WriteItemTest(w, t.Item);
        }

        private static SequenceTypeTest ReadSequenceType(BinaryReader r)
        {
            var cardinality = (Cardinality)r.ReadByte();
            return new SequenceTypeTest(ReadItemTest(r), cardinality);
        }

        private static void WriteItemTest(BinaryWriter w, ItemTest? test)
        {
            switch (test)
            {
                case null:
                    w.Write((byte)TestKind.None);
                    break;
                case AnyItemTest:
                    w.Write((byte)TestKind.Any);
                    break;
                case AtomicTypeTest a:
                    w.Write((byte)TestKind.Atomic);
                    WriteAtomicType(w, a.Type);
                    break;
                case NumericTest:
                    w.Write((byte)TestKind.Numeric);
                    break;
                case NodeKindTest n:
                    if (w is not SchemaConstantWriter) NoSchema(n.Names != null || n.SchemaType != null && !Xsl3.Schema.SchemaTypes.IsBuiltIn(n.SchemaType));
                    w.Write((byte)TestKind.Node);
                    w.Write(n.Kind is XPathNodeType kind ? (int)kind : -1);
                    WriteNullable(w, n.NamespaceUri);
                    WriteNullable(w, n.LocalName);
                    w.Write(n.AnyType);
                    WriteItemTest(w, n.DocumentElement);
                    WriteNullable(w, n.TypeName);
                    w.Write(n.Nillable);
                    if (w is SchemaConstantWriter nodeWriter)
                    {
                        nodeWriter.Context.WriteType(w, n.SchemaType);
                        w.Write(n.Names?.Count ?? -1);
                        if (n.Names != null) foreach (var name in n.Names.OrderBy(name => name.Ns, System.StringComparer.Ordinal).ThenBy(name => name.Local, System.StringComparer.Ordinal))
                        { w.Write(name.Ns); w.Write(name.Local); }
                    }
                    break;
                case FunctionItemTest f:
                    w.Write((byte)TestKind.Function);
                    w.Write(f.Parameters?.Count ?? -1);
                    foreach (SequenceTypeTest p in f.Parameters ?? System.Array.Empty<SequenceTypeTest>())
                    {
                        WriteSequenceType(w, p);
                    }
                    w.Write(f.ReturnType != null);
                    if (f.ReturnType != null)
                    {
                        WriteSequenceType(w, f.ReturnType);
                    }
                    break;
                case MapTest m:
                    w.Write((byte)TestKind.Map);
                    w.Write(m.KeyType != null);
                    if (m.KeyType != null)
                    {
                        WriteAtomicType(w, m.KeyType);
                    }
                    w.Write(m.ValueType != null);
                    if (m.ValueType != null)
                    {
                        WriteSequenceType(w, m.ValueType);
                    }
                    break;
                case ArrayTest a:
                    w.Write((byte)TestKind.Array);
                    w.Write(a.MemberType != null);
                    if (a.MemberType != null)
                    {
                        WriteSequenceType(w, a.MemberType);
                    }
                    break;
                default:
                    throw new System.NotSupportedException($"A compiled stylesheet can't be saved with the item type {test}");
            }
        }

        private static ItemTest? ReadItemTest(BinaryReader r)
        {
            switch ((TestKind)r.ReadByte())
            {
                case TestKind.None:
                    return null;
                case TestKind.Any:
                    return AnyItemTest.Instance;
                case TestKind.Atomic:
                    return new AtomicTypeTest(ReadAtomicType(r));
                case TestKind.Numeric:
                    return NumericTest.Instance;
                case TestKind.Node:
                {
                    int kind = r.ReadInt32();
                    string? ns = ReadNullable(r), local = ReadNullable(r);
                    bool anyType = r.ReadBoolean();
                    var documentElement = (NodeKindTest?)ReadItemTest(r);
                    string? typeName = ReadNullable(r);
                    bool nillable = r.ReadBoolean();
                    System.Xml.Schema.XmlSchemaType? schema = typeName == null ? null : typeName == "anyType" ? Xsl3.Schema.SchemaTypes.AnyType
                        : System.Xml.Schema.XmlSchemaType.GetBuiltInSimpleType(new System.Xml.XmlQualifiedName(typeName, QName.Ns.Xs));
                    HashSet<(string Ns, string Local)>? names = null;
                    if (r is SchemaConstantReader nodeReader)
                    {
                        schema = nodeReader.Context.ReadType(r);
                        int length = r.ReadInt32();
                        if (length < -1) throw new InvalidDataException("Invalid saved schema name set");
                        if (length >= 0)
                        {
                            names = new();
                            for (int i = 0; i < length; i++) names.Add((r.ReadString(), r.ReadString()));
                        }
                    }
                    return new NodeKindTest(kind < 0 ? null : (XPathNodeType)kind, ns, local, anyType, documentElement)
                    { TypeName = typeName, Nillable = nillable, SchemaType = schema, Names = names };
                }
                case TestKind.Function:
                {
                    int count = r.ReadInt32();
                    SequenceTypeTest[]? parameters = count < 0 ? null : new SequenceTypeTest[count];
                    for (int i = 0; i < count; i++)
                    {
                        parameters![i] = ReadSequenceType(r);
                    }
                    SequenceTypeTest? returnType = r.ReadBoolean() ? ReadSequenceType(r) : null;
                    return parameters == null && returnType == null ? FunctionItemTest.Instance : new FunctionItemTest(parameters, returnType);
                }
                case TestKind.Map:
                {
                    AtomicType? key = r.ReadBoolean() ? ReadAtomicType(r) : null;
                    SequenceTypeTest? value = r.ReadBoolean() ? ReadSequenceType(r) : null;
                    return key == null && value == null ? MapTest.Any : new MapTest(key, value);
                }
                case TestKind.Array:
                {
                    SequenceTypeTest? member = r.ReadBoolean() ? ReadSequenceType(r) : null;
                    return member == null ? ArrayTest.Any : new ArrayTest(member);
                }
                default:
                    throw new System.NotSupportedException("Unknown item type in a compiled stylesheet");
            }
        }

        // ------------------------------------------------------------ output formats

        private static void WriteResultFormat(BinaryWriter w, ResultFormat format)
        {
            format.Settings.GetObjectData(w is SchemaConstantWriter scoped ? scoped.Original : (XmlQueryDataWriter)w);
            WriteOutputExtras(w, format.Extras);
            w.Write(format.Text);
        }

        private static ResultFormat ReadResultFormat(BinaryReader r) =>
            new(XmlWriterSettingsInternals.Create(r is SchemaConstantReader scoped ? scoped.Original : (XmlQueryDataReader)r), ReadOutputExtras(r), r.ReadBoolean());

        internal static void WriteOutputExtras(BinaryWriter w, OutputExtras? extras)
        {
            w.Write(extras != null);
            if (extras == null)
            {
                return;
            }
            w.Write(extras.Xhtml);
            w.Write(extras.IncludeContentType);
            w.Write(extras.Encoding);
            w.Write(extras.MediaType);
            w.Write(extras.Normalization is System.Text.NormalizationForm form ? (int)form : -1);
            w.Write(extras.FullyNormalized);
            w.Write(extras.CharacterMap?.Count ?? -1);
            foreach (var (c, s) in extras.CharacterMap ?? new Dictionary<int, string>())
            {
                w.Write(c);
                w.Write(s);
            }
            w.Write(extras.EscapeUriAttributes);
            WriteNullable(w, extras.RawMethod);
            WriteNullable(w, extras.ItemSeparator);
            WriteNullable(w, extras.JsonNodeOutputMethod);
            w.Write(extras.AllowDuplicateNames);
            w.Write(extras.AdaptiveXmlDeclaration);
            w.Write(extras.Indent);
            w.Write(extras.Html5);
            w.Write(extras.HtmlMethod);
            w.Write(extras.DefaultDoctype);
        }

        internal static OutputExtras? ReadOutputExtras(BinaryReader r)
        {
            if (!r.ReadBoolean())
            {
                return null;
            }
            var extras = new OutputExtras
            {
                Xhtml = r.ReadBoolean(),
                IncludeContentType = r.ReadBoolean(),
                Encoding = r.ReadString(),
                MediaType = r.ReadString(),
            };
            int form = r.ReadInt32();
            extras.Normalization = form < 0 ? null : (System.Text.NormalizationForm)form;
            extras.FullyNormalized = r.ReadBoolean();
            int count = r.ReadInt32();
            if (count >= 0)
            {
                var map = new Dictionary<int, string>();
                for (int i = 0; i < count; i++)
                {
                    map[r.ReadInt32()] = r.ReadString();
                }
                extras.CharacterMap = map;
            }
            extras.EscapeUriAttributes = r.ReadBoolean();
            extras.RawMethod = ReadNullable(r);
            extras.ItemSeparator = ReadNullable(r);
            extras.JsonNodeOutputMethod = ReadNullable(r);
            extras.AllowDuplicateNames = r.ReadBoolean();
            extras.AdaptiveXmlDeclaration = r.ReadBoolean();
            extras.Indent = r.ReadBoolean();
            extras.Html5 = r.ReadBoolean();
            extras.HtmlMethod = r.ReadBoolean();
            extras.DefaultDoctype = r.ReadBoolean();
            return extras;
        }

        // ------------------------------------------------------------ decimal formats, namespaces, names

        private static void WriteDecimalFormats(BinaryWriter w, DecimalFormatTable table)
        {
            WriteDecimalFormat(w, table.Unnamed);
            w.Write(table.Named.Count);
            foreach (var (name, format) in table.Named)
            {
                WriteName(w, name);
                WriteDecimalFormat(w, format);
            }
            WriteResolver(w, table.Namespaces);
        }

        private static DecimalFormatTable ReadDecimalFormats(BinaryReader r)
        {
            XPathDecimalFormat unnamed = ReadDecimalFormat(r);
            var named = new Dictionary<QName, XPathDecimalFormat>();
            for (int i = r.ReadInt32(); i > 0; i--)
            {
                named[ReadName(r)] = ReadDecimalFormat(r);
            }
            return new DecimalFormatTable(unnamed, named, ReadResolver(r));
        }

        private static void WriteDecimalFormat(BinaryWriter w, XPathDecimalFormat f)
        {
            w.Write(f.DecimalSeparator);
            w.Write(f.GroupingSeparator);
            w.Write(f.ExponentSeparator);
            w.Write(f.Infinity);
            w.Write(f.MinusSign);
            w.Write(f.NaN);
            w.Write(f.Percent);
            w.Write(f.PerMille);
            w.Write(f.ZeroDigit);
            w.Write(f.Digit);
            w.Write(f.PatternSeparator);
        }

        private static XPathDecimalFormat ReadDecimalFormat(BinaryReader r) => new()
        {
            DecimalSeparator = r.ReadInt32(),
            GroupingSeparator = r.ReadInt32(),
            ExponentSeparator = r.ReadString(),
            Infinity = r.ReadString(),
            MinusSign = r.ReadInt32(),
            NaN = r.ReadString(),
            Percent = r.ReadInt32(),
            PerMille = r.ReadInt32(),
            ZeroDigit = r.ReadInt32(),
            Digit = r.ReadInt32(),
            PatternSeparator = r.ReadInt32(),
        };

        private static void WriteResolver(BinaryWriter w, IXmlNamespaceResolver? resolver)
        {
            w.Write(resolver != null);
            if (resolver != null)
            {
                WriteStringMap(w, resolver.GetNamespacesInScope(XmlNamespaceScope.ExcludeXml));
            }
        }

        private static IXmlNamespaceResolver? ReadResolver(BinaryReader r)
        {
            if (!r.ReadBoolean())
            {
                return null;
            }
            var manager = new XmlNamespaceManager(new NameTable());
            foreach (var (prefix, uri) in ReadStringMap(r))
            {
                if (prefix != "xmlns")
                {
                    manager.AddNamespace(prefix, uri);
                }
            }
            return manager;
        }

        private static void WriteStringMap(BinaryWriter w, IEnumerable<KeyValuePair<string, string>> map)
        {
            var pairs = map.ToList();
            w.Write(pairs.Count);
            foreach (var (key, value) in pairs)
            {
                w.Write(key);
                w.Write(value);
            }
        }

        private static Dictionary<string, string> ReadStringMap(BinaryReader r)
        {
            var map = new Dictionary<string, string>(System.StringComparer.Ordinal);
            for (int i = r.ReadInt32(); i > 0; i--)
            {
                map[r.ReadString()] = r.ReadString();
            }
            return map;
        }

        private static void WriteName(BinaryWriter w, QName name)
        {
            w.Write(name.NamespaceUri);
            w.Write(name.LocalName);
            w.Write(name.Prefix);
        }

        private static QName ReadName(BinaryReader r) => new(r.ReadString(), r.ReadString(), r.ReadString());

        private static void WriteOptionalName(BinaryWriter w, QName? name)
        {
            w.Write(name != null);
            if (name != null)
            {
                WriteName(w, name);
            }
        }

        private static QName? ReadOptionalName(BinaryReader r) => r.ReadBoolean() ? ReadName(r) : null;

        private static void WriteNullable(BinaryWriter w, string? s)
        {
            w.Write(s != null);
            if (s != null)
            {
                w.Write(s);
            }
        }

        private static string? ReadNullable(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;

        private static AtomicValue WithSavedType(AtomicValue value, AtomicType type) => ReferenceEquals(value.Type, type) ? value : value.WithType(type);

        private static void WriteAtomicType(BinaryWriter writer, AtomicType type)
        {
            if (writer is not SchemaConstantWriter) NoSchema(type.IsUserDefined);
            WriteName(writer, type.Name);
            if (writer is SchemaConstantWriter scoped) scoped.Context.WriteType(writer, type.UserSchemaType);
        }

        private static AtomicType ReadAtomicType(BinaryReader reader)
        {
            QName name = ReadName(reader);
            if (reader is SchemaConstantReader scoped && scoped.Context.ReadType(reader) is { } schema)
            {
                AtomicType type = Xsl3.Schema.SchemaTypes.AtomicTypeOf(schema) ?? throw new InvalidDataException("A saved atomic type is not atomic");
                if (!type.Name.Equals(name)) throw new InvalidDataException("Saved atomic type name does not match its schema");
                return type;
            }
            return Type(name);
        }

        private static AtomicType Type(QName name) =>
            AtomicTypes.Find(name.NamespaceUri, name.LocalName) ?? throw new System.NotSupportedException($"Unknown atomic type {name.EQName} in a compiled stylesheet");
    }
}
