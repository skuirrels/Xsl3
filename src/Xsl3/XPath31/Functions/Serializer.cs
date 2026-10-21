// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    /// <summary>
    /// fn:serialize for the xml, text and html methods with the common parameters. Full Serialization 3.1
    /// (json, adaptive, character maps, …) arrives with SPEC §15 in P5.
    /// </summary>
    internal static class Serializer
    {
        private static readonly HashSet<string> s_parameters = new(System.StringComparer.Ordinal)
        {
            "allow-duplicate-names", "byte-order-mark", "cdata-section-elements", "doctype-public", "doctype-system",
            "encoding", "escape-uri-attributes", "html-version", "include-content-type", "indent", "item-separator",
            "json-node-output-method", "media-type", "method", "normalization-form", "omit-xml-declaration",
            "standalone", "suppress-indentation", "undeclare-prefixes", "use-character-maps", "version",
        };

        private static bool YesNo(string value, string name) => value.Trim() switch
        {
            "yes" or "true" or "1" => true,
            "no" or "false" or "0" => false,
            _ => throw new XdmException("SEPM0016", $"'{value}' is not a valid value for output:{name}"),
        };

        private static void CheckCharacterMaps(XPathNavigator useCharacterMaps, Dictionary<int, string> map)
        {
            var characters = new HashSet<string>(System.StringComparer.Ordinal);
            XPathNavigator m = useCharacterMaps.Clone();
            if (!m.MoveToFirstChild())
            {
                return;
            }
            do
            {
                if (m.NodeType != XPathNodeType.Element)
                {
                    continue;
                }
                if (m.LocalName != "character-map" || m.NamespaceURI != OutputNamespace)
                {
                    throw new XdmException("SEPM0017", "output:use-character-maps may contain only output:character-map elements");
                }
                CheckAttributes(m, "character", "map-string");
                string character = m.GetAttribute("character", "");
                if (System.Globalization.StringInfo.GetNextTextElementLength(character) != character.Length || character.Length == 0 ||
                    (character.Length == 2 && !char.IsSurrogatePair(character[0], character[1])) || character.Length > 2)
                {
                    throw new XdmException("SEPM0017", $"output:character-map/@character must be a single character, not '{character}'");
                }
                if (!characters.Add(character))
                {
                    throw new XdmException("SEPM0018", $"Character '{character}' is mapped twice");
                }
                map[char.ConvertToUtf32(character, 0)] = m.GetAttribute("map-string", "");
            }
            while (m.MoveToNext());
        }

        private static string MapCharacters(string s, Dictionary<int, string> map)
        {
            if (map.Count == 0)
            {
                return s;
            }
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                int cp = char.IsSurrogatePair(s, i) ? char.ConvertToUtf32(s[i], s[++i]) : s[i];
                if (map.TryGetValue(cp, out string? replacement)) sb.Append(replacement);
                else sb.Append(char.ConvertFromUtf32(cp));
            }
            return sb.ToString();
        }

        public const string OutputNamespace = "http://www.w3.org/2010/xslt-xquery-serialization";

        // Attributes in no namespace must be ones the schema declares; attributes in other namespaces are allowed.
        private static void CheckAttributes(XPathNavigator element, params string?[]? allowed)
        {
            XPathNavigator a = element.Clone();
            if (!a.MoveToFirstAttribute())
            {
                return;
            }
            do
            {
                if (a.NamespaceURI.Length == 0 && (allowed == null || System.Array.IndexOf(allowed, a.LocalName) < 0))
                {
                    throw new XdmException("SEPM0017", $"Attribute {a.LocalName} is not allowed on {element.Name}");
                }
            }
            while (a.MoveToNextAttribute());
        }

        public static string Serialize(IList<XPathItem> items, XPathNavigator? parameters)
        {
            string method = "xml";
            string? separator = null, standalone = null;
            bool indent = false, omitDeclaration = true;
            var characterMap = new Dictionary<int, string>();
            if (parameters != null)
            {
                if (parameters.NodeType != XPathNodeType.Element || parameters.LocalName != "serialization-parameters" || parameters.NamespaceURI != OutputNamespace)
                {
                    throw Err.XPTY0004("The serialization parameters must be an output:serialization-parameters element");
                }
                CheckAttributes(parameters, allowed: null);
                var seen = new HashSet<string>(System.StringComparer.Ordinal);
                XPathNavigator p = parameters.Clone();
                if (p.MoveToFirstChild())
                {
                    do
                    {
                        if (p.NodeType != XPathNodeType.Element)
                        {
                            continue;
                        }
                        if (p.NamespaceURI.Length == 0)
                        {
                            throw new XdmException("SEPM0017", $"Serialization parameter {p.LocalName} must be in the output namespace");
                        }
                        if (!seen.Add("{" + p.NamespaceURI + "}" + p.LocalName))
                        {
                            throw new XdmException("SEPM0019", $"Serialization parameter {p.Name} is given twice");
                        }
                        if (p.NamespaceURI != OutputNamespace)
                        {
                            continue; // other namespaces are implementation extensions, ignored
                        }
                        if (!s_parameters.Contains(p.LocalName))
                        {
                            throw new XdmException("SEPM0017", $"Unknown serialization parameter output:{p.LocalName}");
                        }
                        CheckAttributes(p, p.LocalName == "use-character-maps" ? null : "value");
                        string name = p.LocalName;
                        bool hasValue = p.MoveToAttribute("value", "");
                        string value = hasValue ? p.Value : "";
                        if (hasValue) p.MoveToParent();
                        if (name == "use-character-maps")
                        {
                            if (hasValue) throw new XdmException("SEPM0017", "output:use-character-maps has character-map children, not a value");
                            CheckCharacterMaps(p, characterMap);
                            continue;
                        }
                        if (!hasValue)
                        {
                            throw new XdmException("SEPM0017", $"output:{name} needs a value attribute");
                        }
                        switch (name)
                        {
                            case "method": method = value.Trim(); break;
                            case "indent": indent = YesNo(value, name); break;
                            case "omit-xml-declaration": omitDeclaration = YesNo(value, name); break;
                            case "item-separator": separator = value; break;
                            case "standalone":
                                standalone = value.Trim() switch
                                {
                                    "omit" => null,
                                    var v => YesNo(v, name) ? "yes" : "no",
                                };
                                break;
                            case "byte-order-mark" or "escape-uri-attributes" or "include-content-type" or "undeclare-prefixes" or "allow-duplicate-names":
                                YesNo(value, name);
                                break;
                        }
                    }
                    while (p.MoveToNext());
                }
            }

            return Serialize(items, method, separator, standalone, indent, omitDeclaration, characterMap);
        }

        /// <summary>fn:serialize with the parameters as a map (F&amp;O 3.1 §14.1.1), which also selects json and adaptive.</summary>
        public static string Serialize(IList<XPathItem> items, MapItem parameters)
        {
            // The option parameter conventions (F&O 3.1 §1.5): each value is converted to the parameter's type,
            // xs:untypedAtomic by casting; anything else is XPTY0004.
            AtomicValue? Option(string name, AtomicType type)
            {
                if (parameters.Get(AtomicValue.OfString(name)) is not { Count: > 0 } value)
                {
                    return null;
                }
                IList<XPathItem> atoms = Sequences.Atomize(value);
                if (atoms.Count != 1)
                {
                    throw Err.XPTY0004($"The serialization parameter {name} must be a single {type.Name}");
                }
                var a = (AtomicValue)atoms[0];
                if (a.Type.Primitive == Primitive.UntypedAtomic)
                {
                    return Casting.Cast(a, type);
                }
                return a.Type.DerivesFrom(type) ? a : throw Err.XPTY0004($"The serialization parameter {name} must be {type.Name}, not {a.Type.Name}");
            }
            string? String(string name) => Option(name, AtomicTypes.String)?.Value;
            bool? Boolean(string name) => Option(name, AtomicTypes.Boolean) is BooleanValue b ? b.Boolean : null;
            string method = parameters.Get(AtomicValue.OfString("method")) is { Count: 1 } m && m[0] is QNameValue q ? q.QName.LocalName : String("method") ?? "xml";
            bool indent = Boolean("indent") ?? false;
            var characterMap = new Dictionary<int, string>();
            if (parameters.Get(AtomicValue.OfString("use-character-maps")) is { Count: > 0 } maps)
            {
                if (maps.Count != 1 || maps[0] is not MapItem characters)
                {
                    throw Err.XPTY0004("The serialization parameter use-character-maps must be a map(xs:string, xs:string)");
                }
                foreach (var (key, value) in characters.Entries)
                {
                    if (!key.Type.DerivesFrom(AtomicTypes.String) || value.Count != 1 || value[0] is not AtomicValue v || !v.Type.DerivesFrom(AtomicTypes.String))
                    {
                        throw Err.XPTY0004("The serialization parameter use-character-maps must be a map(xs:string, xs:string)");
                    }
                    string c = key.Value;
                    if (c.Length == 0 || c.Length > 2 || (c.Length == 2 && !char.IsSurrogatePair(c[0], c[1])))
                    {
                        throw new XdmException("SEPM0016", $"The key '{c}' of use-character-maps must be a single character");
                    }
                    characterMap[char.ConvertToUtf32(c, 0)] = v.Value;
                }
            }
            string? separator = String("item-separator");
            bool allowDuplicates = Boolean("allow-duplicate-names") ?? false;
            string? jsonNodes = parameters.Get(AtomicValue.OfString("json-node-output-method")) is { Count: 1 } j && j[0] is QNameValue jq ? jq.QName.LocalName : String("json-node-output-method");
            bool? standaloneOption = Boolean("standalone");
            bool omitDeclaration = Boolean("omit-xml-declaration") ?? true;
            foreach (string name in new[] { "byte-order-mark", "escape-uri-attributes", "include-content-type", "undeclare-prefixes" })
            {
                Boolean(name);
            }
            foreach (string name in new[] { "doctype-public", "doctype-system", "encoding", "media-type", "normalization-form", "version" })
            {
                String(name);
            }
            Option("html-version", AtomicTypes.Decimal);
            _ = (method, indent, separator, allowDuplicates, jsonNodes, standaloneOption, omitDeclaration);

            // Xsl3: the parameters as the engine's serializer takes them (SerializationParameters), so that fn:serialize
            // writes as xsl:output and xsl:result-document do: the html and xhtml methods with their doctype and meta
            // element, cdata-section-elements, encodings, normalization. A QName key names a vendor parameter, and one
            // in no namespace isn't a parameter at all (F&O 3.1 §14.1.1): both are ignored.
            var encoded = new StringBuilder();
            void Add(string name, string value) =>
                encoded.Append(name).Append(Xsl3.Runtime.SerializationParameters.NameEnd).Append(value).Append(Xsl3.Runtime.SerializationParameters.ValueEnd);
            static string Lexical(XPathItem item) => item switch
            {
                QNameValue qn => $"Q{{{qn.QName.NamespaceUri}}}{qn.QName.LocalName}",
                BooleanValue b => b.Boolean ? "yes" : "no",
                _ => item.Value,
            };
            foreach (var (key, value) in parameters.Entries)
            {
                if (key is QNameValue || key.Value is "use-character-maps" || value.Count == 0)
                {
                    continue;
                }
                IList<XPathItem> atoms = Sequences.Atomize(value);
                Add(key.Value, key.Value is "cdata-section-elements" or "suppress-indentation"
                    ? string.Join(" ", atoms.Select(Lexical))
                    : Lexical(atoms[0]));
            }
            if (parameters.Get(AtomicValue.OfString("omit-xml-declaration")) is not { Count: > 0 })
            {
                Add("omit-xml-declaration", "yes"); // fn:serialize's default
            }
            var names = new Xsl3.Runtime.XsltNameContext(new Dictionary<string, string>(), new HashSet<(string, string, int)>());
            var defaults = new System.Xml.XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            var (settings, extras, _) = Xsl3.Runtime.SerializationParameters.Apply(new Xsl3.Runtime.ResultFormat(defaults, null, false), encoded.ToString(), names);
            extras ??= new Xsl3.Runtime.OutputExtras();
            if (characterMap.Count > 0)
            {
                extras.CharacterMap = characterMap;
            }
            var sw = new StringWriter();
            if (extras.RawMethod is "json" or "adaptive")
            {
                Xsl3.Runtime.ItemSerializer.Write(items, extras, sw);
            }
            else
            {
                Xsl3.Runtime.ItemSerializer.WriteSequence(items, settings, extras, ws => System.Xml.XmlWriter.Create(sw, ws));
            }
            string result = sw.ToString();
            // Characters the encoding lacks: \uXXXX in JSON, character references otherwise (Serialization 3.1 §10, §11).
            return parameters.Get(AtomicValue.OfString("encoding")) is { Count: > 0 } && extras.RawMethod != "adaptive"
                ? Xsl3.Runtime.ItemSerializer.EscapeUnencodable(result, settings.Encoding, extras.RawMethod == "json")
                : result;
        }

        private static string Serialize(IList<XPathItem> items, string method, string? separator, string? standalone, bool indent, bool omitDeclaration,
            Dictionary<int, string> characterMap)
        {
            var sb = new StringBuilder();
            if (method == "text")
            {
                bool first = true;
                bool previousAtomic = false;
                foreach (XPathItem item in items)
                {
                    // Without item-separator, a space separates adjacent atomic values only.
                    if (!first && (separator != null || (previousAtomic && item is AtomicValue))) sb.Append(separator ?? " ");
                    sb.Append(MapCharacters(Sequences.StringValue(item), characterMap));
                    first = false;
                    previousAtomic = item is AtomicValue;
                }
                return sb.ToString();
            }

            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = omitDeclaration,
                Indent = indent,
                ConformanceLevel = ConformanceLevel.Fragment,
            };
            if (!omitDeclaration && method == "xml")
            {
                sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"");
                if (standalone != null) sb.Append(" standalone=\"").Append(standalone).Append('"');
                sb.Append("?>");
            }
            using (var sw = new StringWriter(sb))
            using (XmlWriter inner = XmlWriter.Create(sw, settings))
            using (XmlWriter w = characterMap.Count > 0 ? new CharacterMapWriter(inner, characterMap) : inner)
            {
                bool previousAtomic = false, first = true;
                foreach (XPathItem item in items)
                {
                    bool atomic = item is not XPathNavigator;
                    // Without item-separator, a space separates adjacent atomic values only.
                    if (!first && (separator != null || (previousAtomic && atomic)))
                    {
                        w.WriteString(separator ?? " ");
                    }
                    if (item is XPathNavigator nav)
                    {
                        if (nav.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace)
                        {
                            throw new XdmException("SENR0001", $"An {nav.NodeType.ToString().ToLowerInvariant()} node can't be serialized on its own");
                        }
                        w.WriteNode(nav, defattr: true);
                    }
                    else
                    {
                        w.WriteString(item.Value);
                    }
                    previousAtomic = atomic;
                    first = false;
                }
            }
            return sb.ToString();
        }
    }
}
