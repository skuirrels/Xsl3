// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Runtime;
using Xsl3.Qil;
using Xsl3.XPath31;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class GroundedSetRuntimeTests
{
    private static XsltLibrary Runtime()
    {
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true, Constants = new ConstantTable() };
        return new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter()).XsltFunctions;
    }

    private static XPathItem[] Select(string xml, string path) =>
        XdmNode.Load(new StringReader(xml)).Navigator.Select(path).Cast<XPathNavigator>().Cast<XPathItem>().ToArray();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void DisjointPopulationsDoNotCompareEveryPair(int operation)
    {
        const int count = 1000;
        var nodes = Select("<doc>" + string.Concat(Enumerable.Repeat("<n/><n/>", count)) + "</doc>", "/doc/n");
        var comparisons = new Counter();
        var left = nodes.Where((_, i) => i % 2 == 0).Select(n => (XPathItem)new CountingNavigator((XPathNavigator)n, comparisons)).ToArray();
        var right = nodes.Where((_, i) => i % 2 != 0).Select(n => (XPathItem)new CountingNavigator((XPathNavigator)n, comparisons)).ToArray();
        var result = Runtime().X31SetOp(left, right, operation);
        Assert.Equal(operation == 1 ? 0 : count, result.Count);
        // Includes both sorts and deduplication. The old nested scan uses over a million comparisons.
        Assert.InRange(comparisons.Value, 1, 50000);
    }

    [Theory]
    [InlineData(0, new[] { 0, 1, 2 })]
    [InlineData(1, new[] { 2 })]
    [InlineData(2, new[] { 0 })]
    public void UnsortedDuplicatesUseIdentityAndDocumentOrder(int operation, int[] expected)
    {
        var nodes = Select("<doc><a/><b/><c/></doc>", "/doc/*");
        var result = Runtime().X31SetOp(new[] { nodes[2], nodes[0], nodes[0] }, new[] { nodes[1], nodes[2], nodes[2] }, operation);
        Assert.Equal(expected.Length, result.Count);
        for (int i = 0; i < expected.Length; i++) Assert.True(((XPathNavigator)result[i]).IsSamePosition((XPathNavigator)nodes[expected[i]]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void NodesFromDifferentDocumentsKeepTheUnionOrder(int operation)
    {
        var first = Select("<a><b/><c/></a>", "/a/*");
        var second = Select("<a><b/><c/></a>", "/a/*");
        var library = Runtime();
        var left = new[] { second[1], first[1], second[0], first[0], first[0] };
        var right = new[] { first[1], second[0] };
        var ordered = library.X31SetOp(left, Array.Empty<XPathItem>(), 0);
        var expected = ordered.Where(n => right.Any(r => ((XPathNavigator)n).IsSamePosition((XPathNavigator)r)) == (operation == 1)).ToArray();
        var actual = library.X31SetOp(left, right, operation);
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++) Assert.True(((XPathNavigator)actual[i]).IsSamePosition((XPathNavigator)expected[i]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AttributeAndNamespaceIdentitySurvivesMerging(int operation)
    {
        var nodes = Select("<doc xmlns:p='urn:test' a='one' b='two'/>", "/doc | /doc/@* | /doc/namespace::*");
        var left = nodes.AsEnumerable().Reverse().Concat(nodes).ToArray();
        var right = new[] { nodes[1], nodes[^1], nodes[1] };
        var result = Runtime().X31SetOp(left, right, operation);
        var expected = nodes.Where(n => right.Any(r => ((XPathNavigator)n).IsSamePosition((XPathNavigator)r)) == (operation == 1)).ToArray();
        Assert.Equal(expected.Length, result.Count);
        for (int i = 0; i < expected.Length; i++) Assert.True(((XPathNavigator)result[i]).IsSamePosition((XPathNavigator)expected[i]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EmptyOperandsStillValidateLateItems(int operation)
    {
        var items = new[] { Select("<doc/>", "/doc")[0], AtomicValue.OfInteger(1) };
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => Runtime().X31SetOp(Array.Empty<XPathItem>(), items, operation)).Code);
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => Runtime().X31SetOp(items, Array.Empty<XPathItem>(), operation)).Code);
        Assert.Empty(Runtime().X31SetOp(Array.Empty<XPathItem>(), Array.Empty<XPathItem>(), operation));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void CancellationIsCheckedDuringTheMerge(int operation)
    {
        using var cancellation = new CancellationTokenSource();
        var previous = XmlQueryRuntime.Cancellation;
        try
        {
            XmlQueryRuntime.Cancellation = cancellation.Token;
            var nodes = Select("<doc><a/><b/></doc>", "/doc/*");
            // Each singleton skips sort/dedup comparisons; the first cross-operand comparison cancels.
            var comparisons = new Counter { OnCompare = cancellation.Cancel };
            var left = new XPathItem[] { new CountingNavigator((XPathNavigator)nodes[0], comparisons) };
            var right = new XPathItem[] { new CountingNavigator((XPathNavigator)nodes[1], comparisons) };
            Assert.Throws<OperationCanceledException>(() => Runtime().X31SetOp(left, right, operation));
        }
        finally { XmlQueryRuntime.Cancellation = previous; }
    }

    private sealed class Counter
    {
        public int Value;
        public Action? OnCompare;
    }

    private sealed class CountingNavigator(XPathNavigator inner, Counter counter) : XPathNavigator
    {
        public override string BaseURI => inner.BaseURI;
        public override bool IsEmptyElement => inner.IsEmptyElement;
        public override string LocalName => inner.LocalName;
        public override string Name => inner.Name;
        public override string NamespaceURI => inner.NamespaceURI;
        public override XmlNameTable NameTable => inner.NameTable;
        public override XPathNodeType NodeType => inner.NodeType;
        public override string Prefix => inner.Prefix;
        public override string Value => inner.Value;
        public override XPathNavigator Clone() => new CountingNavigator(inner.Clone(), counter);
        public override bool IsSamePosition(XPathNavigator other) => other is CountingNavigator wrapped && inner.IsSamePosition(wrapped.Inner);
        private XPathNavigator Inner => inner;
        public override XmlNodeOrder ComparePosition(XPathNavigator? other)
        {
            counter.Value++;
            counter.OnCompare?.Invoke();
            return inner.ComparePosition(other is CountingNavigator wrapped ? wrapped.Inner : other);
        }
        public override bool MoveTo(XPathNavigator other) => other is CountingNavigator wrapped && inner.MoveTo(wrapped.Inner);
        public override bool MoveToFirstAttribute() => inner.MoveToFirstAttribute();
        public override bool MoveToFirstChild() => inner.MoveToFirstChild();
        public override bool MoveToFirstNamespace(XPathNamespaceScope scope) => inner.MoveToFirstNamespace(scope);
        public override bool MoveToId(string id) => inner.MoveToId(id);
        public override bool MoveToNext() => inner.MoveToNext();
        public override bool MoveToNextAttribute() => inner.MoveToNextAttribute();
        public override bool MoveToNextNamespace(XPathNamespaceScope scope) => inner.MoveToNextNamespace(scope);
        public override bool MoveToParent() => inner.MoveToParent();
        public override bool MoveToPrevious() => inner.MoveToPrevious();
    }
}
