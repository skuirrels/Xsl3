// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Tree;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

/// <summary>One forward reader and its open ancestor stack, with subtree replay only for explicit grounding.</summary>
internal sealed partial class StreamedDocument : IXPathNavigable, IDisposable
{
    internal sealed class Node(long number, XPathNodeType kind, Node? parent)
    {
        internal readonly long Number = number;
        internal readonly XPathNodeType Kind = kind;
        internal readonly Node? Parent = parent;
        internal Node? Original;
        internal readonly int Depth = parent == null ? -1 : parent.Depth + 1;
        internal string LocalName = "", Namespace = "", Prefix = "", Text = "", BaseUri = "", Language = "";
        internal Attribute[] Attributes = Array.Empty<Attribute>();
        internal Namespace[] Namespaces = Array.Empty<Namespace>();
        // Immutable namespace headers only: clones can share a resolved scope without
        // retaining descendants or transferring a source NameTable into a fork view.
        internal Namespace[]? InScopeNamespaces;
        internal IXmlSchemaInfo? Schema;
        internal bool EmptyElement, Ended, ChildrenStarted, Consumed, Skipped, NoNext;
        internal bool ObservedStart, ObservedEnd;
        // Observer annotations die with this frame, or survive through explicit replay/copy provenance.
        internal object? ObserverValues;
        internal object? RuntimeFacts;
        internal long FirstChild = -1, NextSibling = -1;
        internal ReplayBuffer? Replay;
        internal StreamedValueReplay.Value? ValueReplay;
    }

    internal readonly record struct Attribute(string LocalName, string Namespace, string Prefix, string Value, IXmlSchemaInfo? Schema);
    internal sealed record Namespace(string Prefix, string Uri, long Owner);

    private XmlReader _reader;
    private readonly bool _ownsReader;
    private readonly XmlSpace _space;
    private readonly CancellationToken _cancellation;
    private readonly bool _completeValidation;
    private IReadOnlyDictionary<(string, string), (string, string)>? _idDeclarations;
    private IReadOnlySet<(string, string, string, string)>? _idrefDeclarations;
    private readonly List<Node> _open = new();
    private Node _current;
    private bool _ready, _disposed;
    private long _number;
    private int _consuming;
    private List<WeakReference<StreamedDescendantCursor>>? _descendants;
    internal readonly Node Root;
    internal StreamedDocument Identity { get; }
    private readonly long _created;
    internal long Created => Identity._created;
    internal XmlNameTable NameTable { get; }
    internal int RetainedNodeCount => _open.Count + (_open[^1] == _current ? 0 : 1);

    internal StreamedDocument(XmlReader reader, bool ownsReader = false, XmlSpace space = XmlSpace.Preserve,
        CancellationToken cancellation = default, bool completeValidation = false)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.ReadState is ReadState.Closed or ReadState.Error) throw new ArgumentException("The source reader is not readable", nameof(reader));
        if (reader.NodeType == XmlNodeType.Attribute) throw new ArgumentException("The source reader must be positioned on content", nameof(reader));
        _reader = reader;
        Identity = this;
        _created = DocumentCreationOrder.Next();
        _ownsReader = ownsReader;
        _space = space;
        _cancellation = cancellation;
        _completeValidation = completeValidation;
        _idDeclarations = Runtime.DocumentFragment.IdDeclarations(reader);
        _idrefDeclarations = Runtime.DocumentFragment.IdrefDeclarations(reader);
        NameTable = reader.NameTable ?? new NameTable();
        Root = _current = new(0, XPathNodeType.Root, null) { BaseUri = reader.BaseURI };
        _open.Add(Root);
        _ready = reader.ReadState == ReadState.Interactive;
    }

    public XPathNavigator CreateNavigator() => new StreamedNavigator(this, Root);

    internal bool IsIdAttribute(Node node, int attribute) => Identity._idDeclarations != null
        && Identity._idDeclarations.TryGetValue((node.LocalName, node.Prefix), out var declared)
        && node.Attributes[attribute].LocalName == declared.Item1 && node.Attributes[attribute].Prefix == declared.Item2;

    internal bool IsIdrefAttribute(Node node, int attribute) => Identity._idrefDeclarations?.Contains(
        (node.LocalName, node.Prefix, node.Attributes[attribute].LocalName, node.Attributes[attribute].Prefix)) == true;

    private void ReadDtdIdentity()
    {
        _idDeclarations = Runtime.DocumentFragment.IdDeclarations(_reader) ?? _idDeclarations;
        _idrefDeclarations = Runtime.DocumentFragment.IdrefDeclarations(_reader) ?? _idrefDeclarations;
    }

    internal void CompleteValidation()
    {
        if (!_completeValidation) return;
        Check();
        if (_observer != null)
        {
            while (!Root.Ended) ReadNext();
            return;
        }
        while (_reader.Read()) Check();
    }

    internal const string ErrorNamespace = "http://xsl3/err";
    internal static XdmException PositionError() => new("stream-position", "The requested navigation would revisit a streamed node")
        { Name = new QName(ErrorNamespace, "stream-position") };

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cancellation.ThrowIfCancellationRequested();
    }

    private Node? Active(long number)
    {
        for (Node? node = _current; node != null; node = node.Parent)
            if (node.Number == number) return node;
        return null;
    }

    // Metadata demands on a reconstructed ancestor may use its still-open source frame.
    // This lookup neither retains closed history nor reopens skipped content.
    internal Node? ActiveHeader(long number)
    {
        Check();
        return Active(number);
    }

    internal bool HasChildren(Node node)
    {
        Check();
        if (node.Kind is not (XPathNodeType.Root or XPathNodeType.Element)) return false;
        if (CanReplay(node)) return node.Replay!.Child(node) != null;
        if (node.FirstChild >= 0) return true;
        if (node.Skipped) throw PositionError();
        return FirstChild(node) != null;
    }

    internal Node? FirstChild(Node node)
    {
        Check();
        if (node.Kind is not (XPathNodeType.Root or XPathNodeType.Element)) return null;
        if (CanReplay(node)) return node.Replay!.Child(node);
        if (node.Skipped) throw PositionError();
        if (node.ChildrenStarted)
        {
            if (node.FirstChild >= 0) return Active(node.FirstChild) ?? throw PositionError();
            if (node.Ended) return null;
        }
        if (node.EmptyElement) return null;
        if (_current != node || node.Ended) throw PositionError();
        ReadNext();
        return _current.Parent == node ? _current : null;
    }

    internal Node? Next(Node node)
    {
        Check();
        if (CanReplay(node) && node != node.Replay!.Root) return node.Replay!.Sibling(node);
        if (node.Parent == null || node.NoNext) return null;
        if (node == _forkBoundary) throw PositionError();
        if (node.NextSibling >= 0) return Active(node.NextSibling) ?? throw PositionError();
        if (Active(node.Number) == null) throw PositionError();
        Finish(node);
        ReadNext();
        if (_current.Parent == node.Parent && _current != node)
        {
            node.NextSibling = _current.Number;
            return _current;
        }
        node.NoNext = true;
        return null;
    }

    internal void RegisterDescendantCursor(StreamedDescendantCursor cursor)
    {
        PruneValueReplays();
        _descendants ??= new();
        _descendants.RemoveAll(reference => !reference.TryGetTarget(out var target) || target.Finished);
        _descendants.Add(new(cursor));
    }

    private void Consumed(Node node)
    {
        StreamedNavigator? position = null;
        if (_consumedHeaders != null)
            foreach (var headers in _consumedHeaders) headers.Observe(position ??= new(this, node));

        if (_consumptionMatches != null)
            foreach (var match in _consumptionMatches) match.Observe(position ??= new(this, node));
        if (_capturing != null && node.Replay == _capturing) return;
        if (_descendants == null) return;
        for (int i = _descendants.Count - 1; i >= 0; i--)
            if (!_descendants[i].TryGetTarget(out var cursor) || cursor.Finished) _descendants.RemoveAt(i);
            else cursor.Consumed(position ??= new(this, node));
    }

    internal readonly struct ConsumptionScope(StreamedDocument? document, Node? node, ReplayBuffer? capture = null) : IDisposable
    {
        public void Dispose()
        {
            if (document == null) return;
            document._consuming--;
            node!.Consumed = true;
            if (capture != null) document.FinishReplay(capture);
        }
    }

    internal ConsumptionScope BeginConsumption(Node node, bool replay = false, bool accumulators = false, bool forceReplay = false)
    {
        Check();
        if (CanReplay(node)) return default;
        if (_notifying) throw PositionError();
        if (node.Consumed || node.Skipped || Active(node.Number) == null) throw PositionError();
        if (_current != node)
        {
            if (_current.Parent != node || _current.Number != node.FirstChild || _current.ChildrenStarted) throw PositionError();
        }
        ReplayBuffer? capture = replay ? BeginReplay(node, accumulators, forceReplay) : null;
        if (_current != node) Consumed(_current);
        _consuming++;
        return new(this, node, capture);
    }

    internal Node? NextDescendant(Node node, Node root)
    {
        Check();
        // A scan can resume after absorption, provided its cursor observed no lost matching descendants.
        if ((CanReplay(node) || !node.Consumed && !node.Skipped) && FirstChild(node) is { } child) return child;
        return NextNonDescendant(node, root);
    }

    internal Node? NextNonDescendant(Node node, Node root)
    {
        Check();
        while (node != root)
        {
            if (Next(node) is { } sibling) return sibling;
            node = node.Parent ?? throw PositionError();
        }
        return null;
    }

    private void Finish(Node node)
    {
        if (node.Kind != XPathNodeType.Element || node.Ended) return;
        if (_observer == null && _current == node && !node.ChildrenStarted && !_ready)
        {
            // Skip an unselected subtree without allocating frames for its descendants.
            if (_capturing != null && node.Replay == _capturing) _capturing.Unrecorded = true;
            _reader.Skip();
            _ready = _reader.ReadState == ReadState.Interactive;
            node.Ended = node.Skipped = node.ChildrenStarted = true;
            return;
        }
        while (!node.Ended) ReadNext();
    }

    internal string StringValue(Node node, StreamedDescendantCursor? valueConsumer = null)
    {
        Check();
        if (node.Kind is not (XPathNodeType.Root or XPathNodeType.Element)) return node.Text;
        if (node.ValueReplay != null) return node.ValueReplay.Read();
        if (CanReplay(node)) return node.Replay!.StringValue(node);
        if (node.EmptyElement || node.Ended && node.FirstChild < 0 && !node.Skipped) return "";
        if (node.Consumed || node.Skipped || node.Ended || Active(node.Number) == null) throw PositionError();
        if (_current != node)
        {
            // HasChildren/MoveToFirstChild may have read one child header, but none of its content.
            if (_current.Parent != node || _current.Number != node.FirstChild || _current.ChildrenStarted) throw PositionError();
        }
        string first = _current != node && IsText(_current.Kind) ? _current.Text : "";
        StringBuilder? value = null;
        using var values = valueConsumer?.PreserveValues(node);
        using (BeginConsumption(node))
        {
            while (!node.Ended)
            {
                ReadNext();
                if (IsText(_current.Kind))
                {
                    if (first.Length == 0) first = _current.Text;
                    else (value ??= new StringBuilder(first)).Append(_current.Text);
                }
            }
        }
        return value?.ToString() ?? first;
    }

    /// <summary>Ground only this subtree, preserving the validating reader and small-tree builder.</summary>
    internal XPathNavigator Ground(Node node, bool copyAccumulators)
    {
        Check();
        bool replaying = CanReplay(node);
        if (node.Kind is not (XPathNodeType.Root or XPathNodeType.Element) || !replaying && (node.Skipped || node.Consumed || Active(node.Number) == null))
            throw PositionError();
        var builder = new CompactDocument.Builder(NameTable, node.BaseUri, fragment: node.Kind != XPathNodeType.Root, small: true);
        if (!replaying && node.ChildrenStarted && (_current.Parent != node || _current.Number != node.FirstChild || _current.ChildrenStarted)) throw PositionError();
        using (BeginConsumption(node, replay: true, accumulators: copyAccumulators))
        {
            // Read through the navigator so active descendant scans can detect matches absorbed by the copy.
            var navigator = new StreamedNavigator(this, node);
            using XmlReader subtree = navigator.ReadSubtree();
            builder.Read(subtree, _space, typed: _reader is ForkEventReader || node.Schema != null || _reader.SchemaInfo != null,
                rootNamespaces: navigator.GetNamespacesInScope(XmlNamespaceScope.All), idDeclarations: Identity._idDeclarations,
                idrefDeclarations: Identity._idrefDeclarations);
        }
        if (!replaying)
        {
            node.Skipped = node.Consumed = node.Ended = node.ChildrenStarted = true;
            _current = node;
            _ready = false;
        }
        CompactDocument grounded = builder.Finish();
        grounded._constructed = true;
        grounded.NoDocumentUri = true; // a copy keeps its base URI, but has no document URI
        return grounded.CreateNavigator();
    }

    private static bool IsText(XPathNodeType kind) => kind is XPathNodeType.Text or XPathNodeType.SignificantWhitespace or XPathNodeType.Whitespace;
    private bool KeepText() => _reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA
        || _reader.NodeType == XmlNodeType.SignificantWhitespace && _reader.XmlSpace == XmlSpace.Preserve
        || _space == XmlSpace.Preserve && _open.Count > 1;

    private void ReadNext()
    {
        if (_notifying) throw PositionError(); // observer rules cannot consume the reader recursively
        long previous = _number;
        ReadNextCore();
        ObserveCurrent(previous);
        if (_current.Number > previous) _capturing?.Add(_current);
        ObserveValueReplays(_current.Number > previous);
        if (_consuming != 0 && _current.Number > previous) Consumed(_current);
    }

    private void ReadNextCore()
    {
        Check();
        if (Root.Ended) throw PositionError();
        if (_open.Count > 1 && _open[^1].Ended) _open.RemoveAt(_open.Count - 1);
        while (true)
        {
            bool read = _ready || _reader.Read();
            _ready = false;
            if (!read)
            {
                Root.Ended = Root.ChildrenStarted = true;
                _current = Root;
                return;
            }
            switch (_reader.NodeType)
            {
                case XmlNodeType.DocumentType:
                    ReadDtdIdentity();
                    break;
                case XmlNodeType.Element:
                {
                    Node node = Child(XPathNodeType.Element);
                    node.LocalName = _reader.LocalName;
                    node.Namespace = _reader.NamespaceURI;
                    node.Prefix = _reader.Prefix;
                    node.EmptyElement = node.Ended = _reader.IsEmptyElement;
                    // Reader headers give an upper bound including namespace declarations. Keep
                    // the final attribute array directly; ordinary elements need no temporary lists.
                    int headerAttributeCount = _reader.AttributeCount;
                    Attribute[] attributes = headerAttributeCount == 0 ? Array.Empty<Attribute>() : new Attribute[headerAttributeCount];
                    int attributeCount = 0;
                    List<Namespace>? namespaces = null;
                    if (_reader.MoveToFirstAttribute())
                    {
                        do
                        {
                            string localName = _reader.LocalName, ns = _reader.NamespaceURI, prefix = _reader.Prefix;
                            if (ns == "http://www.w3.org/2000/xmlns/")
                                (namespaces ??= new()).Add(new(prefix == "xmlns" ? localName : "", _reader.Value, node.Number));
                            else
                            {
                                IXmlSchemaInfo? schema = Schema(_reader.SchemaInfo);
                                string value = Normalize(_reader.Value, schema?.SchemaType);
                                if (localName == "id" && ns == QName.Ns.Xml) value = Lexical.Collapse(value);
                                attributes[attributeCount++] = new(localName, ns, prefix, value, schema);
                            }
                        } while (_reader.MoveToNextAttribute());
                        _reader.MoveToElement();
                    }
                    node.Schema = Schema(_reader.SchemaInfo);
                    if (node.Parent == Root && _reader is IXmlNamespaceResolver resolver)
                    {
                        var declared = new HashSet<string>(StringComparer.Ordinal) { "xml" };
                        if (namespaces != null)
                            foreach (Namespace ns in namespaces) declared.Add(ns.Prefix);
                        foreach (var ns in resolver.GetNamespacesInScope(XmlNamespaceScope.All))
                            if (ns.Value.Length != 0 && declared.Add(ns.Key)) (namespaces ??= new()).Add(new(ns.Key, ns.Value, node.Number));
                    }
                    if (attributeCount != attributes.Length) Array.Resize(ref attributes, attributeCount);
                    node.Attributes = attributes;
                    node.Namespaces = namespaces?.ToArray() ?? Array.Empty<Namespace>();
                    _open.Add(node);
                    _current = node;
                    return;
                }
                case XmlNodeType.EndElement:
                    _current = _open[^1];
                    if (_current == Root)
                    {
                        Root.Ended = Root.ChildrenStarted = true;
                        return;
                    }
                    _current.Schema = Schema(_reader.SchemaInfo) ?? _current.Schema;
                    _current.Ended = _current.ChildrenStarted = true;
                    return;
                case XmlNodeType.Text: case XmlNodeType.CDATA: case XmlNodeType.SignificantWhitespace: case XmlNodeType.Whitespace:
                {
                    Node? text = null;
                    string first = "";
                    StringBuilder? value = null;
                    do
                    {
                        if (KeepText())
                        {
                            text ??= Child(_reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA ? XPathNodeType.Text
                                : _reader.NodeType == XmlNodeType.SignificantWhitespace && _reader.XmlSpace == XmlSpace.Preserve ? XPathNodeType.SignificantWhitespace : XPathNodeType.Whitespace);
                            if (first.Length == 0) first = _reader.Value;
                            else (value ??= new StringBuilder(first)).Append(_reader.Value);
                            // Kind precedence is Text, then SignificantWhitespace, then Whitespace.
                            if (_reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA && text.Kind != XPathNodeType.Text)
                                text = ReplaceTextKind(text, XPathNodeType.Text);
                            else if (_reader.NodeType == XmlNodeType.SignificantWhitespace && _reader.XmlSpace == XmlSpace.Preserve && text.Kind == XPathNodeType.Whitespace)
                                text = ReplaceTextKind(text, XPathNodeType.SignificantWhitespace);
                        }
                        _cancellation.ThrowIfCancellationRequested();
                        _ready = _reader.Read();
                        while (_ready && _reader.NodeType is XmlNodeType.EntityReference or XmlNodeType.EndEntity)
                        {
                            if (_reader.NodeType == XmlNodeType.EntityReference) _reader.ResolveEntity();
                            _cancellation.ThrowIfCancellationRequested();
                            _ready = _reader.Read();
                        }
                    } while (_ready && _reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace);
                    if (text == null)
                    {
                        if (_ready) continue;
                        Root.Ended = Root.ChildrenStarted = true;
                        _current = Root;
                        return;
                    }
                    Node parent = text.Parent!;
                    if (_ready && _reader.NodeType == XmlNodeType.EndElement) parent.Schema = Schema(_reader.SchemaInfo) ?? parent.Schema;
                    string content = value?.ToString() ?? first;
                    text.Text = parent.Schema?.SchemaType is XmlSchemaComplexType { ContentType: XmlSchemaContentType.ElementOnly }
                        && string.IsNullOrWhiteSpace(content) ? "" : Normalize(content, parent.Schema?.SchemaType);
                    if (text.Text.Length == 0)
                    {
                        if (parent.FirstChild == text.Number) parent.FirstChild = -1;
                        if (_ready) continue;
                        Root.Ended = Root.ChildrenStarted = true;
                        _current = Root;
                        return;
                    }
                    _current = text;
                    return;
                }
                case XmlNodeType.Comment: case XmlNodeType.ProcessingInstruction:
                    _current = Child(_reader.NodeType == XmlNodeType.Comment ? XPathNodeType.Comment : XPathNodeType.ProcessingInstruction);
                    _current.LocalName = _reader.NodeType == XmlNodeType.ProcessingInstruction ? _reader.LocalName : "";
                    _current.Text = _reader.Value;
                    return;
                case XmlNodeType.EntityReference:
                    _reader.ResolveEntity();
                    break;
            }
        }
    }

    private Node Child(XPathNodeType kind)
    {
        Node parent = _open[^1];
        Node? original = (_reader as ForkEventReader)?.Current?.Header.Original;
        long number = original?.Number ?? _number + 1;
        _number = Math.Max(_number, number);
        var node = new Node(number, kind, parent) { Original = original, BaseUri = _reader.BaseURI, Language = _reader.XmlLang };
        parent.ChildrenStarted = true;
        if (parent.FirstChild < 0) parent.FirstChild = node.Number;
        return node;
    }

    private static Node ReplaceTextKind(Node node, XPathNodeType kind) => new(node.Number, kind, node.Parent)
        { BaseUri = node.BaseUri, Language = node.Language, Original = node.Original };

    private static IXmlSchemaInfo? Schema(IXmlSchemaInfo? info) => info == null ? null : new XmlSchemaInfo
    {
        SchemaType = info.SchemaType, SchemaElement = info.SchemaElement, SchemaAttribute = info.SchemaAttribute,
        MemberType = info.MemberType, IsNil = info.IsNil, IsDefault = info.IsDefault, Validity = info.Validity,
    };

    private static string Normalize(string value, XmlSchemaType? type) => type != null && Xsl3.Schema.SchemaTypes.Normalization(type) is { } facet
        ? Lexical.ApplyWhitespace(value, facet) : value;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _open.Clear();
        // Retained source frames/copy provenance need their headers and phases, not
        // the parser or its input buffers. Releasing a borrowed reader does not close it.
        XmlReader reader = _reader;
        _reader = null!;
        try
        {
            try { DisposeValueReplays(); }
            finally { DisposeHeaderSpool(); }
        }
        finally
        {
            try { _observer?.Dispose(); }
            finally { if (_ownsReader) reader.Dispose(); }
        }
    }
}
