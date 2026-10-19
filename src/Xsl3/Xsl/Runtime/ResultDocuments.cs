// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>Xsl3: an output definition (xsl:output) a result document is serialized by.</summary>
    internal sealed record ResultFormat(XmlWriterSettings Settings, OutputExtras? Extras, bool Text);

    /// <summary>
    /// Xsl3: the serialization parameters of an xsl:result-document (XSLT 3.0 §25.1, §26) applied over an output
    /// definition. The attributes arrive as one string: name, U+0001, value, U+0002, … (their values are attribute
    /// value templates, evaluated by the instruction).
    /// </summary>
    internal static class SerializationParameters
    {
        public const char NameEnd = '\u0001', ValueEnd = '\u0002';

        public static (XmlWriterSettings Settings, OutputExtras? Extras, bool Text) Apply(ResultFormat format, string parameters, XsltNameContext names)
        {
            XmlWriterSettings settings = format.Settings.Clone();
            OutputExtras? extras = format.Extras?.Clone();
            bool text = format.Text;
            OutputExtras Extras() => extras ??= new OutputExtras();
            string? htmlVersion = null;
            foreach (string pair in parameters.Split(ValueEnd, System.StringSplitOptions.RemoveEmptyEntries))
            {
                int end = pair.IndexOf(NameEnd);
                string name = pair[..end], value = pair[(end + 1)..].Trim();
                switch (name)
                {
                    case "method":
                        var (ns, local) = names.Resolve(value, "", "XTDE0290");
                        if (ns.Length != 0)
                        {
                            break; // a vendor method: the default
                        }
                        text = local == "text";
                        Extras().Xhtml = local == "xhtml";
                        Extras().RawMethod = local is "json" or "adaptive" ? local : extras!.RawMethod == "sequence" ? "sequence" : null;
                        XmlOutputMethod method = local switch
                        {
                            "xml" or "xhtml" => XmlOutputMethod.Xml,
                            "html" => XmlOutputMethod.Html,
                            "text" or "json" or "adaptive" => XmlOutputMethod.Text,
                            _ => throw new XdmException("SEPM0016", $"method=\"{value}\" is not a serialization method"),
                        };
                        settings.SetOutputMethod(method);
                        break;
                    case "encoding":
                        try
                        {
                            settings.Encoding = Encoding.GetEncoding(value);
                            Extras().Encoding = value;
                        }
                        catch (System.ArgumentException)
                        {
                            throw new XdmException("SESU0007", $"The encoding '{value}' is not supported");
                        }
                        break;
                    case "indent":
                        settings.Indent = YesNo(value, name);
                        break;
                    case "omit-xml-declaration":
                        settings.OmitXmlDeclaration = YesNo(value, name);
                        break;
                    case "standalone":
                        settings.Standalone = value == "omit" ? XmlStandalone.Omit : YesNo(value, name) ? XmlStandalone.Yes : XmlStandalone.No;
                        break;
                    case "doctype-public":
                        settings.DocTypePublic = value.Length == 0 ? null : value; // a zero-length value means absent
                        break;
                    case "doctype-system":
                        settings.DocTypeSystem = value.Length == 0 ? null : value;
                        break;
                    case "media-type":
                        settings.MediaType = value;
                        Extras().MediaType = value;
                        break;
                    case "cdata-section-elements":
                        foreach (string qname in value.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries))
                        {
                            var (cns, clocal) = names.Resolve(qname, names.Namespaces.TryGetValue("", out string? d) ? d : "", "XTDE0290");
                            settings.CDataSectionElements.Add(new XmlQualifiedName(clocal, cns));
                        }
                        break;
                    case "byte-order-mark":
                        bool bom = YesNo(value, name);
                        settings.Encoding = settings.Encoding switch
                        {
                            UTF8Encoding => new UTF8Encoding(bom),
                            UnicodeEncoding u => new UnicodeEncoding(u.CodePage == 1201, bom),
                            var e => e,
                        };
                        break;
                    case "include-content-type":
                        Extras().IncludeContentType = YesNo(value, name);
                        break;
                    case "normalization-form":
                        if (!OutputExtras.TryParseNormalizationForm(value, out NormalizationForm? form))
                        {
                            throw new XdmException("SESU0011", $"normalization-form=\"{value}\" is not supported");
                        }
                        Extras().Normalization = form;
                        break;
                    case "escape-uri-attributes":
                        Extras().EscapeUriAttributes = YesNo(value, name);
                        break;
                    case "undeclare-prefixes":
                        YesNo(value, name);
                        break;
                    case "output-version":
                        if (settings.OutputMethod == XmlOutputMethod.Html)
                        {
                            htmlVersion ??= value;
                        }
                        break;
                    case "html-version":
                        htmlVersion = value;
                        break;
                    case "item-separator":
                        // Not trimmed; "#absent" means no separator (XSLT 3.0 §25).
                        Extras().ItemSeparator = value == "#absent" ? null : pair[(end + 1)..];
                        Extras().RawMethod ??= "sequence";
                        break;
                    case "allow-duplicate-names":
                        Extras().AllowDuplicateNames = YesNo(value, name);
                        break;
                    case "json-node-output-method":
                        Extras().JsonNodeOutputMethod = value;
                        break;
                }
            }
            bool html = settings.OutputMethod == XmlOutputMethod.Html;
            if (htmlVersion != null && htmlVersion.StartsWith('5') && (html || extras?.Xhtml == true))
            {
                // HTML5 serialization (Serialization 3.1 §7.1).
                Extras().Html5 = true;
                extras!.HtmlMethod = html;
                extras.DefaultDoctype = settings.DocTypeSystem == null;
            }
            return (settings, extras, text);
        }

        private static bool YesNo(string value, string name) => value switch
        {
            "yes" or "true" or "1" => true,
            "no" or "false" or "0" => false,
            _ => throw new XdmException("SEPM0016", $"{name}=\"{value}\" must be yes or no"),
        };
    }

    /// <summary>
    /// Xsl3: the principal result's writer, created when the first output reaches it. An xsl:result-document
    /// without href may claim the principal result before that, with its own serialization parameters when the
    /// writer is Xsl3's to create; output to the principal result besides it is an error (XTDE1490).
    /// </summary>
    internal sealed class PrincipalResult : XmlWriter
    {
        private enum State { NotStarted, Implicit, Explicit, Closed }

        private readonly System.Func<XmlWriterSettings, XmlWriter>? _factory;
        private XmlWriterSettings _settings;
        private OutputExtras? _extras;
        private XmlWriter? _inner;
        private State _state;
        private bool _startDocument;

        /// <summary>A writer the caller supplied: its settings are fixed.</summary>
        public PrincipalResult(XmlWriter writer, OutputExtras? extras)
        {
            _inner = extras is { IsEmpty: false } ? extras.Wrap(writer) : writer;
            _settings = writer.Settings ?? new XmlWriterSettings();
            _extras = extras;
        }

        /// <summary>A writer to create from the output definition, or from a result document's parameters.</summary>
        public PrincipalResult(System.Func<XmlWriterSettings, XmlWriter> factory, XmlWriterSettings settings, OutputExtras? extras)
        {
            _factory = factory;
            _settings = settings;
            _extras = extras;
        }

        public bool CanConfigure => _factory != null && _inner == null;

        /// <summary>xsl:result-document without href: it takes the principal result (XTDE1490 if output reached it).</summary>
        public void BeginExplicit(XmlWriterSettings? settings, OutputExtras? extras)
        {
            if (_state != State.NotStarted)
            {
                throw new XdmException("XTDE1490", "The principal result has already been written");
            }
            if (settings != null && CanConfigure)
            {
                _settings = settings;
                _extras = extras;
            }
            _state = State.Implicit;
        }

        public void EndExplicit()
        {
            _state = State.Explicit;
        }

        /// <summary>
        /// xsl:result-document without href, with the json or adaptive method or an item-separator: the items
        /// serialized as the principal result. False if the principal result's writer is already made.
        /// </summary>
        public bool WriteItems(IList<XPathItem> items, XmlWriterSettings settings, OutputExtras extras)
        {
            if (!CanConfigure)
            {
                return false;
            }
            BeginExplicit(null, null);
            if (extras.RawMethod is "json" or "adaptive")
            {
                var text = new System.IO.StringWriter();
                ItemSerializer.Write(items, extras, text);
                XmlWriterSettings s = settings.Clone();
                s.SetOutputMethod(XmlOutputMethod.Text);
                s.ConformanceLevel = ConformanceLevel.Fragment;
                _inner = _factory!(s);
                _inner.WriteString(text.ToString());
                _inner.Flush();
            }
            else
            {
                ItemSerializer.WriteSequence(items, settings, extras, _factory!);
            }
            _wroteItems = true;
            EndExplicit();
            return true;
        }

        private bool _wroteItems;

        private XmlWriter Content()
        {
            if (_state == State.Explicit)
            {
                throw new XdmException("XTDE1490", "The principal result was written by xsl:result-document; there can be no other output to it");
            }
            _state = State.Implicit;
            return Inner();
        }

        private XmlWriter Inner()
        {
            if (_inner == null)
            {
                _inner = Create(_factory!, _settings, _extras);
                if (_startDocument && _settings.OutputMethod is not (XmlOutputMethod.Xml or XmlOutputMethod.AutoDetect))
                {
                    _inner.WriteStartDocument();
                }
            }
            return _inner;
        }

        /// <summary>
        /// A writer for a serialization. The XML declaration is written here, naming the encoding as the stylesheet
        /// does (.NET writes its web name, "utf-8").
        /// </summary>
        public static XmlWriter Create(System.Func<XmlWriterSettings, XmlWriter> factory, XmlWriterSettings settings, OutputExtras? extras)
        {
            bool declaration = settings.OutputMethod is XmlOutputMethod.Xml or XmlOutputMethod.AutoDetect && !settings.OmitXmlDeclaration;
            XmlWriter w;
            if (declaration)
            {
                XmlWriterSettings s = settings.Clone();
                s.OmitXmlDeclaration = true;
                w = factory(s);
                string encoding = extras?.Encoding ?? settings.Encoding.WebName.ToUpperInvariant();
                string standalone = settings.Standalone switch { XmlStandalone.Yes => " standalone=\"yes\"", XmlStandalone.No => " standalone=\"no\"", _ => "" };
                w.WriteRaw($"<?xml version=\"1.0\" encoding=\"{encoding}\"{standalone}?>");
            }
            else if (settings.OutputMethod == XmlOutputMethod.Html)
            {
                // Xsl3: the html method's own serializer, over a text-method writer that does the encoding.
                XmlWriterSettings s = settings.Clone();
                s.SetOutputMethod(XmlOutputMethod.Text);
                s.ConformanceLevel = ConformanceLevel.Fragment;
                OutputExtras html = extras ?? new OutputExtras { Encoding = settings.Encoding.WebName.ToUpperInvariant(), MediaType = settings.MediaType ?? "text/html" };
                w = new HtmlSerializer(factory(s), settings, html);
            }
            else
            {
                w = factory(settings);
            }
            return extras is { IsEmpty: false } ? extras.Wrap(w) : w;
        }

        public override WriteState WriteState => _inner?.WriteState ?? WriteState.Start;

        public override XmlWriterSettings? Settings => _inner?.Settings ?? _settings;

        public override void Flush() => _inner?.Flush();

        public override string? LookupPrefix(string ns) => _inner?.LookupPrefix(ns);

        public override void WriteStartDocument()
        {
            if (_inner == null) _startDocument = true;
            else _inner.WriteStartDocument();
        }

        public override void WriteStartDocument(bool standalone) => WriteStartDocument();

        public override void WriteEndDocument() => _inner?.WriteEndDocument();

        public override void WriteDocType(string name, string? pubid, string? sysid, string? subset) => Content().WriteDocType(name, pubid, sysid, subset);

        public override void WriteStartElement(string? prefix, string localName, string? ns)
        {
            if (_inner == null && _factory != null && _settings.OutputMethod == XmlOutputMethod.AutoDetect)
            {
                // The default method (XSLT 3.0 §26.1): html if the first element is an unprefixed html element.
                XmlWriterSettings settings = _settings.Clone();
                bool html = string.IsNullOrEmpty(ns) && string.Equals(localName, "html", System.StringComparison.OrdinalIgnoreCase);
                settings.SetOutputMethod(html ? XmlOutputMethod.Html : XmlOutputMethod.Xml);
                if (html && _settings.IndentInternal == TriState.Unknown)
                {
                    settings.Indent = true; // the html method indents unless indent="no"
                }
                _settings = settings;
            }
            Content().WriteStartElement(prefix, localName, ns);
        }

        public override void WriteEndElement() => Content().WriteEndElement();

        public override void WriteFullEndElement() => Content().WriteFullEndElement();

        public override void WriteStartAttribute(string? prefix, string localName, string? ns) => Content().WriteStartAttribute(prefix, localName, ns);

        public override void WriteEndAttribute() => Content().WriteEndAttribute();

        public override void WriteCData(string? text) => Content().WriteCData(text);

        public override void WriteComment(string? text) => Content().WriteComment(text);

        public override void WriteProcessingInstruction(string name, string? text) => Content().WriteProcessingInstruction(name, text);

        public override void WriteEntityRef(string name) => Content().WriteEntityRef(name);

        public override void WriteCharEntity(char ch) => Content().WriteCharEntity(ch);

        public override void WriteWhitespace(string? ws) => Content().WriteWhitespace(ws);

        public override void WriteString(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            Content().WriteString(text);
        }

        public override void WriteSurrogateCharEntity(char lowChar, char highChar) => Content().WriteSurrogateCharEntity(lowChar, highChar);

        public override void WriteChars(char[] buffer, int index, int count) => Content().WriteChars(buffer, index, count);

        public override void WriteRaw(char[] buffer, int index, int count) => Content().WriteRaw(buffer, index, count);

        public override void WriteRaw(string data) => Content().WriteRaw(data);

        public override void WriteBase64(byte[] buffer, int index, int count) => Content().WriteBase64(buffer, index, count);

        public override void Close()
        {
            if (_state != State.Closed)
            {
                // An empty principal result still gets its writer (an XML declaration, say), unless a result document
                // took it, or wrote it as items with a writer of its own.
                if (_inner != null || !_wroteItems)
                {
                    Inner().Flush();
                    if (_factory != null)
                    {
                        _inner!.Close();
                    }
                }
                _state = State.Closed;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Close();
            }
        }
    }
}
