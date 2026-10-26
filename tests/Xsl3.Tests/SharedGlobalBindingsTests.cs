// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class SharedGlobalBindingsTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private static XmlQueryStaticData Data(int count)
    {
        var manager = new StaticDataManager();
        for (int i = 0; i < count; i++) manager.DeclareGlobalValue("g" + i);
        return new(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), manager) { Xslt2 = true };
    }

    private static XmlQueryRuntime Runtime(XmlQueryStaticData data) =>
        new(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter());

    private static SharedGlobalBindings Bindings(int count) => new(
        Enumerable.Range(0, count).Select(i => "g" + i).ToArray(), new object[count], new bool[count], null);

    private static void Run(params Action<CancellationToken>[] bodies)
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        ForkProngs.Run<object>(() => null, bodies.Select<Action<CancellationToken>, Action<StreamTee<object>.Reader, CancellationToken>>(
            body => (_, token) => {
                CancellationToken saved = XmlQueryRuntime.Cancellation;
                XmlQueryRuntime.Cancellation = token;
                try { body(token); }
                finally { XmlQueryRuntime.Cancellation = saved; }
            }).ToArray(), 1, cancellation.Token);
    }

    [Fact]
    public void RuntimesEvaluateSharedGlobalsOnceAndKeepValueIdentity()
    {
        XmlQueryStaticData data = Data(1);
        XmlQueryRuntime parent = Runtime(data);
        XmlQueryRuntime[] children = Enumerable.Range(0, 4).Select(_ => Runtime(data)).ToArray();
        foreach (XmlQueryRuntime child in children) child.ShareGlobalBindings(parent);
        using var barrier = new Barrier(children.Length);
        object expected = new();
        int evaluations = 0;
        Run(children.Select<XmlQueryRuntime, Action<CancellationToken>>(child => token => {
            Assert.True(barrier.SignalAndWait(Deadline, token));
            if (!child.IsGlobalComputed(0)) { Interlocked.Increment(ref evaluations); child.SetGlobalValue(0, expected); }
            Assert.Same(expected, child.GetGlobalValue(0));
        }).ToArray());
        Assert.Equal(1, evaluations);
        Assert.True(parent.IsGlobalComputed(0)); Assert.Same(expected, parent.GetGlobalValue(0));
    }

    [Fact]
    public void PromotionKeepsPreviouslyComputedValuesAndFailures()
    {
        XmlQueryStaticData data = Data(2);
        XmlQueryRuntime parent = Runtime(data), child = Runtime(data);
        object expected = new();
        Assert.False(parent.IsGlobalComputed(0)); parent.SetGlobalValue(0, expected);
        var failure = new InvalidOperationException("global failure");
        Assert.False(parent.IsGlobalComputed(1)); parent.GlobalValueFailed(1, failure);
        child.ShareGlobalBindings(parent);
        Assert.True(child.IsGlobalComputed(0)); Assert.Same(expected, child.GetGlobalValue(0));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => child.IsGlobalComputed(1)));
    }

    [Fact]
    public void ForkingDuringGlobalEvaluationPreservesItsDependency()
    {
        XmlQueryStaticData data = Data(1);
        XmlQueryRuntime parent = Runtime(data), child = Runtime(data);
        Assert.False(parent.IsGlobalComputed(0));
        child.ShareGlobalBindings(parent);
        Run(_ => Assert.Contains("XTDE0640", Assert.Throws<XslTransformException>(() => child.IsGlobalComputed(0)).Message));
        parent.SetGlobalValue(0, "finished");
        Assert.True(child.IsGlobalComputed(0)); Assert.Equal("finished", child.GetGlobalValue(0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedEvaluationUsesTransformationCancellationAndRestoresProngCancellation(bool fail)
    {
        using var transformation = new CancellationTokenSource();
        using var prong = new CancellationTokenSource();
        CancellationToken saved = XmlQueryRuntime.Cancellation;
        XmlQueryStaticData data = Data(2);
        XmlQueryRuntime parent = Runtime(data), child = Runtime(data);
        XmlQueryRuntime.Cancellation = transformation.Token;
        try { child.ShareGlobalBindings(parent); }
        finally { XmlQueryRuntime.Cancellation = saved; }
        Run(_ => {
            XmlQueryRuntime.Cancellation = prong.Token;
            Assert.False(child.IsGlobalComputed(0));
            Assert.Equal(transformation.Token, XmlQueryRuntime.Cancellation);
            prong.Cancel();
            Assert.False(child.IsGlobalComputed(1));
            child.SetGlobalValue(1, "nested");
            XmlQueryRuntime.CheckCancellation();
            if (fail) child.GlobalValueFailed(0, new InvalidOperationException("global failed"));
            else child.SetGlobalValue(0, "finished");
            Assert.Equal(prong.Token, XmlQueryRuntime.Cancellation);
            Assert.Throws<OperationCanceledException>(XmlQueryRuntime.CheckCancellation);
        });
    }

    [Fact]
    public void PromotionDuringEvaluationDoesNotReplaceTheInvokingThreadsCancellation()
    {
        using var transformation = new CancellationTokenSource();
        CancellationToken saved = XmlQueryRuntime.Cancellation;
        XmlQueryRuntime.Cancellation = transformation.Token;
        try
        {
            XmlQueryStaticData data = Data(1);
            XmlQueryRuntime parent = Runtime(data), child = Runtime(data);
            Assert.False(parent.IsGlobalComputed(0)); child.ShareGlobalBindings(parent);
            parent.SetGlobalValue(0, "finished");
            Assert.Equal(transformation.Token, XmlQueryRuntime.Cancellation);
        }
        finally { XmlQueryRuntime.Cancellation = saved; }
    }

    [Fact]
    public void TransformationCancellationStillAbortsASharedGlobal()
    {
        using var transformation = new CancellationTokenSource();
        CancellationToken saved = XmlQueryRuntime.Cancellation;
        XmlQueryStaticData data = Data(1);
        XmlQueryRuntime parent = Runtime(data), child = Runtime(data);
        XmlQueryRuntime.Cancellation = transformation.Token;
        try { child.ShareGlobalBindings(parent); }
        finally { XmlQueryRuntime.Cancellation = saved; }
        OperationCanceledException? failure = null;
        Run(token => {
            Assert.False(child.IsGlobalComputed(0));
            transformation.Cancel();
            failure = Assert.Throws<OperationCanceledException>(XmlQueryRuntime.CheckCancellation);
            child.GlobalValueFailed(0, failure);
            Assert.Equal(token, XmlQueryRuntime.Cancellation);
        });
        Assert.Same(failure, Assert.Throws<OperationCanceledException>(() => parent.IsGlobalComputed(0)));
    }

    [Fact]
    public void UnrelatedTransformationsKeepIndependentBindings()
    {
        XmlQueryStaticData data = Data(1);
        XmlQueryRuntime first = Runtime(data), second = Runtime(data), a = Runtime(data), b = Runtime(data);
        a.ShareGlobalBindings(first); b.ShareGlobalBindings(second);
        Assert.False(a.IsGlobalComputed(0)); a.SetGlobalValue(0, "first");
        Assert.False(b.IsGlobalComputed(0)); b.SetGlobalValue(0, "second");
        Assert.Equal("first", first.GetGlobalValue(0)); Assert.Equal("second", second.GetGlobalValue(0));
    }

    [Fact]
    public void IncompatibleOrAlreadyUsedRuntimesCannotShareBindings()
    {
        XmlQueryStaticData data = Data(1);
        XmlQueryRuntime parent = Runtime(data), child = Runtime(data);
        Assert.Throws<InvalidOperationException>(() => Runtime(Data(1)).ShareGlobalBindings(parent));
        Assert.False(child.IsGlobalComputed(0)); child.SetGlobalValue(0, "used");
        Assert.Throws<InvalidOperationException>(() => child.ShareGlobalBindings(parent));
        XmlQueryRuntime failed = Runtime(data);
        Assert.False(failed.IsGlobalComputed(0)); failed.GlobalValueFailed(0, new InvalidOperationException("already failed"));
        Assert.Throws<InvalidOperationException>(() => failed.ShareGlobalBindings(parent));
    }

    [Fact]
    public void DebuggerReadsAndWritesTheSharedValue()
    {
        XmlQueryStaticData data = Data(1);
        XmlQueryRuntime parent = Runtime(data), child = Runtime(data);
        Assert.False(parent.IsGlobalComputed(0)); parent.SetGlobalValue(0, new XmlQueryItemSequence());
        child.ShareGlobalBindings(parent);
        var value = new XmlQueryItemSequence();
        value.Add(Xdm.AtomicValue.OfInteger(5));
        child.DebugSetGlobalValue("g0", value);
        Assert.Same(value, parent.DebugGetGlobalValue("g0"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void CrossThreadDependencyCyclesFailWithoutDeadlocking(int count)
    {
        SharedGlobalBindings bindings = Bindings(count);
        using var barrier = new Barrier(count);
        var errors = new Exception?[count];
        Run(Enumerable.Range(0, count).Select<int, Action<CancellationToken>>(index => token => {
            Assert.False(bindings.IsComputed(index, null, token));
            Assert.True(barrier.SignalAndWait(Deadline, token));
            try { bindings.IsComputed((index + 1) % count, null, token); }
            catch (Exception error) { errors[index] = error; bindings.Fail(index, error); }
        }).ToArray());
        Assert.All(errors, error => Assert.Contains("XTDE0640", Assert.IsType<XslTransformException>(error).Message));
        Assert.Equal(0, bindings.WaitingCount);
    }

    [Fact]
    public async Task CancellationWakesAWaiterWithoutReleasingTheOwnersBinding()
    {
        SharedGlobalBindings bindings = Bindings(1);
        Assert.False(bindings.IsComputed(0, null, default));
        using var cancellation = new CancellationTokenSource(Deadline);
        // These waiters block synchronously; give them dedicated threads so the test
        // does not depend on thread-pool injection while xUnit is running other cases.
        Task<bool> waiter = Task.Factory.StartNew(() => bindings.IsComputed(0, null, cancellation.Token),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(SpinWait.SpinUntil(() => bindings.WaitingCount == 1, Deadline));
        cancellation.Cancel();
        bindings.Set(0, "owner");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.True(bindings.IsComputed(0, null, default)); Assert.Equal("owner", bindings.Get(0));
        Assert.Equal(0, bindings.WaitingCount);
    }

    [Fact]
    public async Task FailurePublishesTheOriginalExceptionToEveryWaiter()
    {
        SharedGlobalBindings bindings = Bindings(1);
        using var cancellation = new CancellationTokenSource(Deadline);
        var expected = new InvalidOperationException("original global failure");
        Assert.False(bindings.IsComputed(0, null, cancellation.Token));
        Task<Exception>[] waiters = Enumerable.Range(0, 3).Select(_ => Task.Factory.StartNew(() =>
            Assert.Throws<InvalidOperationException>(() => bindings.IsComputed(0, null, cancellation.Token)) as Exception,
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        Assert.True(SpinWait.SpinUntil(() => bindings.WaitingCount == 3, Deadline));
        bindings.Fail(0, expected);
        Assert.All(await Task.WhenAll(waiters), error => Assert.Same(expected, error));
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => bindings.IsComputed(0, null, default)));
        Assert.Equal(0, bindings.WaitingCount);
    }

    [Fact]
    public void AnotherThreadsFailureCannotReleaseAnOwnedBinding()
    {
        SharedGlobalBindings bindings = Bindings(1);
        Assert.False(bindings.IsComputed(0, null, default));
        Run(_ => bindings.Fail(0, new InvalidOperationException("not the owner")));
        bindings.Set(0, "valid");
        Assert.True(bindings.IsComputed(0, null, default)); Assert.Equal("valid", bindings.Get(0));
    }

#if NET10_0_OR_GREATER
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_command")]
    private static extern ref XmlILCommand? Command(XslCompiledTransform transform);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedGlobalFailuresKeepTheirErrorCodeOnRepeatedDemands(bool shared)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:err="http://www.w3.org/2005/xqt-errors" exclude-result-prefixes="xs err">
              <xsl:param name="p" select="'invalid'"/>
              <xsl:variable name="g" select="xs:integer($p)"/>
              <xsl:template match="/"><xsl:value-of select="$g"/></xsl:template>
            </xsl:stylesheet>
            """;
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(sheet)), new(), null,
            "Xsl3.Tests.Globals", "Saved.Globals", assembly);
        Type type = Assembly.Load(assembly.ToArray()).GetType("Saved.Globals", true)!;
        var transform = new XslCompiledTransform(); transform.Load(type);
        XmlQueryStaticData data = Command(transform)!.StaticData;
        XmlQueryRuntime owner = Runtime(data);
        MethodInfo global = type.GetMethod("g", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        Assert.NotNull(global);
        Exception Demand(XmlQueryRuntime runtime) => Assert.IsType<Xdm.XdmException>(
            Assert.Throws<TargetInvocationException>(() => global.Invoke(null, new object[] { runtime })).InnerException);
        if (!shared)
        {
            Exception first = Demand(owner);
            Assert.Contains("FORG0001", first.Message);
            Assert.Same(first, Demand(owner));
        }
        else
        {
            XmlQueryRuntime[] children = Enumerable.Range(0, 3).Select(_ => Runtime(data)).ToArray();
            foreach (XmlQueryRuntime child in children) child.ShareGlobalBindings(owner);
            var errors = new Exception[children.Length];
            Run(children.Select<XmlQueryRuntime, Action<CancellationToken>>((child, index) => _ => {
                errors[index] = Demand(child);
                Assert.Same(errors[index], Demand(child));
            }).ToArray());
            Assert.Contains("FORG0001", errors[0].Message);
            Assert.All(errors, error => Assert.Same(errors[0], error));
        }
    }
#endif
}
