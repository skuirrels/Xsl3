// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;

namespace System.Xml
{
    /// <summary>
    /// Xsl3 (P4): the raw text writers' buffers, one kept per thread for the next writer the thread makes. Every
    /// transformation to a Stream or TextWriter makes a writer, and its 6 KB byte (or 12 KB char) buffer was most of
    /// what a small transformation allocated. Only the synchronous size is kept; async writers allocate their own.
    /// </summary>
    internal static class WriterBuffers<T> where T : struct
    {
        /// <summary>The writers' synchronous buffer length: 2048 * 3, and 32 for overflow.</summary>
        public const int Length = 2048 * 3 + 32;

        [ThreadStatic]
        private static T[]? t_cached;

        /// <summary>
        /// What a closed writer points at instead of the buffer it gave back: writes after Close (which go nowhere,
        /// the writer writes to null) land here, never in a buffer another writer is using. Nothing reads it.
        /// </summary>
        public static readonly T[] Closed = new T[Length];

        public static T[] Rent()
        {
            T[]? buffer = t_cached;
            if (buffer == null)
            {
                return new T[Length];
            }
            t_cached = null;
            // As a new buffer starts: the writers read the "previous character" at the start before writing it.
            Array.Clear(buffer, 0, 4);
            return buffer;
        }

        public static void Return(T[] buffer)
        {
            if (buffer.Length == Length && !ReferenceEquals(buffer, Closed))
            {
                t_cached = buffer;
            }
        }
    }
}
