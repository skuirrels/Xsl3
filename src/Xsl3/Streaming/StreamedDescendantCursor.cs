// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using Xsl3.Runtime;
using System.Xml.XPath;

namespace Xsl3.Streaming;

/// <summary>A descendant scan bounded by an ancestor, without seeking ahead to its following sibling.</summary>
internal sealed class StreamedDescendantCursor
{
    private readonly StreamedNavigator _position;
    private readonly StreamedDocument.Node _root;
    private readonly XmlNavigatorFilter _filter;
    private bool _missing;
    internal bool Finished { get; private set; }

    internal StreamedDescendantCursor(StreamedNavigator position, XmlNavigatorFilter filter)
    {
        _position = position;
        _root = position.PositionNode;
        _filter = filter;
        Finished = position.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace;
        if (!Finished) position.Document.RegisterDescendantCursor(this);
    }

    internal void Consumed(StreamedNavigator node)
    {
        // A body may consume its selected subtree. Continue only if no later match was lost inside it.
        // Keep one bit, never a list of the consumed nodes. Overlapping absorption still needs replay.
        if (!Finished && _position.IsDescendant(node) && !_filter.IsFiltered(node)) _missing = true;
    }

    internal bool MoveNext()
    {
        if (Finished) return false;
        if (_missing) throw StreamedDocument.PositionError();
        while (_position.MoveToNextDescendant(_root))
            if (!_filter.IsFiltered(_position)) return true;
        Finished = true;
        return false;
    }
}
