// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Xml.XPath;

namespace Xsl3.Streaming;

/// <summary>Prunes only descendant cursors used while pulling this population's current item.</summary>
internal sealed class DescendantPruning
{
    [ThreadStatic] private static DescendantPruning? s_current;
    private List<WeakReference<StreamedDescendantCursor>>? _cursors;

    internal readonly struct Scope(DescendantPruning owner, DescendantPruning? previous) : IDisposable
    {
        public void Dispose()
        {
            owner._cursors?.Clear();
            s_current = previous;
        }
    }

    internal Scope Enter()
    {
        DescendantPruning? previous = s_current;
        s_current = this;
        return new(this, previous);
    }

    internal static void Observe(StreamedDescendantCursor cursor)
    {
        if (s_current is not { } current) return;
        current._cursors ??= new();
        current._cursors.RemoveAll(static reference => !reference.TryGetTarget(out var target) || target.Finished);
        foreach (var reference in current._cursors)
            if (reference.TryGetTarget(out var target) && ReferenceEquals(target, cursor)) return;
        current._cursors.Add(new(cursor));
    }

    internal void Prune(XPathNavigator node)
    {
        if (_cursors == null) return;
        foreach (var reference in _cursors)
            if (reference.TryGetTarget(out var cursor)) cursor.PruneAt(node);
    }
}
