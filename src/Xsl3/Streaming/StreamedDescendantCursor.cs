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
    private bool _missing, _skipDescendants, _started;
    private readonly bool _topmost;
    private readonly int? _maximumDepth;
    private StreamedValueReplay? _values;
    private StreamedNavigator? _resume;
    private bool _capturingValues;
    internal bool Finished { get; private set; }

    internal StreamedDescendantCursor(StreamedNavigator position, XmlNavigatorFilter filter, bool topmost = false, bool selfMatched = false, int? maximumDepth = null)
    {
        _position = position;
        _root = position.PositionNode;
        _filter = filter;
        _topmost = topmost;
        _maximumDepth = maximumDepth;
        _skipDescendants = topmost && selfMatched;
        // An or-self result has already started this registered cursor. Consumption
        // after that yield reports lost matches through Consumed; earlier consumption
        // is unobserved and must still fail when descendants are requested.
        _started = selfMatched && !_root.Consumed && !_root.Skipped;
        Finished = position.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace;
        if (!Finished)
        {
            position.Document.RegisterDescendantCursor(this);
            DescendantPruning.Observe(this);
        }
        if (selfMatched) _position.ValueCursor(this);
    }

    internal void Consumed(StreamedNavigator node)
    {
        // A body may consume its selected subtree. Continue only if no later match was lost inside it.
        // Descendants of an accepted outermost node are deliberately excluded. Otherwise a lost matching
        // descendant still needs replay. Explicit grounding owns that replay data; this cursor only keeps flags.
        if (!Finished && !_skipDescendants && (_maximumDepth == null || node.PositionNode.Depth - _root.Depth <= _maximumDepth) && _position.IsDescendant(node) && !_filter.IsFiltered(node))
        {
            if (!_capturingValues) _missing = true;
            else
            {
                _resume ??= (StreamedNavigator)_position.Clone();
                if (_values == null) _values = _position.Document.CreateValueReplay(this);
                _values.Add(node.PositionNode);
            }
        }
    }

    internal readonly struct ValueCapture(StreamedDescendantCursor cursor, StreamedDocument.Node root) : System.IDisposable
    {
        public void Dispose()
        {
            cursor._capturingValues = false;
            if (root.Ended) cursor._values?.FinishCapture();
            else cursor._missing = true;
        }
    }

    internal ValueCapture? PreserveValues(StreamedDocument.Node node)
    {
        if (Finished || _skipDescendants || _position.PositionNode.Number != node.Number || !NeedsReplay(_position)) return null;
        _capturingValues = true;
        return new(this, node);
    }

    internal void PruneAt(XPathNavigator node)
    {
        if (!Finished && _position.IsSamePosition(node)) _skipDescendants = true;
    }

    internal bool NeedsReplay(StreamedNavigator node) => !Finished && !_skipDescendants
        && (_maximumDepth == null || node.PositionNode.Depth - _root.Depth < _maximumDepth)
        && (_position.IsSamePosition(node) || _position.IsDescendant(node));

    internal void ReplayInterrupted(StreamedNavigator root)
    {
        if (!Finished && !_skipDescendants && (NeedsReplay(root) || root.IsDescendant(_position))) _missing = true;
    }

    private bool AtDepthLimit => _maximumDepth is { } maximum && _position.PositionNode.Depth - _root.Depth >= maximum;

    internal bool MoveNext()
    {
        if (Finished) return false;
        DescendantPruning.Observe(this);
        if (_missing) throw StreamedDocument.PositionError();
        if (_values?.MoveNext(_position) == true)
        {
            _position.ValueCursor(this);
            return true;
        }
        if (_resume != null)
        {
            _position.MoveTo(_resume);
            _resume = null;
            _values!.Reset();
        }
        if (!_skipDescendants && _maximumDepth != 0 && _root.Kind is XPathNodeType.Root or XPathNodeType.Element
            && !_root.EmptyElement && !StreamedDocument.CanReplay(_root) && (_root.Skipped || !_started && _root.Consumed))
            throw StreamedDocument.PositionError();
        _started = true;
        bool moved = _skipDescendants || AtDepthLimit ? _position.MoveToNextNonDescendant(_root) : _position.MoveToNextDescendant(_root);
        _skipDescendants = false;
        while (moved)
        {
            if (!_filter.IsFiltered(_position))
            {
                _skipDescendants = _topmost;
                _position.ValueCursor(this);
                return true;
            }
            moved = AtDepthLimit ? _position.MoveToNextNonDescendant(_root) : _position.MoveToNextDescendant(_root);
        }
        Finished = true;
        if (_values != null) _position.Document.ReleaseValueReplay(_values);
        return false;
    }
}
