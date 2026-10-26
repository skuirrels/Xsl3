// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;

namespace Xsl3.Streaming;

internal sealed class StreamedReplayStorage : IDisposable
{
    internal readonly CacheStream Headers, Content;
    internal StreamedReplayStorage() => (Headers, Content) = (new(this), new(this));
    internal int MemoryCapacity => Headers.MemoryCapacity + Content.MemoryCapacity;
    public void Dispose()
    {
        try { Headers.Dispose(); }
        finally { Content.Dispose(); }
    }

    private static FileStream Temporary()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None,
            BufferSize = 4096, Options = FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), options);
    }

    // A per-cursor serialized cache, shared by both streams. This is independent
    // of input length; MemoryStream capacity growth is bounded by twice this limit.
    internal const int MemoryLimit = 64 * 1024;

    private void EnsureStorage(CacheStream stream, long end)
    {
        if (Headers.FileName != null) return;
        long growth = Math.Max(stream.Length, end) - stream.Length;
        if (growth <= MemoryLimit - Headers.Length - Content.Length) return;
        // Acquire both owner-private handles before switching either stream. The
        // reader/writer keep stable stream objects and preserve all recorded offsets.
        FileStream headers = Temporary();
        FileStream? text = null;
        try
        {
            text = Temporary();
            Headers.CopyToFile(headers);
            Content.CopyToFile(text);
            Headers.UseFile(headers);
            Content.UseFile(text);
        }
        catch
        {
            try { headers.Dispose(); }
            finally { text?.Dispose(); }
            throw;
        }
    }

    internal sealed class CacheStream(StreamedReplayStorage owner) : Stream
    {
        private Stream _stream = new MemoryStream();
        internal string? FileName { get; private set; }
        internal int MemoryCapacity => _stream is MemoryStream memory ? memory.Capacity : 0;
        public override bool CanRead => _stream.CanRead;
        public override bool CanSeek => _stream.CanSeek;
        public override bool CanWrite => _stream.CanWrite;
        public override long Length => _stream.Length;
        public override long Position { get => _stream.Position; set => _stream.Position = value; }
        public override void Flush() => _stream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);
        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _stream.Read(buffer);
        public override int ReadByte() => _stream.ReadByte();
        public override void SetLength(long value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            owner.EnsureStorage(this, value);
            _stream.SetLength(value);
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            owner.EnsureStorage(this, checked(Position + buffer.Length));
            _stream.Write(buffer);
        }
        public override void WriteByte(byte value)
        {
            owner.EnsureStorage(this, checked(Position + 1));
            _stream.WriteByte(value);
        }
        internal void CopyToFile(FileStream file)
        {
            var memory = (MemoryStream)_stream;
            long position = memory.Position;
            try
            {
                memory.Position = 0;
                memory.CopyTo(file);
                file.Position = position;
            }
            finally { memory.Position = position; }
        }
        internal void UseFile(FileStream file)
        {
            Stream memory = _stream;
            _stream = file;
            FileName = file.Name;
            memory.Dispose();
        }
        internal void Rewind()
        {
            // Clear the logical in-memory length without discarding its bounded
            // backing array. File streams retain their reusable high-water storage.
            if (_stream is MemoryStream) _stream.SetLength(0);
            _stream.Position = 0;
        }
        protected override void Dispose(bool disposing)
        {
            try { if (disposing) _stream.Dispose(); }
            finally { base.Dispose(disposing); }
        }
    }
}
