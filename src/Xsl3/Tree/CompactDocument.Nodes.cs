// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Runtime.CompilerServices;

namespace Xsl3.Tree;

public sealed partial class CompactDocument
{
    private NodePages? _nodePages;
    private int NodeCapacity => _nodePages?.Capacity ?? _kind.Length;

    // Full source trees append fixed-size pages instead of copying growing columns.
    // Temporary trees keep their flat arrays and the same indexing fast path.
    private sealed class NodePages
    {
        private const int Shift = 18;
        internal const int Length = 1 << Shift;
        private const int Mask = Length - 1;
        internal byte[][] Kind;
        internal int[][] Parent, End, Name, Data;
        internal int Capacity = Length;
        private int _count = 1;

        internal NodePages(CompactDocument document)
        {
            Kind = new[] { document._kind };
            Parent = new[] { document._parent };
            End = new[] { document._end };
            Name = new[] { document._name };
            Data = new[] { document._data };
        }

        internal void Add()
        {
            int remaining = Array.MaxLength - Capacity;
            if (remaining <= 0) throw new OutOfMemoryException();
            int length = Math.Min(Length, remaining);
            if (_count == Kind.Length)
            {
                int size = _count * 2;
                Array.Resize(ref Kind, size);
                Array.Resize(ref Parent, size);
                Array.Resize(ref End, size);
                Array.Resize(ref Name, size);
                Array.Resize(ref Data, size);
            }
            Kind[_count] = new byte[length];
            Parent[_count] = new int[length];
            End[_count] = new int[length];
            Name[_count] = new int[length];
            Data[_count] = new int[length];
            _count++;
            Capacity += length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref byte KindAt(int node) => ref Kind[node >> Shift][node & Mask];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref int ParentAt(int node) => ref Parent[node >> Shift][node & Mask];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref int EndAt(int node) => ref End[node >> Shift][node & Mask];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref int NameAt(int node) => ref Name[node >> Shift][node & Mask];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref int DataAt(int node) => ref Data[node >> Shift][node & Mask];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref byte KindAt(int node) => ref (_nodePages == null ? ref _kind[node] : ref _nodePages.KindAt(node));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref int ParentAt(int node) => ref (_nodePages == null ? ref _parent[node] : ref _nodePages.ParentAt(node));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref int EndAt(int node) => ref (_nodePages == null ? ref _end[node] : ref _nodePages.EndAt(node));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref int NameAt(int node) => ref (_nodePages == null ? ref _name[node] : ref _nodePages.NameAt(node));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref int DataAt(int node) => ref (_nodePages == null ? ref _data[node] : ref _nodePages.DataAt(node));
}
