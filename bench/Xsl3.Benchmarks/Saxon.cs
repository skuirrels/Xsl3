// Licensed under the MIT license. See LICENSE.TXT in the repository root.

#if SAXON
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using S = Saxon.Api;

namespace Xsl3.Benchmarks;

/// <summary>
/// The SaxonCS-HE comparison (remaining-scope 3, SPEC §19.5–19.6): the XSLT 3.0 micro suite on Xsl3 and on SaxonCS-HE,
/// "The Saxon XSLT and XQuery Processor from Saxonica Limited" (https://www.saxonica.com/), MPL-2.0. Built with
/// <c>-p:Saxon=true</c> for .NET 10 only. Each engine runs on its own tree, parsed once, and serializes to
/// <see cref="Stream.Null"/>. Cold start (compile and first transform) and peak working set come from a fresh process
/// per run; "xsl3-saved" is Xsl3 loading a stylesheet saved with <c>CompileToAssembly</c> (Sink B).
/// </summary>
public static class SaxonComparison
{
    public sealed record Row(string Name, string Size, string Engine, double MedianMs, double MinMs, long AllocatedBytes, int Runs, long PeakWorkingSet = 0);

    private static readonly S.Processor s_processor = new(false);

    private static string Path30(string name) => Path.Combine(AppContext.BaseDirectory, "corpus", "xslt30", "micro", name + ".xsl");

    private static string Xml(string size)
    {
        (int orders, int products) = Micro.Sizes[size];
        return Corpus.Orders(orders, products);
    }

    /// <summary>
    /// saxon [--sizes 1kb,1mb] [--ms budget] [--cold-runs n] [--json file] [name...]: checks that both engines give
    /// the same output, then compares compile time, steady-state transform time and allocation, cold start and peak
    /// working set.
    /// </summary>
    public static void Run(string[] args)
    {
        string[] sizes = { "1kb", "1mb" };
        int budget = 1000, coldRuns = 5;
        string? json = null;
        var names = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--sizes": sizes = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries).Where(Micro.Sizes.ContainsKey).ToArray(); break;
                case "--ms": budget = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--cold-runs": coldRuns = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--json": json = args[++i]; break;
                default: names.Add(args[i]); break;
            }
        }
        if (names.Count == 0)
        {
            names.AddRange(Micro.Names);
        }

        string xsl3Version = typeof(XslCompiledTransform).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        Console.WriteLine($"Xsl3 {xsl3Version}; {s_processor.ProductTitle} {s_processor.ProductVersion} ({s_processor.Edition}); .NET {Environment.Version}");

        // The same output from both, or the row isn't a comparison.
        var comparable = new List<string>();
        foreach (string name in names)
        {
            string ours = TransformXsl3(name, Xml("1kb")), theirs;
            try
            {
                theirs = TransformSaxon(name, Xml("1kb"));
            }
            catch (Exception e)
            {
                Console.WriteLine($"{name,-12} SaxonCS-HE failed: {e.GetType().Name}: {e.Message}");
                continue;
            }
            bool same = Normalize(ours) == Normalize(theirs);
            Console.WriteLine($"{name,-12} output {(same ? "identical" : $"differs ({ours.Length} and {theirs.Length} characters)")}");
            if (!same)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), $"xsl3-{name}.out"), ours);
                File.WriteAllText(Path.Combine(Path.GetTempPath(), $"saxon-{name}.out"), theirs);
            }
            comparable.Add(name);
        }

        var rows = new List<Row>();
        foreach (string name in comparable)
        {
            rows.Add(Measure(name, "compile", "xsl3", budget, () => new XslCompiledTransform().Load(Path30(name))));
            rows.Add(Measure(name, "compile", "saxon", budget, () => s_processor.NewXsltCompiler().Compile(new Uri(Path30(name)))));
        }
        foreach (string size in sizes)
        {
            string xml = Xml(size);
            Tree.CompactDocument ourInput = Tree.CompactDocument.Load(new StringReader(xml));
            S.XdmNode theirInput = SaxonInput(xml);
            foreach (string name in comparable)
            {
                var ours = new XslCompiledTransform();
                ours.Load(Path30(name));
                rows.Add(Measure(name, size, "xsl3", budget, () => ours.Transform(ourInput, null, Stream.Null)));
                S.Xslt30Transformer theirs = s_processor.NewXsltCompiler().Compile(new Uri(Path30(name))).Load30();
                theirs.GlobalContextItem = theirInput;
                rows.Add(Measure(name, size, "saxon", budget, () => theirs.ApplyTemplates(theirInput, s_processor.NewSerializer(Stream.Null))));
            }
        }

        // Cold start and peak working set, a fresh process per run.
        string saved = Path.Combine(Path.GetTempPath(), "xsl3-saxon-saved");
        Directory.CreateDirectory(saved);
        string coldSize = sizes.Contains("1mb") ? "1mb" : sizes.FirstOrDefault() ?? "1kb";
        foreach (string name in comparable)
        {
            SaveAssembly(name, saved);
            foreach (string engine in new[] { "xsl3", "xsl3-saved", "saxon" })
            {
                var samples = Enumerable.Range(0, coldRuns).Select(_ => Child(engine, name, coldSize, saved)).ToList();
                var times = samples.Select(s => s.ColdMs).ToList();
                rows.Add(new Row(name, "cold", engine, Median(times), times.Min(), 0, coldRuns, (long)Median(samples.Select(s => (double)s.PeakWorkingSet).ToList())));
            }
        }

        Report(rows, coldSize);
        if (json != null)
        {
            File.WriteAllText(json, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    /// <summary>
    /// saxon-child engine name size dir: in a fresh process, the time to compile (or load) the stylesheet and run its
    /// first transform on an input parsed beforehand, then ten more transforms, and the process's peak working set.
    /// </summary>
    public static void RunChild(string[] args)
    {
        string engine = args[0], name = args[1], size = args[2], dir = args[3];
        string xml = Xml(size);
        Action first;
        Action again;
        switch (engine)
        {
            case "saxon":
            {
                S.XdmNode input = SaxonInput(xml);
                S.Xslt30Transformer? transformer = null;
                first = () =>
                {
                    transformer = s_processor.NewXsltCompiler().Compile(new Uri(Path30(name))).Load30();
                    transformer.GlobalContextItem = input;
                    transformer.ApplyTemplates(input, s_processor.NewSerializer(Stream.Null));
                };
                again = () => transformer!.ApplyTemplates(input, s_processor.NewSerializer(Stream.Null));
                break;
            }
            default:
            {
                Tree.CompactDocument input = Tree.CompactDocument.Load(new StringReader(xml));
                var transform = new XslCompiledTransform();
                first = () =>
                {
                    if (engine == "xsl3-saved")
                    {
                        Type type = Assembly.LoadFrom(Path.Combine(dir, $"Saved.{name}.dll")).GetType($"Saved.{TypeName(name)}", throwOnError: true)!;
                        transform.Load(type, new XsltSettings());
                    }
                    else
                    {
                        transform.Load(Path30(name));
                    }
                    transform.Transform(input, null, Stream.Null);
                };
                again = () => transform.Transform(input, null, Stream.Null);
                break;
            }
        }
        var watch = Stopwatch.StartNew();
        first();
        double cold = watch.Elapsed.TotalMilliseconds;
        for (int i = 0; i < 10; i++)
        {
            again();
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{cold} {PeakWorkingSet()}"));
    }

    // Process.PeakWorkingSet64 is 0 on macOS; there, and on Linux, getrusage's ru_maxrss (bytes on macOS, KB on Linux).
    private static long PeakWorkingSet()
    {
        if (OperatingSystem.IsWindows())
        {
            using Process self = Process.GetCurrentProcess();
            return self.PeakWorkingSet64;
        }
        var usage = new long[18]; // struct rusage: two timevals, then ru_maxrss
        if (getrusage(0, usage) != 0)
        {
            return 0;
        }
        return OperatingSystem.IsMacOS() ? usage[4] : usage[4] * 1024;
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int getrusage(int who, long[] usage);

    private static (double ColdMs, long PeakWorkingSet) Child(string engine, string name, string size, string dir)
    {
        string self = Environment.ProcessPath!;
        var start = new ProcessStartInfo(self) { RedirectStandardOutput = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(self) == "dotnet")
        {
            start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        }
        foreach (string arg in new[] { "saxon-child", engine, name, size, dir })
        {
            start.ArgumentList.Add(arg);
        }
        using Process child = Process.Start(start)!;
        string output = child.StandardOutput.ReadToEnd();
        child.WaitForExit();
        string[] parts = output.Trim().Split('\n')[^1].Split(' ');
        if (child.ExitCode != 0 || parts.Length != 2)
        {
            throw new InvalidOperationException($"saxon-child {engine} {name} failed: {output}");
        }
        return (double.Parse(parts[0], CultureInfo.InvariantCulture), long.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static string TypeName(string name) => name.Replace("-", "_", StringComparison.Ordinal);

    private static void SaveAssembly(string name, string dir)
    {
        using XmlReader reader = XmlReader.Create(Path30(name));
        using FileStream output = File.Create(Path.Combine(dir, $"Saved.{name}.dll"));
        new XslCompiledTransform().CompileToAssembly(reader, new XsltSettings(), new XmlUrlResolver(), $"Saved.{name}", $"Saved.{TypeName(name)}", output);
    }

    private static S.XdmNode SaxonInput(string xml)
    {
        S.DocumentBuilder builder = s_processor.NewDocumentBuilder();
        builder.BaseUri = new Uri("file:///orders.xml");
        // The same tree as Xsl3's: a compact tree, like XPathDocument, leaves out whitespace-only text nodes.
        builder.WhitespacePolicy = S.WhitespacePolicy.StripAll;
        return builder.Build(new StringReader(xml));
    }

    private static string TransformXsl3(string name, string xml)
    {
        var transform = new XslCompiledTransform();
        transform.Load(Path30(name));
        using var writer = new StringWriter();
        transform.Transform(Tree.CompactDocument.Load(new StringReader(xml)), null, writer);
        return writer.ToString();
    }

    private static string TransformSaxon(string name, string xml)
    {
        S.XdmNode input = SaxonInput(xml);
        S.Xslt30Transformer transformer = s_processor.NewXsltCompiler().Compile(new Uri(Path30(name))).Load30();
        transformer.GlobalContextItem = input;
        using var writer = new StringWriter();
        transformer.ApplyTemplates(input, s_processor.NewSerializer(writer));
        return writer.ToString();
    }

    // The XML declaration and line endings aside.
    private static string Normalize(string output)
    {
        string text = output.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return text.StartsWith("<?xml", StringComparison.Ordinal) ? text[(text.IndexOf("?>", StringComparison.Ordinal) + 2)..].TrimStart() : text;
    }

    /// <summary>Warm up, then run for about <paramref name="budget"/> ms (at least five runs); allocation on every thread.</summary>
    private static Row Measure(string name, string size, string engine, int budget, Action action)
    {
        for (int i = 0; i < 3; i++)
        {
            action();
        }
        var times = new List<double>();
        long bytes = 0;
        var total = Stopwatch.StartNew();
        while (total.ElapsedMilliseconds < budget || times.Count < 5)
        {
            long before = GC.GetTotalAllocatedBytes(precise: true);
            var watch = Stopwatch.StartNew();
            action();
            times.Add(watch.Elapsed.TotalMilliseconds);
            bytes += GC.GetTotalAllocatedBytes(precise: true) - before;
            if (times.Count >= 10_000)
            {
                break;
            }
        }
        return new Row(name, size, engine, Median(times), times.Min(), bytes / times.Count, times.Count);
    }

    private static void Report(List<Row> rows, string coldSize)
    {
        Console.WriteLine();
        Console.WriteLine($"{"benchmark",-12} {"size",-8} {"xsl3 ms",10} {"saxon ms",10} {"speed-up",9} {"xsl3 KB",10} {"saxon KB",10}");
        var speedUps = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var group in rows.Where(r => r.Size != "cold").GroupBy(r => (r.Name, r.Size)).OrderBy(g => g.Key.Size == "compile" ? 0 : 1).ThenBy(g => g.Key.Size, StringComparer.Ordinal))
        {
            Row ours = group.Single(r => r.Engine == "xsl3"), theirs = group.Single(r => r.Engine == "saxon");
            double speedUp = theirs.MedianMs / Math.Max(1e-6, ours.MedianMs);
            (speedUps.TryGetValue(group.Key.Size, out var list) ? list : speedUps[group.Key.Size] = new()).Add(speedUp);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{group.Key.Name,-12} {group.Key.Size,-8} {ours.MedianMs,10:F3} {theirs.MedianMs,10:F3} {speedUp,8:F2}× {ours.AllocatedBytes / 1024.0,10:F1} {theirs.AllocatedBytes / 1024.0,10:F1}"));
        }

        Console.WriteLine();
        Console.WriteLine($"Cold start: compile (or load) and first transform on {coldSize}, in a fresh process; median of the runs");
        Console.WriteLine($"{"benchmark",-12} {"xsl3 ms",10} {"saved ms",10} {"saxon ms",10} {"speed-up",9} {"saved",9} {"xsl3 MB",9} {"saved MB",9} {"saxon MB",9}");
        foreach (var group in rows.Where(r => r.Size == "cold").GroupBy(r => r.Name))
        {
            Row ours = group.Single(r => r.Engine == "xsl3"), saved = group.Single(r => r.Engine == "xsl3-saved"), theirs = group.Single(r => r.Engine == "saxon");
            double speedUp = theirs.MedianMs / ours.MedianMs, savedSpeedUp = theirs.MedianMs / saved.MedianMs;
            (speedUps.TryGetValue("cold", out var list) ? list : speedUps["cold"] = new()).Add(speedUp);
            (speedUps.TryGetValue("cold-saved", out var savedList) ? savedList : speedUps["cold-saved"] = new()).Add(savedSpeedUp);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{group.Key,-12} {ours.MedianMs,10:F1} {saved.MedianMs,10:F1} {theirs.MedianMs,10:F1} {speedUp,8:F2}× {savedSpeedUp,8:F2}× {ours.PeakWorkingSet / 1048576.0,9:F1} {saved.PeakWorkingSet / 1048576.0,9:F1} {theirs.PeakWorkingSet / 1048576.0,9:F1}"));
        }

        Console.WriteLine();
        Console.WriteLine("Speed-up over SaxonCS-HE (its time / Xsl3's): median, geometric mean, minimum");
        foreach (var (size, values) in speedUps)
        {
            double geomean = Math.Exp(values.Average(Math.Log));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {size,-10} {Median(values),6:F2}× {geomean,6:F2}× {values.Min(),6:F2}×"));
        }
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }
}
#endif
