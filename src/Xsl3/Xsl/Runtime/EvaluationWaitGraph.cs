// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;

namespace Xsl3.Runtime;

/// <summary>Dependencies between shared computations, including different cache kinds.</summary>
internal sealed class EvaluationWaitGraph
{
    internal sealed class Node(int owner)
    {
        internal readonly int Owner = owner;
        internal bool Complete;
    }

    private readonly object _gate = new();
    private readonly HashSet<Node> _active = new();
    private readonly Dictionary<int, Node> _waiting = new();
    internal int WaitingCount { get { lock (_gate) return _waiting.Count; } }

    internal Node Start(int owner)
    {
        var node = new Node(owner);
        lock (_gate) _active.Add(node);
        return node;
    }

    internal void Finish(Node? node)
    {
        if (node == null) return;
        lock (_gate) { node.Complete = true; _active.Remove(node); }
    }

    // This graph never enters a cache lock or runs a computation. Cache owners may use
    // it under their own monitor, then release that monitor with Monitor.Wait as usual.
    internal WaitScope Wait(Node node, IReadOnlySet<Node>? inherited, Func<Exception> circular)
    {
        int thread = Environment.CurrentManagedThreadId;
        lock (_gate)
        {
            if (node.Complete) return default;
            if (node.Owner == thread || inherited?.Contains(node) == true) throw circular();
            for (Node other = node; _waiting.TryGetValue(other.Owner, out Node? pending);)
            {
                if (pending.Complete) break;
                if (pending.Owner == thread || inherited?.Contains(pending) == true) throw circular();
                other = pending;
            }
            _waiting.Add(thread, node);
            return new(this, thread);
        }
    }

    internal readonly struct WaitScope(EvaluationWaitGraph? graph, int thread) : IDisposable
    {
        public void Dispose()
        {
            if (graph != null) lock (graph._gate) graph._waiting.Remove(thread);
        }
    }

    internal IReadOnlySet<Node>? Capture(IReadOnlySet<Node>? inherited)
    {
        HashSet<Node>? result = inherited == null ? null : new(inherited);
        int thread = Environment.CurrentManagedThreadId;
        lock (_gate)
            foreach (Node node in _active)
                if (node.Owner == thread) (result ??= new()).Add(node);
        return result;
    }
}
