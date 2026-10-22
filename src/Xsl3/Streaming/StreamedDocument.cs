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

/// <summary>One forward reader and its open ancestor stack. No index of visited nodes or siblings is retained.</summary>
internal sealed class StreamedDocument : IXPathNavigable, IDisposable
{
    internal sealed class Node(long number, XPathNodeType kind, Node? parent)
    {
        internal readonly long Number = number;
        internal readonly XPathNodeType Kind = kind;
        internal readonly Node? Parent = parent;
        internal readonly int Depth = parent == null ? -1 : parent.Depth + 1;
        internal string LocalName = "", Namespace = "", Prefix = "", Text = "", BaseUri = "", Language = "";
        internal Attribute[] Attributes = Array.Empty<Attribute>();
        internal Namespace[] Namespaces = Array.Empty<Namespace>();
        internal IXmlSchemaInfo? Schema;
        internal bool EmptyElement, Ended, ChildrenStarted, Consumed, Skipped, NoNext;
        internal long FirstChild = -1, NextSibling = -1;
    }

    internal sealed record Attribute(string LocalName, string Namespace, string Prefix, string Value, IXmlSchemaInfo? Schema);
    internal sealed record Namespace(string Prefix, string Uri, long Owner);

    private readonly XmlReader _reader;
    private readonly bool _ownsReader;
    private readonly XmlSpace _space;
    private readonly CancellationToken _cancellation;
    private readonly bool _completeValidation;
    private readonly List<Node> _open = new();
    private Node _current;
    private bool _ready, _disposed;
    private long _number;
    private int _consuming;
    private List<WeakReference<StreamedDescendantCursor>>? _descendants;
    internal readonly Node Root;
    internal XmlNameTable NameTable { get; }
    internal int RetainedNodeCount => _open.Count + (_open[^1] == _current ? 0 : 1);

    internal StreamedDocument(XmlReader reader, bool ownsReader = false, XmlSpace space = XmlSpace.Preserve,
        CancellationToken cancellation = default, bool completeValidation = false)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.ReadState is ReadState.Closed or ReadState.Error) throw new ArgumentException("The source reader is not readable", nameof(reader));
        if (reader.NodeType == XmlNodeType.Attribute) throw new ArgumentException("The source reader must be positioned on content", nameof(reader));
        _reader = reader;
        _ownsReader = ownsReader;
        _space = space;
        _cancellation = cancellation;
        _completeValidation = completeValidation;
        NameTable = reader.NameTable ?? new NameTable();
        Root = _current = new(0, XPathNodeType.Root, null) { BaseUri = reader.BaseURI };
        _open.Add(Root);
        _ready = reader.ReadState == ReadState.Interactive;
    }

    public XPathNavigator CreateNavigator() => new StreamedNavigator(this, Root);

    internal void CompleteValidation()
    {
        if (!_completeValidation) return;
        Check();
        while (_reader.Read()) Check();
    }

    internal static XdmException PositionError() => new("stream-position", "The requested navigation would revisit a streamed node")
        { Name = new QName("http://xsl3/err", "stream-position") };

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

    internal bool HasChildren(Node node)
    {
        Check();
        if (node.Kind is not (XPathNodeType.Root or XPathNodeType.Element)) return false;
        if (node.FirstChild >= 0) return true;
        if (node.Skipped) throw PositionError();
        return FirstChild(node) != null;
    }

    internal Node? FirstChild(Node node)
    {
        Check();
        if (node.Kind is not (XPathNodeType.Root or XPathNodeType.Element)) return null;
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
        if (node.Parent == null || node.NoNext) return null;
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
        _descendants ??= new();
        _descendants.RemoveAll(reference => !reference.TryGetTarget(out var target) || target.Finished);
        _descendants.Add(new(cursor));
    }

    private void Consumed(Node node)
    {
        if (_descendants == null) return;
        StreamedNavigator? position = null;
        for (int i = _descendants.Count - 1; i >= 0; i--)
            if (!_descendants[i].TryGetTarget(out var cursor) || cursor.Finished) _descendants.RemoveAt(i);
            else cursor.Consumed(position ??= new(this, node));
    }

    internal readonly struct ConsumptionScope(StreamedDocument? document, Node? node) : IDisposable
    {
        public void Dispose()
        {
            if (document == null) return;
            document._consuming--;
            node!.Consumed = true;
        }
    }

    internal ConsumptionScope BeginConsumption(Node node)
    {
        Check();
        if (node.Consumed || node.Skipped || Active(node.Number) == null) throw PositionError();
        if (_current != node)
        {
            if (_current.Parent != node || _current.Number != node.FirstChild || _current.ChildrenStarted) throw PositionError();
            Consumed(_current);
        }
        _consuming++;
        return new(this, node);
    }

    internal Node? NextDescendant(Node node, Node root)
    {
        Check();
        // A scan can resume after absorption, provided its cursor observed no lost matching descendants.
        if (!node.Consumed && !node.Skipped && FirstChild(node) is { } child) return child;
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
        if (_current == node && !node.ChildrenStarted && !_ready)
        {
            // Skip an unselected subtree without allocating frames for its descendants.
            _reader.Skip();
            _ready = _reader.ReadState == ReadState.Interactive;
            node.Ended = node.Skipped = node.ChildrenStarted = true;
            return;
        }
        while (!node.Ended) ReadNext();
    }

    internal string StringValue(Node node)
    {
        Check();
        if (node.Kind is not (XPathNodeType.Root or XPathNodeType.Element)) return node.Text;
        if (node.EmptyElement || node.Ended && node.FirstChild < 0 && !node.Skipped) return "";
        if (node.Consumed || node.Skipped || node.Ended || Active(node.Number) == null) throw PositionError();
        if (_current != node)
        {
            // HasChildren/MoveToFirstChild may have read one child header, but none of its content.
            if (_current.Parent != node || _current.Number != node.FirstChild || _current.ChildrenStarted) throw PositionError();
        }
        var value = new StringBuilder();
        if (_current != node && IsText(_current.Kind)) value.Append(_current.Text);
        using (BeginConsumption(node))
        {
            while (!node.Ended)
            {
                ReadNext();
                if (IsText(_current.Kind)) value.Append(_current.Text);
            }
        }
        return value.ToString();
    }

    /// <summary>Ground only this subtree, preserving the validating reader and small-tree builder.</summary>
    internal XPathNavigator Ground(Node node)
    {
        Check();
        if (node.Kind is not (XPathNodeType.Root or XPathNodeType.Element) || node.Skipped || node.Consumed || Active(node.Number) == null)
            throw PositionError();
        var builder = new CompactDocument.Builder(NameTable, node.BaseUri, fragment: node.Kind != XPathNodeType.Root, small: true);
        if (node.ChildrenStarted && (_current.Parent != node || _current.Number != node.FirstChild || _current.ChildrenStarted)) throw PositionError();
        using (BeginConsumption(node))
        {
            // Read through the navigator so active descendant scans can detect matches absorbed by the copy.
            var navigator = new StreamedNavigator(this, node);
            using XmlReader subtree = navigator.ReadSubtree();
            builder.Read(subtree, _space, typed: node.Schema != null || _reader.SchemaInfo != null,
                rootNamespaces: navigator.GetNamespacesInScope(XmlNamespaceScope.All));
        }
        node.Skipped = node.Consumed = node.Ended = node.ChildrenStarted = true;
        _current = node;
        _ready = false;
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
        long previous = _number;
        ReadNextCore();
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
                case XmlNodeType.Element:
                {
                    Node node = Child(XPathNodeType.Element);
                    node.LocalName = _reader.LocalName;
                    node.Namespace = _reader.NamespaceURI;
                    node.Prefix = _reader.Prefix;
                    node.EmptyElement = node.Ended = _reader.IsEmptyElement;
                    var attributes = new List<Attribute>();
                    var namespaces = new List<Namespace>();
                    if (_reader.MoveToFirstAttribute())
                    {
                        do
                        {
                            if (_reader.NamespaceURI == "http://www.w3.org/2000/xmlns/")
                                namespaces.Add(new(_reader.Prefix == "xmlns" ? _reader.LocalName : "", _reader.Value, node.Number));
                            else
                            {
                                IXmlSchemaInfo? schema = Schema(_reader.SchemaInfo);
                                string value = Normalize(_reader.Value, schema?.SchemaType);
                                if (_reader.LocalName == "id" && _reader.NamespaceURI == QName.Ns.Xml) value = Lexical.Collapse(value);
                                attributes.Add(new(_reader.LocalName, _reader.NamespaceURI, _reader.Prefix, value, schema));
                            }
                        } while (_reader.MoveToNextAttribute());
                        _reader.MoveToElement();
                    }
                    node.Schema = Schema(_reader.SchemaInfo);
                    if (node.Parent == Root && _reader is IXmlNamespaceResolver resolver)
                    {
                        var declared = new HashSet<string>(namespaces.ConvertAll(n => n.Prefix), StringComparer.Ordinal) { "xml" };
                        foreach (var ns in resolver.GetNamespacesInScope(XmlNamespaceScope.All))
                            if (ns.Value.Length != 0 && declared.Add(ns.Key)) namespaces.Add(new(ns.Key, ns.Value, node.Number));
                    }
                    node.Attributes = attributes.ToArray();
                    node.Namespaces = namespaces.ToArray();
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
                    var value = new StringBuilder();
                    do
                    {
                        if (KeepText())
                        {
                            text ??= Child(_reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA ? XPathNodeType.Text
                                : _reader.NodeType == XmlNodeType.SignificantWhitespace && _reader.XmlSpace == XmlSpace.Preserve ? XPathNodeType.SignificantWhitespace : XPathNodeType.Whitespace);
                            value.Append(_reader.Value);
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
                    text.Text = parent.Schema?.SchemaType is XmlSchemaComplexType { ContentType: XmlSchemaContentType.ElementOnly }
                        && string.IsNullOrWhiteSpace(value.ToString()) ? "" : Normalize(value.ToString(), parent.Schema?.SchemaType);
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
        var node = new Node(++_number, kind, parent) { BaseUri = _reader.BaseURI, Language = _reader.XmlLang };
        parent.ChildrenStarted = true;
        if (parent.FirstChild < 0) parent.FirstChild = node.Number;
        return node;
    }

    private static Node ReplaceTextKind(Node node, XPathNodeType kind) => new(node.Number, kind, node.Parent)
        { BaseUri = node.BaseUri, Language = node.Language };

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
        if (_ownsReader) _reader.Dispose();
    }
}
