// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.Tree;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class ForkRuntimeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private sealed class Sources(string xml = "<doc/>") : XmlResolver
    {
        internal int Calls, Closed;
        public override System.Net.ICredentials? Credentials { set { } }
        public override object GetEntity(Uri uri, string? role, Type? type)
        {
            Interlocked.Increment(ref Calls);
            return new TrackedStream(xml, () => Interlocked.Increment(ref Closed));
        }
    }
    private sealed class TrackedStream(string xml, Action closed) : MemoryStream(Encoding.UTF8.GetBytes(xml))
    {
        protected override void Dispose(bool disposing) { if (CanRead) closed(); base.Dispose(disposing); }
    }
    private static XmlQueryStaticData Data(bool forward = false, StaticDataManager? manager = null)
    {
        manager ??= new StaticDataManager();
        manager.DeclareGlobalValue("global");
        return new(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), manager) { Xslt2 = true, StreamedInitialMode = forward };
    }
    private static XmlQueryRuntime Runtime(XmlQueryStaticData? data = null, object? input = null, XmlResolver? resolver = null, XsltArgumentList? arguments = null) =>
        new(data ?? Data(), input ?? XslCompiledTransform.AbsentInput, resolver ?? XmlResolver.ThrowingResolver, arguments, new XmlCachedSequenceWriter());

    // Thread-scoped policy tests use fresh threads so that even an assertion failure cannot
    // leave a test-runner thread with a different transformation policy.
    private static void OnThread(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() => { try { body(); } catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); } }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(Deadline), "The fork-runtime probe did not finish");
        failure?.Throw();
    }

    [Fact]
    public void FactoryBorrowsTheGlobalItemWithoutReopeningItAndIsolatesNamesAndOutputs()
    {
        var sources = new Sources();
        XmlQueryRuntime owner = Runtime(input: "urn:input", resolver: sources);
        var sink = new XmlCachedSequenceWriter();
        XmlQueryRuntime child = owner.CreateForkRuntime(sink);
        XmlQueryRuntime nested = child.CreateForkRuntime(new XmlCachedSequenceWriter());
        Assert.Equal(1, sources.Calls);
        Assert.Same(owner.Documents, child.Documents);
        Assert.Same(owner.Documents, nested.Documents);
        Assert.NotSame(owner.Output, child.Output);
        Assert.NotSame(owner.ExternalContext.QueryNameTable, child.ExternalContext.QueryNameTable);
        Assert.NotSame(child.ExternalContext.QueryNameTable, nested.ExternalContext.QueryNameTable);
        XPathNavigator original = owner.ExternalContext.DefaultDataSource, borrowed = child.ExternalContext.DefaultDataSource;
        Assert.NotSame(original, borrowed);
        Assert.True(original.IsSamePosition(borrowed));
        Assert.True(borrowed.MoveToFirstChild());
        Assert.Equal(XPathNodeType.Root, original.NodeType);
        child.RunFork(r => r.Output.WriteItem(AtomicValue.OfString("child")), default, null);
        Assert.Equal("child", Assert.Single(sink.ResultSequence).Value);
        Assert.True(nested.ExternalContext.DefaultDataSource.IsSamePosition(original));
        Assert.True(nested.ExternalContext.InitialSelection(false).IsSamePosition(original));
    }

    [Fact]
    public void FactoryKeepsTheQueryReadersConfiguredEntityResolver()
    {
        var documents = new Sources("<!DOCTYPE doc SYSTEM 'urn:dtd'><doc>&value;</doc>");
        var entities = new Sources("<!ENTITY value 'from-policy'>");
        using var input = XmlReader.Create(new StringReader("<input/>"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = entities });
        XmlQueryRuntime owner = Runtime(input: input, resolver: documents);
        XmlQueryRuntime child = owner.CreateForkRuntime(new XmlCachedSequenceWriter());
        OnThread(() => child.RunFork(r => Assert.Equal("from-policy", r.ExternalContext.GetDataSource("urn:doc", null)!.Value), default, null));
        Assert.Equal(1, documents.Calls);
        Assert.Equal(1, entities.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyDtdIgnoreRemainsIgnoredWhenTheReaderPolicyIsCopied(bool fork)
    {
        var entities = new Sources("<!ENTITY unused 'ignored'>");
        using var reader = new XmlTextReader("urn:input", new StringReader("<input/>")) { DtdProcessing = DtdProcessing.Ignore, XmlResolver = entities };
        var policy = new QueryReaderSettings(reader);
        if (fork) policy = policy.Fork();
        var resources = new DocumentResources();
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE doc SYSTEM 'urn:dtd'><doc/>"));
        using var copy = policy.CreateReader(bytes, "urn:copy", resources);
        Assert.Equal(DtdProcessing.Ignore, copy.Settings?.DtdProcessing ?? ((XmlTextReader)copy).DtdProcessing);
        while (copy.Read()) { }
        Assert.Equal(0, entities.Calls);
        resources.ClaimWrite("urn:dtd");
    }

    [Fact]
    public void ParallelProngsShareOneGlobalAndOneDocumentLoad()
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        var sources = new Sources();
        XmlQueryRuntime owner = Runtime(resolver: sources);
        XmlQueryRuntime[] children = Enumerable.Range(0, 3).Select(_ => owner.CreateForkRuntime(new XmlCachedSequenceWriter())).ToArray();
        using var barrier = new Barrier(children.Length);
        object expected = new();
        var documents = new XPathNavigator[children.Length];
        int evaluations = 0;
        ForkProngs.Run<object>(() => null, children.Select<XmlQueryRuntime, Action<StreamTee<object>.Reader, CancellationToken>>((child, index) =>
            (_, token) => child.RunFork(r => {
                Assert.True(barrier.SignalAndWait(Deadline, token));
                if (!r.IsGlobalComputed(0)) { Interlocked.Increment(ref evaluations); r.SetGlobalValue(0, expected); }
                Assert.Same(expected, r.GetGlobalValue(0));
                documents[index] = r.ExternalContext.GetDataSource("urn:doc", null)!;
            }, token, null)).ToArray(), 1, cancellation.Token);
        Assert.Equal(1, evaluations);
        Assert.Equal(1, sources.Calls);
        Assert.True(documents.All(d => d.IsSamePosition(documents[0])));
        Assert.Same(expected, owner.GetGlobalValue(0));
    }

    [Fact]
    public void PublishedGlobalFailuresKeepTheComputingStackAcrossProngs()
    {
        var owner = Runtime();
        var origin = new XsltFrame("template", null, "origin", null, "urn:sheet", 1, 1);
        owner.TraceStack.Add(origin);
        Assert.False(owner.IsGlobalComputed(0));
        var failure = new InvalidOperationException("global failure");
        owner.GlobalValueFailed(0, failure);
        owner.TraceStack.Clear();
        var children = Enumerable.Range(0, 3).Select(_ => owner.CreateForkRuntime(new XmlCachedSequenceWriter())).ToArray();
        using var cancellation = new CancellationTokenSource(Deadline);
        ForkProngs.Run<object>(() => null, children.Select<XmlQueryRuntime, Action<StreamTee<object>.Reader, CancellationToken>>((child, i) =>
            (_, token) => {
                child.TraceStack.Add(new XsltFrame("template", null, "prong", null, "urn:sheet", i + 2, 1));
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => child.RunFork(r => r.IsGlobalComputed(0), token, null)));
                Assert.Same(origin, Assert.Single(Assert.IsType<XsltFrame[]>(failure.Data[XsltDynamicException.StackKey])));
            }).ToArray(), 1, cancellation.Token);
    }

    private sealed class UnavailableMetadataException : Exception
    {
        public override System.Collections.IDictionary Data => throw new InvalidOperationException("Metadata is unavailable");
    }

    [Fact]
    public void UnavailableExceptionMetadataDoesNotReplaceOrLeaveAGlobalFailureClaimed()
    {
        var owner = Runtime();
        var child = owner.CreateForkRuntime(new XmlCachedSequenceWriter());
        var failure = new UnavailableMetadataException();
        Assert.False(owner.IsGlobalComputed(0));
        owner.GlobalValueFailed(0, failure);
        OnThread(() => {
            Assert.Same(failure, Assert.Throws<UnavailableMetadataException>(() => child.RunFork(r => r.IsGlobalComputed(0), default, null)));
            Assert.Same(failure, Assert.Throws<UnavailableMetadataException>(() => child.IsGlobalComputed(0)));
        });
    }

    [Fact]
    public void PoliciesAreCapturedForEachProngAndRestoredAfterFailure()
    {
        OnThread(() => {
            using var cancellation = new CancellationTokenSource(Deadline);
            using var destination = new StringWriter();
            using var parentWriter = XmlWriter.Create(destination, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment });
            var parentPrincipal = new PrincipalResult(parentWriter, null);
            Func<string, Stream> opener = _ => new MemoryStream();
            Func<string, string, TextWriter> handler = (_, _) => new StringWriter();
            Action<string, XPathNavigator> trees = (_, _) => { };
            var packages = new[] { ("package", "1", new Uri("urn:package")) };
            XmlQueryRuntime.Cancellation = cancellation.Token;
            XmlQueryRuntime.DocumentFunctions = false;
            XmlQueryRuntime.ExtensionFunctions = false;
            XmlQueryRuntime.EnvironmentVariables = true;
            XmlQueryRuntime.DynamicTransforms = false;
            XmlQueryRuntime.Depth = 3;
            XmlQueryRuntime.RegexTimeout = Deadline;
            XmlQueryRuntime.Packages = packages;
            XmlQueryRuntime.ResultDocumentOpener = opener;
            XmlQueryRuntime.ResultDocumentHandler = handler;
            XmlQueryRuntime.ResultTreeHandler = trees;
            XmlQueryRuntime.BaseOutputUri = "urn:output";
            XmlQueryRuntime.Principal = parentPrincipal;
            CompactDocument.StripInputAnnotations = true;
            HtmlVoidElements.Html5 = true;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            XmlQueryRuntime child = Runtime().CreateForkRuntime(new XmlCachedSequenceWriter());
            var expected = new InvalidOperationException("body failure");
            OnThread(() => {
                // Deliberately different worker state: both success and failure must restore it.
                XmlQueryRuntime.Depth = 9;
                XmlQueryRuntime.DocumentFunctions = true;
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
                using var text = new StringWriter();
                using var writer = XmlWriter.Create(text, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment });
                var principal = new PrincipalResult(writer, null);
                Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => child.RunFork(r => {
                    Assert.Equal(cancellation.Token, XmlQueryRuntime.Cancellation);
                    Assert.False(XmlQueryRuntime.DocumentFunctions);
                    Assert.False(XmlQueryRuntime.ExtensionFunctions);
                    Assert.True(XmlQueryRuntime.EnvironmentVariables);
                    Assert.False(XmlQueryRuntime.DynamicTransforms);
                    Assert.Equal(3, XmlQueryRuntime.Depth);
                    Assert.Equal(Deadline, XmlQueryRuntime.RegexTimeout);
                    Assert.Same(packages, XmlQueryRuntime.Packages);
                    Assert.Same(opener, XmlQueryRuntime.ResultDocumentOpener);
                    Assert.Same(handler, XmlQueryRuntime.ResultDocumentHandler);
                    Assert.Same(trees, XmlQueryRuntime.ResultTreeHandler);
                    Assert.Equal("urn:output", XmlQueryRuntime.BaseOutputUri);
                    Assert.Same(principal, XmlQueryRuntime.Principal);
                    Assert.True(CompactDocument.StripInputAnnotations);
                    Assert.True(HtmlVoidElements.Html5);
                    Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
                    Assert.Equal("de-DE", CultureInfo.CurrentUICulture.Name);
                    XmlQueryRuntime.Principal!.WriteString("child");
                    Assert.Contains("FODC0002", Assert.Throws<XdmException>(() => r.XsltFunctions.X31OpenStreamedSource("urn:doc", "", "", "", "")).Message);
                    throw expected;
                }, default, principal)));
                Assert.Equal(9, XmlQueryRuntime.Depth);
                Assert.True(XmlQueryRuntime.DocumentFunctions);
                Assert.Null(XmlQueryRuntime.Principal);
                Assert.Null(XmlQueryRuntime.ResultDocumentHandler);
                Assert.Null(XmlQueryRuntime.ResultDocumentOpener);
                Assert.Null(XmlQueryRuntime.ResultTreeHandler);
                Assert.Null(XmlQueryRuntime.Packages);
                Assert.Null(XmlQueryRuntime.RegexTimeout);
                Assert.Null(XmlQueryRuntime.BaseOutputUri);
                Assert.False(XmlQueryRuntime.EnvironmentVariables);
                Assert.True(XmlQueryRuntime.ExtensionFunctions);
                Assert.Null(XmlQueryRuntime.DynamicTransforms);
                Assert.False(CompactDocument.StripInputAnnotations);
                Assert.False(HtmlVoidElements.Html5);
                Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.CurrentCulture);
                Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.CurrentUICulture);
                Assert.Equal(default, XmlQueryRuntime.Cancellation);
                writer.Flush();
                Assert.Equal("child", text.ToString());
                Assert.Empty(destination.ToString());
            });
            Assert.Same(parentPrincipal, XmlQueryRuntime.Principal);
            Assert.Equal(3, XmlQueryRuntime.Depth);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothTransformationAndProngCancellationApply(bool cancelTransformation)
    {
        OnThread(() => {
            using var transformation = new CancellationTokenSource();
            using var prong = new CancellationTokenSource();
            XmlQueryRuntime.Cancellation = transformation.Token;
            XmlQueryRuntime child = Runtime().CreateForkRuntime(new XmlCachedSequenceWriter());
            OnThread(() => Assert.Throws<OperationCanceledException>(() => child.RunFork(_ => {
                (cancelTransformation ? transformation : prong).Cancel();
                XmlQueryRuntime.EnterFunction();
            }, prong.Token, null)));
        });
    }

    [Fact]
    public void RecursionLimitIncludesTheInvokingDepthAndAChildOnlyRunsOnce()
    {
        OnThread(() => {
            XmlQueryRuntime.Depth = 2;
            XmlQueryRuntime child = Runtime().CreateForkRuntime(new XmlCachedSequenceWriter());
            OnThread(() => {
                Assert.Throws<InsufficientExecutionStackException>(() => child.RunFork(_ => XmlQueryRuntime.EnterFunctionCounted(2), default, null));
                Assert.Equal(0, XmlQueryRuntime.Depth);
                Assert.Throws<InvalidOperationException>(() => child.RunFork(_ => { }, default, null));
            });
            Assert.Equal(2, XmlQueryRuntime.Depth);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationBeforeEntryPreventsTheBodyAndRestoresWorkerState(bool cancelTransformation)
    {
        OnThread(() => {
            using var transformation = new CancellationTokenSource();
            using var prong = new CancellationTokenSource();
            XmlQueryRuntime.Cancellation = transformation.Token;
            XmlQueryRuntime child = Runtime().CreateForkRuntime(new XmlCachedSequenceWriter());
            (cancelTransformation ? transformation : prong).Cancel();
            OnThread(() => {
                bool ran = false;
                Assert.Throws<OperationCanceledException>(() => child.RunFork(_ => ran = true, prong.Token, null));
                Assert.False(ran);
                Assert.Equal(default, XmlQueryRuntime.Cancellation);
            });
        });
    }

    [Fact]
    public void DynamicContextAndAnnotationStateAreInheritedButStacksAreIndependent()
    {
        XmlQueryRuntime owner = Runtime();
        owner.PreserveAnnotations = false;
        owner.AnnotationScopes = new Stack<bool>(); owner.AnnotationScopes.Push(true);
        owner.SchemaAware = true;
        owner.TraceStack.Add(new XsltFrame("template", null, "/", null, "urn:sheet", 1, 1));
        owner.TryLines.Add(7);
        var now = owner.XsltFunctions.X31Context.CurrentDateTime;
        XmlQueryRuntime child = owner.CreateForkRuntime(new XmlCachedSequenceWriter());
        Assert.True(child.PreserveAnnotations);
        Assert.True(child.SchemaAware);
        Assert.Null(child.AnnotationScopes);
        Assert.False(child.PendingAnyType);
        Assert.Equal(now, child.XsltFunctions.X31Context.CurrentDateTime);
        Assert.Equal(owner.XsltFunctions.X31Context.ImplicitTimezoneMinutes, child.XsltFunctions.X31Context.ImplicitTimezoneMinutes);
        Assert.Equal(owner.RandomSeed, child.RandomSeed);
        Assert.NotSame(owner.XsltFunctions.X31Context, child.XsltFunctions.X31Context);
        child.TryLines.Add(8); child.TraceStack.Clear();
        Assert.Single(owner.TryLines); Assert.Single(owner.TraceStack);
        var failure = new InvalidOperationException("trace failure");
        child.TraceStack.Add(owner.TraceStack[0]);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => child.RunFork(_ => throw failure, default, null)));
        Assert.Single(Assert.IsType<XsltFrame[]>(failure.Data[XsltDynamicException.StackKey]));
    }

    [Fact]
    public void BorrowedInitialSelectionIsNeverClosedOrReopenedByTheChild()
    {
        using var input = XmlReader.Create(new StringReader("<input><row>text</row></input>"));
        XmlQueryRuntime owner = Runtime(Data(forward: true), input);
        XPathNavigator selection = owner.ExternalContext.InitialSelection(false);
        XmlQueryRuntime child = owner.CreateForkRuntime(new XmlCachedSequenceWriter());
        Assert.True(child.ExternalContext.IsInitialSelectionDocument(selection));
        Assert.Throws<InvalidOperationException>(() => child.ExternalContext.InitialSelection(false));
        child.ExternalContext.CloseInitialSource();
        Assert.Equal("text", selection.Value);
        owner.CloseStreamedSources();
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void FactoryDoesNotOpenAPendingInitialInput()
    {
        var sources = new Sources();
        var input = new InitialSource(new Uri("urn:input"), null, XslCompiledTransform.AbsentInput);
        XmlQueryRuntime owner = Runtime(Data(forward: true), input, sources);
        XmlQueryRuntime child = owner.CreateForkRuntime(new XmlCachedSequenceWriter());
        XmlQueryRuntime nested = child.CreateForkRuntime(new XmlCachedSequenceWriter());
        Assert.Throws<InvalidOperationException>(() => child.ExternalContext.InitialSelection(false));
        Assert.Throws<InvalidOperationException>(() => nested.ExternalContext.InitialSelection(false));
        Assert.Equal(0, sources.Calls);
    }

    [Fact]
    public void FailedProngClosesOnlyItsOwnForwardSources()
    {
        var sources = new Sources();
        XmlQueryRuntime owner = Runtime(resolver: sources);
        XPathNavigator parentSource = owner.XsltFunctions.X31OpenStreamedSource("urn:parent", "", "", "", "");
        XmlQueryRuntime child = owner.CreateForkRuntime(new XmlCachedSequenceWriter());
        var failure = new InvalidOperationException("body failure");
        OnThread(() => Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => child.RunFork(r => {
            r.XsltFunctions.X31OpenStreamedSource("urn:child", "", "", "", "");
            throw failure;
        }, default, null))));
        Assert.Equal(1, sources.Closed);
        Assert.Equal("", parentSource.Value);
        owner.CloseStreamedSources();
        Assert.Equal(2, sources.Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedCompiledAndCoercedFunctionsUseTheCallingProngsRuntime(bool coerce)
    {
        XmlQueryStaticData data = Data();
        XmlQueryRuntime? called = null;
        data.Dispatcher = (runtime, _) => { called = runtime; return new[] { AtomicValue.OfString("called") }; };
        XmlQueryRuntime owner = Runtime(data);
        var target = new CompiledFunction(4, new(null, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence), owner);
        FunctionItem function = coerce ? new CoercedFunction(target, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence) : target;
        XmlQueryRuntime child = owner.CreateForkRuntime(new XmlCachedSequenceWriter());
        OnThread(() => child.RunFork(r => {
            Assert.Equal("called", Assert.Single(r.XsltFunctions.CallFunction(function)).Value);
            Assert.Same(r, called);
            var call = new PendingCall(target);
            Assert.Equal(4, r.XsltFunctions.X31CallTarget(call));
            Assert.Equal("called", Assert.Single(r.XsltFunctions.X31CallRuntime(call)).Value);
            Assert.Same(r, called);
        }, default, null));
        Assert.Equal(-1, Runtime(data).XsltFunctions.X31CallTarget(new PendingCall(target)));
    }

    [Fact]
    public void CapturedBuiltInFunctionKeepsItsStaticContextWithAnIsolatedRuntime()
    {
        XmlQueryRuntime owner = Runtime();
        XmlQueryRuntime? expectedRuntime = null;
        var context = new FunctionContext(60, (_, _) => 0) { Runtime = owner, StaticBaseUri = "urn:base" };
        var expectedTime = context.CurrentDateTime;
        var definition = new FunctionDefinition(0, new QName("urn:test", "probe"), Array.Empty<(string, SequenceTypeTest)>(), SequenceTypeTest.AnySequence,
            (c, _) => {
                Assert.Same(expectedRuntime, c.Runtime);
                Assert.Equal("urn:base", c.StaticBaseUri);
                Assert.Equal(60, c.ImplicitTimezoneMinutes);
                Assert.Equal(expectedTime, c.CurrentDateTime);
                return new[] { AtomicValue.OfString("called") };
            }, false);
        var function = new LibraryFunction(definition, 0, context);
        XmlQueryRuntime child = owner.CreateForkRuntime(new XmlCachedSequenceWriter());
        expectedRuntime = child;
        OnThread(() => child.RunFork(r => Assert.Equal("called", Assert.Single(r.XsltFunctions.CallFunction(function)).Value), default, null));
        Assert.Same(owner, context.Runtime);
    }

    public sealed class ExtensionProbe
    {
        internal static int Instances;
        public ExtensionProbe() => Interlocked.Increment(ref Instances);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EarlyBoundObjectHasOneIdentityForTheTransformation(bool createdBeforeFork)
    {
        var manager = new StaticDataManager();
        manager.DeclareEarlyBound("urn:extension", typeof(ExtensionProbe));
        XmlQueryRuntime owner = Runtime(Data(manager: manager));
        ExtensionProbe.Instances = 0;
        object? existing = createdBeforeFork ? owner.GetEarlyBoundObject(0) : null;
        XmlQueryRuntime[] children = Enumerable.Range(0, 3).Select(_ => owner.CreateForkRuntime(new XmlCachedSequenceWriter())).ToArray();
        object[] instances = new object[children.Length];
        using var cancellation = new CancellationTokenSource(Deadline);
        ForkProngs.Run<object>(() => null, children.Select<XmlQueryRuntime, Action<StreamTee<object>.Reader, CancellationToken>>((child, i) =>
            (_, token) => child.RunFork(r => instances[i] = r.GetEarlyBoundObject(0), token, null)).ToArray(), 1, cancellation.Token);
        Assert.Equal(1, ExtensionProbe.Instances);
        Assert.All(instances, value => Assert.Same(owner.GetEarlyBoundObject(0), value));
        if (existing != null) Assert.Same(existing, instances[0]);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_command")]
    private static extern ref XmlILCommand? Command(XslCompiledTransform transform);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_delExec")]
    private static extern ref ExecuteDelegate Execute(XmlILCommand command);

    [Theory]
    [InlineData(false)]
#if NET9_0_OR_GREATER
    [InlineData(true)]
#endif
    public void CompiledCapturedFunctionsConstructIndependentResultsOverTeeViews(bool saved)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:f="urn:functions"
                xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="f xs">
              <xsl:variable name="g" select="name(/*)"/>
              <xsl:variable name="d" select="doc('urn:shared')"/>
              <xsl:function name="f:render" streamability="absorbing" as="element(out)">
                <xsl:param name="n" as="node()"/><xsl:param name="captured" as="xs:string"/>
                <out global="{$g}" captured="{$captured}" doc="{name($d/*)}"><xsl:value-of select="count($n/doc/row)"/></out>
              </xsl:function>
              <xsl:template match="/"><xsl:sequence select="let $captured := name(/*) return function($n) { f:render($n, $captured) }"/></xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XslCompiledTransform { RawResult = true };
        var settings = new XsltSettings { EnableDocumentFunction = true, StreamedExecution = true };
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null, "Xsl3.Tests.ForkFunctions", "Saved.ForkFunctions", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.ForkFunctions", true)!);
#else
            throw new NotSupportedException("Saving assemblies requires .NET 9 or later");
#endif
        }
        else
            transform.Load(XmlReader.Create(new StringReader(sheet)), settings, null);
        XmlILCommand command = Command(transform)!;
        var sources = new Sources("<resource/>");
        using var global = XmlReader.Create(new StringReader("<global/>"));
        var sink = new XmlCachedSequenceWriter();
        var owner = new XmlQueryRuntime(command.StaticData, global, sources, null, sink);
        Execute(command)(owner);
        var function = Assert.IsAssignableFrom<FunctionItem>(Assert.Single(sink.ResultSequence));
        XmlQueryRuntime[] children = Enumerable.Range(0, 2).Select(_ => owner.CreateForkRuntime(new XmlCachedSequenceWriter())).ToArray();
        using var reader = XmlReader.Create(new StringReader("<doc><row/><row/></doc>"));
        using var document = new StreamedDocument(reader);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator());
        using var cancellation = new CancellationTokenSource(Deadline);
        var results = new XPathNavigator[children.Length];
        fork.Run(children.Select<XmlQueryRuntime, Action<StreamedNavigator, CancellationToken>>((child, i) =>
            (view, token) => child.RunFork(r => results[i] = Assert.IsAssignableFrom<XPathNavigator>(
                Assert.Single(r.XsltFunctions.CallFunction(function, new XPathItem[] { view }))), token, null)).ToArray(), 1, cancellation.Token);
        Assert.All(results, node => Assert.Equal("<out global=\"global\" captured=\"global\" doc=\"resource\">2</out>", node.OuterXml));
        Assert.False(results[0].IsSamePosition(results[1]));
        Assert.Single(sink.ResultSequence);
        Assert.Equal(1, sources.Calls);
    }
}
