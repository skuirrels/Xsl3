// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;
using Xunit;

namespace Xsl3.Tests;

public class EntityResourceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private const string Xml = "<!DOCTYPE doc SYSTEM 'urn:dtd'><doc>&external;</doc>";
    private sealed class Sources(bool text = false) : XmlResolver
    {
        internal readonly Dictionary<string, string> Content = new() {
            ["urn:doc"] = Xml, ["urn:dtd"] = "<!ENTITY external SYSTEM 'urn:entity'>", ["urn:entity"] = "expanded",
        };
        internal readonly ConcurrentDictionary<string, int> Calls = new();
        internal readonly ConcurrentBag<Func<bool>> Closed = new();
        internal Func<Uri, object?>? Open;
        internal ICredentials? ReceivedCredentials;
        public override ICredentials? Credentials { set => ReceivedCredentials = value; }
        public override bool SupportsType(Uri uri, Type? type) => text && type == typeof(TextReader) || base.SupportsType(uri, type);
        public override object GetEntity(Uri uri, string? role, Type? type)
        {
            Calls.AddOrUpdate(uri.OriginalString, 1, (_, count) => count + 1);
            if (Open?.Invoke(uri) is { } provided) return provided;
            if (!Content.TryGetValue(uri.OriginalString, out string? content)) throw new IOException("No configured resource: " + uri);
            if (type == typeof(TextReader))
            {
                var reader = new TrackedText(content); Closed.Add(() => reader.Closed); return reader;
            }
            var stream = new TrackedStream(content); Closed.Add(() => stream.Closed); return stream;
        }
    }
    private sealed class TrackedStream(string text, bool failClose = false) : MemoryStream(Encoding.UTF8.GetBytes(text))
    {
        internal bool Closed;
        protected override void Dispose(bool disposing)
        {
            Closed = true; base.Dispose(disposing);
            if (failClose) throw new IOException("close-failed");
        }
    }
    private sealed class TrackedText(string text) : StringReader(text)
    {
        internal bool Closed;
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }
    private sealed class CancelStream(string text, CancellationTokenSource cancellation) : MemoryStream(Encoding.UTF8.GetBytes(text))
    {
        public override int Read(Span<byte> buffer) { int read = base.Read(buffer); cancellation.Cancel(); return read; }
        public override int Read(byte[] buffer, int offset, int count) { int read = base.Read(buffer, offset, count); cancellation.Cancel(); return read; }
    }
    private sealed class AsyncSource(object entity, Task ready) : XmlResolver
    {
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri uri, string? role, Type? type) => throw new NotSupportedException();
        public override async Task<object> GetEntityAsync(Uri uri, string? role, Type? type)
        { await ready.ConfigureAwait(false); return entity; }
    }
    private sealed class UnreadableStream : Stream
    {
        internal bool Closed;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }
    private static XmlQueryStaticData Data(bool forward = false) =>
        new(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true, StreamedInitialMode = forward };
    private static XmlQueryRuntime Runtime(XmlResolver sources, object? input = null, bool forward = false) =>
        new(Data(forward), input ?? XslCompiledTransform.AbsentInput, sources, null, new XmlCachedSequenceWriter());
    private static XPathNavigator Read(XmlQueryRuntime runtime, int mode) => mode switch {
        0 => runtime.ExternalContext.GetDataSource("urn:doc", null)!,
        1 => runtime.ExternalContext.OpenStreamedSource("urn:doc", "", stripAnnotations: true),
        _ => runtime.ExternalContext.InitialSelection(accumulators: false),
    };
    private static void Code(string code, Action body) => Assert.Contains(code, Assert.Throws<XdmException>(body).Message);

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    [InlineData(3, false)] [InlineData(3, true)]
    public void ProcessorCreatedReadersTrackActualDtdAndEntityReads(int mode, bool text)
    {
        var sources = new Sources(text);
        using var borrowed = mode == 3 ? new TrackedStream(Xml) : null;
        var runtime = Runtime(sources, mode >= 2 ? new InitialSource((object?)borrowed ?? new Uri("urn:doc"),
            mode == 3 ? new Uri("urn:doc") : null, XslCompiledTransform.AbsentInput) : null, mode >= 2);
        try { Assert.Equal("expanded", Read(runtime, mode).Value); }
        finally { runtime.CloseStreamedSources(); }
        Assert.Equal(1, sources.Calls["urn:dtd"]); Assert.Equal(1, sources.Calls["urn:entity"]);
        Assert.All(sources.Closed, closed => Assert.True(closed()));
        if (borrowed != null) Assert.False(borrowed.Closed);
        Code("XTDE1500", () => runtime.Documents.ClaimWrite("urn:dtd"));
        Code("XTDE1500", () => runtime.Documents.ClaimWrite("urn:entity"));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void WrittenEntitiesAreRejectedBeforeCallingTheProviderAndKeepTheirCode(int mode)
    {
        var sources = new Sources();
        var runtime = Runtime(sources, mode == 2 ? new InitialSource(new Uri("urn:doc"), null, XslCompiledTransform.AbsentInput) : null, mode == 2);
        runtime.Documents.ClaimWrite("urn:entity");
        try { Code("XTDE1500", () => _ = Read(runtime, mode).Value); }
        finally { runtime.CloseStreamedSources(); }
        Assert.False(sources.Calls.ContainsKey("urn:entity"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReaderSettingsKeepTheirConfiguredResolverInsteadOfUsingTheDocumentProvider(bool legacy)
    {
        var documents = new Sources(); var entities = new Sources();
        documents.Content.Remove("urn:dtd"); documents.Content.Remove("urn:entity");
        using XmlReader input = legacy
            ? new XmlTextReader(new StringReader("<input/>")) { XmlResolver = entities, DtdProcessing = DtdProcessing.Parse }
            : XmlReader.Create(new StringReader("<input/>"), new() { XmlResolver = entities, DtdProcessing = DtdProcessing.Parse });
        var runtime = Runtime(documents, new InitialSource(input, null, XslCompiledTransform.AbsentInput));
        Assert.Equal("expanded", Read(runtime, 0).Value);
        Assert.Equal(1, entities.Calls["urn:dtd"]); Assert.Equal(1, documents.Calls["urn:doc"]);
        Assert.Equal(ReadState.Initial, input.ReadState);
    }

    [Fact]
    public void ExplicitlyDisabledResolversStayDisabled()
    {
        var sources = new Sources(); sources.Content["urn:doc"] = "<!DOCTYPE doc SYSTEM 'urn:dtd'><doc/>";
        using var input = XmlReader.Create(new StringReader("<input/>"), new() { XmlResolver = null, DtdProcessing = DtdProcessing.Parse });
        var runtime = Runtime(sources, new InitialSource(input, null, XslCompiledTransform.AbsentInput));
        Assert.Equal("", Read(runtime, 0).Value);
        Assert.False(sources.Calls.ContainsKey("urn:dtd"));
        runtime.Documents.ClaimWrite("urn:dtd");
    }

    [Fact]
    public void InternalEntitiesDoNotInventExternalReads()
    {
        var sources = new Sources(); sources.Content["urn:doc"] = "<!DOCTYPE doc [<!ENTITY external 'internal'>]><doc>&external;</doc>";
        var runtime = Runtime(sources);
        Assert.Equal("internal", Read(runtime, 0).Value);
        Assert.Single(sources.Calls); runtime.Documents.ClaimWrite("urn:entity");
    }

    [Fact]
    public void UnusedExternalEntityDeclarationsDoNotClaimTheirResources()
    {
        var sources = new Sources(); sources.Content["urn:doc"] = "<!DOCTYPE doc SYSTEM 'urn:dtd'><doc/>";
        var runtime = Runtime(sources);
        Assert.Equal("", Read(runtime, 0).Value);
        Assert.False(sources.Calls.ContainsKey("urn:entity"));
        runtime.Documents.ClaimWrite("urn:entity");
    }

    [Fact]
    public void IgnoredDtdsDoNotEnableOrClaimExternalResolution()
    {
        var sources = new Sources(); sources.Content["urn:doc"] = "<!DOCTYPE doc SYSTEM 'urn:dtd'><doc/>";
        using var input = XmlReader.Create(new StringReader("<input/>"), new() { XmlResolver = sources, DtdProcessing = DtdProcessing.Ignore });
        var runtime = Runtime(sources, new InitialSource(input, null, XslCompiledTransform.AbsentInput));
        Assert.Equal("", Read(runtime, 0).Value);
        Assert.False(sources.Calls.ContainsKey("urn:dtd"));
        runtime.Documents.ClaimWrite("urn:dtd");
    }

    [Fact]
    public void ResolverCallbacksRunOutsideTheUriClaimLock()
    {
        var sources = new Sources(); var parent = Runtime(sources); var child = Runtime(sources); child.ShareDocuments(parent);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        sources.Open = uri => { if (uri.OriginalString == "urn:dtd") { entered.Set(); Assert.True(release.Wait(Deadline)); } return null; };
        using var deadline = new CancellationTokenSource(Deadline);
        ForkProngs.Run<object>(() => null, new Action<StreamTee<object>.Reader, CancellationToken>[] {
            (_, _) => Assert.Equal("expanded", Read(child, 0).Value),
            (_, token) => {
                Assert.True(entered.Wait(Deadline, token));
                try { Code("XTDE1500", () => parent.Documents.ClaimWrite("urn:dtd")); parent.Documents.ClaimWrite("urn:other"); }
                finally { release.Set(); }
            },
        }, 1, deadline.Token);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void CancellationDuringEntityReadsKeepsTheCancellationException(int mode)
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        var sources = new Sources { Open = uri => uri.OriginalString == "urn:entity" ? new CancelStream("expanded", cancellation) : null };
        var runtime = Runtime(sources, mode == 2 ? new InitialSource(new Uri("urn:doc"), null, XslCompiledTransform.AbsentInput) : null, mode == 2);
        CancellationToken previous = XmlQueryRuntime.Cancellation; XmlQueryRuntime.Cancellation = cancellation.Token;
        try { Assert.ThrowsAny<OperationCanceledException>(() => _ = Read(runtime, mode).Value); }
        finally { runtime.CloseStreamedSources(); XmlQueryRuntime.Cancellation = previous; }
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReaderCloseFailuresDoNotReplaceAParseFailure(bool valid)
    {
        var stream = new TrackedStream(valid ? "<doc/>" : "<doc>", failClose: true);
        var sources = new Sources { Open = _ => stream };
        var runtime = Runtime(sources);
        XdmException error = Assert.Throws<XdmException>(() => Read(runtime, 0));
        Assert.Contains("FODC0002", error.Message);
        if (valid) Assert.Contains("close-failed", error.Message);
        else Assert.DoesNotContain("close-failed", error.Message);
        Assert.True(stream.Closed);
    }

    [Fact]
    public void FailureToConstructAReaderStillClosesTheOwnedStream()
    {
        var stream = new UnreadableStream();
        var runtime = Runtime(new Sources { Open = _ => stream });
        Code("FODC0002", () => Read(runtime, 0));
        Assert.True(stream.Closed);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task AsyncEntityOpensKeepClaimsAndBothCancellationTokens(bool text, bool cancelCaptured)
    {
        using var captured = new CancellationTokenSource(Deadline); using var caller = new CancellationTokenSource(Deadline);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resources = new DocumentResources();
        object entity = text ? new StringReader("entity") : new MemoryStream(Encoding.UTF8.GetBytes("entity"));
        var resolver = resources.TrackResolver(new AsyncSource(entity, ready.Task))!;
        CancellationToken previous = XmlQueryRuntime.Cancellation; XmlQueryRuntime.Cancellation = captured.Token;
        Task<object> pending;
        try { pending = resolver.GetEntityAsync(new Uri("urn:entity"), null, text ? typeof(TextReader) : typeof(Stream)); }
        finally { XmlQueryRuntime.Cancellation = previous; }
        Code("XTDE1500", () => resources.ClaimWrite("urn:entity"));
        ready.SetResult();
        using var opened = (IDisposable)await pending;
        if (cancelCaptured) captured.Cancel(); else caller.Cancel();
        if (text) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ((TextReader)opened).ReadAsync(new char[8].AsMemory(), caller.Token).AsTask());
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ((Stream)opened).ReadAsync(new byte[8].AsMemory(), caller.Token).AsTask());
        Code("XTDE1500", () => resources.ClaimWrite("urn:entity"));
    }

    [Fact]
    public async Task FailedAsyncEntityOpensReleaseTheirReservation()
    {
        var resources = new DocumentResources();
        XmlResolver resolver = resources.TrackResolver(new AsyncSource(new object(), Task.FromException(new IOException("unavailable"))))!;
        await Assert.ThrowsAsync<IOException>(() => resolver.GetEntityAsync(new Uri("urn:entity"), null, typeof(Stream)));
        resources.ClaimWrite("urn:entity");
    }

    [Fact]
    public void RetargetingAnEntityResolverDoesNotRetainAnotherTransformationsClaims()
    {
        var sources = new Sources(); var first = new DocumentResources(); var second = new DocumentResources();
        XmlResolver original = first.TrackResolver(sources)!;
        Assert.Same(original, first.TrackResolver(original));
        XmlResolver next = second.TrackResolver(original)!;
        first.ClaimWrite("urn:entity");
        using var stream = (Stream)next.GetEntity(new Uri("urn:entity"), null, typeof(Stream))!;
        Assert.Equal(1, sources.Calls["urn:entity"]);
        Code("XTDE1500", () => second.ClaimWrite("urn:entity"));
    }

    [Fact]
    public void ResolverCapabilitiesAndCredentialsAreForwarded()
    {
        var sources = new Sources(text: true); var resources = new DocumentResources();
        XmlResolver tracked = resources.TrackResolver(sources)!;
        var credentials = new NetworkCredential(); tracked.Credentials = credentials;
        Assert.Same(credentials, sources.ReceivedCredentials);
        Assert.True(tracked.SupportsType(new Uri("urn:entity"), typeof(TextReader)));
        Assert.Equal(sources.ResolveUri(null, "urn:entity"), tracked.ResolveUri(null, "urn:entity"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ParseXmlTracksEntitiesAndPreservesConflictCodes(bool written)
    {
        var sources = new Sources(); var runtime = Runtime(sources);
        var function = FunctionLibrary.Find(QName.Ns.Fn, "parse-xml", 1)!;
        IList<XPathItem> Parse() => function.Invoke(runtime.XsltFunctions.X31Context,
            new IList<XPathItem>[] { new XPathItem[] { AtomicValue.OfString(Xml) } });
        if (written)
        {
            runtime.Documents.ClaimWrite("urn:entity"); Code("XTDE1500", () => Parse());
        }
        else
        {
            Assert.Equal("expanded", Assert.Single(Parse()).Value);
            Code("XTDE1500", () => runtime.Documents.ClaimWrite("urn:entity"));
        }
        Assert.False(sources.Calls.ContainsKey("urn:doc"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LegacyUnparsedEntityMetadataReadsUseTrackedResolvers(bool written)
    {
        var sources = new Sources(); sources.Content["urn:doc"] = "<!DOCTYPE doc SYSTEM 'urn:dtd'><doc/>";
        sources.Content["urn:dtd"] = "<!NOTATION n SYSTEM 'urn:notation'><!ENTITY unparsed SYSTEM 'urn:entity' NDATA n>";
        using var input = XmlReader.Create(new StringReader("<doc/>"), new XmlReaderSettings(), "urn:doc");
        XPathNavigator root = new XPathDocument(input).CreateNavigator();
        var runtime = Runtime(sources);
        if (written)
        {
            runtime.Documents.ClaimWrite("urn:dtd"); Code("XTDE1500", () => runtime.XsltFunctions.X31UnparsedEntity("unparsed", root, false));
        }
        else
        {
            Assert.Equal("urn:entity", runtime.XsltFunctions.X31UnparsedEntity("unparsed", root, false).Value);
            Code("XTDE1500", () => runtime.Documents.ClaimWrite("urn:dtd"));
            runtime.Documents.ClaimWrite("urn:entity"); // An unparsed declaration does not read its target.
        }
        Assert.False(sources.Calls.ContainsKey("urn:entity"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SerializationParameterDocumentsParticipateInUriClaims(bool writeFirst)
    {
        const string parameterRead = "<xsl:result-document href='urn:one' parameter-document='urn:params'><one/></xsl:result-document>";
        const string parameterWrite = "<xsl:result-document href='urn:params'><two/></xsl:result-document>";
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:template match='/'>" +
            (writeFirst ? parameterWrite + parameterRead : parameterRead + parameterWrite) + "</xsl:template></xsl:stylesheet>";
        var sources = new Sources(); sources.Content["urn:params"] =
            "<output:serialization-parameters xmlns:output='http://www.w3.org/2010/xslt-xquery-serialization'><output:omit-xml-declaration value='yes'/></output:serialization-parameters>";
        var transform = new XsltCompiler { Settings = new() { EnableResultDocument = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        transform.DocumentResolver = sources;
        int opened = 0;
        transform.ResultDocumentHandler = _ => { opened++; return new StringWriter(); };
        using var input = XmlReader.Create(new StringReader("<input/>")); using var output = new StringWriter();
        Exception error = Assert.ThrowsAny<Exception>(() => transform.ApplyTemplates(input, output));
        Assert.Contains("XTDE1500", error.ToString()); Assert.Equal(1, opened);
        Assert.Equal(writeFirst ? 0 : 1, sources.Calls.GetValueOrDefault("urn:params"));
    }
}
