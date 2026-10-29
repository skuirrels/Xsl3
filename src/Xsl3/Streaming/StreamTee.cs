// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Xsl3.Streaming;

/// <summary>
/// Shares immutable source events among independently advancing prongs. The ring owns at most
/// the configured capacity of events; each reader may additionally retain its current event.
/// The requesting prongs take turns reading the source, so no producer thread is required.
/// </summary>
internal sealed class StreamTee<T> : IDisposable where T : class
{
    private readonly object _gate = new();
    private readonly Func<Memory<T?>, Batch> _readSource;
    private readonly T?[] _staging;
    private readonly CancellationToken _cancellation;
    private readonly CancellationTokenRegistration _registration;
    private readonly T?[] _events;
    private readonly int[] _remaining;
    private readonly long[] _positions;
    private readonly Reader[] _readers;
    private int _active, _highWaterMark, _waiting, _producerThread;
    private long _first, _next;
    private bool _producing, _complete, _disposed;
    private ExceptionDispatchInfo? _fault;

    internal readonly record struct Batch(int Count, bool Complete, ExceptionDispatchInfo? Fault = null);

    internal StreamTee(Func<T?> readSource, int readers, int capacity, CancellationToken cancellation = default)
        : this(OneAtATime(readSource), readers, capacity, cancellation) { }

    private static Func<Memory<T?>, Batch> OneAtATime(Func<T?> readSource)
    {
        ArgumentNullException.ThrowIfNull(readSource);
        return destination => {
            T? item = readSource();
            destination.Span[0] = item;
            return new(item == null ? 0 : 1, item == null);
        };
    }

    // A batch can fill only space reserved from this same ring. Staged payloads plus
    // published payloads never exceed capacity; staging adds only a reference array.
    internal StreamTee(Func<Memory<T?>, Batch> readSource, int readers, int capacity, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(readSource);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(readers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _readSource = readSource;
        _cancellation = cancellation;
        _events = new T?[capacity];
        _staging = new T?[capacity];
        _remaining = new int[capacity];
        _positions = new long[readers];
        _readers = new Reader[readers];
        _active = readers;
        for (int i = 0; i < readers; i++) _readers[i] = new(this, i);
        _registration = cancellation.UnsafeRegister(static state => ((StreamTee<T>)state!).Wake(), this);
    }

    internal Reader GetReader(int index) => _readers[index];
    internal int BufferedCount { get { lock (_gate) return (int)(_next - _first); } }
    internal int HighWaterMark { get { lock (_gate) return _highWaterMark; } }
    internal int WaitingReaders { get { lock (_gate) return _waiting; } }
    internal int ActiveReaders { get { lock (_gate) return _active; } }

    internal sealed class Reader(StreamTee<T> owner, int index) : IDisposable
    {
        internal T? Read() => owner.Read(index);
        // The predicate inspects immutable headers while the ring is locked. It must
        // not perform I/O, request another event or call back into a prong.
        internal T? ReadUntil(Predicate<T> stop) => owner.Read(index, stop);
        public void Dispose() => owner.Detach(index);
    }

    private void Wake()
    {
        lock (_gate) Monitor.PulseAll(_gate);
    }

    private T? Read(int reader, Predicate<T>? stop = null)
    {
        while (true)
        {
            int available;
            lock (_gate)
            {
                _cancellation.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed || _positions[reader] < 0, this);
                long position = _positions[reader];
                if (position < _next)
                {
                    try
                    {
                        do
                        {
                            int slot = (int)(position % _events.Length);
                            T result = _events[slot]!;
                            _positions[reader] = ++position;
                            _remaining[slot]--;
                            if (stop == null || stop(result)) return result;
                            _cancellation.ThrowIfCancellationRequested();
                        } while (position < _next);
                    }
                    finally
                    {
                        // Skipped events release backpressure before requesting more
                        // source data, including when a predicate or cancellation fails.
                        ReleaseHead();
                        Monitor.PulseAll(_gate);
                    }
                }
                _fault?.Throw();
                if (_complete) return null;
                if (_producing || _next - _first == _events.Length)
                {
                    if (_producing && _producerThread == Environment.CurrentManagedThreadId)
                        throw new InvalidOperationException("A stream tee source cannot recursively request its own input");
                    _waiting++;
                    try { Monitor.Wait(_gate); }
                    finally { _waiting--; }
                    continue;
                }
                available = _events.Length - (int)(_next - _first);
                _producing = true;
                _producerThread = Environment.CurrentManagedThreadId;
            }

            // Source I/O cannot hold the ring lock: slower readers must be able to consume
            // buffered events or detach while another prong is waiting for source data.
            Batch batch = default;
            ExceptionDispatchInfo? fault = null;
            try
            {
                batch = _readSource(_staging.AsMemory(0, available));
                if (batch.Count < 0 || batch.Count > available || batch.Count == 0 && !batch.Complete && batch.Fault == null)
                    throw new InvalidOperationException("A stream tee source batch must fit its reserved space and make progress");
                for (int i = 0; i < batch.Count; i++)
                    if (_staging[i] == null) throw new InvalidOperationException("A stream tee source batch cannot contain null events");
            }
            catch (Exception error)
            { fault = ExceptionDispatchInfo.Capture(error); }
            finally
            {
                lock (_gate)
                {
                    _producing = false;
                    _producerThread = 0;
                    if (!_disposed)
                    {
                        if (fault != null) { _fault = fault; _complete = true; }
                        else if (_active == 0) _complete = true;
                        else
                        {
                            for (int i = 0; i < batch.Count; i++)
                            {
                                int slot = (int)(_next % _events.Length);
                                _events[slot] = _staging[i];
                                _remaining[slot] = _active;
                                _next++;
                            }
                            _highWaterMark = Math.Max(_highWaterMark, (int)(_next - _first));
                            _fault = batch.Fault;
                            _complete = batch.Complete || batch.Fault != null;
                        }
                    }
                    Array.Clear(_staging);
                    Monitor.PulseAll(_gate);
                }
            }
        }
    }

    private void ReleaseHead()
    {
        while (_first < _next)
        {
            int slot = (int)(_first % _events.Length);
            if (_remaining[slot] != 0) break;
            _events[slot] = null;
            _first++;
        }
    }

    private void Detach(int reader)
    {
        lock (_gate)
        {
            long position = _positions[reader];
            if (position < 0) return;
            _positions[reader] = -1;
            _active--;
            for (; position < _next; position++) _remaining[(int)(position % _events.Length)]--;
            ReleaseHead();
            Monitor.PulseAll(_gate);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Array.Fill(_positions, -1);
            Array.Clear(_events);
            Array.Clear(_remaining);
            _first = _next;
            _active = 0;
            Monitor.PulseAll(_gate);
        }
        // Disposing a registration can wait for its callback, which also acquires the ring lock.
        _registration.Dispose();
    }
}
