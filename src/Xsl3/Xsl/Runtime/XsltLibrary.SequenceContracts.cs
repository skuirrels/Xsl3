// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Numerics;
using System.Xml.XPath;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class CardinalityAssertion(string function) : FunctionItem
    {
        internal readonly string Function = function;
        internal bool Seen;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a forward cardinality assertion";
    }

    public XPathItem X31CardinalityNew(string function)
    {
        XmlQueryRuntime.CheckCancellation();
        return new CardinalityAssertion(function);
    }

    public bool X31CardinalityAccept(XPathItem state)
    {
        XmlQueryRuntime.CheckCancellation();
        var assertion = (CardinalityAssertion)state;
        if (assertion.Seen) SequenceFunctions.CheckCardinality(assertion.Function, 2);
        assertion.Seen = true;
        return true;
    }

    public void X31CardinalityEnd(XPathItem state)
    {
        XmlQueryRuntime.CheckCancellation();
        var assertion = (CardinalityAssertion)state;
        SequenceFunctions.CheckCardinality(assertion.Function, assertion.Seen ? 1 : 0);
    }

    private sealed class InsertionWindow(BigInteger position) : FunctionItem
    {
        internal readonly BigInteger At = position;
        internal BigInteger Position;
        internal bool Inserted;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a forward insertion window";
    }

    private static BigInteger SequencePosition(string function, int arity, IList<XPathItem> position)
    {
        FunctionDefinition definition = FunctionLibrary.Find(QName.Ns.Fn, function, arity)!;
        return ((IntegerValue)Coercion.Coerce(position, definition.ParameterType(1), definition, 1)[0]).Integer;
    }

    public XPathItem X31InsertNew(IList<XPathItem> position)
    {
        XmlQueryRuntime.CheckCancellation();
        return new InsertionWindow(SequencePosition("insert-before", 3, position));
    }

    public IList<XPathNavigator> X31InsertValidate(IList<XPathItem> position)
    {
        XmlQueryRuntime.CheckCancellation();
        _ = SequencePosition("insert-before", 3, position);
        return System.Array.Empty<XPathNavigator>();
    }

    public bool X31InsertBefore(XPathItem state)
    {
        XmlQueryRuntime.CheckCancellation();
        var window = (InsertionWindow)state;
        window.Position += BigInteger.One;
        if (window.Inserted || window.Position < window.At) return false;
        window.Inserted = true;
        return true;
    }

    public bool X31InsertTail(XPathItem state)
    {
        XmlQueryRuntime.CheckCancellation();
        var window = (InsertionWindow)state;
        if (window.Inserted) return false;
        window.Inserted = true;
        return true;
    }
}
