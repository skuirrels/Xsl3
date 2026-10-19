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

        /// <summary>The character map (use-character-maps), code point to string.</summary>
        public IReadOnlyDictionary<int, string>? CharacterMap { get; set; }

        /// <summary>escape-uri-attributes (the html method's own serializer, <see cref="HtmlSerializer"/>).</summary>
        public bool EscapeUriAttributes { get; set; } = true;

        /// <summary>"json" or "adaptive": the raw result sequence is serialized by <see cref="ItemSerializer"/>.</summary>
        public string? RawMethod { get; set; }

        public string? ItemSeparator { get; set; }

        public string? JsonNodeOutputMethod { get; set; }

        public bool AllowDuplicateNames { get; set; }

        public bool Indent { get; set; }

        /// <summary>HTML5 serialization (html-version 5): its void elements, and &lt;!DOCTYPE html&gt; by default.</summary>
        public bool Html5 { get; set; }

        /// <summary>With <see cref="Html5"/>: the html method (not xhtml).</summary>
        public bool HtmlMethod { get; set; }

        /// <summary>With <see cref="Html5"/>: no doctype-system, so &lt;!DOCTYPE html&gt; precedes the html element.</summary>
        public bool DefaultDoctype { get; set; }

        public OutputExtras Clone() => (OutputExtras)MemberwiseClone();

        public bool IsEmpty => !Xhtml && !Html5 && RawMethod == null && EscapeUriAttributes && IncludeContentType && Normalization == null && (CharacterMap == null || CharacterMap.Count == 0);

        public XmlWriter Wrap(XmlWriter writer) => IsEmpty ? writer : new SerializationWriter(writer, this);

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
        private bool _inAttribute;
        private bool _pendingHead;

        public SerializationWriter(XmlWriter inner, OutputExtras extras)
        {
            _inner = inner;
            _extras = extras;
        }

        // Before content of an XHTML head: the meta element that declares the content type.
        private void Content()
        {
            if (_pendingHead && !_inAttribute)
            {
                _pendingHead = false;
                _inner.WriteStartElement("meta", XhtmlNs);
                _inner.WriteAttributeString("http-equiv", "Content-Type");
                _inner.WriteAttributeString("content", _extras.MediaType + "; charset=" + _extras.Encoding);
                _inner.WriteEndElement();
            }
        }

        private string Normalize(string text) =>
            _extras.Normalization is NormalizationForm form && !text.IsNormalized(form) ? text.Normalize(form) : text;

        public override void WriteString(string? text)
        {
            if (_namespaceDeclaration is var (_, declaration))
            {
                declaration.Append(text);
                return;
            }
            Content();
            if (string.IsNullOrEmpty(text))
            {
                _inner.WriteString(text);
                return;
            }
            text = Normalize(text);
            if (_inAttribute && _uriAttribute)
            {
                text = EscapeUri(text);
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
                            WriteMapped(text[from..i]);
                        }
                        _inner.WriteCharEntity(text[i]);
                        from = i + 1;
                    }
                }
                if (from < text.Length)
                {
                    WriteMapped(text[from..]);
                }
                return;
            }
            WriteMapped(text);
        }

        private void WriteMapped(string text)
        {
            IReadOnlyDictionary<int, string>? map = _extras.CharacterMap;
            if (map == null || map.Count == 0)
            {
                _inner.WriteString(text);
                return;
            }
            // Mapped characters are written as their map strings, unescaped (Serialization 3.1 §11).
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int width = char.IsSurrogatePair(text, i) ? 2 : 1;
                int cp = width == 2 ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
                if (map.TryGetValue(cp, out string? replacement))
                {
                    if (i > start)
                    {
                        _inner.WriteString(text[start..i]);
                    }
                    _inner.WriteRaw(replacement);
                    start = i + width;
                }
                i += width - 1;
            }
            if (start < text.Length)
            {
                _inner.WriteString(text[start..]);
            }
        }

        public override void WriteStartElement(string? prefix, string localName, string? ns)
        {
            Content();
            if (!_seenElement)
            {
                _seenElement = true;
                if (_extras.Html5 && _extras.DefaultDoctype && !OwnHtml && string.Equals(localName, "html", System.StringComparison.OrdinalIgnoreCase))
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
            bool xhtml = _extras.Xhtml && (ns == XhtmlNs || _extras.Html5 && string.IsNullOrEmpty(ns));
            _currentXhtml = xhtml;
            _void.Push(!_extras.Xhtml || xhtml && (_extras.Html5 ? s_voidElements5 : s_voidElements).Contains(localName));
            if (xhtml && localName == "head" && _extras.IncludeContentType)
            {
                _pendingHead = true;
            }
        }

        public override void WriteEndElement()
        {
            Content();
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
            Content();
            if (_void.Count > 0)
            {
                _void.Pop();
            }
            _inner.WriteFullEndElement();
        }

        public override void WriteStartAttribute(string? prefix, string localName, string? ns)
        {
            if (_extras.Xhtml && _extras.Html5 && prefix == "xmlns" && ns == "http://www.w3.org/2000/xmlns/")
            {
                // A prefixed declaration: dropped if prefix normalization unprefixes its namespace's elements.
                _namespaceDeclaration = (localName, new System.Text.StringBuilder());
                return;
            }
            _inAttribute = true;
            // xhtml: URI attributes of XHTML elements are escaped like the html method's (Serialization 3.1 §7.3).
            _uriAttribute = _extras.Xhtml && _extras.EscapeUriAttributes && string.IsNullOrEmpty(ns) && _currentXhtml && HtmlUriAttributes.Contains(localName);
            _inner.WriteStartAttribute(prefix, localName, ns);
        }

        private bool _uriAttribute;
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

        public override void Flush() => _inner.Flush();

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

        public override void WriteDocType(string name, string? pubid, string? sysid, string? subset) => _inner.WriteDocType(name, pubid, sysid, subset);

        public override void WriteEndDocument() => _inner.WriteEndDocument();

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
            Content();
            _inner.WriteWhitespace(ws);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Flush();
            }
            base.Dispose(disposing);
        }
    }
}
