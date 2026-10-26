// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Xml;
using Xsl3.Qil;
using Xsl3.Streaming;
using Xunit;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Tests;

public class StreamedFocusPruningTests
{
    private readonly QilFactory _f = new();
    private QilFunction Function(out QilParameter last)
    {
        last = _f.Parameter(null, _f.LiteralQName("last", XmlReservedNs.NsXslDebug, ""), T.DoubleX);
        QilList arguments = _f.FormalParameterList();
        arguments.Add(last);
        return _f.Function(arguments, _f.LiteralDouble(1), _f.False(), T.DoubleX);
    }
    private QilInvoke Call(QilFunction function, QilNode argument)
    {
        QilList arguments = _f.ActualParameterList();
        arguments.Add(argument);
        return _f.Invoke(function, arguments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextSizeDemandPropagatesThroughCalls(bool used)
    {
        QilFunction first = Function(out var firstLast), second = Function(out var secondLast);
        first.Definition = Call(second, firstLast);
        if (used) second.Definition = secondLast;
        QilExpression expression = _f.QilExpression(Call(first, _f.LiteralDouble(3)), _f);
        expression.FunctionList.Add(first);
        expression.FunctionList.Add(second);
        new StreamedFocusPruning(_f).Prune(expression);
        Assert.Equal(used ? 1 : 0, first.Arguments.Count);
        Assert.Equal(used ? 1 : 0, second.Arguments.Count);
        Assert.Equal(used ? 1 : 0, ((QilInvoke)expression.Root).Arguments.Count);
        Assert.Equal(used ? 1 : 0, ((QilInvoke)first.Definition).Arguments.Count);
    }

    [Fact]
    public void APassThroughCycleDoesNotInventAContextSizeDemand()
    {
        QilFunction first = Function(out var firstLast), second = Function(out var secondLast);
        first.Definition = Call(second, firstLast);
        second.Definition = Call(first, secondLast);
        QilExpression expression = _f.QilExpression(Call(first, _f.LiteralDouble(3)), _f);
        expression.FunctionList.Add(first);
        expression.FunctionList.Add(second);
        new StreamedFocusPruning(_f).Prune(expression);
        Assert.Empty(first.Arguments);
        Assert.Empty(second.Arguments);
        Assert.Empty(((QilInvoke)first.Definition).Arguments);
        Assert.Empty(((QilInvoke)second.Definition).Arguments);
    }
}
