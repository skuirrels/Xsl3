// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.Threading;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class BoundedFilteredForkTests
{
    private sealed record Event(int Number);

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void EmptyFilteredBatchesWakeEveryReader(int capacity)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int calls = 0;
        Action<StreamTee<Event>.Reader, CancellationToken> body = (reader, token) => {
            Assert.Equal(1, reader.Read()!.Number);
            Assert.Null(reader.Read());
        };
        ForkProngs.RunSource<Event>(() => {
            int call = ++calls;
            return call <= 3 * capacity ? default
                : call == 3 * capacity + 1 ? new(new Event(1), false) : new(null, true);
        }, new[] { body, body }, capacity, cancellation.Token);
        Assert.Equal(3 * capacity + 2, calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    public void EmptyScanYieldsToAlreadyQueuedCallerCallback(int capacity)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var caller = new ForkCaller(cancellation.Token);
        using var secondScan = new ManualResetEventSlim();
        using var queued = new ManualResetEventSlim();
        using var delivered = new ManualResetEventSlim();
        Thread? outputWorker = null;
        int calls = 0, callerThread = Environment.CurrentManagedThreadId;
        var bodies = new Action<StreamTee<Event>.Reader, CancellationToken>[] {
            (reader, token) => {
                Assert.Equal(1, reader.Read()!.Number);
                secondScan.Wait(token);
                outputWorker = Thread.CurrentThread;
                queued.Set();
                caller.Invoke(() => {
                    Assert.Equal(callerThread, Environment.CurrentManagedThreadId);
                    delivered.Set();
                }, token);
            },
            (reader, token) => {
                Assert.Equal(1, reader.Read()!.Number);
                Assert.Null(reader.Read());
            },
        };
        ForkProngs.RunSource<Event>(() => {
            int call = ++calls;
            if (call == 1) return new(new Event(1), false);
            if (call == capacity + 1)
            {
                secondScan.Set();
                queued.Wait(cancellation.Token);
                Assert.True(SpinWait.SpinUntil(() =>
                    (outputWorker!.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
            }
            if (delivered.IsSet) return new(null, true);
            Assert.True(call <= 2 * capacity, "Empty scan withheld caller callback");
            return default;
        }, bodies, capacity, cancellation.Token, caller);
        Assert.True(delivered.IsSet);
    }
}
