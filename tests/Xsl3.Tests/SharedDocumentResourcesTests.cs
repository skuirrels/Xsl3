// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.Tree;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class SharedDocumentResourcesTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private const string Xml = "<doc><row>one</row><row>two</row></doc>";
    private sealed class TrackedStream(string xml, Action closed) : MemoryStream(Encoding.UTF8.GetBytes(xml))
    {
        protected override void Dispose(bool disposing) { if (CanRead) closed(); base.Dispose(disposing); }
    }
    private sealed class Sources : XmlResolver
    {
        internal int Calls, Closed;
        internal Func<Uri, object?>? Load;
        public override System.Net.ICredentials? Credentials { set { } }
        public override object GetEntity(Uri uri, string? role, Type? type)
        {
            Interlocked.Increment(ref Calls);
            return Load != null ? Load(uri)! : new TrackedStream(Xml, () => Interlocked.Increment(ref Closed));
        }
    }
    private static XmlQueryStaticData Data(bool forward = false) =>
        new(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true, StreamedInitialMode = forward };
    private static XmlQueryRuntime Runtime(XmlQueryStaticData data, XmlResolver source, object? input = null) =>
        new(data, input ?? XslCompiledTransform.AbsentInput, source, null, new XmlCachedSequenceWriter());
    private static void Run(params Action<CancellationToken>[] bodies)
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        ForkProngs.Run<object>(() => null, bodies.Select<Action<CancellationToken>, Action<StreamTee<object>.Reader, CancellationToken>>(
            body => (_, token) => {
                CancellationToken saved = XmlQueryRuntime.Cancellation;
                XmlQueryRuntime.Cancellation = token;
                try { body(token); } finally { XmlQueryRuntime.Cancellation = saved; }
            }).ToArray(), 1, cancellation.Token);
    }
    private static void Code(string code, Action body) => Assert.Contains(code, Assert.Throws<XdmException>(body).Message);

    private static string Hosted(string body, Sources sources)
    {
        string stylesheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>" +
            "<xsl:output omit-xml-declaration='yes'/><xsl:template match='/'>" + body + "</xsl:template></xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet), new XmlReaderSettings(), "urn:doc"),
            new() { EnableEvaluate = true, EnableDocumentFunction = true, EnableResultDocument = true }, null);
        using var input = XmlReader.Create(new StringReader("<input/>")); using var output = new StringWriter();
        transform.Transform(input, null, output, sources);
        return output.ToString();
    }

    [Fact]
    public void HostedEvaluationSharesDocumentsWithoutCachingItsFocusAsAnotherPrincipalInput()
    {
        var sources = new Sources();
        string result = Hosted("""
            <xsl:variable name="d" select="doc('urn:doc')"/>
            <xsl:variable name="focus" as="document-node()"><xsl:document><temp/></xsl:document></xsl:variable>
            <xsl:variable name="e" as="document-node()"><xsl:evaluate xpath="'doc(''urn:doc'')'" context-item="$focus"/></xsl:variable>
            <out same="{$d is $e}"><xsl:evaluate xpath="'name(/*)'" context-item="$focus"/></out>
            """, sources);
        Assert.Equal("<out same=\"true\">temp</out>", result); Assert.Equal(1, sources.Calls);
    }

    [Fact]
    public void HostedEvaluationReadClaimsPreventAnOuterResultDocumentWrite()
    {
        var sources = new Sources();
        Exception error = Assert.ThrowsAny<Exception>(() => Hosted("""
            <xsl:variable name="e" as="document-node()"><xsl:evaluate xpath="'doc(''urn:doc'')'"/></xsl:variable>
            <out><xsl:value-of select="count($e/*)"/><xsl:result-document href="urn:doc"><bad/></xsl:result-document></out>
            """, sources));
        Assert.Contains("XTDE1500", error.ToString()); Assert.Equal(1, sources.Calls);
    }

    [Fact]
    public void ConcurrentLoadsKeepOneDocumentAndIndependentPositions()
    {
        var sources = new Sources(); var data = Data();
        XmlQueryRuntime parent = Runtime(data, sources);
        XmlQueryRuntime[] children = Enumerable.Range(0, 4).Select(_ => Runtime(data, sources)).ToArray();
        foreach (var child in children) child.ShareDocuments(parent);
        var positions = new XPathNavigator[children.Length];
        var ids = new string[children.Length];
        using var barrier = new Barrier(children.Length);
        Run(children.Select<XmlQueryRuntime, Action<CancellationToken>>((child, index) => token => {
            Assert.True(barrier.SignalAndWait(Deadline, token));
            positions[index] = child.ExternalContext.GetDataSource("urn:doc", null)!;
            ids[index] = child.GenerateId(positions[index]);
        }).ToArray());
        Assert.Equal(1, sources.Calls); Assert.Equal(1, sources.Closed);
        Assert.All(positions, position => Assert.True(position.IsSamePosition(positions[0])));
        Assert.All(ids, id => Assert.Equal(ids[0], id));
        Assert.True(positions[0].MoveToFirstChild());
        Assert.Equal(XPathNodeType.Root, positions[1].NodeType);
        XPathNavigator again = parent.ExternalContext.GetDataSource("urn:doc", null)!;
        Assert.Equal(XPathNodeType.Root, again.NodeType); Assert.Equal(ids[0], parent.GenerateId(again));
    }

    [Fact]
    public void ResolvedAliasesUseTheSameCacheEntry()
    {
        var sources = new Sources(); var data = Data();
        XmlQueryRuntime parent = Runtime(data, sources), child = Runtime(data, sources); child.ShareDocuments(parent);
        XPathNavigator a = parent.ExternalContext.GetDataSource("doc.xml", "https://example.test/data/")!;
        XPathNavigator b = child.ExternalContext.GetDataSource("https://example.test/data/doc.xml", null)!;
        Assert.True(a.IsSamePosition(b)); Assert.Equal(1, sources.Calls);
    }

    [Fact]
    public void PendingReadsBlockWritesWithoutHoldingTheCacheLockAcrossIo()
    {
        var sources = new Sources(); var data = Data();
        XmlQueryRuntime parent = Runtime(data, sources), child = Runtime(data, sources); child.ShareDocuments(parent);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        sources.Load = _ => { entered.Set(); Assert.True(release.Wait(Deadline)); return new MemoryStream(Encoding.UTF8.GetBytes(Xml)); };
        Run(_ => Assert.Equal("onetwo", parent.ExternalContext.GetDataSource("urn:doc", null)!.Value), token => {
            Assert.True(entered.Wait(Deadline, token));
            try { Code("XTDE1500", () => child.Documents.ClaimWrite("urn:doc")); child.Documents.ClaimWrite("urn:other"); }
            finally { release.Set(); }
        });
        Code("XTDE1500", () => parent.Documents.ClaimWrite("urn:doc"));
    }

    [Fact]
    public void AWriteClaimPreventsReadingBeforeTheProviderIsCalled()
    {
        var sources = new Sources(); var data = Data();
        XmlQueryRuntime parent = Runtime(data, sources), child = Runtime(data, sources); child.ShareDocuments(parent);
        parent.Documents.ClaimWrite("urn:doc");
        Code("XTDE1500", () => child.ExternalContext.GetDataSource("urn:doc", null));
        Assert.Equal(0, sources.Calls);
    }

    [Fact]
    public void ConcurrentWriteClaimsChooseOneOwnerAndReportTheDuplicate()
    {
        var resources = new DocumentResources();
        using var barrier = new Barrier(2);
        var errors = new Exception?[2];
        Run(Enumerable.Range(0, 2).Select<int, Action<CancellationToken>>(index => token => {
            Assert.True(barrier.SignalAndWait(Deadline, token));
            try { resources.ClaimWrite("urn:result"); } catch (XdmException error) { errors[index] = error; }
        }).ToArray());
        Assert.Single(errors, error => error == null);
        Assert.Contains("XTDE1490", Assert.IsType<XdmException>(Assert.Single(errors, error => error != null)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrAbsentLoadsCanBeRetriedWithoutInventingACachedDocument(bool absent)
    {
        var sources = new Sources(); var runtime = Runtime(Data(), sources);
        sources.Load = _ => sources.Calls == 1 ? absent ? null : throw new IOException("unavailable") : new MemoryStream(Encoding.UTF8.GetBytes(Xml));
        if (absent) Assert.Null(runtime.ExternalContext.GetDataSource("urn:doc", null));
        else Code("FODC0002", () => runtime.ExternalContext.GetDataSource("urn:doc", null));
        Assert.Equal("onetwo", runtime.ExternalContext.GetDataSource("urn:doc", null)!.Value);
        Assert.Equal(2, sources.Calls);
    }

    [Fact]
    public void FailedLoadsReleaseTheReadReservation()
    {
        var sources = new Sources { Load = _ => throw new IOException("unavailable") };
        var runtime = Runtime(Data(), sources);
        Code("FODC0002", () => runtime.ExternalContext.GetDataSource("urn:doc", null));
        runtime.Documents.ClaimWrite("urn:doc");
        Code("XTDE1500", () => runtime.ExternalContext.GetDataSource("urn:doc", null));
        Assert.Equal(1, sources.Calls);
    }

    [Fact]
    public void ADocumentThatWasReadButFailedToParseStillConflictsWithWriting()
    {
        var sources = new Sources { Load = _ => new MemoryStream(Encoding.UTF8.GetBytes("<doc>")) };
        var runtime = Runtime(Data(), sources);
        Code("FODC0002", () => runtime.ExternalContext.GetDataSource("urn:doc", null));
        Code("XTDE1500", () => runtime.Documents.ClaimWrite("urn:doc"));
    }

    [Theory]
    [InlineData("unparsed-text")]
    [InlineData("unparsed-text-lines")]
    [InlineData("unparsed-text-available")]
    [InlineData("stream-available")]
    [InlineData("json-doc")]
    [InlineData("doc-available")]
    public void TextJsonAndAvailabilityFunctionsShareResourceClaims(string function)
    {
        var data = Data();
        var definition = FunctionLibrary.Find(QName.Ns.Fn, function, 1)!;
        foreach (bool writeFirst in new[] { false, true })
        {
            var sources = new Sources { Load = _ => new MemoryStream(Encoding.UTF8.GetBytes(function == "json-doc" ? "1" : Xml)) };
            XmlQueryRuntime parent = Runtime(data, sources), child = Runtime(data, sources); child.ShareDocuments(parent);
            IList<XPathItem> Read() => definition.Invoke(child.XsltFunctions.X31Context,
                new IList<XPathItem>[] { new XPathItem[] { AtomicValue.OfString("urn:data") } });
            if (writeFirst)
            {
                parent.Documents.ClaimWrite("urn:data"); Code("XTDE1500", () => Read());
                Assert.Equal(0, sources.Calls);
            }
            else
            {
                Assert.NotEmpty(Read()); Code("XTDE1500", () => parent.Documents.ClaimWrite("urn:data"));
                Assert.Equal(1, sources.Calls);
            }
        }
    }

    [Fact]
    public void ConcurrentFailedLoadsPublishTheFailureAndReleaseAllWaiters()
    {
        var sources = new Sources(); var data = Data();
        XmlQueryRuntime parent = Runtime(data, sources);
        var children = Enumerable.Range(0, 3).Select(_ => Runtime(data, sources)).ToArray();
        foreach (var child in children) child.ShareDocuments(parent);
        sources.Load = _ => {
            Assert.True(SpinWait.SpinUntil(() => parent.Documents.WaitingCount == 2, Deadline));
            throw new IOException("unavailable");
        };
        Run(children.Select<XmlQueryRuntime, Action<CancellationToken>>(child => _ =>
            Code("FODC0002", () => child.ExternalContext.GetDataSource("urn:doc", null))).ToArray());
        Assert.Equal(1, sources.Calls); Assert.Equal(0, parent.Documents.WaitingCount);
        parent.Documents.ClaimWrite("urn:doc");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecursiveRetrievalReportsAnErrorInsteadOfDeadlocking(bool crossThread)
    {
        var sources = new Sources(); var data = Data();
        XmlQueryRuntime first = Runtime(data, sources), second = Runtime(data, sources);
        using var watchdog = new CancellationTokenSource(Deadline);
        CancellationToken saved = XmlQueryRuntime.Cancellation;
        XmlQueryRuntime.Cancellation = watchdog.Token;
        try { second.ShareDocuments(first); } finally { XmlQueryRuntime.Cancellation = saved; }
        using var barrier = new Barrier(2);
        sources.Load = uri => {
            if (!crossThread) return first.ExternalContext.GetDataSource("urn:a", null);
            Assert.True(barrier.SignalAndWait(Deadline));
            return uri.OriginalString == "urn:a" ? first.ExternalContext.GetDataSource("urn:b", null) : second.ExternalContext.GetDataSource("urn:a", null);
        };
        if (crossThread) Run(_ => Code("FODC0002", () => first.ExternalContext.GetDataSource("urn:a", null)),
            _ => Code("FODC0002", () => second.ExternalContext.GetDataSource("urn:b", null)));
        else Code("FODC0002", () => first.ExternalContext.GetDataSource("urn:a", null));
        Assert.Equal(0, first.Documents.WaitingCount);
        first.Documents.ClaimWrite("urn:a");
    }

    [Fact]
    public void CompletedDependenciesDoNotCreateSpuriousRetrievalCycles()
    {
        for (int iteration = 0; iteration < 30; iteration++)
        {
            var sources = new Sources(); var data = Data();
            XmlQueryRuntime first = Runtime(data, sources), second = Runtime(data, sources); second.ShareDocuments(first);
            using var cEntered = new ManualResetEventSlim();
            sources.Load = uri => {
                if (uri.OriginalString == "urn:c")
                {
                    cEntered.Set(); Assert.True(SpinWait.SpinUntil(() => first.Documents.WaitingCount == 1, Deadline));
                    return new MemoryStream(Encoding.UTF8.GetBytes(Xml));
                }
                if (uri.OriginalString == "urn:b") return second.ExternalContext.GetDataSource("urn:c", null);
                first.ExternalContext.GetDataSource("urn:c", null);
                return first.ExternalContext.GetDataSource("urn:b", null);
            };
            Run(_ => Assert.NotNull(first.ExternalContext.GetDataSource("urn:a", null)), token => {
                Assert.True(cEntered.Wait(Deadline, token)); Assert.NotNull(second.ExternalContext.GetDataSource("urn:b", null));
            });
            Assert.Equal(3, sources.Calls); Assert.Equal(0, first.Documents.WaitingCount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForksCreatedDuringRetrievalInheritOnlyThatLoadAttempt(bool failFirst)
    {
        var sources = new Sources(); var data = Data();
        XmlQueryRuntime parent = Runtime(data, sources), child = Runtime(data, sources);
        sources.Load = _ => {
            if (sources.Calls == 1)
            {
                child.ShareDocuments(parent);
                Run(_ => Code("FODC0002", () => child.ExternalContext.GetDataSource("urn:doc", null)));
                if (failFirst) throw new IOException("first attempt unavailable");
            }
            return new MemoryStream(Encoding.UTF8.GetBytes(Xml));
        };
        if (failFirst) Code("FODC0002", () => parent.ExternalContext.GetDataSource("urn:doc", null));
        else Assert.NotNull(parent.ExternalContext.GetDataSource("urn:doc", null));
        Assert.NotNull(child.ExternalContext.GetDataSource("urn:doc", null));
        Assert.Equal(failFirst ? 2 : 1, sources.Calls);
    }

    [Fact]
    public void CancelledWaitersDoNotAbortTheOwnersLoad()
    {
        var sources = new Sources(); var data = Data();
        XmlQueryRuntime parent = Runtime(data, sources), child = Runtime(data, sources); child.ShareDocuments(parent);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var waiter = new CancellationTokenSource(Deadline);
        sources.Load = _ => { entered.Set(); Assert.True(release.Wait(Deadline)); return new MemoryStream(Encoding.UTF8.GetBytes(Xml)); };
        Run(_ => Assert.NotNull(parent.ExternalContext.GetDataSource("urn:doc", null)), token => {
            Assert.True(entered.Wait(Deadline, token));
            XmlQueryRuntime.Cancellation = waiter.Token;
            try { Assert.Throws<OperationCanceledException>(() => child.ExternalContext.GetDataSource("urn:doc", null)); }
            finally { release.Set(); }
        }, _ => {
            Assert.True(SpinWait.SpinUntil(() => parent.Documents.WaitingCount == 1, Deadline)); waiter.Cancel();
        });
        Assert.Equal(1, sources.Calls); Assert.Equal(0, parent.Documents.WaitingCount);
        Assert.NotNull(child.ExternalContext.GetDataSource("urn:doc", null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedLoadsUseTransformationCancellationAndRestoreTheProng(bool cancelTransformation)
    {
        var sources = new Sources(); var data = Data();
        XmlQueryRuntime parent = Runtime(data, sources), child = Runtime(data, sources);
        using var transformation = new CancellationTokenSource(); using var prong = new CancellationTokenSource();
        CancellationToken saved = XmlQueryRuntime.Cancellation;
        XmlQueryRuntime.Cancellation = transformation.Token;
        try { child.ShareDocuments(parent); } finally { XmlQueryRuntime.Cancellation = saved; }
        sources.Load = _ => {
            Assert.Equal(transformation.Token, XmlQueryRuntime.Cancellation);
            if (cancelTransformation) transformation.Cancel(); else prong.Cancel();
            XmlQueryRuntime.CheckCancellation();
            return new MemoryStream(Encoding.UTF8.GetBytes(Xml));
        };
        Run(_ => {
            XmlQueryRuntime.Cancellation = prong.Token;
            if (cancelTransformation) Assert.Throws<OperationCanceledException>(() => child.ExternalContext.GetDataSource("urn:doc", null));
            else Assert.NotNull(child.ExternalContext.GetDataSource("urn:doc", null));
            Assert.Equal(prong.Token, XmlQueryRuntime.Cancellation);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamedAndInitialSourcesReserveTheUriWhileOpeningAndAfterClosure(bool initial)
    {
        var sources = new Sources(); var data = Data(initial);
        XmlQueryRuntime parent = Runtime(data, sources), child = Runtime(data, sources,
            initial ? new InitialSource(new Uri("urn:doc"), null, XslCompiledTransform.AbsentInput) : null);
        child.ShareDocuments(parent);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        sources.Load = _ => {
            entered.Set(); Assert.True(release.Wait(Deadline));
            return new TrackedStream(Xml, () => Interlocked.Increment(ref sources.Closed));
        };
        Run(_ => {
            if (initial) child.ExternalContext.InitialSelection(accumulators: false);
            else child.ExternalContext.OpenStreamedSource("urn:doc", "", stripAnnotations: true);
        }, token => {
            Assert.True(entered.Wait(Deadline, token));
            try { Code("XTDE1500", () => parent.Documents.ClaimWrite("urn:doc")); }
            finally { release.Set(); }
        });
        child.CloseStreamedSources();
        Assert.Equal(1, sources.Closed);
        Code("XTDE1500", () => parent.Documents.ClaimWrite("urn:doc"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcurrentDocumentIdsRemainStableAndDistinct(bool compact)
    {
        var sources = new Sources(); var data = Data();
        XmlQueryRuntime parent = Runtime(data, sources);
        var nodes = new XPathNavigator[50];
        for (int i = 0; i < nodes.Length; i++)
        {
            using var input = XmlReader.Create(new StringReader(Xml));
            nodes[i] = compact ? CompactDocument.Load(input).CreateNavigator() : new XPathDocument(input).CreateNavigator();
        }
        var results = new string[4][];
        var children = Enumerable.Range(0, results.Length).Select(_ => Runtime(data, sources)).ToArray();
        foreach (var child in children) child.ShareDocuments(parent);
        using var barrier = new Barrier(children.Length);
        Run(children.Select<XmlQueryRuntime, Action<CancellationToken>>((child, index) => token => {
            Assert.True(barrier.SignalAndWait(Deadline, token)); results[index] = new string[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                int n = (i + index) % nodes.Length;
                results[index][n] = child.GenerateId(nodes[n].Clone());
            }
        }).ToArray());
        Assert.Equal(nodes.Length, results[0].Distinct().Count());
        Assert.All(results, ids => Assert.Equal(results[0], ids));
    }

    [Fact]
    public void IndependentTransformationsDoNotShareDocumentsOrUriClaims()
    {
        var sources = new Sources(); var data = Data();
        XmlQueryRuntime first = Runtime(data, sources), second = Runtime(data, sources);
        XPathNavigator a = first.ExternalContext.GetDataSource("urn:doc", null)!;
        XPathNavigator b = second.ExternalContext.GetDataSource("urn:doc", null)!;
        Assert.False(a.IsSamePosition(b)); Assert.Equal(2, sources.Calls);
        first.Documents.ClaimWrite("urn:result"); second.Documents.ClaimWrite("urn:result");
    }

    [Fact]
    public void SharingRejectsAUsedRuntimeOrDifferentResolver()
    {
        var sources = new Sources(); var data = Data();
        XmlQueryRuntime parent = Runtime(data, sources), child = Runtime(data, sources);
        Assert.Throws<InvalidOperationException>(() => Runtime(data, new Sources()).ShareDocuments(parent));
        child.ExternalContext.GetDataSource("urn:doc", null);
        Assert.Throws<InvalidOperationException>(() => child.ShareDocuments(parent));
        XmlQueryRuntime identified = Runtime(data, sources);
        identified.GenerateId(new XPathDocument(new StringReader(Xml)).CreateNavigator());
        Assert.Throws<InvalidOperationException>(() => identified.ShareDocuments(parent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParentRuntimesSeeProngCopiesAndCopiesOfCopiesAfterInputClosure(bool snapshot)
    {
        var data = Data(forward: true); var constants = new ConstantTable(); data.Constants = constants;
        int table = constants.Add(new Dictionary<string, AccumulatorInfo> { ["Q{}n"] = new(0, new[] { false }, null, Streamable: true) });
        int names = constants.Add(new XsltNameContext(new Dictionary<string, string>(), new HashSet<(string, string, int)>()));
        IList<XPathItem> One(System.Numerics.BigInteger value) => new XPathItem[] { AtomicValue.OfInteger(value) };
        var functions = new XPathItem[] {
            new NativeFunction(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence, _ => One(0), "initial"),
            new NativeFunction(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence, a => ((XPathNavigator)a[0][0]).LocalName == "row" ? One(0) : Array.Empty<XPathItem>(), "match"),
            new NativeFunction(new[] { SequenceTypeTest.AnySequence, SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence, a => One(((IntegerValue)a[1][0]).Integer + 1), "rule"),
        };
        var sources = new Sources(); using var input = XmlReader.Create(new StringReader(Xml));
        XmlQueryRuntime parent = Runtime(data, sources, new InitialSource(input, null, XslCompiledTransform.AbsentInput));
        var children = new[] { Runtime(data, sources), Runtime(data, sources) };
        foreach (var child in children) child.ShareDocuments(parent);
        var focus = (StreamedNavigator)parent.XsltFunctions.X31InitialObservedSource(functions, table);
        var copies = new XPathNavigator[children.Length];
        using var cancellation = new CancellationTokenSource(Deadline);
        using (var fork = focus.Document.Fork(focus))
            fork.Run(children.Select<XmlQueryRuntime, Action<StreamedNavigator, CancellationToken>>((child, index) => (view, _) => {
                copies[index] = (XPathNavigator)Assert.Single(snapshot
                    ? child.XsltFunctions.X31Snapshot(new XPathItem[] { view }, false)
                    : child.XsltFunctions.X31CopyWithAccumulators(new XPathItem[] { view }));
            }).ToArray(), 1, cancellation.Token);
        parent.CloseStreamedSources();
        foreach (XPathNavigator copy in copies)
        {
            Assert.True(copy.MoveToFirstChild()); Assert.True(copy.MoveToFirstChild()); Assert.True(copy.MoveToNext());
            Assert.Equal("2", Assert.Single(parent.XsltFunctions.X31AccumulatorValue(functions, table, "n", names, new XPathItem[] { copy }, false, 0)).Value);
            XPathNavigator again = (XPathNavigator)Assert.Single(parent.XsltFunctions.X31CopyWithAccumulators(new XPathItem[] { copy }));
            Assert.Equal("2", Assert.Single(parent.XsltFunctions.X31AccumulatorValue(functions, table, "n", names, new XPathItem[] { again }, true, 0)).Value);
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }
}
