// Licensed under the MIT license. See LICENSE.TXT in the repository root.

namespace Xsl3.Xdm
{
    /// <summary>
    /// A dynamic or type error raised while evaluating XPath (SPEC §16.7), identified by its code in the
    /// <c>http://www.w3.org/2005/xqt-errors</c> namespace (<c>FORG0001</c>, <c>XPTY0004</c>, …).
    /// </summary>
    /// <remarks>
    /// Internal for now. When the public XPath API lands this becomes (or is wrapped by) the public
    /// XsltDynamicException with a QName code, an error object and a stylesheet stack trace.
    /// </remarks>
    internal sealed class XdmException : Exception
    {
        public const string ErrorNamespace = "http://www.w3.org/2005/xqt-errors";

        public XdmException(string code, string message)
            : base($"{code}: {message}")
        {
            Code = code;
        }

        /// <summary>The local part of the error code.</summary>
        public string Code { get; }

        // A streamed validation error can be caught around its source instruction, not inside its body.
        internal Streaming.StreamedDocument? ValidationSource { get; init; }

        // An actual accumulator dependency cycle, distinct from fn:error using the same error QName.
        internal bool AccumulatorCycle { get; init; }

        /// <summary>The error code, if it isn't in the err namespace (fn:error with a QName).</summary>
        public QName? Name { get; init; }

        /// <summary>The error object of fn:error, if any.</summary>
        public System.Collections.Generic.IList<System.Xml.XPath.XPathItem>? ErrorObject { get; init; }

        /// <summary>The description, without the code.</summary>
        public string? Description { get; init; }
    }

    /// <summary>The errors of XPath 3.1 Appendix F and F&amp;O 3.1 Appendix C used by the XDM layer.</summary>
    internal static class Err
    {
        /// <summary>A value does not match a required type (type error).</summary>
        public static XdmException XPTY0004(string message) => new("XPTY0004", message);

        /// <summary>Casting from a source to a target type is not allowed.</summary>
        public static XdmException XPTY0004Cast(AtomicType from, AtomicType to) =>
            new("XPTY0004", $"Casting from {from.Name} to {to.Name} is not allowed");

        /// <summary>The target of a cast is xs:NOTATION, xs:anyAtomicType or abstract.</summary>
        public static XdmException XPST0080(AtomicType to) => new("XPST0080", $"Cannot cast to {to.Name}");

        /// <summary>An unknown atomic type name.</summary>
        public static XdmException XPST0051(string name) => new("XPST0051", $"{name} is not a known atomic type");

        /// <summary>Invalid value for cast/constructor.</summary>
        public static XdmException FORG0001(string value, AtomicType type) =>
            new("FORG0001", $"'{Truncate(value)}' is not a valid {type.Name}");

        public static XdmException FORG0001(string message) => new("FORG0001", message);

        /// <summary>Division by zero.</summary>
        public static XdmException FOAR0001() => new("FOAR0001", "Division by zero");

        /// <summary>Numeric operation overflow/underflow.</summary>
        public static XdmException FOAR0002(string what) => new("FOAR0002", $"Numeric overflow: {what}");

        /// <summary>Input value too large for decimal.</summary>
        public static XdmException FOCA0001(string value) => new("FOCA0001", $"'{Truncate(value)}' is too large for xs:decimal");

        /// <summary>Invalid lexical value.</summary>
        public static XdmException FOCA0002(string message) => new("FOCA0002", message);

        /// <summary>Input value too large for integer.</summary>
        public static XdmException FOCA0003(string value) => new("FOCA0003", $"'{Truncate(value)}' is too large for xs:integer");

        /// <summary>NaN supplied as a float/double value where it isn't allowed.</summary>
        public static XdmException FOCA0005() => new("FOCA0005", "NaN supplied as a numeric operand");

        /// <summary>Overflow/underflow in date/time operation.</summary>
        public static XdmException FODT0001(string what) => new("FODT0001", $"Date/time overflow: {what}");

        /// <summary>Overflow/underflow in duration operation.</summary>
        public static XdmException FODT0002(string what) => new("FODT0002", $"Duration overflow: {what}");

        /// <summary>Invalid timezone value.</summary>
        public static XdmException FODT0003(string what) => new("FODT0003", $"Invalid timezone: {what}");

        private static string Truncate(string s) => s.Length > 80 ? s[..77] + "..." : s;
    }
}
