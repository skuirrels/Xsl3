// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.XPath31;
using Xsl3.Xslt;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class StreamedHiddenGroundingTests(ITestOutputHelper output)
{
    // Original P7 push managed budget. The smaller negative input keeps a deliberately
    // grounded tree affordable; neither this JIT probe nor its enlarged control heap is an RSS gate.
    private const long ManagedBudget = 20_971_520, ControlBytes = 33_554_432, PushBytes = 209_715_200;
    private static ProbeState? s_probe; // Only installed in this fixture's isolated child process.
    internal sealed record Sample(string Point, long InputBytes, long LiveManagedBytes);
    internal sealed record Report(string Mode, bool Debug, long RequestedBytes, long ReadBytes, long ExpectedRows,
        string Output, long AvailableManagedBytes, long ManagedBudgetBytes, int InjectedCalls, int HelperCalls,
        bool WithinBudget, Sample[] Samples, StreamingExecutionCapture.Report Execution,
        StreamingExecutionCapture.CompilationReport Compilation);

    public static IEnumerable<object[]> Cases()
    {
        foreach (bool debug in new[] { false, true })
        {
            foreach (string mode in new[] { "original", "observe", "ground" })
                yield return new object[] { mode, debug, ControlBytes };
            yield return new object[] { "original", debug, PushBytes };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task MemoryGuardDetectsGroundingThatForwardSourceAndOutputChecksCannot(string mode, bool debug, long bytes)
    {
        string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";
        var start = new ProcessStartInfo(host) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(Program.HiddenGroundingProbe);
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(debug ? "debug" : "release");
        start.ArgumentList.Add(bytes.ToString(CultureInfo.InvariantCulture));
        foreach (string key in start.Environment.Keys.Where(key => key.StartsWith("DOTNET_GCHeapHardLimit", StringComparison.Ordinal)
                     || key.StartsWith("COMPlus_GCHeapHardLimit", StringComparison.Ordinal)).ToArray())
            start.Environment.Remove(key);
        // Complete the negative transformation and report its actual live-byte violation, not an OOM.
        long heap = mode == "ground" ? ManagedBudget * 32 : ManagedBudget;
        start.Environment["DOTNET_GCHeapHardLimit"] = heap.ToString("X", CultureInfo.InvariantCulture);
        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Hidden-grounding probe did not finish within three minutes");
        }
        string json = await stdout, errors = await stderr;
        output.WriteLine(json);
        Assert.True(process.ExitCode == (mode == "ground" ? 3 : 0), $"exit {process.ExitCode}: {json}\n{errors}");
        Report report = JsonSerializer.Deserialize<Report>(json)!;
        Assert.Equal(mode, report.Mode);
        Assert.Equal(debug, report.Debug);
        Assert.Equal(bytes, report.RequestedBytes);
        Assert.Equal(bytes, report.ReadBytes);
        Assert.Equal(report.ExpectedRows.ToString(CultureInfo.InvariantCulture), report.Output);
        Assert.Equal(ManagedBudget, report.ManagedBudgetBytes);
        Assert.InRange(report.AvailableManagedBytes, 1, heap);
        Assert.Equal(mode == "original" ? 0 : 1, report.InjectedCalls);
        Assert.Equal(report.InjectedCalls, report.HelperCalls);
        var execution = Assert.Single(report.Execution.Executions);
        Assert.Equal("Stylesheet", execution.Stage);
        Assert.True(execution.StreamedInitialMode);
        Assert.True(execution.ForwardInput);
        Assert.True(execution.BodyCompleted);
        var source = Assert.Single(execution.Sources);
        Assert.True(source.RequestedForward);
        Assert.True(source.ActualForward);
        Assert.Equal(typeof(StreamedNavigator).FullName, source.NavigatorType);
        var compilation = Assert.Single(report.Compilation.Compilations);
        Assert.True(compilation.HasStreamedInputs);
        Assert.True(compilation.OptimizationCompleted);
#if DEBUG
        Assert.Equal(1, compilation.VerifierStarted);
        Assert.Equal(1, compilation.VerifierCompleted);
#endif
        Assert.Contains(report.Samples, sample => sample.Point == "reading" && sample.InputBytes >= bytes / 2);
        Assert.Equal(mode != "ground", report.WithinBudget);
        if (mode == "ground")
            Assert.True(Assert.Single(report.Samples, sample => sample.Point == "helper-return").LiveManagedBytes > ManagedBudget);
        else
            Assert.All(report.Samples, sample => Assert.InRange(sample.LiveManagedBytes, 1, ManagedBudget));
    }

    internal static int Probe(string mode, bool debug, long bytes)
    {
        if (mode is not ("original" or "observe" or "ground") || bytes is not (ControlBytes or PushBytes))
            throw new ArgumentException("Unknown hidden-grounding probe scenario");
        using var input = new StreamingProbe.GeneratedInput(bytes);
        var state = new ProbeState(mode, input);
        s_probe = state;
        try
        {
            using var constants = ConstantPool.Use(new ConstantTable());
            using var capture = new StreamingExecutionCapture();
            var compiler = new Compiler(new XsltSettings { StreamedExecution = true, Streamability = XsltStreamabilityMode.Strict }, debug, null);
            using var sheet = XmlReader.Create(new StringReader("""
                <xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>
                  <xsl:mode streamable='yes'/><xsl:output method='text'/>
                  <xsl:template match='/'><xsl:value-of select='count(doc/row)'/></xsl:template>
                </xsl:stylesheet>
                """));
            Assert.Empty(compiler.Compile(sheet, null, null, out QilExpression? graph));
            Assert.True(graph!.HasStreamedInputs);
            Assert.True(graph.StreamedInitialMode);
            int injected = mode == "original" ? 0 : Inject(graph);
            XmlILCommand command = new XmlILGenerator().Generate(graph, null)!;
            using var reader = new SamplingReader(XmlReader.Create(input), state);
            using var result = new StringWriter(CultureInfo.InvariantCulture);
            using var writer = XmlWriter.Create(result, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment, OmitXmlDeclaration = true });
            state.Measure("before");
            using (StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet))
                command.Execute(reader, null, null, writer);
            state.Measure("after");
            Assert.Equal(input.Records.ToString(CultureInfo.InvariantCulture), result.ToString());
            Assert.Equal(bytes, input.ReadBytes);
            bool bounded = state.Samples.All(sample => sample.LiveManagedBytes <= ManagedBudget);
            Console.WriteLine(JsonSerializer.Serialize(new Report(mode, debug, bytes, input.ReadBytes, input.Records,
                result.ToString(), GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, ManagedBudget, injected, state.Calls,
                bounded, state.Samples.ToArray(), capture.Snapshot(), capture.SnapshotCompilations())));
            return bounded ? 0 : 3;
        }
        finally { s_probe = null; }
    }

    // Inject after static streamability analysis, before ordinary IL generation. The same helper call
    // and graph shape run in both controls. The optimizer verifier cannot see the helper's implementation.
    // No engine hook or fabricated source report is involved: X31InitialSource opens and records the input.
    private static int Inject(QilExpression graph)
    {
        int replacements = 0;
        var visited = new HashSet<QilNode>();
        void Visit(QilNode node)
        {
            if (!visited.Add(node)) return;
            for (int i = 0; i < node.Count; i++)
            {
                QilNode child = node[i];
                if (child == null) continue;
                if (child is QilInvokeEarlyBound call && call.ClrMethod.DeclaringType == typeof(XsltLibrary)
                    && call.ClrMethod.Name == nameof(XsltLibrary.X31InitialSource))
                {
                    QilFactory f = graph.Factory;
                    node[i] = f.XsltInvokeEarlyBound(f.LiteralQName(nameof(ObserveContext)),
                        f.LiteralObject(typeof(StreamedHiddenGroundingTests).GetMethod(nameof(ObserveContext))!),
                        f.ActualParameterList(new[] { child }), child.XmlType!);
                    replacements++;
                }
                else Visit(child);
            }
        }
        Visit(graph);
        Assert.Equal(1, replacements);
        return replacements;
    }

    public static XPathNavigator ObserveContext(XPathNavigator source)
    {
        ProbeState state = s_probe ?? throw new InvalidOperationException("No isolated grounding probe");
        state.Calls++;
        var forward = Assert.IsType<StreamedNavigator>(source);
        XPathNavigator result = state.Mode == "ground" ? forward.Ground() : source;
        state.Measure("helper-return");
        GC.KeepAlive(result); // The actual copied tree must remain live at the measured point.
        return result;
    }

    private sealed class ProbeState(string mode, StreamingProbe.GeneratedInput input)
    {
        internal string Mode => mode;
        internal int Calls;
        internal List<Sample> Samples { get; } = new();
        internal void Measure(string point) => Samples.Add(new(point, input.ReadBytes, GC.GetTotalMemory(forceFullCollection: true)));
        internal long ReadBytes => input.ReadBytes;
    }

    // Observe the same reader while the engine consumes it; never pre-read, retain nodes, or construct a tree.
    private sealed class SamplingReader(XmlReader inner, ProbeState state) : XmlWrappingReader(inner)
    {
        private long _next = 1_048_576;
        public override bool Read()
        {
            bool read = base.Read();
            if (state.ReadBytes >= _next)
            {
                state.Measure("reading");
                _next *= 2;
            }
            return read;
        }
    }
}
