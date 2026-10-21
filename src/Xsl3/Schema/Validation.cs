// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Xdm;

namespace Xsl3.Schema
{
    /// <summary>
    /// Xsl3 (P6): validation of input documents with System.Xml.Schema. .NET's built-in datatypes are narrower than
    /// XSD's: years outside 1 to 9999, decimals and integers beyond System.Decimal and durations beyond TimeSpan are
    /// rejected. Such a value is accepted when Xsl3's own casting accepts it for the same built-in type (the type
    /// annotation is kept, and typed values come from Xsl3's casting anyway).
    /// </summary>
    internal static partial class Validation
    {
        /// <summary>
        /// Reader settings that validate against <paramref name="schemas"/>: strictly (an element or attribute the
        /// schemas don't declare is an error) or laxly. <paramref name="error"/> makes the exception for an invalid
        /// document.
        /// </summary>
        public static XmlReaderSettings ReaderSettings(XmlSchemaSet schemas, bool strict, XmlReaderSettings? baseSettings, Func<string, Exception> error)
        {
            XmlReaderSettings settings = baseSettings?.Clone() ?? new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse };
            settings.ValidationType = ValidationType.Schema;
            settings.Schemas = schemas;
            settings.ValidationFlags |= XmlSchemaValidationFlags.ProcessIdentityConstraints | XmlSchemaValidationFlags.ReportValidationWarnings;
            settings.ValidationEventHandler += (sender, e) =>
            {
                // (Strict validation needs a declaration for the document element; below it, the schema says how strict.)
                if (e.Severity == XmlSeverityType.Warning ? strict && IsUndeclared(e) && sender is XmlReader { Depth: 0 } : !AcceptedByXdm(e))
                {
                    throw error(e.Message);
                }
            };
            return settings;
        }

        // A warning that an element or attribute has no declaration (lax validation skips it; strict fails).
        private static bool IsUndeclared(ValidationEventArgs e) => e.Exception is XmlSchemaValidationException;

        [GeneratedRegex(@"The value '(?<value>[^']*)' is invalid according to its datatype '(?<ns>[^']*):(?<local>[^':]+)'")]
        private static partial Regex DatatypeError();

        // A datatype error for a built-in type that XSD allows and only .NET's implementation rejects.
        private static bool AcceptedByXdm(ValidationEventArgs e)
        {
            Match m = DatatypeError().Match(e.Message);
            if (!m.Success || m.Groups["ns"].Value != XmlSchema.Namespace || AtomicTypes.Find(QName.Ns.Xs, m.Groups["local"].Value) is not { } type)
            {
                return false;
            }
            try
            {
                Casting.FromString(m.Groups["value"].Value, type);
                return true;
            }
            catch (XdmException)
            {
                return false;
            }
        }
    }
}
