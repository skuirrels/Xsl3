// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Reflection;
using System.Xml;
using Xsl3.Xdm;
using Xsl3.XPath31;
using Xunit;

namespace Xsl3.Tests;

public class StreamedInspectionTests
{
    private const string Input = "<doc><group><a>1</a><b>2</b></group><group><a>3</a></group></doc>";

    [Theory]
    [InlineData("exists(doc/group/a | doc/group/b)", "true")]
    [InlineData("empty(doc/group/missing | doc/group/absent)", "true")]
    [InlineData("boolean(.//* | .//text())", "true")]
    [InlineData("not(.//* | .//text())", "false")]
    [InlineData("count(doc/group/a | doc/group/b | doc/group/a)", "3")]
    [InlineData("count(doc/group | doc/descendant::a)", "4")]
    [InlineData("count(. | .//a)", "3")]
    [InlineData("string-join((doc/group/a | doc/group/b)/text(), ',')", "1,2,3")]
    [InlineData("count(doc/@x | doc/@y | doc/@x)", "0")]
    public void CompatibleUnionBranchesShareOneForwardTraversal(string expression, string expected)
    {
        Assert.Equal(expected, Run(expression, false));
        Assert.Equal(expected, Run(expression, true));
    }

    [Theory]
    [InlineData("exists(remove(data(//a/text()), 3))", "true")]
    [InlineData("empty(remove(data(//a/text()), 1))", "false")]
    [InlineData("string-join(//group/a/text(), ',')", "1,3")]
    [InlineData("string-join(.//group/child::text(), ',')", "")]
    [InlineData("count(descendant-or-self::node()/doc)", "1")]
    public void DescendantChildrenAreConsumedBeforeTheirNextParent(string expression, string expected)
    {
        Assert.Equal(expected, Run(expression, false));
        Assert.Equal(expected, Run(expression, true));
    }

    [Theory]
    [InlineData("boolean(remove(data(//a/text()), 3))")]
    [InlineData("not(remove(data(//a/text()), 3))")]
    public void MultipleAtomicValuesKeepTheirEffectiveBooleanValueError(string expression)
    {
        Assert.Equal("FORG0006", Assert.Throws<XdmException>(() => Run(expression, false)).Code);
        Assert.Equal("FORG0006", Assert.Throws<XdmException>(() => Run(expression, true)).Code);
    }

    [Theory]
    [InlineData("boolean(())", "false")]
    [InlineData("boolean(0)", "false")]
    [InlineData("boolean(1)", "true")]
    [InlineData("boolean(number('NaN'))", "false")]
    [InlineData("boolean('')", "false")]
    [InlineData("boolean('value')", "true")]
    [InlineData("not(false())", "true")]
    [InlineData("boolean(remove(data(//a/text()), 2))", "true")]
    [InlineData("string-join(remove(doc/group/*, 2)/text(), ',')", "1,3")]
    [InlineData("count(remove(doc/group/a, 0))", "2")]
    [InlineData("count(remove(doc/group/a, -1))", "2")]
    [InlineData("count(remove(doc/group/a, 999999999999999999999999999999999))", "2")]
    [InlineData("string-join(copy-of(doc)//group/a[1]/text(), ',')", "1,3")]
    public void SequenceInspectionPreservesCanonicalValuesAndRemovalPositions(string expression, string expected)
    {
        Assert.Equal(expected, Run(expression, false));
        Assert.Equal(expected, Run(expression, true));
    }

    [Theory]
    [InlineData("boolean((false(), true()))", "FORG0006")]
    [InlineData("boolean(map{})", "FORG0006")]
    [InlineData("boolean(xs:QName('x'))", "FORG0006")]
    [InlineData("boolean((false(), error()))", "FOER0000")]
    [InlineData("remove(doc/group/a, 1.5)", "XPTY0004")]
    public void SequenceInspectionPreservesTypeAndEvaluationErrors(string expression, string code)
    {
        Assert.Equal(code, Assert.Throws<XdmException>(() => Run(expression, false)).Code);
        Assert.Equal(code, Assert.Throws<XdmException>(() => Run(expression, true)).Code);
    }

    [Fact]
    public void GroundedUnionAnchorsRetainSortingAndDeduplication()
    {
        const string declarations = "<xsl:variable name='g' select='copy-of(doc)'/><xsl:variable name='s' as='element()*' select='($g/group[2], $g/group[1], $g/group[1])'/>";
        const string expression = "string-join(($s/a | $s/b)/text(), ',')";
        Assert.Equal("1,2,3", Run(expression, false, declarations: declarations));
        Assert.Equal("1,2,3", Run(expression, true, declarations: declarations));
    }

    [Theory]
    [InlineData("count(copy-of($g)/group/a | copy-of($g)/group/a)", "4")]
    [InlineData("count($g/group/a[1] | $g/group/b)", "3")]
    [InlineData("($g/group/a[1] | $g/group/b)[1] is ($g/group/a)[1]", "true")]
    public void IndependentConstructorsAndPositionalBranchesKeepTheirOwnEvaluation(string expression, string expected)
    {
        const string declarations = "<xsl:variable name='g' select='copy-of(doc)'/>";
        Assert.Equal(expected, Run(expression, false, declarations: declarations));
        Assert.Equal(expected, Run(expression, true, declarations: declarations));
    }

    [Theory]
    [InlineData("count(doc/group/group/descendant::group/group/a/text())")]
    [InlineData("count(doc/group/group/descendant-or-self::group/group/a/text())")]
    public void DescendantChildMatchingCannotEscapeItsAnchor(string expression)
    {
        const string input = "<doc><group><group><a>1</a></group></group></doc>";
        Assert.Equal("0", Run(expression, false, input: input));
        Assert.Equal("0", Run(expression, true, input: input));
    }

    [Theory]
    [InlineData("exists(. | .)", "XPTY0004")]
    [InlineData("exists(child::a | child::b)", "XPTY0020")]
    [InlineData("exists(./a | ./b)", "XPTY0019")]
    [InlineData("exists(descendant::a/b)", "XPTY0020")]
    [InlineData("exists(.//a/b)", "XPTY0019")]
    public void TraversalRewritesRetainTheCorrectContextTypeErrors(string expression, string expected)
    {
        foreach (bool forward in new[] { false, true })
        {
            var compiled = XPath31Compiler.Compile(expression, new XPathStaticContext { ForwardOnly = forward });
            Assert.Equal(expected, Assert.Throws<XdmException>(() => compiled.Evaluate(AtomicValue.OfInteger(1))).Code);
        }
    }

    [Fact]
    public void AttributeUnionDoesNotConsumeTheFollowingChildren() =>
        Assert.Equal("4", Run("doc ! (count(@x | @y) + count(group))", true, input: "<doc x='1' y='2'><group/><group/></doc>"));

    [Theory]
    [InlineData("doc ! (count(. | .) + count(group))", "3")]
    [InlineData("doc ! (count(self::missing | self::other) + count(group))", "2")]
    public void SelfOnlyUnionDoesNotConsumeTheFollowingChildren(string expression, string expected)
    {
        Assert.Equal(expected, Run(expression, false));
        Assert.Equal(expected, Run(expression, true));
    }

    [Theory]
    [InlineData("exists(doc/group/a | doc/group/b)")]
    [InlineData("boolean((doc/group/a, error()))")]
    public void NodeInspectionCanProduceAResultBeforeReachingTheEndOfTheInput(string expression)
    {
        string sheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode streamable="yes"/><xsl:template match="/"><xsl:message select="{{expression}}"/></xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(sheet)), new() { StreamedExecution = true, StreamingBufferCapacity = 1 }, null);
        using var input = XmlReader.Create(new StringReader(Input));
        var arguments = new XsltArgumentList();
        bool observed = false;
        arguments.XsltMessageEncountered += (_, message) => {
            Assert.Equal("true", message.Message);
            Assert.False(input.EOF);
            observed = true;
        };
        using var output = new StringWriter();
        transform.Transform(input, arguments, output);
        Assert.True(observed);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

#if NET9_0_OR_GREATER
    [Fact]
    public void SharedUnionTraversalSurvivesSavedCompilation() =>
        Assert.Equal("1,2,3", Run("string-join((doc/group/a | doc/group/b)/text(), ',')", true, saved: true));
#endif

    private static string Run(string expression, bool forward, bool saved = false, string input = Input, string declarations = "")
    {
        string stylesheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:template match="/">{{declarations}}<xsl:value-of select="{{expression}}"/></xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XslCompiledTransform();
        var settings = new XsltSettings { StreamedExecution = forward, StreamingBufferCapacity = 1 };
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(XmlReader.Create(new StringReader(stylesheet)), settings, null, "Inspection", "Saved.Inspection", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.Inspection", true)!);
#endif
        }
        else transform.Load(XmlReader.Create(new StringReader(stylesheet)), settings, null);
        using var reader = XmlReader.Create(new StringReader(input));
        using var output = new StringWriter();
        transform.Transform(reader, null, output);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
        return output.ToString();
    }
}
