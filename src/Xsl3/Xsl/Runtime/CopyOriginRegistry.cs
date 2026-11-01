// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Tree;

namespace Xsl3.Runtime;

/// <summary>A copy keeps its original's accumulator provenance for as long as the copy is live.</summary>
internal sealed class CopyOriginRegistry
{
    internal sealed record Origin(XPathNavigator Top, XPathNavigator? Original, bool Validated, IStreamedAccumulatorCopy? Accumulators = null);
    private sealed record StreamedOrigins(object Scope, Origin Value, StreamedOrigins? Next);

    // This token cannot keep the registry or invocation alive through a retained copy.
    private readonly object _scope = new();
    private ConditionalWeakTable<CompactDocument, Origin>? _origins;

    internal void Set(XPathNavigator copy, XPathNavigator original, bool validated = false)
    {
        if (copy is not CompactNavigator compact)
            throw new InvalidOperationException("A generated copy must belong to a compact document");
        bool streamed = original is StreamedNavigator
            || original is CompactNavigator source && FindStreamed(source.Document) != null
            || FindStreamed(compact.Document) != null;
        if (streamed)
        {
            // A long-lived weak table builds collection pressure for short-lived merge
            // copies. Copy-owned cycles can be collected with the copies themselves.
            IStreamedAccumulatorCopy? captured = original is StreamedNavigator streamedOriginal
                && streamedOriginal.Document.Identity.Observer is IStreamedAccumulatorCopyProvider provider
                ? provider.CaptureCopy(copy, streamedOriginal) : null;
            var value = new Origin(copy.Clone(), captured == null ? original.Clone() : null, validated, captured);
            while (true)
            {
                var previous = (StreamedOrigins?)Volatile.Read(ref compact.Document.StreamedCopyOrigins);
                StreamedOrigins updated = Updated(previous, value);
                if (ReferenceEquals(Interlocked.CompareExchange(ref compact.Document.StreamedCopyOrigins, updated, previous), previous)) break;
            }
            Volatile.Read(ref _origins)?.Remove(compact.Document);
            return;
        }
        // Ordinary copies retain the existing invocation-owned lifetime: a small returned
        // grounded copy must not keep its entire original input alive after the run.
        ConditionalWeakTable<CompactDocument, Origin>? table = Volatile.Read(ref _origins);
        if (table == null)
        {
            table = new();
            table = Interlocked.CompareExchange(ref _origins, table, null) ?? table;
        }
        table.AddOrUpdate(compact.Document, new(copy.Clone(), original.Clone(), validated));
    }

    private StreamedOrigins Updated(StreamedOrigins? entries, Origin value)
    {
        StreamedOrigins? found = entries;
        while (found != null && !ReferenceEquals(found.Scope, _scope)) found = found.Next;
        if (found == null) return new(_scope, value, entries);
        var updated = new StreamedOrigins(_scope, value, found.Next);
        if (ReferenceEquals(found, entries)) return updated;
        // Registries can independently annotate the same copy. Replacing one scope must
        // preserve the others without retaining an obsolete value for that scope.
        var prefix = new List<StreamedOrigins>();
        for (StreamedOrigins? entry = entries; !ReferenceEquals(entry, found); entry = entry!.Next) prefix.Add(entry!);
        for (int i = prefix.Count - 1; i >= 0; i--) updated = prefix[i] with { Next = updated };
        return updated;
    }

    private StreamedOrigins? FindStreamed(CompactDocument document)
    {
        for (var entry = (StreamedOrigins?)Volatile.Read(ref document.StreamedCopyOrigins); entry != null; entry = entry.Next)
            if (ReferenceEquals(entry.Scope, _scope)) return entry;
        return null;
    }

    internal bool TryGet(XPathNavigator copy, [NotNullWhen(true)] out Origin? origin)
    {
        origin = null;
        if (copy is not CompactNavigator compact) return false;
        if (FindStreamed(compact.Document) is { } streamed) { origin = streamed.Value; return true; }
        return Volatile.Read(ref _origins)?.TryGetValue(compact.Document, out origin) == true;
    }
}
