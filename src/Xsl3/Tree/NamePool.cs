// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Xml;

namespace Xsl3.Tree
{
    /// <summary>
    /// The names of the trees built over one name table (SPEC §14.2): each (local name, namespace, prefix) has one id,
    /// and each expanded name (namespace and local name) one id, shared by every tree in the pool, so that a temporary
    /// tree doesn't build tables of its own and a name test compares integers. Ids are only ever added; the arrays
    /// are replaced when they grow, so a tree holding older arrays still reads its own names. Adds and lookups lock.
    /// </summary>
    internal sealed class NamePool
    {
        private static readonly ConditionalWeakTable<XmlNameTable, NamePool> s_pools = new();

        private readonly object _lock = new();
        private readonly XmlNameTable _nameTable;
        private readonly Dictionary<string, List<(string Uri, string Prefix, int Id)>> _ids = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, List<(string Uri, int Expanded)>> _expandedIds = new(StringComparer.Ordinal);
        private int _expandedCount;

        internal string[] Local = new string[64];
        internal string[] Uri = new string[64];
        internal string[] Prefix = new string[64];
        internal int[] Expanded = new int[64];
        internal int Count;

        internal readonly string XmlPrefix, XmlUri;

        private NamePool(XmlNameTable nameTable)
        {
            _nameTable = nameTable;
            XmlPrefix = nameTable.Add("xml");
            XmlUri = nameTable.Add(XmlReservedNs.NsXml);
        }

        /// <summary>The pool of the trees built over a name table.</summary>
        public static NamePool For(XmlNameTable nameTable) => s_pools.GetValue(nameTable, table => new NamePool(table));

        /// <summary>The id of a name, added if new. The strings are atomized in the pool's name table.</summary>
        public int Name(string prefix, string local, string ns)
        {
            lock (_lock)
            {
                if (!_ids.TryGetValue(local, out var list))
                {
                    local = _nameTable.Add(local);
                    if (!_ids.TryGetValue(local, out list))
                    {
                        _ids[local] = list = new List<(string, string, int)>(1);
                    }
                }
                foreach (var (uri, p, id) in list)
                {
                    if (((object)uri == (object)ns || uri == ns) && ((object)p == (object)prefix || p == prefix))
                    {
                        return id;
                    }
                }
                ns = _nameTable.Add(ns);
                prefix = _nameTable.Add(prefix);
                int newId = Count;
                if (newId == Local.Length)
                {
                    // New arrays, so that trees holding the old ones are undisturbed.
                    var local2 = new string[newId * 2];
                    var uri2 = new string[newId * 2];
                    var prefix2 = new string[newId * 2];
                    var expanded2 = new int[newId * 2];
                    Array.Copy(Local, local2, newId);
                    Array.Copy(Uri, uri2, newId);
                    Array.Copy(Prefix, prefix2, newId);
                    Array.Copy(Expanded, expanded2, newId);
                    Local = local2;
                    Uri = uri2;
                    Prefix = prefix2;
                    Expanded = expanded2;
                }
                Local[newId] = local;
                Uri[newId] = ns;
                Prefix[newId] = prefix;
                Expanded[newId] = ExpandedLocked(local, ns, add: true);
                Count = newId + 1;
                list.Add((ns, prefix, newId));
                return newId;
            }
        }

        /// <summary>The expanded-name id of a local name and namespace, or CompactDocument.Absent.</summary>
        public int ExpandedId(string localName, string namespaceUri)
        {
            lock (_lock)
            {
                return ExpandedLocked(localName, namespaceUri, add: false);
            }
        }

        private int ExpandedLocked(string localName, string namespaceUri, bool add)
        {
            if (!_expandedIds.TryGetValue(localName, out var list))
            {
                if (!add)
                {
                    return CompactDocument.Absent;
                }
                _expandedIds[localName] = list = new List<(string, int)>(1);
            }
            foreach (var (uri, id) in list)
            {
                if (uri == namespaceUri)
                {
                    return id;
                }
            }
            if (!add)
            {
                return CompactDocument.Absent;
            }
            int expanded = _expandedCount++;
            list.Add((namespaceUri, expanded));
            return expanded;
        }
    }
}
