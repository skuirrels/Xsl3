// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.XPath;
using Xsl3.XPath31;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

/// <summary>XPath 3.1 expressions compiled to QIL and IL and evaluated end to end.</summary>
public class XPath31EvaluationTests
{
    private static readonly XPathNavigator s_doc = new XPathDocument(new StringReader(
        "<doc><a n='1'>x</a><a n='2'>y</a><b><a n='3'>z</a></b><!--c--></doc>")).CreateNavigator();

    /// <summary>upper-case and lower-case of ASCII strings (a fast path) agree with the Unicode tables.</summary>
    [Fact]
    public void AsciiCaseMappingMatchesTheTables()
    {
        string all = new(Enumerable.Range(0, 128).Select(c => (char)c).ToArray());
        Assert.Equal(Xsl3.XPath31.Functions.CaseMapping.ToUpperByTable(all), Xsl3.XPath31.Functions.CaseMapping.ToUpper(all));
        Assert.Equal(Xsl3.XPath31.Functions.CaseMapping.ToLowerByTable(all), Xsl3.XPath31.Functions.CaseMapping.ToLower(all));
        Assert.Same("ABC 1", Xsl3.XPath31.Functions.CaseMapping.ToUpper("ABC 1"));
    }

    private static IList<XPathItem> Eval(string expression, XPathItem? context = null)
    {
        XPath31Executable exe = XPath31Compiler.Compile(expression, new XPathStaticContext());
        return exe.Evaluate(context, implicitTimezoneMinutes: 0);
    }

    // The result rendered as space-separated string values.
    private static string Render(string expression, XPathItem? context = null) =>
        string.Join(" ", Eval(expression, context).Select(i => i.Value));

    [Theory]
    [InlineData("1 + 2", "3")]
    [InlineData("string-to-codepoints(xs:string(xs:untypedAtomic('a' || codepoints-to-string(10) || 'b')))", "97 10 98")] // xs:string preserves whitespace
    [InlineData("(1, 2, 3)", "1 2 3")]
    [InlineData("()", "")]
    [InlineData("5 div 2", "2.5")]
    [InlineData("7 idiv 2, 7 mod 2", "3 1")]
    [InlineData("1.5e0 * 2", "3")]
    [InlineData("-(3)", "-3")]
    [InlineData("'a' || 'b' || 1", "ab1")]
    [InlineData("1 to 5", "1 2 3 4 5")]
    [InlineData("1 eq 1", "true")]
    [InlineData("(1, 2) = (2, 3)", "true")]
    [InlineData("(1, 2) != (1, 2)", "true")]
    [InlineData("() = 1", "false")]
    [InlineData("1 lt 2 and 2 lt 1", "false")]
    [InlineData("1 lt 2 or 2 lt 1", "true")]
    [InlineData("if (1) then 'yes' else 'no'", "yes")]
    [InlineData("for $x in 1 to 3 return $x * $x", "1 4 9")]
    [InlineData("for $x in (1, 2), $y in (10, 20) return $x + $y", "11 21 12 22")]
    [InlineData("let $x := 5, $y := $x * 2 return $y", "10")]
    [InlineData("some $x in (1, 2, 3) satisfies $x gt 2", "true")]
    [InlineData("every $x in (1, 2, 3) satisfies $x gt 2", "false")]
    [InlineData("(10, 20, 30)[2]", "20")]
    [InlineData("(10, 20, 30)[. gt 15]", "20 30")]
    [InlineData("(10, 20, 30)[last()]", "30")]
    [InlineData("(10, 20, 30)[position() lt 3]", "10 20")]
    [InlineData("(1, 2, 3) ! (. * 10)", "10 20 30")]
    [InlineData("'12' cast as xs:integer + 1", "13")]
    [InlineData("xs:date('2000-02-29') + xs:yearMonthDuration('P1Y')", "2001-02-28")]
    [InlineData("'x' castable as xs:integer", "false")]
    [InlineData("5 instance of xs:integer", "true")]
    [InlineData("5 instance of xs:string", "false")]
    [InlineData("(1, 2) instance of xs:integer+", "true")]
    [InlineData("xs:double('1e6')", "1.0E6")]
    [InlineData("concat('a', 'b', 'c', 'd', 'e', 'f')", "abcdef")]
    [InlineData("string-join((1, 2, 3), '-')", "1-2-3")]
    [InlineData("substring('motor car', 6)", " car")]
    [InlineData("count((1, 2, 3))", "3")]
    [InlineData("sum((1, 2.5))", "3.5")]
    [InlineData("avg((1, 2, 3, 4))", "2.5")]
    [InlineData("max((1, 5, 3)), min(('b', 'a'))", "5 a")]
    [InlineData("reverse(1 to 3)", "3 2 1")]
    [InlineData("distinct-values((1, 1.0, 2, '2'))", "1 2 2")]
    [InlineData("deep-equal((1, 2), (1, 2.0))", "true")]
    [InlineData("round(2.5), round(-2.5), round-half-to-even(2.5)", "3 -2 2")]
    [InlineData("abs(-3), floor(2.7), ceiling(2.1)", "3 2 3")]
    [InlineData("upper-case('abc') => lower-case()", "abc")]
    [InlineData("upper-case('Mixed 09 _z'), lower-case('Mixed 09 _Z'), upper-case('straße'), lower-case('K')", "MIXED 09 _Z mixed 09 _z STRASSE k")]
    [InlineData("string-length('\U0001F600x')", "2")]
    [InlineData("exists(()), empty(())", "false true")]
    [InlineData("not(()), boolean('a')", "true true")]
    [InlineData("year-from-date(xs:date('2024-05-06'))", "2024")]
    [InlineData("(2, 4, 6) ! position#0(), (2, 4, 6) ! last#0()", "1 2 3 3 3 3")] // the focus the reference is made in
    [InlineData("10 < (1 to 1000000000000000000000), (1 to 5) >= 6, 3 > (4 to 2)", "true false false")] // not materialised
    [InlineData("format-integer(1, 'Ww;o(-er)', 'de'), format-integer(3, 'w;o(-en)', 'de')", "Erster dritten")]
    public void EvaluatesWithoutContext(string expression, string expected)
    {
        Assert.Equal(expected, Render(expression));
    }

    [Theory]
    [InlineData("/doc/a", "x y")]
    [InlineData("//a", "x y z")]
    [InlineData("//a/@n", "1 2 3")]
    [InlineData("count(//a)", "3")]
    [InlineData("/doc/a[2]", "y")]
    [InlineData("//a[@n = '3']", "z")]
    [InlineData("//a[last()]", "y z")] // the last a child of each parent
    [InlineData("(//a)[last()]", "z")]
    [InlineData("/doc/*[1]/@n", "1")]
    [InlineData("//b/a/ancestor::*[1]/name()", "b")]
    [InlineData("//a/string(@n)", "1 2 3")]
    [InlineData("sum(//a/@n)", "6")]
    [InlineData("//a/@n * 2", "2")] // atomised value of the first?
    [InlineData("//comment()", "c")]
    [InlineData("/doc/node()[last()] instance of comment()", "true")]
    [InlineData("(//a)[1] << (//a)[2]", "true")]
    [InlineData("count(//a | //b)", "4")]
    [InlineData("count(//a intersect /doc/a)", "2")]
    [InlineData("count(//a except /doc/a)", "1")]
    [InlineData("name(/doc/*[3])", "b")]
    [InlineData("string(.)", "xyz")]
    [InlineData("/doc/a/following-sibling::*[1]/name()", "a b")]
    [InlineData("//a[1]/preceding::a/@n/string()", "1 2")]
    [InlineData("/doc/element(a)[@n eq '2']", "y")]
    [InlineData("number(/doc/b/a/@n), number(/doc/b/a), number(/doc/c), upper-case(/doc/b/a), concat(/doc/c, /doc/b/a)", "3 NaN NaN Z z")]
    [InlineData("sum(()), sum(()) instance of xs:integer, sum(/doc/a/@n/x), sum(/doc/a/@n), sum(/doc/a/@n) instance of xs:double", "0 true 0 3 true")]
    [InlineData("upper-case(/doc/a)", "XPTY0004")]
    [InlineData("number(/doc/a)", "XPTY0004")]
    public void EvaluatesPaths(string expression, string expected)
    {
        if (expected == "XPTY0004")
        {
            Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => Render(expression, s_doc)).Code);
            return;
        }
        if (expression == "//a/@n * 2")
        {
            Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => Render(expression, s_doc)).Code);
            return;
        }
        Assert.Equal(expected, Render(expression, s_doc));
    }

    [Theory]
    [InlineData("1 div 0", "FOAR0001")]
    [InlineData("'a' + 1", "XPTY0004")]
    [InlineData("(1, 2) eq 1", "XPTY0004")]
    [InlineData("xs:integer('x')", "FORG0001")]
    [InlineData(".", "XPDY0002")]
    [InlineData("exactly-one((1, 2))", "FORG0005")]
    [InlineData("error(xs:QName('err:FOO0001'))", "FOO0001")]
    [InlineData("(1, 2)/a", "XPTY0019")]
    [InlineData("3 treat as xs:string", "XPDY0050")]
    public void DynamicErrors(string expression, string code)
    {
        Assert.Equal(code, Assert.Throws<XdmException>(() => Render(expression)).Code);
    }

    [Theory]
    [InlineData("$undeclared", "XPST0008")]
    [InlineData("nope()", "XPST0017")]
    [InlineData("p:x", "XPST0081")]
    [InlineData("1 cast as xs:NOTATION", "XPST0080")]
    [InlineData("1 cast as xs:nope", "XPST0051")]
    public void StaticErrors(string expression, string code)
    {
        Assert.Equal(code, Assert.Throws<XPathStaticException>(() => Eval(expression)).Code);
    }

    [Fact]
    public void ExternalVariables()
    {
        var sc = new XPathStaticContext();
        var x = new QName("", "x");
        sc.ExternalVariables.Add(x);
        XPath31Executable exe = XPath31Compiler.Compile("$x * 2", sc);
        IList<XPathItem> result = exe.Evaluate(variables: new Dictionary<QName, IList<XPathItem>> { [x] = new XPathItem[] { AtomicValue.OfInteger(21) } });
        Assert.Equal("42", Assert.Single(result).Value);
    }
}
