// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    /// <summary>
    /// The dynamic context that built-in functions see (XPath 3.1 §2.1.2): implicit timezone, current
    /// dateTime, default collation, document order. One per evaluation, so fn:current-dateTime() is stable.
    /// </summary>
    internal sealed class FunctionContext
    {
        private XsDateTime? _now;
        private readonly System.Func<XPathNavigator, XPathNavigator, int> _comparePosition;

        public FunctionContext(int implicitTimezoneMinutes, System.Func<XPathNavigator, XPathNavigator, int> comparePosition)
        {
            ImplicitTimezoneMinutes = implicitTimezoneMinutes;
            _comparePosition = comparePosition;
        }

        public int ImplicitTimezoneMinutes { get; }

        public IComparer<string> DefaultCollation { get; init; } = Comparison.CodepointCollation;

        /// <summary>The query runtime, for documents and node identity; null outside a compiled evaluation.</summary>
        public Xsl3.Runtime.XmlQueryRuntime? Runtime { get; init; }

        /// <summary>The static base URI, against which fn:doc and friends resolve relative URIs.</summary>
        public string? StaticBaseUri { get; init; }

        /// <summary>The decimal formats fn:format-number resolves its third argument against.</summary>
        public DecimalFormatTable DecimalFormats { get; init; } = DecimalFormatTable.Empty;

        /// <summary>fn:current-dateTime(): fixed for the whole evaluation, in the implicit timezone.</summary>
        public XsDateTime CurrentDateTime => _now ??= Now();

        private XsDateTime Now()
        {
            System.DateTimeOffset now = System.DateTimeOffset.UtcNow.ToOffset(System.TimeSpan.FromMinutes(ImplicitTimezoneMinutes));
            decimal seconds = now.Second + now.Millisecond / 1000m;
            return new XsDateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, seconds, ImplicitTimezoneMinutes);
        }

        /// <summary>Document order across all trees, stable for the evaluation (as upstream's DocumentOrderComparer).</summary>
        public int ComparePosition(XPathNavigator a, XPathNavigator b) => _comparePosition(a, b);

        /// <summary>The local timezone offset, in minutes, used as the implicit timezone when none is configured.</summary>
        public static int LocalTimezoneMinutes => (int)System.TimeZoneInfo.Local.GetUtcOffset(System.DateTime.UtcNow).TotalMinutes;
    }
}
