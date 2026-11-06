// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Xsl3.Streaming;

/// <summary>
/// Shares immutable source events among independently advancing prongs. The ring owns at most
/// the configured capacity of events; each reader may additionally retain its current event.
/// Requesting prongs take turns producing, or signal the invoking thread when caller-owned
/// production is configured. Neither mode requires an additional producer thread.
/// </summary>
internal sealed class StreamTee<T> : IDisposable where T : class
{
    private readonly object _gate = new();
    private readonly Func<Memory<T?>, Batch> _readSource;
    private readonly ForkCaller? _productionCaller;
    private readonly T?[] _staging;
    private readonly CancellationToken _cancellation;
    private readonly CancellationTokenRegistration _registration;
    private readonly T?[] _events;
    private readonly int[] _remaining;
    // Only the private fork-event adapter opts in. Each cursor then has one
    // consuming thread; external disposal can detach it but never advances it.
    private readonly int[]? _exclusiveThreads;
    private readonly int _slotMask;
    private readonly long[] _positions;
    private readonly Reader[] _readers;
    private int _active, _highWaterMark, _waiting, _waitingForSpace, _producerThread;
    private long _first, _next, _publicationVersion;
    private bool _producing, _productionRequested, _complete;
    private volatile bool _disposed;
    private ExceptionDispatchInfo? _fault;

    internal readonly record struct Batch(int Count, bool Complete, ExceptionDispatchInfo? Fault = null, bool Progress = false);

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
    internal StreamTee(Func<Memory<T?>, Batch> readSource, int readers, int capacity, CancellationToken cancellation = default,
        ForkCaller? productionCaller = null, bool exclusiveReaders = false)
    {
        ArgumentNullException.ThrowIfNull(readSource);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(readers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _readSource = readSource;
        _productionCaller = productionCaller;
        _cancellation = cancellation;
        _slotMask = (capacity & (capacity - 1)) == 0 ? capacity - 1 : -1;
        _events = new T?[capacity];
        _staging = new T?[capacity];
        _remaining = exclusiveReaders ? Array.Empty<int>() : new int[capacity];
        _exclusiveThreads = exclusiveReaders ? new int[readers] : null;
        _positions = new long[readers];
        _readers = new Reader[readers];
        _active = readers;
        for (int i = 0; i < readers; i++) _readers[i] = new(this, i);
        _registration = cancellation.UnsafeRegister(static state => ((StreamTee<T>)state!).Wake(), this);
    }

    // Avoid division in the event path when the configured ring size is a power of two.
    // Arbitrary capacities keep the original remainder operation.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Slot(long position) => _slotMask >= 0 ? (int)position & _slotMask : (int)(position % _events.Length);

    internal Reader GetReader(int index) => _readers[index];
    internal int BufferedCount { get { lock (_gate) return (int)(_next - _first); } }
    internal int HighWaterMark { get { lock (_gate) return _highWaterMark; } }
    internal int WaitingReaders { get { lock (_gate) return _waiting; } }
    internal int ActiveReaders => Volatile.Read(ref _active);

    internal sealed class Reader(StreamTee<T> owner, int index) : IDisposable
    {
        internal T? Read() => owner.Read(index);
        // The predicate inspects immutable headers while the ring is locked. It must
        // not perform I/O, request another event or call back into a prong.
        internal T? ReadUntil(Predicate<T> stop) => owner.Read(index, stop);
        // Inspection must be pure and nonthrowing: a failed range claim may retry it.
        internal T? ReadUntilImmutable(Predicate<T> stop) => owner.ReadImmutable(index, stop);
        public void Dispose() => owner.Detach(index);
    }

    private void Wake()
    {
        lock (_gate) Monitor.PulseAll(_gate);
    }

    // Claim a published position before releasing its remaining-reader count. The same
    // atomic claim under the gate prevents aliases or detachment from duplicating a read.
    private bool TryReadBuffered(int reader, out T? item)
    {
        _cancellation.ThrowIfCancellationRequested();
        long position = Volatile.Read(ref _positions[reader]);
        ObjectDisposedException.ThrowIf(_disposed || position < 0, this);
        if (position < Volatile.Read(ref _next))
        {
            int slot = Slot(position);
            T? result = Volatile.Read(ref _events[slot]);
            if (result != null && Interlocked.CompareExchange(ref _positions[reader], position + 1, position) == position)
            {
                if (Interlocked.Decrement(ref _remaining[slot]) == 0)
                    lock (_gate) ReleaseHead();
                item = result;
                return true;
            }
        }
        item = null;
        return false;
    }

    private T? ReadImmutable(int reader, Predicate<T> stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        if (_exclusiveThreads != null || !ForkCaller.HasSpareProcessor(_readers.Length)) return Read(reader, stop);
        while (true)
        {
            if (TryClaimImmutableRun(reader, stop, out T? match))
            {
                if (match != null) return match;
                continue;
            }
            T? item = Read(reader);
            if (item == null || stop(item)) return item;
        }
    }

    private bool TryClaimImmutableRun(int reader, Predicate<T> stop, out T? match)
    {
        match = null;
        _cancellation.ThrowIfCancellationRequested();
        long start = Volatile.Read(ref _positions[reader]);
        ObjectDisposedException.ThrowIf(_disposed || start < 0, this);
        long published = Volatile.Read(ref _next), end = start;
        while (end < published)
        {
            _cancellation.ThrowIfCancellationRequested();
            T? item = Volatile.Read(ref _events[Slot(end)]);
            // An alias may already have claimed/released this epoch. Retry through
            // the ordinary path rather than inspecting a reused or empty slot.
            if (item == null) return false;
            end++;
            if (stop(item)) { match = item; break; }
        }
        if (end == start || Interlocked.CompareExchange(ref _positions[reader], end, start) != start)
        {
            match = null;
            return false;
        }
        // Every claimed epoch still owns this reader's count until the complete
        // range has been released. Neither an alias nor detachment can free it early.
        bool released = false;
        for (long position = start; position < end; position++)
            if (Interlocked.Decrement(ref _remaining[Slot(position)]) == 0) released = true;
        if (released) lock (_gate) ReleaseHead();
        return true;
    }

    private bool Attached(int reader) => _exclusiveThreads == null
        ? Volatile.Read(ref _positions[reader]) >= 0 : Volatile.Read(ref _exclusiveThreads[reader]) >= 0;

    private void CheckExclusiveOwner(int reader)
    {
        _cancellation.ThrowIfCancellationRequested();
        int thread = Environment.CurrentManagedThreadId;
        int owner = Volatile.Read(ref _exclusiveThreads![reader]);
        if (owner == 0) owner = Interlocked.CompareExchange(ref _exclusiveThreads[reader], thread, 0);
        ObjectDisposedException.ThrowIf(_disposed || owner < 0, this);
        if (owner != 0 && owner != thread)
            throw new InvalidOperationException("A private fork-event reader must stay on its consuming thread");
    }

    private bool TryReadExclusiveBuffered(int reader, Predicate<T>? stop, out T? item)
    {
        item = null;
        long position = Volatile.Read(ref _positions[reader]);
        long published = Volatile.Read(ref _next);
        bool advanced = false;
        try
        {
            while (position < published)
            {
                _cancellation.ThrowIfCancellationRequested();
                T? current = Volatile.Read(ref _events[Slot(position)]);
                ObjectDisposedException.ThrowIf(_disposed || !Attached(reader), this);
                // The publisher releases each reused slot after detachment. Acquiring
                // that slot before checking attachment cannot return a new epoch to a
                // detached reader. An attached owner protects every unread position.
                if (current == null) return false;
                Volatile.Write(ref _positions[reader], ++position);
                advanced = true;
                if (stop == null || stop(current)) { item = current; return true; }
                // A predicate can read recursively on the same owning thread.
                position = Volatile.Read(ref _positions[reader]);
            }
            return false;
        }
        finally
        {
            // Release before returning, including partial batches and exceptions.
            // The monitor also orders cursor publication with producer demand and
            // delivers the existing full-ring wakeup without a separate racy hint.
            if (advanced) lock (_gate) ReleaseHead();
        }
    }

    private T? Read(int reader, Predicate<T>? stop = null)
    {
        if (_exclusiveThreads != null) CheckExclusiveOwner(reader);
        while (true)
        {
            if (_exclusiveThreads != null)
            {
                if (TryReadExclusiveBuffered(reader, stop, out T? exclusive)) return exclusive;
            }
            else if (stop == null && TryReadBuffered(reader, out T? buffered)) return buffered;
            int available;
            lock (_gate)
            {
                _cancellation.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed || !Attached(reader), this);
                long position = _positions[reader];
                if (_exclusiveThreads != null)
                {
                    ReleaseHead();
                    if (position < _next) continue;
                }
                else if (position < _next)
                {
                    try
                    {
                        do
                        {
                            int slot = Slot(position);
                            T result = _events[slot]!;
                            if (Interlocked.CompareExchange(ref _positions[reader], position + 1, position) != position)
                            {
                                position = Volatile.Read(ref _positions[reader]);
                                ObjectDisposedException.ThrowIf(position < 0, this);
                                continue;
                            }
                            position++;
                            Interlocked.Decrement(ref _remaining[slot]);
                            if (stop == null || stop(result)) return result;
                            _cancellation.ThrowIfCancellationRequested();
                        } while (position < _next);
                    }
                    finally
                    {
                        // Skipped events release backpressure before requesting more
                        // source data, including when a predicate or cancellation fails.
                        ReleaseHead();
                    }
                }
                _fault?.Throw();
                if (_complete) return null;
                if (_producing && _producerThread == Environment.CurrentManagedThreadId)
                    throw new InvalidOperationException("A stream tee source cannot recursively request its own input");
                if (_productionCaller != null && !_productionCaller.IsCurrentThread)
                {
                    if (!_productionRequested)
                    {
                        _productionRequested = true;
                        NotifyProduction();
                    }
                    WaitForPublication(reader);
                    continue;
                }
                if (_producing || _next - _first == _events.Length)
                {
                    WaitForPublication(reader, waitingForSpace: !_producing);
                    continue;
                }
                available = _events.Length - (int)(_next - _first);
                _producing = true;
                _producerThread = Environment.CurrentManagedThreadId;
            }

            Produce(available);
        }
    }

    private void WaitForPublication(int reader, bool waitingForSpace = false)
    {
        _waiting++;
        if (waitingForSpace) _waitingForSpace++;
        try
        {
            // Only spend a bounded optimistic wait when caller plus prongs fit the
            // effective processor allowance. Oversubscribed forks retain parking.
            if (_productionCaller != null && ForkCaller.HasSpareProcessor(_readers.Length))
            {
                long publication = _publicationVersion;
                Monitor.Exit(_gate);
                try
                {
                    var spin = new ForkCaller.SpinBudget(Environment.TickCount64);
                    while (Volatile.Read(ref _publicationVersion) == publication && !_disposed
                        && !_cancellation.IsCancellationRequested && Attached(reader)
                        && spin.Continue()) { }
                }
                finally { Monitor.Enter(_gate); }
                // A publication, terminal state or reclaimed slot can arrive while
                // the monitor is released. Recheck before parking to retain its pulse.
                if (_publicationVersion != publication || _complete || _fault != null || _disposed || _cancellation.IsCancellationRequested
                    || !Attached(reader) || _positions[reader] < _next
                    || waitingForSpace && !_producing && _next - _first < _events.Length) return;
            }
            Monitor.Wait(_gate);
        }
        finally
        {
            _waiting--;
            if (waitingForSpace) _waitingForSpace--;
        }
    }

    // Called by the invoking thread's pump, without its queue lock. Consumers only
    // signal demand; a refill needs no queued closure or blocked callback request.
    internal bool ProduceRequested()
    {
        if (_productionCaller == null || !_productionCaller.IsCurrentThread)
            throw new InvalidOperationException("A caller-owned tee must produce on its invoking thread");
        int available;
        lock (_gate)
        {
            if (_exclusiveThreads != null) ReleaseHead();
            if (!_productionRequested || _producing || _complete || _disposed || _active == 0
                || _cancellation.IsCancellationRequested || _next - _first == _events.Length) return false;
            available = _events.Length - (int)(_next - _first);
            _producing = true;
            _producerThread = Environment.CurrentManagedThreadId;
        }
        Produce(available);
        return true;
    }

    // Only the pump's own synchronization signal runs here, never I/O or a prong callback.
    private void NotifyProduction()
    {
        if (_productionRequested && !_producing && _next - _first < _events.Length)
            _productionCaller?.NotifyProduction();
    }

    private void Produce(int available)
    {
        // Source I/O cannot hold the ring lock: slower readers must be able to consume
        // buffered events or detach while another prong is waiting for source data.
        Batch batch = default;
        ExceptionDispatchInfo? fault = null;
        try
        {
            batch = _readSource(_staging.AsMemory(0, available));
            if (batch.Count < 0 || batch.Count > available || batch.Count == 0 && !batch.Complete && batch.Fault == null && !batch.Progress)
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
                _productionRequested = false;
                _producerThread = 0;
                if (!_disposed)
                {
                    if (fault != null) { _fault = fault; _complete = true; }
                    else if (_active == 0) _complete = true;
                    else
                    {
                        long next = _next;
                        for (int i = 0; i < batch.Count; i++)
                        {
                            int slot = Slot(next);
                            if (_exclusiveThreads != null)
                                // Acquire readers can also observe detachment preceding a reused epoch.
                                Volatile.Write(ref _events[slot], _staging[i]);
                            else
                            {
                                _events[slot] = _staging[i];
                                _remaining[slot] = _active;
                            }
                            next++;
                        }
                        // Release publication follows initialization of every payload/count.
                        Volatile.Write(ref _next, next);
                        _highWaterMark = Math.Max(_highWaterMark, (int)(_next - _first));
                        _fault = batch.Fault;
                        _complete = batch.Complete || batch.Fault != null;
                    }
                }
                Array.Clear(_staging);
                // Empty filtered scans still publish bounded source progress. A
                // spinning reader must not lose this notification when next is unchanged.
                Volatile.Write(ref _publicationVersion, _publicationVersion + 1);
                Monitor.PulseAll(_gate);
            }
        }
    }

    private void ReleaseHead()
    {
        bool wasFull = _next - _first == _events.Length;
        long first = _first;
        if (_exclusiveThreads != null)
        {
            long released = _next;
            for (int i = 0; i < _positions.Length; i++)
                if (Attached(i)) released = Math.Min(released, Volatile.Read(ref _positions[i]));
            while (_first < released) _events[Slot(_first++)] = null;
        }
        else
        {
            while (_first < _next)
            {
                int slot = Slot(_first);
                if (Volatile.Read(ref _remaining[slot]) != 0) break;
                _events[slot] = null;
                _first++;
            }
        }
        if (first != _first)
        {
            // Only freeing the first slot of a full ring makes a parked producer
            // newly eligible. Readers awaiting publication need no consumption pulses.
            if (wasFull) NotifyProduction();
            if (_waitingForSpace != 0) Monitor.PulseAll(_gate);
        }
    }

    private void Detach(int reader)
    {
        lock (_gate)
        {
            if (_exclusiveThreads != null)
            {
                if (Interlocked.Exchange(ref _exclusiveThreads[reader], -1) < 0) return;
            }
            else
            {
                long position = Interlocked.Exchange(ref _positions[reader], -1);
                if (position < 0) return;
                for (; position < _next; position++) Interlocked.Decrement(ref _remaining[Slot(position)]);
            }
            Volatile.Write(ref _active, _active - 1);
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
            if (_exclusiveThreads != null)
                for (int i = 0; i < _exclusiveThreads.Length; i++) Interlocked.Exchange(ref _exclusiveThreads[i], -1);
            else Array.Fill(_positions, -1);
            Array.Clear(_events);
            Array.Clear(_remaining);
            _first = _next;
            Volatile.Write(ref _active, 0);
            Monitor.PulseAll(_gate);
        }
        // Disposing a registration can wait for its callback, which also acquires the ring lock.
        _registration.Dispose();
    }
}
