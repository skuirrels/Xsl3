// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class AstRewriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeTryBodiesKeepTheirCatchChildren(bool forward) => Run(forward, saved: false);

#if NET9_0_OR_GREATER
    [Fact]
    public void LargeTryBodiesSurviveSavedCompilation()
    {
        Run(forward: false, saved: true);
        Run(forward: true, saved: true);
    }
#endif

    private static void Run(bool forward, bool saved)
    {
        // Exceed the AST method-splitting threshold inside the try. Its catch must
        // remain a child of that try even when the sequence constructor is large.
        string branches = string.Concat(Enumerable.Repeat("<xsl:when test='false()'><xsl:value-of select='1'/></xsl:when>", 64));
        string source = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>" +
            "<xsl:mode streamable='yes'/><xsl:output method='text'/><xsl:template match='/'>" +
            "<xsl:try><xsl:choose>" + branches + "<xsl:otherwise><xsl:sequence select='error()'/></xsl:otherwise>" +
            "</xsl:choose><xsl:catch>caught</xsl:catch></xsl:try></xsl:template></xsl:stylesheet>";
        using var stylesheet = XmlReader.Create(new StringReader(source));
        var transform = new XslCompiledTransform();
        var settings = new XsltSettings { StreamedExecution = forward };
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(stylesheet, settings, null, "LargeTry", "Saved.LargeTry", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.LargeTry", true)!);
#endif
        }
        else transform.Load(stylesheet, settings, null);
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("caught", output.ToString());
    }
}
