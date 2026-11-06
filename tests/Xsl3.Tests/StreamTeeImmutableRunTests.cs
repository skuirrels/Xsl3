// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class StreamTeeImmutableRunTests
{
    private sealed record Item(int Number);

    [Fact]
    public void AReturnedRunReleasesItsPartialPrefixBeforeAnotherRead()
    {
        int produced = 0;
        using var tee = new StreamTee<Item>(destination => {
            int count = Math.Min(destination.Length, 12 - produced);
            for (int i = 0; i < count; i++) destination.Span[i] = new(++produced);
            return new(count, produced == 12);
        }, 1, 64);
        Assert.Equal(3, tee.GetReader(0).ReadUntilImmutable(item => item.Number == 3)!.Number);
        Assert.Equal(9, tee.BufferedCount);
        Assert.Null(tee.GetReader(0).ReadUntilImmutable(_ => false));
        Assert.Equal(0, tee.BufferedCount);
    }

    [Fact]
    public void AcceptedMarkersPrecedeTheOriginalSourceFailure()
    {
        int produced = 0;
        var failure = new IOException("source failure");
        using var tee = new StreamTee<Item>(() => ++produced <= 9 ? new(produced) : throw failure, 1, 64);
        Assert.Equal(4, tee.GetReader(0).ReadUntilImmutable(item => item.Number % 4 == 0)!.Number);
        Assert.Equal(8, tee.GetReader(0).ReadUntilImmutable(item => item.Number % 4 == 0)!.Number);
        Assert.Same(failure, Assert.Throws<IOException>(() => tee.GetReader(0).ReadUntilImmutable(_ => false)));
        Assert.Equal(0, tee.BufferedCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void ImmutableRunsAndSingleClaimsShareAnAliasedReader(int capacity)
    {
        const int count = 20000;
        int produced = 0;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var start = new Barrier(5);
        using var tee = new StreamTee<Item>(destination => {
            int n = Math.Min(destination.Length, count - produced);
            for (int i = 0; i < n; i++) destination.Span[i] = new(++produced);
            return new(n, produced == count);
        }, 1, capacity, cancellation.Token);
        var outputs = new ConcurrentQueue<int>();
        var errors = new ConcurrentQueue<Exception>();
        var workers = new Thread[4];
        for (int i = 0; i < workers.Length; i++)
        {
            bool skip = i % 2 != 0;
            workers[i] = new Thread(() => {
                try
                {
                    start.SignalAndWait(cancellation.Token);
                    while (true)
                    {
                        Item? item = skip ? tee.GetReader(0).ReadUntilImmutable(value => value.Number % 3 == 0) : tee.GetReader(0).Read();
                        if (item == null) break;
                        outputs.Enqueue(item.Number);
                    }
                }
                catch (Exception error) { errors.Enqueue(error); }
            }) { IsBackground = true };
            workers[i].Start();
        }
        try
        {
            start.SignalAndWait(cancellation.Token);
            foreach (Thread worker in workers) Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
            Assert.Empty(errors);
            int[] values = outputs.ToArray();
            Assert.Equal(values.Length, values.Distinct().Count());
            Assert.All(values, value => Assert.InRange(value, 1, count));
            Assert.Equal(Enumerable.Range(1, count).Where(value => value % 3 == 0), values.Where(value => value % 3 == 0).Order());
            Assert.Equal(count, produced);
            Assert.Equal(0, tee.BufferedCount);
        }
        finally
        {
            cancellation.Cancel();
            tee.Dispose();
            foreach (Thread worker in workers) Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
        }
    }
}
