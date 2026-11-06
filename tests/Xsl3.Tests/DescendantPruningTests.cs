// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class DescendantPruningTests
{
    [Fact]
    public void PruningDoesNotHideMissingMatchesFromIndependentCursors()
    {
        using var reader = XmlReader.Create(new StringReader("<doc><a><a>nested</a>tail</a><a>last</a></doc>"));
        using var document = new StreamedDocument(reader);
        var root = document.CreateNavigator(); Assert.True(root.MoveToFirstChild());
        var independent = new DescendantIterator(); independent.Create(root, XmlNavNameFilter.Create("a", ""), false);
        Assert.True(independent.MoveNext());
        var owned = new DescendantIterator();
        var pruning = new DescendantPruning();
        using (pruning.Enter())
        {
            owned.Create(root, XmlNavNameFilter.Create("a", ""), false);
            Assert.True(owned.MoveNext());
            pruning.Prune(owned.Current);
        }
        Assert.Equal("nestedtail", owned.Current.Value);
        Assert.True(owned.MoveNext()); Assert.Equal("last", owned.Current.Value);
        Assert.False(owned.MoveNext());
        Assert.Equal("stream-position", Assert.Throws<XdmException>(() => independent.MoveNext()).Code);
    }

    [Fact]
    public void NestedScopesRestoreTheirParentAfterAnException()
    {
        using var reader = XmlReader.Create(new StringReader("<doc><a><a>nested</a></a><a>last</a></doc>"));
        using var document = new StreamedDocument(reader);
        var root = document.CreateNavigator(); Assert.True(root.MoveToFirstChild());
        var cursor = new DescendantIterator();
        var outer = new DescendantPruning(); var inner = new DescendantPruning();
        using (outer.Enter())
        {
            Assert.Throws<InvalidOperationException>((Action)(() => { using var scope = inner.Enter(); throw new InvalidOperationException(); }));
            cursor.Create(root, XmlNavNameFilter.Create("a", ""), false);
            Assert.True(cursor.MoveNext()); outer.Prune(cursor.Current);
        }
        Assert.Equal("nested", cursor.Current.Value);
        Assert.True(cursor.MoveNext()); Assert.Equal("last", cursor.Current.Value);
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void AnOrSelfYieldIsOwnedBeforeItsFirstDescendantAdvance()
    {
        using var reader = XmlReader.Create(new StringReader("<doc><a><a>nested</a></a><a>last</a></doc>"));
        using var document = new StreamedDocument(reader);
        var root = document.CreateNavigator(); Assert.True(root.MoveToFirstChild()); Assert.True(root.MoveToFirstChild());
        var cursor = new DescendantIterator(); var pruning = new DescendantPruning();
        using (pruning.Enter())
        {
            cursor.Create(root, XmlNavNameFilter.Create("a", ""), true);
            Assert.True(cursor.MoveNext()); pruning.Prune(cursor.Current);
        }
        Assert.Equal("nested", cursor.Current.Value);
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void ALongPullDoesNotOwnAbandonedDescendantCursors()
    {
        using var reader = XmlReader.Create(new StringReader("<doc><a/></doc>"));
        using var document = new StreamedDocument(reader);
        var pruning = new DescendantPruning();
        using var scope = pruning.Enter();
        WeakReference cursor = AbandonedCursor(document);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(cursor.IsAlive);
        GC.KeepAlive(document); GC.KeepAlive(pruning);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonedCursor(StreamedDocument document)
    {
        var cursor = new StreamedDescendantCursor((StreamedNavigator)document.CreateNavigator(), XmlNavNeverFilter.Create());
        return new WeakReference(cursor);
    }

}
