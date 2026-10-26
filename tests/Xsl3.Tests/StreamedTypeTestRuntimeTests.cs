// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.XPath31;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedTypeTestRuntimeTests
{
    private static (XsltLibrary Library, int Type) Runtime(SequenceTypeTest required)
    {
        var constants = new ConstantTable(); int type = constants.Add(required);
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true, Constants = constants };
        return (new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter()).XsltFunctions, type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AContinuingCheckDoesNotOwnDiscardedSourceFrames(bool assertion)
    {
        var (library, type) = Runtime(new(new NodeKindTest(null), Cardinality.ZeroOrMore));
        var state = library.X31TypeTestNew(type, assertion ? "XPDY0050" : "");
        using var input = XmlReader.Create(new StringReader("<doc><first/><second/></doc>")); using var source = new StreamedDocument(input);
        var nav = (StreamedNavigator)source.CreateNavigator(); Assert.True(nav.MoveToFirstChild());
        WeakReference frame = InspectFirst(library, state, nav);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Assert.False(frame.IsAlive);
        Assert.True(library.X31TypeTestAccept(state, nav)); Assert.True(library.X31TypeTestResult(state));
        GC.KeepAlive(state); GC.KeepAlive(source);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference InspectFirst(XsltLibrary library, XPathItem state, StreamedNavigator nav)
    {
        Assert.True(nav.MoveToFirstChild()); var weak = new WeakReference(nav.PositionNode);
        Assert.True(library.X31TypeTestAccept(state, nav)); Assert.True(nav.MoveToNext()); return weak;
    }

    [Fact]
    public void AssertionFailuresRemainTerminal()
    {
        var (library, type) = Runtime(new(new AtomicTypeTest(AtomicTypes.Integer), Cardinality.OneOrMore));
        var state = library.X31TypeTestNew(type, "XPDY0050");
        Assert.True(library.X31TypeTestAccept(state, AtomicValue.OfInteger(1)));
        var error = Assert.Throws<XdmException>(() => library.X31TypeTestAccept(state, AtomicValue.OfString("wrong")));
        Assert.Equal("XPDY0050", error.Code);
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31TypeTestResult(state)));
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31TypeTestAccept(state, AtomicValue.OfInteger(2))));
    }

    [Fact]
    public void MissingRequiredItemsFailAtCompletionAndRemainTerminal()
    {
        var (library, type) = Runtime(new(new NodeKindTest(null), Cardinality.OneOrMore)); var state = library.X31TypeTestNew(type, "XPDY0050");
        var error = Assert.Throws<XdmException>(() => library.X31TreatEnd(state)); Assert.Equal("XPDY0050", error.Code);
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31TreatEnd(state)));
    }

    [Fact]
    public void CancellationDoesNotPermitResumingAnInspection()
    {
        var (library, type) = Runtime(SequenceTypeTest.AnySequence); var state = library.X31TypeTestNew(type, "");
        using var cancellation = new System.Threading.CancellationTokenSource(); cancellation.Cancel();
        var previous = XmlQueryRuntime.Cancellation; OperationCanceledException error;
        try { XmlQueryRuntime.Cancellation = cancellation.Token; error = Assert.Throws<OperationCanceledException>(() => library.X31TypeTestAccept(state, AtomicValue.OfInteger(1))); }
        finally { XmlQueryRuntime.Cancellation = previous; }
        Assert.Same(error, Assert.Throws<OperationCanceledException>(() => library.X31TypeTestResult(state)));
    }
}
