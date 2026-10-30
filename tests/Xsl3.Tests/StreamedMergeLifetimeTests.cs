// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.Tree;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.Tests;

public class StreamedMergeLifetimeTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> RetentionCases()
    {
        foreach (int delivery in Deliveries())
        foreach (bool debug in new[] { false, true })
        foreach (int groups in new[] { 16, 128 })
        {
            yield return new object[] { delivery, debug, groups, 2, 1, 32, 1 };
            yield return new object[] { delivery, debug, groups, 3, 7, 256, 1 };
            yield return new object[] { delivery, debug, groups, 2, 3, 8192, 8 };
        }
    }

    private static IEnumerable<int> Deliveries()
    {
        yield return 0; // executable API
        yield return 1; // legacy API
#if NET10_0_OR_GREATER
        yield return 2; // saved legacy stylesheet
#endif
    }

    [Theory]
    [MemberData(nameof(RetentionCases))]
    public void CompletedGroupsDoNotAccumulateWithProcessedInput(int delivery, bool debug, int groups,
        int sources, int width, int payloadCharacters, int depth)
    {
        var probe = new RetentionProbe(sources, width, payloadCharacters, depth);
        using var resolver = new Sources(groups, sources, width, payloadCharacters, depth);
        Execute(Sheet(sources, depth), delivery, debug, resolver, probe);
        Assert.Equal(groups, probe.Groups);
        Assert.Equal(groups * sources * width, probe.Rows);
        probe.AssertReleased();
        Assert.All(resolver.Streams, stream => Assert.True(stream.Closed));
        Assert.All(resolver.Readers, reader => Assert.False(reader.Closed));
        output.WriteLine("Groups={0}; sources={1}; rows per source/group={2}; payload characters={3}; ancestor depth={4}; samples={5}; maximum live snapshots={6}; maximum live keys={7}; maximum snapshot logical characters={8}",
            groups, sources, width, payloadCharacters, depth, probe.Samples, probe.MaximumSnapshots, probe.MaximumKeys, probe.MaximumCharacters);
    }

    public static IEnumerable<object[]> CleanupCases()
    {
        foreach (int delivery in Deliveries())
        foreach (bool debug in new[] { false, true })
        foreach (bool lendReader in new[] { false, true })
        foreach (string ending in new[] { "complete", "stop", "action-error", "key-error", "cancel-action", "cancel-key", "source-error" })
            yield return new object[] { delivery, debug, lendReader, ending };
    }

    [Theory]
    [MemberData(nameof(CleanupCases))]
    public void CompletionEarlyExitAndFaultsReleaseUnretainedState(int delivery, bool debug, bool lendReader, string ending)
    {
        const int groups = 128, sources = 2, width = 3, payload = 256, depth = 2;
        using var cancellation = new CancellationTokenSource();
        var probe = new RetentionProbe(sources, width, payload, depth) { Ending = ending, Cancellation = cancellation };
        using var resolver = new Sources(groups, sources, width, payload, depth) { LendReader = lendReader, Fail = ending == "source-error" };
        Exception? error = Record.Exception(() => Execute(Sheet(sources, depth, ending), delivery, debug, resolver, probe, cancellation.Token));
        if (ending == "source-error")
        {
            Assert.NotNull(error);
            if (lendReader)
            {
                var dynamicError = XsltDynamicException.From(error)!;
                Assert.Equal("FODC0002", dynamicError.Code.LocalName);
                Assert.Contains(resolver.Failure.Message, dynamicError.Description, StringComparison.Ordinal);
            }
            else Assert.Contains(Exceptions(error), item => ReferenceEquals(item, resolver.Failure));
        }
        else if (ending.EndsWith("error", StringComparison.Ordinal))
            Assert.Equal(new QName("urn:merge-lifetime", "failure"), XsltDynamicException.From(Assert.IsAssignableFrom<Exception>(error))!.Code);
        else if (ending == "stop")
            Assert.Equal("XTMM9000", XsltDynamicException.From(Assert.IsAssignableFrom<Exception>(error))!.Code.LocalName);
        else if (ending.StartsWith("cancel-", StringComparison.Ordinal))
        {
            Assert.Equal(new QName("http://xsl3/err", "cancelled"), XsltDynamicException.From(Assert.IsAssignableFrom<Exception>(error))!.Code);
            Assert.Equal(cancellation.Token, Assert.Single(Exceptions(error!).OfType<OperationCanceledException>()).CancellationToken);
        }
        else Assert.Null(error);
        if (ending == "complete") Assert.Equal(groups * sources * width, probe.Rows);
        else Assert.InRange(probe.Rows, 1, ending == "source-error" ? groups * sources * width - 1 : (width + 1) * sources * 2);
        probe.AssertReleased();
        Assert.Equal(sources, resolver.Streams.Count + resolver.Readers.Count);
        Assert.All(resolver.Streams, stream => Assert.True(stream.Closed));
        Assert.All(resolver.Readers, reader => Assert.False(reader.Closed));
    }

    private static IEnumerable<Exception> Exceptions(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException) yield return current;
    }

    public static IEnumerable<object[]> SavedValuesCases()
    {
        foreach (int delivery in Deliveries())
        foreach (bool debug in new[] { false, true }) yield return new object[] { delivery, debug };
    }

    [Theory]
    [MemberData(nameof(SavedValuesCases))]
    public void ExplicitSnapshotsAndCompiledClosuresSurviveUntilTheirOwnerReleasesThem(int delivery, bool debug)
    {
        const int groups = 128, sources = 2, width = 3, payload = 256, depth = 2;
        var probe = new RetentionProbe(sources, width, payload, depth) { Ending = "retain" };
        using var resolver = new Sources(groups, sources, width, payload, depth);
        Execute(Sheet(sources, depth, "retain"), delivery, debug, resolver, probe);
        Assert.Equal(groups, probe.Groups);
        probe.VerifyAndReleaseSavedValues();
        probe.AssertReleased();
    }

    private static string Sheet(int sources, int depth, string ending = "complete")
    {
        string path = string.Concat(Enumerable.Repeat("level/", depth)) + "row";
        string declarations = string.Concat(Enumerable.Range(0, sources).Select(source =>
            $"<xsl:merge-source name='s{source}' streamable='yes' for-each-source=\"'urn:merge:{source}'\" select='{path}'><xsl:merge-key select=\"$probe(., 'row', ())\"/></xsl:merge-source>"));
        string action = "<xsl:variable name='nodes' select='current-merge-group()'/><xsl:sequence select=\"$probe($nodes, 'group', "
                + (ending == "retain" ? "if (current-merge-key() le 2) then function() { $nodes } else ()" : "()") + ")\"/>"
                + (ending == "stop" ? "<xsl:message terminate='yes'>requested-stop</xsl:message>" : "");
        string merge = $"<xsl:merge>{declarations}<xsl:merge-action>{action}</xsl:merge-action></xsl:merge>";
        return $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xsl:output method="text"/>
              <xsl:param name="probe" as="function(*)" required="yes"/>
              <xsl:template match="/">
                {{merge}}
              </xsl:template>
            </xsl:stylesheet>
            """;
    }

    private static void Execute(string sheet, int delivery, bool debug, Sources resolver, RetentionProbe probe,
        CancellationToken cancellation = default)
    {
        var settings = new XsltSettings {
            StreamedExecution = true, EnableDocumentFunction = true, IncludeDebugInformation = debug,
        };
        using var input = XmlReader.Create(new StringReader("<launch/>"));
        using var sink = new StringWriter(CultureInfo.InvariantCulture);
        using var capture = new StreamingExecutionCapture();
        var function = new NativeFunction(Enumerable.Repeat(SequenceTypeTest.AnySequence, 3).ToArray(),
            SequenceTypeTest.AnySequence, probe.Invoke, "merge retention observer");
        try
        {
            using var stage = StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet);
            if (delivery == 0)
            {
                var transformer = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
                transformer.DocumentResolver = resolver;
                transformer.CancellationToken = cancellation;
                transformer.StylesheetParameters[new QName("probe")] = XdmValue.Wrap(new XPathItem[] { function });
                transformer.ApplyTemplates(input, sink);
            }
            else
            {
                var transform = new XslCompiledTransform(debug);
                using var stylesheet = XmlReader.Create(new StringReader(sheet));
#if NET10_0_OR_GREATER
                if (delivery == 2)
                {
                    using var image = new MemoryStream();
                    transform.CompileToAssembly(stylesheet, settings, null, "MergeRetention" + Guid.NewGuid().ToString("N"), "Saved.MergeRetention", image);
                    transform.Load(Assembly.Load(image.ToArray()).GetType("Saved.MergeRetention", true)!, settings);
                }
                else
#endif
                    transform.Load(stylesheet, settings, null);
                var arguments = new XsltArgumentList { CancellationToken = cancellation };
                arguments.AddParam("probe", "", new XPathItem[] { function });
                transform.Transform(input, arguments, sink, resolver);
            }
        }
        finally { Assert.NotEqual(ReadState.Closed, input.ReadState); }
        Assert.Equal("", sink.ToString());
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        var execution = Assert.Single(capture.Snapshot().Executions);
        Assert.True(execution.BodyCompleted);
        var external = execution.Sources.Where(source => source.Kind == "SourceDocument").ToArray();
        Assert.Equal(resolver.Count, external.Length);
        Assert.All(external, source => { Assert.True(source.RequestedForward); Assert.True(source.ActualForward); });
    }

    private sealed class RetentionProbe(int sources, int width, int payloadCharacters, int depth)
    {
        private readonly List<(WeakReference<CompactDocument> Document, int Characters)> _documents = new();
        private readonly List<WeakReference<XPathItem>> _keys = new();
        private readonly ConditionalWeakTable<CompactDocument, object> _seen = new();
        private readonly List<IList<XPathItem>> _savedNodes = new();
        private readonly List<CompiledFunction> _savedClosures = new();
        private int _maximumRowCharacters;
        internal string Ending = "complete";
        internal CancellationTokenSource? Cancellation;
        internal int Rows, Groups, Samples, MaximumSnapshots, MaximumKeys;
        internal long MaximumCharacters;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal IList<XPathItem> Invoke(IList<XPathItem>[] arguments)
        {
            string phase = Assert.Single(arguments[1]).Value;
            if (phase == "row")
            {
                var node = Assert.IsType<CompactNavigator>(Assert.Single(arguments[0]));
                Assert.False(_seen.TryGetValue(node.Document, out _));
                _seen.Add(node.Document, new object());
                int characters = VerifyPayload(node);
                _maximumRowCharacters = Math.Max(_maximumRowCharacters, characters);
                _documents.Add((new(node.Document), characters));
                XPathItem key = AtomicValue.OfInteger(int.Parse(node.GetAttribute("k", ""), CultureInfo.InvariantCulture));
                _keys.Add(new(key));
                Rows++;
                if (Rows == (width + 1) * sources + 1) Finish("key");
                return new[] { key };
            }
            Assert.Equal("group", phase);
            Groups++;
            Assert.Equal(sources * width, arguments[0].Count);
            for (int source = 0; source < sources; source++)
            for (int row = 0; row < width; row++)
            {
                var node = Assert.IsType<CompactNavigator>(arguments[0][source * width + row]);
                Assert.Equal(Id(source, Groups, row), node.GetAttribute("id", ""));
                Assert.Equal(Groups.ToString(CultureInfo.InvariantCulture), node.GetAttribute("k", ""));
                VerifyPayload(node);
            }
            if (Ending == "retain" && Groups <= 2)
            {
                _savedNodes.Add(arguments[0].ToArray());
                _savedClosures.Add(Assert.IsType<CompiledFunction>(Assert.Single(arguments[2])));
            }
            if (Groups % 8 == 0) Sample();
            if (Groups == 2) Finish("action");
            return Sequences.Empty;
        }

        private void Finish(string phase)
        {
            if (Ending == phase + "-error")
                throw new XdmException("failure", "Merge lifetime test failure") { Name = new QName("urn:merge-lifetime", "failure") };
            if (Ending == "cancel-" + phase) Cancellation!.Cancel();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void VerifyAndReleaseSavedValues()
        {
            Collect();
            Assert.Equal(2, _savedNodes.Count);
            Assert.Equal(2, _savedClosures.Count);
            Assert.Equal(2 * sources * width, LiveDocuments().Count);
            for (int group = 0; group < _savedNodes.Count; group++)
            {
                var closure = _savedClosures[group];
                var nodes = closure.Owner.XsltFunctions.CallFunction(closure);
                Assert.Equal(sources * width, nodes.Count);
                for (int source = 0; source < sources; source++)
                for (int row = 0; row < width; row++)
                {
                    int index = source * width + row;
                    var saved = Assert.IsType<CompactNavigator>(_savedNodes[group][index]);
                    var invoked = Assert.IsType<CompactNavigator>(nodes[index]);
                    Assert.Equal(Id(source, group + 1, row), invoked.GetAttribute("id", ""));
                    Assert.True(saved.IsSamePosition(invoked));
                    VerifyPayload(invoked);
                }
            }
            _savedNodes.Clear();
            _savedClosures.Clear();
        }

        private int VerifyPayload(XPathNavigator node)
        {
            string id = node.GetAttribute("id", "");
            string expected = Payload(id, payloadCharacters);
            Assert.Equal(expected, node.Value);
            int characters = expected.Length + id.Length + node.GetAttribute("k", "").Length;
            XPathNavigator ancestor = node.Clone();
            for (int level = depth - 1; level >= 0; level--)
            {
                Assert.True(ancestor.MoveToParent());
                Assert.Equal("level", ancestor.LocalName);
                string label = level.ToString(CultureInfo.InvariantCulture);
                Assert.Equal(label, ancestor.GetAttribute("n", ""));
                characters += label.Length;
            }
            Assert.True(ancestor.MoveToParent());
            Assert.Equal(XPathNodeType.Root, ancestor.NodeType);
            return characters;
        }

        private void Sample()
        {
            Collect();
            var (documents, characters) = LiveDocuments();
            int keys = LiveKeys();
            // The current group and at most one lookahead per input. Keys for the current
            // group have been replaced by the public raw-key value and are no longer rows.
            int savedRows = _savedNodes.Sum(nodes => nodes.Count);
            Assert.InRange(documents, sources * width + savedRows, sources * width + sources + savedRows);
            Assert.InRange(characters, 0, (long)(sources * width + sources + savedRows) * _maximumRowCharacters);
            Assert.InRange(keys, 0, sources + 1 + savedRows);
            MaximumSnapshots = Math.Max(MaximumSnapshots, documents);
            MaximumKeys = Math.Max(MaximumKeys, keys);
            MaximumCharacters = Math.Max(MaximumCharacters, characters);
            Samples++;
        }

        internal void AssertReleased()
        {
            Collect();
            Assert.Equal(0, LiveDocuments().Count);
            Assert.Equal(0, LiveKeys());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private (int Count, long Characters) LiveDocuments()
        {
            int count = 0; long characters = 0;
            foreach (var entry in _documents)
                if (entry.Document.TryGetTarget(out _)) { count++; characters += entry.Characters; }
            return (count, characters);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private int LiveKeys() => _keys.Count(reference => reference.TryGetTarget(out _));
    }

    private static void Collect()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        GC.WaitForPendingFinalizers(); GC.Collect();
    }

    private static string Id(int source, int group, int row) => string.Create(CultureInfo.InvariantCulture, $"s{source}g{group}r{row}");
    private static string Payload(string id, int characters) => string.Concat(Enumerable.Repeat(id, (characters + id.Length - 1) / id.Length))[..characters];

    private sealed class Sources(int groups, int sources, int width, int payloadCharacters, int depth) : XmlResolver, IDisposable
    {
        internal int Count => sources;
        internal readonly List<TrackingStream> Streams = new();
        internal readonly List<TrackingReader> Readers = new();
        internal bool LendReader;
        internal bool Fail;
        internal readonly IOException Failure = new("Merge source read failure");
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri uri, string? role, Type? type)
        {
            int source = int.Parse(uri.OriginalString["urn:merge:".Length..], CultureInfo.InvariantCulture);
            Assert.InRange(source, 0, sources - 1);
            string prefix = string.Concat(Enumerable.Range(0, depth).Select(level => $"<level n='{level}'>"));
            string suffix = string.Concat(Enumerable.Repeat("</level>", depth));
            string rows = string.Concat(Enumerable.Range(1, groups).SelectMany(group => Enumerable.Range(0, width).Select(row => {
                string id = Id(source, group, row);
                return $"<row id='{id}' k='{group}'>{Payload(id, payloadCharacters)}</row>";
            })));
            if (LendReader)
            {
                var reader = new TrackingReader(XmlReader.Create(new StringReader(prefix + rows + suffix), new(), uri.OriginalString), Fail ? Failure : null);
                Readers.Add(reader);
                return reader;
            }
            var stream = new TrackingStream(System.Text.Encoding.UTF8.GetBytes(prefix + rows + suffix), Fail ? Failure : null);
            Streams.Add(stream);
            return stream;
        }
        public void Dispose()
        {
            foreach (var stream in Streams) stream.Dispose();
            foreach (var reader in Readers) reader.Dispose();
        }
    }

    private sealed class TrackingStream(byte[] bytes, IOException? failure) : Stream
    {
        private readonly MemoryStream _input = new(bytes);
        internal bool Closed;
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (failure != null)
            {
                if (Position >= Length / 2) throw failure;
                buffer = buffer[..(int)Math.Min(buffer.Length, Length / 2 - Position)];
            }
            return _input.Read(buffer);
        }
        public override bool CanRead => !Closed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _input.Length;
        public override long Position { get => _input.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { Closed = true; _input.Dispose(); } base.Dispose(disposing); }
    }

    private sealed class TrackingReader(XmlReader reader, IOException? failure) : XmlWrappingReader(reader)
    {
        internal bool Closed;
        private int _rows;
        public override bool Read()
        {
            bool read = base.Read();
            if (read && NodeType == XmlNodeType.Element && LocalName == "row" && ++_rows == 12 && failure != null) throw failure;
            return read;
        }
        public override void Close() { Closed = true; base.Close(); }
    }
}
