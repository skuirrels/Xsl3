// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    /// <summary>
    /// fn:format-integer (F&amp;O 3.1 §4.6.1), also used for the numeric components of format-date and friends.
    /// A picture is a primary format token and an optional format modifier after ';'.
    /// </summary>
    internal sealed class IntegerFormatter
    {
        private enum Kind { Sequence, Chinese, Decimal, AlphaLower, AlphaUpper, RomanLower, RomanUpper, WordsLower, WordsUpper, WordsTitle, OtherAlpha }

        private readonly Kind _kind;
        private readonly bool _ordinal;
        private readonly int _zero;            // the zero digit of the digit family (decimal)
        private readonly int _mandatory;       // number of mandatory digits (decimal)
        private readonly List<(int Position, string Separator)> _grouping = new(); // positions from the right
        private readonly int? _regularInterval;
        private readonly string? _regularSeparator;
        private readonly int _alphaStart;      // first code point of an "other" alphabetic sequence

        /// <summary>Parses a picture. <paramref name="error"/> is the code for an invalid one (FODF1310, or FOFD1340 inside date pictures).</summary>
        /// <remarks>
        /// Inside date pictures the modifier is not separated by ';' (which is then a grouping separator): pass
        /// <paramref name="dateModifier"/> instead, with <paramref name="modifierInPicture"/> false.
        /// </remarks>
        public IntegerFormatter(string picture, string error = "FODF1310", bool allowEmpty = false, bool modifierInPicture = true, char? dateModifier = null)
        {
            string token = picture;
            string? modifier = null;
            int semicolon = modifierInPicture ? picture.LastIndexOf(';') : -1;
            _ordinal = dateModifier == 'o';
            if (semicolon >= 0)
            {
                token = picture[..semicolon];
                modifier = picture[(semicolon + 1)..];
            }
            if (token.Length == 0)
            {
                if (!allowEmpty) throw new XdmException(error, $"Invalid picture '{picture}': empty format token");
                token = "1";
            }
            if (modifier != null)
            {
                // ^([co](\(.+\))?)?[at]?$
                int i = 0;
                if (i < modifier.Length && modifier[i] is 'c' or 'o')
                {
                    _ordinal = modifier[i] == 'o';
                    i++;
                    if (i < modifier.Length && modifier[i] == '(')
                    {
                        int close = modifier.IndexOf(')', i);
                        if (close < 0 || close == i + 1) throw new XdmException(error, $"Invalid format modifier '{modifier}'");
                        i = close + 1;
                    }
                }
                if (i < modifier.Length && modifier[i] is 'a' or 't') i++;
                if (i != modifier.Length) throw new XdmException(error, $"Invalid format modifier '{modifier}'");
            }

            switch (token)
            {
                case "A": _kind = Kind.AlphaUpper; return;
                case "a": _kind = Kind.AlphaLower; return;
                case "I": _kind = Kind.RomanUpper; return;
                case "i": _kind = Kind.RomanLower; return;
                case "W": _kind = Kind.WordsUpper; return;
                case "w": _kind = Kind.WordsLower; return;
                case "Ww": _kind = Kind.WordsTitle; return;
                case "一": _kind = Kind.Chinese; return;
            }

            int[] cps = CodePoints.Of(token).ToArray();
            bool hasDigit = cps.Any(cp => CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0) == UnicodeCategory.DecimalDigitNumber);
            if (!hasDigit)
            {
                // A numbering sequence such as ①②③ or ⑴⑵⑶: consecutive code points with numeric values 1, 2, 3, …
                if (cps.Length == 1 && CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cps[0]), 0) == UnicodeCategory.OtherNumber &&
                    CharUnicodeInfo.GetNumericValue(char.ConvertFromUtf32(cps[0]), 0) == 1 && SequenceLength(cps[0]) > 1)
                {
                    _kind = Kind.Sequence;
                    _alphaStart = cps[0];
                    return;
                }
                // Some other format token: number with the alphabetic sequence starting at its first letter,
                // or fall back to decimal "1".
                if (cps.Length == 1 && (cps[0] is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or 0x3B1 or 0x391 or 0x430 or 0x410))
                {
                    _kind = Kind.OtherAlpha;
                    _alphaStart = cps[0];
                    return;
                }
                _kind = Kind.Decimal;
                _zero = '0';
                _mandatory = 1;
                return;
            }

            // A decimal-digit-pattern: optional '#' digits, then mandatory digits of one family, with grouping
            // separators (anything that isn't a digit or a letter) between them.
            _kind = Kind.Decimal;
            int? family = null;
            bool seenMandatory = false;
            var digitPositions = new List<int>(); // separators, counted from the left in digits
            int digits = 0;
            bool lastWasSeparator = true;
            var separators = new List<(int AfterDigits, string Separator)>();
            foreach (int cp in cps)
            {
                string ch = char.ConvertFromUtf32(cp);
                if (cp == '#')
                {
                    if (seenMandatory) throw new XdmException(error, $"Invalid picture '{picture}': '#' after a mandatory digit");
                    digits++;
                    lastWasSeparator = false;
                    continue;
                }
                if (CharUnicodeInfo.GetUnicodeCategory(ch, 0) == UnicodeCategory.DecimalDigitNumber)
                {
                    int zero = cp - (int)CharUnicodeInfo.GetDecimalDigitValue(ch, 0);
                    if (family != null && family != zero) throw new XdmException(error, $"Invalid picture '{picture}': digits from different families");
                    family = zero;
                    seenMandatory = true;
                    _mandatory++;
                    digits++;
                    lastWasSeparator = false;
                    continue;
                }
                if (char.IsLetter(ch, 0))
                {
                    throw new XdmException(error, $"Invalid picture '{picture}'");
                }
                // grouping separator
                if (lastWasSeparator) throw new XdmException(error, $"Invalid picture '{picture}': misplaced grouping separator");
                separators.Add((digits, ch));
                lastWasSeparator = true;
            }
            if (lastWasSeparator || family == null) throw new XdmException(error, $"Invalid picture '{picture}'");
            _zero = family.Value;

            // Positions counted from the right.
            foreach (var (after, sep) in separators)
            {
                _grouping.Add((digits - after, sep));
            }
            // Regular grouping: same separator, positions all multiples of the first.
            if (_grouping.Count > 0)
            {
                int first = _grouping.Min(g => g.Position);
                var positions = _grouping.Select(g => g.Position).OrderBy(p => p).ToList();
                // Regular: one separator character, at every multiple of the first position within the picture's digits.
                bool regular = _grouping.All(g => g.Separator == _grouping[0].Separator) &&
                               positions.All(p => p % first == 0) &&
                               Enumerable.Range(1, System.Math.Max(0, (digits - 1) / first)).All(k => positions.Contains(k * first));
                if (regular)
                {
                    _regularInterval = first;
                    _regularSeparator = _grouping[0].Separator;
                }
            }
        }

        /// <summary>True for a decimal-digit-pattern (including the fallback "1").</summary>
        public bool IsDecimal => _kind == Kind.Decimal;

        /// <summary>The number of mandatory digits of a decimal-digit-pattern.</summary>
        public int MandatoryDigits => _mandatory;

        public string Format(BigInteger value, string? language = null)
        {
            bool negative = value.Sign < 0;
            BigInteger abs = BigInteger.Abs(value);
            string body = _kind switch
            {
                Kind.Decimal => FormatDecimal(abs) + (_ordinal ? OrdinalSuffix(abs) : ""),
                Kind.Chinese => abs >= 1 && abs < 100_000_000 ? Chinese((int)abs) : FormatDecimalPlain(abs),
                Kind.Sequence => abs >= 1 && abs <= SequenceLength(_alphaStart) ? char.ConvertFromUtf32(_alphaStart + (int)abs - 1) : FormatDecimalPlain(abs),
                Kind.AlphaLower => abs.IsZero ? FormatDecimalPlain(abs) : Alphabetic(abs, 'a', 26),
                Kind.AlphaUpper => abs.IsZero ? FormatDecimalPlain(abs) : Alphabetic(abs, 'A', 26),
                Kind.OtherAlpha => abs.IsZero ? FormatDecimalPlain(abs) : Alphabetic(abs, _alphaStart, AlphabetLength(_alphaStart)),
                Kind.RomanLower => Roman(abs)?.ToLowerInvariant() ?? FormatDecimalPlain(abs),
                Kind.RomanUpper => Roman(abs) ?? FormatDecimalPlain(abs),
                Kind.WordsLower or Kind.WordsUpper or Kind.WordsTitle when GermanWords.Applies(language) => _kind switch
                {
                    Kind.WordsLower => GermanWords.ToWords(abs, _ordinal),
                    Kind.WordsUpper => GermanWords.ToWords(abs, _ordinal).ToUpperInvariant(),
                    _ => GermanWords.TitleCase(GermanWords.ToWords(abs, _ordinal)),
                },
                Kind.WordsLower => Words.ToWords(abs, _ordinal),
                Kind.WordsUpper => Words.ToWords(abs, _ordinal).ToUpperInvariant(),
                _ => Words.TitleCase(Words.ToWords(abs, _ordinal)),
            };
            return negative ? "-" + body : body;
        }

        private string FormatDecimalPlain(BigInteger abs) => abs.ToString(CultureInfo.InvariantCulture);

        private string FormatDecimal(BigInteger abs)
        {
            string ascii = abs.ToString(CultureInfo.InvariantCulture).PadLeft(_mandatory, '0');
            var digits = ascii.Select(c => char.ConvertFromUtf32(_zero + (c - '0'))).ToList();
            var sb = new StringBuilder();
            for (int i = 0; i < digits.Count; i++)
            {
                int fromRight = digits.Count - i;
                sb.Append(digits[i]);
                int after = fromRight - 1; // digits that follow this one
                if (after > 0)
                {
                    if (_regularInterval is int interval)
                    {
                        if (after % interval == 0) sb.Append(_regularSeparator);
                    }
                    else
                    {
                        foreach (var (pos, sep) in _grouping)
                        {
                            if (pos == after) sb.Append(sep);
                        }
                    }
                }
            }
            return sb.ToString();
        }

        private static string OrdinalSuffix(BigInteger n)
        {
            int lastTwo = (int)(n % 100), last = (int)(n % 10);
            if (lastTwo is >= 11 and <= 13) return "th";
            return last switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        }

        // Chinese numerals, in the form Saxon produces for the QT3 tests: 十一, 二十, 百五十一, 三百二, 二千二十五.
        private static string Chinese(int n)
        {
            const string digits = "〇一二三四五六七八九";
            if (n >= 10000)
            {
                return Chinese(n / 10000) + "万" + (n % 10000 == 0 ? "" : Chinese(n % 10000));
            }
            var sb = new StringBuilder();
            (int Value, string Unit)[] places = { (1000, "千"), (100, "百"), (10, "十"), (1, "") };
            foreach (var (value, unit) in places)
            {
                int d = n / value % 10;
                if (d > 0)
                {
                    sb.Append(d == 1 && value > 1 ? "" : digits[d].ToString()).Append(unit);
                }
            }
            return sb.ToString();
        }

        private static int SequenceLength(int start)
        {
            int n = 0;
            while (start + n <= 0x10FFFF && !(start + n is >= 0xD800 and <= 0xDFFF) && CharUnicodeInfo.GetNumericValue(char.ConvertFromUtf32(start + n), 0) == n + 1)
            {
                n++;
            }
            return n;
        }

        private static int AlphabetLength(int start) =>
            start switch
            {
                0x3B1 or 0x391 => 24, // Greek
                0x430 or 0x410 => 32, // Cyrillic
                _ => 26,
            };

        // a, b, …, z, aa, ab, … (bijective base N).
        private static string Alphabetic(BigInteger n, int first, int length)
        {
            var sb = new StringBuilder();
            while (n > 0)
            {
                n -= 1;
                int digit = (int)(n % length);
                int cp = first + digit;
                if (first == 0x3B1 && cp >= 0x3C2) cp++; // skip final sigma
                if (first == 0x391 && cp >= 0x3A2) cp++; // no capital final sigma
                sb.Insert(0, char.ConvertFromUtf32(cp));
                n /= length;
            }
            return sb.ToString();
        }

        private static string? Roman(BigInteger n)
        {
            if (n <= 0 || n >= 4000) return null;
            int v = (int)n;
            (int, string)[] table = { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
            var sb = new StringBuilder();
            foreach (var (value, symbol) in table)
            {
                while (v >= value)
                {
                    sb.Append(symbol);
                    v -= value;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>English number words, cardinal and ordinal.</summary>
    internal static class Words
    {
        private static readonly string[] s_units = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
        private static readonly string[] s_tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };
        private static readonly (BigInteger Value, string Name)[] s_scales =
        {
            (BigInteger.Pow(10, 18), "quintillion"), (BigInteger.Pow(10, 15), "quadrillion"), (BigInteger.Pow(10, 12), "trillion"),
            (BigInteger.Pow(10, 9), "billion"), (BigInteger.Pow(10, 6), "million"), (1000, "thousand"),
        };

        public static string ToWords(BigInteger n, bool ordinal)
        {
            string words = Cardinal(n);
            return ordinal ? MakeOrdinal(words) : words;
        }

        private static string Cardinal(BigInteger n)
        {
            if (n < 20) return s_units[(int)n];
            if (n < 100)
            {
                int t = (int)(n / 10), u = (int)(n % 10);
                return u == 0 ? s_tens[t] : s_tens[t] + " " + s_units[u];
            }
            if (n < 1000)
            {
                int h = (int)(n / 100);
                BigInteger rest = n % 100;
                return s_units[h] + " hundred" + (rest.IsZero ? "" : " and " + Cardinal(rest));
            }
            foreach (var (value, name) in s_scales)
            {
                if (n >= value)
                {
                    BigInteger high = n / value, rest = n % value;
                    string tail = rest.IsZero ? "" : rest < 100 ? " and " + Cardinal(rest) : " " + Cardinal(rest);
                    return Cardinal(high) + " " + name + tail;
                }
            }
            return n.ToString(CultureInfo.InvariantCulture);
        }

        private static string MakeOrdinal(string cardinal)
        {
            int space = cardinal.LastIndexOf(' ');
            string head = space < 0 ? "" : cardinal[..(space + 1)];
            string last = space < 0 ? cardinal : cardinal[(space + 1)..];
            string ord = last switch
            {
                "one" => "first", "two" => "second", "three" => "third", "five" => "fifth", "eight" => "eighth",
                "nine" => "ninth", "twelve" => "twelfth",
                _ when last.EndsWith('y') => last[..^1] + "ieth",
                _ => last + "th",
            };
            return head + ord;
        }

        public static string TitleCase(string s) =>
            string.Join(" ", s.Split(' ').Select(w => w == "and" ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }

    /// <summary>
    /// German number words (lang="de", "de-…"), cardinal and ordinal: written as one word below a million
    /// (einundzwanzig, zweihundertdrei), with Million and Milliarde as words of their own.
    /// </summary>
    internal static class GermanWords
    {
        private static readonly string[] s_units =
            { "null", "eins", "zwei", "drei", "vier", "fünf", "sechs", "sieben", "acht", "neun", "zehn", "elf", "zwölf",
              "dreizehn", "vierzehn", "fünfzehn", "sechzehn", "siebzehn", "achtzehn", "neunzehn" };
        private static readonly string[] s_tens = { "", "", "zwanzig", "dreißig", "vierzig", "fünfzig", "sechzig", "siebzig", "achtzig", "neunzig" };

        public static bool Applies(string? language) =>
            language != null && (language.Equals("de", StringComparison.OrdinalIgnoreCase) || language.StartsWith("de-", StringComparison.OrdinalIgnoreCase));

        public static string ToWords(BigInteger n, bool ordinal)
        {
            string words = Cardinal(n, alone: true);
            if (!ordinal)
            {
                return words;
            }
            int last = (int)(n % 100);
            // Up to nineteen the ordinal adds -te (erste, dritte, siebte, achte); from twenty, -ste.
            if (n.IsZero || last is 0 or >= 20)
            {
                return words + "ste";
            }
            string stem = words[..^s_units[last].Length];
            return stem + (last switch { 1 => "erste", 3 => "dritte", 7 => "siebte", 8 => "achte", _ => s_units[last] + "te" });
        }

        // alone: "eins" for one standing alone; "ein" inside a word (einundzwanzig, einhundert).
        private static string Cardinal(BigInteger n, bool alone)
        {
            if (n < 20)
            {
                return n == 1 && !alone ? "ein" : s_units[(int)n];
            }
            if (n < 100)
            {
                int t = (int)(n / 10), u = (int)(n % 10);
                return u == 0 ? s_tens[t] : (u == 1 ? "ein" : s_units[u]) + "und" + s_tens[t];
            }
            if (n < 1000)
            {
                BigInteger rest = n % 100;
                return Cardinal(n / 100, alone: false) + "hundert" + (rest.IsZero ? "" : Cardinal(rest, alone));
            }
            if (n < 1_000_000)
            {
                BigInteger rest = n % 1000;
                return Cardinal(n / 1000, alone: false) + "tausend" + (rest.IsZero ? "" : Cardinal(rest, alone));
            }
            foreach (var (value, one, many) in new[] { (BigInteger.Pow(10, 9), "Milliarde", "Milliarden"), (BigInteger.Pow(10, 6), "Million", "Millionen") })
            {
                if (n >= value)
                {
                    BigInteger high = n / value, rest = n % value;
                    string head = high == 1 ? "eine " + one : Cardinal(high, alone: true) + " " + many;
                    return rest.IsZero ? head : head + " " + Cardinal(rest, alone);
                }
            }
            return n.ToString(CultureInfo.InvariantCulture);
        }

        public static string TitleCase(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }
}
