// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Numerics;
using System.Xml.XPath;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;

namespace Xsl3.Runtime
{
    /// <summary>Xsl3: run-time support for XPath 3.1 maps and arrays: constructors and the lookup operator.</summary>
    public sealed partial class XsltLibrary
    {
        // A map or array under construction; the constructor calls are chained, so a builder is used once.
        private sealed class Builder : FunctionItem
        {
            public readonly Dictionary<MapKey, KeyValuePair<AtomicValue, IList<XPathItem>>> Entries = new();
            public readonly List<IList<XPathItem>> Members = new();

            public override int Arity => 0;
            public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
            public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
            public override string Describe() => "a map or array under construction";
        }

        public XPathItem X31MapNew() => new Builder();

        /// <summary>xsl:map-entry (XSLT 3.0 §21.2): a map with one entry; the key is one atomic value (XTTE3375).</summary>
        public XPathItem X31MapSingleton(IList<XPathItem> key, IList<XPathItem> value)
        {
            IList<XPathItem> atoms = Sequences.Atomize(key);
            if (atoms.Count != 1)
            {
                throw new XdmException("XPTY0004", $"The key of xsl:map-entry must be one atomic value, not {Coercion.Describe(atoms)}");
            }
            return MapItem.Empty.Put((AtomicValue)atoms[0], Copy(value));
        }

        /// <summary>xsl:map: the maps its content gives, merged; anything else is XTTE3375, a duplicate key XTDE3365.</summary>
        public XPathItem X31MapMerge(IList<XPathItem> maps)
        {
            MapItem result = MapItem.Empty;
            foreach (XPathItem item in maps)
            {
                if (item is not MapItem map)
                {
                    throw new XdmException("XTTE3375", $"The content of xsl:map must be maps, not {Coercion.Describe(Sequences.One(item))}");
                }
                foreach (var (key, value) in map.Entries)
                {
                    if (result.ContainsKey(key))
                    {
                        throw new XdmException("XTDE3365", $"xsl:map has two entries with the key {key.Value}");
                    }
                    result = result.Put(key, value);
                }
            }
            return result;
        }

        /// <summary>An entry of a map constructor: the key is a single atomic value (XPTY0004), not already present (XQDY0137).</summary>
        public XPathItem X31MapEntry(XPathItem builder, IList<XPathItem> key, IList<XPathItem> value)
        {
            var b = (Builder)builder;
            AtomicValue k = Sequences.AtomizeOptional(key, "a map key") ?? throw Err.XPTY0004("A map key can't be an empty sequence");
            if (!b.Entries.TryAdd(new MapKey(k), new(k, Copy(value))))
            {
                throw new XdmException("XQDY0137", $"The map constructor has the key '{k.Value}' twice");
            }
            return b;
        }

        public XPathItem X31MapDone(XPathItem builder) => MapItem.Of(((Builder)builder).Entries.Values);

        public XPathItem X31ArrayNew() => new Builder();

        public XPathItem X31ArrayMember(XPathItem builder, IList<XPathItem> member)
        {
            var b = (Builder)builder;
            b.Members.Add(Copy(member));
            return b;
        }

        public XPathItem X31ArrayDone(XPathItem builder) => ArrayItem.Of(((Builder)builder).Members);

        /// <summary><c>array { E }</c>: one member per item.</summary>
        public XPathItem X31ArrayOfItems(IList<XPathItem> items)
        {
            var members = new IList<XPathItem>[items.Count];
            for (int i = 0; i < members.Length; i++)
            {
                members[i] = new[] { Unshare(items[i]) };
            }
            return ArrayItem.Of(members);
        }

        /// <summary>
        /// The lookup operator (XPath 3.1 §3.11.3): for each map or array in <paramref name="bases"/>, the values of
        /// the keys (all of them for <c>?*</c>). Anything else is XPTY0004.
        /// </summary>
        public IList<XPathItem> X31Lookup(IList<XPathItem> bases, IList<XPathItem> keys, bool wildcard)
        {
            var result = new List<XPathItem>();
            IList<XPathItem> atoms = wildcard ? Sequences.Empty : Sequences.Atomize(keys);
            foreach (XPathItem item in bases)
            {
                switch (item)
                {
                    case MapItem map:
                        if (wildcard)
                        {
                            foreach (var e in map.Entries)
                            {
                                Add(result, e.Value);
                            }
                        }
                        else
                        {
                            foreach (XPathItem k in atoms)
                            {
                                if (map.Get((AtomicValue)k) is IList<XPathItem> v)
                                {
                                    Add(result, v);
                                }
                            }
                        }
                        break;
                    case ArrayItem array:
                        if (wildcard)
                        {
                            foreach (IList<XPathItem> m in array.Members)
                            {
                                Add(result, m);
                            }
                        }
                        else
                        {
                            foreach (XPathItem k in atoms)
                            {
                                if (k is not IntegerValue i)
                                {
                                    throw Err.XPTY0004($"An array is looked up with integers, not {((AtomicValue)k).Type.Name}");
                                }
                                Add(result, array.Get(i.Integer));
                            }
                        }
                        break;
                    default:
                        throw Err.XPTY0004($"The lookup operator applies to maps and arrays, not {FunctionDescription(item)}");
                }
            }
            return result;
        }

        private static void Add(List<XPathItem> result, IList<XPathItem> value)
        {
            for (int i = 0; i < value.Count; i++)
            {
                result.Add(value[i]);
            }
        }

        private static string FunctionDescription(XPathItem item) => item switch
        {
            FunctionItem f => f.Describe(),
            AtomicValue a => a.Type.Name.ToString(),
            _ => "a node",
        };

        // Values stored in maps and arrays outlive the loop that computed them: the loop's list and navigator may
        // be reused (XmlQueryItemSequence.CreateOrReuse, the iterator's navigator), so they are copied.
        private static IList<XPathItem> Copy(IList<XPathItem> value)
        {
            if (value.Count == 0)
            {
                return Sequences.Empty;
            }
            var copy = new XPathItem[value.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = Unshare(value[i]);
            }
            return copy;
        }

        private static XPathItem Unshare(XPathItem item) => item is XPathNavigator n ? n.Clone() : item;
    }
}
