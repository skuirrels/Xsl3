// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class DeepEqualCall(IList<XPathItem> reference, FunctionContext context, NodeKindTest? descendants) : FunctionItem
    {
        internal IList<XPathItem> Reference = reference;
        internal readonly FunctionContext Context = context;
        internal readonly NodeKindTest? Descendants = descendants;
        internal int Position;
        internal bool Equal = true, Ended;
        internal ExceptionDispatchInfo? Fault;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a forward deep-equal comparison";
        internal void Complete() { Ended = true; Reference = Array.Empty<XPathItem>(); }
    }

    public XPathItem X31DeepEqualNew(double context, IList<XPathItem> reference, IList<XPathItem> collation, bool hasCollation, double descendantType)
    {
        XmlQueryRuntime.CheckCancellation();
        FunctionContext comparison = X31ContextFor(context);
        if (hasCollation)
        {
            FunctionDefinition function = FunctionLibrary.Find(QName.Ns.Fn, "deep-equal", 3)!;
            IList<XPathItem> value = Coercion.Coerce(collation, function.ParameterType(2), function, 2);
            comparison = comparison.WithDefaultCollation(FunctionLibrary.Coll(value, comparison));
        }
        foreach (XPathItem item in reference) { DeepEqual.RequireComparable(item); RequireGroundedComparison(item); }
        return new DeepEqualCall(Copy(reference), comparison,
            descendantType < 0 ? null : (NodeKindTest)((SequenceTypeTest)Constant((int)descendantType)).Item!);
    }

    private static void RequireGroundedComparison(XPathItem item)
    {
        XmlQueryRuntime.CheckCancellation();
        switch (item)
        {
            case StreamedNavigator { NodeType: XPathNodeType.Root or XPathNodeType.Element } node when !StreamedDocument.CanReplay(node.PositionNode):
                throw StreamedDocument.PositionError();
            case MapItem map:
                foreach (var entry in map.Entries)
                    foreach (XPathItem value in entry.Value) RequireGroundedComparison(value);
                break;
            case ArrayItem array:
                for (int i = 0; i < array.Count; i++)
                    foreach (XPathItem value in array[i]) RequireGroundedComparison(value);
                break;
        }
    }

    public void X31DeepEqualItem(XPathItem state, XPathItem item)
    {
        var call = (DeepEqualCall)state;
        call.Fault?.Throw();
        if (call.Ended) throw new InvalidOperationException("The deep-equal comparison has ended");
        try
        {
            XmlQueryRuntime.CheckCancellation();
            DeepEqual.RequireComparable(item);
            if (!call.Equal) return;
            if (call.Position == call.Reference.Count) { call.Equal = false; return; }
            XPathItem expected = call.Reference[call.Position++];
            call.Equal = DeepEqual.Items(item, expected, call.Context);
            if (!call.Equal || call.Descendants == null || expected is not XPathNavigator root) return;
            // A successful subtree comparison proves equality of its element descendants.
            // Compare those already-consumed members through the existing grounded operand;
            // no input subtree, source positions or document-wide match list is retained.
            XPathNavigator descendant = root.Clone();
            XPathNodeIterator nested = descendant.SelectDescendants(XPathNodeType.Element, matchSelf: false);
            while (nested.MoveNext())
            {
                XmlQueryRuntime.CheckCancellation();
                if (!call.Descendants.Matches(nested.Current!)) continue;
                if (call.Position == call.Reference.Count || !DeepEqual.Items(nested.Current!, call.Reference[call.Position++], call.Context))
                { call.Equal = false; return; }
            }
        }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        { call.Fault = ExceptionDispatchInfo.Capture(error); call.Complete(); throw; }
    }

    public bool X31DeepEqualEnd(XPathItem state)
    {
        var call = (DeepEqualCall)state;
        call.Fault?.Throw();
        XmlQueryRuntime.CheckCancellation();
        if (!call.Ended) { call.Equal &= call.Position == call.Reference.Count; call.Complete(); }
        return call.Equal;
    }

    public bool X31DeepEqualRun(XPathItem state, XPathItem producer)
    {
        var call = (DeepEqualCall)state;
        call.Fault?.Throw();
        if (call.Ended) return call.Equal;
        var cursor = new PullState(this, (FunctionItem)producer);
        try
        {
            while (true)
            {
                IList<XPathItem> next = X31PullNext(cursor);
                if (next.Count == 0) break;
                if (next.Count != 1) throw new InvalidOperationException("A comparison population must yield at most one item");
                X31DeepEqualItem(call, next[0]);
            }
            return X31DeepEqualEnd(call);
        }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        { call.Fault = ExceptionDispatchInfo.Capture(error); call.Complete(); throw; }
        finally { cursor.Complete(); }
    }
}
