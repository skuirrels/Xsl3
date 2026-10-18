// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Text;

namespace Xsl3.XPath31.Functions
{
    /// <summary>
    /// URI reference resolution exactly as RFC 3986 §5.2 specifies it, for fn:resolve-uri and XML Base.
    /// System.Uri also normalises (it unescapes %-encoded characters, lower-cases hosts, drops default ports),
    /// which these functions must not do.
    /// </summary>
    internal static class UriReference
    {
        private readonly record struct Parts(string? Scheme, string? Authority, string Path, string? Query, string? Fragment);

        /// <summary>True if <paramref name="s"/> has a scheme (RFC 3986 §3.1).</summary>
        public static bool IsAbsolute(string s) => Parse(s).Scheme != null;

        /// <summary>Resolves <paramref name="reference"/> against the absolute URI <paramref name="baseUri"/>.</summary>
        public static string Resolve(string reference, string baseUri)
        {
            Parts r = Parse(reference), b = Parse(baseUri);
            string? scheme, authority, query;
            string path;
            if (r.Scheme != null)
            {
                scheme = r.Scheme;
                authority = r.Authority;
                path = RemoveDotSegments(r.Path);
                query = r.Query;
            }
            else
            {
                scheme = b.Scheme;
                if (r.Authority != null)
                {
                    authority = r.Authority;
                    path = RemoveDotSegments(r.Path);
                    query = r.Query;
                }
                else
                {
                    authority = b.Authority;
                    if (r.Path.Length == 0)
                    {
                        path = b.Path;
                        query = r.Query ?? b.Query;
                    }
                    else
                    {
                        path = r.Path.StartsWith('/') ? RemoveDotSegments(r.Path) : RemoveDotSegments(Merge(b, r.Path));
                        query = r.Query;
                    }
                }
            }
            var sb = new StringBuilder();
            if (scheme != null) sb.Append(scheme).Append(':');
            if (authority != null) sb.Append("//").Append(authority);
            sb.Append(path);
            if (query != null) sb.Append('?').Append(query);
            if (r.Fragment != null) sb.Append('#').Append(r.Fragment);
            return sb.ToString();
        }

        // RFC 3986 Appendix B: ^(([^:/?#]+):)?(//([^/?#]*))?([^?#]*)(\?([^#]*))?(#(.*))?
        private static Parts Parse(string s)
        {
            int i = 0;
            string? scheme = null, authority = null, query = null, fragment = null;
            int colon = s.IndexOf(':');
            int delimiter = s.IndexOfAny(new[] { '/', '?', '#' });
            if (colon > 0 && (delimiter < 0 || colon < delimiter))
            {
                scheme = s[..colon];
                i = colon + 1;
            }
            if (string.CompareOrdinal(s, i, "//", 0, 2) == 0)
            {
                int end = s.IndexOfAny(new[] { '/', '?', '#' }, i + 2);
                if (end < 0) end = s.Length;
                authority = s[(i + 2)..end];
                i = end;
            }
            int pathEnd = s.IndexOfAny(new[] { '?', '#' }, i);
            if (pathEnd < 0) pathEnd = s.Length;
            string path = s[i..pathEnd];
            i = pathEnd;
            if (i < s.Length && s[i] == '?')
            {
                int end = s.IndexOf('#', i);
                if (end < 0) end = s.Length;
                query = s[(i + 1)..end];
                i = end;
            }
            if (i < s.Length && s[i] == '#')
            {
                fragment = s[(i + 1)..];
            }
            return new Parts(scheme, authority, path, query, fragment);
        }

        // §5.2.3
        private static string Merge(Parts b, string path)
        {
            if (b.Authority != null && b.Path.Length == 0)
            {
                return "/" + path;
            }
            int slash = b.Path.LastIndexOf('/');
            return slash < 0 ? path : b.Path[..(slash + 1)] + path;
        }

        // §5.2.4
        private static string RemoveDotSegments(string input)
        {
            var output = new StringBuilder();
            while (input.Length > 0)
            {
                if (input.StartsWith("../", System.StringComparison.Ordinal)) input = input[3..];
                else if (input.StartsWith("./", System.StringComparison.Ordinal)) input = input[2..];
                else if (input.StartsWith("/./", System.StringComparison.Ordinal)) input = input[2..];
                else if (input == "/.") input = "/";
                else if (input.StartsWith("/../", System.StringComparison.Ordinal) || input == "/..")
                {
                    input = input == "/.." ? "/" : input[3..];
                    string o = output.ToString();
                    int last = o.LastIndexOf('/');
                    output.Clear().Append(last < 0 ? "" : o[..last]);
                }
                else if (input is "." or "..") input = "";
                else
                {
                    int start = input.StartsWith('/') ? 1 : 0;
                    int next = input.IndexOf('/', start);
                    if (next < 0) next = input.Length;
                    output.Append(input, 0, next);
                    input = input[next..];
                }
            }
            return output.ToString();
        }
    }
}
