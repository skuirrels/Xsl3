// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Buffers.Binary;
using System.Numerics;

namespace Xsl3.Tests;

// Test-infrastructure SHA-256, avoiding a native crypto provider's startup footprint in the
// process-memory probe. Algorithm, IV and round constants: FIPS 180-4 §§4.2.2, 5.1.1, 5.3.3, 6.2.
// https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.180-4.pdf
internal sealed class ProbeSha256 : IDisposable
{
    private readonly uint[] _state = new uint[8];
    private readonly byte[] _buffer = new byte[64];
    private int _buffered;
    private ulong _length;
    private bool _disposed;

    internal ProbeSha256() => Reset();

    private void Reset()
    {
        _state[0] = 0x6a09e667; _state[1] = 0xbb67ae85; _state[2] = 0x3c6ef372; _state[3] = 0xa54ff53a;
        _state[4] = 0x510e527f; _state[5] = 0x9b05688c; _state[6] = 0x1f83d9ab; _state[7] = 0x5be0cd19;
        _length = 0; _buffered = 0; _buffer.AsSpan().Clear();
    }

    internal void AppendData(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((ulong)data.Length > (ulong.MaxValue >> 3) - _length) throw new ArgumentOutOfRangeException(nameof(data), "SHA-256 message length exceeds its 64-bit bit-count field");
        _length += (ulong)data.Length;
        if (_buffered != 0)
        {
            int take = Math.Min(64 - _buffered, data.Length);
            data[..take].CopyTo(_buffer.AsSpan(_buffered)); _buffered += take; data = data[take..];
            if (_buffered != 64) return;
            Compress(_buffer); _buffered = 0;
        }
        while (data.Length >= 64) { Compress(data[..64]); data = data[64..]; }
        data.CopyTo(_buffer); _buffered = data.Length;
    }

    internal byte[] GetHashAndReset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _buffer[_buffered++] = 0x80;
        if (_buffered > 56)
        {
            _buffer.AsSpan(_buffered).Clear(); Compress(_buffer); _buffered = 0;
        }
        _buffer.AsSpan(_buffered, 56 - _buffered).Clear();
        BinaryPrimitives.WriteUInt64BigEndian(_buffer.AsSpan(56), _length << 3);
        Compress(_buffer);
        byte[] result = new byte[32];
        for (int i = 0; i < _state.Length; i++) BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(i * 4), _state[i]);
        Reset(); return result;
    }

    private static ReadOnlySpan<uint> Constants => [
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2];

    private void Compress(ReadOnlySpan<byte> block)
    {
        Span<uint> words = stackalloc uint[64];
        for (int i = 0; i < 16; i++) words[i] = BinaryPrimitives.ReadUInt32BigEndian(block[(i * 4)..]);
        unchecked
        {
            for (int i = 16; i < 64; i++)
            {
                uint x = words[i - 15], y = words[i - 2];
                uint s0 = BitOperations.RotateRight(x, 7) ^ BitOperations.RotateRight(x, 18) ^ (x >> 3);
                uint s1 = BitOperations.RotateRight(y, 17) ^ BitOperations.RotateRight(y, 19) ^ (y >> 10);
                words[i] = words[i - 16] + s0 + words[i - 7] + s1;
            }
            uint a = _state[0], b = _state[1], c = _state[2], d = _state[3], e = _state[4], f = _state[5], g = _state[6], h = _state[7];
            ReadOnlySpan<uint> constants = Constants;
            for (int i = 0; i < 64; i++)
            {
                uint s1 = BitOperations.RotateRight(e, 6) ^ BitOperations.RotateRight(e, 11) ^ BitOperations.RotateRight(e, 25);
                uint t1 = h + s1 + ((e & f) ^ (~e & g)) + constants[i] + words[i];
                uint s0 = BitOperations.RotateRight(a, 2) ^ BitOperations.RotateRight(a, 13) ^ BitOperations.RotateRight(a, 22);
                uint t2 = s0 + ((a & b) ^ (a & c) ^ (b & c));
                h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
            }
            _state[0] += a; _state[1] += b; _state[2] += c; _state[3] += d;
            _state[4] += e; _state[5] += f; _state[6] += g; _state[7] += h;
        }
    }

    public void Dispose()
    {
        _disposed = true; _state.AsSpan().Clear(); _buffer.AsSpan().Clear(); _length = 0; _buffered = 0;
    }
}
