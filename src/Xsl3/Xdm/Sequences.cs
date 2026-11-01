// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Xml.XPath;

namespace Xsl3.Xdm
{
    /// <summary>Sequence-level operations of XPath 3.1 §2.4: atomisation and effective boolean value.</summary>
    internal static class Sequences
    {
        public static readonly IList<XPathItem> Empty = System.Array.Empty<XPathItem>();

        public static IList<XPathItem> One(XPathItem item) => new XPathItem[] { item };

        // The caller applies function conversion/atomization before joining. Share
        // lexical conversion with the registry implementation, including exact integers.
        internal static string JoinAtomicValues(IList<XPathItem> items, string separator)
        {
            var values = items.Select(item => ((AtomicValue)item).Value);
            return separator.Length == 0 ? string.Concat(values) : string.Join(separator, values);
        }

        /// <summary>The typed value of an item (XPath 3.1 §2.5.2), for untyped trees.</summary>
        public static AtomicValue Atomize(XPathItem item)
        {
            switch (item)
            {
                case AtomicValue a:
                    return a;
                case ArrayItem array:
                {
                    // An array atomizes to its members' atomized values: here exactly one is expected.
                    IList<XPathItem> atoms = Atomize(Flatten(array));
                    return atoms.Count == 1 ? (AtomicValue)atoms[0] : throw Err.XPTY0004($"{array.Describe()} atomizes to {atoms.Count} values where one is expected");
                }
                case FunctionItem f:
                    throw new XdmException("FOTY0013", $"{f.Describe()} can't be atomized");
                case XPathNavigator n:
                    if (Xsl3.Schema.SchemaTypes.TypeOf(n) is { } type)
                    {
                        // Xsl3 (P6): a node a schema validated has the typed value of its type annotation.
                        IList<XPathItem> typed = Xsl3.Schema.SchemaTypes.TypedValue(n, type);
                        return typed.Count == 1 ? (AtomicValue)typed[0] : throw Err.XPTY0004($"{n.Name} atomizes to {typed.Count} values where one is expected");
                    }
                    // Without schema validation every node is untyped: comments, PIs and namespace nodes have
                    // xs:string typed values, everything else xs:untypedAtomic.
                    return UntypedNodeValue(n);
                default:
                    return FromLegacy(item);
            }
        }

        public static IList<XPathItem> Atomize(IList<XPathItem> seq)
        {
            bool alreadyAtomic = true, arrays = false;
            // Xsl3 (P4): by index, so that a node sequence isn't enumerated through an allocated enumerator.
            for (int i = 0; i < seq.Count; i++)
            {
                XPathItem item = seq[i];
                if (item is not AtomicValue)
                {
                    alreadyAtomic = false;
                    // (Xsl3, P6: a node of a list type atomizes to any number of values.)
                    arrays |= item is ArrayItem || (item is XPathNavigator n && Xsl3.Schema.SchemaTypes.TypeOf(n) != null);
                }
            }
            if (alreadyAtomic)
            {
                return seq;
            }
            if (arrays)
            {
                var flat = new List<XPathItem>(seq.Count);
                foreach (XPathItem item in seq)
                {
                    if (item is ArrayItem array)
                    {
                        flat.AddRange(Atomize(Flatten(array)));
                    }
                    else if (item is XPathNavigator n && Xsl3.Schema.SchemaTypes.TypeOf(n) is { } type)
                    {
                        flat.AddRange(Xsl3.Schema.SchemaTypes.TypedValue(n, type));
                    }
                    else
                    {
                        flat.Add(Atomize(item));
                    }
                }
                return flat;
            }
            var result = new XPathItem[seq.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Atomize(seq[i]);
            }
            return result;
        }

        private static AtomicValue UntypedNodeValue(XPathNavigator node) =>
            node.NodeType is XPathNodeType.Comment or XPathNodeType.ProcessingInstruction or XPathNodeType.Namespace
                ? AtomicValue.OfString(node.Value) : AtomicValue.OfUntyped(node.Value);

        /// <summary>Atomises a sequence, then requires at most one atomic value; null if empty, XPTY0004 if longer.</summary>
        public static AtomicValue? AtomizeOptional(IList<XPathItem> seq, string what)
        {
            if (seq.Count == 1)
            {
                if (seq[0] is XPathNavigator node)
                {
                    if (Xsl3.Schema.SchemaTypes.TypeOf(node) is not { } type) return UntypedNodeValue(node);
                    seq = Xsl3.Schema.SchemaTypes.TypedValue(node, type);
                }
                else if (seq[0] is ArrayItem array) seq = Atomize(Flatten(array));
                else return Atomize(seq[0]);
            }
            else seq = Atomize(seq);
            if (seq.Count == 0) return null;
            if (seq.Count > 1) throw Err.XPTY0004($"A sequence of more than one atomic value is not allowed as {what}");
            return (AtomicValue)seq[0];
        }

        /// <summary>
        /// Converts an atomic item produced by upstream code (System.Xml's XmlAtomicValue, used by the legacy
        /// engine and by QIL literals) to an <see cref="AtomicValue"/>.
        /// </summary>
        public static AtomicValue FromLegacy(XPathItem item)
        {
            System.Xml.Schema.XmlTypeCode code = item.XmlType?.TypeCode ?? System.Xml.Schema.XmlTypeCode.String;
            return code switch
            {
                System.Xml.Schema.XmlTypeCode.Boolean => AtomicValue.OfBoolean(item.ValueAsBoolean),
                System.Xml.Schema.XmlTypeCode.Double => AtomicValue.OfDouble(item.ValueAsDouble),
                System.Xml.Schema.XmlTypeCode.Float => AtomicValue.OfFloat((float)item.ValueAsDouble),
                System.Xml.Schema.XmlTypeCode.Decimal => AtomicValue.OfDecimal((decimal)item.ValueAs(typeof(decimal))),
                System.Xml.Schema.XmlTypeCode.Integer or System.Xml.Schema.XmlTypeCode.Long or System.Xml.Schema.XmlTypeCode.Int => AtomicValue.OfInteger(item.ValueAsLong),
                System.Xml.Schema.XmlTypeCode.UntypedAtomic => AtomicValue.OfUntyped(item.Value),
                _ => AtomicValue.OfString(item.Value),
            };
        }

        /// <summary>The effective boolean value (XPath 3.1 §2.4.3). FORG0006 for sequences it isn't defined for.</summary>
        public static bool EffectiveBooleanValue(IList<XPathItem> seq)
        {
            if (seq.Count == 0)
            {
                return false;
            }
            XPathItem first = seq[0];
            if (first.IsNode)
            {
                return true;
            }
            if (first is FunctionItem fn)
            {
                throw new XdmException("FORG0006", $"The effective boolean value of {fn.Describe()} is not defined");
            }
            if (seq.Count > 1)
            {
                throw new XdmException("FORG0006", "The effective boolean value of a sequence of two or more atomic values is not defined");
            }
            AtomicValue a = Atomize(first);
            return a switch
            {
                BooleanValue b => b.Boolean,
                StringValue s when a.Type.Primitive is Primitive.String or Primitive.UntypedAtomic or Primitive.AnyUri => s.String.Length != 0,
                IntegerValue i => !i.Integer.IsZero,
                DecimalValue d => d.Decimal != 0,
                DoubleValue d => d.Double != 0 && !double.IsNaN(d.Double),
                FloatValue f => f.Float != 0 && !float.IsNaN(f.Float),
                _ => throw new XdmException("FORG0006", $"The effective boolean value of a value of type {a.Type.Name} is not defined"),
            };
        }

        /// <summary>The string value of an item (fn:string); FOTY0014 for function items.</summary>
        public static string StringValue(XPathItem item) => item is AtomicValue a ? a.Value : item.Value;

        /// <summary>An array's members, concatenated (array:flatten one level deep, recursively for nested arrays).</summary>
        public static IList<XPathItem> Flatten(ArrayItem array)
        {
            var result = new List<XPathItem>();
            foreach (IList<XPathItem> member in array.Members)
            {
                foreach (XPathItem item in member)
                {
                    if (item is ArrayItem inner)
                    {
                        result.AddRange(Flatten(inner));
                    }
                    else
                    {
                        result.Add(item);
                    }
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Xsl3 (perf): a sequence the function conversion rules have converted to <see cref="Type"/>, read-only. Passed on
    /// to a parameter of the same declared type (a recursive call), it isn't checked again.
    /// </summary>
    internal sealed class CoercedSequence : IList<XPathItem>, IReadOnlyList<XPathItem>
    {
        /// <summary>Shorter sequences are cheaper to check again than to wrap.</summary>
        public const int MinimumCount = 8;

        private readonly IList<XPathItem> _items;

        public CoercedSequence(IList<XPathItem> items, SequenceTypeTest type)
        {
            _items = items is CoercedSequence c ? c._items : items;
            Type = type;
        }

        public SequenceTypeTest Type { get; }

        public XPathItem this[int index]
        {
            get => _items[index];
            set => throw new System.NotSupportedException();
        }

        public int Count => _items.Count;

        public bool IsReadOnly => true;

        public bool Contains(XPathItem item) => _items.Contains(item);

        public void CopyTo(XPathItem[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public IEnumerator<XPathItem> GetEnumerator() => _items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();

        public int IndexOf(XPathItem item) => _items.IndexOf(item);

        public void Add(XPathItem item) => throw new System.NotSupportedException();

        public void Clear() => throw new System.NotSupportedException();

        public void Insert(int index, XPathItem item) => throw new System.NotSupportedException();

        public bool Remove(XPathItem item) => throw new System.NotSupportedException();

        public void RemoveAt(int index) => throw new System.NotSupportedException();
    }
}
