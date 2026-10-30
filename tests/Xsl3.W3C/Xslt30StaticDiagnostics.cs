// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using CompilerError = Xsl3.Xslt.CompilerError;
using CompilerErrorCollection = Xsl3.Xslt.CompilerErrorCollection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Xsl3.W3C.Qt3;
using Xunit;

namespace Xsl3.W3C;

public partial class Xslt30RunTests
{
    internal sealed record StaticDiagnostic(string? Code, bool IsStatic, string? ModuleUri, int Line, int Column, string? Reason);

    // Compare the reported primary diagnostic, never a code mentioned in explanatory text or an
    // unrelated inner exception. Keep the ordinary W3C runner's historical accounting separate.
    internal static StaticDiagnostic PrimaryDiagnostic(Exception? error, CompilerErrorCollection? compilationErrors = null)
    {
        if (error != null && compilationErrors?.Cast<CompilerError>().FirstOrDefault(e => !e.IsWarning) is { } primary)
        {
            var actual = new XsltStaticError(primary);
            return new(actual.Code?.EQName, true, actual.ModuleUri, actual.LineNumber, actual.ColumnNumber, actual.Message);
        }
        for (Exception? e = error; e != null; e = e.InnerException)
        {
            if (e is XsltStaticException compilation)
            {
                XsltStaticError? first = compilation.Errors.FirstOrDefault(d => !d.IsWarning);
                return new(first?.Code?.EQName, true, first?.ModuleUri, first?.LineNumber ?? 0,
                    first?.ColumnNumber ?? 0, first?.Message ?? compilation.Message);
            }
            if (e is XsltDynamicException dynamic)
                return new(dynamic.Code.EQName, false, null, 0, 0, dynamic.Description);
            if (e is Xdm.XdmException xdm)
                return new(xdm.Name?.EQName, false, null, 0, 0, xdm.Message);
        }
        return new(null, false, null, 0, 0, error?.Message);
    }

    internal static bool MatchesStaticCode(StaticDiagnostic actual, string code) =>
        actual.IsStatic && actual.Code == new Xdm.QName("http://www.w3.org/2005/xqt-errors", code).EQName;

    private void RunStaticDiagnostics(Xslt30.Catalog catalog, bool reviewed)
    {
        XNamespace ns = Xslt30.Catalog.Ns;
        bool Target(XElement e) => e.Name == ns + "error" && (string?)e.Attribute("code") is "XTSE3430" or "XTSE3155";
        var tests = catalog.TestCases.Where(Xslt30.Profile.Streaming.Applies)
            .Where(t => t.Result.DescendantsAndSelf().Any(Target) || catalog.ReviewedOriginalCodes.ContainsKey(t.Id)).OrderBy(t => t.Id, StringComparer.Ordinal).ToArray();
        // Sourced from the pinned catalogue inventory, not a selectable subset of passing cases.
        Assert.True(tests.Length == 155, $"P7B catalogue scope changed: expected 155 cases, got {tests.Length}; reconcile the manifest and plan.");
        var failures = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var records = new List<object>();
        int matchingCodes = 0, allowedResults = 0, notRun = 0;
        foreach (Xslt30.TestCase test in tests)
        {
            Exception? raised = null;
            bool executed = false;
            var runner = new Xslt30.Runner { ExecutionCompleted = (_, error) => { executed = true; raised = error; } };
            Outcome outcome = runner.Run(test);
            StaticDiagnostic actual = PrimaryDiagnostic(raised);
            string[] expected = test.Result.DescendantsAndSelf().Where(Target).Select(e => (string)e.Attribute("code")!).Distinct().ToArray();
            bool required = Target(test.Result);
            bool exact = executed && expected.Any(code => MatchesStaticCode(actual, code));
            bool located = !string.IsNullOrEmpty(actual.ModuleUri) && actual.Line > 0 && actual.Column > 0 && !string.IsNullOrEmpty(actual.Reason);
            bool result = executed && !required && raised == null && outcome.Kind == OutcomeKind.Pass;
            bool pass = outcome.Kind == OutcomeKind.Pass && (exact && located || result);
            if (pass && exact) matchingCodes++;
            if (pass && result) allowedResults++;
            if (outcome.Kind == OutcomeKind.NotRun) notRun++;
            string expectedDescription = expected.Length == 0 ? "reviewed XML result" : string.Join(" or ", expected) + (required ? "" : " or allowed result");
            string detail = $"expected {expectedDescription}; " +
                $"actual {actual.Code ?? (executed && raised == null ? "result" : "no coded diagnostic")}; {outcome.Detail}";
            if (exact && !located) detail += "; static diagnostic lacks a source location or reason";
            if (!pass) failures.Add(test.Id, detail);
            records.Add(new { Case = test.Id, Expected = expected, OriginalCode = catalog.ReviewedOriginalCodes.GetValueOrDefault(test.Id), Required = required, Executed = executed,
                Outcome = pass ? "Pass" : outcome.Kind == OutcomeKind.NotRun ? "NotRun" : "Fail", Actual = actual, Detail = detail });
        }
        string summary = $"XSLT 3.0 exact static streamability: {tests.Length - failures.Count} passed, {failures.Count} failed " +
            $"(including {notRun} not run); {matchingCodes} matching static codes, {allowedResults} allowed results";
        if (reviewed) summary += "; P7B REVIEWED";
        string directory = Path.Combine(AppContext.BaseDirectory, "w3c-reports");
        Directory.CreateDirectory(directory);
        string report = "xslt30-static-streamability" + (reviewed ? "-reviewed" : "");
        File.WriteAllText(Path.Combine(directory, report + ".json"),
            JsonSerializer.Serialize(new { Summary = summary, catalog.ReviewManifestHash, Cases = records }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllLines(Path.Combine(directory, report + ".md"),
            new[] { "# " + summary, "", reviewed ? "Reviewed expectations; original raw-suite results are retained separately." : "A matching failure baseline does not satisfy the conformance gate.", "", "## Failures", "" }
                .Concat(failures.Select(f => $"- `{f.Key}`: {f.Value}")));
        _output.WriteLine(summary);
        Assert.Equal(0, notRun);
        Assert.True(failures.Count < tests.Length, "No exact-code or permitted-result cases passed.");
        if (reviewed)
        {
            Assert.Empty(failures);
            Assert.True(matchingCodes == 152 && allowedResults == 3,
                $"P7B diagnostic/result coverage changed: got {matchingCodes} exact codes and {allowedResults} results; reconcile the manifest and plan.");
            return;
        }
        string problems = Baseline.Check("xslt30-static-streamability", failures);
        Assert.True(problems.Length == 0, problems);
    }
}
