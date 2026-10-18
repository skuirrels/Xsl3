// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Globalization;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    /// <summary>
    /// fn:parse-ietf-date (F&amp;O 3.1 §9.8.3): the RFC 1123 / RFC 850 / asctime date formats.
    /// <code>
    /// input    ::= S? (dayname ","? S)? ((datespec S time) | asctime) S?
    /// datespec ::= daynum dsep monthname dsep year
    /// asctime  ::= monthname dsep daynum S time S year
    /// dsep     ::= S | (S? "-" S?)
    /// time     ::= hours ":" minutes (":" seconds)? (S? timezone)?
    /// timezone ::= tzname | tzoffset (S? "(" S? tzname S? ")")?
    /// </code>
    /// </summary>
    internal static class IetfDate
    {
        private static readonly string[] s_months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
        private static readonly string[] s_days = { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday", "mon", "tue", "wed", "thu", "fri", "sat", "sun" };

        public static DateTimeValue Parse(string input)
        {
            var p = new Cursor(input);
            p.Spaces();
            // Optional day name
            foreach (string d in s_days)
            {
                if (p.TryWord(d))
                {
                    p.Accept(',');
                    if (!p.Spaces()) throw Bad(input);
                    break;
                }
            }

            int day, month;
            long year;
            int hour, minute;
            decimal second;
            int tz;
            if (char.IsAsciiDigit(p.Peek))
            {
                day = p.Digits(1, 2);
                p.DateSeparator(input);
                month = p.Month(input);
                p.DateSeparator(input);
                year = Year(p.Digits(2, 4, exactly: new[] { 2, 4 }));
                if (!p.Spaces()) throw Bad(input);
                (hour, minute, second, tz) = Time(p, input);
            }
            else
            {
                month = p.Month(input);
                p.DateSeparator(input);
                day = p.Digits(1, 2);
                if (!p.Spaces()) throw Bad(input);
                (hour, minute, second, tz) = Time(p, input);
                if (!p.Spaces()) throw Bad(input);
                year = Year(p.Digits(2, 4, exactly: new[] { 2, 4 }));
            }
            p.Spaces();
            if (!p.AtEnd) throw Bad(input);

            if (month < 1 || day < 1 || day > XsDateTime.DaysInMonth(year, month) || hour > 24 || minute > 59 || second >= 60 || (hour == 24 && (minute != 0 || second != 0)))
            {
                throw new XdmException("FORG0010", $"'{input}' is not a valid date");
            }
            var value = new XsDateTime(year, month, day, hour == 24 ? 0 : hour, minute, second, tz);
            if (hour == 24)
            {
                value = XsDateTime.FromTimeline(value.ToTimeline(Primitive.DateTime, 0) + 86400 + tz * 60, tz, tz);
            }
            // The result is normalised to UTC.
            return new DateTimeValue(AtomicTypes.DateTime, XsDateTime.FromTimeline(value.ToTimeline(Primitive.DateTime, 0), 0, 0));
        }

        private static long Year(int digits) => digits < 100 ? 1900 + digits : digits;

        private static (int, int, decimal, int) Time(Cursor p, string input)
        {
            int hour = p.Digits(1, 2);
            if (!p.Accept(':')) throw Bad(input);
            int minute = p.Digits(2, 2);
            decimal second = 0;
            if (p.Accept(':'))
            {
                second = p.Digits(2, 2);
                if (p.Accept('.'))
                {
                    int start = p.Position;
                    while (char.IsAsciiDigit(p.Peek)) p.Next();
                    if (p.Position == start) throw Bad(input);
                    second += decimal.Parse("0." + input[start..p.Position], CultureInfo.InvariantCulture);
                }
            }
            int save = p.Position;
            p.Spaces();
            int tz = 0;
            if (p.Peek is '+' or '-')
            {
                int sign = p.Next() == '-' ? -1 : 1;
                int hh = p.Digits(1, 2), mm = 0;
                if (p.Accept(':'))
                {
                    mm = p.Digits(2, 2);
                }
                else if (char.IsAsciiDigit(p.Peek))
                {
                    mm = p.Digits(2, 2);
                }
                if (mm > 59) throw Bad(input);
                tz = sign * (hh * 60 + mm);
                int afterOffset = p.Position;
                p.Spaces();
                if (p.Accept('('))
                {
                    p.Spaces();
                    if (TzName(p) == null) throw Bad(input);
                    p.Spaces();
                    if (!p.Accept(')')) throw Bad(input);
                }
                else
                {
                    p.Position = afterOffset;
                }
            }
            else if (TzName(p) is int named)
            {
                tz = named;
            }
            else
            {
                p.Position = save;
            }
            if (System.Math.Abs(tz) > 14 * 60) throw Bad(input);
            return (hour, minute, second, tz);
        }

        private static int? TzName(Cursor p)
        {
            (string Name, int Offset)[] names =
            {
                ("UTC", 0), ("UT", 0), ("GMT", 0), ("EST", -300), ("EDT", -240), ("CST", -360), ("CDT", -300),
                ("MST", -420), ("MDT", -360), ("PST", -480), ("PDT", -420),
            };
            foreach (var (name, offset) in names)
            {
                if (p.TryWord(name.ToLowerInvariant()))
                {
                    return offset;
                }
            }
            return null;
        }

        private static XdmException Bad(string input) => new("FORG0010", $"'{input}' is not a valid IETF date");

        private sealed class Cursor(string s)
        {
            public int Position { get; set; }

            public bool AtEnd => Position >= s.Length;

            public char Peek => Position < s.Length ? s[Position] : '\0';

            public char Next() => s[Position++];

            public bool Spaces()
            {
                int start = Position;
                while (Peek is ' ' or '\t' or '\n' or '\r') Position++;
                return Position > start;
            }

            public bool Accept(char c)
            {
                if (Peek == c)
                {
                    Position++;
                    return true;
                }
                return false;
            }

            /// <summary>Matches a word case-insensitively, not followed by another letter.</summary>
            public bool TryWord(string lower)
            {
                if (Position + lower.Length > s.Length || !s.AsSpan(Position, lower.Length).Equals(lower, System.StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                if (Position + lower.Length < s.Length && char.IsAsciiLetter(s[Position + lower.Length]))
                {
                    return false;
                }
                Position += lower.Length;
                return true;
            }

            public int Digits(int min, int max, int[]? exactly = null)
            {
                int start = Position;
                while (Position - start < max && char.IsAsciiDigit(Peek)) Position++;
                int n = Position - start;
                if (n < min || (exactly != null && System.Array.IndexOf(exactly, n) < 0))
                {
                    throw Bad(s);
                }
                return int.Parse(s.AsSpan(start, n), NumberStyles.None, CultureInfo.InvariantCulture);
            }

            public int Month(string input)
            {
                for (int i = 0; i < s_months.Length; i++)
                {
                    if (TryWord(s_months[i]))
                    {
                        return i + 1;
                    }
                }
                throw Bad(input);
            }

            // dsep ::= S | (S? "-" S?)
            public void DateSeparator(string input)
            {
                bool space = Spaces();
                if (Accept('-'))
                {
                    Spaces();
                    return;
                }
                if (!space) throw Bad(input);
            }
        }
    }
}
