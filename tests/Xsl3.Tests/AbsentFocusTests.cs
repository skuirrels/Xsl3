// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Reflection;
using System.Security;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class AbsentFocusTests
{
    private static string Sheet(string expression) => $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:err="http://www.w3.org/2005/xqt-errors">
              <xsl:output method="text"/>
              <xsl:accumulator name="a" initial-value="0"><xsl:accumulator-rule match="*" select="$value + 1"/></xsl:accumulator>
              <xsl:key name="k" match="*" use="name()"/>
              <xsl:param name="captured-accumulator" select="accumulator-before#1"/>
              <xsl:param name="captured-key" select="key#2"/>
              <xsl:param name="captured-string" select="string#0"/>
              <xsl:template name="main"><xsl:try>
                <xsl:sequence select="{{SecurityElement.Escape(expression)}}"/>
                <xsl:catch><xsl:sequence select="local-name-from-QName($err:code)"/></xsl:catch>
              </xsl:try></xsl:template>
            </xsl:stylesheet>
            """;

    private static string Run(string expression, bool forward)
    {
        var compiler = new XsltCompiler { Settings = new() { StreamedExecution = forward } };
        return compiler.Compile(new StringReader(Sheet(expression))).CreateTransformer().CallTemplate(new QName("main")).ToString();
    }

    private static string Legacy(string expression, bool forward, bool debug, bool saved)
    {
        var transform = new XslCompiledTransform(debug) { InitialTemplate = new XmlQualifiedName("main") };
        var settings = new XsltSettings { StreamedExecution = forward };
        using var sheet = XmlReader.Create(new StringReader(Sheet(expression)));
        if (saved)
        {
#if NET10_0_OR_GREATER
            using var bytes = new MemoryStream();
            transform.CompileToAssembly(sheet, settings, null, "AbsentFocus", "Saved.AbsentFocus", bytes);
            transform.Load(Assembly.Load(bytes.ToArray()).GetType("Saved.AbsentFocus", true)!, settings);
#else
            throw new System.InvalidOperationException("Saved compilation requires .NET 10");
#endif
        }
        else transform.Load(sheet, settings, null);
        using var output = new StringWriter(); transform.Transform(XslCompiledTransform.AbsentInput, null, output);
        return output.ToString();
    }

    private static void Check(string expression, string expected)
    {
        foreach (bool forward in new[] { false, true })
        {
            Assert.Equal(expected, Run(expression, forward));
            foreach (bool debug in new[] { false, true })
            {
                Assert.Equal(expected, Legacy(expression, forward, debug, false));
#if NET10_0_OR_GREATER
                Assert.Equal(expected, Legacy(expression, forward, debug, true));
#endif
            }
        }
    }

    [Theory]
    [InlineData("accumulator-before('a')")]
    [InlineData("accumulator-after('a')")]
    [InlineData("function(){accumulator-before('a')}()")]
    [InlineData("let $f := accumulator-after#1 return $f('a')")]
    [InlineData("accumulator-before(?)('a')")]
    public void AbsentAccumulatorContextHasItsFunctionError(string expression)
    {
        Check(expression, "XTDE3350");
    }

    [Theory]
    [InlineData("key('k', 'd')")]
    [InlineData("let $f := key#2 return $f('k', 'd')")]
    public void AbsentKeyContextHasItsFunctionError(string expression)
    {
        Check(expression, "XTDE1270");
    }

    [Theory]
    [InlineData("accumulator-before#1", "accumulator-before")]
    [InlineData("accumulator-after#1", "accumulator-after")]
    [InlineData("key#2", "key")]
    [InlineData("string#0", "string")]
    public void AReferenceCanBeCreatedWithoutInvokingItsMissingContext(string reference, string name)
    {
        Check($"local-name-from-QName(function-name({reference}))", name);
    }

    [Theory]
    [InlineData("let $f := string#0 return $f()", "XPDY0002")]
    [InlineData("string()", "XPDY0002")]
    [InlineData("42 ! string#0()", "42")]
    [InlineData("42 ! accumulator-before('a')", "XTTE3360")]
    [InlineData("42 ! (let $f := accumulator-after#1 return $f('a'))", "XTTE3360")]
    [InlineData("parse-xml('<d a=\"v\"/>')/d/@a ! accumulator-before('a')", "XTTE3360")]
    [InlineData("let $n := parse-xml('<d><x/><y/></d>')/d/y return $n ! (let $f := accumulator-before#1 return parse-xml('<g/>') ! $f('a'))", "3")]
    public void PresentInvalidAndOrdinaryAbsentFocusKeepTheirOwnContracts(string expression, string expected)
    {
        Check(expression, expected);
    }

    [Theory]
    [InlineData("$captured-accumulator('a')", "XTDE3350")]
    [InlineData("$captured-key('k', 'd')", "XTDE1270")]
    [InlineData("$captured-string()", "XPDY0002")]
    [InlineData("local-name-from-QName(function-name($captured-accumulator))", "accumulator-before")]
    [InlineData("local-name-from-QName(function-name($captured-key))", "key")]
    [InlineData("local-name-from-QName(function-name($captured-string))", "string")]
    public void GlobalReferencesDeferAbsentContextErrorsUntilInvocation(string expression, string expected) => Check(expression, expected);
}
