// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.XPath;
using Xunit;
using Stock = System.Xml.Xsl;

namespace Xsl3.Differential;

/// <summary>
/// Runs every stylesheet of the 1.0 corpus over every input document in its directory, through stock
/// XslCompiledTransform and through Xsl3's, and requires identical behaviour: the same load or transform
/// error, or byte-identical output on each kind of destination.
/// </summary>
/// <remarks>
/// The corpus pairs stylesheets with inputs they weren't written for, and some of those combinations
/// recurse without end. A stack overflow can't be caught in .NET, so each stylesheet runs in a child process
/// (<see cref="Child"/>) that reports one result per line; the parent restarts it after any step that crashes
/// or hangs, recording that step's outcome as "crash" or "timeout".
/// </remarks>
public class StockDifferentialTests
{
    internal static readonly string Corpus = Path.Combine(AppContext.BaseDirectory, "corpus");

    /// <summary>
    /// (stylesheet, input) pairs whose output differs from stock on purpose. Each one is listed in
    /// docs/divergences.md; keep the two in step.
    /// </summary>
    private static readonly HashSet<(string Stylesheet, string Input)> s_knownDivergences = new()
    {
        // ms:format-date/format-time with an empty picture: upstream main fix not yet in .NET 8/10 (dotnet/runtime#115455).
        ("XsltApiV2/bug93189.xsl", "bug93189.xml"),
    };
    private static readonly TimeSpan s_stepTimeout = TimeSpan.FromSeconds(15);

    public static IEnumerable<object[]> Stylesheets() =>
        Directory.EnumerateFiles(Corpus, "*.xsl", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(Corpus, p).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(Stylesheets))]
    public void SameBehaviourAsStock(string stylesheet)
    {
        var results = new Dictionary<int, string>();
        int count = Child.Steps(stylesheet).Count;
        int next = 0;
        while (next < count)
        {
            next = RunChild(stylesheet, next, results);
        }

        var mismatches = new List<string>();
        foreach (var group in Child.Steps(stylesheet).Select((s, i) => (s, i)).GroupBy(x => (x.s.Input, x.s.Destination)))
        {
            string Result(Child.Run run) => results[group.Single(x => x.s.Run == run).i];
            string stock = Result(Child.Run.Stock);
            if (stock != Result(Child.Run.StockAgain) || stock.StartsWith("timeout", StringComparison.Ordinal))
            {
                continue; // not deterministic on stock (current date/time, random numbers, ...), or never finishes
            }
            string xsl3 = Result(Child.Run.Xsl3);
            if (stock != xsl3 && !s_knownDivergences.Contains((stylesheet, group.Key.Input ?? "")))
            {
                mismatches.Add($"{group.Key.Input ?? "(load)"} -> {group.Key.Destination}:\n  stock: {stock}\n  xsl3:  {xsl3}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    // Runs the child from step "first" and returns the step to resume at.
    private static int RunChild(string stylesheet, int first, Dictionary<int, string> results)
    {
        // The test assembly is an executable; prefer its apphost, else run it through the dotnet host.
        string assembly = typeof(Child).Assembly.Location;
        string appHost = Path.ChangeExtension(assembly, OperatingSystem.IsWindows() ? ".exe" : null);
        var psi = new ProcessStartInfo(File.Exists(appHost) ? appHost : "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        if (!File.Exists(appHost))
        {
            psi.ArgumentList.Add(assembly);
        }
        psi.ArgumentList.Add("--child");
        psi.ArgumentList.Add(stylesheet);
        psi.ArgumentList.Add(first.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using var process = Process.Start(psi)!;
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        int current = first;
        while (true)
        {
            var read = process.StandardOutput.ReadLineAsync();
            if (!read.Wait(s_stepTimeout))
            {
                process.Kill(entireProcessTree: true);
                results[current] = "timeout";
                return current + 1;
            }
            string? line = read.Result;
            if (line == null)
            {
                process.WaitForExit();
                if (current < Child.Steps(stylesheet).Count)
                {
                    results[current] = $"crash (exit code {process.ExitCode})";
                    return current + 1;
                }
                return current;
            }
            var (index, outcome) = JsonSerializer.Deserialize<(int, string)>(line, Child.Json);
            results[index] = outcome;
            current = index + 1;
        }
    }
}

/// <summary>The child-process side of <see cref="StockDifferentialTests"/>.</summary>
internal static class Child
{
    internal static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    internal enum Run { Stock, StockAgain, Xsl3 }

    internal enum Destination
    {
        Load,
        Stream,
        TextWriter,
        // A writer the caller creates from OutputSettings. Stock drives the raw writer inside it; Xsl3 can't
        // reach that internal type and goes through the public XmlWriter API instead (docs/internals.md).
        CallerXmlWriter,
    }

    internal sealed record Step(string? Input, Destination Destination, Run Run);

    internal static List<Step> Steps(string stylesheet)
    {
        string xslPath = Path.Combine(StockDifferentialTests.Corpus, stylesheet);
        var steps = new List<Step>();
        foreach (Run run in Enum.GetValues<Run>())
        {
            steps.Add(new Step(null, Destination.Load, run));
        }
        foreach (string input in Directory.GetFiles(Path.GetDirectoryName(xslPath)!, "*.xml").Order(StringComparer.Ordinal))
        {
            foreach (Destination destination in Enum.GetValues<Destination>().Where(d => d != Destination.Load))
            {
                foreach (Run run in Enum.GetValues<Run>())
                {
                    steps.Add(new Step(Path.GetFileName(input), destination, run));
                }
            }
        }
        return steps;
    }

    public static int Main(string[] args)
    {
        if (args.Length != 3 || args[0] != "--child")
        {
            Console.Error.WriteLine("usage: --child <stylesheet> <first step>");
            return 2;
        }

        string stylesheet = args[1];
        string xslPath = Path.Combine(StockDifferentialTests.Corpus, stylesheet);
        List<Step> steps = Steps(stylesheet);
        Stock.XslCompiledTransform? stock = null;
        XslCompiledTransform? xsl3 = null;
        Console.OutputEncoding = Encoding.UTF8;

        for (int i = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture); i < steps.Count; i++)
        {
            Step step = steps[i];
            string outcome;
            try
            {
                outcome = step.Destination == Destination.Load
                    ? Load(step.Run, xslPath, ref stock, ref xsl3)
                    : Transform(step, xslPath, ref stock, ref xsl3);
            }
            catch (Exception e)
            {
                outcome = Error(e);
            }
            Console.WriteLine(JsonSerializer.Serialize((i, outcome), Json));
        }
        return 0;
    }

    private static string Load(Run run, string xslPath, ref Stock.XslCompiledTransform? stock, ref XslCompiledTransform? xsl3)
    {
        if (run == Run.Xsl3)
        {
            var x = new XslCompiledTransform();
            x.Load(xslPath, XsltSettings.TrustedXslt, new XmlUrlResolver());
            xsl3 = x;
        }
        else
        {
            var x = new Stock.XslCompiledTransform();
            x.Load(xslPath, Stock.XsltSettings.TrustedXslt, new XmlUrlResolver());
            stock = x;
        }
        return "loaded";
    }

    private static string Transform(Step step, string xslPath, ref Stock.XslCompiledTransform? stock, ref XslCompiledTransform? xsl3)
    {
        // A restarted child may resume past the load steps.
        if (step.Run == Run.Xsl3 && xsl3 == null)
        {
            Load(Run.Xsl3, xslPath, ref stock, ref xsl3);
        }
        else if (step.Run != Run.Xsl3 && stock == null)
        {
            Load(Run.Stock, xslPath, ref stock, ref xsl3);
        }

        var input = new XPathDocument(Path.Combine(Path.GetDirectoryName(xslPath)!, step.Input!));
        var ms = new MemoryStream();
        XmlWriterSettings outputSettings = step.Run == Run.Xsl3 ? xsl3!.OutputSettings! : stock!.OutputSettings!;
        switch (step.Destination)
        {
            case Destination.Stream:
                if (step.Run == Run.Xsl3) xsl3!.Transform(input, null, ms); else stock!.Transform(input, null, ms);
                return Output(ms.ToArray());
            case Destination.TextWriter:
                var sw = new StringWriter();
                if (step.Run == Run.Xsl3) xsl3!.Transform(input, null, sw); else stock!.Transform(input, null, sw);
                return Output(Encoding.UTF8.GetBytes(sw.ToString()));
            default:
                using (XmlWriter w = XmlWriter.Create(ms, outputSettings))
                {
                    if (step.Run == Run.Xsl3) xsl3!.Transform(input, null, w, new XmlUrlResolver()); else stock!.Transform(input, null, w, new XmlUrlResolver());
                }
                return Output(ms.ToArray());
        }
    }

    private static string Output(byte[] bytes) =>
        $"output ({bytes.Length} bytes) " + (bytes.Length > 2048
            ? "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
            : Convert.ToHexString(bytes)) +
        " \"" + Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 300)).ReplaceLineEndings("⏎") + "\"";

    // Exception types are compared by name: Xsl3 throws its own XsltException types, not System.Xml.Xsl's.
    private static string Error(Exception e) => $"error {Name(e)}: {e.Message}" + (e.InnerException is { } inner ? $" <- {Name(inner)}: {Detail(inner)}" : "");

    // A network failure's message comes from the resolver of the moment ("Name or service not known", "Temporary
    // failure in name resolution", …), so two runs of the same lookup may differ: its type is compared, not its text.
    private static string Detail(Exception e) =>
        e is System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException or System.Net.WebException ? "(network error)" : e.Message;

    private static string Name(Exception e) => e.GetType().Namespace is "Xsl3" or "System.Xml.Xsl" ? "Xsl." + e.GetType().Name : e.GetType().FullName!;
}
