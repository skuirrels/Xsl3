// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class OutermostState(XsltLibrary owner, FunctionItem producer) : FunctionItem
    {
        internal readonly PullState Source = new(owner, producer);
        internal readonly Streaming.DescendantPruning Pruning = new();
        internal XPathNavigator? Previous;
        internal bool Ended;
        internal System.Runtime.ExceptionServices.ExceptionDispatchInfo? Fault;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "an ordered outermost node filter";
        internal void Complete() { Source.Complete(); Previous = null; Ended = true; }
    }

    public XPathItem X31OutermostNew(XPathItem producer) => new OutermostState(this, (FunctionItem)producer);

    public IList<XPathNavigator> X31OutermostNext(XPathItem state)
    {
        var filter = (OutermostState)state;
        filter.Fault?.Throw();
        try
        {
            XmlQueryRuntime.CheckCancellation();
            if (filter.Ended) return Array.Empty<XPathNavigator>();
            while (true)
            {
                using var scope = filter.Pruning.Enter();
                IList<XPathItem> next = X31PullNext(filter.Source);
                if (next.Count == 0) { filter.Complete(); return Array.Empty<XPathNavigator>(); }
                if (next.Count != 1) throw new InvalidOperationException("An outermost producer must yield at most one item");
                XPathNavigator node = next[0] as XPathNavigator ?? throw Err.XPTY0004("outermost() requires nodes");
                if (filter.Previous is { } previous)
                {
                    if (_runtime.ComparePosition(previous, node) > 0) throw new InvalidOperationException("An ordered outermost population changed document order");
                    if (previous.IsSamePosition(node) || previous.IsDescendant(node)) continue;
                }
                filter.Pruning.Prune(node);
                filter.Previous = node.Clone();
                return new[] { node.Clone() };
            }
        }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        {
            filter.Complete(); filter.Fault = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); throw;
        }
    }

    private sealed class OutermostChildCursor : FunctionItem
    {
        internal DescendantIterator Iterator;
        internal OutermostChildCursor(XPathNavigator root, XmlNavigatorFilter filter) => Iterator.CreateTopmost(root, filter, orSelf: false);
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "an outermost descendant child-path cursor";
    }

    // The complete selection must be tested inside the pruning iterator. A separate parent
    // predicate after pruning would incorrectly hide matching descendants of rejected candidates.
    private sealed class ChildPathFilter(string parentLocal, string parentNamespace, string childLocal, string childNamespace) : XmlNavigatorFilter
    {
        private readonly XmlNavigatorFilter _child = XmlNavNameFilter.Create(childLocal, childNamespace);

        public override bool IsFiltered(XPathNavigator navigator)
        {
            if (_child.IsFiltered(navigator)) return true;
            XPathNavigator parent = navigator.Clone();
            return !parent.MoveToParent() || parent.NodeType != XPathNodeType.Element
                || parent.LocalName != parentLocal || parent.NamespaceURI != parentNamespace;
        }

        public override bool MoveToContent(XPathNavigator navigator) => _child.MoveToContent(navigator)
            && (!IsFiltered(navigator) || MoveToNextContent(navigator));

        public override bool MoveToNextContent(XPathNavigator navigator)
        {
            while (_child.MoveToNextContent(navigator)) if (!IsFiltered(navigator)) return true;
            return false;
        }

        public override bool MoveToFollowingSibling(XPathNavigator navigator) => MoveToNextContent(navigator);

        public override bool MoveToPreviousSibling(XPathNavigator navigator)
        {
            while (_child.MoveToPreviousSibling(navigator)) if (!IsFiltered(navigator)) return true;
            return false;
        }

        public override bool MoveToFollowing(XPathNavigator navigator, XPathNavigator? navigatorEnd)
        {
            while (_child.MoveToFollowing(navigator, navigatorEnd)) if (!IsFiltered(navigator)) return true;
            return false;
        }
    }

    public XPathItem X31OutermostChildNew(XPathNavigator root, string parentLocal, string parentNamespace, string childLocal, string childNamespace) =>
        new OutermostChildCursor(root, new ChildPathFilter(parentLocal, parentNamespace, childLocal, childNamespace));

    public IList<XPathNavigator> X31OutermostChildNext(XPathItem state)
    {
        var cursor = (OutermostChildCursor)state;
        return cursor.Iterator.MoveNext() ? new[] { cursor.Iterator.Current.Clone() } : Array.Empty<XPathNavigator>();
    }
}
