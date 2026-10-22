// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Xsl3: the serialization parameters of an XSLT 2.0+ xsl:output that XmlWriterSettings can't express
    /// (XSLT 3.0 §26, Serialization 3.1): the xhtml method, character maps, Unicode normalization and
    /// include-content-type. They are applied by wrapping the result writer.
    /// </summary>
    internal sealed class OutputExtras
    {
        /// <summary>method="xhtml": XML syntax, with the XHTML rules for empty elements and the meta element.</summary>
        public bool Xhtml { get; set; }

        public bool IncludeContentType { get; set; } = true;

        /// <summary>The encoding as the output definition names it (meta element, XML declaration).</summary>
        public string Encoding { get; set; } = "UTF-8";

        /// <summary>The media type of the meta element of an XHTML head.</summary>
        public string MediaType { get; set; } = "text/html";

        public NormalizationForm? Normalization { get; set; }

        /// <summary>normalization-form="fully-normalized": also SERE0012 for text that starts with a combining character.</summary>
        public bool FullyNormalized { get; set; }

        /// <summary>The character map (use-character-maps), code point to string.</summary>
        public IReadOnlyDictionary<int, string>? CharacterMap { get; set; }

        /// <summary>escape-uri-attributes (the html method's own serializer, <see cref="HtmlSerializer"/>).</summary>
        public bool EscapeUriAttributes { get; set; } = true;

        /// <summary>"json" or "adaptive": the raw result sequence is serialized by <see cref="ItemSerializer"/>.</summary>
        public string? RawMethod { get; set; }

        public string? ItemSeparator { get; set; }

        public string? JsonNodeOutputMethod { get; set; }

        public bool AllowDuplicateNames { get; set; }

        /// <summary>
        /// The adaptive method: nodes are written with an XML declaration, since omit-xml-declaration (which the
        /// method passes to the xml method, Serialization 3.1 §10.1.6) is "no". Not for fn:serialize, whose default is "yes".
        /// </summary>
        public bool AdaptiveXmlDeclaration { get; set; }

        public bool Indent { get; set; }

        /// <summary>HTML5 serialization (html-version 5): its void elements, and &lt;!DOCTYPE html&gt; by default.</summary>
        public bool Html5 { get; set; }

        /// <summary>With <see cref="Html5"/>: the html method (not xhtml).</summary>
        public bool HtmlMethod { get; set; }

        /// <summary>With <see cref="Html5"/>: no doctype-system, so &lt;!DOCTYPE html&gt; precedes the html element.</summary>
        public bool DefaultDoctype { get; set; }

        public OutputExtras Clone() => (OutputExtras)MemberwiseClone();

        /// <summary>
        /// Xsl3: standalone is given (xml method): the result must be a well-formed document, one element and no text at
        /// the top level, else SEPM0004 (Serialization 3.1 §5.1).
        /// </summary>
        public bool SingleRoot { get; set; }

        public bool IsEmpty => !Xhtml && !Html5 && !SingleRoot && RawMethod == null && EscapeUriAttributes && IncludeContentType && Normalization == null && (CharacterMap == null || CharacterMap.Count == 0);

        /// <param name="settings">The writer's settings as they were given (its own Settings lose the cdata-section-elements).</param>
        public XmlWriter Wrap(XmlWriter writer, XmlWriterSettings? settings = null) => IsEmpty ? writer : new SerializationWriter(writer, this, settings);

        /// <summary>A normalization-form value as a .NET form; null for "none" (SESU0011 for unsupported forms).</summary>
        public static bool TryParseNormalizationForm(string value, out NormalizationForm? form)
        {
            switch (value.Trim())
            {
                case "NFC": case "fully-normalized": form = NormalizationForm.FormC; return true;
                case "NFD": form = NormalizationForm.FormD; return true;
                case "NFKC": form = NormalizationForm.FormKC; return true;
                case "NFKD": form = NormalizationForm.FormKD; return true;
                case "none": form = null; return true;
                default: form = null; return false;
            }
        }
    }

    /// <summary>The writer <see cref="OutputExtras"/> puts in front of the result writer.</summary>
    /// <summary>Xsl3: tells the html method's writers that the element being started is an HTML5 void element.</summary>
    internal static class HtmlVoidElements
    {
        [System.ThreadStatic]
        public static bool Html5;
    }

    internal sealed class SerializationWriter : XmlWriter
    {
        private const string XhtmlNs = "http://www.w3.org/1999/xhtml";
        private const string SvgNs = "http://www.w3.org/2000/svg";
        private const string MathMLNs = "http://www.w3.org/1998/Math/MathML";

        // The elements written with the minimized syntax (<br />) by the xhtml method: those declared EMPTY in the
        // XHTML 1.0 DTDs (the default html-version of XSLT 2.0). Others get an end tag.
        private static readonly HashSet<string> s_voidElements = new(System.StringComparer.Ordinal)
        {
            "area", "base", "basefont", "br", "col", "frame", "hr", "img", "input", "isindex", "link", "meta", "param",
        };

        // The void elements of HTML5 (Serialization 3.1 §7.1).
        private static readonly HashSet<string> s_voidElements5 = new(System.StringComparer.Ordinal)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "keygen", "link", "meta", "param", "source", "track", "wbr",
        };

        // The HTML5 void elements the html method's writer doesn't know as empty (HTML 4.01).
        internal static readonly HashSet<string> NewVoidElements5 = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "embed", "keygen", "source", "track", "wbr",
        };

        private readonly XmlWriter _inner;
        private readonly OutputExtras _extras;
        private bool _seenElement;
        private bool OwnHtml => _inner is HtmlSerializer; // which does HTML5's doctype and void elements itself
        private readonly Stack<bool> _void = new();
        private readonly Stack<bool> _cdata = new(); // in a cdata-section-elements element
        private bool _inAttribute;
        private bool _pendingHead;

        /// <param name="ownCData">The writer does cdata-section-elements itself (the inner writer's settings have none).</param>
        public SerializationWriter(XmlWriter inner, OutputExtras extras, XmlWriterSettings? settings = null, bool ownCData = false)
        {
            _inner = inner;
            _extras = extras;
            _settings = settings ?? inner.Settings;
            _ownCData = ownCData;
        }

        private readonly bool _ownCData;
        private bool _textStarted; // text has been written since the last element, comment or the like
        private StringBuilder? _cdataText;

        private readonly XmlWriterSettings? _settings;

        // Before content of an XHTML head: the meta element that declares the content type.
        private void Content(bool text = false)
        {
            if (!text)
            {
                FlushCData();
                _textStarted = false;
            }
            FlushMeta();
            if (_pendingHead && !_inAttribute)
            {
                _pendingHead = false;
                // Its attributes are written like the stylesheet's (a character map applies to them).
                _inner.WriteStartElement("meta", XhtmlNs);
                foreach (var (name, value) in new[] { ("http-equiv", "Content-Type"), ("content", _extras.MediaType + "; charset=" + _extras.Encoding) })
                {
                    WriteStartAttribute(null, name, null);
                    WriteString(value);
                    WriteEndAttribute();
                }
                _inner.WriteEndElement();
            }
        }

        // A meta element of an XHTML head, held back until its attributes show whether it declares the content type:
        // with include-content-type, the serializer's own meta element replaces it (Serialization 3.1 §6).
        private sealed class PendingMeta(string? prefix, string local, string? ns)
        {
            public readonly string? Prefix = prefix, Ns = ns;
            public readonly string Local = local;
            public readonly List<(string? Prefix, string Local, string? Ns, StringBuilder Value)> Attributes = new();
            public StringBuilder? Current;

            public bool DeclaresContentType => Attributes.Exists(a => string.IsNullOrEmpty(a.Ns) && a.Local.Equals("http-equiv", System.StringComparison.OrdinalIgnoreCase) &&
                a.Value.ToString().Trim().Equals("Content-Type", System.StringComparison.OrdinalIgnoreCase));
        }

        private PendingMeta? _meta;
        private int _depth, _headDepth = -1; // the open elements; how many are open inside the XHTML head, or -1

        private void FlushMeta()
        {
            if (_meta is not { Current: null } m)
            {
                return;
            }
            _meta = null;
            _inner.WriteStartElement(m.Prefix, m.Local, m.Ns);
            foreach (var (prefix, local, ns, value) in m.Attributes)
            {
                WriteStartAttribute(prefix, local, ns);
                WriteString(value.ToString());
                WriteEndAttribute();
            }
        }

        private string Normalize(string text) =>
            _extras.Normalization is NormalizationForm form && !text.IsNormalized(form) ? text.Normalize(form) : text;

        public override void WriteString(string? text)
        {
            if (_extras.SingleRoot && _depth == 0 && !string.IsNullOrWhiteSpace(text))
            {
                throw new Xsl3.Xdm.XdmException("SEPM0004", "With standalone or doctype-system, the result can't have text at the top level");
            }
            if (_meta?.Current is StringBuilder metaValue)
            {
                metaValue.Append(text);
                return;
            }
            if (_namespaceDeclaration is var (_, declaration))
            {
                declaration.Append(text);
                return;
            }
            Content(text: true);
            if (string.IsNullOrEmpty(text))
            {
                _inner.WriteString(text);
                return;
            }
            IReadOnlyDictionary<int, string>? map = _extras.CharacterMap;
            if (map != null && map.Count > 0 && !(_inAttribute ? _uriAttribute || _ownUriAttribute || _declaration : _cdata.Count > 0 && _cdata.Peek()))
            {
                // Character maps (Serialization 3.1 §11): the characters to map are found before normalization, and
                // their strings are written as they are, neither normalized nor escaped. They don't apply to the
                // text of CDATA sections, to URI attributes that are escaped, or to namespace declarations.
                int start = 0;
                for (int i = 0; i < text.Length; i++)
                {
                    int width = char.IsSurrogatePair(text, i) ? 2 : 1;
                    if (map.TryGetValue(width == 2 ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i], out string? replacement))
                    {
                        if (i > start)
                        {
                            WriteUnmapped(text[start..i]);
                        }
                        _inner.WriteRaw(replacement);
                        start = i + width;
                    }
                    i += width - 1;
                }
                if (start < text.Length)
                {
                    WriteUnmapped(start == 0 ? text : text[start..]);
                }
                return;
            }
            WriteUnmapped(text);
        }

        // Text that no character map applies to: normalized, then URI-escaped, split around unencodable characters
        // in CDATA sections, or with C1 controls as references (xhtml).
        private void WriteUnmapped(string text)
        {
            if (_extras.FullyNormalized && !_inAttribute && !_textStarted && text.Length > 0 &&
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(text, 0) is System.Globalization.UnicodeCategory.NonSpacingMark
                    or System.Globalization.UnicodeCategory.SpacingCombiningMark or System.Globalization.UnicodeCategory.EnclosingMark)
            {
                throw new Xdm.XdmException("SERE0012", "With normalization-form=\"fully-normalized\", a text node can't start with a combining character");
            }
            _textStarted = !_inAttribute;
            text = Normalize(text);
            if (_inAttribute && _uriAttribute)
            {
                text = EscapeUri(text);
            }
            if (!_inAttribute && _cdata.Count > 0 && _cdata.Peek())
            {
                if (_ownCData)
                {
                    // Adjacent text is one CDATA section, written when something else comes.
                    (_cdataText ??= new StringBuilder()).Append(text);
                    return;
                }
                if (FirstUnencodable(text) >= 0)
                {
                    WriteCDataEncoded(text);
                    return;
                }
            }
            if (_extras.Xhtml && text.AsSpan().IndexOfAnyInRange('', '') >= 0)
            {
                // xhtml: C1 control characters as character references.
                int from = 0;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] is >= '' and <= '')
                    {
                        if (i > from)
                        {
                            WriteEscaped(text[from..i]);
                        }
                        _inner.WriteCharEntity(text[i]);
                        from = i + 1;
                    }
                }
                if (from < text.Length)
                {
                    WriteEscaped(text[from..]);
                }
                return;
            }
            WriteEscaped(text);
        }

        // Text of a cdata-section-elements element as CDATA sections. A character the encoding lacks ends the section
        // and is written as a character reference (Serialization 3.1 §5.1.4), then a new section starts.
        private void WriteCDataEncoded(string text)
        {
            if (FirstUnencodable(text) < 0)
            {
                _inner.WriteCData(text);
                return;
            }
            int from = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int width = char.IsSurrogatePair(text, i) ? 2 : 1;
                if (!CanEncode(text.AsSpan(i, width)))
                {
                    if (i > from)
                    {
                        _inner.WriteCData(text[from..i]);
                    }
                    if (width == 2)
                    {
                        _inner.WriteSurrogateCharEntity(text[i + 1], text[i]);
                    }
                    else
                    {
                        _inner.WriteCharEntity(text[i]);
                    }
                    from = i + width;
                }
                i += width - 1;
            }
            if (from < text.Length)
            {
                _inner.WriteCData(text[from..]);
            }
        }

        private void FlushCData()
        {
            if (_cdataText is { Length: > 0 })
            {
                string text = _cdataText.ToString();
                _cdataText.Clear();
                WriteCDataEncoded(text);
            }
        }

        // xhtml: a quotation mark in an attribute value as a character reference (&#34;), which HTML parsers also read.
        private void WriteEscaped(string text)
        {
            if (!(_inAttribute && _extras.Xhtml) || !text.Contains('"'))
            {
                _inner.WriteString(text);
                return;
            }
            string[] parts = text.Split('"');
            for (int k = 0; k < parts.Length; k++)
            {
                if (k > 0)
                {
                    _inner.WriteRaw("&#34;");
                }
                if (parts[k].Length > 0)
                {
                    _inner.WriteString(parts[k]);
                }
            }
        }

        // Xsl3: the top-level elements written so far (SingleRoot).
        private int _roots;

        public override void WriteStartElement(string? prefix, string localName, string? ns)
        {
            if (_extras.SingleRoot && _depth == 0 && ++_roots > 1)
            {
                throw new Xsl3.Xdm.XdmException("SEPM0004", "With standalone or doctype-system, the result must have a single document element");
            }
            Content();
            if (_extras.Xhtml && _extras.IncludeContentType && _depth == _headDepth && localName == "meta" && (ns == XhtmlNs || _extras.Html5 && string.IsNullOrEmpty(ns)))
            {
                _meta = new PendingMeta(prefix, localName, ns);
                _depth++;
                _cdata.Push(false);
                _void.Push(true);
                _currentXhtml = _currentHtml = true;
                return;
            }
            _depth++;
            if (!_seenElement)
            {
                _seenElement = true;
                // Only for an HTML html element (an html element in another namespace isn't HTML: Serialization 3.1 §6).
                if (_extras.Html5 && _extras.DefaultDoctype && !OwnHtml && string.Equals(localName, "html", System.StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(ns) || ns == XhtmlNs))
                {
                    _inner.WriteRaw($"<!DOCTYPE {localName}>"); // the html writer would write "<!DOCTYPE html >"
                }
            }
            if (_extras.Html5 && _extras.HtmlMethod && !OwnHtml && string.IsNullOrEmpty(ns) && NewVoidElements5.Contains(localName))
            {
                // The html method's writer reads this while it starts the element.
                HtmlVoidElements.Html5 = true;
                try
                {
                    _inner.WriteStartElement(prefix, localName, ns);
                }
                finally
                {
                    HtmlVoidElements.Html5 = false;
                }
            }
            else
            {
                if (_extras.Xhtml && _extras.Html5 && !string.IsNullOrEmpty(prefix) && ns is XhtmlNs or SvgNs or MathMLNs)
                {
                    // Prefix normalization (Serialization 3.1 §6, HTML5): XHTML, SVG and MathML elements are unprefixed.
                    prefix = "";
                }
                _inner.WriteStartElement(prefix, localName, ns);
            }
            // With HTML5, elements in no namespace are serialized like XHTML's.
            _cdata.Push(_settings?.CDataSectionElements is { Count: > 0 } cdata && cdata.Exists(q => q.Name == localName && q.Namespace == (ns ?? "")));
            _currentHtml = string.IsNullOrEmpty(ns) || ns == XhtmlNs;
            bool xhtml = _extras.Xhtml && (ns == XhtmlNs || _extras.Html5 && string.IsNullOrEmpty(ns));
            _currentXhtml = xhtml;
            _void.Push(!_extras.Xhtml || xhtml && (_extras.Html5 ? s_voidElements5 : s_voidElements).Contains(localName));
            if (xhtml && localName == "head" && _extras.IncludeContentType)
            {
                _pendingHead = true;
                _headDepth = _depth;
            }
        }

        public override void WriteEndElement()
        {
            if (DroppedMeta())
            {
                return;
            }
            Content();
            EndDepth();
            if (_cdata.Count > 0)
            {
                _cdata.Pop();
            }
            bool minimize = _void.Count == 0 || _void.Pop();
            if (minimize)
            {
                _inner.WriteEndElement();
            }
            else
            {
                _inner.WriteFullEndElement();
            }
        }

        public override void WriteFullEndElement()
        {
            if (DroppedMeta())
            {
                return;
            }
            Content();
            EndDepth();
            if (_cdata.Count > 0)
            {
                _cdata.Pop();
            }
            if (_void.Count > 0)
            {
                _void.Pop();
            }
            _inner.WriteFullEndElement();
        }

        // The end of a held-back meta element that declared the content type: it isn't written.
        private bool DroppedMeta()
        {
            if (_meta is not { Current: null, DeclaresContentType: true })
            {
                return false;
            }
            _meta = null;
            _depth--;
            _cdata.Pop();
            _void.Pop();
            return true;
        }

        private void EndDepth()
        {
            if (_depth == _headDepth)
            {
                _headDepth = -1;
            }
            _depth--;
        }

        public override void WriteStartAttribute(string? prefix, string localName, string? ns)
        {
            if (_meta is { Current: null } pending)
            {
                pending.Current = new StringBuilder();
                pending.Attributes.Add((prefix, localName, ns, pending.Current));
                return;
            }
            if (_extras.Xhtml && _extras.Html5 && prefix == "xmlns" && ns == "http://www.w3.org/2000/xmlns/")
            {
                // A prefixed declaration: dropped if prefix normalization unprefixes its namespace's elements.
                _namespaceDeclaration = (localName, new System.Text.StringBuilder());
                return;
            }
            _inAttribute = true;
            // xhtml: URI attributes of XHTML elements are escaped like the html method's (Serialization 3.1 §7.3).
            _uriAttribute = _extras.Xhtml && _extras.EscapeUriAttributes && string.IsNullOrEmpty(ns) && _currentXhtml && HtmlUriAttributes.Contains(localName);
            // The html method's serializer escapes the URI attributes of HTML elements itself.
            _ownUriAttribute = OwnHtml && _extras.EscapeUriAttributes && string.IsNullOrEmpty(ns) && _currentHtml && HtmlUriAttributes.Contains(localName);
            _declaration = ns == "http://www.w3.org/2000/xmlns/";
            _inner.WriteStartAttribute(prefix, localName, ns);
        }

        private bool _uriAttribute;
        private bool _ownUriAttribute, _declaration;
        private bool _currentHtml; // the element being started is an HTML element (no namespace, or XHTML's)

        // The output encoding with an exception fallback, for testing characters (null for the Unicode encodings).
        private Encoding? _strictEncoding;
        private bool _strictEncodingKnown;

        private int FirstUnencodable(string text)
        {
            if (!_strictEncodingKnown)
            {
                _strictEncodingKnown = true;
                Encoding? encoding = _settings?.Encoding;
                if (encoding != null && encoding.CodePage is not (65001 or 1200 or 1201 or 12000 or 12001))
                {
                    _strictEncoding = Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                }
            }
            if (_strictEncoding == null)
            {
                return -1;
            }
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] >= 0x80 && !CanEncode(text.AsSpan(i, char.IsSurrogatePair(text, i) ? 2 : 1)))
                {
                    return i;
                }
            }
            return -1;
        }

        private bool CanEncode(System.ReadOnlySpan<char> c)
        {
            if (_strictEncoding == null || c.Length == 1 && c[0] < 0x80)
            {
                return true;
            }
            try
            {
                _strictEncoding.GetByteCount(c);
                return true;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }
        private bool _currentXhtml;

        internal static readonly HashSet<string> HtmlUriAttributes = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "action", "archive", "background", "cite", "classid", "codebase", "data", "datasrc", "for", "formaction", "href",
            "icon", "longdesc", "manifest", "poster", "profile", "src", "usemap",
        };

        // %HH of the UTF-8 bytes of the characters outside printable ASCII (spaces stay).
        internal static string EscapeUri(string value)
        {
            value = value.Normalize(NormalizationForm.FormC); // escape-uri-attributes normalizes to NFC first
            var sb = new System.Text.StringBuilder(value.Length);
            byte[] bytes = new byte[4];
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c is >= ' ' and < '')
                {
                    sb.Append(c);
                    continue;
                }
                int width = char.IsSurrogatePair(value, i) ? 2 : 1;
                int n = System.Text.Encoding.UTF8.GetBytes(value, i, width, bytes, 0);
                i += width - 1;
                for (int b = 0; b < n; b++)
                {
                    sb.Append('%').Append(bytes[b].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }

        public override void WriteEndAttribute()
        {
            if (_meta is { Current: not null } pending)
            {
                pending.Current = null;
                return;
            }
            if (_namespaceDeclaration is var (declared, value))
            {
                _namespaceDeclaration = null;
                if (value.ToString() is not (XhtmlNs or SvgNs or MathMLNs))
                {
                    _inner.WriteAttributeString("xmlns", declared, "http://www.w3.org/2000/xmlns/", value.ToString());
                }
                return;
            }
            _inAttribute = false;
            _inner.WriteEndAttribute();
        }

        private (string Prefix, System.Text.StringBuilder Value)? _namespaceDeclaration;

        public override WriteState WriteState => _inner.WriteState;

        public override XmlWriterSettings? Settings => _inner.Settings;

        public override void Flush()
        {
            FlushCData();
            _inner.Flush();
        }

        public override string? LookupPrefix(string ns) => _inner.LookupPrefix(ns);

        public override void WriteBase64(byte[] buffer, int index, int count) => _inner.WriteBase64(buffer, index, count);

        public override void WriteCData(string? text)
        {
            Content();
            _inner.WriteCData(text == null ? null : Normalize(text));
        }

        public override void WriteCharEntity(char ch)
        {
            Content();
            _inner.WriteCharEntity(ch);
        }

        public override void WriteChars(char[] buffer, int index, int count) => WriteString(new string(buffer, index, count));

        public override void WriteComment(string? text)
        {
            Content();
            _inner.WriteComment(text);
        }

        public override void WriteDocType(string name, string? pubid, string? sysid, string? subset)
        {
            if (sysid != null && sysid.Contains('"') && subset == null)
            {
                // .NET's writer would quote it with quotation marks.
                _inner.WriteRaw(pubid != null ? $"<!DOCTYPE {name} PUBLIC \"{pubid}\" '{sysid}'>" : $"<!DOCTYPE {name} SYSTEM '{sysid}'>");
                return;
            }
            _inner.WriteDocType(name, pubid, sysid, subset);
        }

        public override void WriteEndDocument()
        {
            Content();
            _inner.WriteEndDocument();
        }

        public override void WriteEntityRef(string name)
        {
            Content();
            _inner.WriteEntityRef(name);
        }

        public override void WriteProcessingInstruction(string name, string? text)
        {
            Content();
            _inner.WriteProcessingInstruction(name, text);
        }

        public override void WriteRaw(char[] buffer, int index, int count)
        {
            Content();
            _inner.WriteRaw(buffer, index, count);
        }

        public override void WriteRaw(string data)
        {
            Content();
            _inner.WriteRaw(data);
        }

        public override void WriteStartDocument() => _inner.WriteStartDocument();

        public override void WriteStartDocument(bool standalone) => _inner.WriteStartDocument(standalone);

        public override void WriteSurrogateCharEntity(char lowChar, char highChar)
        {
            Content();
            _inner.WriteSurrogateCharEntity(lowChar, highChar);
        }

        public override void WriteWhitespace(string? ws)
        {
            if (_ownCData && !_inAttribute && _cdata.Count > 0 && _cdata.Peek())
            {
                WriteString(ws);
                return;
            }
            Content();
            _inner.WriteWhitespace(ws);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                FlushCData();
                _inner.Flush();
            }
            base.Dispose(disposing);
        }
    }
}
