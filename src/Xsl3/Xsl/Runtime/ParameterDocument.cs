// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Xsl3: a serialization parameter document (Serialization 3.1 §3.1, the parameter-document parameter of
    /// xsl:output and xsl:result-document): an output:serialization-parameters element whose children name the
    /// parameters, each with a value attribute, and output:use-character-maps with output:character-map children.
    /// The parameters come back in the form <see cref="SerializationParameters.Apply"/> takes, QName values as EQNames.
    /// </summary>
    internal static class ParameterDocument
    {
        public const string Namespace = "http://www.w3.org/2010/xslt-xquery-serialization";

        private static readonly HashSet<string> s_parameters = new(System.StringComparer.Ordinal)
        {
            "allow-duplicate-names", "byte-order-mark", "cdata-section-elements", "doctype-public", "doctype-system", "encoding",
            "escape-uri-attributes", "html-version", "include-content-type", "indent", "item-separator", "json-node-output-method",
            "media-type", "method", "normalization-form", "omit-xml-declaration", "standalone", "suppress-indentation",
            "undeclare-prefixes", "use-character-maps", "version",
        };

        // The parameters whose values are QNames (lists of them), resolved against the element's namespaces.
        private static readonly HashSet<string> s_qnames = new(System.StringComparer.Ordinal) { "cdata-section-elements", "method", "suppress-indentation", "json-node-output-method" };

        public sealed record Parameters(List<(string Name, string Value)> Values, Dictionary<int, string> CharacterMap)
        {
            /// <summary>The values for <see cref="SerializationParameters.Apply"/>: name, U+0001, value, U+0002, …</summary>
            public string Encode(System.Func<string, bool>? include = null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var (name, value) in Values)
                {
                    if (include == null || include(name))
                    {
                        sb.Append(name).Append(SerializationParameters.NameEnd).Append(value).Append(SerializationParameters.ValueEnd);
                    }
                }
                return sb.ToString();
            }
        }

        /// <summary>Reads a parameter document; SEPM0017 if it isn't one, SEPM0019 for a parameter given twice.</summary>
        public static Parameters Read(XmlReader reader)
        {
            XElement root;
            try
            {
                root = XDocument.Load(reader).Root!;
            }
            catch (XmlException e)
            {
                throw new XdmException("SEPM0017", $"The serialization parameter document isn't well-formed: {e.Message}");
            }
            if (root.Name != XName.Get("serialization-parameters", Namespace))
            {
                throw new XdmException("SEPM0017", $"A serialization parameter document's element is output:serialization-parameters, not {root.Name}");
            }
            var values = new List<(string, string)>();
            var map = new Dictionary<int, string>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (XElement e in root.Elements())
            {
                if (e.Name.NamespaceName != Namespace)
                {
                    continue; // parameters of other vendors
                }
                string name = e.Name.LocalName;
                if (!s_parameters.Contains(name))
                {
                    throw new XdmException("SEPM0017", $"output:{name} is not a serialization parameter");
                }
                if (!seen.Add(name))
                {
                    throw new XdmException("SEPM0019", $"The serialization parameter {name} is given twice");
                }
                if (name == "use-character-maps")
                {
                    foreach (XElement c in e.Elements(XName.Get("character-map", Namespace)))
                    {
                        string character = (string?)c.Attribute("character") ?? "";
                        string? mapString = (string?)c.Attribute("map-string");
                        if (mapString == null || character.Length == 0 || character.Length > (char.IsSurrogatePair(character, 0) ? 2 : 1))
                        {
                            throw new XdmException("SEPM0017", "An output:character-map needs a single character and a map-string");
                        }
                        int cp = char.ConvertToUtf32(character, 0);
                        if (map.ContainsKey(cp))
                        {
                            throw new XdmException("SEPM0018", $"The character '{character}' is mapped twice");
                        }
                        map[cp] = mapString;
                    }
                    continue;
                }
                string value = (string?)e.Attribute("value") ?? throw new XdmException("SEPM0017", $"output:{name} needs a value attribute");
                if (s_qnames.Contains(name))
                {
                    var names = new List<string>();
                    foreach (string token in value.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        names.Add(ToEQName(token, e));
                    }
                    value = string.Join(" ", names);
                }
                values.Add((name, value));
            }
            return new Parameters(values, map);
        }

        // A lexical QName as an EQName; unprefixed names are in no namespace.
        private static string ToEQName(string token, XElement scope)
        {
            if (token.StartsWith("Q{", System.StringComparison.Ordinal))
            {
                return token;
            }
            int colon = token.IndexOf(':');
            if (colon < 0)
            {
                return "Q{}" + token;
            }
            XNamespace? ns = scope.GetNamespaceOfPrefix(token[..colon]) ?? throw new XdmException("SEPM0017", $"The prefix of '{token}' is not declared");
            return $"Q{{{ns.NamespaceName}}}{token[(colon + 1)..]}";
        }
    }
}
