// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Runtime
{
    public sealed partial class XsltLibrary
    {
        /// <summary>
        /// fn:snapshot (XSLT 3.0 §20.3.2): each node is copied with its subtree, its ancestors (shallow, with their
        /// attributes and namespaces) and, for an attribute or a namespace node, its parent's other attributes.
        /// </summary>
        public IList<XPathItem> X31Snapshot(IList<XPathItem> items)
        {
            var result = new List<XPathItem>(items.Count);
            foreach (XPathItem item in items)
            {
                if (item is XPathNavigator n)
                {
                    // The snapshot keeps the accumulator values of the node and its descendants.
                    XPathNavigator copy = Snapshot(n);
                    XPathNavigator top = copy.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace ? null! : copy;
                    if (top != null)
                    {
                        XPathNavigator root = copy.Clone();
                        root.MoveToRoot();
                        (_accumulatorCopies ??= new())[_runtime.GenerateId(root)] = (copy.Clone(), n.Clone());
                    }
                    result.Add(copy);
                }
                else
                {
                    result.Add(item);
                }
            }
            return result;
        }

        private static XPathNavigator Snapshot(XPathNavigator node)
        {
            // The chain from the root down to the element that holds the node (or the node itself).
            var chain = new List<XPathNavigator>();
            XPathNavigator top = node.Clone();
            if (node.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace)
            {
                if (!top.MoveToParent())
                {
                    return CopyParentless(node);
                }
            }
            XPathNavigator target = top.Clone();
            do
            {
                chain.Add(top.Clone());
            }
            while (top.MoveToParent());
            chain.Reverse();
            bool rooted = chain[0].NodeType == XPathNodeType.Root;
            var doc = new InternalXPathDocument(node.NameTable) { NoDocumentUri = !rooted };
            XmlRawWriter w = doc.LoadFromWriter(rooted ? InternalXPathDocument.LoadFlags.None : InternalXPathDocument.LoadFlags.Fragment, target.BaseURI);
            int start = rooted ? 1 : 0;
            for (int i = start; i < chain.Count - 1; i++)
            {
                WriteStartShallow(w, chain[i]);
            }
            if (chain.Count - 1 >= start)
            {
                CopyDeep(w, chain[^1]);
            }
            else
            {
                // The document node itself.
                CopyChildren(w, chain[0]);
            }
            for (int i = chain.Count - 2; i >= start; i--)
            {
                w.WriteFullEndElement(chain[i].Prefix, chain[i].LocalName, chain[i].NamespaceURI);
            }
            w.Close();

            // Each copied ancestor has the next one as its only child (a fragment's root is its top element).
            XPathNavigator nav = doc.CreateNavigator();
            for (int i = 1; i < chain.Count; i++)
            {
                nav.MoveToFirstChild();
            }
            switch (node.NodeType)
            {
                case XPathNodeType.Attribute:
                    // By walking the attributes: MoveToAttribute compares names atomized in the copy's name table.
                    if (nav.MoveToFirstAttribute())
                    {
                        while (!(nav.LocalName == node.LocalName && nav.NamespaceURI == node.NamespaceURI) && nav.MoveToNextAttribute())
                        {
                        }
                    }
                    break;
                case XPathNodeType.Namespace:
                    nav.MoveToNamespace(node.LocalName);
                    break;
            }
            return nav;
        }

        // A deep copy of a node, in a new tree (a parentless node, or a document for a document node).
        private static XPathNavigator CopyTree(XPathNavigator node)
        {
            if (node.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace)
            {
                return CopyParentless(node);
            }
            bool document = node.NodeType == XPathNodeType.Root;
            var doc = new InternalXPathDocument(node.NameTable) { NoDocumentUri = !document };
            XmlRawWriter w = doc.LoadFromWriter(document ? InternalXPathDocument.LoadFlags.None : InternalXPathDocument.LoadFlags.Fragment, node.BaseURI);
            if (node.NodeType == XPathNodeType.Element)
            {
                // The copy keeps the element's in-scope namespaces, inherited ones too.
                WriteStartShallow(w, node, XPathNamespaceScope.ExcludeXml);
                CopyChildren(w, node);
                w.WriteFullEndElement(node.Prefix, node.LocalName, node.NamespaceURI);
            }
            else
            {
                CopyDeep(w, node);
            }
            w.Close();
            return doc.CreateNavigator(); // a fragment's root is its top node
        }

        private static XPathNavigator CopyParentless(XPathNavigator node)
        {
            var doc = new InternalXPathDocument(node.NameTable) { NoDocumentUri = true };
            XmlRawWriter w = doc.LoadFromWriter(InternalXPathDocument.LoadFlags.Fragment, node.BaseURI);
            if (node.NodeType == XPathNodeType.Attribute)
            {
                w.WriteStartAttribute(node.Prefix, node.LocalName, node.NamespaceURI);
                w.WriteString(node.Value);
                w.WriteEndAttribute();
            }
            else
            {
                w.WriteNamespaceDeclaration(node.LocalName, node.Value);
            }
            w.Close();
            XPathNavigator nav = doc.CreateNavigator();
            if (node.NodeType == XPathNodeType.Attribute)
            {
                nav.MoveToFirstAttribute();
            }
            else
            {
                nav.MoveToFirstNamespace(XPathNamespaceScope.Local);
            }
            return nav;
        }

        private static void WriteStartShallow(XmlRawWriter w, XPathNavigator element, XPathNamespaceScope scope = XPathNamespaceScope.Local)
        {
            w.WriteStartElement(element.Prefix, element.LocalName, element.NamespaceURI);
            XPathNavigator n = element.Clone();
            if (n.MoveToFirstNamespace(scope))
            {
                do
                {
                    if (!(n.LocalName == element.Prefix && n.Value == element.NamespaceURI))
                    {
                        w.WriteNamespaceDeclaration(n.LocalName, n.Value);
                    }
                }
                while (n.MoveToNextNamespace(scope));
                n.MoveToParent();
            }
            if (n.MoveToFirstAttribute())
            {
                do
                {
                    w.WriteStartAttribute(n.Prefix, n.LocalName, n.NamespaceURI);
                    w.WriteString(n.Value);
                    w.WriteEndAttribute();
                }
                while (n.MoveToNextAttribute());
            }
            w.StartElementContent();
        }

        private static void CopyDeep(XmlRawWriter w, XPathNavigator node)
        {
            switch (node.NodeType)
            {
                case XPathNodeType.Element:
                    WriteStartShallow(w, node);
                    CopyChildren(w, node);
                    w.WriteFullEndElement(node.Prefix, node.LocalName, node.NamespaceURI);
                    break;
                case XPathNodeType.Text:
                case XPathNodeType.SignificantWhitespace:
                case XPathNodeType.Whitespace:
                    w.WriteString(node.Value);
                    break;
                case XPathNodeType.Comment:
                    w.WriteComment(node.Value);
                    break;
                case XPathNodeType.ProcessingInstruction:
                    w.WriteProcessingInstruction(node.LocalName, node.Value);
                    break;
                case XPathNodeType.Root:
                    CopyChildren(w, node);
                    break;
            }
        }

        private static void CopyChildren(XmlRawWriter w, XPathNavigator node)
        {
            XPathNavigator c = node.Clone();
            if (c.MoveToFirstChild())
            {
                do
                {
                    CopyDeep(w, c);
                }
                while (c.MoveToNext());
            }
        }
    }
}
