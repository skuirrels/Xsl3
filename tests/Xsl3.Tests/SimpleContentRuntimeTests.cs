// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class SimpleContentRuntimeTests
{
    private static (XsltLibrary Library, XmlQueryOutput Output, XmlCachedSequenceWriter Writer) Runtime()
    {
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager())
            { Xslt2 = true, Constants = new ConstantTable() };
        var writer = new XmlCachedSequenceWriter(compact: true);
        var runtime = new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, writer);
        return (runtime.XsltFunctions, runtime.Output, writer);
    }

    private static (Action<XPathItem> Accept, Action End) State(bool valueState, XsltLibrary library, XmlQueryOutput output)
    {
        if (valueState)
        {
            var state = new SimpleContentWriter();
            state.Create("|", output, false);
            return (item => state.Accept(item), () => state.End());
        }
        XPathItem boxed = library.X31SimpleContentNew("|", output, false);
        return (item => library.X31SimpleContentAccept(boxed, item), () => library.X31SimpleContentEnd(boxed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncrementalBoundariesMatchTheCanonicalSimpleContentFunction(bool valueState)
    {
        XPathNavigator text = XdmNode.Load(new System.IO.StringReader("<doc>b</doc>")).Navigator;
        Assert.True(text.MoveToFirstChild()); Assert.True(text.MoveToFirstChild());
        XPathItem[] choices = { AtomicValue.OfString(""), AtomicValue.OfString("a"), text,
            ArrayItem.Of(Array.Empty<IList<XPathItem>>()),
            ArrayItem.Of(new IList<XPathItem>[] { Array.Empty<XPathItem>(), new XPathItem[] { AtomicValue.OfString("c"), AtomicValue.OfString("d") } }) };
        foreach (XPathItem first in choices)
            foreach (XPathItem second in choices)
                foreach (XPathItem third in choices)
                {
                    var (library, output, writer) = Runtime();
                    XPathItem[] items = { first, second, third };
                    string expected = library.X31SimpleContent(items, "|");
                    var state = State(valueState, library, output);
                    foreach (XPathItem item in items) state.Accept(item);
                    state.End();
                    state.End();
                    XPathNavigator node = Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(writer.ResultSequence));
                    Assert.Equal(XPathNodeType.Text, node.NodeType);
                    Assert.Equal(expected, node.Value);
                }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CancellationRemainsAFaultAfterTheTokenIsRestored(bool atEnd, bool valueState)
    {
        var (library, output, _) = Runtime();
        var state = State(valueState, library, output);
        state.Accept(AtomicValue.OfString("a"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var previous = XmlQueryRuntime.Cancellation; OperationCanceledException error;
        try
        {
            XmlQueryRuntime.Cancellation = cancellation.Token;
            error = Assert.Throws<OperationCanceledException>(() => {
                if (atEnd) state.End();
                else state.Accept(AtomicValue.OfString("b"));
            });
        }
        finally { XmlQueryRuntime.Cancellation = previous; }
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => state.End()));
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => state.Accept(AtomicValue.OfString("c"))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtomizationErrorsRemainAFault(bool valueState)
    {
        var (library, output, _) = Runtime();
        var state = State(valueState, library, output);
        XdmException error = Assert.Throws<XdmException>(() => state.Accept(MapItem.Empty));
        Assert.Equal("FOTY0013", error.Code);
        Assert.Same(error, Assert.Throws<XdmException>(() => state.End()));
    }

    [Fact]
    public void ReinitializationStartsAnIndependentConstructorAfterSuccessOrFailure()
    {
        var state = new SimpleContentWriter();
        for (int i = 0; i < 3; i++)
        {
            var (_, output, writer) = Runtime();
            state.Create(i.ToString(), output, false);
            state.Accept(AtomicValue.OfString("a"));
            if (i == 1) Assert.Throws<XdmException>(() => state.Accept(MapItem.Empty));
            else
            {
                state.Accept(AtomicValue.OfString("b"));
                state.End();
                XPathNavigator node = Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(writer.ResultSequence));
                Assert.Equal("a" + i + "b", node.Value);
            }
        }
    }
}
