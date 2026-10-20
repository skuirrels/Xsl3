// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Globalization;
using Xsl3.Runtime;
using Xunit;

namespace Xsl3.Tests;

/// <summary>
/// format-number's simple path (P4) against the general one, .NET's custom numeric format, which it must match exactly.
/// </summary>
public class DecimalFormatterTests
{
    private static DecimalFormatter Formatter(string picture, string group = ",", string point = ".")
    {
        var info = (NumberFormatInfo)NumberFormatInfo.InvariantInfo.Clone();
        info.NumberGroupSeparator = group;
        info.NumberDecimalSeparator = point;
        return new DecimalFormatter(picture, new DecimalFormat(info, '#', '0', ';'));
    }

    [Fact]
    public void ShortDecimalsConvertAsTheGeneralAlgorithm()
    {
        var random = new Random(31);
        void Check(double value)
        {
            if (XPathConvert.TryShortDecimalToString(value, out string? fast))
            {
                // The general path, without the fast one: DoubleToString's digits for a value it would not shortcut.
                Assert.Equal(GeneralDoubleToString(value), fast);
            }
        }
        double[] edges = { 0.1, 0.2, 0.3, 0.1 + 0.2, 1.5, -2.25, 12345.67, 0.000001, 1e-15, 999999999999999.9, 123456789012345.6,
            0.30000000000000004, 1.0000000000000002, 5e-324, 1e15 - 1, 4.35, 2.675, 1.005, 100.01, 9007199254740.993 };
        foreach (double edge in edges)
        {
            Check(edge);
            Check(-edge);
            Check(Math.BitIncrement(edge));
            Check(Math.BitDecrement(edge));
        }
        for (int i = 0; i < 300_000; i++)
        {
            int decimals = random.Next(0, 16);
            Check(random.NextInt64(1, 1_000_000_000_000_000) / Math.Pow(10, decimals));
            Check(random.Next(1, 100000) / 100.0 + random.Next(1, 100000) / 100.0);
            Check(random.NextDouble() * Math.Pow(10, random.Next(-16, 16)));
            Check(-random.Next(1, 1000) / 8.0);
        }
    }

    /// <summary>The short-decimal digits are the shortest round-trip ones, as ToString("R") gives them (format-number uses both).</summary>
    [Fact]
    public void ShortDecimalDigitsAreTheRoundTripDigits()
    {
        var random = new Random(5);
        void Check(double value)
        {
            if (!XPathConvert.TryShortDecimal(value, out long digits, out int k))
            {
                return;
            }
            string r = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
            int exp = 0;
            int e = r.IndexOfAny(new[] { 'E', 'e' });
            if (e >= 0)
            {
                exp = int.Parse(r[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                r = r[..e];
            }
            int dot = r.IndexOf('.');
            if (dot >= 0)
            {
                exp -= r.Length - dot - 1;
                r = r.Remove(dot, 1);
            }
            Assert.Equal((long.Parse(r, CultureInfo.InvariantCulture), exp), (digits, -k));
        }
        for (int i = 0; i < 300_000; i++)
        {
            Check(random.NextInt64(1, 1_000_000_000_000_000) / Math.Pow(10, random.Next(0, 16)));
            Check(random.Next(1, 100000) / 100.0 * random.Next(1, 10));
            Check(random.Next(1, 1000) * Math.Pow(10, random.Next(-15, 12)));
            Check(random.NextDouble() * Math.Pow(10, random.Next(-12, 15)));
        }
    }

    /// <summary>XPathConvert.DoubleToString as it was, without the short-decimal path.</summary>
    private static string GeneralDoubleToString(double value)
    {
        return XPathConvert.DoubleToString(value, shortDecimals: false);
    }

    [Theory]
    [InlineData("#,##0.00", true)]
    [InlineData("0.00", true)]
    [InlineData("#.##", true)]
    [InlineData("0", true)]
    [InlineData("#,##0", true)]
    [InlineData("00.0##", true)]
    [InlineData("0,000.0", true)]
    [InlineData("#,###", true)]
    [InlineData("0%", false)]
    [InlineData("#,##0.00;(#,##0.00)", true)]
    [InlineData("$#,##0.00", false)]
    [InlineData("0.00e0", false)]
    public void SimplePictures(string picture, bool simple) => Assert.Equal(simple, Formatter(picture).IsSimple);

    [Theory]
    [InlineData("#,##0.00")]
    [InlineData("0.00")]
    [InlineData("#.##")]
    [InlineData("0")]
    [InlineData("#,##0")]
    [InlineData("00.0##")]
    [InlineData("0,000.0")]
    [InlineData("#,###.###")]
    [InlineData("#0.0000")]
    [InlineData("0.000000")]
    [InlineData("#,##0.00;(#,##0.00)")]
    public void SimplePathMatchesGeneral(string picture)
    {
        DecimalFormatter formatter = Formatter(picture);
        Assert.True(formatter.IsSimple);
        var random = new Random(picture.GetHashCode(StringComparison.Ordinal) ^ 17);
        void Check(double value)
        {
            if (value > 0 && value < 1e15)
            {
                Assert.Equal(formatter.FormatGeneral(value), formatter.Format(value));
            }
        }
        double[] edges = { 0.005, 0.015, 0.025, 0.125, 0.5, 0.95, 0.995, 0.9995, 1.005, 2.675, 9.995, 99.995, 999.995, 1e-7, 5e-5, 4.9999e-5,
            1e14, 999999999999999, 123456789012345.6, 1e15 - 0.5, 0.45, 1.45, 0.0005, 0.00049999999999999, 1e-5, 0.001, 0.01, 0.1,
            123456789012345.5, 123456789012344.5, 100000000000000.5, 999999999999999.5, 999999999999998.5, 1e-4, 9.99999999999999e-5 };
        foreach (double edge in edges)
        {
            Check(edge);
            Check(Math.BitIncrement(edge));
            Check(Math.BitDecrement(edge));
        }
        for (int i = 0; i < 40_000; i++)
        {
            // Ties and near-ties at 1 to 6 decimals, products like qty * price, and random magnitudes.
            int decimals = random.Next(1, 7);
            double tie = (random.Next(0, 1_000_000) * 10 + 5) / Math.Pow(10, decimals + 1);
            Check(tie);
            Check(Math.BitIncrement(tie));
            Check(Math.BitDecrement(tie));
            Check(random.Next(1, 10) * (random.Next(100, 100000) / 100.0));
            Check(random.NextDouble() * Math.Pow(10, random.Next(-8, 15)));
            Check(random.Next(1, 1_000_000));
            // Exact ties at the 15th significant digit: an integer in [1e14, 1e15) plus a half.
            Check(random.NextInt64(100_000_000_000_000, 999_999_999_999_999) + 0.5);
            Check(Math.Pow(2, -random.Next(1, 17)) * random.Next(1, 1000));
        }
    }
}
