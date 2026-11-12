// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class MergeInput(XsltLibrary owner, XPathItem producer, int source, int anchor, XPathNavigator? document) : FunctionItem
    {
        internal readonly XsltLibrary Owner = owner;
        internal readonly XPathItem Producer = producer;
        internal readonly int Source = source, Anchor = anchor;
        internal XPathNavigator? Document = document;
        internal MergeEntry? Head;
        private List<MergeEntry>? _sorted;
        private int _sortedPosition;
        private bool _prepared;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a merge input iterator";

        internal void Advance(MergePlan plan)
        {
            if (plan.Spec.SortBeforeMerge[Source])
            {
                if (!_prepared)
                {
                    _prepared = true;
                    var entries = new List<MergeEntry>();
                    for (IList<XPathItem> batch = Owner.X31PullNext(Producer); batch.Count != 0; batch = Owner.X31PullNext(Producer))
                        entries.Add((MergeEntry)batch[0]);
                    _sorted = Owner.SortMergeInput(entries, plan.Spec);
                }
                Head = _sortedPosition < _sorted!.Count ? _sorted[_sortedPosition++] : null;
                if (Head == null) { _sorted = null; CloseDocument(); }
                return;
            }
            IList<XPathItem>[]? previous = Head?.Key;
            IList<XPathItem> next = Owner.X31PullNext(Producer);
            Head = next.Count == 0 ? null : (MergeEntry)next[0];
            if (Head == null)
            {
                CloseDocument();
                return;
            }
            if (previous == null) return;
            if (Owner.CompareMergeInputKeys(previous, Head.Key, plan.Spec) > 0)
                throw new XdmException("XTDE2220", "A merge input sequence isn't in the order of its merge keys");
        }

        private void CloseDocument()
        {
            if (Document != null) Owner.X31CloseStreamedSource(Document);
            Document = null;
        }
    }

    private sealed class ForwardMerge : FunctionItem
    {
        internal readonly XsltLibrary Owner;
        internal readonly List<MergeInput> Inputs;
        internal readonly MergePlan Plan;
        internal readonly int SourceCount;
        internal readonly PriorityQueue<MergeInput, MergeInput> Heads;
        internal bool Initialized;
        internal ForwardMerge(XsltLibrary owner, IList<XPathItem> inputs, MergePlan plan)
        {
            Owner = owner; Inputs = inputs.Cast<MergeInput>().ToList(); Plan = plan;
            SourceCount = plan.Spec.SortBeforeMerge.Count;
            Heads = new(Comparer<MergeInput>.Create((a, b) =>
            {
                int order = Compare(a.Head!.Key, b.Head!.Key);
                if (order != 0) return order;
                order = a.Source.CompareTo(b.Source);
                return order != 0 ? order : a.Anchor.CompareTo(b.Anchor);
            }));
        }
        internal int Compare(IList<XPathItem>[] a, IList<XPathItem>[] b) => Owner.CompareMergeKeys(a, b, Plan.Spec);
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a k-way merge cursor";
    }

    private sealed class MergeGroup : FunctionItem
    {
        internal readonly IList<XPathItem> Items, Key;
        private readonly int[]? _sourceCounts;
        private IList<XPathItem>? _sources;

        internal MergeGroup(IList<XPathItem> items, IList<XPathItem> key, IList<XPathItem> sources)
        {
            Items = items; Key = key; _sources = sources;
        }

        internal MergeGroup(IList<XPathItem> items, IList<XPathItem> key, int[] sourceCounts)
        {
            Items = items; Key = key; _sourceCounts = sourceCounts;
        }

        internal IList<XPathItem> Sources
        {
            get
            {
                if (System.Threading.Volatile.Read(ref _sources) is { } cached) return cached;
                // Equal-key rows are contiguous by source: the heap breaks ties by
                // source, then anchor. Preserve the same grounded node references;
                // no source is advanced when a named group is first requested.
                var bySource = new IList<XPathItem>[_sourceCounts!.Length];
                int offset = 0;
                for (int source = 0; source < bySource.Length; source++)
                {
                    int count = _sourceCounts[source];
                    if (count == 0) bySource[source] = Sequences.Empty;
                    else
                    {
                        var members = new XPathItem[count];
                        for (int i = 0; i < count; i++) members[i] = Items[offset++];
                        bySource[source] = members;
                    }
                }
                IList<XPathItem> sources = Sequences.One(ArrayItem.Of(bySource));
                return System.Threading.Interlocked.CompareExchange(ref _sources, sources, null) ?? sources;
            }
        }
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "the current merge group";
    }

    public XPathItem X31MergeRowNew(XPathItem item, XPathItem keyFunction, XPathItem plan, double source, bool snapshot)
    {
        if (snapshot) item = X31Snapshot(Sequences.One(item), _runtime.SchemaAware)[0];
        else if (ForwardGroupMember(item)) throw StreamedDocument.PositionError();
        else if (item is XPathNavigator node) item = node.Clone();
        IList<XPathItem>[] raw = MergeRawKeys(CallFunction((FunctionItem)keyFunction, Sequences.One(item)));
        return new MergeEntry(MergeSortKeys(raw, (MergePlan)plan), raw, (int)source, item);
    }

    public XPathItem X31MergeInputNew(XPathItem producer, double source, double anchor, IList<XPathItem> document) =>
        new MergeInput(this, producer, (int)source, (int)anchor, document.Count == 0 ? null : (XPathNavigator)document[0]);

    public XPathItem X31ForwardMergeNew(IList<XPathItem> inputs, XPathItem plan) => new ForwardMerge(this, inputs, (MergePlan)plan);

    public IList<XPathItem> X31MergeNextGroup(XPathItem state)
    {
        var merge = (ForwardMerge)state;
        if (!merge.Initialized)
        {
            merge.Initialized = true;
            foreach (MergeInput input in merge.Inputs)
            {
                input.Advance(merge.Plan);
                if (input.Head != null) merge.Heads.Enqueue(input, input);
            }
        }
        if (!merge.Heads.TryPeek(out MergeInput? first, out _)) return Sequences.Empty;
        IList<XPathItem>[] key = first.Head!.Key;
        IList<XPathItem> rawKey = first.Head.RawKey.SelectMany(part => part).ToArray();
        var items = new List<XPathItem>();
        var sourceCounts = new int[merge.SourceCount];
        while (merge.Heads.TryPeek(out MergeInput? input, out _) && merge.Compare(key, input.Head!.Key) == 0)
        {
            merge.Heads.Dequeue();
            do
            {
                XPathItem item = input.Head!.Item;
                items.Add(item); sourceCounts[input.Source]++;
                input.Advance(merge.Plan);
            }
            while (input.Head != null && merge.Compare(key, input.Head.Key) == 0);
            if (input.Head != null) merge.Heads.Enqueue(input, input);
        }
        return Sequences.One(new MergeGroup(items, rawKey, sourceCounts));
    }

    public XPathItem X31MergeGroupFirst(XPathItem group) => ((MergeGroup)group).Items[0];
    public IList<XPathItem> X31MergeGroupItems(XPathItem group) => ((MergeGroup)group).Items;
    public IList<XPathItem> X31MergeGroupKey(XPathItem group) => ((MergeGroup)group).Key;
    public IList<XPathItem> X31MergeGroupSources(XPathItem group) => ((MergeGroup)group).Sources;
}
