// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.Tree;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class StreamedAccumulatorOpenReplayTests
{
    [Theory]
    [InlineData(16, true, false)]
    [InlineData(128, true, false)]
    [InlineData(16, false, false)]
    [InlineData(128, false, false)]
    [InlineData(16, true, true)]
    [InlineData(128, true, true)]
    [InlineData(16, false, true)]
    [InlineData(128, false, true)]
    public void CopiesOfCompletedRowsDetachWhenTheirOuterReplayEnds(int rows, bool complete, bool closeBeforeEnd)
    {
        var (copies, payloads) = CopyRowsInsideOuterReplay(rows, complete, closeBeforeEnd);
        Collect();
        Assert.Equal(2, payloads.Count(reference => reference.TryGetTarget(out _)));
        copies.Clear();
        Collect();
        Assert.All(payloads, reference => Assert.False(reference.TryGetTarget(out _)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (List<XPathNavigator> Copies, List<WeakReference<CompactDocument>> Payloads) CopyRowsInsideOuterReplay(int rows, bool complete, bool closeBeforeEnd)
    {
        var payloads = new List<WeakReference<CompactDocument>>();
        var copies = new List<XPathNavigator>();
        using var input = XmlReader.Create(new StringReader("<doc>" + string.Concat(Enumerable.Range(1, complete ? rows : rows + 16).Select(row => "<row id='" + row + "'/>")) + "</doc>"));
        using var source = new StreamedForkAccumulatorTests.Source(input, initialValue: Array.Empty<XPathItem>(), rule: (node, previous, after) => {
            if (!after) return previous;
            return AppendPayload(node, previous, payloads);
        });
        using (source.Focus.BeginConsumption(replay: true, accumulators: true))
        {
            Assert.True(source.Focus.MoveToFirstChild()); Assert.True(source.Focus.MoveToFirstChild());
            int row = 0;
            do
            {
                row++;
                var copy = Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(source.Library.X31CopyWithAccumulators(new XPathItem[] { source.Focus })));
                Assert.Equal(row, Assert.IsType<MapItem>(Assert.Single(source.Items(copy, after: true))).Count);
                if (row <= 2) copies.Add(copy);
            } while (row < rows && source.Focus.MoveToNext());
            Assert.Equal(rows, row);
            if (complete) source.Focus.Document.CompleteSubtree(source.Focus.Document.Root);
            if (closeBeforeEnd) source.Close();
        }
        Assert.Equal(complete, StreamedDocument.CanReplay(source.Focus.Document.Root));
        source.Close();
        for (int i = 0; i < copies.Count; i++)
            Assert.Equal(i + 1, Assert.IsType<MapItem>(Assert.Single(source.Items(copies[i], after: true))).Count);
        return (copies, payloads);
    }

    [Fact]
    public void AnOpenReplayDoesNotOwnDiscardedCopies()
    {
        using var source = new StreamedForkAccumulatorTests.Source("<doc><row/><row/></doc>");
        using (source.Focus.BeginConsumption(replay: true, accumulators: true))
        {
            Assert.True(source.Focus.MoveToFirstChild()); Assert.True(source.Focus.MoveToFirstChild());
            var copy = DiscardCopy(source);
            Collect();
            Assert.False(copy.TryGetTarget(out _));
            source.Focus.Document.CompleteSubtree(source.Focus.Document.Root);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<CompactDocument> DiscardCopy(StreamedForkAccumulatorTests.Source source)
    {
        var copy = Assert.IsType<CompactNavigator>(Assert.Single(source.Library.X31CopyWithAccumulators(new XPathItem[] { source.Focus })));
        return new(copy.Document);
    }

    [Theory]
    [InlineData("rule-error")]
    [InlineData("cycle")]
    [InlineData("cancel")]
    public void FaultedOuterCapturesReleaseUnrelatedGraphsAndPreserveCopiedValues(string ending)
    {
        var (copies, payloads) = CopyRowsBeforeFailure(ending);
        Collect();
        Assert.Equal(2, payloads.Count(reference => reference.TryGetTarget(out _)));
        copies.Clear();
        Collect();
        Assert.All(payloads, reference => Assert.False(reference.TryGetTarget(out _)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (List<XPathNavigator> Copies, List<WeakReference<CompactDocument>> Payloads) CopyRowsBeforeFailure(string ending)
    {
        var payloads = new List<WeakReference<CompactDocument>>();
        var copies = new List<XPathNavigator>();
        using var cancellation = new CancellationTokenSource();
        using var input = XmlReader.Create(new StringReader("<doc>" + string.Concat(Enumerable.Range(1, 32).Select(row => "<row id='" + row + "'/>")) + "</doc>"));
        using var source = new StreamedForkAccumulatorTests.Source(input, initialValue: Array.Empty<XPathItem>(), rule: (node, previous, after) => {
            if (!after) return previous;
            if (node.GetAttribute("id", "") == "16")
            {
                if (ending == "cancel") { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
                throw new XdmException(ending == "cycle" ? "XTDE3400" : "TEST0001", "Outer capture rule failed") {
                    AccumulatorCycle = ending == "cycle", ErrorObject = ending == "rule-error" ? previous : null,
                };
            }
            return AppendPayload(node, previous, payloads);
        });
        using (source.Focus.BeginConsumption(replay: true, accumulators: true))
        {
            Assert.True(source.Focus.MoveToFirstChild()); Assert.True(source.Focus.MoveToFirstChild());
            void Consume()
            {
                int row = 0;
                do
                {
                    row++;
                    var copy = Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(source.Library.X31CopyWithAccumulators(new XPathItem[] { source.Focus })));
                    Assert.Equal(row, Assert.IsType<MapItem>(Assert.Single(source.Items(copy, after: true))).Count);
                    if (row <= 2) copies.Add(copy);
                } while (source.Focus.MoveToNext());
            }
            if (ending == "cancel") Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(Consume).CancellationToken);
            else Assert.Equal(ending == "cycle" ? "XTDE3400" : "TEST0001", Assert.Throws<XdmException>(Consume).Code);
            source.Close();
        }
        Assert.False(StreamedDocument.CanReplay(source.Focus.Document.Root));
        for (int i = 0; i < copies.Count; i++)
            if (ending == "cycle") Assert.Equal("XTDE3400", Assert.Throws<XdmException>(() => source.Items(copies[i], after: true)).Code);
            else Assert.Equal(i + 1, Assert.IsType<MapItem>(Assert.Single(source.Items(copies[i], after: true))).Count);
        return (copies, payloads);
    }

    private static IList<XPathItem> AppendPayload(XPathNavigator node, IList<XPathItem> previous, List<WeakReference<CompactDocument>> payloads)
    {
        var payload = XdmNode.Load(new StringReader("<payload id='" + node.GetAttribute("id", "") + "'/>" )).Navigator;
        Assert.True(payload.MoveToFirstChild());
        payloads.Add(new(Assert.IsType<CompactNavigator>(payload).Document));
        var map = previous.Count == 0 ? MapItem.Empty : Assert.IsType<MapItem>(Assert.Single(previous));
        return new XPathItem[] { map.Put(AtomicValue.OfString(node.GetAttribute("id", "")), new XPathItem[] { payload }) };
    }

    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
}
