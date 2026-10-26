// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Reflection;
using System.Xml;
using Xsl3.Xdm;
using Xsl3.XPath31;
using Xunit;

namespace Xsl3.Tests;

public class MapConstructorHostTests
{
    [Theory]
    [InlineData("map{$key:1, 'same':2}")]
    [InlineData("(function(){map{$key:1, 'same':2}})()")]
    [InlineData("map{'outer':map{$key:1, 'same':2}}")]
    public void XsltMapConstructorsUseTheXsltDuplicateKeyError(string expression)
    {
        string body = "<xsl:sequence select=\"" + expression + "\"/>";
        Assert.Equal("caught", Run(body, false));
        Assert.Equal("caught", Run(body, true));
    }

#if NET9_0_OR_GREATER
    [Fact]
    public void SavedStylesheetsRetainTheXsltDuplicateKeyContract() =>
        Assert.Equal("caught", Run("<xsl:sequence select=\"map{$key:1, 'same':2}\"/>", true, saved: true));
#endif

    [Fact]
    public void DynamicEvaluationRetainsTheXsltDuplicateKeyContract() =>
        Assert.Equal("caught", Run("<xsl:evaluate xpath=\"&quot;map{'same':1, 'same':2}&quot;\"/>", true));

    [Fact]
    public void AMapConstructorFailureInsideAForkKeepsItsErrorCode() =>
        Assert.Equal("caught", Run("<xsl:fork><xsl:sequence select=\"map{$key:1, 'same':count(doc/row)}\"/></xsl:fork>", true));

    [Fact]
    public void StandaloneXPathRetainsItsDuplicateKeyError()
    {
        var expression = XPath31Compiler.Compile("map{'same':1, 'same':2}", new XPathStaticContext());
        Assert.Equal("XQDY0137", Assert.Throws<XdmException>(() => expression.Evaluate()).Code);
    }

    private static string Run(string body, bool forward, bool saved = false)
    {
        string stylesheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:err="http://www.w3.org/2005/xqt-errors">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:param name="key" select="'same'"/>
              <xsl:template match="/"><xsl:try>{{body}}<xsl:catch errors="err:XTDE3365">caught</xsl:catch></xsl:try></xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XslCompiledTransform();
        var settings = new XsltSettings { StreamedExecution = forward, EnableEvaluate = true, StreamingBufferCapacity = 1 };
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(XmlReader.Create(new StringReader(stylesheet)), settings, null, "MapHost", "Saved.MapHost", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.MapHost", true)!);
#endif
        }
        else transform.Load(XmlReader.Create(new StringReader(stylesheet)), settings, null);
        using var input = XmlReader.Create(new StringReader("<doc><row/></doc>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        return output.ToString();
    }
}
