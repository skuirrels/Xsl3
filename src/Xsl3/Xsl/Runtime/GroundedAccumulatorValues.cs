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
    internal sealed class Document
    {
        internal readonly Dictionary<string, State> Accumulators = new(StringComparer.Ordinal);
        internal bool HasSelection;
        internal HashSet<string>? Selected;
    }

    internal sealed class State(Document document, string name)
    {
        internal readonly Document Document = document;
        internal readonly string Name = name;
        internal readonly int Owner = Environment.CurrentManagedThreadId;
        internal readonly Dictionary<string, (IList<XPathItem>? Before, IList<XPathItem>? After)> Values = new(StringComparer.Ordinal);
        internal EvaluationWaitGraph.Node? Dependency;
        internal ExceptionDispatchInfo? Fault;
        internal bool Active = true;
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
    internal State? Demand(XPathNavigator root, string rootId, string name, string nodeId, bool after,
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
                    state = new(document, name) { Dependency = _evaluations?.Start(thread) };
                    document.Accumulators.Add(name, state);
                    _active.Add(state);
                    value = null;
                    return state;
                }
                if (state.Fault is { } cycle && XsltLibrary.IsAccumulatorCycle(cycle.SourceException)) cycle.Throw();
                // Recursive demands can use an already defined phase. Unrelated prongs wait for
                // completion so a later circular error cannot escape as a successful early read.
                bool own = state.Owner == thread || state.Dependency != null && inherited?.Contains(state.Dependency) == true;
                if ((!state.Active || own) && TryValue(state, nodeId, after, out value)) return null;
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

    private static bool TryValue(State state, string nodeId, bool after, out IList<XPathItem>? value)
    {
        value = null;
        if (state.Values.TryGetValue(nodeId, out var phases)) value = after ? phases.After : phases.Before;
        return value != null;
    }

    internal void Record(State state, string nodeId, IList<XPathItem> before, IList<XPathItem>? after)
    {
        lock (_gate)
        {
            if (!state.Active || state.Owner != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Only the evaluating thread may record accumulator phases");
            state.Values[nodeId] = (before, after);
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

    internal IList<XPathItem> Result(State state, string nodeId, bool after)
    {
        lock (_gate)
        {
            if (state.Fault is { } cycle && XsltLibrary.IsAccumulatorCycle(cycle.SourceException)) cycle.Throw();
            if (TryValue(state, nodeId, after, out var value)) return value!;
            state.Fault?.Throw();
            throw new InvalidOperationException("An accumulator computation produced no value for the requested node and phase");
        }
    }

    private void Wake() { lock (_gate) Monitor.PulseAll(_gate); }
}
