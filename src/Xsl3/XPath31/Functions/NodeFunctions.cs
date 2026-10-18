// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    // Node functions (F&O 3.1 §2, §13, §14.5–14.6) and resource access (§14.6, §14.8).
    internal static partial class FunctionLibrary
    {
        private static IList<XPathItem> Uri(string? uri) => uri is { Length: > 0 } ? One(new StringValue(AtomicTypes.AnyUri, uri)) : Empty;

        private static void RegisterNodes()
        {
            Define("fn:document-uri($arg as node()?) as xs:anyURI?", (c, a) => Node(a[0]) is XPathNavigator n && n.NodeType == XPathNodeType.Root ? Uri(n.BaseURI) : Empty);
            Define("fn:base-uri($arg as node()?) as xs:anyURI?", (c, a) => Node(a[0]) is XPathNavigator n ? Uri(BaseUri(n)) : Empty);
            Define("fn:generate-id($arg as node()?) as xs:string", (c, a) =>
                Str(Node(a[0]) is XPathNavigator n ? (c.Runtime?.GenerateId(n) ?? "id" + n.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture)) : ""));
            Define("fn:nilled($arg as node()?) as xs:boolean?", (c, a) => Node(a[0]) is XPathNavigator { NodeType: XPathNodeType.Element } ? Bool(false) : Empty);
            Define("fn:lang($testlang as xs:string?, $node as node()) as xs:boolean", (c, a) => Bool(Lang(S(a[0]), (XPathNavigator)a[1][0])));
            Define("fn:id($arg as xs:string*, $node as node()) as element()*", (c, a) => Id(a[0], (XPathNavigator)a[1][0], c));
            Define("fn:element-with-id($arg as xs:string*, $node as node()) as element()*", (c, a) => Id(a[0], (XPathNavigator)a[1][0], c));
            Define("fn:idref($arg as xs:string*, $node as node()) as node()*", (c, a) => RootIsDocument((XPathNavigator)a[1][0]) ? Empty : throw new XdmException("FODC0001", "fn:idref: the node is not in a tree rooted at a document node"));
            Define("fn:outermost($nodes as node()*) as node()*", (c, a) => OuterOrInnermost(a[0], c, outer: true));
            Define("fn:innermost($nodes as node()*) as node()*", (c, a) => OuterOrInnermost(a[0], c, outer: false));
            Define("fn:path($arg as node()?) as xs:string?", (c, a) => Node(a[0]) is XPathNavigator n ? Str(Path(n)) : Empty);
            Define("fn:in-scope-prefixes($element as element()) as xs:string*", (c, a) => InScopeNamespaces((XPathNavigator)a[0][0]).Keys.Select(p => (XPathItem)AtomicValue.OfString(p)).ToList());
            Define("fn:namespace-uri-for-prefix($prefix as xs:string?, $element as element()) as xs:anyURI?", (c, a) =>
                InScopeNamespaces((XPathNavigator)a[1][0]).TryGetValue(S(a[0]), out string? uri) ? Uri(uri) : Empty);
            Define("fn:resolve-QName($qname as xs:string?, $element as element()) as xs:QName?", (c, a) =>
            {
                if (a[0].Count == 0) return Empty;
                string lexical = S(a[0]);
                int colon = lexical.IndexOf(':');
                string prefix = colon < 0 ? "" : lexical[..colon], local = colon < 0 ? lexical : lexical[(colon + 1)..];
                if (!Casting.IsNCName(local) || (prefix.Length > 0 && !Casting.IsNCName(prefix)))
                {
                    throw Err.FOCA0002($"'{lexical}' is not a lexical QName");
                }
                Dictionary<string, string> ns = InScopeNamespaces((XPathNavigator)a[1][0]);
                if (!ns.TryGetValue(prefix, out string? uri))
                {
                    if (prefix.Length > 0) throw new XdmException("FONS0004", $"No namespace is bound to prefix '{prefix}'");
                    uri = "";
                }
                return One(new QNameValue(AtomicTypes.QName, new QName(uri, local, prefix)));
            });

            Define("fn:sort($input as item()*) as item()*", (c, a) => Sort(a[0], c));
            Define("fn:sort($input as item()*, $collation as xs:string?) as item()*", (c, a) => Sort(a[0], a[1].Count == 0 ? c : WithCollation(c, Coll(a[1]))));

            // ---------------------------------------------------------- documents and text resources
            Define("fn:doc($uri as xs:string?) as document-node()?", (c, a) => a[0].Count == 0 ? Empty : One(Doc(S(a[0]), c)));
            Define("fn:doc-available($uri as xs:string?) as xs:boolean", (c, a) =>
            {
                if (a[0].Count == 0) return Bool(false);
                try
                {
                    Doc(S(a[0]), c);
                    return Bool(true);
                }
                catch (XdmException e) when (e.Code is "FODC0002" or "FODC0005")
                {
                    return Bool(false);
                }
            });
            Define("fn:unparsed-text($href as xs:string?) as xs:string?", (c, a) => a[0].Count == 0 ? Empty : Str(UnparsedText(S(a[0]), null, c)));
            Define("fn:unparsed-text($href as xs:string?, $encoding as xs:string) as xs:string?", (c, a) => a[0].Count == 0 ? Empty : Str(UnparsedText(S(a[0]), S(a[1]), c)));
            Define("fn:unparsed-text-lines($href as xs:string?) as xs:string*", (c, a) => a[0].Count == 0 ? Empty : Lines(UnparsedText(S(a[0]), null, c)));
            Define("fn:unparsed-text-lines($href as xs:string?, $encoding as xs:string) as xs:string*", (c, a) => a[0].Count == 0 ? Empty : Lines(UnparsedText(S(a[0]), S(a[1]), c)));
            Define("fn:unparsed-text-available($href as xs:string?) as xs:boolean", (c, a) => Bool(a[0].Count != 0 && TextAvailable(S(a[0]), null, c)));
            Define("fn:unparsed-text-available($href as xs:string?, $encoding as xs:string) as xs:boolean", (c, a) => Bool(a[0].Count != 0 && TextAvailable(S(a[0]), S(a[1]), c)));
            Define("fn:parse-xml($arg as xs:string?) as document-node(element(*))?", (c, a) => a[0].Count == 0 ? Empty : One(ParseXml(S(a[0]), fragment: false, c)));
            Define("fn:parse-xml-fragment($arg as xs:string?) as document-node()?", (c, a) => a[0].Count == 0 ? Empty : One(ParseXml(S(a[0]), fragment: true, c)));
            Define("fn:serialize($arg as item()*) as xs:string", (c, a) => Str(Serializer.Serialize(a[0], null)));
            Define("fn:serialize($arg as item()*, $params as element()?) as xs:string", (c, a) => Str(Serializer.Serialize(a[0], Node(a[1]))));
        }

        private static string BaseUri(XPathNavigator n)
        {
            XPathNavigator nav = n.Clone();
            if (nav.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace or XPathNodeType.Text or XPathNodeType.Comment)
            {
                nav.MoveToParent();
            }
            return nav.BaseURI;
        }

        private static bool RootIsDocument(XPathNavigator n)
        {
            XPathNavigator root = n.Clone();
            root.MoveToRoot();
            return root.NodeType == XPathNodeType.Root;
        }

        // xml:lang on the nearest ancestor-or-self; equal ignoring case, or a prefix followed by '-'.
        private static bool Lang(string test, XPathNavigator node)
        {
            XPathNavigator nav = node.Clone();
            if (nav.NodeType != XPathNodeType.Element)
            {
                nav.MoveToParent();
            }
            do
            {
                string lang = nav.GetAttribute("lang", QName.Ns.Xml);
                if (nav.NodeType == XPathNodeType.Element && nav.MoveToAttribute("lang", QName.Ns.Xml))
                {
                    lang = nav.Value;
                    return lang.Equals(test, System.StringComparison.OrdinalIgnoreCase) ||
                           (lang.Length > test.Length && lang[test.Length] == '-' && lang.StartsWith(test, System.StringComparison.OrdinalIgnoreCase));
                }
            }
            while (nav.MoveToParent());
            return false;
        }

        private static IList<XPathItem> Id(IList<XPathItem> ids, XPathNavigator node, FunctionContext c)
        {
            if (!RootIsDocument(node))
            {
                throw new XdmException("FODC0001", "fn:id: the node is not in a tree rooted at a document node");
            }
            var found = new List<XPathItem>();
            foreach (XPathItem idList in ids)
            {
                foreach (string id in ((AtomicValue)idList).Value.Split(new[] { ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    XPathNavigator nav = node.Clone();
                    if (Casting.IsNCName(id) && nav.MoveToId(id))
                    {
                        found.Add(nav);
                    }
                }
            }
            return DocumentOrder(found, c);
        }

        internal static IList<XPathItem> DocumentOrder(IList<XPathItem> nodes, FunctionContext c)
        {
            var list = nodes.Select(n => (XPathNavigator)n).ToList();
            list.Sort(c.ComparePosition);
            var result = new List<XPathItem>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                if (i == 0 || c.ComparePosition(list[i - 1], list[i]) != 0)
                {
                    result.Add(list[i]);
                }
            }
            return result;
        }

        private static IList<XPathItem> OuterOrInnermost(IList<XPathItem> nodes, FunctionContext c, bool outer)
        {
            IList<XPathItem> sorted = DocumentOrder(nodes, c);
            var set = sorted.Select(n => (XPathNavigator)n).ToList();
            bool IsAncestor(XPathNavigator a, XPathNavigator d)
            {
                XPathNavigator p = d.Clone();
                while (p.MoveToParent())
                {
                    if (p.IsSamePosition(a)) return true;
                }
                return false;
            }
            return set.Where(n => outer
                    ? !set.Any(other => !ReferenceEquals(other, n) && IsAncestor(other, n))
                    : !set.Any(other => !ReferenceEquals(other, n) && IsAncestor(n, other)))
                .Select(n => (XPathItem)n).ToList();
        }

        /// <summary>fn:path (F&amp;O 3.1 §14.5.3).</summary>
        private static string Path(XPathNavigator node)
        {
            var steps = new List<string>();
            XPathNavigator nav = node.Clone();
            while (true)
            {
                switch (nav.NodeType)
                {
                    case XPathNodeType.Root:
                        return steps.Count == 0 ? "/" : "/" + string.Join("/", Enumerable.Reverse(steps));
                    case XPathNodeType.Element:
                        steps.Add($"Q{{{nav.NamespaceURI}}}{nav.LocalName}[{Position(nav, n => n.NodeType == XPathNodeType.Element && n.LocalName == nav.LocalName && n.NamespaceURI == nav.NamespaceURI)}]");
                        break;
                    case XPathNodeType.Attribute:
                        steps.Add(nav.NamespaceURI.Length == 0 ? "@" + nav.LocalName : $"@Q{{{nav.NamespaceURI}}}{nav.LocalName}");
                        break;
                    case XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace:
                        steps.Add($"text()[{Position(nav, n => n.NodeType is XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace)}]");
                        break;
                    case XPathNodeType.Comment:
                        steps.Add($"comment()[{Position(nav, n => n.NodeType == XPathNodeType.Comment)}]");
                        break;
                    case XPathNodeType.ProcessingInstruction:
                        steps.Add($"processing-instruction({nav.LocalName})[{Position(nav, n => n.NodeType == XPathNodeType.ProcessingInstruction && n.LocalName == nav.LocalName)}]");
                        break;
                    case XPathNodeType.Namespace:
                        steps.Add(nav.LocalName.Length == 0 ? "namespace::*[Q{http://www.w3.org/2005/xpath-functions}local-name()=\"\"]" : "namespace::" + nav.LocalName);
                        break;
                }
                if (!nav.MoveToParent())
                {
                    // The root of the tree isn't a document node.
                    return "Q{http://www.w3.org/2005/xpath-functions}root()" + (steps.Count > 1 ? "/" + string.Join("/", Enumerable.Reverse(steps).Skip(1)) : "");
                }
            }
        }

        private static int Position(XPathNavigator node, System.Func<XPathNavigator, bool> similar)
        {
            int position = 1;
            XPathNavigator nav = node.Clone();
            while (nav.MoveToPrevious())
            {
                if (similar(nav)) position++;
            }
            return position;
        }

        private static Dictionary<string, string> InScopeNamespaces(XPathNavigator element)
        {
            var result = new Dictionary<string, string>(System.StringComparer.Ordinal) { ["xml"] = QName.Ns.Xml };
            XPathNavigator nav = element.Clone();
            if (nav.MoveToFirstNamespace(XPathNamespaceScope.All))
            {
                do
                {
                    if (nav.Value.Length > 0)
                    {
                        result.TryAdd(nav.LocalName, nav.Value);
                    }
                }
                while (nav.MoveToNextNamespace(XPathNamespaceScope.All));
            }
            return result;
        }

        // fn:sort without a key function: by the atomised values, compared as by lt (F&O 3.1 §14.7.1).
        private static IList<XPathItem> Sort(IList<XPathItem> input, FunctionContext c)
        {
            var keyed = input.Select((item, i) => (Item: item, Key: Sequences.Atomize(Sequences.One(item)), Index: i)).ToList();
            int CompareKeys(IList<XPathItem> x, IList<XPathItem> y)
            {
                for (int i = 0; i < System.Math.Min(x.Count, y.Count); i++)
                {
                    var a = (AtomicValue)x[i];
                    var b = (AtomicValue)y[i];
                    if (a.Type.Primitive == Primitive.UntypedAtomic) a = Casting.Cast(a, AtomicTypes.String);
                    if (b.Type.Primitive == Primitive.UntypedAtomic) b = Casting.Cast(b, AtomicTypes.String);
                    bool aNaN = a is DoubleValue { Double: double.NaN } or FloatValue { Float: float.NaN };
                    bool bNaN = b is DoubleValue { Double: double.NaN } or FloatValue { Float: float.NaN };
                    if (aNaN || bNaN)
                    {
                        if (aNaN && bNaN) continue;
                        return aNaN ? -1 : 1; // NaN sorts first
                    }
                    if (Comparison.Compare(a, b, ValueComparisonOp.Lt, c.ImplicitTimezoneMinutes, c.DefaultCollation)) return -1;
                    if (Comparison.Compare(b, a, ValueComparisonOp.Lt, c.ImplicitTimezoneMinutes, c.DefaultCollation)) return 1;
                }
                return x.Count.CompareTo(y.Count);
            }
            try
            {
                keyed.Sort((x, y) =>
                {
                    int r = CompareKeys(x.Key, y.Key);
                    return r != 0 ? r : x.Index.CompareTo(y.Index); // stable
                });
            }
            catch (System.InvalidOperationException e) when (e.InnerException is XdmException inner)
            {
                throw inner; // incomparable keys: XPTY0004
            }
            return keyed.Select(k => k.Item).ToList();
        }

        private static XPathNavigator Doc(string uri, FunctionContext c)
        {
            if (uri.Contains('#'))
            {
                throw new XdmException("FODC0005", $"'{uri}' has a fragment identifier");
            }
            if (c.Runtime == null)
            {
                throw new XdmException("FODC0002", $"Document '{uri}' can't be retrieved");
            }
            try
            {
                XPathNavigator? doc = c.Runtime.ExternalContext.GetDataSource(uri, c.StaticBaseUri);
                if (doc == null)
                {
                    throw new XdmException("FODC0002", $"Document '{uri}' can't be retrieved");
                }
                return doc;
            }
            catch (Exception e) when (e is not XdmException && e is not OutOfMemoryException)
            {
                throw new XdmException("FODC0002", $"Document '{uri}' can't be retrieved: {e.InnerException?.Message ?? e.Message}");
            }
        }

        private static string UnparsedText(string href, string? encoding, FunctionContext c)
        {
            if (href.Contains('#'))
            {
                throw new XdmException("FOUT1170", $"'{href}' has a fragment identifier");
            }
            XmlResolver? resolver = c.Runtime?.ExternalContext.DataSources;
            if (resolver == null)
            {
                throw new XdmException("FOUT1170", $"'{href}' can't be retrieved");
            }
            Encoding? enc = null;
            if (encoding != null)
            {
                try
                {
                    enc = Encoding.GetEncoding(encoding.Trim(), EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                }
                catch (System.ArgumentException)
                {
                    throw new XdmException("FOUT1190", $"Encoding '{encoding}' is not supported");
                }
            }
            byte[] bytes;
            try
            {
                System.Uri? baseUri = c.StaticBaseUri != null ? resolver.ResolveUri(null, c.StaticBaseUri) : null;
                System.Uri resolved = resolver.ResolveUri(baseUri, href);
                using var stream = (Stream)resolver.GetEntity(resolved, null, typeof(Stream))!;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                bytes = ms.ToArray();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                throw new XdmException("FOUT1170", $"'{href}' can't be retrieved: {e.Message}");
            }
            string text;
            try
            {
                // A byte order mark wins over the requested encoding; UTF-8 is the default. (StreamReader's own
                // detection would swap in a decoder that doesn't throw on malformed input.)
                int skip = 0;
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                {
                    enc = new UTF8Encoding(false, throwOnInvalidBytes: true);
                    skip = 3;
                }
                else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                {
                    enc = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
                    skip = 2;
                }
                else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                {
                    enc = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
                    skip = 2;
                }
                enc ??= new UTF8Encoding(false, throwOnInvalidBytes: true);
                text = enc.GetString(bytes, skip, bytes.Length - skip);
            }
            catch (DecoderFallbackException)
            {
                throw new XdmException("FOUT1190", $"'{href}' can't be decoded");
            }
            foreach (Rune r in text.EnumerateRunes())
            {
                int cp = r.Value;
                if (!(cp == 0x9 || cp == 0xA || cp == 0xD || (cp >= 0x20 && cp <= 0xD7FF) || (cp >= 0xE000 && cp <= 0xFFFD) || cp >= 0x10000))
                {
                    throw new XdmException("FOUT1190", $"'{href}' contains a character that is not allowed in XML");
                }
            }
            return text;
        }

        private static bool TextAvailable(string href, string? encoding, FunctionContext c)
        {
            try
            {
                UnparsedText(href, encoding, c);
                return true;
            }
            catch (XdmException)
            {
                return false;
            }
        }

        private static IList<XPathItem> Lines(string text)
        {
            if (text.Length == 0)
            {
                return Empty;
            }
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (normalized.EndsWith('\n'))
            {
                normalized = normalized[..^1];
            }
            return normalized.Split('\n').Select(l => (XPathItem)AtomicValue.OfString(l)).ToList();
        }

        private static XPathNavigator ParseXml(string text, bool fragment, FunctionContext c)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                ConformanceLevel = fragment ? ConformanceLevel.Fragment : ConformanceLevel.Document,
            };
            if (fragment && text.StartsWith("<?xml", System.StringComparison.Ordinal))
            {
                // A text declaration starts an external parsed entity.
                int end = text.IndexOf("?>", System.StringComparison.Ordinal);
                // A text declaration must declare the encoding and may not be standalone.
                if (end > 0 && text[..end].Contains("encoding", System.StringComparison.Ordinal) && !text[..end].Contains("standalone", System.StringComparison.Ordinal))
                {
                    text = text[(end + 2)..];
                }
                else
                {
                    throw new XdmException("FODC0006", "fn:parse-xml-fragment: invalid text declaration");
                }
            }
            try
            {
                using XmlReader reader = XmlReader.Create(new StringReader(text), settings, c.StaticBaseUri);
                return new XPathDocument(reader, XmlSpace.Preserve).CreateNavigator();
            }
            catch (XmlException e)
            {
                throw new XdmException("FODC0006", $"{(fragment ? "fn:parse-xml-fragment" : "fn:parse-xml")}: not well-formed: {e.Message}");
            }
        }
    }
}
