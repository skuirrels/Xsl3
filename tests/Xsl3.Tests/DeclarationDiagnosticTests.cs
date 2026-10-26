// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Linq;
using Xunit;

namespace Xsl3.Tests;

public class DeclarationDiagnosticTests
{
    private const string Invalid = "<xsl:attribute-set name='a' streamable='yes' use-attribute-sets='b c'><xsl:attribute name='a' select='1'/></xsl:attribute-set>"
        + "<xsl:attribute-set name='b' streamable='yes'><xsl:attribute name='b' select='1'/></xsl:attribute-set>"
        + "<xsl:attribute-set name='c' streamable='no'><xsl:attribute name='c' select='1'/></xsl:attribute-set>";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReferenceContractReportsItsSpecificError(bool fallback)
    {
        XsltStaticException error = CompileError(Invalid, fallback);
        Assert.Equal("XTSE0730", error.Errors.First(e => !e.IsWarning).Code?.LocalName);
        Assert.DoesNotContain(error.Errors, e => e.Code?.LocalName == "XTSE3430");
    }

    [Fact]
    public void ASeparateNonMotionlessDeclarationKeepsItsStreamabilityDiagnostic()
    {
        const string consuming = "<xsl:attribute-set name='d' streamable='yes'><xsl:attribute name='d' select='string(*)'/></xsl:attribute-set>";
        XsltStaticException error = CompileError(Invalid + consuming);
        Assert.Contains(error.Errors, e => e.Code?.LocalName == "XTSE0730");
        Assert.Contains(error.Errors, e => e.Code?.LocalName == "XTSE3430");
    }

    [Fact]
    public void IndependentXPathErrorsAreStillAssessed()
    {
        XsltStaticException error = CompileError(Invalid.Replace("name='a' select='1'", "name='a' select='$missing'"));
        Assert.Contains(error.Errors, e => e.Code?.LocalName == "XTSE0730");
        Assert.Contains(error.Errors, e => e.Code?.LocalName == "XPST0008");
    }

    [Fact]
    public void AValidReferenceWithAConsumingBodyStillReports3430()
    {
        const string declarations = "<xsl:attribute-set name='a' streamable='yes' use-attribute-sets='b'><xsl:attribute name='a' select='string(*)'/></xsl:attribute-set>"
            + "<xsl:attribute-set name='b' streamable='yes'><xsl:attribute name='b' select='1'/></xsl:attribute-set>";
        Assert.Contains(CompileError(declarations).Errors, e => e.Code?.LocalName == "XTSE3430");
    }

    private static XsltStaticException CompileError(string declarations, bool fallback = false)
    {
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>" + declarations
            + "<xsl:template name='main'><out/></xsl:template></xsl:stylesheet>";
        return Assert.Throws<XsltStaticException>(() => new XsltCompiler
            { Settings = new() { Streamability = fallback ? XsltStreamabilityMode.Fallback : XsltStreamabilityMode.Strict } }
            .Compile(new StringReader(sheet)));
    }
}
