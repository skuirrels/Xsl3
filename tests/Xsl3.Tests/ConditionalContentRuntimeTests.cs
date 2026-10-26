// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class ConditionalContentRuntimeTests
{
    private static XmlQueryRuntime RuntimeState()
    {
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true, Constants = new ConstantTable() };
        return new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter());
    }

    private static XsltLibrary Runtime() => RuntimeState().XsltFunctions;

    private static NativeFunction Producer(Func<IList<XPathItem>> next) =>
        new(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence, _ => next(), "test conditional population");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceFaultsRemainTerminalAndDoNotResumeTheProducer(bool complex)
    {
        var library = Runtime(); int reads = 0; var error = new XdmException("TEST0001", "late source failure");
        var producer = Producer(() => ++reads == 1 ? Sequences.One(AtomicValue.OfInteger(1)) : throw error);
        var state = complex ? library.X31ContentNew(producer) : library.X31OnEmptyNew(producer);
        IList<XPathItem> Next() => complex ? library.X31ContentNext(state) : library.X31OnEmptyNext(state);
        Assert.Single(Next());
        Assert.Same(error, Assert.Throws<XdmException>(() => Next()));
        Assert.Same(error, Assert.Throws<XdmException>(() => Next()));
        Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationIsTerminalAndDoesNotReadTheProducer(bool complex)
    {
        var library = Runtime(); int reads = 0;
        var producer = Producer(() => { reads++; return Sequences.Empty; });
        var state = complex ? library.X31ContentNew(producer) : library.X31OnEmptyNew(producer);
        IList<XPathItem> Next() => complex ? library.X31ContentNext(state) : library.X31OnEmptyNext(state);
        using var cancellation = new System.Threading.CancellationTokenSource(); cancellation.Cancel();
        var previous = XmlQueryRuntime.Cancellation; OperationCanceledException error;
        try { XmlQueryRuntime.Cancellation = cancellation.Token; error = Assert.Throws<OperationCanceledException>(() => Next()); }
        finally { XmlQueryRuntime.Cancellation = previous; }
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => Next())); Assert.Equal(0, reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedPopulationsReleaseTheirProducerAndDoNotRestartIt(bool complex)
    {
        var library = Runtime(); var (state, producer) = Complete(library, complex);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Assert.False(producer.IsAlive);
        Assert.Empty(complex ? library.X31ContentNext(state) : library.X31OnEmptyNext(state)); GC.KeepAlive(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (XPathItem, WeakReference) Complete(XsltLibrary library, bool complex)
    {
        var producer = Producer(() => Sequences.Empty);
        var state = complex ? library.X31ContentNew(producer) : library.X31OnEmptyNew(producer);
        Assert.Empty(complex ? library.X31ContentNext(state) : library.X31OnEmptyNext(state));
        return (state, new WeakReference(producer));
    }

    [Fact]
    public void ContinuingConditionalContentDoesNotOwnTransmittedSourceFrames()
    {
        var library = Runtime(); using var input = XmlReader.Create(new StringReader("<doc><first/><second/></doc>")); using var document = new StreamedDocument(input);
        var nav = (StreamedNavigator)document.CreateNavigator(); Assert.True(nav.MoveToFirstChild());
        bool first = true;
        var state = library.X31OnEmptyNew(Producer(() =>
        {
            if (first) { first = false; return Sequences.One(nav.Clone()); }
            return nav.MoveToNext() ? Sequences.One(nav.Clone()) : Sequences.Empty;
        }));
        WeakReference frame = TransmitFirst(library, state, nav);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Assert.False(frame.IsAlive);
        GC.KeepAlive(state); GC.KeepAlive(document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuspendedAnnotationScopesArePrivateAndRestoredAfterCompletionOrFault(bool fault)
    {
        var runtime = RuntimeState(); var library = runtime.XsltFunctions;
        var callerScopes = new Stack<bool>(new[] { true, false }); runtime.AnnotationScopes = callerScopes;
        var error = new XdmException("TEST0001", "producer failure"); int reads = 0;
        var producer = new NativeFunction(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence, arguments =>
        {
            reads++;
            if (reads == 1)
            {
                Assert.False(runtime.AnnotationScopes!.Peek());
                runtime.AnnotationScopes.Push(true); runtime.PendingAnyType = true;
                return Sequences.One(AtomicValue.OfInteger(1));
            }
            Assert.True(runtime.AnnotationScopes!.Peek()); Assert.True(runtime.PendingAnyType);
            runtime.AnnotationScopes.Pop(); runtime.PendingAnyType = false;
            if (fault) throw error;
            return XsltLibrary.X31PullEnd(arguments[0]);
        }, "test annotation population");
        var state = library.X31PullNew(producer);
        Assert.Single(library.X31PullNext(state));
        Assert.Same(callerScopes, runtime.AnnotationScopes); Assert.Equal(2, callerScopes.Count); Assert.False(runtime.PendingAnyType);
        callerScopes.Push(false);
        if (fault)
        {
            Assert.Same(error, Assert.Throws<XdmException>(() => library.X31PullNext(state)));
            Assert.Same(error, Assert.Throws<XdmException>(() => library.X31PullNext(state)));
        }
        else { Assert.Empty(library.X31PullNext(state)); Assert.Empty(library.X31PullNext(state)); }
        Assert.Same(callerScopes, runtime.AnnotationScopes); Assert.Equal(3, callerScopes.Count); Assert.False(runtime.PendingAnyType);
        Assert.Equal(2, reads);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference TransmitFirst(XsltLibrary library, XPathItem state, StreamedNavigator nav)
    {
        Assert.True(nav.MoveToFirstChild()); var frame = new WeakReference(nav.PositionNode);
        Assert.Single(library.X31OnEmptyNext(state));
        Assert.Single(library.X31OnEmptyNext(state));
        return frame;
    }
}
