// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Xml;
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
