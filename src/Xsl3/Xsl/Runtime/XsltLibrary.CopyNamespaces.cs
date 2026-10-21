// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Runtime
{
    public sealed partial class XsltLibrary
    {
        /// <summary>
        /// xsl:copy-of copy-namespaces="no" (XSLT 3.0 §11.9.1): element and document nodes copied with only the
        /// namespaces their element and attribute names need; other items as they are.
        /// </summary>
        public IList<XPathItem> X31CopyWithoutNamespaces(IList<XPathItem> items)
        {
            var result = new XPathItem[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                result[i] = items[i] is XPathNavigator { NodeType: XPathNodeType.Element or XPathNodeType.Root } node ? CopyWithoutNamespaces(node) : items[i];
            }
            return result;
        }

        private static XPathNavigator CopyWithoutNamespaces(XPathNavigator node)
        {
            bool document = node.NodeType == XPathNodeType.Root;
            var (w, root) = Xsl3.Tree.CompactDocument.TemporaryTree(node.NameTable, node.BaseURI, fragment: !document, noDocumentUri: !document, compact: true);
            var scopes = new List<Dictionary<string, string>>();
            CopyWithoutNamespaces(w, node, scopes);
            w.Close();
            return root();
        }

        // The namespaces declared so far on the open elements of the copy, innermost last.
        private static void CopyWithoutNamespaces(XmlRawWriter w, XPathNavigator node, List<Dictionary<string, string>> scopes)
        {
            switch (node.NodeType)
            {
                case XPathNodeType.Element:
                    var declared = new Dictionary<string, string>();
                    w.WriteStartElement(node.Prefix, node.LocalName, node.NamespaceURI);
                    void Need(string prefix, string ns)
                    {
                        if (prefix == "xml" || declared.ContainsKey(prefix))
                        {
                            return;
                        }
                        for (int i = scopes.Count - 1; i >= 0; i--)
                        {
                            if (scopes[i].TryGetValue(prefix, out string? inScope))
                            {
                                if (inScope == ns)
                                {
                                    return;
                                }
                                break;
                            }
                        }
                        if (prefix.Length == 0 && ns.Length == 0 && !scopes.Exists(s => s.ContainsKey("")))
                        {
                            return; // no default namespace to undeclare
                        }
                        declared[prefix] = ns;
                        w.WriteNamespaceDeclaration(prefix, ns);
                    }
                    Need(node.Prefix, node.NamespaceURI);
                    XPathNavigator a = node.Clone();
                    if (a.MoveToFirstAttribute())
                    {
                        do
                        {
                            if (a.Prefix.Length != 0)
                            {
                                Need(a.Prefix, a.NamespaceURI);
                            }
                        }
                        while (a.MoveToNextAttribute());
                        a.MoveToParent();
                    }
                    if (a.MoveToFirstAttribute())
                    {
                        do
                        {
                            w.WriteStartAttribute(a.Prefix, a.LocalName, a.NamespaceURI);
                            w.WriteString(a.Value);
                            w.WriteEndAttribute();
                        }
                        while (a.MoveToNextAttribute());
                    }
                    w.StartElementContent();
                    scopes.Add(declared);
                    CopyChildrenWithoutNamespaces(w, node, scopes);
                    scopes.RemoveAt(scopes.Count - 1);
                    w.WriteFullEndElement(node.Prefix, node.LocalName, node.NamespaceURI);
                    break;
                case XPathNodeType.Root:
                    CopyChildrenWithoutNamespaces(w, node, scopes);
                    break;
                default:
                    CopyDeep(w, node);
                    break;
            }
        }

        private static void CopyChildrenWithoutNamespaces(XmlRawWriter w, XPathNavigator node, List<Dictionary<string, string>> scopes)
        {
            XPathNavigator c = node.Clone();
            if (c.MoveToFirstChild())
            {
                do
                {
                    CopyWithoutNamespaces(w, c, scopes);
                }
                while (c.MoveToNext());
            }
        }
    }
}
