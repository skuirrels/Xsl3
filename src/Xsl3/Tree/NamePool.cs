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
    /// are replaced when they grow, so a tree holding older arrays still reads its own names. Adds and lookups lock,
    /// behind a per-thread cache of the names last looked up.
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

        // Xsl3: the names this thread looked up last, by the strings' identity: ids never change once given, so a hit
        // needs no lock (reading and building trees asks for the same few names over and over).
        private struct CacheEntry
        {
            public NamePool? Pool;
            public string? Prefix, Local, Ns;
            public int Id;
        }

        private const int CacheMask = 255;

        [ThreadStatic]
        private static CacheEntry[]? t_names;

        [ThreadStatic]
        private static CacheEntry[]? t_expanded;

        private static int Slot(string? a, string b, string c) =>
            (RuntimeHelpers.GetHashCode(b) * 31 + RuntimeHelpers.GetHashCode(c) * 17 + (a == null ? 0 : RuntimeHelpers.GetHashCode(a))) & CacheMask;

        /// <summary>The id of a name, added if new. The strings are atomized in the pool's name table.</summary>
        public int Name(string prefix, string local, string ns)
        {
            CacheEntry[] cache = t_names ??= new CacheEntry[CacheMask + 1];
            ref CacheEntry hit = ref cache[Slot(prefix, local, ns)];
            if (hit.Pool == this && (object?)hit.Local == local && (object?)hit.Ns == ns && (object?)hit.Prefix == prefix)
            {
                return hit.Id;
            }
            int id = NameLocked(prefix, local, ns);
            hit = new CacheEntry { Pool = this, Prefix = prefix, Local = local, Ns = ns, Id = id };
            return id;
        }

        private int NameLocked(string prefix, string local, string ns)
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
            CacheEntry[] cache = t_expanded ??= new CacheEntry[CacheMask + 1];
            ref CacheEntry hit = ref cache[Slot(null, localName, namespaceUri)];
            if (hit.Pool == this && (object?)hit.Local == localName && (object?)hit.Ns == namespaceUri)
            {
                return hit.Id;
            }
            int id;
            lock (_lock)
            {
                id = ExpandedLocked(localName, namespaceUri, add: false);
            }
            if (id != CompactDocument.Absent)
            {
                // (Only names that exist: an absent one may be added later.)
                hit = new CacheEntry { Pool = this, Local = localName, Ns = namespaceUri, Id = id };
            }
            return id;
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
