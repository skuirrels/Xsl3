// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class StreamTeeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private sealed record Event(int Number);

    private static Thread Start(Action body, ConcurrentQueue<Exception> errors)
    {
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception error) { errors.Enqueue(error); }
        }) { IsBackground = true };
        thread.Start();
        return thread;
    }

    private static void Join(Thread thread) => Assert.True(thread.Join(Deadline), "A tee reader did not finish");
    private static void Blocked(StreamTee<Event> tee) =>
        Assert.True(SpinWait.SpinUntil(() => tee.WaitingReaders != 0, Deadline), "No tee reader reached backpressure");

    [Fact]
    public void ReadersReceiveTheSameOrderedEventsAndReadTheSourceOnlyOnce()
    {
        int calls = 0;
        using var tee = new StreamTee<Event>(() => ++calls <= 100 ? new(calls) : null, readers: 3, capacity: 2);
        for (int n = 1; n <= 100; n++)
        {
            Event first = Assert.IsType<Event>(tee.GetReader(0).Read());
            Assert.Equal(n, first.Number);
            Assert.Same(first, tee.GetReader(1).Read());
            Assert.Same(first, tee.GetReader(2).Read());
            Assert.Equal(0, tee.BufferedCount);
        }
        for (int i = 0; i < 3; i++)
        {
            Assert.Null(tee.GetReader(i).Read());
            Assert.Null(tee.GetReader(i).Read());
        }
        Assert.Equal(101, calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void FastReaderWaitsForTheSlowReaderAndTheRingStaysBounded(int capacity)
    {
        int calls = 0;
        using var watchdog = new CancellationTokenSource(Deadline);
        using var tee = new StreamTee<Event>(() => ++calls <= 1000 ? new(calls) : null, 2, capacity, watchdog.Token);
        var errors = new ConcurrentQueue<Exception>();
        var fast = new List<int>();
        Thread worker = Start(() =>
        {
            using var reader = tee.GetReader(0);
            while (reader.Read() is { } item) fast.Add(item.Number);
        }, errors);
        try
        {
            Blocked(tee);
            Assert.Equal(capacity, Volatile.Read(ref calls));
            Assert.Equal(capacity, tee.BufferedCount);
            using var slow = tee.GetReader(1);
            for (int n = 1; n <= 1000; n++) Assert.Equal(n, slow.Read()!.Number);
            Assert.Null(slow.Read());
            Join(worker);
            Assert.Empty(errors);
            Assert.Equal(1000, fast.Count);
            for (int i = 0; i < fast.Count; i++) Assert.Equal(i + 1, fast[i]);
            Assert.Equal(capacity, tee.HighWaterMark);
            Assert.Equal(0, tee.BufferedCount);
        }
        finally { tee.Dispose(); Join(worker); }
    }

    [Fact]
    public void AProngThatStopsEarlyDoesNotHoldBackOtherReaders()
    {
        int calls = 0;
        using var watchdog = new CancellationTokenSource(Deadline);
        using var tee = new StreamTee<Event>(() => ++calls <= 100 ? new(calls) : null, 2, 1, watchdog.Token);
        var errors = new ConcurrentQueue<Exception>();
        int count = 0;
        Thread worker = Start(() =>
        {
            using var reader = tee.GetReader(0);
            while (reader.Read() != null) count++;
        }, errors);
        try
        {
            Blocked(tee);
            tee.GetReader(1).Dispose();
            tee.GetReader(1).Dispose();
            Join(worker);
            Assert.Empty(errors);
            Assert.Equal(100, count);
            Assert.Equal(0, tee.BufferedCount);
            Assert.Throws<ObjectDisposedException>(() => tee.GetReader(1).Read());
        }
        finally { tee.Dispose(); Join(worker); }
    }

    [Fact]
    public void SourceFailureFollowsBufferedEventsForEveryReader()
    {
        int calls = 0;
        var failure = new IOException("input failure");
        using var tee = new StreamTee<Event>(() => ++calls <= 2 ? new(calls) : throw failure, 2, 3);
        Event? first = tee.GetReader(0).Read(), second = tee.GetReader(0).Read();
        Assert.Same(failure, Assert.Throws<IOException>(() => tee.GetReader(0).Read()));
        Assert.Same(first, tee.GetReader(1).Read());
        Assert.Same(second, tee.GetReader(1).Read());
        Assert.Same(failure, Assert.Throws<IOException>(() => tee.GetReader(1).Read()));
        Assert.Same(failure, Assert.Throws<IOException>(() => tee.GetReader(1).Read()));
        Assert.Equal(3, calls);
        Assert.Equal(0, tee.BufferedCount);
    }

    [Fact]
    public void CancellationWakesAReaderWaitingForBufferSpace()
    {
        using var cancellation = new CancellationTokenSource();
        using var tee = new StreamTee<Event>(() => new(1), 2, 1, cancellation.Token);
        Assert.NotNull(tee.GetReader(0).Read());
        var errors = new ConcurrentQueue<Exception>();
        Thread worker = Start(() => tee.GetReader(0).Read(), errors);
        try
        {
            Blocked(tee);
            cancellation.Cancel();
            Join(worker);
            var error = Assert.IsType<OperationCanceledException>(Assert.Single(errors));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Throws<OperationCanceledException>(() => tee.GetReader(1).Read());
        }
        finally { tee.Dispose(); Join(worker); }
    }

    [Fact]
    public void DisposalWakesBlockedReadersAndReleasesTheRing()
    {
        using var tee = new StreamTee<Event>(() => new(1), 2, 1);
        Assert.NotNull(tee.GetReader(0).Read());
        var errors = new ConcurrentQueue<Exception>();
        Thread worker = Start(() => tee.GetReader(0).Read(), errors);
        try
        {
            Blocked(tee);
            tee.Dispose();
            Join(worker);
            Assert.IsType<ObjectDisposedException>(Assert.Single(errors));
            Assert.Equal(0, tee.BufferedCount);
        }
        finally { tee.Dispose(); Join(worker); }
    }

    [Fact]
    public void ReadingTheSourceDoesNotBlockConsumptionOfAlreadyBufferedEvents()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var consumed = new ManualResetEventSlim();
        int calls = 0;
        using var tee = new StreamTee<Event>(() =>
        {
            if (++calls == 1) return new(1);
            entered.Set();
            if (!release.Wait(Deadline)) throw new TimeoutException("Source read was not released");
            return null;
        }, 2, 2);
        Event? first = tee.GetReader(0).Read();
        var errors = new ConcurrentQueue<Exception>();
        Thread producer = Start(() => Assert.Null(tee.GetReader(0).Read()), errors);
        Thread? slow = null;
        try
        {
            Assert.True(entered.Wait(Deadline));
            slow = Start(() => { Assert.Same(first, tee.GetReader(1).Read()); consumed.Set(); }, errors);
            Assert.True(consumed.Wait(Deadline), "Source I/O held the ring lock");
            release.Set();
            Join(producer);
            Join(slow);
            Assert.Empty(errors);
        }
        finally { release.Set(); tee.Dispose(); Join(producer); if (slow != null) Join(slow); }
    }

    [Fact]
    public void EventsPublishedAfterTheLastReaderDetachesAreNotRetained()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var tee = new StreamTee<Event>(() =>
        {
            entered.Set();
            if (!release.Wait(Deadline)) throw new TimeoutException("Source read was not released");
            return new(1);
        }, 1, 1);
        var errors = new ConcurrentQueue<Exception>();
        Thread producer = Start(() => tee.GetReader(0).Read(), errors);
        try
        {
            Assert.True(entered.Wait(Deadline));
            tee.GetReader(0).Dispose();
            release.Set();
            Join(producer);
            Assert.IsType<ObjectDisposedException>(Assert.Single(errors));
            Assert.Equal(0, tee.BufferedCount);
            Assert.Equal(0, tee.HighWaterMark);
        }
        finally { release.Set(); tee.Dispose(); Join(producer); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Event> Consume(StreamTee<Event> tee)
    {
        Event item = tee.GetReader(0).Read()!;
        Assert.Same(item, tee.GetReader(1).Read());
        return new(item);
    }

    [Fact]
    public void ConsumedEventsAreCollectibleWhileTheTeeIsStillAlive()
    {
        using var tee = new StreamTee<Event>(() => new(1), 2, 1);
        WeakReference<Event> previous = Consume(tee);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(previous.TryGetTarget(out _));
        Assert.Equal(0, tee.BufferedCount);
        Assert.NotNull(tee.GetReader(0).Read());
        GC.KeepAlive(tee);
    }

    [Fact]
    public void RecursiveSourceRequestsFailInsteadOfDeadlocking()
    {
        StreamTee<Event>? tee = null;
        using var watchdog = new CancellationTokenSource(Deadline);
        using (tee = new StreamTee<Event>(() => tee!.GetReader(0).Read(), 1, 1, watchdog.Token))
            Assert.Throws<InvalidOperationException>(() => tee.GetReader(0).Read());
    }

    [Fact]
    public void DetachingAPartiallyAdvancedReaderReleasesOnlyItsUnreadEvents()
    {
        int calls = 0;
        using var watchdog = new CancellationTokenSource(Deadline);
        using var tee = new StreamTee<Event>(() => ++calls <= 6 ? new(calls) : null, 3, 4, watchdog.Token);
        for (int n = 1; n <= 4; n++) Assert.Equal(n, tee.GetReader(0).Read()!.Number);
        for (int n = 1; n <= 2; n++) Assert.Equal(n, tee.GetReader(1).Read()!.Number);
        Assert.Equal(1, tee.GetReader(2).Read()!.Number);
        tee.GetReader(1).Dispose();
        Assert.Equal(5, tee.GetReader(0).Read()!.Number);
        for (int n = 2; n <= 5; n++) Assert.Equal(n, tee.GetReader(2).Read()!.Number);
        Assert.Equal(0, tee.BufferedCount);
        Assert.Equal(6, tee.GetReader(0).Read()!.Number);
        Assert.Equal(6, tee.GetReader(2).Read()!.Number);
        Assert.Null(tee.GetReader(0).Read());
        Assert.Null(tee.GetReader(2).Read());
        Assert.Equal(7, calls);
    }

    [Fact]
    public void NestedTeesShareOneSourceWithoutDeadlocking()
    {
        int calls = 0;
        using var watchdog = new CancellationTokenSource(Deadline);
        using var outer = new StreamTee<Event>(() => ++calls <= 1000 ? new(calls) : null, 2, 2, watchdog.Token);
        using var source = outer.GetReader(0);
        using var inner = new StreamTee<Event>(source.Read, 2, 1, watchdog.Token);
        var errors = new ConcurrentQueue<Exception>();
        int count = 0;
        Thread first = Start(() =>
        {
            using var reader = inner.GetReader(0);
            Assert.Equal(1, reader.Read()!.Number);
            Assert.Equal(2, reader.Read()!.Number);
        }, errors);
        Thread second = Start(() =>
        {
            using var reader = inner.GetReader(1);
            try { while (reader.Read() is { } item) Assert.Equal(++count, item.Number); }
            finally { source.Dispose(); }
        }, errors);
        try
        {
            using var reader = outer.GetReader(1);
            for (int n = 1; n <= 1000; n++) Assert.Equal(n, reader.Read()!.Number);
            Assert.Null(reader.Read());
            Join(first); Join(second);
            Assert.Empty(errors);
            Assert.Equal(1000, count);
            Assert.Equal(1001, calls);
            Assert.Equal(0, outer.BufferedCount);
            Assert.Equal(0, inner.BufferedCount);
        }
        finally { inner.Dispose(); outer.Dispose(); Join(first); Join(second); }
    }

    [Fact]
    public void BatchesUseOnlyFreeRingSpaceAndPreserveEventIdentity()
    {
        var reservations = new List<int>(); int next = 0;
        using var tee = new StreamTee<Event>(destination => {
            reservations.Add(destination.Length);
            for (int i = 0; i < destination.Length; i++) destination.Span[i] = new(++next);
            return new(destination.Length, false);
        }, 2, 4);
        var items = new Event[5];
        for (int i = 0; i < 4; i++) items[i] = tee.GetReader(0).Read()!;
        Assert.Equal(4, next); Assert.Equal(4, tee.BufferedCount);
        Assert.Same(items[0], tee.GetReader(1).Read());
        items[4] = tee.GetReader(0).Read()!;
        Assert.Equal(new[] { 4, 1 }, reservations);
        Assert.Equal(5, next); Assert.Equal(4, tee.HighWaterMark);
        for (int i = 1; i < items.Length; i++) Assert.Same(items[i], tee.GetReader(1).Read());
        for (int i = 0; i < items.Length; i++) Assert.Equal(i + 1, items[i].Number);
        Assert.Equal(0, tee.BufferedCount);
    }

    [Fact]
    public void EveryReaderSeesTheBatchPrefixBeforeItsSourceFailure()
    {
        var failure = new IOException("batch input failure"); int calls = 0;
        using var tee = new StreamTee<Event>(destination => {
            calls++; destination.Span[0] = new(1); destination.Span[1] = new(2);
            return new(2, true, ExceptionDispatchInfo.Capture(failure));
        }, 2, 4);
        Event first = tee.GetReader(0).Read()!, second = tee.GetReader(0).Read()!;
        Assert.Same(failure, Assert.Throws<IOException>(() => tee.GetReader(0).Read()));
        Assert.Same(first, tee.GetReader(1).Read()); Assert.Same(second, tee.GetReader(1).Read());
        Assert.Same(failure, Assert.Throws<IOException>(() => tee.GetReader(1).Read()));
        Assert.Equal(1, calls); Assert.Equal(0, tee.BufferedCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void CallerOwnedProductionWaitsForDemandAndPublishesThePrefixBeforeFailure(int capacity)
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        using var caller = new ForkCaller(cancellation.Token);
        int owner = Environment.CurrentManagedThreadId, next = 0;
        var failure = new IOException("caller-owned input failure");
        using var tee = new StreamTee<Event>(destination => {
            Assert.Equal(owner, Environment.CurrentManagedThreadId);
            int count = Math.Min(destination.Length, 3 - next);
            for (int i = 0; i < count; i++) destination.Span[i] = new(++next);
            return new(count, next == 3, next == 3 ? ExceptionDispatchInfo.Capture(failure) : null);
        }, 2, capacity, cancellation.Token, productionCaller: caller);
        Assert.False(tee.ProduceRequested());
        Assert.Equal(0, next);
        caller.Start(2);
        var errors = new ConcurrentQueue<Exception>();
        Thread Read(int index) => Start(() => {
            try
            {
                using var reader = tee.GetReader(index);
                for (int i = 1; i <= 3; i++) Assert.Equal(i, reader.Read()!.Number);
                Assert.Same(failure, Assert.Throws<IOException>(() => reader.Read()));
            }
            finally { caller.WorkerDone(); }
        }, errors);
        Thread first = Read(0), second = Read(1);
        try
        {
            caller.Pump(tee.ProduceRequested);
            Join(first); Join(second);
            Assert.Empty(errors);
            Assert.Equal(3, next);
            Assert.InRange(tee.HighWaterMark, 1, capacity);
            Assert.Equal(0, tee.BufferedCount);
        }
        finally { cancellation.Cancel(); tee.Dispose(); Join(first); Join(second); }
    }

    [Fact]
    public void SkippingBufferedEventsPreservesTheOtherReadersInputAndStopsAtTheMatch()
    {
        int next = 0;
        using var tee = new StreamTee<Event>(destination => {
            for (int i = 0; i < destination.Length; i++) destination.Span[i] = new(++next);
            return new(destination.Length, false);
        }, 2, 4);
        Assert.Equal(3, tee.GetReader(0).ReadUntil(item => item.Number == 3)!.Number);
        var expected = new[] { 1, 2, 3, 4 };
        foreach (int number in expected) Assert.Equal(number, tee.GetReader(1).Read()!.Number);
        Assert.Equal(1, tee.BufferedCount);
        Assert.Equal(4, tee.GetReader(0).Read()!.Number);
        Assert.Equal(0, tee.BufferedCount);
        Assert.Equal(4, next);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SkippingReleasesAcceptedEventsBeforeEofOrTheSourceFailure(bool fail)
    {
        var failure = new IOException("skip input failure");
        int calls = 0;
        using var tee = new StreamTee<Event>(destination => {
            calls++;
            destination.Span[0] = new(1); destination.Span[1] = new(2);
            return new(2, true, fail ? ExceptionDispatchInfo.Capture(failure) : null);
        }, 2, 4);
        if (fail) Assert.Same(failure, Assert.Throws<IOException>(() => tee.GetReader(0).ReadUntil(_ => false)));
        else Assert.Null(tee.GetReader(0).ReadUntil(_ => false));
        Assert.Equal(1, tee.GetReader(1).Read()!.Number);
        Assert.Equal(2, tee.GetReader(1).Read()!.Number);
        if (fail) Assert.Same(failure, Assert.Throws<IOException>(() => tee.GetReader(1).Read()));
        else Assert.Null(tee.GetReader(1).Read());
        Assert.Equal(1, calls); Assert.Equal(0, tee.BufferedCount);
    }

    [Fact]
    public void CancellationDuringSkippingReleasesConsumedEvents()
    {
        using var cancellation = new CancellationTokenSource();
        using var tee = new StreamTee<Event>(destination => {
            destination.Span[0] = new(1); destination.Span[1] = new(2);
            return new(2, true);
        }, 1, 4, cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => tee.GetReader(0).ReadUntil(_ => {
            cancellation.Cancel(); return false;
        }));
        Assert.Equal(1, tee.BufferedCount);
        tee.GetReader(0).Dispose();
        Assert.Equal(0, tee.BufferedCount);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(5, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void InvalidBatchesFailEveryReaderWithoutPublishingPayloads(int count, bool nullEvent)
    {
        using var tee = new StreamTee<Event>(destination => {
            if (!nullEvent) destination.Span[0] = new(1);
            return new(count, false);
        }, 2, 4);
        var failure = Assert.Throws<InvalidOperationException>(() => tee.GetReader(0).Read());
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => tee.GetReader(1).Read()));
        Assert.Equal(0, tee.BufferedCount); Assert.Equal(0, tee.HighWaterMark);
    }

    [Fact]
    public void BatchSourceIoDoesNotHoldTheRingLock()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int calls = 0;
        using var tee = new StreamTee<Event>(destination => {
            if (++calls == 1)
            {
                destination.Span[0] = new(1); destination.Span[1] = new(2);
                return new(2, false);
            }
            destination.Span[0] = new(3); entered.Set();
            if (!release.Wait(Deadline)) throw new TimeoutException("Batch source was not released");
            return new(1, true);
        }, 2, 3);
        Event first = tee.GetReader(0).Read()!, second = tee.GetReader(0).Read()!;
        Assert.Same(first, tee.GetReader(1).Read());
        var errors = new ConcurrentQueue<Exception>();
        Thread producer = Start(() => Assert.Equal(3, tee.GetReader(0).Read()!.Number), errors);
        try
        {
            Assert.True(entered.Wait(Deadline));
            Assert.Same(second, tee.GetReader(1).Read());
            release.Set(); Join(producer); Assert.Empty(errors);
            Assert.Equal(3, tee.GetReader(1).Read()!.Number);
            Assert.Null(tee.GetReader(0).Read()); Assert.Null(tee.GetReader(1).Read());
            Assert.InRange(tee.HighWaterMark, 1, 3);
        }
        finally { release.Set(); tee.Dispose(); Join(producer); }
    }

    [Fact]
    public void AStagedBatchIsDiscardedWhenItsLastReaderDetaches()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var tee = new StreamTee<Event>(destination => {
            destination.Span[0] = new(1); entered.Set();
            if (!release.Wait(Deadline)) throw new TimeoutException("Batch source was not released");
            return new(1, false);
        }, 1, 4);
        var errors = new ConcurrentQueue<Exception>();
        Thread producer = Start(() => tee.GetReader(0).Read(), errors);
        try
        {
            Assert.True(entered.Wait(Deadline)); tee.GetReader(0).Dispose(); release.Set(); Join(producer);
            Assert.IsType<ObjectDisposedException>(Assert.Single(errors));
            Assert.Equal(0, tee.BufferedCount); Assert.Equal(0, tee.HighWaterMark);
        }
        finally { release.Set(); tee.Dispose(); Join(producer); }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(7, false)]
    [InlineData(64, false)]
    [InlineData(1, true)]
    [InlineData(7, true)]
    [InlineData(64, true)]
    public void ConcurrentAliasesOfOneReaderClaimEveryEventExactlyOnce(int capacity, bool skip)
    {
        const int count = 20000;
        int produced = 0;
        using var cancellation = new CancellationTokenSource(Deadline);
        using var tee = new StreamTee<Event>(destination => {
            int n = Math.Min(destination.Length, count - produced);
            for (int i = 0; i < n; i++) destination.Span[i] = new(++produced);
            return new(n, produced == count);
        }, 1, capacity, cancellation.Token);
        using var start = new ManualResetEventSlim();
        var errors = new ConcurrentQueue<Exception>();
        var received = new ConcurrentQueue<int>();
        var threads = new Thread[4];
        for (int i = 0; i < threads.Length; i++)
        {
            bool skipping = skip && i % 2 != 0;
            threads[i] = Start(() => {
                Assert.True(start.Wait(Deadline));
                while (true)
                {
                    Event? item = skipping ? tee.GetReader(0).ReadUntil(value => {
                        received.Enqueue(value.Number);
                        return value.Number % 3 == 0;
                    }) : tee.GetReader(0).Read();
                    if (item == null) break;
                    if (!skipping) received.Enqueue(item.Number);
                }
            }, errors);
        }
        try
        {
            start.Set();
            foreach (Thread thread in threads) Join(thread);
            Assert.Empty(errors);
            int[] numbers = received.ToArray();
            Assert.Equal(count, numbers.Length);
            Array.Sort(numbers);
            for (int i = 0; i < numbers.Length; i++) Assert.Equal(i + 1, numbers[i]);
            Assert.Equal(count, produced);
            Assert.Equal(0, tee.BufferedCount);
            Assert.InRange(tee.HighWaterMark, 1, capacity);
        }
        finally { cancellation.Cancel(); tee.Dispose(); foreach (Thread thread in threads) Join(thread); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void AliasesReleaseAPartialPrefixBeforeAnyFurtherReadIsRequested(int aliases)
    {
        const int readsPerCheckpoint = 13, checkpoints = 128, capacity = 64;
        int produced = 0;
        // Each checkpoint has a liveness guard; total elapsed time is not a throughput assertion.
        using var cancellation = new CancellationTokenSource();
        using var tee = new StreamTee<Event>(destination => {
            for (int i = 0; i < destination.Length; i++) destination.Span[i] = new(++produced);
            return new(destination.Length, false);
        }, 1, capacity, cancellation.Token);
        using var barrier = new Barrier(aliases + 1);
        var errors = new ConcurrentQueue<Exception>();
        var workers = new Thread[aliases];
        int started = 0;
        try
        {
            for (int i = 0; i < aliases; i++)
            {
                workers[i] = Start(() => {
                    try
                    {
                        for (int point = 0; point < checkpoints; point++)
                        {
                            for (int read = 0; read < readsPerCheckpoint; read++)
                                Assert.NotNull(tee.GetReader(0).Read());
                            Assert.True(barrier.SignalAndWait(Deadline, cancellation.Token), "A tee prefix checkpoint did not make progress");
                            Assert.True(barrier.SignalAndWait(Deadline, cancellation.Token), "A tee prefix checkpoint did not make progress");
                        }
                    }
                    catch { cancellation.Cancel(); throw; }
                }, errors);
                started++;
            }
            for (int point = 0; point < checkpoints; point++)
            {
                Assert.True(barrier.SignalAndWait(Deadline, cancellation.Token), "A tee prefix checkpoint did not make progress");
                int consumed = (point + 1) * aliases * readsPerCheckpoint;
                // Every read has returned and no alias can request another event.
                // Consumed slots must already be released, including a partial batch.
                Assert.Equal(Volatile.Read(ref produced) - consumed, tee.BufferedCount);
                Assert.True(barrier.SignalAndWait(Deadline, cancellation.Token), "A tee prefix checkpoint did not make progress");
            }
            foreach (Thread worker in workers) Join(worker);
            Assert.Empty(errors);
            Assert.InRange(tee.HighWaterMark, 1, capacity);
        }
        finally
        {
            cancellation.Cancel(); tee.Dispose();
            for (int i = 0; i < started; i++) Join(workers[i]);
        }
    }

    [Fact]
    public void DetachingDuringConsumptionDoesNotReleaseAnotherReadersEvents()
    {
        const int count = 10000;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            int produced = 0;
            using var cancellation = new CancellationTokenSource(Deadline);
            using var entered = new ManualResetEventSlim();
            using var tee = new StreamTee<Event>(() => ++produced <= count ? new(produced) : null, 2, 7, cancellation.Token);
            var errors = new ConcurrentQueue<Exception>();
            Thread departing = Start(() => {
                try
                {
                    while (tee.GetReader(0).Read() is { }) entered.Set();
                }
                catch (ObjectDisposedException) { }
            }, errors);
            Thread remaining = Start(() => {
                for (int n = 1; n <= count; n++) Assert.Equal(n, tee.GetReader(1).Read()!.Number);
                Assert.Null(tee.GetReader(1).Read());
            }, errors);
            try
            {
                Assert.True(entered.Wait(Deadline));
                tee.GetReader(0).Dispose();
                Join(departing); Join(remaining);
                Assert.Empty(errors);
                Assert.Equal(0, tee.BufferedCount);
                Assert.InRange(tee.HighWaterMark, 1, 7);
                Assert.Throws<ObjectDisposedException>(() => tee.GetReader(0).Read());
            }
            finally { cancellation.Cancel(); tee.Dispose(); Join(departing); Join(remaining); }
        }
    }

    [Fact]
    public void DisposingDuringBufferedConsumptionStopsAllAliasesAndReleasesTheRing()
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int produced = 0, stopped = 0;
            using var cancellation = new CancellationTokenSource(Deadline);
            using var entered = new ManualResetEventSlim();
            using var tee = new StreamTee<Event>(destination => {
                for (int i = 0; i < destination.Length; i++) destination.Span[i] = new(++produced);
                return new(destination.Length, false);
            }, 1, 64, cancellation.Token);
            var errors = new ConcurrentQueue<Exception>();
            var threads = new Thread[4];
            for (int i = 0; i < threads.Length; i++) threads[i] = Start(() => {
                try
                {
                    while (tee.GetReader(0).Read() is { }) entered.Set();
                }
                catch (ObjectDisposedException) { Interlocked.Increment(ref stopped); }
            }, errors);
            try
            {
                Assert.True(entered.Wait(Deadline));
                tee.Dispose();
                foreach (Thread thread in threads) Join(thread);
                Assert.Empty(errors);
                Assert.Equal(threads.Length, stopped);
                Assert.Equal(0, tee.BufferedCount);
                Assert.InRange(tee.HighWaterMark, 1, 64);
            }
            finally { cancellation.Cancel(); tee.Dispose(); foreach (Thread thread in threads) Join(thread); }
        }
    }

}
