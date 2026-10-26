// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class OrderedNodeMergeRuntimeTests
{
    private static XsltLibrary Runtime()
    {
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager())
            { Xslt2 = true, Constants = new ConstantTable() };
        return new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter()).XsltFunctions;
    }

    private static NativeFunction Producer(Func<IList<XPathItem>> next) =>
        new(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence, _ => next(), "test node population");

    private static NativeFunction Items(params XPathItem[] items)
    {
        int position = 0;
        return Producer(() => position < items.Length ? new[] { items[position++] } : Array.Empty<XPathItem>());
    }

    private static XPathNavigator[] Nodes()
    {
        XPathNavigator root = XdmNode.Load(new StringReader("<doc><row>one</row><row>two</row><row>three</row></doc>")).Navigator;
        Assert.True(root.MoveToFirstChild()); Assert.True(root.MoveToFirstChild());
        XPathNavigator one = root.Clone(); Assert.True(root.MoveToNext());
        XPathNavigator two = root.Clone(); Assert.True(root.MoveToNext());
        return new[] { one, two, root.Clone() };
    }

    [Fact]
    public void AStreamedPopulationIsAdvancedAfterItsCurrentNodeIsConsumed()
    {
        XsltLibrary library = Runtime();
        using var input = XmlReader.Create(new StringReader("<doc><row>one</row><row>two</row></doc>"));
        using var document = new StreamedDocument(input);
        XPathNavigator current = document.CreateNavigator(); Assert.True(current.MoveToFirstChild());
        bool started = false;
        int reads = 0;
        var producer = Producer(() => {
            reads++;
            bool found = started ? current.MoveToNext() : current.MoveToFirstChild(); started = true;
            return found ? new XPathItem[] { current.Clone() } : Array.Empty<XPathItem>();
        });
        XPathItem merge = library.X31NodeMergeNew(new[] { library.X31NodeMergeInputNew(producer, true), library.X31NodeMergeInputNew(Items(), true) });
        XPathNavigator first = Assert.Single(library.X31NodeMergeNext(merge));
        Assert.Equal(1, reads); Assert.Equal("one", first.Value);
        XPathNavigator second = Assert.Single(library.X31NodeMergeNext(merge));
        Assert.Equal(2, reads); Assert.Equal("two", second.Value);
        Assert.Empty(library.X31NodeMergeNext(merge)); Assert.Equal(3, reads);
        Assert.Empty(library.X31NodeMergeNext(merge)); Assert.Equal(3, reads);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void MultiplePopulationsAreSortedAndDeduplicatedByIdentity()
    {
        XsltLibrary library = Runtime(); XPathNavigator[] nodes = Nodes();
        XPathItem merge = library.X31NodeMergeNew(new[] {
            library.X31NodeMergeInputNew(Items(nodes[2], nodes[0], nodes[0]), false),
            library.X31NodeMergeInputNew(Items(nodes[0], nodes[1]), true),
            library.X31NodeMergeInputNew(Items(nodes[1], nodes[2]), true),
        });
        foreach (XPathNavigator expected in nodes)
        {
            XPathNavigator actual = Assert.Single(library.X31NodeMergeNext(merge));
            Assert.True(actual.IsSamePosition(expected)); Assert.Equal(expected.Value, actual.Value);
        }
        Assert.Empty(library.X31NodeMergeNext(merge));
    }

    [Fact]
    public void ProducerFailuresAreRetainedAndDoNotResumeThePopulation()
    {
        XsltLibrary library = Runtime(); XPathNavigator first = Nodes()[0];
        var failure = new XdmException("TEST0001", "producer failed"); int reads = 0;
        var producer = Producer(() => ++reads == 1 ? new XPathItem[] { first } : throw failure);
        XPathItem merge = library.X31NodeMergeNew(new[] { library.X31NodeMergeInputNew(producer, true) });
        Assert.True(Assert.Single(library.X31NodeMergeNext(merge)).IsSamePosition(first));
        Assert.Same(failure, Assert.Throws<XdmException>(() => library.X31NodeMergeNext(merge)));
        Assert.Same(failure, Assert.Throws<XdmException>(() => library.X31NodeMergeNext(merge)));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void AnInvalidOrderingProofFailsAndRetainsItsFailure()
    {
        XsltLibrary library = Runtime(); XPathNavigator[] nodes = Nodes();
        XPathItem merge = library.X31NodeMergeNew(new[] { library.X31NodeMergeInputNew(Items(nodes[1], nodes[0]), true) });
        Assert.True(Assert.Single(library.X31NodeMergeNext(merge)).IsSamePosition(nodes[1]));
        var failure = Assert.Throws<InvalidOperationException>(() => library.X31NodeMergeNext(merge));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => library.X31NodeMergeNext(merge)));
    }
}
