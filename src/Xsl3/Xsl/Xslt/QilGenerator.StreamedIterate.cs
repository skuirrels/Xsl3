// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xsl3.Xdm;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Xslt;

internal sealed partial class QilGenerator
{
    private readonly Dictionary<VarPar, SequenceTypeTest> _iterateParameterTypes31 = new();

    // First called in the parameter declaration's scope while compiling its initial value. Later
    // next-iteration callers must use that resolved type, regardless of their namespace rebindings.
    private QilNode CoerceIterateParameter31(QilNode value, VarPar parameter)
    {
        if (parameter.V2?.As is not string type) return value;
        try
        {
            XPathStaticContext sc = StaticContext31();
            if (!_iterateParameterTypes31.TryGetValue(parameter, out SequenceTypeTest? test))
                _iterateParameterTypes31.Add(parameter, test = ResolveSequenceType31(type, sc));
            return CoerceResolved31(value, test, sc, "XTTE0590");
        }
        catch (XPathStaticException error) { return ReportXPath31Error(error.Code, error.Message); }
    }

    private QilNode Stateful31(string method, XmlQueryType type, params QilNode[] arguments)
    {
        QilNode call = X31(method, type, arguments);
        OptimizerPatterns.Write(call).AddPattern(OptimizerPatternName.MaybeSideEffects);
        return call;
    }

    private QilNode CompileStreamedIterate31(XslNode node, List<VarPar> parameters, IList<XslNode> body, XslNode? completion)
    {
        QilNode selection = CompileXPath31(node.Select);
        var initialValues = new List<QilIterator>();
        QilNode initial = Stateful31(nameof(XsltLibrary.X31IterationNew), T.Item, _f.False());
        foreach (VarPar parameter in parameters)
        {
            EnterScope(parameter);
            QilNode value;
            try
            {
                value = CoerceIterateParameter31(Box(CompileVarParValue(parameter)), parameter);
            }
            finally { ExitScope(); }
            QilIterator binding = _f.Let(value);
            initialValues.Add(binding);
            _scope.AddVariable(parameter.Name!, binding);
            initial = Stateful31(nameof(XsltLibrary.X31IterationAdd), T.Item, initial, binding);
        }
        QilIterator state = _f.Let(initial);
        List<QilIterator> Values()
        {
            var values = new List<QilIterator>();
            for (int i = 0; i < parameters.Count; i++)
            {
                QilIterator value = _f.Let(Stateful31(nameof(XsltLibrary.X31IterationValue), T.ItemS, state, _f.Double(i)));
                values.Add(value);
                _scope.AddVariable(parameters[i].Name!, value);
            }
            return values;
        }
        QilNode BindValues(List<QilIterator> values, QilNode result)
        {
            for (int i = values.Count - 1; i >= 0; i--) result = _f.Loop(values[i], result);
            return result;
        }
        QilNode Active() => Stateful31(nameof(XsltLibrary.X31IterationActive), T.BooleanX, state);

        LoopFocus savedLoop = _curLoop;
        var savedFixed = _fixedFocus31;
        QilIterator? savedNodeContext = _nodeContext31;
        bool savedNoFocus = _noFocus;
        QilIterator source = _f.For(selection);
        QilNode iteration;
        _scope.EnterScope();
        try
        {
            _curLoop = new LoopFocus(_f);
            _curLoop.SetFocus(source);
            _fixedFocus31 = null;
            _nodeContext31 = null;
            _noFocus = false;
            int variables = _varHelper.StartVariables();
            List<QilIterator> values = Values();
            _iterate31.Push((parameters, values, state));
            try { iteration = WithoutTemplateRule(() => CompileInstructions(body)); }
            finally { _iterate31.Pop(); }
            iteration = AddCurrentPositionLast(iteration);
            iteration = _curLoop.ConstructLoop(BindValues(values, iteration));
            // ConstructLoop may have introduced a real last() cache. Gate its for binding, so break
            // stops either the direct input or the grounded cache. Unused count caches can be pruned.
            source.Binding = Streaming.StreamedLoopGate.Create(_f.BaseFactory, source.Binding!, Active());
            iteration = _varHelper.FinishVariables(iteration, variables);
        }
        finally
        {
            _scope.ExitScope();
            _curLoop = savedLoop;
            _fixedFocus31 = savedFixed;
            _nodeContext31 = savedNodeContext;
            _noFocus = savedNoFocus;
        }

        QilNode completed = _f.Sequence();
        if (completion != null)
        {
            _scope.EnterScope();
            _curLoop = new LoopFocus(_f);
            _fixedFocus31 = null;
            _nodeContext31 = null;
            _noFocus = true;
            try
            {
                int variables = _varHelper.StartVariables();
                List<QilIterator> values = Values();
                EnterScope(completion);
                try { completed = WithoutTemplateRule(() => SelectOrContent31(completion, completion.Select)); }
                finally { ExitScope(); }
                completed = _varHelper.FinishVariables(BindValues(values, completed), variables);
            }
            finally
            {
                _scope.ExitScope();
                _curLoop = savedLoop;
                _fixedFocus31 = savedFixed;
                _nodeContext31 = savedNodeContext;
                _noFocus = savedNoFocus;
            }
            completed = _f.Conditional(Active(), completed, _f.Sequence());
        }
        QilNode result = _f.Loop(state, _f.Sequence(iteration, completed));
        return BindValues(initialValues, result);
    }
}
