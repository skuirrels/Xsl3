// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Xsl3.W3C;

/// <summary>
/// The known failures of a conformance run, committed under tests/Xsl3.W3C/baselines. A run fails on any
/// failure that isn't listed (a regression) and on any listed test that now passes (a stale baseline, so
/// progress is always recorded). Set XSL3_UPDATE_BASELINES=1 to rewrite the file from the current run.
/// </summary>
public static class Baseline
{
    public static string PathOf(string name)
    {
        // Baselines live in the source tree, not the output directory.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "Xsl3.W3C", "baselines");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(candidate, name + ".txt");
            }
        }
        throw new DirectoryNotFoundException("tests/Xsl3.W3C/baselines not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Compares <paramref name="failures"/> (test id → reason) with the baseline; returns problems, empty if none.</summary>
    public static string Check(string name, IReadOnlyDictionary<string, string> failures)
    {
        string path = PathOf(name);
        if (Environment.GetEnvironmentVariable("XSL3_UPDATE_BASELINES") == "1")
        {
            File.WriteAllLines(path, new[] { $"# Known failures for {name}. Regenerate with XSL3_UPDATE_BASELINES=1; see tests/Xsl3.W3C/Baseline.cs." }
                .Concat(failures.Keys.Order(StringComparer.Ordinal)));
            return "";
        }

        var known = File.Exists(path)
            ? File.ReadAllLines(path).Where(l => l.Length > 0 && !l.StartsWith('#')).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var regressions = failures.Keys.Where(k => !known.Contains(k)).Order(StringComparer.Ordinal).ToList();
        var fixedNow = known.Where(k => !failures.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();

        var problems = new List<string>();
        if (regressions.Count > 0)
        {
            problems.Add($"{regressions.Count} new failure(s):\n" + string.Join("\n", regressions.Take(200).Select(k => $"  {k}: {failures[k]}")));
        }
        if (fixedNow.Count > 0)
        {
            problems.Add($"{fixedNow.Count} test(s) in the baseline now pass; regenerate it with XSL3_UPDATE_BASELINES=1:\n" + string.Join("\n", fixedNow.Take(50).Select(k => "  " + k)));
        }
        return string.Join("\n\n", problems);
    }
}
