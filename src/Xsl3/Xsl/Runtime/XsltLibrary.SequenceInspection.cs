// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Numerics;
using System.Xml.XPath;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class BooleanInspection : FunctionItem
    {
        internal XPathItem? First;
        internal bool Result, Complete;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a forward effective-boolean-value inspection";
    }

    public XPathItem X31EbvNew()
    {
        XmlQueryRuntime.CheckCancellation();
        return new BooleanInspection();
    }
    public bool X31EbvActive(XPathItem state) => !((BooleanInspection)state).Complete;
    public int X31EbvItem(XPathItem state, XPathItem item)
    {
        XmlQueryRuntime.CheckCancellation();
        var inspection = (BooleanInspection)state;
        if (inspection.First != null)
            Sequences.EffectiveBooleanValue(new[] { inspection.First, item }); // canonical cardinality error
        inspection.Result = Sequences.EffectiveBooleanValue(new[] { item });
        inspection.Complete = item.IsNode;
        if (!inspection.Complete) inspection.First = item;
        return 0;
    }
    public bool X31EbvResult(XPathItem state)
    {
        XmlQueryRuntime.CheckCancellation();
        return ((BooleanInspection)state).Result;
    }

    private sealed class RemovalWindow(BigInteger excluded) : FunctionItem
    {
        internal readonly BigInteger Excluded = excluded;
        internal BigInteger Position;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a forward removal window";
    }
    public XPathItem X31RemoveNew(IList<XPathItem> position)
    {
        XmlQueryRuntime.CheckCancellation();
        return new RemovalWindow(SequencePosition("remove", 2, position));
    }
    public bool X31RemoveAccept(XPathItem state)
    {
        XmlQueryRuntime.CheckCancellation();
        var window = (RemovalWindow)state;
        window.Position += BigInteger.One;
        return window.Position != window.Excluded;
    }
}
