// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Xsl3.Xdm
{
    /// <summary>
    /// Casting between atomic types (F&amp;O 3.1 §19, XPath 3.1 §3.14.2). A cast goes source primitive →
    /// target primitive by the casting table (§19.1), then checks the target's facets if it is a derived type
    /// (§19.3). Casting from xs:string or xs:untypedAtomic parses the target's lexical form (§19.2).
    /// </summary>
    internal static partial class Casting
    {
        /// <summary>Casts <paramref name="value"/> to <paramref name="target"/>. <paramref name="namespaces"/> resolves prefixes when casting to xs:QName.</summary>
        public static AtomicValue Cast(AtomicValue value, AtomicType target, IXmlNamespaceResolver? namespaces = null)
        {
            if (target.IsAbstract)
            {
                throw Err.XPST0080(target);
            }
            if (ReferenceEquals(value.Type, target))
            {
                return value;
            }

            Primitive from = value.Type.Primitive;
            if (from is Primitive.String or Primitive.UntypedAtomic)
            {
                return FromString(value.Value, target, namespaces);
            }

            AtomicValue primitive = ToPrimitive(value, target.Primitive);
            return Restrict(primitive, target);
        }

        /// <summary><c>castable as</c>: whether <see cref="Cast"/> would succeed.</summary>
        public static bool IsCastable(AtomicValue value, AtomicType target, IXmlNamespaceResolver? namespaces = null)
        {
            try
            {
                Cast(value, target, namespaces);
                return true;
            }
            catch (XdmException e) when (e.Code != "XPST0080")
            {
                return false;
            }
        }

        /// <summary>The constructor function <c>xs:T($s)</c> / <c>$s cast as xs:T</c> from a string (F&amp;O 3.1 §19.2).</summary>
        public static AtomicValue FromString(string s, AtomicType target, IXmlNamespaceResolver? namespaces = null)
        {
            if (target.IsAbstract)
            {
                throw Err.XPST0080(target);
            }
            string lexical = Lexical.ApplyWhitespace(s, target.Whitespace);
            AtomicValue? result = target.Primitive switch
            {
                Primitive.UntypedAtomic => new StringValue(AtomicTypes.UntypedAtomic, s),
                Primitive.String => new StringValue(AtomicTypes.String, lexical),
                Primitive.AnyUri => new StringValue(AtomicTypes.AnyUri, lexical),
                Primitive.Boolean => lexical switch
                {
                    "true" or "1" => BooleanValue.True,
                    "false" or "0" => BooleanValue.False,
                    _ => null,
                },
                Primitive.Integer => Lexical.TryParseInteger(lexical, out BigInteger i) ? new IntegerValue(AtomicTypes.Integer, i) : null,
                Primitive.Decimal => Lexical.TryParseDecimal(lexical, out decimal d) ? new DecimalValue(AtomicTypes.Decimal, d) : null,
                Primitive.Double => Lexical.TryParseDouble(lexical, out double dbl) ? new DoubleValue(dbl) : null,
                Primitive.Float => Lexical.TryParseFloat(lexical, out float f) ? new FloatValue(f) : null,
                Primitive.Duration or Primitive.YearMonthDuration or Primitive.DayTimeDuration =>
                    XsDuration.TryParse(lexical, target, out XsDuration dur) ? new DurationValue(AtomicTypes.Of(target.Primitive), dur) : null,
                Primitive.DateTime or Primitive.Date or Primitive.Time or Primitive.GYearMonth or Primitive.GYear or
                Primitive.GMonthDay or Primitive.GDay or Primitive.GMonth =>
                    XsDateTime.TryParse(lexical, target.Primitive, out XsDateTime dt) ? new DateTimeValue(AtomicTypes.Of(target.Primitive), dt) : null,
                Primitive.HexBinary => Lexical.TryParseHex(lexical, out byte[] hex) ? new BinaryValue(AtomicTypes.HexBinary, hex) : null,
                Primitive.Base64Binary => Lexical.TryParseBase64(lexical, out byte[] b64) ? new BinaryValue(AtomicTypes.Base64Binary, b64) : null,
                Primitive.QName => ParseQName(lexical, namespaces),
                _ => null,
            };
            if (result == null)
            {
                throw Err.FORG0001(s, target);
            }
            return Restrict(result, target);
        }

        private static QNameValue? ParseQName(string lexical, IXmlNamespaceResolver? namespaces)
        {
            int colon = lexical.IndexOf(':');
            string prefix = colon < 0 ? "" : lexical[..colon];
            string local = colon < 0 ? lexical : lexical[(colon + 1)..];
            if ((prefix.Length > 0 && !IsNCName(prefix)) || !IsNCName(local))
            {
                return null;
            }
            string? uri = prefix.Length == 0 ? namespaces?.LookupNamespace("") ?? "" : namespaces?.LookupNamespace(prefix);
            if (uri == null)
            {
                throw new XdmException("FONS0004", $"No namespace is bound to prefix '{prefix}'");
            }
            return new QNameValue(AtomicTypes.QName, new QName(uri, local, prefix));
        }

        // ------------------------------------------------------------ the casting table (§19.1)

        private static AtomicValue ToPrimitive(AtomicValue v, Primitive to)
        {
            Primitive from = v.Type.Primitive;
            switch (to)
            {
                case Primitive.UntypedAtomic:
                    return new StringValue(AtomicTypes.UntypedAtomic, v.Value);
                case Primitive.String:
                    return new StringValue(AtomicTypes.String, v.Value);
                case Primitive.AnyUri:
                    if (from == Primitive.AnyUri) return v;
                    break;
                case Primitive.Boolean:
                    switch (v)
                    {
                        case BooleanValue: return v;
                        case IntegerValue i: return AtomicValue.OfBoolean(!i.Integer.IsZero);
                        case DecimalValue d: return AtomicValue.OfBoolean(d.Decimal != 0);
                        case DoubleValue d: return AtomicValue.OfBoolean(d.Double != 0 && !double.IsNaN(d.Double));
                        case FloatValue f: return AtomicValue.OfBoolean(f.Float != 0 && !float.IsNaN(f.Float));
                    }
                    break;
                case Primitive.Integer:
                    switch (v)
                    {
                        case IntegerValue i: return new IntegerValue(AtomicTypes.Integer, i.Integer);
                        case DecimalValue d: return AtomicValue.OfInteger(new BigInteger(decimal.Truncate(d.Decimal)));
                        case DoubleValue d: return AtomicValue.OfInteger(DoubleToInteger(d.Double));
                        case FloatValue f: return AtomicValue.OfInteger(DoubleToInteger(f.Float));
                        case BooleanValue b: return AtomicValue.OfInteger(b.Boolean ? 1 : 0);
                    }
                    break;
                case Primitive.Decimal:
                    switch (v)
                    {
                        case DecimalValue d: return new DecimalValue(AtomicTypes.Decimal, d.Decimal);
                        case IntegerValue i: return AtomicValue.OfDecimal(IntegerToDecimal(i.Integer));
                        case DoubleValue d: return AtomicValue.OfDecimal(DoubleToDecimal(d.Double, d.Value));
                        case FloatValue f: return AtomicValue.OfDecimal(DoubleToDecimal(f.Float, f.Float.ToString("R", CultureInfo.InvariantCulture)));
                        case BooleanValue b: return AtomicValue.OfDecimal(b.Boolean ? 1 : 0);
                    }
                    break;
                case Primitive.Double:
                    switch (v)
                    {
                        case DoubleValue: return v;
                        case FloatValue f: return AtomicValue.OfDouble(f.Float);
                        case DecimalValue d: return AtomicValue.OfDouble(Numeric.ToDouble(d.Decimal));
                        case IntegerValue i: return AtomicValue.OfDouble(Numeric.ToDouble(i.Integer));
                        case BooleanValue b: return AtomicValue.OfDouble(b.Boolean ? 1 : 0);
                    }
                    break;
                case Primitive.Float:
                    switch (v)
                    {
                        case FloatValue: return v;
                        case DoubleValue d: return AtomicValue.OfFloat((float)d.Double);
                        case DecimalValue d: return AtomicValue.OfFloat(Numeric.ToFloat(d.Decimal));
                        case IntegerValue i: return AtomicValue.OfFloat(Numeric.ToFloat(i.Integer));
                        case BooleanValue b: return AtomicValue.OfFloat(b.Boolean ? 1 : 0);
                    }
                    break;
                case Primitive.Duration:
                case Primitive.YearMonthDuration:
                case Primitive.DayTimeDuration:
                    if (v is DurationValue dur)
                    {
                        XsDuration d = to switch
                        {
                            Primitive.YearMonthDuration => new XsDuration(dur.Duration.Months, 0),
                            Primitive.DayTimeDuration => new XsDuration(0, dur.Duration.Seconds),
                            _ => dur.Duration,
                        };
                        return new DurationValue(AtomicTypes.Of(to), d);
                    }
                    break;
                case Primitive.DateTime:
                case Primitive.Date:
                case Primitive.Time:
                case Primitive.GYearMonth:
                case Primitive.GYear:
                case Primitive.GMonthDay:
                case Primitive.GDay:
                case Primitive.GMonth:
                    if (v is DateTimeValue dtv && DateTimeCastAllowed(from, to))
                    {
                        XsDateTime x = dtv.DateTime;
                        XsDateTime r = to switch
                        {
                            Primitive.DateTime => new XsDateTime(x.Year, x.Month, x.Day, x.Hour, x.Minute, x.Second, x.TimezoneMinutes),
                            Primitive.Date => new XsDateTime(x.Year, x.Month, x.Day, 0, 0, 0, x.TimezoneMinutes),
                            Primitive.Time => new XsDateTime(0, 0, 0, x.Hour, x.Minute, x.Second, x.TimezoneMinutes),
                            Primitive.GYearMonth => new XsDateTime(x.Year, x.Month, 0, 0, 0, 0, x.TimezoneMinutes),
                            Primitive.GYear => new XsDateTime(x.Year, 0, 0, 0, 0, 0, x.TimezoneMinutes),
                            Primitive.GMonthDay => new XsDateTime(0, x.Month, x.Day, 0, 0, 0, x.TimezoneMinutes),
                            Primitive.GDay => new XsDateTime(0, 0, x.Day, 0, 0, 0, x.TimezoneMinutes),
                            _ => new XsDateTime(0, x.Month, 0, 0, 0, 0, x.TimezoneMinutes),
                        };
                        return new DateTimeValue(AtomicTypes.Of(to), r);
                    }
                    break;
                case Primitive.HexBinary:
                case Primitive.Base64Binary:
                    if (v is BinaryValue bin)
                    {
                        return new BinaryValue(AtomicTypes.Of(to), bin.Bytes.ToArray());
                    }
                    break;
                case Primitive.QName:
                    if (v is QNameValue q && from == Primitive.QName)
                    {
                        return v;
                    }
                    break;
            }
            throw Err.XPTY0004Cast(v.Type, AtomicTypes.Of(to));
        }

        // dateTime → anything date/time; date → dateTime, date and the g-types; otherwise only identity.
        private static bool DateTimeCastAllowed(Primitive from, Primitive to) =>
            from == to ||
            from == Primitive.DateTime ||
            (from == Primitive.Date && to is not Primitive.Time);

        private static BigInteger DoubleToInteger(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                throw Err.FOCA0002($"Cannot convert {Lexical.DoubleToString(d)} to xs:integer");
            }
            return new BigInteger(Math.Truncate(d));
        }

        private static decimal IntegerToDecimal(BigInteger i)
        {
            if (i > new BigInteger(decimal.MaxValue) || i < new BigInteger(decimal.MinValue))
            {
                throw Err.FOCA0001(i.ToString(CultureInfo.InvariantCulture));
            }
            return (decimal)i;
        }

        // Uses the shortest round-trip digits, so 0.1e0 becomes 0.1 rather than 0.1000000000000000055511….
        private static decimal DoubleToDecimal(double d, string roundTrip)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                throw Err.FOCA0002($"Cannot convert {Lexical.DoubleToString(d)} to xs:decimal");
            }
            if (Math.Abs(d) >= 7.9228162514264337593543950335e28)
            {
                throw Err.FOCA0001(roundTrip);
            }
            if (decimal.TryParse(roundTrip, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }
            return (decimal)d; // below decimal's resolution: rounds towards zero
        }

        // ------------------------------------------------------------ derived types (§19.3)

        /// <summary>Relabels a primitive value as <paramref name="target"/> after checking the target's facets.</summary>
        private static AtomicValue Restrict(AtomicValue v, AtomicType target)
        {
            if (ReferenceEquals(v.Type, target))
            {
                return v;
            }
            switch (v)
            {
                case IntegerValue i:
                    if ((target.MinInclusive is BigInteger min && i.Integer < min) || (target.MaxInclusive is BigInteger max && i.Integer > max))
                    {
                        throw Err.FORG0001(i.Value, target);
                    }
                    return new IntegerValue(target, i.Integer);
                case StringValue s when target.Primitive == Primitive.String:
                    string value = Lexical.ApplyWhitespace(s.String, target.Whitespace);
                    if (!SatisfiesStringConstraint(value, target.StringConstraint))
                    {
                        throw Err.FORG0001(s.String, target);
                    }
                    return new StringValue(target, value);
                case DateTimeValue dt when target.RequiresTimezone:
                    if (!dt.DateTime.HasTimezone)
                    {
                        throw Err.FORG0001(dt.Value, target);
                    }
                    return new DateTimeValue(target, dt.DateTime);
                default:
                    if (target.Base == null || ReferenceEquals(AtomicTypes.Of(target.Primitive), target))
                    {
                        return v;
                    }
                    throw Err.XPTY0004Cast(v.Type, target);
            }
        }

        private static bool SatisfiesStringConstraint(string s, StringConstraint constraint) => constraint switch
        {
            StringConstraint.None => true,
            StringConstraint.NormalizedString => s.IndexOfAny(new[] { '\t', '\n', '\r' }) < 0,
            StringConstraint.Token => s == Lexical.Collapse(s),
            StringConstraint.Language => LanguagePattern().IsMatch(s),
            StringConstraint.NmToken => s.Length > 0 && IsValid(() => XmlConvert.VerifyNMTOKEN(s)),
            StringConstraint.Name => s.Length > 0 && IsValid(() => XmlConvert.VerifyName(s)),
            StringConstraint.NCName => IsNCName(s),
            _ => false,
        };

        internal static bool IsNCName(string s) => s.Length > 0 && IsValid(() => XmlConvert.VerifyNCName(s));

        private static bool IsValid(Func<string> verify)
        {
            try
            {
                verify();
                return true;
            }
            catch (XmlException)
            {
                return false;
            }
        }

        [GeneratedRegex("^[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*$", RegexOptions.CultureInvariant)]
        private static partial Regex LanguagePattern();
    }
}
