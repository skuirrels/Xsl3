// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class MaterializedMerge(List<List<MergeEntry>> groups, int sources) : FunctionItem
    {
        internal List<MergeEntry>?[] Groups = groups.ToArray();
        internal readonly int Count = groups.Count, Sources = sources;
        internal int Position;
        internal ExceptionDispatchInfo? Failure;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a materialized merge group cursor";
        internal void Release()
        {
            Array.Clear(Groups);
            Groups = Array.Empty<List<MergeEntry>?>();
            Position = Count;
        }
    }

    /// <summary>Prepare groups and their total count, without evaluating or retaining action results.</summary>
    public XPathItem X31MaterializedMergeNew(IList<XPathItem> sources, IList<XPathItem> keyFunctions, XPathItem plan)
    {
        XmlQueryRuntime.CheckCancellation();
        var spec = (MergePlan)plan;
        return new MaterializedMerge(PrepareMergeGroups(sources, keyFunctions, Sequences.Empty, spec.Spec, spec), sources.Count);
    }

    public double X31MaterializedMergeSize(XPathItem state)
    {
        var merge = (MaterializedMerge)state;
        merge.Failure?.Throw();
        XmlQueryRuntime.CheckCancellation();
        return merge.Count;
    }

    public IList<XPathItem> X31MaterializedMergeNextGroup(XPathItem state)
    {
        var merge = (MaterializedMerge)state;
        merge.Failure?.Throw();
        try
        {
            XmlQueryRuntime.CheckCancellation();
            if (merge.Position == merge.Count) return Sequences.Empty;
            var group = merge.Groups[merge.Position]!;
            merge.Groups[merge.Position++] = null;
            // The frame owns only this group. Earlier groups and their action output
            // are not retained by the cursor; the total remains available for last().
            var items = group.Select(entry => entry.Item).ToArray();
            var key = group[0].RawKey.SelectMany(part => part).ToArray();
            var bySource = new IList<XPathItem>[merge.Sources];
            for (int i = 0; i < bySource.Length; i++)
            {
                int source = i;
                bySource[i] = group.Where(entry => entry.Source == source).Select(entry => entry.Item).ToArray();
            }
            if (merge.Position == merge.Count) merge.Release();
            return Sequences.One(new MergeGroup(items, key, Sequences.One(ArrayItem.Of(bySource))));
        }
        catch (Exception error) when (System.Xml.XmlException.IsCatchableException(error))
        {
            merge.Failure = ExceptionDispatchInfo.Capture(error);
            merge.Release();
            throw;
        }
    }
}
