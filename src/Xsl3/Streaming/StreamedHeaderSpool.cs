// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.XPath;

namespace Xsl3.Streaming;

/// <summary>
/// Source-owned scratch storage for deferred intrinsic headers and mutable set membership.
/// Offsets, rather than source-node references, connect closed records and their parents.
/// </summary>
internal sealed class StreamedHeaderSpool : IDisposable
{
    internal readonly record struct Owner(StreamedHeaderSpool Store, long Offset);
    internal readonly record struct Cell(StreamedHeaderSpool Store, long Offset);
    private const byte OwnerKind = 1, CellKind = 2;
    private const int OwnerPayload = sizeof(byte), OwnerParent = OwnerPayload + sizeof(long),
        OwnerFinal = OwnerParent + sizeof(long), OwnerSealed = OwnerFinal + sizeof(bool), OwnerAttributes = OwnerSealed + sizeof(bool),
        OwnerSize = OwnerAttributes + sizeof(int);
    private const int CellNext = sizeof(byte), CellOwner = CellNext + sizeof(long),
        CellPosition = CellOwner + sizeof(long), CellCount = CellPosition + sizeof(int),
        CellBits = CellCount + sizeof(int);
    private readonly StreamedDocument _document;
    private readonly StreamedReplayStorage _storage = new();
    private readonly BinaryWriter _indexWriter, _dataWriter;
    private readonly BinaryReader _indexReader, _dataReader;
    private readonly StreamedHeaderCodec _codec;
    private readonly StreamedXdmValueStore _values;
    private readonly Dictionary<IStreamedHeaderMetadataCodec, int> _metadataIds = new(ReferenceEqualityComparer.Instance);
    private readonly List<IStreamedHeaderMetadataCodec> _metadata = new();
    private bool _disposed;

    internal StreamedHeaderSpool(StreamedDocument document)
    {
        _document = document;
        _values = new(document.CheckHeaderSpool);
        _codec = new(document.NameTable);
        _indexWriter = new(_storage.Headers, Encoding.UTF8, leaveOpen: true);
        _indexReader = new(_storage.Headers, Encoding.UTF8, leaveOpen: true);
        _dataWriter = new(_storage.Content, Encoding.UTF8, leaveOpen: true);
        _dataReader = new(_storage.Content, Encoding.UTF8, leaveOpen: true);
    }

    internal int MemoryCapacity => _storage.MemoryCapacity;
    internal IEnumerable<string> Files
    {
        get
        {
            if (_storage.Headers.FileName is { } index) yield return index;
            if (_storage.Content.FileName is { } data) yield return data;
        }
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _document.CheckHeaderSpool();
    }

    private void Seek(long offset, int size, byte kind)
    {
        if (offset < 0 || offset > _storage.Headers.Length - size)
            throw new ArgumentOutOfRangeException(nameof(offset));
        _storage.Headers.Position = offset;
        if (_indexReader.ReadByte() != kind) throw new ArgumentException("The scratch record has the wrong kind", nameof(offset));
    }

    private void Seek(Owner owner)
    {
        if (!ReferenceEquals(owner.Store, this)) throw new ArgumentException("The header belongs to a different scratch store", nameof(owner));
        Seek(owner.Offset, OwnerSize, OwnerKind);
    }

    private void Seek(Cell cell)
    {
        if (!ReferenceEquals(cell.Store, this)) throw new ArgumentException("The membership belongs to a different scratch store", nameof(cell));
        Seek(cell.Offset, CellBits, CellKind);
    }

    private long WriteHeader(StreamedDocument.Node node)
    {
        object? observer = node.ObserverValues ?? node.Original?.ObserverValues;
        if (observer != null && observer is not IStreamedHeaderMetadata)
            throw new Xdm.XdmException("stream-header-metadata", "The source observer has no deferred-header metadata codec")
                { Name = new Xdm.QName(StreamedDocument.ErrorNamespace, "stream-header-metadata") };
        long offset = _storage.Content.Length;
        _storage.Content.Position = offset;
        _codec.Write(_dataWriter, node);
        if (observer is IStreamedHeaderMetadata snapshot)
        {
            if (!_metadataIds.TryGetValue(snapshot.Codec, out int id))
            { _metadataIds.Add(snapshot.Codec, id = _metadata.Count); _metadata.Add(snapshot.Codec); }
            _dataWriter.Write(id);
            snapshot.Write(_dataWriter, _values);
        }
        else _dataWriter.Write(-1);
        return offset;
    }

    internal Owner AddOwner(StreamedDocument.Node node, Owner? parent = null)
    {
        Check();
        if ((parent == null) != (node.Parent == null)) throw new ArgumentException("The header's parent must be retained", nameof(parent));
        if (parent is { } p)
        {
            Seek(p);
            _storage.Content.Position = _indexReader.ReadInt64();
            if (_dataReader.ReadInt64() != node.Parent!.Number)
                throw new ArgumentException("The header's parent has a different source position", nameof(parent));
        }
        long payload = WriteHeader(node), offset = _storage.Headers.Length;
        _storage.Headers.Position = offset;
        _indexWriter.Write(OwnerKind);
        _indexWriter.Write(payload);
        _indexWriter.Write(parent?.Offset ?? -1);
        _indexWriter.Write(node.Ended);
        _indexWriter.Write(false);
        _indexWriter.Write(node.Attributes.Length);
        return new(this, offset);
    }

    internal void UpdateOwner(Owner owner, StreamedDocument.Node node)
    {
        Check();
        Seek(owner);
        _storage.Content.Position = _indexReader.ReadInt64();
        if (_dataReader.ReadInt64() != node.Number || _dataReader.ReadInt32() != (int)node.Kind)
            throw new ArgumentException("The updated header must retain its source position", nameof(node));
        long payload = WriteHeader(node);
        _storage.Headers.Position = owner.Offset + OwnerPayload;
        _indexWriter.Write(payload);
        _storage.Headers.Position = owner.Offset + OwnerFinal;
        _indexWriter.Write(node.Ended);
        _storage.Headers.Position = owner.Offset + OwnerAttributes;
        _indexWriter.Write(node.Attributes.Length);
    }

    internal void SealOwner(Owner owner)
    {
        Check();
        Seek(owner);
        _storage.Headers.Position = owner.Offset + OwnerSealed;
        _indexWriter.Write(true);
    }

    internal Cell AddCell(Owner owner, int position, int leaves)
    {
        Check();
        Seek(owner);
        _storage.Headers.Position = owner.Offset + OwnerAttributes;
        if (position < -1 || position >= _indexReader.ReadInt32()) throw new ArgumentOutOfRangeException(nameof(position));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaves);
        long offset = _storage.Headers.Length;
        _storage.Headers.Position = offset;
        _indexWriter.Write(CellKind);
        _indexWriter.Write(-1L);
        _indexWriter.Write(owner.Offset);
        _indexWriter.Write(position);
        _indexWriter.Write(leaves);
        for (int i = 0; i < leaves; i++) _indexWriter.Write(false);
        return new(this, offset);
    }

    internal void Link(Cell previous, Cell next)
    {
        Check();
        Seek(previous);
        Seek(next);
        if (next.Offset <= previous.Offset) throw new ArgumentException("Scratch queue links must advance", nameof(next));
        _storage.Headers.Position = previous.Offset + CellNext;
        _indexWriter.Write(next.Offset);
    }

    internal void Select(Cell cell, int leaf)
    {
        Check();
        Seek(cell);
        _storage.Headers.Position = cell.Offset + CellCount;
        int count = _indexReader.ReadInt32();
        if (leaf < 0 || leaf >= count) throw new ArgumentOutOfRangeException(nameof(leaf));
        _storage.Headers.Position = cell.Offset + CellBits + leaf;
        _indexWriter.Write(true);
    }

    internal bool Read(Cell cell, Span<bool> leaves, out Owner owner, out int position, out Cell? next)
        => Read(cell, leaves, out owner, out position, out next, out _);

    internal bool Read(Cell cell, Span<bool> leaves, out Owner owner, out int position, out Cell? next, out bool sealedOwner)
    {
        Check();
        Seek(cell);
        long link = _indexReader.ReadInt64();
        owner = new(this, _indexReader.ReadInt64());
        position = _indexReader.ReadInt32();
        int count = _indexReader.ReadInt32();
        if (count != leaves.Length) throw new ArgumentException("The membership buffer has the wrong number of leaves", nameof(leaves));
        for (int i = 0; i < count; i++) leaves[i] = _indexReader.ReadBoolean();
        next = link < 0 ? null : new Cell(this, link);
        Seek(owner);
        _storage.Headers.Position = owner.Offset + OwnerFinal;
        bool final = _indexReader.ReadBoolean();
        sealedOwner = _indexReader.ReadBoolean();
        return final;
    }

    internal StreamedNavigator Read(Owner owner, int position = -1)
    {
        Check();
        Seek(owner);
        var chain = new Stack<(long Payload, bool Final)>();
        long current = owner.Offset;
        while (current >= 0)
        {
            Seek(current, OwnerSize, OwnerKind);
            long payload = _indexReader.ReadInt64();
            long parent = _indexReader.ReadInt64();
            chain.Push((payload, _indexReader.ReadBoolean()));
            if (parent >= current) throw new InvalidDataException("A scratch header's parent must precede it");
            current = parent;
        }
        StreamedDocument.Node? node = null;
        while (chain.TryPop(out var header))
        {
            Check();
            _storage.Content.Position = header.Payload;
            node = _codec.Read(_dataReader, node);
            int metadata = _dataReader.ReadInt32();
            if (metadata >= 0) node.ObserverValues = _metadata[metadata].Read(_dataReader, _values, _document, node.Number);
            // The existence of children is known, but their content is not stored.
            // In particular, a still-active source child must not grant downward access.
            node.Skipped = node.FirstChild >= 0 || !header.Final && !node.EmptyElement;
        }
        return new StreamedNavigator(_document, node!).AtHeaderAttribute(position);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _codec.Reset();
        _values.Clear(); _metadataIds.Clear(); _metadata.Clear();
        // Readers and writers leave both streams open; the owner closes the pair
        // even when disposing either stream fails.
        _storage.Dispose();
    }
}
