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
        private sealed class MergeEntry(IList<XPathItem>[] key, IList<XPathItem>[] rawKey, int source, XPathItem item) : FunctionItem
        {
            internal readonly IList<XPathItem>[] Key = key, RawKey = rawKey;
            internal readonly int Source = source;
            internal readonly XPathItem Item = item;
            public override int Arity => 0;
            public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
            public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
            public override string Describe() => "a merge row and its keys";
        }
        public IList<XPathItem> X31FirstItem(IList<XPathItem> items) => items.Count == 0 ? Sequences.Empty : Sequences.One(items[0]);

        /// <summary>current-merge-group('name'): the group's items from the source at <paramref name="index"/>.</summary>
        public IList<XPathItem> X31MergeSourceGroup(IList<XPathItem> sources, double index) => ((ArrayItem)sources[0])[(int)index];

        public IList<XPathItem> X31MergeSourceGroupNamed(IList<XPathItem> sources, string name, double namesId)
        {
            int index = System.Array.IndexOf((string?[])Constant((int)namesId), name);
            return index < 0 ? throw new XdmException("XTDE3490", $"There is no merge source named {name}") : X31MergeSourceGroup(sources, index);
        }

        /// <summary>
        /// xsl:merge (XSLT 3.0 §15): the items of every input of every source, in the order of their merge keys
        /// (inputs must already be in that order, XTDE2220, unless sort-before-merge), grouped by equal keys; the
        /// action is called for each group with the group, its key, the group's items by source, and the focus.
        /// </summary>
        public IList<XPathItem> X31Merge(IList<XPathItem> sources, IList<XPathItem> keyFunctions, IList<XPathItem> action, IList<XPathItem> orders, double specId)
            => MergeCore(sources, keyFunctions, action, orders, (MergeSpec)Constant((int)specId), null);

        public IList<XPathItem> X31MergeConfigured(IList<XPathItem> sources, IList<XPathItem> keyFunctions, IList<XPathItem> action, XPathItem plan)
            => MergeCore(sources, keyFunctions, action, Sequences.Empty, ((MergePlan)plan).Spec, (MergePlan)plan);

        private IList<XPathItem> MergeCore(IList<XPathItem> sources, IList<XPathItem> keyFunctions, IList<XPathItem> action,
            IList<XPathItem> orders, MergeSpec spec, MergePlan? plan)
        {
            var groups = PrepareMergeGroups(sources, keyFunctions, orders, spec, plan);
            var actionFunction = (FunctionItem)action[0];
            var output = new List<XPathItem>();
            IList<XPathItem> size = Sequences.One(AtomicValue.OfInteger(groups.Count));
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                IList<XPathItem> items = group.Select(e => e.Item).ToList();
                IList<XPathItem> key = group[0].RawKey.SelectMany(k => k).ToList();
                var bySource = new List<IList<XPathItem>>();
                for (int s = 0; s < sources.Count; s++)
                {
                    int source = s;
                    bySource.Add(group.Where(e => e.Source == source).Select(e => e.Item).ToList());
                }
                output.AddRange(Copy(CallFunction(actionFunction, items, key, Sequences.One(ArrayItem.Of(bySource)),
                    Sequences.One(AtomicValue.OfInteger(g + 1)), size)));
            }
            return output;
        }

        private List<List<MergeEntry>> PrepareMergeGroups(IList<XPathItem> sources, IList<XPathItem> keyFunctions,
            IList<XPathItem> orders, MergeSpec spec, MergePlan? plan)
        {
            if (orders.Count > 0)
            {
                // The order attributes are attribute value templates (XTDE0030 for other values).
                var effective = new MergeSpec();
                effective.Collations.AddRange(spec.Collations);
                effective.SortBeforeMerge.AddRange(spec.SortBeforeMerge);
                spec = effective;
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
            var entries = new List<MergeEntry>();
            for (int s = 0; s < sources.Count; s++)
            {
                var keyFunction = (FunctionItem)keyFunctions[s];
                foreach (XPathItem wrappedInput in ((ArrayItem)sources[s])[0])
                {
                    var input = new List<MergeEntry>();
                    foreach (XPathItem item in ((ArrayItem)wrappedInput)[0])
                    {
                        XmlQueryRuntime.CheckCancellation();
                        IList<XPathItem> wrappedKeys = CallFunction(keyFunction, Sequences.One(item));
                        IList<XPathItem>[] raw = MergeRawKeys(wrappedKeys);
                        IList<XPathItem>[] key = MergeSortKeys(raw, plan);
                        if (!spec.SortBeforeMerge[s] && input.Count > 0 && CompareMergeInputKeys(input[^1].Key, key, spec) > 0)
                        {
                            throw new XdmException("XTDE2220", "A merge input sequence isn't in the order of its merge keys");
                        }
                        input.Add(new(key, raw, s, item is XPathNavigator n ? n.Clone() : item));
                    }
                    if (spec.SortBeforeMerge[s])
                    {
                        input = SortMergeInput(input, spec);
                    }
                    entries.AddRange(input);
                }
            }
            // Stable: equal keys keep the order of their sources, inputs and items.
            List<MergeEntry> merged;
            try
            {
                merged = entries.OrderBy(entry => entry.Key,
                    Comparer<IList<XPathItem>[]>.Create((a, b) => CompareMergeKeys(a, b, spec))).ToList();
            }
            catch (System.InvalidOperationException error) when (error.InnerException is XdmException or System.OperationCanceledException) { throw error.InnerException!; }

            var groups = new List<List<MergeEntry>>();
            foreach (var entry in merged)
            {
                XmlQueryRuntime.CheckCancellation();
                if (groups.Count > 0 && CompareMergeKeys(groups[^1][0].Key, entry.Key, spec) == 0)
                {
                    groups[^1].Add(entry);
                }
                else
                {
                    groups.Add(new() { entry });
                }
            }
            return groups;
        }

        private List<MergeEntry> SortMergeInput(List<MergeEntry> input, MergeSpec spec)
        {
            try
            {
                // OrderBy preserves the original order of equal keys.
                return input.OrderBy(entry => entry.Key,
                    Comparer<IList<XPathItem>[]>.Create((a, b) => CompareMergeInputKeys(a, b, spec))).ToList();
            }
            catch (System.InvalidOperationException error) when (error.InnerException is XdmException or System.OperationCanceledException) { throw error.InnerException!; }
        }

        private int CompareMergeInputKeys(IList<XPathItem>[] x, IList<XPathItem>[] y, MergeSpec spec)
        {
            try { return CompareMergeKeys(x, y, spec); }
            catch (XdmException error) when (error.Code == "XTTE2230") { throw new XdmException("XTDE1030", error.Message); }
        }

        private int CompareMergeKeys(IList<XPathItem>[] x, IList<XPathItem>[] y, MergeSpec spec)
        {
            XmlQueryRuntime.CheckCancellation();
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

        // Empty sorts first, then NaN; incomparable merge inputs raise XTTE2230 (§15.5).
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
            if ((aNaN || bNaN) && a.Type.IsNumeric && b.Type.IsNumeric)
            {
                return aNaN && bNaN ? 0 : aNaN ? -1 : 1;
            }
            Collation c = Collation.Resolve(collation);
            ImplicitTimezone tz = X31Context.Timezone;
            try
            {
                if (Comparison.Compare(a, b, ValueComparisonOp.Lt, tz, c)) return -1;
                if (Comparison.Compare(b, a, ValueComparisonOp.Lt, tz, c)) return 1;
            }
            catch (XdmException e) when (e.Code == "XPTY0004")
            {
                throw new XdmException("XTTE2230", $"Merge keys {a.Value} and {b.Value} can't be compared");
            }
            return 0;
        }
    }
}
