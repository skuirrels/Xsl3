// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Xsl3.Xslt
{
    /// <summary>
    /// Xsl3: package versions and the version ranges of xsl:use-package (XSLT 3.0 §3.5.1, §3.5.2): a version is
    /// integers separated by dots, optionally with a "-name" suffix; a range is "*", a version, "V+" (V or later),
    /// "V.*" (versions starting with V), "V1 to V2" or "to V2" (V2 may end in ".*"), or a comma-separated list.
    /// Of several versions in range, the highest is used.
    /// </summary>
    internal static class PackageVersions
    {
        /// <summary>The location of the highest version of the named package that the range allows, if any.</summary>
        public static Uri? Find(IReadOnlyList<(string Name, string Version, Uri Location)>? packages, string name, string range)
        {
            if (packages == null)
            {
                return null;
            }
            return packages.Where(p => p.Name == name && Matches(p.Version, range))
                .OrderByDescending(p => p.Version, Comparer<string>.Create(Compare))
                .Select(p => p.Location)
                .FirstOrDefault();
        }

        public static bool Matches(string version, string range)
        {
            range = range.Trim();
            if (range is "*" or "")
            {
                return true;
            }
            if (range.Contains(','))
            {
                // A list of ranges: any of them.
                return range.Split(',').Any(r => r.Trim().Length > 0 && Matches(version, r));
            }
            if (range.StartsWith("to ", StringComparison.Ordinal))
            {
                range = " " + range; // no lower bound
            }
            int to = range.IndexOf(" to ", StringComparison.Ordinal);
            if (to >= 0)
            {
                string low = range[..to].Trim(), high = range[(to + 4)..].Trim();
                return (low.Length == 0 || Compare(version, low) >= 0) &&
                    (high.EndsWith(".*", StringComparison.Ordinal) ? StartsWith(version, high[..^2]) || Compare(version, high[..^2]) <= 0 : Compare(version, high) <= 0);
            }
            if (range.EndsWith('+'))
            {
                return Compare(version, range[..^1]) >= 0;
            }
            if (range.EndsWith(".*", StringComparison.Ordinal))
            {
                return StartsWith(version, range[..^2]);
            }
            return Compare(version, range) == 0;
        }

        // Whether the version's numbers start with the prefix's.
        private static bool StartsWith(string version, string prefix)
        {
            var (v, _) = Parse(version);
            var (p, _) = Parse(prefix);
            return v.Length >= p.Length && p.Select((n, i) => v[i] == n).All(b => b);
        }

        // Numbers compared numerically, missing ones as 0; a version with a suffix comes before the one without.
        public static int Compare(string x, string y)
        {
            var (a, suffixA) = Parse(x);
            var (b, suffixB) = Parse(y);
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                long m = i < a.Length ? a[i] : 0, n = i < b.Length ? b[i] : 0;
                if (m != n)
                {
                    return m.CompareTo(n);
                }
            }
            if (suffixA == suffixB)
            {
                return 0;
            }
            return suffixA == null ? 1 : suffixB == null ? -1 : string.CompareOrdinal(suffixA, suffixB);
        }

        private static (long[] Numbers, string? Suffix) Parse(string version)
        {
            version = version.Trim();
            int dash = version.IndexOf('-');
            string numbers = dash < 0 ? version : version[..dash];
            string? suffix = dash < 0 ? null : version[(dash + 1)..];
            long[] parts = numbers.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(p => long.TryParse(p, out long n) ? n : 0).ToArray();
            return (parts, suffix);
        }
    }
}
