// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Xsl3: the json and adaptive output methods (Serialization 3.1 §10, §11), which serialize the raw result
    /// sequence (maps, arrays, atomic values and nodes) rather than a tree.
    /// </summary>
    internal static class ItemSerializer
    {
        public static void Write(IList<XPathItem> items, OutputExtras extras, TextWriter output)
        {
            var sb = new StringBuilder();
            if (extras.RawMethod == "json")
            {
                if (items.Count > 1)
                {
                    throw new XdmException("SERE0023", $"The json output method can't serialize a sequence of {items.Count} items");
                }
                Json(sb, items.Count == 0 ? null : items[0], extras, 0);
            }
            else
            {
                string separator = extras.ItemSeparator ?? "\n";
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(separator);
                    }
                    Adaptive(sb, items[i], top: true);
                }
            }
            output.Write(extras.Normalization is NormalizationForm form ? sb.ToString().Normalize(form) : sb.ToString());
        }

        /// <summary>
        /// Sequence normalization with item-separator (Serialization 3.1 §2), for the xml, html, xhtml and text
        /// methods: the items, written with the separator between them.
        /// </summary>
        public static void WriteSequence(IList<XPathItem> items, XmlWriterSettings settings, OutputExtras extras, System.Func<XmlWriterSettings, XmlWriter> factory)
        {
            OutputExtras plain = extras.Clone();
            plain.RawMethod = null;
            XmlWriterSettings s = settings.Clone();
            s.ConformanceLevel = ConformanceLevel.Fragment;
            using XmlWriter w = PrincipalResult.Create(factory, s, plain);
            for (int i = 0; i < items.Count; i++)
            {
                // Without a separator, adjacent atomic values are separated by a space.
                if (i > 0 && (extras.ItemSeparator != null || items[i] is not XPathNavigator && items[i - 1] is not XPathNavigator))
                {
                    w.WriteString(extras.ItemSeparator ?? " ");
                }
                switch (items[i])
                {
                    case XPathNavigator { NodeType: XPathNodeType.Attribute or XPathNodeType.Namespace } n:
                        throw new XdmException("SENR0001", $"An {n.NodeType.ToString().ToLowerInvariant()} node can't be serialized on its own");
                    case XPathNavigator { NodeType: XPathNodeType.Root } doc:
                        XPathNavigator child = doc.Clone();
                        if (child.MoveToFirstChild())
                        {
                            do
                            {
                                w.WriteNode(child, defattr: true);
                            }
                            while (child.MoveToNext());
                        }
                        break;
                    case XPathNavigator node:
                        w.WriteNode(node, defattr: true);
                        break;
                    case XdmFunction f:
                        throw new XdmException("SENR0001", $"{f.Describe()} can't be serialized with this method");
                    case var item:
                        w.WriteString(item.Value);
                        break;
                }
            }
            w.Flush();
        }

        // ------------------------------------------------------------ json

        private static void Json(StringBuilder sb, XPathItem? item, OutputExtras extras, int depth)
        {
            switch (item)
            {
                case null:
                    sb.Append("null");
                    break;
                case XdmMap map:
                {
                    sb.Append('{');
                    var seen = new HashSet<string>(System.StringComparer.Ordinal);
                    bool first = true;
                    foreach (var (key, value) in map.Entries)
                    {
                        string name = key.Value;
                        if (!seen.Add(name) && !extras.AllowDuplicateNames)
                        {
                            throw new XdmException("SERE0022", $"The map has two keys that serialize as \"{name}\"");
                        }
                        if (!first)
                        {
                            sb.Append(',');
                        }
                        first = false;
                        Indent(sb, extras, depth + 1);
                        JsonString(sb, name);
                        sb.Append(extras.Indent ? ": " : ":");
                        Json(sb, Single(value), extras, depth + 1);
                    }
                    if (!first)
                    {
                        Indent(sb, extras, depth);
                    }
                    sb.Append('}');
                    break;
                }
                case XdmArray array:
                {
                    sb.Append('[');
                    for (int i = 0; i < array.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(',');
                        }
                        Indent(sb, extras, depth + 1);
                        Json(sb, Single(array.Members[i]), extras, depth + 1);
                    }
                    if (array.Count > 0)
                    {
                        Indent(sb, extras, depth);
                    }
                    sb.Append(']');
                    break;
                }
                case XdmFunction f:
                    throw new XdmException("SERE0021", $"The json output method can't serialize {f.Describe()}");
                case XPathNavigator node:
                    JsonString(sb, extras.JsonNodeOutputMethod == "text" ? node.Value : NodeAsXml(node));
                    break;
                case AtomicValue a:
                    switch (a.Type.Primitive)
                    {
                        case Primitive.Boolean:
                            sb.Append(a.Value == "true" ? "true" : "false");
                            break;
                        case Primitive.Double or Primitive.Float or Primitive.Decimal or Primitive.Integer:
                            string number = a.Value;
                            if (number is "NaN" or "INF" or "-INF")
                            {
                                throw new XdmException("SERE0020", $"The json output method can't serialize the number {number}");
                            }
                            sb.Append(number);
                            break;
                        default:
                            JsonString(sb, a.Value);
                            break;
                    }
                    break;
                default:
                    JsonString(sb, item.Value);
                    break;
            }
        }

        // A map entry's value or an array member: one item, or null for none (SERE0023 for more).
        private static XPathItem? Single(IList<XPathItem> value) => value.Count switch
        {
            0 => null,
            1 => value[0],
            _ => throw new XdmException("SERE0023", $"The json output method can't serialize a sequence of {value.Count} items"),
        };

        private static void Indent(StringBuilder sb, OutputExtras extras, int depth)
        {
            if (extras.Indent)
            {
                sb.Append('\n').Append(' ', 2 * depth);
            }
        }

        private static void JsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '/': sb.Append("\\/"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c is >= '\u007f' and <= '\u009f')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        // ------------------------------------------------------------ adaptive

        private static void Adaptive(StringBuilder sb, XPathItem item, bool top)
        {
            switch (item)
            {
                case XdmMap map:
                {
                    sb.Append("map{");
                    bool first = true;
                    foreach (var (key, value) in map.Entries)
                    {
                        if (!first)
                        {
                            sb.Append(',');
                        }
                        first = false;
                        Adaptive(sb, key, top: false);
                        sb.Append(':');
                        AdaptiveSequence(sb, value);
                    }
                    sb.Append('}');
                    break;
                }
                case XdmArray array:
                    sb.Append('[');
                    for (int i = 0; i < array.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(',');
                        }
                        AdaptiveSequence(sb, array.Members[i]);
                    }
                    sb.Append(']');
                    break;
                case XdmFunction f:
                    sb.Append(f.Name is QName name ? name.EQName : "(anonymous-function)").Append('#').Append(f.Arity);
                    break;
                case XPathNavigator node:
                    switch (node.NodeType)
                    {
                        case XPathNodeType.Attribute:
                            sb.Append(node.Name).Append("=\"").Append(EscapeAttribute(node.Value)).Append('"');
                            break;
                        case XPathNodeType.Namespace:
                            sb.Append(node.LocalName.Length == 0 ? "xmlns" : "xmlns:" + node.LocalName).Append("=\"").Append(EscapeAttribute(node.Value)).Append('"');
                            break;
                        default:
                            sb.Append(NodeAsXml(node));
                            break;
                    }
                    break;
                case AtomicValue a:
                    switch (a.Type.Primitive)
                    {
                        case Primitive.String or Primitive.UntypedAtomic or Primitive.AnyUri:
                            sb.Append('"').Append(a.Value.Replace("\"", "\"\"")).Append('"');
                            break;
                        case Primitive.Boolean:
                            sb.Append(a.Value == "true" ? "true()" : "false()");
                            break;
                        case Primitive.Decimal or Primitive.Double or Primitive.Integer:
                            sb.Append(a.Value);
                            break;
                        case Primitive.QName:
                            sb.Append(((QNameValue)a).QName.EQName);
                            break;
                        default:
                            sb.Append(a.Type.Name).Append("(\"").Append(a.Value.Replace("\"", "\"\"")).Append("\")");
                            break;
                    }
                    break;
                default:
                    sb.Append(item.Value);
                    break;
            }
        }

        private static void AdaptiveSequence(StringBuilder sb, IList<XPathItem> value)
        {
            if (value.Count == 1)
            {
                Adaptive(sb, value[0], top: false);
                return;
            }
            sb.Append('(');
            for (int i = 0; i < value.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                Adaptive(sb, value[i], top: false);
            }
            sb.Append(')');
        }

        private static string EscapeAttribute(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;").Replace("\n", "&#xA;").Replace("\r", "&#xD;").Replace("\t", "&#x9;");

        // A node serialized with the xml method, without an XML declaration.
        private static string NodeAsXml(XPathNavigator node)
        {
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            using (XmlWriter w = XmlWriter.Create(sw, new XmlWriterSettings { OmitXmlDeclaration = true, ConformanceLevel = ConformanceLevel.Fragment }))
            {
                if (node.NodeType == XPathNodeType.Root)
                {
                    XPathNavigator child = node.Clone();
                    if (child.MoveToFirstChild())
                    {
                        do
                        {
                            w.WriteNode(child, defattr: true);
                        }
                        while (child.MoveToNext());
                    }
                }
                else
                {
                    w.WriteNode(node, defattr: true);
                }
            }
            // The xml method's empty-element tag, without the space XmlWriter puts before "/>" (a ">" in content is escaped).
            return sw.ToString().Replace(" />", "/>");
        }
    }
}
