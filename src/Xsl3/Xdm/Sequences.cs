// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;

namespace Xsl3.Xdm
{
    /// <summary>Sequence-level operations of XPath 3.1 §2.4: atomisation and effective boolean value.</summary>
    internal static class Sequences
    {
        public static readonly IList<XPathItem> Empty = System.Array.Empty<XPathItem>();

        public static IList<XPathItem> One(XPathItem item) => new XPathItem[] { item };

        /// <summary>The typed value of an item (XPath 3.1 §2.5.2), for untyped trees.</summary>
        public static AtomicValue Atomize(XPathItem item)
        {
            switch (item)
            {
                case AtomicValue a:
                    return a;
                case XPathNavigator n:
                    // Without schema validation every node is untyped: comments, PIs and namespace nodes have
                    // xs:string typed values, everything else xs:untypedAtomic.
                    return n.NodeType is XPathNodeType.Comment or XPathNodeType.ProcessingInstruction or XPathNodeType.Namespace
                        ? AtomicValue.OfString(n.Value)
                        : AtomicValue.OfUntyped(n.Value);
                default:
                    return FromLegacy(item);
            }
        }

        public static IList<XPathItem> Atomize(IList<XPathItem> seq)
        {
            bool alreadyAtomic = true;
            foreach (XPathItem item in seq)
            {
                if (item is not AtomicValue)
                {
                    alreadyAtomic = false;
                    break;
                }
            }
            if (alreadyAtomic)
            {
                return seq;
            }
            var result = new XPathItem[seq.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Atomize(seq[i]);
            }
            return result;
        }

        /// <summary>Atomises a sequence that must hold at most one item; null if empty, XPTY0004 if longer.</summary>
        public static AtomicValue? AtomizeOptional(IList<XPathItem> seq, string what)
        {
            if (seq.Count == 0)
            {
                return null;
            }
            if (seq.Count > 1)
            {
                throw Err.XPTY0004($"A sequence of more than one item is not allowed as {what}");
            }
            return Atomize(seq[0]);
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

        /// <summary>The string value of an item (fn:string).</summary>
        public static string StringValue(XPathItem item) => item is AtomicValue a ? a.Value : item.Value;
    }
}
