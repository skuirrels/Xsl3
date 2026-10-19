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
/// Runs every xslt30-test case that applies to what Xsl3 claims (<see cref="Xslt30.Profile.Xslt20"/> in P2) and
/// compares the failures with baselines/xslt30-run.txt. The report goes to w3c-reports/xslt30-run.md next
/// to the test binaries; docs/conformance.md summarises it.
/// </summary>
public class Xslt30RunTests
{
    private readonly ITestOutputHelper _output;

    public Xslt30RunTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Xslt30Conformance()
    {
        string? root = Xslt30.Catalog.Locate();
        if (root == null)
        {
            _output.WriteLine("xslt30-test not found (run eng/fetch-w3c.sh); skipping.");
            return;
        }

        Xslt30.Catalog catalog = Xslt30.Catalog.Load(root);
        var runner = new Xslt30.Runner();
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        var perSet = new SortedDictionary<string, int[]>(StringComparer.Ordinal); // pass, fail, not run
        int wrongErrorCode = 0;
        // XSL3_XSLT30_TRACE=file appends each test id before running it, to find a test that crashes the process.
        string? trace = Environment.GetEnvironmentVariable("XSL3_XSLT30_TRACE");

        // XSL3_XSLT30_SETS=sequence,for-each-group runs only those test sets (prefixes); the baseline is then not checked.
        string[]? sets = Environment.GetEnvironmentVariable("XSL3_XSLT30_SETS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // The unicode-90 sets loop over every Unicode character in documents of tens of megabytes: hours of run time
        // for character classes that QT3 covers. They run only when named in XSL3_XSLT30_SETS.
        bool Routine(Xslt30.TestCase t) => sets != null || !t.Set.Name.StartsWith("unicode-90", StringComparison.Ordinal);
        foreach (Xslt30.TestCase test in catalog.TestCases.Where(Xslt30.Profile.Xslt20.Applies).Where(Routine).Where(t => sets == null || sets.Any(s => t.Set.Name.StartsWith(s, StringComparison.Ordinal))))
        {
            if (trace != null)
            {
                File.AppendAllText(trace, test.Id + Environment.NewLine);
            }
            Outcome outcome = runner.Run(test);
            int[] counts = perSet.TryGetValue(test.Set.Name, out int[]? c) ? c : perSet[test.Set.Name] = new int[3];
            counts[(int)outcome.Kind]++;
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
        string summary = $"xslt30-test (XSLT 2.0 profile): {pass} passed, {fail} failed, {notRun} not run; {100.0 * pass / Math.Max(1, pass + fail):F1}% of those run ({wrongErrorCode} expected errors passed without a matching code)";
        _output.WriteLine(summary);
        WriteReport(summary, perSet, failures);
        if (sets != null)
        {
            return;
        }
        string problems = Baseline.Check("xslt30-run", failures);
        Assert.True(problems.Length == 0, problems);
    }

    private static void WriteReport(string summary, SortedDictionary<string, int[]> perSet, Dictionary<string, string> failures)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# " + summary).AppendLine();
        sb.AppendLine("| Test set | Passed | Failed | Not run | Pass rate |").AppendLine("|---|---:|---:|---:|---:|");
        foreach (var (set, c) in perSet)
        {
            sb.AppendLine($"| {set} | {c[0]} | {c[1]} | {c[2]} | {(c[0] + c[1] == 0 ? "" : $"{100.0 * c[0] / (c[0] + c[1]):F1}%")} |");
        }
        sb.AppendLine().AppendLine("## Failures").AppendLine();
        foreach (var (id, reason) in failures.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"- `{id}`: {reason}");
        }
        string dir = Path.Combine(AppContext.BaseDirectory, "w3c-reports");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "xslt30-run.md"), sb.ToString());
    }
}
