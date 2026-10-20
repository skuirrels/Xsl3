// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Xsl3.Runtime
{
    internal sealed class DecimalFormat
    {
        public NumberFormatInfo info;
        public char digit;
        public char zeroDigit;
        public char patternSeparator;

        internal DecimalFormat(NumberFormatInfo info, char digit, char zeroDigit, char patternSeparator)
        {
            this.info = info;
            this.digit = digit;
            this.zeroDigit = zeroDigit;
            this.patternSeparator = patternSeparator;
        }
    }

    internal sealed class DecimalFormatter
    {
        private readonly NumberFormatInfo _posFormatInfo;
        private readonly NumberFormatInfo? _negFormatInfo;
        private readonly string? _posFormat;
        private readonly string? _negFormat;
        private readonly char _zeroDigit;

        // Xsl3 (P4): a positive-number picture of the plain shape [#,]*[0,]*(.0*#*)? formats without the general
        // custom-format machinery: these are its digit counts and grouping (_simple is false for any other picture).
        private readonly bool _simple;
        private readonly int _minIntegerDigits, _minFractionDigits, _maxFractionDigits, _groupingSize;

        // These characters have special meaning for CLR and must be escaped
        // <spec>https://learn.microsoft.com/dotnet/standard/base-types/custom-numeric-format-strings</spec>
        private const string ClrSpecialChars = "0#.,%\u2030Ee\\'\";";

        // This character is used to escape literal (passive) digits '0'..'9'
        private const char EscChar = '\a';

        public DecimalFormatter(string formatPicture, DecimalFormat decimalFormat)
        {
            Debug.Assert(formatPicture != null && decimalFormat != null);
            if (formatPicture.Length == 0)
            {
                throw XsltException.Create(SR.Xslt_InvalidFormat);
            }

            _zeroDigit = decimalFormat.zeroDigit;
            _posFormatInfo = (NumberFormatInfo)decimalFormat.info.Clone();
            StringBuilder temp = new StringBuilder();

            bool integer = true;
            bool sawPattern = false, sawZeroDigit = false, sawDigit = false, sawDecimalSeparator = false;
            bool digitOrZeroDigit = false;
            char decimalSeparator = _posFormatInfo.NumberDecimalSeparator[0];
            char groupSeparator = _posFormatInfo.NumberGroupSeparator[0];
            char percentSymbol = _posFormatInfo.PercentSymbol[0];
            char perMilleSymbol = _posFormatInfo.PerMilleSymbol[0];

            int commaIndex = 0;
            int groupingSize;
            int decimalIndex = -1;
            int lastDigitIndex = -1;

            for (int i = 0; i < formatPicture.Length; i++)
            {
                char ch = formatPicture[i];

                if (ch == decimalFormat.digit)
                {
                    if (sawZeroDigit && integer)
                    {
                        throw XsltException.Create(SR.Xslt_InvalidFormat1, formatPicture);
                    }
                    lastDigitIndex = temp.Length;
                    sawDigit = digitOrZeroDigit = true;
                    temp.Append('#');
                    continue;
                }
                if (ch == decimalFormat.zeroDigit)
                {
                    if (sawDigit && !integer)
                    {
                        throw XsltException.Create(SR.Xslt_InvalidFormat2, formatPicture);
                    }
                    lastDigitIndex = temp.Length;
                    sawZeroDigit = digitOrZeroDigit = true;
                    temp.Append('0');
                    continue;
                }
                if (ch == decimalFormat.patternSeparator)
                {
                    if (!digitOrZeroDigit)
                    {
                        throw XsltException.Create(SR.Xslt_InvalidFormat8);
                    }
                    if (sawPattern)
                    {
                        throw XsltException.Create(SR.Xslt_InvalidFormat3, formatPicture);
                    }
                    sawPattern = true;

                    if (decimalIndex < 0)
                    {
                        decimalIndex = lastDigitIndex + 1;
                    }
                    groupingSize = RemoveTrailingComma(temp, commaIndex, decimalIndex);

                    if (groupingSize > 9)
                    {
                        groupingSize = 0;
                    }
                    _posFormatInfo.NumberGroupSizes = new int[] { groupingSize };
                    if (!sawDecimalSeparator)
                    {
                        _posFormatInfo.NumberDecimalDigits = 0;
                    }

                    _posFormat = temp.ToString();

                    temp.Length = 0;
                    decimalIndex = -1;
                    lastDigitIndex = -1;
                    commaIndex = 0;
                    sawDigit = sawZeroDigit = digitOrZeroDigit = false;
                    sawDecimalSeparator = false;
                    integer = true;
                    _negFormatInfo = (NumberFormatInfo)decimalFormat.info.Clone();
                    _negFormatInfo.NegativeSign = string.Empty;
                    continue;
                }
                if (ch == decimalSeparator)
                {
                    if (sawDecimalSeparator)
                    {
                        throw XsltException.Create(SR.Xslt_InvalidFormat5, formatPicture);
                    }
                    decimalIndex = temp.Length;
                    sawDecimalSeparator = true;
                    sawDigit = sawZeroDigit = integer = false;
                    temp.Append('.');
                    continue;
                }
                if (ch == groupSeparator)
                {
                    commaIndex = temp.Length;
                    lastDigitIndex = commaIndex;
                    temp.Append(',');
                    continue;
                }
                if (ch == percentSymbol)
                {
                    temp.Append('%');
                    continue;
                }
                if (ch == perMilleSymbol)
                {
                    temp.Append('\u2030');
                    continue;
                }
                if (ch == '\'')
                {
                    int pos = formatPicture.IndexOf('\'', i + 1);
                    if (pos < 0)
                    {
                        pos = formatPicture.Length - 1;
                    }
                    temp.Append(formatPicture, i, pos - i + 1);
                    i = pos;
                    continue;
                }
                // Escape literal digits with EscChar, double literal EscChar
                if (char.IsAsciiDigit(ch) || ch == EscChar)
                {
                    if (decimalFormat.zeroDigit != '0')
                    {
                        temp.Append(EscChar);
                    }
                }
                // Escape characters having special meaning for CLR
                if (ClrSpecialChars.Contains(ch))
                {
                    temp.Append('\\');
                }
                temp.Append(ch);
            }

            if (!digitOrZeroDigit)
            {
                throw XsltException.Create(SR.Xslt_InvalidFormat8);
            }
            NumberFormatInfo formatInfo = sawPattern ? _negFormatInfo! : _posFormatInfo;

            if (decimalIndex < 0)
            {
                decimalIndex = lastDigitIndex + 1;
            }
            groupingSize = RemoveTrailingComma(temp, commaIndex, decimalIndex);
            if (groupingSize > 9)
            {
                groupingSize = 0;
            }
            formatInfo.NumberGroupSizes = new int[] { groupingSize };
            if (!sawDecimalSeparator)
            {
                formatInfo.NumberDecimalDigits = 0;
            }

            if (sawPattern)
            {
                _negFormat = temp.ToString();
            }
            else
            {
                _posFormat = temp.ToString();
            }
            _simple = IsSimplePicture(_posFormat!, out _minIntegerDigits, out _minFractionDigits, out _maxFractionDigits, out bool grouped) &&
                (!grouped || _posFormatInfo.NumberGroupSizes is [> 0 and var size] && (_groupingSize = size) > 0);
        }

        /// <summary>
        /// Xsl3 (P4): whether a .NET custom format is [#,]*[0,]*(.0*#*)?, starting and ending its integer part with a
        /// digit placeholder (so that no comma scales the number), and its digit counts.
        /// </summary>
        private static bool IsSimplePicture(string format, out int minInteger, out int minFraction, out int maxFraction, out bool grouped)
        {
            minInteger = minFraction = maxFraction = 0;
            grouped = false;
            int i = 0;
            bool zeros = false;
            for (; i < format.Length && format[i] != '.'; i++)
            {
                switch (format[i])
                {
                    case '#' when !zeros: break;
                    case '0': zeros = true; minInteger++; break;
                    case ',' when i > 0: grouped = true; break;
                    default: return false;
                }
            }
            if (i == 0 || format[i - 1] == ',')
            {
                return false;
            }
            if (i < format.Length)
            {
                bool hashes = false;
                for (i++; i < format.Length; i++)
                {
                    switch (format[i])
                    {
                        case '0' when !hashes: minFraction++; maxFraction++; break;
                        case '#': hashes = true; maxFraction++; break;
                        default: return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Xsl3 (P4): formats a positive number with a simple picture as value.ToString(_posFormat, _posFormatInfo)
        /// would: the number's first 15 significant digits correctly rounded (the precision .NET's custom formats use),
        /// rounded again half up at the last fraction digit, then grouped and padded. False when the number is outside
        /// [1e-5, 1e15) or the result rounds to zero, which the general path formats.
        /// </summary>
        private bool TryFormatSimple(double value, out string result)
        {
            result = "";
            if (!TryGetSignificantDigits(value, out ulong q, out int scale))
            {
                return false;
            }
            // digits d[0..count) of 0.d0d1... x 10^scale
            Span<byte> d = stackalloc byte[16];
            int count = 15;
            if (q == 1_000_000_000_000_000)
            {
                // Rounding carried into a 16th digit.
                q /= 10;
                scale++;
            }
            for (int k = 14; k >= 0; k--)
            {
                d[k] = (byte)(q % 10);
                q /= 10;
            }
            while (count > 0 && d[count - 1] == 0)
            {
                count--;
            }

            // Round at the last fraction digit: as .NET's custom formats do, half up on the 15 digits.
            int pos = scale + _maxFractionDigits;
            if (pos < 0)
            {
                return false;
            }
            if (pos < count)
            {
                bool up = d[pos] >= 5;
                count = pos;
                if (up)
                {
                    while (count > 0 && d[count - 1] == 9)
                    {
                        count--;
                    }
                    if (count == 0)
                    {
                        d[0] = 1;
                        count = 1;
                        scale++;
                    }
                    else
                    {
                        d[count - 1]++;
                    }
                }
                while (count > 0 && d[count - 1] == 0)
                {
                    count--;
                }
            }
            if (count == 0)
            {
                return false;
            }

            int integerDigits = Math.Max(scale, 0);
            int shown = Math.Max(integerDigits, _minIntegerDigits);
            int fraction = _maxFractionDigits;
            while (fraction > _minFractionDigits && (scale + fraction - 1 >= count || scale + fraction - 1 < 0 || d[scale + fraction - 1] == 0))
            {
                fraction--;
            }
            string group = _posFormatInfo.NumberGroupSeparator, point = _posFormatInfo.NumberDecimalSeparator;
            int groups = _groupingSize > 0 && shown > 0 ? (shown - 1) / _groupingSize : 0;
            int length = shown + groups * group.Length + (fraction > 0 ? point.Length + fraction : 0);
            Span<char> chars = length <= 128 ? stackalloc char[length] : new char[length];
            int at = 0;
            for (int k = 0; k < shown; k++)
            {
                // integer digit k of shown, counting the padding zeros
                int index = k - (shown - integerDigits);
                chars[at++] = index >= 0 && index < count ? (char)('0' + d[index]) : '0';
                int left = shown - k - 1;
                if (_groupingSize > 0 && left > 0 && left % _groupingSize == 0)
                {
                    group.CopyTo(chars.Slice(at));
                    at += group.Length;
                }
            }
            if (fraction > 0)
            {
                point.CopyTo(chars.Slice(at));
                at += point.Length;
                for (int k = 0; k < fraction; k++)
                {
                    int index = scale + k;
                    chars[at++] = index >= 0 && index < count ? (char)('0' + d[index]) : '0';
                }
            }
            Debug.Assert(at == length);
            result = new string(chars);
            return true;
        }

        // Xsl3 (P4): 5^0 .. 5^27 (5^27 < 2^63).
        private static readonly ulong[] s_powersOf5 = CreatePowersOf5();

        private static ulong[] CreatePowersOf5()
        {
            var powers = new ulong[28];
            powers[0] = 1;
            for (int i = 1; i < powers.Length; i++)
            {
                powers[i] = powers[i - 1] * 5;
            }
            return powers;
        }

        /// <summary>
        /// Xsl3 (P4): the first 15 significant digits of a positive double, correctly rounded (ties to even, as .NET's
        /// Dragon4 rounds them), as q in [10^14, 10^15] with value ~ q x 10^(scale - 15). Exact: the double is m x 2^e,
        /// and m x 5^k x 2^(e + k), its value times 10^k, fits in 128 bits for the k this needs. False outside [1e-5, 1e15).
        /// </summary>
        internal static bool TryGetSignificantDigits(double value, out ulong q, out int scale)
        {
            q = 0;
            scale = 0;
            if (!(value >= 1e-5 && value < 1e15))
            {
                return false;
            }
            long bits = BitConverter.DoubleToInt64Bits(value);
            ulong m = (ulong)(bits & ((1L << 52) - 1)) | (1UL << 52);
            int e = (int)((bits >> 52) & 0x7FF) - 1075;
            int p = (int)Math.Floor(Math.Log10(value));
            for (int attempt = 0; attempt < 3; attempt++)
            {
                // q = value x 10^k, rounded, where k makes it 15 digits: 10^14 <= floor < 10^15.
                int k = 14 - p;
                if (k < 0 || k >= s_powersOf5.Length)
                {
                    return false;
                }
                UInt128 n = (UInt128)m * s_powersOf5[k];
                int shift = e + k;
                UInt128 floor, rest = 0, half = 0;
                if (shift >= 0)
                {
                    if (shift > 10)
                    {
                        return false;
                    }
                    floor = n << shift;
                }
                else
                {
                    int s = -shift;
                    if (s >= 127)
                    {
                        return false;
                    }
                    floor = n >> s;
                    rest = n & ((UInt128.One << s) - 1);
                    half = UInt128.One << (s - 1);
                }
                if (floor < 100_000_000_000_000)
                {
                    p--;
                    continue;
                }
                if (floor >= 1_000_000_000_000_000)
                {
                    p++;
                    continue;
                }
                if (rest > half || (rest == half && half != 0 && (floor & 1) == 1))
                {
                    floor++;
                }
                q = (ulong)floor;
                scale = 15 - k;
                return true;
            }
            return false;
        }

        private static int RemoveTrailingComma(StringBuilder builder, int commaIndex, int decimalIndex)
        {
            if (commaIndex > 0 && commaIndex == (decimalIndex - 1))
            {
                builder.Remove(decimalIndex - 1, 1);
            }
            else if (decimalIndex > commaIndex)
            {
                return decimalIndex - commaIndex - 1;
            }
            return 0;
        }

        public string Format(double value)
        {
            NumberFormatInfo formatInfo;
            string? subPicture;

            if (value < 0 && _negFormatInfo != null)
            {
                formatInfo = _negFormatInfo;
                subPicture = _negFormat;
            }
            else
            {
                formatInfo = _posFormatInfo;
                subPicture = _posFormat;
            }

            string result = _simple && formatInfo == _posFormatInfo && TryFormatSimple(value, out string simple)
                ? simple
                : value.ToString(subPicture, formatInfo);

            if (_zeroDigit != '0')
            {
                StringBuilder builder = new StringBuilder(result.Length);
                int shift = _zeroDigit - '0';
                for (int i = 0; i < result.Length; i++)
                {
                    char ch = result[i];
                    if (char.IsAsciiDigit(ch))
                    {
                        ch += (char)shift;
                    }
                    else if (ch == EscChar)
                    {
                        // This is an escaped literal digit or EscChar, thus unescape it. We make use
                        // of the fact that no extra EscChar could be inserted by value.ToString().
                        Debug.Assert(i + 1 < result.Length);
                        ch = result[++i];
                        Debug.Assert(char.IsAsciiDigit(ch) || ch == EscChar);
                    }
                    builder.Append(ch);
                }
                result = builder.ToString();
            }
            return result;
        }

        /// <summary>Xsl3: the general path of Format for a positive number, which the simple path must agree with (for tests).</summary>
        internal string FormatGeneral(double value) => value.ToString(_posFormat, _posFormatInfo);

        /// <summary>Xsl3: whether the picture takes the simple path (for tests).</summary>
        internal bool IsSimple => _simple;

        public static string Format(double value, string formatPicture, DecimalFormat decimalFormat)
        {
            return new DecimalFormatter(formatPicture, decimalFormat).Format(value);
        }
    }
}
