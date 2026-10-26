// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

/// <summary>Fully published key indexes and once-only builds in one compiled fork family.</summary>
internal sealed class SharedKeyIndexes(EvaluationWaitGraph evaluations, CancellationToken evaluationCancellation)
{
    private sealed class Entry
    {
        internal XmlILIndex? Value;
        internal ExceptionDispatchInfo? Fault;
        internal EvaluationWaitGraph.Node? Dependency;
        internal CancellationToken? Previous;
    }
    private readonly object _gate = new();
    private readonly ConditionalWeakTable<object, Dictionary<int, Entry>> _documents = new();
    private readonly Dictionary<string, Dictionary<int, Entry>> _foreignDocuments = new();

    // A value may point back to its own document. Dependent handles allow that whole
    // index/document cycle to be collected between grounded records.
    private Dictionary<int, Entry> Entries(XPathNavigator root, string rootId)
    {
        if (DocumentOrderComparer.NativeDocumentIdentity(root) is { } document)
            return _documents.GetValue(document, static _ => new());
        if (!_foreignDocuments.TryGetValue(rootId, out var entries)) _foreignDocuments.Add(rootId, entries = new());
        return entries;
    }

    internal void Seed(int id, XPathNavigator root, string rootId, XmlILIndex? value, ExceptionDispatchInfo? fault, bool active)
    {
        if (!active) value?.Share();
        Entries(root, rootId).Add(id, new Entry { Value = active ? null : value, Fault = fault,
            Dependency = active ? evaluations.Start(Environment.CurrentManagedThreadId) : null });
    }

    internal bool Find(int id, XPathNavigator root, string rootId, CancellationToken cancellation, IReadOnlySet<EvaluationWaitGraph.Node>? inherited, out XmlILIndex value)
    {
        using var registration = cancellation.UnsafeRegister(static state => ((SharedKeyIndexes)state!).Wake(), this);
        XmlILIndex? building = null;
        lock (_gate)
        {
            var entries = Entries(root, rootId);
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!entries.TryGetValue(id, out Entry? entry))
                {
                    building = new XmlILIndex();
                    entry = new() { Dependency = evaluations.Start(Environment.CurrentManagedThreadId), Previous = cancellation };
                    entries.Add(id, entry);
                }
                entry.Fault?.Throw();
                if (entry.Value != null) { value = entry.Value; return true; }
                if (building != null) { value = building; break; }
                using var wait = evaluations.Wait(entry.Dependency!, inherited,
                    static () => new XdmException("XTDE0640", "The definition of a key depends on itself"));
                Monitor.Wait(_gate);
            }
        }
        XmlQueryRuntime.Cancellation = evaluationCancellation;
        return false;
    }

    internal CancellationToken? Publish(int id, XPathNavigator root, string rootId, XmlILIndex? value, Exception? error)
    {
        lock (_gate)
        {
            if (!Entries(root, rootId).TryGetValue(id, out Entry? entry) || entry.Dependency?.Owner != Environment.CurrentManagedThreadId
                || entry.Value != null || entry.Fault != null) return null;
            value?.Share();
            entry.Value = value;
            entry.Fault = error == null ? null : ExceptionDispatchInfo.Capture(error);
            evaluations.Finish(entry.Dependency);
            CancellationToken? previous = entry.Previous;
            entry.Previous = null;
            Monitor.PulseAll(_gate);
            return previous;
        }
    }

    private void Wake() { lock (_gate) Monitor.PulseAll(_gate); }
}
