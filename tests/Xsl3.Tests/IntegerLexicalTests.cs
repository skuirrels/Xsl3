// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Globalization;
using System.Numerics;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class IntegerLexicalTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("+000")]
    [InlineData("2147483647")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("9223372036854775807")]
    [InlineData("-9223372036854775808")]
    [InlineData("9223372036854775808")]
    [InlineData("18446744073709551615")]
    [InlineData("-18446744073709551615")]
    [InlineData("18446744073709551616")]
    [InlineData("-18446744073709551616")]
    [InlineData("+0000000000000000000000000000000123")]
    public void ValidLexicalFormsRetainExactValue(string lexical)
    {
        Assert.True(Lexical.TryParseInteger(lexical, out BigInteger value));
        Assert.Equal(BigInteger.Parse(lexical, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("--1")]
    [InlineData("1+")]
    [InlineData("1.0")]
    [InlineData("1e3")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1\n")]
    [InlineData("1\0")]
    [InlineData("١")]
    [InlineData("１")]
    [InlineData("18446744073709551616x")]
    [InlineData("18446744073709551616-1")]
    public void InvalidFormsAreRejectedBeforeAndAfterMagnitudeOverflow(string lexical)
    {
        Assert.False(Lexical.TryParseInteger(lexical, out BigInteger value));
        Assert.Equal(BigInteger.Zero, value);
    }

    [Fact]
    public void ArbitraryPrecisionAndLeadingZerosMatchTheReferenceParser()
    {
        var random = new Random(3719);
        byte[] bytes = new byte[128];
        for (int i = 0; i < 512; i++)
        {
            random.NextBytes(bytes);
            BigInteger expected = new(bytes);
            string lexical = expected.ToString(CultureInfo.InvariantCulture);
            string padded = (expected.Sign < 0 ? "-" : "+") + new string('0', i % 37) + BigInteger.Abs(expected).ToString(CultureInfo.InvariantCulture);
            Assert.True(Lexical.TryParseInteger(lexical, out BigInteger actual));
            Assert.Equal(expected, actual);
            Assert.True(Lexical.TryParseInteger(padded, out actual));
            Assert.Equal(expected, actual);
            Assert.False(Lexical.TryParseInteger(padded + "x", out actual));
        }
    }

    [Fact]
    public void CastingStillAppliesWhitespaceAndDerivedIntegerFacets()
    {
        Assert.Equal("-9223372036854775808", Casting.FromString(" \t-9223372036854775808\r\n", AtomicTypes.Long).Value);
        Assert.Equal("18446744073709551615", Casting.FromString("+18446744073709551615", AtomicTypes.UnsignedLong).Value);
        Assert.Throws<XdmException>(() => Casting.FromString("9223372036854775808", AtomicTypes.Long));
        Assert.Throws<XdmException>(() => Casting.FromString("-1", AtomicTypes.UnsignedLong));
        Assert.Throws<XdmException>(() => Casting.FromString("18446744073709551616", AtomicTypes.UnsignedLong));
    }
}
