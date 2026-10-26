// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Reflection;
using System.Security;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedSequenceContractTests
{
    private const string Input = "<doc><row><value>1</value><value>2</value></row><row><value>3</value></row></doc>";

    [Theory]
    [InlineData("one-or-more(doc/row)", "12|3")]
    [InlineData("one-or-more(doc/row)/value", "1|2|3")]
    [InlineData("exactly-one(doc/row[1])/value", "1|2")]
    [InlineData("zero-or-one(doc/row[1])/value", "1|2")]
    [InlineData("zero-or-one(doc/missing)", "")]
    [InlineData("one-or-more((1, 2, 3))", "1|2|3")]
    [InlineData("one-or-more((map{'n':1}, map{'n':2})) ? n", "1|2")]
    [InlineData("doc/row ! count(one-or-more(value))", "2|1")]
    [InlineData("unordered(doc/row)/value", "1|2|3")]
    [InlineData("unordered(doc/row)", "12|3")]
    [InlineData("doc/row ! (exactly-one(.) is .)", "true|true")]
    [InlineData("let $g := copy-of(doc) return let $s := ($g/row[2], $g/row[1], $g/row[1]) return string-join(one-or-more($s)/value, ',')", "1,2,3")]
    public void CardinalityAndIdentityFunctionsTransmitBeforeTheNextSourceItem(string expression, string expected)
    {
        Assert.Equal(expected, Run(expression, false));
        Assert.Equal(expected, Run(expression, true));
    }

    [Theory]
    [InlineData("one-or-more(doc/missing)", "FORG0004")]
    [InlineData("exactly-one(doc/missing)", "FORG0005")]
    [InlineData("exactly-one(doc/row)", "FORG0005")]
    [InlineData("zero-or-one(doc/row)", "FORG0003")]
    [InlineData("exactly-one((1, 2))", "FORG0005")]
    [InlineData("zero-or-one((1, 2))", "FORG0003")]
    [InlineData("count(exactly-one(doc/row))", "FORG0005")]
    [InlineData("count(zero-or-one(doc/row))", "FORG0003")]
    [InlineData("count(one-or-more(()))", "FORG0004")]
    [InlineData("exists(exactly-one(()))", "FORG0005")]
    public void CardinalityAssertionsRetainCanonicalErrors(string expression, string code)
    {
        Assert.Equal(code, Assert.Throws<XdmException>(() => Run(expression, false)).Code);
        Assert.Equal(code, Assert.Throws<XdmException>(() => Run(expression, true)).Code);
    }

    [Theory]
    [InlineData("-10", "x|1|2|3")]
    [InlineData("0", "x|1|2|3")]
    [InlineData("1", "x|1|2|3")]
    [InlineData("2", "1|x|2|3")]
    [InlineData("3", "1|2|x|3")]
    [InlineData("4", "1|2|3|x")]
    [InlineData("999999999999999999999999999999999", "1|2|3|x")]
    public void InsertionUsesExactClampedPositions(string position, string expected)
    {
        string expression = $"insert-before(doc/row/value, {position}, 'x')";
        Assert.Equal(expected, Run(expression, false));
        Assert.Equal(expected, Run(expression, true));
    }

    [Theory]
    [InlineData("insert-before(('x', 'y'), 2, doc/row/value)", "x|1|2|3|y")]
    [InlineData("insert-before((), 999999999999999999999, doc/row/value)", "1|2|3")]
    [InlineData("insert-before(doc/row/value, 2, ())", "1|2|3")]
    [InlineData("insert-before(doc/row, 2, ())/value", "1|2|3")]
    [InlineData("insert-before((), 0, ())", "")]
    [InlineData("sum(insert-before(data(doc/row/value), 2, 10))", "16")]
    [InlineData("doc/row ! insert-before(value, 2, 'x')", "1|x|2|3|x")]
    [InlineData("let $n := parse-xml('<e/>') return insert-before((), 1, $n)[1] is $n", "true")]
    public void InsertionSupportsEitherForwardOperandAndPreservesIdentity(string expression, string expected)
    {
        Assert.Equal(expected, Run(expression, false));
        Assert.Equal(expected, Run(expression, true));
    }

    [Theory]
    [InlineData("insert-before(doc/row, 1.5, ())")]
    [InlineData("insert-before((), (), ())")]
    [InlineData("insert-before((), (1, 2), ())")]
    public void InsertionChecksPositionEvenWhenTheResultIsEmpty(string expression)
    {
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => Run(expression, false)).Code);
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => Run(expression, true)).Code);
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData("one-or-more(doc/row)/value", "1|2|3")]
    [InlineData("insert-before(doc/row/value, 2, 'x')", "1|x|2|3")]
    public void SequenceContractsSurviveSavedCompilation(string expression, string expected) =>
        Assert.Equal(expected, Run(expression, true, saved: true));
#endif

    [Fact]
    public void TheExecutableApiConsumesTransmittedNodesIncrementally()
    {
        var executable = new XsltCompiler { Settings = new() { StreamedExecution = true, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(Sheet("insert-before(one-or-more(doc/row)/value, 2, 'x')")));
        using var input = XmlReader.Create(new StringReader(Input));
        Assert.Equal("1|x|2|3", string.Concat(executable.CreateTransformer().ApplyTemplates(input)));
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Theory]
    [InlineData("one-or-more(doc/row/value)", "1")]
    [InlineData("insert-before(doc/row/value, 1, 'x')", "x")]
    public void AConsumerCanObserveTheFirstItemBeforeTheInputEnds(string expression, string expected)
    {
        string sheet = SheetBody($"<xsl:for-each select=\"{SecurityElement.Escape(expression)}\"><xsl:message select=\"string(.)\"/></xsl:for-each>");
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(sheet)), new() { StreamedExecution = true, StreamingBufferCapacity = 1 }, null);
        using var input = XmlReader.Create(new StringReader(Input));
        bool first = true;
        var arguments = new XsltArgumentList();
        arguments.XsltMessageEncountered += (_, message) => {
            if (!first) return;
            Assert.Equal(expected, message.Message);
            Assert.False(input.EOF);
            first = false;
        };
        using var output = new StringWriter();
        transform.Transform(input, arguments, output);
        Assert.False(first);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    private static string Sheet(string expression) => SheetBody($"<xsl:value-of select=\"{SecurityElement.Escape(expression)}\" separator=\"|\"/>");

    private static string SheetBody(string body) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/">{{body}}</xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string expression, bool forward, bool saved = false)
    {
        var transform = new XslCompiledTransform();
        var settings = new XsltSettings { StreamedExecution = forward, StreamingBufferCapacity = 1 };
        using var stylesheet = XmlReader.Create(new StringReader(Sheet(expression)));
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(stylesheet, settings, null, "SequenceContract", "Saved.SequenceContract", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.SequenceContract", true)!);
#endif
        }
        else transform.Load(stylesheet, settings, null);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }
}
