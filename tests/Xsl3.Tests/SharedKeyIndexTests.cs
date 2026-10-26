// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class SharedKeyIndexTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static XmlQueryRuntime Runtime(XmlResolver? resolver = null)
    {
        var manager = new StaticDataManager(); manager.DeclareGlobalValue("global");
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), manager) { Xslt2 = true };
        using var input = XmlReader.Create(new StringReader("<doc><row id='1'/><row id='2'/></doc>"));
        return new(data, input, resolver ?? XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter());
    }
    private static XmlQueryRuntime Child(XmlQueryRuntime owner) => owner.CreateForkRuntime(new XmlCachedSequenceWriter());
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
        ForkProngs.Run<object>(() => null, bodies.Select<Action<CancellationToken>, Action<StreamTee<object>.Reader, CancellationToken>>(
            body => (_, cancellation) => body(cancellation)).ToArray(), 1, token);
    private static void Fill(XmlILIndex index, XPathNavigator root)
    {
        var rows = root.Select("/doc/row");
        while (rows.MoveNext()) index.Add("x", rows.Current!);
    }

    [Fact]
    public void ParallelProngsBuildOneIndexAndOwnTheirLookupPositionsAndSequenceState()
    {
        WithRoot(token => {
            var owner = Runtime();
            var children = Enumerable.Range(0, 3).Select(_ => Child(owner)).ToArray();
            using var barrier = new Barrier(children.Length);
            var found = new XmlILIndex[children.Length];
            int builds = 0;
            Run(token, children.Select<XmlQueryRuntime, Action<CancellationToken>>((child, i) => t => child.RunFork(r => {
                Assert.True(barrier.SignalAndWait(Deadline, t));
                var root = r.ExternalContext.DefaultDataSource;
                if (!r.FindIndex(root, 0, out var index)) { Interlocked.Increment(ref builds); Fill(index, root); r.AddNewIndex(root, 0, index); }
                found[i] = index;
                var lookup = index.Lookup("x");
                Assert.Equal(2, lookup.Count);
                Assert.True(lookup[0].MoveToParent()); lookup.Clear();
                var fresh = index.Lookup("x");
                Assert.Equal(2, fresh.Count); Assert.All<XPathNavigator>(fresh, node => Assert.Equal("row", node.LocalName));
            }, t, null)).ToArray());
            Assert.Equal(1, builds);
            Assert.All(found, index => Assert.Same(found[0], index));
            Assert.Throws<InvalidOperationException>(() => found[0].Add("later", owner.ExternalContext.DefaultDataSource));
        });
    }

    [Fact]
    public void PromotionPreservesExistingIndexesAndIndexAndDocumentBoundaries()
    {
        var owner = Runtime(); var root = owner.ExternalContext.DefaultDataSource;
        Assert.False(owner.FindIndex(root, 0, out var existing)); Fill(existing, root); owner.AddNewIndex(root, 0, existing);
        var child = Child(owner);
        Assert.True(child.FindIndex(root, 0, out var found)); Assert.Same(existing, found);
        Assert.False(child.FindIndex(root, 1, out var other)); child.AddNewIndex(root, 1, other);
        var unrelated = Runtime().ExternalContext.DefaultDataSource;
        Assert.False(child.FindIndex(unrelated, 0, out var different)); child.AddNewIndex(unrelated, 0, different);
        Assert.NotSame(existing, different); Assert.NotSame(existing, other);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference IndexedTemporaryTree(XmlQueryRuntime runtime)
    {
        using var reader = XmlReader.Create(new StringReader("<doc><row/></doc>"));
        var document = Xsl3.Tree.CompactDocument.Load(reader);
        var root = document.CreateNavigator();
        Assert.False(runtime.FindIndex(root, 0, out var index)); Fill(index, root); runtime.AddNewIndex(root, 0, index);
        return new WeakReference(document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedIndexesDoNotRetainCompletedTemporaryTrees(bool beforePromotion)
    {
        var owner = Runtime();
        XmlQueryRuntime runtime = beforePromotion ? owner : Child(owner);
        var weak = IndexedTemporaryTree(runtime);
        if (beforePromotion) _ = Child(owner);
        // Release the runtime's single reusable navigator, which may retain its last document.
        var root = runtime.ExternalContext.DefaultDataSource;
        Assert.False(runtime.FindIndex(root, 1, out var current)); runtime.AddNewIndex(root, 1, current);
        for (int i = 0; i < 4 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(weak.IsAlive);
        GC.KeepAlive(owner); GC.KeepAlive(runtime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailuresKeepTheirIdentityOnLaterDemands(bool shared)
    {
        var owner = Runtime(); var runtime = shared ? Child(owner) : owner; var root = runtime.ExternalContext.DefaultDataSource;
        var error = new InvalidOperationException("key failure");
        Assert.False(runtime.FindIndex(root, 0, out _)); runtime.IndexValueFailed(root, 0, error);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => runtime.FindIndex(root, 0, out _)));
        if (shared) Assert.Same(error, Assert.Throws<InvalidOperationException>(() => Child(owner).FindIndex(root, 0, out _)));
    }

    [Fact]
    public void ForkingDuringAKeyBuildKeepsItsDependencyInsteadOfWaitingForTheParent()
    {
        WithRoot(token => {
            var owner = Runtime(); var root = owner.ExternalContext.DefaultDataSource;
            Assert.False(owner.FindIndex(root, 0, out var index));
            var child = Child(owner);
            Run(token, t => child.RunFork(r => Assert.Contains("XTDE0640", Assert.Throws<Xsl3.Xdm.XdmException>(() => r.FindIndex(root, 0, out _)).Message), t, null));
            Fill(index, root); owner.AddNewIndex(root, 0, index);
            Assert.True(child.FindIndex(root, 0, out var complete)); Assert.Same(index, complete);
        });
    }

    [Fact]
    public void CancelledWaiterDoesNotCompleteOrPoisonTheOwnersIndex()
    {
        WithRoot(token => {
            var owner = Runtime(); var builder = Child(owner); var waiter = Child(owner); var root = owner.ExternalContext.DefaultDataSource;
            using var claimed = new ManualResetEventSlim(); using var cancelled = new ManualResetEventSlim();
            using var local = CancellationTokenSource.CreateLinkedTokenSource(token);
            Run(token,
                t => builder.RunFork(r => {
                    Assert.False(r.FindIndex(root, 0, out var index)); claimed.Set();
                    Assert.True(SpinWait.SpinUntil(() => owner.Documents.Evaluations.WaitingCount == 1, Deadline));
                    local.Cancel(); Assert.True(cancelled.Wait(Deadline, t));
                    Fill(index, root); r.AddNewIndex(root, 0, index);
                }, t, null),
                ignored => waiter.RunFork(r => {
                    Assert.True(claimed.Wait(Deadline, token));
                    Assert.Throws<OperationCanceledException>(() => r.FindIndex(root, 0, out _));
                    cancelled.Set();
                }, local.Token, null));
            Assert.True(owner.FindIndex(root, 0, out var found)); Assert.Equal(2, found.Lookup("x").Count);
            Assert.Equal(0, owner.Documents.Evaluations.WaitingCount);
        });
    }

    [Fact]
    public void ABuildersLocalCancellationDoesNotAbortASharedIndexAndIsRestoredAfterPublication()
    {
        WithRoot(token => {
            var owner = Runtime(); var builder = Child(owner); var root = owner.ExternalContext.DefaultDataSource;
            using var local = new CancellationTokenSource();
            Run(token, _ => builder.RunFork(r => {
                Assert.False(r.FindIndex(root, 0, out var index));
                local.Cancel(); XmlQueryRuntime.EnterFunction(); // Shared evaluation uses the transformation token.
                Fill(index, root); r.AddNewIndex(root, 0, index);
                Assert.True(XmlQueryRuntime.Cancellation.IsCancellationRequested);
            }, local.Token, null));
            Assert.True(owner.FindIndex(root, 0, out var found)); Assert.Equal(2, found.Lookup("x").Count);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CyclesAcrossKeysOrAKeyAndAGlobalAreDetected(bool useGlobal)
    {
        WithRoot(token => {
            var owner = Runtime(); var key = Child(owner); var global = Child(owner); var root = owner.ExternalContext.DefaultDataSource;
            using var barrier = new Barrier(2);
            Exception failure = Assert.ThrowsAny<Exception>(() => Run(token,
                t => key.RunFork(r => {
                    Assert.False(r.FindIndex(root, 0, out _));
                    try { Assert.True(barrier.SignalAndWait(Deadline, t)); if (useGlobal) r.IsGlobalComputed(0); else r.FindIndex(root, 1, out _); }
                    catch (Exception error) { r.IndexValueFailed(root, 0, error); throw; }
                }, t, null),
                t => global.RunFork(r => {
                    if (useGlobal) Assert.False(r.IsGlobalComputed(0));
                    else Assert.False(r.FindIndex(root, 1, out _));
                    try { Assert.True(barrier.SignalAndWait(Deadline, t)); r.FindIndex(root, 0, out _); }
                    catch (Exception error) { if (useGlobal) r.GlobalValueFailed(0, error); else r.IndexValueFailed(root, 1, error); throw; }
                }, t, null)));
            Assert.Contains("XTDE0640", failure.Message);
            Assert.Equal(0, owner.Documents.Evaluations.WaitingCount);
        });
    }

    private sealed class Sources : XmlResolver
    {
        internal Action? Load;
        public override System.Net.ICredentials? Credentials { set { } }
        public override object GetEntity(Uri uri, string? role, Type? type) { Load!(); return new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<external/>")); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CyclesAcrossAKeyOrGlobalAndADocumentLoadAreDetected(bool useGlobal)
    {
        WithRoot(token => {
            var sources = new Sources(); var owner = Runtime(sources); var key = Child(owner); var document = Child(owner);
            var root = owner.ExternalContext.DefaultDataSource;
            using var barrier = new Barrier(2);
            sources.Load = () => { Assert.True(barrier.SignalAndWait(Deadline, token)); if (useGlobal) document.IsGlobalComputed(0); else document.FindIndex(root, 0, out _); };
            Exception failure = Assert.ThrowsAny<Exception>(() => Run(token,
                t => key.RunFork(r => {
                    if (useGlobal) Assert.False(r.IsGlobalComputed(0));
                    else Assert.False(r.FindIndex(root, 0, out _));
                    try { Assert.True(barrier.SignalAndWait(Deadline, t)); r.ExternalContext.GetDataSource("urn:external", null); }
                    catch (Exception error) { if (useGlobal) r.GlobalValueFailed(0, error); else r.IndexValueFailed(root, 0, error); throw; }
                }, t, null),
                t => document.RunFork(r => r.ExternalContext.GetDataSource("urn:external", null), t, null)));
            Assert.True(failure.Message.Contains("XTDE0640", StringComparison.Ordinal) || failure.Message.Contains("FODC0002", StringComparison.Ordinal), failure.ToString());
            Assert.Equal(0, owner.Documents.Evaluations.WaitingCount);
        });
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_command")]
    private static extern ref XmlILCommand? Command(XslCompiledTransform transform);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_delExec")]
    private static extern ref ExecuteDelegate Execute(XmlILCommand command);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedProngsShareOneCompletedKeyBuild(bool precomputed)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:p="urn:probe" exclude-result-prefixes="p">
              <xsl:key name="k" match="row" use="p:Value(string(@k))"/>
              <xsl:template match="/"><out><xsl:value-of select="key('k', 'x')/@id" separator=","/></out></xsl:template>
            </xsl:stylesheet>
            """;
        int evaluations = 0;
        var compiler = new XsltCompiler();
        compiler.RegisterExtensionFunction(new QName("urn:probe", "Value"), 1, arguments => { Interlocked.Increment(ref evaluations); return arguments[0]; });
        var transform = compiler.Compile(new StringReader(sheet)).Principal();
        var command = Command(transform)!;
        using var input = XmlReader.Create(new StringReader("<doc><row k='x' id='1'/><row k='x' id='2'/></doc>"));
        var owner = new XmlQueryRuntime(command.StaticData, input, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter());
        if (precomputed) Execute(command)(owner);
        var sinks = Enumerable.Range(0, 3).Select(_ => new XmlCachedSequenceWriter()).ToArray();
        var children = sinks.Select(owner.CreateForkRuntime).ToArray();
        using var barrier = new Barrier(children.Length);
        using var cancellation = new CancellationTokenSource(Deadline);
        Run(cancellation.Token, children.Select<XmlQueryRuntime, Action<CancellationToken>>(child => token => child.RunFork(r => {
            Assert.True(barrier.SignalAndWait(Deadline, token)); Execute(command)(r);
        }, token, null)).ToArray());
        Assert.Equal(2, evaluations);
        foreach (var sink in sinks) Assert.Equal("<out>1,2</out>", Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(sink.ResultSequence)).OuterXml);
    }

    [Theory]
    [InlineData(false)]
#if NET9_0_OR_GREATER
    [InlineData(true)]
#endif
    public void GeneratedKeyFailuresPublishTheOriginalErrorInsteadOfLeavingAnActiveBuild(bool saved)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xsl:param name="p" select="'bad'"/>
              <xsl:key name="k" match="row" use="xs:integer($p)"/>
              <xsl:template match="/">
                <xsl:try><xsl:sequence select="key('k', 1)"/><xsl:catch><first code="{$err:code}" xmlns:err="http://www.w3.org/2005/xqt-errors"/></xsl:catch></xsl:try>
                <xsl:try><xsl:sequence select="key('k', 1)"/><xsl:catch><second code="{$err:code}" xmlns:err="http://www.w3.org/2005/xqt-errors"/></xsl:catch></xsl:try>
              </xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XslCompiledTransform();
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(XmlReader.Create(new StringReader(sheet)), new(), null, "Xsl3.Tests.KeyFailures", "Saved.KeyFailures", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.KeyFailures", true)!);
#else
            throw new NotSupportedException();
#endif
        }
        else transform.Load(XmlReader.Create(new StringReader(sheet)));
        using var input = XmlReader.Create(new StringReader("<doc><row/></doc>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(output.ToString(), "FORG0001").Count);
        Assert.DoesNotContain("XTDE0640", output.ToString());
    }
}
