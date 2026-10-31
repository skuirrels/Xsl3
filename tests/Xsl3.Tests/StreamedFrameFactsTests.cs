// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class StreamedFrameFactsTests
{
    [Fact]
    public void IndependentScopesOnTheSameHeaderKeepTheirOwnValues()
    {
        using var reader = XmlReader.Create(new StringReader("<doc/>")); using var document = new StreamedDocument(reader);
        using var first = new StreamedFrameFacts(); using var second = new StreamedFrameFacts();
        object a = first.Get(document.Root, static () => new object());
        object b = second.Get(document.Root, static () => new object());
        Assert.NotSame(a, b); Assert.Same(a, first.Get(document.Root, static () => new object()));
        Assert.Same(b, second.Get(document.Root, static () => new object()));
    }

    [Fact]
    public void ConcurrentScopesDoNotReplaceAnotherScopesFacts()
    {
        using var reader = XmlReader.Create(new StringReader("<doc/>")); using var document = new StreamedDocument(reader);
        var scopes = new StreamedFrameFacts[8]; var values = new object[8];
        try
        {
            Parallel.For(0, scopes.Length, i => { scopes[i] = new(); values[i] = scopes[i].Get(document.Root, static () => new object()); });
            for (int i = 0; i < scopes.Length; i++) Assert.Same(values[i], scopes[i].Get(document.Root, static () => new object()));
        }
        finally { foreach (var scope in scopes) scope?.Dispose(); }
    }

    [Fact]
    public void AHeaderDoesNotKeepAnAbandonedScopeAlive()
    {
        using var reader = XmlReader.Create(new StringReader("<doc/>")); using var document = new StreamedDocument(reader);
        WeakReference scope = AbandonedScope(document.Root);
        Collect(); Assert.False(scope.IsAlive); GC.KeepAlive(document);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonedScope(StreamedDocument.Node node)
    {
        var scope = new StreamedFrameFacts(); scope.Get(node, static () => new object());
        return new WeakReference(scope);
    }

    [Fact]
    public void ANewScopeDropsPayloadsFromCompletedScopes()
    {
        using var reader = XmlReader.Create(new StringReader("<doc/>")); using var document = new StreamedDocument(reader);
        WeakReference value = CompletedScope(document.Root);
        using var next = new StreamedFrameFacts(); next.Get(document.Root, static () => new object());
        Collect(); Assert.False(value.IsAlive); GC.KeepAlive(document); GC.KeepAlive(next);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CompletedScope(StreamedDocument.Node node)
    {
        using var scope = new StreamedFrameFacts(); return new WeakReference(scope.Get(node, static () => new object()));
    }

    [Fact]
    public void AContinuingScopeDoesNotKeepDiscardedFramesAlive()
    {
        using var reader = XmlReader.Create(new StringReader("<doc><first/><second/></doc>")); using var document = new StreamedDocument(reader);
        using var scope = new StreamedFrameFacts(); var nav = document.CreateNavigator(); Assert.True(nav.MoveToFirstChild());
        WeakReference frame = RecordChild(scope, (StreamedNavigator)nav);
        Collect(); Assert.False(frame.IsAlive); GC.KeepAlive(document); GC.KeepAlive(scope);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RecordChild(StreamedFrameFacts scope, StreamedNavigator nav)
    {
        Assert.True(nav.MoveToFirstChild()); var node = nav.PositionNode;
        scope.Get(node, static () => new object()); var weak = new WeakReference(node);
        Assert.True(nav.MoveToNext()); return weak;
    }

    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
}
