// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Xml.XPath;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>Xsl3: the static parts of an xsl:merge: per merge key its order and collation, per source sort-before-merge.</summary>
    internal sealed class MergeSpec
    {
        public List<bool> Descending { get; } = new();
        public List<string> Collations { get; } = new();
        public List<bool> SortBeforeMerge { get; } = new();
    }

    public sealed partial class XsltLibrary
    {
        public IList<XPathItem> X31FirstItem(IList<XPathItem> items) => items.Count == 0 ? Sequences.Empty : Sequences.One(items[0]);

        /// <summary>current-merge-group('name'): the group's items from the source at <paramref name="index"/>.</summary>
        public IList<XPathItem> X31MergeSourceGroup(IList<XPathItem> sources, double index) => ((XdmArray)sources[0])[(int)index];

        public IList<XPathItem> X31MergeSourceGroupNamed(IList<XPathItem> sources, string name, double namesId)
        {
            int index = System.Array.IndexOf((string?[])Constant((int)namesId), name.Trim());
            return index < 0 ? throw new XdmException("XTDE3490", $"There is no merge source named {name}") : X31MergeSourceGroup(sources, index);
        }

        /// <summary>
        /// xsl:merge (XSLT 3.0 §15): the items of every input of every source, in the order of their merge keys
        /// (inputs must already be in that order, XTDE2220, unless sort-before-merge), grouped by equal keys; the
        /// action is called for each group with the group, its key, the group's items by source, and the focus.
        /// </summary>
        public IList<XPathItem> X31Merge(IList<XPathItem> sources, IList<XPathItem> keyFunctions, IList<XPathItem> action, IList<XPathItem> orders, double specId)
        {
            var spec = (MergeSpec)Constant((int)specId);
            if (orders.Count > 0)
            {
                // The order attributes are attribute value templates (XTDE0030 for other values).
                spec = new MergeSpec();
                spec.Collations.AddRange(((MergeSpec)Constant((int)specId)).Collations);
                spec.SortBeforeMerge.AddRange(((MergeSpec)Constant((int)specId)).SortBeforeMerge);
                foreach (XPathItem order in orders)
                {
                    spec.Descending.Add(order.Value.Trim() switch
                    {
                        "ascending" => false,
                        "descending" => true,
                        var other => throw new XdmException("XTDE0030", $"order=\"{other}\" must be ascending or descending"),
                    });
                }
            }
            var entries = new List<(IList<XPathItem>[] Key, int Source, XPathItem Item)>();
            for (int s = 0; s < sources.Count; s++)
            {
                var keyFunction = (XdmFunction)keyFunctions[s];
                foreach (XPathItem wrappedInput in ((XdmArray)sources[s])[0])
                {
                    var input = new List<(IList<XPathItem>[] Key, int Source, XPathItem Item)>();
                    foreach (XPathItem item in ((XdmArray)wrappedInput)[0])
                    {
                        IList<XPathItem> wrappedKeys = CallFunction(keyFunction, Sequences.One(item));
                        var key = new IList<XPathItem>[wrappedKeys.Count];
                        for (int k = 0; k < key.Length; k++)
                        {
                            key[k] = Sequences.Atomize(((XdmArray)wrappedKeys[k])[0]);
                            if (key[k].Count > 1)
                            {
                                throw new XdmException("XTTE1020", $"A merge key has {key[k].Count} values; it must have at most one");
                            }
                        }
                        if (!spec.SortBeforeMerge[s] && input.Count > 0 && CompareMergeKeys(input[^1].Key, key, spec) > 0)
                        {
                            throw new XdmException("XTDE2220", "A merge input sequence isn't in the order of its merge keys");
                        }
                        input.Add((key, s, item is XPathNavigator n ? n.Clone() : item));
                    }
                    if (spec.SortBeforeMerge[s])
                    {
                        input = input.Select((e, i) => (e, i)).OrderBy(x => x.e.Key, Comparer<IList<XPathItem>[]>.Create((a, b) => CompareMergeKeys(a, b, spec)))
                            .ThenBy(x => x.i).Select(x => x.e).ToList();
                    }
                    entries.AddRange(input);
                }
            }
            // Stable: equal keys keep the order of their sources, inputs and items.
            List<(IList<XPathItem>[] Key, int Source, XPathItem Item)> merged = entries.Select((e, i) => (e, i))
                .OrderBy(x => x.e.Key, Comparer<IList<XPathItem>[]>.Create((a, b) => CompareMergeKeys(a, b, spec))).ThenBy(x => x.i).Select(x => x.e).ToList();

            var groups = new List<List<(IList<XPathItem>[] Key, int Source, XPathItem Item)>>();
            foreach (var entry in merged)
            {
                if (groups.Count > 0 && CompareMergeKeys(groups[^1][0].Key, entry.Key, spec) == 0)
                {
                    groups[^1].Add(entry);
                }
                else
                {
                    groups.Add(new() { entry });
                }
            }
            var actionFunction = (XdmFunction)action[0];
            var output = new List<XPathItem>();
            IList<XPathItem> size = Sequences.One(AtomicValue.OfInteger(groups.Count));
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                IList<XPathItem> items = group.Select(e => e.Item).ToList();
                IList<XPathItem> key = group[0].Key.SelectMany(k => k).ToList();
                var bySource = new List<IList<XPathItem>>();
                for (int s = 0; s < sources.Count; s++)
                {
                    int source = s;
                    bySource.Add(group.Where(e => e.Source == source).Select(e => e.Item).ToList());
                }
                output.AddRange(Copy(CallFunction(actionFunction, items, key, Sequences.One(XdmArray.Of(bySource)),
                    Sequences.One(AtomicValue.OfInteger(g + 1)), size)));
            }
            return output;
        }

        private int CompareMergeKeys(IList<XPathItem>[] x, IList<XPathItem>[] y, MergeSpec spec)
        {
            for (int k = 0; k < System.Math.Min(x.Length, y.Length); k++)
            {
                int r = CompareMergeKey(x[k], y[k], k < spec.Collations.Count ? spec.Collations[k] : Collation.CodepointUri);
                if (r != 0)
                {
                    return k < spec.Descending.Count && spec.Descending[k] ? -r : r;
                }
            }
            return 0;
        }

        // Empty sorts first, then NaN; other values are compared as by "lt" (XTDE2210 if they aren't comparable).
        private int CompareMergeKey(IList<XPathItem> x, IList<XPathItem> y, string collation)
        {
            if (x.Count == 0 || y.Count == 0)
            {
                return x.Count.CompareTo(y.Count);
            }
            var a = (AtomicValue)x[0];
            var b = (AtomicValue)y[0];
            if (a.Type.Primitive == Primitive.UntypedAtomic) a = Casting.Cast(a, AtomicTypes.String);
            if (b.Type.Primitive == Primitive.UntypedAtomic) b = Casting.Cast(b, AtomicTypes.String);
            bool aNaN = a is DoubleValue { Double: double.NaN } or FloatValue { Float: float.NaN };
            bool bNaN = b is DoubleValue { Double: double.NaN } or FloatValue { Float: float.NaN };
            if (aNaN || bNaN)
            {
                return aNaN && bNaN ? 0 : aNaN ? -1 : 1;
            }
            Collation c = Collation.Resolve(collation);
            int tz = X31Context.ImplicitTimezoneMinutes;
            try
            {
                if (Comparison.Compare(a, b, ValueComparisonOp.Lt, tz, c)) return -1;
                if (Comparison.Compare(b, a, ValueComparisonOp.Lt, tz, c)) return 1;
            }
            catch (XdmException e) when (e.Code == "XPTY0004")
            {
                throw new XdmException("XTDE2210", $"Merge keys {a.Value} and {b.Value} can't be compared");
            }
            return 0;
        }
    }
}
