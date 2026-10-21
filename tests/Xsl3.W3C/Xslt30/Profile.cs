// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Xsl3.W3C.Xslt30;

/// <summary>
/// Which xslt30-test cases apply to Xsl3 today, from their dependencies (the suite's catalog-schema.xsd lists
/// the kinds). Grows with each phase: P2 claims an XSLT 2.0 processor's behaviour on a 3.0 processor, so the
/// tests that apply are those for "XSLT 1.0 or later" and "XSLT 2.0 or later"; tests of 2.0-only behaviour
/// that 3.0 changed ("XSLT20") don't apply to a 3.0 processor.
/// </summary>
public sealed class Profile
{
    /// <summary>The spec tokens Xsl3 satisfies.</summary>
    public IReadOnlySet<string> Specs { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlySet<string> Features { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public static Profile Xslt20 { get; } = new()
    {
        Specs = new HashSet<string>(StringComparer.Ordinal) { "XSLT10+", "XSLT20+" },
        Features = new HashSet<string>(StringComparer.Ordinal) { "namespace_axis", "dtd", "disabling_output_escaping", "serialization", "xsl-stylesheet-processing-instruction", "backwards_compatibility" },
    };

    /// <summary>P3: a basic XSLT 3.0 processor with XPath 3.1 and higher-order functions; P5 adds dynamic evaluation.</summary>
    public static Profile Xslt30 { get; } = new()
    {
        Specs = new HashSet<string>(StringComparer.Ordinal) { "XSLT10+", "XSLT20+", "XSLT30+" },
        Features = new HashSet<string>(StringComparer.Ordinal)
        {
            "namespace_axis", "dtd", "disabling_output_escaping", "serialization", "higher_order_functions", "XPath_3.1",
            // XsltCompiler.GetAssociatedStylesheet: the source's xml-stylesheet processing instruction names the stylesheet.
            "xsl-stylesheet-processing-instruction",
            // version="1.0" stylesheets run on the 3.0 processor in backwards-compatible mode (XsltVersionMode.BackwardsCompatible).
            "backwards_compatibility",
            // P5: xsl:evaluate (the runner enables it with XsltSettings.EnableEvaluate).
            "dynamic_evaluation",
            // P6: schema-awareness (the runner turns on XsltSettings.SchemaAware, loads the environments' schemas and
            // validates their sources; docs/plan/p6-schema.md).
            "schema_aware",
        },
    };

    /// <summary>P6 (docs/plan/p6-schema.md): the 3.0 profile; XSL3_XSLT30_PROFILE=sa runs only the tests that need schema-awareness.</summary>
    public static Profile SchemaAware => Xslt30;

    public bool Applies(TestCase test) => test.EffectiveDependencies.All(Meets);

    private bool Meets(Dependency d)
    {
        bool met = d.Type switch
        {
            "spec" => d.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(Specs.Contains),
            "feature" => Features.Contains(d.Value),
            // Years above 9999 and negative years are supported; year zero is XSD 1.1's, not XSD 1.0's.
            "year_component_values" => d.Value is "support year above 9999" or "support negative year",
            "default_language" or "languages_for_numbering" => d.Value == "en",
            "default_calendar" => d.Value is "AD" or "ISO",
            // Upstream recovers from conflicting template rules by choosing the last.
            "on-multiple-match" => d.Value == "recover",
            "xsd-version" => d.Value == "1.0",
            // Regex character classes use .NET's Unicode tables, newer than any version the suite tests (6.0, 9.0).
            "unicode-version" => false,
            // xsl:use-package chooses the highest version in range (implementation-defined, XSLT 3.0 §3.5.2).
            "package_version_resolution" => d.Value is "highest_version" or "unspecified",
            "xml-version" => d.Value.Split(' ').Any(v => v is "1.0" or "1.0:5+"),
            // Assertions can be on or off: the runner sets XsltSettings.EnableAssertions as the test asks.
            "enable_assertions" => d.Satisfied,
            "sweep_and_posture" or "combinations_for_numbering" or "extension-function" or "ordinal_scheme_name" => false,
            // NFD, NFKC and NFKD are supported, besides NFC.
            "additional_normalization_form" => true,
            // .NET decimal: 28 significant digits.
            "maximum_number_of_decimal_digits" => int.TryParse(d.Value, out int digits) && digits <= 28,
            // The html method's default html-version is 4 (5 when html-version says so).
            "default_html_version" => d.Value == "4",
            "supported_calendars_in_date_formatting_functions" => d.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(c => c is "AD" or "ISO"),
            // unparsed-text defaults to UTF-8; documents on the web aren't fetched; doc() failures aren't ignored.
            "unparsed_text_encoding" => d.Value.Equals("utf-8", StringComparison.OrdinalIgnoreCase),
            "available_documents" or "ignore_doc_failure" => false,
            _ => true,
        };
        return met == d.Satisfied;
    }
}
