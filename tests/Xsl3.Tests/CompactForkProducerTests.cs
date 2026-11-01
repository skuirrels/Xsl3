// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class CompactForkProducerTests
{
    private static ForkInputProjection Projection(bool compact) => new(new string?[] {
        "-1", "", "", "0", "0", "doc", "", "0", "1", "row", "", "0", "2", "value", "", "1",
    }, allowCompactProducer: compact);

    private static string Input(int rows)
    {
        var text = new StringBuilder("<doc xmlns:p='urn:outer' xml:lang='en'>");
        for (int i = 0; i < rows; i++) text.Append("<row id='").Append(i).Append("'><ignored><leaf/></ignored><value xmlns:p='urn:inner'>before<![CDATA[middle]]>after<p:n/><!--comment--><?pi q?>last</value></row>");
        return text.Append("</doc>").ToString();
    }

    private static (List<string>[] Rows, StreamedNavigator?[] First, bool[] Active) Read(bool compact, int capacity, bool early = false, string flavor = "plain")
    {
        using var input = new StringReader(Input(200));
        using var original = XmlReader.Create(input, new XmlReaderSettings {
            DtdProcessing = flavor == "dtd" ? DtdProcessing.Parse : DtdProcessing.Prohibit,
        }, "file:///compact/input.xml");
        using var custom = flavor == "custom" ? new CustomReader(original) : null;
        XmlReader reader = custom ?? original;
        using var document = new StreamedDocument(reader);
        if (flavor == "observer") document.Observe(new Observer());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var caller = new ForkCaller(cancellation.Token);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator(), Projection(compact));
        var rows = new[] { new List<string>(), new List<string>() };
        var first = new StreamedNavigator?[2]; var active = new bool[2];
        fork.Run(Enumerable.Range(0, 2).Select<int, Action<StreamedNavigator, CancellationToken>>(index => (view, _) => {
            Assert.True(view.MoveToFirstChild());
            Assert.True(view.MoveToFirstChild());
            do
            {
                first[index] ??= (StreamedNavigator)view.Clone();
                string prefix = view.GetAttribute("id", "") + ":" + view.PositionNode.Number + ":" + view.BaseURI + ":" + view.XmlLang + ":" + view.GetNamespace("p");
                Assert.True(view.MoveToChild("value", ""));
                Assert.Equal("value", view.LocalName);
                prefix += ":" + view.GetNamespace("p") + ":" + view.PositionNode.Number;
                string value = view.Value;
                rows[index].Add(prefix + ":" + value);
                active[index] = caller.Invoke(() => document.HasCompactForkProducer, cancellation.Token);
                Assert.True(view.MoveToParent());
                if (early) break;
            } while (view.MoveToNext());
        }).ToArray(), capacity, cancellation.Token, caller);
        Assert.False(document.HasCompactForkProducer);
        document.CompleteSubtree(document.Root);
        Assert.True(document.Root.Ended);
        Assert.True(reader.EOF);
        Assert.NotEqual(ReadState.Closed, original.ReadState);
        return (rows, first, active);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void CompactHeadersPreserveNumbersTextNamespacesAndCrossProngIdentity(int capacity)
    {
        var baseline = Read(false, capacity);
        var compact = Read(true, capacity);
        for (int i = 0; i < 2; i++)
        {
            Assert.False(baseline.Active[i]); Assert.True(compact.Active[i]);
            Assert.Equal(200, compact.Rows[i].Count);
            Assert.Equal(baseline.Rows[i], compact.Rows[i]);
            Assert.EndsWith(":urn:inner:5:beforemiddleafterlast", compact.Rows[i][0]);
            Assert.Equal("0", compact.First[i]!.GetAttribute("id", ""));
            Assert.Equal("urn:outer", compact.First[i]!.GetNamespace("p"));
        }
        Assert.True(compact.First[0]!.IsSamePosition(compact.First[1]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void EarlyProngsRestoreTheSourceBeforeItsRemainingInputIsConsumed(int capacity)
    {
        var baseline = Read(false, capacity, early: true);
        var compact = Read(true, capacity, early: true);
        for (int i = 0; i < 2; i++)
        {
            Assert.Single(compact.Rows[i]); Assert.True(compact.Active[i]);
            Assert.Equal(baseline.Rows[i], compact.Rows[i]);
        }
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("dtd")]
    [InlineData("observer")]
    public void InputsWithoutThePrivateUntypedContractKeepOrdinaryFrames(string flavor)
    {
        var result = Read(true, 7, flavor: flavor);
        Assert.All(result.Active, Assert.False);
        Assert.All(result.Rows, rows => Assert.Equal(200, rows.Count));
    }

    [Fact]
    public void SourceErrorsRetainTheAcceptedPrefixAndBorrowedReader()
    {
        using var reader = XmlReader.Create(new StringReader("<doc><row id='0'><value>first</value></row><row id='1'><value>last</value>"));
        using var document = new StreamedDocument(reader);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator(), Projection(true));
        var seen = new[] { new List<string>(), new List<string>() };
        var bodies = Enumerable.Range(0, 2).Select<int, Action<StreamedNavigator, CancellationToken>>(index => (view, _) => {
            Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToFirstChild());
            do { Assert.True(view.MoveToFirstChild()); seen[index].Add(view.Value); Assert.True(view.MoveToParent()); }
            while (view.MoveToNext());
        }).ToArray();
        Assert.ThrowsAny<XmlException>(() => fork.Run(bodies, 1));
        Assert.All(seen, values => Assert.Equal(new[] { "first", "last" }, values));
        Assert.False(document.HasCompactForkProducer);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Fact]
    public void AStockWrapperCannotHideCustomDtdIdentityFromTheOrdinaryPath()
    {
        const string xml = "<!DOCTYPE doc [<!ELEMENT doc (row*)><!ELEMENT row (value)><!ATTLIST row id ID #REQUIRED><!ELEMENT value (#PCDATA)>]><doc><row id='r'><value>7</value></row></doc>";
        foreach (bool compact in new[] { false, true })
        {
            using var parser = new MisreportedDtdParser(xml) { DtdProcessing = DtdProcessing.Parse };
            using var reader = XmlReader.Create(parser, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true });
            Assert.Equal(typeof(XmlReader).Assembly, reader.GetType().Assembly);
            using var document = new StreamedDocument(reader);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var caller = new ForkCaller(cancellation.Token);
            using var fork = document.Fork((StreamedNavigator)document.CreateNavigator(), Projection(compact));
            var bodies = Enumerable.Range(0, 2).Select<int, Action<StreamedNavigator, CancellationToken>>(_ => (view, _) => {
                Assert.True(view.MoveToFirstChild());
                Assert.True(view.MoveToChild("row", ""));
                Assert.Equal("r", view.GetAttribute("id", ""));
                Assert.True(view.Document.IsIdAttribute(view.PositionNode, 0));
                Assert.False(caller.Invoke(() => document.HasCompactForkProducer, cancellation.Token));
                Assert.True(view.MoveToChild("value", ""));
                Assert.Equal("7", view.Value);
            }).ToArray();
            fork.Run(bodies, 7, cancellation.Token, caller);
            document.CompleteSubtree(document.Root);
            Assert.True(reader.EOF);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeepIgnoredScopesPreserveHeadersAndRestoreOrdinaryContinuation(bool early)
    {
        var input = new StringBuilder("<doc xmlns:p='urn:outer' xml:lang='en'><row id='0'><ignored>");
        for (int i = 0; i < 100; i++) input.Append("<n xmlns:p='urn:deep").Append(i).Append("' xml:lang='deep").Append(i).Append("'>");
        input.Append("discard");
        for (int i = 0; i < 100; i++) input.Append("</n>");
        input.Append("</ignored><value xmlns:p='urn:value'>first<![CDATA[second]]><p:n/>tail</value></row><row id='1'><value>next</value></row></doc>");
        List<string>[] ReadDeep(bool compact)
        {
            using var reader = XmlReader.Create(new StringReader(input.ToString()), new XmlReaderSettings(), "file:///compact/deep.xml");
            using var document = new StreamedDocument(reader);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var caller = new ForkCaller(cancellation.Token);
            using var fork = document.Fork((StreamedNavigator)document.CreateNavigator(), Projection(compact));
            var rows = new[] { new List<string>(), new List<string>() };
            fork.Run(Enumerable.Range(0, 2).Select<int, Action<StreamedNavigator, CancellationToken>>(index => (view, _) => {
                Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToChild("row", ""));
                do
                {
                    string id = view.GetAttribute("id", "");
                    Assert.True(view.MoveToChild("value", ""));
                    rows[index].Add(id + ":" + view.PositionNode.Number + ":" + view.Value + ":" + view.GetNamespace("p") + ":" + view.XmlLang + ":" + view.BaseURI);
                    Assert.Equal(compact, caller.Invoke(() => document.HasCompactForkProducer, cancellation.Token));
                    Assert.True(view.MoveToParent());
                    if (early) break;
                } while (view.MoveToNext());
            }).ToArray(), 7, cancellation.Token, caller);
            Assert.False(document.HasCompactForkProducer);
            document.CompleteSubtree(document.Root);
            Assert.True(document.Root.Ended); Assert.True(reader.EOF);
            return rows;
        }
        var baseline = ReadDeep(false); var actual = ReadDeep(true);
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(baseline[i], actual[i]);
            Assert.Equal(early ? 1 : 2, actual[i].Count);
            Assert.Contains(":firstsecondtail:urn:value:en:file:///compact/deep.xml", actual[i][0]);
        }
    }

    private sealed class MisreportedDtdParser(string xml) : XmlTextReader(new StringReader(xml))
    {
        public override XmlReaderSettings Settings => new() { DtdProcessing = DtdProcessing.Prohibit };
    }

    private sealed class CustomReader(XmlReader input) : XmlWrappingReader(input);
    private sealed class Observer : IStreamedNodeObserver
    {
        public void Start(StreamedNavigator node) { }
        public void End(StreamedNavigator node) { }
        public void Dispose() { }
    }
}
