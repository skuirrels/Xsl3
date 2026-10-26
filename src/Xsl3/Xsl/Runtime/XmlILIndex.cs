// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Xml.XPath;

namespace Xsl3.Runtime
{
    /// <summary>
    /// This class manages nodes from one input document, indexed by key value(s).
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class XmlILIndex
    {
        private readonly Dictionary<string, XmlQueryNodeSequence> _table;
        private bool _shared;
        internal void Share() => _shared = true;

        /// <summary>
        /// This constructor is internal so that external users cannot construct it (and therefore we do not have to test it separately).
        /// </summary>
        internal XmlILIndex()
        {
            _table = new Dictionary<string, XmlQueryNodeSequence>();
        }

        /// <summary>
        /// Add a node indexed by the specified key value.
        /// </summary>
        public void Add(string key, XPathNavigator navigator)
        {
            if (_shared) throw new System.InvalidOperationException("An index cannot be changed after it is shared");
            // Xsl3 (P4): one probe of the table for both the lookup and the insertion
            ref XmlQueryNodeSequence? seq = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_table, key, out bool exists);

            if (!exists)
            {
                // Create a new sequence and add it to the index
                seq = new XmlQueryNodeSequence();
                seq.AddClone(navigator);
            }
            else
            {
                // The nodes are guaranteed to be added in document order with possible duplicates.
                // Add node to the existing sequence if it differs from the last one.
                Debug.Assert(navigator.ComparePosition(seq![seq.Count - 1]) >= 0, "Index nodes must be added in document order");
                if (!navigator.IsSamePosition(seq![seq.Count - 1]))
                {
                    seq.AddClone(navigator);
                }
            }
        }

        /// <summary>
        /// Lookup a sequence of nodes that are indexed by the specified key value.
        /// Return a non-null empty sequence, if there are no nodes associated with the key.
        /// </summary>
        public XmlQueryNodeSequence Lookup(string key)
        {
            XmlQueryNodeSequence? seq;

            if (!_table.TryGetValue(key, out seq))
                seq = new XmlQueryNodeSequence();
            // Each prong owns sequence reuse/sorting state and navigator positions. The index
            // itself is immutable once published to workers.
            return _shared ? new XmlQueryNodeSequence(seq) { IsDocOrderDistinct = true } : seq;
        }
    }
}
