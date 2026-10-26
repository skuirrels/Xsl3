// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class ClimbingSetRuntimeTests
{
    private static XsltLibrary Runtime()
    {
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true, Constants = new ConstantTable() };
        return new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter()).XsltFunctions;
    }

    private static XPathNavigator[] Nodes()
    {
        var node = XdmNode.Load(new StringReader("<doc><a/><b/></doc>")).Navigator;
        Assert.True(node.MoveToFirstChild()); Assert.True(node.MoveToFirstChild());
        var first = node.Clone(); Assert.True(node.MoveToNext()); return new[] { first, node.Clone() };
    }

    [Fact]
    public void PendingMembershipSurvivesEventsWithoutRepeatingAnEmittedHeader()
    {
        var library = Runtime(); var nodes = Nodes(); var state = library.X31ClimbingSetNew("0 1 I");
        library.X31ClimbingSetAdd(state, 0, new[] { nodes[1], nodes[0], nodes[0].Clone() });
        Assert.Empty(library.X31ClimbingSetDrain(state));
        library.X31ClimbingSetAdd(state, 1, new[] { nodes[1], nodes[0].Clone() });
        var result = library.X31ClimbingSetDrain(state);
        Assert.Equal(2, result.Count); Assert.True(result[0].IsSamePosition(nodes[0])); Assert.True(result[1].IsSamePosition(nodes[1]));
        library.X31ClimbingSetAdd(state, 1, nodes); Assert.Empty(library.X31ClimbingSetDrain(state));
        Assert.Empty(library.X31ClimbingSetEnd(state)); Assert.Empty(library.X31ClimbingSetDrain(state));
    }

    [Fact]
    public void AnOrderingFailureCannotResumeOrEmitAnotherPartialResult()
    {
        var library = Runtime(); var nodes = Nodes(); var state = library.X31ClimbingSetNew("0 1 U");
        library.X31ClimbingSetAdd(state, 0, new[] { nodes[1] }); Assert.Single(library.X31ClimbingSetDrain(state));
        library.X31ClimbingSetAdd(state, 1, new[] { nodes[0] });
        var error = Assert.Throws<InvalidOperationException>(() => library.X31ClimbingSetDrain(state));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => library.X31ClimbingSetDrain(state)));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => library.X31ClimbingSetAdd(state, 0, nodes)));
        Assert.Empty(library.X31ClimbingSetEnd(state));
    }

    [Fact]
    public void CancellationRemainsTerminalAfterTheAmbientTokenIsRestored()
    {
        var library = Runtime(); var state = library.X31ClimbingSetNew("0 1 I");
        using var cancellation = new System.Threading.CancellationTokenSource(); cancellation.Cancel();
        var previous = XmlQueryRuntime.Cancellation; OperationCanceledException error;
        try
        {
            XmlQueryRuntime.Cancellation = cancellation.Token;
            error = Assert.Throws<OperationCanceledException>(() => library.X31ClimbingSetDrain(state));
        }
        finally { XmlQueryRuntime.Cancellation = previous; }
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31ClimbingSetAdd(state, 0, Nodes())));
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31ClimbingSetDrain(state)));
    }
    [Fact]
    public void ACompletedStateReleasesForeignGroundedDocumentRoots()
    {
        var library = Runtime(); var state = library.X31ClimbingSetNew("0 1 U");
        WeakReference document = CompletedForeignDocument(library, state);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(document.IsAlive); GC.KeepAlive(state); GC.KeepAlive(library);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CompletedForeignDocument(XsltLibrary library, XPathItem state)
    {
        var document = new XmlDocument(); document.LoadXml("<doc/>");
        library.X31ClimbingSetAdd(state, 0, new[] { document.CreateNavigator()! });
        Assert.Single(library.X31ClimbingSetDrain(state)); library.X31ClimbingSetEnd(state);
        return new WeakReference(document);
    }

    [Fact]
    public void DeferredGroundedResultsSortLateDiscoveriesAndDeduplicateBeforeCompletion()
    {
        var library = Runtime(); var nodes = Nodes(); var state = library.X31DeferredClimbingSetNew("0 1 E", false);
        library.X31DeferredClimbingSetBegin(state, nodes[1], new[] { nodes[1] });
        library.X31DeferredClimbingSetAdd(state, 0, new[] { nodes[1] });
        Assert.Empty(library.X31DeferredClimbingSetDrain(state, false));
        library.X31DeferredClimbingSetBegin(state, nodes[0], new[] { nodes[0], nodes[1].Clone() });
        library.X31DeferredClimbingSetAdd(state, 0, new[] { nodes[0], nodes[1].Clone() });
        var first = Assert.Single(library.X31DeferredClimbingSetDrain(state, true));
        var second = Assert.Single(library.X31DeferredClimbingSetDrain(state, true));
        Assert.True(first.IsSamePosition(nodes[0])); Assert.True(second.IsSamePosition(nodes[1]));
        Assert.Empty(library.X31DeferredClimbingSetDrain(state, true));
        Assert.Empty(library.X31DeferredClimbingSetDrain(state, false));
    }

    [Fact]
    public void DeferredCompletionReleasesForeignGroundedDocumentRoots()
    {
        var library = Runtime(); var state = library.X31DeferredClimbingSetNew("0 1 E", false);
        WeakReference document = CompletedDeferredDocument(library, state);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(document.IsAlive); GC.KeepAlive(state); GC.KeepAlive(library);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CompletedDeferredDocument(XsltLibrary library, XPathItem state)
    {
        var document = new XmlDocument(); document.LoadXml("<doc/>");
        var node = document.CreateNavigator()!;
        library.X31DeferredClimbingSetBegin(state, node, new[] { node });
        library.X31DeferredClimbingSetAdd(state, 0, new[] { node });
        Assert.Single(library.X31DeferredClimbingSetDrain(state, true));
        Assert.Empty(library.X31DeferredClimbingSetDrain(state, true));
        return new WeakReference(document);
    }

    [Fact]
    public void DeferredCancellationRemainsTerminalAfterTheAmbientTokenIsRestored()
    {
        var library = Runtime(); var state = library.X31DeferredClimbingSetNew("0 1 E", true);
        using var cancellation = new System.Threading.CancellationTokenSource(); cancellation.Cancel();
        var previous = XmlQueryRuntime.Cancellation; OperationCanceledException error;
        try
        {
            XmlQueryRuntime.Cancellation = cancellation.Token;
            error = Assert.Throws<OperationCanceledException>(() => library.X31DeferredClimbingSetDrain(state, false));
        }
        finally { XmlQueryRuntime.Cancellation = previous; }
        var nodes = Nodes();
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31DeferredClimbingSetAdd(state, 0, nodes)));
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31DeferredClimbingSetBegin(state, nodes[0], nodes)));
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31DeferredClimbingSetDrain(state, true)));
    }

}
