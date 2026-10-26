// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private static class EmptyNodeSet
    {
        internal static readonly FunctionItem Producer = new NativeFunction(new[] { SequenceTypeTest.AnySequence },
            SequenceTypeTest.AnySequence, _ => Sequences.Empty, "an empty node population");
    }

    private sealed class NodeSet(NodeMergeInput left, NodeMergeInput right, int operation, bool leftGrounded, bool rightGrounded) : FunctionItem
    {
        internal readonly NodeMergeInput Left = left, Right = right;
        internal readonly int Operation = operation;
        internal readonly bool LeftGrounded = leftGrounded, RightGrounded = rightGrounded;
        internal bool Started, Ended, AdvanceLeft, AdvanceRight;
        internal XPathNavigator? Previous;
        internal ExceptionDispatchInfo? Fault;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a forward node set operation";
        internal void Complete()
        {
            Left.Release(); Right.Release(); Previous = null;
            AdvanceLeft = AdvanceRight = false;
            Ended = true;
        }
    }

    public XPathItem X31NodeSetInputNew(IList<XPathItem> producer, bool ordered)
    {
        XmlQueryRuntime.CheckCancellation();
        FunctionItem function = producer.Count switch {
            0 => EmptyNodeSet.Producer,
            1 when producer[0] is FunctionItem value => value,
            _ => throw new InvalidOperationException("A node population requires one producer or an empty sequence"),
        };
        return new NodeMergeInput(this, function, ordered, "XPTY0004", groundedSort: true);
    }

    public XPathItem X31NodeSetNew(XPathItem left, XPathItem right, double operation, bool leftGrounded, bool rightGrounded)
    {
        XmlQueryRuntime.CheckCancellation();
        if (operation < 0 || operation > 2 || operation != Math.Truncate(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        if (!leftGrounded && !rightGrounded) throw new InvalidOperationException("Independent set populations require a grounded operand");
        return new NodeSet((NodeMergeInput)left, (NodeMergeInput)right, (int)operation, leftGrounded, rightGrounded);
    }

    public IList<XPathNavigator> X31NodeSetNext(XPathItem state)
    {
        var set = (NodeSet)state;
        set.Fault?.Throw();
        try
        {
            XmlQueryRuntime.CheckCancellation();
            return set.Ended ? Array.Empty<XPathNavigator>() : NextNodeSet(set);
        }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        {
            set.Complete(); set.Fault = ExceptionDispatchInfo.Capture(error); throw;
        }
    }

    private IList<XPathNavigator> NextNodeSet(NodeSet set)
    {
        if (!set.Started)
        {
            set.Started = true;
            // A grounded argument is validated and sorted before another iterator can
            // advance a forward source. No live population acquires a grounding buffer.
            if (set.LeftGrounded) set.Left.Advance();
            if (set.RightGrounded) set.Right.Advance();
            if (!set.LeftGrounded) set.Left.Advance();
            if (!set.RightGrounded) set.Right.Advance();
        }
        while (true)
        {
            if (set.AdvanceLeft) { set.AdvanceLeft = false; set.Left.Advance(); }
            if (set.AdvanceRight) { set.AdvanceRight = false; set.Right.Advance(); }
            XPathNavigator? left = set.Left.Head, right = set.Right.Head, selected;
            if (set.Operation == 0)
            {
                if (left == null && right == null) { set.Complete(); return Array.Empty<XPathNavigator>(); }
                int order = left == null ? 1 : right == null ? -1 : _runtime.ComparePosition(left, right);
                selected = order <= 0 ? left : right;
                set.AdvanceLeft = order <= 0;
                set.AdvanceRight = order >= 0;
            }
            else
            {
                if (left == null)
                {
                    // Complete evaluation still validates the other argument, including
                    // an invalid late item in an otherwise empty intersection.
                    while (set.Right.Head != null) set.Right.Advance();
                    set.Complete(); return Array.Empty<XPathNavigator>();
                }
                int order = right == null ? -1 : _runtime.ComparePosition(left, right);
                if (order > 0) { set.AdvanceRight = true; continue; }
                set.AdvanceLeft = true;
                set.AdvanceRight = order == 0;
                if ((order == 0) != (set.Operation == 1)) continue;
                selected = left;
            }
            if (set.Previous != null && _runtime.ComparePosition(set.Previous, selected!) == 0) continue;
            set.Previous = selected!.Clone();
            return new[] { selected.Clone() };
        }
    }
}
