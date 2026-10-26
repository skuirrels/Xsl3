// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Text;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedRecursiveFunctionTests
{
    private const string Input = "<doc><record><a>one</a><b>two</b><c>three</c></record><record><a>four</a><b>five</b></record></doc>";
    private const string Odd = """
        <xsl:function name="f:odd" as="node()*" streamability="shallow-descent">
          <xsl:param name="input" as="element()"/><xsl:param name="level" as="xs:integer"/>
          <xsl:sequence select="if ($level=0) then $input/*[position() mod 2 = 1] else $input/* ! f:odd(., $level - 1)"/>
        </xsl:function>
        """;
    private const string Peek = """
        <xsl:function name="f:peek" as="node()*" streamability="shallow-descent">
          <xsl:param name="input" as="element()"/>
          <xsl:variable name="empty" as="element()"><empty/></xsl:variable>
          <xsl:sequence select="if (has-children($input)) then $input else f:peek($empty)"/>
        </xsl:function>
        """;

    private static string Sheet(string functions, string body) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:f="urn:test" exclude-result-prefixes="xs f">
          <xsl:mode streamable="yes"/><xsl:output omit-xml-declaration="yes"/>
          {{functions}}<xsl:template match="/">{{body}}</xsl:template>
        </xsl:stylesheet>
        """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RecursiveChildrenAreConsumedBeforeTheirNextSibling(bool debug, bool modern)
    {
        string sheet = Sheet(Odd, "<out><xsl:copy-of select='f:odd(doc, 1)'/></out>");
        const string expected = "<out><a>one</a><c>three</c><a>four</a></out>";
        Assert.Equal(expected, Run(sheet, Input, false, debug, modern));
        Assert.Equal(expected, Run(sheet, Input, true, debug, modern));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InspectionAndTransmissionShareTheLiveSingleton(bool debug, bool modern)
    {
        string sheet = Sheet(Peek, "<out><xsl:copy-of select='f:peek(doc)'/></out>");
        Assert.Equal("<out>" + Input + "</out>", Run(sheet, Input, true, debug, modern));
    }

    [Theory]
    [InlineData("element()", "<outer/>", false)]
    [InlineData("element()", "<outer><doc/><doc/></outer>", false)]
    [InlineData("element(other)", "<outer><doc/></outer>", false)]
    [InlineData("element()", "<outer/>", true)]
    [InlineData("element()", "<outer><doc/><doc/></outer>", true)]
    [InlineData("element(other)", "<outer><doc/></outer>", true)]
    public void RecursiveEntryRetainsItsArgumentContract(string parameter, string xml, bool modern)
    {
        string functions = Odd.Replace("as=\"element()\"", "as=\"" + parameter + "\"");
        string sheet = Sheet(functions, "<xsl:copy-of select='f:odd(outer/doc, 0)'/>");
        string error = modern
            ? Assert.Throws<XsltDynamicException>(() => Run(sheet, xml, true, modern: true)).Message
            : Assert.Throws<Xdm.XdmException>(() => Run(sheet, xml, true)).Message;
        Assert.Contains("XPTY0004", error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecursiveReturnChecksRetainTheirErrorCode(bool modern)
    {
        string functions = Odd.Replace("as=\"node()*\"", "as=\"element(a)*\"");
        string sheet = Sheet(functions, "<xsl:copy-of select='f:odd(doc, 1)'/>");
        string error = modern
            ? Assert.Throws<XsltDynamicException>(() => Run(sheet, Input, true, modern: true)).Message
            : Assert.Throws<Xdm.XdmException>(() => Run(sheet, Input, true)).Message;
        Assert.Contains("XTTE0780", error);
    }

    [Fact]
    public void MutualRecursionKeepsEachSuspendedInvocationSeparate()
    {
        string functions = Odd.Replace("f:odd(.,", "f:even(.,") + Odd.Replace("name=\"f:odd\"", "name=\"f:even\"");
        string sheet = Sheet(functions, "<out><xsl:copy-of select='f:odd(doc, 1)'/></out>");
        Assert.Equal("<out><a>one</a><c>three</c><a>four</a></out>", Run(sheet, Input, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecursiveOutputStartsBeforeEofWithBoundedSourceHeaders(bool debug)
    {
        const int records = 5000;
        var xml = new StringBuilder("<doc>");
        for (int i = 0; i < records; i++) xml.Append("<record><a>").Append(i).Append("</a><b>skip</b></record>");
        xml.Append("</doc>");
        string sheet = Sheet(Odd, "<xsl:for-each select='f:odd(doc, 1)'><xsl:message select='string(.)'/></xsl:for-each>");
        var transform = new XslCompiledTransform(enableDebug: debug);
        transform.Load(XmlReader.Create(new StringReader(sheet)), new() { StreamedExecution = true, StreamingBufferCapacity = 1 }, null);
        using var input = XmlReader.Create(new StringReader(xml.ToString()));
        using var document = new Streaming.StreamedDocument(input);
        int received = 0, maximumHeaders = 0;
        var arguments = new XsltArgumentList();
        arguments.XsltMessageEncountered += (_, message) => {
            Assert.Equal(received.ToString(System.Globalization.CultureInfo.InvariantCulture), message.Message);
            if (received == 0) Assert.False(input.EOF);
            received++;
            maximumHeaders = System.Math.Max(maximumHeaders, document.RetainedNodeCount);
        };
        using var output = new StringWriter();
        transform.Transform(document, arguments, output);
        Assert.Equal(records, received);
        // Five live node levels plus one lookahead chain in this fixture.
        Assert.InRange(maximumHeaders, 1, 10);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedFunctionsResumeTheirRecursiveResults(bool debug)
    {
        string sheet = Sheet(Odd, "<out><xsl:copy-of select='f:odd(doc, 1)'/></out>");
        Assert.Equal("<out><a>one</a><c>three</c><a>four</a></out>", Run(sheet, Input, true, debug, saved: true));
    }
#endif

    private static string Run(string sheet, string xml, bool forward, bool debug = false, bool modern = false, bool saved = false)
    {
        var settings = new XsltSettings { StreamedExecution = forward, StreamingBufferCapacity = 1 };
        using var input = XmlReader.Create(new StringReader(xml));
        using var output = new StringWriter();
        if (modern)
            new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(input, output);
        else
        {
            var transform = new XslCompiledTransform(enableDebug: debug);
            using var source = XmlReader.Create(new StringReader(sheet));
            if (saved)
            {
#if NET10_0_OR_GREATER
                using var assembly = new MemoryStream();
                transform.CompileToAssembly(source, settings, null, "Xsl3.Tests.RecursiveStream", "Saved.RecursiveStream", assembly);
                transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.RecursiveStream", true)!, settings);
#endif
            }
            else transform.Load(source, settings, null);
            transform.Transform(input, null, output);
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }
}
