// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class DeferredClimbingFrame(StreamedDocument.Node node)
    {
        internal readonly StreamedDocument.Node Node = node;
        internal StreamedHeaderSpool.Owner? Owner;
        internal readonly Dictionary<HeaderSlot, StreamedHeaderSpool.Cell> Cells = new();
    }

    private sealed class DeferredClimbingQueue(StreamedDocument document)
    {
        internal readonly StreamedDocument Document = document;
        internal readonly List<DeferredClimbingFrame> Open = new();
        internal StreamedHeaderSpool.Cell? Head, Tail;
        internal XPathNavigator? Ready;
    }

    private sealed class DeferredGroundedCell(XPathNavigator node, int leaves)
    {
        internal readonly XPathNavigator Node = node.Clone();
        internal readonly bool[] Leaves = new bool[leaves];
    }

    private sealed class DeferredClimbingSet : FunctionItem
    {
        internal readonly int[] Plan;
        internal readonly bool[] Membership;
        internal readonly byte[] Stack;
        internal readonly bool Early;
        internal readonly StreamedFrameFacts Frames = new();
        internal readonly Dictionary<StreamedDocument, DeferredClimbingQueue> Queues = new();
        internal readonly Dictionary<(int Document, string Id), DeferredGroundedCell> Grounded = new();
        internal DocumentOrderComparer? Documents;
        internal List<DeferredGroundedCell>? OrderedGrounded;
        internal int GroundedPosition;
        internal bool Captured, Ended;
        internal ExceptionDispatchInfo? Fault;
        internal XPathNavigator? Previous;

        internal DeferredClimbingSet(string plan, bool early)
        {
            string[] tokens = plan.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Plan = new int[tokens.Length]; Stack = new byte[tokens.Length];
            int depth = 0, leaves = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (token is "U" or "I" or "E")
                {
                    if (depth < 2) throw new ArgumentException("Invalid deferred climbing-set operator stack", nameof(plan));
                    Plan[i] = token == "U" ? -1 : token == "I" ? -2 : -3; depth--;
                }
                else
                {
                    if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int leaf) || leaf != leaves++)
                        throw new ArgumentException("Deferred climbing-set leaves must be consecutive", nameof(plan));
                    Plan[i] = leaf; depth++;
                }
            }
            if (depth != 1) throw new ArgumentException("Invalid deferred climbing-set plan", nameof(plan));
            Membership = new bool[leaves]; Early = early;
        }

        // Bit 0 means false is possible, bit 1 means true is possible. Seen leaf
        // memberships are permanent; an unseen leaf remains unknown until sealed.
        internal byte Selected(ReadOnlySpan<bool> leaves, bool final)
        {
            int depth = 0;
            foreach (int token in Plan)
            {
                if (token >= 0) { Stack[depth++] = leaves[token] ? (byte)2 : final ? (byte)1 : (byte)3; continue; }
                byte right = Stack[--depth], left = Stack[--depth];
                bool lf = (left & 1) != 0, lt = (left & 2) != 0, rf = (right & 1) != 0, rt = (right & 2) != 0;
                bool canFalse = token == -1 ? lf && rf : token == -2 ? lf || rf : lf || rt;
                bool canTrue = token == -1 ? lt || rt : token == -2 ? lt && rt : lt && rf;
                Stack[depth++] = (byte)((canFalse ? 1 : 0) | (canTrue ? 2 : 0));
            }
            return Stack[0];
        }

        internal void Complete()
        {
            foreach (var queue in Queues.Values) { queue.Open.Clear(); queue.Ready = null; }
            Queues.Clear(); Grounded.Clear(); OrderedGrounded = null; Documents = null; Previous = null;
            Frames.Dispose(); Ended = true;
        }
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "deferred climbing-set membership";
    }

    public XPathItem X31DeferredClimbingSetNew(string plan, bool early) => new DeferredClimbingSet(plan, early);

    private static HeaderSlot DeferredSlot(XPathNavigator node) => new(0, node.NodeType, node.LocalName, node.NamespaceURI);

    private static void CloseDeferredFrames(DeferredClimbingQueue queue, int remaining)
    {
        for (int i = queue.Open.Count - 1; i >= remaining; i--)
        {
            var frame = queue.Open[i];
            if (frame.Owner is { } owner)
            {
                queue.Document.HeaderSpool.UpdateOwner(owner, frame.Node);
                queue.Document.HeaderSpool.SealOwner(owner);
            }
            queue.Open.RemoveAt(i);
        }
    }

    private static StreamedHeaderSpool.Owner DeferredOwner(DeferredClimbingSet set, DeferredClimbingQueue queue, DeferredClimbingFrame frame)
    {
        if (frame.Owner is { } existing) return existing;
        var pending = new Stack<DeferredClimbingFrame>();
        StreamedHeaderSpool.Owner? parent = null;
        var current = frame;
        while (true)
        {
            if (current.Owner is { } known) { parent = known; break; }
            pending.Push(current);
            if (current.Node.Parent is not { } sourceParent) break;
            if (!set.Frames.TryGet<DeferredClimbingFrame>(sourceParent, out var parentFrame))
                throw new InvalidOperationException("A climbing header's parent is outside the current source frames");
            current = parentFrame!;
        }
        while (pending.TryPop(out var missing))
        {
            parent = queue.Document.HeaderSpool.AddOwner(missing.Node, parent);
            missing.Owner = parent;
        }
        return frame.Owner!.Value;
    }

    public bool X31DeferredClimbingSetBegin(XPathItem state, XPathNavigator candidate, IList<XPathNavigator> potential)
    {
        var set = (DeferredClimbingSet)state;
        set.Fault?.Throw();
        try
        {
            XmlQueryRuntime.CheckCancellation();
            if (set.Captured || set.Ended) throw new InvalidOperationException("The deferred climbing scan is complete");
            if (candidate is not StreamedNavigator source || source.Replayable)
            {
                foreach (var node in potential)
                {
                    var key = ((set.Documents ??= new()).GetDocumentIndex(node), node.UniqueId);
                    if (set.Grounded.ContainsKey(key)) continue;
                    var grounded = new DeferredGroundedCell(node, set.Membership.Length);
                    if (node is StreamedNavigator replay && set.Frames.TryGet<DeferredClimbingFrame>(replay.PositionNode, out var known)
                        && known!.Cells.TryGetValue(DeferredSlot(node), out var existing))
                        replay.Document.HeaderSpool.Read(existing, grounded.Leaves, out _, out _, out _);
                    set.Grounded.Add(key, grounded);
                }
                return true;
            }
            if (!set.Queues.TryGetValue(source.Document, out var queue))
            {
                if (set.Early && set.Queues.Count != 0) throw new InvalidOperationException("A singleton climbing anchor changed source document");
                set.Queues.Add(source.Document, queue = new(source.Document));
            }
            var chain = new Stack<StreamedDocument.Node>();
            for (var node = source.PositionNode; node != null; node = node.Parent) chain.Push(node);
            int common = 0;
            foreach (var node in chain)
            {
                if (common >= queue.Open.Count || queue.Open[common].Node.Number != node.Number) break;
                common++;
            }
            CloseDeferredFrames(queue, common);
            int index = 0;
            foreach (var node in chain)
            {
                if (index++ < common) continue;
                var frame = set.Frames.Get(node, () => new DeferredClimbingFrame(node));
                queue.Open.Add(frame);
            }
            foreach (var item in potential)
            {
                if (item is not StreamedNavigator node || !ReferenceEquals(node.Document, source.Document)
                    || !set.Frames.TryGet<DeferredClimbingFrame>(node.PositionNode, out var frame))
                    throw new InvalidOperationException("A potential climbing result is outside the current source headers");
                var slot = DeferredSlot(node);
                if (frame!.Cells.ContainsKey(slot)) continue;
                int position = -1;
                if (node.NodeType == XPathNodeType.Attribute)
                {
                    position = Array.FindIndex(frame.Node.Attributes, a => a.LocalName == node.LocalName && a.Namespace == node.NamespaceURI);
                    if (position < 0) throw StreamedDocument.PositionError();
                }
                var cell = source.Document.HeaderSpool.AddCell(DeferredOwner(set, queue, frame), position, set.Membership.Length);
                frame.Cells.Add(slot, cell);
                var groundedKey = ((set.Documents ??= new()).GetDocumentIndex(node), node.UniqueId);
                if (set.Grounded.TryGetValue(groundedKey, out var grounded))
                    for (int i = 0; i < grounded.Leaves.Length; i++)
                        if (grounded.Leaves[i]) source.Document.HeaderSpool.Select(cell, i);
                if (queue.Tail is { } tail) source.Document.HeaderSpool.Link(tail, cell);
                queue.Tail = cell;
                queue.Head ??= cell;
            }
            return true;
        }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        {
            set.Complete(); set.Fault = ExceptionDispatchInfo.Capture(error); throw;
        }
    }

    public bool X31DeferredClimbingSetAdd(XPathItem state, double leaf, IList<XPathNavigator> nodes)
    {
        var set = (DeferredClimbingSet)state;
        set.Fault?.Throw();
        try
        {
            XmlQueryRuntime.CheckCancellation();
            if (set.Captured || set.Ended) throw new InvalidOperationException("The deferred climbing scan is complete");
            if (leaf < 0 || leaf >= set.Membership.Length || leaf != Math.Truncate(leaf)) throw new ArgumentOutOfRangeException(nameof(leaf));
            foreach (var node in nodes)
            {
                var key = ((set.Documents ??= new()).GetDocumentIndex(node), node.UniqueId);
                bool registered = set.Grounded.TryGetValue(key, out var grounded);
                if (registered) grounded!.Leaves[(int)leaf] = true;
                if (node is StreamedNavigator source)
                {
                    if (set.Frames.TryGet<DeferredClimbingFrame>(source.PositionNode, out var frame)
                        && frame!.Cells.TryGetValue(DeferredSlot(source), out var cell))
                    {
                        source.Document.HeaderSpool.Select(cell, (int)leaf);
                        registered = true;
                    }
                }
                if (!registered) throw new InvalidOperationException("A climbing result was not registered before projection");
            }
            return true;
        }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        {
            set.Complete(); set.Fault = ExceptionDispatchInfo.Capture(error); throw;
        }
    }

    private static XPathNavigator? DrainDeferredQueue(DeferredClimbingSet set, DeferredClimbingQueue queue)
    {
        while (queue.Head is { } cell)
        {
            XmlQueryRuntime.CheckCancellation();
            queue.Document.HeaderSpool.Read(cell, set.Membership, out var owner, out int position, out var next, out bool sealedOwner);
            byte selected = set.Selected(set.Membership, sealedOwner || set.Captured);
            if (selected == 3 || selected == 2 && !sealedOwner && !set.Captured) return null;
            queue.Head = next;
            if (selected == 2) return queue.Document.HeaderSpool.Read(owner, position);
        }
        return null;
    }

    public IList<XPathNavigator> X31DeferredClimbingSetDrain(XPathItem state, bool finish)
    {
        var set = (DeferredClimbingSet)state;
        set.Fault?.Throw();
        try
        {
            XmlQueryRuntime.CheckCancellation();
            if (set.Ended) return Array.Empty<XPathNavigator>();
            if (finish && !set.Captured)
            {
                foreach (var queue in set.Queues.Values) CloseDeferredFrames(queue, 0);
                set.Captured = true;
                set.OrderedGrounded = new(set.Grounded.Values);
                set.OrderedGrounded.Sort((left, right) => _runtime.ComparePosition(left.Node, right.Node));
            }
            if ((!set.Early || set.Grounded.Count != 0) && !set.Captured) return Array.Empty<XPathNavigator>();
            while (true)
            {
                XPathNavigator? result = null;
                DeferredClimbingQueue? selectedQueue = null;
                foreach (var queue in set.Queues.Values)
                {
                    queue.Ready ??= DrainDeferredQueue(set, queue);
                    if (queue.Ready != null && (result == null || _runtime.ComparePosition(queue.Ready, result) < 0))
                    { result = queue.Ready; selectedQueue = queue; }
                }
                if (set.Captured && set.OrderedGrounded != null)
                {
                    while (set.GroundedPosition < set.OrderedGrounded.Count)
                    {
                        var cell = set.OrderedGrounded[set.GroundedPosition];
                        if (set.Selected(cell.Leaves, final: true) != 2) { set.GroundedPosition++; continue; }
                        if (result == null || _runtime.ComparePosition(cell.Node, result) <= 0)
                        { result = cell.Node; selectedQueue = null; }
                        break;
                    }
                }
                if (result == null)
                {
                    if (set.Captured) set.Complete();
                    return Array.Empty<XPathNavigator>();
                }
                if (selectedQueue != null) selectedQueue.Ready = null;
                else set.GroundedPosition++;
                if (set.Previous != null)
                {
                    int order = _runtime.ComparePosition(set.Previous, result);
                    if (order == 0) continue;
                    if (order > 0) throw new InvalidOperationException("A deferred climbing projection changed document order");
                }
                set.Previous = result.Clone();
                return new[] { result.Clone() };
            }
        }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        {
            set.Complete(); set.Fault = ExceptionDispatchInfo.Capture(error); throw;
        }
    }
}
