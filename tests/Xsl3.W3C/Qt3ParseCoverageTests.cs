// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xsl3.W3C.Qt3;
using Xsl3.XPath31;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.W3C;

/// <summary>
/// Parse coverage over QT3 (docs/plan/p1-xdm-xpath.md): every XPath 3.1 test whose expected outcome isn't a
/// syntax error must parse, and every test whose only acceptable outcome is XPST0003 must be rejected.
/// Tests that accept XPST0003 among other outcomes are not counted.
/// </summary>
public class Qt3ParseCoverageTests
{
    private readonly ITestOutputHelper _output;

    public Qt3ParseCoverageTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ParseCoverage()
    {
        string? root = Catalog.Locate();
        if (root == null)
        {
            _output.WriteLine("QT3 not found (run eng/fetch-w3c.sh); skipping.");
            return;
        }

        Catalog catalog = Catalog.Load(root);
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        var perSet = new SortedDictionary<string, (int Total, int Failed)>(StringComparer.Ordinal);
        int mustReject = 0;

        foreach (TestCase test in catalog.TestCases.Where(Profile.ForParsing.Applies))
        {
            IReadOnlyList<string>? errors = test.ExpectedErrorsOnly();
            bool expectSyntaxError = errors != null && errors.All(c => c == "XPST0003");
            if (!expectSyntaxError && test.AcceptsError("XPST0003"))
            {
                continue; // either outcome is acceptable
            }

            if (expectSyntaxError)
            {
                mustReject++;
            }

            string? failure = null;
            try
            {
                Parser.ParseXPath(test.Expression);
                if (expectSyntaxError)
                {
                    failure = "parsed, but XPST0003 was expected";
                }
            }
            catch (XPathStaticException e) when (!expectSyntaxError && errors?.Contains(e.Code) != true)
            {
                failure = e.Message;
            }
            catch (XPathStaticException)
            {
                // expected
            }
            catch (Exception e)
            {
                failure = "crashed: " + e.GetType().Name + ": " + e.Message;
            }

            var (total, failed) = perSet.GetValueOrDefault(test.Set.Name);
            perSet[test.Set.Name] = (total + 1, failed + (failure != null ? 1 : 0));
            if (failure != null)
            {
                failures[test.Id] = failure.ReplaceLineEndings(" ");
            }
        }

        int all = perSet.Values.Sum(v => v.Total);
        int failedAll = perSet.Values.Sum(v => v.Failed);
        string summary = $"QT3 parse coverage: {all - failedAll} / {all} ({100.0 * (all - failedAll) / all:F2}%), of which {mustReject} must be rejected with XPST0003";
        _output.WriteLine(summary);
        WriteReport(summary, perSet, failures);

        string problems = Baseline.Check("qt3-parse", failures);
        Assert.True(problems.Length == 0, problems);
    }

    private static void WriteReport(string summary, SortedDictionary<string, (int Total, int Failed)> perSet, Dictionary<string, string> failures)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# " + summary).AppendLine();
        sb.AppendLine("| Test set | Tests | Failed |").AppendLine("|---|---:|---:|");
        foreach (var (set, (total, failed)) in perSet.Where(kv => kv.Value.Failed > 0))
        {
            sb.AppendLine($"| {set} | {total} | {failed} |");
        }
        sb.AppendLine().AppendLine("## Failures").AppendLine();
        foreach (var (id, reason) in failures.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"- `{id}`: {reason}");
        }
        string dir = Path.Combine(AppContext.BaseDirectory, "w3c-reports");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "qt3-parse.md"), sb.ToString());
    }
}
