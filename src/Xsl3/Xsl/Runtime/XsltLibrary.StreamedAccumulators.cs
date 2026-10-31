// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Tree;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class StreamedAccumulators : IStreamedNodeObserver, IStreamedAccumulatorCopyProvider
    {
        // Copies must observe a later detected cycle without retaining Current or the source document.
        private sealed class CycleState { internal volatile ExceptionDispatchInfo? Fault; }

        private sealed class State(string name, AccumulatorInfo info)
        {
            internal readonly string Name = name;
            internal readonly AccumulatorInfo Info = info;
            internal readonly bool HasStartRules = Array.IndexOf(info.EndPhases, false) >= 0;
            internal readonly bool HasEndRules = Array.IndexOf(info.EndPhases, true) >= 0;
            internal IList<XPathItem>? Current;
            internal bool Initialized, Initializing;
            internal readonly CycleState Cycles = new();
            private volatile ExceptionDispatchInfo? _fault;
            internal ExceptionDispatchInfo? Fault
            {
                get => _fault;
                set
                {
                    _fault = value;
                    if (value != null && IsAccumulatorCycle(value.SourceException)) Cycles.Fault = value;
                }
            }
        }

        private sealed class Phase
        {
            internal volatile IList<XPathItem>? Before, After;
            internal bool Starting, Ending;
            internal List<WeakReference<Phase>>? Readers;
            private int _pruneAt = 1;

            internal void Register(Phase reader)
            {
                reader.Before ??= Before;
                reader.After ??= After;
                if (Before != null && After != null) return;
                Readers ??= new();
                if (Readers.Count >= _pruneAt)
                {
                    Readers.RemoveAll(weak => !weak.TryGetTarget(out _));
                    // A long-lived ancestor can be inspected for every input record. Grow
                    // the prune interval with live readers instead of rescanning them per record.
                    _pruneAt = Readers.Count > int.MaxValue / 2 ? int.MaxValue : Math.Max(1, Readers.Count * 2);
                }
                Readers.Add(new(reader));
            }

            internal void Publish(IList<XPathItem> value, bool after)
            {
                if (after) After = value;
                else Before = value;
                if (Readers == null) return;
                foreach (var weak in Readers)
                    if (weak.TryGetTarget(out var reader))
                    {
                        if (after) reader.After = value;
                        else reader.Before = value;
                    }
                if (Before != null && After != null) Readers = null;
            }
        }

        private sealed class NodeValues(object owner, SnapshotCodec codec, bool frozen = false) : IStreamedHeaderMetadata
        {
            internal readonly object Owner = owner;
            internal readonly bool Frozen = frozen;
            // The common single-accumulator frame needs no dictionary or entry array.
            // Additional expanded names retain the original ordinal lookup semantics.
            private string? _firstName;
            private Phase? _firstPhase;
            private Dictionary<string, Phase>? _more;
            internal int Count => _firstName == null ? 0 : 1 + (_more?.Count ?? 0);

            internal bool TryGetValue(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Phase? phase)
            {
                if (_firstName != null && string.Equals(name, _firstName, StringComparison.Ordinal))
                {
                    phase = _firstPhase!;
                    return true;
                }
                if (_more != null) return _more.TryGetValue(name, out phase);
                phase = null;
                return false;
            }

            internal void Add(string name, Phase phase)
            {
                ArgumentNullException.ThrowIfNull(name);
                if (_firstName == null) { _firstName = name; _firstPhase = phase; return; }
                if (string.Equals(name, _firstName, StringComparison.Ordinal))
                    throw new ArgumentException("An accumulator phase is already recorded", nameof(name));
                (_more ??= new(StringComparer.Ordinal)).Add(name, phase);
            }

            public Enumerator GetEnumerator() => new(this);
            public struct Enumerator : IDisposable
            {
                private readonly KeyValuePair<string, Phase> _first;
                private readonly bool _hasFirst, _hasMore;
                private Dictionary<string, Phase>.Enumerator _more;
                private bool _started;
                internal Enumerator(NodeValues values)
                {
                    _hasFirst = values._firstName != null;
                    _hasMore = values._more != null;
                    _first = _hasFirst ? new(values._firstName!, values._firstPhase!) : default;
                    _more = values._more?.GetEnumerator() ?? default;
                    _started = false;
                    Current = default;
                }
                public KeyValuePair<string, Phase> Current { get; private set; }
                public bool MoveNext()
                {
                    if (!_started) { _started = true; if (_hasFirst) { Current = _first; return true; } }
                    if (_hasMore && _more.MoveNext()) { Current = _more.Current; return true; }
                    return false;
                }
                public void Dispose() { if (_hasMore) _more.Dispose(); }
            }

            public IStreamedHeaderMetadataCodec Codec => codec;
            public void Write(BinaryWriter writer, StreamedXdmValueStore values)
            {
                lock (Owner)
                {
                    writer.Write(Count);
                    foreach (var (name, phase) in this)
                    {
                        writer.Write(name);
                        values.Write(writer, phase.Before);
                        values.Write(writer, phase.After);
                    }
                }
            }
        }

        private sealed class SnapshotCodec(object owner) : IStreamedHeaderMetadataCodec
        {
            public object Read(BinaryReader reader, StreamedXdmValueStore values, StreamedDocument document, long number)
            {
                var snapshot = new NodeValues(owner, this, frozen: true);
                for (int count = StreamedXdmValueStore.Count(reader); count > 0; count--)
                    snapshot.Add(reader.ReadString(), new Phase { Before = values.Read(reader), After = values.Read(reader) });
                // Only open source/prong frames participate. Weak readers live on their
                // original phase and disappear at publication, without a closed-frame index.
                lock (owner)
                {
                    var active = document.ActiveHeader(number);
                    if ((active?.Original ?? active)?.ObserverValues is NodeValues live && ReferenceEquals(live.Owner, owner))
                        foreach (var (name, phase) in snapshot)
                            if (live.TryGetValue(name, out var original)) original.Register(phase);
                }
                return snapshot;
            }
        }

        private readonly record struct CopyError(CycleState Cycles, ExceptionDispatchInfo? Fault);

        private sealed class CopiedValues(CompactDocument document, object owner,
            Dictionary<long, NodeValues?> nodes, Dictionary<string, CopyError> errors) : IStreamedAccumulatorCopy
        {
            public bool Contains(XPathNavigator node) => node is CompactNavigator compact
                && node.NodeType is not (XPathNodeType.Attribute or XPathNodeType.Namespace)
                && ReferenceEquals(compact.Document, document) && nodes.ContainsKey(compact.TraversalPhases.Before);

            public bool TryValue(XPathNavigator node, string name, bool after, out IList<XPathItem>? value)
            {
                value = null;
                if (node is not CompactNavigator compact || node.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace
                    || !ReferenceEquals(compact.Document, document) || !nodes.TryGetValue(compact.TraversalPhases.Before, out var recorded)) return false;
                if (!errors.TryGetValue(name, out var error))
                    throw new XdmException("XTDE3362", $"The accumulator {name} isn't applicable to the document (use-accumulators)");
                if (error.Cycles.Fault is { } cycle) cycle.Throw();
                lock (owner)
                    if (recorded != null && recorded.TryGetValue(name, out var phase))
                        value = after ? phase.After : phase.Before;
                if (value != null) return true;
                error.Fault?.Throw();
                throw StreamedDocument.PositionError();
            }
        }

        public IStreamedAccumulatorCopy? CaptureCopy(XPathNavigator copy, StreamedNavigator original)
        {
            StreamedDocument.Node boundary = original.PositionNode;
            if (copy is not CompactNavigator compact || !StreamedDocument.CanReplay(boundary)) return null;
            StreamedDocument.ReplayBuffer replay = boundary.Replay!;
            var nodes = new Dictionary<long, NodeValues?>();
            var cursor = (CompactNavigator)compact.Clone();
            using var source = replay.Subtree(boundary).GetEnumerator();
            int depth = 0;
            do
            {
                if (!source.MoveNext() || source.Current.Depth - boundary.Depth != depth || source.Current.Kind != cursor.NodeType
                    || source.Current.LocalName != cursor.LocalName || source.Current.Namespace != cursor.NamespaceURI) return null;
                lock (_valuesGate)
                    nodes.Add(cursor.TraversalPhases.Before,
                        source.Current.ObserverValues is NodeValues values && ReferenceEquals(values.Owner, _valuesGate) ? values
                            : source.Current.Original?.ObserverValues is NodeValues originalValues && ReferenceEquals(originalValues.Owner, _valuesGate) ? originalValues : null);
            } while (NextCopiedNode(cursor, compact, ref depth));
            if (source.MoveNext()) return null;
            var errors = new Dictionary<string, CopyError>(StringComparer.Ordinal);
            lock (_valuesGate)
                foreach (var (name, state) in _states)
                {
                    bool missing = false;
                    foreach (var recorded in nodes.Values)
                        if (recorded == null || !recorded.TryGetValue(name, out var phase) || phase.Before == null || phase.After == null)
                        { missing = true; break; }
                    // Complete phases need no later ordinary error or its optional payload.
                    // Missing phases preserve the failure already known when this copy was made.
                    errors.Add(name, new(state.Cycles, missing ? state.Fault : null));
                }
            return new CopiedValues(compact.Document, _valuesGate, nodes, errors);
        }

        private static bool NextCopiedNode(XPathNavigator cursor, XPathNavigator boundary, ref int depth)
        {
            if (cursor.MoveToFirstChild()) { depth++; return true; }
            while (!cursor.IsSamePosition(boundary))
            {
                if (cursor.MoveToNext()) return true;
                if (!cursor.MoveToParent()) return false;
                depth--;
            }
            return false;
        }

        private XsltLibrary? _owner;
        private IList<XPathItem>? _items;
        private readonly StreamedDocument _document;
        private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
        // Frame-owned values are ordinary short-lived objects, not dependent handles in a
        // document-wide weak table. Replay/copy provenance still determines their lifetime.
        // The token also prevents a foreign source frame from supplying another observer's values.
        private readonly object _valuesGate = new();
        private readonly SnapshotCodec _snapshotCodec;

        internal StreamedAccumulators(XsltLibrary owner, StreamedDocument document, IList<XPathItem> items,
            Dictionary<string, AccumulatorInfo> table, HashSet<string> selected)
        {
            _owner = owner;
            _snapshotCodec = new(_valuesGate);
            _document = document;
            _items = items;
            foreach (string name in selected)
            {
                if (!table.TryGetValue(name, out AccumulatorInfo? info))
                    throw new XdmException("XTDE3340", $"There is no accumulator named {name}");
                if (!info.Streamable)
                    throw new XdmException("streaming-unimplemented", $"Accumulator {name} is not declared streamable")
                        { Name = new QName(StreamedDocument.ErrorNamespace, "streaming-unimplemented") };
                _states.Add(name, new(name, info));
            }
        }

        private Phase Values(StreamedDocument.Node node, string name)
        {
            lock (_valuesGate)
            {
                NodeValues values;
                if (node.ObserverValues == null) node.ObserverValues = values = new NodeValues(_valuesGate, _snapshotCodec);
                else if (node.ObserverValues is NodeValues recorded && ReferenceEquals(recorded.Owner, _valuesGate)) values = recorded;
                else throw StreamedDocument.PositionError();
                if (!values.TryGetValue(name, out Phase? phase)) values.Add(name, phase = new());
                return phase;
            }
        }

        private IList<XPathItem>? Recorded(State state, StreamedDocument.Node node, bool after)
        {
            // Rules run only on the original source's calling thread. Fork workers read published
            // phases without creating values, invoking functions or holding a lock across input I/O.
            if (state.Fault is { } cycle && IsAccumulatorCycle(cycle.SourceException)) cycle.Throw();
            lock (_valuesGate)
            {
                if (node.ObserverValues is NodeValues values && ReferenceEquals(values.Owner, _valuesGate)
                    && values.TryGetValue(state.Name, out Phase? phase))
                    if ((after ? phase.After : phase.Before) is { } cached) return cached;
            }
            state.Fault?.Throw();
            return null;
        }

        private static XdmException Circular(State state) => new("XTDE3400", $"The accumulator {state.Name} depends on its own value") { AccumulatorCycle = true };

        private void Initialize(State state)
        {
            if (state.Initialized) return;
            if (state.Initializing) throw Circular(state);
            state.Initializing = true;
            try
            {
                state.Current = AccumulatorCoerce(state.Info,
                    _owner!.CallFunction((FunctionItem)_items![state.Info.Offset], Sequences.One(new StreamedNavigator(_document, _document.Root))), state.Name);
                state.Initialized = true;
            }
            finally { state.Initializing = false; }
        }

        private IList<XPathItem> Before(State state, StreamedDocument.Node node)
        {
            Phase phase = Values(node, state.Name);
            if (state.Fault is { } cycle && IsAccumulatorCycle(cycle.SourceException)) cycle.Throw();
            if (phase.Before != null) return phase.Before;
            state.Fault?.Throw();
            if (_owner == null) throw StreamedDocument.PositionError();
            if (phase.Starting) throw Circular(state);
            phase.Starting = true;
            try
            {
                Initialize(state);
                // Even a phase without rules must initialize and publish its unchanged value.
                var value = state.HasStartRules ? _owner.AccumulatorApply(new StreamedNavigator(_document, node), state.Current!,
                    end: false, _items!, state.Info, state.Name) : state.Current!;
                lock (_valuesGate) { phase.Publish(value, after: false); return state.Current = value; }
            }
            catch (Exception error) when (error is not OperationCanceledException && XmlException.IsCatchableException(error))
            {
                state.Fault ??= ExceptionDispatchInfo.Capture(error);
                state.Current = null;
                throw;
            }
            finally { phase.Starting = false; }
        }

        private IList<XPathItem> After(State state, StreamedDocument.Node node)
        {
            Phase phase = Values(node, state.Name);
            if (state.Fault is { } cycle && IsAccumulatorCycle(cycle.SourceException)) cycle.Throw();
            if (phase.After != null) return phase.After;
            state.Fault?.Throw();
            if (_owner == null) throw StreamedDocument.PositionError();
            if (phase.Ending) throw Circular(state);
            phase.Ending = true;
            try
            {
                Before(state, node);
                var value = state.HasEndRules ? _owner.AccumulatorApply(new StreamedNavigator(_document, node), state.Current!,
                    end: true, _items!, state.Info, state.Name) : state.Current!;
                lock (_valuesGate) { phase.Publish(value, after: true); return state.Current = value; }
            }
            catch (Exception error) when (error is not OperationCanceledException && XmlException.IsCatchableException(error))
            {
                state.Fault ??= ExceptionDispatchInfo.Capture(error);
                state.Current = null;
                throw;
            }
            finally { phase.Ending = false; }
        }

        private void Visit(StreamedDocument.Node node, bool end)
        {
            foreach (State state in _states.Values)
            {
                if (state.Fault != null) continue;
                try
                {
                    if (end) After(state, node);
                    else Before(state, node);
                }
                catch (Exception error) when (error is not OperationCanceledException && XmlException.IsCatchableException(error))
                {
                    // Evaluation may be lazy: a failing accumulator need not prevent use of other
                    // accumulators or the source. Demanding its undefined value rethrows the real error.
                    state.Fault ??= ExceptionDispatchInfo.Capture(error);
                    state.Current = null;
                }
            }
        }

        public void Start(StreamedNavigator node) => Visit(node.PositionNode, end: false);
        public void End(StreamedNavigator node) => Visit(node.PositionNode, end: true);
        void IStreamedNodeObserver.Start(StreamedDocument document, StreamedDocument.Node node) => Visit(node, end: false);
        void IStreamedNodeObserver.End(StreamedDocument document, StreamedDocument.Node node) => Visit(node, end: true);

        internal IList<XPathItem> Value(string name, StreamedNavigator node, bool after)
        {
            if (!_states.TryGetValue(name, out State? state))
                throw new XdmException("XTDE3362", $"The accumulator {name} isn't applicable to the document (use-accumulators)");
            if (node.PositionNode.ObserverValues is NodeValues { Frozen: true } snapshot
                && ReferenceEquals(snapshot.Owner, _valuesGate))
            {
                if (Recorded(state, node.PositionNode, after) is { } recorded) return recorded;
                // A bounded scan can finish while an ancestor is still open. Its missing end
                // phase must be computed through the live source/prong, never on the scratch header.
                var active = node.Document.ActiveHeader(node.PositionNode.Number) ?? throw StreamedDocument.PositionError();
                node = new StreamedNavigator(node.Document, active);
            }
            if (!ReferenceEquals(node.Document, _document))
            {
                StreamedDocument.Node original = node.PositionNode.Original ?? throw StreamedDocument.PositionError();
                if (Recorded(state, original, after) is { } known) return known;
                // Completing the original directly would bypass the tee and lose other prongs' events.
                // A copied, closed view needs only its already-published phases, handled above.
                if (after) node.Document.CompleteSubtree(node.PositionNode);
                return Recorded(state, original, after) ?? throw StreamedDocument.PositionError();
            }
            Phase phase = Values(node.PositionNode, name);
            if (state.Fault is { } cycle && IsAccumulatorCycle(cycle.SourceException)) cycle.Throw();
            if ((after ? phase.After : phase.Before) is { } cached) return cached;
            state.Fault?.Throw();
            if (_owner == null) throw StreamedDocument.PositionError();
            if (after) _document.CompleteSubtree(node.PositionNode);
            return after ? After(state, node.PositionNode) : Before(state, node.PositionNode);
        }

        public void Dispose()
        {
            // Copied nodes may still need the values already stored on their original source frames.
            // They do not need the invocation's functions, global variables or runtime to remain alive.
            _owner = null;
            _items = null;
            foreach (State state in _states.Values) state.Current = null;
        }
    }

    private XPathNavigator ObservePrincipalAccumulators(XPathNavigator source, IList<XPathItem> items, double tableId)
    {
        if (source is not StreamedNavigator streamed) return source;
        var table = (Dictionary<string, AccumulatorInfo>)Constant((int)tableId);
        var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in table) if (pair.Value.OnPrincipal && pair.Value.Streamable) selected.Add(pair.Key);
        if (selected.Count != 0) streamed.Document.Observe(new StreamedAccumulators(this, streamed.Document, items, table, selected));
        return source;
    }

    private XPathNavigator ObserveAccumulators(XPathNavigator source, IList<XPathItem> items, double tableId, string names, int package)
    {
        if (source is not StreamedNavigator streamed) return source;
        var table = (Dictionary<string, AccumulatorInfo>)Constant((int)tableId);
        var selected = new HashSet<string>(StringComparer.Ordinal);
        if (names == "#all")
        {
            foreach (string key in table.Keys)
            {
                var (ns, local) = XsltNameContext.ResolveExpandedName(key, "XTDE3340");
                var name = new QName(ns, local);
                if (table[key].Streamable && Xsl3.Xslt.PackageComponents.Demangle(name.NamespaceUri).Package == package) selected.Add(key);
            }
        }
        else
        {
            foreach (string token in names.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var (ns, local) = XsltNameContext.ResolveExpandedName(token, "XTDE3340");
                var name = new QName(ns, local);
                selected.Add(new QName(Xsl3.Xslt.PackageComponents.MangleNamespace(package, name.NamespaceUri), name.LocalName).EQName);
            }
        }
        if (selected.Count != 0) streamed.Document.Observe(new StreamedAccumulators(this, streamed.Document, items, table, selected));
        return source;
    }
}
