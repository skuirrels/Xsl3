// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;

namespace Xsl3.Streaming;

/// <summary>A position in a shared forward reader. Explicit grounding may attach replay data to its source subtree.</summary>
internal sealed class StreamedNavigator : XPathNavigator
{
    private readonly StreamedDocument _document;
    private StreamedDocument.Node _node;
    private int _attribute = -1, _namespace = -1;
    private StreamedDocument.Namespace[]? _scope;
    private StreamedDescendantCursor? _valueCursor;
    private bool _valueOperand;
    private bool Content => _attribute < 0 && _namespace < 0;
    internal StreamedDocument Document => _document;
    internal StreamedDocument.Node PositionNode => _node;
    internal StreamedNavigator AtHeaderAttribute(int index)
    {
        if (index < -1 || index >= _node.Attributes.Length) throw new ArgumentOutOfRangeException(nameof(index));
        return new(_document, _node) { _attribute = index };
    }
    internal bool IsIdAttribute => _attribute >= 0 && _namespace < 0 && _document.IsFragmentId(_node, _attribute);
    internal bool IsIdrefAttribute => _attribute >= 0 && _namespace < 0 && _document.IsFragmentIdref(_node, _attribute);
    internal bool Replayable => StreamedDocument.CanReplay(_node);
    internal bool CanFork => Content && _document.CanFork(_node);
    internal StreamedNavigator BufferForFork() => BufferSubtree();

    internal StreamedNavigator BufferSubtree()
    {
        if (Content) _document.BufferSubtree(_node);
        return (StreamedNavigator)Clone();
    }

    internal StreamedNavigator(StreamedDocument document, StreamedDocument.Node node) => (_document, _node) = (document, node);
    private StreamedNavigator(StreamedNavigator other)
    {
        _document = other._document;
        _node = other._node;
        _attribute = other._attribute;
        _namespace = other._namespace;
        _scope = other._scope;
        _valueCursor = other._valueCursor;
        _valueOperand = other._valueOperand;
    }

    public override XPathNavigator Clone() => new StreamedNavigator(this);

    internal sealed class FrozenPosition(StreamedDocument.FrozenPosition position, int attribute, int ns, StreamedDocument.Namespace[]? scope)
    {
        internal StreamedNavigator InFork(StreamedDocument? document, StreamedDocument.ForkViews views)
        {
            var local = views.Bind(position, document);
            return new(local.Document, local.Node) { _attribute = attribute, _namespace = ns, _scope = ScopeIn(local.Document, scope) };
        }
    }

    internal FrozenPosition FreezeForkPosition(StreamedDocument.ForkCapture capture, CancellationToken cancellation) =>
        new(capture.Capture(_document, _node, cancellation), _attribute, _namespace, _scope);

    internal StreamedNavigator InFork(StreamedDocument document) => ReferenceEquals(document, _document)
        ? new(this)
        : new(document, document.ForkPosition(this))
        {
            _attribute = _attribute,
            _namespace = _namespace,
            _scope = ScopeIn(document, _scope),
        };

    private static StreamedDocument.Namespace[]? ScopeIn(StreamedDocument document, StreamedDocument.Namespace[]? scope)
    {
        if (scope == null) return null;
        var result = new StreamedDocument.Namespace[scope.Length];
        for (int i = 0; i < result.Length; i++)
        {
            StreamedDocument.Namespace ns = scope[i];
            result[i] = ns with { Prefix = document.NameTable.Add(ns.Prefix), Uri = document.NameTable.Add(ns.Uri) };
        }
        return result;
    }
    public override XPathNodeType NodeType => _namespace >= 0 ? XPathNodeType.Namespace : _attribute >= 0 ? XPathNodeType.Attribute : _node.Kind;
    public override string LocalName => _namespace >= 0 ? _scope![_namespace].Prefix : _attribute >= 0 ? _node.Attributes[_attribute].LocalName : _node.LocalName;
    public override string NamespaceURI => _namespace >= 0 ? "" : _attribute >= 0 ? _node.Attributes[_attribute].Namespace : _node.Namespace;
    public override string Prefix => _namespace >= 0 ? "" : _attribute >= 0 ? _node.Attributes[_attribute].Prefix : _node.Prefix;
    public override string Name => Prefix.Length == 0 ? LocalName : Prefix + ":" + LocalName;
    public override string BaseURI => _node.BaseUri;
    public override string XmlLang => _node.Language;
    public override XmlNameTable NameTable => _document.NameTable;
    public override bool IsEmptyElement => Content && _node.EmptyElement;
    public override bool HasAttributes => Content && _node.Attributes.Length != 0;
    public override bool HasChildren => Content && _document.HasChildren(_node);
    public override object UnderlyingObject => Clone();
    public override IXmlSchemaInfo? SchemaInfo => _namespace >= 0 ? null : _attribute >= 0 ? _node.Attributes[_attribute].Schema : _node.Schema;
    public override string Value => _namespace >= 0 ? _scope![_namespace].Uri : _attribute >= 0 ? _node.Attributes[_attribute].Value : _document.StringValue(_node, _valueOperand ? _valueCursor : null);

    internal void ValueCursor(StreamedDescendantCursor cursor) => _valueCursor = cursor;
    internal StreamedNavigator ValueOperand() => _valueCursor == null ? this : new(this) { _valueOperand = true };

    internal string UniqueId => _attribute >= 0 ? $"A{_node.Number}a{_attribute}" : _namespace >= 0 ? $"N{_node.Number}n{_namespace}"
        : $"{XPathNavigator.NodeTypeLetter[(int)NodeType]}{_node.Number}";

    internal XPathNavigator Ground(bool copyAccumulators = true)
    {
        if (!Content) throw StreamedDocument.PositionError();
        return _document.Ground(_node, copyAccumulators);
    }

    internal StreamedDocument.ConsumptionScope BeginConsumption(bool replay = false, bool accumulators = false) => _document.BeginConsumption(_node, replay, accumulators);

    // Copy provenance needs already-recorded source positions after its input has closed, without
    // reopening the reader or changing the navigation contract of a closed source navigator.
    internal XPathNavigator OriginalAtPath(IReadOnlyList<int> path)
    {
        StreamedDocument.Node node = _node;
        for (int i = path.Count - 1; i >= 0; i--)
        {
            if (!StreamedDocument.CanReplay(node)) throw StreamedDocument.PositionError();
            StreamedDocument.ReplayBuffer replay = node.Replay!;
            node = replay.Child(node) ?? throw StreamedDocument.PositionError();
            for (int k = 0; k < path[i]; k++) node = replay.Sibling(node) ?? throw StreamedDocument.PositionError();
        }
        return new StreamedNavigator(_document, node);
    }

    private bool Position(StreamedDocument.Node? node)
    {
        if (node == null) return false;
        _node = node;
        _attribute = _namespace = -1;
        _scope = null;
        return true;
    }

    public override bool MoveToFirstChild() => Content && Position(_document.FirstChild(_node));
    public override bool MoveToNext() => Content && Position(_document.Next(_node));
    internal bool MoveToNextDescendant(StreamedDocument.Node root) => Content && Position(_document.NextDescendant(_node, root));
    internal bool MoveToNextNonDescendant(StreamedDocument.Node root) => Content && Position(_document.NextNonDescendant(_node, root));
    public override bool MoveToPrevious()
    {
        if (!Content || _node.Parent == null) return false;
        throw StreamedDocument.PositionError();
    }
    public override bool MoveToParent()
    {
        if (!Content)
        {
            _attribute = _namespace = -1;
            _scope = null;
            return true;
        }
        return Position(_node.Parent);
    }
    public override void MoveToRoot() => Position(_document.Root);
    public override bool MoveToId(string id) => throw StreamedDocument.PositionError();
    public override bool MoveTo(XPathNavigator other)
    {
        if (other is not StreamedNavigator that || !ReferenceEquals(that._document, _document)) return false;
        _node = that._node;
        _attribute = that._attribute;
        _namespace = that._namespace;
        _scope = that._scope;
        _valueCursor = that._valueCursor;
        _valueOperand = that._valueOperand;
        return true;
    }

    public override bool MoveToFirstAttribute()
    {
        if (!HasAttributes) return false;
        _attribute = 0;
        return true;
    }
    public override bool MoveToNextAttribute()
    {
        if (_attribute < 0 || _attribute + 1 >= _node.Attributes.Length) return false;
        _attribute++;
        return true;
    }
    public override bool MoveToAttribute(string localName, string namespaceURI)
    {
        if (!Content || _node.Kind != XPathNodeType.Element) return false;
        for (int i = 0; i < _node.Attributes.Length; i++)
            if (_node.Attributes[i].LocalName == localName && _node.Attributes[i].Namespace == namespaceURI)
            {
                _attribute = i;
                return true;
            }
        return false;
    }
    public override string GetAttribute(string localName, string namespaceURI)
    {
        if (!Content) return "";
        foreach (StreamedDocument.Attribute attribute in _node.Attributes)
            if (attribute.LocalName == localName && attribute.Namespace == namespaceURI) return attribute.Value;
        return "";
    }

    private static readonly StreamedDocument.Namespace[] s_xmlNamespaces = [new("xml", "http://www.w3.org/XML/1998/namespace", -1)];

    private static StreamedDocument.Namespace[] PublishNamespaces(StreamedDocument.Node node, StreamedDocument.Namespace[] scope) =>
        Interlocked.CompareExchange(ref node.InScopeNamespaces, scope, null) ?? scope;

    private StreamedDocument.Namespace[] Namespaces()
    {
        // Headers do not change after publication. A frame without declarations inherits
        // its ancestor's exact scope, including the original namespace owner numbers.
        // Walk iteratively so deeply nested input does not consume the managed call stack.
        StreamedDocument.Node? owner = _node;
        while (owner != null)
        {
            if (Volatile.Read(ref owner.InScopeNamespaces) is { } cached)
                return ReferenceEquals(owner, _node) ? cached : PublishNamespaces(_node, cached);
            if (owner.Namespaces.Length != 0) break;
            owner = owner.Parent;
        }
        if (owner == null) return PublishNamespaces(_node, s_xmlNamespaces);

        var found = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<StreamedDocument.Namespace>();
        for (StreamedDocument.Node? node = owner; node != null; node = node.Parent)
            for (int i = node.Namespaces.Length - 1; i >= 0; i--)
            {
                StreamedDocument.Namespace ns = node.Namespaces[i];
                if (found.Add(ns.Prefix)) result.Add(ns);
            }
        if (found.Add("xml")) result.Add(new("xml", "http://www.w3.org/XML/1998/namespace", -1));
        StreamedDocument.Namespace[] scope = PublishNamespaces(owner, result.ToArray());
        return ReferenceEquals(owner, _node) ? scope : PublishNamespaces(_node, scope);
    }
    private int NamespaceIndex(StreamedDocument.Namespace[] list, int start, XPathNamespaceScope scope)
    {
        for (int i = start; i < list.Length; i++)
            if (scope != XPathNamespaceScope.Local || list[i].Owner == _node.Number)
                if (scope != XPathNamespaceScope.ExcludeXml || list[i].Prefix != "xml") return i;
        return -1;
    }
    public override bool MoveToFirstNamespace(XPathNamespaceScope namespaceScope)
    {
        if (!Content || _node.Kind != XPathNodeType.Element) return false;
        StreamedDocument.Namespace[] list = Namespaces();
        int index = NamespaceIndex(list, 0, namespaceScope);
        if (index < 0) return false;
        _scope = list;
        _namespace = index;
        return true;
    }
    public override bool MoveToNextNamespace(XPathNamespaceScope namespaceScope)
    {
        if (_namespace < 0) return false;
        int index = NamespaceIndex(_scope!, _namespace + 1, namespaceScope);
        if (index < 0) return false;
        _namespace = index;
        return true;
    }
    public override bool MoveToNamespace(string name)
    {
        if (!Content || _node.Kind != XPathNodeType.Element) return false;
        StreamedDocument.Namespace[] list = Namespaces();
        int index = Array.FindIndex(list, n => n.Prefix == name);
        if (index < 0) return false;
        _scope = list;
        _namespace = index;
        return true;
    }
    public override string? LookupNamespace(string prefix)
    {
        if (prefix == "xml") return "http://www.w3.org/XML/1998/namespace";
        if (prefix == "xmlns") return "http://www.w3.org/2000/xmlns/";
        StreamedDocument.Node? node = _node;
        for (; node != null; node = node.Parent)
            for (int i = node.Namespaces.Length - 1; i >= 0; i--)
                if (node.Namespaces[i].Prefix == prefix) return node.Namespaces[i].Uri;
        return prefix.Length == 0 ? "" : null;
    }
    public override IDictionary<string, string> GetNamespacesInScope(XmlNamespaceScope scope)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (StreamedDocument.Namespace ns in Namespaces())
            if ((scope != XmlNamespaceScope.Local || ns.Owner == _node.Number)
                && (scope != XmlNamespaceScope.ExcludeXml || ns.Prefix != "xml")
                && (scope == XmlNamespaceScope.Local || ns.Uri.Length != 0)) result.Add(ns.Prefix, ns.Uri);
        return result;
    }

    public override bool IsSamePosition([NotNullWhen(true)] XPathNavigator? other) => other is StreamedNavigator that
        && ReferenceEquals(_document.Identity, that._document.Identity) && _node.Number == that._node.Number
        && _attribute == that._attribute && _namespace == that._namespace;

    private int Secondary => _namespace >= 0 ? _namespace + 1 : _attribute >= 0 ? Namespaces().Length + _attribute + 1 : 0;
    public override XmlNodeOrder ComparePosition(XPathNavigator? other)
    {
        if (other is not StreamedNavigator that || !ReferenceEquals(_document.Identity, that._document.Identity)) return XmlNodeOrder.Unknown;
        int compare = _node.Number.CompareTo(that._node.Number);
        if (compare == 0) compare = Secondary.CompareTo(that.Secondary);
        return compare < 0 ? XmlNodeOrder.Before : compare > 0 ? XmlNodeOrder.After : XmlNodeOrder.Same;
    }
    public override bool IsDescendant([NotNullWhen(true)] XPathNavigator? other)
    {
        if (!Content || other is not StreamedNavigator that || !ReferenceEquals(_document.Identity, that._document.Identity)) return false;
        if (!that.Content && _node.Number == that._node.Number) return true;
        for (StreamedDocument.Node? parent = that._node.Parent; parent != null; parent = parent.Parent)
            if (parent.Number == _node.Number) return true;
        return false;
    }
}
