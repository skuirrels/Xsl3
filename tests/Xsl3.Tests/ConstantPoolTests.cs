// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

/// <summary>Tests that read process-wide state, so they don't run alongside the others.</summary>
[CollectionDefinition("Process-wide state", DisableParallelization = true)]
public sealed class ProcessWideState
{
}

[Collection("Process-wide state")]
public class ConstantPoolTests
{
    /// <summary>A stylesheet's constants are its own (Sink B, B2): loading stylesheets doesn't grow the process-wide table.</summary>
    [Fact]
    public void ConstantsBelongToTheStylesheet()
    {
        Xslt30TestsAccess.Run("<xsl:template match='/'><o><xsl:value-of select=\"'warm-up', xs:date('2026-10-21'), 1.5\"/></o></xsl:template>");
        int before = Xsl3.XPath31.ConstantPool.GlobalCount;
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal($"<o>{i} 2026-10-21 1.5 true</o>", Xslt30TestsAccess.Run($"<xsl:template match='/' use-when=\"{i} ge 0\"><o><xsl:value-of select=\"{i}, xs:date('2026-10-21'), 1.5, . instance of document-node()\"/></o></xsl:template>"));
        }
        Assert.Equal(before, Xsl3.XPath31.ConstantPool.GlobalCount);
    }
}

internal static class Xslt30TestsAccess
{
    public static string Run(string templates)
    {
        string stylesheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' exclude-result-prefixes='xs'>" +
                            "<xsl:output method='xml' omit-xml-declaration='yes'/>" + templates + "</xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var sw = new StringWriter();
        transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, sw);
        return sw.ToString();
    }
}
