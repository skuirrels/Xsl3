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
        private const int FormatVersion = 1;

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
        }

        public static void Write(XmlQueryDataWriter w, ConstantTable table)
        {
            w.Write(FormatVersion);
            w.Write(table.Count);
            for (int i = 0; i < table.Count; i++)
            {
                WriteValue(w, table.Get(i));
            }
        }

        public static ConstantTable Read(XmlQueryDataReader r)
        {
            if (r.ReadInt32() != FormatVersion)
            {
                throw new System.NotSupportedException("The compiled stylesheet's constants were saved in a format this version of Xsl3 doesn't read");
            }
            var table = new ConstantTable();
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                table.Add(ReadValue(r)!);
            }
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
                    WriteName(w, q.Type.Name);
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
                    w.Write((byte)Kind.CastTarget);
                    WriteName(w, c.Type.Name);
                    WriteResolver(w, c.Namespaces);
                    w.Write((byte)c.Kind);
                    break;
                case SequenceTypeTest t:
                    w.Write((byte)Kind.SequenceType);
                    WriteSequenceType(w, t);
                    break;
                case XsltNameContext n:
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
                default:
                    throw new System.NotSupportedException($"A compiled stylesheet can't be saved with a constant of type {value.GetType().Name}");
            }
        }

        private static object? ReadValue(BinaryReader r)
        {
            var kind = (Kind)r.ReadByte();
            switch (kind)
            {
                case Kind.Null:
                    return null;
                case Kind.QName:
                    return new QNameValue(Type(ReadName(r)), ReadName(r));
                case Kind.Atomic:
                    return ReadAtomic(r);
                case Kind.StaticInfo:
                    return new StaticInfo(ReadNullable(r), r.ReadString(), ReadDecimalFormats(r)) { Namespaces = ReadResolver(r) };
                case Kind.CastTarget:
                    return new CastTarget(Type(ReadName(r)), ReadResolver(r), (CastKind)r.ReadByte());
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
                    return new XsltNameContext(namespaces, functions) { EvaluateEnabled = r.ReadBoolean() };
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
                    return new EvaluateContext(namespaces, defaultElement, baseUri, collation, unnamed, named);
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
                        accumulators[name] = new AccumulatorInfo(offset, phases, type, r.ReadBoolean());
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
                case XPathNavigator { NodeType: XPathNodeType.Root } document:
                    // A document is saved as its markup, and parsed again into a compact tree.
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
                        new FunctionContext(FunctionContext.LocalTimezoneMinutes, (a, b) => a.ComparePosition(b) switch
                        {
                            XmlNodeOrder.Before => -1,
                            XmlNodeOrder.After => 1,
                            _ => 0,
                        }));
                }
                default:
                    return (XPathItem)ReadValue(r)!;
            }
        }

        // A node's tree: element, attribute, namespace, text, comment and PI records, from the root down.
        private const byte TreeElement = 1, TreeEnd = 2, TreeAttribute = 3, TreeNamespace = 4, TreeText = 5, TreeComment = 6, TreePI = 7, TreeDone = 0;

        private static void WriteNode(BinaryWriter w, XPathNavigator node)
        {
            XPathNavigator root = node.Clone();
            root.MoveToRoot();
            w.Write(root.NodeType == XPathNodeType.Root);
            WriteNullable(w, root.BaseURI);
            WriteTree(w, root);
            w.Write(TreeDone);

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

        private static void WriteTree(BinaryWriter w, XPathNavigator node)
        {
            switch (node.NodeType)
            {
                case XPathNodeType.Root:
                    WriteChildren(w, node);
                    break;
                case XPathNodeType.Element:
                    w.Write(TreeElement);
                    w.Write(node.Prefix);
                    w.Write(node.LocalName);
                    w.Write(node.NamespaceURI);
                    w.Write(node.BaseURI);
                    if (node.MoveToFirstNamespace(XPathNamespaceScope.Local))
                    {
                        do
                        {
                            WriteTree(w, node);
                        }
                        while (node.MoveToNextNamespace(XPathNamespaceScope.Local));
                        node.MoveToParent();
                    }
                    if (node.MoveToFirstAttribute())
                    {
                        do
                        {
                            WriteTree(w, node);
                        }
                        while (node.MoveToNextAttribute());
                        node.MoveToParent();
                    }
                    WriteChildren(w, node);
                    w.Write(TreeEnd);
                    break;
                case XPathNodeType.Attribute:
                    w.Write(TreeAttribute);
                    w.Write(node.Prefix);
                    w.Write(node.LocalName);
                    w.Write(node.NamespaceURI);
                    w.Write(node.Value);
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
                    break;
            }
        }

        private static void WriteChildren(BinaryWriter w, XPathNavigator node)
        {
            if (node.MoveToFirstChild())
            {
                do
                {
                    WriteTree(w, node);
                }
                while (node.MoveToNext());
                node.MoveToParent();
            }
        }

        private static XPathNavigator ReadNode(BinaryReader r)
        {
            bool document = r.ReadBoolean();
            string baseUri = ReadNullable(r) ?? "";
            var builder = new Tree.CompactDocument.Builder(new NameTable(), baseUri, fragment: !document, small: true);
            for (byte record = r.ReadByte(); record != TreeDone; record = r.ReadByte())
            {
                switch (record)
                {
                    case TreeElement:
                        builder.StartElement(r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString());
                        break;
                    case TreeEnd:
                        builder.EndElement(emptyTag: false);
                        break;
                    case TreeAttribute:
                        builder.Attribute(r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString());
                        break;
                    case TreeNamespace:
                        builder.NamespaceDeclaration(r.ReadString(), r.ReadString());
                        break;
                    case TreeComment:
                        builder.Comment(r.ReadString());
                        break;
                    case TreePI:
                        builder.ProcessingInstruction(r.ReadString(), r.ReadString());
                        break;
                    default:
                        var type = (XPathNodeType)r.ReadByte();
                        builder.Text(r.ReadString(), type);
                        break;
                }
            }
            XPathNavigator node = builder.Finish().CreateNavigator();
            for (int steps = r.ReadInt32(); steps > 0; steps--)
            {
                byte kind = r.ReadByte();
                int index = r.ReadInt32();
                string prefix = r.ReadString();
                switch (kind)
                {
                    case 1:
                        node.MoveToFirstAttribute();
                        for (; index > 0; index--)
                        {
                            node.MoveToNextAttribute();
                        }
                        break;
                    case 2:
                        node.MoveToNamespace(prefix);
                        break;
                    default:
                        node.MoveToFirstChild();
                        for (; index > 0; index--)
                        {
                            node.MoveToNext();
                        }
                        break;
                }
            }
            return node;
        }

        private static void WriteAtomic(BinaryWriter w, AtomicValue a)
        {
            WriteName(w, a.Type.Name);
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
            AtomicType type = Type(ReadName(r));
            return r.ReadByte() switch
            {
                1 => AtomicValue.OfDouble(r.ReadDouble()),
                2 => AtomicValue.OfFloat(r.ReadSingle()),
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
                    WriteName(w, a.Type.Name);
                    break;
                case NumericTest:
                    w.Write((byte)TestKind.Numeric);
                    break;
                case NodeKindTest n:
                    w.Write((byte)TestKind.Node);
                    w.Write(n.Kind is XPathNodeType kind ? (int)kind : -1);
                    WriteNullable(w, n.NamespaceUri);
                    WriteNullable(w, n.LocalName);
                    w.Write(n.AnyType);
                    WriteItemTest(w, n.DocumentElement);
                    WriteNullable(w, n.TypeName);
                    w.Write(n.Nillable);
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
                        WriteName(w, m.KeyType.Name);
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
                    return new AtomicTypeTest(Type(ReadName(r)));
                case TestKind.Numeric:
                    return NumericTest.Instance;
                case TestKind.Node:
                {
                    int kind = r.ReadInt32();
                    string? ns = ReadNullable(r), local = ReadNullable(r);
                    bool anyType = r.ReadBoolean();
                    var documentElement = (NodeKindTest?)ReadItemTest(r);
                    return new NodeKindTest(kind < 0 ? null : (XPathNodeType)kind, ns, local, anyType, documentElement)
                    {
                        TypeName = ReadNullable(r),
                        Nillable = r.ReadBoolean(),
                    };
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
                    AtomicType? key = r.ReadBoolean() ? Type(ReadName(r)) : null;
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
            format.Settings.GetObjectData((XmlQueryDataWriter)w);
            WriteOutputExtras(w, format.Extras);
            w.Write(format.Text);
        }

        private static ResultFormat ReadResultFormat(BinaryReader r) =>
            new(XmlWriterSettingsInternals.Create((XmlQueryDataReader)r), ReadOutputExtras(r), r.ReadBoolean());

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

        private static AtomicType Type(QName name) =>
            AtomicTypes.Find(name.NamespaceUri, name.LocalName) ?? throw new System.NotSupportedException($"Unknown atomic type {name.EQName} in a compiled stylesheet");
    }
}
