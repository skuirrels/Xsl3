// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.Threading;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class ForkWaitHintTests
{
    [Fact]
    public void AHintCanReceiveASourceNotificationWithoutHoldingTheQueueGate()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var caller = new ForkCaller(cancellation.Token);
        using var enteredHint = new ManualResetEventSlim();
        using var notified = new ManualResetEventSlim();
        Exception? failure = null;
        bool queried = false;
        caller.Start(1);
        var worker = new Thread(() => {
            try
            {
                enteredHint.Wait(cancellation.Token);
                caller.NotifyProduction();
                notified.Set();
            }
            catch (Exception error) { failure = error; }
            finally { caller.WorkerDone(); }
        }) { IsBackground = true };
        worker.Start();
        try
        {
            caller.Pump(() => false, () => {
                queried = true;
                enteredHint.Set();
                notified.Wait(cancellation.Token);
                return false;
            });
            Assert.True(queried);
        }
        finally { cancellation.Cancel(); Assert.True(worker.Join(TimeSpan.FromSeconds(10))); }
        Assert.Null(failure);
    }

    [Fact]
    public void AFalseHintDoesNotLoseACallbackArrivingBeforeParking()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var caller = new ForkCaller(cancellation.Token);
        using var enteredHint = new ManualResetEventSlim();
        using var notified = new ManualResetEventSlim();
        Exception? failure = null;
        int thread = Environment.CurrentManagedThreadId;
        bool callbackRan = false;
        caller.Start(1);
        var worker = new Thread(() => {
            try
            {
                enteredHint.Wait(cancellation.Token);
                caller.NotifyProduction();
                notified.Set();
                caller.Invoke(() => {
                    Assert.Equal(thread, Environment.CurrentManagedThreadId);
                    callbackRan = true;
                }, cancellation.Token);
            }
            catch (Exception error) { failure = error; }
            finally { caller.WorkerDone(); }
        }) { IsBackground = true };
        worker.Start();
        try
        {
            caller.Pump(() => false, () => {
                enteredHint.Set();
                notified.Wait(cancellation.Token);
                return false;
            });
            Assert.True(callbackRan);
        }
        finally { cancellation.Cancel(); Assert.True(worker.Join(TimeSpan.FromSeconds(10))); }
        Assert.Null(failure);
    }
}
