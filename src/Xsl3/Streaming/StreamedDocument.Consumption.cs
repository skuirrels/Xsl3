// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

internal sealed partial class StreamedDocument
{
    private List<ConsumptionMatch>? _consumptionMatches;

    private List<ConsumedHeaders>? _consumedHeaders;

    internal sealed class ConsumedHeaders : System.IDisposable
    {
        private StreamedDocument? _owner;
        private StreamedNavigator? _root;
        private System.Action<StreamedNavigator>? _observe;

        internal ConsumedHeaders(StreamedDocument owner, StreamedNavigator root, System.Action<StreamedNavigator> observe) =>
            (_owner, _root, _observe) = (owner, root, observe);

        internal void Observe(StreamedNavigator node)
        {
            if (_owner is not { } owner || !_root!.IsDescendant(node)) return;
            bool notifying = owner._notifying;
            owner._notifying = true;
            try { _observe!(node); }
            finally { owner._notifying = notifying; }
        }

        public void Dispose()
        {
            if (_owner is not { } owner) return;
            _owner = null; _root = null; _observe = null;
            owner._consumedHeaders!.Remove(this);
            if (owner._consumedHeaders.Count == 0) owner._consumedHeaders = null;
        }
    }

    internal ConsumedHeaders ObserveConsumedHeaders(StreamedNavigator root, System.Action<StreamedNavigator> observe)
    {
        Check();
        var headers = new ConsumedHeaders(this, (StreamedNavigator)root.Clone(), observe);
        (_consumedHeaders ??= new()).Add(headers);
        return headers;
    }

    // Observe only immutable headers while a callee absorbs a selected subtree. No
    // descendant positions, payloads or replay frames are retained by this proof.
    internal sealed class ConsumptionMatch : System.IDisposable
    {
        private StreamedDocument? _owner;
        private readonly StreamedNavigator _root;
        private readonly NodeKindTest _test;
        internal bool Matched { get; private set; }
        internal ConsumptionMatch(StreamedDocument owner, StreamedNavigator root, NodeKindTest test) =>
            (_owner, _root, _test) = (owner, (StreamedNavigator)root.Clone(), test);

        internal void Observe(StreamedNavigator node)
        {
            if (!Matched && _root.IsDescendant(node) && _test.Matches(node)) Matched = true;
        }

        public void Dispose()
        {
            if (_owner is not { } owner) return;
            _owner = null;
            owner._consumptionMatches!.Remove(this);
            if (owner._consumptionMatches.Count == 0) owner._consumptionMatches = null;
        }
    }

    internal ConsumptionMatch MatchConsumption(StreamedNavigator root, NodeKindTest test)
    {
        Check();
        var match = new ConsumptionMatch(this, root, test);
        (_consumptionMatches ??= new()).Add(match);
        return match;
    }

    // Keep validation and header observations active when a semantic error ends an
    // absorbing operation before its source subtree has been fully read.
    internal void CompleteConsumption(Node node)
    {
        Check();
        if (CanReplay(node)) return;
        while (!node.Ended) ReadNext();
    }
}
