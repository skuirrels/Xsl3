// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Xml.XPath;

namespace Xsl3.Streaming;

// A value consumer can absorb nested matches before their turn in document order.
// Serialize character ranges and matching headers in a bounded cache, spilling larger
// windows to disk rather than retaining a navigable source tree.
// The only in-memory records are selected nodes still open on the reader's stack.
internal sealed partial class StreamedValueReplay : IDisposable
{
    private readonly StreamedDocument _document;
    private readonly StreamedReplayStorage _storage;
    private readonly StreamedReplayStorage.CacheStream _headers, _text;
    private readonly BinaryWriter _writer;
    private readonly BinaryReader _reader;
    private readonly Stack<(StreamedDocument.Node Node, long Offset)> _open = new();
    private readonly StreamedHeaderCodec _codec;
    private long _length, _tail = -1, _next, _count, _read, _writeEnd;
    private long _generation;
    private bool _disposed;
    internal int OpenHeaderCount => _open.Count;
    internal IEnumerable<string> Files
    {
        get
        {
            if (_headers.FileName is { } header) yield return header;
            if (_text.FileName is { } text) yield return text;
        }
    }

    internal sealed class Value(StreamedValueReplay owner, long generation, long start, long end)
    {
        internal string Read() => owner.ReadValue(generation, start, end);
    }

    internal StreamedValueReplay(StreamedDocument document)
    {
        _document = document;
        _codec = new(document.NameTable);
        _storage = new();
        _headers = _storage.Headers;
        _text = _storage.Content;
        _writer = new(_headers, Encoding.UTF8, leaveOpen: true);
        _reader = new(_headers, Encoding.UTF8, leaveOpen: true);
    }

    internal void Add(StreamedDocument.Node node)
    {
        long offset = _writeEnd;
        if (_tail >= 0) { _headers.Position = _tail; _writer.Write(offset); }
        _headers.Position = offset;
        _writer.Write(-1L); // next matching header, in document order
        _writer.Write(_length);
        _writer.Write(-1L); // end character offset, filled when this node closes
        _writer.Write(0); // final schema information
        _writer.Write(-1L); // final first-child number
        var chain = new Stack<StreamedDocument.Node>();
        for (var parent = node; parent != null; parent = parent.Parent) chain.Push(parent);
        _writer.Write(chain.Count);
        foreach (var header in chain) _codec.Write(_writer, header);
        _writeEnd = _headers.Position;
        _tail = offset;
        _count++;
        if (node.Kind is XPathNodeType.Element or XPathNodeType.Root && !node.Ended)
            _open.Push((node, offset));
        else Complete(node, offset);
    }

    internal void Observe(StreamedDocument.Node node, bool newNode)
    {
        if (newNode && _open.Count != 0 && node.Kind is XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace)
        {
            // This private, invocation-local file stores native UTF-16 code units.
            // No decoder or per-value reader buffer is needed to read a range.
            _text.Write(MemoryMarshal.AsBytes(node.Text.AsSpan()));
            _length += node.Text.Length;
        }
        while (_open.TryPeek(out var pending) && pending.Node.Ended)
        {
            _open.Pop();
            Complete(pending.Node, pending.Offset);
        }
    }

    private void Complete(StreamedDocument.Node node, long offset)
    {
        long saved = _headers.Position;
        _headers.Position = offset + 2 * sizeof(long);
        _writer.Write(_length);
        _writer.Write(_codec.Schema(node.Schema));
        _writer.Write(node.FirstChild);
        _headers.Position = saved;
    }

    internal void FinishCapture()
    {
        if (_open.Count != 0) throw StreamedDocument.PositionError();
        _text.Flush();
        _writer.Flush();
    }

    internal bool MoveNext(StreamedNavigator position)
    {
        _document.CheckValueReplay();
        if (_read == _count) return false;
        _headers.Position = _next;
        _next = _reader.ReadInt64();
        long start = _reader.ReadInt64(), end = _reader.ReadInt64();
        int schema = _reader.ReadInt32();
        long firstChild = _reader.ReadInt64();
        int count = _reader.ReadInt32();
        StreamedDocument.Node? node = null;
        for (int i = 0; i < count; i++) node = _codec.Read(_reader, node);
        node!.Schema = _codec.Info(schema);
        node.FirstChild = firstChild;
        node.ValueReplay = new(this, _generation, start, end);
        position.MoveTo(new StreamedNavigator(_document, node));
        _read++;
        return true;
    }

    internal void Reset()
    {
        if (_read != _count || _open.Count != 0) throw StreamedDocument.PositionError();
        _generation++;
        // Reuse the high-water storage. Truncating at every source record would
        // add filesystem operations proportional to document size.
        _headers.Rewind();
        _text.Rewind();
        _length = _next = _count = _read = _writeEnd = 0;
        _tail = -1;
        _codec.Reset();
    }

    private string ReadValue(long generation, long start, long end)
    {
        if (_disposed || generation != _generation || end < start) throw StreamedDocument.PositionError();
        _text.Position = checked(start * sizeof(char));
        var result = new StringBuilder();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            for (long remaining = end - start; remaining > 0;)
            {
                _document.CheckValueReplay();
                int count = (int)Math.Min(buffer.Length / sizeof(char), remaining);
                Span<byte> block = buffer.AsSpan(0, count * sizeof(char));
                _text.ReadExactly(block);
                result.Append(MemoryMarshal.Cast<byte, char>(block));
                remaining -= count;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        return result.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _open.Clear();
        try { _writer.Dispose(); }
        finally
        {
            try { _reader.Dispose(); }
            finally
            {
                try { _headers.Dispose(); }
                finally { _text.Dispose(); }
            }
        }
    }
}
