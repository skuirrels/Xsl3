// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class StreamingReadBoundaryTests
{
    public static IEnumerable<object[]> Paths() => PathsFor("identity", "source-document", "merge", "adjacent", "fork");
    public static IEnumerable<object[]> FinalTotalPaths() => PathsFor("accumulator", "iterate");

    private static IEnumerable<object[]> PathsFor(params string[] workloads)
    {
        foreach (string workload in workloads)
        foreach (string delivery in new[] { "api", "legacy", "saved" })
        foreach (long bytes in new[] { 1024L * 1024, 4L * 1024 * 1024 })
        {
#if !NET10_0_OR_GREATER
            if (delivery == "saved") continue;
#endif
            yield return new object[] { workload, delivery, bytes };
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void ForwardExecutionWritesBeforeCrossingThePhysicalReadBoundary(string workload, string delivery, long bytes) =>
        StreamingProbe.AssertReadBoundary(workload, delivery, bytes, forward: true);

    [Theory]
    [MemberData(nameof(Paths))]
    public void TreeExecutionDemonstratesThatThePhysicalReadGuardRejectsEagerInput(string workload, string delivery, long bytes) =>
        StreamingProbe.AssertReadBoundary(workload, delivery, bytes, forward: false);

    [Theory]
    [MemberData(nameof(FinalTotalPaths))]
    public void FinalTotalsRequireTheCompleteInputInEitherMode(string workload, string delivery, long bytes)
    {
        StreamingProbe.AssertReadBoundary(workload, delivery, bytes, forward: true);
        StreamingProbe.AssertReadBoundary(workload, delivery, bytes, forward: false);
    }
}

// Reuse the canonical generated corpus, stylesheet and independent output oracle.
// This file belongs only to unit tests, not the native or benchmark probe builds.
internal static partial class StreamingProbe
{
    internal static void AssertReadBoundary(string workload, string delivery, long bytes, bool forward)
    {
        bool earlyOutput = workload is not ("accumulator" or "iterate");
        var settings = new XsltSettings {
            StreamedExecution = forward, EnableDocumentFunction = true, EnableResultDocument = true,
            Timeout = TimeSpan.FromSeconds(30),
        };
        string sheet = Stylesheet(workload);
        Invocation invoke;
        if (delivery == "api")
        {
            var executable = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet));
            invoke = (input, output, resolver, secondary) => {
                var transformer = executable.CreateTransformer();
                transformer.DocumentResolver = resolver;
                transformer.ResultDocumentHandler = secondary;
                transformer.ApplyTemplates(input, output);
            };
        }
        else
        {
            var transform = new XslCompiledTransform();
            using var stylesheet = XmlReader.Create(new StringReader(sheet));
            if (delivery == "saved")
            {
#if NET10_0_OR_GREATER
                using var image = new MemoryStream();
                transform.CompileToAssembly(stylesheet, settings, null, "ReadBoundary" + Guid.NewGuid().ToString("N"), "Saved.ReadBoundary", image);
                transform.Load(System.Reflection.Assembly.Load(image.ToArray()).GetType("Saved.ReadBoundary", true)!, settings);
#else
                throw new PlatformNotSupportedException("Saving stylesheets requires .NET 10 in this test matrix");
#endif
            }
            else transform.Load(stylesheet, settings, null);
            invoke = (input, output, resolver, secondary) => {
                var previous = XmlQueryRuntime.ResultDocumentHandler;
                try
                {
                    XmlQueryRuntime.ResultDocumentHandler = (href, _) => secondary(new Uri(href));
                    transform.Transform(input, null, output, resolver);
                }
                finally { XmlQueryRuntime.ResultDocumentHandler = previous; }
            };
        }

        var boundary = new ReadBoundary(earlyOutput);
        using var inputs = new Inputs(workload, bytes);
        var resolver = new BoundaryResolver(inputs, boundary);
        using var principal = workload is not ("merge" or "source-document") ? new BoundaryInput(new GeneratedInput(bytes), boundary) : null;
        using var launch = principal == null ? new MemoryStream("<launch/>"u8.ToArray()) : null;
        using var reader = XmlReader.Create((Stream?)principal ?? launch!, new XmlReaderSettings { CloseInput = false });
        long ReadBytes() => (principal?.Input.ReadBytes ?? 0) + inputs.Streams.Values.Sum(s => s.ReadBytes);
        int caller = Environment.CurrentManagedThreadId;
        long? inputAtFirstWrite = null;
        var emission = new object();
        void ObserveWrite(int count)
        {
            if (count == 0 || boundary.Emitted) return;
            lock (emission)
                if (!boundary.Emitted)
                {
                    inputAtFirstWrite = ReadBytes();
                    boundary.Emitted = true;
                }
        }
        using var output = new HashSink(count => {
            Assert.Equal(caller, Environment.CurrentManagedThreadId);
            ObserveWrite(count);
        });
        var secondarySinks = new Dictionary<string, HashSink>(StringComparer.Ordinal);
        var writers = new List<TextWriter>();
        TextWriter Secondary(Uri uri)
        {
            Assert.Equal(caller, Environment.CurrentManagedThreadId);
            string name = uri.AbsoluteUri;
            Assert.Equal("fork", workload);
            Assert.Contains(name, new[] { FirstUri, SecondUri });
            Assert.DoesNotContain(name, secondarySinks.Keys);
            var sink = new HashSink(ObserveWrite);
            secondarySinks.Add(name, sink);
            var writer = new StreamWriter(sink, new UTF8Encoding(false), 1024, leaveOpen: true);
            writers.Add(writer);
            return writer;
        }
        using var capture = new StreamingExecutionCapture();
        Exception? error;
        try
        {
            using (StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet))
                error = Record.Exception(() => {
                    try { invoke(reader, output, resolver, Secondary); }
                    finally { foreach (TextWriter writer in writers) writer.Dispose(); }
                });
            if (!forward && earlyOutput)
            {
                Assert.NotNull(error);
                Assert.Contains(ReadBoundary.Message, error.ToString(), StringComparison.Ordinal);
                Assert.True(boundary.Blocked);
                Assert.False(boundary.Emitted);
                return;
            }

            Assert.Null(error);
            Assert.False(boundary.Blocked);
            Assert.True(boundary.Emitted);
            if (earlyOutput) Assert.InRange(inputAtFirstWrite!.Value, 1, bytes / 2);
            else Assert.Equal(bytes, inputAtFirstWrite);
            Assert.Equal(bytes, ReadBytes());
            long records = principal?.Input.Records ?? inputs.Streams.Values.Sum(s => s.Records);
            int padding = principal?.Input.Padding ?? (workload == "source-document" ? inputs.Streams[SourceUri].Padding : 0);
            var expected = Expected(workload, records, padding);
            Assert.Equal(expected["principal"], output.Finish());
            Assert.Equal(expected.Count - 1, secondarySinks.Count);
            foreach (var sink in secondarySinks) Assert.Equal(expected[sink.Key], sink.Value.Finish());
            var execution = Assert.Single(capture.Snapshot().Executions);
            Assert.Equal("Stylesheet", execution.Stage);
            Assert.True(execution.BodyCompleted);
            var sources = execution.Sources.Where(s => s.RequestedForward).ToArray();
            // Merge also streams its launch document, separately from its two external sources.
            Assert.Equal(!forward ? 0 : workload == "merge" ? 3 : 1, sources.Length);
            Assert.Equal(!forward || principal != null ? 0 : workload == "merge" ? 2 : 1,
                sources.Count(s => s.Kind == "SourceDocument"));
            Assert.All(sources, s => Assert.True(s.ActualForward));
            Assert.NotEqual(ReadState.Closed, reader.ReadState);
        }
        finally
        {
            foreach (HashSink sink in secondarySinks.Values) sink.Dispose();
        }
    }

    private sealed class ReadBoundary(bool guardBeforeOutput)
    {
        internal const string Message = "The input crossed its physical read boundary before the first output write";
        internal readonly bool GuardBeforeOutput = guardBeforeOutput;
        internal volatile bool Emitted, Blocked;
    }

    private sealed class BoundaryResolver(Inputs inputs, ReadBoundary boundary) : XmlResolver
    {
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri uri, string? role, Type? type) =>
            new BoundaryInput((GeneratedInput)inputs.GetEntity(uri, role, type), boundary);
    }

    private sealed class BoundaryInput(GeneratedInput input, ReadBoundary boundary) : Stream
    {
        internal GeneratedInput Input => input;
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty) return 0;
            if (boundary.GuardBeforeOutput && !boundary.Emitted)
            {
                long remaining = input.Length / 2 - input.ReadBytes;
                if (remaining <= 0)
                {
                    boundary.Blocked = true;
                    throw new IOException(ReadBoundary.Message);
                }
                buffer = buffer[..(int)Math.Min(buffer.Length, remaining)];
            }
            return input.Read(buffer);
        }
        public override bool CanRead => input.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => input.Length;
        public override long Position { get => input.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) input.Dispose(); base.Dispose(disposing); }
    }
}
