// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Threading;

namespace Xsl3.Tree;

// Native source and constructed trees share one order, independent of their
// representation. Prong views use their source document's creation number.
internal static class DocumentCreationOrder
{
    private static long s_next;
    internal static long Next() => Interlocked.Increment(ref s_next);
}
