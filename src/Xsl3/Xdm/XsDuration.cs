// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Globalization;
using System.Text;

namespace Xsl3.Xdm
{
    /// <summary>
    /// The value of an xs:duration, xs:yearMonthDuration or xs:dayTimeDuration: a signed number of months and a
    /// signed number of seconds, both with the same sign (F&amp;O 3.1 §8.1). Seconds are exact decimals, so
    /// fractional seconds keep the precision of the lexical form (up to 28 significant digits).
    /// </summary>
    internal readonly struct XsDuration : IEquatable<XsDuration>
    {
        public XsDuration(long months, decimal seconds)
        {
            if ((months > 0 && seconds < 0) || (months < 0 && seconds > 0))
            {
                throw new ArgumentException("Months and seconds must have the same sign.");
            }
            Months = months;
            Seconds = seconds;
        }

        public long Months { get; }

        public decimal Seconds { get; }

        public bool IsNegative => Months < 0 || Seconds < 0;

        public bool IsZero => Months == 0 && Seconds == 0;

        public XsDuration Negate() => new(-Months, -Seconds);

        public bool Equals(XsDuration other) => Months == other.Months && Seconds == other.Seconds;

        public override bool Equals(object? obj) => obj is XsDuration d && Equals(d);

        public override int GetHashCode() => HashCode.Combine(Months, Seconds);

        /// <summary>
        /// Parses the lexical form of <paramref name="type"/> (after whitespace collapsing). Returns false if
        /// it isn't one; throws FODT0002 if a component overflows.
        /// </summary>
        public static bool TryParse(string s, AtomicType type, out XsDuration result)
        {
            result = default;
            bool yearMonthOnly = type.Primitive == Primitive.YearMonthDuration;
            bool dayTimeOnly = type.Primitive == Primitive.DayTimeDuration;
            int i = 0;
            bool negative = false;
            if (i < s.Length && s[i] == '-')
            {
                negative = true;
                i++;
            }
            if (i >= s.Length || s[i] != 'P')
            {
                return false;
            }
            i++;

            System.Numerics.BigInteger months = 0;
            decimal seconds = 0;
            bool any = false, inTime = false;
            int lastOrder = -1; // components must appear in the order Y M D T H M S
            while (i < s.Length)
            {
                if (s[i] == 'T')
                {
                    if (inTime || yearMonthOnly)
                    {
                        return false;
                    }
                    inTime = true;
                    i++;
                    if (i >= s.Length)
                    {
                        return false; // "T" must be followed by a time component
                    }
                    continue;
                }
                int start = i;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                bool hasFraction = false;
                if (i < s.Length && s[i] == '.')
                {
                    hasFraction = true;
                    i++;
                    int fracStart = i;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                    if (i == fracStart)
                    {
                        return false;
                    }
                }
                if (i == start || i >= s.Length)
                {
                    return false;
                }
                string number = s[start..i];
                char designator = s[i++];
                int order = (inTime, designator) switch
                {
                    (false, 'Y') => 0,
                    (false, 'M') => 1,
                    (false, 'D') => 2,
                    (true, 'H') => 3,
                    (true, 'M') => 4,
                    (true, 'S') => 5,
                    _ => -1,
                };
                if (order <= lastOrder || (hasFraction && order != 5) || number[0] == '.')
                {
                    return false;
                }
                if ((yearMonthOnly && order > 1) || (dayTimeOnly && order < 2))
                {
                    return false;
                }
                lastOrder = order;
                any = true;
                try
                {
                    switch (order)
                    {
                        case 0: months += System.Numerics.BigInteger.Parse(number, CultureInfo.InvariantCulture) * 12; break;
                        case 1: months += System.Numerics.BigInteger.Parse(number, CultureInfo.InvariantCulture); break;
                        case 2: seconds += decimal.Parse(number, CultureInfo.InvariantCulture) * 86400; break;
                        case 3: seconds += decimal.Parse(number, CultureInfo.InvariantCulture) * 3600; break;
                        case 4: seconds += decimal.Parse(number, CultureInfo.InvariantCulture) * 60; break;
                        default: seconds += decimal.Parse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture); break;
                    }
                }
                catch (OverflowException)
                {
                    throw Err.FODT0002("duration component too large in '" + s + "'");
                }
            }
            if (!any || (inTime && lastOrder < 3))
            {
                return false;
            }
            if (months > long.MaxValue)
            {
                throw Err.FODT0002("months in '" + s + "'");
            }
            result = negative ? new XsDuration(-(long)months, -seconds) : new XsDuration((long)months, seconds);
            return true;
        }

        /// <summary>The canonical lexical form for <paramref name="type"/> (F&amp;O 3.1 §19.1.2.2 casting to xs:string).</summary>
        public string ToString(AtomicType type)
        {
            if (IsZero)
            {
                return type.Primitive == Primitive.YearMonthDuration ? "P0M" : "PT0S";
            }
            var sb = new StringBuilder();
            if (IsNegative)
            {
                sb.Append('-');
            }
            sb.Append('P');
            ulong months = (ulong)Math.Abs((decimal)Months);
            if (type.Primitive != Primitive.DayTimeDuration)
            {
                if (months / 12 != 0) sb.Append((months / 12).ToString(CultureInfo.InvariantCulture)).Append('Y');
                if (months % 12 != 0) sb.Append((months % 12).ToString(CultureInfo.InvariantCulture)).Append('M');
            }
            if (type.Primitive != Primitive.YearMonthDuration)
            {
                decimal secs = Math.Abs(Seconds);
                decimal days = decimal.Truncate(secs / 86400);
                secs -= days * 86400;
                decimal hours = decimal.Truncate(secs / 3600);
                secs -= hours * 3600;
                decimal minutes = decimal.Truncate(secs / 60);
                secs -= minutes * 60;
                if (days != 0) sb.Append(days.ToString(CultureInfo.InvariantCulture)).Append('D');
                if (hours != 0 || minutes != 0 || secs != 0)
                {
                    sb.Append('T');
                    if (hours != 0) sb.Append(hours.ToString(CultureInfo.InvariantCulture)).Append('H');
                    if (minutes != 0) sb.Append(minutes.ToString(CultureInfo.InvariantCulture)).Append('M');
                    if (secs != 0) sb.Append(Lexical.DecimalToString(secs)).Append('S');
                }
            }
            return sb.ToString();
        }
    }
}
