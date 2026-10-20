// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace Xsl3.Runtime
{
    public sealed partial class XmlCollation
    {
        /// <summary>
        /// Create a sort key that can be compared quickly with other keys.
        /// </summary>
        /// <remarks>
        /// Upstream compiles XmlCollation.Windows.cs or XmlCollation.Unix.cs depending on the target OS.
        /// Xsl3 ships one portable assembly, so the choice is made at run time instead.
        /// </remarks>
        internal XmlSortKey CreateSortKey(string s) => CreateSortKey(CreateSortKeyData(s));

        /// <summary>Xsl3: a sort key for the data CreateSortKeyData made (which a sort may reuse for equal strings).</summary>
        internal XmlSortKey CreateSortKey(object data) =>
            data is SortKey sortKey ? new XmlStringSortKey(sortKey, DescendingOrder) : new XmlStringSortKey((byte[])data, DescendingOrder);

        /// <summary>Xsl3: the collation key of a string: a SortKey, or its bytes with the case weights inverted (upper-first).</summary>
        internal object CreateSortKeyData(string s)
        {
            SortKey sortKey;
            byte[] bytesKey;
            int idx;

            if (UpperFirst && !OperatingSystem.IsWindows())
            {
                // TODO: Support upper case first on Linux
                // https://github.com/dotnet/runtime/issues/19462
                // https://github.com/dotnet/runtime/issues/19170
                throw new PlatformNotSupportedException(SR.Xslt_UpperCaseFirstNotSupported);
            }

            sortKey = Culture.CompareInfo.GetSortKey(s, _compops);

            // Create an XmlStringSortKey using the SortKey if possible
#if DEBUG
            // In debug-only code, test other code path more frequently
            if (!UpperFirst && DescendingOrder)
                return sortKey;
#else
            if (!UpperFirst)
                return sortKey;
#endif

            // Get byte buffer from SortKey and modify it
            bytesKey = sortKey.KeyData;
            if (UpperFirst && bytesKey.Length != 0)
            {
                // By default lower-case is always sorted first for any locale (verified by empirical testing).
                // In order to place upper-case first, invert the case weights in the generated sort key.
                // Skip to case weight section (3rd weight section)
                idx = 0;
                while (bytesKey[idx] != 1)
                    idx++;

                do
                {
                    idx++;
                }
                while (bytesKey[idx] != 1);

                // Invert all case weights (including terminating 0x1)
                do
                {
                    idx++;
                    bytesKey[idx] ^= 0xff;
                }
                while (bytesKey[idx] != 0xfe);
            }

            return bytesKey;
        }
    }
}
