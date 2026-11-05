// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.Tests;

// A separate child owns the heap; other test fixtures cannot inflate its live-byte samples.
[Collection("Process-wide state")]
public class StreamedNavigatorMemoryTests(ITestOutputHelper output)
{
    // Original P7 push input and managed budget, docs/plan/p7-streaming.md decision 6.
    // This JIT-hosted diagnostic is not a substitute for the NativeAOT absolute RSS gate.
    private const long InputBytes = 209_715_200, ManagedBudget = 20_971_520;
    public sealed record Sample(long Records, long InputBytes, long LiveManagedBytes, int RetainedFrames);
    public sealed record Report(bool RetainControl, bool Complete, long RequestedInputBytes, long ExpectedRecords,
        long AvailableManagedBytes, long ManagedBudgetBytes, Sample[] Samples);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveBytesStayBoundedUnlessTheCallerRetainsPastPositions(bool retain)
    {
        string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";
        var start = new ProcessStartInfo(host) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(Program.NavigatorMemoryProbe);
        start.ArgumentList.Add(retain ? "retain" : "release");
        foreach (string key in start.Environment.Keys.Where(key => key.StartsWith("DOTNET_GCHeapHardLimit", StringComparison.Ordinal)
                     || key.StartsWith("COMPlus_GCHeapHardLimit", StringComparison.Ordinal)).ToArray())
            start.Environment.Remove(key);
        // Let the negative control report an observed budget violation rather than crashing with OOM.
        long heapLimit = retain ? ManagedBudget * 32 : ManagedBudget;
        start.Environment["DOTNET_GCHeapHardLimit"] = heapLimit.ToString("X", CultureInfo.InvariantCulture);
        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Navigator memory probe did not finish within three minutes");
        }
        string json = await stdout, errors = await stderr;
        output.WriteLine(json);
        Assert.True(process.ExitCode == (retain ? 3 : 0), $"exit {process.ExitCode}: {json}\n{errors}");
        var report = JsonSerializer.Deserialize<Report>(json)!;
        Assert.Equal(retain, report.RetainControl);
        Assert.Equal(!retain, report.Complete);
        Assert.Equal(InputBytes, report.RequestedInputBytes);
        Assert.Equal(ManagedBudget, report.ManagedBudgetBytes);
        Assert.InRange(report.AvailableManagedBytes, 1, heapLimit);
        Assert.NotEmpty(report.Samples);
        Assert.All(report.Samples, sample => Assert.InRange(sample.RetainedFrames, 1, 4));
        if (retain)
        {
            Assert.True(report.Samples[^1].LiveManagedBytes > ManagedBudget);
            Assert.True(report.Samples[^1].Records < report.ExpectedRecords);
        }
        else
        {
            Assert.Equal(new long[] { 0, 100, 1000, 10000, 100000, 1000000, report.ExpectedRecords },
                report.Samples.Select(sample => sample.Records));
            Assert.All(report.Samples, sample => Assert.InRange(sample.LiveManagedBytes, 1, ManagedBudget));
            Assert.Equal(InputBytes, report.Samples[^1].InputBytes);
        }
    }

    internal static int Probe(bool retain)
    {
        using var input = new StreamingProbe.GeneratedInput(InputBytes);
        using var reader = XmlReader.Create(input);
        using var document = new StreamedDocument(reader);
        XPathNavigator navigator = document.CreateNavigator();
        if (!navigator.MoveToFirstChild() || !navigator.MoveToFirstChild()) throw new InvalidOperationException("Missing generated rows");
        var retained = retain ? new List<XPathNavigator>() : null;
        var samples = new List<Sample>();
        long visited = 0, nextSample = 100, available = 0;
        string expectedValue = StreamingProbe.GeneratedInput.Value.ToString(CultureInfo.InvariantCulture)
            + StreamingProbe.GeneratedInput.BeforeText + StreamingProbe.GeneratedInput.MiddleText + StreamingProbe.GeneratedInput.AfterText;

        bool Measure()
        {
            long live = GC.GetTotalMemory(forceFullCollection: true);
            available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            samples.Add(new(visited, input.ReadBytes, live, document.RetainedNodeCount));
            GC.KeepAlive(navigator);
            GC.KeepAlive(retained);
            return live <= ManagedBudget;
        }

        int Finish(bool complete)
        {
            Console.WriteLine(JsonSerializer.Serialize(new Report(retain, complete, InputBytes, input.Records,
                available, ManagedBudget, samples.ToArray())));
            GC.KeepAlive(navigator);
            GC.KeepAlive(retained);
            return complete ? 0 : 3;
        }

        if (!Measure()) return Finish(false);
        do
        {
            visited++;
            if (navigator.LocalName != "row" || long.Parse(navigator.GetAttribute("id", ""), CultureInfo.InvariantCulture) != visited
                || navigator.Value != expectedValue)
                throw new InvalidOperationException($"Incorrect generated record {visited}");
            retained?.Add(navigator.Clone());
            if (visited == nextSample)
            {
                if (!Measure()) return Finish(false);
                nextSample = checked(nextSample * 10);
            }
        } while (navigator.MoveToNext(XPathNodeType.Element)); // The exact-size corpus ends with whitespace padding.
        if (visited != input.Records || input.ReadBytes != InputBytes) throw new InvalidOperationException("Incomplete generated input");
        return Finish(Measure());
    }
}
