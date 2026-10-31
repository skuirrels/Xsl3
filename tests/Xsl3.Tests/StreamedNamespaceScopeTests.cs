// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class StreamedNamespaceScopeTests
{
    [Theory]
    [InlineData(XPathNamespaceScope.All)]
    [InlineData(XPathNamespaceScope.ExcludeXml)]
    [InlineData(XPathNamespaceScope.Local)]
    public void RepeatedNamespaceAxesDoNotAllocatePerEnumeration(XPathNamespaceScope scope)
    {
        using var reader = XmlReader.Create(new StringReader("<doc xmlns:p='urn:p' xmlns:q='urn:q'/>"));
        using var document = new StreamedDocument(reader);
        XPathNavigator nav = document.CreateNavigator(); Assert.True(nav.MoveToFirstChild());
        int count = Enumerate(nav, scope); Assert.True(count >= 2);
        // The first traversal builds the scope; warm its cached path separately.
        int warmed = 0;
        for (int i = 0; i < 10000; i++) warmed += Enumerate(nav, scope);
        Assert.Equal(count * 10000, warmed);
        long start = GC.GetAllocatedBytesForCurrentThread();
        int total = 0;
        for (int i = 0; i < 10000; i++) total += Enumerate(nav, scope);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(count * 10000, total);
        Assert.True(allocated < 4096, $"Namespace enumeration allocated {allocated} bytes after warmup");
    }

    private static int Enumerate(XPathNavigator nav, XPathNamespaceScope scope)
    {
        if (!nav.MoveToFirstNamespace(scope)) return 0;
        int count = 1;
        while (nav.MoveToNextNamespace(scope)) count++;
        nav.MoveToParent();
        return count;
    }

    [Fact]
    public void ScopeReusePreservesLocalOwnershipAndRebindingAcrossSiblings()
    {
        using var reader = XmlReader.Create(new StringReader("<doc xmlns:p='urn:outer'><a xmlns:q='urn:q'/><b/><c xmlns:p='urn:inner'/><d/></doc>"));
        using var document = new StreamedDocument(reader);
        var nav = (StreamedNavigator)document.CreateNavigator();
        Assert.True(nav.MoveToFirstChild()); Assert.True(nav.MoveToFirstChild());
        Assert.Equal("urn:q", nav.GetNamespacesInScope(XmlNamespaceScope.Local)["q"]);
        XPathNavigator old = nav.Clone();
        Assert.True(nav.MoveToNext());
        Assert.Empty(nav.GetNamespacesInScope(XmlNamespaceScope.Local));
        Assert.Equal("urn:outer", nav.LookupNamespace("p"));
        Assert.True(nav.MoveToNamespace("p")); Assert.Equal("urn:outer", nav.Value); Assert.True(nav.MoveToParent());
        Assert.True(nav.MoveToNext());
        Assert.Equal("urn:inner", nav.GetNamespacesInScope(XmlNamespaceScope.Local)["p"]);
        Assert.True(nav.MoveToNext());
        Assert.Empty(nav.GetNamespacesInScope(XmlNamespaceScope.Local));
        Assert.Equal("urn:outer", nav.GetNamespacesInScope(XmlNamespaceScope.ExcludeXml)["p"]);
        Assert.True(old.MoveToNamespace("q")); Assert.Equal("urn:q", old.Value);
        Assert.True(old.MoveToParent()); Assert.Equal("a", old.LocalName);
        Assert.Equal("d", reader.LocalName);
    }

    [Fact]
    public void ResolvedScopesDoNotRetainExpiredSourceFrames()
    {
        using var reader = XmlReader.Create(new StringReader("<doc xmlns:p='urn:p'><a xmlns:q='urn:q'/><b/><c/></doc>"));
        using var document = new StreamedDocument(reader);
        WeakReference first = ResolveAndAdvance(document);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(first.IsAlive);
        GC.KeepAlive(document);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ResolveAndAdvance(StreamedDocument document)
    {
        var nav = (StreamedNavigator)document.CreateNavigator();
        nav.MoveToFirstChild(); nav.MoveToFirstChild();
        Enumerate(nav, XPathNamespaceScope.All);
        var first = new WeakReference(nav.PositionNode);
        nav.MoveToNext(); Enumerate(nav, XPathNamespaceScope.All);
        nav.MoveToNext(); Enumerate(nav, XPathNamespaceScope.All);
        return first;
    }
}
