// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.Tree;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class GroundedAccumulatorTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static IList<XPathItem> Integer(BigInteger value) => new XPathItem[] { AtomicValue.OfInteger(value) };

    private sealed class Fixture
    {
        internal readonly XmlQueryRuntime Runtime;
        internal readonly XPathNavigator Root, First, Second;
        private readonly int _table, _names;
        private readonly IList<XPathItem> _functions;
        internal int Starts, Ends, Initials;
        internal Action<XmlQueryRuntime, XPathNavigator>? Start;
        internal Func<XmlQueryRuntime, XPathNavigator, IList<XPathItem>>? Initial;
        internal Func<XmlQueryRuntime, XPathNavigator, IList<XPathItem>>? OtherInitial;
        internal Fixture(string xml = "<doc><row/><row/></doc>", XmlResolver? resolver = null, bool compact = true)
        {
            var constants = new ConstantTable();
            _table = constants.Add(new Dictionary<string, AccumulatorInfo> {
                ["Q{}n"] = new(0, new[] { false, true }, null),
                ["Q{}limited"] = new(0, new[] { false, true }, null, OnPrincipal: false),
                ["Q{}m"] = new(5, Array.Empty<bool>(), null),
            });
            _names = constants.Add(new XsltNameContext(new Dictionary<string, string>(), new HashSet<(string, string, int)>()));
            var manager = new StaticDataManager(); manager.DeclareGlobalValue("g");
            var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), manager) { Xslt2 = true, CompactTree = compact, Constants = constants };
            using var input = XmlReader.Create(new StringReader(xml));
            Runtime = new(data, input, resolver ?? XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter());
            Root = Runtime.ExternalContext.DefaultDataSource;
            First = Root.SelectSingleNode("/doc/row[1]") ?? Root;
            Second = Root.SelectSingleNode("/doc/row[2]") ?? Root;
            LibraryFunction Function(int arity, FunctionBody body) => new(new FunctionDefinition(0, new QName("urn:fixture", "rule"),
                Enumerable.Range(0, arity).Select(i => ("a" + i, SequenceTypeTest.AnySequence)).ToArray(), SequenceTypeTest.AnySequence, body, false), arity, Runtime.XsltFunctions.X31Context);
            _functions = new XPathItem[] {
                Function(1, (c, a) => { Interlocked.Increment(ref Initials); return Initial?.Invoke(c.Runtime!, (XPathNavigator)a[0][0]) ?? Integer(0); }),
                Function(1, (_, a) => ((XPathNavigator)a[0][0]).LocalName == "row" ? Integer(0) : Array.Empty<XPathItem>()),
                Function(2, (c, a) => {
                    Interlocked.Increment(ref Starts); Start?.Invoke(c.Runtime!, (XPathNavigator)a[0][0]);
                    return Integer(((IntegerValue)a[1][0]).Integer + 1);
                }),
                Function(1, (_, a) => ((XPathNavigator)a[0][0]).LocalName == "row" ? Integer(0) : Array.Empty<XPathItem>()),
                Function(2, (_, a) => { Interlocked.Increment(ref Ends); return Integer(((IntegerValue)a[1][0]).Integer + 10); }),
                Function(1, (c, a) => OtherInitial?.Invoke(c.Runtime!, (XPathNavigator)a[0][0]) ?? Integer(7)),
            };
        }
        internal XmlQueryRuntime Child() => Runtime.CreateForkRuntime(new XmlCachedSequenceWriter());
        internal IList<XPathItem> Value(XmlQueryRuntime runtime, XPathNavigator node, bool after = false, string name = "n") =>
            runtime.XsltFunctions.X31AccumulatorValue(_functions, _table, name, _names, new XPathItem[] { node }, after, 0);
        internal string Text(XmlQueryRuntime runtime, XPathNavigator node, bool after = false, string name = "n") => Assert.Single(Value(runtime, node, after, name)).Value;
    }

    private static void WithRoot(Action<CancellationToken> body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() => {
            try { using var limit = new CancellationTokenSource(Deadline); XmlQueryRuntime.Cancellation = limit.Token; body(limit.Token); }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        }) { IsBackground = true };
        thread.Start(); Assert.True(thread.Join(Deadline + Deadline)); failure?.Throw();
    }
    private static void Run(CancellationToken token, params Action<CancellationToken>[] bodies) =>
        ForkProngs.Run<object>(() => null, bodies.Select<Action<CancellationToken>, Action<StreamTee<object>.Reader, CancellationToken>>(body => (_, t) => body(t)).ToArray(), 1, token);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProngsShareOneEvaluationAndKeepPreviouslyComputedPhases(bool precomputed)
    {
        WithRoot(token => {
            var fixture = new Fixture();
            if (precomputed) Assert.Equal("1", fixture.Text(fixture.Runtime, fixture.First));
            var children = Enumerable.Range(0, 3).Select(_ => fixture.Child()).ToArray();
            using var barrier = new Barrier(children.Length);
            Run(token, children.Select<XmlQueryRuntime, Action<CancellationToken>>(child => t => child.RunFork(r => {
                Assert.True(barrier.SignalAndWait(Deadline, t));
                Assert.Equal("1", fixture.Text(r, fixture.First)); Assert.Equal("11", fixture.Text(r, fixture.First, true));
                Assert.Equal("12", fixture.Text(r, fixture.Second)); Assert.Equal("22", fixture.Text(r, fixture.Second, true));
            }, t, null)).ToArray());
            Assert.Equal(1, fixture.Initials); Assert.Equal(2, fixture.Starts); Assert.Equal(2, fixture.Ends);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OrdinaryErrorsKeepDefinedPhasesButCircularErrorsInvalidateThem(bool shared, bool circular)
    {
        var fixture = new Fixture(); var runtime = shared ? fixture.Child() : fixture.Runtime;
        var error = new XdmException(circular ? "XTDE3400" : "TEST0001", "rule failed") { AccumulatorCycle = circular };
        fixture.Start = (_, node) => { if (node.IsSamePosition(fixture.Second)) throw error; };
        if (circular) Assert.Same(error, Assert.Throws<XdmException>(() => fixture.Value(runtime, fixture.First)));
        else { Assert.Equal("1", fixture.Text(runtime, fixture.First)); Assert.Equal("11", fixture.Text(runtime, fixture.First, true)); }
        Assert.Same(error, Assert.Throws<XdmException>(() => fixture.Value(runtime, fixture.Second, true)));
        Assert.Equal(2, fixture.Starts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationDiscardsPartialPhasesAndAllowsAFreshEvaluation(bool shared)
    {
        var fixture = new Fixture(); var runtime = shared ? fixture.Child() : fixture.Runtime;
        bool cancel = true;
        fixture.Start = (_, node) => { if (cancel && node.IsSamePosition(fixture.Second)) { cancel = false; throw new OperationCanceledException(); } };
        Assert.Throws<OperationCanceledException>(() => fixture.Value(runtime, fixture.First));
        Assert.Equal("22", fixture.Text(runtime, fixture.Second, true));
        Assert.Equal(2, fixture.Initials); Assert.Equal(4, fixture.Starts); Assert.Equal(3, fixture.Ends);
    }

    [Fact]
    public void AFailedLaterPhaseDoesNotLeaveConstructionOrAnnotationStateBehind()
    {
        var fixture = new Fixture(); var runtime = fixture.Child();
        var output = runtime.Output;
        runtime.AnnotationScopes = new Stack<bool>(); runtime.AnnotationScopes.Push(true);
        fixture.Start = (r, node) => {
            if (!node.IsSamePosition(fixture.Second)) return;
            r.StartSequenceConstruction(out var temporary);
            temporary.WriteStartElement("", "unfinished", "");
            r.AnnotationScopes!.Push(false); r.PendingAnyType = true;
            r.TryLines.Add(99); r.TraceStack.Add(new XsltFrame("function", null, null, null, "urn:fixture", 99, 1));
            XmlQueryRuntime.EnterFunctionCounted(10);
            throw new XdmException("TEST0001", "after a known phase");
        };
        int depth = XmlQueryRuntime.Depth;
        Assert.Equal("1", fixture.Text(runtime, fixture.First));
        Assert.Same(output, runtime.Output); Assert.Equal(depth, XmlQueryRuntime.Depth);
        Assert.True(Assert.Single(runtime.AnnotationScopes!)); Assert.False(runtime.PendingAnyType);
        Assert.Empty(runtime.TryLines); Assert.Empty(runtime.TraceStack);
    }

    [Fact]
    public void SuccessfulRuleConstructionPreservesTheEnclosingPendingType()
    {
        var fixture = new Fixture("<doc/>"); var runtime = fixture.Child();
        runtime.PendingAnyType = true;
        fixture.Initial = (r, _) => {
            r.StartSequenceConstruction(out var temporary);
            temporary.WriteStartElement("", "value", ""); temporary.WriteEndElement();
            return r.EndSequenceConstruction(out temporary);
        };
        Assert.Equal("value", Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(fixture.Value(runtime, fixture.Root))).LocalName);
        Assert.True(runtime.PendingAnyType);
    }

    [Fact]
    public void AStoredRuleErrorClosesItsInputsWhileKeepingTheCallersSourceOpen()
    {
        var sources = new Sources(); var fixture = new Fixture(resolver: sources); var runtime = fixture.Child();
        var caller = runtime.XsltFunctions.X31OpenStreamedSource("urn:caller", "", "", "", "");
        fixture.Start = (r, node) => {
            if (!node.IsSamePosition(fixture.Second)) return;
            r.XsltFunctions.X31OpenStreamedSource("urn:rule", "", "", "", "");
            throw new XdmException("TEST0001", "rule failure after opening an input");
        };
        Assert.Equal("1", fixture.Text(runtime, fixture.First));
        Assert.Equal(1, sources.Closed); Assert.Equal("", caller.Value);
        runtime.CloseStreamedSources(); Assert.Equal(2, sources.Closed);
    }

    [Fact]
    public void CompactMixedContentKeepsAllBeforeAndAfterValuesInReverseLookupOrder()
    {
        CheckMixedContentPhases(compact: true);
    }

    [Fact]
    public void LegacyMixedContentKeepsAllBeforeAndAfterValuesInReverseLookupOrder()
    {
        CheckMixedContentPhases(compact: false);
    }

    [Fact]
    public void ForeignMixedContentKeepsAllBeforeAndAfterValuesInReverseLookupOrder()
    {
        CheckMixedContentPhases(compact: false, foreign: true);
    }

    private static void CheckMixedContentPhases(bool compact, bool foreign = false)
    {
        const string xml = "<doc><!--first--><row><n/>x<?p t?></row><row/><tail>y</tail></doc>";
        var fixture = new Fixture(xml, compact: compact);
        XPathNavigator root = fixture.Root;
        if (foreign)
        {
            var document = new XmlDocument(); document.LoadXml(xml);
            root = document.CreateNavigator()!;
        }
        XPathNavigator[] nodes = root.Select("descendant-or-self::node()").Cast<XPathNavigator>().ToArray();
        int[] before = { 0, 0, 0, 1, 1, 1, 1, 12, 22, 22 };
        int[] after = { 22, 22, 0, 11, 1, 1, 1, 22, 22, 22 };
        Assert.Equal(before.Length, nodes.Length);
        for (int i = nodes.Length - 1; i >= 0; i--)
        {
            Assert.Equal(before[i].ToString(System.Globalization.CultureInfo.InvariantCulture), fixture.Text(fixture.Runtime, nodes[i]));
            Assert.Equal(after[i].ToString(System.Globalization.CultureInfo.InvariantCulture), fixture.Text(fixture.Runtime, nodes[i], true));
        }
        Assert.Equal(1, fixture.Initials); Assert.Equal(2, fixture.Starts); Assert.Equal(2, fixture.Ends);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManyChangesAndUnchangedDescendantsKeepTheirPhasesInReverseOrder(bool compact)
    {
        const int rows = 1200;
        var fixture = new Fixture("<doc>" + string.Concat(Enumerable.Repeat("<row><n/>text</row>", rows)) + "</doc>", compact: compact);
        XPathNavigator[] nodes = fixture.Root.Select("/doc/row").Cast<XPathNavigator>().ToArray();
        Assert.Equal(rows, nodes.Length);
        for (int i = rows - 1; i >= 0; i--)
        {
            string before = (i * 11 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string after = ((i + 1) * 11).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(before, fixture.Text(fixture.Runtime, nodes[i]));
            Assert.Equal(after, fixture.Text(fixture.Runtime, nodes[i], true));
            XPathNavigator child = nodes[i].SelectSingleNode("n")!;
            Assert.Equal(before, fixture.Text(fixture.Runtime, child));
            Assert.Equal(before, fixture.Text(fixture.Runtime, child, true));
        }
        Assert.Equal(1, fixture.Initials); Assert.Equal(rows, fixture.Starts); Assert.Equal(rows, fixture.Ends);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecursiveDemandsKeepCompletedPhasesButRejectUnrecordedEnds(bool compact)
    {
        var fixture = new Fixture("<doc><row><n/>text</row><row/></doc>", compact: compact);
        fixture.Start = (runtime, node) => {
            if (!node.IsSamePosition(fixture.Second)) return;
            Assert.Equal("0", fixture.Text(runtime, fixture.Root));
            Assert.Equal("1", fixture.Text(runtime, fixture.First));
            Assert.Equal("11", fixture.Text(runtime, fixture.First, true));
            Assert.Contains("XTDE3400", Assert.Throws<XdmException>(() => fixture.Value(runtime, fixture.Root, true)).Message);
            Assert.Contains("XTDE3400", Assert.Throws<XdmException>(() => fixture.Value(runtime, fixture.Second)).Message);
        };
        Assert.Equal("22", fixture.Text(fixture.Runtime, fixture.Root, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStoredRuleErrorDoesNotDefineUnvisitedPhases(bool compact)
    {
        var fixture = new Fixture("<doc><row/><row/><later/></doc>", compact: compact);
        var error = new XdmException("TEST0001", "failed before the next phase");
        fixture.Start = (_, node) => { if (node.IsSamePosition(fixture.Second)) throw error; };
        Assert.Equal("1", fixture.Text(fixture.Runtime, fixture.First));
        Assert.Equal("11", fixture.Text(fixture.Runtime, fixture.First, true));
        Assert.Equal("0", fixture.Text(fixture.Runtime, fixture.Root));
        Assert.Same(error, Assert.Throws<XdmException>(() => fixture.Value(fixture.Runtime, fixture.Root, true)));
        Assert.Same(error, Assert.Throws<XdmException>(() => fixture.Value(fixture.Runtime, fixture.Second)));
        Assert.Same(error, Assert.Throws<XdmException>(() => fixture.Value(fixture.Runtime, fixture.Root.SelectSingleNode("/doc/later")!)));
    }

    [Fact]
    public void AnAlreadyRecordedPhaseCanBeReadWithinItsOwnComputation()
    {
        var fixture = new Fixture();
        fixture.Start = (runtime, node) => {
            if (node.IsSamePosition(fixture.Second)) Assert.Equal("1", fixture.Text(runtime, fixture.First));
        };
        Assert.Equal("22", fixture.Text(fixture.Child(), fixture.Second, true));
    }

    [Fact]
    public void ForkingDuringEvaluationRetainsRecordedPhasesAndDetectsAnUndefinedSelfDependency()
    {
        WithRoot(token => {
            var fixture = new Fixture();
            fixture.Start = (runtime, node) => {
                if (!node.IsSamePosition(fixture.Second)) return;
                var child = runtime.CreateForkRuntime(new XmlCachedSequenceWriter());
                Run(token, t => child.RunFork(r => {
                    Assert.Equal("1", fixture.Text(r, fixture.First));
                    Assert.Contains("XTDE3400", Assert.Throws<XdmException>(() => fixture.Value(r, fixture.Second, true)).Message);
                }, t, null));
            };
            Assert.Equal("22", fixture.Text(fixture.Runtime, fixture.Second, true));
        });
    }

    [Fact]
    public void ApplicabilityUpdatesAreSharedAndPrincipalRestrictionsRemainEffective()
    {
        var fixture = new Fixture(); var first = fixture.Child(); var second = fixture.Child();
        first.XsltFunctions.X31SourceDocuments(new XPathItem[] { fixture.Root }, "Q{}m");
        Assert.Contains("XTDE3362", Assert.Throws<XdmException>(() => fixture.Value(second, fixture.First)).Message);
        Assert.Equal("7", fixture.Text(second, fixture.First, name: "m"));
        first.XsltFunctions.X31SourceDocuments(new XPathItem[] { fixture.Root }, "#all");
        Assert.Equal("1", fixture.Text(second, fixture.First));
        Assert.Contains("XTDE3362", Assert.Throws<XdmException>(() => fixture.Value(second, fixture.First, name: "limited")).Message);
    }

    [Fact]
    public void SharedValuesKeepNodeIdentityWithIndependentNavigatorPositions()
    {
        var fixture = new Fixture("<doc/>");
        fixture.Initial = (_, root) => new XPathItem[] { root.Clone() };
        var first = fixture.Child(); var second = fixture.Child();
        var value = Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(fixture.Value(first, fixture.Root)));
        Assert.True(value.MoveToFirstChild());
        var other = Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(fixture.Value(second, fixture.Root)));
        Assert.True(other.IsSamePosition(fixture.Root)); Assert.Equal(XPathNodeType.Root, other.NodeType);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ComputeTemporary(Fixture fixture, XmlQueryRuntime runtime)
    {
        using var reader = XmlReader.Create(new StringReader("<temporary/>"));
        var document = CompactDocument.Load(reader);
        fixture.Initial = (_, root) => new XPathItem[] { root.Clone() }; // A value-to-document cycle must also be collectible.
        fixture.Value(runtime, document.CreateNavigator());
        return new WeakReference(document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedAccumulatorValuesDoNotRetainTemporaryDocuments(bool beforePromotion)
    {
        var fixture = new Fixture(); var runtime = beforePromotion ? fixture.Runtime : fixture.Child();
        var weak = ComputeTemporary(fixture, runtime);
        if (beforePromotion) _ = fixture.Child();
        for (int i = 0; i < 4 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(weak.IsAlive); GC.KeepAlive(fixture); GC.KeepAlive(runtime);
    }

    [Fact]
    public void DeepGroundedTreesDoNotUseTheClrCallStackForTraversal()
    {
        const int depth = 20_000;
        var xml = new StringBuilder(); for (int i = 0; i < depth; i++) xml.Append("<n>"); for (int i = 0; i < depth; i++) xml.Append("</n>");
        var fixture = new Fixture(xml.ToString());
        Assert.Equal("7", fixture.Text(fixture.Child(), fixture.Root, true, "m"));
    }

    [Fact]
    public void ACancelledWaiterDoesNotPoisonTheOwnersComputation()
    {
        WithRoot(token => {
            var fixture = new Fixture(); var owner = fixture.Child(); var waiter = fixture.Child();
            using var started = new ManualResetEventSlim(); using var cancelled = new ManualResetEventSlim();
            using var local = CancellationTokenSource.CreateLinkedTokenSource(token);
            fixture.Start = (_, node) => {
                if (!node.IsSamePosition(fixture.First)) return;
                started.Set(); Assert.True(SpinWait.SpinUntil(() => fixture.Runtime.Documents.Evaluations.WaitingCount == 1, Deadline));
                local.Cancel(); Assert.True(cancelled.Wait(Deadline, token));
            };
            Run(token, t => owner.RunFork(r => Assert.Equal("22", fixture.Text(r, fixture.Second, true)), t, null),
                ignored => waiter.RunFork(r => {
                    Assert.True(started.Wait(Deadline, token));
                    Assert.Throws<OperationCanceledException>(() => fixture.Value(r, fixture.Second, true)); cancelled.Set();
                }, local.Token, null));
            Assert.Equal("22", fixture.Text(fixture.Runtime, fixture.Second, true)); Assert.Equal(2, fixture.Starts);
        });
    }

    [Fact]
    public void OwnerUsesTransformationCancellationAndRestoresItsLocalToken()
    {
        WithRoot(token => {
            var fixture = new Fixture(); var child = fixture.Child(); using var local = new CancellationTokenSource();
            fixture.Start = (_, _) => { local.Cancel(); XmlQueryRuntime.EnterFunction(); };
            Run(token, ignored => child.RunFork(r => {
                Assert.Equal("22", fixture.Text(r, fixture.Second, true));
                Assert.True(XmlQueryRuntime.Cancellation.IsCancellationRequested);
            }, local.Token, null));
            Assert.Equal("22", fixture.Text(fixture.Runtime, fixture.Second, true));
        });
    }

    [Fact]
    public void UnrelatedProngsWaitForCircularFailureBeforeUsingAnEarlyPhase()
    {
        WithRoot(token => {
            var fixture = new Fixture(); var owner = fixture.Child(); var waiter = fixture.Child();
            using var partial = new ManualResetEventSlim();
            var error = new XdmException("XTDE3400", "late circular failure") { AccumulatorCycle = true };
            fixture.Start = (_, node) => {
                if (!node.IsSamePosition(fixture.Second)) return;
                partial.Set(); Assert.True(SpinWait.SpinUntil(() => fixture.Runtime.Documents.Evaluations.WaitingCount == 1, Deadline));
                throw error;
            };
            Run(token, t => owner.RunFork(r => Assert.Same(error, Assert.Throws<XdmException>(() => fixture.Value(r, fixture.Second, true))), t, null),
                t => waiter.RunFork(r => {
                    Assert.True(partial.Wait(Deadline, t));
                    Assert.Same(error, Assert.Throws<XdmException>(() => fixture.Value(r, fixture.First)));
                }, t, null));
        });
    }

    private sealed class Sources : XmlResolver
    {
        internal Action? Load;
        internal int Closed;
        private sealed class Input(Action close) : MemoryStream(Encoding.UTF8.GetBytes("<external/>"))
        {
            protected override void Dispose(bool disposing) { if (CanRead) close(); base.Dispose(disposing); }
        }
        public override System.Net.ICredentials? Credentials { set { } }
        public override object GetEntity(Uri uri, string? role, Type? type) { Load?.Invoke(); return new Input(() => Interlocked.Increment(ref Closed)); }
    }

    [Theory]
    [InlineData("accumulator")]
    [InlineData("global")]
    [InlineData("key")]
    [InlineData("document")]
    public void AccumulatorDependenciesParticipateInTheCommonCycleGraph(string kind)
    {
        WithRoot(token => {
            var sources = new Sources(); var fixture = new Fixture(resolver: sources); var first = fixture.Child(); var second = fixture.Child();
            using var barrier = new Barrier(2);
            fixture.Initial = (r, root) => {
                Assert.True(barrier.SignalAndWait(Deadline, token));
                switch (kind)
                {
                    case "accumulator": fixture.Value(r, root, name: "m"); break;
                    case "global": r.IsGlobalComputed(0); break;
                    case "key": r.FindIndex(root, 0, out _); break;
                    case "document": r.ExternalContext.GetDataSource("urn:external", null); break;
                }
                return Integer(0);
            };
            fixture.OtherInitial = (r, root) => { Assert.True(barrier.SignalAndWait(Deadline, token)); return fixture.Value(r, root); };
            sources.Load = () => { Assert.True(barrier.SignalAndWait(Deadline, token)); fixture.Value(second, fixture.Second, true); };
            Exception failure = Assert.ThrowsAny<Exception>(() => Run(token,
                t => first.RunFork(r => fixture.Value(r, fixture.Second, true), t, null),
                t => second.RunFork(r => {
                    if (kind == "accumulator") { fixture.Value(r, fixture.Root, name: "m"); return; }
                    if (kind == "document") { r.ExternalContext.GetDataSource("urn:external", null); return; }
                    if (kind == "global") Assert.False(r.IsGlobalComputed(0)); else Assert.False(r.FindIndex(fixture.Root, 0, out _));
                    try { Assert.True(barrier.SignalAndWait(Deadline, token)); fixture.Value(r, fixture.Second, true); }
                    catch (Exception error) { if (kind == "global") r.GlobalValueFailed(0, error); else r.IndexValueFailed(fixture.Root, 0, error); throw; }
                }, t, null)));
            Assert.False(failure is OperationCanceledException, failure.ToString());
            Assert.True(failure.Message.Contains("XTDE3400", StringComparison.Ordinal) || failure.Message.Contains("XTDE0640", StringComparison.Ordinal)
                || kind == "document" && failure.Message.Contains("FODC0002", StringComparison.Ordinal), failure.ToString());
            Assert.Equal(0, fixture.Runtime.Documents.Evaluations.WaitingCount);
        });
    }

    [Fact]
    public void CopiesMadeByAnotherProngUseTheOriginalGroundedValues()
    {
        var fixture = new Fixture(); var first = fixture.Child(); var second = fixture.Child();
        Assert.Equal("22", fixture.Text(first, fixture.Second, true));
        var copy = Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(second.XsltFunctions.X31CopyWithAccumulators(new XPathItem[] { fixture.Root })));
        Assert.False(copy.IsSamePosition(fixture.Root));
        Assert.Equal("1", fixture.Text(second, copy.SelectSingleNode("/doc/row[1]")!));
        Assert.Equal(1, fixture.Initials); Assert.Equal(2, fixture.Starts);
    }

    [Fact]
    public void TransformationCancellationStopsTheOwningTraversal()
    {
        WithRoot(ignored => {
            using var cancellation = new CancellationTokenSource();
            XmlQueryRuntime.Cancellation = cancellation.Token;
            var fixture = new Fixture(); var child = fixture.Child();
            fixture.Start = (_, _) => cancellation.Cancel();
            Run(default, token => child.RunFork(r => Assert.Throws<OperationCanceledException>(() => fixture.Value(r, fixture.Second, true)), token, null));
            Assert.Equal(1, fixture.Starts); Assert.Equal(0, fixture.Ends);
            Assert.Equal(0, fixture.Runtime.Documents.Evaluations.WaitingCount);
        });
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_command")]
    private static extern ref XmlILCommand? Command(XslCompiledTransform transform);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_delExec")]
    private static extern ref ExecuteDelegate Execute(XmlILCommand command);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
#if NET9_0_OR_GREATER
    [InlineData(true, false)]
    [InlineData(true, true)]
#endif
    public void CompiledRulesShareValuesAcrossIsolatedRuntimes(bool saved, bool precomputed)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema"
                xmlns:p="urn:probe" exclude-result-prefixes="xs p">
              <xsl:mode use-accumulators="n"/>
              <xsl:accumulator name="n" as="xs:integer" initial-value="0">
                <xsl:accumulator-rule match="row" select="p:observe($value + 1)"/>
                <xsl:accumulator-rule match="row" phase="end" select="$value + 10"/>
              </xsl:accumulator>
              <xsl:template match="/"><out><xsl:for-each select="doc/row"><n b="{accumulator-before('n')}" a="{accumulator-after('n')}"/></xsl:for-each></out></xsl:template>
            </xsl:stylesheet>
            """;
        int evaluations = 0;
        XslCompiledTransform transform;
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(sheet.Replace("p:observe($value + 1)", "$value + 1", StringComparison.Ordinal))),
                new(), null, "Xsl3.Tests.GroundedAccumulators", "Saved.GroundedAccumulators", assembly);
            transform = new XslCompiledTransform(); transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.GroundedAccumulators", true)!);
#else
            throw new NotSupportedException();
#endif
        }
        else
        {
            var compiler = new XsltCompiler();
            compiler.RegisterExtensionFunction(new QName("urn:probe", "observe"), 1, args => { Interlocked.Increment(ref evaluations); return args[0]; });
            transform = compiler.Compile(new StringReader(sheet)).Principal();
        }
        var command = Command(transform)!;
        using var input = XmlReader.Create(new StringReader("<doc><row/><row/></doc>"));
        var owner = new XmlQueryRuntime(command.StaticData, input, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter());
        if (precomputed) Execute(command)(owner);
        var sinks = Enumerable.Range(0, 3).Select(_ => new XmlCachedSequenceWriter()).ToArray();
        var children = sinks.Select(owner.CreateForkRuntime).ToArray();
        using var barrier = new Barrier(children.Length); using var cancellation = new CancellationTokenSource(Deadline);
        Run(cancellation.Token, children.Select<XmlQueryRuntime, Action<CancellationToken>>(child => token => child.RunFork(r => {
            Assert.True(barrier.SignalAndWait(Deadline, token)); Execute(command)(r);
        }, token, null)).ToArray());
        if (!saved) Assert.Equal(2, evaluations);
        foreach (var sink in sinks)
        {
            var root = Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(sink.ResultSequence));
            var first = root.SelectSingleNode("descendant-or-self::out/n[1]")!; var second = root.SelectSingleNode("descendant-or-self::out/n[2]")!;
            Assert.Equal("1", first.GetAttribute("b", "")); Assert.Equal("11", first.GetAttribute("a", ""));
            Assert.Equal("12", second.GetAttribute("b", "")); Assert.Equal("22", second.GetAttribute("a", ""));
        }
    }
}
