// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Runtime
{
    /// <summary>
    /// IComparer implementation that orders navigators based on ComparePosition.  When ComparePosition returns
    /// XmlNodeOrder.Unknown, a stable order between documents is maintained by an ordered list mapping each root node
    /// to an ordering index.
    /// </summary>
    internal sealed class DocumentOrderComparer : IComparer<XPathNavigator>
    {
        private List<(XPathNavigator Root, int Index)>? _roots;     // foreign trees, by root
        private ConditionalWeakTable<object, DocumentIndex>? _documents; // live Xsl3 trees, by document
        private int _count;
        private readonly object _gate = new();
        internal bool IsUnused { get { lock (_gate) return _count == 0; } }
        private sealed record DocumentIndex(int Value);

        /// <summary>
        /// Return:
        ///     -1 if navThis is positioned before navThat
        ///      0 if navThis has the same position as navThat
        ///      1 if navThis is positioned after navThat
        /// </summary>
        public int Compare(XPathNavigator? navThis, XPathNavigator? navThat)
        {
            switch (navThis!.ComparePosition(navThat))
            {
                case XmlNodeOrder.Before: return -1;
                case XmlNodeOrder.Same: return 0;
                case XmlNodeOrder.After: return 1;
            }

            // Representation must not change relative document order. A streamed source
            // and every replay/prong view use the same creation number.
            static long? Created(XPathNavigator? node) => node switch {
                Xsl3.Tree.CompactNavigator compact => compact.Document.Created,
                Xsl3.Streaming.StreamedNavigator streamed => streamed.Document.Created,
                _ => null,
            };
            if (Created(navThis) is { } createdThis && Created(navThat) is { } createdThat)
            {
                return createdThis < createdThat ? -1 : 1;
            }
            if (navThis is Xsl3.Tree.CompactNavigator != navThat is Xsl3.Tree.CompactNavigator)
            {
                return navThis is Xsl3.Tree.CompactNavigator ? -1 : 1;
            }
            int left = GetDocumentIndex(navThis), right = GetDocumentIndex(navThat!);
            Debug.Assert(left != right);
            return left < right ? -1 : 1;
        }

        /// <summary>
        /// Map navigator's document to a unique index.
        /// When consecutive calls are made to GetIndexOfNavigator for navThis and navThat, it is not possible
        /// for them to return the same index.  navThis compared to navThat is always XmlNodeOrder.Unknown.
        /// Therefore, no matter where navThis is inserted in the list, navThat will never be inserted just
        /// before navThis, and therefore will never have the same index.
        /// </summary>
        public int GetDocumentIndex(XPathNavigator nav)
        {
            lock (_gate) return GetDocumentIndexCore(nav);
        }

        internal static object? NativeDocumentIdentity(XPathNavigator nav) => nav switch
        {
            Xsl3.Tree.CompactNavigator compact => compact.Document,
            MS.Internal.Xml.Cache.XPathDocumentNavigator vendored => vendored.OwnerDocument,
            Xsl3.Streaming.StreamedNavigator streamed => streamed.Document.Identity,
            _ => null,
        };

        private int GetDocumentIndexCore(XPathNavigator nav)
        {
            // Xsl3 (P4): Xsl3's own trees are known by their document object, so that finding the index is a hash
            // lookup, not a scan of every root seen (which made sorting nodes from many temporary trees quadratic).
            object? document = NativeDocumentIdentity(nav);
            if (document != null)
            {
                _documents ??= new ConditionalWeakTable<object, DocumentIndex>();
                if (!_documents.TryGetValue(document, out DocumentIndex? index))
                {
                    _documents.Add(document, index = new DocumentIndex(_count++));
                }
                return index.Value;
            }

            XPathNavigator navRoot;

            // Use this.roots to impose stable ordering
            _roots ??= new List<(XPathNavigator, int)>();

            // Position navigator to root
            navRoot = nav.Clone();
            navRoot.MoveToRoot();

            foreach (var (root, index) in _roots)
            {
                if (navRoot.IsSamePosition(root))
                {
                    // navigator's document was previously mapped to a unique index
                    return index;
                }
            }

            // Add navigator to this.roots mapping
            _roots.Add((navRoot, _count));

            return _count++;
        }
    }
}
