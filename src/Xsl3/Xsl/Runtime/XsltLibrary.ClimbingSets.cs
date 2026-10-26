// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class ClimbingCell(int leaves)
    {
        internal readonly bool[] Leaves = new bool[leaves];
        internal bool Emitted, Queued;
    }

    private sealed class ClimbingSet : FunctionItem
    {
        internal readonly int[] Plan;
        internal readonly int Leaves;
        internal readonly bool[] Stack;
        internal readonly StreamedFrameFacts Frames = new();
        internal readonly Dictionary<(int Document, string Id), ClimbingCell> Grounded = new();
        internal DocumentOrderComparer? Documents;
        internal readonly List<(XPathNavigator Node, ClimbingCell Cell)> Pending = new();
        internal bool Ended;
        internal System.Runtime.ExceptionServices.ExceptionDispatchInfo? Fault;
        internal XPathNavigator? Previous;

        internal ClimbingSet(string plan)
        {
            string[] tokens = plan.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Plan = new int[tokens.Length]; Stack = new bool[tokens.Length];
            int depth = 0, leaves = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (token is "U" or "I")
                {
                    if (depth < 2) throw new ArgumentException("Invalid climbing-set operator stack", nameof(plan));
                    Plan[i] = token == "U" ? -1 : -2; depth--;
                }
                else
                {
                    if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int leaf) || leaf != leaves++)
                        throw new ArgumentException("Climbing-set leaves must be consecutive", nameof(plan));
                    Plan[i] = leaf; depth++;
                }
            }
            if (depth != 1) throw new ArgumentException("Invalid climbing-set plan", nameof(plan));
            Leaves = leaves;
        }

        internal bool Selected(ClimbingCell cell)
        {
            int depth = 0;
            foreach (int token in Plan)
            {
                if (token >= 0) Stack[depth++] = cell.Leaves[token];
                else
                {
                    bool right = Stack[--depth], left = Stack[--depth];
                    Stack[depth++] = token == -1 ? left || right : left && right;
                }
            }
            return Stack[0];
        }

        internal ClimbingCell Cell(XPathNavigator node)
        {
            if (node is StreamedNavigator source)
            {
                var cells = Frames.Get(source.PositionNode, static () => new Dictionary<HeaderSlot, ClimbingCell>());
                var key = new HeaderSlot(0, node.NodeType, node.LocalName, node.NamespaceURI);
                if (!cells.TryGetValue(key, out var cell)) cells.Add(key, cell = new(Leaves));
                return cell;
            }
            var id = ((Documents ??= new()).GetDocumentIndex(node), node.UniqueId);
            if (!Grounded.TryGetValue(id, out var result)) Grounded.Add(id, result = new(Leaves));
            return result;
        }

        internal void Complete() { Pending.Clear(); Grounded.Clear(); Documents = null; Previous = null; Frames.Dispose(); Ended = true; }
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "monotonic climbing-set membership";
    }

    public XPathItem X31ClimbingSetNew(string plan) => new ClimbingSet(plan);

    public bool X31ClimbingSetAdd(XPathItem state, double leaf, IList<XPathNavigator> nodes)
    {
        var set = (ClimbingSet)state;
        set.Fault?.Throw();
        try
        {
            XmlQueryRuntime.CheckCancellation();
            if (set.Ended) throw new InvalidOperationException("The climbing set is complete");
            if (leaf < 0 || leaf >= set.Leaves || leaf != Math.Truncate(leaf)) throw new ArgumentOutOfRangeException(nameof(leaf));
            foreach (XPathNavigator node in nodes)
            {
                ClimbingCell cell = set.Cell(node);
                if (cell.Emitted) continue;
                cell.Leaves[(int)leaf] = true;
                if (!cell.Queued) { cell.Queued = true; set.Pending.Add((node.Clone(), cell)); }
            }
            return true;
        }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        {
            set.Complete(); set.Fault = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); throw;
        }
    }

    public IList<XPathNavigator> X31ClimbingSetDrain(XPathItem state)
    {
        var set = (ClimbingSet)state;
        set.Fault?.Throw();
        try
        {
            XmlQueryRuntime.CheckCancellation();
            if (set.Ended) return Array.Empty<XPathNavigator>();
            List<XPathNavigator>? result = null;
            foreach (var pending in set.Pending)
            {
                pending.Cell.Queued = false;
                if (!pending.Cell.Emitted && set.Selected(pending.Cell))
                {
                    pending.Cell.Emitted = true; (result ??= new()).Add(pending.Node);
                }
            }
            set.Pending.Clear();
            if (result == null) return Array.Empty<XPathNavigator>();
            result.Sort(_runtime.ComparePosition);
            if (set.Previous != null && _runtime.ComparePosition(set.Previous, result[0]) >= 0)
                throw new InvalidOperationException("A climbing-set projection changed document order");
            set.Previous = result[^1].Clone();
            return result;
        }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        {
            set.Complete(); set.Fault = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); throw;
        }
    }

    public IList<XPathNavigator> X31ClimbingSetEnd(XPathItem state)
    {
        ((ClimbingSet)state).Complete();
        return Array.Empty<XPathNavigator>();
    }
}
