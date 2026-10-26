// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class MaterializedMergeRuntimeTests
{
    private static (XsltLibrary Library, XPathItem State) State(params XPathItem[] rows)
    {
        var constants = new ConstantTable(); var spec = new MergeSpec(); spec.SortBeforeMerge.Add(false);
        int id = constants.Add(spec);
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true, Constants = constants };
        var library = new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter()).XsltFunctions;
        var options = library.X31MergeKeyOptions("ascending", "", "", "", "", 0, "");
        var plan = library.X31MergePlan(new[] { options }, 1, id);
        var keys = new NativeFunction(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence,
            args => new XPathItem[] { ArrayItem.Of(new[] { args[0] }) }, "test merge key");
        var input = ArrayItem.Of(new IList<XPathItem>[] { rows });
        var source = ArrayItem.Of(new IList<XPathItem>[] { new XPathItem[] { input } });
        return (library, library.X31MaterializedMergeNew(new XPathItem[] { source }, new XPathItem[] { keys }, plan));
    }

    [Fact]
    public void EmptyGroupsHaveZeroSizeAndRemainExhausted()
    {
        var (library, state) = State();
        Assert.Equal(0, library.X31MaterializedMergeSize(state));
        Assert.Empty(library.X31MaterializedMergeNextGroup(state));
        Assert.Empty(library.X31MaterializedMergeNextGroup(state));
    }

    [Fact]
    public void CompletedGroupsReleaseTheirRowsWhileKeepingTheTotalSize()
    {
        var (library, state, row) = ConsumedFirstGroup();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(row.IsAlive); GC.KeepAlive(state);
        Assert.Equal(2, library.X31MaterializedMergeSize(state));
        Assert.Single(library.X31MaterializedMergeNextGroup(state));
        Assert.Empty(library.X31MaterializedMergeNextGroup(state));
        Assert.Equal(2, library.X31MaterializedMergeSize(state));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (XsltLibrary Library, XPathItem State, WeakReference Row) ConsumedFirstGroup()
    {
        var row = AtomicValue.OfString("a" + Guid.NewGuid().ToString("N"));
        var weak = new WeakReference(row);
        var (library, state) = State(row, AtomicValue.OfString("b"));
        Assert.Single(library.X31MaterializedMergeNextGroup(state));
        return (library, state, weak);
    }

    [Fact]
    public void CancellationCannotResumeAGroupCursorAfterTheTokenIsRestored()
    {
        var (library, state) = State(AtomicValue.OfString("a"), AtomicValue.OfString("b"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var previous = XmlQueryRuntime.Cancellation; OperationCanceledException error;
        try
        {
            XmlQueryRuntime.Cancellation = cancellation.Token;
            error = Assert.Throws<OperationCanceledException>(() => library.X31MaterializedMergeNextGroup(state));
        }
        finally { XmlQueryRuntime.Cancellation = previous; }
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31MaterializedMergeNextGroup(state)));
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31MaterializedMergeSize(state)));
    }
}
