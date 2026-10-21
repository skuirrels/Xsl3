// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xsl3.W3C.Qt3;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.W3C;

/// <summary>
/// Runs every QT3 test that applies to what Xsl3 claims (<see cref="Profile.Xsl3"/>) and compares the
/// failures with baselines/qt3-run.txt. The report (pass rates per test set) goes to w3c-reports/qt3-run.md
/// next to the test binaries; docs/conformance.md summarises it.
/// </summary>
public class Qt3RunTests
{
    private readonly ITestOutputHelper _output;

    public Qt3RunTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Qt3Conformance()
    {
        string? root = Catalog.Locate();
        if (root == null)
        {
            _output.WriteLine("QT3 not found (run eng/fetch-w3c.sh); skipping.");
            return;
        }

        Catalog catalog = Catalog.Load(root);
        var runner = new Runner();
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        var perSet = new SortedDictionary<string, int[]>(StringComparer.Ordinal); // pass, fail, not run
        var perFeature = new SortedDictionary<string, int[]>(StringComparer.Ordinal); // the same, by spec and feature
        int wrongErrorCode = 0;

        // XSL3_QT3_SETS=fn-format-date,fn-format-time runs only those test sets (prefixes), for quick iteration;
        // the baseline is then not checked.
        string[]? sets = Environment.GetEnvironmentVariable("XSL3_QT3_SETS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (TestCase test in catalog.TestCases.Where(Profile.Xsl3.Applies).Where(t => sets == null || sets.Any(s => t.Set.Name.StartsWith(s, StringComparison.Ordinal))))
        {
            Outcome outcome = runner.Run(test);
            int[] counts = perSet.TryGetValue(test.Set.Name, out int[]? c) ? c : perSet[test.Set.Name] = new int[3];
            counts[(int)outcome.Kind]++;
            foreach (Dependency d in test.EffectiveDependencies.Where(d => d.Satisfied && d.Type is "spec" or "feature"))
            {
                foreach (string value in d.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(v => d.Type != "spec" || v.StartsWith("XP", StringComparison.Ordinal)))
                {
                    string key = $"{d.Type} {value}";
                    (perFeature.TryGetValue(key, out int[]? f) ? f : perFeature[key] = new int[3])[(int)outcome.Kind]++;
                }
            }
            if (outcome.Kind == OutcomeKind.Fail)
            {
                failures[test.Id] = outcome.Detail.ReplaceLineEndings(" ");
            }
            else if (outcome.Kind == OutcomeKind.Pass && outcome.Detail.StartsWith("error code", StringComparison.Ordinal))
            {
                wrongErrorCode++;
            }
        }

        int pass = perSet.Values.Sum(v => v[0]), fail = perSet.Values.Sum(v => v[1]), notRun = perSet.Values.Sum(v => v[2]);
        string summary = $"QT3 (XPath 3.1): {pass} passed, {fail} failed, {notRun} not run; {100.0 * pass / Math.Max(1, pass + fail):F1}% of those run ({wrongErrorCode} passes raised a different error code)";
        _output.WriteLine(summary);
        WriteReport(summary, perSet, perFeature, failures);

        if (sets != null)
        {
            return;
        }
        string problems = Baseline.Check("qt3-run", failures);
        Assert.True(problems.Length == 0, problems);
    }

    private static void WriteReport(string summary, SortedDictionary<string, int[]> perSet, SortedDictionary<string, int[]> perFeature, Dictionary<string, string> failures)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# " + summary).AppendLine();
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
        File.WriteAllText(Path.Combine(dir, "qt3-run.md"), sb.ToString());
    }

    private static string Rate(int[] c) => c[0] + c[1] == 0 ? "" : $"{100.0 * c[0] / (c[0] + c[1]):F1}%";
}
