// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Xsl3.XPath31.Functions
{
    /// <summary>
    /// An XmlWriter that applies a character map (Serialization 3.1 §11): mapped characters in text and attribute
    /// values are written as their map strings, unescaped. Everything else passes through to the inner writer.
    /// </summary>
    internal sealed class CharacterMapWriter : XmlWriter
    {
        private readonly XmlWriter _inner;
        private readonly IReadOnlyDictionary<int, string> _map;

        public CharacterMapWriter(XmlWriter inner, IReadOnlyDictionary<int, string> map)
        {
            _inner = inner;
            _map = map;
        }

        public override void WriteString(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                _inner.WriteString(text);
                return;
            }
            var pending = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                int cp = char.IsSurrogatePair(text, i) ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
                int width = cp > 0xFFFF ? 2 : 1;
                if (_map.TryGetValue(cp, out string? replacement))
                {
                    if (pending.Length > 0)
                    {
                        _inner.WriteString(pending.ToString());
                        pending.Clear();
                    }
                    _inner.WriteRaw(replacement);
                }
                else
                {
                    pending.Append(text, i, width);
                }
                i += width - 1;
            }
            if (pending.Length > 0)
            {
                _inner.WriteString(pending.ToString());
            }
        }

        public override WriteState WriteState => _inner.WriteState;

        public override XmlWriterSettings? Settings => _inner.Settings;

        public override void Flush() => _inner.Flush();

        public override string? LookupPrefix(string ns) => _inner.LookupPrefix(ns);

        public override void WriteBase64(byte[] buffer, int index, int count) => _inner.WriteBase64(buffer, index, count);

        public override void WriteCData(string? text) => _inner.WriteCData(text);

        public override void WriteCharEntity(char ch) => _inner.WriteCharEntity(ch);

        public override void WriteChars(char[] buffer, int index, int count) => WriteString(new string(buffer, index, count));

        public override void WriteComment(string? text) => _inner.WriteComment(text);

        public override void WriteDocType(string name, string? pubid, string? sysid, string? subset) => _inner.WriteDocType(name, pubid, sysid, subset);

        public override void WriteEndAttribute() => _inner.WriteEndAttribute();

        public override void WriteEndDocument() => _inner.WriteEndDocument();

        public override void WriteEndElement() => _inner.WriteEndElement();

        public override void WriteEntityRef(string name) => _inner.WriteEntityRef(name);

        public override void WriteFullEndElement() => _inner.WriteFullEndElement();

        public override void WriteProcessingInstruction(string name, string? text) => _inner.WriteProcessingInstruction(name, text);

        public override void WriteRaw(char[] buffer, int index, int count) => _inner.WriteRaw(buffer, index, count);

        public override void WriteRaw(string data) => _inner.WriteRaw(data);

        public override void WriteStartAttribute(string? prefix, string localName, string? ns) => _inner.WriteStartAttribute(prefix, localName, ns);

        public override void WriteStartDocument() => _inner.WriteStartDocument();

        public override void WriteStartDocument(bool standalone) => _inner.WriteStartDocument(standalone);

        public override void WriteStartElement(string? prefix, string localName, string? ns) => _inner.WriteStartElement(prefix, localName, ns);

        public override void WriteSurrogateCharEntity(char lowChar, char highChar) => _inner.WriteSurrogateCharEntity(lowChar, highChar);

        public override void WriteWhitespace(string? ws) => _inner.WriteWhitespace(ws);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
