// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class CallerTeeAliasTests
{
    private sealed record Event(int Number);

    [Theory]
    [InlineData(1, false)]
    [InlineData(7, false)]
    [InlineData(64, false)]
    [InlineData(1, true)]
    [InlineData(64, true)]
    public void CallerBackedAliasesClaimEachEpochOnceAndDisposeTogether(int capacity, bool disposeDuringReads)
    {
        const int total = 4096;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var caller = new ForkCaller(cancellation.Token);
        using var entered = new ManualResetEventSlim();
        int produced = 0, stopped = 0;
        using var tee = new StreamTee<Event>(destination => {
            int count = 0;
            while (count < destination.Length && produced < total)
                destination.Span[count++] = new(++produced);
            return new(count, produced == total);
        }, 2, capacity, cancellation.Token, productionCaller: caller);
        caller.Start(3);
        var errors = new ConcurrentQueue<Exception>();
        var claims = new ConcurrentBag<int>();
        var threads = new Thread[3];
        int started = 0;
        Thread? disposer = null;
        try
        {
            for (int i = 0; i < threads.Length; i++)
            {
                int index = i;
                threads[i] = new Thread(() => {
                    int seen = 0;
                    try
                    {
                        var reader = tee.GetReader(index == 2 ? 1 : 0);
                        while ((index == 0 ? reader.ReadUntilImmutable(item => item.Number % 3 == 0) : reader.Read()) is { } item)
                        {
                            if (index == 2) Assert.Equal(++seen, item.Number);
                            else { claims.Add(item.Number); seen++; }
                            if (disposeDuringReads && seen == 128)
                            {
                                entered.Set();
                                // Keep the test's disposal boundary deterministic; source
                                // progress cannot turn an intended dispose into normal EOF.
                                cancellation.Token.WaitHandle.WaitOne();
                                cancellation.Token.ThrowIfCancellationRequested();
                            }
                        }
                        if (!disposeDuringReads && index == 2) Assert.Equal(total, seen);
                    }
                    catch (ObjectDisposedException) when (disposeDuringReads) { Interlocked.Increment(ref stopped); }
                    catch (OperationCanceledException) when (disposeDuringReads && entered.IsSet) { Interlocked.Increment(ref stopped); }
                    catch (Exception error) { errors.Enqueue(error); cancellation.Cancel(); }
                    finally { caller.WorkerDone(); }
                }) { IsBackground = true };
                threads[i].Start();
                started++;
            }
            if (disposeDuringReads)
            {
                disposer = new Thread(() => {
                    try
                    {
                        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                        tee.Dispose();
                        cancellation.Cancel();
                    }
                    catch (Exception error) { errors.Enqueue(error); cancellation.Cancel(); }
                }) { IsBackground = true };
                disposer.Start();
            }
            Exception? pumpError = Record.Exception(() => caller.Pump(tee.ProduceRequested));
            foreach (Thread thread in threads) Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            if (disposer != null) Assert.True(disposer.Join(TimeSpan.FromSeconds(5)));
            Assert.Empty(errors);
            if (!disposeDuringReads)
            {
                Assert.Null(pumpError);
                Assert.NotEmpty(claims);
                Assert.Equal(claims.Count, claims.Distinct().Count());
                Assert.All(claims, value => Assert.InRange(value, 1, total));
                tee.GetReader(0).Dispose(); tee.GetReader(1).Dispose();
            }
            else Assert.Equal(3, stopped);
            Assert.Equal(0, tee.BufferedCount);
            Assert.InRange(tee.HighWaterMark, 1, capacity);
        }
        finally
        {
            cancellation.Cancel(); caller.Dispose(); tee.Dispose();
            for (int i = 0; i < started; i++) threads[i].Join(TimeSpan.FromSeconds(5));
            disposer?.Join(TimeSpan.FromSeconds(5));
        }
    }
}
