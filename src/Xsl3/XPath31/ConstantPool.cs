// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.Xdm;

namespace Xsl3.XPath31
{
    /// <summary>
    /// Objects that compiled code refers to by number: literal values, cast targets, sequence types.
    /// </summary>
    /// <remarks>
    /// Process-wide and append-only for now, which is simple and thread-safe but never frees entries. P4 moves
    /// constants into the compiled executable's own static data (SPEC §12.1) so they unload with it.
    /// </remarks>
    internal static class ConstantPool
    {
        private static readonly List<object> s_entries = new();
        private static readonly object s_lock = new();

        public static int Add(object value)
        {
            lock (s_lock)
            {
                s_entries.Add(value);
                return s_entries.Count - 1;
            }
        }

        public static object Get(int id)
        {
            lock (s_lock)
            {
                return s_entries[id];
            }
        }
    }

    /// <summary>The target of <c>cast as</c>/<c>castable as</c>, with the namespaces xs:QName casts resolve prefixes against.</summary>
    internal sealed record CastTarget(AtomicType Type, IXmlNamespaceResolver? Namespaces);
}
