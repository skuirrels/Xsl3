// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
public class Xslt30RunTests
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
            Assert.False(requestedProfile is "sandp" or "strm", "The streaming development profiles require xslt30-test; run eng/fetch-w3c.sh.");
            _output.WriteLine("xslt30-test not found (run eng/fetch-w3c.sh); skipping.");
            return;
        }

        bool sandp = requestedProfile == "sandp";
        Xslt30.Catalog catalog = Xslt30.Catalog.Load(root, includePostureAndSweep: sandp);
        var runner = new Xslt30.Runner();
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
        bool Selected(Xslt30.TestCase t) => sandp ? Requires(t, "sweep_and_posture", "supports-sweep-and-posture-assessments")
            : strm ? Requires(t, "feature", "streaming") : !sa || Requires(t, "feature", "schema_aware");
        foreach (Xslt30.TestCase test in catalog.TestCases.Where(profile.Applies).Where(Routine).Where(Selected).Where(t => sets == null || sets.Any(s => t.Set.Name.StartsWith(s, StringComparison.Ordinal))))
        {
            if (trace != null)
            {
                File.AppendAllText(trace, test.Id + Environment.NewLine);
            }
            Outcome outcome = runner.Run(test);
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
        string streamingProfile = streamedSources ? "3.0 streaming development, STRICT SOURCE ENTRIES" : "3.0 streaming development, UNSTREAMED";
        string profileName = p2 ? "2.0" : sa ? "3.0 schema-aware" : strm ? streamingProfile : sandp ? "3.0 posture and sweep" : "3.0";
        if (strictExecution && !strm) profileName += ", STRICT SOURCE ENTRIES";
        string summary = $"xslt30-test (XSLT {profileName} profile): {pass} passed, {fail} failed, {notRun} not run; {100.0 * pass / Math.Max(1, pass + fail):F1}% of those run ({wrongErrorCode} expected errors passed without a matching code)";
        _output.WriteLine(summary);
        string report = p2 ? "xslt30-run-2.0" : sa ? "xslt30-run-sa" : streamedSources ? "xslt30-run-strm-strict" : strm ? "xslt30-run-strm" : sandp ? "xslt30-run-sandp" : "xslt30-run";
        if (strictExecution && !strm) report += "-strict";
        WriteReport(report + ".md", summary, perSet, perFeature, failures, notRunReasons);
        Assert.True(perSet.Count > 0, "No test cases selected; check the profile, set filter and suite checkout.");
        if (sandp) Assert.True(notRun == 0, $"Posture-and-sweep coverage regressed: {notRun} cases were not run.");
        if (sets != null || p2 || sa)
        {
            return;
        }
        string problems = Baseline.Check(report, failures);
        Assert.True(problems.Length == 0, problems);
    }

    internal static bool Requires(Xslt30.TestCase test, string type, string value) => test.EffectiveDependencies
        .Any(d => d.Satisfied && d.Type == type && d.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(value, StringComparer.Ordinal));

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
