// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Xdm;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31;

internal sealed partial class XPath31Compiler
{
    // Node/item function conversions and treat-as use the same streaming matcher.
    // Their distinct error codes remain explicit at the original call sites.
    internal static QilNode TypeCheckedPopulation(QilNode source, QilNode type, QilNode errorCode, QilPatternFactory factory)
    {
        QilNode Call(string method, XmlQueryType resultType, params QilNode[] arguments)
        {
            QilNode call = Invoke(factory, method, resultType, arguments);
            Xsl3.IlGen.OptimizerPatterns.Write(call).AddPattern(Xsl3.IlGen.OptimizerPatternName.MaybeSideEffects);
            return call;
        }
        QilIterator state = factory.Let(Call(nameof(XsltLibrary.X31TypeTestNew), T.Item, type, errorCode));
        QilIterator item = factory.For(source);
        QilNode transmitted = factory.Filter(item, Call(nameof(XsltLibrary.X31TypeTestAccept), T.BooleanX, state, item));
        QilNode result = factory.Loop(state, factory.Sequence(transmitted, Call(nameof(XsltLibrary.X31TreatEnd), T.Empty, state)));
        return source.XmlType!.IsSubtypeOf(T.NodeNotRtfS) ? factory.TypeAssert(result, source.XmlType) : result;
    }

    private QilNode ForwardTypeTest(Expr operand, SequenceType required, bool assertion)
    {
        QilNode source = Items(Build(operand));
        var type = _types.Resolve(required);
        QilNode id = _f.Double(ConstantPool.Add(type));
        if (!assertion)
        {
            QilIterator state = _f.Let(StatefulCall(nameof(XsltLibrary.X31TypeTestNew), T.Item, id, _f.String("")));
            QilIterator item = _f.For(source);
            // Consume every producer item, including late source errors, without retaining
            // its nodes. The state short-circuits only the subsequent item-type comparisons.
            QilNode inspect = _f.Loop(item, StatefulCall(nameof(XsltLibrary.X31TypeTestInspect), T.Empty, state, item));
            return _f.Loop(state, _f.Sequence(inspect, StatefulCall(nameof(XsltLibrary.X31TypeTestResult), T.BooleanX, state)));
        }
        QilNode result = TypeCheckedPopulation(source, id, _f.String("XPDY0050"), _f);
        if (!source.XmlType!.IsSubtypeOf(T.NodeNotRtfS) && type.Item is NodeKindTest)
            result = _f.TypeAssert(result, type.AllowsMany ? T.NodeNotRtfS : type.AllowsEmpty ? T.NodeNotRtfQ : T.NodeNotRtf);
        else if (type.Item is AtomicTypeTest or NumericTest or UnionTypeTest)
            s_atomicItems.AddOrUpdate(result, AtomicTypes.AnyAtomicType);
        return PreserveForwardSequenceProofs(source, result, !type.AllowsMany || DisjointChildAnchors(operand));
    }
}
