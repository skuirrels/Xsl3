// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedForkValueTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    public static IEnumerable<object[]> Values()
    {
        foreach (int capacity in new[] { 1, 7, 64 })
        foreach (bool peek in new[] { false, true })
        foreach (string content in new[] { "", "text", "a<![CDATA[b]]>c", "a<n>b<m>c</m>d</n>e", "<!--c--><?pi x?>", " \t<n xml:space='preserve'> \n</n> " })
            yield return new object[] { capacity, peek, content };
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void ValuesKeepIdentityHeadersAndFollowingSiblings(int capacity, bool peek, string content)
    {
        string xml = "<doc xmlns:p='urn:values' xml:lang='en'><value id='selected'>" + content + "</value><after id='next'/></doc>";
        var expected = new XmlDocument { PreserveWhitespace = true }; expected.LoadXml(xml);
        string text = expected.DocumentElement!.FirstChild!.InnerText;
        Run(xml, capacity, view => {
            Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToFirstChild());
            XPathNavigator original = view.Clone();
            if (peek) Assert.Equal(expected.DocumentElement.FirstChild.HasChildNodes, view.HasChildren);
            Assert.Equal(text, view.Value);
            Assert.True(original.IsSamePosition(view));
            Assert.Equal("selected", view.GetAttribute("id", ""));
            Assert.Equal("en", view.XmlLang);
            Assert.Equal("urn:values", view.LookupNamespace("p"));
            Assert.True(view.PositionNode.Ended);
            Assert.True(view.MoveToNext());
            Assert.Equal("after", view.LocalName); Assert.Equal("next", view.GetAttribute("id", ""));
            Assert.False(view.MoveToNext());
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void BoundedScansContinueAfterValuesWithoutLosingSiblingMatches(int capacity)
    {
        const string xml = "<doc><n><n>1</n><n>2</n></n><n>3</n><n/></doc>";
        Run(xml, capacity, view => {
            Assert.True(view.MoveToFirstChild());
            var rows = new DescendantIterator(); rows.CreateBounded(view, XmlNavNameFilter.Create("n", ""), false, 1);
            var values = new List<string>();
            while (rows.MoveNext()) values.Add(((StreamedNavigator)rows.Current).ValueOperand().Value);
            Assert.Equal(new[] { "12", "3", "" }, values);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void UnboundedValueScansPreserveNestedMatchesAndReplay(int capacity)
    {
        const string xml = "<doc><n><n>1</n><n>2</n></n><n>3</n></doc>";
        Run(xml, capacity, view => {
            var scan = new DescendantIterator(); scan.Create(view, XmlNavNameFilter.Create("n", ""), false);
            var values = new List<string>();
            while (scan.MoveNext()) values.Add(((StreamedNavigator)scan.Current).ValueOperand().Value);
            Assert.Equal(new[] { "12", "1", "2", "3" }, values);
            Assert.Equal(0, view.Document.RetainedValueHeaderCount);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void AbsorbedHeadersAndMatchObserversStillReceiveEveryDescendant(int capacity)
    {
        const string xml = "<doc><value>a<n>b</n><!--c--><?pi x?>d</value><after/></doc>";
        Run(xml, capacity, view => {
            Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToFirstChild());
            var names = new List<string>();
            using var headers = view.Document.ObserveConsumedHeaders(view, node => names.Add(node.NodeType + ":" + node.LocalName));
            using var match = view.Document.MatchConsumption(view, new NodeKindTest(null));
            Assert.Equal("abd", view.Value);
            Assert.Equal(new[] { "Text:", "Element:n", "Text:", "Comment:", "ProcessingInstruction:pi", "Text:" }, names);
            Assert.True(match.Matched);
            Assert.True(view.MoveToNext()); Assert.Equal("after", view.LocalName);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void AnEnclosingReplayRetainsTheConsumedValueAndItsDescendants(int capacity)
    {
        const string xml = "<doc><value>a<n>b</n>c</value><after/></doc>";
        Run(xml, capacity, view => {
            Assert.True(view.MoveToFirstChild());
            var root = (StreamedNavigator)view.Clone();
            using (root.Document.BeginConsumption(root.PositionNode, replay: true, forceReplay: true))
            {
                Assert.True(view.MoveToFirstChild()); Assert.Equal("abc", view.Value);
                Assert.True(view.MoveToNext()); Assert.Equal("after", view.LocalName);
                Assert.False(view.MoveToNext());
            }
            Assert.True(root.MoveToFirstChild());
            Assert.Equal("abc", root.Value);
            Assert.True(root.MoveToFirstChild()); Assert.Equal("a", root.Value);
            Assert.True(root.MoveToNext()); Assert.Equal("n", root.LocalName); Assert.Equal("b", root.Value);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void SourceFailureDuringAValueKeepsTheBorrowedReaderOpen(int capacity)
    {
        using var input = XmlReader.Create(new StringReader("<doc><value>a<n>b</wrong></value></doc>"));
        using var source = new StreamedDocument(input);
        using var fork = source.Fork((StreamedNavigator)source.CreateNavigator());
        using var cancellation = new CancellationTokenSource(Deadline);
        Assert.Throws<XmlException>(() => fork.Run(new Action<StreamedNavigator, CancellationToken>[] {
            (view, unusedToken) => { Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToFirstChild()); _ = view.Value; },
            (view, unusedToken) => { _ = view.Value; }
        }, capacity, cancellation.Token));
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(7, false)]
    [InlineData(64, false)]
    [InlineData(1, true)]
    [InlineData(7, true)]
    [InlineData(64, true)]
    public void FailureAndCancellationInterruptAValueWithoutStrandingOtherProngs(int capacity, bool cancel)
    {
        string xml = "<doc><value>" + string.Concat(Enumerable.Repeat("a<n>b</n>", 512)) + "</value></doc>";
        using var input = XmlReader.Create(new StringReader(xml));
        using var source = new StreamedDocument(input);
        using var fork = source.Fork((StreamedNavigator)source.CreateNavigator());
        using var cancellation = new CancellationTokenSource(Deadline);
        using var entered = new ManualResetEventSlim();
        var failure = new InvalidOperationException("prong failure");
        Action run = () => fork.Run(new Action<StreamedNavigator, CancellationToken>[] {
            (view, unusedToken) => {
                Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToFirstChild()); entered.Set();
                _ = view.Value;
            },
            (view, token) => {
                Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToFirstChild());
                Assert.True(entered.Wait(Deadline, token));
                if (cancel) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                throw failure;
            }
        }, capacity, cancellation.Token);
        if (cancel) Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(run).CancellationToken);
        else Assert.Same(failure, Assert.Throws<InvalidOperationException>(run));
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    private static void Run(string xml, int capacity, Action<StreamedNavigator> body)
    {
        using var input = XmlReader.Create(new StringReader(xml), new() { CloseInput = false }, "file:///fork/values.xml");
        using var source = new StreamedDocument(input);
        using var fork = source.Fork((StreamedNavigator)source.CreateNavigator());
        using var cancellation = new CancellationTokenSource(Deadline);
        fork.Run(new Action<StreamedNavigator, CancellationToken>[] {
            (view, unusedToken) => body(view),
            (view, unusedToken) => { Thread.Yield(); body(view); },
        }, capacity, cancellation.Token);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }
}
