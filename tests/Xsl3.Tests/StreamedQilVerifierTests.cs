// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using Xsl3.Qil;
using Xsl3.IlGen;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class StreamedQilVerifierTests
{
    private readonly QilFactory _f = new();
    private QilExpression Expression(QilNode root) => _f.QilExpression(root, _f);
    private QilNode Read() => _f.Content(_f.XmlContext());

    [Fact]
    public void ChangingThePullPolicyOfAnOperationIsRejected()
    {
        QilNode read = Read();
        var expression = Expression(read);
        var verifier = StreamedQilVerifier.Capture(expression);
        OptimizerPatterns.Write(read).AddPattern(OptimizerPatternName.PullSequence);
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression));
    }

    [Fact]
    public void ChangingAPopulationsContinuationArgumentIsRejected()
    {
        QilFunction function = _f.Function(_f.FormalParameterList(), Read(), _f.True(), XmlQueryTypeFactory.ItemS);
        function.PullArgument = 0;
        var expression = Expression(_f.Sequence());
        expression.FunctionList.Add(function);
        var verifier = StreamedQilVerifier.Capture(expression);
        function.PullArgument = -1;
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression));
    }

    [Fact]
    public void ChangingSubtreePruningOfAnExistingScanIsRejected()
    {
        QilNode scan = _f.Descendant(_f.XmlContext());
        var expression = Expression(scan);
        var verifier = StreamedQilVerifier.Capture(expression);
        OptimizerPatterns.Write(scan).AddPattern(OptimizerPatternName.Topmost);
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression));
    }

    [Fact]
    public void ChangingTheBeforeNextPolicyOfAFilterIsRejected()
    {
        QilLoop filter = _f.Filter(_f.For(Read()), _f.True());
        var expression = Expression(filter);
        var verifier = StreamedQilVerifier.Capture(expression);
        OptimizerPatterns.Write(filter).AddPattern(OptimizerPatternName.LoopGate);
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression));
    }

    [Fact]
    public void UnchangedReadsAndEliminatedOrderingAreAllowed()
    {
        QilNode read = Read();
        var expression = Expression(_f.DocOrderDistinct(read));
        var verifier = StreamedQilVerifier.Capture(expression);
        expression.Root = read;
        verifier.Verify(expression);
    }

    [Fact]
    public void DuplicatingAReadIsRejected()
    {
        QilNode read = Read();
        var expression = Expression(read);
        var verifier = StreamedQilVerifier.Capture(expression);
        expression.Root = _f.Sequence(new[] { read, read });
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression));
    }

    [Fact]
    public void IntroducingAReadOrBufferIsRejected()
    {
        var expression = Expression(Read());
        var verifier = StreamedQilVerifier.Capture(expression);
        expression.Root = _f.DocOrderDistinct(expression.Root);
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression));
        expression.Root = Read();
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression));
    }

    [Fact]
    public void ReorderingReadsIsRejected()
    {
        QilNode first = Read(), second = Read();
        var expression = Expression(_f.Sequence(new[] { first, second }));
        var verifier = StreamedQilVerifier.Capture(expression);
        expression.Root = _f.Sequence(new[] { second, first });
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression));
    }

    [Fact]
    public void HoistingAReadOutOfALoopIsRejected()
    {
        QilNode read = Read();
        QilIterator iterator = _f.For(_f.Sequence(new[] { _f.LiteralInt32(1), _f.LiteralInt32(2) }));
        var expression = Expression(_f.Loop(iterator, read));
        var verifier = StreamedQilVerifier.Capture(expression);
        expression.Root = _f.Sequence(new[] { read, _f.Loop(iterator, _f.Sequence()) });
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression));
    }

    [Fact]
    public void AddingACacheBindingIsRejected()
    {
        QilNode read = Read();
        var expression = Expression(read);
        var verifier = StreamedQilVerifier.Capture(expression);
        QilIterator cache = _f.Let(read);
        expression.Root = _f.Loop(cache, cache);
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression));
    }

    [Fact]
    public void HoistingAReadOutOfABranchIsRejected()
    {
        QilNode read = Read();
        var expression = Expression(_f.Conditional(_f.True(), read, _f.Sequence()));
        var verifier = StreamedQilVerifier.Capture(expression);
        expression.Root = read;
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression));
    }

    [Fact]
    public void ChangingAForBindingIntoACacheIsRejected()
    {
        QilIterator iterator = _f.For(Read());
        var expression = Expression(_f.Loop(iterator, iterator));
        var verifier = StreamedQilVerifier.Capture(expression);
        iterator.NodeType = QilNodeType.Let;
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression));
    }

    [Fact]
    public void ChangingTheAxisOfAnExistingReadIsRejected()
    {
        QilNode read = Read();
        var expression = Expression(read);
        var verifier = StreamedQilVerifier.Capture(expression);
        read.NodeType = QilNodeType.Descendant;
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression));
    }
}
