// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.XPath;
using Stock = System.Xml.Xsl;

namespace Xsl3.Benchmarks;

/// <summary>
/// A quick stock-against-Xsl3 comparison for development: the two engines run interleaved (so machine noise hits
/// both), the output must be byte-identical, and the medians are compared.
/// </summary>
public static class Quick
{
    private static readonly string[] s_all = { "identity", "sort", "muenchian", "string-recursion", "key-join", "html-report" };

    public static void Run(string[] args)
    {
        int orders = args.Length > 0 && int.TryParse(args[0], out int n) ? n : 20_000;
        string[] sheets = args.Length > 1 ? args[1..] : s_all;
        string xml = Corpus.Orders(orders);
        var input = new XPathDocument(new StringReader(xml));
        // XSL3_QUICK_TREE=compact gives Xsl3 a CompactDocument (stock keeps XPathDocument).
        IXPathNavigable xsl3Input = input;
        if (Environment.GetEnvironmentVariable("XSL3_QUICK_TREE") == "compact")
        {
            xsl3Input = Tree.CompactDocument.Load(new StringReader(xml));
            ReportTrees(xml);
        }
        Console.WriteLine($"{"stylesheet",-18} {"stock ms",10} {"xsl3 ms",10} {"xsl3/stock",11} {"min ratio",10}");
        foreach (string sheet in sheets)
        {
            var stock = new Stock.XslCompiledTransform();
            stock.Load(Corpus.Stylesheet(sheet));
            var xsl3 = new XslCompiledTransform();
            xsl3.Load(Corpus.Stylesheet(sheet));
            var expected = new MemoryStream();
            var actual = new MemoryStream();
            stock.Transform(input, null, expected);
            xsl3.Transform(xsl3Input, null, actual);
            if (!expected.ToArray().AsSpan().SequenceEqual(actual.ToArray()))
            {
                Console.WriteLine($"{sheet,-18} OUTPUT DIFFERS");
                continue;
            }
            // About XSL3_QUICK_MS (default 3 s) per engine; fewer runs for the slow ones. Both engines warm up first.
            int budget = int.TryParse(Environment.GetEnvironmentVariable("XSL3_QUICK_MS"), out int b) ? b : 3000;
            for (int i = 0; i < 5; i++)
            {
                stock.Transform(input, null, Stream.Null);
                xsl3.Transform(xsl3Input, null, Stream.Null);
            }
            var watch = Stopwatch.StartNew();
            stock.Transform(input, null, Stream.Null);
            int runs = Math.Clamp((int)(budget / Math.Max(1, watch.ElapsedMilliseconds)), 3, 401);
            var s = new List<double>();
            var x = new List<double>();
            for (int i = 0; i < runs; i++)
            {
                watch.Restart();
                stock.Transform(input, null, Stream.Null);
                s.Add(watch.Elapsed.TotalMilliseconds);
                watch.Restart();
                xsl3.Transform(xsl3Input, null, Stream.Null);
                x.Add(watch.Elapsed.TotalMilliseconds);
            }
            double ms = Median(s), mx = Median(x);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{sheet,-18} {ms,10:F2} {mx,10:F2} {mx / ms,11:F2} {x.Min() / s.Min(),10:F2}"));
        }
    }

    /// <summary>
    /// The rewritten pairs: each 1.0 benchmark on stock, the same on Xsl3, and its 3.0 rewrite on Xsl3, interleaved;
    /// the rewrite's output is compared with stock's.
    /// </summary>
    public static void RunPairs(string[] args)
    {
        int orders = args.Length > 0 && int.TryParse(args[0], out int n) ? n : 20_000;
        string[] names = args.Length > 1 ? args[1..] : Corpus.Pairs;
        var input = new XPathDocument(new StringReader(Corpus.Orders(orders)));
        int budget = int.TryParse(Environment.GetEnvironmentVariable("XSL3_QUICK_MS"), out int b) ? b : 3000;
        Console.WriteLine($"{"pair",-18} {"stock 1.0",10} {"xsl3 1.0",10} {"xsl3 3.0",10} {"3.0/stock",10} {"output",8}");
        foreach (string name in names)
        {
            var stock = new Stock.XslCompiledTransform();
            stock.Load(Corpus.Stylesheet(name));
            var xsl3 = new XslCompiledTransform();
            xsl3.Load(Corpus.Stylesheet(name));
            var rewrite = new XslCompiledTransform();
            rewrite.Load(Corpus.Rewrite(name));
            var expected = new MemoryStream();
            var actual = new MemoryStream();
            stock.Transform(input, null, expected);
            rewrite.Transform(input, null, actual);
            // The 2.0+ serializer writes no BOM and "UTF-8"; compare what follows the XML declaration.
            bool same = AfterDeclaration(expected.ToArray()) == AfterDeclaration(actual.ToArray());
            if (!same && Environment.GetEnvironmentVariable("XSL3_PAIRS_DUMP") is string dir)
            {
                File.WriteAllBytes(Path.Combine(dir, name + ".stock.out"), expected.ToArray());
                File.WriteAllBytes(Path.Combine(dir, name + ".rewrite.out"), actual.ToArray());
            }
            for (int i = 0; i < 3; i++)
            {
                stock.Transform(input, null, Stream.Null);
                xsl3.Transform(input, null, Stream.Null);
                rewrite.Transform(input, null, Stream.Null);
            }
            var watch = Stopwatch.StartNew();
            stock.Transform(input, null, Stream.Null);
            int runs = Math.Clamp((int)(budget / Math.Max(1, watch.ElapsedMilliseconds)), 3, 201);
            var s = new List<double>();
            var x = new List<double>();
            var r = new List<double>();
            for (int i = 0; i < runs; i++)
            {
                watch.Restart();
                stock.Transform(input, null, Stream.Null);
                s.Add(watch.Elapsed.TotalMilliseconds);
                watch.Restart();
                xsl3.Transform(input, null, Stream.Null);
                x.Add(watch.Elapsed.TotalMilliseconds);
                watch.Restart();
                rewrite.Transform(input, null, Stream.Null);
                r.Add(watch.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{name,-18} {Median(s),10:F2} {Median(x),10:F2} {Median(r),10:F2} {Median(r) / Median(s),10:F2} {(same ? "same" : "DIFFERS"),8}"));
        }
    }

    private static string AfterDeclaration(byte[] bytes)
    {
        string text = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        return text.StartsWith("<?xml", StringComparison.Ordinal) ? text[(text.IndexOf("?>", StringComparison.Ordinal) + 2)..] : text;
    }

    /// <summary>An XSLT 3.0 stylesheet, Xsl3 alone: median and minimum time, and allocation, per transform.</summary>
    public static void Run30(string[] args)
    {
        int orders = args.Length > 0 && int.TryParse(args[0], out int n) ? n : 20_000;
        string sheet = args.Length > 1 ? args[1] : "temporary-trees";
        var input = new XPathDocument(new StringReader(Corpus.Orders(orders)));
        var xsl3 = new XslCompiledTransform();
        xsl3.Load(Path.Combine(AppContext.BaseDirectory, "corpus", "xslt30", sheet + ".xsl"));
        for (int i = 0; i < 5; i++)
        {
            xsl3.Transform(input, null, Stream.Null);
        }
        int budget = int.TryParse(Environment.GetEnvironmentVariable("XSL3_QUICK_MS"), out int b) ? b : 5000;
        var times = new List<double>();
        long allocated = GC.GetTotalAllocatedBytes(true);
        var total = Stopwatch.StartNew();
        while (total.ElapsedMilliseconds < budget || times.Count < 5)
        {
            var watch = Stopwatch.StartNew();
            xsl3.Transform(input, null, Stream.Null);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        allocated = (GC.GetTotalAllocatedBytes(true) - allocated) / times.Count;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{sheet}: median {Median(times):F2} ms, min {times.Min():F2} ms, {allocated / 1024} KB allocated per transform ({times.Count} runs)"));
    }

    /// <summary>Parse time and retained memory of the two trees for the same text.</summary>
    private static void ReportTrees(string xml)
    {
        for (int round = 0; round < 3; round++)
        {
            s_keep = null;
            long before = GC.GetTotalMemory(true);
            var watch = Stopwatch.StartNew();
            s_keep = new XPathDocument(new StringReader(xml));
            double ta = watch.Elapsed.TotalMilliseconds;
            long ma = GC.GetTotalMemory(true) - before;
            s_keep = null;
            before = GC.GetTotalMemory(true);
            watch.Restart();
            s_keep = Tree.CompactDocument.Load(new StringReader(xml));
            double tb = watch.Elapsed.TotalMilliseconds;
            long mb = GC.GetTotalMemory(true) - before;
            s_keep = null;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"XPathDocument: {ta:F1} ms, {ma / 1024} KB; CompactDocument: {tb:F1} ms, {mb / 1024} KB ({100.0 * mb / ma:F0}%)"));
        }
    }

    private static object? s_keep;

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }
}
