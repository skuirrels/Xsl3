// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
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

[Collection("Process-wide state")]
public class StreamedFunctionParameterRuntimeTests
{
    private static (XsltLibrary Library, int Type) Runtime(SequenceTypeTest? required = null)
    {
        var constants = new ConstantTable();
        int type = constants.Add(required ?? new SequenceTypeTest(new NodeKindTest(null), Cardinality.ZeroOrMore));
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager())
            { Xslt2 = true, Constants = constants };
        var runtime = new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter());
        return (runtime.XsltFunctions, type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ItemReadersPreserveGroundedAtomicMapArrayFunctionAndNodeValues(bool lazy)
    {
        var (library, type) = Runtime(SequenceTypeTest.AnySequence);
        XPathNavigator node = XdmNode.Load(new StringReader("<value>node</value>")).Navigator;
        XPathItem[] items = { AtomicValue.OfInteger(1), MapItem.Empty, ArrayItem.Of(new[] { new XPathItem[] { AtomicValue.OfInteger(2) } }),
            new NativeFunction(Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence, _ => Array.Empty<XPathItem>(), "grounded test function"), node };
        int produced = 0;
        var producer = new NativeFunction(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence,
            _ => produced < items.Length ? new[] { items[produced++] } : Array.Empty<XPathItem>(), "test item population");
        XPathItem parameter = library.X31FunctionParameterNew(lazy ? new[] { library.X31FunctionArgument(producer, false) } : items, type);
        for (int read = 0; read < 2; read++)
        {
            XPathItem cursor = library.X31FunctionParameterCursor(parameter);
            foreach (XPathItem expected in items)
            {
                XPathItem actual = Assert.Single(library.X31FunctionParameterItemNext(cursor));
                if (expected is XPathNavigator navigator) Assert.True(navigator.IsSamePosition(Assert.IsAssignableFrom<XPathNavigator>(actual)));
                else Assert.Same(expected, actual);
            }
            Assert.Empty(library.X31FunctionParameterItemNext(cursor));
        }
        Assert.Equal(lazy ? items.Length : 0, produced);
    }

    [Fact]
    public void GroundedParameterFramesOwnTheirListsAndNavigatorPositions()
    {
        var (library, type) = Runtime(SequenceTypeTest.AnySequence);
        XPathNavigator node = XdmNode.Load(new StringReader("<root><one/><two/></root>")).Navigator;
        Assert.True(node.MoveToFirstChild()); Assert.True(node.MoveToFirstChild());
        var items = new System.Collections.Generic.List<XPathItem> { node, AtomicValue.OfInteger(1) };
        XPathItem parameter = library.X31FunctionParameterNew(items, type);
        items.Clear(); Assert.True(node.MoveToNext());
        XPathItem cursor = library.X31FunctionParameterCursor(parameter);
        Assert.Equal("one", Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(library.X31FunctionParameterItemNext(cursor))).LocalName);
        Assert.Equal("1", Assert.Single(library.X31FunctionParameterItemNext(cursor)).Value);
        Assert.Empty(library.X31FunctionParameterItemNext(cursor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundedValuesHaveIndependentRepeatableReaders(bool lazy)
    {
        var (library, type) = Runtime();
        XPathNavigator node = XdmNode.Load(new StringReader("<doc><one/><two/></doc>")).Navigator;
        Assert.True(node.MoveToFirstChild()); Assert.True(node.MoveToFirstChild());
        XPathNavigator one = node.Clone(); Assert.True(node.MoveToNext());
        XPathItem[] items = { one, node.Clone() };
        int index = 0;
        var producer = new NativeFunction(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence,
            _ => index < items.Length ? new[] { items[index++] } : Array.Empty<XPathItem>(), "test population");
        XPathItem parameter = library.X31FunctionParameterNew(lazy ? new[] { library.X31FunctionArgument(producer, false) } : items, type);
        XPathItem first = library.X31FunctionParameterCursor(parameter), second = library.X31FunctionParameterCursor(parameter);
        Assert.Equal("one", Assert.Single(library.X31FunctionParameterNext(first)).LocalName);
        Assert.Equal("one", Assert.Single(library.X31FunctionParameterNext(second)).LocalName);
        Assert.Equal("two", Assert.Single(library.X31FunctionParameterNext(second)).LocalName);
        Assert.Equal("two", Assert.Single(library.X31FunctionParameterNext(first)).LocalName);
        Assert.Empty(library.X31FunctionParameterNext(first));
        Assert.Empty(library.X31FunctionParameterNext(second));
        Assert.Equal("2", library.X31FunctionParameterCount(parameter).Value);
        Assert.True(library.X31FunctionParameterExists(parameter));
    }

    [Fact]
    public void AForwardReaderRetainsOneHeaderAndRejectsUnavailablePastContent()
    {
        var (library, type) = Runtime();
        using var input = XmlReader.Create(new StringReader("<doc><row>one</row><row>two</row></doc>"));
        using var document = new StreamedDocument(input);
        XPathNavigator node = document.CreateNavigator();
        Assert.True(node.MoveToFirstChild());
        bool started = false;
        var producer = new NativeFunction(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence, _ => {
            bool found = started ? node.MoveToNext() : node.MoveToFirstChild();
            started = true;
            return found ? new XPathItem[] { node.Clone() } : Array.Empty<XPathItem>();
        }, "test forward population");
        XPathItem parameter = library.X31FunctionParameterNew(new[] { library.X31FunctionArgument(producer, false) }, type);
        XPathItem peek = library.X31FunctionParameterCursor(parameter), consumer = library.X31FunctionParameterCursor(parameter);
        Assert.Equal("row", Assert.Single(library.X31FunctionParameterNext(peek)).LocalName);
        Assert.Equal("one", Assert.Single(library.X31FunctionParameterNext(consumer)).Value);
        Assert.Equal("two", Assert.Single(library.X31FunctionParameterNext(consumer)).Value);
        Assert.Empty(library.X31FunctionParameterNext(consumer));
        Assert.Equal("stream-position", Assert.Throws<XdmException>(() => library.X31FunctionParameterNext(peek)).Code);
        Assert.Equal("2", library.X31FunctionParameterCount(parameter).Value);
        Assert.Equal("2", library.X31FunctionParameterCount(parameter).Value);
    }

    [Fact]
    public void CountingStreamedAttributesDoesNotRetainEveryParentFrame()
    {
        var (library, type) = Runtime();
        const int rows = 200;
        using var input = XmlReader.Create(new StringReader("<doc>" + string.Concat(System.Linq.Enumerable.Repeat("<row id='value'/>", rows)) + "</doc>"));
        using var document = new StreamedDocument(input);
        var position = (StreamedNavigator)document.CreateNavigator();
        Assert.True(position.MoveToFirstChild());
        bool started = false;
        int seen = 0;
        WeakReference<StreamedDocument.Node>? middle = null;
        var producer = new NativeFunction(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence, _ => {
            if (started) Assert.True(position.MoveToParent());
            bool found = started ? position.MoveToNext() : position.MoveToFirstChild();
            started = true;
            if (!found) return Array.Empty<XPathItem>();
            if (++seen == rows / 2) middle = new(position.PositionNode);
            Assert.True(position.MoveToAttribute("id", ""));
            return new XPathItem[] { position.Clone() };
        }, "test attribute population");
        XPathItem parameter = library.X31FunctionParameterNew(new[] { library.X31FunctionArgument(producer, false) }, type);
        Assert.Equal(rows.ToString(System.Globalization.CultureInfo.InvariantCulture), library.X31FunctionParameterCount(parameter).Value);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.NotNull(middle);
        Assert.False(middle.TryGetTarget(out _));
        GC.KeepAlive(parameter);
        GC.KeepAlive(document);
    }
}
