// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace Xsl3.Benchmarks;

/// <summary>
/// The XSLT 3.0 micro suite (remaining-scope 1.3, 1.6 and 1.7): one stylesheet per feature in bench/corpus/xslt30/micro,
/// run at several input sizes, with compile time per stylesheet and the cold start of the first. Each row has a
/// median and minimum time and the bytes allocated per run. <c>micro-compare</c> compares two sets of results, for the
/// performance gate in CI (eng/perf-gate.sh).
/// </summary>
public static class Micro
{
    public static readonly string[] Names = { "grouping", "sorting", "keys", "join", "regex", "strings", "maps", "fold-left", "iterate", "identity", "dispatch", "recursion", "json" };

    // Orders and products for each size: about 1 KB, 1 MB and 100 MB of input.
    internal static readonly Dictionary<string, (int Orders, int Products)> Sizes = new(StringComparer.Ordinal)
    {
        ["1kb"] = (2, 2),
        ["1mb"] = (2_200, 500),
        ["100mb"] = (220_000, 500),
    };

    public sealed record Row(string Name, string Size, double MedianMs, double MinMs, long AllocatedBytes, int Runs);

    private static string Path30(string name) => Path.Combine(AppContext.BaseDirectory, "corpus", "xslt30", "micro", name + ".xsl");

    /// <summary>
    /// micro [--sizes 1kb,1mb] [--ms budget] [--json file] [name...]: the suite, printed as a table, and written as
    /// JSON for micro-compare.
    /// </summary>
    public static void Run(string[] args)
    {
        string[] sizes = { "1kb", "1mb" };
        int budget = 1000;
        string? json = null;
        var names = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--sizes":
                    sizes = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries).Where(Sizes.ContainsKey).ToArray(); // "none": compile only
                    break;
                case "--ms":
                    budget = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--json":
                    json = args[++i];
                    break;
                case "--alloc-types":
                    s_allocations = new AllocationSampler();
                    break;
                default:
                    names.Add(args[i]);
                    break;
            }
        }
        if (names.Count == 0)
        {
            names.AddRange(Names);
        }

        var rows = new List<Row>();
        // Cold start: the first compile and transform in the process, JIT of Xsl3 itself included.
        var watch = Stopwatch.StartNew();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var cold = new XslCompiledTransform();
        cold.Load(Path30(names[0]));
        cold.Transform(CompactInput("1kb"), null, Stream.Null);
        rows.Add(new Row(names[0], "cold", watch.Elapsed.TotalMilliseconds, watch.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated, 1));
        Print(rows[^1], header: true);

        // Compile time per stylesheet.
        foreach (string name in names)
        {
            rows.Add(Measure(name, "compile", budget, () => new XslCompiledTransform().Load(Path30(name))));
            Print(rows[^1]);
        }

        // Transform time per stylesheet and size, on an input parsed once (Xsl3's compact tree).
        foreach (string size in sizes)
        {
            Tree.CompactDocument input = CompactInput(size);
            foreach (string name in names)
            {
                var transform = new XslCompiledTransform();
                transform.Load(Path30(name));
                rows.Add(Measure(name, size, budget, () => transform.Transform(input, null, Stream.Null)));
                Print(rows[^1]);
            }
        }

        if (json != null)
        {
            File.WriteAllText(json, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }
        s_allocations?.Dispose();
    }

    // --alloc-types: the runtime's allocation samples (one per ~100 KB) by type, after each row.
    private static AllocationSampler? s_allocations;

    private sealed class AllocationSampler : System.Diagnostics.Tracing.EventListener
    {
        private readonly Dictionary<string, long> _bytes = new(StringComparer.Ordinal);

        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
            {
                EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1); // GC
            }
        }

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
        {
            if (e.EventName?.StartsWith("GCAllocationTick", StringComparison.Ordinal) == true && e.Payload != null)
            {
                string type = e.PayloadNames!.IndexOf("TypeName") is int t and >= 0 ? e.Payload[t] as string ?? "?" : "?";
                long amount = e.PayloadNames.IndexOf("AllocationAmount64") is int a and >= 0 ? Convert.ToInt64(e.Payload[a], CultureInfo.InvariantCulture) : 0;
                lock (_bytes)
                {
                    _bytes[type] = _bytes.GetValueOrDefault(type) + amount;
                }
            }
        }

        public void Report()
        {
            lock (_bytes)
            {
                foreach (var (type, bytes) in _bytes.OrderByDescending(kv => kv.Value).Take(15))
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    {bytes / 1024,10} KB  {type}"));
                }
                _bytes.Clear();
            }
        }
    }

    /// <summary>
    /// tei [--ms budget]: real stylesheets (remaining-scope 1.3), the TEI Stylesheets' HTML, LaTeX, XSL-FO and text
    /// conversions of Test/test14.xml (550 KB): compile time and transform time and allocation.
    /// </summary>
    public static void RunTei(string[] args)
    {
        int budget = 3000;
        string? only = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ms":
                    budget = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--alloc-types":
                    s_allocations = new AllocationSampler();
                    break;
                default:
                    only = args[i];
                    break;
            }
        }
        string? root = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null && root == null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "external", "bench", "tei");
            root = File.Exists(Path.Combine(candidate, "html", "html.xsl")) ? candidate : null;
        }
        if (root == null)
        {
            Console.WriteLine("TEI Stylesheets not found: run eng/fetch-bench.sh");
            return;
        }
        var settings = new XsltSettings(enableDocumentFunction: true, enableScript: false);
        var resolver = new XmlUrlResolver();
        var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse };
        Tree.CompactDocument input;
        using (XmlReader reader = XmlReader.Create(Path.Combine(root, "Test", "test14.xml"), readerSettings))
        {
            input = Tree.CompactDocument.Load(reader);
        }
        bool header = true;
        foreach (string stylesheet in new[] { "html/html.xsl", "latex/latex.xsl", "fo/fo.xsl", "txt/tei-to-text.xsl" }.Where(n => only == null || n.StartsWith(only, StringComparison.Ordinal)))
        {
            string path = Path.Combine(root, stylesheet);
            XslCompiledTransform Compile()
            {
                var t = new XslCompiledTransform();
                using XmlReader reader = XmlReader.Create(path, readerSettings);
                t.Load(reader, settings, resolver);
                return t;
            }
            string name = stylesheet.Split('/')[0];
            Print(Measure(name, "compile", budget, () => Compile()), header);
            header = false;
            XslCompiledTransform transform = Compile();
            Print(Measure(name, "test14", budget, () =>
            {
                // (With the resolver: the stylesheets read their own i18n.xml with document().)
                using XmlWriter writer = XmlWriter.Create(Stream.Null, transform.OutputSettings);
                transform.Transform(input, null, writer, resolver);
            }));
        }
    }

    /// <summary>
    /// docbook [--ms budget]: DocBook xslTNG (eng/fetch-bench.sh), its HTML conversion: compile time, and a book made of
    /// the test book's chapters repeated (tests/Xsl3.W3C/docbook/book.xml) at about 400 KB.
    /// </summary>
    public static void RunDocBook(string[] args)
    {
        int budget = 3000, chapters = 200;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--ms") budget = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
            if (args[i] == "--chapters") chapters = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
        }
        if (args.Contains("--alloc-types"))
        {
            s_allocations = new AllocationSampler();
        }
        string? xslt = null, book = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null && (xslt == null || book == null); dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "external", "bench", "docbook", "docbook-xslTNG-nosaxon-2.7.1", "xslt", "docbook.xsl");
            xslt ??= File.Exists(candidate) ? candidate : null;
            string test = Path.Combine(dir.FullName, "tests", "Xsl3.W3C", "docbook", "book.xml");
            book ??= File.Exists(test) ? test : null;
        }
        if (xslt == null || book == null)
        {
            Console.WriteLine("DocBook xslTNG not found: run eng/fetch-bench.sh");
            return;
        }
        var settings = new XsltSettings(enableDocumentFunction: true, enableScript: false) { EnableEvaluate = true }; // (it runs its pipeline with fn:transform)
        var resolver = new XmlUrlResolver();
        XslCompiledTransform Compile()
        {
            var t = new XslCompiledTransform();
            t.Load(xslt, settings, resolver);
            return t;
        }
        if (!args.Contains("--transform-only"))
        {
            Print(Measure("docbook", "compile", budget, () => Compile()), header: true);
        }
        // The chapters, 200 times over, their ids made unique.
        string text = File.ReadAllText(book);
        int start = text.IndexOf("<chapter", StringComparison.Ordinal), end = text.LastIndexOf("</chapter>", StringComparison.Ordinal) + "</chapter>".Length;
        var sb = new StringBuilder(text[..start]);
        for (int i = 0; i < chapters; i++)
        {
            sb.Append(text[start..end].Replace("xml:id=\"", $"xml:id=\"c{i}-", StringComparison.Ordinal).Replace("linkend=\"", $"linkend=\"c{i}-", StringComparison.Ordinal));
        }
        sb.Append(text[end..]);
        // (With the test book's URI: the stylesheets use the document's base URI.)
        Tree.CompactDocument input;
        using (XmlReader reader = XmlReader.Create(new StringReader(sb.ToString()), null, new Uri(book).AbsoluteUri))
        {
            input = Tree.CompactDocument.Load(reader);
        }
        XslCompiledTransform transform = Compile();
        Print(Measure("docbook", $"{sb.Length / 1024}kb", budget, () =>
        {
            using XmlWriter writer = XmlWriter.Create(Stream.Null, transform.OutputSettings);
            transform.Transform(input, null, writer, resolver);
        }));
    }

    private static readonly Dictionary<string, Tree.CompactDocument> s_inputs = new(StringComparer.Ordinal);

    private static Tree.CompactDocument CompactInput(string size)
    {
        if (!s_inputs.TryGetValue(size, out Tree.CompactDocument? document))
        {
            (int orders, int products) = Sizes[size];
            s_inputs[size] = document = Tree.CompactDocument.Load(new StringReader(Corpus.Orders(orders, products)));
        }
        return document;
    }

    /// <summary>Warm up, then run for about <paramref name="budget"/> ms (at least five runs).</summary>
    private static Row Measure(string name, string size, int budget, Action action)
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
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            action();
            times.Add(watch.Elapsed.TotalMilliseconds);
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            if (times.Count >= 10_000)
            {
                break;
            }
        }
        return new Row(name, size, Median(times), times.Min(), bytes / times.Count, times.Count);
    }

    private static void Print(Row row, bool header = false)
    {
        if (header)
        {
            Console.WriteLine($"{"benchmark",-12} {"size",-8} {"median ms",10} {"min ms",10} {"KB/run",12} {"runs",6}");
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{row.Name,-12} {row.Size,-8} {row.MedianMs,10:F3} {row.MinMs,10:F3} {row.AllocatedBytes / 1024.0,12:F1} {row.Runs,6}"));
        s_allocations?.Report();
    }

    /// <summary>
    /// micro-compare base.json[,base.json…] head.json[,head.json…]: the median of each row over the files of each side,
    /// head against base. Fails (exit code 1) when the geometric mean of the time ratios grows by more than 5%, or a
    /// row allocates more than 5% (and 1 KB) more. Cold start is reported, not gated: one sample per process is noise.
    /// </summary>
    public static int Compare(string[] args)
    {
        Dictionary<(string, string), Row> Load(string files)
        {
            var all = files.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(f => JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(f))!)
                .GroupBy(r => (r.Name, r.Size));
            return all.ToDictionary(g => g.Key, g => new Row(g.Key.Name, g.Key.Size,
                Median(g.Select(r => r.MedianMs).ToList()), g.Min(r => r.MinMs), (long)Median(g.Select(r => (double)r.AllocatedBytes).ToList()), g.Sum(r => r.Runs)));
        }

        Dictionary<(string, string), Row> baseline = Load(args[0]), head = Load(args[1]);
        double logSum = 0;
        int count = 0;
        var failures = new List<string>();
        Console.WriteLine($"{"benchmark",-12} {"size",-8} {"base ms",10} {"head ms",10} {"time",7} {"base KB",10} {"head KB",10} {"alloc",7}");
        foreach (var (key, h) in head.OrderBy(kv => kv.Key.Item2 == "cold" ? 0 : kv.Key.Item2 == "compile" ? 1 : 2).ThenBy(kv => kv.Key.Item2, StringComparer.Ordinal).ThenBy(kv => kv.Key.Item1, StringComparer.Ordinal))
        {
            if (!baseline.TryGetValue(key, out Row? b))
            {
                Console.WriteLine($"{h.Name,-12} {h.Size,-8} (new)");
                continue;
            }
            double time = h.MedianMs / Math.Max(1e-6, b.MedianMs);
            double alloc = b.AllocatedBytes == 0 ? (h.AllocatedBytes == 0 ? 1 : double.PositiveInfinity) : (double)h.AllocatedBytes / b.AllocatedBytes;
            string mark = "";
            if (h.Size != "cold")
            {
                logSum += Math.Log(time);
                count++;
                if (alloc > 1.05 && h.AllocatedBytes - b.AllocatedBytes > 1024)
                {
                    failures.Add($"{h.Name} {h.Size}: {100 * (alloc - 1):F1}% more allocation");
                    mark = " ALLOC";
                }
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{h.Name,-12} {h.Size,-8} {b.MedianMs,10:F3} {h.MedianMs,10:F3} {time,7:F3} {b.AllocatedBytes / 1024.0,10:F1} {h.AllocatedBytes / 1024.0,10:F1} {alloc,7:F3}{mark}"));
        }
        double geomean = Math.Exp(logSum / Math.Max(1, count));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Time, geometric mean of head/base over {count} rows: {geomean:F3}"));
        if (geomean > 1.05)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"time: geometric mean {100 * (geomean - 1):F1}% slower"));
        }
        foreach (string failure in failures)
        {
            Console.WriteLine("FAIL " + failure);
        }
        return failures.Count == 0 ? 0 : 1;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }
}
