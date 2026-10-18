// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    /// <summary>
    /// A collation (F&amp;O 3.1 §5.3, SPEC §13.2): string ordering plus, where the collation supports it,
    /// matching of substrings for fn:contains and friends.
    /// </summary>
    internal sealed class Collation : IComparer<string>
    {
        public const string CodepointUri = "http://www.w3.org/2005/xpath-functions/collation/codepoint";
        public const string HtmlAsciiCaseInsensitiveUri = "http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive";
        public const string UcaPrefix = "http://www.w3.org/2013/collation/UCA";

        public static readonly Collation Codepoint = new(CodepointUri, Comparison.CodepointCollation, null, CompareOptions.Ordinal);

        private static readonly ConcurrentDictionary<string, Collation> s_cache = new();

        private readonly IComparer<string> _comparer;
        private readonly CompareInfo? _compareInfo;
        private readonly CompareOptions _options;

        private Collation(string uri, IComparer<string> comparer, CompareInfo? compareInfo, CompareOptions options)
        {
            Uri = uri;
            _comparer = comparer;
            _compareInfo = compareInfo;
            _options = options;
        }

        public string Uri { get; }

        public int Compare(string? x, string? y) => _comparer.Compare(x, y);

        // The HTML ASCII case-insensitive collation folds A-Z only; ordinal matching on folded strings.
        private bool AsciiFold => Uri == HtmlAsciiCaseInsensitiveUri;

        private static string Fold(string s)
        {
            if (!s.Any(c => c is >= 'A' and <= 'Z'))
            {
                return s;
            }
            return string.Create(s.Length, s, (span, src) =>
            {
                for (int i = 0; i < src.Length; i++)
                {
                    span[i] = src[i] is >= 'A' and <= 'Z' ? (char)(src[i] + 32) : src[i];
                }
            });
        }

        public int IndexOf(string s, string t) =>
            AsciiFold ? Fold(s).IndexOf(Fold(t), System.StringComparison.Ordinal)
            : _compareInfo == null ? s.IndexOf(t, System.StringComparison.Ordinal)
            : _compareInfo.IndexOf(s, t, _options);

        /// <summary>Like <see cref="IndexOf"/>, also giving the length of the match in <paramref name="s"/> (it can differ from t's under a collation).</summary>
        public int IndexOf(string s, string t, out int matchLength)
        {
            if (AsciiFold || _compareInfo == null)
            {
                matchLength = t.Length;
                return IndexOf(s, t);
            }
            return _compareInfo.IndexOf(s, t, _options, out matchLength);
        }

        public bool StartsWith(string s, string t) =>
            AsciiFold ? Fold(s).StartsWith(Fold(t), System.StringComparison.Ordinal)
            : _compareInfo == null ? s.StartsWith(t, System.StringComparison.Ordinal)
            : (_options & CompareOptions.IgnoreSymbols) != 0 ? _compareInfo.IsPrefix(StripVariable(s), StripVariable(t), _options & ~CompareOptions.IgnoreSymbols)
            : _compareInfo.IsPrefix(s, t, _options);

        public bool EndsWith(string s, string t) =>
            AsciiFold ? Fold(s).EndsWith(Fold(t), System.StringComparison.Ordinal)
            : _compareInfo == null ? s.EndsWith(t, System.StringComparison.Ordinal)
            : (_options & CompareOptions.IgnoreSymbols) != 0 ? _compareInfo.IsSuffix(StripVariable(s), StripVariable(t), _options & ~CompareOptions.IgnoreSymbols)
            : _compareInfo.IsSuffix(s, t, _options);

        // ICU's IsPrefix/IsSuffix mishandle ignorable characters at the edges (an all-ignorable suffix of "" is
        // reported missing), so with alternate=blanked the variable characters (punctuation, symbols, spaces)
        // are removed first.
        private static string StripVariable(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (System.Text.Rune r in s.EnumerateRunes())
            {
                UnicodeCategory cat = System.Text.Rune.GetUnicodeCategory(r);
                bool variable = cat is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
                    or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
                    or UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol
                    or UnicodeCategory.OtherSymbol or UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                    or UnicodeCategory.Control;
                if (!variable)
                {
                    sb.Append(r.ToString());
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// fn:collation-key: bytes that compare (as unsigned octets) as the strings do under this collation. UTF-8
        /// preserves code point order, so it serves the code point and ASCII case-folding collations.
        /// </summary>
        public byte[] KeyBytes(string s) =>
            AsciiFold ? System.Text.Encoding.UTF8.GetBytes(Fold(s))
            : _compareInfo == null ? System.Text.Encoding.UTF8.GetBytes(s)
            : _compareInfo.GetSortKey(s, _options).KeyData;

        /// <summary>A key such that equal keys mean equal strings under this collation (fn:collation-key, grouping).</summary>
        public string Key(string s) =>
            AsciiFold ? Fold(s)
            : _compareInfo == null ? s
            : System.Convert.ToBase64String(_compareInfo.GetSortKey(s, _options).KeyData);

        /// <summary>Resolves a collation URI. FOCH0002 if it isn't supported.</summary>
        public static Collation Resolve(string uri) => uri == CodepointUri ? Codepoint : s_cache.GetOrAdd(uri, Create);

        private static Collation Create(string uri)
        {
            if (uri == HtmlAsciiCaseInsensitiveUri)
            {
                return new Collation(uri, new HtmlAsciiCaseInsensitive(), null, CompareOptions.Ordinal);
            }
            if (uri.StartsWith(UcaPrefix, System.StringComparison.Ordinal) && (uri.Length == UcaPrefix.Length || uri[UcaPrefix.Length] == '?'))
            {
                return Uca(uri);
            }
            throw new XdmException("FOCH0002", $"Collation '{uri}' is not supported");
        }

        // http://www.w3.org/2013/collation/UCA?lang=en;strength=primary;fallback=no
        private static Collation Uca(string uri)
        {
            var parameters = new Dictionary<string, string>(System.StringComparer.Ordinal);
            int q = uri.IndexOf('?');
            if (q >= 0)
            {
                foreach (string pair in uri[(q + 1)..].Split(';', System.StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    if (eq > 0)
                    {
                        parameters[pair[..eq]] = pair[(eq + 1)..];
                    }
                }
            }
            bool fallback = !parameters.TryGetValue("fallback", out string? fb) || fb == "yes";
            CultureInfo culture = CultureInfo.InvariantCulture;
            if (parameters.TryGetValue("lang", out string? lang))
            {
                try
                {
                    culture = CultureInfo.GetCultureInfo(lang);
                }
                catch (CultureNotFoundException) when (fallback)
                {
                }
                catch (CultureNotFoundException)
                {
                    throw new XdmException("FOCH0002", $"Language '{lang}' is not supported by collation '{uri}'");
                }
            }
            string? strength = parameters.GetValueOrDefault("strength");
            CompareOptions options = strength switch
            {
                "primary" or "1" => CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth,
                "secondary" or "2" => CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth,
                null or "tertiary" or "3" or "quaternary" or "4" or "identical" or "5" => CompareOptions.None,
                _ when fallback => CompareOptions.None,
                _ => throw new XdmException("FOCH0002", $"Collation strength '{strength}' is not supported"),
            };
            // alternate=blanked: punctuation and symbols are ignorable (variable weighting).
            switch (parameters.GetValueOrDefault("alternate"))
            {
                case null or "non-ignorable":
                    break;
                case "blanked":
                    options |= CompareOptions.IgnoreSymbols;
                    break;
                case "shifted":
                    // Shifted keeps punctuation as a quaternary-level tie-breaker, which CompareOptions can't express.
                    if (strength is "quaternary" or "4" or "identical" or "5")
                    {
                        if (!fallback)
                        {
                            throw new XdmException("FOCH0002", "alternate=shifted at quaternary strength is not supported");
                        }
                    }
                    else
                    {
                        options |= CompareOptions.IgnoreSymbols;
                    }
                    break;
                case var other when !fallback:
                    throw new XdmException("FOCH0002", $"Collation parameter alternate={other} is not supported");
            }
            foreach (string key in parameters.Keys)
            {
                bool known = key is "fallback" or "lang" or "strength" or "alternate";
                if (!known && !fallback)
                {
                    throw new XdmException("FOCH0002", $"Collation parameter '{key}' is not supported");
                }
            }
            CompareInfo info = culture.CompareInfo;
            if (strength is "identical" or "5")
            {
                // Identical strength: strings equal at every level are still ordered (and distinguished) by code points.
                IComparer<string> levels = Comparer.Create(info, options);
                return new Collation(uri, Comparer<string>.Create((x, y) => levels.Compare(x, y) is int c && c != 0 ? c : Comparison.CodepointCollation.Compare(x, y)), null, CompareOptions.Ordinal);
            }
            return new Collation(uri, Comparer.Create(info, options), info, options);
        }

        private static class Comparer
        {
            public static IComparer<string> Create(CompareInfo info, CompareOptions options) => new CultureComparer(info, options);

            private sealed class CultureComparer(CompareInfo info, CompareOptions options) : IComparer<string>
            {
                public int Compare(string? x, string? y) => info.Compare(x, y, options);
            }
        }

        // HTML ASCII case-insensitive: ASCII letters fold, everything else compares by code point.
        private sealed class HtmlAsciiCaseInsensitive : IComparer<string>
        {
            public int Compare(string? x, string? y) =>
                Comparison.CodepointCollation.Compare(Fold(x ?? ""), Fold(y ?? ""));

            private static string Fold(string s)
            {
                char[] chars = s.ToCharArray();
                for (int i = 0; i < chars.Length; i++)
                {
                    if (chars[i] is >= 'A' and <= 'Z') chars[i] = (char)(chars[i] + 32);
                }
                return new string(chars);
            }
        }
    }
}
