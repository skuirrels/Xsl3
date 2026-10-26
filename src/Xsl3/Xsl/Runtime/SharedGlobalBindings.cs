// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Xsl3.Runtime;

/// <summary>Once-only global evaluation shared by runtimes in one transformation.</summary>
internal sealed class SharedGlobalBindings
{
    private readonly string[] _names;
    private readonly object[] _values;
    private readonly ExceptionDispatchInfo?[] _faults;
    private readonly int[] _owners;
    private readonly CancellationToken?[] _priorCancellation;
    private readonly EvaluationWaitGraph _evaluations;
    private readonly EvaluationWaitGraph.Node?[] _dependencies;
    internal CancellationToken EvaluationCancellation { get; }
    private readonly object _gate = new();
    private int _waiting;
    internal int WaitingCount { get { lock (_gate) return _waiting; } }

    internal SharedGlobalBindings(string[] names, object[] values, bool[] active, ExceptionDispatchInfo?[]? faults,
        CancellationToken cancellation = default, EvaluationWaitGraph? evaluations = null)
    {
        _names = names;
        _values = values;
        _faults = faults ?? new ExceptionDispatchInfo?[names.Length];
        _owners = new int[names.Length];
        _priorCancellation = new CancellationToken?[names.Length];
        EvaluationCancellation = cancellation;
        _evaluations = evaluations ?? new();
        _dependencies = new EvaluationWaitGraph.Node?[names.Length];
        // Promotion happens on the invoking thread, before any prong starts. A fork can itself be
        // evaluating a global: retain that ownership rather than allowing its evaluation a second time.
        for (int i = 0; i < active.Length; i++)
            if (active[i]) _dependencies[i] = _evaluations.Start(_owners[i] = Environment.CurrentManagedThreadId);
    }

    private XslTransformException Circular(int index) =>
        new("XTDE0640: circular definition of global variable or parameter " + _names[index]);

    internal bool IsComputed(int index, HashSet<int>? ancestors, CancellationToken cancellation,
        IReadOnlySet<EvaluationWaitGraph.Node>? dependencies = null)
    {
        if (Volatile.Read(ref _values[index]) != null) return true;
        // Dispose outside the gate: an executing cancellation callback may be waiting for it.
        using var registration = cancellation.UnsafeRegister(static state => ((SharedGlobalBindings)state!).Wake(), this);
        int thread = Environment.CurrentManagedThreadId;
        lock (_gate)
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                _faults[index]?.Throw();
                if (_values[index] != null) return true;
                int owner = _owners[index];
                if (owner == 0)
                {
                    _owners[index] = thread;
                    _dependencies[index] = _evaluations.Start(thread);
                    _priorCancellation[index] = cancellation;
                    return false;
                }
                if (owner == thread || ancestors?.Contains(index) == true) throw Circular(index);
                using var dependency = _evaluations.Wait(_dependencies[index]!, dependencies, () => Circular(index));
                _waiting++;
                try { Monitor.Wait(_gate); }
                finally { _waiting--; }
            }
        }
    }

    private void Wake()
    {
        lock (_gate) Monitor.PulseAll(_gate);
    }

    internal object Get(int index) => Volatile.Read(ref _values[index]);

    internal CancellationToken? Set(int index, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            if (_owners[index] != Environment.CurrentManagedThreadId || _values[index] != null || _faults[index] != null)
                throw new InvalidOperationException("Only the owner may complete a global binding");
            Volatile.Write(ref _values[index], value);
            _owners[index] = 0;
            _evaluations.Finish(_dependencies[index]);
            Monitor.PulseAll(_gate);
            return TakeCancellation(index);
        }
    }

    internal CancellationToken? Fail(int index, Exception error)
    {
        lock (_gate)
        {
            // A failed request for somebody else's binding must not release that owner's claim.
            if (_owners[index] != Environment.CurrentManagedThreadId) return null;
            _faults[index] ??= ExceptionDispatchInfo.Capture(error);
            _owners[index] = 0;
            _evaluations.Finish(_dependencies[index]);
            Monitor.PulseAll(_gate);
            return TakeCancellation(index);
        }
    }

    private CancellationToken? TakeCancellation(int index)
    {
        CancellationToken? previous = _priorCancellation[index];
        _priorCancellation[index] = null;
        return previous;
    }

    internal void SetDebugValue(int index, object value)
    {
        lock (_gate) Volatile.Write(ref _values[index], value);
    }

    internal HashSet<int>? CaptureDependencies(HashSet<int>? inherited)
    {
        HashSet<int>? result = inherited == null ? null : new(inherited);
        int thread = Environment.CurrentManagedThreadId;
        lock (_gate)
            for (int i = 0; i < _owners.Length; i++)
                if (_owners[i] == thread) (result ??= new()).Add(i);
        return result;
    }
}
