// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;
using Xunit;

namespace Xsl3.Tests;

public class StreamedXdmValueStoreTests
{
    private static IList<XPathItem>? Roundtrip(StreamedXdmValueStore store, IList<XPathItem>? items)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true);
        store.Write(writer, items); bytes.Position = 0;
        return store.Read(new BinaryReader(bytes));
    }

    [Fact]
    public void NullEmptyDerivedAndNonLexicalAtomicValuesRoundtripExactly()
    {
        var store = new StreamedXdmValueStore();
        Assert.Null(Roundtrip(store, null)); Assert.Empty(Roundtrip(store, Array.Empty<XPathItem>())!);
        var derived = new AtomicType(new QName("urn:types", "integer"), AtomicTypes.Integer, new XmlSchemaSimpleType(), null);
        XPathItem[] input = {
            new IntegerValue(derived, BigInteger.Parse("123456789012345678901234567890")),
            AtomicValue.OfDouble(-0.0), AtomicValue.OfFloat(float.NaN), AtomicValue.OfDouble(double.NegativeInfinity),
            AtomicValue.OfDecimal(12.50m), AtomicValue.OfUntyped(" a  b "),
            new QNameValue(AtomicTypes.QName, new QName("urn:q", "value", "p")),
            Casting.Cast(AtomicValue.OfString("2001-02-03T04:05:06.123+05:30"), AtomicTypes.DateTime),
            Casting.Cast(AtomicValue.OfString("P2Y3M4DT5H6M7.25S"), AtomicTypes.Duration),
            Casting.Cast(AtomicValue.OfString("00FF"), AtomicTypes.HexBinary),
        };
        var output = Roundtrip(store, input)!;
        for (int i = 0; i < input.Length; i++)
        {
            Assert.Same(((AtomicValue)input[i]).Type, ((AtomicValue)output[i]).Type);
            Assert.Equal(input[i].Value, output[i].Value);
        }
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(((DoubleValue)output[1]).Double));
        Assert.Equal("p", ((QNameValue)output[6]).QName.Prefix);
    }

    [Fact]
    public void ContainerAliasesCyclesAndEmptyMembersSurviveWithoutRecursiveCalls()
    {
        var store = new StreamedXdmValueStore();
        var self = new XPathItem[1];
        var array = ArrayItem.Of(new IList<XPathItem>[] { self, Array.Empty<XPathItem>() }); self[0] = array;
        var result = Roundtrip(store, new XPathItem[] { array, array })!;
        var copy = Assert.IsType<ArrayItem>(result[0]);
        Assert.Same(copy, result[1]); Assert.Same(copy, copy[0][0]); Assert.Empty(copy[1]);
        XPathItem nested = AtomicValue.OfInteger(7);
        for (int i = 0; i < 20000; i++) nested = ArrayItem.Of(new[] { new[] { nested } });
        nested = Assert.Single(Roundtrip(store, new[] { nested })!);
        for (int i = 0; i < 20000; i++) nested = Assert.Single(Assert.IsType<ArrayItem>(nested)[0]);
        Assert.Equal("7", nested.Value);
    }

    [Fact]
    public void MapsKeepOrderedTypedKeysAndSequenceValuedEntries()
    {
        var key = new QNameValue(AtomicTypes.QName, new QName("urn:k", "key", "q"));
        var map = MapItem.Empty.Put(key, new XPathItem[] { AtomicValue.OfInteger(1), AtomicValue.OfString("two") })
            .Put(AtomicValue.OfString("empty"), Array.Empty<XPathItem>());
        var result = Assert.IsType<MapItem>(Assert.Single(Roundtrip(new(), new[] { map })!));
        Assert.Equal("q", Assert.IsType<QNameValue>(new List<AtomicValue>(result.Keys)[0]).QName.Prefix);
        Assert.Equal(2, result.Get(key)!.Count); Assert.Empty(result.GetString("empty")!);
    }

    [Fact]
    public void IdentityValuesKeepTheirNodePositionsAndOpaqueCallableObjects()
    {
        var node = XdmNode.Load(new StringReader("<doc><a/><b/></doc>")).Navigator;
        Assert.True(node.MoveToFirstChild()); Assert.True(node.MoveToFirstChild());
        var first = node.Clone();
        var store = new StreamedXdmValueStore();
        var before = Roundtrip(store, new[] { node })!;
        Assert.True(node.MoveToNext()); var after = Roundtrip(store, new[] { node })!;
        Assert.True(first.IsSamePosition((XPathNavigator)before[0])); Assert.True(node.IsSamePosition((XPathNavigator)after[0]));
        Assert.False(((XPathNavigator)before[0]).IsSamePosition((XPathNavigator)after[0]));
        var function = new TestFunction();
        Assert.Same(function, Assert.Single(Roundtrip(store, new[] { function })!));
    }

    [Fact]
    public void ScalarAndContainerWritesDoNotRetainTheRecordedValueHistory()
    {
        var store = new StreamedXdmValueStore();
        WeakReference recorded = WriteValue(store);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(recorded.IsAlive); GC.KeepAlive(store);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WriteValue(StreamedXdmValueStore store)
    {
        var map = MapItem.Empty.Put(AtomicValue.OfString("value"), new XPathItem[] { AtomicValue.OfInteger(123) });
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        store.Write(writer, new[] { map }); return new WeakReference(map);
    }

    [Fact]
    public void CompiledCapturesKeepAliasesCyclesSignaturesAndDistinctCodeOwners()
    {
        var owner = ForkBindingTests.CreateRuntime();
        var other = ForkBindingTests.CreateRuntime();
        var signature = new FunctionSignature(new QName("urn:functions", "saved"),
            new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence);
        var first = new CompiledFunction(3, signature, owner);
        var second = new CompiledFunction(3, signature, other);
        var map = MapItem.Empty.Put(AtomicValue.OfString("self"), new XPathItem[] { first })
            .Put(AtomicValue.OfString("other"), new XPathItem[] { second });
        var array = ArrayItem.Of(new[] { new XPathItem[] { map, first } });
        first.Captured.Add(new XPathItem[] { array });
        first.Captured.Add(Array.Empty<XPathItem>());
        second.Captured.Add(new XPathItem[] { first });
        var output = Roundtrip(new(), new XPathItem[] { first, first, second, array })!;
        var copy = Assert.IsType<CompiledFunction>(output[0]);
        var peer = Assert.IsType<CompiledFunction>(output[2]);
        Assert.NotSame(first, copy); Assert.Same(copy, output[1]);
        Assert.Same(owner, copy.Owner); Assert.Same(other, peer.Owner);
        Assert.Same(signature, copy.Signature); Assert.Equal(3, copy.Id);
        Assert.Equal(signature.Name, copy.Name); Assert.Equal(1, copy.Arity);
        Assert.Same(signature.Parameters[0], copy.ParameterType(0)); Assert.Same(signature.Return, copy.ReturnType);
        Assert.Empty(copy.Captured[1]); Assert.Same(copy, peer.Captured[0][0]);
        var captured = Assert.IsType<ArrayItem>(copy.Captured[0][0]);
        Assert.Same(captured, output[3]); Assert.Same(copy, captured[0][1]);
        var capturedMap = Assert.IsType<MapItem>(captured[0][0]);
        Assert.Same(copy, Assert.Single(capturedMap.GetString("self")!));
        Assert.Same(peer, Assert.Single(capturedMap.GetString("other")!));
    }

    [Fact]
    public void DeepCompiledCaptureGraphsUseAnIterativeTraversal()
    {
        var owner = ForkBindingTests.CreateRuntime();
        var signature = new FunctionSignature(null, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence);
        XPathItem value = AtomicValue.OfString("leaf");
        for (int i = 0; i < 20000; i++)
        {
            var closure = new CompiledFunction(1, signature, owner);
            closure.Captured.Add(new[] { value }); value = closure;
        }
        value = Assert.Single(Roundtrip(new(), new[] { value })!);
        for (int i = 0; i < 20000; i++) value = Assert.Single(Assert.IsType<CompiledFunction>(value).Captured[0]);
        Assert.Equal("leaf", value.Value);
    }

    [Fact]
    public void CompiledWritesReleaseClosuresAndCapturesWhileKeepingTheirCodeUntilClear()
    {
        var store = new StreamedXdmValueStore();
        var recorded = WriteCompiledHistory(store);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(recorded.Values, value => Assert.False(value.IsAlive));
        Assert.True(recorded.Owner.IsAlive);
        store.Clear();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(recorded.Owner.IsAlive); GC.KeepAlive(store);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Owner, List<WeakReference> Values) WriteCompiledHistory(StreamedXdmValueStore store)
    {
        var owner = ForkBindingTests.CreateRuntime();
        var signature = new FunctionSignature(null, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence);
        var references = new List<WeakReference>();
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        var offsets = new List<long>();
        for (int i = 0; i < 1000; i++)
        {
            var closure = new CompiledFunction(1, signature, owner);
            var capture = MapItem.Empty.Put(AtomicValue.OfString("value"), new XPathItem[] { AtomicValue.OfInteger(i) });
            closure.Captured.Add(new XPathItem[] { capture });
            references.Add(new(closure)); references.Add(new(capture));
            offsets.Add(bytes.Position); store.Write(writer, new[] { closure });
        }
        using var reader = new BinaryReader(bytes);
        for (int i = offsets.Count - 1; i >= 0; i--)
        {
            bytes.Position = offsets[i];
            var closure = Assert.IsType<CompiledFunction>(Assert.Single(store.Read(reader)!));
            Assert.Equal(i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Assert.Single(Assert.IsType<MapItem>(closure.Captured[0][0]).GetString("value")!).Value);
        }
        return (new(owner), references);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
#if NET10_0_OR_GREATER
    [InlineData(true, false)]
    [InlineData(true, true)]
#endif
    public void ReplayedCodeUsesEachOwnersGlobalsAndCapturedArguments(bool saved, bool debug)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xsl:param name="owner"/>
              <xsl:template match="/">
                <xsl:sequence select="function($value) {function($n as xs:integer) as xs:string {concat($owner, ':', $value, ':', $n)}}"/>
              </xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XslCompiledTransform(debug) { RawResult = true };
        using var stylesheet = XmlReader.Create(new StringReader(sheet));
        if (saved)
        {
#if NET10_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(stylesheet, new(), null, "ScratchClosure", "Saved.ScratchClosure", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.ScratchClosure", true)!);
#endif
        }
        else transform.Load(stylesheet);
        XmlILCommand command = Command(transform)!;
        CompiledFunction CreateClosure(string ownerName, string captured)
        {
            using var input = XmlReader.Create(new StringReader("<input/>"));
            var sink = new XmlCachedSequenceWriter();
            var arguments = new XsltArgumentList(); arguments.AddParam("owner", "", ownerName);
            var runtime = new XmlQueryRuntime(command.StaticData, input, XmlResolver.ThrowingResolver, arguments, sink);
            Execute(command)(runtime);
            var binder = Assert.IsAssignableFrom<FunctionItem>(Assert.Single(sink.ResultSequence));
            return Assert.IsType<CompiledFunction>(Assert.Single(runtime.XsltFunctions.CallFunction(binder,
                new XPathItem[] { AtomicValue.OfString(captured) })));
        }
        var first = CreateClosure("one", "capture1"); var second = CreateClosure("two", "capture2");
        Assert.Equal(first.Id, second.Id); Assert.Same(first.Signature, second.Signature);
        var restored = Roundtrip(new(), new XPathItem[] { first, second })!;
        var caller = ForkBindingTests.CreateRuntime().XsltFunctions;
        Assert.Equal("one:capture1:7", Assert.Single(caller.CallFunction((FunctionItem)restored[0], new XPathItem[] { AtomicValue.OfUntyped("7") })).Value);
        Assert.Equal("two:capture2:9", Assert.Single(caller.CallFunction((FunctionItem)restored[1], new XPathItem[] { AtomicValue.OfInteger(9) })).Value);
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => caller.CallFunction((FunctionItem)restored[0],
            new XPathItem[] { AtomicValue.OfString("wrong") })).Code);
        var wrapper = new CoercedFunction(first, new[] { first.ParameterType(0) }, first.ReturnType);
        var badArgument = new CoercedFunction(first, new[] { SequenceTypeTest.EmptySequence }, first.ReturnType);
        var badResult = new CoercedFunction(first, new[] { first.ParameterType(0) }, SequenceTypeTest.EmptySequence);
        var wrappers = Roundtrip(new(), new XPathItem[] { wrapper, badArgument, badResult })!;
        Assert.Equal("one:capture1:5", Assert.Single(caller.CallFunction((FunctionItem)wrappers[0],
            new XPathItem[] { AtomicValue.OfUntyped("5") })).Value);
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => caller.CallFunction((FunctionItem)wrappers[1],
            new XPathItem[] { AtomicValue.OfInteger(5) })).Code);
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => caller.CallFunction((FunctionItem)wrappers[2],
            new XPathItem[] { AtomicValue.OfInteger(5) })).Code);
        var callbackType = new SequenceTypeTest(new FunctionItemTest(new[] { first.ParameterType(0) }, SequenceTypeTest.EmptySequence), Cardinality.ExactlyOne);
        var receiver = new Xsl3.XPath31.Functions.NativeFunction(new[] { callbackType }, SequenceTypeTest.AnySequence,
            arguments => caller.CallFunction((FunctionItem)arguments[0][0], new XPathItem[] { AtomicValue.OfInteger(5) }), "typed callback receiver");
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => caller.CallFunction(receiver, new XPathItem[] { first })).Code);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_command")]
    private static extern ref XmlILCommand? Command(XslCompiledTransform transform);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_delExec")]
    private static extern ref ExecuteDelegate Execute(XmlILCommand command);

    [Fact]
    public void RandomGeneratorFunctionsKeepTheirActualStateAliasesAndArgumentNodes()
    {
        var store = new StreamedXdmValueStore();
        var caller = ForkBindingTests.CreateRuntime().XsltFunctions;
        var node = XdmNode.Load(new StringReader("<doc><a/><b/></doc>")).Navigator;
        var marker = MapItem.Empty.Put(AtomicValue.OfString("node"), new XPathItem[] { node });
        XPathItem[] input = { AtomicValue.OfString("one"), node, marker, AtomicValue.OfInteger(4) };
        IList<XPathItem>[] arguments = { input };
        foreach (long seed in new[] { 0L, 1L, -1L, long.MinValue, long.MaxValue })
        {
            var generator = RandomGenerator(caller, seed);
            var next = Assert.IsType<NativeFunction>(Assert.Single(generator.GetString("next")!));
            var permute = Assert.IsType<NativeFunction>(Assert.Single(generator.GetString("permute")!));
            var wrapper = new CoercedFunction(permute, new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence);
            var graph = Roundtrip(store, new XPathItem[] { generator, next, next, permute, wrapper })!;
            var copy = Assert.IsType<MapItem>(graph[0]);
            Assert.NotSame(generator, copy); Assert.NotSame(next, graph[1]); Assert.NotSame(permute, graph[3]);
            Assert.Same(graph[1], graph[2]); Assert.Same(graph[1], Assert.Single(copy.GetString("next")!));
            Assert.Same(graph[3], Assert.Single(copy.GetString("permute")!));
            Assert.Same(graph[3], Assert.IsType<CoercedFunction>(graph[4]).Target);
            foreach (int index in new[] { 1, 3 })
            {
                var original = index == 1 ? next : permute;
                var restored = Assert.IsType<NativeFunction>(graph[index]);
                Assert.Equal(original.Arity, restored.Arity); Assert.Null(restored.Name);
                Assert.Same(original.ReturnType, restored.ReturnType); Assert.Equal(original.Describe(), restored.Describe());
                for (int i = 0; i < original.Arity; i++) Assert.Same(original.ParameterType(i), restored.ParameterType(i));
            }
            var expected = caller.CallFunction(permute, arguments);
            for (int repeat = 0; repeat < 2; repeat++)
            {
                var actual = caller.CallFunction((FunctionItem)graph[4], arguments);
                Assert.Equal(expected.Count, actual.Count);
                for (int i = 0; i < actual.Count; i++)
                    if (expected[i] is XPathNavigator position) Assert.True(position.IsSamePosition(Assert.IsAssignableFrom<XPathNavigator>(actual[i])));
                    else Assert.Same(expected[i], actual[i]);
            }
            Assert.Same(node, input[1]); Assert.Same(marker, input[2]);
            for (int generation = 0; generation < 32; generation++)
            {
                Assert.Equal(generator.GetString("number")![0].Value, copy.GetString("number")![0].Value);
                generator = Assert.IsType<MapItem>(Assert.Single(caller.CallFunction((FunctionItem)generator.GetString("next")![0], Array.Empty<IList<XPathItem>>())));
                copy = Assert.IsType<MapItem>(Assert.Single(caller.CallFunction((FunctionItem)copy.GetString("next")![0], Array.Empty<IList<XPathItem>>())));
            }
        }
        var opaque = new NativeFunction(Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence,
            _ => new XPathItem[] { marker }, "a provider callback");
        Assert.Same(opaque, Assert.Single(Roundtrip(store, new XPathItem[] { opaque })!));
    }

    [Fact]
    public void RandomGeneratorHistoryDoesNotRetainMapsOrTheirCallableClosures()
    {
        var store = new StreamedXdmValueStore();
        var references = WriteRandomHistory(store);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(references, value => Assert.False(value.IsAlive)); GC.KeepAlive(store);
    }

    private static MapItem RandomGenerator(XsltLibrary caller, long seed)
    {
        var definition = Assert.IsType<FunctionDefinition>(FunctionLibrary.Find(QName.Ns.Fn, "random-number-generator", 1));
        return Assert.IsType<MapItem>(Assert.Single(caller.CallFunction(new LibraryFunction(definition, 1, caller.X31Context),
            new IList<XPathItem>[] { new XPathItem[] { AtomicValue.OfInteger(seed) } })));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> WriteRandomHistory(StreamedXdmValueStore store)
    {
        var caller = ForkBindingTests.CreateRuntime().XsltFunctions;
        var references = new List<WeakReference>(); var offsets = new List<long>();
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        for (int i = 0; i < 1000; i++)
        {
            var generator = RandomGenerator(caller, i);
            references.Add(new(generator)); references.Add(new(generator.GetString("next")![0])); references.Add(new(generator.GetString("permute")![0]));
            offsets.Add(bytes.Position); store.Write(writer, new XPathItem[] { generator });
        }
        using var reader = new BinaryReader(bytes);
        for (int i = offsets.Count - 1; i >= 0; i--)
        {
            bytes.Position = offsets[i]; var generator = Assert.IsType<MapItem>(Assert.Single(store.Read(reader)!));
            var expected = RandomGenerator(caller, i);
            for (int generation = 0; generation < 3; generation++)
            {
                Assert.Equal(expected.GetString("number")![0].Value, generator.GetString("number")![0].Value);
                expected = Assert.IsType<MapItem>(Assert.Single(caller.CallFunction((FunctionItem)expected.GetString("next")![0], Array.Empty<IList<XPathItem>>())));
                generator = Assert.IsType<MapItem>(Assert.Single(caller.CallFunction((FunctionItem)generator.GetString("next")![0], Array.Empty<IList<XPathItem>>())));
            }
        }
        return references;
    }

    [Fact]
    public void LibraryFunctionsKeepAliasedContextsAndEachRecordedVariadicArityWithoutHistory()
    {
        var store = new StreamedXdmValueStore();
        var recorded = WriteLibraryHistory(store);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(recorded.Functions, value => Assert.False(value.IsAlive));
        Assert.True(recorded.Context.IsAlive); Assert.True(recorded.Owner.IsAlive);
        store.Clear();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(recorded.Context.IsAlive); Assert.False(recorded.Owner.IsAlive); GC.KeepAlive(store);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (List<WeakReference> Functions, WeakReference Context, WeakReference Owner) WriteLibraryHistory(StreamedXdmValueStore store)
    {
        var owner = ForkBindingTests.CreateRuntime();
        var context = owner.XsltFunctions.X31Context;
        var definition = Assert.IsType<FunctionDefinition>(FunctionLibrary.Find(QName.Ns.Fn, "concat", 2));
        var references = new List<WeakReference>(); var offsets = new List<long>();
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        for (int i = 0; i < 1000; i++)
        {
            var function = new LibraryFunction(definition, i + 2, context);
            references.Add(new(function)); offsets.Add(bytes.Position);
            store.Write(writer, new XPathItem[] { function, function });
        }
        using var reader = new BinaryReader(bytes);
        for (int i = offsets.Count - 1; i >= 0; i--)
        {
            bytes.Position = offsets[i]; var items = store.Read(reader)!;
            var function = Assert.IsType<LibraryFunction>(items[0]);
            Assert.Same(function, items[1]); Assert.Same(context, function.Context); Assert.Same(definition, function.Definition);
            Assert.Equal(i + 2, function.Arity); Assert.Equal(definition.Name, function.Name);
            Assert.Same(definition.ParameterType(1), function.ParameterType(i + 1)); Assert.Same(definition.ReturnType, function.ReturnType);
        }
        return (references, new(context), new(owner));
    }

    [Fact]
    public void LibraryFunctionsKeepTheirActualStaticAndDynamicContextsWhenCalledElsewhere()
    {
        var firstOwner = ForkBindingTests.CreateRuntime(); var secondOwner = ForkBindingTests.CreateRuntime();
        var contexts = new[] {
            new FunctionContext(330, firstOwner.ComparePosition) {
                Runtime = firstOwner, StaticBaseUri = "https://first.example/base/",
                DefaultCollation = Collation.Resolve(Collation.HtmlAsciiCaseInsensitiveUri),
                FixedCurrentDateTime = new XsDateTime(2001, 2, 3, 4, 5, 6.5m, 330),
            },
            new FunctionContext(-60, secondOwner.ComparePosition) {
                Runtime = secondOwner, StaticBaseUri = "https://second.example/base/",
                FixedCurrentDateTime = new XsDateTime(2002, 3, 4, 5, 6, 7.5m, -60),
            },
        };
        var caller = ForkBindingTests.CreateRuntime().XsltFunctions;
        foreach (string name in new[] { "static-base-uri", "current-dateTime", "implicit-timezone", "compare" })
        {
            bool compare = name == "compare";
            var definition = Assert.IsType<FunctionDefinition>(FunctionLibrary.Find(QName.Ns.Fn, name, compare ? 2 : 0));
            var first = new LibraryFunction(definition, definition.Arity, contexts[0]);
            var second = new LibraryFunction(definition, definition.Arity, contexts[1]);
            IList<XPathItem>[] arguments = compare ? new IList<XPathItem>[] { new XPathItem[] { AtomicValue.OfString("a") }, new XPathItem[] { AtomicValue.OfString("A") } } : Array.Empty<IList<XPathItem>>();
            string expectedFirst = Assert.Single(caller.CallFunction(first, arguments)).Value;
            string expectedSecond = Assert.Single(caller.CallFunction(second, arguments)).Value;
            Assert.NotEqual(expectedFirst, expectedSecond);
            var restored = Roundtrip(new(), new XPathItem[] { first, second })!;
            var copy = Assert.IsType<LibraryFunction>(restored[0]);
            Assert.NotSame(first, copy); Assert.Same(contexts[0], copy.Context);
            Assert.Equal(expectedFirst, Assert.Single(caller.CallFunction(copy, arguments)).Value);
            Assert.Equal(expectedSecond, Assert.Single(caller.CallFunction((LibraryFunction)restored[1], arguments)).Value);
        }
    }

    [Fact]
    public void LibraryDefinitionIdentityIsNotReplacedByItsNameOrNumericRegistryId()
    {
        var name = new QName("urn:providers", "same");
        var parameters = new[] { ("items", SequenceTypeTest.AnySequence) };
        var first = new FunctionDefinition(0, name, parameters, SequenceTypeTest.AnySequence, static (c, a) => a[0], false);
        var second = new FunctionDefinition(0, name, parameters, SequenceTypeTest.AnySequence,
            static (c, a) => new XPathItem[] { a[0][1], a[0][0] }, false);
        var context = ForkBindingTests.CreateRuntime().XsltFunctions.X31Context;
        var output = Roundtrip(new(), new XPathItem[] { new LibraryFunction(first, 1, context), new LibraryFunction(second, 1, context) })!;
        var caller = ForkBindingTests.CreateRuntime().XsltFunctions;
        XPathItem[] input = { AtomicValue.OfInteger(1), AtomicValue.OfInteger(2) };
        Assert.Same(first, Assert.IsType<LibraryFunction>(output[0]).Definition);
        Assert.Same(second, Assert.IsType<LibraryFunction>(output[1]).Definition);
        Assert.Equal("1", caller.CallFunction((LibraryFunction)output[0], input)[0].Value);
        Assert.Equal("2", caller.CallFunction((LibraryFunction)output[1], input)[0].Value);
    }

    [Fact]
    public void CoercedCapturesKeepAliasesAndRecursiveReferencesThroughTheirTargets()
    {
        var owner = ForkBindingTests.CreateRuntime();
        var target = new CompiledFunction(1, new(null, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence), owner);
        var wrapper = new CoercedFunction(target, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence);
        var map = MapItem.Empty.Put(AtomicValue.OfString("wrapper"), new XPathItem[] { wrapper });
        target.Captured.Add(new XPathItem[] { map, wrapper, target });
        var output = Roundtrip(new(), new XPathItem[] { wrapper, wrapper, target, map })!;
        var copy = Assert.IsType<CoercedFunction>(output[0]);
        Assert.NotSame(wrapper, copy); Assert.Same(copy, output[1]); Assert.Same(output[2], copy.Target);
        var closure = Assert.IsType<CompiledFunction>(copy.Target);
        Assert.Same(owner, closure.Owner); Assert.Same(output[3], closure.Captured[0][0]);
        Assert.Same(copy, closure.Captured[0][1]); Assert.Same(closure, closure.Captured[0][2]);
        Assert.Same(copy, Assert.Single(Assert.IsType<MapItem>(output[3]).GetString("wrapper")!));
    }

    [Fact]
    public void DeepCoercionChainsUseIterativeScratchReconstruction()
    {
        FunctionItem value = MapItem.Empty.Put(AtomicValue.OfString("key"), new XPathItem[] { AtomicValue.OfString("leaf") });
        for (int i = 0; i < 20000; i++) value = new CoercedFunction(value, new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence);
        value = Assert.IsAssignableFrom<FunctionItem>(Assert.Single(Roundtrip(new(), new[] { value })!));
        for (int i = 0; i < 20000; i++) value = Assert.IsType<CoercedFunction>(value).Target;
        Assert.Equal("leaf", Assert.Single(Assert.IsType<MapItem>(value).GetString("key")!).Value);
    }

    [Fact]
    public void DeepCoercionNamesComeFromTheOriginalFunction()
    {
        var name = new QName("urn:functions", "named", "f");
        FunctionItem function = new CompiledFunction(1, new(name, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence), ForkBindingTests.CreateRuntime());
        string description = function.Describe();
        for (int i = 0; i < 20000; i++) function = new CoercedFunction(function, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence);
        Assert.Equal(name, function.Name);
        Assert.Equal(description, function.Describe());
        Assert.Equal(name, Assert.IsAssignableFrom<FunctionItem>(Assert.Single(Roundtrip(new(), new[] { function })!)).Name);
    }

    [Fact]
    public void CoercionTargetsCanWaitForAnOuterWrapperInACyclicGraph()
    {
        var member = new XPathItem[1];
        var map = MapItem.Of(new[] { new KeyValuePair<AtomicValue, IList<XPathItem>>(AtomicValue.OfString("inner"), member) });
        var outer = new CoercedFunction(map, new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence);
        var inner = new CoercedFunction(outer, new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence);
        member[0] = inner;
        var output = Roundtrip(new(), new XPathItem[] { outer, inner, map })!;
        var first = Assert.IsType<CoercedFunction>(output[0]); var second = Assert.IsType<CoercedFunction>(output[1]);
        Assert.NotSame(outer, first); Assert.Same(first, second.Target); Assert.Same(output[2], first.Target);
        Assert.Same(second, Assert.Single(Assert.IsType<MapItem>(output[2]).GetString("inner")!));
    }

    [Fact]
    public void CoercionWritesDoNotRetainWrappersTargetsCapturesOrFreshParameterArrays()
    {
        var store = new StreamedXdmValueStore();
        var references = WriteCoercedHistory(store);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(references, item => Assert.False(item.IsAlive)); GC.KeepAlive(store);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> WriteCoercedHistory(StreamedXdmValueStore store)
    {
        var owner = ForkBindingTests.CreateRuntime();
        var signature = new FunctionSignature(null, new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence);
        var references = new List<WeakReference>();
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        for (int i = 0; i < 1000; i++)
        {
            var target = new CompiledFunction(1, signature, owner);
            var capture = MapItem.Empty.Put(AtomicValue.OfString("value"), new XPathItem[] { AtomicValue.OfInteger(i) });
            target.Captured.Add(new XPathItem[] { capture });
            var parameters = new[] { SequenceTypeTest.AnySequence };
            var wrapper = new CoercedFunction(target, parameters, SequenceTypeTest.AnySequence);
            references.Add(new(wrapper)); references.Add(new(target)); references.Add(new(capture)); references.Add(new(parameters));
            store.Write(writer, new XPathItem[] { wrapper });
        }
        return references;
    }

    [Fact]
    public void ClearingTheSourceStoreReleasesRetainedIdentityValues()
    {
        var store = new StreamedXdmValueStore();
        WeakReference recorded = WriteIdentity(store);
        store.Clear();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(recorded.IsAlive); GC.KeepAlive(store);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WriteIdentity(StreamedXdmValueStore store)
    {
        var function = new TestFunction();
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        store.Write(writer, new[] { function }); return new WeakReference(function);
    }

    [Fact]
    public void CancellationIsObservedWhileWalkingValueGraphs()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var previous = XmlQueryRuntime.Cancellation;
        try
        {
            XmlQueryRuntime.Cancellation = cancellation.Token;
            Assert.Throws<OperationCanceledException>(() => Roundtrip(new(), new XPathItem[] { AtomicValue.OfInteger(1) }));
        }
        finally { XmlQueryRuntime.Cancellation = previous; }
    }

    private sealed class TestFunction : FunctionItem
    {
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "test callable";
    }
}
