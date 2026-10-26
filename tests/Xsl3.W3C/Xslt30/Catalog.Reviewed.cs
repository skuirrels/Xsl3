// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Xsl3.W3C.Xslt30;

public sealed partial class Catalog
{
    internal string? ReviewManifestHash { get; private set; }
    internal IReadOnlyList<TestCase> RetiredAnalysisCases { get; private set; } = Array.Empty<TestCase>();
    internal IReadOnlyDictionary<string, string> ReviewedOriginalCodes { get; private set; } = new Dictionary<string, string>();

    // An explicit test-data view over the ordinary loader. Neither the official checkout nor
    // compiler behavior is modified. Every original expression must have exactly one destination.
    internal static Catalog LoadReviewed(string source, string prepared)
    {
        string manifestFile = Path.Combine(prepared, "review.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestFile));
        JsonElement manifest = document.RootElement;
        Require(manifest.GetProperty("Version").GetInt32() == 1, "Unsupported reviewed suite format");
        VerifyHashes(source, manifest.GetProperty("SourceHashes"));
        VerifyHashes(prepared, manifest.GetProperty("GeneratedHashes"));
        Catalog catalog = Load(source, includePostureAndSweep: true);
        var rawSets = catalog.TestSets.Where(s => s.TestCases.Any(IsAnalysis)).ToDictionary(s => Path.GetFullPath(s.File), StringComparer.Ordinal);
        var originals = rawSets.Values.SelectMany(s => s.TestCases).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var replacements = new Dictionary<string, TestSet>(StringComparer.Ordinal);
        foreach (JsonElement set in manifest.GetProperty("Sets").EnumerateArray())
        {
            string rawFile = Within(source, set.GetProperty("OriginalFile").GetString()!);
            string reviewedFile = Within(prepared, set.GetProperty("ReviewedFile").GetString()!);
            Require(rawSets.ContainsKey(rawFile), "Unknown original analysis set: " + rawFile);
            Require(manifest.GetProperty("SourceHashes").TryGetProperty(set.GetProperty("OriginalFile").GetString()!, out _), "Unhashed source set");
            Require(manifest.GetProperty("GeneratedHashes").TryGetProperty(set.GetProperty("ReviewedFile").GetString()!, out _), "Unhashed reviewed set");
            replacements.Add(rawFile, TestSet.Load(catalog, null, reviewedFile));
        }
        Require(replacements.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(rawSets.Keys), "Incomplete analysis-set mapping");
        catalog.TestSets = catalog.TestSets.Select(s => replacements.GetValueOrDefault(Path.GetFullPath(s.File), s)).ToArray();
        var reviewed = replacements.Values.SelectMany(s => s.TestCases).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var mappedOriginals = new HashSet<string>(StringComparer.Ordinal);
        var mappedReviewed = new HashSet<string>(StringComparer.Ordinal);
        var retired = new List<TestCase>();
        int expressions = 0;
        foreach (JsonElement mapping in manifest.GetProperty("Mappings").EnumerateArray())
        {
            string id = mapping.GetProperty("OriginalId").GetString()!;
            Require(mappedOriginals.Add(id) && originals.ContainsKey(id), "Unknown/duplicate original case: " + id);
            TestCase original = originals[id];
            XElement input = original.TestElement.Element(Ns + "posture-and-sweep")!;
            Require(input.Elements().All(e => e.Name == Ns + "xpath"), "Unsupported source analysis input");
            var parts = input.Elements().ToDictionary(e => (string)e.Attribute("part")!, StringComparer.Ordinal);
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            JsonElement groups = mapping.GetProperty("Groups");
            if (groups.GetArrayLength() == 0)
            {
                Require(parts.Count == 0 && mapping.TryGetProperty("InvalidReason", out _), "A nonempty source case cannot be retired");
                retired.Add(original);
            }
            foreach (JsonElement group in groups.EnumerateArray())
            {
                string target = group.GetProperty("ReviewedId").GetString()!;
                Require(mappedReviewed.Add(target) && reviewed.ContainsKey(target), "Unknown/duplicate reviewed case: " + target);
                TestCase corrected = reviewed[target];
                XElement correctedInput = corrected.TestElement.Element(Ns + "posture-and-sweep")!;
                Require(Attributes(input) == Attributes(correctedInput), "Changed analysis context: " + target);
                string[] members = group.GetProperty("Parts").EnumerateArray().Select(e => e.GetString()!).ToArray();
                XElement[] actual = correctedInput.Elements().ToArray();
                Require(members.Length > 0 && members.SequenceEqual(actual.Select(e => (string?)e.Attribute("part"))), "Changed/empty part group: " + target);
                Require(corrected.Result.Name == Ns + "assert-posture-and-sweep"
                    && (string?)corrected.Result.Attribute("result-posture") == group.GetProperty("Posture").GetString()
                    && (string?)corrected.Result.Attribute("result-sweep") == group.GetProperty("Sweep").GetString(), "Changed reviewed assertion: " + target);
                foreach (var (part, expression) in members.Zip(actual))
                {
                    Require(claimed.Add(part) && parts.ContainsKey(part), "Unknown/duplicate original part: " + id + "/" + part);
                    XElement before = parts[part];
                    Require(expression.Name == before.Name && expression.Value == before.Value && Attributes(expression) == Attributes(before), "Changed source expression: " + target);
                    foreach (string prefix in before.AncestorsAndSelf().Attributes().Where(a => a.IsNamespaceDeclaration && a.Name.LocalName != "xmlns").Select(a => a.Name.LocalName).Distinct())
                        Require(before.GetNamespaceOfPrefix(prefix) == expression.GetNamespaceOfPrefix(prefix), "Changed namespace binding: " + target + "/" + prefix);
                    expressions++;
                }
            }
            Require(claimed.SetEquals(parts.Keys), "Incomplete source-part mapping: " + id);
        }
        Require(mappedOriginals.SetEquals(originals.Keys) && mappedReviewed.SetEquals(reviewed.Keys), "Incomplete case mapping");
        Require(manifest.GetProperty("OriginalCases").GetInt32() == originals.Count
            && manifest.GetProperty("ReviewedCases").GetInt32() == reviewed.Count
            && manifest.GetProperty("Expressions").GetInt32() == expressions, "Incorrect reviewed population metadata");
        Require(retired.Select(t => t.Id).ToHashSet(StringComparer.Ordinal).SetEquals(
            manifest.GetProperty("InvalidCases").EnumerateArray().Select(e => e.GetString()!)), "Incorrect invalid-source inventory");
        catalog.RetiredAnalysisCases = retired;
        var codes = new Dictionary<string, string>(StringComparer.Ordinal);
        var allCases = catalog.TestCases.ToDictionary(t => t.Id, StringComparer.Ordinal);
        foreach (JsonElement correction in manifest.GetProperty("StaticCorrections").EnumerateArray())
        {
            string id = correction.GetProperty("OriginalId").GetString()!;
            TestCase test = allCases[id];
            string code = correction.GetProperty("OriginalExpectedCode").GetString()!;
            Require(test.Result.Name == Ns + "error" && (string?)test.Result.Attribute("code") == code, "Changed original static expectation: " + id);
            string file = correction.GetProperty("AssertionFile").GetString()!;
            Require(manifest.GetProperty("GeneratedHashes").TryGetProperty(file, out _), "Unhashed result assertion");
            XElement assertion = XElement.Load(Within(prepared, file), LoadOptions.PreserveWhitespace);
            Require(assertion.Name == Ns + "assert-xml", "A reviewed positive case must assert its actual XML result");
            test.Result.Name = assertion.Name;
            test.Result.ReplaceAttributes(assertion.Attributes());
            test.Result.ReplaceNodes(assertion.Nodes());
            if (correction.GetProperty("StylesheetFile").GetString() is { } stylesheet)
            {
                Require(manifest.GetProperty("GeneratedHashes").TryGetProperty(stylesheet, out _), "Unhashed reviewed stylesheet");
                test.TestElement.Element(Ns + "stylesheet")!.SetAttributeValue("file", Within(prepared, stylesheet));
            }
            codes.Add(id, code);
        }
        catalog.ReviewedOriginalCodes = codes;
        catalog.ReviewManifestHash = Hash(manifestFile);
        return catalog;
    }

    private static bool IsAnalysis(TestCase test) => test.TestElement.Element(Ns + "posture-and-sweep") != null;
    private static string Attributes(XElement element) => string.Join("\n", element.Attributes().Where(a => !a.IsNamespaceDeclaration)
        .OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).Select(a => a.Name + "=" + a.Value));
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    private static string Within(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = Path.GetFullPath(root);
        if (!Path.EndsInDirectorySeparator(prefix)) prefix += Path.DirectorySeparatorChar;
        Require(!Path.IsPathRooted(relative) && path.StartsWith(prefix, StringComparison.Ordinal), "Fixture path escapes its root");
        return path;
    }
    private static void VerifyHashes(string root, JsonElement hashes)
    {
        Require(hashes.EnumerateObject().Any(), "Missing fixture hashes");
        foreach (JsonProperty entry in hashes.EnumerateObject())
            Require(Hash(Within(root, entry.Name)) == entry.Value.GetString(), "Fixture hash changed: " + entry.Name);
    }
}
