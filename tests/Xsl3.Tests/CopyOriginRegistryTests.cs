// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.Tree;
using Xunit;

namespace Xsl3.Tests;

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
        Assert.True(origin.Original.IsSamePosition(original));
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
}
