// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Xml.XPath;
using Xsl3.Tree;

namespace Xsl3.Runtime;

/// <summary>A copy keeps its original's accumulator provenance for as long as the copy is live.</summary>
internal sealed class CopyOriginRegistry
{
    internal sealed record Origin(XPathNavigator Top, XPathNavigator Original, bool Validated);
    private readonly ConditionalWeakTable<CompactDocument, Origin> _origins = new();

    internal void Set(XPathNavigator copy, XPathNavigator original, bool validated = false)
    {
        if (copy is not CompactNavigator compact)
            throw new InvalidOperationException("A generated copy must belong to a compact document");
        // Origin.Top points back into the key's document. Ephemeron semantics let that cycle be collected.
        _origins.AddOrUpdate(compact.Document, new(copy.Clone(), original.Clone(), validated));
    }

    internal bool TryGet(XPathNavigator copy, [NotNullWhen(true)] out Origin? origin)
    {
        origin = null;
        return copy is CompactNavigator compact && _origins.TryGetValue(compact.Document, out origin);
    }
}
