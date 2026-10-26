// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Runtime;
using Xsl3.Qil;
using Xsl3.Streaming;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class NodeSetRuntimeTests
{
    private static XsltLibrary Runtime()
    {
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true, Constants = new ConstantTable() };
        return new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter()).XsltFunctions;
    }

    private static NativeFunction Producer(Func<IList<XPathItem>> next) =>
        new(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence, _ => next(), "test node population");

    private static NativeFunction Items(params XPathItem[] items)
    {
        int position = 0;
        return Producer(() => position < items.Length ? new[] { items[position++] } : Array.Empty<XPathItem>());
    }

    private static XPathItem Input(XsltLibrary library, FunctionItem producer, bool ordered) => library.X31NodeSetInputNew(new[] { producer }, ordered);

    private static XPathNavigator[] Nodes()
    {
        var node = XdmNode.Load(new StringReader("<doc><a/><b/><c/></doc>")).Navigator;
        Assert.True(node.MoveToFirstChild()); Assert.True(node.MoveToFirstChild());
        var a = node.Clone(); Assert.True(node.MoveToNext()); var b = node.Clone(); Assert.True(node.MoveToNext());
        return new[] { a, b, node.Clone() };
    }

    [Theory]
    [InlineData(0, new[] { 0, 1, 2 })]
    [InlineData(1, new[] { 2 })]
    [InlineData(2, new[] { 0 })]
    public void UnsortedGroundedInputsUseIdentityAndCanonicalOrder(int operation, int[] indices)
    {
        var library = Runtime(); var nodes = Nodes();
        var state = library.X31NodeSetNew(Input(library, Items(nodes[2], nodes[0], nodes[0]), false),
            Input(library, Items(nodes[1], nodes[2], nodes[2]), false), operation, true, true);
        foreach (int index in indices) Assert.True(Assert.Single(library.X31NodeSetNext(state)).IsSamePosition(nodes[index]));
        Assert.Empty(library.X31NodeSetNext(state)); Assert.Empty(library.X31NodeSetNext(state));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AReturnedStreamedNodeIsConsumedBeforeItsPopulationAdvances(int operation)
    {
        var library = Runtime(); using var input = XmlReader.Create(new StringReader("<doc><row>one</row><row>two</row></doc>"));
        using var document = new StreamedDocument(input);
        var node = document.CreateNavigator(); Assert.True(node.MoveToFirstChild());
        bool started = false; int reads = 0;
        var producer = Producer(() => {
            reads++; bool found = started ? node.MoveToNext() : node.MoveToFirstChild(); started = true;
            return found ? new XPathItem[] { node.Clone() } : Array.Empty<XPathItem>();
        });
        var state = library.X31NodeSetNew(Input(library, producer, true), library.X31NodeSetInputNew(Array.Empty<XPathItem>(), false), operation, false, true);
        Assert.Equal("one", Assert.Single(library.X31NodeSetNext(state)).Value); Assert.Equal(1, reads);
        Assert.Equal("two", Assert.Single(library.X31NodeSetNext(state)).Value); Assert.Equal(2, reads);
        Assert.Empty(library.X31NodeSetNext(state)); Assert.Equal(3, reads);
        Assert.Empty(library.X31NodeSetNext(state)); Assert.Equal(3, reads);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void AnEmptyIntersectionStillChecksTheOtherArgumentsLateItems()
    {
        var library = Runtime(); var nodes = Nodes();
        var state = library.X31NodeSetNew(library.X31NodeSetInputNew(Array.Empty<XPathItem>(), false),
            Input(library, Items(nodes[0], AtomicValue.OfInteger(1)), true), 1, true, false);
        var error = Assert.Throws<XdmException>(() => library.X31NodeSetNext(state));
        Assert.Equal("XPTY0004", error.Code);
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31NodeSetNext(state)));
    }

    [Fact]
    public void ProducerFailuresAreRetainedWithoutResumingTheirSource()
    {
        var library = Runtime(); var error = new XdmException("TEST0001", "failed producer"); int reads = 0;
        var producer = Producer(() => ++reads == 1 ? new XPathItem[] { Nodes()[0] } : throw error);
        var state = library.X31NodeSetNew(Input(library, producer, true), library.X31NodeSetInputNew(Array.Empty<XPathItem>(), false), 0, false, true);
        Assert.Single(library.X31NodeSetNext(state));
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31NodeSetNext(state)));
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31NodeSetNext(state)));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void CancellationClosesTheCursorWithoutStartingItsProducer()
    {
        var library = Runtime(); int reads = 0;
        var state = library.X31NodeSetNew(Input(library, Producer(() => { reads++; return Array.Empty<XPathItem>(); }), true),
            library.X31NodeSetInputNew(Array.Empty<XPathItem>(), false), 0, false, true);
        using var cancellation = new System.Threading.CancellationTokenSource(); cancellation.Cancel();
        var previous = XmlQueryRuntime.Cancellation;
        OperationCanceledException error;
        try
        {
            XmlQueryRuntime.Cancellation = cancellation.Token;
            error = Assert.Throws<OperationCanceledException>(() => library.X31NodeSetNext(state));
        }
        finally { XmlQueryRuntime.Cancellation = previous; }
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31NodeSetNext(state)));
        Assert.Equal(0, reads);
    }

    [Fact]
    public void PredicatePositionsHaveSeparateParentAndStageScopes()
    {
        var library = Runtime(); var nodes = Nodes(); var state = library.X31NodeSetMatchNew();
        Assert.Equal("1", library.X31NodeSetPositionNext(state, nodes[0], 0).Value);
        Assert.Equal("2", library.X31NodeSetPositionNext(state, nodes[0].Clone(), 0).Value);
        Assert.Equal("1", library.X31NodeSetPositionNext(state, nodes[0], 1).Value);
        Assert.Equal("1", library.X31NodeSetPositionNext(state, nodes[1], 0).Value);
        Assert.Equal("1", library.X31NodeSetPositionNext(state, Nodes()[0], 0).Value);
        using var reader = XmlReader.Create(new StringReader("<doc><row/><row/></doc>"));
        using var document = new StreamedDocument(reader); var parent = document.CreateNavigator();
        Assert.True(parent.MoveToFirstChild()); Assert.True(parent.MoveToFirstChild());
        Assert.Equal("1", library.X31NodeSetPositionNext(state, parent, 0).Value);
        Assert.Equal("2", library.X31NodeSetPositionNext(state, parent.Clone(), 0).Value);
        Assert.True(parent.MoveToNext());
        Assert.Equal("1", library.X31NodeSetPositionNext(state, parent, 0).Value);
    }

    [Fact]
    public void PositionalEqualityUsesExactIntegerComparison()
    {
        var library = Runtime(); var exact = System.Numerics.BigInteger.Parse("9007199254740993");
        var position = AtomicValue.OfInteger(exact);
        Assert.True(library.X31PredicateAt(new XPathItem[] { position }, position));
        Assert.False(library.X31PredicateAt(new XPathItem[] { AtomicValue.OfInteger(exact - 1) }, position));
        Assert.False(library.X31PredicateAt(new XPathItem[] { AtomicValue.OfDouble(double.NaN) }, position));
        Assert.True(library.X31PredicateAt(new XPathItem[] { AtomicValue.OfBoolean(true) }, position));
    }

    [Fact]
    public void HeaderMemoizationUsesNodeIdentityRatherThanNavigatorObjects()
    {
        var library = Runtime(); using var input = XmlReader.Create(new StringReader("<doc><row/><row/></doc>"));
        using var document = new StreamedDocument(input);
        var node = document.CreateNavigator(); Assert.True(node.MoveToFirstChild()); Assert.True(node.MoveToFirstChild());
        var state = library.X31NodeSetMatchNew();
        Assert.Equal(-1, library.X31NodeSetMatchGet(state, node, 0));
        Assert.True(library.X31NodeSetMatchPut(state, node, 0, true));
        Assert.Equal(1, library.X31NodeSetMatchGet(state, node.Clone(), 0));
        Assert.Equal(-1, library.X31NodeSetMatchGet(state, node, 1));
        Assert.True(node.MoveToNext());
        Assert.Equal(-1, library.X31NodeSetMatchGet(state, node, 0));
        var foreign = Nodes()[0];
        Assert.False(library.X31NodeSetMatchPut(state, foreign, 0, false));
        Assert.Equal(0, library.X31NodeSetMatchGet(state, foreign.Clone(), 0));
        Assert.Equal(-1, library.X31NodeSetMatchGet(state, Nodes()[0], 0));
    }

    [Fact]
    public void AnUnprovedLivePopulationCannotAcquireASortingBuffer()
    {
        var library = Runtime(); using var input = XmlReader.Create(new StringReader("<doc><row>one</row><row>two</row></doc>"));
        using var document = new StreamedDocument(input);
        var node = document.CreateNavigator(); Assert.True(node.MoveToFirstChild());
        bool started = false;
        var producer = Producer(() => {
            bool found = started ? node.MoveToNext() : node.MoveToFirstChild(); started = true;
            return found ? new XPathItem[] { node.Clone() } : Array.Empty<XPathItem>();
        });
        var state = library.X31NodeSetNew(Input(library, producer, false), library.X31NodeSetInputNew(Array.Empty<XPathItem>(), false), 0, false, true);
        Assert.Equal("streaming-unimplemented", Assert.Throws<XdmException>(() => library.X31NodeSetNext(state)).Code);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }
}
