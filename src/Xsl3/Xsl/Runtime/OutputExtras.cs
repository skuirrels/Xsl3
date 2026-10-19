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

        /// <summary>The encoding named in the meta element of an XHTML head.</summary>
        public string Encoding { get; set; } = "UTF-8";

        public NormalizationForm? Normalization { get; set; }

        /// <summary>The character map (use-character-maps), code point to string.</summary>
        public IReadOnlyDictionary<int, string>? CharacterMap { get; set; }

        public bool IsEmpty => !Xhtml && Normalization == null && (CharacterMap == null || CharacterMap.Count == 0);

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
    internal sealed class SerializationWriter : XmlWriter
    {
        private const string XhtmlNs = "http://www.w3.org/1999/xhtml";

        // The elements written with the minimized syntax (<br />) by the xhtml method: those declared EMPTY in the
        // XHTML 1.0 DTDs (the default html-version of XSLT 2.0). Others get an end tag.
        private static readonly HashSet<string> s_voidElements = new(System.StringComparer.Ordinal)
        {
            "area", "base", "basefont", "br", "col", "frame", "hr", "img", "input", "isindex", "link", "meta", "param",
        };

        private readonly XmlWriter _inner;
        private readonly OutputExtras _extras;
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
                _inner.WriteAttributeString("content", "text/html; charset=" + _extras.Encoding);
                _inner.WriteEndElement();
            }
        }

        private string Normalize(string text) =>
            _extras.Normalization is NormalizationForm form && !text.IsNormalized(form) ? text.Normalize(form) : text;

        public override void WriteString(string? text)
        {
            Content();
            if (string.IsNullOrEmpty(text))
            {
                _inner.WriteString(text);
                return;
            }
            text = Normalize(text);
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
            _inner.WriteStartElement(prefix, localName, ns);
            bool xhtml = _extras.Xhtml && ns == XhtmlNs;
            _void.Push(!_extras.Xhtml || xhtml && s_voidElements.Contains(localName));
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
            _inAttribute = true;
            _inner.WriteStartAttribute(prefix, localName, ns);
        }

        public override void WriteEndAttribute()
        {
            _inAttribute = false;
            _inner.WriteEndAttribute();
        }

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
