// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Xsl3.XPath31.Functions
{
    /// <summary>
    /// An immutable set of Unicode code points as sorted, disjoint, non-adjacent ranges. Regular expression
    /// character classes are computed as these sets and then written out for .NET, whose classes work on
    /// UTF-16 code units and so can't express supplementary characters, negation or subtraction directly.
    /// </summary>
    internal sealed class CodePointSet
    {
        public const int MaxCodePoint = 0x10FFFF;

        private readonly (int Low, int High)[] _ranges;

        private CodePointSet((int, int)[] ranges) => _ranges = ranges;

        public static readonly CodePointSet Empty = new(System.Array.Empty<(int, int)>());

        /// <summary>Every code point except the surrogates, which never stand alone in a valid string.</summary>
        public static readonly CodePointSet All = Of((0, 0xD7FF), (0xE000, MaxCodePoint));

        public IReadOnlyList<(int Low, int High)> Ranges => _ranges;

        public bool IsEmpty => _ranges.Length == 0;

        public static CodePointSet Of(params (int Low, int High)[] ranges) => Normalize(ranges);

        public static CodePointSet Single(int cp) => new(new[] { (cp, cp) });

        public bool Contains(int cp)
        {
            int lo = 0, hi = _ranges.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (cp < _ranges[mid].Low) hi = mid - 1;
                else if (cp > _ranges[mid].High) lo = mid + 1;
                else return true;
            }
            return false;
        }

        public CodePointSet Union(CodePointSet other) => other.IsEmpty ? this : IsEmpty ? other : Normalize(_ranges.Concat(other._ranges));

        public CodePointSet Complement()
        {
            var result = new List<(int, int)>();
            int next = 0;
            foreach (var (low, high) in _ranges)
            {
                if (low > next) result.Add((next, low - 1));
                next = high + 1;
            }
            if (next <= MaxCodePoint) result.Add((next, MaxCodePoint));
            return new CodePointSet(result.ToArray()).Intersect(All);
        }

        public CodePointSet Intersect(CodePointSet other)
        {
            var result = new List<(int, int)>();
            int i = 0, j = 0;
            while (i < _ranges.Length && j < other._ranges.Length)
            {
                int low = System.Math.Max(_ranges[i].Low, other._ranges[j].Low);
                int high = System.Math.Min(_ranges[i].High, other._ranges[j].High);
                if (low <= high) result.Add((low, high));
                if (_ranges[i].High < other._ranges[j].High) i++;
                else j++;
            }
            return new CodePointSet(result.ToArray());
        }

        public CodePointSet Subtract(CodePointSet other) => Intersect(other.Complement());

        private static CodePointSet Normalize(IEnumerable<(int Low, int High)> ranges)
        {
            var sorted = ranges.Where(r => r.Low <= r.High).OrderBy(r => r.Low).ToList();
            var result = new List<(int, int)>(sorted.Count);
            foreach (var (low, high) in sorted)
            {
                if (result.Count > 0 && low <= result[^1].Item2 + 1)
                {
                    result[^1] = (result[^1].Item1, System.Math.Max(result[^1].Item2, high));
                }
                else
                {
                    result.Add((low, high));
                }
            }
            return new CodePointSet(result.ToArray());
        }

        /// <summary>
        /// A .NET pattern matching exactly one code point of the set: a character class for the Basic Multilingual
        /// Plane and an alternation of surrogate pairs for the rest.
        /// </summary>
        public string ToPattern()
        {
            var bmp = new StringBuilder();
            var supplementary = new List<string>();
            foreach (var (low, high) in _ranges)
            {
                if (low <= 0xFFFF)
                {
                    int h = System.Math.Min(high, 0xFFFF);
                    bmp.Append(Escape(low));
                    if (h > low)
                    {
                        bmp.Append(h > low + 1 ? "-" : "").Append(Escape(h));
                    }
                }
                if (high > 0xFFFF)
                {
                    supplementary.Add(Utf16Range(System.Math.Max(low, 0x10000), high));
                }
            }
            if (bmp.Length == 0 && supplementary.Count == 0)
            {
                return "(?!)";
            }
            if (supplementary.Count == 0)
            {
                return "[" + bmp + "]";
            }
            string pairs = string.Join("|", supplementary);
            return bmp.Length == 0 ? "(?:" + pairs + ")" : "(?:[" + bmp + "]|" + pairs + ")";
        }

        /// <summary>
        /// Xsl3 (P4): a .NET character class for the set's code points in the Basic Multilingual Plane, surrogates left
        /// out. It matches exactly what <see cref="ToPattern"/> matches in a string without surrogates, and without
        /// ToPattern's alternation for the supplementary planes, which keeps .NET's set loops (\w+ 41× faster).
        /// </summary>
        public string ToBmpPattern()
        {
            var bmp = new StringBuilder();
            foreach (var (low, high) in _ranges)
            {
                foreach (var (l, h) in new[] { (low, System.Math.Min(high, 0xD7FF)), (System.Math.Max(low, 0xE000), System.Math.Min(high, 0xFFFF)) })
                {
                    if (l <= h)
                    {
                        bmp.Append(Escape(l));
                        if (h > l)
                        {
                            bmp.Append(h > l + 1 ? "-" : "").Append(Escape(h));
                        }
                    }
                }
            }
            return bmp.Length == 0 ? "(?!)" : "[" + bmp + "]";
        }

        private static string Escape(int cp) =>
            cp is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' ? ((char)cp).ToString() : "\\u" + cp.ToString("X4", CultureInfo.InvariantCulture);

        // An alternation matching the UTF-16 encodings of the code points low..high (both supplementary).
        private static string Utf16Range(int low, int high)
        {
            static (char Hi, char Lo) Split(int cp) => (char.ConvertFromUtf32(cp)[0], char.ConvertFromUtf32(cp)[1]);
            static string U(char c) => "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture);
            var (h1, l1) = Split(low);
            var (h2, l2) = Split(high);
            if (h1 == h2)
            {
                return l1 == l2 ? $"{U(h1)}{U(l1)}" : $"{U(h1)}[{U(l1)}-{U(l2)}]";
            }
            var parts = new List<string> { $"{U(h1)}[{U(l1)}-\\uDFFF]" };
            if (h2 - h1 > 1)
            {
                parts.Add($"[{U((char)(h1 + 1))}-{U((char)(h2 - 1))}][\\uDC00-\\uDFFF]");
            }
            parts.Add($"{U(h2)}[\\uDC00-{U(l2)}]");
            return string.Join("|", parts);
        }

        // ------------------------------------------------------------ Unicode data

        private static CodePointSet[]? s_categories;

        /// <summary>The code points of a general category (one letter for the whole group: L, M, N, P, S, Z, C).</summary>
        public static CodePointSet? Category(string name)
        {
            CodePointSet[] categories = s_categories ??= BuildCategories();
            if (name.Length == 1)
            {
                // XSD's \p{C} covers Cc, Cf, Co and Cn (not the surrogates, which never occur alone).
                string group = name;
                if ("LMNPSZC".IndexOf(group[0]) < 0) return null;
                CodePointSet result = Empty;
                foreach (var (abbrev, category) in s_categoryNames)
                {
                    if (abbrev[0] == group[0] && category != UnicodeCategory.Surrogate)
                    {
                        result = result.Union(categories[(int)category]);
                    }
                }
                return result;
            }
            foreach (var (abbrev, category) in s_categoryNames)
            {
                if (abbrev == name && category != UnicodeCategory.Surrogate)
                {
                    return categories[(int)category];
                }
            }
            return null;
        }

        private static readonly (string, UnicodeCategory)[] s_categoryNames =
        {
            ("Lu", UnicodeCategory.UppercaseLetter), ("Ll", UnicodeCategory.LowercaseLetter), ("Lt", UnicodeCategory.TitlecaseLetter),
            ("Lm", UnicodeCategory.ModifierLetter), ("Lo", UnicodeCategory.OtherLetter), ("Mn", UnicodeCategory.NonSpacingMark),
            ("Mc", UnicodeCategory.SpacingCombiningMark), ("Me", UnicodeCategory.EnclosingMark), ("Nd", UnicodeCategory.DecimalDigitNumber),
            ("Nl", UnicodeCategory.LetterNumber), ("No", UnicodeCategory.OtherNumber), ("Zs", UnicodeCategory.SpaceSeparator),
            ("Zl", UnicodeCategory.LineSeparator), ("Zp", UnicodeCategory.ParagraphSeparator), ("Cc", UnicodeCategory.Control),
            ("Cf", UnicodeCategory.Format), ("Cs", UnicodeCategory.Surrogate), ("Co", UnicodeCategory.PrivateUse),
            ("Pc", UnicodeCategory.ConnectorPunctuation), ("Pd", UnicodeCategory.DashPunctuation), ("Ps", UnicodeCategory.OpenPunctuation),
            ("Pe", UnicodeCategory.ClosePunctuation), ("Pi", UnicodeCategory.InitialQuotePunctuation), ("Pf", UnicodeCategory.FinalQuotePunctuation),
            ("Po", UnicodeCategory.OtherPunctuation), ("Sm", UnicodeCategory.MathSymbol), ("Sc", UnicodeCategory.CurrencySymbol),
            ("Sk", UnicodeCategory.ModifierSymbol), ("So", UnicodeCategory.OtherSymbol), ("Cn", UnicodeCategory.OtherNotAssigned),
        };

        private static CodePointSet[] BuildCategories()
        {
            var ranges = new List<(int, int)>[30];
            for (int i = 0; i < ranges.Length; i++) ranges[i] = new List<(int, int)>();
            int start = 0;
            UnicodeCategory current = CharUnicodeInfo.GetUnicodeCategory(0);
            for (int cp = 1; cp <= MaxCodePoint + 1; cp++)
            {
                UnicodeCategory cat = cp <= MaxCodePoint ? CharUnicodeInfo.GetUnicodeCategory(cp) : (UnicodeCategory)(-1);
                if (cat != current)
                {
                    ranges[(int)current].Add((start, cp - 1));
                    start = cp;
                    current = cat;
                }
            }
            return ranges.Select(r => new CodePointSet(r.ToArray())).ToArray();
        }

        private static readonly ConcurrentDictionary<string, CodePointSet?> s_blocks = new(System.StringComparer.Ordinal);

        /// <summary>A Unicode block by its XML Schema name (IsBasicLatin is "BasicLatin"), or null if unknown.</summary>
        public static CodePointSet? Block(string name) => s_blocks.GetOrAdd(name, BuildBlock);

        // XML Schema 1.0 block names (Unicode 3.1) outside the Basic Multilingual Plane; .NET only knows BMP blocks.
        private static readonly Dictionary<string, (int, int)> s_supplementaryBlocks = new(System.StringComparer.Ordinal)
        {
            ["OldItalic"] = (0x10300, 0x1032F),
            ["Gothic"] = (0x10330, 0x1034F),
            ["Deseret"] = (0x10400, 0x1044F),
            ["ByzantineMusicalSymbols"] = (0x1D000, 0x1D0FF),
            ["MusicalSymbols"] = (0x1D100, 0x1D1FF),
            ["MathematicalAlphanumericSymbols"] = (0x1D400, 0x1D7FF),
            ["CJKUnifiedIdeographsExtensionB"] = (0x20000, 0x2A6DF),
            ["CJKCompatibilityIdeographsSupplement"] = (0x2F800, 0x2FA1F),
            ["Tags"] = (0xE0000, 0xE007F),
            // Later Unicode versions (XPath 3.1 allows the block names of the Unicode version in use).
            ["LinearBSyllabary"] = (0x10000, 0x1007F),
            ["LinearBIdeograms"] = (0x10080, 0x100FF),
            ["AegeanNumbers"] = (0x10100, 0x1013F),
            ["Ugaritic"] = (0x10380, 0x1039F),
            ["Shavian"] = (0x10450, 0x1047F),
            ["Osmanya"] = (0x10480, 0x104AF),
            ["CypriotSyllabary"] = (0x10800, 0x1083F),
            ["TaiXuanJingSymbols"] = (0x1D300, 0x1D35F),
            ["MahjongTiles"] = (0x1F000, 0x1F02F),
            ["DominoTiles"] = (0x1F030, 0x1F09F),
            ["PlayingCards"] = (0x1F0A0, 0x1F0FF),
            ["EnclosedAlphanumericSupplement"] = (0x1F100, 0x1F1FF),
            ["EnclosedIdeographicSupplement"] = (0x1F200, 0x1F2FF),
            ["MiscellaneousSymbolsandPictographs"] = (0x1F300, 0x1F5FF),
            ["Emoticons"] = (0x1F600, 0x1F64F),
            ["TransportandMapSymbols"] = (0x1F680, 0x1F6FF),
            ["AlchemicalSymbols"] = (0x1F700, 0x1F77F),
            ["SupplementalSymbolsandPictographs"] = (0x1F900, 0x1F9FF),
            ["CJKUnifiedIdeographsExtensionC"] = (0x2A700, 0x2B73F),
            ["CJKUnifiedIdeographsExtensionD"] = (0x2B740, 0x2B81F),
            ["VariationSelectorsSupplement"] = (0xE0100, 0xE01EF),
            ["SupplementaryPrivateUseArea-A"] = (0xF0000, 0xFFFFF),
            ["SupplementaryPrivateUseArea-B"] = (0x100000, 0x10FFFF),
        };

        private static string? s_bmp;

        private static CodePointSet? BuildBlock(string name)
        {
            if (s_supplementaryBlocks.TryGetValue(name, out (int Low, int High) range))
            {
                return Of(range);
            }
            Regex probe;
            try
            {
                probe = new Regex("\\p{Is" + name + "}", RegexOptions.CultureInvariant);
            }
            catch (System.ArgumentException)
            {
                return null;
            }
            // Every BMP character (surrogates as U+FFFF stand-ins), matched in one pass.
            s_bmp ??= string.Create(0x10000, 0, (span, _) =>
            {
                for (int i = 0; i < span.Length; i++) span[i] = i is >= 0xD800 and <= 0xDFFF ? '￿' : (char)i;
            });
            var ranges = new List<(int, int)>();
            foreach (Match m in probe.Matches(s_bmp))
            {
                if (m.Index is < 0xD800 or > 0xDFFF)
                {
                    ranges.Add((m.Index, m.Index));
                }
            }
            return Normalize(ranges);
        }

        private static Dictionary<int, int[]>? s_caseVariants;

        /// <summary>The set with every code point's case variants added (for the regular expression flag i).</summary>
        public CodePointSet WithCaseVariants()
        {
            Dictionary<int, int[]> variants = s_caseVariants ??= BuildCaseVariants();
            var extra = new List<(int, int)>();
            foreach (var (low, high) in _ranges)
            {
                if (high - low > 0x30000)
                {
                    // A huge range: walk the variant table instead of the range.
                    foreach (var (cp, others) in variants)
                    {
                        if (cp >= low && cp <= high) extra.AddRange(others.Select(o => (o, o)));
                    }
                    continue;
                }
                for (int cp = low; cp <= high; cp++)
                {
                    if (variants.TryGetValue(cp, out int[]? others)) extra.AddRange(others.Select(o => (o, o)));
                }
            }
            return extra.Count == 0 ? this : Normalize(_ranges.Concat(extra));
        }

        // Code points that are case variants of each other (simple mappings, both directions, transitively).
        private static Dictionary<int, int[]> BuildCaseVariants()
        {
            var groups = new Dictionary<int, HashSet<int>>();
            void Link(int a, int b)
            {
                if (a == b) return;
                if (!groups.TryGetValue(a, out HashSet<int>? ga)) groups[a] = ga = new HashSet<int> { a };
                if (!groups.TryGetValue(b, out HashSet<int>? gb)) groups[b] = gb = new HashSet<int> { b };
                if (ReferenceEquals(ga, gb)) return;
                ga.UnionWith(gb);
                foreach (int m in gb) groups[m] = ga;
            }
            for (int cp = 0; cp <= MaxCodePoint; cp++)
            {
                if (cp is >= 0xD800 and <= 0xDFFF) continue;
                var r = new Rune(cp);
                Link(cp, Rune.ToUpperInvariant(r).Value);
                Link(cp, Rune.ToLowerInvariant(r).Value);
            }
            return groups.ToDictionary(kv => kv.Key, kv => kv.Value.Where(v => v != kv.Key).ToArray());
        }
    }
}
