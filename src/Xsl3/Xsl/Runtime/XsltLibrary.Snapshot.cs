// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Runtime
{
    public sealed partial class XsltLibrary
    {
        /// <summary>fn:snapshot without schema-awareness.</summary>
        /// <param name="items">The items to snapshot.</param>
        // Xsl3: the signature 1.0.0-rc.1 shipped, kept so that assemblies Xsl3.Build saved with it still bind;
        // the compiler calls the two-argument form.
        [Obsolete("Compiled stylesheets call X31Snapshot(items, schemaAware).")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public IList<XPathItem> X31Snapshot(IList<XPathItem> items) => X31Snapshot(items, false);

        /// <summary>
        /// fn:snapshot (XSLT 3.0 §20.3.2): each node is copied with its subtree, its ancestors (shallow, with their
        /// attributes and namespaces) and, for an attribute or a namespace node, its parent's other attributes.
        /// </summary>
        /// <param name="items">The items to snapshot.</param>
        /// <param name="schemaAware">A typed document's snapshot keeps its type annotations (P6).</param>
        public IList<XPathItem> X31Snapshot(IList<XPathItem> items, bool schemaAware)
        {
            var result = new List<XPathItem>(items.Count);
            foreach (XPathItem item in items)
            {
                if (schemaAware && item is Xsl3.Tree.CompactNavigator { Typed: true, NodeType: XPathNodeType.Root } typedDocument)
                {
                    // Xsl3 (P6): a typed document's snapshot keeps its type annotations (its copy is made with
                    // validation="preserve"), and its accumulator values.
                    result.AddRange(X31CopyPreserving(new XPathItem[] { typedDocument }, true));
                    continue;
                }
                if (item is XPathNavigator n)
                {
                    // The snapshot keeps the accumulator values of the node and its descendants.
                    XPathNavigator copy = Snapshot(n, schemaAware);
                    XPathNavigator top = copy.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace ? null! : copy;
                    if (top != null)
                    {
                        _runtime.Documents.RecordCopy(copy, n);
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

        private static XPathNavigator Snapshot(XPathNavigator node, bool schemaAware)
        {
            using var consumption = node is Streaming.StreamedNavigator streamed && node.NodeType is XPathNodeType.Element or XPathNodeType.Root
                ? streamed.BeginConsumption(replay: true, accumulators: true) : default;
            // The chain from the root down to the element that holds the node (or the node itself).
            var chain = new List<XPathNavigator>();
            XPathNavigator top = node.Clone();
            if (node.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace)
            {
                if (!top.MoveToParent())
                {
                    return schemaAware && node.NodeType == XPathNodeType.Attribute
                        ? Schema.NodeValidator.Preserve(node, copyNamespaces: true) : CopyParentless(node);
                }
            }
            do
            {
                chain.Add(top.Clone());
            }
            while (top.MoveToParent());
            chain.Reverse();
            bool rooted = chain[0].NodeType == XPathNodeType.Root;
            int start = rooted ? 1 : 0;
            // Each copied ancestor has the next one as its only child (a fragment's root is its top element).
            bool shallow = node.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace;
            XPathNavigator nav = schemaAware ? TypedSnapshotRoot(chain, rooted, start, shallow) : SnapshotRoot(chain, rooted, start, shallow);
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

        private static XPathNavigator SnapshotRoot(List<XPathNavigator> chain, bool rooted, int start, bool shallow)
        {
            XPathNavigator target = chain[^1];
            var (w, root) = Tree.CompactDocument.TemporaryTree(target.NameTable, chain[0].BaseURI, fragment: !rooted, noDocumentUri: !rooted, compact: true);
            for (int i = start; i < chain.Count - 1; i++) WriteStartShallow(w, chain[i]);
            if (shallow)
            {
                WriteStartShallow(w, target);
                w.WriteFullEndElement(target.Prefix, target.LocalName, target.NamespaceURI);
            }
            else if (chain.Count - 1 >= start) CopyDeep(w, target);
            else CopyChildren(w, chain[0]);
            for (int i = chain.Count - 2; i >= start; i--) w.WriteFullEndElement(chain[i].Prefix, chain[i].LocalName, chain[i].NamespaceURI);
            w.Close();
            return root();
        }

        private static XPathNavigator TypedSnapshotRoot(List<XPathNavigator> chain, bool rooted, int start, bool shallow)
        {
            XPathNavigator target = chain[^1];
            var builder = new Tree.CompactDocument.Builder(target.NameTable, chain[0].BaseURI, fragment: !rooted, small: true);
            for (int i = start; i < chain.Count - 1; i++)
                Schema.NodeValidator.CopyElementStart(builder, chain[i].Clone(), copyNamespaces: true, top: i == start, keepTypes: true, snapshotAncestor: true);
            if (shallow)
            {
                Schema.NodeValidator.CopyElementStart(builder, target.Clone(), copyNamespaces: true, top: start == chain.Count - 1, keepTypes: true, snapshotAncestor: true);
                builder.EndElement(emptyTag: true);
            }
            else if (chain.Count - 1 >= start)
                Schema.NodeValidator.CopyChild(builder, target, copyNamespaces: true, keepTypes: true);
            else
            {
                XPathNavigator child = target.Clone();
                if (child.MoveToFirstChild())
                    do { Schema.NodeValidator.CopyChild(builder, child, copyNamespaces: true, keepTypes: true); } while (child.MoveToNext());
            }
            for (int i = chain.Count - 2; i >= start; i--) builder.EndElement(emptyTag: false);
            Tree.CompactDocument tree = builder.Finish();
            tree._constructed = true;
            tree.NoDocumentUri = !rooted;
            return tree.CreateNavigator();
        }

        private static XPathNavigator CopyParentless(XPathNavigator node)
        {
            var (w, root) = Xsl3.Tree.CompactDocument.TemporaryTree(node.NameTable, node.BaseURI, fragment: true, noDocumentUri: true, compact: true);
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
            XPathNavigator nav = root();
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
                    // The element's own namespace too: the tree writer adds no namespace nodes of its own.
                    w.WriteNamespaceDeclaration(n.LocalName, n.Value);
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
