// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class ForkCallerTests
{
    private sealed record Item(int Number);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public void ProductionRequestsBetweenTheSourceCheckAndWaitAreNotLost()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        using var caller = new ForkCaller(cancellation.Token);
        using var checkedSource = new ManualResetEventSlim();
        using var notified = new ManualResetEventSlim();
        using var delivered = new ManualResetEventSlim();
        Exception? failure = null;
        bool checkedOnce = false;
        int requested = 0;
        caller.Start(1);
        var worker = new Thread(() => {
            try
            {
                checkedSource.Wait(cancellation.Token);
                Interlocked.Exchange(ref requested, 1);
                caller.NotifyProduction();
                notified.Set();
                delivered.Wait(cancellation.Token);
            }
            catch (Exception error) { failure = error; }
            finally { caller.WorkerDone(); }
        }) { IsBackground = true };
        worker.Start();
        try
        {
            caller.Pump(() => {
                if (!checkedOnce)
                {
                    checkedOnce = true;
                    checkedSource.Set();
                    notified.Wait(cancellation.Token);
                    // The source check completed before demand arrived. The pump must
                    // recheck instead of waiting for a notification already delivered.
                    return false;
                }
                if (Interlocked.Exchange(ref requested, 0) == 0) return false;
                delivered.Set();
                return true;
            });
            Assert.True(delivered.IsSet);
        }
        finally { cancellation.Cancel(); Assert.True(worker.Join(Deadline)); }
        Assert.Null(failure);
    }

    [Fact]
    public void AnInvokingThreadCallbackCanReadItsOwnForkInput()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        using var caller = new ForkCaller(cancellation.Token);
        int thread = Environment.CurrentManagedThreadId, reads = 0;
        var values = new List<int>();
        ForkProngs.Run(() => {
            Assert.Equal(thread, Environment.CurrentManagedThreadId);
            return ++reads <= 100 ? new Item(reads) : null;
        }, new Action<StreamTee<Item>.Reader, CancellationToken>[] {
            (reader, token) => {
                while (caller.Invoke(reader.Read, token) is { } item) values.Add(item.Number);
            },
        }, 1, cancellation.Token, caller);
        Assert.Equal(Enumerable.Range(1, 100), values);
    }

    [Fact]
    public void OutputCallbacksCanRunWhileTheInputRingIsFull()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        using var caller = new ForkCaller(cancellation.Token);
        using var firstRead = new ManualResetEventSlim();
        int reads = 0, count = 0;
        bool callbackRan = false;
        ForkProngs.Run(() => ++reads <= 100 ? new Item(reads) : null,
            new Action<StreamTee<Item>.Reader, CancellationToken>[] {
                (reader, _) => {
                    Assert.Equal(1, reader.Read()!.Number);
                    count++;
                    firstRead.Set();
                    while (reader.Read() != null) count++;
                },
                (reader, token) => {
                    firstRead.Wait(token);
                    caller.Invoke(() => {
                        Assert.Equal(1, reads);
                        callbackRan = true;
                    }, token);
                    while (reader.Read() != null) { }
                },
            }, 1, cancellation.Token, caller);
        Assert.True(callbackRan);
        Assert.Equal(100, count);
    }

    [Fact]
    public void InvokingThreadCallbacksCanRefillPartiallyDrainedInput()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        using var caller = new ForkCaller(cancellation.Token);
        using var fastRead = new ManualResetEventSlim();
        using var spaceReleased = new ManualResetEventSlim();
        using var callbackRead = new ManualResetEventSlim();
        int owner = Environment.CurrentManagedThreadId, reads = 0;
        var counts = new int[2];
        ForkProngs.Run(() => {
            Assert.Equal(owner, Environment.CurrentManagedThreadId);
            return ++reads <= 100 ? new Item(reads) : null;
        }, new Action<StreamTee<Item>.Reader, CancellationToken>[] {
            (reader, token) => {
                for (int i = 1; i <= 3; i++) Assert.Equal(++counts[0], reader.Read()!.Number);
                fastRead.Set();
                spaceReleased.Wait(token);
                Assert.Equal(++counts[0], caller.Invoke(reader.Read, token)!.Number);
                callbackRead.Set();
                while (reader.Read() is { } item) Assert.Equal(++counts[0], item.Number);
            },
            (reader, token) => {
                fastRead.Wait(token);
                Assert.Equal(++counts[1], reader.Read()!.Number);
                spaceReleased.Set();
                // The owner must finish the callback's read while this reader still
                // holds the rest of the published batch, before it can drain the ring.
                callbackRead.Wait(token);
                while (reader.Read() is { } item) Assert.Equal(++counts[1], item.Number);
            },
        }, 3, cancellation.Token, caller);
        Assert.All(counts, count => Assert.Equal(100, count));
        Assert.Equal(101, reads);
    }

    [Fact]
    public void FreeInputSlotsAllowAProngToAdvanceBeforeItsPeerDrainsTheBatch()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        using var fourthRead = new ManualResetEventSlim();
        int reads = 0;
        var counts = new int[2];
        ForkProngs.Run(() => ++reads <= 100 ? new Item(reads) : null,
            new Action<StreamTee<Item>.Reader, CancellationToken>[] {
                (reader, _) => {
                    while (reader.Read() is { } item)
                    {
                        Assert.Equal(++counts[0], item.Number);
                        if (item.Number == 4) fourthRead.Set();
                    }
                },
                (reader, token) => {
                    Assert.Equal(++counts[1], reader.Read()!.Number);
                    // One free slot permits the first prong's fourth event without
                    // exceeding capacity, even while this prong retains two events.
                    fourthRead.Wait(token);
                    while (reader.Read() is { } item) Assert.Equal(++counts[1], item.Number);
                },
            }, 3, cancellation.Token);
        Assert.All(counts, count => Assert.Equal(100, count));
        Assert.Equal(101, reads);
    }

    [Fact]
    public void InputAndOutputRequestsRunOnTheInvokingThread()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        using var caller = new ForkCaller(cancellation.Token);
        int thread = Environment.CurrentManagedThreadId, reads = 0;
        var values = new List<int>();
        ForkProngs.Run(() => {
            Assert.Equal(thread, Environment.CurrentManagedThreadId);
            return ++reads <= 100 ? new Item(reads) : null;
        }, new Action<StreamTee<Item>.Reader, CancellationToken>[] {
            (reader, token) => {
                while (reader.Read() is { } item)
                    caller.Invoke(() => {
                        Assert.Equal(thread, Environment.CurrentManagedThreadId);
                        caller.Invoke(() => values.Add(item.Number), token);
                    }, token);
            },
            (reader, _) => { while (reader.Read() != null) { } },
        }, 1, cancellation.Token, caller);
        Assert.Equal(Enumerable.Range(1, 100), values);
    }

    [Fact]
    public void CallbackFailureCancelsAndJoinsFollowingProngs()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        using var caller = new ForkCaller(cancellation.Token);
        using var started = new CountdownEvent(2);
        bool left = false;
        var failure = new IOException("writer failure");
        Exception actual = Assert.Throws<IOException>(() => ForkProngs.Run<object>(() => null,
            new Action<StreamTee<object>.Reader, CancellationToken>[] {
                (_, token) => { started.Signal(); started.Wait(token); caller.Invoke(() => throw failure, token); },
                (_, token) => {
                    try { started.Signal(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
                    finally { left = true; }
                },
            }, 1, cancellation.Token, caller));
        Assert.Same(failure, actual);
        Assert.True(left);
    }

    [Fact]
    public void CancellationDoesNotDetachAnActiveCallbackFromItsArgumentBuffer()
    {
        using var deadline = new CancellationTokenSource(Deadline);
        using var cancellation = new CancellationTokenSource();
        using var caller = new ForkCaller(deadline.Token);
        int returned = 0;
        Assert.Throws<OperationCanceledException>(() => ForkProngs.Run<object>(() => null,
            new Action<StreamTee<object>.Reader, CancellationToken>[] {
                (_, _) => {
                    try
                    {
                        caller.Invoke(() => {
                            cancellation.Cancel();
                            Assert.Equal(0, Volatile.Read(ref returned));
                        }, cancellation.Token);
                    }
                    finally { Interlocked.Exchange(ref returned, 1); }
                },
            }, 1, deadline.Token, caller));
        Assert.Equal(1, returned);
    }

    [Fact]
    public void DisposedCallersRejectInlineCallbacks()
    {
        var caller = new ForkCaller(default);
        caller.Dispose();
        Assert.Throws<ObjectDisposedException>(() => caller.Invoke(() => { }, default));
    }

    [Fact]
    public void DisposalDoesNotReleaseAnActiveCallbacksArgumentBuffer()
    {
        using var deadline = new CancellationTokenSource(Deadline);
        using var caller = new ForkCaller(deadline.Token);
        int returned = 0;
        ForkProngs.Run<object>(() => null,
            new Action<StreamTee<object>.Reader, CancellationToken>[] {
                (_, token) => {
                    try
                    {
                        caller.Invoke(() => {
                            caller.Dispose();
                            Assert.Equal(0, Volatile.Read(ref returned));
                        }, token);
                    }
                    finally { Interlocked.Exchange(ref returned, 1); }
                },
            }, 1, deadline.Token, caller);
        Assert.Equal(1, returned);
    }
}
