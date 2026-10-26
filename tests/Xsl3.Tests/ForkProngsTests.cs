// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class ForkProngsTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private sealed record Event(int Number);

    [Fact]
    public void EveryProngRunsOnItsOwnThreadAndSeesTheSameInput()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        int caller = Environment.CurrentManagedThreadId, calls = 0;
        var threads = new int[3];
        var counts = new int[3];
        var bodies = new Action<StreamTee<Event>.Reader, CancellationToken>[3];
        for (int i = 0; i < bodies.Length; i++)
        {
            int index = i;
            bodies[i] = (reader, token) =>
            {
                threads[index] = Environment.CurrentManagedThreadId;
                Assert.NotEqual(caller, threads[index]);
                while (reader.Read() is { } item) Assert.Equal(++counts[index], item.Number);
            };
        }
        ForkProngs.Run(() =>
        {
            Assert.Equal(caller, Environment.CurrentManagedThreadId);
            return ++calls <= 1000 ? new Event(calls) : null;
        }, bodies, 2, cancellation.Token);
        Assert.All(counts, count => Assert.Equal(1000, count));
        Assert.Equal(1001, calls);
        Assert.Equal(3, new HashSet<int>(threads).Count);
    }

    [Fact]
    public void DeclarationOrderSelectsTheErrorEvenWhenALaterProngFailsFirst()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        using var started = new CountdownEvent(3);
        using var laterFailed = new ManualResetEventSlim();
        using var lastLeft = new ManualResetEventSlim();
        var earlier = new IOException("earlier prong");
        var later = new IOException("later prong");
        var bodies = new Action<StreamTee<Event>.Reader, CancellationToken>[] {
            (reader, token) => { started.Signal(); started.Wait(token); laterFailed.Wait(token); throw earlier; },
            (reader, token) => { started.Signal(); started.Wait(token); throw later; },
            (reader, token) => {
                try { started.Signal(); started.Wait(token); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
                catch (OperationCanceledException) { laterFailed.Set(); throw; }
                finally { lastLeft.Set(); }
            },
        };
        Assert.Same(earlier, Assert.Throws<IOException>(() => ForkProngs.Run(() => new Event(1), bodies, 1, cancellation.Token)));
        Assert.True(lastLeft.IsSet);
    }

    [Fact]
    public void ALaterFailureDoesNotCancelAnEarlierProng()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        var failure = new IOException("second prong");
        int calls = 0, count = 0;
        var bodies = new Action<StreamTee<Event>.Reader, CancellationToken>[] {
            (reader, token) => { while (reader.Read() is { } item) { token.ThrowIfCancellationRequested(); Assert.Equal(++count, item.Number); } },
            (reader, token) => throw failure,
        };
        Assert.Same(failure, Assert.Throws<IOException>(() => ForkProngs.Run(() => ++calls <= 1000 ? new Event(calls) : null, bodies, 1, cancellation.Token)));
        Assert.Equal(1000, count);
        Assert.Equal(1001, calls);
    }

    [Fact]
    public void AFailingProngReleasesFollowingReadersBlockedByBackpressure()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        using var followingRead = new ManualResetEventSlim();
        var failure = new IOException("first prong");
        int calls = 0, left = 0;
        var bodies = new Action<StreamTee<Event>.Reader, CancellationToken>[] {
            (reader, token) => { followingRead.Wait(token); throw failure; },
            (reader, token) => {
                try { Assert.NotNull(reader.Read()); followingRead.Set(); while (reader.Read() != null) { } }
                finally { Interlocked.Increment(ref left); }
            },
        };
        Assert.Same(failure, Assert.Throws<IOException>(() => ForkProngs.Run(() => new Event(++calls), bodies, 1, cancellation.Token)));
        Assert.Equal(1, left);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FinishingAProngEarlyDoesNotTruncateTheOtherProngs()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        int calls = 0, count = 0;
        var bodies = new Action<StreamTee<Event>.Reader, CancellationToken>[] {
            (reader, token) => Assert.Equal(1, reader.Read()!.Number),
            (reader, token) => { while (reader.Read() is { } item) Assert.Equal(++count, item.Number); },
        };
        ForkProngs.Run(() => ++calls <= 1000 ? new Event(calls) : null, bodies, 1, cancellation.Token);
        Assert.Equal(1000, count);
        Assert.Equal(1001, calls);
    }

    [Fact]
    public void EmptyForkDoesNotReadItsInput()
    {
        ForkProngs.Run<Event>(() => throw new IOException("unused source"),
            Array.Empty<Action<StreamTee<Event>.Reader, CancellationToken>>(), 1);
    }

    [Fact]
    public void SuccessfulCompletionDoesNotCancelProngTokens()
    {
        CancellationToken observed = default;
        var bodies = new Action<StreamTee<Event>.Reader, CancellationToken>[] { (reader, token) => observed = token };
        ForkProngs.Run<Event>(() => throw new IOException("unused source"), bodies, 1);
        Assert.True(observed.CanBeCanceled);
        Assert.False(observed.IsCancellationRequested);
    }

    [Fact]
    public void ParentCancellationStopsAndJoinsEveryProng()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        using var started = new CountdownEvent(2);
        int left = 0;
        var bodies = new Action<StreamTee<Event>.Reader, CancellationToken>[] {
            (reader, token) => {
                try { Assert.NotNull(reader.Read()); started.Signal(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
                finally { Interlocked.Increment(ref left); }
            },
            (reader, token) => { try { started.Signal(); while (reader.Read() != null) { } } finally { Interlocked.Increment(ref left); } },
        };
        var canceller = new Thread(() => { if (started.Wait(Deadline)) cancellation.Cancel(); }) { IsBackground = true };
        canceller.Start();
        try
        {
            var error = Assert.Throws<OperationCanceledException>(() => ForkProngs.Run(() => new Event(1), bodies, 1, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(2, left);
        }
        finally { cancellation.Cancel(); Assert.True(canceller.Join(Deadline)); }
    }

    [Fact]
    public void CancellationCallbackFailuresDoNotReplaceTheOriginalProngFailure()
    {
        using var registered = new ManualResetEventSlim();
        using var callbackRan = new ManualResetEventSlim();
        var failure = new IOException("first prong");
        var bodies = new Action<StreamTee<Event>.Reader, CancellationToken>[] {
            (reader, token) => { Assert.True(registered.Wait(Deadline)); throw failure; },
            (reader, token) => {
                using var registration = token.Register(() => { callbackRan.Set(); throw new NullReferenceException("callback failure"); });
                registered.Set();
                Assert.True(callbackRan.Wait(Deadline)); token.ThrowIfCancellationRequested();
            },
        };
        Assert.Same(failure, Assert.Throws<IOException>(() => ForkProngs.Run(() => new Event(1), bodies, 1)));
        Assert.True(callbackRan.IsSet);
    }

    [Fact]
    public void RuntimeFaultsAreTransferredToTheCallerWithoutEscapingTheWorkerThread()
    {
        var failure = new NullReferenceException("prong failure");
        var bodies = new Action<StreamTee<Event>.Reader, CancellationToken>[] { (reader, token) => throw failure };
        Assert.Same(failure, Assert.Throws<NullReferenceException>(() => ForkProngs.Run(() => new Event(1), bodies, 1)));
    }

    [Fact]
    public void SourceRuntimeFaultsAreTransferredThroughTheTee()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        var failure = new NullReferenceException("source failure");
        var bodies = new Action<StreamTee<Event>.Reader, CancellationToken>[] { (reader, token) => reader.Read(), (reader, token) => reader.Read() };
        Assert.Same(failure, Assert.Throws<NullReferenceException>(() => ForkProngs.Run<Event>(() => throw failure, bodies, 1, cancellation.Token)));
    }

    [Fact]
    public void NestedForksCanTruncateOneChildAndContinueAllOtherProngs()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        int calls = 0, childCount = 0, outerCount = 0;
        var children = new Action<StreamTee<Event>.Reader, CancellationToken>[] {
            (reader, token) => Assert.Equal(1, reader.Read()!.Number),
            (reader, token) => { while (reader.Read() is { } item) Assert.Equal(++childCount, item.Number); },
        };
        var bodies = new Action<StreamTee<Event>.Reader, CancellationToken>[] {
            (reader, token) => ForkProngs.Run(reader.Read, children, 1, token),
            (reader, token) => { while (reader.Read() is { } item) Assert.Equal(++outerCount, item.Number); },
        };
        ForkProngs.Run(() => ++calls <= 1000 ? new Event(calls) : null, bodies, 2, cancellation.Token);
        Assert.Equal(1000, childCount);
        Assert.Equal(1000, outerCount);
        Assert.Equal(1001, calls);
    }
}
