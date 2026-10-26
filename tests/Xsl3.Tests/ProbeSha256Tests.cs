// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Xsl3.Tests;

public class ProbeSha256Tests
{
    [Theory]
    [InlineData("", "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    [InlineData("abc", "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    [InlineData("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", "248D6A61D20638B8E5C026930C3E6039A33CE45964FF2167F6ECEDD419DB06C1")]
    public void StandardVectorsAndResetMatchTheReference(string text, string expected)
    {
        using var hash = new ProbeSha256();
        for (int i = 0; i < 2; i++)
        {
            hash.AppendData(Encoding.ASCII.GetBytes(text)); Assert.Equal(expected, Convert.ToHexString(hash.GetHashAndReset()));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(8192)]
    public void ArbitraryChunksAndEveryPaddingBoundaryMatchTheBcl(int chunk)
    {
        var random = new Random(731);
        foreach (int size in new[] { 0, 1, 55, 56, 57, 63, 64, 65, 119, 120, 127, 128, 129, 10000 })
        {
            byte[] data = new byte[size]; random.NextBytes(data);
            using var hash = new ProbeSha256();
            hash.AppendData(ReadOnlySpan<byte>.Empty);
            for (int offset = 0; offset < size; offset += chunk) hash.AppendData(data.AsSpan(offset, Math.Min(chunk, size - offset)));
            Assert.Equal(SHA256.HashData(data), hash.GetHashAndReset());
        }
    }

    [Fact]
    public void OneMillionByteVectorAndDisposalAreChecked()
    {
        var hash = new ProbeSha256(); byte[] block = new byte[1000]; block.AsSpan().Fill((byte)'a');
        for (int i = 0; i < 1000; i++) hash.AppendData(block);
        Assert.Equal("CDC76E5C9914FB9281A1C7E284D73E67F1809A48A497200E046D39CCC7112CD0", Convert.ToHexString(hash.GetHashAndReset()));
        hash.Dispose(); Assert.Throws<ObjectDisposedException>(() => hash.AppendData(block));
        Assert.Throws<ObjectDisposedException>(() => hash.GetHashAndReset());
    }
}
