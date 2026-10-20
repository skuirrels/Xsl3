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
        var input = new XPathDocument(new StringReader(Corpus.Orders(orders)));
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
            xsl3.Transform(input, null, actual);
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
                xsl3.Transform(input, null, Stream.Null);
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
                xsl3.Transform(input, null, Stream.Null);
                x.Add(watch.Elapsed.TotalMilliseconds);
            }
            double ms = Median(s), mx = Median(x);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{sheet,-18} {ms,10:F2} {mx,10:F2} {mx / ms,11:F2} {x.Min() / s.Min(),10:F2}"));
        }
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }
}
