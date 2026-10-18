// Licensed under the MIT license. See LICENSE.TXT in the repository root.

namespace Xsl3.XPath31.Functions
{
    /// <summary>The substring functions under a collation (F&amp;O 3.1 §5.5).</summary>
    internal static class StringMatch
    {
        public static bool Contains(string s, string t, Collation c) => t.Length == 0 || c.IndexOf(s, t) >= 0;

        public static bool StartsWith(string s, string t, Collation c) => t.Length == 0 || c.StartsWith(s, t);

        public static bool EndsWith(string s, string t, Collation c) => t.Length == 0 || c.EndsWith(s, t);

        public static string Before(string s, string t, Collation c)
        {
            if (t.Length == 0)
            {
                return "";
            }
            int i = c.IndexOf(s, t);
            return i < 0 ? "" : s[..i];
        }

        public static string After(string s, string t, Collation c)
        {
            if (t.Length == 0)
            {
                return s;
            }
            int i = c.IndexOf(s, t, out int length);
            return i < 0 ? "" : s[(i + length)..];
        }
    }
}
