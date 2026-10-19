// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    // fn:parse-json, fn:json-doc, fn:json-to-xml and fn:xml-to-json (F&O 3.1 §17.5).
    internal static partial class FunctionLibrary
    {
        private const string FnNamespace = "http://www.w3.org/2005/xpath-functions";

        private static void RegisterJson()
        {
            Define("fn:parse-json($json-text as xs:string?) as item()?", (c, a) => a[0].Count == 0 ? Empty : ParseJson(S(a[0]), null));
            Define("fn:parse-json($json-text as xs:string?, $options as map(*)) as item()?", (c, a) => a[0].Count == 0 ? Empty : ParseJson(S(a[0]), Map(a[1])));
            Define("fn:json-doc($href as xs:string?) as item()?", (c, a) => a[0].Count == 0 ? Empty : ParseJson(JsonText(S(a[0]), c), null));
            Define("fn:json-doc($href as xs:string?, $options as map(*)) as item()?", (c, a) => a[0].Count == 0 ? Empty : ParseJson(JsonText(S(a[0]), c), Map(a[1])));
            Define("fn:json-to-xml($json-text as xs:string?) as document-node()?", (c, a) => a[0].Count == 0 ? Empty : JsonToXml(S(a[0]), null, c));
            Define("fn:json-to-xml($json-text as xs:string?, $options as map(*)) as document-node()?", (c, a) => a[0].Count == 0 ? Empty : JsonToXml(S(a[0]), Map(a[1]), c));
            Define("fn:xml-to-json($input as node()?) as xs:string?", (c, a) => a[0].Count == 0 ? Empty : Str(XmlToJson((XPathNavigator)a[0][0], null)));
            Define("fn:xml-to-json($input as node()?, $options as map(*)) as xs:string?", (c, a) => a[0].Count == 0 ? Empty : Str(XmlToJson((XPathNavigator)a[0][0], Map(a[1]))));
        }

        private static string JsonText(string href, FunctionContext c)
        {
            try
            {
                return UnparsedText(href, null, c);
            }
            catch (XdmException e) when (e.Code.StartsWith("FOUT", System.StringComparison.Ordinal))
            {
                throw new XdmException("FODC0002", e.Message);
            }
        }

        // ---------------------------------------------------------------- options

        private sealed class JsonOptions
        {
            public bool Liberal;
            public string Duplicates = "use-first";
            public bool Escape;
            public bool Validate;
            public bool Indent;
            public bool HasFallback;
        }

        private static JsonOptions Options(XdmMap? map, bool toXml)
        {
            var o = new JsonOptions { Duplicates = toXml ? "retain" : "use-first" };
            if (map == null)
            {
                return o;
            }
            bool Flag(string name, bool value)
            {
                if (map.Get(AtomicValue.OfString(name)) is not IList<XPathItem> v)
                {
                    return value;
                }
                return v.Count == 1 && v[0] is BooleanValue b ? b.Boolean : throw Err.XPTY0004($"The {name} option must be an xs:boolean");
            }
            o.Liberal = Flag("liberal", false);
            o.Escape = Flag("escape", false);
            o.Validate = Flag("validate", false);
            o.Indent = Flag("indent", false);
            if (map.Get(AtomicValue.OfString("duplicates")) is IList<XPathItem> d)
            {
                if (d.Count != 1 || d[0] is not AtomicValue s || !s.Type.DerivesFrom(AtomicTypes.String))
                {
                    throw Err.XPTY0004("The duplicates option must be an xs:string");
                }
                o.Duplicates = s.Value;
                bool valid = toXml ? o.Duplicates is "reject" or "retain" or "use-first" : o.Duplicates is "reject" or "use-first" or "use-last";
                if (!valid)
                {
                    throw new XdmException("FOJS0005", $"'{o.Duplicates}' is not a value of the duplicates option");
                }
            }
            if (map.Get(AtomicValue.OfString("fallback")) is IList<XPathItem> f)
            {
                if (f.Count != 1 || f[0] is not XdmFunction { Arity: 1 } || f[0] is XdmMap or XdmArray)
                {
                    throw Err.XPTY0004("The fallback option must be a function(xs:string) as xs:string");
                }
                if (o.Escape)
                {
                    throw new XdmException("FOJS0005", "The escape and fallback options can't both be set");
                }
                o.HasFallback = true;
            }
            if (o.Validate && toXml)
            {
                throw new XdmException("FOJS0004", "json-to-xml: validation needs a schema-aware processor");
            }
            return o;
        }

        // ---------------------------------------------------------------- the parser

        /// <summary>A JSON value as parsed: an object, array, string, number, boolean or null.</summary>
        private abstract record JsonValue;

        private sealed record JsonObject(List<(string Key, string RawKey, JsonValue Value)> Members) : JsonValue;

        private sealed record JsonArray(List<JsonValue> Members) : JsonValue;

        // Value: the unescaped string; Raw: the string with JSON escapes for the special characters (escape="true").
        private sealed record JsonString(string Value, string Raw) : JsonValue;

        private sealed record JsonNumber(string Lexical) : JsonValue;

        private sealed record JsonBoolean(bool Value) : JsonValue;

        private sealed record JsonNull : JsonValue;

        private sealed class JsonParser
        {
            private readonly string _s;
            private readonly bool _liberal;
            private int _i;

            public JsonParser(string s, bool liberal)
            {
                _s = s;
                _liberal = liberal;
            }

            private XdmException Error(string message) => new("FOJS0001", $"Invalid JSON at offset {_i}: {message}");

            public JsonValue Parse()
            {
                SkipSpace();
                JsonValue v = Value();
                SkipSpace();
                if (_i < _s.Length)
                {
                    throw Error("text after the value");
                }
                return v;
            }

            private void SkipSpace()
            {
                while (_i < _s.Length && _s[_i] is ' ' or '\t' or '\n' or '\r')
                {
                    _i++;
                }
            }

            private JsonValue Value()
            {
                if (_i >= _s.Length)
                {
                    throw Error("a value is missing");
                }
                switch (_s[_i])
                {
                    case '{':
                        return Object();
                    case '[':
                        return Array();
                    case '"':
                        return String();
                    case 't':
                        Expect("true");
                        return new JsonBoolean(true);
                    case 'f':
                        Expect("false");
                        return new JsonBoolean(false);
                    case 'n':
                        Expect("null");
                        return new JsonNull();
                    default:
                        return Number();
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                {
                    throw Error($"'{word}' expected");
                }
                _i += word.Length;
            }

            private JsonObject Object()
            {
                _i++;
                var members = new List<(string, string, JsonValue)>();
                SkipSpace();
                if (_i < _s.Length && _s[_i] == '}')
                {
                    _i++;
                    return new JsonObject(members);
                }
                while (true)
                {
                    SkipSpace();
                    if (_i >= _s.Length || _s[_i] != '"')
                    {
                        throw Error("a key string expected");
                    }
                    JsonString key = String();
                    SkipSpace();
                    if (_i >= _s.Length || _s[_i] != ':')
                    {
                        throw Error("':' expected");
                    }
                    _i++;
                    SkipSpace();
                    members.Add((key.Value, key.Raw, Value()));
                    SkipSpace();
                    if (_i < _s.Length && _s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (_i < _s.Length && _s[_i] == '}')
                    {
                        _i++;
                        return new JsonObject(members);
                    }
                    throw Error("',' or '}' expected");
                }
            }

            private JsonArray Array()
            {
                _i++;
                var members = new List<JsonValue>();
                SkipSpace();
                if (_i < _s.Length && _s[_i] == ']')
                {
                    _i++;
                    return new JsonArray(members);
                }
                while (true)
                {
                    SkipSpace();
                    members.Add(Value());
                    SkipSpace();
                    if (_i < _s.Length && _s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (_i < _s.Length && _s[_i] == ']')
                    {
                        _i++;
                        return new JsonArray(members);
                    }
                    throw Error("',' or ']' expected");
                }
            }

            private JsonString String()
            {
                _i++;
                var value = new StringBuilder();
                var raw = new StringBuilder();
                while (true)
                {
                    if (_i >= _s.Length)
                    {
                        throw Error("the string isn't closed");
                    }
                    char ch = _s[_i++];
                    if (ch == '"')
                    {
                        return new JsonString(value.ToString(), raw.ToString());
                    }
                    if (ch < 0x20 && !_liberal)
                    {
                        throw Error("a control character in a string");
                    }
                    if (ch != '\\')
                    {
                        if (char.IsSurrogate(ch) && !(char.IsHighSurrogate(ch) && _i < _s.Length && char.IsLowSurrogate(_s[_i])))
                        {
                            value.Append('�');
                            AppendEscaped(raw, ch);
                            continue;
                        }
                        value.Append(ch);
                        AppendEscaped(raw, ch);
                        continue;
                    }
                    if (_i >= _s.Length)
                    {
                        throw Error("an escape is incomplete");
                    }
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': value.Append('"'); raw.Append('"'); break;
                        case '\\': value.Append('\\'); raw.Append("\\\\"); break;
                        case '/': value.Append('/'); raw.Append('/'); break;
                        case 'b': Special(value, raw, '\b'); break;
                        case 'f': Special(value, raw, '\f'); break;
                        case 'n': Special(value, raw, '\n'); break;
                        case 'r': Special(value, raw, '\r'); break;
                        case 't': Special(value, raw, '\t'); break;
                        case 'u':
                        {
                            int cp = Hex4();
                            // A surrogate pair written as two escapes is one character.
                            if (cp is >= 0xD800 and <= 0xDBFF && _i + 1 < _s.Length && _s[_i] == '\\' && _s[_i + 1] == 'u')
                            {
                                int save = _i;
                                _i += 2;
                                int low = Hex4();
                                if (low is >= 0xDC00 and <= 0xDFFF)
                                {
                                    value.Append((char)cp).Append((char)low);
                                    raw.Append((char)cp).Append((char)low);
                                    break;
                                }
                                _i = save;
                            }
                            Special(value, raw, (char)cp);
                            break;
                        }
                        default:
                            throw Error($"'\\{e}' is not a JSON escape");
                    }
                }
            }

            // A character from an escape: it is kept if it is valid XML; otherwise U+FFFD in the value, and the escape
            // in the raw form.
            private static void Special(StringBuilder value, StringBuilder raw, char ch)
            {
                bool valid = ch is '\t' or '\n' or '\r' || ch >= 0x20 && !char.IsSurrogate(ch) && ch is not ('￾' or '￿');
                value.Append(valid ? ch : '�');
                AppendEscaped(raw, ch);
            }

            private int Hex4()
            {
                if (_i + 4 > _s.Length || !int.TryParse(_s.AsSpan(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int cp))
                {
                    throw Error("\\u needs four hexadecimal digits");
                }
                _i += 4;
                return cp;
            }

            private JsonNumber Number()
            {
                int start = _i;
                if (_i < _s.Length && _s[_i] == '-') _i++;
                if (_i < _s.Length && _s[_i] == '0') _i++;
                else if (_i < _s.Length && char.IsAsciiDigit(_s[_i])) while (_i < _s.Length && char.IsAsciiDigit(_s[_i])) _i++;
                else throw Error("a value expected");
                if (_i < _s.Length && _s[_i] == '.')
                {
                    _i++;
                    if (_i >= _s.Length || !char.IsAsciiDigit(_s[_i])) throw Error("digits expected after '.'");
                    while (_i < _s.Length && char.IsAsciiDigit(_s[_i])) _i++;
                }
                if (_i < _s.Length && _s[_i] is 'e' or 'E')
                {
                    _i++;
                    if (_i < _s.Length && _s[_i] is '+' or '-') _i++;
                    if (_i >= _s.Length || !char.IsAsciiDigit(_s[_i])) throw Error("digits expected in the exponent");
                    while (_i < _s.Length && char.IsAsciiDigit(_s[_i])) _i++;
                }
                return new JsonNumber(_s[start.._i]);
            }
        }

        /// <summary>A character in JSON-escaped form, for the special characters of escape="true" (F&amp;O 3.1 §17.5.1).</summary>
        private static void AppendEscaped(StringBuilder sb, char ch)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); return;
                case '\b': sb.Append("\\b"); return;
                case '\f': sb.Append("\\f"); return;
                case '\n': sb.Append("\\n"); return;
                case '\r': sb.Append("\\r"); return;
                case '\t': sb.Append("\\t"); return;
            }
            if (ch < 0x20 || ch is >= '\u007F' and <= '\u009F' || char.IsSurrogate(ch) || ch is '￾' or '￿')
            {
                sb.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                return;
            }
            sb.Append(ch);
        }

        // ---------------------------------------------------------------- parse-json

        private static IList<XPathItem> ParseJson(string text, XdmMap? options)
        {
            JsonOptions o = Options(options, toXml: false);
            JsonValue value = new JsonParser(text, o.Liberal).Parse();
            return ToXdm(value, o);
        }

        private static IList<XPathItem> ToXdm(JsonValue v, JsonOptions o)
        {
            switch (v)
            {
                case JsonObject obj:
                {
                    var entries = new Dictionary<string, (string Key, IList<XPathItem> Value)>(System.StringComparer.Ordinal);
                    var order = new List<string>();
                    foreach (var (key, raw, member) in obj.Members)
                    {
                        string k = o.Escape ? raw : key;
                        if (entries.ContainsKey(k))
                        {
                            switch (o.Duplicates)
                            {
                                case "reject":
                                    throw new XdmException("FOJS0003", $"The JSON object has the key '{k}' twice");
                                case "use-last":
                                    entries[k] = (k, ToXdm(member, o));
                                    break;
                            }
                            continue;
                        }
                        entries[k] = (k, ToXdm(member, o));
                        order.Add(k);
                    }
                    return One(XdmMap.Of(order.Select(k => new KeyValuePair<AtomicValue, IList<XPathItem>>(AtomicValue.OfString(k), entries[k].Value))));
                }
                case JsonArray array:
                    return One(XdmArray.Of(array.Members.Select(m => ToXdm(m, o))));
                case JsonString s:
                    return Str(o.Escape ? s.Raw : s.Value);
                case JsonNumber n:
                    return One(AtomicValue.OfDouble(double.Parse(n.Lexical, NumberStyles.Float, CultureInfo.InvariantCulture)));
                case JsonBoolean b:
                    return Bool(b.Value);
                default:
                    return Empty;
            }
        }

        // ---------------------------------------------------------------- json-to-xml

        private static IList<XPathItem> JsonToXml(string text, XdmMap? options, FunctionContext c)
        {
            JsonOptions o = Options(options, toXml: true);
            JsonValue value = new JsonParser(text, o.Liberal).Parse();
            var sb = new StringBuilder();
            using (XmlWriter w = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true }))
            {
                WriteJsonXml(w, value, null, o, root: true);
            }
            var doc = new InternalXPathDocument(new XmlTextReader(new StringReader(sb.ToString())), XmlSpace.Preserve) { NoDocumentUri = true };
            return One(doc.CreateNavigator());
        }

        private static void WriteJsonXml(XmlWriter w, JsonValue v, (string Value, string Raw)? key, JsonOptions o, bool root)
        {
            string name = v switch
            {
                JsonObject => "map",
                JsonArray => "array",
                JsonString => "string",
                JsonNumber => "number",
                JsonBoolean => "boolean",
                _ => "null",
            };
            w.WriteStartElement(name, FnNamespace);
            if (key is var (k, raw))
            {
                string written = o.Escape ? raw : k;
                w.WriteAttributeString("key", written);
                if (o.Escape && written.Contains('\\'))
                {
                    w.WriteAttributeString("escaped-key", "true");
                }
            }
            switch (v)
            {
                case JsonObject obj:
                {
                    var seen = new HashSet<string>(System.StringComparer.Ordinal);
                    foreach (var (mk, mraw, member) in obj.Members)
                    {
                        if (!seen.Add(mk))
                        {
                            if (o.Duplicates == "reject")
                            {
                                throw new XdmException("FOJS0003", $"The JSON object has the key '{mk}' twice");
                            }
                            if (o.Duplicates == "use-first")
                            {
                                continue;
                            }
                        }
                        WriteJsonXml(w, member, (mk, mraw), o, root: false);
                    }
                    break;
                }
                case JsonArray array:
                    foreach (JsonValue member in array.Members)
                    {
                        WriteJsonXml(w, member, null, o, root: false);
                    }
                    break;
                case JsonString s:
                    if (o.Escape && s.Raw.Contains('\\'))
                    {
                        w.WriteAttributeString("escaped", "true");
                        w.WriteString(s.Raw);
                    }
                    else
                    {
                        w.WriteString(o.Escape ? s.Raw : s.Value);
                    }
                    break;
                case JsonNumber n:
                    w.WriteString(n.Lexical);
                    break;
                case JsonBoolean b:
                    w.WriteString(b.Value ? "true" : "false");
                    break;
            }
            w.WriteEndElement();
        }

        // ---------------------------------------------------------------- xml-to-json

        private static string XmlToJson(XPathNavigator node, XdmMap? options)
        {
            JsonOptions o = Options(options, toXml: false);
            XPathNavigator n = node.Clone();
            if (n.NodeType == XPathNodeType.Root)
            {
                XPathNavigator? element = null;
                if (n.MoveToFirstChild())
                {
                    do
                    {
                        if (n.NodeType == XPathNodeType.Element)
                        {
                            if (element != null)
                            {
                                throw Invalid("the document has more than one element");
                            }
                            element = n.Clone();
                        }
                        else if (n.NodeType is XPathNodeType.Text && n.Value.Trim().Length > 0)
                        {
                            throw Invalid("the document has text outside its element");
                        }
                    }
                    while (n.MoveToNext());
                }
                n = element ?? throw Invalid("the document has no element");
            }
            if (n.NodeType != XPathNodeType.Element)
            {
                throw Invalid($"a {n.NodeType} node can't be converted");
            }
            var sb = new StringBuilder();
            WriteJson(sb, n, o.Indent, 0);
            return sb.ToString();
        }

        private static XdmException Invalid(string message) => new("FOJS0006", $"xml-to-json: {message}");

        private static void WriteJson(StringBuilder sb, XPathNavigator e, bool indent, int depth)
        {
            if (e.NamespaceURI != FnNamespace)
            {
                throw Invalid($"the element {e.Name} isn't in the namespace {FnNamespace}");
            }
            // Attributes: key and escaped-key belong to map members, escaped to strings; others must be in a namespace.
            string? escapedAttr = null;
            XPathNavigator a = e.Clone();
            if (a.MoveToFirstAttribute())
            {
                do
                {
                    if (a.NamespaceURI.Length > 0 && a.NamespaceURI != FnNamespace)
                    {
                        continue;
                    }
                    switch (a.LocalName)
                    {
                        case "key" or "escaped-key" when a.NamespaceURI.Length == 0:
                            break;
                        case "escaped" when a.NamespaceURI.Length == 0 && e.LocalName == "string":
                            escapedAttr = a.Value;
                            break;
                        default:
                            throw Invalid($"the attribute {a.Name} isn't allowed on {e.LocalName}");
                    }
                }
                while (a.MoveToNextAttribute());
            }
            switch (e.LocalName)
            {
                case "map":
                {
                    sb.Append('{');
                    var keys = new HashSet<string>(System.StringComparer.Ordinal);
                    bool first = true;
                    foreach (XPathNavigator child in Children(e))
                    {
                        string? key = child.GetAttribute("key", "");
                        if (!child.MoveToAttribute("key", ""))
                        {
                            throw Invalid("a member of a map has no key attribute");
                        }
                        child.MoveToParent();
                        bool keyEscaped = Boolean(child.GetAttribute("escaped-key", ""), "escaped-key");
                        string normal = keyEscaped ? Unescape(key) : key;
                        if (!keys.Add(normal))
                        {
                            throw Invalid($"the map has the key '{normal}' twice");
                        }
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, indent, depth + 1);
                        sb.Append('"').Append(keyEscaped ? Reescape(key) : Escape(key)).Append("\":");
                        if (indent) sb.Append(' ');
                        WriteJson(sb, child, indent, depth + 1);
                    }
                    if (!first) NewLine(sb, indent, depth);
                    sb.Append('}');
                    break;
                }
                case "array":
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (XPathNavigator child in Children(e))
                    {
                        if (child.MoveToAttribute("key", ""))
                        {
                            throw Invalid("a member of an array has a key attribute");
                        }
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, indent, depth + 1);
                        WriteJson(sb, child, indent, depth + 1);
                    }
                    if (!first) NewLine(sb, indent, depth);
                    sb.Append(']');
                    break;
                }
                case "string":
                {
                    NoElementChildren(e);
                    bool escaped = escapedAttr != null && Boolean(escapedAttr, "escaped");
                    sb.Append('"').Append(escaped ? Reescape(e.Value) : Escape(e.Value)).Append('"');
                    break;
                }
                case "number":
                {
                    NoElementChildren(e);
                    string v = e.Value.Trim();
                    if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d) ||
                        !Casting.IsCastable(AtomicValue.OfString(e.Value), AtomicTypes.Double))
                    {
                        throw Invalid($"'{e.Value}' is not a JSON number");
                    }
                    sb.Append(Lexical.DoubleToString(d));
                    break;
                }
                case "boolean":
                {
                    NoElementChildren(e);
                    string v = e.Value.Trim();
                    sb.Append(v switch
                    {
                        "true" or "1" => "true",
                        "false" or "0" => "false",
                        _ => throw Invalid($"'{e.Value}' is not a boolean"),
                    });
                    break;
                }
                case "null":
                    if (e.HasChildren && e.Value.Length > 0 || Children(e).Any())
                    {
                        throw Invalid("null must be empty");
                    }
                    sb.Append("null");
                    break;
                default:
                    throw Invalid($"{e.LocalName} is not an element of the JSON representation");
            }
        }

        private static void NewLine(StringBuilder sb, bool indent, int depth)
        {
            if (indent)
            {
                sb.Append('\n').Append(' ', depth * 2);
            }
        }

        private static bool Boolean(string value, string name) => value.Trim() switch
        {
            "" or "false" or "0" => false,
            "true" or "1" => true,
            _ => throw Invalid($"{name}=\"{value}\" is not a boolean"),
        };

        private static void NoElementChildren(XPathNavigator e)
        {
            XPathNavigator c = e.Clone();
            if (c.MoveToFirstChild())
            {
                do
                {
                    if (c.NodeType == XPathNodeType.Element)
                    {
                        throw Invalid($"{e.LocalName} can't have element children");
                    }
                }
                while (c.MoveToNext());
            }
        }

        // The element children of a map or array; text other than whitespace is an error.
        private static IEnumerable<XPathNavigator> Children(XPathNavigator e)
        {
            XPathNavigator c = e.Clone();
            if (!c.MoveToFirstChild())
            {
                yield break;
            }
            do
            {
                switch (c.NodeType)
                {
                    case XPathNodeType.Element:
                        yield return c.Clone();
                        break;
                    case XPathNodeType.Text when c.Value.Trim().Length > 0:
                        throw Invalid($"{e.LocalName} can't have text content");
                }
            }
            while (c.MoveToNext());
        }

        // A string for JSON output: the quotation mark, backslash, solidus and control characters are escaped.
        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '/': sb.Append("\\/"); break;
                    default: AppendEscaped(sb, ch); break;
                }
            }
            return sb.ToString();
        }

        // An already-escaped string (escaped="true"): its escapes must be valid JSON (FOJS0007); unescaped quotation
        // marks, solidi and control characters are escaped.
        private static string Reescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\\')
                {
                    if (i + 1 >= s.Length)
                    {
                        throw new XdmException("FOJS0007", "xml-to-json: an escape is incomplete");
                    }
                    char e = s[i + 1];
                    if (e == 'u')
                    {
                        if (i + 6 > s.Length || !int.TryParse(s.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
                        {
                            throw new XdmException("FOJS0007", "xml-to-json: \\u needs four hexadecimal digits");
                        }
                        sb.Append(s, i, 6);
                        i += 5;
                        continue;
                    }
                    if (e is not ('"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't'))
                    {
                        throw new XdmException("FOJS0007", $"xml-to-json: '\\{e}' is not a JSON escape");
                    }
                    sb.Append('\\').Append(e);
                    i++;
                    continue;
                }
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '/': sb.Append("\\/"); break;
                    default: AppendEscaped(sb, ch); break;
                }
            }
            return sb.ToString();
        }

        private static string Unescape(string s)
        {
            var parser = new JsonParser("\"" + s.Replace("\"", "\\\"") + "\"", true);
            try
            {
                return ((JsonString)parser.Parse()).Value;
            }
            catch (XdmException)
            {
                throw new XdmException("FOJS0007", $"xml-to-json: '{s}' has an invalid escape");
            }
        }
    }
}
