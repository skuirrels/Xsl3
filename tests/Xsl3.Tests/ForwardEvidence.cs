// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Xsl3.Tests;

// Early-output evidence for forward execution. The input counts what the parser has read; the output records
// that count at its first non-empty write. A forward run writes the first record's result before half of the
// input is read. A tree run, the control, has to read the whole input first.
internal static class ForwardEvidence
{
    internal static string Records(int count, Func<int, string> record, string root = "doc") =>
        "<" + root + ">" + string.Concat(Enumerable.Range(1, count).Select(record)) + "</" + root + ">";

    internal static void AssertWrittenEarly(long firstWrite, long length, bool forward, string because = "")
    {
        if (forward) Assert.True(firstWrite >= 1 && firstWrite <= length / 2, $"{because} forward execution first wrote after reading {firstWrite} of {length}");
        else Assert.True(firstWrite > length / 2, $"{because} tree execution first wrote after reading only {firstWrite} of {length}");
    }

    internal sealed class CountingReader(string xml) : TextReader
    {
        private readonly StringReader _source = new(xml);
        internal int Characters;
        public override int Peek() => _source.Peek();
        public override int Read() { int value = _source.Read(); if (value >= 0) Characters++; return value; }
        public override int Read(char[] buffer, int index, int count) { int read = _source.Read(buffer, index, count); Characters += read; return read; }
        public override int Read(Span<char> buffer) { int read = _source.Read(buffer); Characters += read; return read; }
        protected override void Dispose(bool disposing) { if (disposing) _source.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class CountingStream(string xml) : Stream
    {
        private readonly MemoryStream _source = new(Encoding.UTF8.GetBytes(xml), writable: false);
        internal long Bytes;
        internal bool Closed;
        public override bool CanRead => !Closed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { int read = _source.Read(buffer, offset, count); Bytes += read; return read; }
        public override int Read(Span<byte> buffer) { int read = _source.Read(buffer); Bytes += read; return read; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { Closed = true; _source.Dispose(); } base.Dispose(disposing); }
    }

    internal sealed class EarlyWriter(Func<long> position) : StringWriter
    {
        internal EarlyWriter(CountingReader reader) : this(() => reader.Characters) { }
        internal long FirstWrite = -1;
        private void Capture() { if (FirstWrite < 0) FirstWrite = position(); }
        public override void Write(char value) { Capture(); base.Write(value); }
        public override void Write(string? value) { if (!string.IsNullOrEmpty(value)) Capture(); base.Write(value); }
        public override void Write(char[] buffer, int index, int count) { if (count != 0) Capture(); base.Write(buffer, index, count); }
        public override void Write(ReadOnlySpan<char> buffer) { if (!buffer.IsEmpty) Capture(); base.Write(buffer); }
    }

    internal sealed class EarlyStream(Func<long> position) : MemoryStream
    {
        internal long FirstWrite = -1;
        private void Capture() { if (FirstWrite < 0) FirstWrite = position(); }
        public override void Write(byte[] buffer, int offset, int count) { if (count != 0) Capture(); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { if (!buffer.IsEmpty) Capture(); base.Write(buffer); }
        public override void WriteByte(byte value) { Capture(); base.WriteByte(value); }
    }
}
