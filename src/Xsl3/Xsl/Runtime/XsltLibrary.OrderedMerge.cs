// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class NodeMergeInput(XsltLibrary owner, FunctionItem producer, bool ordered, string nodeError = "XPTY0019", bool groundedSort = false) : FunctionItem
    {
        private XsltLibrary? _owner = owner;
        private readonly PullState _source = new(owner, producer);
        private List<XPathNavigator>? _buffer;
        private int _position;
        private bool _prepared;
        internal XPathNavigator? Head;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "an ordered node population";

        internal void Advance()
        {
            XmlQueryRuntime.CheckCancellation();
            if (_owner == null) return;
            if (!ordered)
            {
                if (!_prepared)
                {
                    _prepared = true;
                    _buffer = new();
                    while (Read() is { } node)
                    {
                        if (groundedSort && node is Streaming.StreamedNavigator { Replayable: false } streamed
                            && streamed.NodeType is not (XPathNodeType.Attribute or XPathNodeType.Namespace))
                        {
                            // Validate remaining item kinds without retaining live positions.
                            // A late non-node is still XPTY0004, even when sorting is unproved.
                            _buffer.Clear();
                            while (Read() != null) { }
                            throw new XdmException("streaming-unimplemented", "An unproved live node population cannot be sorted as grounded input")
                                { Name = new QName(Streaming.StreamedDocument.ErrorNamespace, "streaming-unimplemented") };
                        }
                        _buffer.Add(node.Clone());
                    }
                    _buffer.Sort(_owner!._runtime.ComparePosition);
                }
                Head = _buffer != null && _position < _buffer.Count ? _buffer[_position++] : null;
                if (Head == null) Release();
                return;
            }
            XPathNavigator? previous = Head;
            Head = Read();
            if (Head != null && previous != null && _owner!._runtime.ComparePosition(previous, Head) > 0)
                throw new InvalidOperationException("An ordered node population changed document order");
            if (Head == null) Release();
        }

        private XPathNavigator? Read()
        {
            XmlQueryRuntime.CheckCancellation();
            IList<XPathItem> next = _owner!.X31PullNext(_source);
            if (next.Count > 1) throw new InvalidOperationException("A node producer must yield at most one item");
            return next.Count == 0 ? null : next[0] as XPathNavigator
                ?? throw new XdmException(nodeError, "A node population requires nodes");
        }

        internal void Release()
        {
            Head = null;
            _buffer = null;
            _owner = null;
            _source.Complete();
        }
    }

    private sealed class OrderedNodeMerge(IList<XPathItem> inputs) : FunctionItem
    {
        internal readonly NodeMergeInput[] Inputs = Copy(inputs);
        internal NodeMergeInput? Pending;
        internal XPathNavigator? Previous;
        internal bool Started;
        internal bool Ended;
        internal ExceptionDispatchInfo? Fault;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "an ordered distinct node merge";

        private static NodeMergeInput[] Copy(IList<XPathItem> inputs)
        {
            var result = new NodeMergeInput[inputs.Count];
            for (int i = 0; i < result.Length; i++) result[i] = (NodeMergeInput)inputs[i];
            return result;
        }

        internal void Complete()
        {
            foreach (NodeMergeInput input in Inputs) input.Release();
            Pending = null;
            Previous = null;
            Ended = true;
        }
    }

    public XPathItem X31NodeMergeInputNew(XPathItem producer, bool ordered) => new NodeMergeInput(this, (FunctionItem)producer, ordered);
    public XPathItem X31NodeMergeNew(IList<XPathItem> inputs) => new OrderedNodeMerge(inputs);

    public IList<XPathNavigator> X31NodeMergeNext(XPathItem state)
    {
        XmlQueryRuntime.CheckCancellation();
        var merge = (OrderedNodeMerge)state;
        merge.Fault?.Throw();
        if (merge.Ended) return Array.Empty<XPathNavigator>();
        try { return NextOrderedNode(merge); }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        {
            merge.Complete();
            merge.Fault = ExceptionDispatchInfo.Capture(error);
            throw;
        }
    }

    private IList<XPathNavigator> NextOrderedNode(OrderedNodeMerge merge)
    {
        if (!merge.Started)
        {
            merge.Started = true;
            foreach (NodeMergeInput input in merge.Inputs) input.Advance();
        }
        if (merge.Pending != null)
        {
            merge.Pending.Advance();
            merge.Pending = null;
        }
        while (true)
        {
            NodeMergeInput? first = null;
            foreach (NodeMergeInput input in merge.Inputs)
                if (input.Head != null && (first == null || _runtime.ComparePosition(input.Head, first.Head!) < 0)) first = input;
            if (first == null)
            {
                merge.Complete();
                return Array.Empty<XPathNavigator>();
            }
            if (merge.Previous != null && _runtime.ComparePosition(merge.Previous, first.Head!) == 0)
            {
                first.Advance();
                continue;
            }
            merge.Previous = first.Head!.Clone();
            merge.Pending = first;
            return new[] { first.Head.Clone() };
        }
    }
}
