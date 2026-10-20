// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Xsl3: the XDM values of the CLR values a caller passes as stylesheet parameters (SPEC §7.1), for 2.0+
    /// stylesheets: integers as xs:integer, decimal, float and double as themselves, dates and times and durations,
    /// URIs, binary, dictionaries as maps and other enumerables as sequences. XSLT 1.0 stylesheets keep stock's
    /// conversions (every number a double, a DateTime a string).
    /// </summary>
    internal static class ClrValues
    {
        /// <summary>The value as an XDM sequence; false for the values stock's rules handle (strings, booleans, nodes).</summary>
        public static bool TryConvert(object value, out IList<XPathItem> items)
        {
            switch (value)
            {
                case IList<XPathItem> sequence:
                    items = sequence;
                    return true;
                case AtomicValue or XdmFunction:
                    items = new[] { (XPathItem)value };
                    return true;
                case string or bool or XPathNavigator or XPathNodeIterator or IXPathNavigable:
                    items = null!;
                    return false;
            }
            if (Atomic(value) is AtomicValue atomic)
            {
                items = new XPathItem[] { atomic };
                return true;
            }
            if (value is IDictionary dictionary)
            {
                var entries = new List<KeyValuePair<AtomicValue, IList<XPathItem>>>();
                foreach (DictionaryEntry entry in dictionary)
                {
                    AtomicValue key = entry.Key is string s ? AtomicValue.OfString(s) : Atomic(entry.Key)
                        ?? throw new XdmException("XPTY0004", $"A map key can't be a {entry.Key.GetType().Name}");
                    entries.Add(new(key, entry.Value == null ? System.Array.Empty<XPathItem>() : Sequence(entry.Value)));
                }
                items = new XPathItem[] { XdmMap.Of(entries) };
                return true;
            }
            if (value is IEnumerable enumerable)
            {
                var list = new List<XPathItem>();
                foreach (object? member in enumerable)
                {
                    if (member != null)
                    {
                        list.AddRange(Sequence(member));
                    }
                }
                items = list;
                return true;
            }
            items = null!;
            return false;
        }

        // A member of a map or sequence: also strings, booleans and nodes.
        private static IList<XPathItem> Sequence(object value) => value switch
        {
            string s => new XPathItem[] { AtomicValue.OfString(s) },
            bool b => new XPathItem[] { AtomicValue.OfBoolean(b) },
            XPathNavigator n => new XPathItem[] { n },
            IXPathNavigable n => new XPathItem[] { n.CreateNavigator()! },
            _ => TryConvert(value, out IList<XPathItem> items) ? items : throw new XdmException("XPTY0004", $"A {value.GetType().Name} has no XDM value"),
        };

        private static AtomicValue? Atomic(object value) => value switch
        {
            int or long or short or sbyte or byte or uint or ulong or ushort => AtomicValue.OfInteger(System.Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture) is decimal d ? new BigInteger(d) : default),
            BigInteger i => AtomicValue.OfInteger(i),
            decimal d => AtomicValue.OfDecimal(d),
            double d => AtomicValue.OfDouble(d),
            float f => AtomicValue.OfFloat(f),
            System.DateTimeOffset o => new DateTimeValue(AtomicTypes.DateTime, DateTime(o.DateTime, (int)o.Offset.TotalMinutes)),
            System.DateTime t => new DateTimeValue(AtomicTypes.DateTime, DateTime(t, t.Kind switch
            {
                System.DateTimeKind.Utc => 0,
                System.DateTimeKind.Local => (int)System.TimeZoneInfo.Local.GetUtcOffset(t).TotalMinutes,
                _ => null,
            })),
            System.DateOnly d => new DateTimeValue(AtomicTypes.Date, new XsDateTime(d.Year, d.Month, d.Day, 0, 0, 0m, null)),
            System.TimeOnly t => new DateTimeValue(AtomicTypes.Time, new XsDateTime(1972, 12, 31, t.Hour, t.Minute, t.Second + t.Millisecond / 1000m + t.Microsecond / 1_000_000m, null)),
            System.TimeSpan s => new DurationValue(AtomicTypes.DayTimeDuration, new XsDuration(0, (decimal)s.Ticks / System.TimeSpan.TicksPerSecond)),
            System.Uri u => new StringValue(AtomicTypes.AnyUri, u.OriginalString),
            byte[] b => new BinaryValue(AtomicTypes.Base64Binary, b),
            _ => null,
        };

        private static XsDateTime DateTime(System.DateTime t, int? timezoneMinutes) =>
            new(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second + (decimal)(t.Ticks % System.TimeSpan.TicksPerSecond) / System.TimeSpan.TicksPerSecond, timezoneMinutes);
    }
}
