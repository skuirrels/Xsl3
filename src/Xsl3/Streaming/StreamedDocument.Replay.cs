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
        private object? _completionGate;
        private List<System.WeakReference<System.Action>>? _completionReaders;
        private int _pruneReadersAt = 1;
        internal readonly Node Root = root;
        internal volatile bool Complete;
        // A stopped capture is immutable even when its enclosing subtree was interrupted.
        // This does not make that incomplete subtree replayable.
        internal volatile bool Sealed;
        internal bool Unrecorded;
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

        internal IEnumerable<Node> Subtree(Node root)
        {
            yield return root;
            for (Node? node = Child(root); node != null; node = NextWithin(node, root)) yield return node;
        }

        internal void WhenComplete(System.Action reader)
        {
            lock (System.Threading.LazyInitializer.EnsureInitialized(ref _completionGate))
            {
                if (!Complete && !Sealed)
                {
                    _completionReaders ??= new();
                    if (_completionReaders.Count >= _pruneReadersAt)
                    {
                        _completionReaders.RemoveAll(weak => !weak.TryGetTarget(out _));
                        _pruneReadersAt = _completionReaders.Count > int.MaxValue / 2
                            ? int.MaxValue : System.Math.Max(1, _completionReaders.Count * 2);
                    }
                    _completionReaders.Add(new(reader));
                    return;
                }
            }
            reader();
        }

        internal void NotifyCompleted()
        {
            List<System.WeakReference<System.Action>>? readers;
            object? gate = System.Threading.Volatile.Read(ref _completionGate);
            if (gate == null) return;
            lock (gate) { readers = _completionReaders; _completionReaders = null; }
            if (readers == null) return;
            foreach (var reader in readers)
                if (reader.TryGetTarget(out var completed)) completed();
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
        capture.Sealed = true;
        capture.NotifyCompleted();
        // An interrupted copy must not make an incomplete subtree appear empty to its selection.
        if (!capture.Complete && _descendants != null)
        {
            var root = new StreamedNavigator(this, capture.Root);
            foreach (var reference in _descendants)
                if (reference.TryGetTarget(out var cursor)) cursor.ReplayInterrupted(root);
        }
    }
}
