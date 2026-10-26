// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
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

public class StreamedForkAccumulatorTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    // Exercise the actual initial-source observer and runtime entry points with native rule functions.
    // Compiled stylesheet fork coverage lives in StreamedForkTests.
    internal sealed class Source : IDisposable
    {
        internal readonly XmlReader Input;
        internal readonly XmlQueryRuntime Runtime;
        internal readonly XsltLibrary Library;
        internal readonly StreamedNavigator Focus;
        private readonly IList<XPathItem> _functions;
        private readonly int _table, _names;
        internal int Starts, Ends;

        internal Source(string xml, bool fail = false, bool cycle = false)
        {
            int thread = Environment.CurrentManagedThreadId;
            var constants = new ConstantTable();
            _table = constants.Add(new Dictionary<string, AccumulatorInfo> {
                ["Q{}n"] = new(0, new[] { false, true }, null, Streamable: true),
                ["Q{}unused"] = new(0, new[] { false, true }, null, OnPrincipal: false, Streamable: true),
                ["Q{}m"] = new(5, Array.Empty<bool>(), null, Streamable: true),
            });
            _names = constants.Add(new XsltNameContext(new Dictionary<string, string>(), new HashSet<(string, string, int)>()));
            var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) {
                Xslt2 = true, StreamedInitialMode = true, Constants = constants,
            };
            Input = XmlReader.Create(new StringReader(xml));
            Runtime = new XmlQueryRuntime(data, new InitialSource(Input, null, XslCompiledTransform.AbsentInput),
                XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter());
            Library = Runtime.XsltFunctions;
            _functions = new XPathItem[] {
                Function(1, _ => Integer(0)),
                Function(1, a => ((XPathNavigator)a[0][0]).LocalName == "row" ? Integer(0) : Array.Empty<XPathItem>()),
                Function(2, a => {
                    Assert.Equal(thread, Environment.CurrentManagedThreadId);
                    Starts++;
                    if (fail && Starts == 2) throw new XdmException(cycle ? "XTDE3400" : "TEST0001", "Accumulator rule failed") { AccumulatorCycle = cycle };
                    return Integer(((IntegerValue)a[1][0]).Integer + 1);
                }),
                Function(1, a => ((XPathNavigator)a[0][0]).LocalName == "row" ? Integer(0) : Array.Empty<XPathItem>()),
                Function(2, a => {
                    Assert.Equal(thread, Environment.CurrentManagedThreadId);
                    Ends++;
                    return Integer(((IntegerValue)a[1][0]).Integer + 10);
                }),
                Function(1, _ => Integer(7)),
            };
            Focus = (StreamedNavigator)Library.X31InitialObservedSource(_functions, _table);
        }

        private static NativeFunction Function(int arity, Func<IList<XPathItem>[], IList<XPathItem>> body) =>
            new(Enumerable.Repeat(SequenceTypeTest.AnySequence, arity).ToArray(), SequenceTypeTest.AnySequence, body, "test accumulator rule");

        private static IList<XPathItem> Integer(System.Numerics.BigInteger value) => new XPathItem[] { AtomicValue.OfInteger(value) };

        internal string Value(XPathNavigator node, bool after = false, string name = "n") =>
            Assert.Single(Items(node, after, name)).Value;

        internal IList<XPathItem> Items(XPathNavigator node, bool after = false, string name = "n") =>
            Library.X31AccumulatorValue(_functions, _table, name, _names, new XPathItem[] { node }, after, 0);

        internal void Run(StreamedNavigator focus, params Action<StreamedNavigator>[] bodies)
        {
            using var cancellation = new CancellationTokenSource(Deadline);
            using var fork = focus.Document.Fork(focus);
            fork.Run(bodies.Select<Action<StreamedNavigator>, Action<StreamedNavigator, CancellationToken>>(body =>
                (view, _) => body(view)).ToArray(), 1, cancellation.Token);
        }

        internal void Close() => Runtime.ExternalContext.CloseInitialSource();

        public void Dispose()
        {
            Close();
            Runtime.CloseStreamedSources();
            Input.Dispose();
        }
    }

    [Fact]
    public void BufferedForkBindingsRetainPublishedAccumulatorPhases()
    {
        using var source = new Source("<doc><row>one</row><row>two</row></doc>");
        source.Run(source.Focus, view => {
            Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToFirstChild());
            source.Library.X31BufferForkItem(view);
            var bindings = source.Library.PrepareForkBindings(new XPathItem[] { view });
            var child = source.Runtime.CreateForkRuntime(new XmlCachedSequenceWriter());
            child.RunFork(r => {
                var node = Assert.IsType<StreamedNavigator>(Assert.Single(r.XsltFunctions.ProjectForkBindings(bindings)));
                Assert.Equal("one", node.Value);
                Assert.True(node.IsSamePosition(view));
                Assert.Equal("1", source.Value(node));
                Assert.Equal("11", source.Value(node, after: true));
            }, default, null);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProngsReadValuesWithoutDuplicatingRulesOrLosingEvents(bool element)
    {
        using var source = new Source("<doc><row>one</row><row>two</row></doc>");
        if (element) Assert.True(source.Focus.MoveToFirstChild());
        var values = new List<string>();
        source.Run(source.Focus,
            view => Assert.Equal("22", source.Value(view, after: true)),
            view => {
                if (!element) Assert.True(view.MoveToFirstChild());
                Assert.True(view.MoveToFirstChild());
                do {
                    values.Add(source.Value(view));
                    values.Add(source.Value(view, after: true));
                } while (view.MoveToNext());
            });
        Assert.Equal(new[] { "1", "11", "12", "22" }, values);
        Assert.Equal(2, source.Starts); Assert.Equal(2, source.Ends);
        Assert.NotEqual(ReadState.Closed, source.Input.ReadState);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CopiesKeepDescendantValuesAfterViewsAndOriginalInputClose(int kind)
    {
        using var source = new Source("<doc><row>one</row><row>two</row></doc>");
        XPathNavigator? copy = null;
        source.Run(source.Focus, view => copy = (XPathNavigator)Assert.Single(kind switch {
                0 => source.Library.X31Snapshot(new XPathItem[] { view }, false),
                1 => source.Library.X31CopyWithAccumulators(new XPathItem[] { view }),
                _ => source.Library.X31CopyPreserving(new XPathItem[] { view }, true),
            }),
            view => Assert.Equal("onetwo", view.Value));
        source.Close();
        Assert.Equal("0", source.Value(copy!));
        Assert.Equal("22", source.Value(copy!, after: true));
        Assert.Equal("7", source.Value(copy!, after: true, name: "m"));
        Assert.True(copy!.MoveToFirstChild()); Assert.True(copy.MoveToFirstChild());
        Assert.Equal("1", source.Value(copy)); Assert.Equal("11", source.Value(copy, after: true));
        Assert.True(copy.MoveToFirstChild());
        Assert.Equal("1", source.Value(copy)); Assert.Equal("1", source.Value(copy, after: true));
        Assert.True(copy.MoveToParent()); Assert.True(copy.MoveToNext());
        Assert.Equal("12", source.Value(copy)); Assert.Equal("22", source.Value(copy, after: true));
    }

    [Fact]
    public void NestedForksUseTheSameAccumulatorObserver()
    {
        using var source = new Source("<doc><row>one</row><row>two</row></doc>");
        source.Run(source.Focus,
            view => source.Run(view, nested => Assert.Equal("22", source.Value(nested, true)), nested => Assert.Equal("onetwo", nested.Value)),
            view => Assert.Equal("22", source.Value(view, true)));
        Assert.Equal(2, source.Starts); Assert.Equal(2, source.Ends);
    }

    [Fact]
    public void HeaderOnlyValuesDoNotReadInputAndApplicabilityIsPreserved()
    {
        using var source = new Source("<doc><row/></doc>");
        source.Run(source.Focus,
            view => Assert.Equal("0", source.Value(view)),
            view => Assert.Contains("XTDE3362", Assert.Throws<XdmException>(() => source.Value(view, name: "unused")).Message));
        Assert.Equal(ReadState.Initial, source.Input.ReadState);
        Assert.Equal(0, source.Starts); Assert.Equal(0, source.Ends);
    }

    [Fact]
    public void ReadersAtDifferentPacesSharePublishedPhasesAcrossManyNodes()
    {
        const int count = 1000;
        using var source = new Source("<doc>" + string.Concat(Enumerable.Repeat("<row>v</row>", count)) + "</doc>");
        int visited = 0;
        source.Run(source.Focus,
            view => Assert.Equal((11 * count).ToString(), source.Value(view, true)),
            view => {
                Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToFirstChild());
                do {
                    Assert.Equal((11 * visited + 1).ToString(), source.Value(view));
                    Assert.Equal("7", source.Value(view, name: "m"));
                    Assert.Equal((11 * ++visited).ToString(), source.Value(view, true));
                } while (view.MoveToNext());
            });
        Assert.Equal(count, visited); Assert.Equal(count, source.Starts); Assert.Equal(count, source.Ends);
    }

    [Fact]
    public void GroundingWithoutAccumulatorCopiesDoesNotRecordReplayFrames()
    {
        using var source = new Source("<doc><row>one</row><row>two</row></doc>");
        source.Run(source.Focus, view => {
            Assert.Equal("onetwo", view.Ground(copyAccumulators: false).Value);
            Assert.Null(view.PositionNode.Replay);
        }, view => Assert.Equal("22", source.Value(view, true)));
    }

    [Fact]
    public void PublishedPhasesDoNotKeepConsumedSourceFramesAlive()
    {
        using var source = new Source("<doc><row>one</row><row>two</row></doc>");
        WeakReference first = ObserveFirstFrame(source);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(first.IsAlive);
        GC.KeepAlive(source); // The original observer and its phase cache are still live.
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ObserveFirstFrame(Source source)
    {
        WeakReference? first = null;
        source.Run(source.Focus, view => {
            Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToFirstChild());
            first = new WeakReference(view.PositionNode.Original!);
            Assert.Equal("1", source.Value(view));
        }, view => Assert.Equal("22", source.Value(view, true)));
        return first!;
    }

    [Fact]
    public void AncestorAfterCannotConsumeBeyondTheForkBoundary()
    {
        using var source = new Source("<doc><group><row>one</row></group><row>two</row></doc>");
        Assert.True(source.Focus.MoveToFirstChild()); Assert.True(source.Focus.MoveToFirstChild());
        source.Run(source.Focus, view => {
            XPathNavigator ancestor = view.Clone(); Assert.True(ancestor.MoveToParent());
            Assert.Equal("0", source.Value(ancestor));
            Assert.Contains("stream-position", Assert.Throws<XdmException>(() => source.Value(ancestor, true)).Message);
            Assert.Equal("11", source.Value(view, true));
        });
        Assert.True(source.Focus.MoveToNext()); Assert.Equal("row", source.Focus.LocalName);
        Assert.Equal("12", source.Value(source.Focus));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredErrorsAndCycleErrorsRetainTheirOriginalCodes(bool cycle)
    {
        using var source = new Source("<doc><row>one</row><row>two</row></doc>", fail: true, cycle: cycle);
        source.Run(source.Focus, view => {
            Assert.Equal("onetwo", view.Value);
            Assert.Equal("7", source.Value(view, true, "m"));
            if (cycle) Assert.Contains("XTDE3400", Assert.Throws<XdmException>(() => source.Value(view)).Message);
            else Assert.Equal("0", source.Value(view));
            Assert.Contains(cycle ? "XTDE3400" : "TEST0001", Assert.Throws<XdmException>(() => source.Value(view, true)).Message);
        });
    }
}
