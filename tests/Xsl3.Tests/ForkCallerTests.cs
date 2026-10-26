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
