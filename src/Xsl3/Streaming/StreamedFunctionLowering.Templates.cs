// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xsl3.Xdm;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Streaming;

internal sealed partial class StreamedFunctionLowering
{
    private readonly HashSet<QilFunction> _templates;
    private readonly System.Func<QilNode, XmlQueryType, QilNode>? _unbox;
    private readonly System.Action<QilFunction, QilFunction>? _copyFrame;

    private QilNode TemplatePullCall(QilInvoke call)
    {
        int producer = TemplateProducer(call.Function);
        var signature = new FunctionSignature(null, new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence);
        QilNode item = XPath31Compiler.Invoke(_patterns, nameof(XsltLibrary.X31FunctionNew), T.Item,
            f.LiteralDouble(producer), f.LiteralDouble(ConstantPool.Add(signature)));
        foreach (QilNode argument in call.Arguments)
            item = Capture(item, XPath31Compiler.Box(argument, _patterns));
        QilIterator state = f.Let(XPath31Compiler.Invoke(_patterns, nameof(XsltLibrary.X31PullNew), T.Item, item));
        // The cursor is invocation-owned state, even when its producer has no context or arguments.
        // Otherwise an inlined let can allocate a fresh cursor on every resumed pull.
        OptimizerPatterns.Write(state.Binding!).AddPattern(OptimizerPatternName.MaybeSideEffects);
        QilNode next = XPath31Compiler.Invoke(_patterns, nameof(XsltLibrary.X31PullNext), T.ItemS, state);
        OptimizerPatterns.Write(next).AddPattern(OptimizerPatternName.PullSequence);
        OptimizerPatterns.Write(next).AddPattern(OptimizerPatternName.MaybeSideEffects);
        QilNode result = f.Loop(state, next);
        result.SourceLine = call.SourceLine;
        return result;
    }

    private int TemplateProducer(QilFunction original)
    {
        if (_producers.TryGetValue(original, out int id)) return id;
        QilList formal = f.FormalParameterList();
        var substitutions = new SubstitutionList();
        var bindings = new List<QilIterator>();
        foreach (QilNode argument in original.Arguments)
        {
            QilParameter parameter = f.Parameter(T.ItemS);
            formal.Add(parameter);
            QilIterator binding = f.Let(_unbox!(parameter, argument.XmlType!));
            bindings.Add(binding);
            substitutions.AddSubstitutionPair(argument, binding);
        }
        formal.Add(f.Parameter(T.ItemS));
        QilFunction function = f.Function(formal, f.True(), T.ItemS);
        function.PullArgument = formal.Count - 1;
        function.DebugName = $"<streamed template {original.DebugName}>";
        function.SourceLine = original.SourceLine;
        _pullFunctions.Add(function);
        id = _registry!.Lift(function, original.Arguments.Count, 1);
        _producers.Add(original, id);
        _copyFrame?.Invoke(original, function);
        QilNode body = new BodyClone(f, substitutions).Clone(_originals[original]);
        body = new IteratorChoices(f).Rewrite(body);
        body = Visit(body);
        for (int i = bindings.Count - 1; i >= 0; i--) body = f.Loop(bindings[i], body);
        function.Definition = body;
        return id;
    }
    // Choice's legacy backend writes a complete branch before returning its cache.
    // Pull companions need value branches; keep one selector evaluation and a
    // balanced branch tree rather than growing a linear/exponential expression.
    private sealed class IteratorChoices(QilFactory factory) : QilReplaceVisitor(factory)
    {
        internal QilNode Rewrite(QilNode node) => VisitAssumeReference(node);

        protected override QilNode VisitChoice(QilChoice choice)
        {
            base.VisitChoice(choice);
            if (choice.Branches.Count == 0) return choice;
            QilIterator selector = f.Let(choice.Expression);
            int regular = choice.Branches.Count - 1;
            QilNode Branch(int first, int end)
            {
                if (end - first == 1) return choice.Branches[first];
                int middle = first + (end - first) / 2;
                return f.Conditional(f.Lt(selector, f.LiteralInt32(middle)), Branch(first, middle), Branch(middle, end));
            }
            QilNode result = choice.Branches[regular];
            if (regular != 0)
                result = f.Conditional(f.And(f.Ge(selector, f.LiteralInt32(0)), f.Lt(selector, f.LiteralInt32(regular))),
                    Branch(0, regular), result);
            else
                // Even a one-branch choice evaluates its selector, including faults.
                OptimizerPatterns.Write(selector.Binding!).AddPattern(OptimizerPatternName.MaybeSideEffects);
            result = f.Loop(selector, result);
            result.SourceLine = choice.SourceLine;
            return result;
        }
    }

}
