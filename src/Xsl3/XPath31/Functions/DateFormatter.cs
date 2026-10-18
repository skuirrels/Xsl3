// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    /// <summary>fn:format-dateTime, fn:format-date and fn:format-time (F&amp;O 3.1 §9.8).</summary>
    internal static class DateFormatter
    {
        private const string Error = "FOFD1340";

        private static readonly string[] s_englishMonths = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
        private static readonly string[] s_englishDays = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        private static readonly string[] s_germanMonths = { "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember" };
        private static readonly string[] s_germanDays = { "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag" };

        // Calendars F&O 3.1 §9.8.4.3 lists; only AD and ISO (the proleptic Gregorian calendar) are implemented.
        private static readonly HashSet<string> s_knownCalendars = new(System.StringComparer.Ordinal)
        {
            "AD", "AH", "AME", "AM", "AP", "AS", "BE", "CB", "CE", "CL", "CS", "EE", "FE", "ISO", "JE", "KE", "KY", "ME", "MS", "NS", "OS", "RS", "SE", "SH", "SS", "TE", "VE", "VS",
        };

        public static string Format(XsDateTime value, Primitive kind, string picture, string? language, string? calendar, string? place)
        {
            bool german = false;
            var prefix = new StringBuilder();
            if (!string.IsNullOrEmpty(language))
            {
                string lang = language.ToLowerInvariant();
                if (lang == "de" || lang.StartsWith("de-", System.StringComparison.Ordinal))
                {
                    german = true;
                }
                else if (lang != "en" && !lang.StartsWith("en-", System.StringComparison.Ordinal))
                {
                    prefix.Append("[Language: en]");
                }
            }
            if (!string.IsNullOrEmpty(calendar))
            {
                string cal = calendar.Trim();
                if (cal.StartsWith("Q{}", System.StringComparison.Ordinal))
                {
                    cal = cal[3..]; // no namespace: one of the calendars below
                }
                if (cal.StartsWith("Q{", System.StringComparison.Ordinal))
                {
                    int close = cal.IndexOf('}');
                    if (close < 0 || !IsNCName(cal[(close + 1)..]))
                    {
                        throw new XdmException(Error, $"Invalid calendar '{calendar}'");
                    }
                    prefix.Append("[Calendar: AD]");
                }
                else if (cal.Contains(':'))
                {
                    int colon = cal.IndexOf(':');
                    if (!IsNCName(cal[..colon]) || !IsNCName(cal[(colon + 1)..]))
                    {
                        throw new XdmException(Error, $"Invalid calendar '{calendar}'");
                    }
                    prefix.Append("[Calendar: AD]");
                }
                else if (!s_knownCalendars.Contains(cal))
                {
                    throw new XdmException(Error, $"Unknown calendar '{calendar}'");
                }
                else if (cal != "AD" && cal != "ISO")
                {
                    prefix.Append("[Calendar: AD]");
                }
            }

            var sb = new StringBuilder();
            sb.Append(prefix);
            int i = 0;
            while (i < picture.Length)
            {
                char c = picture[i];
                if (c == '[')
                {
                    if (i + 1 < picture.Length && picture[i + 1] == '[')
                    {
                        sb.Append('[');
                        i += 2;
                        continue;
                    }
                    int close = picture.IndexOf(']', i + 1);
                    if (close < 0)
                    {
                        throw new XdmException(Error, $"Picture '{picture}' has an unclosed variable marker");
                    }
                    string marker = new string(picture[(i + 1)..close].Where(ch => !char.IsWhiteSpace(ch)).ToArray());
                    sb.Append(Component(value, kind, marker, german, picture));
                    i = close + 1;
                    continue;
                }
                if (c == ']')
                {
                    if (i + 1 < picture.Length && picture[i + 1] == ']')
                    {
                        sb.Append(']');
                        i += 2;
                        continue;
                    }
                    throw new XdmException(Error, $"Picture '{picture}' has an unescaped ']'");
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static string Component(XsDateTime v, Primitive kind, string marker, bool german, string picture)
        {
            if (marker.Length == 0)
            {
                throw new XdmException(Error, $"Picture '{picture}' has an empty variable marker");
            }
            char component = marker[0];
            if ("YMDdFWwHhPmsfZzCE".IndexOf(component) < 0)
            {
                throw new XdmException(Error, $"Picture '{picture}': unknown component '{component}'");
            }
            bool hasDate = kind != Primitive.Time, hasTime = kind != Primitive.Date;
            if ((!hasDate && "YMDdFWwE".IndexOf(component) >= 0) || (!hasTime && "HhPmsf".IndexOf(component) >= 0))
            {
                throw new XdmException("FOFD1350", $"Component '{component}' is not available in an xs:{(kind == Primitive.Date ? "date" : "time")}");
            }

            // [component][presentation][second modifier][,width]
            string rest = marker[1..];
            int? minWidth = null, maxWidth = null;
            int comma = rest.LastIndexOf(',');
            bool hasWidth = comma >= 0;
            if (hasWidth)
            {
                (minWidth, maxWidth) = ParseWidth(rest[(comma + 1)..], picture);
                rest = rest[..comma];
            }
            char? second = null;
            if (rest.EndsWith(')'))
            {
                int open = rest.LastIndexOf('(');
                if (open < 1 || rest[open - 1] is not ('o' or 'c'))
                {
                    throw new XdmException(Error, $"Picture '{picture}': invalid modifier in [{marker}]");
                }
                second = rest[open - 1];
                rest = rest[..(open - 1)];
            }
            else if (rest.Length > 1 && rest[^1] is 'a' or 't' or 'o' or 'c' && !(rest == "Nn"))
            {
                second = rest[^1];
                rest = rest[..^1];
            }
            string presentation = rest.Length > 0 ? rest : DefaultPresentation(component);
            if (rest.Length > 0 && (rest.Contains('[') || rest.Contains(']')))
            {
                throw new XdmException(Error, $"Picture '{picture}': invalid presentation in [{marker}]");
            }
            if (rest.Length == 0 && second != null && !(second is 'o' or 'c'))
            {
                throw new XdmException(Error, $"Picture '{picture}': invalid modifier in [{marker}]");
            }

            bool isNamePresentation = presentation is "N" or "n" or "Nn";
            long year = v.Year;
            switch (component)
            {
                case 'M' when isNamePresentation:
                    return Name((german ? s_germanMonths : s_englishMonths)[v.Month - 1], presentation, minWidth, maxWidth);
                case 'F' when isNamePresentation:
                    return Name((german ? s_germanDays : s_englishDays)[DayOfWeek(v) - 1], presentation, minWidth, maxWidth);
                case 'P':
                    return Name(v.Hour < 12 ? "am" : "pm", isNamePresentation ? presentation : "n", minWidth, maxWidth);
                case 'E':
                    return Name(year > 0 ? "AD" : "BC", isNamePresentation ? (presentation == "n" ? "n" : presentation) : "N", minWidth, maxWidth);
                case 'C':
                    return Name("ISO", isNamePresentation ? presentation : "N", minWidth, maxWidth);
                case 'Z' or 'z':
                    return Timezone(v, component, rest.Length > 0 ? rest : null, second, picture);
                case 'f':
                    return FractionalSeconds(v.Second, presentation, hasWidth, minWidth, maxWidth, picture);
            }

            BigInteger number = component switch
            {
                'Y' => year < 0 ? -year : year,
                'M' => v.Month,
                'D' => v.Day,
                'd' => DayOfYear(v),
                'F' => DayOfWeek(v),
                'W' => IsoWeek(v),
                'w' => WeekOfMonth(v),
                'H' => v.Hour,
                'h' => v.Hour % 12 == 0 ? 12 : v.Hour % 12,
                'm' => v.Minute,
                _ => (int)decimal.Truncate(v.Second),
            };
            if (isNamePresentation)
            {
                presentation = DefaultPresentation(component) is "n" ? "1" : DefaultPresentation(component);
            }
            var formatter = new IntegerFormatter(presentation, Error, allowEmpty: true, modifierInPicture: false, dateModifier: second);
            if (component == 'Y')
            {
                // The year is truncated to the maximum width: given by the width modifier, else by a
                // decimal-digit-pattern of two or more digits ([Y01] is a two-digit year).
                int digits = CountDigits(presentation);
                int? max = hasWidth ? maxWidth : formatter.IsDecimal && digits >= 2 ? digits : null;
                if (max is int m && m < 30)
                {
                    number %= BigInteger.Pow(10, m);
                }
            }
            string text = formatter.Format(number);
            if (minWidth is int min)
            {
                if (formatter.IsDecimal)
                {
                    if (CountDigits(text) < min)
                    {
                        text = PadDigits(text, min, presentation);
                    }
                }
                else if (CodePoints.Length(text) < min)
                {
                    text += new string(' ', min - CodePoints.Length(text));
                }
            }
            return text;
        }

        private static string DefaultPresentation(char component) => component switch
        {
            'F' or 'P' or 'C' or 'E' => "n",
            'm' or 's' => "01",
            'Z' or 'z' => "01:01",
            _ => "1",
        };

        private static (int? Min, int? Max) ParseWidth(string width, string picture)
        {
            int dash = width.IndexOf('-');
            string minPart = dash >= 0 ? width[..dash] : width;
            string? maxPart = dash >= 0 ? width[(dash + 1)..] : null;
            int? Parse(string s)
            {
                if (s == "*")
                {
                    return null;
                }
                if (s.Length == 0 || !s.All(char.IsAsciiDigit) || !int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1)
                {
                    throw new XdmException(Error, $"Picture '{picture}': invalid width modifier ',{width}'");
                }
                return n;
            }
            int? min = Parse(minPart);
            int? max = maxPart != null ? Parse(maxPart) : null;
            if (min != null && max != null && max < min)
            {
                throw new XdmException(Error, $"Picture '{picture}': maximum width is below the minimum in ',{width}'");
            }
            return (min, max);
        }

        private static string Name(string name, string presentation, int? minWidth, int? maxWidth)
        {
            if (maxWidth is int max && name.Length > max)
            {
                // Abbreviate: the conventional three-letter form when it fits, else truncate.
                name = name[..(max >= 3 ? 3 : max)];
            }
            name = presentation switch
            {
                "N" => name.ToUpperInvariant(),
                "n" => name.ToLowerInvariant(),
                _ => name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant() : name,
            };
            if (minWidth is int min && name.Length < min)
            {
                name = name.PadRight(min);
            }
            return name;
        }

        private static int CountDigits(string s) =>
            CodePoints.Of(s).Count(cp => cp == '#' || CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0) == UnicodeCategory.DecimalDigitNumber);

        private static int ZeroOf(string presentation)
        {
            foreach (int cp in CodePoints.Of(presentation))
            {
                string ch = char.ConvertFromUtf32(cp);
                if (CharUnicodeInfo.GetUnicodeCategory(ch, 0) == UnicodeCategory.DecimalDigitNumber)
                {
                    return cp - (int)CharUnicodeInfo.GetDecimalDigitValue(ch, 0);
                }
            }
            return '0';
        }

        private static string PadDigits(string text, int width, string presentation)
        {
            string zero = char.ConvertFromUtf32(ZeroOf(presentation));
            var sb = new StringBuilder();
            for (int n = CountDigits(text); n < width; n++)
            {
                sb.Append(zero);
            }
            // Keep a sign (and any ordinal suffix) in place: zeros go before the first digit.
            int firstDigit = 0;
            while (firstDigit < text.Length && !(char.IsDigit(text, firstDigit) || (char.IsHighSurrogate(text[firstDigit]) && firstDigit + 1 < text.Length &&
                   CharUnicodeInfo.GetUnicodeCategory(text, firstDigit) == UnicodeCategory.DecimalDigitNumber)))
            {
                firstDigit++;
            }
            return text[..firstDigit] + sb + text[firstDigit..];
        }

        private static bool IsDecimalDigit(int cp) => CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0) == UnicodeCategory.DecimalDigitNumber;

        private static string MapDigits(string ascii, int zero) =>
            zero == '0' ? ascii : string.Concat(ascii.Select(d => d is >= '0' and <= '9' ? char.ConvertFromUtf32(zero + (d - '0')) : d.ToString()));

        /// <summary>
        /// Fractional seconds: the picture reads from the left (mandatory digits, then optional '#' digits), and
        /// digits are truncated, not rounded.
        /// </summary>
        private static string FractionalSeconds(decimal seconds, string presentation, bool hasWidth, int? minWidth, int? maxWidth, string picture)
        {
            decimal fraction = seconds - decimal.Truncate(seconds);
            string digits = fraction == 0 ? "" : fraction.ToString(CultureInfo.InvariantCulture)[2..];
            int[] cps = CodePoints.Of(presentation).ToArray();
            if (!cps.Any(IsDecimalDigit))
            {
                // Not a decimal-digit-pattern (e.g. [fi]): present the digits as an integer.
                digits = digits.TrimEnd('0');
                var other = new IntegerFormatter(presentation, Error, allowEmpty: true, modifierInPicture: false);
                return other.Format(digits.Length == 0 ? BigInteger.Zero : BigInteger.Parse(digits, CultureInfo.InvariantCulture));
            }

            int mandatory = 0, optional = 0, zero = -1;
            var separators = new List<(int After, string Separator)>();
            bool lastWasSeparator = true;
            foreach (int cp in cps)
            {
                if (cp == '#')
                {
                    optional++;
                }
                else if (IsDecimalDigit(cp))
                {
                    int family = cp - (int)CharUnicodeInfo.GetDecimalDigitValue(char.ConvertFromUtf32(cp), 0);
                    if (optional > 0 || (zero >= 0 && zero != family))
                    {
                        throw new XdmException(Error, $"Picture '{picture}': invalid fractional seconds pattern '{presentation}'");
                    }
                    zero = family;
                    mandatory++;
                }
                else if (char.IsLetter(char.ConvertFromUtf32(cp), 0) || lastWasSeparator)
                {
                    throw new XdmException(Error, $"Picture '{picture}': invalid fractional seconds pattern '{presentation}'");
                }
                else
                {
                    separators.Add((mandatory + optional, char.ConvertFromUtf32(cp)));
                    lastWasSeparator = true;
                    continue;
                }
                lastWasSeparator = false;
            }
            if (lastWasSeparator)
            {
                throw new XdmException(Error, $"Picture '{picture}': invalid fractional seconds pattern '{presentation}'");
            }

            int total = mandatory + optional;
            int min = System.Math.Max(System.Math.Max(mandatory, 1), hasWidth ? minWidth ?? 1 : 1);
            int? max = hasWidth ? maxWidth : total > 1 ? total : null;
            if (max is int m)
            {
                max = System.Math.Max(m, min);
            }
            if (max is int limit && digits.Length > limit)
            {
                digits = digits[..limit];
            }
            digits = digits.TrimEnd('0');
            if (digits.Length < min)
            {
                digits = digits.PadRight(min, '0');
            }
            var sb = new StringBuilder();
            for (int i = 0; i < digits.Length; i++)
            {
                sb.Append(MapDigits(digits[i].ToString(), zero));
                foreach (var (after, sep) in separators)
                {
                    if (after == i + 1 && i + 1 < digits.Length)
                    {
                        sb.Append(sep);
                    }
                }
            }
            return sb.ToString();
        }

        private static string Timezone(XsDateTime v, char component, string? presentation, char? second, string picture)
        {
            if (v.TimezoneMinutes is not int tz)
            {
                return presentation == "Z" ? "J" : "";
            }
            if (presentation == "Z")
            {
                if (tz % 60 == 0 && tz / 60 is >= -12 and <= 12)
                {
                    int h = tz / 60;
                    return h switch
                    {
                        0 => "Z",
                        > 0 and <= 9 => ((char)('A' + h - 1)).ToString(),
                        > 9 => ((char)('K' + h - 10)).ToString(),
                        _ => ((char)('N' - h - 1)).ToString(),
                    };
                }
                presentation = null;
            }
            if (presentation is "N" or "n" or "Nn")
            {
                presentation = null;
            }
            if (second == 't' && tz == 0)
            {
                return "Z";
            }
            string pattern = presentation ?? "01:01";
            string sign = tz < 0 ? "-" : "+";
            int abs = System.Math.Abs(tz);
            int hours = abs / 60, minutes = abs % 60;
            int[] cps = CodePoints.Of(pattern).ToArray();
            int zero = ZeroOf(pattern);
            string H(int width) => MapDigits(hours.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0'), zero);
            string M() => MapDigits(minutes.ToString("00", CultureInfo.InvariantCulture), zero);
            int sepAt = System.Array.FindIndex(cps, cp => !IsDecimalDigit(cp) && cp != '#');
            string result;
            if (sepAt >= 0)
            {
                if (sepAt == 0 || sepAt == cps.Length - 1)
                {
                    throw new XdmException(Error, $"Picture '{picture}': invalid timezone presentation '{pattern}'");
                }
                result = sign + H(sepAt) + char.ConvertFromUtf32(cps[sepAt]) + MapDigits(minutes.ToString(CultureInfo.InvariantCulture).PadLeft(cps.Length - sepAt - 1, '0'), zero);
            }
            else if (cps.Length <= 2)
            {
                result = sign + H(cps.Length) + (minutes != 0 ? ":" + M() : "");
            }
            else
            {
                result = sign + H(cps.Length - 2) + M();
            }
            return component == 'z' ? "GMT" + result : result;
        }

        // ------------------------------------------------------------ calendar arithmetic

        private static long Days(XsDateTime v) => XsDateTime.DaysFromCivil(v.Year, v.Month, v.Day);

        /// <summary>ISO day of the week: Monday is 1, Sunday 7.</summary>
        private static int DayOfWeek(XsDateTime v) => (int)((Days(v) % 7 + 7 + 3) % 7) + 1;

        private static int DayOfYear(XsDateTime v) => (int)(Days(v) - XsDateTime.DaysFromCivil(v.Year, 1, 1)) + 1;

        /// <summary>ISO 8601 week of the year: the week (Monday to Sunday) containing the year's first Thursday is week 1.</summary>
        private static int IsoWeek(XsDateTime v)
        {
            long thursday = Days(v) + (4 - DayOfWeek(v));
            var (y, _, _) = CivilFromDays(thursday);
            return (int)((thursday - XsDateTime.DaysFromCivil(y, 1, 1)) / 7) + 1;
        }

        /// <summary>
        /// Week of the month on the same principle: the week containing the month's first Thursday is week 1, and
        /// days before it belong to the previous month's last week. Days at the end of a month stay in that month.
        /// </summary>
        private static int WeekOfMonth(XsDateTime v)
        {
            long days = Days(v);
            long thursday = days + (4 - DayOfWeek(v));
            var (_, m, d) = CivilFromDays(thursday);
            if (m != v.Month && d < 7 && v.Day > 7)
            {
                var (_, _, d0) = CivilFromDays(thursday - 7);
                return (d0 - 1) / 7 + 2;
            }
            return (d - 1) / 7 + 1;
        }

        // Inverse of DaysFromCivil (H. Hinnant's civil_from_days), with the same year convention.
        private static (long Year, int Month, int Day) CivilFromDays(long z)
        {
            z += 719468;
            long era = (z >= 0 ? z : z - 146096) / 146097;
            long doe = z - era * 146097;
            long yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
            long y = yoe + era * 400;
            long doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
            long mp = (5 * doy + 2) / 153;
            int d = (int)(doy - (153 * mp + 2) / 5 + 1);
            int m = (int)(mp < 10 ? mp + 3 : mp - 9);
            y += m <= 2 ? 1 : 0;
            return (y <= 0 ? y - 1 : y, m, d);
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
}
