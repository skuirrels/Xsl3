// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Globalization;
using System.Text;

namespace Xsl3.Xdm
{
    /// <summary>
    /// The value of any of the eight date/time types (xs:dateTime, xs:date, xs:time, xs:gYearMonth, xs:gYear,
    /// xs:gMonthDay, xs:gDay, xs:gMonth) and xs:dateTimeStamp (SPEC §8.2). Components that a type doesn't have
    /// are zero. Unlike <see cref="System.DateTime"/> it has an optional timezone and unbounded years.
    /// </summary>
    /// <remarks>
    /// Years follow XML Schema 1.0, which Xsl3 implements (SPEC §18): there is no year 0, and -0001 is 1 BCE.
    /// All arithmetic and comparison goes through the "timeline", an exact decimal count of seconds, so
    /// fractional seconds are never rounded.
    /// </remarks>
    internal readonly struct XsDateTime
    {
        /// <summary>Years beyond ±10^12 overflow (FODT0001); XSD only requires four digits.</summary>
        public const long MaxYear = 999_999_999_999;

        public XsDateTime(long year, int month, int day, int hour, int minute, decimal second, int? timezoneMinutes)
        {
            Year = year;
            Month = month;
            Day = day;
            Hour = hour;
            Minute = minute;
            Second = second;
            TimezoneMinutes = timezoneMinutes;
        }

        public long Year { get; }

        public int Month { get; }

        public int Day { get; }

        public int Hour { get; }

        public int Minute { get; }

        /// <summary>Seconds including the fractional part, 0 &lt;= Second &lt; 60.</summary>
        public decimal Second { get; }

        /// <summary>The timezone as an offset from UTC in minutes, or null if the value has none.</summary>
        public int? TimezoneMinutes { get; }

        public bool HasTimezone => TimezoneMinutes.HasValue;

        public XsDateTime WithTimezone(int? tz) => new(Year, Month, Day, Hour, Minute, Second, tz);

        // ------------------------------------------------------------ calendar

        public static bool IsLeapYear(long year)
        {
            long astro = year < 0 ? year + 1 : year; // XSD 1.0: -0001 is 1 BCE, which is leap
            return (astro % 4 == 0 && astro % 100 != 0) || astro % 400 == 0;
        }

        public static int DaysInMonth(long year, int month) => month switch
        {
            2 => IsLeapYear(year) ? 29 : 28,
            4 or 6 or 9 or 11 => 30,
            _ => 31,
        };

        // Days since 1970-01-01 in the proleptic Gregorian calendar (H. Hinnant's days_from_civil).
        internal static long DaysFromCivil(long year, int month, int day)
        {
            long y = (year < 0 ? year + 1 : year) - (month <= 2 ? 1 : 0);
            long era = (y >= 0 ? y : y - 399) / 400;
            long yoe = y - era * 400;
            long mp = (month + 9) % 12;
            long doy = (153 * mp + 2) / 5 + day - 1;
            long doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
            return era * 146097 + doe - 719468;
        }

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

        // ------------------------------------------------------------ timeline

        /// <summary>
        /// Seconds since 1970-01-01T00:00:00Z, normalising to UTC with <paramref name="implicitTimezoneMinutes"/>
        /// when the value has no timezone. Missing components use the reference values of F&amp;O 3.1 §9.4
        /// (year 1972, December, day 1 for gMonth, and 00:00:00).
        /// </summary>
        public decimal ToTimeline(Primitive kind, int implicitTimezoneMinutes)
        {
            (long year, int month, int day) = kind switch
            {
                Primitive.Time => (1972L, 12, 31),
                Primitive.GMonthDay => (1972L, Month, Day),
                Primitive.GDay => (1972L, 12, Day),
                Primitive.GMonth => (1972L, Month, 1),
                Primitive.GYear => (Year, 1, 1),
                Primitive.GYearMonth => (Year, Month, 1),
                _ => (Year, Month, Day),
            };
            decimal seconds = (decimal)DaysFromCivil(year, month, day) * 86400 + Hour * 3600 + Minute * 60 + Second;
            return seconds - (TimezoneMinutes ?? implicitTimezoneMinutes) * 60;
        }

        /// <summary>The inverse of <see cref="ToTimeline"/> for dateTime-like values, presented in timezone <paramref name="tz"/>.</summary>
        public static XsDateTime FromTimeline(decimal utcSeconds, int? tz, int presentAsMinutes)
        {
            decimal local = utcSeconds + presentAsMinutes * 60;
            decimal daysD = decimal.Floor(local / 86400);
            decimal rem = local - daysD * 86400;
            long days = (long)daysD;
            (long y, int mo, int d) = CivilFromDays(days);
            int h = (int)decimal.Truncate(rem / 3600);
            rem -= h * 3600;
            int mi = (int)decimal.Truncate(rem / 60);
            rem -= mi * 60;
            if (Math.Abs(y) > MaxYear)
            {
                throw Err.FODT0001("year out of range");
            }
            return new XsDateTime(y, mo, d, h, mi, rem, tz);
        }

        /// <summary>Adds <paramref name="months"/> to the year and month, clamping the day to the month's length (F&amp;O 3.1 §10.8.4).</summary>
        public XsDateTime AddMonths(long months)
        {
            long astro = Year < 0 ? Year + 1 : Year;
            decimal total = (decimal)astro * 12 + (Month - 1) + months;
            decimal yearAstro = decimal.Floor(total / 12);
            int month = (int)(total - yearAstro * 12) + 1;
            long year = (long)yearAstro;
            year = year <= 0 ? year - 1 : year;
            if (Math.Abs(year) > MaxYear)
            {
                throw Err.FODT0001("year out of range");
            }
            int day = Math.Min(Day, DaysInMonth(year, month));
            return new XsDateTime(year, month, day, Hour, Minute, Second, TimezoneMinutes);
        }

        // ------------------------------------------------------------ lexical forms

        /// <summary>Parses the lexical form of a date/time type (after whitespace collapsing).</summary>
        public static bool TryParse(string s, Primitive kind, out XsDateTime result)
        {
            result = default;
            int i = 0;
            long year = 0;
            int month = 0, day = 0, hour = 0, minute = 0;
            decimal second = 0;

            bool ok = kind switch
            {
                Primitive.DateTime => ParseYear(s, ref i, out year) && Expect(s, ref i, '-') && Two(s, ref i, out month) && Expect(s, ref i, '-') && Two(s, ref i, out day)
                                      && Expect(s, ref i, 'T') && ParseTime(s, ref i, out hour, out minute, out second),
                Primitive.Date => ParseYear(s, ref i, out year) && Expect(s, ref i, '-') && Two(s, ref i, out month) && Expect(s, ref i, '-') && Two(s, ref i, out day),
                Primitive.Time => ParseTime(s, ref i, out hour, out minute, out second),
                Primitive.GYearMonth => ParseYear(s, ref i, out year) && Expect(s, ref i, '-') && Two(s, ref i, out month),
                Primitive.GYear => ParseYear(s, ref i, out year),
                Primitive.GMonthDay => Expect(s, ref i, '-') && Expect(s, ref i, '-') && Two(s, ref i, out month) && Expect(s, ref i, '-') && Two(s, ref i, out day),
                Primitive.GDay => Expect(s, ref i, '-') && Expect(s, ref i, '-') && Expect(s, ref i, '-') && Two(s, ref i, out day),
                Primitive.GMonth => Expect(s, ref i, '-') && Expect(s, ref i, '-') && Two(s, ref i, out month),
                _ => false,
            };
            if (!ok)
            {
                return false;
            }

            int? tz = null;
            if (i < s.Length)
            {
                if (!ParseTimezone(s, ref i, out int tzValue) || i != s.Length)
                {
                    return false;
                }
                tz = tzValue;
            }

            bool hasDate = kind is Primitive.DateTime or Primitive.Date or Primitive.GYearMonth or Primitive.GMonthDay or Primitive.GMonth;
            if (hasDate && (month < 1 || month > 12))
            {
                return false;
            }
            bool hasDay = kind is Primitive.DateTime or Primitive.Date or Primitive.GMonthDay or Primitive.GDay;
            if (hasDay)
            {
                // gMonthDay allows --02-29; gDay is checked against the longest month.
                int max = kind switch
                {
                    Primitive.GMonthDay => month == 2 ? 29 : DaysInMonth(2000, month),
                    Primitive.GDay => 31,
                    _ => DaysInMonth(year, month),
                };
                if (day < 1 || day > max)
                {
                    return false;
                }
            }

            var value = new XsDateTime(year, month, day, hour, minute, second, tz);
            // 24:00:00 is the first instant of the next day (XSD 1.0 errata, F&O 3.1 §9.3).
            if (hour == 24)
            {
                if (kind == Primitive.Time)
                {
                    value = new XsDateTime(0, 0, 0, 0, 0, 0, tz);
                }
                else
                {
                    decimal next = (decimal)DaysFromCivil(year, month, day) * 86400 + 86400;
                    value = FromTimeline(next, tz, 0);
                }
            }
            result = value;
            return true;
        }

        private static bool Expect(string s, ref int i, char c)
        {
            if (i < s.Length && s[i] == c)
            {
                i++;
                return true;
            }
            return false;
        }

        private static bool Two(string s, ref int i, out int value)
        {
            value = 0;
            if (i + 1 >= s.Length || !char.IsAsciiDigit(s[i]) || !char.IsAsciiDigit(s[i + 1]))
            {
                return false;
            }
            value = (s[i] - '0') * 10 + (s[i + 1] - '0');
            i += 2;
            return true;
        }

        // yearFrag ::= '-'? (([1-9] digit digit digit+)) | ('0' digit digit digit))
        private static bool ParseYear(string s, ref int i, out long year)
        {
            year = 0;
            bool negative = Expect(s, ref i, '-');
            int start = i;
            while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
            int length = i - start;
            if (length < 4 || (length > 4 && s[start] == '0'))
            {
                return false;
            }
            if (length > 12)
            {
                throw Err.FODT0001("year has too many digits in '" + s + "'");
            }
            year = long.Parse(s.AsSpan(start, length), NumberStyles.None, CultureInfo.InvariantCulture);
            if (year == 0)
            {
                return false; // no year zero in XML Schema 1.0
            }
            if (negative)
            {
                year = -year;
            }
            return true;
        }

        // hh:mm:ss(.s+)? with 24:00:00 allowed
        private static bool ParseTime(string s, ref int i, out int hour, out int minute, out decimal second)
        {
            second = 0;
            minute = 0;
            if (!Two(s, ref i, out hour) || !Expect(s, ref i, ':') || !Two(s, ref i, out minute) || !Expect(s, ref i, ':') || !Two(s, ref i, out int whole))
            {
                return false;
            }
            second = whole;
            if (i < s.Length && s[i] == '.')
            {
                int start = i;
                i++;
                while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                if (i == start + 1)
                {
                    return false;
                }
                // Keep at most 28 significant digits of the fraction.
                string fraction = s.Substring(start, Math.Min(i - start, 29));
                second += decimal.Parse("0" + fraction, CultureInfo.InvariantCulture);
            }
            if (minute > 59 || whole > 59)
            {
                return false;
            }
            if (hour == 24)
            {
                return minute == 0 && second == 0;
            }
            return hour < 24;
        }

        // 'Z' | ('+' | '-') hh ':' mm, with |offset| <= 14:00
        private static bool ParseTimezone(string s, ref int i, out int minutes)
        {
            minutes = 0;
            if (Expect(s, ref i, 'Z'))
            {
                return true;
            }
            int sign = Expect(s, ref i, '+') ? 1 : Expect(s, ref i, '-') ? -1 : 0;
            if (sign == 0 || !Two(s, ref i, out int hh) || !Expect(s, ref i, ':') || !Two(s, ref i, out int mm))
            {
                return false;
            }
            if (mm > 59 || hh > 14 || (hh == 14 && mm != 0))
            {
                return false;
            }
            minutes = sign * (hh * 60 + mm);
            return true;
        }

        /// <summary>The canonical lexical form for <paramref name="kind"/>.</summary>
        public string ToString(Primitive kind)
        {
            var sb = new StringBuilder(32);
            switch (kind)
            {
                case Primitive.DateTime:
                    AppendYear(sb); sb.Append('-'); Two(sb, Month); sb.Append('-'); Two(sb, Day); sb.Append('T'); AppendTime(sb);
                    break;
                case Primitive.Date:
                    AppendYear(sb); sb.Append('-'); Two(sb, Month); sb.Append('-'); Two(sb, Day);
                    break;
                case Primitive.Time:
                    AppendTime(sb);
                    break;
                case Primitive.GYearMonth:
                    AppendYear(sb); sb.Append('-'); Two(sb, Month);
                    break;
                case Primitive.GYear:
                    AppendYear(sb);
                    break;
                case Primitive.GMonthDay:
                    sb.Append("--"); Two(sb, Month); sb.Append('-'); Two(sb, Day);
                    break;
                case Primitive.GDay:
                    sb.Append("---"); Two(sb, Day);
                    break;
                case Primitive.GMonth:
                    sb.Append("--"); Two(sb, Month);
                    break;
            }
            AppendTimezone(sb, TimezoneMinutes);
            return sb.ToString();
        }

        private void AppendYear(StringBuilder sb)
        {
            if (Year < 0)
            {
                sb.Append('-');
            }
            sb.Append(Math.Abs(Year).ToString("D4", CultureInfo.InvariantCulture));
        }

        private void AppendTime(StringBuilder sb)
        {
            Two(sb, Hour);
            sb.Append(':');
            Two(sb, Minute);
            sb.Append(':');
            int whole = (int)decimal.Truncate(Second);
            Two(sb, whole);
            decimal fraction = Second - whole;
            if (fraction != 0)
            {
                // "0.25" → ".25"
                sb.Append(Lexical.DecimalToString(fraction).AsSpan(1));
            }
        }

        private static void Two(StringBuilder sb, int value) => sb.Append(value.ToString("D2", CultureInfo.InvariantCulture));

        internal static void AppendTimezone(StringBuilder sb, int? tz)
        {
            if (tz is not int minutes)
            {
                return;
            }
            if (minutes == 0)
            {
                sb.Append('Z');
                return;
            }
            sb.Append(minutes < 0 ? '-' : '+');
            minutes = Math.Abs(minutes);
            Two(sb, minutes / 60);
            sb.Append(':');
            Two(sb, minutes % 60);
        }
    }
}
