// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class PrunedForkFrameTests
{
    private static ForkInputProjection Projection() => new(new string?[] {
        "-1", "", "", "0", "0", "row", "", "0",
    });

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void OmittedMixedContentKeepsFollowingPositionIdentity(int capacity)
    {
        const string xml = "<doc><omitted> A<![CDATA[B]]> C<!--c--><?p v?><nested><leaf/></nested></omitted>"
            + "<row id='one'>X<![CDATA[Y]]><n/> Z</row><row id='two'><empty></empty></row></doc><after/>";
        static (long[] Rows, long After) Read(string xml, int capacity, bool projected)
        {
            using var reader = XmlReader.Create(new StringReader(xml), new() { ConformanceLevel = ConformanceLevel.Fragment });
            using var document = new StreamedDocument(reader);
            var position = (StreamedNavigator)document.CreateNavigator();
            Assert.True(position.MoveToFirstChild());
            var rows = new List<long>();
            using (var fork = document.Fork(position, projected ? Projection() : null))
                fork.Run(new Action<StreamedNavigator, CancellationToken>[] { (view, _) => {
                    Assert.True(view.MoveToFirstChild());
                    do { if (view.LocalName == "row") rows.Add(view.PositionNode.Number); } while (view.MoveToNext());
                } }, capacity);
            Assert.True(position.MoveToNext());
            Assert.Equal("after", position.LocalName);
            return (rows.ToArray(), position.PositionNode.Number);
        }
        var baseline = Read(xml, capacity, false);
        var candidate = Read(xml, capacity, true);
        Assert.Equal(2, candidate.Rows.Length);
        Assert.Equal(baseline.Rows, candidate.Rows);
        Assert.Equal(baseline.After, candidate.After);
    }

    private sealed class CallbackReader(XmlReader reader, Action<CallbackReader> callback) : XmlWrappingReader(reader)
    {
        public override bool Read()
        {
            bool result = base.Read();
            callback(this);
            return result;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EarlyCompletionCanFinishTheOriginalBoundaryWithoutLosingParserDepth(bool kept)
    {
        using var finished = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int interiors = 0;
        string name = kept ? "row" : "omitted";
        string xml = "<doc><row/><" + name + "><inside>"
            + string.Concat(Enumerable.Repeat("<leaf/>", 200)) + "</inside></" + name + "></doc><after/>";
        using var reader = new CallbackReader(XmlReader.Create(new StringReader(xml),
            new() { ConformanceLevel = ConformanceLevel.Fragment }), input => {
            if (input.LocalName == "leaf" && ++interiors == 7) Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
        });
        using var document = new StreamedDocument(reader);
        var position = (StreamedNavigator)document.CreateNavigator();
        Assert.True(position.MoveToFirstChild());
        using (var fork = document.Fork(position, Projection()))
            fork.Run(new Action<StreamedNavigator, CancellationToken>[] { (view, _) => {
                try { Assert.True(view.MoveToFirstChild()); Assert.Equal("row", view.LocalName); }
                finally { finished.Set(); }
            } }, 7, cancellation.Token);
        Assert.InRange(interiors, 1, 7);
        document.CompleteConsumption(position.PositionNode);
        Assert.True(position.PositionNode.Ended);
        Assert.Equal(200, interiors);
        Assert.True(position.MoveToNext());
        Assert.Equal("after", position.LocalName);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EarlyCompletionStillReportsLaterMalformedContent(bool kept)
    {
        string name = kept ? "row" : "omitted";
        string xml = "<doc><row/><" + name + "><inside>"
            + string.Concat(Enumerable.Repeat("<leaf/>", 200)) + "<bad></wrong></inside></" + name + "></doc>";
        using var reader = XmlReader.Create(new StringReader(xml));
        using var document = new StreamedDocument(reader);
        var position = (StreamedNavigator)document.CreateNavigator();
        Assert.True(position.MoveToFirstChild());
        using (var fork = document.Fork(position, Projection()))
            fork.Run(new Action<StreamedNavigator, CancellationToken>[] { (view, _) => {
                Assert.True(view.MoveToFirstChild()); Assert.Equal("row", view.LocalName);
            } }, 7);
        Assert.Throws<XmlException>(() => document.CompleteConsumption(position.PositionNode));
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingHeaderObserversStillReceiveOmittedDescendants(bool registeredAfterForkCreation)
    {
        using var reader = XmlReader.Create(new StringReader("<doc><omitted><inside/></omitted><row><value/></row></doc>"));
        using var document = new StreamedDocument(reader);
        var position = (StreamedNavigator)document.CreateNavigator();
        Assert.True(position.MoveToFirstChild());
        var names = new List<string>();
        using var earlyFork = registeredAfterForkCreation ? document.Fork(position, Projection()) : null;
        using var headers = document.ObserveConsumedHeaders(position, node => names.Add(node.LocalName));
        using var lateFork = registeredAfterForkCreation ? null : document.Fork(position, Projection());
        var fork = earlyFork ?? lateFork!;
        fork.Run(new Action<StreamedNavigator, CancellationToken>[] { (view, _) => {
            Assert.True(view.MoveToFirstChild()); Assert.Equal("row", view.LocalName); Assert.False(view.MoveToNext());
        } }, 7);
        Assert.Equal(new[] { "omitted", "inside", "row", "value" }, names);
    }
}
