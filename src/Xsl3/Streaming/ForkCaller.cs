// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Xsl3.Streaming;

// Source reads and principal-output calls run on the invoking thread. Each worker can
// have only one synchronous request outstanding, so this queue is bounded by the prongs.
internal sealed class ForkCaller : IDisposable
{
    private sealed class Work(Action action, CancellationToken cancellation)
    {
        internal readonly Action Action = action;
        internal readonly CancellationToken Cancellation = cancellation;
        internal bool Running, Done, Cancelled;
        internal ExceptionDispatchInfo? Fault;
    }

    private readonly object _gate = new();
    private readonly Queue<Work> _pending = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly CancellationToken _cancellation;
    private readonly CancellationTokenRegistration _registration;
    private int _workers;
    private long _wakeVersion;
    private bool _started, _closed;

    internal bool IsCurrentThread => Environment.CurrentManagedThreadId == _thread;

    internal ForkCaller(CancellationToken cancellation)
    {
        _cancellation = cancellation;
        _registration = cancellation.UnsafeRegister(static state => ((ForkCaller)state!).Wake(), this);
    }

    internal void Start(int workers)
    {
        lock (_gate)
        {
            if (_started || _closed) throw new InvalidOperationException("A fork caller can only run once");
            _started = true;
            _workers = workers;
        }
    }

    internal T Invoke<T>(Func<T> action, CancellationToken cancellation)
    {
        T result = default!;
        Invoke(() => { result = action(); }, cancellation);
        return result;
    }

    internal void Invoke(Action action, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (Environment.CurrentManagedThreadId == _thread)
        {
            lock (_gate) ObjectDisposedException.ThrowIf(_closed, this);
            action();
            return;
        }
        var work = new Work(action, cancellation);
        using var registration = cancellation.UnsafeRegister(static state => ((ForkCaller)state!).Wake(), this);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _pending.Enqueue(work);
            Monitor.PulseAll(_gate);
            while (!work.Done && (!_closed || work.Running))
            {
                if (cancellation.IsCancellationRequested && !work.Running)
                {
                    work.Cancelled = true;
                    cancellation.ThrowIfCancellationRequested();
                }
                // Running callbacks finish before their worker can reuse an argument
                // buffer. Cancellation cannot detach a callback that is already active.
                Monitor.Wait(_gate);
            }
            work.Fault?.Throw();
            ObjectDisposedException.ThrowIf(_closed && !work.Done, this);
            cancellation.ThrowIfCancellationRequested();
        }
    }

    // Source production runs outside this gate, as do user callbacks. A source can
    // therefore notify this pump while holding its own ring lock without a lock cycle.
    internal void NotifyProduction() => Wake();

    internal void Pump(Func<bool>? produce = null)
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("A fork must pump on its invoking thread");
        while (true)
        {
            Work? work = null;
            long version;
            lock (_gate)
            {
                _cancellation.ThrowIfCancellationRequested();
                if (_closed || _workers == 0) return;
                version = _wakeVersion;
                if (_pending.Count != 0)
                {
                    work = _pending.Dequeue();
                    if (work.Cancelled || work.Cancellation.IsCancellationRequested)
                    {
                        work.Done = true;
                        Monitor.PulseAll(_gate);
                        continue;
                    }
                    work.Running = true;
                }
            }
            if (work == null)
            {
                if (produce?.Invoke() == true) continue;
                lock (_gate)
                {
                    // Production requests can arrive between checking the source and
                    // entering Wait. A pulse alone would lose that notification.
                    if (version == _wakeVersion && _pending.Count == 0 && _workers != 0 && !_closed)
                    {
                        _cancellation.ThrowIfCancellationRequested();
                        Monitor.Wait(_gate);
                    }
                }
                continue;
            }
            try { work.Action(); }
            catch (Exception error) { work.Fault = ExceptionDispatchInfo.Capture(error); }
            finally
            {
                lock (_gate) { work.Done = true; work.Running = false; Monitor.PulseAll(_gate); }
            }
        }
    }

    internal void WorkerDone() { lock (_gate) { _workers--; Monitor.PulseAll(_gate); } }
    private void Wake() { lock (_gate) { _wakeVersion++; Monitor.PulseAll(_gate); } }

    public void Dispose()
    {
        lock (_gate) { _closed = true; _pending.Clear(); Monitor.PulseAll(_gate); }
        _registration.Dispose();
    }
}
