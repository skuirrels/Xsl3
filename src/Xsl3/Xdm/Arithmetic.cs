// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Numerics;

namespace Xsl3.Xdm
{
    internal enum ArithmeticOp { Add, Subtract, Multiply, Divide, IntegerDivide, Modulo }

    /// <summary>Numeric type promotion and conversions shared by comparison and arithmetic.</summary>
    internal static class Numeric
    {
        /// <summary>The type both operands are promoted to (XPath 3.1 B.1): integer, decimal, float or double.</summary>
        public static Primitive CommonType(Primitive a, Primitive b)
        {
            if (a == Primitive.Double || b == Primitive.Double) return Primitive.Double;
            if (a == Primitive.Float || b == Primitive.Float) return Primitive.Float;
            if (a == Primitive.Decimal || b == Primitive.Decimal) return Primitive.Decimal;
            return Primitive.Integer;
        }

        public static double ToDouble(AtomicValue v) => v switch
        {
            DoubleValue d => d.Double,
            FloatValue f => f.Float,
            DecimalValue d => (double)d.Decimal,
            IntegerValue i => (double)i.Integer,
            _ => throw new InvalidCastException(),
        };

        public static float ToFloat(AtomicValue v) => v switch
        {
            FloatValue f => f.Float,
            DoubleValue d => (float)d.Double,
            DecimalValue d => (float)d.Decimal,
            IntegerValue i => (float)(double)i.Integer,
            _ => throw new InvalidCastException(),
        };

        public static decimal ToDecimal(AtomicValue v)
        {
            switch (v)
            {
                case DecimalValue d:
                    return d.Decimal;
                case IntegerValue i:
                    try
                    {
                        return (decimal)i.Integer;
                    }
                    catch (OverflowException)
                    {
                        throw Err.FOAR0002("integer too large for xs:decimal arithmetic");
                    }
                default:
                    throw new InvalidCastException();
            }
        }
    }

    /// <summary>
    /// Arithmetic operators (XPath 3.1 §3.5) over atomic values: numerics (F&amp;O 3.1 §4.2), durations
    /// (§8.4) and dates (§9.7). xs:untypedAtomic operands are cast to xs:double first.
    /// </summary>
    internal static class Arithmetic
    {
        public static AtomicValue Apply(ArithmeticOp op, AtomicValue a, AtomicValue b, int implicitTimezoneMinutes)
        {
            a = UntypedToDouble(a);
            b = UntypedToDouble(b);
            if (a.Type.IsNumeric && b.Type.IsNumeric)
            {
                return NumericOp(op, a, b);
            }
            if (a is DurationValue || b is DurationValue || a is DateTimeValue || b is DateTimeValue)
            {
                return TemporalOp(op, a, b, implicitTimezoneMinutes);
            }
            throw NotApplicable(op, a, b);
        }

        /// <summary>Unary minus (op:numeric-unary-minus) or plus.</summary>
        public static AtomicValue Unary(bool minus, AtomicValue a)
        {
            a = UntypedToDouble(a);
            if (!a.Type.IsNumeric)
            {
                throw Err.XPTY0004($"Unary {(minus ? "minus" : "plus")} needs a numeric operand, not {a.Type.Name}");
            }
            if (!minus)
            {
                return a;
            }
            return a switch
            {
                IntegerValue i => AtomicValue.OfInteger(-i.Integer),
                DecimalValue d => AtomicValue.OfDecimal(-d.Decimal),
                FloatValue f => AtomicValue.OfFloat(-f.Float),
                DoubleValue d => AtomicValue.OfDouble(-d.Double),
                _ => throw new InvalidCastException(),
            };
        }

        private static AtomicValue UntypedToDouble(AtomicValue v) =>
            v.Type.Primitive == Primitive.UntypedAtomic ? Casting.Cast(v, AtomicTypes.Double) : v;

        private static XdmException NotApplicable(ArithmeticOp op, AtomicValue a, AtomicValue b) =>
            Err.XPTY0004($"Operator {OpName(op)} is not defined for {a.Type.Name} and {b.Type.Name}");

        private static string OpName(ArithmeticOp op) => op switch
        {
            ArithmeticOp.Add => "+", ArithmeticOp.Subtract => "-", ArithmeticOp.Multiply => "*",
            ArithmeticOp.Divide => "div", ArithmeticOp.IntegerDivide => "idiv", _ => "mod",
        };

        // ------------------------------------------------------------ numerics (F&O §4.2)

        private static AtomicValue NumericOp(ArithmeticOp op, AtomicValue a, AtomicValue b)
        {
            Primitive common = Numeric.CommonType(a.Type.Primitive, b.Type.Primitive);
            // integer div integer is decimal (op:numeric-divide)
            if (common == Primitive.Integer && op == ArithmeticOp.Divide)
            {
                common = Primitive.Decimal;
            }

            switch (common)
            {
                case Primitive.Integer:
                {
                    BigInteger x = ((IntegerValue)a).Integer, y = ((IntegerValue)b).Integer;
                    switch (op)
                    {
                        case ArithmeticOp.Add: return AtomicValue.OfInteger(x + y);
                        case ArithmeticOp.Subtract: return AtomicValue.OfInteger(x - y);
                        case ArithmeticOp.Multiply: return AtomicValue.OfInteger(x * y);
                        case ArithmeticOp.IntegerDivide:
                            if (y.IsZero) throw Err.FOAR0001();
                            return AtomicValue.OfInteger(BigInteger.Divide(x, y)); // truncates towards zero
                        default:
                            if (y.IsZero) throw Err.FOAR0001();
                            return AtomicValue.OfInteger(BigInteger.Remainder(x, y)); // sign of the dividend
                    }
                }

                case Primitive.Decimal:
                {
                    decimal x = Numeric.ToDecimal(a), y = Numeric.ToDecimal(b);
                    try
                    {
                        switch (op)
                        {
                            case ArithmeticOp.Add: return AtomicValue.OfDecimal(x + y);
                            case ArithmeticOp.Subtract: return AtomicValue.OfDecimal(x - y);
                            case ArithmeticOp.Multiply: return AtomicValue.OfDecimal(x * y);
                            case ArithmeticOp.Divide:
                                if (y == 0) throw Err.FOAR0001();
                                return AtomicValue.OfDecimal(x / y);
                            case ArithmeticOp.IntegerDivide:
                                if (y == 0) throw Err.FOAR0001();
                                return AtomicValue.OfInteger(new BigInteger(decimal.Truncate(x / y)));
                            default:
                                if (y == 0) throw Err.FOAR0001();
                                return AtomicValue.OfDecimal(x % y);
                        }
                    }
                    catch (OverflowException)
                    {
                        throw Err.FOAR0002($"xs:decimal {OpName(op)}");
                    }
                }

                case Primitive.Float:
                {
                    float x = Numeric.ToFloat(a), y = Numeric.ToFloat(b);
                    return op switch
                    {
                        ArithmeticOp.Add => AtomicValue.OfFloat(x + y),
                        ArithmeticOp.Subtract => AtomicValue.OfFloat(x - y),
                        ArithmeticOp.Multiply => AtomicValue.OfFloat(x * y),
                        ArithmeticOp.Divide => AtomicValue.OfFloat(x / y),
                        ArithmeticOp.IntegerDivide => AtomicValue.OfInteger(FloatingIntegerDivide(x, y)),
                        _ => AtomicValue.OfFloat(x % y),
                    };
                }

                default:
                {
                    double x = Numeric.ToDouble(a), y = Numeric.ToDouble(b);
                    return op switch
                    {
                        ArithmeticOp.Add => AtomicValue.OfDouble(x + y),
                        ArithmeticOp.Subtract => AtomicValue.OfDouble(x - y),
                        ArithmeticOp.Multiply => AtomicValue.OfDouble(x * y),
                        ArithmeticOp.Divide => AtomicValue.OfDouble(x / y),
                        ArithmeticOp.IntegerDivide => AtomicValue.OfInteger(FloatingIntegerDivide(x, y)),
                        _ => AtomicValue.OfDouble(x % y),
                    };
                }
            }
        }

        // op:numeric-integer-divide for floating operands: FOAR0001 on a zero divisor; FOAR0002 if either
        // operand is NaN or the dividend is infinite.
        private static BigInteger FloatingIntegerDivide(double x, double y)
        {
            if (y == 0) throw Err.FOAR0001();
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x)) throw Err.FOAR0002("idiv of NaN or INF");
            if (double.IsInfinity(y)) return BigInteger.Zero;
            return new BigInteger(Math.Truncate(x / y));
        }

        // ------------------------------------------------------------ durations and dates

        private static AtomicValue TemporalOp(ArithmeticOp op, AtomicValue a, AtomicValue b, int implicitTz)
        {
            Primitive pa = a.Type.Primitive, pb = b.Type.Primitive;

            // duration ± duration, duration div duration (same subtype only)
            if (a is DurationValue da && b is DurationValue db && pa == pb && pa != Primitive.Duration)
            {
                try
                {
                    return DurationOp(op, pa, da.Duration, db.Duration) ?? throw NotApplicable(op, a, b);
                }
                catch (OverflowException)
                {
                    throw Err.FODT0002($"{a.Type.Name} {OpName(op)}");
                }
            }

            // duration * number, number * duration, duration div number
            if (a is DurationValue d1 && b.Type.IsNumeric && pa != Primitive.Duration && op is ArithmeticOp.Multiply or ArithmeticOp.Divide)
            {
                return ScaleDuration(d1, Numeric.ToDouble(b), op == ArithmeticOp.Divide);
            }
            if (b is DurationValue d2 && a.Type.IsNumeric && pb != Primitive.Duration && op == ArithmeticOp.Multiply)
            {
                return ScaleDuration(d2, Numeric.ToDouble(a), divide: false);
            }

            // date/time - date/time
            if (a is DateTimeValue ta && b is DateTimeValue tb && op == ArithmeticOp.Subtract && pa == pb && pa is Primitive.DateTime or Primitive.Date or Primitive.Time)
            {
                decimal seconds = ta.DateTime.ToTimeline(pa, implicitTz) - tb.DateTime.ToTimeline(pb, implicitTz);
                return new DurationValue(AtomicTypes.DayTimeDuration, new XsDuration(0, seconds));
            }

            // date/time ± duration, duration + date/time
            if (a is DateTimeValue t1 && b is DurationValue dur1 && op is ArithmeticOp.Add or ArithmeticOp.Subtract)
            {
                return AddToDateTime(t1, dur1, op == ArithmeticOp.Subtract);
            }
            if (b is DateTimeValue t2 && a is DurationValue dur2 && op == ArithmeticOp.Add)
            {
                return AddToDateTime(t2, dur2, subtract: false);
            }
            throw NotApplicable(op, a, b);
        }

        private static AtomicValue? DurationOp(ArithmeticOp op, Primitive kind, XsDuration x, XsDuration y)
        {
            switch (op)
            {
                case ArithmeticOp.Add: return Duration(kind, checked(x.Months + y.Months), x.Seconds + y.Seconds);
                case ArithmeticOp.Subtract: return Duration(kind, checked(x.Months - y.Months), x.Seconds - y.Seconds);
                case ArithmeticOp.Divide:
                    if (kind == Primitive.YearMonthDuration)
                    {
                        if (y.Months == 0) throw Err.FOAR0001();
                        return AtomicValue.OfDecimal((decimal)x.Months / y.Months);
                    }
                    if (y.Seconds == 0) throw Err.FOAR0001();
                    return AtomicValue.OfDecimal(x.Seconds / y.Seconds);
                default:
                    return null;
            }
        }

        private static DurationValue Duration(Primitive kind, long months, decimal seconds)
        {
            if ((months > 0 && seconds < 0) || (months < 0 && seconds > 0))
            {
                throw new InvalidOperationException("Mixed-sign duration.");
            }
            return new DurationValue(AtomicTypes.Of(kind), new XsDuration(months, seconds));
        }

        // op:multiply-yearMonthDuration / op:divide-dayTimeDuration and friends.
        private static AtomicValue ScaleDuration(DurationValue d, double factor, bool divide)
        {
            if (double.IsNaN(factor))
            {
                throw Err.FOCA0005();
            }
            if (divide && factor == 0)
            {
                throw Err.FODT0002("division of a duration by zero");
            }
            if (!divide && double.IsInfinity(factor))
            {
                throw Err.FODT0002("multiplication of a duration by infinity");
            }
            if (d.Type.Primitive == Primitive.YearMonthDuration)
            {
                double months = divide ? d.Duration.Months / factor : d.Duration.Months * factor;
                // Rounded to the nearest month, halves upward (as fn:round).
                double rounded = Math.Floor(months + 0.5);
                if (double.IsInfinity(rounded) || Math.Abs(rounded) > long.MaxValue)
                {
                    throw Err.FODT0002("yearMonthDuration overflow");
                }
                return new DurationValue(AtomicTypes.YearMonthDuration, new XsDuration((long)rounded, 0));
            }
            try
            {
                // Exact decimal arithmetic when the factor has a decimal form (it nearly always does).
                decimal f = (decimal)factor;
                decimal seconds = divide ? d.Duration.Seconds / f : d.Duration.Seconds * f;
                return new DurationValue(AtomicTypes.DayTimeDuration, new XsDuration(0, seconds));
            }
            catch (OverflowException)
            {
                if (divide && Math.Abs(factor) > 1)
                {
                    return new DurationValue(AtomicTypes.DayTimeDuration, new XsDuration(0, 0));
                }
                throw Err.FODT0002("dayTimeDuration overflow");
            }
        }

        private static AtomicValue AddToDateTime(DateTimeValue t, DurationValue d, bool subtract)
        {
            Primitive kind = t.Type.Primitive;
            Primitive dkind = d.Type.Primitive;
            if (kind is not (Primitive.DateTime or Primitive.Date or Primitive.Time) || dkind == Primitive.Duration
                || (kind == Primitive.Time && dkind != Primitive.DayTimeDuration))
            {
                throw Err.XPTY0004($"Can't add {d.Type.Name} to {t.Type.Name}");
            }

            XsDateTime x = t.DateTime;
            XsDateTime result;
            if (dkind == Primitive.YearMonthDuration)
            {
                result = x.AddMonths(subtract ? -d.Duration.Months : d.Duration.Months);
            }
            else
            {
                decimal seconds = subtract ? -d.Duration.Seconds : d.Duration.Seconds;
                if (kind == Primitive.Time)
                {
                    // Times wrap around midnight.
                    decimal secOfDay = x.Hour * 3600 + x.Minute * 60 + x.Second + seconds;
                    secOfDay -= decimal.Floor(secOfDay / 86400) * 86400;
                    int h = (int)decimal.Truncate(secOfDay / 3600);
                    secOfDay -= h * 3600;
                    int m = (int)decimal.Truncate(secOfDay / 60);
                    result = new XsDateTime(0, 0, 0, h, m, secOfDay - m * 60, x.TimezoneMinutes);
                }
                else
                {
                    // Work in the value's own timezone (or none): the local components shift by the duration.
                    int presentAs = x.TimezoneMinutes ?? 0;
                    decimal timeline = x.ToTimeline(Primitive.DateTime, presentAs) + seconds;
                    result = XsDateTime.FromTimeline(timeline, x.TimezoneMinutes, presentAs);
                }
            }
            if (kind == Primitive.Date)
            {
                result = new XsDateTime(result.Year, result.Month, result.Day, 0, 0, 0, result.TimezoneMinutes);
            }
            return new DateTimeValue(AtomicTypes.Of(kind), result);
        }
    }
}
