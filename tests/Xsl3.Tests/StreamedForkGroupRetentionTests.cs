// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using Xsl3.Streaming;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class StreamedForkGroupRetentionTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string grouping in new[] { "group-by-one", "group-by-sorted", "adjacent-sorted" })
        foreach (int rows in new[] { 32, 512 })
        foreach (int capacity in new[] { 1, 7 })
        foreach (bool debug in new[] { false, true })
        foreach (int delivery in new[] { 0, 1, 2 })
        {
#if !NET10_0_OR_GREATER
            if (delivery == 2) continue;
#endif
            yield return new object[] { grouping, rows, capacity, debug, delivery };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ForkGroupingBuffersItsRequiredPopulationAndReleasesPayloads(
        string grouping, int rows, int capacity, bool debug, int delivery)
    {
        var probe = new Probe(grouping, rows, retain: false);
        Execute(probe, capacity, debug, delivery);
        Assert.Equal(rows, probe.Payloads.Count);
        Assert.Equal(Enumerable.Range(1, rows).Sum(row => Payload(row).Length), probe.CharactersAtFirstGroup);
        AssertReleased(probe);
        output.WriteLine("{0}: {1} source rows, {2} groups, {3} retained text characters before first group body; capacity={4}; released",
            grouping, rows, probe.Groups, probe.CharactersAtFirstGroup, capacity);
    }

    [Theory]
    [InlineData("group-by-one")]
    [InlineData("group-by-sorted")]
    [InlineData("adjacent-sorted")]
    public void RetainedPayloadControlIsDetectedUntilItsOwnerReleasesIt(string grouping)
    {
        var probe = new Probe(grouping, 32, retain: true);
        Execute(probe, 1, false, 0);
        Collect();
        Assert.Equal(32, LivePayloads(probe));
        probe.Kept.Clear();
        AssertReleased(probe);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void UnsortedAdjacentControlDoesNotRetainTheWholePopulation(int capacity)
    {
        var probe = new Probe("adjacent-forward", 32, retain: false);
        Execute(probe, capacity, false, 0);
        Assert.InRange(probe.CharactersAtFirstGroup, 0, Enumerable.Range(1, 32).Sum(row => Payload(row).Length) - 1);
        Assert.Equal(32, probe.Payloads.Count);
        AssertReleased(probe);
    }

    private static string Payload(int row) => row.ToString("D6", CultureInfo.InvariantCulture) + ":" + new string('x', 256);

    private static string Sheet(string grouping)
    {
        string key = grouping == "group-by-one" ? "'all'" : grouping == "group-by-sorted" ? "string(@k)" : "string(@pair)";
        string kind = grouping.StartsWith("adjacent", StringComparison.Ordinal) ? "group-adjacent" : "group-by";
        string sort = grouping is "group-by-one" or "adjacent-forward" ? "" : "<xsl:sort select='number(current-grouping-key())' order='descending'/>";
        return $$"""
            <xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>
              <xsl:mode streamable='yes'/><xsl:output method='text'/>
              <xsl:template match='/'>
                <xsl:fork>
                  <xsl:for-each-group select='doc/row' {{kind}}="{{key}}">
                    {{sort}}
                    <xsl:message select='position()'/>
                    <xsl:value-of select='current-group()/value' separator='|'/><xsl:text>;</xsl:text>
                  </xsl:for-each-group>
                </xsl:fork>
              </xsl:template>
            </xsl:stylesheet>
            """;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Execute(Probe probe, int capacity, bool debug, int delivery)
    {
        string xml = "<doc>" + string.Concat(Enumerable.Range(1, probe.Rows).Select(row =>
            $"<row id='{row}' k='{row % 2}' pair='{(row - 1) / 2}'><value>{Payload(row)}</value></row>")) + "</doc>";
        using var input = new ObservingReader(XmlReader.Create(new StringReader(xml)), probe);
        using var sink = new StringWriter(CultureInfo.InvariantCulture);
        var settings = new XsltSettings { StreamedExecution = true, IncludeDebugInformation = debug, StreamingBufferCapacity = capacity };
        using var capture = new StreamingExecutionCapture();
        using (StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet))
        {
            if (delivery == 0)
            {
                var transformer = new XsltCompiler { Settings = settings }.Compile(new StringReader(Sheet(probe.Grouping))).CreateTransformer();
                transformer.MessageHandler = probe.Sample;
                transformer.ApplyTemplates(input, sink);
            }
            else
            {
                var transform = new XslCompiledTransform(debug);
                using var sheet = XmlReader.Create(new StringReader(Sheet(probe.Grouping)));
#if NET10_0_OR_GREATER
                if (delivery == 2)
                {
                    using var image = new MemoryStream();
                    transform.CompileToAssembly(sheet, settings, null, "ForkGroups" + Guid.NewGuid().ToString("N"), "Saved.ForkGroups", image);
                    transform.Load(Assembly.Load(image.ToArray()).GetType("Saved.ForkGroups", true)!, settings);
                }
                else
#endif
                    transform.Load(sheet, settings, null);
                var arguments = new XsltArgumentList();
                arguments.XsltMessageEncountered += (_, message) => probe.Sample(message.Message);
                transform.Transform(input, arguments, sink);
            }
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        Assert.Equal(string.Concat(probe.ExpectedGroups.Select(g => string.Join("|", g.Select(Payload)) + ";")), sink.ToString());
        Assert.Equal(probe.ExpectedGroups.Length, probe.Groups);
        var execution = Assert.Single(capture.Snapshot().Executions);
        Assert.True(execution.BodyCompleted);
        Assert.True(Assert.Single(execution.Sources).ActualForward);
    }

    // Observe the actual strings returned to the engine, holding only weak references. Reading
    // values never advances the parser; each physical text token is recorded once. No copy,
    // extra stylesheet parameter or grounding operation is introduced by this observer.
    private sealed class ObservingReader(XmlReader input, Probe probe) : XmlWrappingReader(input)
    {
        private bool _recorded;
        public override bool Read()
        {
            bool result = base.Read();
            _recorded = false;
            if (result && NodeType == XmlNodeType.EndElement && LocalName == "row") Interlocked.Increment(ref probe.ReadRows);
            return result;
        }
        public override string Value
        {
            get
            {
                string value = base.Value;
                if (!_recorded && NodeType == XmlNodeType.Text)
                {
                    _recorded = true;
                    probe.Record(value);
                }
                return value;
            }
        }
    }

    private sealed class Probe(string grouping, int rows, bool retain)
    {
        internal readonly string Grouping = grouping;
        internal readonly int Rows = rows;
        internal readonly int[][] ExpectedGroups = GroupOrder(grouping, rows);

        private static int[][] GroupOrder(string kind, int count)
        {
            if (kind == "group-by-one") return new[] { Enumerable.Range(1, count).ToArray() };
            var groups = Enumerable.Range(1, count).GroupBy(row => kind == "group-by-sorted" ? row % 2 : (row - 1) / 2);
            return (kind == "adjacent-forward" ? groups : groups.OrderByDescending(g => g.Key)).Select(g => g.ToArray()).ToArray();
        }
        internal int ReadRows, Groups, CharactersAtFirstGroup;
        internal readonly List<WeakReference<string>> Payloads = new();
        internal readonly List<string> Kept = new();

        internal void Record(string value)
        {
            Assert.Equal(Payload(Payloads.Count + 1), value);
            Payloads.Add(new(value));
            if (retain) Kept.Add(value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Sample(string message)
        {
            Assert.Equal((Groups + 1).ToString(CultureInfo.InvariantCulture), message);
            if (Groups++ != 0) return;
            if (Grouping == "adjacent-forward")
            {
                Assert.InRange(Volatile.Read(ref ReadRows), 0, Rows - 1);
                Assert.InRange(Payloads.Count, 0, Rows - 1);
            }
            else
            {
                Assert.Equal(Rows, Volatile.Read(ref ReadRows));
                Assert.Equal(Rows, Payloads.Count);
            }
            Collect();
            if (Grouping != "adjacent-forward") Assert.Equal(Rows, LivePayloads(this));
            foreach (var weak in Payloads)
            {
                bool live = weak.TryGetTarget(out string? value);
                if (Grouping != "adjacent-forward") Assert.True(live);
                if (live) CharactersAtFirstGroup += value!.Length;
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int LivePayloads(Probe probe) => probe.Payloads.Count(weak => weak.TryGetTarget(out _));
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private static void AssertReleased(Probe probe) { Collect(); Assert.Equal(0, LivePayloads(probe)); }
}
