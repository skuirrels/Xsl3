// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class StreamedForkFrameLifetimeTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);
    private const string BaseUri = "file:///fork/frames.xml", NamespaceUri = "urn:fork:frames";

    public static IEnumerable<object[]> Models()
    {
        foreach (var model in new[] { (1, 2, 1, 256, 1024), (7, 3, 8, 2048, 4096), (64, 4, 24, 8192, 16384) })
        foreach (int rounds in new[] { 4, 16 })
        foreach (string exit in new[] { "complete", "prong-error", "cancel" })
            yield return new object[] { model.Item1, model.Item2, model.Item3, model.Item4, model.Item5, rounds, exit };
    }

    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(8192)]
    public void GeneratedInputEndsOnceAcrossReadBoundaries(int bufferSize)
    {
        using var generated = new GeneratedRows(3, 5, 31, 19);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[bufferSize];
        int read;
        while ((read = generated.Read(buffer)) != 0) bytes.Write(buffer, 0, read);
        for (int i = 0; i < 3; i++) Assert.Equal(0, generated.Read(buffer));
        var xml = new XmlDocument(); xml.LoadXml(Encoding.UTF8.GetString(bytes.ToArray()));
        Assert.Equal(5, xml.SelectNodes("/doc/level/level/level/rows/row")!.Count);
        Assert.Equal(GeneratedRows.Text(5, 31), xml.SelectSingleNode("//row[@id='00000005']/value")!.InnerText);
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void ForkFramesAndHeaderPayloadsStayBoundedAndCollectAfterClosure(
        int capacity, int prongs, int depth, int payloadCharacters, int ancestorCharacters, int rounds, string exit)
    {
        int rows = (capacity + 1) * rounds;
        Recorder recorder = Execute(capacity, prongs, depth, payloadCharacters, ancestorCharacters, rows, exit);
        Measurement released = recorder.Measure();
        Assert.Equal(0, released.Frames);
        Assert.Equal(0, released.HeaderCharacters);
        output.WriteLine($"capacity={capacity}; prongs={prongs}; ancestors={depth + 3}; rows={rows}; exit={exit}; "
            + $"checkpoints={recorder.Checkpoints}; maxFrames={recorder.MaxFrames}; frameBound={recorder.FrameBound}; "
            + $"maxHeaderCharacters={recorder.MaxHeaderCharacters}; characterBound={recorder.CharacterBound}; releasedFrames={released.Frames}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Recorder Execute(int capacity, int prongs, int depth, int payloadCharacters, int ancestorCharacters, int rows, string exit)
    {
        // Parser/encoding buffers and the recorder's weak-reference lists are outside this
        // graph measurement. Input generation itself keeps only one record plus its envelope.
        using var generated = new GeneratedRows(depth, rows, payloadCharacters, ancestorCharacters);
        using var input = XmlReader.Create(generated, new() { CloseInput = false }, BaseUri);
        using var document = new StreamedDocument(input);
        int ancestors = depth + 3; // document, doc, level*depth, rows
        // Each published event can retain its row/value/text chain. Add one ancestor
        // chain per document, each prong's current row and original, and the source's
        // three current row frames. This bound is independent of the number of rows.
        int frameBound = (prongs + 1) * ancestors + 3 * capacity + 2 * prongs + 3;
        long intrinsicLimit = GeneratedRows.MaximumHeaderCharacters(payloadCharacters, ancestorCharacters);
        var recorder = new Recorder(prongs, frameBound, checked(frameBound * intrinsicLimit));
        document.Observe(recorder);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild());
        for (int i = 0; i < depth; i++) Assert.True(focus.MoveToFirstChild());
        Assert.True(focus.MoveToFirstChild()); Assert.Equal("rows", focus.LocalName);
        using var barrier = new Barrier(prongs);
        using var cancellation = new CancellationTokenSource(Deadline);
        using var caller = new ForkCaller(cancellation.Token);
        using (var fork = document.Fork(focus))
        {
            var bodies = new Action<StreamedNavigator, CancellationToken>[prongs];
            for (int index = 0; index < prongs; index++)
            {
                int slot = index + 1;
                bodies[index] = (view, token) =>
                {
                    for (StreamedDocument.Node? node = view.PositionNode; node != null; node = node.Parent)
                        recorder.Record(slot, node);
                    Assert.True(view.MoveToFirstChild());
                    for (int row = 1; row <= rows; row++)
                    {
                        ReadRow(view, recorder, slot, row, payloadCharacters);
                        if (row % (capacity + 1) == 0)
                        {
                            Assert.True(barrier.SignalAndWait(Deadline, token));
                            if (slot == 1) caller.Invoke(recorder.Checkpoint, token);
                            Assert.True(barrier.SignalAndWait(Deadline, token));
                        }
                        if (slot == 1 && row == rows / 2)
                        {
                            if (exit == "prong-error") throw new FrameFailure();
                            if (exit == "cancel") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                        }
                        Assert.Equal(row != rows, view.MoveToNext());
                    }
                    GC.KeepAlive(view);
                };
            }
            if (exit == "complete") fork.Run(bodies, capacity, cancellation.Token, caller);
            else if (exit == "prong-error") Assert.Throws<FrameFailure>(() => fork.Run(bodies, capacity, cancellation.Token, caller));
            else
            {
                OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => fork.Run(bodies, capacity, cancellation.Token, caller));
                Assert.Equal(cancellation.Token, error.CancellationToken);
            }
        }
        if (exit == "complete") document.CompleteSubtree(document.Root);
        int completed = exit == "complete" ? rows : rows / 2;
        for (int slot = 0; slot <= prongs; slot++)
            Assert.InRange(recorder.Observed(slot), completed * 3 + ancestors, rows * 3 + ancestors);
        Assert.Equal(completed / (capacity + 1), recorder.Checkpoints);
        document.Dispose();
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        Assert.False(generated.Closed);
        Measurement afterRun = recorder.Measure();
        // An interrupted document can still own its final current frame and that
        // frame's two row ancestors; all worker frames must have been released.
        Assert.InRange(afterRun.RowFrames, 0, exit == "complete" ? 0 : 3);
        Assert.InRange(afterRun.Frames, 1, ancestors + (exit == "complete" ? 0 : 3));
        GC.KeepAlive(document);
        return recorder;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReadRow(StreamedNavigator view, Recorder recorder, int slot, int row, int payloadCharacters)
    {
        Assert.Equal("row", view.LocalName);
        Assert.Equal(GeneratedRows.Id(row), view.GetAttribute("id", ""));
        Assert.Equal(new string('m', GeneratedRows.Width(row, payloadCharacters)), view.GetAttribute("meta", ""));
        Assert.Equal(BaseUri, view.BaseURI); Assert.Equal("en", view.XmlLang);
        Assert.Equal(NamespaceUri, view.GetNamespacesInScope(XmlNamespaceScope.All)["p"]);
        recorder.Record(slot, view.PositionNode);
        var child = (StreamedNavigator)view.Clone();
        Assert.True(child.MoveToFirstChild()); Assert.Equal("value", child.LocalName);
        recorder.Record(slot, child.PositionNode);
        Assert.True(child.MoveToFirstChild()); Assert.Equal(XPathNodeType.Text, child.NodeType);
        recorder.Record(slot, child.PositionNode);
        Assert.Equal(GeneratedRows.Text(row, payloadCharacters), child.Value);
        Assert.False(child.MoveToNext());
        Assert.True(child.MoveToParent()); Assert.Equal("value", child.LocalName);
        Assert.False(child.MoveToNext()); // finish the row before the checkpoint
    }

    private sealed class FrameFailure : Exception;

    private readonly record struct Measurement(int Frames, int RowFrames, long HeaderCharacters);

    private sealed class Recorder(int prongs, int frameBound, long characterBound) : IStreamedNodeObserver
    {
        private readonly List<WeakReference<StreamedDocument.Node>>[] _nodes = Enumerable.Range(0, prongs + 1)
            .Select(_ => new List<WeakReference<StreamedDocument.Node>>()).ToArray();
        private readonly int _thread = Environment.CurrentManagedThreadId;
        internal readonly int FrameBound = frameBound;
        internal readonly long CharacterBound = characterBound;
        internal int Checkpoints, MaxFrames;
        internal long MaxHeaderCharacters;
        internal int Observed(int slot) => _nodes[slot].Count;
        internal void Record(int slot, StreamedDocument.Node node) => _nodes[slot].Add(new(node));
        public void Start(StreamedNavigator node) => throw new InvalidOperationException("Use intrinsic frame notification");
        public void End(StreamedNavigator node) => throw new InvalidOperationException("Use intrinsic frame notification");
        public void Start(StreamedDocument document, StreamedDocument.Node node)
        {
            Assert.Equal(_thread, Environment.CurrentManagedThreadId);
            Record(0, node);
        }
        public void End(StreamedDocument document, StreamedDocument.Node node) => Assert.Equal(_thread, Environment.CurrentManagedThreadId);
        public void Dispose() { }
        internal void Checkpoint()
        {
            Assert.Equal(_thread, Environment.CurrentManagedThreadId);
            Measurement actual = Measure();
            Assert.InRange(actual.Frames, 1, FrameBound);
            Assert.InRange(actual.HeaderCharacters, 1, CharacterBound);
            MaxFrames = Math.Max(MaxFrames, actual.Frames);
            MaxHeaderCharacters = Math.Max(MaxHeaderCharacters, actual.HeaderCharacters);
            Checkpoints++;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal Measurement Measure()
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var frames = new HashSet<StreamedDocument.Node>(ReferenceEqualityComparer.Instance);
            foreach (var list in _nodes)
                foreach (var reference in list)
                    if (reference.TryGetTarget(out var node)) frames.Add(node);
            var strings = new HashSet<string>(ReferenceEqualityComparer.Instance);
            foreach (var node in frames)
            {
                Assert.Null(node.Replay); Assert.Null(node.ValueReplay);
                strings.UnionWith(new[] { node.LocalName, node.Namespace, node.Prefix, node.Text, node.BaseUri, node.Language });
                foreach (var a in node.Attributes) strings.UnionWith(new[] { a.LocalName, a.Namespace, a.Prefix, a.Value });
                foreach (var ns in node.Namespaces) { strings.Add(ns.Prefix); strings.Add(ns.Uri); }
                if (node.InScopeNamespaces is { } scope)
                    foreach (var ns in scope) { strings.Add(ns.Prefix); strings.Add(ns.Uri); }
            }
            return new(frames.Count, frames.Count(n => n.LocalName is "row" or "value" || n.Kind == XPathNodeType.Text),
                strings.Sum(s => (long)s.Length));
        }
    }

    private sealed class GeneratedRows : Stream
    {
        private readonly byte[] _prefix, _suffix;
        private readonly int _rows, _payload;
        private byte[] _segment = Array.Empty<byte>();
        private int _part = -1, _position;
        private bool _eof;
        internal bool Closed;
        internal GeneratedRows(int depth, int rows, int payload, int ancestorCharacters)
        {
            _rows = rows; _payload = payload;
            var prefix = new StringBuilder("<doc xmlns:p='" + NamespaceUri + "' xml:lang='en'>");
            for (int i = 0; i < depth; i++) prefix.Append("<level meta='").Append(new string('a', ancestorCharacters)).Append("'>");
            prefix.Append("<rows>"); _prefix = Encoding.UTF8.GetBytes(prefix.ToString());
            _suffix = Encoding.UTF8.GetBytes("</rows>" + string.Concat(Enumerable.Repeat("</level>", depth)) + "</doc>");
        }
        internal static int Width(int row, int maximum) => row % 3 == 0 ? maximum : Math.Max(1, maximum / 4);
        internal static string Id(int row) => row.ToString("D8", CultureInfo.InvariantCulture);
        internal static string Text(int row, int maximum) => new string('t', Width(row, maximum)) + Id(row);
        internal static long MaximumHeaderCharacters(int payload, int ancestor) =>
            new[] { BaseUri, NamespaceUri, "http://www.w3.org/XML/1998/namespace", "xml", "en", "p", "doc", "level", "rows", "row", "value", "id", "meta", "lang", "00000000" }
                .Sum(s => (long)s.Length) + Math.Max(payload, ancestor);
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(Closed, this);
            if (_eof) return 0;
            int copied = 0;
            while (copied < buffer.Length)
            {
                if (_position == _segment.Length)
                {
                    _part++; _position = 0;
                    if (_part == 0) _segment = _prefix;
                    else if (_part <= _rows) _segment = Encoding.UTF8.GetBytes("<row id='" + Id(_part) + "' meta='" + new string('m', Width(_part, _payload)) + "'><value>" + Text(_part, _payload) + "</value></row>");
                    else if (_part == _rows + 1) _segment = _suffix;
                    else { _eof = true; break; }
                }
                int count = Math.Min(buffer.Length - copied, _segment.Length - _position);
                _segment.AsSpan(_position, count).CopyTo(buffer[copied..]); _position += count; copied += count;
            }
            return copied;
        }
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
        public override bool CanRead => !Closed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
