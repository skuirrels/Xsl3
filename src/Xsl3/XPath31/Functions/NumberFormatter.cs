// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    /// <summary>A decimal format (XPath 3.1 §2.1.1, F&amp;O 3.1 §4.7.2). Characters are code points.</summary>
    internal sealed record XPathDecimalFormat
    {
        public int DecimalSeparator { get; init; } = '.';
        public int GroupingSeparator { get; init; } = ',';
        public string ExponentSeparator { get; init; } = "e";
        public string Infinity { get; init; } = "Infinity";
        public int MinusSign { get; init; } = '-';
        public string NaN { get; init; } = "NaN";
        public int Percent { get; init; } = '%';
        public int PerMille { get; init; } = '‰';
        public int ZeroDigit { get; init; } = '0';
        public int Digit { get; init; } = '#';
        public int PatternSeparator { get; init; } = ';';

        public static readonly XPathDecimalFormat Default = new();

        public int ExponentChar => char.ConvertToUtf32(ExponentSeparator, 0);

        /// <summary>
        /// Builds a decimal format from property values (attribute names as in XSLT's xsl:decimal-format), checking
        /// that single-character properties are single characters (<paramref name="invalidCode"/>) and that the
        /// picture characters are distinct (<paramref name="conflictCode"/>).
        /// </summary>
        public static XPathDecimalFormat FromProperties(IEnumerable<KeyValuePair<string, string>> properties, XPathDecimalFormat? start = null, string invalidCode = "XQST0097", string conflictCode = "XQST0098")
        {
            XPathDecimalFormat f = start ?? Default;
            int Char(string name, string value)
            {
                int[] cps = CodePoints.Of(value).ToArray();
                if (cps.Length != 1)
                {
                    throw new XdmException(invalidCode, $"Decimal format property {name} must be a single character, not '{value}'");
                }
                return cps[0];
            }
            foreach (var (name, value) in properties)
            {
                f = name switch
                {
                    "decimal-separator" => f with { DecimalSeparator = Char(name, value) },
                    "grouping-separator" => f with { GroupingSeparator = Char(name, value) },
                    "exponent-separator" => f with { ExponentSeparator = char.ConvertFromUtf32(Char(name, value)) },
                    "infinity" => f with { Infinity = value },
                    "minus-sign" => f with { MinusSign = Char(name, value) },
                    "NaN" => f with { NaN = value },
                    "percent" => f with { Percent = Char(name, value) },
                    "per-mille" => f with { PerMille = Char(name, value) },
                    "zero-digit" => f with { ZeroDigit = Char(name, value) },
                    "digit" => f with { Digit = Char(name, value) },
                    "pattern-separator" => f with { PatternSeparator = Char(name, value) },
                    _ => f,
                };
            }
            if (!IsZeroDigit(f.ZeroDigit))
            {
                throw new XdmException(invalidCode, $"Decimal format zero-digit '{char.ConvertFromUtf32(f.ZeroDigit)}' is not a digit with numeric value zero");
            }
            var seen = new HashSet<int>();
            foreach (int cp in new[] { f.DecimalSeparator, f.GroupingSeparator, f.ExponentChar, f.Percent, f.PerMille, f.Digit, f.PatternSeparator })
            {
                if (!seen.Add(cp) || (cp >= f.ZeroDigit && cp <= f.ZeroDigit + 9))
                {
                    throw new XdmException(conflictCode, $"Decimal format character '{char.ConvertFromUtf32(cp)}' is used for more than one purpose");
                }
            }
            return f;
        }

        private static bool IsZeroDigit(int cp)
        {
            string s = char.ConvertFromUtf32(cp);
            return CharUnicodeInfo.GetUnicodeCategory(s, 0) == UnicodeCategory.DecimalDigitNumber && CharUnicodeInfo.GetDecimalDigitValue(s, 0) == 0;
        }
    }

    /// <summary>The decimal formats in a static context: the unnamed one and the named ones, with the namespaces to resolve names.</summary>
    internal sealed class DecimalFormatTable(XPathDecimalFormat unnamed, IReadOnlyDictionary<QName, XPathDecimalFormat> named, System.Xml.IXmlNamespaceResolver? namespaces)
    {
        public static readonly DecimalFormatTable Empty = new(XPathDecimalFormat.Default, new Dictionary<QName, XPathDecimalFormat>(), null);

        public XPathDecimalFormat Unnamed => unnamed;

        /// <summary>The decimal format with this name (an EQName or lexical QName); FODF1280 if there is none.</summary>
        public XPathDecimalFormat Resolve(string? name)
        {
            if (name == null)
            {
                return unnamed;
            }
            string trimmed = name.Trim();
            string ns = "", local;
            if (trimmed.StartsWith("Q{", System.StringComparison.Ordinal) && trimmed.IndexOf('}') is int close and > 0)
            {
                ns = trimmed[2..close];
                local = trimmed[(close + 1)..];
            }
            else
            {
                int colon = trimmed.IndexOf(':');
                local = colon >= 0 ? trimmed[(colon + 1)..] : trimmed;
                if (colon >= 0)
                {
                    string prefix = trimmed[..colon];
                    ns = (IsNCName(prefix) ? namespaces?.LookupNamespace(prefix) : null)
                        ?? throw new XdmException("FODF1280", $"Decimal format name '{name}': prefix {prefix} is not declared");
                }
            }
            if (!IsNCName(local))
            {
                throw new XdmException("FODF1280", $"'{name}' is not a valid decimal format name");
            }
            return named.TryGetValue(new QName(ns, local), out XPathDecimalFormat? f) ? f : throw new XdmException("FODF1280", $"No decimal format named '{name}'");
        }

        private static bool IsNCName(string s)
        {
            if (s.Length == 0)
            {
                return false;
            }
            try
            {
                System.Xml.XmlConvert.VerifyNCName(s);
                return true;
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }
        }
    }

    /// <summary>fn:format-number (F&amp;O 3.1 §4.7).</summary>
    internal static class NumberFormatter
    {
        private sealed class SubPicture
        {
            public string Prefix = "", Suffix = "";
            public List<int> IntegerGrouping = new(), FractionGrouping = new();
            public bool RegularIntegerGrouping;
            public int MinInteger, MinFraction, MaxFraction, MinExponent, ScalingFactor;
            public bool HasExponent, Percent, PerMille, HasDecimalSeparator;
        }

        // Xsl3 (P4): parsed pictures, by picture and decimal format (pictures are nearly always literals, so a
        // format-number in a loop would otherwise parse the same one for every number). Format never changes them.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<XPathDecimalFormat, System.Collections.Concurrent.ConcurrentDictionary<string, (SubPicture, SubPicture?)>> s_pictures = new();

        public static string Format(AtomicValue? value, string picture, XPathDecimalFormat df) => Format(value, picture, df, shortPath: true);

        /// <summary>Format, with or without the long-arithmetic path (the tests compare the two).</summary>
        internal static string Format(AtomicValue? value, string picture, XPathDecimalFormat df, bool shortPath)
        {
            var pictures = s_pictures.GetValue(df, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, (SubPicture, SubPicture?)>(StringComparer.Ordinal));
            if (!pictures.TryGetValue(picture, out var parsed))
            {
                string[] parts = Split(picture, df);
                parsed = (Parse(parts[0], df, picture), parts.Length > 1 ? Parse(parts[1], df, picture) : null);
                if (pictures.Count < 256)
                {
                    pictures[picture] = parsed;
                }
            }
            var (positive, negative) = parsed;

            if (value == null || value is DoubleValue { Double: double.NaN } || value is FloatValue { Float: float.NaN })
            {
                return df.NaN;
            }

            bool isNegative = value switch
            {
                DoubleValue d => d.Double < 0 || (d.Double == 0 && double.IsNegative(d.Double)),
                FloatValue f => f.Float < 0 || (f.Float == 0 && float.IsNegative(f.Float)),
                DecimalValue d => d.Decimal < 0,
                IntegerValue i => i.Integer.Sign < 0,
                _ => false,
            };
            SubPicture pic = isNegative && negative != null ? negative : positive;
            string prefix = pic.Prefix, suffix = pic.Suffix;
            if (isNegative && negative == null)
            {
                prefix = char.ConvertFromUtf32(df.MinusSign) + prefix;
            }

            // Percent and per-mille multiply in the value's own type, so a double can overflow to infinity.
            int scale = pic.Percent ? 100 : pic.PerMille ? 1000 : 1;
            if (scale != 1)
            {
                value = value switch
                {
                    DoubleValue d => new DoubleValue(d.Double * scale),
                    FloatValue f => new FloatValue(f.Float * scale),
                    _ => value,
                };
            }
            bool infinite = value is DoubleValue { Double: double.PositiveInfinity or double.NegativeInfinity } or FloatValue { Float: float.PositiveInfinity or float.NegativeInfinity };
            if (infinite)
            {
                return prefix + df.Infinity + suffix;
            }

            if (shortPath && scale == 1 && !pic.HasExponent && FormatShort(value, pic, prefix, suffix, df) is string fast)
            {
                return fast;
            }

            Digits number = Digits.From(value).Abs();
            if (scale != 1 && value is DecimalValue or IntegerValue)
            {
                number = number.Shift(pic.Percent ? 2 : 3);
            }

            int exponent = 0;
            if (pic.HasExponent && !number.IsZero)
            {
                // The mantissa gets as many integer digits as the integer part has mandatory digits (the scaling
                // factor); with none, it is below 1. Rounding may carry it up a digit: that is not renormalised.
                exponent = number.Magnitude + 1 - pic.ScalingFactor;
                number = number.Shift(-exponent);
            }

            number = number.Round(pic.MaxFraction);
            string intDigits = number.IntegerPart();
            string fracDigits = number.FractionPart();
            if (intDigits == "0") intDigits = "";
            intDigits = intDigits.PadLeft(pic.MinInteger, '0');
            fracDigits = fracDigits.TrimEnd('0');
            if (fracDigits.Length < pic.MinFraction) fracDigits = fracDigits.PadRight(pic.MinFraction, '0');
            if (intDigits.Length == 0 && fracDigits.Length == 0)
            {
                // Zero with no mandatory digits shows one digit: in the fraction when the picture has only fractional digits.
                if (pic.MaxFraction > 0 && pic.MinInteger == 0) fracDigits = "0";
                else intDigits = "0";
            }

            var sb = new StringBuilder();
            sb.Append(prefix);
            sb.Append(Group(intDigits, pic.IntegerGrouping, pic.RegularIntegerGrouping, df, fromRight: true));
            if (fracDigits.Length > 0)
            {
                sb.Append(char.ConvertFromUtf32(df.DecimalSeparator));
                sb.Append(Group(fracDigits, pic.FractionGrouping, false, df, fromRight: false));
            }
            if (pic.HasExponent)
            {
                sb.Append(df.ExponentSeparator);
                if (exponent < 0) sb.Append(char.ConvertFromUtf32(df.MinusSign));
                sb.Append(MapDigits(System.Math.Abs(exponent).ToString(CultureInfo.InvariantCulture).PadLeft(pic.MinExponent, '0'), df));
            }
            sb.Append(suffix);
            return sb.ToString();
        }

        private static readonly long[] s_pow10 =
        {
            1, 10, 100, 1_000, 10_000, 100_000, 1_000_000, 10_000_000, 100_000_000, 1_000_000_000, 10_000_000_000,
            100_000_000_000, 1_000_000_000_000, 10_000_000_000_000, 100_000_000_000_000, 1_000_000_000_000_000,
            10_000_000_000_000_000, 100_000_000_000_000_000, 1_000_000_000_000_000_000,
        };

        /// <summary>
        /// Xsl3 (P4): Format's steps in long arithmetic, for a double (by its shortest round-trip digits, at most 17)
        /// or an integer that fits a long, a picture with no exponent, percent, per-mille or fraction grouping,
        /// and symbols in the BMP. The digits and the rounding (half to even) are Format's; null when it doesn't apply.
        /// </summary>
        private static string? FormatShort(AtomicValue value, SubPicture pic, string prefix, string suffix, XPathDecimalFormat df)
        {
            long digits;
            int k;
            switch (value)
            {
                case DoubleValue dv when XPathConvert.TryShortDecimal(dv.Double, out digits, out k) || TryRoundTripDigits(dv.Double, out digits, out k):
                    break;
                case IntegerValue iv when iv.Integer >= long.MinValue + 1 && iv.Integer <= long.MaxValue:
                    digits = (long)iv.Integer;
                    k = 0;
                    break;
                default:
                    return null;
            }
            if (pic.FractionGrouping.Count > 0 || pic.MaxFraction > 18 || prefix.Length + suffix.Length > 32
                || df.ZeroDigit > char.MaxValue || df.GroupingSeparator > char.MaxValue || df.DecimalSeparator > char.MaxValue)
            {
                return null;
            }
            digits = System.Math.Abs(digits);
            if (k < 0)
            {
                // digits × 10^-k, an integer, if it fits.
                if (-k > 18 || digits > long.MaxValue / s_pow10[-k])
                {
                    return null;
                }
                digits *= s_pow10[-k];
                k = 0;
            }
            if (k > pic.MaxFraction)
            {
                int drop = k - pic.MaxFraction;
                long q = 0, r = digits, divisor = 0;
                if (drop <= 18)
                {
                    divisor = s_pow10[drop];
                    q = System.Math.DivRem(digits, divisor, out r);
                }
                // Beyond 18 places the digits (below 10^18) are under half the divisor: q stays 0.
                if (divisor != 0 && (r > divisor - r || (r == divisor - r && (q & 1) == 1)))
                {
                    q++;
                }
                digits = q;
                k = pic.MaxFraction;
            }
            if (k > 18)
            {
                return null;
            }
            long integer = System.Math.DivRem(digits, s_pow10[k], out long fraction);

            Span<char> intBuf = stackalloc char[20];
            int intLen = 0;
            if (integer != 0)
            {
                integer.TryFormat(intBuf, out intLen, default, CultureInfo.InvariantCulture);
            }
            Span<char> fracBuf = stackalloc char[20];
            int fracLen = k;
            for (int i = k - 1; i >= 0; i--)
            {
                fracBuf[i] = (char)('0' + fraction % 10);
                fraction /= 10;
            }
            while (fracLen > 0 && fracBuf[fracLen - 1] == '0') fracLen--;
            while (fracLen < pic.MinFraction) fracBuf[fracLen++] = '0';
            int intPad = System.Math.Max(0, pic.MinInteger - intLen);
            bool zeroInFraction = false;
            if (intLen + intPad == 0 && fracLen == 0)
            {
                // Zero with no mandatory digits shows one digit: in the fraction when the picture has only fractional digits.
                if (pic.MaxFraction > 0 && pic.MinInteger == 0) zeroInFraction = true;
                else intPad = 1;
            }
            if (zeroInFraction)
            {
                fracBuf[fracLen++] = '0';
            }

            int intDigits = intPad + intLen;
            List<int> positions = pic.IntegerGrouping;
            int g = positions.Count == 0 ? 0 : positions.Min();
            Span<char> buf = stackalloc char[prefix.Length + suffix.Length + 2 * intDigits + fracLen + 2];
            int n = 0;
            prefix.AsSpan().CopyTo(buf);
            n += prefix.Length;
            char zero = (char)df.ZeroDigit;
            for (int i = 0; i < intDigits; i++)
            {
                char c = i < intPad ? '0' : intBuf[i - intPad];
                buf[n++] = (char)(zero + (c - '0'));
                int after = intDigits - 1 - i;
                if (g != 0 && after > 0 && (pic.RegularIntegerGrouping ? after % g == 0 : positions.Contains(after)))
                {
                    buf[n++] = (char)df.GroupingSeparator;
                }
            }
            if (fracLen > 0)
            {
                buf[n++] = (char)df.DecimalSeparator;
                for (int i = 0; i < fracLen; i++)
                {
                    buf[n++] = (char)(zero + (fracBuf[i] - '0'));
                }
            }
            suffix.AsSpan().CopyTo(buf[n..]);
            n += suffix.Length;
            return new string(buf[..n]);
        }

        /// <summary>
        /// The digits of a finite double's shortest round-trip form ("R", as Digits.From takes them): value = digits ×
        /// 10^-k, the digits (at most 17) in a long.
        /// </summary>
        private static bool TryRoundTripDigits(double value, out long digits, out int k)
        {
            digits = 0;
            k = 0;
            Span<char> r = stackalloc char[32];
            if (!double.IsFinite(value) || !value.TryFormat(r, out int length, "R", CultureInfo.InvariantCulture))
            {
                return false;
            }
            int i = r[0] == '-' ? 1 : 0, count = 0;
            bool point = false;
            for (; i < length && r[i] != 'E'; i++)
            {
                if (r[i] == '.')
                {
                    point = true;
                    continue;
                }
                if (++count > 18)
                {
                    return false;
                }
                digits = digits * 10 + (r[i] - '0');
                if (point) k++;
            }
            if (i < length)
            {
                k -= int.Parse(r[(i + 1)..length], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            }
            return true;
        }

        private static string[] Split(string picture, XPathDecimalFormat df)
        {
            string sep = char.ConvertFromUtf32(df.PatternSeparator);
            string[] parts = picture.Split(sep);
            if (parts.Length > 2)
            {
                throw new XdmException("FODF1310", $"Picture '{picture}' has more than two sub-pictures");
            }
            return parts;
        }

        private static bool IsDigitFamily(int cp, XPathDecimalFormat df) => cp >= df.ZeroDigit && cp <= df.ZeroDigit + 9;

        private static string MapDigits(string ascii, XPathDecimalFormat df) =>
            string.Concat(ascii.Select(c => char.ConvertFromUtf32(df.ZeroDigit + (c - '0'))));

        private static SubPicture Parse(string sub, XPathDecimalFormat df, string picture)
        {
            XdmException Bad(string why) => new("FODF1310", $"Invalid picture '{picture}': {why}");
            int[] cps = CodePoints.Of(sub).ToArray();
            bool IsMantissaChar(int cp) => cp == df.Digit || IsDigitFamily(cp, df) || cp == df.DecimalSeparator || cp == df.GroupingSeparator;

            // The exponent separator is active only between the mantissa and a following mandatory digit.
            int exponentAt = -1;
            for (int i = 1; i < cps.Length - 1; i++)
            {
                if (cps[i] == df.ExponentChar && df.ExponentSeparator.Length == 1 && (IsDigitFamily(cps[i - 1], df) || cps[i - 1] == df.Digit || cps[i - 1] == df.DecimalSeparator) && IsDigitFamily(cps[i + 1], df))
                {
                    exponentAt = i;
                    break;
                }
            }

            int first = System.Array.FindIndex(cps, IsMantissaChar);
            int last = exponentAt >= 0 ? exponentAt - 1 : System.Array.FindLastIndex(cps, IsMantissaChar);
            if (first < 0 || !cps[first..(last + 1)].Any(cp => cp == df.Digit || IsDigitFamily(cp, df)))
            {
                throw Bad("no digit");
            }
            var p = new SubPicture
            {
                Prefix = string.Concat(cps[..first].Select(char.ConvertFromUtf32)),
            };
            int suffixStart;
            if (exponentAt >= 0)
            {
                int k = exponentAt + 1;
                while (k < cps.Length && IsDigitFamily(cps[k], df))
                {
                    p.MinExponent++;
                    k++;
                }
                p.HasExponent = true;
                suffixStart = k;
            }
            else
            {
                suffixStart = last + 1;
            }
            p.Suffix = string.Concat(cps[suffixStart..].Select(char.ConvertFromUtf32));

            foreach (int cp in cps)
            {
                if (cp == df.Percent) { if (p.Percent || p.PerMille) throw Bad("more than one percent or per-mille sign"); p.Percent = true; }
                if (cp == df.PerMille) { if (p.Percent || p.PerMille) throw Bad("more than one percent or per-mille sign"); p.PerMille = true; }
            }
            // Active characters may not appear in the prefix or suffix.
            foreach (int cp in CodePoints.Of(p.Prefix + p.Suffix))
            {
                if (IsMantissaChar(cp)) throw Bad("a passive character between active characters");
            }
            if (p.HasExponent && (p.Percent || p.PerMille)) throw Bad("an exponent with a percent or per-mille sign");

            int[] mantissa = cps[first..(last + 1)];
            int dot = System.Array.IndexOf(mantissa, df.DecimalSeparator);
            if (dot >= 0 && System.Array.IndexOf(mantissa, df.DecimalSeparator, dot + 1) >= 0) throw Bad("more than one decimal separator");
            p.HasDecimalSeparator = dot >= 0;
            int[] integer = dot >= 0 ? mantissa[..dot] : mantissa;
            int[] fraction = dot >= 0 ? mantissa[(dot + 1)..] : System.Array.Empty<int>();

            foreach (int cp in mantissa)
            {
                if (!IsMantissaChar(cp)) throw Bad("a passive character inside the number");
            }

            // Integer part: optional digits before mandatory ones; grouping separators between digits.
            bool seenMandatory = false;
            int digitsFromRight = 0;
            for (int i = integer.Length - 1; i >= 0; i--)
            {
                int cp = integer[i];
                if (cp == df.GroupingSeparator)
                {
                    if (i == integer.Length - 1 && dot >= 0) throw Bad("a grouping separator next to the decimal separator");
                    if (i == integer.Length - 1) throw Bad("a grouping separator at the end of the integer part");
                    if (i > 0 && integer[i - 1] == df.GroupingSeparator) throw Bad("adjacent grouping separators");
                    p.IntegerGrouping.Add(digitsFromRight);
                    continue;
                }
                digitsFromRight++;
            }
            foreach (int cp in integer)
            {
                if (IsDigitFamily(cp, df))
                {
                    seenMandatory = true;
                    p.MinInteger++;
                }
                else if (cp == df.Digit && seenMandatory)
                {
                    throw Bad("an optional digit after a mandatory digit in the integer part");
                }
            }
            if (p.IntegerGrouping.Count > 0)
            {
                // Regular: a separator at every multiple of the first position, within the integer part's digits.
                int g = p.IntegerGrouping.Min();
                var sorted = p.IntegerGrouping.OrderBy(x => x).ToList();
                p.RegularIntegerGrouping = g > 0 && sorted.All(pos => pos % g == 0) &&
                    Enumerable.Range(1, System.Math.Max(0, (digitsFromRight - 1) / g)).All(k => sorted.Contains(k * g));
            }
            p.ScalingFactor = p.MinInteger;
            bool optionalIntegerDigit = integer.Contains(df.Digit);

            // Fraction part: mandatory digits before optional ones.
            bool seenOptional = false;
            int fracDigits = 0;
            for (int i = 0; i < fraction.Length; i++)
            {
                int cp = fraction[i];
                if (cp == df.GroupingSeparator)
                {
                    if (i == 0) throw Bad("a grouping separator next to the decimal separator");
                    if (i + 1 < fraction.Length && fraction[i + 1] == df.GroupingSeparator) throw Bad("adjacent grouping separators");
                    p.FractionGrouping.Add(fracDigits);
                    continue;
                }
                if (IsDigitFamily(cp, df))
                {
                    if (seenOptional) throw Bad("a mandatory digit after an optional digit in the fractional part");
                    p.MinFraction++;
                }
                else if (cp == df.Digit)
                {
                    seenOptional = true;
                }
                fracDigits++;
            }
            p.MaxFraction = fracDigits;
            if (fraction.Length > 0 && fraction[^1] == df.GroupingSeparator && p.FractionGrouping.Count > 0)
            {
                // trailing separator in the fraction is allowed only if followed by digits
                throw Bad("a grouping separator at the end of the fractional part");
            }

            if (p.MinInteger == 0 && p.MaxFraction == 0)
            {
                if (p.HasExponent)
                {
                    p.MinFraction = p.MaxFraction = 1;
                }
                else
                {
                    p.MinInteger = 1;
                }
            }
            if (p.HasExponent && p.MinInteger == 0 && optionalIntegerDigit)
            {
                p.MinInteger = 1;
            }
            if (p.HasExponent && p.MinInteger == 0 && p.MinFraction == 0)
            {
                p.MinFraction = 1;
            }
            return p;
        }

        private static string Group(string digits, List<int> positions, bool regular, XPathDecimalFormat df, bool fromRight)
        {
            if (positions.Count == 0)
            {
                return MapDigits(digits, df);
            }
            string sep = char.ConvertFromUtf32(df.GroupingSeparator);
            var sb = new StringBuilder();
            int g = positions.Min();
            for (int i = 0; i < digits.Length; i++)
            {
                sb.Append(char.ConvertFromUtf32(df.ZeroDigit + (digits[i] - '0')));
                int after = fromRight ? digits.Length - 1 - i : i + 1;
                if (fromRight ? after > 0 : i < digits.Length - 1)
                {
                    bool here = regular ? after % g == 0 : positions.Contains(after);
                    if (here) sb.Append(sep);
                }
            }
            return sb.ToString();
        }

        /// <summary>A non-negative decimal number as digits × 10^Exponent, exact, for rounding at any position.</summary>
        private readonly record struct Digits(BigInteger Mantissa, int Exponent)
        {
            public bool IsZero => Mantissa.IsZero;

            public static Digits From(AtomicValue v)
            {
                switch (v)
                {
                    case IntegerValue i:
                        return new Digits(i.Integer, 0);
                    case DecimalValue d:
                    {
                        int[] bits = decimal.GetBits(d.Decimal);
                        int scale = (bits[3] >> 16) & 0xFF;
                        var mantissa = new BigInteger(System.Math.Abs(d.Decimal) * Pow10Decimal(scale));
                        return new Digits(d.Decimal < 0 ? -mantissa : mantissa, -scale);
                    }
                    case DoubleValue dv when XPathConvert.TryShortDecimal(dv.Double, out long digits, out int k):
                        // Xsl3 (P4): the shortest round-trip digits, as "R" gives them, for a double of at most 15 digits.
                        return new Digits(dv.Double < 0 ? -digits : digits, -k);
                    default:
                    {
                        string r = v is FloatValue f ? f.Float.ToString("E8", CultureInfo.InvariantCulture) : ((DoubleValue)v).Double.ToString("R", CultureInfo.InvariantCulture);
                        if (v is FloatValue ff) r = ff.Float.ToString("R", CultureInfo.InvariantCulture);
                        return Parse(r);
                    }
                }
            }

            private static decimal Pow10Decimal(int n)
            {
                decimal r = 1;
                for (int i = 0; i < n; i++) r *= 10;
                return r;
            }

            private static Digits Parse(string s)
            {
                int exp = 0;
                int e = s.IndexOfAny(new[] { 'E', 'e' });
                if (e >= 0)
                {
                    exp = int.Parse(s[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                    s = s[..e];
                }
                int dot = s.IndexOf('.');
                if (dot >= 0)
                {
                    exp -= s.Length - dot - 1;
                    s = s.Remove(dot, 1);
                }
                return new Digits(BigInteger.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), exp);
            }

            public Digits Abs() => new(BigInteger.Abs(Mantissa), Exponent);

            /// <summary>Multiplies by 10^n.</summary>
            public Digits Shift(int n) => new(Mantissa, Exponent + n);

            /// <summary>The position of the leading digit: the value is in [10^m, 10^(m+1)).</summary>
            public int Magnitude => Mantissa.ToString(CultureInfo.InvariantCulture).Length - 1 + Exponent;

            /// <summary>Rounds half to even to <paramref name="fractionDigits"/> places.</summary>
            public Digits Round(int fractionDigits)
            {
                if (-Exponent <= fractionDigits)
                {
                    return this;
                }
                int drop = -Exponent - fractionDigits;
                BigInteger divisor = BigInteger.Pow(10, drop);
                BigInteger q = BigInteger.DivRem(Mantissa, divisor, out BigInteger r);
                int cmp = (r * 2).CompareTo(divisor);
                if (cmp > 0 || (cmp == 0 && !q.IsEven)) q += 1;
                return new Digits(q, -fractionDigits);
            }

            public int IntegerDigitCount => IntegerPart() == "0" ? 0 : IntegerPart().Length;

            public string IntegerPart()
            {
                string s = Mantissa.ToString(CultureInfo.InvariantCulture);
                if (Exponent >= 0) return Mantissa.IsZero ? "0" : s + new string('0', Exponent);
                int point = s.Length + Exponent;
                return point <= 0 ? "0" : s[..point];
            }

            public string FractionPart()
            {
                if (Exponent >= 0) return "";
                string s = Mantissa.ToString(CultureInfo.InvariantCulture);
                int point = s.Length + Exponent;
                return point >= 0 ? s[point..] : new string('0', -point) + s;
            }
        }
    }
}
