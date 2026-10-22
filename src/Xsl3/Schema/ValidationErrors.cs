// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Xml.Schema;

namespace Xsl3.Schema;

internal static class ValidationErrors
{
    internal static bool IsIdConstraint(string message) => message.Contains("already used as an ID", StringComparison.Ordinal)
        || message.Contains("undeclared ID", StringComparison.Ordinal);

    // Unique/key/keyref constraints are checked during element assessment (§25.4.1.3).
    // XTTE1555 applies to the additional document-wide ID/IDREF constraints (§25.4.2).
    internal static bool IsDocumentConstraint(ValidationEventArgs error) => IsIdConstraint(error.Message);

    internal static string Code(string fallback, bool strict, string message, bool documentConstraint) => documentConstraint ? "XTTE1555"
        : !strict && fallback == "XTTE1510" && message.Contains("according to its datatype", StringComparison.Ordinal) ? "XTTE1515" : fallback;
}
