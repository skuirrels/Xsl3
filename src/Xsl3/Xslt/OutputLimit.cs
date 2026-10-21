// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Text;

namespace Xsl3
{
    /// <summary>
    /// Xsl3: XsltSettings.MaxOutputBytes (SPEC §21.1): the principal result's stream or text writer, counting what is
    /// written. Past the limit it throws <see cref="ExceededException"/> once (an IOException, which xsl:try doesn't
    /// catch) and drops anything written after, so disposing the writers above it doesn't hide the error.
    /// </summary>
    internal static class OutputLimit
    {
        internal sealed class ExceededException(long limit)
            : IOException($"The transformation wrote more than {limit} bytes (XsltSettings.MaxOutputBytes)")
        {
        }

        internal sealed class LimitedStream(Stream inner, long limit) : Stream
        {
            private long _written;
            private bool _exceeded;

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (_exceeded)
                {
                    return;
                }
                if (_written + buffer.Length > limit)
                {
                    _exceeded = true;
                    throw new ExceededException(limit);
                }
                _written += buffer.Length;
                inner.Write(buffer);
            }

            public override void WriteByte(byte value) => Write(new ReadOnlySpan<byte>(in value));

            public override void Flush()
            {
                if (!_exceeded)
                {
                    inner.Flush();
                }
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        internal sealed class LimitedTextWriter(TextWriter inner, long limit) : TextWriter(inner.FormatProvider)
        {
            private long _written;
            private bool _exceeded;

            public override Encoding Encoding => inner.Encoding;

            private bool Count(ReadOnlySpan<char> text)
            {
                if (_exceeded)
                {
                    return false;
                }
                long bytes = Encoding.UTF8.GetByteCount(text);
                if (_written + bytes > limit)
                {
                    _exceeded = true;
                    throw new ExceededException(limit);
                }
                _written += bytes;
                return true;
            }

            public override void Write(char value)
            {
                if (Count([value]))
                {
                    inner.Write(value);
                }
            }

            public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));

            public override void Write(ReadOnlySpan<char> buffer)
            {
                if (Count(buffer))
                {
                    inner.Write(buffer);
                }
            }

            public override void Write(string? value) => Write(value.AsSpan());

            public override void Flush()
            {
                if (!_exceeded)
                {
                    inner.Flush();
                }
            }
        }
    }
}
