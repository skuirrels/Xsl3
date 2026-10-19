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
        Features = new HashSet<string>(StringComparer.Ordinal) { "namespace_axis", "dtd", "disabling_output_escaping", "serialization" },
    };

    /// <summary>P3: a basic XSLT 3.0 processor with XPath 3.1 and higher-order functions.</summary>
    public static Profile Xslt30 { get; } = new()
    {
        Specs = new HashSet<string>(StringComparer.Ordinal) { "XSLT10+", "XSLT20+", "XSLT30+" },
        Features = new HashSet<string>(StringComparer.Ordinal)
        {
            "namespace_axis", "dtd", "disabling_output_escaping", "serialization", "higher_order_functions", "XPath_3.1",
        },
    };

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
            // xsl:use-package chooses the highest version in range (implementation-defined, XSLT 3.0 §3.5.2).
            "package_version_resolution" => d.Value is "highest_version" or "unspecified",
            "xml-version" => d.Value.Split(' ').Any(v => v is "1.0" or "1.0:5+"),
            "enable_assertions" or "sweep_and_posture" or "combinations_for_numbering" or "additional_normalization_form" => false,
            _ => true,
        };
        return met == d.Satisfied;
    }
}
