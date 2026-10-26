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
    private readonly FunctionItemRegistry? _registry;
    private readonly HashSet<QilFunction> _nodeResults;
    private readonly Dictionary<QilFunction, QilNode> _originals = new();
    private readonly Dictionary<QilFunction, int> _producers = new();
    private readonly HashSet<QilFunction> _pullFunctions = new();

    // Definitions are visited explicitly when their producer is registered. The function list may
    // subsequently reach those new entries; visiting them twice would expand their bodies twice.
    protected override QilNode VisitFunction(QilFunction function) =>
        _pullFunctions.Contains(function) ? function : base.VisitFunction(function);

    private QilNode PullCall(QilInvoke call, bool singleton, SequenceTypeTest parameter)
    {
        var bindings = new List<QilIterator>();
        for (int i = 1; i < call.Arguments.Count; i++) bindings.Add(f.Let(call.Arguments[i]));
        int producer = Producer(call.Function);
        QilNode Expand(QilNode first)
        {
            var signature = new FunctionSignature(null, new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence);
            QilNode item = XPath31Compiler.Invoke(_patterns, nameof(XsltLibrary.X31FunctionNew), T.Item,
                f.LiteralDouble(producer), f.LiteralDouble(ConstantPool.Add(signature)));
            item = Capture(item, first);
            foreach (QilIterator binding in bindings) item = Capture(item, binding);
            QilIterator state = f.Let(XPath31Compiler.Invoke(_patterns, nameof(XsltLibrary.X31PullNew), T.Item, item));
            QilNode next = XPath31Compiler.Invoke(_patterns, nameof(XsltLibrary.X31PullNext), T.ItemS, state);
            OptimizerPatterns.Write(next).AddPattern(OptimizerPatternName.PullSequence);
            OptimizerPatterns.Write(next).AddPattern(OptimizerPatternName.MaybeSideEffects);
            return f.Loop(state, f.TypeAssert(next, T.NodeNotRtfS));
        }
        QilNode result = singleton ? TransmittingSingleton(call, parameter, bindings, Expand) : Expand(call.Arguments[0]);
        for (int i = bindings.Count - 1; i >= 0; i--) result = f.Loop(bindings[i], result);
        result.SourceLine = call.SourceLine;
        return result;
    }

    private QilNode Capture(QilNode item, QilNode value) =>
        XPath31Compiler.Invoke(_patterns, nameof(XsltLibrary.X31FunctionCapture), T.Item, item, f.TypeAssert(value, T.ItemS));

    private int Producer(QilFunction original)
    {
        if (_producers.TryGetValue(original, out int id)) return id;
        QilList formal = f.FormalParameterList();
        var substitutions = new SubstitutionList();
        QilIterator? first = null;
        for (int i = 0; i < original.Arguments.Count; i++)
        {
            QilParameter parameter = f.Parameter(T.ItemS);
            formal.Add(parameter);
            // Capturing the already selected singleton never advances its source. Retain that
            // static fact while cloning so the parameter and result checks can stay incremental.
            if (i == 0) first = f.Let(f.TypeAssert(parameter, T.NodeNotRtf));
            substitutions.AddSubstitutionPair(original.Arguments[i], i == 0 ? first! : parameter);
        }
        formal.Add(f.Parameter(T.ItemS));
        QilFunction function = f.Function(formal, f.True(), T.ItemS);
        function.PullArgument = formal.Count - 1;
        function.DebugName = $"<streamed {original.DebugName}>";
        function.SourceLine = original.SourceLine;
        _pullFunctions.Add(function);
        // Register before rewriting the body: self and mutual calls refer to this producer instead
        // of recursively expanding definitions. Original calls retain their ordinary ABI.
        id = _registry!.Lift(function, original.Arguments.Count, 1);
        _producers.Add(original, id);
        QilNode body = new BodyClone(f, substitutions).Clone(_originals[original]);
        body = new IteratorBindings(f).Rewrite(body);
        _inputs.Push(first!);
        try { body = Visit(body); }
        finally { _inputs.Pop(); }
        body = new IteratorBindings(f).Rewrite(body);
        function.Definition = f.Loop(first!, body);
        return id;
    }
}
