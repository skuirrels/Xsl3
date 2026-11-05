// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;

namespace Xsl3.Streaming;

internal sealed partial class StreamedDocument
{
    private readonly Node? _forkBoundary;
    private readonly long _forkFirstChild = -1;

    // Binding projection reads only this prong's navigation state. The source may already
    // be pumping events on another thread, so its mutable frame flags are never consulted.
    internal Node ForkPosition(StreamedNavigator position)
    {
        return TryForkPosition(position.Document.Identity, position.PositionNode.Number) ?? throw PositionError();
    }

    private Node? TryForkPosition(StreamedDocument identity, long number)
    {
        Check();
        if (_forkBoundary == null) throw PositionError();
        if (!ReferenceEquals(Identity, identity)) return null;
        Node? node = Active(number);
        if (node == null && number == _forkFirstChild && _current == _forkBoundary)
            node = FirstChild(_forkBoundary);
        return node?.Number == number ? node : null;
    }

    // Source frames continue to advance, so an event freezes their metadata and phase flags.
    // The original frame supplies identity/provenance, not mutable navigation state.
    private class ForkHeader
    {
        internal readonly Node? Original;
        // NativeAOT retains direct source metadata reads. JIT-only compact events use
        // separate virtual metadata accessors without inventing source positions.
        internal XPathNodeType Kind
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RuntimeFeature.IsDynamicCodeSupported && Original == null ? CompactKind : Original!.Kind;
        }
        protected virtual XPathNodeType CompactKind => throw new InvalidOperationException("An ordinary fork header has no compact metadata");
        internal string LocalName
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RuntimeFeature.IsDynamicCodeSupported && Original == null ? CompactLocalName : Original!.LocalName;
        }
        protected virtual string CompactLocalName => throw new InvalidOperationException("An ordinary fork header has no compact metadata");
        internal string Namespace
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RuntimeFeature.IsDynamicCodeSupported && Original == null ? CompactNamespace : Original!.Namespace;
        }
        protected virtual string CompactNamespace => throw new InvalidOperationException("An ordinary fork header has no compact metadata");
        internal string Prefix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RuntimeFeature.IsDynamicCodeSupported && Original == null ? CompactPrefix : Original!.Prefix;
        }
        protected virtual string CompactPrefix => throw new InvalidOperationException("An ordinary fork header has no compact metadata");
        internal string Text
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RuntimeFeature.IsDynamicCodeSupported && Original == null ? CompactText : Original!.Text;
        }
        protected virtual string CompactText => throw new InvalidOperationException("An ordinary fork header has no compact metadata");
        internal string BaseUri
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RuntimeFeature.IsDynamicCodeSupported && Original == null ? CompactBaseUri : Original!.BaseUri;
        }
        protected virtual string CompactBaseUri => throw new InvalidOperationException("An ordinary fork header has no compact metadata");
        internal string Language
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RuntimeFeature.IsDynamicCodeSupported && Original == null ? CompactLanguage : Original!.Language;
        }
        protected virtual string CompactLanguage => throw new InvalidOperationException("An ordinary fork header has no compact metadata");
        internal readonly Attribute[] Attributes;
        internal readonly Namespace[] Namespaces;
        internal readonly IXmlSchemaInfo? SchemaInfo;
        internal readonly bool Empty, Ended, Started, Consumed, Skipped, NoNext;
        internal readonly long FirstChild;

        internal ForkHeader(Node node)
        {
            Original = node.Original ?? node;
            Attributes = node.Attributes; Namespaces = node.Namespaces; SchemaInfo = node.Schema;
            Empty = node.EmptyElement; Ended = node.Ended; Started = node.ChildrenStarted;
            Consumed = node.Consumed; Skipped = node.Skipped; NoNext = node.NoNext; FirstChild = node.FirstChild;
        }
        internal long Number
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RuntimeFeature.IsDynamicCodeSupported && Original == null ? CompactNumber : Original!.Number;
        }
        protected virtual long CompactNumber => throw new InvalidOperationException("An ordinary fork header has no compact metadata");
        internal int Depth
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RuntimeFeature.IsDynamicCodeSupported && Original == null ? CompactDepth : Original!.Depth;
        }
        protected virtual int CompactDepth => throw new InvalidOperationException("An ordinary fork header has no compact metadata");

        protected ForkHeader(in CompactFrame frame)
        {
            Original = null;
            Attributes = frame.Attributes; Namespaces = frame.Namespaces; SchemaInfo = null;
            Empty = frame.Empty; Ended = frame.Ended; Started = frame.Started;
            Consumed = Skipped = NoNext = false; FirstChild = frame.FirstChild;
        }

        internal string? LookupNamespace(string prefix)
        {
            if (RuntimeFeature.IsDynamicCodeSupported && Original == null) return CompactLookupNamespace(prefix);
            for (Node? current = Original; current != null; current = current.Parent)
                for (int i = current.Namespaces.Length - 1; i >= 0; i--)
                    if (current.Namespaces[i].Prefix == prefix) return current.Namespaces[i].Uri;
            return prefix.Length == 0 ? "" : null;
        }

        protected virtual string? CompactLookupNamespace(string prefix) => throw new InvalidOperationException("An ordinary fork header has no compact metadata");


        internal Node Create(Node? parent, bool boundary, XmlNameTable? names = null)
        {
            var node = new Node(Number, Kind, parent)
            {
                Original = Original, LocalName = LocalName, Namespace = Namespace, Prefix = Prefix,
                Text = Text, BaseUri = BaseUri, Language = Language, Attributes = Attributes, Namespaces = Namespaces,
                Schema = Schema(SchemaInfo), EmptyElement = Empty, Ended = boundary ? Empty : Ended,
                ChildrenStarted = !boundary && Started, FirstChild = boundary ? -1 : FirstChild,
                Consumed = Consumed, Skipped = Skipped, NoNext = NoNext,
            };
            if (names != null) Atomize(node, names);
            for (int i = 0; i < node.Attributes.Length; i++)
                if (node.Attributes[i].Schema is { } info)
                {
                    // Boundary, ancestor and event projections all own their PSVI snapshots.
                    if (ReferenceEquals(node.Attributes, Attributes)) node.Attributes = (Attribute[])node.Attributes.Clone();
                    node.Attributes[i] = node.Attributes[i] with { Schema = Schema(info) };
                }
            return node;
        }

        // A retained motionless position owns headers, never a replay buffer or a link
        // back to the source frame. Content axes remain unavailable on this snapshot.
        internal Node HeaderOnly(Node? parent, XmlNameTable? names = null)
        {
            Node node = Create(parent, boundary: false, names);
            node.Original = null;
            node.Ended = node.Consumed = node.Skipped = node.ChildrenStarted = true;
            node.FirstChild = node.NextSibling = -1;
            return node;
        }

        internal Node CreateFresh(Node parent, XmlNameTable names)
        {
            // A start event has already been normalized and validated by the source.
            // Navigation and observer state belong to this view, not the pumping source.
            Node node = Create(parent, boundary: true, names);
            node.Consumed = node.Skipped = node.NoNext = false;
            return node;
        }

        private static void Atomize(Node node, XmlNameTable names)
        {
            node.LocalName = names.Add(node.LocalName); node.Namespace = names.Add(node.Namespace); node.Prefix = names.Add(node.Prefix);
            Attribute[] attributes = node.Attributes;
            for (int i = 0; i < attributes.Length; i++)
            {
                Attribute a = attributes[i];
                string local = names.Add(a.LocalName), uri = names.Add(a.Namespace), prefix = names.Add(a.Prefix);
                if (ReferenceEquals(local, a.LocalName) && ReferenceEquals(uri, a.Namespace) && ReferenceEquals(prefix, a.Prefix)) continue;
                // Published records are immutable; copy only when this view has different atoms.
                if (ReferenceEquals(node.Attributes, attributes)) node.Attributes = (Attribute[])attributes.Clone();
                node.Attributes[i] = a with { LocalName = local, Namespace = uri, Prefix = prefix };
            }
            Namespace[] namespaces = node.Namespaces;
            for (int i = 0; i < namespaces.Length; i++)
            {
                Namespace ns = namespaces[i];
                string prefix = names.Add(ns.Prefix), uri = names.Add(ns.Uri);
                if (ReferenceEquals(prefix, ns.Prefix) && ReferenceEquals(uri, ns.Uri)) continue;
                if (ReferenceEquals(node.Namespaces, namespaces)) node.Namespaces = (Namespace[])namespaces.Clone();
                node.Namespaces[i] = ns with { Prefix = prefix, Uri = uri };
            }
        }
    }

    private class ForkEvent : ForkHeader
    {
        internal ForkHeader Header => this;
        internal readonly bool End;
        internal ForkEvent(Node node, bool end) : base(node) => End = end;
        protected ForkEvent(in CompactFrame frame, bool end) : base(in frame) => End = end;
    }

    internal bool CanFork(Node boundary)
    {
        _cancellation.ThrowIfCancellationRequested();
        return !_disposed && boundary.Kind is (XPathNodeType.Root or XPathNodeType.Element) && !CanReplay(boundary)
            && !boundary.Consumed && !boundary.Skipped && Active(boundary.Number) != null
            && (_current == boundary || _current.Parent == boundary && _current.Number == boundary.FirstChild && !_current.ChildrenStarted);
    }

    internal ForkSource Fork(StreamedNavigator position, ForkInputProjection? projection = null)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (!ReferenceEquals(position.Document, this) || position.NodeType is not (XPathNodeType.Root or XPathNodeType.Element))
            throw PositionError();
        return new(this, position.PositionNode, _forkBoundary == null && _observer == null
            && _current == position.PositionNode && !_current.ChildrenStarted ? projection : null);
    }

    /// <summary>A forward subtree and its ancestor headers, shared by one invocation of fork.</summary>
    internal sealed class ForkSource : IDisposable
    {
        private readonly StreamedDocument _source;
        private readonly Node _boundary;
        private readonly ForkHeader[] _headers;
        private ConsumptionScope _consumption;
        private bool _entered, _first = true, _complete, _disposed, _ran;
        private readonly ForkInputProjection.Matcher? _projection;
        private CompactForkProducer? _compact;
        private bool _compactDisabled;

        internal ForkSource(StreamedDocument source, Node boundary, ForkInputProjection? projection = null)
        {
            source.Check();
            if (!source.CanFork(boundary)) throw PositionError();
            _source = source;
            _boundary = boundary;
            _projection = projection?.NewMatcher(boundary);
            var headers = new List<ForkHeader>();
            for (Node? node = boundary; node != null; node = node.Parent) headers.Add(new(node));
            headers.Reverse();
            _headers = headers.ToArray();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Node? ReadNode(out bool end)
        {
            end = false;
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_complete || _boundary.Ended) return null;
            if (!_entered)
            {
                _consumption = _source.BeginConsumption(_boundary);
                _entered = true;
            }
            if (_first && _source._current != _boundary)
                _first = false; // A first-child header may already have been inspected.
            else
            {
                _first = false;
                _source.ReadNext();
            }
            Node current = _source._current;
            end = current.Kind is XPathNodeType.Root or XPathNodeType.Element && current.Ended && !current.EmptyElement;
            if (current == _boundary && current.Ended) _complete = true;
            return current;
        }

        private ForkEvent? Read()
        {
            Node? node = ReadNode(out bool end);
            return node == null ? null : new(node, end);
        }

        private ForkProngs.SourceRead<ForkEvent> ReadProjected()
        {
            if (RuntimeFeature.IsDynamicCodeSupported)
            {
                if (_compact != null && !_source.CompactObservationsAbsent)
                {
                    _compact.Dispose(); _compact = null; _compactDisabled = true;
                }
                if (_compact == null && !_compactDisabled && !_entered && _projection!.AllowCompactProducer
                    && _source.CanUseCompactForkProducer(_boundary))
                {
                    _consumption = _source.BeginConsumption(_boundary); _entered = true; _first = false;
                    _compact = new CompactForkProducer(_source, _boundary, _projection);
                }
                if (_compact != null)
                {
                    ForkProngs.SourceRead<ForkEvent> read = _compact.Read();
                    if (read.Complete) _complete = true;
                    return read;
                }
            }
            Node? node = ReadNode(out bool end);
            if (node == null) return new(null, true);
            return _projection!.Keep(node, end) ? new(new(node, end), false) : default;
        }

        internal void Run(IReadOnlyList<Action<StreamedNavigator, CancellationToken>> bodies, int capacity,
            CancellationToken cancellation = default, ForkCaller? caller = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(bodies);
            if (_ran) throw new InvalidOperationException("A forward fork source can only run once");
            _ran = true;
            var prongs = new Action<StreamTee<ForkEvent>.Reader, CancellationToken>[bodies.Count];
            for (int i = 0; i < prongs.Length; i++)
            {
                Action<StreamedNavigator, CancellationToken> body = bodies[i];
                prongs[i] = (reader, token) =>
                {
                    using var view = new StreamedDocument(_source, _headers, reader, token);
                    body(new StreamedNavigator(view, view._current), token);
                };
            }
            CancellationToken sourceCancellation = _source._cancellation;
            using var linked = sourceCancellation.CanBeCanceled && cancellation.CanBeCanceled && sourceCancellation != cancellation
                ? CancellationTokenSource.CreateLinkedTokenSource(sourceCancellation, cancellation) : null;
            CancellationToken effective = linked?.Token ?? (cancellation.CanBeCanceled ? cancellation : sourceCancellation);
            try
            {
                if (_projection == null) ForkProngs.Run(Read, prongs, capacity, effective, caller);
                else ForkProngs.RunSource(ReadProjected, prongs, capacity, effective, caller);
            }
            finally { Dispose(); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { if (RuntimeFeature.IsDynamicCodeSupported) _compact?.Dispose(); }
            finally { if (_entered) _consumption.Dispose(); }
        }
    }

    private StreamedDocument(StreamedDocument source, ForkHeader[] headers, StreamTee<ForkEvent>.Reader input, CancellationToken cancellation)
    {
        _reader = new ForkEventReader(headers[^1], input);
        _ownsReader = true;
        _space = source._space;
        _cancellation = cancellation;
        NameTable = _reader.NameTable;
        Identity = source.Identity;
        Node? parent = null;
        for (int i = 0; i < headers.Length; i++)
        {
            Node node = headers[i].Create(parent, boundary: i == headers.Length - 1, NameTable);
            _open.Add(node);
            parent = node;
        }
        Root = _open[0];
        _current = _forkBoundary = _open[^1];
        _number = _current.Number;
        _forkFirstChild = headers[^1].FirstChild;
    }

    private void ReadForkEvent(ForkEventReader reader)
    {
        // Do not reparse logical events through XmlReader's attribute, whitespace and
        // text-coalescing machinery: the original source has already performed it.
        bool read = _ready || reader.Read();
        _ready = false;
        if (!read)
        {
            Root.Ended = Root.ChildrenStarted = true;
            _current = Root;
            return;
        }
        ForkEvent item = reader.Current!;
        ForkHeader header = item.Header;
        if (item.End)
        {
            Node current = _open[^1];
            if (current.Number != header.Number) throw PositionError();
            current.Schema = Schema(header.SchemaInfo) ?? current.Schema;
            current.Ended = current.ChildrenStarted = true;
            _current = current;
            return;
        }
        Node parent = _open[^1];
        Node node = header.CreateFresh(parent, NameTable);
        parent.ChildrenStarted = true;
        if (parent.FirstChild < 0) parent.FirstChild = node.Number;
        _number = Math.Max(_number, node.Number);
        if (node.Kind == XPathNodeType.Element) _open.Add(node);
        _current = node;
    }

    /// <summary>Projects logical events into the existing streamed document parser without serializing XML.</summary>
    private sealed class ForkEventReader(ForkHeader boundary, StreamTee<ForkEvent>.Reader input) : XmlReader
    {
        private readonly NameTable _names = new();
        private ReadState _state;
        private int _attribute = -1;
        private bool _attributeValue;
        private long _skipNumber;
        private Predicate<ForkEvent>? _skipEnd;
        internal ForkEvent? Current;
        private ForkHeader Header => Current?.Header ?? boundary;
        private bool NamespaceAttribute => _attribute >= Header.Attributes.Length;
        private Attribute Attribute => Header.Attributes[_attribute];
        private Namespace Namespace => Header.Namespaces[_attribute - Header.Attributes.Length];
        public override XmlNameTable NameTable => _names;
        public override ReadState ReadState => _state;
        public override bool EOF => _state == ReadState.EndOfFile;
        public override int AttributeCount => Current is { End: false, Header.Kind: XPathNodeType.Element }
            ? Header.Attributes.Length + Header.Namespaces.Length : 0;
        public override string BaseURI => Header.BaseUri;
        public override string XmlLang => Header.Language;
        public override XmlSpace XmlSpace => Header.Kind == XPathNodeType.SignificantWhitespace ? XmlSpace.Preserve : XmlSpace.None;
        public override bool IsEmptyElement => _attribute < 0 && Current is { End: false } && Header.Empty;
        public override int Depth => Current is null ? 0 : Header.Depth + (_attributeValue ? 2 : _attribute >= 0 ? 1 : 0);
        public override IXmlSchemaInfo? SchemaInfo => _attribute >= 0 ? NamespaceAttribute ? null : Attribute.Schema : Header.SchemaInfo;
        public override string LocalName => _attributeValue || Current is null ? "" : _names.Add(_attribute >= 0 ? NamespaceAttribute
            ? Namespace.Prefix.Length == 0 ? "xmlns" : Namespace.Prefix : Attribute.LocalName : Header.LocalName);
        public override string Prefix => _attributeValue || Current is null ? "" : _names.Add(_attribute >= 0 ? NamespaceAttribute
            ? Namespace.Prefix.Length == 0 ? "" : "xmlns" : Attribute.Prefix : Header.Prefix);
        public override string NamespaceURI => _attributeValue || Current is null ? "" : _names.Add(_attribute >= 0 ? NamespaceAttribute
            ? "http://www.w3.org/2000/xmlns/" : Attribute.Namespace : Header.Namespace);
        public override string Value => _attribute >= 0 ? NamespaceAttribute ? Namespace.Uri : Attribute.Value : Header.Text;
        public override bool HasValue => NodeType is XmlNodeType.Text or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace
            or XmlNodeType.Comment or XmlNodeType.ProcessingInstruction or XmlNodeType.Attribute;
        public override XmlNodeType NodeType => _attributeValue ? XmlNodeType.Text : _attribute >= 0 ? XmlNodeType.Attribute
            : Current is null ? XmlNodeType.None : Header.Kind switch
            {
                XPathNodeType.Element => Current.End ? XmlNodeType.EndElement : XmlNodeType.Element,
                XPathNodeType.Text => XmlNodeType.Text,
                XPathNodeType.Whitespace => XmlNodeType.Whitespace,
                XPathNodeType.SignificantWhitespace => XmlNodeType.SignificantWhitespace,
                XPathNodeType.Comment => XmlNodeType.Comment,
                XPathNodeType.ProcessingInstruction => XmlNodeType.ProcessingInstruction,
                _ => XmlNodeType.None,
            };

        public override bool Read()
        {
            if (_state is ReadState.Closed or ReadState.EndOfFile) return false;
            _attribute = -1; _attributeValue = false;
            Current = input.Read();
            if (Current is null || Current.Kind == XPathNodeType.Root)
            {
                Current = null; _state = ReadState.EndOfFile;
                return false;
            }
            _state = ReadState.Interactive;
            return true;
        }

        public override void Skip()
        {
            if (ReadState != ReadState.Interactive) return;
            if (Current is { End: false, Header.Kind: XPathNodeType.Element, Header.Empty: false })
            {
                _skipNumber = Header.Number;
                Current = input.ReadUntilImmutable(_skipEnd ??= item => item.End && item.Number == _skipNumber);
                if (Current is null)
                {
                    _state = ReadState.EndOfFile;
                    return;
                }
            }
            Read();
        }

        public override string? GetAttribute(string name)
        {
            int saved = _attribute;
            bool value = _attributeValue;
            try { return MoveToAttribute(name) ? Value : null; }
            finally { _attribute = saved; _attributeValue = value; }
        }
        public override string? GetAttribute(string name, string? ns)
        {
            int saved = _attribute;
            bool value = _attributeValue;
            try { return MoveToAttribute(name, ns) ? Value : null; }
            finally { _attribute = saved; _attributeValue = value; }
        }
        public override string GetAttribute(int index)
        {
            int saved = _attribute;
            bool value = _attributeValue;
            try { MoveToAttribute(index); return Value; }
            finally { _attribute = saved; _attributeValue = value; }
        }
        public override bool MoveToAttribute(string name)
        {
            int saved = _attribute;
            bool value = _attributeValue;
            _attributeValue = false;
            for (int i = 0; i < AttributeCount; i++)
            {
                _attribute = i;
                if (Name == name) return true;
            }
            _attribute = saved; _attributeValue = value;
            return false;
        }
        public override bool MoveToAttribute(string name, string? ns)
        {
            int saved = _attribute;
            bool value = _attributeValue;
            _attributeValue = false;
            for (int i = 0; i < AttributeCount; i++)
            {
                _attribute = i;
                if (LocalName == name && NamespaceURI == (ns ?? "")) return true;
            }
            _attribute = saved; _attributeValue = value;
            return false;
        }
        public override void MoveToAttribute(int index)
        {
            if ((uint)index >= AttributeCount) throw new ArgumentOutOfRangeException(nameof(index));
            _attribute = index; _attributeValue = false;
        }
        public override bool MoveToFirstAttribute()
        {
            if (AttributeCount == 0) return false;
            MoveToAttribute(0); return true;
        }
        public override bool MoveToNextAttribute()
        {
            if (_attribute + 1 >= AttributeCount) return false;
            MoveToAttribute(_attribute + 1); return true;
        }
        public override bool MoveToElement()
        {
            if (_attribute < 0) return false;
            _attribute = -1; _attributeValue = false; return true;
        }
        public override bool ReadAttributeValue()
        {
            if (_attribute < 0 || _attributeValue) return false;
            _attributeValue = true; return true;
        }
        public override string? LookupNamespace(string prefix)
        {
            if (prefix == "xml") return "http://www.w3.org/XML/1998/namespace";
            if (prefix == "xmlns") return "http://www.w3.org/2000/xmlns/";
            return Header.LookupNamespace(prefix);
        }
        public override void ResolveEntity() => throw new InvalidOperationException("Logical fork events contain no entity references");
        public override void Close()
        {
            _state = ReadState.Closed; Current = null; input.Dispose();
        }
    }
}
