// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Tree
{
    /// <summary>
    /// The navigator over a <see cref="CompactDocument"/>. Positioned on a content node (an index), or on an attribute
    /// or namespace node of the element <see cref="_node"/>. It behaves as XPathDocument's navigator does: namespace
    /// nodes in the same order (xmlns="" included), attributes and namespaces before children in document order, and
    /// the attributes of a node counted among its descendants by <see cref="IsDescendant"/>.
    /// </summary>
    internal sealed class CompactNavigator : XPathNavigator
    {
        private const int None = CompactDocument.None;

        private readonly CompactDocument _doc;
        private int _node;
        private int _attr = None;
        private int _ns = None;                                 // index into _scope
        private (string Prefix, string Uri, int Owner, int Decl)[]? _scope;

        // The name last asked for and its expanded-name id (callers ask for the same name over and over).
        private string? _askedLocal, _askedUri;
        private int _askedId;

        /// <summary>The parts of this text node written with disable-output-escaping, or null.</summary>
        internal List<(int Start, int Length)>? RawText =>
            _attr == None && _ns == None && _doc._rawText != null && _doc._rawText.TryGetValue(_node, out var raw) ? raw : null;

        public CompactNavigator(CompactDocument doc)
        {
            _doc = doc;
            SetRoot();
        }

        private void SetRoot()
        {
            (_node, _attr, bool ns) = _doc.Root;
            _ns = ns ? 0 : None;
            _scope = ns ? _doc.RootNamespace : null;
        }

        private CompactNavigator(CompactNavigator other)
        {
            _doc = other._doc;
            _node = other._node;
            _attr = other._attr;
            _ns = other._ns;
            _scope = other._scope;
            _askedLocal = other._askedLocal;
            _askedUri = other._askedUri;
            _askedId = other._askedId;
        }

        internal CompactDocument Document => _doc;

        /// <summary>Xsl3 (P6): the node's type annotation from validation; null when it is untyped.</summary>
        internal System.Xml.Schema.XmlSchemaType? SchemaType =>
            _ns != None || !_doc.HasAnnotations ? null
            : _attr != None ? (_doc._attrTypes != null && _doc._attrTypes.TryGetValue(_attr, out var a) ? a : null)
            : _doc._types != null && _doc._types.TryGetValue(_node, out var e) ? e : null;

        /// <summary>Xsl3 (P6): whether the tree has type annotations (a schema validated it).</summary>
        internal bool Typed => _doc.HasAnnotations;

        /// <summary>Xsl3 (P6): whether the node is an element validated as nilled (xsi:nil="true").</summary>
        internal bool IsNilled => _doc._nilled != null && OnContent && _doc.HasAnnotations && _doc._nilled.Contains(_node);

        public override System.Xml.Schema.IXmlSchemaInfo? SchemaInfo =>
            SchemaType is { } type ? new Xsl3.Schema.NodeSchemaInfo(type, IsNilled) : null;

        private bool OnContent => _attr == None && _ns == None;

        public override XPathNavigator Clone() => new CompactNavigator(this);

        public override XPathNodeType NodeType =>
            _ns != None ? XPathNodeType.Namespace : _attr != None ? XPathNodeType.Attribute : (XPathNodeType)_doc.Kind(_node);

        public override string LocalName
        {
            get
            {
                if (_ns != None)
                {
                    return _scope![_ns].Prefix;
                }
                if (_attr != None)
                {
                    return _doc._local[_doc._attrName[_attr]];
                }
                int name = _doc._name[_node];
                return name == None ? string.Empty : _doc._local[name];
            }
        }

        public override string NamespaceURI
        {
            get
            {
                if (_ns != None)
                {
                    return string.Empty;
                }
                if (_attr != None)
                {
                    return _doc._uri[_doc._attrName[_attr]];
                }
                int name = _doc._name[_node];
                return name == None ? string.Empty : _doc._uri[name];
            }
        }

        public override string Prefix
        {
            get
            {
                if (_ns != None)
                {
                    return string.Empty;
                }
                if (_attr != None)
                {
                    return _doc._prefix[_doc._attrName[_attr]];
                }
                int name = _doc._name[_node];
                return name == None ? string.Empty : _doc._prefix[name];
            }
        }

        public override string Name
        {
            get
            {
                string prefix = Prefix, local = LocalName;
                return prefix.Length == 0 ? local : string.Concat(prefix, ":", local);
            }
        }

        public override string Value
        {
            get
            {
                if (_ns != None)
                {
                    return _scope![_ns].Uri;
                }
                if (_attr != None)
                {
                    return _doc._values[_doc._attrValue[_attr]];
                }
                return _doc.Kind(_node) is (int)XPathNodeType.Element or (int)XPathNodeType.Root
                    ? _doc.StringValue(_node)
                    : _doc.LeafValue(_node);
            }
        }

        public override string BaseURI => _doc.BaseUri(_node);

        public override bool IsEmptyElement => OnContent && (_doc._kind[_node] & CompactDocument.EmptyTag) != 0;

        public override XmlNameTable NameTable => _doc._nameTable;

        public override bool HasAttributes => OnContent && _doc.Kind(_node) == (int)XPathNodeType.Element && _doc._data[_node] != None;

        public override bool HasChildren => OnContent && _node + 1 < _doc._end[_node];

        public override object UnderlyingObject => Clone();

        // Attributes

        public override bool MoveToFirstAttribute()
        {
            if (!OnContent || _doc.Kind(_node) != (int)XPathNodeType.Element || _doc._data[_node] == None)
            {
                return false;
            }
            _attr = _doc._data[_node];
            return true;
        }

        public override bool MoveToNextAttribute()
        {
            if (_attr == None || _attr + 1 >= _doc._attrCount || _doc._attrOwner[_attr + 1] != _node)
            {
                return false;
            }
            _attr++;
            return true;
        }

        public override bool MoveToAttribute(string localName, string namespaceURI)
        {
            if (!OnContent || _doc.Kind(_node) != (int)XPathNodeType.Element)
            {
                return false;
            }
            int a = _doc._data[_node];
            if (a == None)
            {
                return false;
            }
            int target = Target(localName, namespaceURI);
            if (target == CompactDocument.Absent)
            {
                return false;
            }
            for (; a < _doc._attrCount && _doc._attrOwner[a] == _node; a++)
            {
                if (_doc._expanded[_doc._attrName[a]] == target)
                {
                    _attr = a;
                    return true;
                }
            }
            return false;
        }

        public override string GetAttribute(string localName, string namespaceURI)
        {
            if (OnContent && _doc.Kind(_node) == (int)XPathNodeType.Element && _doc._data[_node] is int a and not None)
            {
                int target = Target(localName, namespaceURI);
                for (; target != CompactDocument.Absent && a < _doc._attrCount && _doc._attrOwner[a] == _node; a++)
                {
                    if (_doc._expanded[_doc._attrName[a]] == target)
                    {
                        return _doc._values[_doc._attrValue[a]];
                    }
                }
            }
            return string.Empty;
        }


        // Namespaces

        /// <summary>Xsl3: an element made with inherit-namespaces="no" (its children don't inherit its namespaces).</summary>
        internal bool NoInherit => OnContent && _doc._noInherit != null && _doc._noInherit.Contains(_node);

        public override bool MoveToFirstNamespace(XPathNamespaceScope scope)
        {
            if (!OnContent || _doc.Kind(_node) != (int)XPathNodeType.Element)
            {
                return false;
            }
            var list = _doc.InScopeNamespaces(_node);
            int i = NextNamespace(list, 0, scope);
            if (i == None)
            {
                return false;
            }
            _scope = list;
            _ns = i;
            return true;
        }

        public override bool MoveToNextNamespace(XPathNamespaceScope scope)
        {
            if (_ns == None)
            {
                return false;
            }
            int i = NextNamespace(_scope!, _ns + 1, scope);
            if (i == None)
            {
                return false;
            }
            _ns = i;
            return true;
        }

        /// <summary>The first namespace at or after i that the scope includes; Local stops at the first inherited one.</summary>
        private int NextNamespace((string Prefix, string Uri, int Owner, int Decl)[] list, int i, XPathNamespaceScope scope)
        {
            for (; i < list.Length; i++)
            {
                switch (scope)
                {
                    case XPathNamespaceScope.Local:
                        return list[i].Owner == _node ? i : None;
                    case XPathNamespaceScope.ExcludeXml:
                        if (list[i].Owner == None)
                        {
                            continue;
                        }
                        return i;
                    default:
                        return i;
                }
            }
            return None;
        }

        // Content

        public override bool MoveToNext()
        {
            if (!OnContent)
            {
                return false;
            }
            int next = _doc.Next(_node);
            if (next == None)
            {
                return false;
            }
            _node = next;
            return true;
        }

        public override bool MoveToPrevious()
        {
            if (!OnContent || _node == 0)
            {
                return false;
            }
            int parent = _doc._parent[_node];
            int p = _node - 1;
            if (p == parent)
            {
                return false;
            }
            // p is the last node of the previous sibling's subtree: climb to the child of parent.
            while (_doc._parent[p] != parent)
            {
                p = _doc._parent[p];
            }
            _node = p;
            return true;
        }

        public override bool MoveToFirstChild()
        {
            if (!OnContent || _node + 1 >= _doc._end[_node])
            {
                return false;
            }
            _node++;
            return true;
        }

        public override bool MoveToParent()
        {
            if (!OnContent)
            {
                if (_node == None)
                {
                    // A parentless attribute or namespace node.
                    return false;
                }
                _attr = None;
                _ns = None;
                _scope = null;
                return true;
            }
            int parent = _doc._parent[_node];
            if (parent == None)
            {
                return false;
            }
            _node = parent;
            return true;
        }

        public override void MoveToRoot()
        {
            if (_doc._count > 0)
            {
                // The document node, or a fragment's top node (index 0; a fragment has one).
                _node = 0;
                _attr = None;
                _ns = None;
                _scope = null;
                return;
            }
            SetRoot();
        }

        public override bool MoveTo(XPathNavigator other)
        {
            if (other is not CompactNavigator that || !ReferenceEquals(that._doc, _doc))
            {
                return false;
            }
            _node = that._node;
            _attr = that._attr;
            _ns = that._ns;
            _scope = that._scope;
            return true;
        }

        /// <summary>Xsl3: to an attribute of the tree, by its index (fn:idref).</summary>
        internal void MoveToAttributeIndex(int attribute)
        {
            _node = _doc._attrOwner[attribute];
            _attr = attribute;
            _ns = None;
            _scope = null;
        }

        public override bool MoveToId(string id)
        {
            int element = _doc.LookupId(id);
            if (element == None)
            {
                return false;
            }
            _node = element;
            _attr = None;
            _ns = None;
            _scope = null;
            return true;
        }

        public override bool IsSamePosition(XPathNavigator other) =>
            other is CompactNavigator that && ReferenceEquals(that._doc, _doc) && that._node == _node && that._attr == _attr && that._ns == _ns;

        public override bool MoveToChild(string localName, string namespaceURI)
        {
            if (!OnContent)
            {
                return false;
            }
            int target = Target(localName, namespaceURI);
            if (target == CompactDocument.Absent)
            {
                return false;
            }
            CompactDocument d = _doc;
            int[] expanded = d._expanded;
            for (int c = _node + 1 < d._end[_node] ? _node + 1 : None; c != None; c = d.Next(c))
            {
                if (d.Kind(c) == (int)XPathNodeType.Element && expanded[d._name[c]] == target)
                {
                    _node = c;
                    return true;
                }
            }
            return false;
        }

        public override bool MoveToNext(string localName, string namespaceURI)
        {
            if (!OnContent)
            {
                return false;
            }
            int target = Target(localName, namespaceURI);
            if (target == CompactDocument.Absent)
            {
                return false;
            }
            CompactDocument d = _doc;
            int[] expanded = d._expanded;
            for (int c = d.Next(_node); c != None; c = d.Next(c))
            {
                if (d.Kind(c) == (int)XPathNodeType.Element && expanded[d._name[c]] == target)
                {
                    _node = c;
                    return true;
                }
            }
            return false;
        }

        public override bool MoveToChild(XPathNodeType type)
        {
            if (!OnContent)
            {
                return false;
            }
            int mask = XPathNavigator.GetContentKindMask(type);
            CompactDocument d = _doc;
            for (int c = _node + 1 < d._end[_node] ? _node + 1 : None; c != None; c = d.Next(c))
            {
                if (((1 << d.Kind(c)) & mask) != 0)
                {
                    _node = c;
                    return true;
                }
            }
            return false;
        }

        public override bool MoveToNext(XPathNodeType type)
        {
            if (!OnContent)
            {
                return false;
            }
            int mask = XPathNavigator.GetContentKindMask(type);
            CompactDocument d = _doc;
            for (int c = d.Next(_node); c != None; c = d.Next(c))
            {
                if (((1 << d.Kind(c)) & mask) != 0)
                {
                    _node = c;
                    return true;
                }
            }
            return false;
        }

        public override bool MoveToFollowing(string localName, string namespaceURI, XPathNavigator? end)
        {
            int target = Target(localName, namespaceURI);
            if (target == CompactDocument.Absent)
            {
                return false;
            }
            CompactDocument d = _doc;
            int[] expanded = d._expanded, names = d._name;
            int stop = FollowingEnd(end);
            for (int i = _node + 1; i < stop; i++)
            {
                // Among content nodes only elements and PIs have names.
                int name = names[i];
                if (name >= 0 && expanded[name] == target && d.Kind(i) == (int)XPathNodeType.Element)
                {
                    SetContent(i);
                    return true;
                }
            }
            return false;
        }

        public override bool MoveToFollowing(XPathNodeType type, XPathNavigator? end)
        {
            CompactDocument d = _doc;
            int mask = XPathNavigator.GetContentKindMask(type);
            int stop = FollowingEnd(end);
            for (int i = _node + 1; i < stop; i++)
            {
                if (((1 << d.Kind(i)) & mask) != 0)
                {
                    SetContent(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The index the following scan stops before: the end navigator's node, or just past its element when it is on
        /// an attribute or namespace (which come before the element's children); the document's end without one.
        /// </summary>
        private int FollowingEnd(XPathNavigator? end)
        {
            if (end is CompactNavigator that && ReferenceEquals(that._doc, _doc))
            {
                int stop = that.OnContent ? that._node : that._node + 1;
                // XPathDocument scans until it meets the end node, so an end that isn't ahead means the rest of the
                // document (as for the descendants of the document element, whose "end" stays on it).
                if (stop >= _node + 1)
                {
                    return stop;
                }
            }
            return _doc._count;
        }

        private void SetContent(int node)
        {
            _node = node;
            _attr = None;
            _ns = None;
            _scope = null;
        }

        /// <summary>The expanded-name id of the name asked for (Absent if no node has it).</summary>
        private int Target(string localName, string namespaceURI)
        {
            if (!ReferenceEquals(localName, _askedLocal) || !ReferenceEquals(namespaceURI, _askedUri))
            {
                _askedId = localName == null || namespaceURI == null ? CompactDocument.Absent : _doc.ExpandedId(localName, namespaceURI);
                _askedLocal = localName;
                _askedUri = namespaceURI;
            }
            return _askedId;
        }

        // Order

        public override XmlNodeOrder ComparePosition(XPathNavigator? other)
        {
            if (other is not CompactNavigator that || !ReferenceEquals(that._doc, _doc))
            {
                return XmlNodeOrder.Unknown;
            }
            if (_node != that._node)
            {
                return _node < that._node ? XmlNodeOrder.Before : XmlNodeOrder.After;
            }
            int a = Secondary(), b = that.Secondary();
            return a == b ? XmlNodeOrder.Same : a < b ? XmlNodeOrder.Before : XmlNodeOrder.After;
        }

        /// <summary>
        /// The order within a node: the node, then its namespaces (as XPathDocument creates them: xml, then the
        /// declarations in document order), then its attributes.
        /// </summary>
        private int Secondary() => _ns != None ? int.MinValue + 2 + _scope![_ns].Decl : _attr != None ? _attr : int.MinValue;

        public override bool IsDescendant([NotNullWhen(true)] XPathNavigator? other)
        {
            if (other is not CompactNavigator that || !ReferenceEquals(that._doc, _doc) || !OnContent)
            {
                return false;
            }
            int o = that._node;
            // An attribute or namespace node counts as below its own element, as XPathDocument's navigator has it.
            return that.OnContent ? _node < o && o < _doc._end[_node] : _node <= o && o < _doc._end[_node];
        }

        /// <summary>The id generate-id() uses: the node's kind letter and index, and the attribute or namespace's.</summary>
        internal string UniqueId
        {
            get
            {
                char letter = XPathNavigator.NodeTypeLetter[(int)NodeType];
                return _attr != None ? $"{letter}{_node}a{_attr}" : _ns != None ? $"{letter}{_node}n{_ns}" : $"{letter}{_node}";
            }
        }
    }
}
