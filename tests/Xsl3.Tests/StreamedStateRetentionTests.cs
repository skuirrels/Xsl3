// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Globalization;
using System.IO;
using System.Runtime;
using System.Xml;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.Tests;

/// <summary>
/// P7 S5 state bounds through compiled stylesheets (docs/plan/p7-phase-a.md): xsl:iterate parameters, an
/// adjacent group and node-valued accumulator values must not retain processed rows. The live managed heap
/// is sampled from xsl:message callbacks after full collections, and growth is measured within one run from
/// the first sample, so state that is not reachable from later rows must not accumulate over ~146,000 rows.
/// A warm-up run on a smaller input loads types and caches first. Logical heap bounds, not process RSS.
/// </summary>
[Collection("Process-wide state")]
public class StreamedStateRetentionTests(ITestOutputHelper output)
{
    private const long SmallBytes = 1 << 20, LargeBytes = 16 << 20;
    // One retained object per row exceeds this several times over the ~146,000 rows of the larger input.
    private const long Tolerance = 1 << 20;

    private static string Sheet(string body, string mode = "<xsl:mode streamable='yes'/>") =>
        "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:map='http://www.w3.org/2005/xpath-functions/map' exclude-result-prefixes='xs map'>" +
        "<xsl:output method='xml' omit-xml-declaration='yes'/>" + mode + body + "</xsl:stylesheet>";

    // Each workload emits a message starting with a row id about every EVERY rows; the handler samples there.
    private static string Workload(string name, long every) => name switch
    {
        "iterate" => Sheet($$"""
            <xsl:template match='/'><out><xsl:iterate select='doc/row'>
              <xsl:param name='total' select='0' as='xs:integer'/><xsl:param name='last' select='()' as='element()?'/>
              <xsl:on-completion><total n='{$total}'/><xsl:copy-of select='$last'/></xsl:on-completion>
              <xsl:if test='xs:integer(@id) mod {{every}} = 0'><xsl:message select='string(@id)'/></xsl:if>
              <xsl:next-iteration><xsl:with-param name='total' select='$total + xs:integer(@amount)'/><xsl:with-param name='last' select='copy-of(.)'/></xsl:next-iteration>
            </xsl:iterate></out></xsl:template>
            """),
        // Control: the same iteration, but its parameter keeps an entry for every row (a persistent map,
        // so the control stays linear).
        "retain" => Sheet($$"""
            <xsl:template match='/'><out><xsl:iterate select='doc/row'>
              <xsl:param name='seen' select='map{}' as='map(*)'/>
              <xsl:on-completion><total n='{map:size($seen)}'/></xsl:on-completion>
              <xsl:if test='xs:integer(@id) mod {{every}} = 0'><xsl:message select='string(@id)'/></xsl:if>
              <xsl:next-iteration><xsl:with-param name='seen' select='map:put($seen, string(@id), xs:integer(@amount))'/></xsl:next-iteration>
            </xsl:iterate></out></xsl:template>
            """),
        // Generated groups are two rows long and start at odd ids.
        "adjacent" => Sheet($$"""
            <xsl:template match='/'><out><xsl:for-each-group select='doc/row' group-adjacent='string(@group)'>
              <xsl:if test='xs:integer(@id) mod {{every}} = 1'><xsl:message select='string(@id)'/></xsl:if>
              <g k='{current-grouping-key()}' n='{count(current-group())}'/>
            </xsl:for-each-group></out></xsl:template>
            """),
        // Node-valued state: a window of the last eight constructed elements, replaced at every row.
        "accumulator" => Sheet($$"""
            <xsl:accumulator name='window' initial-value='()' as='element(last)*' streamable='yes'>
              <xsl:accumulator-rule match='row' phase='start'>
                <xsl:sequence select='$value[position() gt last() - 7]'/><last id='{@id}' amount='{@amount}'/>
              </xsl:accumulator-rule>
            </xsl:accumulator>
            <xsl:template match='/'><out><xsl:apply-templates select='doc/row'/></out></xsl:template>
            <xsl:template match='row'>
              <xsl:if test='xs:integer(@id) mod {{every}} = 0'><xsl:message select="string(@id) || ' ' || count(accumulator-before('window'))"/></xsl:if>
            </xsl:template>
            """, "<xsl:mode streamable='yes' use-accumulators='window'/>"),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [InlineData("iterate", false)]
    [InlineData("iterate", true)]
    [InlineData("adjacent", false)]
    [InlineData("adjacent", true)]
    [InlineData("accumulator", false)]
    [InlineData("accumulator", true)]
    public void RetainedStateDoesNotGrowWithProcessedRows(string workload, bool legacy)
    {
        Measurement large = Measure(workload, legacy);
        Assert.InRange(large.GrowthBytes, long.MinValue, Tolerance);
    }

    [Fact]
    public void TheMeasurementDetectsRetainedRows()
    {
        Measurement large = Measure("retain", legacy: false);
        Assert.True(large.GrowthBytes > 8 * Tolerance);
    }

    private Measurement Measure(string workload, bool legacy)
    {
        Run(workload, legacy, SmallBytes); // JIT, type loading and static caches
        Measurement large = Run(workload, legacy, LargeBytes);
        output.WriteLine("{0} legacy={1}: {2} rows, {3} samples, first {4:N0} B, growth {5:N0} B",
            workload, legacy, large.Records, large.Samples, large.FirstLiveBytes, large.GrowthBytes);
        return large;
    }

    private sealed record Measurement(long Records, int Samples, long FirstLiveBytes, long GrowthBytes);

    // Bytes surviving a full blocking compacting collection. GC.GetTotalMemory also counts free
    // space in partly used regions, which moves by tens of megabytes between collections.
    private static long LiveBytes()
    {
        GC.Collect(); GC.WaitForPendingFinalizers();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        return GC.GetGCMemoryInfo(GCKind.FullBlocking).PromotedBytes;
    }

    private static Measurement Run(string workload, bool legacy, long bytes)
    {
        using var input = new StreamingProbe.GeneratedInput(bytes);
        long every = Math.Max(2, input.Records / 32) & ~1L; // even: ids that are 1 modulo it start adjacent groups
        string sheet = Workload(workload, every);
        long first = 0, peak = 0, firstRead = -1;
        int samples = 0;
        void Sample(string message)
        {
            if (firstRead < 0) firstRead = input.ReadBytes;
            long live = LiveBytes();
            if (samples++ == 0) first = live;
            peak = Math.Max(peak, live);
            Assert.True(long.TryParse(message.Split(' ')[0], NumberStyles.None, CultureInfo.InvariantCulture, out _), message);
        }
        var settings = new XsltSettings { StreamedExecution = true };
        using (var reader = XmlReader.Create(input, new XmlReaderSettings { CloseInput = false }))
        {
            if (legacy)
            {
                var transform = new XslCompiledTransform();
                transform.Load(XmlReader.Create(new StringReader(sheet)), settings, null);
                var arguments = new XsltArgumentList();
                arguments.XsltMessageEncountered += (_, e) => Sample(e.Message);
                transform.Transform(reader, arguments, TextWriter.Null);
            }
            else
            {
                var transform = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
                transform.MessageHandler = Sample;
                transform.ApplyTemplates(reader, TextWriter.Null);
            }
        }
        Assert.Equal(bytes, input.ReadBytes);
        Assert.InRange(samples, 16, 64);
        // Forward execution samples before the input is consumed.
        Assert.InRange(firstRead, 0, bytes / 4);
        return new(input.Records, samples, first, peak - first);
    }
}
