// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Runtime;

/// <summary>Tracks actual external-entity opens without caching parser-owned streams.</summary>
internal sealed class ResourceResolver(XmlResolver inner, DocumentResources resources) : XmlResolver
{
    internal readonly XmlResolver Inner = inner;
    internal readonly DocumentResources Resources = resources;
    public override ICredentials? Credentials { set => Inner.Credentials = value!; }
    public override Uri ResolveUri(Uri? baseUri, string? relativeUri) => Inner.ResolveUri(baseUri, relativeUri);
    public override bool SupportsType(Uri absoluteUri, Type? type) => Inner.SupportsType(absoluteUri, type);

    public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
    {
        CancellationToken cancellation = XmlQueryRuntime.Cancellation;
        cancellation.ThrowIfCancellationRequested();
        using var claim = Resources.BeginRead(absoluteUri);
        return Prepare(Inner.GetEntity(absoluteUri, role, ofObjectToReturn), claim, cancellation);
    }

    public override async Task<object> GetEntityAsync(Uri absoluteUri, string? role, Type? ofObjectToReturn)
    {
        CancellationToken cancellation = XmlQueryRuntime.Cancellation;
        cancellation.ThrowIfCancellationRequested();
        using var claim = Resources.BeginRead(absoluteUri);
        object? entity = await Inner.GetEntityAsync(absoluteUri, role, ofObjectToReturn).ConfigureAwait(false);
        return Prepare(entity, claim, cancellation)!;
    }

    private static object? Prepare(object? entity, DocumentResources.ReadClaim claim, CancellationToken cancellation)
    {
        if (entity is Stream or TextReader or XmlReader or IXPathNavigable) claim.Commit();
        if (!cancellation.CanBeCanceled) return entity;
        return entity switch
        {
            Stream stream => new CancellableStream(stream, cancellation),
            TextReader reader => new CancellableText(reader, cancellation),
            _ => entity,
        };
    }

    private sealed class CancellableStream(Stream input, CancellationToken cancellation) : Stream
    {
        public override bool CanRead => input.CanRead;
        public override bool CanSeek => input.CanSeek;
        public override bool CanWrite => input.CanWrite;
        public override bool CanTimeout => input.CanTimeout;
        public override int ReadTimeout { get => input.ReadTimeout; set => input.ReadTimeout = value; }
        public override int WriteTimeout { get => input.WriteTimeout; set => input.WriteTimeout = value; }
        public override long Length => input.Length;
        public override long Position { get => input.Position; set => input.Position = value; }
        public override void Flush() => input.Flush();
        public override long Seek(long offset, SeekOrigin origin) => input.Seek(offset, origin);
        public override void SetLength(long value) => input.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => input.Write(buffer, offset, count);
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            cancellation.ThrowIfCancellationRequested();
            int read = input.Read(buffer);
            cancellation.ThrowIfCancellationRequested();
            return read;
        }
        public override int ReadByte()
        {
            cancellation.ThrowIfCancellationRequested();
            int read = input.ReadByte();
            cancellation.ThrowIfCancellationRequested();
            return read;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            using var linked = token.CanBeCanceled && cancellation.CanBeCanceled && token != cancellation
                ? CancellationTokenSource.CreateLinkedTokenSource(token, cancellation) : null;
            CancellationToken effective = linked?.Token ?? (token.CanBeCanceled ? token : cancellation);
            effective.ThrowIfCancellationRequested();
            int read = await input.ReadAsync(buffer, effective).ConfigureAwait(false);
            effective.ThrowIfCancellationRequested();
            return read;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) input.Dispose();
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync() => input.DisposeAsync();
    }

    private sealed class CancellableText(TextReader input, CancellationToken cancellation) : TextReader
    {
        public override int Peek()
        {
            cancellation.ThrowIfCancellationRequested();
            int next = input.Peek();
            cancellation.ThrowIfCancellationRequested();
            return next;
        }
        public override int Read()
        {
            cancellation.ThrowIfCancellationRequested();
            int read = input.Read();
            cancellation.ThrowIfCancellationRequested();
            return read;
        }
        public override int Read(char[] buffer, int index, int count) => Read(buffer.AsSpan(index, count));
        public override int Read(Span<char> buffer)
        {
            cancellation.ThrowIfCancellationRequested();
            int read = input.Read(buffer);
            cancellation.ThrowIfCancellationRequested();
            return read;
        }
        public override Task<int> ReadAsync(char[] buffer, int index, int count) => ReadAsync(buffer.AsMemory(index, count)).AsTask();
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken token = default)
        {
            using var linked = token.CanBeCanceled && cancellation.CanBeCanceled && token != cancellation
                ? CancellationTokenSource.CreateLinkedTokenSource(token, cancellation) : null;
            CancellationToken effective = linked?.Token ?? (token.CanBeCanceled ? token : cancellation);
            effective.ThrowIfCancellationRequested();
            int read = await input.ReadAsync(buffer, effective).ConfigureAwait(false);
            effective.ThrowIfCancellationRequested();
            return read;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) input.Dispose();
            base.Dispose(disposing);
        }
    }
}
