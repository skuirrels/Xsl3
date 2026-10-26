// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Xml;

namespace Xsl3.Streaming;

internal sealed partial class StreamedDocument
{
    internal sealed record FrozenPosition(StreamedDocument Identity, Node Header, FrozenReplay? Replay);

    // Metadata and links are frozen before workers start. Original frames are retained
    // only for explicitly buffered content, where accumulator/copy provenance needs them.
    internal sealed class FrozenReplay
    {
        private readonly ForkHeader[] _ancestors;
        private readonly (ForkHeader Header, long Parent)[] _nodes;
        internal readonly long Boundary;

        internal FrozenReplay(ReplayBuffer replay, CancellationToken cancellation)
        {
            Boundary = replay.Root.Number;
            var ancestors = new List<ForkHeader>();
            for (Node? node = replay.Root.Parent; node != null; node = node.Parent)
            {
                cancellation.ThrowIfCancellationRequested();
                ancestors.Add(new(node));
            }
            ancestors.Reverse();
            _ancestors = ancestors.ToArray();
            var nodes = new List<(ForkHeader, long)>();
            foreach (Node node in replay.Nodes)
            {
                cancellation.ThrowIfCancellationRequested();
                nodes.Add((new(node), node.Parent?.Number ?? -1));
            }
            _nodes = nodes.ToArray();
        }

        internal StreamedDocument Create(StreamedDocument identity, CancellationToken cancellation) => new(identity, _ancestors, _nodes, Boundary, cancellation);
    }

    // Invoking-thread preparation owns this registry only while copying the captured
    // graph. Snapshots share ancestor headers, but retain no original source frames.
    internal sealed class ForkCapture
    {
        private readonly Dictionary<Node, Node> _headers = new();
        private readonly Dictionary<ReplayBuffer, FrozenReplay> _replays = new();

        internal FrozenPosition Capture(StreamedDocument document, Node node, CancellationToken cancellation)
        {
            var pending = new Stack<Node>();
            Node? parent = node;
            while (parent != null && !_headers.ContainsKey(parent))
            {
                cancellation.ThrowIfCancellationRequested();
                pending.Push(parent);
                parent = parent.Parent;
            }
            Node? snapshot = parent == null ? null : _headers[parent];
            while (pending.TryPop(out Node? original))
            {
                cancellation.ThrowIfCancellationRequested();
                snapshot = new ForkHeader(original).HeaderOnly(snapshot);
                _headers.Add(original, snapshot);
            }
            FrozenReplay? replay = null;
            if (CanReplay(node) && !_replays.TryGetValue(node.Replay!, out replay))
                _replays.Add(node.Replay!, replay = new(node.Replay!, cancellation));
            return new(document.Identity, _headers[node], replay);
        }
    }

    // A worker's temporary registry avoids duplicating shared ancestor chains. The
    // returned navigators own their positions; their documents retain only a root header.
    internal sealed class ForkViews
    {
        private readonly CancellationToken _cancellation;
        private readonly Dictionary<StreamedDocument, HeaderView> _documents = new();
        private readonly Dictionary<FrozenReplay, StreamedDocument> _replays = new();
        internal ForkViews(CancellationToken cancellation = default) => _cancellation = cancellation;

        internal (StreamedDocument Document, Node Node) Bind(FrozenPosition position, StreamedDocument? target)
        {
            if (target?.TryForkPosition(position.Identity, position.Header.Number) is { } local) return (target, local);
            if (position.Replay is { } replay)
            {
                if (!_replays.TryGetValue(replay, out StreamedDocument? document))
                    _replays.Add(replay, document = replay.Create(position.Identity, _cancellation));
                return (document, document._replayPositions![position.Header.Number]);
            }
            if (!_documents.TryGetValue(position.Identity, out HeaderView? view))
            {
                Node root = position.Header;
                while (root.Parent != null)
                {
                    _cancellation.ThrowIfCancellationRequested();
                    root = root.Parent;
                }
                _documents.Add(position.Identity, view = new(position.Identity, root, _cancellation));
            }
            return (view.Document, view.Position(position.Header));
        }

        private sealed class HeaderView
        {
            internal readonly StreamedDocument Document;
            private readonly Dictionary<long, Node> _positions = new();

            internal HeaderView(StreamedDocument identity, Node root, CancellationToken cancellation)
            {
                Document = new(identity, root, cancellation);
                _positions.Add(root.Number, Document.Root);
            }

            internal Node Position(Node header)
            {
                var pending = new Stack<Node>();
                Node parent = header;
                while (!_positions.ContainsKey(parent.Number))
                {
                    Document._cancellation.ThrowIfCancellationRequested();
                    pending.Push(parent);
                    parent = parent.Parent!;
                }
                Node result = _positions[parent.Number];
                while (pending.TryPop(out Node? node))
                {
                    Document._cancellation.ThrowIfCancellationRequested();
                    result = new ForkHeader(node).HeaderOnly(result, Document.NameTable);
                    _positions.Add(node.Number, result);
                }
                return result;
            }
        }
    }

    private readonly Dictionary<long, Node>? _replayPositions;

    private StreamedDocument(StreamedDocument identity, ForkHeader[] ancestors, (ForkHeader Header, long Parent)[] nodes, long boundary, CancellationToken cancellation)
    {
        _reader = HeaderReader.Instance;
        _space = XmlSpace.Preserve;
        _cancellation = cancellation;
        Identity = identity;
        NameTable = new NameTable();
        _replayPositions = new();
        Node? parent = null;
        foreach (ForkHeader header in ancestors)
        {
            cancellation.ThrowIfCancellationRequested();
            parent = header.HeaderOnly(parent, NameTable);
            _replayPositions.Add(parent.Number, parent);
            _open.Add(parent);
        }
        ReplayBuffer? replay = null;
        foreach (var entry in nodes)
        {
            cancellation.ThrowIfCancellationRequested();
            parent = entry.Parent < 0 ? null : _replayPositions[entry.Parent];
            Node node = entry.Header.Create(parent, boundary: false, NameTable);
            _replayPositions.Add(node.Number, node);
            if (node.Number == boundary)
            {
                replay = new(node);
                _open.Add(node);
            }
            replay!.Add(node);
        }
        replay!.Complete = true;
        replay.ReleaseConstructionState();
        Root = _replayPositions[0];
        _current = _forkBoundary = _replayPositions[boundary];
        _number = _current.Number;
    }

    private StreamedDocument(StreamedDocument identity, Node rootHeader, CancellationToken cancellation)
    {
        _reader = HeaderReader.Instance;
        _space = XmlSpace.Preserve;
        _cancellation = cancellation;
        Identity = identity;
        NameTable = new NameTable();
        Root = _current = new ForkHeader(rootHeader).HeaderOnly(null, NameTable);
        _open.Add(Root);
    }

    // Metadata-only documents have no input stream. Fail even if a future content path
    // accidentally reaches the reader rather than the existing position guards.
    private sealed class HeaderReader : XmlReader
    {
        internal static readonly HeaderReader Instance = new();
        public override int AttributeCount => 0;
        public override string BaseURI => "";
        public override int Depth => 0;
        public override bool EOF => true;
        public override bool IsEmptyElement => false;
        public override string LocalName => "";
        public override string NamespaceURI => "";
        public override XmlNameTable NameTable => throw PositionError();
        public override XmlNodeType NodeType => XmlNodeType.None;
        public override string Prefix => "";
        public override ReadState ReadState => ReadState.Closed;
        public override string Value => "";
        public override string? GetAttribute(string name) => null;
        public override string? GetAttribute(string name, string? namespaceURI) => null;
        public override string GetAttribute(int i) => throw new ArgumentOutOfRangeException(nameof(i));
        public override string? LookupNamespace(string prefix) => null;
        public override bool MoveToAttribute(string name) => false;
        public override bool MoveToAttribute(string name, string? ns) => false;
        public override bool MoveToElement() => false;
        public override bool MoveToFirstAttribute() => false;
        public override bool MoveToNextAttribute() => false;
        public override bool Read() => throw PositionError();
        public override bool ReadAttributeValue() => false;
        public override void ResolveEntity() => throw PositionError();
    }
}
