// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Numerics;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class CapturedNumberTests
{
    [Fact]
    public void TypedValuesKeepThePreviousLexicalConversionIncludingFloatRoundingAndNaNBits()
    {
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true };
        XsltLibrary library = new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter()).XsltFunctions;
        XPathItem[] values = [
            AtomicValue.OfDouble(-0d), AtomicValue.OfDouble(double.Epsilon), AtomicValue.OfDouble(double.MaxValue),
            AtomicValue.OfDouble(double.PositiveInfinity), AtomicValue.OfDouble(double.NegativeInfinity),
            AtomicValue.OfDouble(BitConverter.Int64BitsToDouble(0x7FF8000000000001)),
            AtomicValue.OfInteger(BigInteger.Pow(10, 100)), AtomicValue.OfInteger(-BigInteger.Pow(10, 100)),
            AtomicValue.OfDecimal(decimal.MaxValue), AtomicValue.OfDecimal(decimal.MinValue), AtomicValue.OfDecimal(-0.125m),
            AtomicValue.OfFloat(0.1f), AtomicValue.OfFloat(-0f), AtomicValue.OfFloat(float.NaN),
            AtomicValue.OfString(" 1.25 "), AtomicValue.OfUntyped("-INF"),
            new XPathDocument(new StringReader("<n>123.5</n>")).CreateNavigator(),
        ];
        foreach (XPathItem value in values)
        {
            double expected = XmlConvert.ToDouble(value.Value);
            double actual = library.X31CapturedNumber([value]);
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
        }
        foreach (XPathItem value in new XPathItem[] { AtomicValue.OfString("invalid"), AtomicValue.OfBoolean(true) })
        {
            Exception expected = Assert.IsAssignableFrom<Exception>(Record.Exception(() => XmlConvert.ToDouble(value.Value)));
            Exception actual = Assert.IsAssignableFrom<Exception>(Record.Exception(() => library.X31CapturedNumber([value])));
            Assert.Equal(expected.GetType(), actual.GetType());
        }
    }
}
