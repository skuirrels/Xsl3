// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.Qil;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xsl3.IlGen;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xunit;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Tests;

public class StreamedFunctionLoweringTests
{
    private readonly QilFactory _f = new();

    private QilFunction Function(out QilParameter parameter)
    {
        parameter = _f.Parameter(T.NodeNotRtfS);
        QilList arguments = _f.FormalParameterList();
        arguments.Add(parameter);
        return _f.Function(arguments, _f.Length(parameter), _f.False(), T.IntX);
    }

    private QilExpression Expression(QilFunction function, params QilFunction[] others)
    {
        QilList actual = _f.ActualParameterList();
        actual.Add(_f.Content(_f.XmlContext()));
        QilExpression expression = _f.QilExpression(_f.Invoke(function, actual), _f);
        expression.FunctionList.Add(function);
        foreach (QilFunction other in others) expression.FunctionList.Add(other);
        return expression;
    }

    [Fact]
    public void ASingleReferenceInsideARepeatedBodyRetainsTheCall()
    {
        QilFunction function = Function(out var parameter);
        QilIterator repeated = _f.For(_f.Sequence(new QilNode[] { _f.LiteralInt32(1), _f.LiteralInt32(2) }));
        function.Definition = _f.Length(_f.Loop(repeated, parameter));
        QilExpression expression = Expression(function);
        QilNode call = expression.Root;
        new StreamedFunctionLowering(_f, new() { function }).Lower(expression);
        Assert.Same(call, expression.Root);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentGroundedCallsDoNotMultiplyAnExpansion(bool leafFirst)
    {
        var functions = new List<QilFunction> { Function(out _) };
        const int depth = 12;
        for (int i = 1; i < depth; i++)
        {
            QilFunction function = Function(out var parameter);
            QilList dependent = _f.ActualParameterList();
            dependent.Add(parameter);
            QilList independent = _f.ActualParameterList();
            independent.Add(_f.Sequence(new QilNode[] {
                _f.ElementCtor(_f.LiteralQName("left"), _f.Sequence()),
                _f.ElementCtor(_f.LiteralQName("right"), _f.Sequence()),
            }));
            function.Definition = _f.Add(_f.Invoke(functions[i - 1], dependent), _f.Invoke(functions[i - 1], independent));
            functions.Add(function);
        }
        QilExpression expression = Expression(functions[^1]);
        expression.FunctionList.Clear();
        if (!leafFirst) functions.Reverse();
        foreach (QilFunction function in functions) expression.FunctionList.Add(function);
        new StreamedFunctionLowering(_f, new(functions)).Lower(expression);
        var calls = new CountCalls();
        Assert.Equal(depth - 1, calls.Count(expression.Root));
        Assert.InRange(calls.Nodes, 1, depth * 20);
    }

    private sealed class CountCalls : QilVisitor
    {
        private int _count;
        internal int Nodes { get; private set; }
        internal int Count(QilNode node) { Visit(node); return _count; }
        protected override QilNode VisitInvoke(QilInvoke call) { _count++; return base.VisitInvoke(call); }
        protected override QilNode Visit(QilNode node) { if (node != null) Nodes++; return base.Visit(node!); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecursiveCallGraphsRetainTheirBoundaries(bool mutual)
    {
        QilFunction first = Function(out var parameter);
        QilFunction second = mutual ? Function(out _) : first;
        QilList firstActual = _f.ActualParameterList();
        firstActual.Add(parameter);
        first.Definition = _f.Invoke(second, firstActual);
        if (mutual)
        {
            QilList secondActual = _f.ActualParameterList();
            secondActual.Add(second.Arguments[0]);
            second.Definition = _f.Invoke(first, secondActual);
        }
        QilExpression expression = mutual ? Expression(first, second) : Expression(first);
        QilNode call = expression.Root;
        new StreamedFunctionLowering(_f, new() { first, second }).Lower(expression);
        Assert.Same(call, expression.Root);
    }

    [Fact]
    public void OptionalEmptyBranchesDoNotDuplicateTheTransmittingCallChain()
    {
        const int depth = 12;
        var functions = new List<QilFunction>();
        var contracts = new Dictionary<QilFunction, (SequenceTypeTest, bool)>();
        for (int i = 0; i < depth; i++)
        {
            QilFunction function = Function(out var parameter);
            function.XmlType = T.NodeNotRtfS;
            QilNode children = _f.Content(_f.TypeAssert(parameter, T.NodeNotRtf));
            function.Definition = i == 0 ? children : _f.Invoke(functions[i - 1], _f.ActualParameterList(new QilNode[] { children }));
            functions.Add(function);
            contracts.Add(function, (new SequenceTypeTest(new NodeKindTest(null), Cardinality.ZeroOrOne), false));
        }
        QilExpression expression = Expression(functions[^1]);
        expression.FunctionList.Clear();
        foreach (QilFunction function in functions) expression.FunctionList.Add(function);
        new StreamedFunctionLowering(_f, new(functions), contracts).Lower(expression);
        var calls = new CountCalls();
        Assert.Equal(depth, calls.Count(expression.Root)); // one grounded empty call per level
        Assert.InRange(calls.Nodes, 1, depth * 80);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("+")]
    public void TransmittingExpansionDoesNotRelaxACardinalitySensitiveReturnContract(string cardinality)
    {
        QilFunction function = Function(out var parameter);
        function.Definition = _f.TypeAssert(parameter, T.NodeNotRtf);
        function.XmlType = T.PrimeProduct(T.NodeNotRtf, cardinality switch {
            "?" => XmlQueryCardinality.ZeroOrOne, "+" => XmlQueryCardinality.OneOrMore, _ => XmlQueryCardinality.One,
        });
        QilExpression expression = Expression(function);
        QilNode call = expression.Root;
        var contracts = new Dictionary<QilFunction, (SequenceTypeTest, bool)> {
            [function] = (new SequenceTypeTest(new NodeKindTest(null), Cardinality.ExactlyOne), false),
        };
        new StreamedFunctionLowering(_f, new() { function }, contracts).Lower(expression);
        Assert.Same(call, expression.Root);
    }

    [Fact]
    public void AnExpandedBodyRetainsIncrementalTextWriting()
    {
        QilFunction function = Function(out var parameter);
        QilNode content = XPath31Compiler.Invoke(new QilPatternFactory(_f, debug: false),
            nameof(XsltLibrary.X31SimpleContent), T.StringX, parameter, _f.LiteralString("|"));
        OptimizerPatterns.Write(content).AddPattern(OptimizerPatternName.StreamedSimpleContent);
        function.Definition = _f.TextCtor(content);
        function.XmlType = function.Definition.XmlType;
        QilExpression expression = Expression(function);
        new StreamedFunctionLowering(_f, new() { function }).Lower(expression);
        var text = Assert.IsType<QilUnary>(expression.Root);
        Assert.Equal(QilNodeType.TextCtor, text.NodeType);
        Assert.True(OptimizerPatterns.Read(text.Child).MatchesPattern(OptimizerPatternName.StreamedSimpleContent));
    }
}
