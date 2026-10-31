// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class AdjacentGroupRuntimeTests
{
    private static XsltLibrary Library()
    {
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true };
        return new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter()).XsltFunctions;
    }

    private static XPathItem Tuple(string value) => new XsltLibrary.GroupTuple(AtomicValue.OfString(value), value);

    [Fact]
    public void EmptyGroupsKeepZeroSizeAfterRepeatedReads()
    {
        var library = Library();
        var state = library.X31AdjacentGroupsNew(Array.Empty<XPathItem>(), "", false);
        Assert.Equal(0, library.X31AdjacentGroupCount(state));
        Assert.Empty(library.X31AdjacentGroupNext(state));
        Assert.Empty(library.X31AdjacentGroupNext(state));
        Assert.Equal(0, library.X31AdjacentGroupCount(state));
    }

    [Fact]
    public void CompletedCursorAndRetainedFrameReleasePopulationMetadata()
    {
        var (library, state, frame, first, second) = Completed();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(first.IsAlive); Assert.False(second.IsAlive);
        Assert.Equal("a", Assert.Single(library.X31GroupMembers(frame)).Value);
        Assert.Equal("a", Assert.Single(library.X31GroupKey(frame)).Value);
        Assert.Equal(2, library.X31AdjacentGroupCount(state));
        Assert.Empty(library.X31AdjacentGroupNext(state));
        GC.KeepAlive(frame); GC.KeepAlive(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (XsltLibrary, XPathItem, XPathItem, WeakReference, WeakReference) Completed()
    {
        var library = Library();
        var first = Tuple("a"); var second = Tuple("b");
        var state = library.X31AdjacentGroupsNew(new[] { first, second }, "", false);
        var frame = Assert.Single(library.X31AdjacentGroupNext(state));
        Assert.Single(library.X31AdjacentGroupNext(state));
        Assert.Empty(library.X31AdjacentGroupNext(state));
        return (library, state, frame, new WeakReference(first), new WeakReference(second));
    }

    [Fact]
    public void CancelledCursorCannotResumeAfterTheTokenIsRestored()
    {
        var library = Library();
        var state = library.X31AdjacentGroupsNew(new[] { Tuple("a"), Tuple("b") }, "", false);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var previous = XmlQueryRuntime.Cancellation; OperationCanceledException error;
        try
        {
            XmlQueryRuntime.Cancellation = cancellation.Token;
            error = Assert.Throws<OperationCanceledException>(() => library.X31AdjacentGroupNext(state));
        }
        finally { XmlQueryRuntime.Cancellation = previous; }
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31AdjacentGroupNext(state)));
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31AdjacentGroupCount(state)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedAndCompositeKeysMatchTheExistingMaterializedPath(bool composite)
    {
        var keys = new List<IList<XPathItem>>
        {
            new XPathItem[] { AtomicValue.OfString("a") },
            new XPathItem[] { AtomicValue.OfUntyped("a") },
            new XPathItem[] { new StringValue(AtomicTypes.AnyUri, "a") },
            new XPathItem[] { new FloatValue(0.1f) },
            new XPathItem[] { AtomicValue.OfDouble(0.1) },
            new XPathItem[] { AtomicValue.OfInteger(1) },
            new XPathItem[] { AtomicValue.OfDouble(1) },
            new XPathItem[] { new FloatValue(float.NaN) },
            new XPathItem[] { AtomicValue.OfDouble(double.NaN) }
        };
        if (composite)
        {
            keys.Add(Array.Empty<XPathItem>()); keys.Add(Array.Empty<XPathItem>());
            keys.Add(new XPathItem[] { AtomicValue.OfString("a"), AtomicValue.OfInteger(1) });
            keys.Add(new XPathItem[] { AtomicValue.OfUntyped("a"), AtomicValue.OfDouble(1) });
            keys.Add(new XPathItem[] { AtomicValue.OfString("a"), AtomicValue.OfInteger(2) });
        }
        var library = Library();
        XPathItem[] tuples = keys.Select((key, index) => (XPathItem)new XsltLibrary.GroupTuple(AtomicValue.OfInteger(index), key)).ToArray();
        var expected = library.X31Groups(tuples, 1, "", composite);
        var state = library.X31AdjacentGroupsNew(tuples, "", composite);
        Assert.Equal(expected.Count, library.X31AdjacentGroupCount(state));
        foreach (XPathItem group in expected)
        {
            var actual = Assert.Single(library.X31AdjacentGroupNext(state));
            Assert.Equal(library.X31GroupMembers(group), library.X31GroupMembers(actual));
            Assert.Equal(library.X31GroupKey(group).Select(k => (((AtomicValue)k).Type.Name, k.Value)),
                library.X31GroupKey(actual).Select(k => (((AtomicValue)k).Type.Name, k.Value)));
        }
        Assert.Empty(library.X31AdjacentGroupNext(state));
        Assert.Equal(expected.Count, library.X31AdjacentGroupCount(state));
    }

    [Fact]
    public void CancelledPreparationRejectsEvenAnEmptyPopulation()
    {
        var library = Library();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var previous = XmlQueryRuntime.Cancellation;
        try
        {
            XmlQueryRuntime.Cancellation = cancellation.Token;
            Assert.Throws<OperationCanceledException>(() => library.X31AdjacentGroupsNew(Array.Empty<XPathItem>(), "", false));
        }
        finally { XmlQueryRuntime.Cancellation = previous; }
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive")]
    [InlineData(true, "http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive")]
    public void UncachedStringsAndMixedAtomicKeysUseTheCanonicalCollation(bool composite, string collation)
    {
        var library = Library();
        XPathItem[] tuples =
        {
            new XsltLibrary.GroupTuple(AtomicValue.OfInteger(0), "A"),
            new XsltLibrary.GroupTuple(AtomicValue.OfInteger(1), new XPathItem[] { AtomicValue.OfString("a") }),
            new XsltLibrary.GroupTuple(AtomicValue.OfInteger(2), "a"),
            new XsltLibrary.GroupTuple(AtomicValue.OfInteger(3), new XPathItem[] { new StringValue(AtomicTypes.AnyUri, "A") }),
            new XsltLibrary.GroupTuple(AtomicValue.OfInteger(4), "b"),
            new XsltLibrary.GroupTuple(AtomicValue.OfInteger(5), "B"),
            new XsltLibrary.GroupTuple(AtomicValue.OfInteger(6), new XPathItem[] { AtomicValue.OfUntyped("b") }),
            new XsltLibrary.GroupTuple(AtomicValue.OfInteger(7), "\U0001F600"),
            new XsltLibrary.GroupTuple(AtomicValue.OfInteger(8), "\U0001F600")
        };
        // Consume the cursor before the old path can fill the tuple's cached key arrays.
        var state = library.X31AdjacentGroupsNew(tuples, collation, composite);
        var actual = new List<XPathItem>();
        for (var next = library.X31AdjacentGroupNext(state); next.Count != 0; next = library.X31AdjacentGroupNext(state)) actual.Add(next[0]);
        var expected = library.X31Groups(tuples, 1, collation, composite);
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < actual.Count; i++)
        {
            Assert.Equal(library.X31GroupMembers(expected[i]), library.X31GroupMembers(actual[i]));
            Assert.Equal(library.X31GroupKey(expected[i]).Select(k => (((AtomicValue)k).Type.Name, k.Value)),
                library.X31GroupKey(actual[i]).Select(k => (((AtomicValue)k).Type.Name, k.Value)));
        }
    }
}
