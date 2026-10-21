// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    /// <summary>
    /// String functions count Unicode code points, not UTF-16 units (SPEC §13.5). Strings without surrogates,
    /// nearly all of them, take a fast path that indexes the string directly.
    /// </summary>
    internal static class CodePoints
    {
        private static bool HasSurrogates(string s) => s.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF') >= 0;

        public static int Length(string s) => HasSurrogates(s) ? s.EnumerateRunes().Count() : s.Length;

        public static IEnumerable<int> Of(string s)
        {
            foreach (Rune r in s.EnumerateRunes())
            {
                yield return r.Value;
            }
        }

        // fn:round as used by fn:substring: half towards positive infinity.
        private static double Round(double d) => double.IsNaN(d) || double.IsInfinity(d) ? d : System.Math.Floor(d + 0.5);

        /// <summary>fn:substring: the code points at positions p with round($start) &lt;= p &lt; round($start) + round($length).</summary>
        public static string Substring(string s, double start, double length)
        {
            double first = Round(start);
            double end = first + Round(length);
            if (double.IsNaN(first) || double.IsNaN(end))
            {
                return "";
            }
            if (!HasSurrogates(s))
            {
                double from = System.Math.Max(first, 1), to = System.Math.Min(end, s.Length + 1);
                return to <= from ? "" : s.Substring((int)from - 1, (int)(to - from));
            }
            var sb = new StringBuilder();
            int p = 1;
            foreach (Rune r in s.EnumerateRunes())
            {
                if (p >= first && p < end)
                {
                    sb.Append(r.ToString());
                }
                p++;
            }
            return sb.ToString();
        }

        /// <summary>fn:translate, by code point. Characters of $mapString beyond $transString's length are removed.</summary>
        public static string Translate(string s, string map, string trans)
        {
            var mapping = new Dictionary<int, int>();
            int[] from = Of(map).ToArray(), to = Of(trans).ToArray();
            for (int i = 0; i < from.Length; i++)
            {
                mapping.TryAdd(from[i], i < to.Length ? to[i] : -1);
            }
            var sb = new StringBuilder(s.Length);
            foreach (int cp in Of(s))
            {
                if (mapping.TryGetValue(cp, out int replacement))
                {
                    if (replacement >= 0)
                    {
                        sb.Append(char.ConvertFromUtf32(replacement));
                    }
                }
                else
                {
                    sb.Append(char.ConvertFromUtf32(cp));
                }
            }
            return sb.ToString();
        }

        /// <summary>fn:codepoints-to-string. FOCH0001 for a code point that isn't an XML character.</summary>
        public static string ToStringChecked(IEnumerable<BigInteger> codepoints)
        {
            var sb = new StringBuilder();
            foreach (BigInteger cp in codepoints)
            {
                if (!(cp == 0x9 || cp == 0xA || cp == 0xD || (cp >= 0x20 && cp <= 0xD7FF) || (cp >= 0xE000 && cp <= 0xFFFD) || (cp >= 0x10000 && cp <= 0x10FFFF)))
                {
                    throw new XdmException("FOCH0001", $"{cp} is not a valid XML character");
                }
                sb.Append(char.ConvertFromUtf32((int)cp));
            }
            return sb.ToString();
        }

        /// <summary>fn:normalize-unicode with a normalization form (F&amp;O 3.1 §5.4.6).</summary>
        public static string Normalize(string s, string form)
        {
            switch (form.Trim().ToUpperInvariant())
            {
                case "": return s;
                case "NFC": return s.Normalize(NormalizationForm.FormC);
                case "NFD": return s.Normalize(NormalizationForm.FormD);
                case "NFKC": return s.Normalize(NormalizationForm.FormKC);
                case "NFKD": return s.Normalize(NormalizationForm.FormKD);
                default: throw new XdmException("FOCH0003", $"Unsupported normalization form '{form}'");
            }
        }
    }

    /// <summary>abs, ceiling, floor, round and round-half-to-even (F&amp;O 3.1 §4.4). Derived types return their base numeric type.</summary>
    internal static class NumericFunctions
    {
        public static AtomicValue Abs(AtomicValue v) => v switch
        {
            IntegerValue i => AtomicValue.OfInteger(BigInteger.Abs(i.Integer)),
            DecimalValue d => AtomicValue.OfDecimal(System.Math.Abs(d.Decimal)),
            FloatValue f => AtomicValue.OfFloat(System.Math.Abs(f.Float)),
            DoubleValue d => AtomicValue.OfDouble(System.Math.Abs(d.Double)),
            _ => throw new System.InvalidCastException(),
        };

        public static AtomicValue Ceiling(AtomicValue v) => v switch
        {
            IntegerValue i => AtomicValue.OfInteger(i.Integer),
            DecimalValue d => AtomicValue.OfDecimal(decimal.Ceiling(d.Decimal)),
            FloatValue f => AtomicValue.OfFloat(System.MathF.Ceiling(f.Float)),
            DoubleValue d => AtomicValue.OfDouble(System.Math.Ceiling(d.Double)),
            _ => throw new System.InvalidCastException(),
        };

        public static AtomicValue Floor(AtomicValue v) => v switch
        {
            IntegerValue i => AtomicValue.OfInteger(i.Integer),
            DecimalValue d => AtomicValue.OfDecimal(decimal.Floor(d.Decimal)),
            FloatValue f => AtomicValue.OfFloat(System.MathF.Floor(f.Float)),
            DoubleValue d => AtomicValue.OfDouble(System.Math.Floor(d.Double)),
            _ => throw new System.InvalidCastException(),
        };

        /// <summary>
        /// fn:round rounds halves towards positive infinity; fn:round-half-to-even to the even neighbour.
        /// A precision p rounds to a multiple of 10^-p. Doubles and floats are rounded via their decimal form
        /// when it exists, so round(1.125, 2) sees 1.125 rather than its binary approximation.
        /// </summary>
        public static AtomicValue Round(AtomicValue v, long precision, bool halfToEven)
        {
            switch (v)
            {
                case IntegerValue i:
                    if (precision >= 0) return AtomicValue.OfInteger(i.Integer);
                    BigInteger scale = BigInteger.Pow(10, (int)System.Math.Min(-precision, 10000));
                    return AtomicValue.OfInteger(RoundInteger(i.Integer, scale, halfToEven));
                case DecimalValue d:
                    return AtomicValue.OfDecimal(RoundDecimal(d.Decimal, precision, halfToEven));
                case DoubleValue d:
                    return AtomicValue.OfDouble(RoundDouble(d.Double, precision, halfToEven));
                case FloatValue f:
                    return AtomicValue.OfFloat((float)RoundDouble(f.Float, precision, halfToEven));
            }
            throw new System.InvalidCastException();
        }

        private static BigInteger RoundInteger(BigInteger x, BigInteger scale, bool halfToEven)
        {
            BigInteger q = BigInteger.DivRem(x, scale, out BigInteger r);
            BigInteger twice = BigInteger.Abs(r) * 2;
            int cmp = twice.CompareTo(scale);
            if (cmp > 0 || (cmp == 0 && (halfToEven ? !q.IsEven : x.Sign > 0)))
            {
                q += x.Sign;
            }
            return q * scale;
        }

        private static decimal RoundDecimal(decimal x, long precision, bool halfToEven)
        {
            if (precision >= 28)
            {
                return x;
            }
            if (precision >= 0)
            {
                if (halfToEven)
                {
                    return decimal.Round(x, (int)precision, System.MidpointRounding.ToEven);
                }
                // Half towards positive infinity.
                decimal factor = Pow10((int)precision);
                return decimal.Floor(x * factor + 0.5m) / factor;
            }
            if (precision < -28)
            {
                return 0;
            }
            decimal divisor = Pow10((int)-precision);
            decimal scaled = x / divisor;
            decimal rounded = halfToEven ? decimal.Round(scaled, 0, System.MidpointRounding.ToEven) : decimal.Floor(scaled + 0.5m);
            return rounded * divisor;
        }

        private static decimal Pow10(int n)
        {
            decimal r = 1;
            for (int i = 0; i < n; i++) r *= 10;
            return r;
        }

        // Rounding beyond decimal's range: on the double's 17 significant digits (d × 10^scale), then parsed back, so
        // round(9.9e-99, 99) is 1.0E-98 exactly as a double can hold it.
        private static double RoundDigits(double x, long precision, bool halfToEven)
        {
            string e = System.Math.Abs(x).ToString("E16", System.Globalization.CultureInfo.InvariantCulture);
            int ePos = e.IndexOf('E');
            var digits = System.Numerics.BigInteger.Parse(e[..ePos].Replace(".", ""), System.Globalization.CultureInfo.InvariantCulture);
            long scale = long.Parse(e[(ePos + 1)..], System.Globalization.CultureInfo.InvariantCulture) - 16;
            long drop = -precision - scale;
            if (drop <= 0)
            {
                return x;
            }
            if (drop > 17)
            {
                return x < 0 ? -0.0 : 0.0;
            }
            var unit = System.Numerics.BigInteger.Pow(10, (int)drop);
            var q = System.Numerics.BigInteger.DivRem(digits, unit, out System.Numerics.BigInteger r);
            int half = (r * 2).CompareTo(unit);
            // Ties: to even, or towards positive infinity (up in magnitude for positive numbers, down for negative ones).
            if (half > 0 || half == 0 && (halfToEven ? !q.IsEven : x > 0))
            {
                q += 1;
            }
            double magnitude = double.Parse(q.ToString(System.Globalization.CultureInfo.InvariantCulture) + "E" + (-precision).ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture);
            return x < 0 ? -magnitude : magnitude;
        }

        private static double RoundDouble(double x, long precision, bool halfToEven)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || x == 0)
            {
                return x;
            }
            double result;
            // Rounding works on the double's own value, not its shortest form: to 29 digits, -13.65e0 is
            // -13.650000000000000355271367880.
            if (System.Math.Abs(x) < 7.9e27 && decimal.TryParse(x.ToString("E28", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal dx) && dx != 0)
            {
                decimal rounded = RoundDecimal(dx, precision, halfToEven);
                // Unchanged, the double itself; else parsed back (the decimal-to-double conversion isn't correctly rounded).
                result = rounded == dx ? x : double.Parse(rounded.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (precision == 0)
            {
                result = halfToEven ? System.Math.Round(x, System.MidpointRounding.ToEven) : System.Math.Floor(x + 0.5);
            }
            else
            {
                result = RoundDigits(x, precision, halfToEven);
            }
            // Negative values that round to zero give negative zero.
            return result == 0 && x < 0 ? -0.0 : result;
        }
    }

    internal static class SequenceFunctions
    {
        /// <summary>fn:subsequence: items at positions p with round($start) &lt;= p &lt; round($start) + round($length).</summary>
        /// <remarks>The two-argument form (no <paramref name="length"/>) takes every item from round($start) on.</remarks>
        public static IList<XPathItem> Subsequence(IList<XPathItem> seq, double start, double? length)
        {
            double first = double.IsNaN(start) || double.IsInfinity(start) ? start : System.Math.Floor(start + 0.5);
            double len = length is not double l ? double.PositiveInfinity : double.IsNaN(l) || double.IsInfinity(l) ? l : System.Math.Floor(l + 0.5);
            double end = length == null ? double.PositiveInfinity : first + len;
            if (double.IsNaN(first) || double.IsNaN(end))
            {
                return Sequences.Empty;
            }
            var result = new List<XPathItem>();
            for (int p = 1; p <= seq.Count; p++)
            {
                if (p >= first && p < end)
                {
                    result.Add(seq[p - 1]);
                }
            }
            return result;
        }

        /// <summary>Equality as used by index-of, distinct-values and deep-equal: eq, except that NaN equals NaN and incomparable values are unequal.</summary>
        public static bool EqualLenient(AtomicValue a, AtomicValue b, FunctionContext c)
        {
            if (a.Type.IsNumeric && b.Type.IsNumeric && IsNaN(a) && IsNaN(b))
            {
                return true;
            }
            try
            {
                return Comparison.Compare(a, b, ValueComparisonOp.Eq, c.ImplicitTimezoneMinutes, c.DefaultCollation);
            }
            catch (XdmException e) when (e.Code == "XPTY0004")
            {
                return false;
            }
        }

        public static bool EqualForIndexOf(AtomicValue a, AtomicValue b, FunctionContext c) =>
            !(IsNaN(a) || IsNaN(b)) && EqualLenient(a, b, c);

        private static bool IsNaN(AtomicValue v) => v is DoubleValue d ? double.IsNaN(d.Double) : v is FloatValue f && float.IsNaN(f.Float);

        public static IList<XPathItem> DistinctValues(IList<XPathItem> seq, FunctionContext c)
        {
            var result = new List<XPathItem>();
            var buckets = new Dictionary<object, List<AtomicValue>>();
            foreach (XPathItem item in seq)
            {
                var v = (AtomicValue)item;
                object key = HashKey(v, c);
                if (!buckets.TryGetValue(key, out List<AtomicValue>? bucket))
                {
                    buckets[key] = bucket = new List<AtomicValue>();
                }
                if (!bucket.Any(b => EqualLenient(b, v, c)))
                {
                    bucket.Add(v);
                    result.Add(v);
                }
            }
            return result;
        }

        // Values that can be equal must share a key.
        public static object HashKey(AtomicValue v, FunctionContext c) => v switch
        {
            // Keyed by the value as a float: numbers equal after promotion (1.2 and xs:float(1.2)) round to the same float.
            _ when v.Type.IsNumeric => Numeric.ToDouble(v) is double d && double.IsNaN(d) ? "NaN" : (object)(float)Numeric.ToDouble(v),
            // Strings equal under the collation must share a key: the collation's own key.
            StringValue s => "s:" + c.DefaultCollation.Key(s.String),
            DateTimeValue dt => ("t", dt.Type.Primitive is Primitive.DateTime or Primitive.Date or Primitive.Time ? dt.DateTime.ToTimeline(dt.Type.Primitive, c.ImplicitTimezoneMinutes) : 0m),
            DurationValue du => ("d", du.Duration.Months, du.Duration.Seconds),
            QNameValue q => ("q", q.QName.NamespaceUri, q.QName.LocalName),
            _ => v.Type.Primitive.ToString() + ":" + v.Value,
        };
    }

    /// <summary>fn:sum, fn:avg, fn:min, fn:max (F&amp;O 3.1 §14.4).</summary>
    internal static class Aggregates
    {
        // xs:untypedAtomic values are cast to xs:double; the remaining values must all be numeric, or all be
        // durations of one subtype (FORG0006 otherwise).
        private static List<AtomicValue> Prepare(IList<XPathItem> seq, string function)
        {
            var values = seq.Select(i => (AtomicValue)i).Select(v => v.Type.Primitive == Primitive.UntypedAtomic ? Casting.Cast(v, AtomicTypes.Double) : v).ToList();
            if (values.Count == 0)
            {
                return values;
            }
            bool numeric = values.All(v => v.Type.IsNumeric);
            bool ym = values.All(v => v.Type.Primitive == Primitive.YearMonthDuration);
            bool dt = values.All(v => v.Type.Primitive == Primitive.DayTimeDuration);
            if (!numeric && !ym && !dt)
            {
                throw new XdmException("FORG0006", $"{function} needs numeric values or durations of one kind");
            }
            return values;
        }

        public static AtomicValue Sum(IList<XPathItem> seq, AtomicValue? zero, FunctionContext c)
        {
            List<AtomicValue> values = Prepare(seq, "fn:sum");
            if (values.Count == 0)
            {
                return zero!;
            }
            AtomicValue total = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                total = Arithmetic.Apply(ArithmeticOp.Add, total, values[i], c.ImplicitTimezoneMinutes);
            }
            return total;
        }

        public static IList<XPathItem> Avg(IList<XPathItem> seq, FunctionContext c)
        {
            if (seq.Count == 0)
            {
                return Sequences.Empty;
            }
            AtomicValue total = Sum(seq, null, c);
            return Sequences.One(Arithmetic.Apply(ArithmeticOp.Divide, total, AtomicValue.OfInteger(seq.Count), c.ImplicitTimezoneMinutes));
        }

        public static IList<XPathItem> MinMax(IList<XPathItem> seq, FunctionContext c, bool max)
        {
            if (seq.Count == 0)
            {
                return Sequences.Empty;
            }
            var values = seq.Select(i => (AtomicValue)i).Select(v => v.Type.Primitive == Primitive.UntypedAtomic ? Casting.Cast(v, AtomicTypes.Double) : v).ToList();

            // Numeric values are promoted to their common type, and the result has that type.
            if (values.All(v => v.Type.IsNumeric))
            {
                Primitive common = values.Select(v => v.Type.Primitive).Aggregate(Numeric.CommonType);
                AtomicType target = AtomicTypes.Of(common);
                values = values.Select(v => v.Type.Primitive == common ? v : Casting.Cast(v, target)).ToList();
                AtomicValue? nan = values.FirstOrDefault(v => v is DoubleValue d && double.IsNaN(d.Double) || v is FloatValue f && float.IsNaN(f.Float));
                if (nan != null)
                {
                    return Sequences.One(nan);
                }
            }
            else if (values.Any(v => v.Type.IsNumeric))
            {
                throw new XdmException("FORG0006", $"fn:{(max ? "max" : "min")}: values are not mutually comparable");
            }
            else if (values.All(v => v.Type.Primitive is Primitive.String or Primitive.AnyUri) && values.Any(v => v.Type.Primitive == Primitive.String))
            {
                // xs:anyURI and xs:string mix by promotion to xs:string; all-anyURI input stays xs:anyURI.
                values = values.Select(v => v.Type.Primitive == Primitive.AnyUri ? Casting.Cast(v, AtomicTypes.String) : v).ToList();
            }

            AtomicValue best = values[0];
            ValueComparisonOp op = max ? ValueComparisonOp.Gt : ValueComparisonOp.Lt;
            for (int i = 1; i < values.Count; i++)
            {
                try
                {
                    if (Comparison.Compare(values[i], best, op, c.ImplicitTimezoneMinutes, c.DefaultCollation))
                    {
                        best = values[i];
                    }
                }
                catch (XdmException e) when (e.Code == "XPTY0004")
                {
                    throw new XdmException("FORG0006", $"fn:{(max ? "max" : "min")}: values are not mutually comparable");
                }
            }
            // A single value must still be of an ordered type.
            if (values.Count == 1)
            {
                try
                {
                    Comparison.Compare(best, best, ValueComparisonOp.Lt, c.ImplicitTimezoneMinutes, c.DefaultCollation);
                }
                catch (XdmException e) when (e.Code == "XPTY0004")
                {
                    throw new XdmException("FORG0006", $"fn:{(max ? "max" : "min")}: {best.Type.Name} is not an ordered type");
                }
            }
            return Sequences.One(best);
        }
    }

    /// <summary>fn:deep-equal (F&amp;O 3.1 §14.2.1) with the default collation.</summary>
    internal static class DeepEqual
    {
        public static bool Sequences(IList<XPathItem> a, IList<XPathItem> b, FunctionContext c)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                if (!Items(a[i], b[i], c))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool Items(XPathItem a, XPathItem b, FunctionContext c)
        {
            if (a is AtomicValue x && b is AtomicValue y)
            {
                return SequenceFunctions.EqualLenient(x, y, c);
            }
            if (a is XPathNavigator m && b is XPathNavigator n)
            {
                return Nodes(m, n, c.DefaultCollation);
            }
            switch (a, b)
            {
                case (XdmMap p, XdmMap q):
                    // The same keys (op:same-key), with deep-equal values.
                    return p.Count == q.Count && p.Entries.All(e => q.Get(e.Key) is IList<XPathItem> v && Sequences(e.Value, v, c));
                case (XdmArray p, XdmArray q):
                    if (p.Count != q.Count)
                    {
                        return false;
                    }
                    for (int i = 0; i < p.Count; i++)
                    {
                        if (!Sequences(p[i], q[i], c))
                        {
                            return false;
                        }
                    }
                    return true;
                case (XdmFunction, XdmFunction):
                    throw new XdmException("FOTY0015", "deep-equal can't compare function items");
            }
            return false;
        }

        // Text, attribute and other string values compare under the collation (F&O 3.1 §14.2.1); names don't.
        private static bool Nodes(XPathNavigator a, XPathNavigator b, Collation collation)
        {
            if (Kind(a.NodeType) != Kind(b.NodeType))
            {
                return false;
            }
            switch (a.NodeType)
            {
                case XPathNodeType.Root:
                    return Children(a, b, collation);
                case XPathNodeType.Element:
                    return a.LocalName == b.LocalName && a.NamespaceURI == b.NamespaceURI && Attributes(a, b, collation) && Children(a, b, collation);
                case XPathNodeType.Attribute:
                    return a.LocalName == b.LocalName && a.NamespaceURI == b.NamespaceURI && collation.Compare(a.Value, b.Value) == 0;
                case XPathNodeType.ProcessingInstruction:
                    return a.LocalName == b.LocalName && collation.Compare(a.Value, b.Value) == 0;
                case XPathNodeType.Namespace:
                    return a.LocalName == b.LocalName && a.Value == b.Value;
                default:
                    return collation.Compare(a.Value, b.Value) == 0;
            }
        }

        private static XPathNodeType Kind(XPathNodeType t) => t is XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace ? XPathNodeType.Text : t;

        private static bool Attributes(XPathNavigator a, XPathNavigator b, Collation collation)
        {
            var left = AttributeMap(a);
            var right = AttributeMap(b);
            return left.Count == right.Count && left.All(kv => right.TryGetValue(kv.Key, out string? v) && collation.Compare(v, kv.Value) == 0);
        }

        private static Dictionary<(string, string), string> AttributeMap(XPathNavigator n)
        {
            var map = new Dictionary<(string, string), string>();
            XPathNavigator nav = n.Clone();
            if (nav.MoveToFirstAttribute())
            {
                do
                {
                    map[(nav.NamespaceURI, nav.LocalName)] = nav.Value;
                }
                while (nav.MoveToNextAttribute());
            }
            return map;
        }

        // Comments and processing instructions are ignored among children; elements and text are compared in order.
        private static bool Children(XPathNavigator a, XPathNavigator b, Collation collation)
        {
            List<XPathNavigator> left = SignificantChildren(a), right = SignificantChildren(b);
            if (left.Count != right.Count)
            {
                return false;
            }
            for (int i = 0; i < left.Count; i++)
            {
                if (!Nodes(left[i], right[i], collation))
                {
                    return false;
                }
            }
            return true;
        }

        private static List<XPathNavigator> SignificantChildren(XPathNavigator n)
        {
            var list = new List<XPathNavigator>();
            XPathNavigator nav = n.Clone();
            if (nav.MoveToFirstChild())
            {
                do
                {
                    if (nav.NodeType is not (XPathNodeType.Comment or XPathNodeType.ProcessingInstruction))
                    {
                        list.Add(nav.Clone());
                    }
                }
                while (nav.MoveToNext());
            }
            return list;
        }
    }

    internal static class QNameFunctions
    {
        /// <summary>fn:QName($paramURI, $paramQName).</summary>
        public static QNameValue Construct(string uri, string lexical)
        {
            int colon = lexical.IndexOf(':');
            string prefix = colon < 0 ? "" : lexical[..colon];
            string local = colon < 0 ? lexical : lexical[(colon + 1)..];
            if (!Casting.IsNCName(local) || (colon >= 0 && !Casting.IsNCName(prefix)))
            {
                throw Err.FOCA0002($"'{lexical}' is not a lexical QName");
            }
            if (prefix.Length > 0 && uri.Length == 0)
            {
                throw Err.FOCA0002($"A prefixed QName '{lexical}' needs a namespace URI");
            }
            return new QNameValue(AtomicTypes.QName, new QName(uri, local, prefix));
        }
    }
}
