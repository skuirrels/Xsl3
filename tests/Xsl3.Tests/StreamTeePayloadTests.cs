// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Xsl3.Streaming;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.Tests;

public class StreamTeePayloadTests(ITestOutputHelper output)
{
    private sealed class Payload(int number, int bytes)
    {
        internal readonly int Number = number;
        internal readonly byte[] Bytes = new byte[bytes];
    }

    [Theory]
    [InlineData(1, 2, 1024, 16)]
    [InlineData(1, 2, 1024, 64)]
    [InlineData(7, 3, 8192, 16)]
    [InlineData(7, 3, 8192, 64)]
    [InlineData(64, 4, 1024, 16)]
    [InlineData(64, 4, 1024, 64)]
    public void RetainedPayloadBytesFollowTheRingBoundRatherThanTheInputLength(int capacity, int readers, int payloadBytes, int checkpoints)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var caller = new ForkCaller(cancellation.Token);
        var references = new List<WeakReference<Payload>>();
        var current = new Payload?[readers];
        var failures = new ConcurrentQueue<Exception>();
        var releases = Enumerable.Range(0, checkpoints).Select(_ => new ManualResetEventSlim()).ToArray();
        int batchSize = checked(capacity * 4 + 3), total = checked(batchSize * checkpoints), produced = 0, ready = 0;
        long maximumRetainedBytes = 0;
        using var tee = new StreamTee<Payload>(destination => {
            int count = 0;
            while (count < destination.Length && produced < total)
            {
                var payload = new Payload(++produced, payloadBytes);
                references.Add(new(payload));
                destination.Span[count++] = payload;
            }
            return new(count, produced == total);
        }, readers, capacity, cancellation.Token, productionCaller: caller);
        caller.Start(readers);
        var workers = new Thread[readers];
        int started = 0;
        try
        {
            for (int i = 0; i < readers; i++)
            {
                int index = i;
                workers[i] = new Thread(() => {
                    using var reader = tee.GetReader(index);
                    try
                    {
                        for (int checkpoint = 0; checkpoint < checkpoints; checkpoint++)
                        {
                            // All Read frames have returned before reporting readiness.
                            // Each worker retains only its explicitly recorded current item.
                            Advance(reader, current, index, batchSize);
                            int point = checkpoint;
                            caller.Invoke(() => {
                                if (++ready != readers) return;
                                Assert.All(current, item => Assert.Equal((point + 1) * batchSize, item!.Number));
                                Collect();
                                (int alive, long bytes) = Retained(references);
                                // Every worker is at the same event. It is no longer in
                                // the ring, so all workers together retain one payload.
                                Assert.Equal(tee.BufferedCount + 1, alive);
                                Assert.Equal((long)alive * payloadBytes, bytes);
                                Assert.InRange(bytes, payloadBytes, (long)(capacity + 1) * payloadBytes);
                                maximumRetainedBytes = Math.Max(maximumRetainedBytes, bytes);
                                ready = 0;
                                releases[point].Set();
                            }, cancellation.Token);
                            releases[point].Wait(cancellation.Token);
                        }
                    }
                    catch (Exception error)
                    {
                        failures.Enqueue(error);
                        cancellation.Cancel();
                    }
                    finally { caller.WorkerDone(); }
                }) { IsBackground = true };
                workers[i].Start();
                started++;
            }
            Exception? pumpError = Record.Exception(() => caller.Pump(tee.ProduceRequested));
            foreach (Thread worker in workers) Assert.True(worker.Join(TimeSpan.FromSeconds(10)), "Payload reader did not finish");
            Assert.Empty(failures);
            Assert.Null(pumpError);
            Assert.Equal(total, produced);
            Assert.Equal(0, tee.BufferedCount);
            Assert.InRange(tee.HighWaterMark, 1, capacity);
            Array.Clear(current);
            Collect();
            Assert.Equal((0, 0L), Retained(references));
            output.WriteLine("Events={0}; readers={1}; capacity={2}; payload bytes={3}; checkpoints={4}; maximum retained payload bytes={5}; bound={6}",
                total, readers, capacity, payloadBytes, checkpoints, maximumRetainedBytes, (long)(capacity + 1) * payloadBytes);
        }
        finally
        {
            cancellation.Cancel();
            caller.Dispose();
            tee.Dispose();
            foreach (var release in releases) release.Set();
            for (int i = 0; i < started; i++)
                Assert.True(workers[i].Join(TimeSpan.FromSeconds(10)), "Payload reader did not stop during cleanup");
            foreach (var release in releases) release.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Advance(StreamTee<Payload>.Reader reader, Payload?[] current, int index, int count)
    {
        for (int i = 0; i < count; i++) current[index] = reader.Read() ?? throw new InvalidOperationException("Unexpected source exhaustion");
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (int Count, long Bytes) Retained(IEnumerable<WeakReference<Payload>> references)
    {
        int count = 0;
        long bytes = 0;
        foreach (var reference in references)
            if (reference.TryGetTarget(out var payload)) { count++; bytes += payload.Bytes.LongLength; }
        return (count, bytes);
    }
}
