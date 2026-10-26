// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Globalization;
using System.Numerics;
using System.Text;

namespace Xsl3.Xdm
{
    /// <summary>Lexical-space helpers: whitespace facets, strict number parsing, canonical number forms.</summary>
    internal static class Lexical
    {
        private static bool IsXmlSpace(char c) => c is ' ' or '\t' or '\n' or '\r';

        /// <summary>Applies a whiteSpace facet (XSD 1.0 §4.3.6).</summary>
        public static string ApplyWhitespace(string s, WhitespaceFacet facet)
        {
            switch (facet)
            {
                case WhitespaceFacet.Preserve:
                    return s;
                case WhitespaceFacet.Replace:
                    return s.IndexOfAny(new[] { '\t', '\n', '\r' }) < 0 ? s : s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
                default:
                    return Collapse(s);
            }
        }

        public static string Collapse(string s)
        {
            int start = 0, end = s.Length;
            while (start < end && IsXmlSpace(s[start])) start++;
            while (end > start && IsXmlSpace(s[end - 1])) end--;
            bool clean = true;
            for (int i = start; i < end; i++)
            {
                if (s[i] != ' ' && IsXmlSpace(s[i]) || (s[i] == ' ' && i + 1 < end && s[i + 1] == ' '))
                {
                    clean = false;
                    break;
                }
            }
            if (clean)
            {
                return start == 0 && end == s.Length ? s : s[start..end];
            }
            var sb = new StringBuilder(end - start);
            bool space = false;
            for (int i = start; i < end; i++)
            {
                if (IsXmlSpace(s[i]))
                {
                    space = true;
                    continue;
                }
                if (space)
                {
                    sb.Append(' ');
                    space = false;
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ parsing (input already collapsed)

        /// <summary>xs:integer lexical space: <c>(\+|-)?[0-9]+</c>.</summary>
        public static bool TryParseInteger(string s, out BigInteger value)
        {
            value = default;
            int i = s.Length > 0 && (s[0] == '+' || s[0] == '-') ? 1 : 0;
            if (i == s.Length)
            {
                return false;
            }
            // XML integer digits are ASCII. Accumulate values that fit the native
            // unsigned magnitude without invoking the general-purpose number parser;
            // retain arbitrary precision once that magnitude overflows.
            ulong magnitude = 0;
            bool wide = false;
            for (int j = i; j < s.Length; j++)
            {
                uint digit = (uint)(s[j] - '0');
                if (digit > 9) return false;
                if (!wide)
                {
                    if (magnitude > (ulong.MaxValue - digit) / 10) wide = true;
                    else magnitude = magnitude * 10 + digit;
                }
            }
            if (wide)
                value = BigInteger.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            else
            {
                value = new BigInteger(magnitude);
                if (s[0] == '-') value = -value;
            }
            return true;
        }

        /// <summary>xs:decimal lexical space: <c>(\+|-)?([0-9]+(\.[0-9]*)?|\.[0-9]+)</c>. Throws FOCA0001 on overflow.</summary>
        public static bool TryParseDecimal(string s, out decimal value)
        {
            value = 0;
            if (!IsDecimalLexical(s))
            {
                return false;
            }
            try
            {
                value = decimal.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                return true;
            }
            catch (OverflowException)
            {
                throw Err.FOCA0001(s);
            }
        }

        private static bool IsDecimalLexical(string s)
        {
            int i = s.Length > 0 && (s[0] == '+' || s[0] == '-') ? 1 : 0;
            int digits = 0;
            bool point = false;
            for (; i < s.Length; i++)
            {
                if (char.IsAsciiDigit(s[i]))
                {
                    digits++;
                }
                else if (s[i] == '.' && !point)
                {
                    point = true;
                }
                else
                {
                    return false;
                }
            }
            return digits > 0;
        }

        /// <summary>
        /// The xs:anyURI lexical space, as far as XSD 1.0 constrains it: '%' must start an escape of two hex
        /// digits, and a ':' before any '/', '?' or '#' must end a well-formed scheme.
        /// </summary>
        public static bool IsAnyUri(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '%' && !(i + 2 < s.Length && char.IsAsciiHexDigit(s[i + 1]) && char.IsAsciiHexDigit(s[i + 2])))
                {
                    return false;
                }
            }
            int colon = s.IndexOf(':');
            int delimiter = s.IndexOfAny(new[] { '/', '?', '#' });
            if (colon >= 0 && (delimiter < 0 || colon < delimiter))
            {
                if (colon == 0 || !char.IsAsciiLetter(s[0]))
                {
                    return false;
                }
                for (int i = 1; i < colon; i++)
                {
                    if (!(char.IsAsciiLetterOrDigit(s[i]) || s[i] is '+' or '-' or '.'))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>xs:double / xs:float lexical space (XSD 1.0: "+INF" is an XSD 1.1 addition, not accepted).</summary>
        public static bool TryParseDouble(string s, out double value)
        {
            switch (s)
            {
                case "INF": value = double.PositiveInfinity; return true;
                case "-INF": value = double.NegativeInfinity; return true;
                case "NaN": value = double.NaN; return true;
            }
            value = 0;
            if (!IsFloatingLexical(s))
            {
                return false;
            }
            value = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
            return true;
        }

        public static bool TryParseFloat(string s, out float value)
        {
            switch (s)
            {
                case "INF": value = float.PositiveInfinity; return true;
                case "-INF": value = float.NegativeInfinity; return true;
                case "NaN": value = float.NaN; return true;
            }
            value = 0;
            if (!IsFloatingLexical(s))
            {
                return false;
            }
            value = float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
            return true;
        }

        private static bool IsFloatingLexical(string s)
        {
            int e = s.IndexOfAny(new[] { 'e', 'E' });
            if (e < 0)
            {
                return IsDecimalLexical(s);
            }
            if (!IsDecimalLexical(s[..e]))
            {
                return false;
            }
            string exp = s[(e + 1)..];
            int i = exp.Length > 0 && (exp[0] == '+' || exp[0] == '-') ? 1 : 0;
            if (i == exp.Length)
            {
                return false;
            }
            for (; i < exp.Length; i++)
            {
                if (!char.IsAsciiDigit(exp[i]))
                {
                    return false;
                }
            }
            return true;
        }

        // ------------------------------------------------------------ canonical forms

        /// <summary>xs:decimal canonical form: no exponent, no trailing zeros, no point for integral values.</summary>
        public static string DecimalToString(decimal d) =>
            d.ToString("0.############################", CultureInfo.InvariantCulture);

        public static string DoubleToString(double d) => FloatingToString(d, d.ToString("R", CultureInfo.InvariantCulture));

        public static string FloatToString(float f) => FloatingToString(f, f.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>
        /// Casting xs:double/xs:float to xs:string (F&amp;O 3.1 §19.1.2.1): positional notation for
        /// 1e-6 &lt;= |v| &lt; 1e6, otherwise a mantissa with one digit before the point and an exponent
        /// ("1.0E7"). Digits are the shortest that round-trip.
        /// </summary>
        private static string FloatingToString(double value, string roundTrip)
        {
            if (double.IsNaN(value)) return "NaN";
            if (double.IsPositiveInfinity(value)) return "INF";
            if (double.IsNegativeInfinity(value)) return "-INF";
            if (value == 0) return double.IsNegative(value) ? "-0" : "0";

            // Decompose the round-trip string into significant digits and the power of ten of the first one.
            string s = roundTrip;
            bool negative = s[0] == '-';
            if (negative) s = s[1..];
            int exponent = 0;
            int ePos = s.IndexOfAny(new[] { 'E', 'e' });
            if (ePos >= 0)
            {
                exponent = int.Parse(s[(ePos + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                s = s[..ePos];
            }
            int point = s.IndexOf('.');
            string intPart = point < 0 ? s : s[..point];
            string fracPart = point < 0 ? "" : s[(point + 1)..];
            string all = intPart + fracPart;
            int leadingZeros = 0;
            while (leadingZeros < all.Length - 1 && all[leadingZeros] == '0') leadingZeros++;
            string digits = all[leadingZeros..].TrimEnd('0');
            if (digits.Length == 0) digits = "0";
            // value = 0.d1d2… × 10^(pow+1), i.e. d1 is at 10^pow
            int pow = exponent + intPart.Length - 1 - leadingZeros;

            var sb = new StringBuilder();
            if (negative) sb.Append('-');
            double abs = Math.Abs(value);
            if (abs >= 1e-6 && abs < 1e6)
            {
                if (pow >= 0)
                {
                    string whole = digits.Length > pow + 1 ? digits[..(pow + 1)] : digits.PadRight(pow + 1, '0');
                    string frac = digits.Length > pow + 1 ? digits[(pow + 1)..] : "";
                    sb.Append(whole);
                    if (frac.Length > 0) sb.Append('.').Append(frac);
                }
                else
                {
                    sb.Append("0.").Append('0', -pow - 1).Append(digits);
                }
            }
            else
            {
                sb.Append(digits[0]).Append('.').Append(digits.Length > 1 ? digits[1..] : "0");
                sb.Append('E').Append(pow.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>xs:hexBinary canonical form: upper-case hex digits.</summary>
        public static string HexToString(byte[] bytes) => Convert.ToHexString(bytes);

        public static bool TryParseHex(string s, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (s.Length % 2 != 0)
            {
                return false;
            }
            foreach (char c in s)
            {
                if (!char.IsAsciiHexDigit(c))
                {
                    return false;
                }
            }
            bytes = Convert.FromHexString(s);
            return true;
        }

        /// <summary>
        /// xs:base64Binary (XSD 1.0 §3.2.16): groups of four base64 characters, each optionally followed by a
        /// single space, with '=' padding only in the last group and constrained final characters.
        /// </summary>
        public static bool TryParseBase64(string s, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            var compact = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == ' ')
                {
                    continue;
                }
                if (!(char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '='))
                {
                    return false;
                }
                compact.Append(c);
            }
            string b = compact.ToString();
            if (b.Length % 4 != 0)
            {
                return false;
            }
            int pad = b.EndsWith("==", StringComparison.Ordinal) ? 2 : b.EndsWith('=') ? 1 : 0;
            if (b.IndexOf('=') is int first && first >= 0 && first < b.Length - pad)
            {
                return false;
            }
            // The last data character must have zero low bits for the padding (B16 / B04 in the XSD grammar).
            if (pad == 2 && "AQgw".IndexOf(b[^3]) < 0) return false;
            if (pad == 1 && "AEIMQUYcgkosw048".IndexOf(b[^2]) < 0) return false;
            try
            {
                bytes = Convert.FromBase64String(b);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
