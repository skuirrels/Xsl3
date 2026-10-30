// Licensed under the MIT license. See LICENSE.TXT in the repository root.

namespace Xsl3.Streaming;

internal sealed partial class StreamedDocument
{
    private IStreamedNodeObserver? _observer;
    private bool _notifying;

    internal IStreamedNodeObserver? Observer => _observer;

    internal void Observe(IStreamedNodeObserver observer)
    {
        Check();
        // Setup must precede input consumption; attaching late cannot reconstruct earlier values.
        if (_observer != null || Root.ChildrenStarted) throw PositionError();
        _observer = observer;
        Notify(Root, end: false);
    }

    private void Notify(Node node, bool end)
    {
        if (_observer == null || (end ? node.ObservedEnd : node.ObservedStart)) return;
        bool saved = _notifying;
        _notifying = true;
        try
        {
            if (end) _observer.End(this, node);
            else _observer.Start(this, node);
            if (end) node.ObservedEnd = true;
            else node.ObservedStart = true;
        }
        finally { _notifying = saved; }
    }

    private void ObserveCurrent(long previous)
    {
        if (_observer == null) return;
        if (_current.Number > previous)
        {
            Notify(_current, end: false);
            if (_current.EmptyElement || _current.Kind is not (System.Xml.XPath.XPathNodeType.Root or System.Xml.XPath.XPathNodeType.Element))
                Notify(_current, end: true);
        }
        else if (_current.Ended) Notify(_current, end: true);
    }

    internal void CompleteSubtree(Node node)
    {
        Check();
        if (node.Ended || node.Kind is not (System.Xml.XPath.XPathNodeType.Root or System.Xml.XPath.XPathNodeType.Element)) return;
        // Ancestor headers in a fork view do not grant access to input outside its subtree.
        if (_forkBoundary != null && node.Number < _forkBoundary.Number) throw PositionError();
        if (_notifying || node.Skipped || Active(node.Number) == null) throw PositionError();
        // Unlike string-value/copy grounding, accumulator-after can finish an ancestor whose
        // earlier children have already been consumed. Only its remaining events are needed.
        _consuming++;
        using (new ConsumptionScope(this, node))
            while (!node.Ended) ReadNext();
    }
}
