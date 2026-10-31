// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

/// <summary>Grounded accumulator phases and applicability in one compiled fork family.</summary>
internal sealed class GroundedAccumulatorValues
{
    internal readonly record struct Position(string? Id, long Before = 0, long After = 0);

    private sealed class CompactPhases
    {
        private const int BlockShift = 10;
        private const int BlockSize = 1 << BlockShift;
        private const int BlockMask = BlockSize - 1;
        private readonly List<(long Position, IList<XPathItem> Value)[]> _blocks = new();
        private long _count;
        private long _last = -1;

        private (long Position, IList<XPathItem> Value) At(long index) =>
            _blocks[(int)(index >> BlockShift)][(int)(index & BlockMask)];

        internal void Append(IList<XPathItem> value)
        {
            long position = ++_last;
            // An unmatched rule carries the same immutable phase sequence forward.
            if (_count != 0 && ReferenceEquals(At(_count - 1).Value, value)) return;
            int block = (int)(_count >> BlockShift), slot = (int)(_count & BlockMask);
            if (block == _blocks.Count)
                _blocks.Add(new (long, IList<XPathItem>)[_count == 0 ? 4 : BlockSize]);
            else if (slot == _blocks[block].Length)
            {
                // Only the small first block grows. Later blocks avoid copying a
                // large timeline and retaining old/new buffers simultaneously.
                var buffer = _blocks[block];
                Array.Resize(ref buffer, buffer.Length * 2);
                _blocks[block] = buffer;
            }
            _blocks[block][slot] = (position, value);
            _count++;
        }

        internal bool TryValue(long position, out IList<XPathItem>? value)
        {
            value = null;
            // Future phases stay undefined even when the latest value is unchanged.
            if (position < 0 || position > _last) return false;
            long low = 0, high = _count;
            while (low < high)
            {
                long middle = low + (high - low) / 2;
                if (At(middle).Position <= position) low = middle + 1;
                else high = middle;
            }
            if (low == 0) return false;
            value = At(low - 1).Value;
            return true;
        }
    }

    internal sealed class Document
    {
        internal readonly Dictionary<string, State> Accumulators = new(StringComparer.Ordinal);
        internal bool HasSelection;
        internal HashSet<string>? Selected;
    }

    internal sealed class State(Document document, string name, bool compact)
    {
        internal readonly Document Document = document;
        internal readonly string Name = name;
        internal readonly int Owner = Environment.CurrentManagedThreadId;
        private readonly CompactPhases? _compact = compact ? new() : null;
        private readonly Dictionary<string, (IList<XPathItem>? Before, IList<XPathItem>? After)>? _values = compact ? null : new(StringComparer.Ordinal);
        internal bool IsCompact => _compact != null;
        internal EvaluationWaitGraph.Node? Dependency;
        internal ExceptionDispatchInfo? Fault;
        internal bool Active = true;

        internal bool TryValue(Position position, bool after, out IList<XPathItem>? value)
        {
            if (_compact != null) return _compact.TryValue(after ? position.After : position.Before, out value);
            value = null;
            if (_values!.TryGetValue(position.Id!, out var phases)) value = after ? phases.After : phases.Before;
            return value != null;
        }

        internal void Record(string? nodeId, IList<XPathItem> before, IList<XPathItem>? after)
        {
            if (_compact != null) _compact.Append(after ?? before);
            else _values![nodeId!] = (before, after);
        }
    }

    private readonly object _gate = new();
    private readonly ConditionalWeakTable<object, Document> _documents = new();
    private readonly Dictionary<string, Document> _foreignDocuments = new(StringComparer.Ordinal);
    private readonly HashSet<State> _active = new();
    private EvaluationWaitGraph? _evaluations;
    private CancellationToken _evaluationCancellation;
    internal bool IsShared => _evaluations != null;
    internal CancellationToken EvaluationCancellation => _evaluationCancellation;

    private Document GetDocument(XPathNavigator root, string rootId)
    {
        if (DocumentOrderComparer.NativeDocumentIdentity(root) is { } document)
            return _documents.GetValue(document, static _ => new());
        if (!_foreignDocuments.TryGetValue(rootId, out Document? known)) _foreignDocuments.Add(rootId, known = new());
        return known;
    }

    internal void Share(EvaluationWaitGraph evaluations, CancellationToken cancellation)
    {
        lock (_gate)
        {
            if (_evaluations != null) return;
            _evaluations = evaluations;
            _evaluationCancellation = cancellation;
            foreach (State state in _active) state.Dependency = evaluations.Start(state.Owner);
        }
    }

    internal bool Applies(XPathNavigator root, string rootId, string name)
    {
        lock (_gate)
        {
            Document document = GetDocument(root, rootId);
            return !document.HasSelection || document.Selected == null || document.Selected.Contains(name);
        }
    }

    internal void Select(XPathNavigator root, string rootId, HashSet<string>? names)
    {
        lock (_gate)
        {
            Document document = GetDocument(root, rootId);
            document.Selected = names;
            document.HasSelection = true;
        }
    }

    // Null means the requested value was ready. A returned state is an exclusive claim
    // to evaluate; rule callbacks and phase-value copying happen outside this monitor.
    internal State? Demand(XPathNavigator root, string rootId, string name, Position position, bool after,
        IReadOnlySet<EvaluationWaitGraph.Node>? inherited, CancellationToken cancellation, out IList<XPathItem>? value)
    {
        using var registration = cancellation.UnsafeRegister(static state => ((GroundedAccumulatorValues)state!).Wake(), this);
        int thread = Environment.CurrentManagedThreadId;
        lock (_gate)
        {
            Document document = GetDocument(root, rootId);
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!document.Accumulators.TryGetValue(name, out State? state))
                {
                    state = new(document, name, root is Tree.CompactNavigator) { Dependency = _evaluations?.Start(thread) };
                    document.Accumulators.Add(name, state);
                    _active.Add(state);
                    value = null;
                    return state;
                }
                if (state.Fault is { } cycle && XsltLibrary.IsAccumulatorCycle(cycle.SourceException)) cycle.Throw();
                // Recursive demands can use an already defined phase. Unrelated prongs wait for
                // completion so a later circular error cannot escape as a successful early read.
                bool own = state.Owner == thread || state.Dependency != null && inherited?.Contains(state.Dependency) == true;
                if ((!state.Active || own) && state.TryValue(position, after, out value)) return null;
                if (!state.Active)
                {
                    state.Fault?.Throw();
                    throw new InvalidOperationException("An accumulator computation produced no value for the requested node and phase");
                }
                if (_evaluations == null) throw Circular(name);
                using var wait = _evaluations.Wait(state.Dependency!, inherited, () => Circular(name));
                Monitor.Wait(_gate);
            }
        }
    }

    private static XdmException Circular(string name) => new("XTDE3400", $"The accumulator {name} depends on its own value") { AccumulatorCycle = true };

    internal void Record(State state, string? nodeId, IList<XPathItem> before, IList<XPathItem>? after)
    {
        lock (_gate)
        {
            if (!state.Active || state.Owner != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Only the evaluating thread may record accumulator phases");
            state.Record(nodeId, before, after);
        }
    }

    internal void Finish(State state, bool completed, Exception? failure)
    {
        lock (_gate)
        {
            if (!state.Active || state.Owner != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Only the evaluating thread may finish an accumulator");
            if (completed) state.Fault = failure == null ? null : ExceptionDispatchInfo.Capture(failure);
            else state.Document.Accumulators.Remove(state.Name); // Cancellation/fatal errors leave no partial cache.
            state.Active = false;
            _active.Remove(state);
            _evaluations?.Finish(state.Dependency);
            Monitor.PulseAll(_gate);
        }
    }

    internal IList<XPathItem> Result(State state, Position position, bool after)
    {
        lock (_gate)
        {
            if (state.Fault is { } cycle && XsltLibrary.IsAccumulatorCycle(cycle.SourceException)) cycle.Throw();
            if (state.TryValue(position, after, out var value)) return value!;
            state.Fault?.Throw();
            throw new InvalidOperationException("An accumulator computation produced no value for the requested node and phase");
        }
    }

    private void Wake() { lock (_gate) Monitor.PulseAll(_gate); }
}
