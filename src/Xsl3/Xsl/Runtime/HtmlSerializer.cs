// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Xsl3: the html output method (Serialization 3.1 §7) for 2.0+ stylesheets, which .NET's html writer can't be
    /// configured for: the content-type meta element with the declared media type and encoding (or none, with
    /// include-content-type="no"), HTML 4 and HTML5 void elements, URI escaping of URI attributes, minimized boolean
    /// attributes, raw text in script and style, the doctype, and indentation. Elements in a namespace are written
    /// as XML. The markup goes to <paramref name="output"/> as raw text: it is a writer of the text method, which
    /// does the encoding; characters the encoding can't represent are written as character references.
    /// </summary>
    internal sealed class HtmlSerializer(XmlWriter output, XmlWriterSettings settings, OutputExtras extras) : XmlWriter
    {
        private static readonly HashSet<string> s_void = new(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "basefont", "br", "col", "frame", "hr", "img", "input", "isindex", "link", "meta", "param",
        };

        private static readonly HashSet<string> s_void5 = new(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "keygen", "link", "meta", "param", "source", "track", "wbr",
        };

        private static readonly HashSet<string> s_raw = new(StringComparer.OrdinalIgnoreCase) { "script", "style" };

        private static readonly HashSet<string> s_boolean = new(StringComparer.OrdinalIgnoreCase)
        {
            "checked", "compact", "declare", "defer", "disabled", "ismap", "multiple", "nohref", "noresize", "noshade", "nowrap",
            "readonly", "selected",
        };

        // The attributes whose values are URIs (HTML 4.01), escaped with escape-uri-attributes="yes".
        private static readonly HashSet<string> s_uri = new(StringComparer.OrdinalIgnoreCase)
        {
            "action", "archive", "background", "cite", "classid", "codebase", "data", "datasrc", "for", "formaction", "href",
            "icon", "longdesc", "manifest", "poster", "profile", "src", "usemap", "xmlns",
        };

        // Elements written without indentation inside, and those whose start and end don't get line breaks.
        private static readonly HashSet<string> s_preserve = new(StringComparer.OrdinalIgnoreCase) { "pre", "script", "style", "textarea", "title" };

        private static readonly HashSet<string> s_inline = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "abbr", "acronym", "b", "basefont", "bdo", "big", "br", "button", "cite", "code", "dfn", "em", "font", "i", "img",
            "input", "kbd", "label", "q", "s", "samp", "select", "small", "span", "strike", "strong", "sub", "sup", "textarea",
            "tt", "u", "var", "del", "ins",
        };

        private sealed class Element(string? prefix, string local, string ns)
        {
            public string? Prefix = prefix;
            public string Local = local;
            public string Ns = ns;
            public bool Html => Ns.Length == 0 || Ns == "http://www.w3.org/1999/xhtml";
            public string Name => string.IsNullOrEmpty(Prefix) ? Local : Prefix + ":" + Local;
            public Dictionary<string, string>? Namespaces; // declared here: prefix -> namespace
            public bool HasContent, HasText, HasElements, Suppressed;
        }

        private readonly Stack<Element> _elements = new();
        private Element? _open; // the element whose start tag is still open (attributes may follow)
        private readonly List<(string? Prefix, string Local, string Ns, string Value, List<(int Start, int Length)>? Raw)> _attributes = new();
        private (string? Prefix, string Local, string Ns)? _attribute;
        private readonly StringBuilder _attributeValue = new();
        private List<(int Start, int Length)>? _attributeRaw; // the parts written raw (character maps)
        private bool _seenElement;
        private readonly Encoder? _encoder = Encoding.UTF8.WebName == settings.Encoding.WebName ? null : settings.Encoding.GetEncoder();
        private WriteState _state = WriteState.Start;

        private bool Html5 => extras.Html5;
        private bool Indent => settings.Indent;

        // ------------------------------------------------------------ output

        private void Raw(string s) => output.WriteRaw(s);

        // Text with the characters the encoding can't represent as character references.
        private void Encoded(StringBuilder sb, string s, int start, int length)
        {
            for (int i = start; i < start + length; i++)
            {
                char c = s[i];
                if (c < 0x80 || _encoder == null)
                {
                    sb.Append(c);
                    continue;
                }
                int cp = char.IsSurrogatePair(s, i) ? char.ConvertToUtf32(c, s[++i]) : c;
                string chars = char.ConvertFromUtf32(cp);
                if (Encodable(chars))
                {
                    sb.Append(chars);
                }
                else
                {
                    sb.Append("&#").Append(cp).Append(';');
                }
            }
        }

        private bool Encodable(string chars)
        {
            try
            {
                _encoder!.Reset();
                byte[] buffer = new byte[16];
                _encoder.Fallback = EncoderFallback.ExceptionFallback;
                _encoder.GetBytes(chars.ToCharArray(), 0, chars.Length, buffer, 0, true);
                return true;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }

        private void NewLine(int depth)
        {
            Raw("\n");
            Raw(new string(' ', 2 * depth));
        }

        private bool Preserving()
        {
            foreach (Element e in _elements)
            {
                if (e.Html && s_preserve.Contains(e.Local))
                {
                    return true;
                }
            }
            return false;
        }

        // Before an element's start or end tag: a line break, unless the parent has text or it's inline.
        private void IndentBefore(Element e, bool end)
        {
            if (!Indent || Preserving() || (e.Html && s_inline.Contains(e.Local)))
            {
                return;
            }
            Element? parent = end ? e : (_elements.Count > 0 ? _elements.Peek() : null);
            if (parent?.HasText == true || (end && !e.HasElements))
            {
                return;
            }
            if (!end && parent == null && !_seenElement)
            {
                return;
            }
            NewLine(end ? _elements.Count - 1 : _elements.Count);
        }

        // ------------------------------------------------------------ elements

        private void CloseStartTag()
        {
            if (_open == null)
            {
                return;
            }
            Element e = _open;
            _open = null;
            if (e.Html && e.Local.Equals("meta", StringComparison.OrdinalIgnoreCase) && extras.IncludeContentType &&
                _attributes.Exists(a => a.Ns.Length == 0 && a.Local.Equals("http-equiv", StringComparison.OrdinalIgnoreCase) &&
                    a.Value.Trim().Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
            {
                e.Suppressed = true; // the serializer writes its own (§7.4.2)
                _attributes.Clear();
                return;
            }
            var sb = new StringBuilder();
            sb.Append('<').Append(e.Name);
            foreach (var (prefix, local, ns, value, raw) in _attributes)
            {
                string name = string.IsNullOrEmpty(prefix) ? local : prefix + ":" + local;
                if (e.Html && ns.Length == 0 && s_boolean.Contains(local) && value.Equals(local, StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(' ').Append(name);
                    continue;
                }
                sb.Append(' ').Append(name).Append("=\"");
                bool uri = e.Html && ns.Length == 0 && extras.EscapeUriAttributes && s_uri.Contains(local);
                int from = 0;
                foreach (var (start, length) in raw ?? new List<(int, int)>())
                {
                    AppendAttributeValue(sb, uri ? EscapeUri(value[from..start]) : value[from..start], e.Html);
                    sb.Append(value, start, length);
                    from = start + length;
                }
                AppendAttributeValue(sb, uri ? EscapeUri(value[from..]) : value[from..], e.Html);
                sb.Append('"');
            }
            _attributes.Clear();
            sb.Append('>');
            Raw(sb.ToString());
            if (e.Html && e.Local.Equals("head", StringComparison.OrdinalIgnoreCase) && extras.IncludeContentType)
            {
                // §7.4.2: the content type, first in head.
                if (Indent)
                {
                    NewLine(_elements.Count);
                }
                Raw($"<meta http-equiv=\"Content-Type\" content=\"{extras.MediaType}; charset={extras.Encoding}\">");
                e.HasElements = true;
            }
        }

        private void AppendAttributeValue(StringBuilder sb, string value, bool html)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '&':
                        // &{ stays as it is in HTML (§7.1).
                        sb.Append(html && i + 1 < value.Length && value[i + 1] == '{' ? "&" : "&amp;");
                        break;
                    case '"':
                        sb.Append("&quot;");
                        break;
                    case '<' when !html:
                        sb.Append("&lt;");
                        break;
                    case '\n':
                        sb.Append(html ? "\n" : "&#xA;");
                        break;
                    case '\t' when !html:
                        sb.Append("&#x9;");
                        break;
                    default:
                        if (c is >= '\u007f' and <= '\u009f')
                        {
                            sb.Append("&#").Append((int)c).Append(';');
                        }
                        else
                        {
                            Encoded(sb, value, i, char.IsSurrogatePair(value, i) ? 2 : 1);
                            if (char.IsSurrogatePair(value, i))
                            {
                                i++;
                            }
                        }
                        break;
                }
            }
        }

        // §7.3: URI attribute values: characters outside printable ASCII as %HH of their UTF-8 bytes.
        private static string EscapeUri(string value)
        {
            var sb = new StringBuilder(value.Length);
            byte[] bytes = new byte[4];
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c is > ' ' and < '\u007f')
                {
                    sb.Append(c);
                    continue;
                }
                if (c == ' ')
                {
                    sb.Append(c);
                    continue;
                }
                int n = Encoding.UTF8.GetBytes(value, i, char.IsSurrogatePair(value, i) ? 2 : 1, bytes, 0);
                if (char.IsSurrogatePair(value, i))
                {
                    i++;
                }
                for (int b = 0; b < n; b++)
                {
                    sb.Append('%').Append(bytes[b].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }

        private void Content(bool text)
        {
            CloseStartTag();
            if (_elements.Count > 0)
            {
                Element parent = _elements.Peek();
                parent.HasContent = true;
                if (text)
                {
                    parent.HasText = true;
                }
            }
        }

        public override void WriteStartElement(string? prefix, string localName, string? ns)
        {
            Content(text: false);
            ns ??= "";
            var e = new Element(prefix, localName, ns);
            if (_elements.Count > 0)
            {
                _elements.Peek().HasElements = true;
            }
            if (!_seenElement)
            {
                if (settings.DocTypeSystem != null || settings.DocTypePublic != null)
                {
                    WriteDocType(localName, settings.DocTypePublic, settings.DocTypeSystem, null);
                }
                else if (Html5 && extras.DefaultDoctype && localName.Equals("html", StringComparison.OrdinalIgnoreCase))
                {
                    Raw("<!DOCTYPE html>");
                    if (Indent)
                    {
                        Raw("\n");
                    }
                }
            }
            IndentBefore(e, end: false);
            _seenElement = true;
            _state = WriteState.Element;
            // Namespaces: declared where an element in a namespace needs them (§7.1: as the XML method does).
            if (!e.Html || ns.Length != 0 && !string.IsNullOrEmpty(prefix))
            {
                Declare(e, prefix ?? "", ns);
            }
            else if (ns.Length != 0 && LookupNamespace("") != ns)
            {
                Declare(e, "", ns);
            }
            _elements.Push(e);
            _open = e;
        }

        private string? LookupNamespace(string prefix)
        {
            foreach (Element e in _elements)
            {
                if (e.Namespaces != null && e.Namespaces.TryGetValue(prefix, out string? ns))
                {
                    return ns;
                }
            }
            return prefix.Length == 0 ? "" : null;
        }

        private void Declare(Element e, string prefix, string ns)
        {
            if (prefix == "xml" || LookupNamespace(prefix) == ns && (e.Namespaces == null || !e.Namespaces.ContainsKey(prefix)))
            {
                return;
            }
            (e.Namespaces ??= new())[prefix] = ns;
            _attributes.Add(prefix.Length == 0 ? (null, "xmlns", "http://www.w3.org/2000/xmlns/", ns, null) : ("xmlns", prefix, "http://www.w3.org/2000/xmlns/", ns, null));
        }

        public override void WriteEndElement() => End();

        public override void WriteFullEndElement() => End();

        private void End()
        {
            Element e = _elements.Peek();
            if (_open == e)
            {
                CloseStartTag();
            }
            _elements.Pop();
            if (e.Suppressed)
            {
                return;
            }
            bool isVoid = e.Html && (Html5 ? s_void5 : s_void).Contains(e.Local);
            if (e.Html && isVoid && !e.HasContent)
            {
                return;
            }
            _elements.Push(e);
            IndentBefore(e, end: true);
            _elements.Pop();
            Raw("</" + e.Name + ">");
            _state = _elements.Count == 0 ? WriteState.Content : WriteState.Element;
        }

        // ------------------------------------------------------------ attributes

        public override void WriteStartAttribute(string? prefix, string localName, string? ns)
        {
            _attribute = (prefix, localName, ns ?? "");
            _attributeValue.Clear();
            _attributeRaw = null;
            _state = WriteState.Attribute;
        }

        public override void WriteEndAttribute()
        {
            if (_attribute is var (prefix, local, ns))
            {
                string value = _attributeValue.ToString();
                if (ns == "http://www.w3.org/2000/xmlns/")
                {
                    // An explicit namespace declaration.
                    string declared = prefix == "xmlns" ? local : "";
                    if (_open != null && LookupNamespace(declared) != value)
                    {
                        Declare(_open, declared, value);
                    }
                }
                else
                {
                    if (ns.Length != 0 && _open != null && !string.IsNullOrEmpty(prefix))
                    {
                        Declare(_open, prefix, ns);
                    }
                    _attributes.RemoveAll(a => a.Local == local && a.Ns == ns);
                    _attributes.Add((prefix, local, ns, value, _attributeRaw));
                }
            }
            _attribute = null;
            _state = WriteState.Element;
        }

        // ------------------------------------------------------------ content

        public override void WriteString(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            if (_attribute != null)
            {
                _attributeValue.Append(text);
                return;
            }
            Content(text: true);
            _state = WriteState.Content;
            Element? parent = _elements.Count > 0 ? _elements.Peek() : null;
            if (parent?.Suppressed == true)
            {
                return;
            }
            var sb = new StringBuilder(text.Length);
            if (parent != null && parent.Html && s_raw.Contains(parent.Local))
            {
                Encoded(sb, text, 0, text.Length); // script and style: not escaped (§7.1)
            }
            else
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    switch (c)
                    {
                        case '&': sb.Append("&amp;"); break;
                        case '<': sb.Append("&lt;"); break;
                        case '>': sb.Append("&gt;"); break;
                        default:
                            int width = char.IsSurrogatePair(text, i) ? 2 : 1;
                            Encoded(sb, text, i, width);
                            i += width - 1;
                            break;
                    }
                }
            }
            Raw(sb.ToString());
        }

        public override void WriteRaw(string data)
        {
            if (_attribute != null)
            {
                (_attributeRaw ??= new()).Add((_attributeValue.Length, data.Length));
                _attributeValue.Append(data);
                return;
            }
            Content(text: true);
            Raw(data);
        }

        public override void WriteRaw(char[] buffer, int index, int count) => WriteRaw(new string(buffer, index, count));

        public override void WriteChars(char[] buffer, int index, int count) => WriteString(new string(buffer, index, count));

        public override void WriteWhitespace(string? ws) => WriteString(ws);

        public override void WriteCData(string? text) => WriteString(text);

        public override void WriteCharEntity(char ch) => WriteString(ch.ToString());

        public override void WriteSurrogateCharEntity(char lowChar, char highChar) => WriteString(new string(new[] { highChar, lowChar }));

        public override void WriteEntityRef(string name) => WriteRaw("&" + name + ";");

        public override void WriteComment(string? text)
        {
            Content(text: false);
            Raw("<!--" + text + "-->");
        }

        // §7.1: a processing instruction ends with ">".
        public override void WriteProcessingInstruction(string name, string? text)
        {
            Content(text: false);
            Raw("<?" + name + (string.IsNullOrEmpty(text) ? "" : " " + text) + ">");
        }

        public override void WriteDocType(string name, string? pubid, string? sysid, string? subset)
        {
            var sb = new StringBuilder("<!DOCTYPE ").Append(name);
            if (pubid != null)
            {
                sb.Append(" PUBLIC \"").Append(pubid).Append('"');
                if (sysid != null)
                {
                    sb.Append(" \"").Append(sysid).Append('"');
                }
            }
            else if (sysid != null)
            {
                sb.Append(" SYSTEM \"").Append(sysid).Append('"');
            }
            sb.Append('>');
            Raw(sb.ToString());
            if (Indent)
            {
                Raw("\n");
            }
        }

        // ------------------------------------------------------------ the rest

        public override void WriteStartDocument() { }

        public override void WriteStartDocument(bool standalone) { }

        public override void WriteEndDocument()
        {
            while (_elements.Count > 0)
            {
                End();
            }
        }

        public override void WriteBase64(byte[] buffer, int index, int count) => WriteString(Convert.ToBase64String(buffer, index, count));

        public override WriteState WriteState => _state;

        public override XmlWriterSettings Settings => settings;

        public override string? LookupPrefix(string ns)
        {
            foreach (Element e in _elements)
            {
                if (e.Namespaces != null)
                {
                    foreach (var (prefix, uri) in e.Namespaces)
                    {
                        if (uri == ns)
                        {
                            return prefix;
                        }
                    }
                }
            }
            return ns.Length == 0 ? "" : null;
        }

        public override void Flush()
        {
            CloseStartTag();
            output.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CloseStartTag();
                while (_elements.Count > 0)
                {
                    End();
                }
                output.Flush();
                output.Dispose();
            }
            _state = WriteState.Closed;
            base.Dispose(disposing);
        }
    }
}
