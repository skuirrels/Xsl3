// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Xsl3.Streaming;

/// <summary>
/// Runs isolated prong bodies over a shared event source. A failure cancels following prongs;
/// preceding prongs finish so that error selection follows declaration order rather than scheduling.
/// Bodies own their output sinks; the caller arranges concatenation and runtime-state isolation.
/// </summary>
internal static class ForkProngs
{
    internal static void Run<T>(Func<T?> readSource, IReadOnlyList<Action<StreamTee<T>.Reader, CancellationToken>> bodies,
        int capacity, CancellationToken cancellation = default, ForkCaller? caller = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(readSource);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        cancellation.ThrowIfCancellationRequested();
        if (bodies.Count == 0) return;
        using var ownedCaller = caller == null ? new ForkCaller(cancellation) : null;
        caller ??= ownedCaller!;
        caller.Start(bodies.Count);
        StreamTee<T>? sourceTee = null;
        using var tee = sourceTee = new StreamTee<T>(destination => caller.Invoke(() => {
            int count = 0;
            try
            {
                while (count < destination.Length)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (sourceTee!.ActiveReaders == 0) return new StreamTee<T>.Batch(count, true);
                    T? item = readSource();
                    if (item == null) return new StreamTee<T>.Batch(count, true);
                    destination.Span[count++] = item;
                }
                return new StreamTee<T>.Batch(count, false);
            }
            catch (Exception error)
            {
                // Every prong receives the accepted prefix before seeing the same source
                // failure. An early-ending prong need not observe unread future errors.
                return new StreamTee<T>.Batch(count, true, ExceptionDispatchInfo.Capture(error));
            }
        }, cancellation), bodies.Count, capacity, cancellation);
        var workers = new Thread[bodies.Count];
        var cancellations = new CancellationTokenSource[bodies.Count];
        var faults = new ExceptionDispatchInfo?[bodies.Count];
        int started = 0;
        bool joined = false;
        try
        {
            // Every cancellation source must exist before the first worker can fail.
            for (int i = 0; i < cancellations.Length; i++)
                cancellations[i] = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            for (int i = 0; i < workers.Length; i++)
            {
                int index = i;
                workers[i] = new Thread(() =>
                {
                    using StreamTee<T>.Reader reader = tee.GetReader(index);
                    try
                    {
                        CancellationToken token = cancellations[index].Token;
                        token.ThrowIfCancellationRequested();
                        bodies[index](reader, token);
                    }
                    catch (Exception error)
                    {
                        faults[index] = ExceptionDispatchInfo.Capture(error);
                        CancelFollowing(index + 1);
                    }
                    finally { reader.Dispose(); caller.WorkerDone(); }
                }) { IsBackground = true };
                workers[i].Start();
                started++;
            }
            caller.Pump();
            for (int i = 0; i < started; i++) workers[i].Join();
            joined = true;
            // Cancellation by an earlier failed prong only creates faults at greater indices.
            // An external cancellation is independent of prong error selection.
            cancellation.ThrowIfCancellationRequested();
            foreach (ExceptionDispatchInfo? fault in faults) fault?.Throw();
        }
        finally
        {
            // Thread creation or startup can fail after some workers have begun. Detaching every
            // input releases backpressure before waiting for those workers to leave their bodies.
            if (!joined)
            {
                caller.Dispose();
                CancelFollowing(0);
                for (int i = 0; i < started; i++) workers[i].Join();
            }
            foreach (CancellationTokenSource? prongCancellation in cancellations) prongCancellation?.Dispose();
        }

        void CancelFollowing(int first)
        {
            for (int i = first; i < cancellations.Length; i++)
            {
                // Callback failures must not replace the prong's original failure or leave its
                // following inputs attached. Cancellation callbacks run outside the tee lock.
                try { cancellations[i]?.Cancel(); }
                catch (Exception) { }
                finally { tee.GetReader(i).Dispose(); }
            }
        }
    }
}
