// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.Tests;

public class PullCallRuntimeTests(ITestOutputHelper output)
{
    private static (XsltLibrary Library, CompiledFunction Function) Compile(string expression = "function($state){$state}")
    {
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema'>"
            + "<xsl:template name='xsl:initial-template'><xsl:sequence select='" + SecurityElement.Escape(expression) + "'/></xsl:template></xsl:stylesheet>";
        var value = new XsltCompiler().Compile(new StringReader(sheet)).CreateTransformer().CallTemplate(null);
        var function = Assert.IsType<CompiledFunction>(Assert.Single(value.Items));
        return (function.Owner.XsltFunctions, function);
    }

    [Fact]
    public void RepeatedCompiledResumptionsReuseConvertedArguments()
    {
        var (library, function) = Compile(); var state = library.X31PullNew(function);
        var argument = Sequences.One(state);
        for (int i = 0; i < 128; i++)
        {
            Assert.Same(state, library.CallFunction(function, argument)[0]);
            Assert.Same(state, library.X31PullNext(state)[0]);
        }
        bool correct = true;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            var batch = library.CallFunction(function, argument);
            correct &= batch.Count == 1 && ReferenceEquals(state, batch[0]);
        }
        long ordinary = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            var batch = library.X31PullNext(state);
            correct &= batch.Count == 1 && ReferenceEquals(state, batch[0]);
        }
        long resumed = GC.GetAllocatedBytesForCurrentThread() - before;
        XsltLibrary.X31PullEnd(argument);
        output.WriteLine($"Ten thousand identity calls: ordinary={ordinary}, resumed={resumed} bytes");
        Assert.True(correct);
        // Both paths execute the same compiled body and allocate its result containers.
        // Reusing the converted argument must remove most of the ordinary call overhead.
        Assert.True(resumed < ordinary / 2, $"Ordinary calls allocated {ordinary} bytes; resumptions allocated {resumed} bytes");
    }

    [Fact]
    public void CompiledProducerFaultsRemainTerminal()
    {
        var (library, function) = Compile("function($state){error()}"); var state = library.X31PullNew(function);
        var error = Assert.Throws<XdmException>(() => library.X31PullNext(state));
        Assert.Equal("FOER0000", error.Code);
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31PullNext(state)));
    }

    [Fact]
    public void CompiledArgumentConversionRetainsTheCanonicalFailure()
    {
        var (library, function) = Compile("function($state as xs:integer){$state}"); var state = library.X31PullNew(function);
        var expected = Assert.Throws<XdmException>(() => library.CallFunction(function, Sequences.One(state)));
        var actual = Assert.Throws<XdmException>(() => library.X31PullNext(state));
        Assert.Equal(expected.Code, actual.Code);
        Assert.Same(actual, Assert.Throws<XdmException>(() => library.X31PullNext(state)));
    }

    [Fact]
    public void CompletedCompiledCallsReleaseTheirProducer()
    {
        var (state, producer) = Completed();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(producer.IsAlive); GC.KeepAlive(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (XPathItem, WeakReference) Completed()
    {
        var (library, function) = Compile(); var state = library.X31PullNew(function);
        Assert.Same(state, library.X31PullNext(state)[0]); XsltLibrary.X31PullEnd(new[] { state });
        return (state, new WeakReference(function));
    }

    [Fact]
    public void NativeProducersKeepFreshMutableArgumentLists()
    {
        var (library, _) = Compile(); int calls = 0;
        var function = new NativeFunction(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence, arguments =>
        {
            Assert.Single(arguments[0]); Assert.IsNotType<IntegerValue>(arguments[0][0]);
            arguments[0][0] = AtomicValue.OfInteger(-1); calls++;
            return Sequences.One(AtomicValue.OfInteger(calls));
        }, "test mutable native producer");
        var state = library.X31PullNew(function);
        Assert.Equal("1", library.X31PullNext(state)[0].Value); Assert.Equal("2", library.X31PullNext(state)[0].Value);
        XsltLibrary.X31PullEnd(new[] { state });
    }
}
