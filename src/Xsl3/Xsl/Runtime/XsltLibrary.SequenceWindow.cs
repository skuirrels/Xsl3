// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Numerics;
using System.Xml.XPath;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class IntegerWindow(BigInteger at) : FunctionItem
    {
        internal readonly BigInteger At = at;
        internal BigInteger Position;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "an exact forward position window";
    }

    public XPathItem X31IntegerWindowNew(XPathItem position)
    {
        XmlQueryRuntime.CheckCancellation();
        return new IntegerWindow(((IntegerValue)position).Integer);
    }

    public bool X31IntegerWindowActive(XPathItem state)
    {
        XmlQueryRuntime.CheckCancellation();
        var window = (IntegerWindow)state;
        return window.Position < window.At;
    }

    public bool X31IntegerWindowAccept(XPathItem state)
    {
        XmlQueryRuntime.CheckCancellation();
        var window = (IntegerWindow)state;
        window.Position += BigInteger.One;
        return window.Position == window.At;
    }

    private sealed class SequenceWindow(double first, double end) : FunctionItem
    {
        internal readonly double First = first, End = end;
        internal BigInteger Position;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a forward subsequence window";
    }

    public XPathItem X31SubsequenceNew(IList<XPathItem> start, IList<XPathItem> length, bool hasLength)
    {
        FunctionDefinition definition = FunctionLibrary.Find(QName.Ns.Fn, "subsequence", hasLength ? 3 : 2)!;
        double first = ((DoubleValue)Coercion.Coerce(start, definition.ParameterType(1), definition, 1)[0]).Double;
        double? count = hasLength ? ((DoubleValue)Coercion.Coerce(length, definition.ParameterType(2), definition, 2)[0]).Double : null;
        var bounds = SequenceFunctions.SubsequenceBounds(first, count);
        return new SequenceWindow(bounds.First, bounds.End);
    }

    public bool X31SubsequenceActive(XPathItem state)
    {
        var window = (SequenceWindow)state;
        return window.First < window.End && (double)(window.Position + BigInteger.One) < window.End;
    }

    public bool X31SubsequenceAccept(XPathItem state)
    {
        var window = (SequenceWindow)state;
        window.Position += BigInteger.One;
        return (double)window.Position >= window.First;
    }
}
