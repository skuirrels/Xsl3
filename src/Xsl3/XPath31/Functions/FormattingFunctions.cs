// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    internal static partial class FunctionLibrary
    {
        private static void RegisterFormatting()
        {
            // ---------------------------------------------------------- format-integer (F&O §4.6.1), format-number (§4.7)
            Define("fn:format-integer($value as xs:integer?, $picture as xs:string) as xs:string", (c, a) => FormatInteger(a[0], S(a[1]), null));
            Define("fn:format-integer($value as xs:integer?, $picture as xs:string, $lang as xs:string?) as xs:string", (c, a) => FormatInteger(a[0], S(a[1]), OptString(a[2])));
            Define("fn:format-number($value as xs:numeric?, $picture as xs:string) as xs:string", (c, a) =>
                Str(NumberFormatter.Format(OptAtomic(a[0]), S(a[1]), c.DecimalFormats.Unnamed)));
            Define("fn:format-number($value as xs:numeric?, $picture as xs:string, $decimal-format-name as xs:string?) as xs:string", (c, a) =>
                Str(NumberFormatter.Format(OptAtomic(a[0]), S(a[1]), c.DecimalFormats.Resolve(OptString(a[2])))));

            // ---------------------------------------------------------- format-dateTime and friends (§9.8)
            foreach (string type in new[] { "dateTime", "date", "time" })
            {
                Define($"fn:format-{type}($value as xs:{type}?, $picture as xs:string) as xs:string?", (c, a) => FormatDate(a[0], S(a[1]), null, null, null));
                Define($"fn:format-{type}($value as xs:{type}?, $picture as xs:string, $language as xs:string?, $calendar as xs:string?, $place as xs:string?) as xs:string?", (c, a) =>
                    FormatDate(a[0], S(a[1]), OptString(a[2]), OptString(a[3]), OptString(a[4])));
            }
        }

        private static string? OptString(IList<XPathItem> arg) => arg.Count == 0 ? null : ((AtomicValue)arg[0]).Value;

        private static AtomicValue? OptAtomic(IList<XPathItem> arg) => arg.Count == 0 ? null : (AtomicValue)arg[0];

        private static IList<XPathItem> FormatInteger(IList<XPathItem> value, string picture, string? language)
        {
            var formatter = new IntegerFormatter(picture);
            return Str(value.Count == 0 ? "" : formatter.Format(((IntegerValue)value[0]).Integer, language));
        }

        private static IList<XPathItem> FormatDate(IList<XPathItem> value, string picture, string? language, string? calendar, string? place)
        {
            if (value.Count == 0)
            {
                return Empty;
            }
            var v = (DateTimeValue)value[0];
            return Str(DateFormatter.Format(v.DateTime, v.Type.Primitive, picture, language, calendar, place));
        }
    }
}
