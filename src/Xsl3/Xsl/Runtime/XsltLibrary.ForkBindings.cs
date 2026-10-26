// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    internal sealed class PreparedForkBindings(IList<XPathItem> values, IReadOnlyList<StreamedNavigator> sources, bool serialization = false, bool bufferNodes = false)
    {
        internal readonly IList<XPathItem> Values = values;
        // Invocation-scoped candidates supply the streamed operand of a fork in a
        // stylesheet function, whose dynamic context item is absent.
        internal readonly IReadOnlyList<StreamedNavigator> Sources = sources;
        internal readonly bool Serialization = serialization;
        internal readonly bool BufferNodes = bufferNodes;
    }

    // This token is confined to a prepared graph; it is replaced with a navigator before
    // any user body executes. It carries immutable headers, never a live source cursor.
    private sealed class CapturedNode : AtomicValue
    {
        internal StreamedNavigator.FrozenPosition Position = null!;
        internal CapturedNode() : base(AtomicTypes.String) { }
        public override string Value => throw new InvalidOperationException("A prepared fork binding cannot be evaluated");
        public override object ClrValue => throw new InvalidOperationException("A prepared fork binding cannot be evaluated");
    }

    private List<FunctionParameter>? _projectedForkParameters;
    private sealed record ForkParameterSummary(FunctionParameter Original, long Count, PreparedForkBindings? First);

    private List<ForkParameterSummary>? CompleteForkParameters()
    {
        List<FunctionParameter>? parameters = _projectedForkParameters;
        _projectedForkParameters = null;
        if (parameters == null) return null;
        var summaries = new List<ForkParameterSummary>();
        foreach (FunctionParameter parameter in parameters)
        {
            parameter.Finish();
            summaries.Add(new(parameter.ForkOrigin!, parameter.ReadCount, parameter.FirstHeader is { } first
                ? PrepareForkBindings(new XPathItem[] { first }) : null));
        }
        return summaries;
    }

    // Invoke on the caller before starting prongs. Copying the full graph here freezes
    // cursor positions, collection contents and built-in contexts at one execution point.
    internal PreparedForkBindings PrepareForkBindings(IList<XPathItem> bindings) => PrepareForkValues(bindings, bufferNodes: false);

    internal PreparedForkBindings PrepareForkOutput(IList<XPathItem> items) => PrepareForkValues(items, bufferNodes: true, serialization: true);

    internal PreparedForkBindings PrepareForkResult(IList<XPathItem> items) => PrepareForkValues(items, bufferNodes: true);

    private PreparedForkBindings PrepareForkValues(IList<XPathItem> bindings, bool bufferNodes, bool serialization = false)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        XmlQueryRuntime.CheckCancellation();
        var capture = new ForkBindings(_runtime, null, new StreamedDocument.ForkCapture(), bufferNodes, serialization);
        IList<XPathItem> values = capture.Sequence(bindings);
        capture.Complete();
        capture.Sources.RemoveAll(source => !source.CanFork);
        return new(values, capture.Sources.ToArray(), serialization, bufferNodes);
    }

    internal IList<XPathItem> ProjectForkBindings(PreparedForkBindings bindings, StreamedNavigator focus)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return ProjectForkBindings(bindings.Values, focus);
    }

    internal IList<XPathItem> ProjectForkBindings(PreparedForkBindings bindings)
    {
        var projection = new ForkBindings(_runtime, null, bufferNodes: bindings.BufferNodes, serialization: bindings.Serialization);
        IList<XPathItem> values = projection.Sequence(bindings.Values);
        projection.Complete();
        return values;
    }

    // A fork invocation projects its captured graph once per prong, before invoking any
    // user code. No cache outlives that invocation or retains a streamed subtree.
    internal IList<XPathItem> ProjectForkBindings(IList<XPathItem> bindings, StreamedNavigator focus)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(focus);
        XmlQueryRuntime.CheckCancellation();
        var projection = new ForkBindings(_runtime, focus.Document);
        IList<XPathItem> result = projection.Sequence(bindings);
        projection.Complete();
        return result;
    }

    private sealed class ForkBindings(XmlQueryRuntime runtime, StreamedDocument? document, StreamedDocument.ForkCapture? capture = null, bool bufferNodes = false, bool serialization = false)
    {
        private sealed class Entry
        {
            internal XPathItem? Value;
            internal readonly List<Action<XPathItem>> Waiting = new();
        }

        private readonly Dictionary<XPathItem, Entry> _items = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<IList<XPathItem>, XPathItem[]> _sequences = new(ReferenceEqualityComparer.Instance);
        private readonly Queue<Action> _work = new();
        private readonly List<Action> _finishing = new();
        private readonly List<Action> _freezing = new();
        private readonly StreamedDocument.ForkViews _views = new(XmlQueryRuntime.Cancellation);
        internal readonly List<StreamedNavigator> Sources = new();

        // Allocate sequence shells before visiting their contents: closures may refer back
        // to themselves through a captured map/array. Work is queued rather than recursive
        // so deeply nested values and tunnel chains do not use the native call stack.
        internal IList<XPathItem> Sequence(IList<XPathItem> value)
        {
            if (_sequences.TryGetValue(value, out XPathItem[]? existing)) return existing;
            var result = new XPathItem[value.Count];
            _sequences.Add(value, result);
            for (int i = 0; i < result.Length; i++)
            {
                XmlQueryRuntime.CheckCancellation();
                int index = i;
                XPathItem item = value[i];
                _work.Enqueue(() => Visit(item, projected => result[index] = projected));
            }
            return result;
        }

        internal void Complete()
        {
            while (_work.TryDequeue(out Action? action))
            {
                XmlQueryRuntime.CheckCancellation();
                action();
            }
            foreach (Entry entry in _items.Values)
                if (entry.Value == null) throw new InvalidOperationException("A fork binding contains an unresolved value cycle");
            foreach (Action finish in _finishing)
            {
                XmlQueryRuntime.CheckCancellation();
                finish();
            }
            foreach (Action freeze in _freezing)
            {
                XmlQueryRuntime.CheckCancellation();
                freeze();
            }
        }

        private void Visit(XPathItem item, Action<XPathItem> accept)
        {
            // Navigator positions are always private, including aliases in different
            // sequence slots. Identity belongs to the node, not to its mutable cursor.
            if (item is XPathNavigator node)
            {
                if (node is StreamedNavigator source && capture != null)
                {
                    if (bufferNodes) source.BufferForFork();
                    // A later binding can explicitly buffer current-group(). Freeze the
                    // position now, then its replay metadata after the graph is prepared.
                    var position = (StreamedNavigator)source.Clone();
                    if (position.NodeType is XPathNodeType.Root or XPathNodeType.Element) Sources.Add(position);
                    var captured = new CapturedNode();
                    _freezing.Add(() => captured.Position = position.FreezeForkPosition(capture, XmlQueryRuntime.Cancellation));
                    accept(captured);
                }
                else accept(node is StreamedNavigator streamed ? streamed.InFork(document!) : node.Clone());
                return;
            }
            if (item is CapturedNode frozen)
            {
                accept(capture != null ? frozen : frozen.Position.InFork(document!, _views));
                return;
            }
            if (_items.TryGetValue(item, out Entry? entry))
            {
                if (entry.Value is { } value) accept(value);
                else entry.Waiting.Add(accept);
                return;
            }
            _items.Add(item, entry = new());
            entry.Waiting.Add(accept);
            Build(item);
        }

        private void Publish(XPathItem source, XPathItem value)
        {
            Entry entry = _items[source];
            entry.Value = value;
            foreach (Action<XPathItem> accept in entry.Waiting) _work.Enqueue(() => accept(value));
            entry.Waiting.Clear();
        }

        private void Later(XPathItem item, Action<XPathItem> accept) => _work.Enqueue(() => Visit(item, accept));

        private void Build(XPathItem item)
        {
            switch (item)
            {
                case FunctionArgument argument:
                    if (argument.Source.Position != 0 || argument.Source.Slots != null)
                        throw new XdmException("streaming-unimplemented", "A started streamed argument requires continuation projection")
                            { Name = new QName(StreamedDocument.ErrorNamespace, "streaming-unimplemented") };
                    Later(argument.Source.Producer!, producer => Publish(item,
                        new FunctionArgument(runtime.XsltFunctions, (FunctionItem)producer, argument.OrderedChildren, argument.Forkable)));
                    break;
                case FunctionParameter streamedParameter:
                    if (streamedParameter.GroundedItems is { } grounded)
                        Publish(item, new FunctionParameter(Sequence(grounded), streamedParameter.RequiredType, prepared: true));
                    else Later(streamedParameter.ForkArgument, projected => {
                        var copy = new FunctionParameter(new[] { projected }, streamedParameter.RequiredType) { ForkOrigin = capture != null ? streamedParameter : streamedParameter.ForkOrigin ?? streamedParameter };
                        if (capture == null) (runtime.XsltFunctions._projectedForkParameters ??= new()).Add(copy);
                        Publish(item, copy);
                    });
                    break;
                case MapItem map:
                    var entries = new List<KeyValuePair<AtomicValue, IList<XPathItem>>>(map.Count);
                    foreach (var pair in map.Entries)
                    {
                        XmlQueryRuntime.CheckCancellation();
                        entries.Add(new(pair.Key, Sequence(pair.Value)));
                    }
                    Publish(item, MapItem.Of(entries));
                    break;
                case ArrayItem array:
                    var members = new IList<XPathItem>[array.Count];
                    for (int i = 0; i < members.Length; i++)
                    {
                        XmlQueryRuntime.CheckCancellation();
                        members[i] = Sequence(array[i]);
                    }
                    Publish(item, ArrayItem.Of(members));
                    break;
                // Serialization describes a function item; it does not invoke its
                // closure. Maps and arrays above still freeze their item contents.
                case FunctionItem when serialization:
                    Publish(item, item);
                    break;
                case CompiledFunction function when runtime.SharesForkContext(function.Owner):
                    var closure = new CompiledFunction(function.Id, function.Signature, runtime);
                    foreach (IList<XPathItem> capture in function.Captured) closure.Captured.Add(Sequence(capture));
                    Publish(item, closure);
                    break;
                case LibraryFunction function when function.Context.Runtime is { } owner && runtime.SharesForkContext(owner):
                    Publish(item, new LibraryFunction(function.Definition, function.Arity, function.Context.WithRuntime(runtime)));
                    break;
                case CoercedFunction function:
                    Later(function.Target, target => Publish(item, function.WithTarget((FunctionItem)target)));
                    break;
                case TunnelFrame frame:
                    IList<XPathItem> parameter = Sequence(frame.ParamValue);
                    if (frame.Outer == null) Publish(item, new TunnelFrame(null, frame.Ns, frame.Local, parameter));
                    else Later(frame.Outer, outer => Publish(item, new TunnelFrame((TunnelFrame)outer, frame.Ns, frame.Local, parameter)));
                    break;
                case DeferredItem deferred:
                    Later(deferred.Body, body => Publish(item, new DeferredItem(deferred.OnEmpty, (FunctionItem)body)));
                    break;
                case GroupTuple tuple:
                    if (tuple.KeyString is { } keyString)
                        Later(tuple.Item, member => Publish(item, new GroupTuple(member, keyString)));
                    else
                    {
                        IList<XPathItem> key = Sequence(tuple.Key);
                        Later(tuple.Item, member => Publish(item, new GroupTuple(member, key)));
                    }
                    break;
                case Group group:
                    var copy = new Group(group.Key == null ? null : Sequence(group.Key));
                    IList<XPathItem> values = Sequence(group.Members);
                    Publish(item, copy);
                    // Fill after every item assignment; do not retain the parent's lazy
                    // node-sequence cache or its mutable navigator positions.
                    _finishing.Add(() => CopyMembers(copy, values));
                    break;
                case ForwardGroup forward when capture != null:
                    Group buffered = runtime.XsltFunctions.BufferForkGroup(forward);
                    var grouped = new Group(buffered.Key == null ? null : Sequence(buffered.Key));
                    IList<XPathItem> groupedValues = Sequence(buffered.Members);
                    Publish(item, grouped);
                    _finishing.Add(() => CopyMembers(grouped, groupedValues));
                    break;
                case MergeGroup group:
                    Publish(item, new MergeGroup(Sequence(group.Items), Sequence(group.Key), Sequence(group.Sources)));
                    break;
                // These are immutable XDM values. Native functions are the random-number
                // generator's closures over immutable numeric state, never runtime delegates.
                case StringValue or BooleanValue or IntegerValue or DecimalValue or DoubleValue or FloatValue
                    or DurationValue or DateTimeValue or BinaryValue or QNameValue or NativeFunction
                    or Segment or AbsentItem:
                    Publish(item, item);
                    break;
                default:
                    throw new InvalidOperationException($"Fork binding projection does not support {item.GetType().Name}");
            }
        }

        private static void CopyMembers(Group target, IList<XPathItem> members)
        {
            for (int i = 0; i < members.Count; i++)
            {
                XmlQueryRuntime.CheckCancellation();
                target.Members.Add(members[i]);
            }
        }
    }
}
