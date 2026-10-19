// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Xsl3.W3C.Qt3;

/// <summary>
/// What the processor under test claims, used to decide which QT3 test cases apply (the suite's
/// dependency mechanism). A test case applies when every one of its effective dependencies is met.
/// </summary>
public sealed class Profile
{
    /// <summary>The language under test, as a spec token: XPath 3.1.</summary>
    public string Spec { get; init; } = "XP31";

    /// <summary>Optional features the processor supports (catalog feature names).</summary>
    public ISet<string> Features { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Unknown dependency types: whether to treat them as met.</summary>
    public bool AssumeUnknownDependenciesMet { get; init; } = true;

    /// <summary>
    /// Parse coverage only needs the language and the XML version, since features change what an
    /// expression means, not whether it parses.
    /// </summary>
    public static Profile ForParsing { get; } = new() { Features = new HashSet<string> { "*" } };

    /// <summary>
    /// What Xsl3 claims today (docs/conformance.md). Grows as features land: higherOrderFunctions in P3,
    /// schemaImport/schemaValidation in P6. staticTyping is never claimed (SPEC §4.2 doesn't require it).
    /// </summary>
    public static Profile Xsl3 { get; } = new()
    {
        Features = new HashSet<string>(StringComparer.Ordinal) { "namespace-axis", "infoset-dtd", "higherOrderFunctions" },
    };

    public bool Applies(TestCase testCase) => testCase.EffectiveDependencies.All(Meets);

    private bool Meets(Dependency d)
    {
        bool met = d.Type switch
        {
            "spec" => MeetsSpec(d.Value),
            "feature" => Features.Contains("*") || Features.Contains(d.Value),
            // Xsl3 checks names against XML 1.0 Fifth Edition (and Namespaces 1.0).
            "xml-version" => d.Value.Split(' ').Any(v => v is "1.0" or "1.0:5+"),
            // XSD 1.0 only (SPEC §18).
            "xsd-version" => d.Value == "1.0",
            // Month and day names exist for English and German; other languages fall back to English.
            "language" => d.Value is "en" or "de",
            "default-language" => d.Value == "en",
            // Casing and character data follow Unicode 15.0 (eng/gen-casing.py, the CLR's tables).
            "unicode-version" => d.Value == "15.0",
            "unicode-normalization-form" => d.Value is "NFC" or "NFD" or "NFKC" or "NFKD",
            "format-integer-sequence" => d.Value is "①" or "⑴" or "⒈" or "一" or "α" or "Α",
            _ => AssumeUnknownDependenciesMet,
        };
        return met == d.Satisfied;
    }

    // Values are space-separated alternatives: "XP20+ XQ10+" means XPath 2.0 or later, or XQuery 1.0 or later.
    private bool MeetsSpec(string value)
    {
        int ours = Version(Spec);
        foreach (string token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!token.StartsWith(Spec[..2], StringComparison.Ordinal))
            {
                continue; // a different language (XQ.., XT..)
            }
            bool orLater = token.EndsWith('+');
            int version = Version(token.TrimEnd('+'));
            if (version == ours || (orLater && version < ours))
            {
                return true;
            }
        }
        return false;
    }

    private static int Version(string token) => int.Parse(token[2..], System.Globalization.CultureInfo.InvariantCulture);
}
