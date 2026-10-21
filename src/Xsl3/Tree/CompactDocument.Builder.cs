// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Tree
{
    public sealed partial class CompactDocument
    {
        /// <summary>
        /// Builds a compact tree from reader events, following InternalXPathDocument.LoadFromReader and
        /// XPathDocumentBuilder: adjacent text, CDATA and whitespace merge into one text node (Text over significant
        /// whitespace over whitespace), top-level whitespace is dropped, xml:id values are collapsed, and DTD ID
        /// attributes are indexed.
        /// </summary>
        internal sealed class Builder
        {
            private readonly CompactDocument _doc = new();
            private readonly XmlNameTable _nameTable;

            // Names by (atomized) local name, then namespace and prefix.
            private readonly Dictionary<string, List<(string Uri, string Prefix, int Id)>>? _names;
            private readonly List<string>? _local, _uri, _prefix;

            // A temporary tree's names come from the pool of its name table instead.
            private readonly NamePool? _pool;

            // Values: each distinct short one once.
            private string[] _values;
            private int _valueCount;
            private readonly Dictionary<string, int>? _shared;
            private const int ShortValue = 32;

            // Open elements, and their base URIs.
            private int[] _open;
            private string[] _openBase;
            private int _depth;

            // Text not yet made a node.
            private string? _text;
            private StringBuilder? _textMore;
            private XPathNodeType _textType;

            // DTD ID attributes: element (local, prefix) -> attribute (local, prefix); the current element's.
            private Dictionary<(string, string), (string, string)>? _idAttributes;
            private (string Local, string Prefix)? _idAttribute;

            // Xsl3: the DTD's IDREF and IDREFS attributes, by element; those of the element being built (fn:idref).
            private Dictionary<(string, string), List<(string Local, string Prefix)>>? _idrefAttributes;
            private List<(string Local, string Prefix)>? _idrefs;

            /// <param name="fragment">A tree with no document node: its root is its one top-level node (XQuery's fragments).</param>
            /// <param name="small">A tree expected to be small (a temporary tree), so the arrays start small.</param>
            public Builder(XmlNameTable nameTable, string baseUri, bool fragment = false, bool small = false)
            {
                _nameTable = nameTable;
                CompactDocument d = _doc;
                d._nameTable = nameTable;
                d._baseUri = fragment ? "" : baseUri ?? "";
                if (small)
                {
                    _pool = d._pool = NamePool.For(nameTable);
                    d._xmlPrefix = _pool.XmlPrefix;
                    d._xmlUri = _pool.XmlUri;
                    _open = new int[4];
                    _openBase = new string[4];
                }
                else
                {
                    _names = new Dictionary<string, List<(string, string, int)>>(ReferenceEqualityComparer.Instance);
                    _local = new List<string>();
                    _uri = new List<string>();
                    _prefix = new List<string>();
                    _shared = new Dictionary<string, int>(StringComparer.Ordinal);
                    d._xmlPrefix = nameTable.Add("xml");
                    d._xmlUri = nameTable.Add(XmlReservedNs.NsXml);
                    _open = new int[16];
                    _openBase = new string[16];
                }
                int capacity = small ? 8 : 1024, attributes = small ? 4 : 256;
                d._kind = new byte[capacity];
                d._parent = new int[capacity];
                d._end = new int[capacity];
                d._name = new int[capacity];
                d._data = new int[capacity];
                if (small)
                {
                    // Most temporary trees have no attributes or namespace nodes: their arrays come with the first.
                    d._attrOwner = d._attrName = d._attrValue = Array.Empty<int>();
                    d._nsOwner = Array.Empty<int>();
                    d._nsPrefix = d._nsUri = Array.Empty<string>();
                }
                else
                {
                    d._attrOwner = new int[attributes];
                    d._attrName = new int[attributes];
                    d._attrValue = new int[attributes];
                    d._nsOwner = new int[4];
                    d._nsPrefix = new string[4];
                    d._nsUri = new string[4];
                }
                // (Sized here, not by an initializer: a temporary tree would allocate the large array only to drop it.)
                _values = small ? new string[8] : new string[256];
                if (fragment)
                {
                    // Top-level nodes have no parent.
                    _open[0] = None;
                }
                else
                {
                    NewNode(XPathNodeType.Root, None, None, None);
                    _open[0] = 0;
                }
                _openBase[0] = d._baseUri;
                _depth = 1;
            }

            /// <summary>The document under construction (for writer-built trees, whose base URIs differ).</summary>
            internal CompactDocument Document => _doc;

            /// <summary>Whether an element is open (so attributes and namespaces are its, not top-level nodes).</summary>
            internal bool InElement => _open[_depth - 1] != None && _doc.Kind(_open[_depth - 1]) == (int)XPathNodeType.Element;

            public void Read(XmlReader reader, XmlSpace space, bool keepTopLevelWhitespace = false)
            {
                string? xmlnsUri = _nameTable.Get(XmlReservedNs.NsXmlNs);
                bool topLevelReader = reader.ReadState == ReadState.Initial;
                // Xsl3: the elements the DTD declares with element-only content, and whether each open one is such.
                HashSet<(string, string)>? elementOnly = null;
                bool[] inElementOnly = System.Array.Empty<bool>();
                int initialDepth = reader.Depth;
                if (topLevelReader && !reader.Read())
                {
                    return;
                }
                do
                {
                    if (!topLevelReader && reader.Depth < initialDepth)
                    {
                        return;
                    }
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Element:
                        {
                            bool empty = reader.IsEmptyElement;
                            if (elementOnly != null)
                            {
                                // Xsl3: whether the DTD gives this element element-only content (its whitespace is left out).
                                if (reader.Depth >= inElementOnly.Length)
                                {
                                    System.Array.Resize(ref inElementOnly, reader.Depth * 2 + 2);
                                }
                                inElementOnly[reader.Depth] = elementOnly.Contains((reader.LocalName, reader.Prefix));
                            }
                            StartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI, reader.BaseURI);
                            while (reader.MoveToNextAttribute())
                            {
                                string ns = reader.NamespaceURI;
                                if ((object)ns == (object?)xmlnsUri)
                                {
                                    NamespaceDeclaration(reader.Prefix.Length == 0 ? string.Empty : reader.LocalName, reader.Value);
                                }
                                else
                                {
                                    Attribute(reader.Prefix, reader.LocalName, ns, reader.Value);
                                }
                            }
                            if (empty)
                            {
                                EndElement(emptyTag: true);
                            }
                            break;
                        }
                        case XmlNodeType.EndElement:
                            EndElement(emptyTag: false);
                            break;
                        case XmlNodeType.Text:
                        case XmlNodeType.CDATA:
                            Text(reader.Value, XPathNodeType.Text);
                            break;
                        case XmlNodeType.SignificantWhitespace:
                            if (elementOnly != null && reader.Depth > 0 && reader.Depth - 1 < inElementOnly.Length && inElementOnly[reader.Depth - 1])
                            {
                                break; // element content whitespace
                            }
                            if (reader.XmlSpace == XmlSpace.Preserve)
                            {
                                Text(reader.Value, XPathNodeType.SignificantWhitespace);
                            }
                            else
                            {
                                goto case XmlNodeType.Whitespace;
                            }
                            break;
                        case XmlNodeType.Whitespace:
                            if (elementOnly != null && reader.Depth > 0 && reader.Depth - 1 < inElementOnly.Length && inElementOnly[reader.Depth - 1])
                            {
                                break; // element content whitespace
                            }
                            if (space == XmlSpace.Preserve && (keepTopLevelWhitespace || !topLevelReader || reader.Depth != 0))
                            {
                                Text(reader.Value, XPathNodeType.Whitespace);
                            }
                            break;
                        case XmlNodeType.Comment:
                            FlushText();
                            Child(XPathNodeType.Comment, None, Value(reader.Value));
                            break;
                        case XmlNodeType.ProcessingInstruction:
                        {
                            FlushText();
                            int pi = Child(XPathNodeType.ProcessingInstruction, Name(string.Empty, reader.LocalName, string.Empty), Value(reader.Value));
                            string baseUri = reader.BaseURI;
                            if (!ReferenceEquals(baseUri, _openBase[_depth - 1]) && baseUri != _openBase[_depth - 1])
                            {
                                (_doc._baseUris ??= new Dictionary<int, string>())[pi] = baseUri;
                            }
                            break;
                        }
                        case XmlNodeType.EntityReference:
                            reader.ResolveEntity();
                            break;
                        case XmlNodeType.DocumentType:
                            if (DtdInfoBridge.GetElementOnlyElements(reader) is { Count: > 0 } declared)
                            {
                                elementOnly = declared;
                                inElementOnly = new bool[16];
                            }
                            foreach (var (elementLocal, elementPrefix, attributeLocal, attributePrefix) in DtdInfoBridge.GetIdAttributes(reader))
                            {
                                (_idAttributes ??= new())[(elementLocal, elementPrefix)] = (attributeLocal, attributePrefix);
                            }
                            foreach (var (elementLocal, elementPrefix, attributeLocal, attributePrefix) in DtdInfoBridge.GetIdrefAttributes(reader))
                            {
                                _idrefAttributes ??= new();
                                if (!_idrefAttributes.TryGetValue((elementLocal, elementPrefix), out var list))
                                {
                                    _idrefAttributes[(elementLocal, elementPrefix)] = list = new();
                                }
                                list.Add((attributeLocal, attributePrefix));
                            }
                            break;
                    }
                }
                while (reader.Read());
            }

            public CompactDocument Finish()
            {
                FlushText();
                while (_depth > 0)
                {
                    int open = _open[--_depth];
                    if (open != None)
                    {
                        _doc._end[open] = _doc._count;
                    }
                }
                if (_doc._count == 0 && _doc._attrCount == 0 && _doc._nsCount == 0)
                {
                    // An empty fragment is an empty text node, as XPathDocumentBuilder makes it.
                    NewNode(XPathNodeType.Text, None, None, Value(string.Empty));
                }
                if (_pool != null)
                {
                    // The pool's arrays as they are now: they hold every name this tree uses.
                    _doc._local = _pool.Local;
                    _doc._uri = _pool.Uri;
                    _doc._prefix = _pool.Prefix;
                    _doc._expanded = _pool.Expanded;
                }
                else
                {
                    _doc._local = _local!.ToArray();
                    _doc._uri = _uri!.ToArray();
                    _doc._prefix = _prefix!.ToArray();
                }
                if (_values.Length != _valueCount && _pool == null)
                {
                    Array.Resize(ref _values, _valueCount);
                }
                _doc._values = _values;
                _doc.BuildScopes();
                if (_pool == null)
                {
                    _doc.BuildExpandedNames();
                }
                return _doc;
            }

            private int Name(string prefix, string local, string ns)
            {
                if (_pool != null)
                {
                    return _pool.Name(prefix, local, ns);
                }
                // Reader names are atomized in the reader's name table, which is the document's; atomize only on a miss.
                if (!_names!.TryGetValue(local, out var list))
                {
                    local = _nameTable.Add(local);
                    if (!_names.TryGetValue(local, out list))
                    {
                        _names[local] = list = new List<(string, string, int)>(1);
                    }
                }
                foreach (var (uri, p, id) in list)
                {
                    if (((object)uri == (object)ns || uri == ns) && ((object)p == (object)prefix || p == prefix))
                    {
                        return id;
                    }
                }
                ns = _nameTable.Add(ns);
                prefix = _nameTable.Add(prefix);
                int newId = _local!.Count;
                _local.Add(local);
                _uri!.Add(ns);
                _prefix!.Add(prefix);
                list.Add((ns, prefix, newId));
                return newId;
            }

            private int Value(string value)
            {
                if (_shared != null && value.Length <= ShortValue)
                {
                    if (_shared.TryGetValue(value, out int id))
                    {
                        return id;
                    }
                    _shared[value] = _valueCount;
                }
                if (_valueCount == _values.Length)
                {
                    Array.Resize(ref _values, _valueCount * 2);
                }
                _values[_valueCount] = value;
                return _valueCount++;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private int NewNode(XPathNodeType kind, int parent, int name, int data)
            {
                CompactDocument d = _doc;
                int i = d._count;
                if (i == d._kind.Length)
                {
                    Grow();
                }
                d._kind[i] = (byte)kind;
                d._parent[i] = parent;
                d._end[i] = i + 1;
                d._name[i] = name;
                d._data[i] = data;
                d._count = i + 1;
                return i;
            }

            private void Grow()
            {
                CompactDocument d = _doc;
                int size = d._kind.Length * 2;
                Array.Resize(ref d._kind, size);
                Array.Resize(ref d._parent, size);
                Array.Resize(ref d._end, size);
                Array.Resize(ref d._name, size);
                Array.Resize(ref d._data, size);
            }

            /// <summary>Adds a child of the innermost open node.</summary>
            private int Child(XPathNodeType kind, int name, int data) => NewNode(kind, _open[_depth - 1], name, data);

            /// <param name="baseUri">The element's base URI, or null for a writer-built tree (whose elements have none).</param>
            internal void StartElement(string prefix, string local, string ns, string? baseUri)
            {
                FlushText();
                int element = Child(XPathNodeType.Element, Name(prefix, local, ns), None);
                string parentBase = _openBase[_depth - 1];
                baseUri ??= parentBase;
                if (!ReferenceEquals(baseUri, parentBase) && baseUri != parentBase)
                {
                    (_doc._baseUris ??= new Dictionary<int, string>())[element] = baseUri;
                }
                if (_depth == _open.Length)
                {
                    Array.Resize(ref _open, _depth * 2);
                    Array.Resize(ref _openBase, _depth * 2);
                }
                _open[_depth] = element;
                _openBase[_depth] = baseUri;
                _depth++;
                _idAttribute = _idAttributes != null && _idAttributes.TryGetValue((local, prefix), out var id) ? id : null;
                _idrefs = _idrefAttributes != null && _idrefAttributes.TryGetValue((local, prefix), out var idrefs) ? idrefs : null;
            }

            /// <summary>Xsl3: gives the document node a base URI of its own (a copy of a document node keeps its original's).</summary>
            internal void SetDocumentBaseUri(string baseUri)
            {
                if (_depth != 1 || InElement || baseUri.Length == 0)
                {
                    return;
                }
                _doc._baseUri = baseUri;
                _openBase[0] = baseUri;
            }

            /// <summary>Xsl3: gives the innermost open element a base URI of its own (a copy keeps its original's).</summary>
            internal void SetElementBaseUri(string baseUri)
            {
                if (_depth == 0 || !InElement || baseUri.Length == 0)
                {
                    return;
                }
                int element = _open[_depth - 1];
                string parentBase = _depth > 1 ? _openBase[_depth - 2] : _doc._baseUri;
                if (baseUri != parentBase)
                {
                    (_doc._baseUris ??= new Dictionary<int, string>())[element] = baseUri;
                }
                _openBase[_depth - 1] = baseUri;
            }

            internal void EndElement(bool emptyTag)
            {
                FlushText();
                int element = _open[--_depth];
                _doc._end[element] = _doc._count;
                // An element written <e/>: a reader reports it; a writer allows it when the element has no children.
                if (emptyTag && _doc._count == element + 1)
                {
                    _doc._kind[element] |= EmptyTag;
                }
            }

            internal void Attribute(string prefix, string local, string ns, string value)
            {
                CompactDocument d = _doc;
                // None for a fragment's parentless (top-level) attribute.
                int element = InElement ? _open[_depth - 1] : None;
                int i = d._attrCount;
                if (i == d._attrOwner.Length)
                {
                    Array.Resize(ref d._attrOwner, Math.Max(4, i * 2));
                    Array.Resize(ref d._attrName, Math.Max(4, i * 2));
                    Array.Resize(ref d._attrValue, Math.Max(4, i * 2));
                }
                int name = Name(prefix, local, ns);
                if (IsXmlId(name))
                {
                    // xml:id processing normalizes the value as the ID type does (xml:id 1.0 §4), as the vendored builder does.
                    value = Xsl3.Xdm.Lexical.Collapse(value);
                }
                d._attrOwner[i] = element;
                d._attrName[i] = name;
                d._attrValue[i] = Value(value);
                if (element != None && d._data[element] == None)
                {
                    d._data[element] = i;
                }
                d._attrCount = i + 1;
                if (_idrefs != null && element != None && _idrefs.Contains((local, prefix)))
                {
                    (d._idrefAttributes ??= new List<int>()).Add(i);
                }
                if (_idAttribute is { } id && local == id.Local && prefix == id.Prefix)
                {
                    d._ids ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    d._ids.TryAdd(value, element);
                }
            }

            private bool IsXmlId(int name) => _pool != null
                ? _pool.Local[name] == "id" && (object)_pool.Uri[name] == (object)_doc._xmlUri
                : _local![name] == "id" && (object)_uri![name] == (object)_doc._xmlUri;

            internal void NoInherit()
            {
                if (InElement)
                {
                    (_doc._noInherit ??= new HashSet<int>()).Add(_open[_depth - 1]);
                }
            }

            internal void NamespaceDeclaration(string prefix, string uri)
            {
                CompactDocument d = _doc;
                // None for a fragment's parentless (top-level) namespace node.
                int element = InElement ? _open[_depth - 1] : None;
                if (element == None && prefix == "xml")
                {
                    // XPathDocumentBuilder lets a parentless xml namespace node override its built-in one, and the
                    // fragment is then left without a root (so it becomes an empty text node): do the same.
                    return;
                }
                int i = d._nsCount;
                if (i == d._nsOwner.Length)
                {
                    Array.Resize(ref d._nsOwner, Math.Max(4, i * 2));
                    Array.Resize(ref d._nsPrefix, Math.Max(4, i * 2));
                    Array.Resize(ref d._nsUri, Math.Max(4, i * 2));
                }
                d._nsOwner[i] = element;
                d._nsPrefix[i] = _nameTable.Add(prefix);
                d._nsUri[i] = _nameTable.Add(uri);
                d._nsCount = i + 1;
            }

            internal void Comment(string value)
            {
                FlushText();
                Child(XPathNodeType.Comment, None, Value(value));
            }

            internal void ProcessingInstruction(string target, string value)
            {
                FlushText();
                Child(XPathNodeType.ProcessingInstruction, Name(string.Empty, target, string.Empty), Value(value));
            }

            // The parts of the text being gathered that were written raw (disable-output-escaping).
            private List<(int Start, int Length)>? _textRaw;

            internal void Text(string value, XPathNodeType type, bool raw)
            {
                if (raw && value.Length > 0)
                {
                    (_textRaw ??= new()).Add(((_text?.Length ?? 0) + (_textMore?.Length ?? 0), value.Length));
                }
                Text(value, type);
            }

            internal void Text(string value, XPathNodeType type)
            {
                if (_text == null)
                {
                    _text = value;
                    _textType = type;
                    return;
                }
                (_textMore ??= new StringBuilder()).Append(value);
                if (type < _textType)
                {
                    // Text over significant whitespace over whitespace (the enum's order).
                    _textType = type;
                }
            }

            private void FlushText()
            {
                if (_text == null)
                {
                    return;
                }
                string value = _textMore is { Length: > 0 } more ? _text + more.ToString() : _text;
                _textMore?.Clear();
                _text = null;
                int node = Child(_textType, None, Value(value));
                if (_textRaw != null)
                {
                    (_doc._rawText ??= new())[node] = _textRaw;
                    _textRaw = null;
                }
            }
        }
    }
}
