// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class ForkBindingTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    internal static XmlQueryRuntime CreateRuntime() => new(new XmlQueryStaticData(new XmlWriterSettings(),
        Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true },
        XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter());

    internal static void Run(StreamedNavigator focus, params Action<StreamedNavigator>[] bodies)
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        using var fork = focus.Document.Fork(focus);
        fork.Run(bodies.Select<Action<StreamedNavigator>, Action<StreamedNavigator, CancellationToken>>(body =>
            (view, _) => body(view)).ToArray(), 1, cancellation.Token);
    }

    [Fact]
    public void HeadersAndAliasedPositionsBelongToEachProngsView()
    {
        using var reader = XmlReader.Create(new StringReader("<doc xmlns:p='urn:p' xml:lang='en'><n id='a'>value</n></doc>"));
        using var document = new StreamedDocument(reader);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild());
        XPathNavigator parent = focus.Clone();
        Assert.True(focus.MoveToFirstChild());
        XPathNavigator attribute = focus.Clone(), ns = focus.Clone();
        Assert.True(attribute.MoveToAttribute("id", ""));
        Assert.True(ns.MoveToNamespace("p"));
        XPathItem[] bindings = { focus, focus, parent, attribute, ns };
        XmlQueryRuntime owner = CreateRuntime();
        Run(focus, Check, Check);
        Assert.Equal("n", focus.LocalName);
        Assert.Equal("a", attribute.Value);

        void Check(StreamedNavigator view)
        {
            IList<XPathItem> values = owner.XsltFunctions.ProjectForkBindings(bindings, view);
            var node = Assert.IsType<StreamedNavigator>(values[0]);
            var alias = Assert.IsType<StreamedNavigator>(values[1]);
            Assert.NotSame(node, alias);
            Assert.Same(view.Document, node.Document);
            Assert.True(node.IsSamePosition(focus));
            Assert.True(node.MoveToParent());
            Assert.Equal("n", alias.LocalName);
            Assert.Equal("doc", ((XPathNavigator)values[2]).LocalName);
            Assert.Equal("a", values[3].Value);
            Assert.Equal("urn:p", values[4].Value);
            Assert.True(((XPathNavigator)values[4]).IsSamePosition(ns));
            Assert.Equal("value", alias.Value);
        }
    }

    [Fact]
    public void HeaderProjectionDoesNotReadInput()
    {
        using var reader = XmlReader.Create(new StringReader("<doc/>"));
        using var document = new StreamedDocument(reader);
        var focus = (StreamedNavigator)document.CreateNavigator();
        XmlQueryRuntime runtime = CreateRuntime();
        Run(focus, view => Assert.True(Assert.IsType<StreamedNavigator>(
            Assert.Single(runtime.XsltFunctions.ProjectForkBindings(new XPathItem[] { focus }, view))).IsSamePosition(focus)));
        Assert.Equal(ReadState.Initial, reader.ReadState);
    }

    [Fact]
    public void InspectedFirstChildUsesTheLocalTeeHeader()
    {
        using var reader = XmlReader.Create(new StringReader("<doc><n>value</n></doc>"));
        using var document = new StreamedDocument(reader);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild());
        XPathNavigator first = focus.Clone();
        Assert.True(first.MoveToFirstChild());
        XmlQueryRuntime runtime = CreateRuntime();
        Run(focus, Check, Check);

        void Check(StreamedNavigator view)
        {
            var child = Assert.IsType<StreamedNavigator>(Assert.Single(runtime.XsltFunctions.ProjectForkBindings(new XPathItem[] { first }, view)));
            Assert.True(child.IsSamePosition(first));
            Assert.Same(view.Document, child.Document);
            Assert.Equal("value", child.Value);
        }
    }

    [Fact]
    public void PastPositionsAndUnrelatedReadersCannotEscapeProjection()
    {
        using var reader = XmlReader.Create(new StringReader("<doc><a/><b/></doc>"));
        using var document = new StreamedDocument(reader);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild()); Assert.True(focus.MoveToFirstChild());
        XPathNavigator past = focus.Clone(); Assert.True(focus.MoveToNext());
        using var otherReader = XmlReader.Create(new StringReader("<other/>"));
        using var other = new StreamedDocument(otherReader);
        XmlQueryRuntime runtime = CreateRuntime();
        Run(focus, view => {
            foreach (XPathItem node in new XPathItem[] { past, other.CreateNavigator() })
                Assert.Equal("stream-position", Assert.Throws<XdmException>(() =>
                    runtime.XsltFunctions.ProjectForkBindings(new[] { node }, view)).Code);
        });
        Assert.Equal(ReadState.Initial, otherReader.ReadState);
    }

    [Fact]
    public void NestedForksReprojectCapturesOntoTheInnermostView()
    {
        using var reader = XmlReader.Create(new StringReader("<doc>value</doc>"));
        using var document = new StreamedDocument(reader);
        var focus = (StreamedNavigator)document.CreateNavigator();
        XmlQueryRuntime runtime = CreateRuntime();
        Run(focus, outer => Run(outer, inner => {
            var node = Assert.IsType<StreamedNavigator>(Assert.Single(runtime.XsltFunctions.ProjectForkBindings(new XPathItem[] { outer }, inner)));
            Assert.Same(inner.Document, node.Document);
            Assert.Equal("value", node.Value);
        }));
    }

    [Fact]
    public void NestedMapsArraysAndRecursiveClosuresKeepTheirGraphAndPrivateCursors()
    {
        using var reader = XmlReader.Create(new StringReader("<doc>value</doc>"));
        using var document = new StreamedDocument(reader);
        var focus = (StreamedNavigator)document.CreateNavigator();
        XmlQueryRuntime owner = CreateRuntime(), child = owner.CreateForkRuntime(new XmlCachedSequenceWriter());
        var function = new CompiledFunction(3, new(null, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence), owner);
        MapItem map = MapItem.Empty.Put(AtomicValue.OfString("self"), new XPathItem[] { function })
            .Put(AtomicValue.OfString("nodes"), new XPathItem[] { ArrayItem.Of(new[] { new XPathItem[] { focus, focus } }) });
        function.Captured.Add(new XPathItem[] { map });
        var prepared = owner.XsltFunctions.PrepareForkBindings(new XPathItem[] { function, function });
        Run(focus, view => child.RunFork(r => {
            IList<XPathItem> projected = r.XsltFunctions.ProjectForkBindings(prepared, view);
            var closure = Assert.IsType<CompiledFunction>(projected[0]);
            Assert.Same(closure, projected[1]);
            Assert.Same(r, closure.Owner);
            var captured = Assert.IsType<MapItem>(Assert.Single(closure.Captured[0]));
            Assert.Same(closure, Assert.Single(captured.GetString("self")!));
            var array = Assert.IsType<ArrayItem>(Assert.Single(captured.GetString("nodes")!));
            var first = Assert.IsType<StreamedNavigator>(array[0][0]);
            Assert.NotSame(first, array[0][1]);
            Assert.Same(view.Document, first.Document);
            Assert.Equal("value", first.Value);
        }, default, null));
        Assert.Same(owner, function.Owner);
        Assert.Same(map, function.Captured[0][0]);
    }

    [Fact]
    public void TunnelDeferredAndGroundedGroupBindingsProjectTheirNestedValues()
    {
        using var reader = XmlReader.Create(new StringReader("<doc>value</doc>"));
        using var document = new StreamedDocument(reader);
        var focus = (StreamedNavigator)document.CreateNavigator();
        XmlQueryRuntime owner = CreateRuntime(), child = owner.CreateForkRuntime(new XmlCachedSequenceWriter());
        var function = new CompiledFunction(0, new(null, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence), owner);
        function.Captured.Add(new XPathItem[] { focus });
        var group = new XsltLibrary.Group(new XPathItem[] { AtomicValue.OfString("key") });
        group.Members.Add(function);
        group.Members.Add(focus);
        var outer = new XsltLibrary.TunnelFrame(null, "urn:p", "outer", new XPathItem[] { group });
        var frame = new XsltLibrary.TunnelFrame(outer, "urn:p", "inner", new XPathItem[] { new XsltLibrary.DeferredItem(true, function) });
        var prepared = owner.XsltFunctions.PrepareForkBindings(new XPathItem[] { frame });
        Run(focus, view => child.RunFork(r => {
            var projected = Assert.IsType<XsltLibrary.TunnelFrame>(Assert.Single(r.XsltFunctions.ProjectForkBindings(prepared, view)));
            Assert.Equal("inner", projected.Local);
            var deferred = Assert.IsType<XsltLibrary.DeferredItem>(Assert.Single(projected.ParamValue));
            Assert.True(deferred.OnEmpty);
            Assert.NotSame(function, deferred.Body);
            var projectedGroup = Assert.IsType<XsltLibrary.Group>(Assert.Single(projected.Outer!.ParamValue));
            Assert.Same(deferred.Body, projectedGroup.Members[0]);
            Assert.Null(projectedGroup.Nodes);
            Assert.Equal("key", Assert.Single(projectedGroup.Key!).Value);
            Assert.Same(view.Document, Assert.IsType<StreamedNavigator>(projectedGroup.Members[1]).Document);
            Assert.Equal("value", projectedGroup.Members[1].Value);
        }, default, null));
        Assert.Same(focus, group.Members[1]);
    }

    [Fact]
    public void DeepContainersAndTunnelChainsDoNotUseTheNativeStack()
    {
        XmlQueryRuntime runtime = CreateRuntime();
        using var reader = XmlReader.Create(new StringReader("<doc/>"));
        using var document = new StreamedDocument(reader);
        var focus = (StreamedNavigator)document.CreateNavigator();
        XPathItem array = focus;
        XsltLibrary.TunnelFrame? tunnel = null;
        const int depth = 20_000;
        for (int i = 0; i < depth; i++)
        {
            array = ArrayItem.Of(new[] { new[] { array } });
            tunnel = new(tunnel, "", "p", new XPathItem[] { AtomicValue.OfInteger(i) });
        }
        var prepared = runtime.XsltFunctions.PrepareForkBindings(new[] { array, tunnel! });
        Run(focus, view => {
            IList<XPathItem> projected = runtime.XsltFunctions.ProjectForkBindings(prepared, view);
            XPathItem leaf = projected[0];
            for (int i = 0; i < depth; i++) leaf = Assert.IsType<ArrayItem>(leaf)[0][0];
            Assert.Same(view.Document, Assert.IsType<StreamedNavigator>(leaf).Document);
            var frame = Assert.IsType<XsltLibrary.TunnelFrame>(projected[1]);
            int count = 0;
            for (; frame != null; frame = frame.Outer) count++;
            Assert.Equal(depth, count);
        });
    }

    [Fact]
    public void ForeignFunctionsAndMutablePipelineStateRequireTheirOwnIntegration()
    {
        XmlQueryRuntime runtime = CreateRuntime(), foreign = CreateRuntime();
        var function = new CompiledFunction(0, new(null, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence), foreign);
        using var reader = XmlReader.Create(new StringReader("<doc/>"));
        using var document = new StreamedDocument(reader);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Run(focus, view => {
            Assert.Throws<InvalidOperationException>(() => runtime.XsltFunctions.ProjectForkBindings(new XPathItem[] { function }, view));
            Assert.Throws<InvalidOperationException>(() => runtime.XsltFunctions.ProjectForkBindings(new[] { runtime.XsltFunctions.X31PullNew(function) }, view));
        });
        Assert.Equal(ReadState.Initial, reader.ReadState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundedNodesAndBuiltInContextsAreIsolatedWithoutLosingIdentity(bool scratch)
    {
        XmlQueryRuntime owner = CreateRuntime(), child = owner.CreateForkRuntime(new XmlCachedSequenceWriter());
        var tree = new XPathDocument(new StringReader("<grounded/>"));
        XPathNavigator original = tree.CreateNavigator();
        var context = new FunctionContext(60, (_, _) => 0) { Runtime = owner, StaticBaseUri = "urn:base" };
        var time = context.CurrentDateTime;
        var definition = new FunctionDefinition(0, new QName("urn:test", "context"), Array.Empty<(string, SequenceTypeTest)>(),
            SequenceTypeTest.AnySequence, (c, _) => {
                Assert.Same(child, c.Runtime);
                Assert.Equal("urn:base", c.StaticBaseUri);
                Assert.Equal(60, c.ImplicitTimezoneMinutes);
                Assert.Equal(time, c.CurrentDateTime);
                return new XPathItem[] { AtomicValue.OfString("called") };
            }, false);
        var function = new LibraryFunction(definition, 0, context);
        IList<XPathItem> bindings = new XPathItem[] { original, original, function };
        if (scratch)
        {
            var store = new StreamedXdmValueStore();
            using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true);
            store.Write(writer, bindings); bytes.Position = 0;
            bindings = store.Read(new BinaryReader(bytes))!;
            Assert.NotSame(function, bindings[2]);
            Assert.Same(context, Assert.IsType<LibraryFunction>(bindings[2]).Context);
        }
        var prepared = owner.XsltFunctions.PrepareForkBindings(bindings);
        using var reader = XmlReader.Create(new StringReader("<doc/>"));
        using var document = new StreamedDocument(reader);
        Run((StreamedNavigator)document.CreateNavigator(), view => child.RunFork(r => {
            IList<XPathItem> projected = r.XsltFunctions.ProjectForkBindings(prepared, view);
            var first = Assert.IsAssignableFrom<XPathNavigator>(projected[0]);
            var second = Assert.IsAssignableFrom<XPathNavigator>(projected[1]);
            Assert.NotSame(first, second);
            Assert.True(first.IsSamePosition(original));
            Assert.True(first.MoveToFirstChild());
            Assert.Equal(XPathNodeType.Root, second.NodeType);
            Assert.Equal("called", Assert.Single(r.XsltFunctions.CallFunction((FunctionItem)projected[2])).Value);
        }, default, null));
        Assert.Same(owner, context.Runtime);
        Assert.Equal(XPathNodeType.Root, original.NodeType);
        Assert.Equal(ReadState.Initial, reader.ReadState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RandomGeneratorClosuresRemainImmutableAcrossForkWorkers(bool scratch)
    {
        var owner = CreateRuntime(); var caller = owner.XsltFunctions;
        var definition = Assert.IsType<FunctionDefinition>(FunctionLibrary.Find(QName.Ns.Fn, "random-number-generator", 1));
        var generator = Assert.IsType<MapItem>(Assert.Single(caller.CallFunction(new LibraryFunction(definition, 1, caller.X31Context),
            new XPathItem[] { AtomicValue.OfString("fork-seed") })));
        var next = Assert.IsType<NativeFunction>(generator.GetString("next")![0]);
        var permute = Assert.IsType<NativeFunction>(generator.GetString("permute")![0]);
        XPathItem[] argument = { AtomicValue.OfInteger(1), AtomicValue.OfInteger(2), AtomicValue.OfInteger(3), AtomicValue.OfInteger(4) };
        string expectedPermutation = string.Join(",", caller.CallFunction(permute, argument).Select(item => item.Value));
        string expectedNumber = Assert.IsType<MapItem>(Assert.Single(caller.CallFunction(next))).GetString("number")![0].Value;
        IList<XPathItem> bindings = new XPathItem[] { generator, next, next, permute };
        if (scratch)
        {
            var store = new StreamedXdmValueStore(); using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true);
            store.Write(writer, bindings); bytes.Position = 0; bindings = store.Read(new BinaryReader(bytes))!;
            Assert.NotSame(next, bindings[1]); Assert.NotSame(permute, bindings[3]);
        }
        var prepared = caller.PrepareForkBindings(bindings);
        using var reader = XmlReader.Create(new StringReader("<doc/>")); using var document = new StreamedDocument(reader);
        Run((StreamedNavigator)document.CreateNavigator(), Check, Check);
        Assert.Equal(ReadState.Initial, reader.ReadState);

        void Check(StreamedNavigator view)
        {
            var child = owner.CreateForkRuntime(new XmlCachedSequenceWriter());
            child.RunFork(runtime => {
                var projected = runtime.XsltFunctions.ProjectForkBindings(prepared, view);
                Assert.Same(projected[1], projected[2]);
                Assert.Same(projected[1], Assert.IsType<MapItem>(projected[0]).GetString("next")![0]);
                for (int repeat = 0; repeat < 8; repeat++)
                {
                    var nextMap = Assert.IsType<MapItem>(Assert.Single(runtime.XsltFunctions.CallFunction((FunctionItem)projected[1])));
                    Assert.Equal(expectedNumber, nextMap.GetString("number")![0].Value);
                    Assert.Equal(expectedPermutation, string.Join(",", runtime.XsltFunctions.CallFunction((FunctionItem)projected[3], argument).Select(item => item.Value)));
                }
            }, default, null);
        }
    }

    [Fact]
    public void TupleKeysStayLazyAndRetiredPositionsCannotBeRevisited()
    {
        using var reader = XmlReader.Create(new StringReader("<doc><a/><b/></doc>"));
        using var document = new StreamedDocument(reader);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild()); Assert.True(focus.MoveToFirstChild());
        XmlQueryRuntime runtime = CreateRuntime();
        var tuple = new XsltLibrary.GroupTuple(focus.Clone(), "key");
        Run(focus, view => {
            var projected = Assert.IsType<XsltLibrary.GroupTuple>(Assert.Single(runtime.XsltFunctions.ProjectForkBindings(new XPathItem[] { tuple }, view)));
            Assert.Equal("key", projected.KeyString);
            Assert.Same(view.Document, Assert.IsType<StreamedNavigator>(projected.Item).Document);
            Assert.Equal("key", Assert.Single(projected.Key).Value);
        });
        Assert.True(focus.MoveToNext());
        Run(focus, view => Assert.Equal("stream-position", Assert.Throws<XdmException>(() =>
            runtime.XsltFunctions.ProjectForkBindings(new XPathItem[] { tuple }, view)).Code));
    }

    [Fact]
    public void ProjectionHonorsCancellationEvenForEmptyBindings()
    {
        using var reader = XmlReader.Create(new StringReader("<doc/>"));
        using var document = new StreamedDocument(reader);
        XmlQueryRuntime runtime = CreateRuntime();
        Run((StreamedNavigator)document.CreateNavigator(), view => {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            CancellationToken saved = XmlQueryRuntime.Cancellation;
            try
            {
                XmlQueryRuntime.Cancellation = cancellation.Token;
                Assert.Throws<OperationCanceledException>(() => runtime.XsltFunctions.ProjectForkBindings(Array.Empty<XPathItem>(), view));
            }
            finally { XmlQueryRuntime.Cancellation = saved; }
        });
        Assert.Equal(ReadState.Initial, reader.ReadState);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_command")]
    private static extern ref XmlILCommand? Command(XslCompiledTransform transform);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_delExec")]
    private static extern ref ExecuteDelegate Execute(XmlILCommand command);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
#if NET9_0_OR_GREATER
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
#endif
    public void CompiledAndCoercedSourceCapturesRunAgainstEachProngsReader(bool saved, bool scratch, bool debug)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:sequence select="function($bound) { let $map := map{'node': [$bound]} return function() { string($map?node?1) } }"/>
              </xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XslCompiledTransform(debug) { RawResult = true };
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(XmlReader.Create(new StringReader(sheet)), new(), null,
                "ForkBindings", "Saved.ForkBindings", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.ForkBindings", true)!);
#endif
        }
        else transform.Load(XmlReader.Create(new StringReader(sheet)));
        XmlILCommand command = Command(transform)!;
        using var global = XmlReader.Create(new StringReader("<global/>"));
        var sink = new XmlCachedSequenceWriter();
        var owner = new XmlQueryRuntime(command.StaticData, global, XmlResolver.ThrowingResolver, null, sink);
        Execute(command)(owner);
        var binder = Assert.IsAssignableFrom<FunctionItem>(Assert.Single(sink.ResultSequence));
        using var reader = XmlReader.Create(new StringReader("<doc><n>one</n><n>two</n></doc>"));
        using var document = new StreamedDocument(reader);
        var focus = (StreamedNavigator)document.CreateNavigator();
        var closure = Assert.IsType<CompiledFunction>(Assert.Single(owner.XsltFunctions.CallFunction(binder, new XPathItem[] { focus })));
        IList<XPathItem> functions = new XPathItem[] { closure, new CoercedFunction(closure, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence) };
        if (scratch)
        {
            var store = new StreamedXdmValueStore();
            using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true);
            store.Write(writer, functions); bytes.Position = 0;
            var restored = store.Read(new BinaryReader(bytes))!;
            Assert.NotSame(functions[1], restored[1]);
            Assert.Same(restored[0], Assert.IsType<CoercedFunction>(restored[1]).Target);
            functions = restored;
        }
        XsltLibrary.PreparedForkBindings prepared = owner.XsltFunctions.PrepareForkBindings(functions);
        XmlQueryRuntime[] children = Enumerable.Range(0, 2).Select(_ => owner.CreateForkRuntime(new XmlCachedSequenceWriter())).ToArray();
        using var cancellation = new CancellationTokenSource(Deadline);
        using var fork = document.Fork(focus);
        fork.Run(children.Select<XmlQueryRuntime, Action<StreamedNavigator, CancellationToken>>((child, index) =>
            (view, token) => child.RunFork(r => {
                var projected = Assert.IsAssignableFrom<FunctionItem>(r.XsltFunctions.ProjectForkBindings(prepared, view)[index]);
                Assert.Equal("onetwo", Assert.Single(r.XsltFunctions.CallFunction(projected)).Value);
            }, token, null)).ToArray(), 1, cancellation.Token);
    }
}
