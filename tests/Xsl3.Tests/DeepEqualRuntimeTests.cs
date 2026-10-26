// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Runtime;
using Xsl3.Qil;
using Xsl3.Streaming;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class DeepEqualRuntimeTests
{
    private static XsltLibrary Runtime()
    {
        var constants = new ConstantTable();
        Assert.Equal(0, constants.Add(new StaticInfo(null, Collation.CodepointUri, DecimalFormatTable.Empty)));
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true, Constants = constants };
        return new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter()).XsltFunctions;
    }

    private static NativeFunction Producer(Func<IList<XPathItem>> next) =>
        new(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence, _ => next(), "test comparison population");

    private static XPathItem State(XsltLibrary library, params XPathItem[] reference) =>
        library.X31DeepEqualNew(0, reference, Array.Empty<XPathItem>(), false, -1);

    [Fact]
    public void SourceFaultsAreStickyAndDoNotResumeTheProducer()
    {
        var library = Runtime(); var error = new XdmException("TEST0001", "late source failure"); int reads = 0;
        var producer = Producer(() => ++reads == 1 ? new XPathItem[] { AtomicValue.OfInteger(1) } : throw error);
        var state = State(library, AtomicValue.OfInteger(1));
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31DeepEqualRun(state, producer)));
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31DeepEqualRun(state, producer)));
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31DeepEqualEnd(state)));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void ComparisonFaultsCannotReturnAPartialBoolean()
    {
        var library = Runtime(); var state = State(library, AtomicValue.OfInteger(1));
        var function = Producer(() => Array.Empty<XPathItem>());
        var error = Assert.Throws<XdmException>(() => library.X31DeepEqualItem(state, function));
        Assert.Equal("FOTY0015", error.Code);
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31DeepEqualEnd(state)));
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31DeepEqualItem(state, AtomicValue.OfInteger(1))));
    }

    [Fact]
    public void CancellationDoesNotStartOrResumeTheProducer()
    {
        var library = Runtime(); var state = State(library); int reads = 0;
        var producer = Producer(() => { reads++; return Array.Empty<XPathItem>(); });
        using var cancellation = new System.Threading.CancellationTokenSource(); cancellation.Cancel();
        var previous = XmlQueryRuntime.Cancellation; OperationCanceledException error;
        try { XmlQueryRuntime.Cancellation = cancellation.Token; error = Assert.Throws<OperationCanceledException>(() => library.X31DeepEqualRun(state, producer)); }
        finally { XmlQueryRuntime.Cancellation = previous; }
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31DeepEqualRun(state, producer)));
        Assert.Equal(0, reads);
    }

    [Fact]
    public void CompletedComparisonsReleaseTheirGroundedReference()
    {
        var library = Runtime(); var (state, reference) = Completed(library);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(reference.IsAlive); GC.KeepAlive(state);
        Assert.False(library.X31DeepEqualEnd(state));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (XPathItem, WeakReference) Completed(XsltLibrary library)
    {
        MapItem map = MapItem.Empty.Put(AtomicValue.OfString("n"), new XPathItem[] { AtomicValue.OfInteger(1) });
        var state = State(library, map); Assert.False(library.X31DeepEqualEnd(state));
        return (state, new WeakReference(map));
    }

    [Fact]
    public void AGroundedReferenceCannotSilentlyContainALiveSourceSubtree()
    {
        var library = Runtime(); using var input = XmlReader.Create(new StringReader("<doc><n/></doc>"));
        using var document = new StreamedDocument(input); var root = document.CreateNavigator();
        Assert.Equal("stream-position", Assert.Throws<XdmException>(() => State(library, root)).Code);
        MapItem map = MapItem.Empty.Put(AtomicValue.OfString("n"), new XPathItem[] { root });
        Assert.Equal("stream-position", Assert.Throws<XdmException>(() => State(library, map)).Code);
        Assert.Equal(ReadState.Initial, input.ReadState);
    }

    [Fact]
    public void CompletedComparisonsDoNotRestartTheirProducer()
    {
        var library = Runtime(); var state = State(library, AtomicValue.OfInteger(1)); int reads = 0;
        var producer = Producer(() => ++reads == 1 ? new XPathItem[] { AtomicValue.OfInteger(1) } : Array.Empty<XPathItem>());
        Assert.True(library.X31DeepEqualRun(state, producer)); Assert.True(library.X31DeepEqualRun(state, producer));
        Assert.True(library.X31DeepEqualEnd(state)); Assert.Equal(2, reads);
    }
}
