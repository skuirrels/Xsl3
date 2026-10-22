// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;

namespace Xsl3.Streaming;

/// <summary>A position in a shared forward reader. Clones retain metadata, never a subtree or a reader copy.</summary>
internal sealed class StreamedNavigator : XPathNavigator
{
    private readonly StreamedDocument _document;
    private StreamedDocument.Node _node;
    private int _attribute = -1, _namespace = -1;
    private StreamedDocument.Namespace[]? _scope;
    private bool Content => _attribute < 0 && _namespace < 0;
    internal StreamedDocument Document => _document;
    internal StreamedDocument.Node PositionNode => _node;

    internal StreamedNavigator(StreamedDocument document, StreamedDocument.Node node) => (_document, _node) = (document, node);
    private StreamedNavigator(StreamedNavigator other)
    {
        _document = other._document;
        _node = other._node;
        _attribute = other._attribute;
        _namespace = other._namespace;
        _scope = other._scope;
    }

    public override XPathNavigator Clone() => new StreamedNavigator(this);
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
    public override string Value => _namespace >= 0 ? _scope![_namespace].Uri : _attribute >= 0 ? _node.Attributes[_attribute].Value : _document.StringValue(_node);

    internal string UniqueId => _attribute >= 0 ? $"A{_node.Number}a{_attribute}" : _namespace >= 0 ? $"N{_node.Number}n{_namespace}"
        : $"{XPathNavigator.NodeTypeLetter[(int)NodeType]}{_node.Number}";

    internal XPathNavigator Ground()
    {
        if (!Content) throw StreamedDocument.PositionError();
        return _document.Ground(_node);
    }

    internal StreamedDocument.ConsumptionScope BeginConsumption() => _document.BeginConsumption(_node);

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
    public override string GetAttribute(string localName, string namespaceURI) => !Content ? "" :
        _node.Attributes.FirstOrDefault(a => a.LocalName == localName && a.Namespace == namespaceURI)?.Value ?? "";

    private StreamedDocument.Namespace[] Namespaces()
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<StreamedDocument.Namespace>();
        for (StreamedDocument.Node? node = _node; node != null; node = node.Parent)
            for (int i = node.Namespaces.Length - 1; i >= 0; i--)
            {
                StreamedDocument.Namespace ns = node.Namespaces[i];
                if (found.Add(ns.Prefix)) result.Add(ns);
            }
        if (found.Add("xml")) result.Add(new("xml", "http://www.w3.org/XML/1998/namespace", -1));
        return result.ToArray();
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
        && ReferenceEquals(_document, that._document) && _node.Number == that._node.Number
        && _attribute == that._attribute && _namespace == that._namespace;

    private int Secondary => _namespace >= 0 ? _namespace + 1 : _attribute >= 0 ? Namespaces().Length + _attribute + 1 : 0;
    public override XmlNodeOrder ComparePosition(XPathNavigator? other)
    {
        if (other is not StreamedNavigator that || !ReferenceEquals(_document, that._document)) return XmlNodeOrder.Unknown;
        int compare = _node.Number.CompareTo(that._node.Number);
        if (compare == 0) compare = Secondary.CompareTo(that.Secondary);
        return compare < 0 ? XmlNodeOrder.Before : compare > 0 ? XmlNodeOrder.After : XmlNodeOrder.Same;
    }
    public override bool IsDescendant([NotNullWhen(true)] XPathNavigator? other)
    {
        if (!Content || other is not StreamedNavigator that || !ReferenceEquals(_document, that._document)) return false;
        if (!that.Content && _node.Number == that._node.Number) return true;
        for (StreamedDocument.Node? parent = that._node.Parent; parent != null; parent = parent.Parent)
            if (parent.Number == _node.Number) return true;
        return false;
    }
}
