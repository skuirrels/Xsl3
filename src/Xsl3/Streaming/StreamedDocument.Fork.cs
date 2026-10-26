// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
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
    private sealed class ForkHeader(Node node)
    {
        internal readonly Node Original = node.Original ?? node;
        internal readonly XPathNodeType Kind = node.Kind;
        internal readonly string LocalName = node.LocalName, Namespace = node.Namespace, Prefix = node.Prefix,
            Text = node.Text, BaseUri = node.BaseUri, Language = node.Language;
        internal readonly Attribute[] Attributes = node.Attributes;
        internal readonly Namespace[] Namespaces = node.Namespaces;
        internal readonly IXmlSchemaInfo? SchemaInfo = node.Schema;
        internal readonly bool Empty = node.EmptyElement, Ended = node.Ended, Started = node.ChildrenStarted,
            Consumed = node.Consumed, Skipped = node.Skipped, NoNext = node.NoNext;
        internal readonly long FirstChild = node.FirstChild;

        internal Node Create(Node? parent, bool boundary, XmlNameTable? names = null)
        {
            var node = new Node(Original.Number, Kind, parent)
            {
                Original = Original, LocalName = LocalName, Namespace = Namespace, Prefix = Prefix,
                Text = Text, BaseUri = BaseUri, Language = Language, Attributes = Attributes, Namespaces = Namespaces,
                Schema = SchemaInfo, EmptyElement = Empty, Ended = boundary ? Empty : Ended,
                ChildrenStarted = !boundary && Started, FirstChild = boundary ? -1 : FirstChild,
                Consumed = Consumed, Skipped = Skipped, NoNext = NoNext,
            };
            if (names != null) Atomize(node, names);
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

        private static void Atomize(Node node, XmlNameTable names)
        {
            node.LocalName = names.Add(node.LocalName); node.Namespace = names.Add(node.Namespace); node.Prefix = names.Add(node.Prefix);
            Attribute[] attributes = node.Attributes;
            node.Attributes = new Attribute[attributes.Length];
            for (int i = 0; i < attributes.Length; i++)
            {
                Attribute a = attributes[i];
                node.Attributes[i] = a with { LocalName = names.Add(a.LocalName), Namespace = names.Add(a.Namespace), Prefix = names.Add(a.Prefix) };
            }
            Namespace[] namespaces = node.Namespaces;
            node.Namespaces = new Namespace[namespaces.Length];
            for (int i = 0; i < namespaces.Length; i++)
            {
                Namespace ns = namespaces[i];
                node.Namespaces[i] = ns with { Prefix = names.Add(ns.Prefix), Uri = names.Add(ns.Uri) };
            }
        }
    }

    private sealed record ForkEvent(ForkHeader Header, bool End);

    internal bool CanFork(Node boundary)
    {
        _cancellation.ThrowIfCancellationRequested();
        return !_disposed && boundary.Kind is (XPathNodeType.Root or XPathNodeType.Element) && !CanReplay(boundary)
            && !boundary.Consumed && !boundary.Skipped && Active(boundary.Number) != null
            && (_current == boundary || _current.Parent == boundary && _current.Number == boundary.FirstChild && !_current.ChildrenStarted);
    }

    internal ForkSource Fork(StreamedNavigator position)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (!ReferenceEquals(position.Document, this) || position.NodeType is not (XPathNodeType.Root or XPathNodeType.Element))
            throw PositionError();
        return new(this, position.PositionNode);
    }

    /// <summary>A forward subtree and its ancestor headers, shared by one invocation of fork.</summary>
    internal sealed class ForkSource : IDisposable
    {
        private readonly StreamedDocument _source;
        private readonly Node _boundary;
        private readonly ForkHeader[] _headers;
        private ConsumptionScope _consumption;
        private bool _entered, _first = true, _complete, _disposed, _ran;

        internal ForkSource(StreamedDocument source, Node boundary)
        {
            source.Check();
            if (!source.CanFork(boundary)) throw PositionError();
            _source = source;
            _boundary = boundary;
            var headers = new List<ForkHeader>();
            for (Node? node = boundary; node != null; node = node.Parent) headers.Add(new(node));
            headers.Reverse();
            _headers = headers.ToArray();
        }

        private ForkEvent? Read()
        {
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
            bool end = current.Kind is XPathNodeType.Root or XPathNodeType.Element && current.Ended && !current.EmptyElement;
            if (current == _boundary && current.Ended) _complete = true;
            return new(new(current), end);
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
            try { ForkProngs.Run(Read, prongs, capacity, effective, caller); }
            finally { Dispose(); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_entered) _consumption.Dispose();
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

    /// <summary>Projects logical events into the existing streamed document parser without serializing XML.</summary>
    private sealed class ForkEventReader(ForkHeader boundary, StreamTee<ForkEvent>.Reader input) : XmlReader
    {
        private readonly NameTable _names = new();
        private ReadState _state;
        private int _attribute = -1;
        private bool _attributeValue;
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
        public override int Depth => Current == null ? 0 : Header.Original.Depth + (_attributeValue ? 2 : _attribute >= 0 ? 1 : 0);
        public override IXmlSchemaInfo? SchemaInfo => _attribute >= 0 ? NamespaceAttribute ? null : Attribute.Schema : Header.SchemaInfo;
        public override string LocalName => _attributeValue || Current == null ? "" : _names.Add(_attribute >= 0 ? NamespaceAttribute
            ? Namespace.Prefix.Length == 0 ? "xmlns" : Namespace.Prefix : Attribute.LocalName : Header.LocalName);
        public override string Prefix => _attributeValue || Current == null ? "" : _names.Add(_attribute >= 0 ? NamespaceAttribute
            ? Namespace.Prefix.Length == 0 ? "" : "xmlns" : Attribute.Prefix : Header.Prefix);
        public override string NamespaceURI => _attributeValue || Current == null ? "" : _names.Add(_attribute >= 0 ? NamespaceAttribute
            ? "http://www.w3.org/2000/xmlns/" : Attribute.Namespace : Header.Namespace);
        public override string Value => _attribute >= 0 ? NamespaceAttribute ? Namespace.Uri : Attribute.Value : Header.Text;
        public override bool HasValue => NodeType is XmlNodeType.Text or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace
            or XmlNodeType.Comment or XmlNodeType.ProcessingInstruction or XmlNodeType.Attribute;
        public override XmlNodeType NodeType => _attributeValue ? XmlNodeType.Text : _attribute >= 0 ? XmlNodeType.Attribute
            : Current == null ? XmlNodeType.None : Header.Kind switch
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
            if (Current == null || Current.Header.Kind == XPathNodeType.Root)
            {
                Current = null; _state = ReadState.EndOfFile;
                return false;
            }
            _state = ReadState.Interactive;
            return true;
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
            for (Node? node = Header.Original; node != null; node = node.Parent)
                for (int i = node.Namespaces.Length - 1; i >= 0; i--)
                    if (node.Namespaces[i].Prefix == prefix) return node.Namespaces[i].Uri;
            return prefix.Length == 0 ? "" : null;
        }
        public override void ResolveEntity() => throw new InvalidOperationException("Logical fork events contain no entity references");
        public override void Close()
        {
            _state = ReadState.Closed; Current = null; input.Dispose();
        }
    }
}
