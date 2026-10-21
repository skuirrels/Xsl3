// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Text;
using System.Xml;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Xsl3: 2.0+ results of the xml method write empty elements as <c>&lt;a/&gt;</c>, as Saxon does, where .NET's
    /// writers write <c>&lt;a /&gt;</c>. The writers can't be told, so their output passes through a filter that drops the
    /// space before <c>/&gt;</c> at the end of a start tag. It follows the markup (tags, attribute values in quotes,
    /// comments, CDATA sections, processing instructions, the document type declaration), so text is never changed;
    /// runs of output are copied as they are. The xhtml method keeps the space (XHTML's compatibility guidelines), and
    /// so does a result whose method is chosen from its first element when that is html. 1.0 results are untouched.
    /// </summary>
    internal sealed class EmptyTagFilter
    {
        private enum State { Text, Lt, Tag, Quoted, Bang, Comment, CData, Pi, Doctype, DoctypeQuoted }

        private State _state;
        private int _quote;
        private bool _pendingSpace;
        private int _match; // characters of the current terminator ("-->", "]]>", "?>") seen so far
        private readonly bool _autoDetect;
        private bool _disabled;
        private bool _firstTagSeen;
        private readonly StringBuilder _firstName = new();

        private EmptyTagFilter(bool autoDetect) => _autoDetect = autoDetect;

        /// <summary>
        /// Whether the filter applies to a result written with these settings: the xml method, or one chosen from the
        /// first element (not xhtml, not raw).
        /// </summary>
        public static bool Applies(XmlWriterSettings settings, OutputExtras? extras) =>
            settings.OutputMethod is XmlOutputMethod.Xml or XmlOutputMethod.AutoDetect && extras?.Xhtml != true && extras?.RawMethod == null && !t_xhtml;

        // Set while a writer of the xhtml method is made through a factory that sees only its XmlWriterSettings.
        [ThreadStatic]
        private static bool t_xhtml;

        /// <summary>Marks the writers made until the result is disposed as the xhtml method's (they keep "&lt;a /&gt;").</summary>
        public static IDisposable? Xhtml(bool xhtml)
        {
            if (!xhtml || t_xhtml)
            {
                return null;
            }
            t_xhtml = true;
            return new XhtmlScope();
        }

        private sealed class XhtmlScope : IDisposable
        {
            public void Dispose() => t_xhtml = false;
        }

        /// <summary>Whether bytes in this encoding can be filtered as ASCII (the markup characters are single bytes).</summary>
        public static bool AsciiCompatible(Encoding encoding)
        {
            if (encoding.IsSingleByte || encoding.CodePage is 65001 or 932 or 936 or 949 or 950 or 51932 or 54936 or 20932)
            {
                return true;
            }
            try
            {
                // (A NamedEncoding, which names the encoding as the stylesheet does, doesn't say it is single-byte.)
                return Encoding.GetEncoding(encoding.CodePage).IsSingleByte;
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Whether Xsl3's own raw writer writes <c>&lt;a/&gt;</c> for this result: as the filter would (an encoding the
        /// filter can't read keeps <c>&lt;a /&gt;</c> on both paths).
        /// </summary>
        public static bool Compact(XmlWriterSettings settings, OutputExtras? extras) => Applies(settings, extras) && AsciiCompatible(settings.Encoding);

        /// <summary>A writer of the filtered text.</summary>
        public static TextWriter Wrap(TextWriter writer, XmlWriterSettings settings, OutputExtras? extras) =>
            Applies(settings, extras) ? new FilteringWriter(writer, new EmptyTagFilter(settings.OutputMethod == XmlOutputMethod.AutoDetect), settings.CloseOutput) : writer;

        /// <summary>A stream of the filtered bytes (the encoding being ASCII-compatible).</summary>
        public static Stream Wrap(Stream stream, XmlWriterSettings settings, OutputExtras? extras) =>
            Applies(settings, extras) && AsciiCompatible(settings.Encoding)
                ? new FilteringStream(stream, new EmptyTagFilter(settings.OutputMethod == XmlOutputMethod.AutoDetect), settings.CloseOutput)
                : stream;

        // Moves through the markup; true for a space in a tag, which is held until the next character shows whether it
        // is the one before "/>".
        private bool Step(int c)
        {
            switch (_state)
            {
                case State.Text:
                    if (c == '<') _state = State.Lt;
                    return false;
                case State.Lt:
                    _state = c switch { '!' => State.Bang, '?' => State.Pi, _ => State.Tag };
                    _match = 0;
                    if (_state == State.Tag && c != '/' && !_firstTagSeen)
                    {
                        _firstName.Append((char)c);
                    }
                    return false;
                case State.Tag:
                    if (!_firstTagSeen)
                    {
                        if (c is ' ' or '\t' or '\n' or '\r' or '>' or '/')
                        {
                            FirstElement(_firstName.ToString());
                        }
                        else
                        {
                            _firstName.Append((char)c);
                        }
                    }
                    if (c is '"' or '\'')
                    {
                        _quote = c;
                        _state = State.Quoted;
                    }
                    else if (c == '>')
                    {
                        _state = State.Text;
                    }
                    return c == ' ';
                case State.Quoted:
                    if (c == _quote) _state = State.Tag;
                    return false;
                case State.Bang:
                    _state = c switch { '-' => State.Comment, '[' => State.CData, _ => State.Doctype };
                    _match = 0;
                    return false;
                case State.Comment:
                    _match = c == '-' ? Math.Min(_match + 1, 2) : c == '>' && _match == 2 ? 3 : 0;
                    if (_match == 3) _state = State.Text;
                    return false;
                case State.CData:
                    _match = c == ']' ? Math.Min(_match + 1, 2) : c == '>' && _match == 2 ? 3 : 0;
                    if (_match == 3) _state = State.Text;
                    return false;
                case State.Pi:
                    _match = c == '?' ? 1 : c == '>' && _match == 1 ? 2 : 0;
                    if (_match == 2) _state = State.Text;
                    return false;
                case State.Doctype:
                    if (c is '"' or '\'')
                    {
                        _quote = c;
                        _state = State.DoctypeQuoted;
                    }
                    else if (c == '>')
                    {
                        _state = State.Text;
                    }
                    return false;
                default: // DoctypeQuoted
                    if (c == _quote) _state = State.Doctype;
                    return false;
            }
        }

        private static readonly System.Buffers.SearchValues<char> s_tagChars = System.Buffers.SearchValues.Create(" \"'>");
        private static readonly System.Buffers.SearchValues<byte> s_tagBytes = System.Buffers.SearchValues.Create(" \"'>"u8);

        // Where the next character that can change anything is, from i: text runs to '<', quoted values to their
        // quote, tags (once the first element's name is read) to a space, quote or '>'. Searched with IndexOf, which is
        // vectorized, rather than a character at a time.
        private int Next(ReadOnlySpan<char> s, int i)
        {
            if (_pendingSpace) return i;
            int j = _state switch
            {
                State.Text => s[i..].IndexOf('<'),
                State.Quoted or State.DoctypeQuoted => s[i..].IndexOf((char)_quote),
                State.Tag when _firstTagSeen => s[i..].IndexOfAny(s_tagChars),
                _ => 0,
            };
            return j < 0 ? s.Length : i + j;
        }

        private int Next(ReadOnlySpan<byte> s, int i)
        {
            if (_pendingSpace) return i;
            int j = _state switch
            {
                State.Text => s[i..].IndexOf((byte)'<'),
                State.Quoted or State.DoctypeQuoted => s[i..].IndexOf((byte)_quote),
                State.Tag when _firstTagSeen => s[i..].IndexOfAny(s_tagBytes),
                _ => 0,
            };
            return j < 0 ? s.Length : i + j;
        }

        // The character after a held space: the space is written unless this is the "/" of "/>".
        private bool KeepPendingSpace(int c)
        {
            if (!_pendingSpace)
            {
                return false;
            }
            _pendingSpace = false;
            return c != '/' || _disabled;
        }

        // A result whose method is chosen from its first element: html (in any namespace or none) keeps the space.
        private void FirstElement(string name)
        {
            _firstTagSeen = true;
            int colon = name.IndexOf(':');
            if (_autoDetect && string.Equals(colon < 0 ? name : name[(colon + 1)..], "html", StringComparison.OrdinalIgnoreCase))
            {
                _disabled = true;
            }
        }

        private sealed class FilteringWriter(TextWriter inner, EmptyTagFilter filter, bool ownsInner) : TextWriter
        {
            public override Encoding Encoding => inner.Encoding;

            public override IFormatProvider FormatProvider => inner.FormatProvider;

            public override string NewLine
            {
                get => inner.NewLine;
#pragma warning disable CS8765 // (TextWriter's setter allows null)
                set => inner.NewLine = value;
#pragma warning restore CS8765
            }

            public override void Write(char value) => Write(new ReadOnlySpan<char>(in value));

            public override void Write(char[] buffer, int index, int count) => Write(new ReadOnlySpan<char>(buffer, index, count));

            public override void Write(string? value) => Write(value.AsSpan());

            public override void Write(ReadOnlySpan<char> buffer)
            {
                // Runs between held spaces are copied as they are.
                int start = 0;
                for (int i = filter.Next(buffer, 0); i < buffer.Length; i = filter.Next(buffer, i + 1))
                {
                    char c = buffer[i];
                    if (filter.KeepPendingSpace(c))
                    {
                        inner.Write(' ');
                    }
                    if (filter.Step(c))
                    {
                        inner.Write(buffer[start..i]);
                        start = i + 1;
                        filter._pendingSpace = true;
                    }
                }
                inner.Write(buffer[start..]);
            }

            public override void Flush() => inner.Flush();

            protected override void Dispose(bool disposing)
            {
                if (filter._pendingSpace)
                {
                    filter._pendingSpace = false;
                    inner.Write(' ');
                }
                inner.Flush();
                // The writer it wraps is closed with it when the settings say so (XmlWriterSettings.CloseOutput).
                if (disposing && ownsInner)
                {
                    inner.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        private sealed class FilteringStream(Stream inner, EmptyTagFilter filter, bool ownsInner) : Stream
        {
            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count) => Write(new ReadOnlySpan<byte>(buffer, offset, count));

            // In the encodings this is used with, the bytes of a multi-byte character are never ASCII markup characters.
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                int start = 0;
                for (int i = filter.Next(buffer, 0); i < buffer.Length; i = filter.Next(buffer, i + 1))
                {
                    byte b = buffer[i];
                    if (filter.KeepPendingSpace(b))
                    {
                        inner.WriteByte((byte)' ');
                    }
                    if (filter.Step(b))
                    {
                        inner.Write(buffer[start..i]);
                        start = i + 1;
                        filter._pendingSpace = true;
                    }
                }
                inner.Write(buffer[start..]);
            }

            public override void WriteByte(byte value) => Write(new ReadOnlySpan<byte>(in value));

            public override void Flush() => inner.Flush();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (filter._pendingSpace)
                {
                    filter._pendingSpace = false;
                    inner.WriteByte((byte)' ');
                }
                inner.Flush();
                if (disposing && ownsInner)
                {
                    inner.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
