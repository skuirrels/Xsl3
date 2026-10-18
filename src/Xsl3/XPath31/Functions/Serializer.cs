// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Text;
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

        private static void CheckCharacterMaps(XPathNavigator useCharacterMaps)
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
            }
            while (m.MoveToNext());
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
            string method = "xml", separator = " ";
            bool indent = false, omitDeclaration = true;
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
                            CheckCharacterMaps(p);
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
                            case "byte-order-mark" or "escape-uri-attributes" or "include-content-type" or "undeclare-prefixes" or "allow-duplicate-names":
                                YesNo(value, name);
                                break;
                        }
                    }
                    while (p.MoveToNext());
                }
            }

            var sb = new StringBuilder();
            if (method == "text")
            {
                bool first = true;
                foreach (XPathItem item in items)
                {
                    if (!first) sb.Append(separator);
                    sb.Append(Sequences.StringValue(item));
                    first = false;
                }
                return sb.ToString();
            }

            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = omitDeclaration,
                Indent = indent,
                ConformanceLevel = ConformanceLevel.Fragment,
            };
            using (var sw = new StringWriter(sb))
            using (XmlWriter w = XmlWriter.Create(sw, settings))
            {
                bool previousAtomic = false;
                foreach (XPathItem item in items)
                {
                    if (item is XPathNavigator nav)
                    {
                        if (nav.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace)
                        {
                            throw new XdmException("SENR0001", $"An {nav.NodeType.ToString().ToLowerInvariant()} node can't be serialized on its own");
                        }
                        w.WriteNode(nav, defattr: true);
                        previousAtomic = false;
                    }
                    else
                    {
                        if (previousAtomic) w.WriteString(separator);
                        w.WriteString(item.Value);
                        previousAtomic = true;
                    }
                }
            }
            return sb.ToString();
        }
    }
}
