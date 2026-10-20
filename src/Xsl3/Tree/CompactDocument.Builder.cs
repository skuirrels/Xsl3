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
            private readonly Dictionary<string, List<(string Uri, string Prefix, int Id)>> _names = new(ReferenceEqualityComparer.Instance);
            private readonly List<string> _local = new(), _uri = new(), _prefix = new();

            // Values: each distinct short one once.
            private string[] _values = new string[256];
            private int _valueCount;
            private readonly Dictionary<string, int> _shared = new(StringComparer.Ordinal);
            private const int ShortValue = 32;

            // Open elements, and their base URIs.
            private int[] _open = new int[16];
            private string[] _openBase = new string[16];
            private int _depth;

            // Text not yet made a node.
            private string? _text;
            private StringBuilder? _textMore;
            private XPathNodeType _textType;

            // DTD ID attributes: element (local, prefix) -> attribute (local, prefix); the current element's.
            private Dictionary<(string, string), (string, string)>? _idAttributes;
            private (string Local, string Prefix)? _idAttribute;

            public Builder(XmlNameTable nameTable, string baseUri)
            {
                _nameTable = nameTable;
                CompactDocument d = _doc;
                d._nameTable = nameTable;
                d._baseUri = baseUri ?? "";
                d._xmlPrefix = nameTable.Add("xml");
                d._xmlUri = nameTable.Add(XmlReservedNs.NsXml);
                int capacity = 1024;
                d._kind = new byte[capacity];
                d._parent = new int[capacity];
                d._end = new int[capacity];
                d._name = new int[capacity];
                d._data = new int[capacity];
                d._attrOwner = new int[256];
                d._attrName = new int[256];
                d._attrValue = new int[256];
                d._nsOwner = new int[8];
                d._nsPrefix = new string[8];
                d._nsUri = new string[8];
                // The document node.
                NewNode(XPathNodeType.Root, None, None, None);
                _open[0] = 0;
                _openBase[0] = d._baseUri;
                _depth = 1;
            }

            public void Read(XmlReader reader, XmlSpace space, bool keepTopLevelWhitespace = false)
            {
                string? xmlnsUri = _nameTable.Get(XmlReservedNs.NsXmlNs);
                bool topLevelReader = reader.ReadState == ReadState.Initial;
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
                            foreach (var (elementLocal, elementPrefix, attributeLocal, attributePrefix) in DtdInfoBridge.GetIdAttributes(reader))
                            {
                                (_idAttributes ??= new())[(elementLocal, elementPrefix)] = (attributeLocal, attributePrefix);
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
                    _doc._end[_open[--_depth]] = _doc._count;
                }
                _doc._local = _local.ToArray();
                _doc._uri = _uri.ToArray();
                _doc._prefix = _prefix.ToArray();
                Array.Resize(ref _values, _valueCount);
                _doc._values = _values;
                _doc.BuildScopes();
                _doc.BuildExpandedNames();
                return _doc;
            }

            private int Name(string prefix, string local, string ns)
            {
                // Reader names are atomized in the reader's name table, which is the document's; atomize only on a miss.
                if (!_names.TryGetValue(local, out var list))
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
                int newId = _local.Count;
                _local.Add(local);
                _uri.Add(ns);
                _prefix.Add(prefix);
                list.Add((ns, prefix, newId));
                return newId;
            }

            private int Value(string value)
            {
                if (value.Length <= ShortValue)
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

            private void StartElement(string prefix, string local, string ns, string baseUri)
            {
                FlushText();
                int element = Child(XPathNodeType.Element, Name(prefix, local, ns), None);
                string parentBase = _openBase[_depth - 1];
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
            }

            private void EndElement(bool emptyTag)
            {
                FlushText();
                int element = _open[--_depth];
                _doc._end[element] = _doc._count;
                if (emptyTag)
                {
                    _doc._kind[element] |= EmptyTag;
                }
            }

            private void Attribute(string prefix, string local, string ns, string value)
            {
                CompactDocument d = _doc;
                int element = _open[_depth - 1];
                int i = d._attrCount;
                if (i == d._attrOwner.Length)
                {
                    Array.Resize(ref d._attrOwner, i * 2);
                    Array.Resize(ref d._attrName, i * 2);
                    Array.Resize(ref d._attrValue, i * 2);
                }
                int name = Name(prefix, local, ns);
                if ((object)_local[name] == (object)"id" && (object)_uri[name] == (object)d._xmlUri)
                {
                    // xml:id processing normalizes the value as the ID type does (xml:id 1.0 §4), as the vendored builder does.
                    value = Xsl3.Xdm.Lexical.Collapse(value);
                }
                d._attrOwner[i] = element;
                d._attrName[i] = name;
                d._attrValue[i] = Value(value);
                if (d._data[element] == None)
                {
                    d._data[element] = i;
                }
                d._attrCount = i + 1;
                if (_idAttribute is { } id && local == id.Local && prefix == id.Prefix)
                {
                    d._ids ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    d._ids.TryAdd(value, element);
                }
            }

            private void NamespaceDeclaration(string prefix, string uri)
            {
                CompactDocument d = _doc;
                int element = _open[_depth - 1];
                int i = d._nsCount;
                if (i == d._nsOwner.Length)
                {
                    Array.Resize(ref d._nsOwner, i * 2);
                    Array.Resize(ref d._nsPrefix, i * 2);
                    Array.Resize(ref d._nsUri, i * 2);
                }
                d._nsOwner[i] = element;
                d._nsPrefix[i] = _nameTable.Add(prefix);
                d._nsUri[i] = _nameTable.Add(uri);
                d._nsCount = i + 1;
            }

            private void Text(string value, XPathNodeType type)
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
                Child(_textType, None, Value(value));
            }
        }
    }
}
