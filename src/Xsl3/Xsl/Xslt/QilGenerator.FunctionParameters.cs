// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Xdm;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Xslt;

internal sealed partial class QilGenerator
{
    private readonly Dictionary<QilNode, (QilIterator Parameter, bool Nodes)> _streamedParameters31 = new();
    private HashSet<QilNode>? _streamingFirstParameters31;

    private SequenceTypeTest? StreamedSequenceParameter31(XslNode function)
    {
        if (!StreamedCompilation31 || function.V2 is not { StreamabilityCategory: "absorbing", Cache: not true, NewEachTime: not "no" }) return null;
        return FunctionTypes31(function)?.Parameters is { Count: > 0 } parameters
            && parameters[0] is { AllowsMany: true, Item: AnyItemTest or NodeKindTest { DocumentElement: null } } parameter ? parameter : null;
    }

    // Replaying this factory in independent fork views has no observable evaluation
    // effects. More general producers need continuation projection rather than replay.
    private static bool ForkableArgument31(XPath31.Expr expression) => expression switch
    {
        XPath31.ContextItemExpr or XPath31.RootExpr => true,
        XPath31.AxisStep { Axis: XPath31.Axis.Child or XPath31.Axis.Attribute or XPath31.Axis.Self or XPath31.Axis.Descendant or XPath31.Axis.DescendantOrSelf, Predicates.Count: 0 } => true,
        XPath31.PathExpr { Left: var left, Right: XPath31.AxisStep { Axis: XPath31.Axis.Child or XPath31.Axis.Attribute or XPath31.Axis.Self or XPath31.Axis.Descendant or XPath31.Axis.DescendantOrSelf, Predicates.Count: 0 } } => ForkableArgument31(left),
        _ => false,
    };

    private QilNode? StreamedParameterValue31(QilNode? value)
    {
        if (value == null || !_streamedParameters31.TryGetValue(value, out var binding)) return value;
        QilIterator parameter = binding.Parameter;
        QilIterator cursor = _f.Let(Stateful31(nameof(XsltLibrary.X31FunctionParameterCursor), T.Item, parameter));
        return XPath31.XPath31Compiler.FunctionParameterPopulation(
            _f.Loop(cursor, PullSequence31(binding.Nodes ? nameof(XsltLibrary.X31FunctionParameterNext) : nameof(XsltLibrary.X31FunctionParameterItemNext),
                binding.Nodes ? T.NodeNotRtfS : T.ItemS, cursor)), parameter,
            X31(nameof(XsltLibrary.X31FunctionParameterOrderedChildren), T.BooleanX, parameter));
    }
}
