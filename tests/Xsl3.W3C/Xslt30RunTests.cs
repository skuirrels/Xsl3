// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xsl3.W3C.Qt3;
using Xsl3.W3C.Xslt30;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.W3C;

/// <summary>
/// Runs every xslt30-test case that applies to what Xsl3 claims (<see cref="Xslt30.Profile.Xslt30"/> in P3) and
/// compares the failures with baselines/xslt30-run.txt. The report (per test set and per feature) goes to
/// w3c-reports/xslt30-run.md next to the test binaries (xslt30-run-2.0.md for the 2.0 profile);
/// eng/conformance-report.sh assembles the reports into docs/conformance-sets.md.
/// </summary>
public partial class Xslt30RunTests
{
    private readonly ITestOutputHelper _output;

    public Xslt30RunTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Xslt30Conformance()
    {
        string? requestedProfile = Environment.GetEnvironmentVariable("XSL3_XSLT30_PROFILE");
        string? root = Xslt30.Catalog.Locate();
        if (root == null)
        {
            Assert.False(requestedProfile is "sandp" or "strm" or "s2" or "sandp-reviewed" or "s2-reviewed", "The streaming development profiles require xslt30-test; run eng/fetch-w3c.sh.");
            _output.WriteLine("xslt30-test not found (run eng/fetch-w3c.sh); skipping.");
            return;
        }

        bool reviewed = requestedProfile is "sandp-reviewed" or "s2-reviewed";
        bool sandp = requestedProfile is "sandp" or "sandp-reviewed";
        string? prepared = Environment.GetEnvironmentVariable("XSL3_XSLT30_REVIEWED");
        if (reviewed)
        {
            Assert.False(string.IsNullOrEmpty(prepared), "Prepare reviewed fixtures and set XSL3_XSLT30_REVIEWED.");
            Assert.Null(Environment.GetEnvironmentVariable("XSL3_XSLT30_SETS"));
        }
        Xslt30.Catalog catalog = reviewed ? Xslt30.Catalog.LoadReviewed(root, prepared!)
            : Xslt30.Catalog.Load(root, includePostureAndSweep: sandp);
        if (requestedProfile is "s2" or "s2-reviewed")
        {
            RunStaticDiagnostics(catalog, reviewed);
            return;
        }
        string? assessmentPath = Environment.GetEnvironmentVariable("XSL3_XSLT30_ASSESSMENTS");
        using StreamWriter? assessments = assessmentPath == null ? null : new StreamWriter(assessmentPath, append: false);
        var runner = new Xslt30.Runner
        {
            AnalysisCompleted = assessments == null ? null : (test, values) =>
            {
                foreach (var (expression, value) in values)
                    assessments.WriteLine(JsonSerializer.Serialize(new
                    {
                        Case = test.Id, Part = (string?)expression.Attribute("part"), Expression = expression.Value,
                        Type = value.Type.ToString(), Posture = value.Posture.ToString(), Sweep = value.Sweep.ToString(), value.Rule,
                    }));
            },
        };
        if (reviewed)
        {
            Assert.Equal(8, catalog.RetiredAnalysisCases.Count);
            foreach (var invalid in catalog.RetiredAnalysisCases)
            {
                Outcome rejected = new Xslt30.Runner().Run(invalid);
                Assert.Equal(OutcomeKind.Fail, rejected.Kind);
                Assert.Contains("no expressions", rejected.Detail);
            }
        }
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        var perSet = new SortedDictionary<string, int[]>(StringComparer.Ordinal); // pass, fail, not run
        var perFeature = new SortedDictionary<string, int[]>(StringComparer.Ordinal); // the same, by spec and feature
        int wrongErrorCode = 0;
        var notRunReasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        // XSL3_XSLT30_TRACE=file appends each test id before running it, to find a test that crashes the process.
        string? trace = Environment.GetEnvironmentVariable("XSL3_XSLT30_TRACE");
        // XSL3_XSLT30_CODES=file appends each test that expected an error and raised another code, with the codes.
        string? codes = Environment.GetEnvironmentVariable("XSL3_XSLT30_CODES");

        // XSL3_XSLT30_SETS=sequence,for-each-group runs only those test sets (prefixes); the baseline is then not checked.
        string[]? sets = Environment.GetEnvironmentVariable("XSL3_XSLT30_SETS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // The unicode-90 sets loop over every Unicode character in documents of tens of megabytes: hours of run time
        // for character classes that QT3 covers. They run only when named in XSL3_XSLT30_SETS.
        bool Routine(Xslt30.TestCase t) => sets != null || !t.Set.Name.StartsWith("unicode-90", StringComparison.Ordinal);
        // XSL3_XSLT30_PROFILE=2.0 runs the P2 profile (then the baseline is not checked).
        bool p2 = requestedProfile == "2.0";
        // XSL3_XSLT30_PROFILE=sa runs the tests that need schema-awareness (P6, docs/plan/p6-schema.md), unchecked.
        bool sa = requestedProfile == "sa";
        // P7: separate reports and baselines for unstreamed streaming tests and static expression analysis.
        bool strm = requestedProfile == "strm";
        Xslt30.Profile profile = p2 ? Xslt30.Profile.Xslt20 : sa ? Xslt30.Profile.SchemaAware
            : strm || sandp ? Xslt30.Profile.Streaming : Xslt30.Profile.Xslt30;
        string? caseReportPath = Environment.GetEnvironmentVariable("XSL3_XSLT30_RESULTS");
        using RuntimeCaseReport? caseReport = caseReportPath == null ? null : new RuntimeCaseReport(
            caseReportPath, root, requestedProfile ?? "3.0", Environment.GetEnvironmentVariable("XSL3_STREAM_STRICT") == "1");
        bool Selected(Xslt30.TestCase t) => sandp ? Requires(t, "sweep_and_posture", "supports-sweep-and-posture-assessments")
            : strm ? Requires(t, "feature", "streaming") : !sa || Requires(t, "feature", "schema_aware");
        foreach (Xslt30.TestCase test in catalog.TestCases.Where(profile.Applies).Where(Routine).Where(Selected).Where(t => sets == null || sets.Any(s => t.Set.Name.StartsWith(s, StringComparison.Ordinal))))
        {
            if (trace != null)
            {
                File.AppendAllText(trace, test.Id + Environment.NewLine);
            }
            Outcome outcome = runner.Run(test);
            caseReport?.Record(test, outcome);
            int[] counts = perSet.TryGetValue(test.Set.Name, out int[]? c) ? c : perSet[test.Set.Name] = new int[3];
            counts[(int)outcome.Kind]++;
            foreach (Xslt30.Dependency d in test.EffectiveDependencies.Where(d => d.Satisfied && d.Type is "spec" or "feature"))
            {
                foreach (string value in d.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string key = $"{d.Type} {value}";
                    (perFeature.TryGetValue(key, out int[]? f) ? f : perFeature[key] = new int[3])[(int)outcome.Kind]++;
                }
            }
            if (outcome.Kind == OutcomeKind.Fail)
            {
                failures[test.Id] = outcome.Detail.ReplaceLineEndings(" ");
            }
            else if (outcome.Kind == OutcomeKind.NotRun)
            {
                notRunReasons[outcome.Detail] = notRunReasons.GetValueOrDefault(outcome.Detail) + 1;
                if (sandp)
                {
                    // Losing analysis coverage must not look like a fixed failure in the baseline.
                    failures[test.Id] = "not run: " + outcome.Detail.ReplaceLineEndings(" ");
                }
            }
            else if (outcome.Kind == OutcomeKind.Pass && outcome.Detail.StartsWith("error code", StringComparison.Ordinal))
            {
                wrongErrorCode++;
                if (codes != null)
                {
                    File.AppendAllText(codes, $"{test.Id}: {outcome.Detail}{Environment.NewLine}");
                }
            }
        }

        int pass = perSet.Values.Sum(v => v[0]), fail = perSet.Values.Sum(v => v[1]), notRun = perSet.Values.Sum(v => v[2]);
        bool strictExecution = !sandp && Environment.GetEnvironmentVariable("XSL3_STREAM_STRICT") == "1";
        bool streamedSources = strm && strictExecution;
        string streamingProfile = streamedSources ? "3.0 streaming development, STRICT FORWARD INPUTS" : "3.0 streaming development, UNSTREAMED";
        string profileName = p2 ? "2.0" : sa ? "3.0 schema-aware" : strm ? streamingProfile : sandp ? "3.0 posture and sweep" : "3.0";
        if (reviewed) profileName += ", P7B REVIEWED";
        if (strictExecution && !strm) profileName += ", STRICT FORWARD INPUTS";
        string summary = $"xslt30-test (XSLT {profileName} profile): {pass} passed, {fail} failed, {notRun} not run; {100.0 * pass / Math.Max(1, pass + fail):F1}% of those run ({wrongErrorCode} expected errors passed without a matching code)";
        _output.WriteLine(summary);
        string report = p2 ? "xslt30-run-2.0" : sa ? "xslt30-run-sa" : streamedSources ? "xslt30-run-strm-strict" : strm ? "xslt30-run-strm" : sandp ? "xslt30-run-sandp" : "xslt30-run";
        if (reviewed) report += "-reviewed";
        if (strictExecution && !strm) report += "-strict";
        WriteReport(report + ".md", summary, perSet, perFeature, failures, notRunReasons);
        caseReport?.Complete(pass, fail, notRun, wrongErrorCode, notRunReasons);
        Assert.True(perSet.Count > 0, "No test cases selected; check the profile, set filter and suite checkout.");
        if (streamedSources && sets == null)
            CheckStrictStreamingCoverage(pass + fail + notRun, wrongErrorCode, notRunReasons);
        if (sandp) Assert.True(notRun == 0, $"Posture-and-sweep coverage regressed: {notRun} cases were not run.");
        if (sandp && sets == null)
            Assert.True(pass + fail == (reviewed ? 1000 : 919), $"P7B posture/sweep scope changed: got {pass + fail}; reconcile the manifest and plan.");
        if (reviewed)
        {
            Assert.True(fail == 0, "Reviewed fixture regression: " + string.Join("; ", failures.Select(f => f.Key + ": " + f.Value)));
            return;
        }
        if (sets != null || p2 || sa)
        {
            return;
        }
        string problems = Baseline.Check(report, failures);
        Assert.True(problems.Length == 0, problems);
    }

    internal static bool Requires(Xslt30.TestCase test, string type, string value) => test.EffectiveDependencies
        .Any(d => d.Satisfied && d.Type == type && d.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(value, StringComparer.Ordinal));

    internal static void CheckStrictStreamingCoverage(int selected, int unmatchedCodes, IReadOnlyDictionary<string, int> notRunReasons)
    {
        // P7C freezes the complete pinned-suite population, including its one unsupported collection
        // (docs/plan/p7c-results.json: 2695 passed + 16 failed + 1 not run).
        // A failure-ID baseline alone cannot detect lost passing cases, new skips or wrong-code passes.
        Assert.True(selected == 2712, $"Strict-forward population changed: {selected}; reconcile the P7C inventory.");
        Assert.True(unmatchedCodes == 0, $"Strict-forward expected errors lost their exact codes: {unmatchedCodes}.");
        Assert.True(notRunReasons.Count == 1 && notRunReasons.TryGetValue("environment: collection", out int count) && count == 1,
            "Strict-forward not-run coverage changed: " + string.Join("; ", notRunReasons.Select(reason => $"{reason.Key} ({reason.Value})")));
    }

    private static void WriteReport(string file, string summary, SortedDictionary<string, int[]> perSet, SortedDictionary<string, int[]> perFeature,
        Dictionary<string, string> failures, SortedDictionary<string, int> notRunReasons)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# " + summary).AppendLine();
        if (notRunReasons.Count > 0)
            sb.AppendLine("Not run: " + string.Join(", ", notRunReasons.Select(kv => $"{kv.Key} ({kv.Value})"))).AppendLine();
        // The specs and features tests depend on (a test counts under each of its own and its set's dependencies).
        sb.AppendLine("| Dependency | Passed | Failed | Not run | Pass rate |").AppendLine("|---|---:|---:|---:|---:|");
        foreach (var (feature, c) in perFeature)
        {
            sb.AppendLine($"| {feature} | {c[0]} | {c[1]} | {c[2]} | {Rate(c)} |");
        }
        sb.AppendLine();
        sb.AppendLine("| Test set | Passed | Failed | Not run | Pass rate |").AppendLine("|---|---:|---:|---:|---:|");
        foreach (var (set, c) in perSet)
        {
            sb.AppendLine($"| {set} | {c[0]} | {c[1]} | {c[2]} | {Rate(c)} |");
        }
        sb.AppendLine().AppendLine("## Failures").AppendLine();
        foreach (var (id, reason) in failures.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"- `{id}`: {reason}");
        }
        string dir = Path.Combine(AppContext.BaseDirectory, "w3c-reports");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, file), sb.ToString());
    }

    private static string Rate(int[] c) => c[0] + c[1] == 0 ? "" : $"{100.0 * c[0] / (c[0] + c[1]):F1}%";
}
