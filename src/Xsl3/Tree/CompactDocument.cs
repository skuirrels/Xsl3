// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Tree
{
    /// <summary>
    /// An immutable XML tree in columnar layout (SPEC §14.1): each node is an index, in document order, into parallel
    /// arrays; attributes and namespace declarations have arrays of their own; names and values are pooled. A subtree
    /// is a contiguous range of indexes, so document order is an integer comparison, a node's next sibling is the
    /// index just past its subtree, and descendants are scanned in one pass. It reads like an <see cref="XPathDocument"/>
    /// (the same text merging, whitespace rules, namespace nodes, base URIs and DTD IDs) through the navigator
    /// <see cref="CreateNavigator"/> returns.
    /// </summary>
    public sealed partial class CompactDocument : IXPathNavigable
    {
        internal const int None = -1;

        // Nodes; 0 is the document node. _kind holds the XPathNodeType, and EmptyTag for an element written <e/>.
        internal byte[] _kind = null!;
        internal int[] _parent = null!;         // None for the document node
        internal int[] _end = null!;            // index just past the node's subtree
        internal int[] _name = null!;           // name id (elements, PIs), or None
        internal int[] _data = null!;           // elements: first attribute or None; text, comments and PIs: value id
        internal int _count;

        internal const byte KindMask = 0x0F, EmptyTag = 0x80;

        // Attributes; an element's are contiguous.
        internal int[] _attrOwner = null!;
        internal int[] _attrName = null!;
        internal int[] _attrValue = null!;      // value id
        internal int _attrCount;

        // Namespace declarations, in document order; an element's are contiguous.
        internal int[] _nsOwner = null!;
        internal string[] _nsPrefix = null!;
        internal string[] _nsUri = null!;
        internal int _nsCount;

        // Names, atomized in _nameTable; values, each distinct short one once.
        internal string[] _local = null!;
        internal string[] _uri = null!;
        internal string[] _prefix = null!;
        internal string[] _values = null!;

        // The expanded name (namespace and local name, whatever the prefix) of each name id, and the ids by name, so
        // that a name test compares integers.
        internal int[] _expanded = null!;
        private Dictionary<string, List<(string Uri, int Expanded)>> _expandedIds = null!;

        // The shared names of a writer-built (temporary) tree, which then has no tables of its own (SPEC §14.2).
        internal NamePool? _pool;
        internal const int Absent = -2;

        internal XmlNameTable _nameTable = null!;
        internal string _baseUri = "";
        internal Dictionary<int, string>? _baseUris;   // elements and PIs whose base URI differs from their parent's
        internal Dictionary<string, int>? _ids;        // DTD ID value -> element
        internal string _xmlPrefix = null!, _xmlUri = null!;

        /// <summary>Built from writer events (a temporary tree): elements and PIs have an empty base URI, as there.</summary>
        internal bool _writerBuilt;

        /// <summary>
        /// Text nodes written with disable-output-escaping (XSLT 3.0 §26.2): their parts that are written unescaped
        /// when the node reaches a result tree by sequence normalization (xsl:copy-of drops the marking).
        /// </summary>
        internal Dictionary<int, List<(int Start, int Length)>>? _rawText;

        /// <summary>A constructed tree, which has no document URI (fn:document-uri of its root is empty).</summary>
        internal bool NoDocumentUri { get; set; }

        private CompactDocument()
        {
        }

        /// <summary>Builds a tree from a reader, as <c>new XPathDocument(reader, space)</c> would.</summary>
        public static CompactDocument Load(XmlReader reader, XmlSpace space = XmlSpace.Default)
        {
            ArgumentNullException.ThrowIfNull(reader);
            var builder = new Builder(reader.NameTable, reader.BaseURI);
            builder.Read(reader, space);
            return builder.Finish();
        }

        /// <summary>Builds a tree from a document at a URI, with DTDs prohibited, as <c>new XPathDocument(uri)</c> would.</summary>
        public static CompactDocument Load(string uri, XmlSpace space = XmlSpace.Default)
        {
            using XmlReader reader = XmlReader.Create(uri, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            return Load(reader, space);
        }

        /// <summary>Builds a tree from text, as <c>new XPathDocument(textReader)</c> would.</summary>
        public static CompactDocument Load(TextReader text, XmlSpace space = XmlSpace.Default)
        {
            using XmlReader reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            return Load(reader, space);
        }

        public XPathNavigator CreateNavigator() => new CompactNavigator(this);

        /// <summary>
        /// Where a navigator starts and MoveToRoot goes: the document node, a fragment's top node, or the parentless
        /// attribute or namespace node a fragment can consist of.
        /// </summary>
        internal (int Node, int Attr, bool Namespace) Root =>
            _count > 0 ? (0, None, false) : _attrCount > 0 ? (None, 0, false) : (None, None, true);

        /// <summary>The one namespace node of a fragment that is a parentless namespace node.</summary>
        internal (string, string, int, int)[] RootNamespace => _rootNamespace ??= new[] { (_nsPrefix[0], _nsUri[0], None, 1) };

        private (string, string, int, int)[]? _rootNamespace;

        /// <summary>The number of nodes, attributes and namespace declarations the tree holds.</summary>
        public int NodeCount => _count + _attrCount + _nsCount;

        internal int Kind(int node) => _kind[node] & KindMask;

        internal bool IsText(int node) => (_kind[node] & KindMask) is (int)XPathNodeType.Text or (int)XPathNodeType.SignificantWhitespace or (int)XPathNodeType.Whitespace;

        /// <summary>The next sibling: the node just past this one's subtree, if that is still inside the parent's.</summary>
        internal int Next(int node)
        {
            int parent = _parent[node];
            if (parent == None)
            {
                return None;
            }
            int next = _end[node];
            return next < _end[parent] ? next : None;
        }

        internal string LeafValue(int node) => _values[_data[node]];

        /// <summary>The string value of a document or element node: its text descendants, concatenated.</summary>
        internal string StringValue(int node)
        {
            int end = _end[node];
            int first = node + 1;
            // The common case: a single text child.
            if (first < end && IsText(first) && _end[first] == end)
            {
                return LeafValue(first);
            }
            string s = string.Empty;
            StringBuilder? sb = null;
            for (int i = first; i < end; i++)
            {
                if (IsText(i))
                {
                    if (s.Length == 0)
                    {
                        s = LeafValue(i);
                    }
                    else
                    {
                        (sb ??= new StringBuilder(s)).Append(LeafValue(i));
                    }
                }
            }
            return sb?.ToString() ?? s;
        }

        /// <summary>The base URI of an element, PI or the document node; other nodes have their parent's.</summary>
        internal string BaseUri(int node)
        {
            if (_writerBuilt)
            {
                // As XPathDocumentBuilder records them from writer events: the document node has the tree's base URI,
                // elements and PIs none; other nodes take their nearest such ancestor's.
                for (int n = node; n != None; n = _parent[n])
                {
                    switch (Kind(n))
                    {
                        case (int)XPathNodeType.Element:
                        case (int)XPathNodeType.ProcessingInstruction:
                            return string.Empty;
                        case (int)XPathNodeType.Root:
                            return _baseUri;
                    }
                }
                return string.Empty;
            }
            if (_baseUris != null)
            {
                for (int n = node; n != None; n = _parent[n])
                {
                    if (_baseUris.TryGetValue(n, out string? uri))
                    {
                        return uri;
                    }
                }
            }
            return _baseUri;
        }

        /// <summary>
        /// The in-scope namespaces of an element, as XPathDocument orders them: the declarations of the nearest
        /// ancestor-or-self that has any, last to first, then that element's parent's in-scope namespaces less those it
        /// redeclares, ending with xml. Owner is the declaring element (None for xml), so a namespace is local to the
        /// element when it is the owner; Decl is the declaration's index (None for xml), its place in document order.
        /// </summary>
        internal (string Prefix, string Uri, int Owner, int Decl)[] InScopeNamespaces(int element)
        {
            for (int n = element; n != None; n = _parent[n])
            {
                if (_scopes != null && _scopes.TryGetValue(n, out var scope))
                {
                    return scope;
                }
                if (_noInherit != null && _parent[n] != None && _noInherit.Contains(_parent[n]))
                {
                    // Xsl3: a child of an element made with inherit-namespaces="no" has only the namespaces it needs.
                    var needed = NeededNamespaces(n);
                    (_scopes ??= new Dictionary<int, (string, string, int, int)[]>())[n] = needed;
                    return needed;
                }
            }
            return _xmlOnly;
        }

        // Xsl3: the elements made with inherit-namespaces="no" (XSLT 3.0 §11.1.2).
        internal HashSet<int>? _noInherit;

        // The namespaces an element's own name and its attributes' names need, with the xml namespace.
        private (string, string, int, int)[] NeededNamespaces(int element)
        {
            var list = new List<(string, string, int, int)>();
            void Need(int name)
            {
                if (name == None)
                {
                    return;
                }
                string prefix = _prefix[name], uri = _uri[name];
                if ((prefix.Length != 0 || uri.Length != 0) && prefix != "xml" && !list.Exists(ns => (object)ns.Item1 == (object)prefix || ns.Item1 == prefix))
                {
                    list.Add((prefix, uri, element, 0));
                }
            }
            Need(_name[element]);
            for (int a = _data[element]; a != None && a < _attrCount && _attrOwner[a] == element; a++)
            {
                if (_prefix[_attrName[a]].Length != 0)
                {
                    Need(_attrName[a]);
                }
            }
            list.Add(_xmlOnly[0]);
            return list.ToArray();
        }

        // The in-scope namespaces of each element that declares any (built with the tree, parents first).
        internal Dictionary<int, (string, string, int, int)[]>? _scopes;
        internal (string, string, int, int)[] _xmlOnly = null!;

        /// <summary>
        /// Builds each declaring element's in-scope namespaces as XPathDocumentBuilder links its namespace nodes: each
        /// declaration in turn goes to the front of the chain, and when it overrides a prefix the nodes before the
        /// overridden one are copied (after the new node, keeping their owners) to bypass it. Decl numbers the nodes in
        /// the order they are made, the xml namespace first, which is their document order.
        /// </summary>
        internal void BuildScopes()
        {
            _xmlOnly = new[] { (_xmlPrefix, _xmlUri, None, 0) };
            if (_nsCount == 0)
            {
                return;
            }
            _scopes = new Dictionary<int, (string, string, int, int)[]>();
            int location = 1;
            int i = 0;
            while (i < _nsCount)
            {
                int element = _nsOwner[i];
                if (element == None)
                {
                    i++;
                    continue;
                }
                var chain = new List<(string Prefix, string Uri, int Owner, int Decl)>(
                    _noInherit != null && _noInherit.Contains(_parent[element]) ? NeededNamespaces(element) : InScopeNamespaces(_parent[element]));
                for (; i < _nsCount && _nsOwner[i] == element; i++)
                {
                    var added = (_nsPrefix[i], _nsUri[i], element, location++);
                    int overridden = chain.FindIndex(ns => (object)ns.Prefix == (object)_nsPrefix[i]);
                    if (overridden < 0)
                    {
                        chain.Insert(0, added);
                    }
                    else
                    {
                        var rebuilt = new List<(string, string, int, int)>(chain.Count + 1) { added };
                        for (int k = 0; k < overridden; k++)
                        {
                            rebuilt.Add((chain[k].Prefix, chain[k].Uri, chain[k].Owner, location++));
                        }
                        rebuilt.AddRange(chain.GetRange(overridden + 1, chain.Count - overridden - 1));
                        chain = rebuilt;
                    }
                }
                _scopes[element] = chain.ToArray();
            }
        }

        internal void BuildExpandedNames()
        {
            _expanded = new int[_local.Length];
            _expandedIds = new Dictionary<string, List<(string, int)>>(StringComparer.Ordinal);
            int next = 0;
            for (int i = 0; i < _local.Length; i++)
            {
                if (!_expandedIds.TryGetValue(_local[i], out var list))
                {
                    _expandedIds[_local[i]] = list = new List<(string, int)>(1);
                }
                int found = Absent;
                foreach (var (uri, id) in list)
                {
                    if (uri == _uri[i])
                    {
                        found = id;
                        break;
                    }
                }
                if (found == Absent)
                {
                    found = next++;
                    list.Add((_uri[i], found));
                }
                _expanded[i] = found;
            }
        }

        /// <summary>The expanded-name id of a local name and namespace, or Absent when no node in the tree has it.</summary>
        internal int ExpandedId(string localName, string namespaceUri)
        {
            if (_pool != null)
            {
                return _pool.ExpandedId(localName, namespaceUri);
            }
            if (_expandedIds.TryGetValue(localName, out var list))
            {
                foreach (var (uri, id) in list)
                {
                    if (uri == namespaceUri)
                    {
                        return id;
                    }
                }
            }
            return Absent;
        }

        /// <summary>The element whose DTD ID is the value, or None.</summary>
        internal int LookupId(string id)
        {
            if (_ids == null && _idsFrom != null)
            {
                _ids = IdsFrom(_idsFrom);
                _idsFrom = null;
            }
            return _ids != null && _ids.TryGetValue(id, out int element) ? element : None;
        }

        /// <summary>
        /// Xsl3: the tree this one copies with whitespace stripped (XSLT 3.0 §4.3), whose ID attributes (its DTD's,
        /// which the copy can't see) are found on first use: the elements of the two trees are the same, in order.
        /// </summary>
        internal System.Xml.XPath.XPathNavigator? _idsFrom;

        internal void SetIdSource(System.Xml.XPath.XPathNavigator source)
        {
            if (_ids == null)
            {
                _idsFrom = source.Clone();
            }
        }

        private Dictionary<string, int>? IdsFrom(System.Xml.XPath.XPathNavigator source)
        {
            Dictionary<string, int>? ids = null;
            System.Xml.XPath.XPathNavigator root = source.Clone();
            root.MoveToRoot();
            System.Xml.XPath.XPathNodeIterator elements = root.SelectDescendants(System.Xml.XPath.XPathNodeType.Element, matchSelf: false);
            int element = 0;
            System.Xml.XPath.XPathNavigator probe = root.Clone();
            while (elements.MoveNext())
            {
                // The copy's next element, in document order.
                do
                {
                    element++;
                }
                while (element < _kind.Length && Kind(element) != (int)System.Xml.XPath.XPathNodeType.Element);
                System.Xml.XPath.XPathNavigator e = elements.Current!;
                System.Xml.XPath.XPathNavigator attribute = e.Clone();
                if (!attribute.MoveToFirstAttribute())
                {
                    continue;
                }
                do
                {
                    // An attribute is an ID if the source finds its element by its value.
                    string value = attribute.Value;
                    if (value.Length > 0 && probe.MoveToId(value) && probe.IsSamePosition(e))
                    {
                        (ids ??= new Dictionary<string, int>()).TryAdd(value, element);
                    }
                }
                while (attribute.MoveToNextAttribute());
            }
            return ids;
        }
    }
}
