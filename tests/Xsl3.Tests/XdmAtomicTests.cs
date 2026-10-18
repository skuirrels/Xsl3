// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

/// <summary>The XDM atomic layer. Most cases are worked examples from F&amp;O 3.1.</summary>
public class XdmAtomicTests
{
    private const int UtcImplicit = 0;

    private static AtomicType T(string localName) => AtomicTypes.Find(QName.Ns.Xs, localName) ?? throw new System.ArgumentException(localName);

    private static AtomicValue V(string type, string lexical) => Casting.FromString(lexical, T(type));

    private static string Cast(string fromType, string lexical, string toType) => Casting.Cast(V(fromType, lexical), T(toType)).Value;

    private static string ErrorOf(System.Action action) => Assert.Throws<XdmException>(action).Code;

    // ------------------------------------------------------------ lexical forms and canonical strings

    [Theory]
    [InlineData("double", "1e6", "1.0E6")]
    [InlineData("double", "999999", "999999")]
    [InlineData("double", "0.000001", "0.000001")]
    [InlineData("double", "1e-7", "1.0E-7")]
    [InlineData("double", "1.5", "1.5")]
    [InlineData("double", "-0", "-0")]
    [InlineData("double", "0.0", "0")]
    [InlineData("double", "NaN", "NaN")]
    [InlineData("double", "-INF", "-INF")]
    [InlineData("double", "12345678.9", "1.23456789E7")]
    [InlineData("double", "0.1", "0.1")]
    [InlineData("double", "1.0E-10", "1.0E-10")]
    [InlineData("double", "123456.789e0", "123456.789")]
    [InlineData("float", "1.1", "1.1")]
    [InlineData("float", "1e10", "1.0E10")]
    [InlineData("decimal", "1.50", "1.5")]
    [InlineData("decimal", "+.5", "0.5")]
    [InlineData("decimal", "007.000", "7")]
    [InlineData("decimal", "-12.340", "-12.34")]
    [InlineData("integer", "  +0012 ", "12")]
    [InlineData("integer", "-0", "0")]
    [InlineData("integer", "123456789012345678901234567890", "123456789012345678901234567890")]
    [InlineData("boolean", "1", "true")]
    [InlineData("boolean", " false ", "false")]
    [InlineData("dateTime", "2002-10-10T12:00:00-05:00", "2002-10-10T12:00:00-05:00")]
    [InlineData("dateTime", "2002-10-10T12:00:00.500Z", "2002-10-10T12:00:00.5Z")]
    [InlineData("dateTime", "2002-10-10T12:00:00+00:00", "2002-10-10T12:00:00Z")]
    [InlineData("dateTime", "2002-10-10T24:00:00", "2002-10-11T00:00:00")]
    [InlineData("dateTime", "1999-12-31T24:00:00Z", "2000-01-01T00:00:00Z")]
    [InlineData("date", "2000-02-29", "2000-02-29")]
    [InlineData("date", "-0001-01-01", "-0001-01-01")]
    [InlineData("date", "12345-06-07", "12345-06-07")]
    [InlineData("time", "24:00:00", "00:00:00")]
    [InlineData("time", "13:20:00.000100+14:00", "13:20:00.0001+14:00")]
    [InlineData("gYearMonth", "1999-05", "1999-05")]
    [InlineData("gMonthDay", "--02-29", "--02-29")]
    [InlineData("gDay", "---31Z", "---31Z")]
    [InlineData("gMonth", "--12", "--12")]
    [InlineData("duration", "P1Y2M3DT10H30M", "P1Y2M3DT10H30M")]
    [InlineData("duration", "PT36H", "P1DT12H")]
    [InlineData("duration", "P0D", "PT0S")]
    [InlineData("duration", "-P1DT0.5S", "-P1DT0.5S")]
    [InlineData("yearMonthDuration", "P0Y", "P0M")]
    [InlineData("yearMonthDuration", "P13M", "P1Y1M")]
    [InlineData("dayTimeDuration", "PT3600S", "PT1H")]
    [InlineData("dayTimeDuration", "PT0.000S", "PT0S")]
    [InlineData("hexBinary", "0fb7", "0FB7")]
    [InlineData("base64Binary", "AQID", "AQID")]
    [InlineData("base64Binary", "AQ ==", "AQ==")]
    [InlineData("token", "  a   b  ", "a b")]
    [InlineData("normalizedString", "a\tb\nc", "a b c")]
    [InlineData("language", "en-GB", "en-GB")]
    [InlineData("anyURI", " http://x/ y ", "http://x/ y")]
    public void CanonicalForm(string type, string lexical, string expected)
    {
        Assert.Equal(expected, V(type, lexical).Value);
    }

    [Theory]
    [InlineData("integer", "1.0")]
    [InlineData("integer", "")]
    [InlineData("integer", "1 2")]
    [InlineData("decimal", "1e3")]
    [InlineData("decimal", ".")]
    [InlineData("double", "INFINITY")]
    [InlineData("double", "1e")]
    [InlineData("boolean", "TRUE")]
    [InlineData("date", "2000-02-30")]
    [InlineData("date", "1999-02-29")]
    [InlineData("date", "0000-01-01")]
    [InlineData("date", "02000-01-01")]
    [InlineData("date", "2000-1-01")]
    [InlineData("dateTime", "2000-01-01T24:00:01")]
    [InlineData("dateTime", "2000-01-01T12:00:00+14:01")]
    [InlineData("dateTime", "2000-01-01T12:60:00")]
    [InlineData("dateTime", "2000-01-01")]
    [InlineData("dateTimeStamp", "2000-01-01T00:00:00")]
    [InlineData("time", "12:00")]
    [InlineData("gMonthDay", "--02-30")]
    [InlineData("duration", "P")]
    [InlineData("duration", "PT")]
    [InlineData("duration", "P1D2Y")]
    [InlineData("duration", "P1.5Y")]
    [InlineData("duration", "P-1Y")]
    [InlineData("yearMonthDuration", "P1D")]
    [InlineData("yearMonthDuration", "P1YT1H")]
    [InlineData("dayTimeDuration", "P1Y")]
    [InlineData("hexBinary", "abc")]
    [InlineData("base64Binary", "AQI")]
    [InlineData("base64Binary", "AR==")]
    [InlineData("byte", "128")]
    [InlineData("unsignedInt", "-1")]
    [InlineData("positiveInteger", "0")]
    [InlineData("NCName", "a:b")]
    [InlineData("NCName", "1abc")]
    [InlineData("Name", "")]
    [InlineData("language", "toolongsubtag-en")]
    [InlineData("NMTOKEN", "a b")]
    public void InvalidLexicalFormIsFORG0001(string type, string lexical)
    {
        Assert.Equal("FORG0001", ErrorOf(() => V(type, lexical)));
    }

    [Fact]
    public void TypesAreTheRegisteredSingletons()
    {
        Assert.Same(AtomicTypes.Byte, V("byte", "1").Type);
        Assert.True(AtomicTypes.Byte.DerivesFrom(AtomicTypes.Integer));
        Assert.True(AtomicTypes.ID.DerivesFrom(AtomicTypes.String));
        Assert.False(AtomicTypes.Integer.DerivesFrom(AtomicTypes.Double));
        Assert.Equal(46, AtomicTypes.All.Count);
    }

    // ------------------------------------------------------------ casting

    [Theory]
    [InlineData("double", "2.9", "integer", "2")]
    [InlineData("double", "-2.9", "integer", "-2")]
    [InlineData("double", "1e20", "integer", "100000000000000000000")]
    [InlineData("double", "0.1", "decimal", "0.1")]
    [InlineData("decimal", "-3.7", "integer", "-3")]
    [InlineData("integer", "1", "boolean", "true")]
    [InlineData("double", "NaN", "boolean", "false")]
    [InlineData("boolean", "true", "double", "1")]
    [InlineData("integer", "127", "byte", "127")]
    [InlineData("integer", "12", "token", "12")]
    [InlineData("float", "1.1", "double", "1.100000023841858")]
    [InlineData("dateTime", "2000-01-15T10:20:30.5-05:00", "date", "2000-01-15-05:00")]
    [InlineData("dateTime", "2000-01-15T10:20:30.5-05:00", "time", "10:20:30.5-05:00")]
    [InlineData("dateTime", "2000-01-15T10:20:30Z", "gYearMonth", "2000-01Z")]
    [InlineData("dateTime", "2000-01-15T10:20:30Z", "gMonthDay", "--01-15Z")]
    [InlineData("date", "2000-01-15", "dateTime", "2000-01-15T00:00:00")]
    [InlineData("date", "2000-01-15", "gDay", "---15")]
    [InlineData("duration", "P1Y2M3DT4H", "yearMonthDuration", "P1Y2M")]
    [InlineData("duration", "P1Y2M3DT4H", "dayTimeDuration", "P3DT4H")]
    [InlineData("yearMonthDuration", "P1Y", "duration", "P1Y")]
    [InlineData("hexBinary", "0102FF", "base64Binary", "AQL/")]
    [InlineData("base64Binary", "AQL/", "hexBinary", "0102FF")]
    [InlineData("anyURI", "http://x", "string", "http://x")]
    [InlineData("untypedAtomic", " 42 ", "integer", "42")]
    [InlineData("string", "12", "untypedAtomic", "12")]
    public void Casts(string from, string lexical, string to, string expected)
    {
        Assert.Equal(expected, Cast(from, lexical, to));
    }

    [Theory]
    [InlineData("time", "10:00:00", "date", "XPTY0004")]
    [InlineData("anyURI", "http://x", "boolean", "XPTY0004")]
    [InlineData("boolean", "true", "dateTime", "XPTY0004")]
    [InlineData("gYear", "2000", "date", "XPTY0004")]
    [InlineData("double", "NaN", "integer", "FOCA0002")]
    [InlineData("double", "INF", "decimal", "FOCA0002")]
    [InlineData("double", "1e30", "decimal", "FOCA0001")]
    [InlineData("integer", "300", "byte", "FORG0001")]
    [InlineData("double", "1e3", "NCName", "FORG0001")]
    [InlineData("dateTime", "2000-01-01T00:00:00", "dateTimeStamp", "FORG0001")]
    [InlineData("string", "x", "NOTATION", "XPST0080")]
    [InlineData("string", "x", "anyAtomicType", "XPST0080")]
    public void CastErrors(string from, string lexical, string to, string code)
    {
        Assert.Equal(code, ErrorOf(() => Cast(from, lexical, to)));
    }

    [Fact]
    public void Castable()
    {
        Assert.True(Casting.IsCastable(V("string", "12"), AtomicTypes.Integer));
        Assert.False(Casting.IsCastable(V("string", "x"), AtomicTypes.Integer));
        Assert.False(Casting.IsCastable(V("time", "10:00:00"), AtomicTypes.Date));
    }

    [Fact]
    public void QNameCastResolvesPrefixes()
    {
        var ns = new XmlNamespaceManager(new NameTable());
        ns.AddNamespace("p", "urn:p");
        var q = (QNameValue)Casting.FromString("p:local", AtomicTypes.QName, ns);
        Assert.Equal(new QName("urn:p", "local"), q.QName);
        Assert.Equal("p:local", q.Value);
        Assert.Equal("FONS0004", ErrorOf(() => Casting.FromString("zz:local", AtomicTypes.QName, ns)));
        Assert.Equal("FORG0001", ErrorOf(() => Casting.FromString("1:local", AtomicTypes.QName, ns)));
    }

    // ------------------------------------------------------------ comparison

    private static bool Cmp(AtomicValue a, string op, AtomicValue b) => Comparison.Compare(a, b, op switch
    {
        "eq" => ValueComparisonOp.Eq, "ne" => ValueComparisonOp.Ne, "lt" => ValueComparisonOp.Lt,
        "le" => ValueComparisonOp.Le, "gt" => ValueComparisonOp.Gt, _ => ValueComparisonOp.Ge,
    }, UtcImplicit);

    [Theory]
    [InlineData("integer", "1", "eq", "decimal", "1.0", true)]
    [InlineData("integer", "1", "eq", "double", "1", true)]
    [InlineData("decimal", "0.1", "eq", "double", "0.1", true)] // the decimal is promoted to the same double
    [InlineData("double", "NaN", "eq", "double", "NaN", false)]
    [InlineData("double", "NaN", "ne", "double", "NaN", true)]
    [InlineData("double", "NaN", "lt", "double", "1", false)]
    [InlineData("float", "-0", "eq", "double", "0", true)]
    [InlineData("integer", "99999999999999999999999999999999", "gt", "decimal", "1.5", true)]
    [InlineData("string", "a", "lt", "string", "b", true)]
    [InlineData("string", "abc", "lt", "string", "abcd", true)]
    [InlineData("untypedAtomic", "x", "eq", "string", "x", true)]
    [InlineData("anyURI", "http://x", "eq", "string", "http://x", true)]
    [InlineData("boolean", "false", "lt", "boolean", "true", true)]
    [InlineData("dateTime", "2002-04-02T12:00:00-01:00", "eq", "dateTime", "2002-04-02T17:00:00+04:00", true)]
    [InlineData("dateTime", "2002-04-02T12:00:00", "eq", "dateTime", "2002-04-02T23:00:00+06:00", false)]
    [InlineData("dateTime", "2002-04-02T12:00:00", "lt", "dateTime", "2002-04-02T12:00:01Z", true)]
    [InlineData("date", "2004-12-25Z", "eq", "date", "2004-12-25+07:00", false)]
    [InlineData("time", "21:30:00+10:30", "eq", "time", "06:00:00-05:00", true)]
    [InlineData("gYear", "2005-12:00", "eq", "gYear", "2005+12:00", false)]
    [InlineData("gDay", "---12-05:00", "eq", "gDay", "---12+05:00", false)]
    [InlineData("yearMonthDuration", "P1Y", "eq", "yearMonthDuration", "P12M", true)]
    [InlineData("duration", "P1Y", "eq", "yearMonthDuration", "P12M", true)]
    [InlineData("dayTimeDuration", "PT24H", "eq", "dayTimeDuration", "P1D", true)]
    [InlineData("yearMonthDuration", "P0M", "eq", "dayTimeDuration", "PT0S", true)]
    [InlineData("dayTimeDuration", "PT1H", "lt", "dayTimeDuration", "PT61M", true)]
    [InlineData("hexBinary", "00FF", "lt", "hexBinary", "0100", true)]
    public void ValueComparisons(string ta, string a, string op, string tb, string b, bool expected)
    {
        Assert.Equal(expected, Cmp(V(ta, a), op, V(tb, b)));
    }

    [Fact]
    public void CodepointCollationOrdersSupplementaryCharactersAboveBmp()
    {
        Assert.True(Cmp(AtomicValue.OfString("￿"), "lt", AtomicValue.OfString("\U00010000")));
        Assert.True(string.CompareOrdinal("￿", "\U00010000") > 0); // which plain UTF-16 order gets wrong
    }

    [Theory]
    [InlineData("gYear", "2000", "lt", "gYear", "2001")]
    [InlineData("duration", "P1Y", "lt", "duration", "P2Y")]
    [InlineData("QName", "a", "lt", "QName", "b")]
    [InlineData("integer", "1", "eq", "string", "1")]
    [InlineData("date", "2000-01-01", "eq", "dateTime", "2000-01-01T00:00:00")]
    [InlineData("yearMonthDuration", "P1Y", "lt", "dayTimeDuration", "P1D")]
    public void IncomparableIsXPTY0004(string ta, string a, string op, string tb, string b)
    {
        Assert.Equal("XPTY0004", ErrorOf(() => Cmp(V(ta, a), op, V(tb, b))));
    }

    [Fact]
    public void QNamesCompareByExpandedName()
    {
        var a = new QNameValue(AtomicTypes.QName, new QName("urn:x", "n", "a"));
        var b = new QNameValue(AtomicTypes.QName, new QName("urn:x", "n", "b"));
        Assert.True(Cmp(a, "eq", b));
    }

    // ------------------------------------------------------------ arithmetic

    private static AtomicValue Op(string ta, string a, string op, string tb, string b) =>
        Xdm.Arithmetic.Apply(op switch
        {
            "+" => ArithmeticOp.Add, "-" => ArithmeticOp.Subtract, "*" => ArithmeticOp.Multiply,
            "div" => ArithmeticOp.Divide, "idiv" => ArithmeticOp.IntegerDivide, _ => ArithmeticOp.Modulo,
        }, V(ta, a), V(tb, b), UtcImplicit);

    [Theory]
    [InlineData("integer", "1", "+", "integer", "2", "integer", "3")]
    [InlineData("integer", "5", "div", "integer", "2", "decimal", "2.5")]
    [InlineData("integer", "6", "div", "integer", "2", "decimal", "3")]
    [InlineData("integer", "5", "idiv", "integer", "2", "integer", "2")]
    [InlineData("integer", "-5", "idiv", "integer", "2", "integer", "-2")]
    [InlineData("integer", "-5", "mod", "integer", "2", "integer", "-1")]
    [InlineData("integer", "5", "mod", "integer", "-2", "integer", "1")]
    [InlineData("integer", "99999999999", "*", "integer", "99999999999", "integer", "9999999999800000000001")]
    [InlineData("decimal", "10.5", "mod", "integer", "3", "decimal", "1.5")]
    [InlineData("decimal", "1.1", "+", "integer", "1", "decimal", "2.1")]
    [InlineData("integer", "1", "+", "double", "1", "double", "2")]
    [InlineData("float", "1", "+", "decimal", "0.5", "float", "1.5")]
    [InlineData("double", "1", "div", "integer", "0", "double", "INF")]
    [InlineData("double", "-1", "div", "integer", "0", "double", "-INF")]
    [InlineData("double", "10", "idiv", "double", "3", "integer", "3")]
    [InlineData("double", "5", "idiv", "double", "INF", "integer", "0")]
    [InlineData("double", "-5.5", "mod", "integer", "2", "double", "-1.5")]
    [InlineData("untypedAtomic", "2", "*", "integer", "3", "double", "6")]
    // F&O 3.1 examples for durations and dates
    [InlineData("yearMonthDuration", "P2Y11M", "+", "yearMonthDuration", "P3Y3M", "yearMonthDuration", "P6Y2M")]
    [InlineData("yearMonthDuration", "P2Y11M", "*", "decimal", "2.3", "yearMonthDuration", "P6Y9M")]
    [InlineData("yearMonthDuration", "P3Y4M", "div", "decimal", "1.5", "yearMonthDuration", "P2Y3M")]
    [InlineData("yearMonthDuration", "P3Y4M", "div", "yearMonthDuration", "-P1Y4M", "decimal", "-2.5")]
    [InlineData("dayTimeDuration", "P2DT12H5M", "+", "dayTimeDuration", "P5DT12H", "dayTimeDuration", "P8DT5M")]
    [InlineData("dayTimeDuration", "PT2H10M", "*", "decimal", "2.1", "dayTimeDuration", "PT4H33M")]
    [InlineData("dayTimeDuration", "P1DT2H30M10.5S", "div", "decimal", "1.5", "dayTimeDuration", "PT17H40M7S")]
    [InlineData("dayTimeDuration", "P2DT53M11S", "div", "dayTimeDuration", "P1DT10H", "decimal", "1.437834967320261437908496732")] // 175991 s / 122400 s
    [InlineData("dateTime", "2000-10-30T11:12:00", "+", "yearMonthDuration", "P1Y2M", "dateTime", "2001-12-30T11:12:00")]
    [InlineData("dateTime", "2000-10-30T11:12:00", "+", "dayTimeDuration", "P3DT1H15M", "dateTime", "2000-11-02T12:27:00")]
    [InlineData("dateTime", "2000-10-30T11:12:00", "-", "yearMonthDuration", "P1Y2M", "dateTime", "1999-08-30T11:12:00")]
    [InlineData("dateTime", "2000-10-30T11:12:00", "-", "dayTimeDuration", "P3DT1H15M", "dateTime", "2000-10-27T09:57:00")]
    [InlineData("date", "2000-10-30", "+", "yearMonthDuration", "P1Y2M", "date", "2001-12-30")]
    [InlineData("date", "2004-10-30Z", "+", "dayTimeDuration", "P2DT2H30M0S", "date", "2004-11-01Z")]
    [InlineData("date", "2000-02-29", "+", "yearMonthDuration", "P1Y", "date", "2001-02-28")]
    [InlineData("date", "2000-10-30", "-", "date", "1999-11-28", "dayTimeDuration", "P337D")]
    [InlineData("date", "2000-10-15-05:00", "-", "date", "2000-10-10+02:00", "dayTimeDuration", "P5DT7H")]
    [InlineData("dateTime", "2000-10-30T06:12:00", "-", "dateTime", "1999-11-28T09:00:00Z", "dayTimeDuration", "P336DT21H12M")]
    [InlineData("time", "11:12:00Z", "-", "time", "04:00:00-05:00", "dayTimeDuration", "PT2H12M")]
    [InlineData("time", "11:12:00", "+", "dayTimeDuration", "P3DT1H15M", "time", "12:27:00")]
    [InlineData("time", "23:12:00+03:00", "+", "dayTimeDuration", "P1DT3H15M", "time", "02:27:00+03:00")]
    [InlineData("time", "11:12:00", "-", "dayTimeDuration", "P3DT1H15M", "time", "09:57:00")]
    public void Arithmetic(string ta, string a, string op, string tb, string b, string resultType, string expected)
    {
        AtomicValue r = Op(ta, a, op, tb, b);
        Assert.Equal(expected, r.Value);
        Assert.Same(T(resultType), r.Type);
    }

    [Theory]
    [InlineData("integer", "1", "div", "integer", "0", "FOAR0001")]
    [InlineData("integer", "1", "idiv", "integer", "0", "FOAR0001")]
    [InlineData("integer", "1", "mod", "integer", "0", "FOAR0001")]
    [InlineData("decimal", "1.5", "div", "decimal", "0", "FOAR0001")]
    [InlineData("double", "1", "idiv", "double", "0", "FOAR0001")]
    [InlineData("double", "INF", "idiv", "double", "1", "FOAR0002")]
    [InlineData("double", "NaN", "idiv", "double", "1", "FOAR0002")]
    [InlineData("decimal", "79228162514264337593543950335", "+", "integer", "1", "FOAR0002")]
    [InlineData("yearMonthDuration", "P1Y", "div", "double", "0", "FODT0002")]
    [InlineData("yearMonthDuration", "P1Y", "*", "double", "NaN", "FOCA0005")]
    [InlineData("yearMonthDuration", "P1Y", "*", "double", "INF", "FODT0002")]
    [InlineData("string", "1", "+", "integer", "1", "XPTY0004")]
    [InlineData("duration", "P1Y", "+", "duration", "P1Y", "XPTY0004")]
    [InlineData("yearMonthDuration", "P1Y", "+", "dayTimeDuration", "P1D", "XPTY0004")]
    [InlineData("time", "10:00:00", "+", "yearMonthDuration", "P1Y", "XPTY0004")]
    [InlineData("date", "2000-01-01", "-", "dateTime", "2000-01-01T00:00:00", "XPTY0004")]
    [InlineData("untypedAtomic", "abc", "+", "integer", "1", "FORG0001")]
    public void ArithmeticErrors(string ta, string a, string op, string tb, string b, string code)
    {
        Assert.Equal(code, ErrorOf(() => Op(ta, a, op, tb, b)));
    }

    [Fact]
    public void UnaryMinus()
    {
        Assert.Equal("-3", Xdm.Arithmetic.Unary(true, V("integer", "3")).Value);
        Assert.Equal("-0", Xdm.Arithmetic.Unary(true, V("double", "0")).Value);
        Assert.Equal("XPTY0004", ErrorOf(() => Xdm.Arithmetic.Unary(true, V("string", "3"))));
    }

    [Fact]
    public void AtomicValuesAreXPathItems()
    {
        System.Xml.XPath.XPathItem item = V("integer", "42");
        Assert.False(item.IsNode);
        Assert.Equal("42", item.Value);
        Assert.Equal(42L, item.ValueAsLong);
        Assert.Equal(System.Xml.Schema.XmlTypeCode.Integer, item.XmlType!.TypeCode);
    }
}
