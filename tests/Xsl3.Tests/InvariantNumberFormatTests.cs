// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class InvariantNumberFormatTests
{
    [Fact]
    public void IntegerAndDecimalConversionsMatchTheInvariantCultureReference()
    {
        BigInteger integer = BigInteger.Pow(10, 100) + 1;
        foreach (BigInteger value in new[] { integer, -integer, BigInteger.Zero, new BigInteger(ulong.MaxValue) })
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            Assert.True(Lexical.TryParseInteger(text, out BigInteger parsed));
            Assert.Equal(value, parsed);
            Assert.Equal(text, AtomicValue.OfInteger(value).Value);
            Assert.Equal(double.Parse(text, CultureInfo.InvariantCulture), Numeric.ToDouble(value));
            Assert.Equal(float.Parse(text, CultureInfo.InvariantCulture), Numeric.ToFloat(value));
        }

        foreach (decimal value in new[] { decimal.MinValue, decimal.MaxValue, 0m, -0.125m, 0.0000000000000000000000000001m })
        {
            string text = value.ToString("0.############################", CultureInfo.InvariantCulture);
            Assert.True(Lexical.TryParseDecimal(text, out decimal parsed));
            Assert.Equal(value, parsed);
            Assert.Equal(text, AtomicValue.OfDecimal(value).Value);
            Assert.Equal(double.Parse(text, CultureInfo.InvariantCulture), Numeric.ToDouble(value));
            Assert.Equal(float.Parse(text, CultureInfo.InvariantCulture), Numeric.ToFloat(value));
        }
    }

    [Fact]
    public void FloatingPointLexicalFormsRetainBitsAndCanonicalSpecialValues()
    {
        foreach (double value in new[] { double.MinValue, double.MaxValue, double.Epsilon, -double.Epsilon, -0d, 0d, 1e-6, 1e6, double.PositiveInfinity, double.NegativeInfinity, double.NaN })
        {
            string text = Lexical.DoubleToString(value);
            Assert.True(Lexical.TryParseDouble(text, out double parsed));
            if (double.IsNaN(value)) Assert.True(double.IsNaN(parsed));
            else Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(parsed));
        }
        foreach (float value in new[] { float.MinValue, float.MaxValue, float.Epsilon, -float.Epsilon, -0f, 0f, 1e-6f, 1e6f, float.PositiveInfinity, float.NegativeInfinity, float.NaN })
        {
            string text = Lexical.FloatToString(value);
            Assert.True(Lexical.TryParseFloat(text, out float parsed));
            if (float.IsNaN(value)) Assert.True(float.IsNaN(parsed));
            else Assert.Equal(BitConverter.SingleToInt32Bits(value), BitConverter.SingleToInt32Bits(parsed));
        }
        Assert.Equal("-0", Lexical.DoubleToString(-0d));
        Assert.Equal("INF", Lexical.FloatToString(float.PositiveInfinity));
        Assert.Equal("-INF", Lexical.DoubleToString(double.NegativeInfinity));
        Assert.Equal("NaN", Lexical.DoubleToString(double.NaN));
        Assert.Equal("1.0E6", Lexical.DoubleToString(1e6));
    }

    [Fact]
    public void NumericErrorsKeepTheirExistingLexicalAndOverflowContracts()
    {
        foreach (string text in new[] { "1,5", "١", "Infinity", "+INF", "1e", "1\0" })
        {
            Assert.False(Lexical.TryParseDouble(text, out _));
            Assert.False(Lexical.TryParseFloat(text, out _));
            Assert.Contains("FORG0001", Assert.Throws<XdmException>(() => Casting.FromString(text, AtomicTypes.Double)).Message);
        }
        Assert.False(Lexical.TryParseDecimal("1,5", out _));
        string overflow = (new BigInteger(decimal.MaxValue) + 1).ToString(CultureInfo.InvariantCulture);
        Assert.Contains("FOCA0001", Assert.Throws<XdmException>(() => Lexical.TryParseDecimal(overflow, out _)).Message);
        Assert.True(Lexical.TryParseDouble("1e999", out double infinite));
        Assert.Equal(double.PositiveInfinity, infinite);
    }

    [Fact]
    public void SharedNumericFormatIsImmutableAcrossConcurrentCultures()
    {
        Assert.True(InvariantNumberFormat.Info.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => InvariantNumberFormat.Info.NumberDecimalSeparator = ",");
        Parallel.ForEach(new[] { "en-US", "fr-FR", "ar-SA", "tr-TR" }, culture =>
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Assert.True(Lexical.TryParseDecimal("-1234.5", out decimal value));
                Assert.Equal(-1234.5m, value);
                Assert.Equal("-1234.5", Lexical.DecimalToString(value));
                Assert.Equal("18446744073709551616", AtomicValue.OfInteger(BigInteger.One << 64).Value);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        });
    }

    private const string Sheet = """
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/"><xsl:for-each select="doc/n"><xsl:value-of select="(xs:integer(@i), xs:decimal(@d), xs:double(@f), xs:float(@f))" separator="|"/></xsl:for-each></xsl:template>
        </xsl:stylesheet>
        """;
    private const string Input = "<doc><n i='18446744073709551616' d='-1234.50' f='-0'/></doc>";
    private const string Expected = "18446744073709551616|-1234.5|-0|-0";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothPublicApisKeepInvariantNumbersWithLocalizedCurrentCulture(bool debug)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            foreach (bool forward in new[] { false, true })
            {
                var settings = new XsltSettings { StreamedExecution = forward, IncludeDebugInformation = debug };
                var transformer = new XsltCompiler { Settings = settings }.Compile(new StringReader(Sheet)).CreateTransformer();
                using (var input = XmlReader.Create(new StringReader(Input)))
                    Assert.Equal(Expected, string.Concat(transformer.ApplyTemplates(input)));
                var legacy = new XslCompiledTransform(debug);
                using (var stylesheet = XmlReader.Create(new StringReader(Sheet))) legacy.Load(stylesheet, settings, null);
                using var reader = XmlReader.Create(new StringReader(Input));
                using var output = new StringWriter();
                legacy.Transform(reader, null, output);
                Assert.Equal(Expected, output.ToString());
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedStylesheetsKeepInvariantNumbersWithLocalizedCurrentCulture(bool debug)
    {
        var settings = new XsltSettings { StreamedExecution = true, IncludeDebugInformation = debug };
        using var assembly = new MemoryStream();
        var transform = new XslCompiledTransform(debug);
        using (var stylesheet = XmlReader.Create(new StringReader(Sheet)))
            transform.CompileToAssembly(stylesheet, settings, null, "InvariantNumbers", "Saved.InvariantNumbers", assembly);
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.InvariantNumbers", true)!, settings);
            using var input = XmlReader.Create(new StringReader(Input));
            using var output = new StringWriter();
            transform.Transform(input, null, output);
            Assert.Equal(Expected, output.ToString());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
#endif
}
