// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Xsl3.Streaming;

/// <summary>Invocation facts owned by source frames, without a history of discarded frame keys.</summary>
internal sealed class StreamedFrameFacts : IDisposable
{
    private sealed record Entry(WeakReference<StreamedFrameFacts> Scope, object Value, Entry? Next);
    private readonly WeakReference<StreamedFrameFacts> _reference;
    private int _disposed;

    internal StreamedFrameFacts() => _reference = new(this);

    private static bool Live(Entry entry, out StreamedFrameFacts? scope) => entry.Scope.TryGetTarget(out scope)
        && Volatile.Read(ref scope._disposed) == 0;

    internal bool TryGet<T>(StreamedDocument.Node node, out T? value) where T : class
    {
        node = node.Original ?? node;
        for (Entry? entry = Volatile.Read(ref node.RuntimeFacts) as Entry; entry != null; entry = entry.Next)
            if (Live(entry, out var scope) && ReferenceEquals(scope, this)) { value = (T)entry.Value; return true; }
        value = null;
        return false;
    }

    internal T Get<T>(StreamedDocument.Node node, Func<T> factory) where T : class
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(StreamedFrameFacts));
        if (TryGet<T>(node, out var existing)) return existing!;
        node = node.Original ?? node;
        T? created = null;
        while (true)
        {
            object? observed = Volatile.Read(ref node.RuntimeFacts);
            var live = new List<Entry>();
            for (Entry? entry = observed as Entry; entry != null; entry = entry.Next)
            {
                if (!Live(entry, out var scope)) continue;
                if (ReferenceEquals(scope, this)) return (T)entry.Value;
                live.Add(entry);
            }
            created ??= factory() ?? throw new InvalidOperationException("A frame fact factory returned null");
            Entry? next = null;
            for (int i = live.Count - 1; i >= 0; i--) next = new(live[i].Scope, live[i].Value, next);
            var updated = new Entry(_reference, created, next);
            if (ReferenceEquals(Interlocked.CompareExchange(ref node.RuntimeFacts, updated, observed), observed)) return created;
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
