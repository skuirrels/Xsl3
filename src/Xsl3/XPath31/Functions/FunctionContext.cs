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
        private System.Lazy<XsDateTime> _clock;
        private readonly System.Func<XPathNavigator, XPathNavigator, int> _comparePosition;

        public FunctionContext(int implicitTimezoneMinutes, System.Func<XPathNavigator, XPathNavigator, int> comparePosition)
            : this(new ImplicitTimezone(implicitTimezoneMinutes), comparePosition) { }

        internal FunctionContext(ImplicitTimezone timezone, System.Func<XPathNavigator, XPathNavigator, int> comparePosition)
            : this(timezone, comparePosition, null) { }

        private FunctionContext(ImplicitTimezone timezone, System.Func<XPathNavigator, XPathNavigator, int> comparePosition,
            System.Lazy<XsDateTime>? clock)
        {
            Timezone = timezone;
            _comparePosition = comparePosition;
            _clock = clock ?? new System.Lazy<XsDateTime>(Now);
        }

        internal ImplicitTimezone Timezone { get; }
        public int ImplicitTimezoneMinutes => Timezone.Minutes;

        public Collation DefaultCollation { get; init; } = Collation.Codepoint;

        /// <summary>The query runtime, for documents and node identity; null outside a compiled evaluation.</summary>
        public Xsl3.Runtime.XmlQueryRuntime? Runtime { get; init; }

        /// <summary>The static base URI, against which fn:doc and friends resolve relative URIs.</summary>
        public string? StaticBaseUri { get; init; }

        /// <summary>The decimal formats fn:format-number resolves its third argument against.</summary>
        public DecimalFormatTable DecimalFormats { get; init; } = DecimalFormatTable.Empty;

        /// <summary>This dynamic context with the static parts of one expression's context.</summary>
        public FunctionContext WithStatic(StaticInfo info) => new(Timezone, _comparePosition, _clock)
        {
            DefaultCollation = Collation.Resolve(info.DefaultCollation),
            Runtime = Runtime,
            StaticBaseUri = info.BaseUri,
            DecimalFormats = info.DecimalFormats,
        };

        /// <summary>This context with another default collation (for functions given an explicit collation).</summary>
        public FunctionContext WithDefaultCollation(Collation collation) => new(Timezone, _comparePosition, _clock)
        {
            DefaultCollation = collation,
            Runtime = Runtime,
            StaticBaseUri = StaticBaseUri,
            DecimalFormats = DecimalFormats,
        };

        /// <summary>A captured built-in function's static context in an isolated fork runtime.</summary>
        internal FunctionContext WithRuntime(Xsl3.Runtime.XmlQueryRuntime runtime) =>
            new(Timezone, (a, b) => runtime.ComparePosition(a, b), _clock)
            {
                DefaultCollation = DefaultCollation,
                Runtime = runtime,
                StaticBaseUri = StaticBaseUri,
                DecimalFormats = DecimalFormats,
            };

        /// <summary>A current dateTime fixed by the caller (an expression evaluated in a host's execution scope).</summary>
        public XsDateTime? FixedCurrentDateTime
        {
            init
            {
                if (value is XsDateTime fixedTime) _clock = new System.Lazy<XsDateTime>(() => fixedTime);
            }
        }

        /// <summary>fn:current-dateTime(): fixed for the whole evaluation, in the implicit timezone.</summary>
        public XsDateTime CurrentDateTime => _clock.Value;

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
