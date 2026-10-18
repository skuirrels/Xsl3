// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Xml.XPath;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

/// <summary>fn:format-integer, fn:format-number and fn:format-dateTime and friends (F&amp;O 3.1 §4.6, §4.7, §9.8).</summary>
public class FormattingTests
{
    private static string Eval(string expression, XPathStaticContext? sc = null) =>
        string.Join(" ", XPath31Compiler.Compile(expression, sc ?? new XPathStaticContext()).Evaluate(implicitTimezoneMinutes: 0).Select(i => i.Value));

    [Theory]
    [InlineData("format-integer(123, '0000')", "0123")]
    [InlineData("format-integer(1234567, '#,##0')", "1,234,567")]
    [InlineData("format-integer(123456789, '000,00,00')", "12345,67,89")] // irregular: no separator at position 6
    [InlineData("format-integer(-5, '1')", "-5")]
    [InlineData("format-integer(28, 'a')", "ab")]
    [InlineData("format-integer(1999, 'I')", "MCMXCIX")]
    [InlineData("format-integer(21, 'w')", "twenty one")] // Saxon's spelling, which QT3 accepts alongside "twenty-one"
    [InlineData("format-integer(3, 'Ww;o')", "Third")]
    [InlineData("format-integer(22, '1;o')", "22nd")]
    [InlineData("format-integer(3, '①')", "③")]
    [InlineData("format-integer(1500000, '#')", "1500000")] // '#' alone is not a decimal-digit-pattern: fallback to 1
    [InlineData("format-integer((), '1')", "")]
    public void FormatInteger(string expression, string expected) => Assert.Equal(expected, Eval(expression));

    [Theory]
    [InlineData("format-number(1234.5678, '#,##0.00')", "1,234.57")]
    [InlineData("format-number(0.5, '#.#')", ".5")]
    [InlineData("format-number(0.125, '0.00')", "0.12")] // round half to even
    [InlineData("format-number(-3, '0;(0)')", "(3)")]
    [InlineData("format-number(0.25, '0%')", "25%")]
    [InlineData("format-number(12345.678, '#.99e99')", "0.12e05")]
    [InlineData("format-number(0.99999999, '0.0e0')", "10.0e-1")]
    [InlineData("format-number(xs:double('INF'), '0')", "Infinity")]
    [InlineData("format-number(xs:double('NaN'), '0')", "NaN")]
    [InlineData("format-number(642120, '####,##')", "6421,20")]
    public void FormatNumber(string expression, string expected) => Assert.Equal(expected, Eval(expression));

    [Theory]
    [InlineData("format-number(1, '#.#.#')")]
    [InlineData("format-number(1, '0;0;0')")]
    [InlineData("format-number(1, '#0#')")]
    [InlineData("format-number(1, '.e99')")]
    public void FormatNumberRejectsBadPictures(string expression) =>
        Assert.Equal("FODF1310", Assert.Throws<XdmException>(() => Eval(expression)).Code);

    [Fact]
    public void FormatNumberUsesDecimalFormats()
    {
        var sc = new XPathStaticContext
        {
            DefaultDecimalFormat = XPathDecimalFormat.FromProperties(new Dictionary<string, string> { ["decimal-separator"] = ",", ["grouping-separator"] = "." }),
        };
        sc.Namespaces["f"] = "urn:f";
        sc.DecimalFormats[new QName("urn:f", "minus")] = XPathDecimalFormat.FromProperties(new Dictionary<string, string> { ["minus-sign"] = "_" });
        Assert.Equal("1.234,5", Eval("format-number(1234.5, '#.##0,0')", sc));
        Assert.Equal("_5", Eval("format-number(-5, '0', 'f:minus')", sc));
        Assert.Equal("_5", Eval("format-number(-5, '0', 'Q{urn:f}minus')", sc));
        Assert.Equal("FODF1280", Assert.Throws<XdmException>(() => Eval("format-number(1, '0', 'nope')", sc)).Code);
        Assert.Equal("XQST0098", Assert.Throws<XdmException>(() =>
            XPathDecimalFormat.FromProperties(new Dictionary<string, string> { ["decimal-separator"] = "!", ["grouping-separator"] = "!" })).Code);
    }

    [Theory]
    [InlineData("format-date(xs:date('2002-12-31'), '[Y0001]-[M01]-[D01]')", "2002-12-31")]
    [InlineData("format-date(xs:date('2002-12-31'), '[D1o] [MNn], [Y]')", "31st December, 2002")]
    [InlineData("format-date(xs:date('2002-12-31'), '[FNn,3-3] [MNn,3-3] [Y01]')", "Tue Dec 02")]
    [InlineData("format-date(xs:date('2002-12-31'), '[Dwo] [MNn]')", "thirty first December")]
    [InlineData("format-date(xs:date('2005-01-01'), '[W]')", "53")] // ISO week of 2004
    [InlineData("format-date(xs:date('2006-01-30'), '[w]')", "5")]
    [InlineData("format-date(xs:date('2012-05-18'), '[Y9,999,*]')", "2,012")]
    [InlineData("format-date(xs:date('2002-12-31'), '[MNn]', 'de', (), ())", "Dezember")]
    [InlineData("format-date(xs:date('2002-12-31'), '[MNn]', 'xx', (), ())", "[Language: en]December")]
    [InlineData("format-date(xs:date('2002-12-31'), '[[[Y]]]')", "[2002]")]
    [InlineData("format-time(xs:time('15:58:45.762+02:00'), '[h]:[m01] [PN] [Z]')", "3:58 PM +02:00")]
    [InlineData("format-time(xs:time('15:58:45.762'), '[H01]:[m01]:[s01].[f001]')", "15:58:45.762")]
    [InlineData("format-time(xs:time('12:01:01.135'), \"[f0'0'0]\")", "1'3'5")]
    [InlineData("format-time(xs:time('12:00:00+05:30'), '[Z0]')", "+5:30")]
    [InlineData("format-time(xs:time('12:00:00Z'), '[Z01:01t]')", "Z")]
    [InlineData("format-dateTime(xs:dateTime('2002-12-31T09:05:00-05:00'), '[z]')", "GMT-05:00")]
    public void FormatDates(string expression, string expected) => Assert.Equal(expected, Eval(expression));

    [Theory]
    [InlineData("format-date(xs:date('2002-12-31'), '[H]')", "FOFD1350")]
    [InlineData("format-time(xs:time('12:00:00'), '[Y]')", "FOFD1350")]
    [InlineData("format-date(xs:date('2002-12-31'), '[Q]')", "FOFD1340")]
    [InlineData("format-date(xs:date('2002-12-31'), '[Y')", "FOFD1340")]
    [InlineData("format-date(xs:date('2002-12-31'), ']')", "FOFD1340")]
    [InlineData("format-time(xs:time('12:01:01.123'), '[f1,3-2]')", "FOFD1340")]
    [InlineData("format-date(xs:date('2002-12-31'), '[Y]', 'en', 'XX', ())", "FOFD1340")]
    public void FormatDateErrors(string expression, string code) =>
        Assert.Equal(code, Assert.Throws<XdmException>(() => Eval(expression)).Code);
}
