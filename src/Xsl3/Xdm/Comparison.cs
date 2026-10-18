// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Numerics;

namespace Xsl3.Xdm
{
    internal enum ValueComparisonOp { Eq, Ne, Lt, Le, Gt, Ge }

    /// <summary>
    /// Value comparisons (XPath 3.1 §3.7.1) over atomic values, with the type rules of the F&amp;O operator
    /// functions they map to (op:numeric-equal, op:dateTime-less-than, …).
    /// </summary>
    internal static class Comparison
    {
        /// <summary>The default collation: Unicode code point order (F&amp;O 3.1 §5.3.2).</summary>
        public static readonly IComparer<string> CodepointCollation = new CodepointComparer();

        /// <summary>
        /// Evaluates <c>a op b</c>. xs:untypedAtomic operands compare as xs:string. Throws XPTY0004 when the
        /// types can't be compared with this operator.
        /// </summary>
        public static bool Compare(AtomicValue a, AtomicValue b, ValueComparisonOp op, int implicitTimezoneMinutes, IComparer<string>? collation = null)
        {
            bool ordering = op is not (ValueComparisonOp.Eq or ValueComparisonOp.Ne);
            int? c = CompareCore(a, b, ordering, implicitTimezoneMinutes, collation ?? CodepointCollation);
            if (c is not int result)
            {
                return op == ValueComparisonOp.Ne; // NaN: unordered
            }
            return op switch
            {
                ValueComparisonOp.Eq => result == 0,
                ValueComparisonOp.Ne => result != 0,
                ValueComparisonOp.Lt => result < 0,
                ValueComparisonOp.Le => result <= 0,
                ValueComparisonOp.Gt => result > 0,
                _ => result >= 0,
            };
        }

        /// <summary>Returns the sign of a - b, or null when the values are unordered (NaN).</summary>
        private static int? CompareCore(AtomicValue a, AtomicValue b, bool ordering, int implicitTz, IComparer<string> collation)
        {
            Primitive pa = Normalize(a.Type.Primitive), pb = Normalize(b.Type.Primitive);

            if (a.Type.IsNumeric && b.Type.IsNumeric)
            {
                return CompareNumeric(a, b);
            }
            if (pa == Primitive.String && pb == Primitive.String)
            {
                return Math.Sign(collation.Compare(a.Value, b.Value));
            }
            if (pa != pb)
            {
                // Durations of different subtypes are equal-comparable (op:duration-equal).
                if (!ordering && IsDuration(pa) && IsDuration(pb))
                {
                    return ((DurationValue)a).Duration.Equals(((DurationValue)b).Duration) ? 0 : 1;
                }
                throw Incomparable(a, b);
            }

            switch (pa)
            {
                case Primitive.Boolean:
                    return ((BooleanValue)a).Boolean.CompareTo(((BooleanValue)b).Boolean);

                case Primitive.DateTime:
                case Primitive.Date:
                case Primitive.Time:
                    return ((DateTimeValue)a).DateTime.ToTimeline(pa, implicitTz).CompareTo(((DateTimeValue)b).DateTime.ToTimeline(pb, implicitTz));

                case Primitive.GYearMonth:
                case Primitive.GYear:
                case Primitive.GMonthDay:
                case Primitive.GDay:
                case Primitive.GMonth:
                    if (ordering) throw Incomparable(a, b);
                    return ((DateTimeValue)a).DateTime.ToTimeline(pa, implicitTz) == ((DateTimeValue)b).DateTime.ToTimeline(pb, implicitTz) ? 0 : 1;

                case Primitive.Duration:
                    if (ordering) throw Incomparable(a, b);
                    return ((DurationValue)a).Duration.Equals(((DurationValue)b).Duration) ? 0 : 1;
                case Primitive.YearMonthDuration:
                    return ((DurationValue)a).Duration.Months.CompareTo(((DurationValue)b).Duration.Months);
                case Primitive.DayTimeDuration:
                    return ((DurationValue)a).Duration.Seconds.CompareTo(((DurationValue)b).Duration.Seconds);

                case Primitive.HexBinary:
                case Primitive.Base64Binary:
                    return Math.Sign(((BinaryValue)a).Bytes.SequenceCompareTo(((BinaryValue)b).Bytes));

                case Primitive.QName:
                case Primitive.Notation:
                    if (ordering) throw Incomparable(a, b);
                    return ((QNameValue)a).QName.Equals(((QNameValue)b).QName) ? 0 : 1;
            }
            throw Incomparable(a, b);
        }

        // xs:untypedAtomic and xs:anyURI compare as xs:string (XPath 3.1 §3.7.1, B.2 type promotion).
        private static Primitive Normalize(Primitive p) => p is Primitive.UntypedAtomic or Primitive.AnyUri ? Primitive.String : p;

        private static bool IsDuration(Primitive p) => p is Primitive.Duration or Primitive.YearMonthDuration or Primitive.DayTimeDuration;

        private static XdmException Incomparable(AtomicValue a, AtomicValue b) =>
            Err.XPTY0004($"Values of type {a.Type.Name} and {b.Type.Name} can't be compared");

        /// <summary>Numeric comparison after promotion (XPath 3.1 B.1): integer &lt; decimal &lt; float &lt; double.</summary>
        internal static int? CompareNumeric(AtomicValue a, AtomicValue b)
        {
            Primitive common = Numeric.CommonType(a.Type.Primitive, b.Type.Primitive);
            switch (common)
            {
                case Primitive.Integer:
                    return ((IntegerValue)a).Integer.CompareTo(((IntegerValue)b).Integer);
                case Primitive.Decimal:
                    return CompareExact(a, b);
                case Primitive.Float:
                {
                    float x = Numeric.ToFloat(a), y = Numeric.ToFloat(b);
                    return float.IsNaN(x) || float.IsNaN(y) ? null : x.CompareTo(y);
                }
                default:
                {
                    double x = Numeric.ToDouble(a), y = Numeric.ToDouble(b);
                    return double.IsNaN(x) || double.IsNaN(y) ? null : x.CompareTo(y);
                }
            }
        }

        // Integer against decimal without overflow: an integer beyond decimal's range is beyond any decimal.
        private static int CompareExact(AtomicValue a, AtomicValue b)
        {
            if (a is IntegerValue ia && !FitsDecimal(ia.Integer)) return ia.Integer.Sign;
            if (b is IntegerValue ib && !FitsDecimal(ib.Integer)) return -ib.Integer.Sign;
            return Numeric.ToDecimal(a).CompareTo(Numeric.ToDecimal(b));
        }

        private static bool FitsDecimal(BigInteger i) => i <= new BigInteger(decimal.MaxValue) && i >= new BigInteger(decimal.MinValue);

        /// <summary>
        /// Code point order. UTF-16 ordinal order is code point order except that U+E000..U+FFFF sort above
        /// supplementary characters (surrogates); this comparer fixes that up.
        /// </summary>
        private sealed class CodepointComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                int n = Math.Min(x.Length, y.Length);
                for (int i = 0; i < n; i++)
                {
                    char a = x[i], b = y[i];
                    if (a != b)
                    {
                        if (a >= 0xD800 && b >= 0xD800)
                        {
                            return Fix(a).CompareTo(Fix(b));
                        }
                        return a.CompareTo(b);
                    }
                }
                return x.Length.CompareTo(y.Length);
            }

            // Surrogates (supplementary characters) above U+E000..U+FFFF.
            private static int Fix(char c) => c >= 0xE000 ? c - 0x800 : c + 0x2000;
        }
    }
}
