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
