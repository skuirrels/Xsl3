// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using Xsl3.XPath31;
using Xunit;

namespace Xsl3.Tests;

public class XPath31ParserTests
{
    // Each expression and its fully parenthesised rendering (Ast.cs ToString).
    [Theory]
    // precedence and associativity
    [InlineData("1 + 2 * 3", "(1 + (2 * 3))")]
    [InlineData("1 - 2 - 3", "((1 - 2) - 3)")]
    [InlineData("-1 + 2", "((-1) + 2)")]
    [InlineData("- - 1", "(-(-1))")]
    [InlineData("1 to 3", "(1 to 3)")]
    [InlineData("'a' || 'b' || 'c'", "((\"a\" || \"b\") || \"c\")")]
    [InlineData("a or b and c", "(child::a or (child::b and child::c))")]
    [InlineData("1 = 2", "(1 = 2)")]
    [InlineData("1 eq 2", "(1 eq 2)")]
    [InlineData("$a is $b", "($a is $b)")]
    [InlineData("$a << $b", "($a << $b)")]
    [InlineData("$a union $b | $c", "(($a union $b) union $c)")]
    [InlineData("$a intersect $b except $c", "(($a intersect $b) except $c)")]
    [InlineData("1 instance of xs:integer+", "(1 instance of xs:integer+)")]
    [InlineData("$x treat as item()*", "($x treat as item()*)")]
    [InlineData("'1' cast as xs:integer?", "(\"1\" cast as xs:integer?)")]
    [InlineData("'1' castable as xs:integer", "(\"1\" castable as xs:integer)")]
    [InlineData("4 treat as item() + - 5", "((4 treat as item()+) - 5)")]
    // literals
    [InlineData("12", "12")]
    [InlineData("1.5", "1.5")]
    [InlineData(".5", ".5")]
    [InlineData("1.", "1.")]
    [InlineData("1e3", "1e3")]
    [InlineData("1.5E-3", "1.5E-3")]
    [InlineData("'it''s'", "\"it's\"")]
    [InlineData("\"say \"\"hi\"\"\"", "\"say \"\"hi\"\"\"")]
    [InlineData("()", "()")]
    [InlineData("(1, 2, 3)", "(1, 2, 3)")]
    // comments
    [InlineData("1 (: one (: nested :) :) + 2", "(1 + 2)")]
    // names
    [InlineData("a-b", "child::a-b")]
    [InlineData("a - b", "(child::a - child::b)")]
    [InlineData("a -b", "(child::a - child::b)")]
    [InlineData("xs:integer('1')", "xs:integer(\"1\")")]
    [InlineData("Q{http://x}f(1)", "Q{http://x}f(1)")]
    [InlineData("Q{http://x}*", "child::Q{http://x}*")]
    [InlineData("p:*", "child::p:*")]
    [InlineData("*:local", "child::*:local")]
    [InlineData("div div div", "(child::div div child::div)")]
    [InlineData("for", "child::for")]
    [InlineData("if", "child::if")]
    // paths
    [InlineData("/", "/")]
    [InlineData("/a", "(/)/(child::a)")]
    [InlineData("//a", "(/)//(child::a)")]
    [InlineData("a/b//c", "((child::a)/(child::b))//(child::c)")]
    [InlineData("@id", "attribute::id")]
    [InlineData("..", "parent::node()")]
    [InlineData("a[1][@x]", "child::a[1][attribute::x]")]
    [InlineData("ancestor-or-self::*", "ancestor-or-self::*")]
    [InlineData("text()", "child::text()")]
    [InlineData("attribute(*, xs:integer)", "attribute::attribute(*, xs:integer)")]
    [InlineData("element(a, t?)", "child::element(a, t?)")]
    [InlineData("document-node(element(a))", "child::document-node(element(a))")]
    [InlineData("processing-instruction('x')", "child::processing-instruction(x)")]
    [InlineData("(1, 2)[. > 1]", "((1, 2))[(. > 1)]")]
    [InlineData("(/) * 5", "(/ * 5)")]
    [InlineData("$a ! (. + 1)", "($a) ! ((. + 1))")]
    // FLWOR-ish and conditionals
    [InlineData("for $x in 1 to 3, $y in $x return $x * $y", "(for $x in (1 to 3), $y in $x return ($x * $y))")]
    [InlineData("let $x := 1 return $x", "(let $x := 1 return $x)")]
    [InlineData("some $x in (1, 2) satisfies $x = 2", "(some $x in (1, 2) satisfies ($x = 2))")]
    [InlineData("if ($a) then 1 else 2", "(if ($a) then 1 else 2)")]
    // 3.0 / 3.1 syntax
    [InlineData("fn:concat#3", "fn:concat#3")]
    [InlineData("function($a as xs:integer) as xs:integer { $a + 1 }", "function($a as xs:integer) as xs:integer { ($a + 1) }")]
    [InlineData("function() {}", "function() { () }")]
    [InlineData("substring(?, 2)", "substring(?, 2)")]
    [InlineData("$f(1, 2)", "($f)(1, 2)")]
    [InlineData("'abc' => substring(2) => upper-case()", "((\"abc\" => substring(2)) => upper-case())")]
    [InlineData("map { 'a': 1, 'b': 2 }", "map { \"a\": 1, \"b\": 2 }")]
    [InlineData("map{}", "map {  }")]
    [InlineData("[1, (2, 3)]", "[1, (2, 3)]")]
    [InlineData("array { 1, 2 }", "array { (1, 2) }")]
    [InlineData("$m?key", "($m)?key")]
    [InlineData("$a?1?*", "(($a)?1)?*")]
    [InlineData("?name", "?name")]
    [InlineData("$m?(1 + 1)", "($m)?((1 + 1))")]
    [InlineData("$x instance of map(xs:string, item()*)", "($x instance of map(xs:string, item()*))")]
    [InlineData("$x instance of function(xs:int) as xs:int", "($x instance of function(xs:int) as xs:int)")]
    [InlineData("$x instance of (function(*))?", "($x instance of function(*)?)")]
    [InlineData("$x instance of empty-sequence()", "($x instance of empty-sequence())")]
    public void Parses(string expression, string expected)
    {
        Assert.Equal(expected, Parser.ParseXPath(expression).ToString());
    }

    [Theory]
    [InlineData("1 = 2 = 3")]
    [InlineData("1 to 2 to 3")]
    [InlineData("10div 3")]
    [InlineData("1.2.3")]
    [InlineData("a :b")]
    [InlineData("if (1) then 2")]
    [InlineData("for $x in 1")]
    [InlineData("'unterminated")]
    [InlineData("(: unterminated")]
    [InlineData("node(1)")]
    [InlineData("item()")]
    [InlineData("map(1)")]
    [InlineData("1 +")]
    [InlineData("foo::bar")]
    [InlineData("Q{a{b}c")]
    [InlineData("$")]
    [InlineData("a[1")]
    [InlineData("1 2")]
    [InlineData("/ * 5")] // xgc: leading-lone-slash (the spec's own example)
    public void RejectsWithXPST0003(string expression)
    {
        var e = Assert.Throws<XPathStaticException>(() => Parser.ParseXPath(expression));
        Assert.Equal("XPST0003", e.Code);
    }

    [Fact]
    public void RecordsSourceSpans()
    {
        var sum = (BinaryExpr)Parser.ParseXPath("  1 + foo(2)");
        Assert.Equal(new SourceSpan(2, 12), sum.Span);
        Assert.Equal(new SourceSpan(6, 12), sum.Right.Span);
    }
}
