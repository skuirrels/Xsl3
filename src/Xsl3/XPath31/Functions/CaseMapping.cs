// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Text;

namespace Xsl3.XPath31.Functions
{
    /// <summary>
    /// fn:upper-case and fn:lower-case (F&amp;O 3.1 §5.4.7–8): Unicode's default full case mappings, without
    /// language tailoring or context (so ß becomes SS and İ becomes i̇). The CLR's invariant casing is a
    /// simple mapping with exceptions (it leaves µ and ß alone), hence the generated tables.
    /// </summary>
    internal static partial class CaseMapping
    {
        public static string ToUpper(string s) =>
            System.Text.Ascii.IsValid(s) ? AsciiMap(s, upper: true) : Map(s, s_upperKeys, s_upperValues);

        public static string ToLower(string s) =>
            System.Text.Ascii.IsValid(s) ? AsciiMap(s, upper: false) : Map(s, s_lowerKeys, s_lowerValues);

        /// <summary>The table mappings, which the ASCII path must agree with (for the tests).</summary>
        internal static string ToUpperByTable(string s) => Map(s, s_upperKeys, s_upperValues);

        internal static string ToLowerByTable(string s) => Map(s, s_lowerKeys, s_lowerValues);

        // Xsl3 (P4): Unicode's case mappings of ASCII characters are ASCII's own (a-z and A-Z, one to one), so an
        // ASCII string maps with no table look-ups; unchanged, it is returned as it is.
        private static string AsciiMap(string s, bool upper)
        {
            if ((upper ? s.AsSpan().IndexOfAnyInRange('a', 'z') : s.AsSpan().IndexOfAnyInRange('A', 'Z')) < 0)
            {
                return s;
            }
            return string.Create(s.Length, (s, upper), static (span, state) =>
            {
                if (state.upper) System.Text.Ascii.ToUpper(state.s, span, out _);
                else System.Text.Ascii.ToLower(state.s, span, out _);
            });
        }

        private static string Map(string s, int[] keys, string[] values)
        {
            StringBuilder? sb = null;
            int i = 0;
            while (i < s.Length)
            {
                int cp = char.IsSurrogatePair(s, i) ? char.ConvertToUtf32(s[i], s[i + 1]) : s[i];
                int width = cp > 0xFFFF ? 2 : 1;
                int k = System.Array.BinarySearch(keys, cp);
                if (k >= 0)
                {
                    sb ??= new StringBuilder(s.Length + 8).Append(s, 0, i);
                    sb.Append(values[k]);
                }
                else
                {
                    sb?.Append(s, i, width);
                }
                i += width;
            }
            return sb?.ToString() ?? s;
        }
    }
}
