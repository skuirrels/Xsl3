// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.Streaming;
using System.Threading.Tasks;
using Xsl3.Tree;
using Xunit;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class CopyOriginRegistryTests
{
    private static XPathNavigator Tree(string xml)
    {
        using XmlReader reader = XmlReader.Create(new StringReader(xml));
        return CompactDocument.Load(reader).CreateNavigator();
    }

    [Fact]
    public void LiveCopiesKeepTheirOriginsAcrossCollectionAndNavigatorCloning()
    {
        var registry = new CopyOriginRegistry();
        XPathNavigator original = Tree("<source><item/></source>"), copy = Tree("<source><item/></source>");
        original.MoveToFirstChild();
        copy.MoveToFirstChild();
        registry.Set(copy, original, validated: true);
        GC.Collect();
        XPathNavigator child = copy.Clone();
        Assert.True(child.MoveToFirstChild());
        Assert.True(registry.TryGet(child, out CopyOriginRegistry.Origin? origin));
        Assert.True(origin.Validated);
        Assert.True(origin.Top.IsSamePosition(copy));
        Assert.True(origin.Original!.IsSamePosition(original));
    }

    [Fact]
    public void UnreferencedCopiesAndTheirOriginsAreNotKeptByTheRegistry()
    {
        var registry = new CopyOriginRegistry();
        var (copy, original) = RegisterPair(registry);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(copy.IsAlive);
        Assert.False(original.IsAlive);
        GC.KeepAlive(registry);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, WeakReference) RegisterPair(CopyOriginRegistry registry)
    {
        XPathNavigator original = Tree("<source><item/></source>"), copy = Tree("<source><item/></source>");
        registry.Set(copy, original);
        return (new WeakReference(((CompactNavigator)copy).Document), new WeakReference(((CompactNavigator)original).Document));
    }

    [Fact]
    public void StreamedOriginsRemainScopedAndReplacingOneKeepsOtherScopes()
    {
        using var reader = XmlReader.Create(new StringReader("<source><first/><second/></source>"));
        using var document = new StreamedDocument(reader);
        XPathNavigator first = document.CreateNavigator(); Assert.True(first.MoveToFirstChild());
        Assert.True(first.MoveToFirstChild()); XPathNavigator second = first.Clone(); Assert.True(second.MoveToNext());
        XPathNavigator copy = Tree("<copy/>");
        var a = new CopyOriginRegistry(); var b = new CopyOriginRegistry(); var unrelated = new CopyOriginRegistry();
        a.Set(copy, first); b.Set(copy, first, validated: true);
        Assert.False(unrelated.TryGet(copy, out _));
        a.Set(copy, second, validated: true);
        GC.Collect();
        Assert.True(a.TryGet(copy.Clone(), out var originA)); Assert.True(originA.Validated);
        Assert.True(originA.Original!.IsSamePosition(second));
        Assert.True(b.TryGet(copy, out var originB)); Assert.True(originB.Validated);
        Assert.True(originB.Original!.IsSamePosition(first));
        a.Set(copy, first);
        Assert.True(a.TryGet(copy, out originA)); Assert.False(originA.Validated);
        Assert.True(originA.Original!.IsSamePosition(first));
        Assert.True(b.TryGet(copy, out originB)); Assert.True(originB.Validated);
    }

    [Fact]
    public void IndependentProngsCanPublishDifferentScopesOnTheSameCopy()
    {
        using var reader = XmlReader.Create(new StringReader("<source/>"));
        using var document = new StreamedDocument(reader);
        XPathNavigator original = document.CreateNavigator(); Assert.True(original.MoveToFirstChild());
        XPathNavigator copy = Tree("<copy/>");
        var registries = new CopyOriginRegistry[8];
        for (int i = 0; i < registries.Length; i++) registries[i] = new();
        Parallel.For(0, registries.Length, i => registries[i].Set(copy, original, validated: i % 2 == 0));
        for (int i = 0; i < registries.Length; i++)
        {
            Assert.True(registries[i].TryGet(copy, out var origin));
            Assert.Equal(i % 2 == 0, origin.Validated); Assert.True(origin.Original!.IsSamePosition(original));
        }
    }

    [Fact]
    public void ACopyOfAStreamedCopyPreservesTheOriginalProvenanceChain()
    {
        using var reader = XmlReader.Create(new StringReader("<source/>"));
        using var document = new StreamedDocument(reader);
        XPathNavigator original = document.CreateNavigator(); Assert.True(original.MoveToFirstChild());
        var registry = new CopyOriginRegistry();
        XPathNavigator first = Tree("<first/>"), second = Tree("<second/>");
        registry.Set(first, original); registry.Set(second, first);
        GC.Collect();
        Assert.True(registry.TryGet(second, out var middle)); Assert.True(middle.Original!.IsSamePosition(first));
        Assert.True(registry.TryGet(middle.Original!, out var source)); Assert.True(source.Original!.IsSamePosition(original));
        document.Dispose();
        Assert.True(registry.TryGet(second, out middle)); Assert.True(registry.TryGet(middle.Original!, out source));
        Assert.Equal("source", source.Original!.LocalName);
    }

    [Fact]
    public void DiscardedStreamedCopiesAndTheirReadersAreNotOwnedByTheRegistry()
    {
        var registry = new CopyOriginRegistry();
        var weak = RegisterStreamedPair(registry);
        Collect();
        Assert.False(weak.Copy.IsAlive); Assert.False(weak.Source.IsAlive); Assert.False(weak.Reader.IsAlive);
        GC.KeepAlive(registry);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Copy, WeakReference Source, WeakReference Reader) RegisterStreamedPair(CopyOriginRegistry registry)
    {
        var reader = XmlReader.Create(new StringReader("<source/>"));
        var document = new StreamedDocument(reader); XPathNavigator original = document.CreateNavigator();
        Assert.True(original.MoveToFirstChild()); XPathNavigator copy = Tree("<copy/>"); registry.Set(copy, original);
        document.Dispose();
        return (new(((CompactNavigator)copy).Document), new(document), new(reader));
    }

    [Fact]
    public void RetainedCopiesDoNotKeepTheirInvocationRegistryAlive()
    {
        var (copy, registry) = RetainStreamedCopy();
        Collect(); Assert.False(registry.IsAlive);
        Assert.Equal("", copy.Value); GC.KeepAlive(copy);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (XPathNavigator, WeakReference) RetainStreamedCopy()
    {
        using var reader = XmlReader.Create(new StringReader("<source/>"));
        using var document = new StreamedDocument(reader);
        XPathNavigator original = document.CreateNavigator(); original.MoveToFirstChild();
        XPathNavigator copy = Tree("<copy/>"); var registry = new CopyOriginRegistry(); registry.Set(copy, original);
        return (copy, new(registry));
    }

    private static void Collect()
    {
        // ConditionalWeakTable containers finalize in two phases; drain both before
        // asserting that a dead invocation has released values whose copy is still live.
        GC.Collect(); GC.WaitForPendingFinalizers();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    }


    [Fact]
    public void AReturnedGroundedCopyDoesNotRetainItsWholeOriginalAfterTheInvocation()
    {
        var (copy, original) = RetainGroundedCopy();
        Collect(); Assert.False(original.IsAlive);
        Assert.Equal("", copy.Value); GC.KeepAlive(copy);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (XPathNavigator, WeakReference) RetainGroundedCopy()
    {
        XPathNavigator original = Tree("<large><kept/><unneeded/></large>"), copy = Tree("<kept/>");
        var registry = new CopyOriginRegistry(); registry.Set(copy, original);
        return (copy, new(((CompactNavigator)original).Document));
    }

    [Fact]
    public void ReplacingAStreamedScopeReleasesItsObsoleteSource()
    {
        var a = new CopyOriginRegistry(); var b = new CopyOriginRegistry();
        XPathNavigator copy = Tree("<copy/>");
        var (obsolete, borrowedOrigin) = ReplaceStreamedSource(a, b, copy);
        Collect(); Assert.False(obsolete.IsAlive);
        // A caller holding B's payload must not pin A's obsolete scope through storage links.
        Assert.Equal("replacement", borrowedOrigin.Original!.LocalName); GC.KeepAlive(borrowedOrigin);
        Assert.True(a.TryGet(copy, out var originA)); Assert.True(b.TryGet(copy, out var originB));
        Assert.Equal("replacement", originA.Original!.LocalName); Assert.Equal("replacement", originB.Original!.LocalName);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, CopyOriginRegistry.Origin) ReplaceStreamedSource(CopyOriginRegistry a, CopyOriginRegistry b, XPathNavigator copy)
    {
        using var firstReader = XmlReader.Create(new StringReader("<obsolete/>"));
        using var secondReader = XmlReader.Create(new StringReader("<replacement/>"));
        using var first = new StreamedDocument(firstReader); using var second = new StreamedDocument(secondReader);
        XPathNavigator before = first.CreateNavigator(), after = second.CreateNavigator();
        before.MoveToFirstChild(); after.MoveToFirstChild();
        a.Set(copy, before); b.Set(copy, after);
        Assert.True(b.TryGet(copy, out var borrowedOrigin));
        a.Set(copy, after);
        return (new(first), borrowedOrigin);
    }

    [Fact]
    public void AccumulatorCaptureRequiresCorrespondingSubtreeDepths()
    {
        using var source = new StreamedForkAccumulatorTests.Source("<doc><row><a><a/></a></row></doc>");
        Assert.True(source.Focus.MoveToFirstChild()); Assert.True(source.Focus.MoveToFirstChild());
        source.Library.X31BufferForkItem(source.Focus);
        XPathNavigator copy = Tree("<row><a/><a/></row>"); Assert.True(copy.MoveToFirstChild());
        var registry = new CopyOriginRegistry(); registry.Set(copy, source.Focus);
        Assert.True(registry.TryGet(copy, out var origin));
        Assert.Null(origin.Accumulators);
        Assert.NotNull(origin.Original); Assert.True(origin.Original!.IsSamePosition(source.Focus));
    }

    [Fact]
    public void ForkCopiesCaptureTheOriginalFramesPublishedPhases()
    {
        using var source = new StreamedForkAccumulatorTests.Source("<doc><row id='first'/><row id='second'/></doc>");
        XPathNavigator? copy = null;
        source.Run(source.Focus, view => {
            Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToFirstChild());
            copy = (XPathNavigator)Assert.Single(source.Library.X31CopyWithAccumulators(new XPathItem[] { view }));
            Assert.Equal("1", source.Value(copy)); Assert.Equal("11", source.Value(copy, after: true));
        });
        source.Close();
        Assert.NotNull(copy); Assert.Equal("1", source.Value(copy!)); Assert.Equal("11", source.Value(copy!, after: true));
    }

    [Fact]
    public void OpenReplayCopiesKeepTheirLiveValuesUntilTheirCaptureEnds()
    {
        using var source = new StreamedForkAccumulatorTests.Source("<doc><row/><row/></doc>");
        using (source.Focus.BeginConsumption(replay: true, accumulators: true))
        {
            Assert.True(source.Focus.MoveToFirstChild()); Assert.True(source.Focus.MoveToFirstChild());
            XPathNavigator copy = Tree("<row/>"); Assert.True(copy.MoveToFirstChild());
            var registry = new CopyOriginRegistry(); registry.Set(copy, source.Focus);
            Assert.True(registry.TryGet(copy, out var origin));
            Assert.NotNull(origin.Accumulators); Assert.Null(origin.Original);
            Assert.True(origin.Accumulators!.TryValue(copy, "Q{}n", false, out var before));
            Assert.Equal("1", Assert.Single(before!).Value);
            Assert.True(origin.Accumulators.TryValue(copy, "Q{}n", true, out var after));
            Assert.Equal("11", Assert.Single(after!).Value);
        }
    }

}
