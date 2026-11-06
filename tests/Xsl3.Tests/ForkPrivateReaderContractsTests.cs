// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Threading;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class ForkPrivateReaderContractsTests
{
    private sealed record Item(int Number);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false, 1, 1)]
    [InlineData(false, 1, 7)]
    [InlineData(false, 2, 1)]
    [InlineData(false, 2, 7)]
    [InlineData(true, 1, 1)]
    [InlineData(true, 1, 7)]
    [InlineData(true, 2, 1)]
    [InlineData(true, 2, 7)]
    public void ConsumedPrefixIsReleasedBeforeAnotherRead(bool exclusive, int readers, int capacity)
    {
        int next = 0;
        using var tee = new StreamTee<Item>(destination => {
            for (int i = 0; i < destination.Length; i++) destination.Span[i] = new(++next);
            return new(destination.Length, false);
        }, readers, capacity, exclusiveReaders: exclusive);
        WeakReference<Item> consumed = ReadOne(tee, readers);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(consumed.TryGetTarget(out _));
        Assert.Equal(capacity - 1, tee.BufferedCount);
        GC.KeepAlive(tee);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Item> ReadOne(StreamTee<Item> tee, int readers)
    {
        Item first = tee.GetReader(0).Read()!;
        for (int i = 1; i < readers; i++) Assert.Same(first, tee.GetReader(i).Read());
        return new(first);
    }

    [Fact]
    public void ExternalDetachDuringConsumptionPreservesTheRemainingReadersEpochs()
    {
        const int count = 10000;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int next = 0;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var entered = new ManualResetEventSlim();
            using var tee = new StreamTee<Item>(destination => {
                if (next == count) return new(0, true);
                destination.Span[0] = new(++next);
                return new(1, false);
            }, 2, 7, timeout.Token, exclusiveReaders: true);
            var errors = new ConcurrentQueue<Exception>();
            var departing = new Thread(() => {
                try { while (tee.GetReader(0).Read() is { }) entered.Set(); }
                catch (ObjectDisposedException) { }
                catch (Exception error) { errors.Enqueue(error); }
            }) { IsBackground = true };
            var remaining = new Thread(() => {
                try
                {
                    for (int i = 1; i <= count; i++) Assert.Equal(i, tee.GetReader(1).Read()!.Number);
                    Assert.Null(tee.GetReader(1).Read());
                }
                catch (Exception error) { errors.Enqueue(error); }
            }) { IsBackground = true };
            departing.Start();
            remaining.Start();
            try
            {
                Assert.True(entered.Wait(Deadline));
                tee.GetReader(0).Dispose();
                Assert.True(departing.Join(Deadline));
                Assert.True(remaining.Join(Deadline));
                Assert.Empty(errors);
                Assert.Equal(0, tee.BufferedCount);
                Assert.InRange(tee.HighWaterMark, 1, 7);
                Assert.Throws<ObjectDisposedException>(() => tee.GetReader(0).Read());
            }
            finally
            {
                timeout.Cancel();
                tee.Dispose();
                Assert.True(departing.Join(Deadline));
                Assert.True(remaining.Join(Deadline));
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void ReleasingOneSlotWakesTheCallerBeforeTheSlowReaderRequestsMore(int capacity)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var caller = new ForkCaller(timeout.Token);
        caller.Start(2);
        using var produced = new ManualResetEventSlim();
        int next = 0;
        Thread ownerThread = Thread.CurrentThread;
        int owner = ownerThread.ManagedThreadId;
        using var tee = new StreamTee<Item>(destination => {
            Assert.Equal(owner, Environment.CurrentManagedThreadId);
            int count = 0;
            while (count < destination.Length)
            {
                if (next == capacity + 1) return new(count, true);
                destination.Span[count++] = new(++next);
                if (next == capacity + 1) produced.Set();
            }
            return new(count, false);
        }, 2, capacity, timeout.Token, caller, exclusiveReaders: true);
        var errors = new ConcurrentQueue<Exception>();
        Thread Start(Action body)
        {
            var thread = new Thread(() => {
                try { body(); }
                catch (Exception error) { errors.Enqueue(error); }
                finally { caller.WorkerDone(); }
            }) { IsBackground = true };
            thread.Start();
            return thread;
        }
        Thread fast = Start(() => {
            using var reader = tee.GetReader(0);
            for (int i = 1; i <= capacity + 1; i++) Assert.Equal(i, reader.Read()!.Number);
            Assert.Null(reader.Read());
        });
        Thread slow = Start(() => {
            using var reader = tee.GetReader(1);
            Assert.True(SpinWait.SpinUntil(() => tee.WaitingReaders != 0
                && (ownerThread.ThreadState & ThreadState.WaitSleepJoin) != 0, Deadline));
            Assert.Equal(1, reader.Read()!.Number);
            Assert.True(produced.Wait(Deadline), "Freeing the head slot did not wake the caller");
            for (int i = 2; i <= capacity + 1; i++) Assert.Equal(i, reader.Read()!.Number);
            Assert.Null(reader.Read());
        });
        try { caller.Pump(tee.ProduceRequested); }
        finally
        {
            tee.Dispose();
            Assert.True(fast.Join(Deadline));
            Assert.True(slow.Join(Deadline));
        }
        Assert.Empty(errors);
        Assert.InRange(tee.HighWaterMark, 1, capacity);
    }

    [Fact]
    public void AnotherThreadCannotConsumeAnOwnedCursorButCanDetachIt()
    {
        int next = 0;
        using var tee = new StreamTee<Item>(destination => {
            destination.Span[0] = new(++next);
            return new(1, false);
        }, 1, 1, exclusiveReaders: true);
        var reader = tee.GetReader(0);
        Assert.Equal(1, reader.Read()!.Number);
        Exception? wrongThread = null;
        var alias = new Thread(() => wrongThread = Record.Exception(() => reader.Read()));
        alias.Start();
        Assert.True(alias.Join(Deadline));
        Assert.IsType<InvalidOperationException>(wrongThread);
        Assert.Equal(2, reader.Read()!.Number);
        var detach = new Thread(reader.Dispose);
        detach.Start();
        Assert.True(detach.Join(Deadline));
        Assert.Throws<ObjectDisposedException>(() => reader.Read());
        Assert.Equal(0, tee.ActiveReaders);
    }

    [Fact]
    public void EveryReaderReceivesTheAcceptedPrefixBeforeTheOriginalSourceFault()
    {
        var source = Enumerable.Range(1, 5).ToArray();
        var failure = new IOException("source fault after accepted prefix");
        int calls = 0;
        var observed = new ConcurrentQueue<int[]>();
        Action<StreamTee<Item>.Reader, CancellationToken> body = (reader, cancellation) => {
            var values = new System.Collections.Generic.List<int>();
            Exception actual = Assert.Throws<IOException>(() => {
                while (reader.Read() is { } item) values.Add(item.Number);
            });
            Assert.Same(failure, actual);
            observed.Enqueue(values.ToArray());
        };
        ForkProngs.Run(() => calls < source.Length ? new Item(source[calls++]) : throw failure,
            new[] { body, body }, 7, exclusiveReaders: true);
        Assert.Equal(2, observed.Count);
        foreach (int[] values in observed) Assert.Equal(source, values);
    }
}
