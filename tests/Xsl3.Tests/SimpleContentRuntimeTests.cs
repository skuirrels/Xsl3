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

    [Fact]
    public void IncrementalBoundariesMatchTheCanonicalSimpleContentFunction()
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
                    XPathItem state = library.X31SimpleContentNew("|", output, false);
                    foreach (XPathItem item in items) library.X31SimpleContentAccept(state, item);
                    library.X31SimpleContentEnd(state);
                    library.X31SimpleContentEnd(state);
                    XPathNavigator node = Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(writer.ResultSequence));
                    Assert.Equal(XPathNodeType.Text, node.NodeType);
                    Assert.Equal(expected, node.Value);
                }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationRemainsAFaultAfterTheTokenIsRestored(bool atEnd)
    {
        var (library, output, _) = Runtime();
        XPathItem state = library.X31SimpleContentNew("|", output, false);
        library.X31SimpleContentAccept(state, AtomicValue.OfString("a"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var previous = XmlQueryRuntime.Cancellation; OperationCanceledException error;
        try
        {
            XmlQueryRuntime.Cancellation = cancellation.Token;
            error = Assert.Throws<OperationCanceledException>(() => {
                if (atEnd) library.X31SimpleContentEnd(state);
                else library.X31SimpleContentAccept(state, AtomicValue.OfString("b"));
            });
        }
        finally { XmlQueryRuntime.Cancellation = previous; }
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31SimpleContentEnd(state)));
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31SimpleContentAccept(state, AtomicValue.OfString("c"))));
    }

    [Fact]
    public void AtomizationErrorsRemainAFault()
    {
        var (library, output, _) = Runtime();
        XPathItem state = library.X31SimpleContentNew("|", output, false);
        XdmException error = Assert.Throws<XdmException>(() => library.X31SimpleContentAccept(state, MapItem.Empty));
        Assert.Equal("FOTY0013", error.Code);
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31SimpleContentEnd(state)));
    }
}
