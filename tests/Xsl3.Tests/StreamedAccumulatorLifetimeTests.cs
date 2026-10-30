// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.Tests;

public class StreamedAccumulatorLifetimeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(65536)]
    [InlineData(1048576)]
    public void ScalarPhaseValuesAndFramesDoNotAccumulateWithProcessedRecords(long bytes)
    {
        using var input = new StreamingProbe.GeneratedInput(bytes);
        using var source = new StreamedForkAccumulatorTests.Source(XmlReader.Create(input, new XmlReaderSettings { CloseInput = false }));
        var nodes = new List<WeakReference<object>>();
        var metadata = new List<WeakReference<object>>();
        var values = new List<WeakReference<object>>();
        Assert.True(source.Focus.MoveToFirstChild()); // doc
        Assert.True(source.Focus.MoveToFirstChild()); // first row
        int rows = 0, samples = 0, maximumFrames = 0, maximumMetadata = 0, maximumValues = 0;
        do
        {
            RecordCurrent(source, nodes, metadata, values);
            rows++;
            if (rows % 128 == 0) Sample();
        } while (Next(source));
        Sample();
        Assert.Equal(input.Records, rows);
        Assert.Equal(rows, source.Starts);
        Assert.Equal(rows, source.Ends);
        source.Focus.MoveToRoot();
        source.Close();
        Collect();
        Assert.Equal(0, Alive(nodes));
        Assert.Equal(0, Alive(metadata));
        // The document root legitimately publishes the final scalar after-value.
        Assert.InRange(Alive(values), 0, 1);
        Assert.Equal(bytes, input.ReadBytes);
        Assert.True(input.CanRead);
        output.WriteLine("Input bytes={0}; records={1}; samples={2}; maximum retained row frames={3}; row metadata={4}; scalar phase lists={5}",
            bytes, rows, samples, maximumFrames, maximumMetadata, maximumValues);
        GC.KeepAlive(source);

        void Sample()
        {
            Collect();
            int frameCount = Alive(nodes), metadataCount = Alive(metadata), valueCount = Alive(values);
            // Navigation retains the current row header and its two phase values.
            // Earlier rows have no explicit snapshots or result copies in this test.
            Assert.InRange(frameCount, 0, 1);
            Assert.InRange(metadataCount, 0, 1);
            Assert.InRange(valueCount, 0, 2);
            maximumFrames = Math.Max(maximumFrames, frameCount);
            maximumMetadata = Math.Max(maximumMetadata, metadataCount);
            maximumValues = Math.Max(maximumValues, valueCount);
            samples++;
        }
    }

    [Theory]
    [InlineData(1, 1, 65536)]
    [InlineData(1, 1, 1048576)]
    [InlineData(7, 8192, 65536)]
    [InlineData(7, 8192, 1048576)]
    [InlineData(64, 128, 65536)]
    [InlineData(64, 128, 1048576)]
    public void RollingAtomicValuesFollowTheWindowBoundRatherThanProcessedRecords(int width, int payloadCopies, long bytes)
    {
        using var input = new StreamingProbe.GeneratedInput(bytes);
        using var source = new StreamedForkAccumulatorTests.Source(
            XmlReader.Create(input, new XmlReaderSettings { CloseInput = false }),
            initialValue: Array.Empty<XPathItem>(), rule: (node, value, end) => {
                if (!end) return value;
                var result = new List<XPathItem>();
                for (int i = Math.Max(0, value.Count - width + 1); i < value.Count; i++) result.Add(value[i]);
                result.Add(AtomicValue.OfString(string.Concat(Enumerable.Repeat(node.GetAttribute("id", ""), payloadCopies))));
                return result;
            });
        var nodes = new List<WeakReference<object>>();
        var metadata = new List<WeakReference<object>>();
        var phases = new List<WeakReference<object>>();
        var values = new AtomicValues();
        Assert.True(source.Focus.MoveToFirstChild());
        Assert.True(source.Focus.MoveToFirstChild());
        int rows = 0, samples = 0, maximumValues = 0, maximumCharacters = 0, maximumPayloadLength = 0;
        do
        {
            maximumPayloadLength = Math.Max(maximumPayloadLength, source.Focus.GetAttribute("id", "").Length * payloadCopies);
            RecordWindow(source, width, rows, nodes, metadata, phases, values);
            rows++;
            if (rows % 128 == 0) Sample();
        } while (Next(source));
        Sample();
        Assert.Equal(input.Records, rows);
        Assert.Equal(rows, source.Starts);
        Assert.Equal(rows, source.Ends);
        source.Focus.MoveToRoot();
        source.Close();
        Collect();
        Assert.Equal(0, Alive(nodes));
        Assert.Equal(0, Alive(metadata));
        Assert.InRange(Alive(phases), 0, 1);
        // The retained root may publish its final window after the input closes.
        Assert.InRange(values.Snapshot().Count, 0, width);
        Assert.Equal(bytes, input.ReadBytes);
        Assert.True(input.CanRead);
        output.WriteLine("Input bytes={0}; records={1}; width={2}; samples={3}; maximum retained atomic values={4}; payload characters={5}; observed maximum item characters={6}",
            bytes, rows, width, samples, maximumValues, maximumCharacters, maximumPayloadLength);
        GC.KeepAlive(source);

        void Sample()
        {
            Collect();
            Assert.InRange(Alive(nodes), 0, 1);
            Assert.InRange(Alive(metadata), 0, 1);
            Assert.InRange(Alive(phases), 0, 2);
            var snapshot = values.Snapshot();
            // Before/after windows overlap except for one entering/leaving value.
            Assert.InRange(snapshot.Count, 0, width + 1);
            Assert.InRange(snapshot.Characters, 0, (width + 1) * maximumPayloadLength);
            maximumValues = Math.Max(maximumValues, snapshot.Count);
            maximumCharacters = Math.Max(maximumCharacters, snapshot.Characters);
            samples++;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordWindow(StreamedForkAccumulatorTests.Source source, int width, int previousRows,
        List<WeakReference<object>> nodes, List<WeakReference<object>> metadata, List<WeakReference<object>> phases,
        AtomicValues values)
    {
        nodes.Add(new(source.Focus.PositionNode));
        var before = source.Items(source.Focus);
        var after = source.Items(source.Focus, after: true);
        Assert.Equal(Math.Min(previousRows, width), before.Count);
        Assert.Equal(Math.Min(previousRows + 1, width), after.Count);
        // Empty coercions use the shared runtime sequence, whose static lifetime is
        // independent of this document. Nonempty before/after arrays belong to frames.
        if (before.Count == 0) Assert.Same(Sequences.Empty, before);
        else phases.Add(new(before));
        phases.Add(new(after));
        values.Record(before); values.Record(after);
        metadata.Add(new(Assert.IsAssignableFrom<object>(source.Focus.PositionNode.ObserverValues)));
    }

    private sealed class AtomicValues
    {
        private readonly ConditionalWeakTable<XPathItem, object> _seen = new();
        private readonly object _marker = new();
        private readonly List<WeakReference<XPathItem>> _values = new();

        internal void Record(IList<XPathItem> items)
        {
            foreach (var item in items)
            {
                Assert.IsType<StringValue>(item);
                if (_seen.TryGetValue(item, out _)) continue;
                _seen.Add(item, _marker);
                _values.Add(new(item));
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal (int Count, int Characters) Snapshot()
        {
            int count = 0, characters = 0;
            foreach (var reference in _values)
                if (reference.TryGetTarget(out var item)) { count++; characters += item.Value.Length; }
            return (count, characters);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordCurrent(StreamedForkAccumulatorTests.Source source, List<WeakReference<object>> nodes,
        List<WeakReference<object>> metadata, List<WeakReference<object>> values)
    {
        Assert.Equal("row", source.Focus.LocalName);
        nodes.Add(new(source.Focus.PositionNode));
        values.Add(new(source.Items(source.Focus)));
        values.Add(new(source.Items(source.Focus, after: true)));
        metadata.Add(new(Assert.IsAssignableFrom<object>(source.Focus.PositionNode.ObserverValues)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Next(StreamedForkAccumulatorTests.Source source) => source.Focus.MoveToNext("row", "");

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Alive(IEnumerable<WeakReference<object>> references)
    {
        int count = 0;
        foreach (var reference in references) if (reference.TryGetTarget(out _)) count++;
        return count;
    }
}
