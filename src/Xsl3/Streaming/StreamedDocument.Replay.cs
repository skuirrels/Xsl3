// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Text;

namespace Xsl3.Streaming;

internal sealed partial class StreamedDocument
{
    // Only explicit subtree grounding starts a replay buffer, when a live descendant scan or copied
    // accumulator provenance needs the source frames. Positions retain their original identity and metadata.
    // No document-wide index owns these buffers: they die with the last position in that subtree.
    internal sealed class ReplayBuffer(Node root)
    {
        private readonly Dictionary<long, Node> _nodes = new();
        private Dictionary<long, Node>? _lastChildren = new();
        internal readonly Node Root = root;
        internal bool Complete, Unrecorded;
        internal int Count => _nodes.Count;
        internal IEnumerable<Node> Nodes => _nodes.Values;

        internal void Add(Node node)
        {
            if (node != Root && node.Parent?.Replay != this) return;
            if (!_nodes.TryAdd(node.Number, node)) return;
            node.Replay = this;
            if (node == Root) return;
            if (_lastChildren!.TryGetValue(node.Parent!.Number, out Node? previous)) previous.NextSibling = node.Number;
            _lastChildren[node.Parent.Number] = node;
        }

        internal Node? Child(Node node) => _nodes.GetValueOrDefault(node.FirstChild);
        internal Node? Sibling(Node node) => _nodes.GetValueOrDefault(node.NextSibling);

        internal string StringValue(Node node)
        {
            var value = new StringBuilder();
            for (Node? child = Child(node); child != null; child = NextWithin(child, node))
                if (IsText(child.Kind)) value.Append(child.Text);
            return value.ToString();
        }

        private Node? NextWithin(Node node, Node root)
        {
            if (Child(node) is { } child) return child;
            while (node != root)
            {
                if (Sibling(node) is { } sibling) return sibling;
                node = node.Parent!;
            }
            return null;
        }

        internal void ReleaseConstructionState() => _lastChildren = null;
    }

    private ReplayBuffer? _capturing;
    internal static bool CanReplay(Node node) => node.Replay?.Complete == true;

    private ReplayBuffer? BeginReplay(Node node, bool accumulators, bool force)
    {
        if (_capturing != null) return null;
        var position = new StreamedNavigator(this, node);
        bool needed = force || accumulators && Identity.Observer != null || _descendants?.Exists(reference => reference.TryGetTarget(out var cursor) && cursor.NeedsReplay(position)) == true;
        if (!needed) return null;
        var capture = _capturing = new ReplayBuffer(node);
        capture.Add(node);
        if (_current != node) capture.Add(_current); // header lookahead, before any content was consumed
        return capture;
    }

    // Explicit buffering (fork grouping and trace diagnostics) retains original source
    // positions and annotations rather than replacing every member with a copied tree.
    internal void BufferSubtree(Node node)
    {
        Check();
        if (CanReplay(node) || node.Kind is not (System.Xml.XPath.XPathNodeType.Root or System.Xml.XPath.XPathNodeType.Element)) return;
        using (BeginConsumption(node, replay: true, forceReplay: true))
            while (!node.Ended) ReadNext();
    }

    private void FinishReplay(ReplayBuffer capture)
    {
        _capturing = null;
        capture.Complete = capture.Root.Ended && !capture.Unrecorded;
        capture.ReleaseConstructionState();
        // An interrupted copy must not make an incomplete subtree appear empty to its selection.
        if (!capture.Complete && _descendants != null)
        {
            var root = new StreamedNavigator(this, capture.Root);
            foreach (var reference in _descendants)
                if (reference.TryGetTarget(out var cursor)) cursor.ReplayInterrupted(root);
        }
    }
}
