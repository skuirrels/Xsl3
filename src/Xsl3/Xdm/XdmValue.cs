// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;

namespace Xsl3.Xdm
{
    /// <summary>
    /// A value of the XPath and XQuery Data Model (XDM 3.1 §2.5): a sequence of items, each a node, an atomic value or
    /// a function item (maps and arrays among them). Values are immutable. An <see cref="XdmItem"/> is also a value: a
    /// sequence of one item.
    /// </summary>
    public class XdmValue : IReadOnlyList<XdmItem>
    {
        private protected XdmValue(IList<XPathItem> items)
        {
            Items = items;
        }

        /// <summary>A sequence of the given items, in order.</summary>
        public XdmValue(IEnumerable<XdmItem> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            Items = items.Select(i => i.Item).ToArray();
        }

        /// <summary>The engine's form of the sequence.</summary>
        internal IList<XPathItem> Items { get; }

        /// <summary>The empty sequence.</summary>
        public static XdmValue Empty => XdmEmptySequence.Instance;

        /// <summary>The number of items.</summary>
        public int Count => Items.Count;

        /// <summary>The item at <paramref name="index"/> (from 0).</summary>
        public XdmItem this[int index] => XdmItem.Wrap(Items[index]);

        /// <summary>This sequence followed by <paramref name="other"/>'s items.</summary>
        public XdmValue Append(XdmValue other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return Wrap(Items.Concat(other.Items).ToArray());
        }

        /// <summary>
        /// The XDM form of a .NET value, as a stylesheet parameter is given one: integers become xs:integer, <c>decimal</c>,
        /// <c>double</c> and <c>float</c> their XSD types, <c>DateTime</c>, <c>DateTimeOffset</c>, <c>DateOnly</c>,
        /// <c>TimeOnly</c>, <c>TimeSpan</c>, <c>Uri</c>, <c>byte[]</c> and <see cref="QName"/> theirs, strings xs:string,
        /// booleans xs:boolean, navigators nodes, dictionaries maps and other enumerables sequences. Null is the empty
        /// sequence, and an <see cref="XdmValue"/> is returned as it is.
        /// </summary>
        /// <exception cref="ArgumentException">The value has no XDM form.</exception>
        public static XdmValue MakeValue(object? value)
        {
            switch (value)
            {
                case null:
                    return Empty;
                case XdmValue v:
                    return v;
                case string s:
                    return new XdmAtomicValue(AtomicValue.OfString(s));
                case bool b:
                    return new XdmAtomicValue(AtomicValue.OfBoolean(b));
                case XPathNavigator n:
                    return new XdmNode(n);
                case IXPathNavigable navigable:
                    return new XdmNode(navigable.CreateNavigator() ?? throw new ArgumentException("The document has no navigator", nameof(value)));
                case XPathNodeIterator iterator:
                    var nodes = new List<XPathItem>();
                    while (iterator.MoveNext())
                    {
                        nodes.Add(iterator.Current!.Clone());
                    }
                    return Wrap(nodes);
            }
            try
            {
                if (ClrValues.TryConvert(value, out IList<XPathItem> items))
                {
                    return Wrap(items);
                }
            }
            catch (XdmException e)
            {
                throw new ArgumentException(e.Message, nameof(value), e);
            }
            throw new ArgumentException($"A {value.GetType().Name} has no XDM form", nameof(value));
        }

        /// <summary>The value of the engine's sequence, as an item when it has one item.</summary>
        internal static XdmValue Wrap(IList<XPathItem> items) => items.Count switch
        {
            0 => Empty,
            1 => XdmItem.Wrap(items[0]),
            _ => new XdmValue(items),
        };

        public IEnumerator<XdmItem> GetEnumerator()
        {
            foreach (XPathItem item in Items)
            {
                yield return XdmItem.Wrap(item);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>The items' forms (see <see cref="XdmItem.ToString"/>), separated by spaces.</summary>
        public override string ToString() => string.Join(" ", this.Select(i => i.ToString()));
    }

    /// <summary>The empty sequence.</summary>
    public sealed class XdmEmptySequence : XdmValue
    {
        private XdmEmptySequence() : base(Array.Empty<XPathItem>())
        {
        }

        public static XdmEmptySequence Instance { get; } = new();
    }

    /// <summary>One item: a node, an atomic value or a function item.</summary>
    public abstract class XdmItem : XdmValue
    {
        private protected XdmItem(XPathItem item) : base(new[] { item })
        {
        }

        internal XPathItem Item => Items[0];

        /// <summary>Whether the item is an atomic value.</summary>
        public abstract bool IsAtomic { get; }

        /// <summary>The item's string value (fn:string).</summary>
        /// <exception cref="InvalidOperationException">The item is a function item, which has no string value.</exception>
        public virtual string StringValue => Item.Value;

        internal static XdmItem Wrap(XPathItem item) => item switch
        {
            XPathNavigator node => new XdmNode(node),
            AtomicValue atomic => new XdmAtomicValue(atomic),
            MapItem map => new XdmMap(map),
            ArrayItem array => new XdmArray(array),
            FunctionItem function => new XdmFunctionItem(function),
            _ => throw new ArgumentException($"Not an XDM item: {item.GetType().Name}", nameof(item)),
        };

        /// <summary>A node's markup, an atomic value's lexical form, a function item's description.</summary>
        public override string ToString() => Item.ToString() ?? "";
    }

    /// <summary>A node (XDM 3.1 §6), over an <see cref="XPathNavigator"/>.</summary>
    public sealed class XdmNode : XdmItem
    {
        /// <summary>The node <paramref name="navigator"/> is on (a copy of the navigator is kept, not the tree).</summary>
        public XdmNode(XPathNavigator navigator) : base((navigator ?? throw new ArgumentNullException(nameof(navigator))).Clone())
        {
        }

        /// <summary>A navigator on the node (a new one each time).</summary>
        public XPathNavigator Navigator => ((XPathNavigator)Item).Clone();

        /// <summary>The kind of node.</summary>
        public XPathNodeType NodeKind => ((XPathNavigator)Item).NodeType;

        /// <summary>The node's name, or null for a kind of node without one.</summary>
        public QName? NodeName
        {
            get
            {
                var node = (XPathNavigator)Item;
                return node.NodeType is XPathNodeType.Element or XPathNodeType.Attribute or XPathNodeType.ProcessingInstruction or XPathNodeType.Namespace
                    ? new QName(node.NamespaceURI, node.LocalName, node.Prefix)
                    : null;
            }
        }

        /// <summary>The node's base URI, or null.</summary>
        public Uri? BaseUri => Uri.TryCreate(((XPathNavigator)Item).BaseURI, UriKind.Absolute, out Uri? uri) ? uri : null;

        /// <summary>The node's markup, as <see cref="XPathNavigator.OuterXml"/> gives it.</summary>
        public string OuterXml => ((XPathNavigator)Item).OuterXml;

        /// <summary>
        /// Parses a document into Xsl3's compact tree, the tree XSLT 2.0+ stylesheets use. Whitespace is kept; DTDs are
        /// read when the reader's settings allow it.
        /// </summary>
        public static XdmNode Load(XmlReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            return new XdmNode(Tree.CompactDocument.Load(reader, XmlSpace.Preserve).CreateNavigator());
        }

        /// <summary>Parses a document from a stream; <paramref name="baseUri"/> is its base URI, if it has one.</summary>
        public static XdmNode Load(System.IO.Stream stream, string? baseUri = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore }, baseUri);
            return Load(reader);
        }

        /// <summary>Parses a document from text; <paramref name="baseUri"/> is its base URI, if it has one.</summary>
        public static XdmNode Load(System.IO.TextReader text, string? baseUri = null)
        {
            ArgumentNullException.ThrowIfNull(text);
            using XmlReader reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore }, baseUri);
            return Load(reader);
        }

        public override bool IsAtomic => false;

        public override string ToString() => OuterXml;
    }

    /// <summary>An atomic value (XDM 3.1 §2.7): a value of one of the XSD atomic types.</summary>
    public sealed class XdmAtomicValue : XdmItem
    {
        internal XdmAtomicValue(AtomicValue value) : base(value)
        {
        }

        /// <summary>An xs:string.</summary>
        public XdmAtomicValue(string value) : this(AtomicValue.OfString(value ?? throw new ArgumentNullException(nameof(value))))
        {
        }

        /// <summary>An xs:boolean.</summary>
        public XdmAtomicValue(bool value) : this(AtomicValue.OfBoolean(value))
        {
        }

        /// <summary>An xs:integer.</summary>
        public XdmAtomicValue(long value) : this(AtomicValue.OfInteger(value))
        {
        }

        /// <summary>An xs:integer.</summary>
        public XdmAtomicValue(BigInteger value) : this(AtomicValue.OfInteger(value))
        {
        }

        /// <summary>An xs:decimal.</summary>
        public XdmAtomicValue(decimal value) : this(AtomicValue.OfDecimal(value))
        {
        }

        /// <summary>An xs:double.</summary>
        public XdmAtomicValue(double value) : this(AtomicValue.OfDouble(value))
        {
        }

        /// <summary>An xs:float.</summary>
        public XdmAtomicValue(float value) : this(AtomicValue.OfFloat(value))
        {
        }

        /// <summary>An xs:QName.</summary>
        public XdmAtomicValue(QName value) : this(new QNameValue(AtomicTypes.QName, value ?? throw new ArgumentNullException(nameof(value))))
        {
        }

        /// <summary>An xs:anyURI.</summary>
        public XdmAtomicValue(Uri value) : this(new StringValue(AtomicTypes.AnyUri, (value ?? throw new ArgumentNullException(nameof(value))).OriginalString))
        {
        }

        /// <summary>
        /// The value of type <paramref name="type"/> (a built-in xs: type, such as xs:date) whose lexical form is
        /// <paramref name="lexical"/>, as <c>xs:date('2026-10-21')</c> makes one.
        /// </summary>
        /// <exception cref="ArgumentException">The type isn't a built-in atomic type, or the lexical form isn't valid for it.</exception>
        public XdmAtomicValue(string lexical, QName type) : this(Construct(lexical, type))
        {
        }

        private static AtomicValue Construct(string lexical, QName type)
        {
            ArgumentNullException.ThrowIfNull(lexical);
            ArgumentNullException.ThrowIfNull(type);
            AtomicType target = AtomicTypes.Find(type.NamespaceUri, type.LocalName) ?? throw new ArgumentException($"{type.EQName} is not a built-in atomic type", nameof(type));
            try
            {
                return Casting.Cast(AtomicValue.OfUntyped(lexical), target);
            }
            catch (XdmException e)
            {
                throw new ArgumentException(e.Message, nameof(lexical), e);
            }
        }

        internal AtomicValue Atomic => (AtomicValue)Item;

        /// <summary>The name of the value's type, such as xs:integer.</summary>
        public QName TypeName => Atomic.Type.Name;

        /// <summary>
        /// The value as .NET has it: a <c>string</c> (also for xs:anyURI and xs:untypedAtomic), <c>bool</c>, <c>long</c>
        /// (or <see cref="BigInteger"/> beyond its range) for integers, <c>decimal</c>, <c>double</c>, <c>float</c>, a
        /// <see cref="QName"/>, and otherwise the lexical form.
        /// </summary>
        public object Value => Atomic switch
        {
            StringValue s => s.String,
            BooleanValue b => b.Boolean,
            IntegerValue i => i.Integer >= long.MinValue && i.Integer <= long.MaxValue ? (object)(long)i.Integer : i.Integer,
            DecimalValue d => d.Decimal,
            DoubleValue d => d.Double,
            FloatValue f => f.Float,
            QNameValue q => q.QName,
            AtomicValue a => a.Value,
        };

        public override bool IsAtomic => true;

        public override bool Equals(object? obj) => obj is XdmAtomicValue other && Atomic.Type == other.Atomic.Type && Atomic.Value == other.Atomic.Value;

        public override int GetHashCode() => HashCode.Combine(Atomic.Type, Atomic.Value);
    }

    /// <summary>A function item (XDM 3.1 §2.8): a named or anonymous function, a map or an array.</summary>
    public class XdmFunctionItem : XdmItem
    {
        internal XdmFunctionItem(FunctionItem function) : base(function)
        {
        }

        internal FunctionItem Function => (FunctionItem)Item;

        /// <summary>The function's name, or null for an anonymous function, a map or an array.</summary>
        public QName? Name => Function.Name;

        /// <summary>The number of arguments the function takes.</summary>
        public int Arity => Function.Arity;

        public override bool IsAtomic => false;

        /// <summary>Always throws: a function item has no string value.</summary>
        public override string StringValue => throw new InvalidOperationException($"{Function.Describe()} has no string value");
    }

    /// <summary>A map (XPath 3.1 §3.11.1): entries from distinct atomic keys to values. Maps are immutable.</summary>
    public sealed class XdmMap : XdmFunctionItem
    {
        internal XdmMap(MapItem map) : base(map)
        {
        }

        /// <summary>A map of the given entries.</summary>
        /// <exception cref="ArgumentException">Two keys are the same key (op:same-key).</exception>
        public XdmMap(IEnumerable<KeyValuePair<XdmAtomicValue, XdmValue>> entries) : this(Build(entries))
        {
        }

        private static MapItem Build(IEnumerable<KeyValuePair<XdmAtomicValue, XdmValue>> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);
            MapItem map = MapItem.Empty;
            foreach (var (key, value) in entries)
            {
                ArgumentNullException.ThrowIfNull(key);
                if (map.ContainsKey(key.Atomic))
                {
                    throw new ArgumentException($"The key {key} is in the map twice", nameof(entries));
                }
                map = map.Put(key.Atomic, (value ?? Empty).Items);
            }
            return map;
        }

        /// <summary>The empty map.</summary>
        public static XdmMap EmptyMap { get; } = new(MapItem.Empty);

        internal MapItem Map => (MapItem)Item;

        /// <summary>The number of entries (map:size). (<see cref="XdmValue.Count"/> is 1: a map is one item.)</summary>
        public int Size => Map.Count;

        /// <summary>The keys, in the map's order.</summary>
        public IEnumerable<XdmAtomicValue> Keys => Map.Keys.Select(k => new XdmAtomicValue(k));

        /// <summary>The entries, in the map's order.</summary>
        public IEnumerable<KeyValuePair<XdmAtomicValue, XdmValue>> Entries =>
            Map.Entries.Select(e => new KeyValuePair<XdmAtomicValue, XdmValue>(new XdmAtomicValue(e.Key), Wrap(e.Value)));

        /// <summary>The value for <paramref name="key"/>, or null if the map has no such key.</summary>
        public XdmValue? Get(XdmAtomicValue key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return Map.Get(key.Atomic) is IList<XPathItem> value ? Wrap(value) : null;
        }

        /// <summary>The value for the xs:string key <paramref name="key"/>, or null.</summary>
        public XdmValue? Get(string key) => Get(new XdmAtomicValue(key));

        /// <summary>Whether the map has <paramref name="key"/>.</summary>
        public bool ContainsKey(XdmAtomicValue key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return Map.ContainsKey(key.Atomic);
        }

        /// <summary>A map with <paramref name="key"/> bound to <paramref name="value"/>, replacing an entry with the same key.</summary>
        public XdmMap Put(XdmAtomicValue key, XdmValue value)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            return new XdmMap(Map.Put(key.Atomic, value.Items));
        }

        /// <summary>A map without <paramref name="key"/>.</summary>
        public XdmMap Remove(XdmAtomicValue key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return new XdmMap(Map.Remove(key.Atomic));
        }
    }

    /// <summary>An array (XPath 3.1 §3.11.2): an ordered list of members, each a value. Arrays are immutable.</summary>
    public sealed class XdmArray : XdmFunctionItem
    {
        internal XdmArray(ArrayItem array) : base(array)
        {
        }

        /// <summary>An array of the given members.</summary>
        public XdmArray(IEnumerable<XdmValue> members) : this(ArrayItem.Of((members ?? throw new ArgumentNullException(nameof(members))).Select(m => (m ?? Empty).Items)))
        {
        }

        /// <summary>The empty array.</summary>
        public static XdmArray EmptyArray { get; } = new(ArrayItem.Empty);

        internal ArrayItem Array => (ArrayItem)Item;

        /// <summary>The number of members (array:size).</summary>
        public int Size => Array.Count;

        /// <summary>The member at <paramref name="index"/>, from 0 (array:get counts from 1).</summary>
        public XdmValue Get(int index) => Wrap(Array[index]);

        /// <summary>The members, in order.</summary>
        public IEnumerable<XdmValue> Members => Array.Members.Select(Wrap);

        /// <summary>An array with <paramref name="member"/> added at the end.</summary>
        public XdmArray AddMember(XdmValue member)
        {
            ArgumentNullException.ThrowIfNull(member);
            return new XdmArray(Array.Append(member.Items));
        }
    }
}
