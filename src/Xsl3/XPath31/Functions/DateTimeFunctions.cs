// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    // Component extraction, timezone adjustment and the context dateTime functions (F&O 3.1 §8.5, §9.5–9.8).
    internal static partial class FunctionLibrary
    {
        private static IList<XPathItem> DateTimeComponent(IList<XPathItem> arg, System.Func<XsDateTime, AtomicValue?> component) =>
            Opt(arg) is DateTimeValue dt && component(dt.DateTime) is AtomicValue v ? One(v) : Empty;

        private static IList<XPathItem> DurationComponent(IList<XPathItem> arg, System.Func<XsDuration, AtomicValue> component) =>
            Opt(arg) is DurationValue d ? One(component(d.Duration)) : Empty;

        private static AtomicValue? Timezone(XsDateTime x) =>
            x.TimezoneMinutes is int tz ? new DurationValue(AtomicTypes.DayTimeDuration, new XsDuration(0, tz * 60m)) : null;

        private static AtomicValue Integer(long i) => AtomicValue.OfInteger(i);

        private static void RegisterDateTime()
        {
            foreach (string type in new[] { "dateTime", "date" })
            {
                Define($"fn:year-from-{type}($arg as xs:{type}?) as xs:integer?", (c, a) => DateTimeComponent(a[0], x => Integer(x.Year)));
                Define($"fn:month-from-{type}($arg as xs:{type}?) as xs:integer?", (c, a) => DateTimeComponent(a[0], x => Integer(x.Month)));
                Define($"fn:day-from-{type}($arg as xs:{type}?) as xs:integer?", (c, a) => DateTimeComponent(a[0], x => Integer(x.Day)));
                Define($"fn:timezone-from-{type}($arg as xs:{type}?) as xs:dayTimeDuration?", (c, a) => DateTimeComponent(a[0], Timezone));
            }
            foreach (string type in new[] { "dateTime", "time" })
            {
                Define($"fn:hours-from-{type}($arg as xs:{type}?) as xs:integer?", (c, a) => DateTimeComponent(a[0], x => Integer(x.Hour)));
                Define($"fn:minutes-from-{type}($arg as xs:{type}?) as xs:integer?", (c, a) => DateTimeComponent(a[0], x => Integer(x.Minute)));
                Define($"fn:seconds-from-{type}($arg as xs:{type}?) as xs:decimal?", (c, a) => DateTimeComponent(a[0], x => AtomicValue.OfDecimal(x.Second)));
            }
            Define("fn:timezone-from-time($arg as xs:time?) as xs:dayTimeDuration?", (c, a) => DateTimeComponent(a[0], Timezone));

            // Duration components keep the duration's sign.
            Define("fn:years-from-duration($arg as xs:duration?) as xs:integer?", (c, a) => DurationComponent(a[0], d => Integer(d.Months / 12)));
            Define("fn:months-from-duration($arg as xs:duration?) as xs:integer?", (c, a) => DurationComponent(a[0], d => Integer(d.Months % 12)));
            Define("fn:days-from-duration($arg as xs:duration?) as xs:integer?", (c, a) => DurationComponent(a[0], d => AtomicValue.OfInteger(new System.Numerics.BigInteger(decimal.Truncate(d.Seconds / 86400)))));
            Define("fn:hours-from-duration($arg as xs:duration?) as xs:integer?", (c, a) => DurationComponent(a[0], d => Integer((long)decimal.Truncate(d.Seconds % 86400 / 3600))));
            Define("fn:minutes-from-duration($arg as xs:duration?) as xs:integer?", (c, a) => DurationComponent(a[0], d => Integer((long)decimal.Truncate(d.Seconds % 3600 / 60))));
            Define("fn:seconds-from-duration($arg as xs:duration?) as xs:decimal?", (c, a) => DurationComponent(a[0], d => AtomicValue.OfDecimal(d.Seconds % 60)));

            Define("fn:current-dateTime() as xs:dateTimeStamp", (c, a) => One(new DateTimeValue(AtomicTypes.DateTimeStamp, c.CurrentDateTime)));
            Define("fn:current-date() as xs:date", (c, a) => One(Casting.Cast(new DateTimeValue(AtomicTypes.DateTime, c.CurrentDateTime), AtomicTypes.Date)));
            Define("fn:current-time() as xs:time", (c, a) => One(Casting.Cast(new DateTimeValue(AtomicTypes.DateTime, c.CurrentDateTime), AtomicTypes.Time)));
            Define("fn:implicit-timezone() as xs:dayTimeDuration", (c, a) => One(new DurationValue(AtomicTypes.DayTimeDuration, new XsDuration(0, c.ImplicitTimezoneMinutes * 60m))));

            Define("fn:dateTime($arg1 as xs:date?, $arg2 as xs:time?) as xs:dateTime?", (c, a) =>
            {
                if (Opt(a[0]) is not DateTimeValue d || Opt(a[1]) is not DateTimeValue t) return Empty;
                int? tz = d.DateTime.TimezoneMinutes, ttz = t.DateTime.TimezoneMinutes;
                if (tz != null && ttz != null && tz != ttz)
                {
                    throw new XdmException("FORG0008", "fn:dateTime: the date and time have different timezones");
                }
                XsDateTime x = d.DateTime, y = t.DateTime;
                return One(new DateTimeValue(AtomicTypes.DateTime, new XsDateTime(x.Year, x.Month, x.Day, y.Hour, y.Minute, y.Second, tz ?? ttz)));
            });

            foreach (string type in new[] { "dateTime", "date", "time" })
            {
                string t = type;
                Define($"fn:adjust-{t}-to-timezone($arg as xs:{t}?) as xs:{t}?", (c, a) =>
                    Opt(a[0]) is DateTimeValue v ? One(AdjustToTimezone(v, c.ImplicitTimezoneMinutes, c)) : Empty);
                Define($"fn:adjust-{t}-to-timezone($arg as xs:{t}?, $timezone as xs:dayTimeDuration?) as xs:{t}?", (c, a) =>
                {
                    if (Opt(a[0]) is not DateTimeValue v) return Empty;
                    if (Opt(a[1]) is not DurationValue tz) return One(new DateTimeValue(v.Type, v.DateTime.WithTimezone(null)));
                    decimal seconds = tz.Duration.Seconds;
                    if (seconds % 60 != 0 || System.Math.Abs(seconds) > 14 * 3600)
                    {
                        throw Err.FODT0003($"{tz.Value} is not a valid timezone");
                    }
                    return One(AdjustToTimezone(v, (int)(seconds / 60), c));
                });
            }
        }

        // A value without a timezone takes the target timezone unchanged; one with a timezone is shifted to it.
        private static DateTimeValue AdjustToTimezone(DateTimeValue v, int tz, FunctionContext c)
        {
            XsDateTime x = v.DateTime;
            Primitive kind = v.Type.Primitive;
            if (!x.HasTimezone)
            {
                return new DateTimeValue(v.Type, x.WithTimezone(tz));
            }
            decimal timeline = x.ToTimeline(kind == Primitive.Time ? Primitive.Time : Primitive.DateTime, 0);
            XsDateTime shifted = XsDateTime.FromTimeline(timeline, tz, tz);
            XsDateTime result = kind switch
            {
                Primitive.Date => new XsDateTime(shifted.Year, shifted.Month, shifted.Day, 0, 0, 0, tz),
                Primitive.Time => new XsDateTime(0, 0, 0, shifted.Hour, shifted.Minute, shifted.Second, tz),
                _ => shifted,
            };
            return new DateTimeValue(v.Type, result);
        }
    }
}
