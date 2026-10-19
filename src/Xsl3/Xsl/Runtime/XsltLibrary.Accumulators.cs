// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml;
using System.Xml.XPath;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Xsl3: where an accumulator's compiled functions are among the accumulator items: the initial value's at
    /// <paramref name="Offset"/>, then a pattern and a value function per rule (most preferred first), whose
    /// phases (end: true) are in <paramref name="EndPhases"/>.
    /// </summary>
    internal sealed record AccumulatorInfo(int Offset, bool[] EndPhases, SequenceTypeTest? Type);

    public sealed partial class XsltLibrary
    {
        // The values of each accumulator at each node of the trees seen so far, by accumulator and root.
        private Dictionary<(string Accumulator, string Root), Dictionary<string, (IList<XPathItem> Before, IList<XPathItem> After)>>? _accumulatorValues;

        /// <summary>
        /// accumulator-before and accumulator-after (XSLT 3.0 §18.2.4): the accumulator's value before or after the
        /// context node, computed for the node's whole tree the first time it is asked for.
        /// </summary>
        public IList<XPathItem> X31AccumulatorValue(IList<XPathItem> items, double tableId, string name, double nameContext, IList<XPathItem> context, bool after)
        {
            var (ns, local) = NameContext(nameContext).Resolve(name, "", "XTDE3340");
            string key = new QName(ns, local).EQName;
            var table = (Dictionary<string, AccumulatorInfo>)ConstantPool.Get((int)tableId);
            if (!table.TryGetValue(key, out AccumulatorInfo? info))
            {
                throw new XdmException("XTDE3340", $"There is no accumulator named {name}");
            }
            if (context.Count != 1 || context[0] is not XPathNavigator node)
            {
                throw new XdmException("XTTE3360", $"{(after ? "accumulator-after" : "accumulator-before")}() needs a node as the context item");
            }
            if (node.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace)
            {
                throw new XdmException("XTTE3360", $"Accumulators have no value at {node.NodeType.ToString().ToLowerInvariant()} nodes");
            }
            XPathNavigator root = node.Clone();
            root.MoveToRoot();
            _accumulatorValues ??= new();
            if (!_accumulatorValues.TryGetValue((key, _runtime.GenerateId(root)), out var values))
            {
                values = new Dictionary<string, (IList<XPathItem>, IList<XPathItem>)>();
                IList<XPathItem> initial = AccumulatorCoerce(info, CallFunction((XdmFunction)items[info.Offset], Sequences.One(root.Clone())), name);
                AccumulatorVisit(root, initial, items, info, name, values);
                _accumulatorValues[(key, _runtime.GenerateId(root))] = values;
            }
            var (before, afterValue) = values[_runtime.GenerateId(node)];
            return after ? afterValue : before;
        }

        // Visits a node and its descendants in document order: the start rule, the children, the end rule.
        private IList<XPathItem> AccumulatorVisit(XPathNavigator node, IList<XPathItem> value, IList<XPathItem> items, AccumulatorInfo info, string name,
            Dictionary<string, (IList<XPathItem>, IList<XPathItem>)> values)
        {
            IList<XPathItem> before = AccumulatorApply(node, value, end: false, items, info, name);
            IList<XPathItem> current = before;
            XPathNavigator child = node.Clone();
            if (child.MoveToFirstChild())
            {
                do
                {
                    current = AccumulatorVisit(child, current, items, info, name, values);
                }
                while (child.MoveToNext());
            }
            IList<XPathItem> afterValue = AccumulatorApply(node, current, end: true, items, info, name);
            values[_runtime.GenerateId(node)] = (before, afterValue);
            return afterValue;
        }

        // The rule of the phase that matches the node with the highest priority (the first of equal ones), applied.
        private IList<XPathItem> AccumulatorApply(XPathNavigator node, IList<XPathItem> value, bool end, IList<XPathItem> items, AccumulatorInfo info, string name)
        {
            int best = -1;
            double bestPriority = double.NegativeInfinity;
            for (int r = 0; r < info.EndPhases.Length; r++)
            {
                if (info.EndPhases[r] != end)
                {
                    continue;
                }
                IList<XPathItem> priority = CallFunction((XdmFunction)items[info.Offset + 1 + 2 * r], Sequences.One(node.Clone()));
                if (priority.Count == 1)
                {
                    double p = XmlConvert.ToDouble(priority[0].Value);
                    if (best < 0 || p > bestPriority)
                    {
                        best = r;
                        bestPriority = p;
                    }
                }
            }
            if (best < 0)
            {
                return value;
            }
            return AccumulatorCoerce(info, CallFunction((XdmFunction)items[info.Offset + 2 + 2 * best], Sequences.One(node.Clone()), value), name);
        }

        private static IList<XPathItem> AccumulatorCoerce(AccumulatorInfo info, IList<XPathItem> value, string name) =>
            info.Type == null ? Copy(value) : Coercion.CoerceTo(Copy(value), info.Type, "XTTE3400", () => $"the value of accumulator {name}");
    }
}
