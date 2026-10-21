// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Text;
using System.Xml;
using Xsl3.XPath31;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

/// <summary>
/// Mutation fuzzing in the unit suite (remaining-scope 8.1): seeded random edits of valid XPath expressions, JSON texts
/// and stylesheets. Whatever the input, the processor answers with its own errors (a static or dynamic error, an XML
/// error) and never with anything else: no crash, no stray .NET exception, no hang.
/// </summary>
public class RobustnessTests
{
    // XSL3_FUZZ_MUTATIONS and XSL3_FUZZ_SEED run longer campaigns from other seeds.
    private static readonly int Mutations = int.TryParse(Environment.GetEnvironmentVariable("XSL3_FUZZ_MUTATIONS"), out int m) ? m : 1500;

    private static readonly int Seed = int.TryParse(Environment.GetEnvironmentVariable("XSL3_FUZZ_SEED"), out int s) ? s : 0;

    private static readonly string[] s_expressions =
    {
        "for $i in 1 to 10 return $i * 2",
        "map { 'a': [1, 2, (3, 4)], 'b': map { 1: true() } }?a?3",
        "string-join(tokenize('a,b,,c', ','), '|') || upper-case('x')",
        "let $f := function($x as xs:integer) as xs:integer { $x + 1 } return fold-left(1 to 5, 0, $f)",
        "xs:date('2026-10-21') + xs:dayTimeDuration('P1D') eq xs:date('2026-10-22')",
        "every $x in (1, 2.5, 3e0) satisfies $x instance of xs:numeric",
        "format-number(1234.5678, '#,##0.00') || format-integer(42, 'Ww')",
        "array:for-each([1, 2, 3], function($m) { $m * $m }) => array:size()",
        "(1, 2, 3)[. = 2] ! (. + 1) , sort(('b', 'a'), (), function($s) { $s })",
        "if (empty(())) then 'yes' else 'no', matches('abc', '^a(b)c$'), replace('aaa', 'a(a)?', '$1')",
    };

    private static readonly string[] s_json =
    {
        """{"a": [1, 2.5, -3e2, true, false, null], "b": {"c": "é\n"}, "d": []}""",
        """[{"x": "y"}, 1, "two", [3], {"": 0}]""",
    };

    private const string Stylesheet = """
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xsl:param name="n" as="xs:integer" select="3"/>
          <xsl:template match="/">
            <out>
              <xsl:for-each-group select="1 to $n" group-by=". mod 2">
                <g key="{current-grouping-key()}"><xsl:value-of select="current-group()"/></g>
              </xsl:for-each-group>
              <xsl:iterate select="1 to 3"><xsl:param name="t" select="0"/><xsl:on-completion><t><xsl:value-of select="$t"/></t></xsl:on-completion><xsl:next-iteration><xsl:with-param name="t" select="$t + ."/></xsl:next-iteration></xsl:iterate>
              <xsl:try><xsl:sequence select="1 div 0"/><xsl:catch><caught/></xsl:catch></xsl:try>
            </out>
          </xsl:template>
        </xsl:stylesheet>
        """;

    private const string Alphabet = "()[]{}<>,.;:$@!?*+-=/|&'\"#%^~` \t\nabcxyz019eE_\\";

    private static string Mutate(string text, Random r)
    {
        var sb = new StringBuilder(text);
        for (int edits = r.Next(1, 4); edits > 0 && sb.Length > 0; edits--)
        {
            int at = r.Next(sb.Length);
            switch (r.Next(4))
            {
                case 0:
                    sb.Remove(at, Math.Min(sb.Length - at, r.Next(1, 4)));
                    break;
                case 1:
                    sb.Insert(at, Alphabet[r.Next(Alphabet.Length)]);
                    break;
                case 2:
                    sb[at] = Alphabet[r.Next(Alphabet.Length)];
                    break;
                default:
                    // Duplicate a slice: unbalanced brackets, repeated operators.
                    int length = Math.Min(sb.Length - at, r.Next(1, 8));
                    sb.Insert(r.Next(sb.Length), sb.ToString(at, length));
                    break;
            }
        }
        return sb.ToString();
    }

    private static bool Expected(Exception e) =>
        e is XPathStaticException or XdmException or XsltException or XmlException or XsltStaticException or XsltDynamicException;

    [Fact]
    public void MutatedExpressionsFailCleanly()
    {
        var r = new Random(81 + Seed);
        for (int i = 0; i < Mutations; i++)
        {
            string expression = Mutate(s_expressions[r.Next(s_expressions.Length)], r);
            Exception? e = Record.Exception(() => XPath31Compiler.Compile(expression, new XPathStaticContext()).Evaluate());
            Assert.True(e == null || Expected(e), $"{expression}\n{e}");
        }
    }

    [Fact]
    public void MutatedJsonFailsCleanly()
    {
        var r = new Random(82 + Seed);
        XPath31Executable parse = XPath31Compiler.Compile("parse-json($json)", Context("json"));
        XPath31Executable toXml = XPath31Compiler.Compile("json-to-xml($json)", Context("json"));
        for (int i = 0; i < Mutations; i++)
        {
            string json = Mutate(s_json[r.Next(s_json.Length)], r);
            var variables = new System.Collections.Generic.Dictionary<QName, System.Collections.Generic.IList<System.Xml.XPath.XPathItem>>
            {
                [new QName("json")] = new System.Xml.XPath.XPathItem[] { AtomicValue.OfString(json) },
            };
            foreach (XPath31Executable function in new[] { parse, toXml })
            {
                Exception? e = Record.Exception(() => function.Evaluate(null, variables));
                Assert.True(e == null || Expected(e), $"{json}\n{e}");
            }
        }
    }

    [Fact]
    public void MutatedStylesheetsFailCleanly()
    {
        var r = new Random(83 + Seed);
        for (int i = 0; i < Mutations / 5; i++)
        {
            string stylesheet = Mutate(Stylesheet, r);
            Exception? e = Record.Exception(() =>
            {
                var transform = new XslCompiledTransform();
                transform.Load(XmlReader.Create(new StringReader(stylesheet)));
                transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, TextWriter.Null);
            });
            Assert.True(e == null || Expected(e), $"{stylesheet}\n{e}");
        }
    }

    private static XPathStaticContext Context(string variable)
    {
        var sc = new XPathStaticContext();
        sc.ExternalVariables.Add(new QName(variable));
        return sc;
    }
}
