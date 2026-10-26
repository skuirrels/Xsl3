// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Reflection;
using System.Xml;
using Xsl3.Xdm;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class StreamedDeferredClimbingTests
{
    private const string Input = "<doc id='R'><rows id='G'><item id='a'><x/></item><item id='b'><y/></item><item id='c'><x/><y/></item></rows></doc>";
    private static string Sheet(string expression) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/"><xsl:value-of select="{{expression}}" separator="|"/></xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string expression, bool forward, bool debug = false, bool saved = false)
    {
        var settings = new XsltSettings { StreamedExecution = forward };
        var transform = new XslCompiledTransform(debug);
        using var style = XmlReader.Create(new StringReader(Sheet(expression)));
        if (saved)
        {
#if NET10_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(style, settings, null, "DeferredClimbing", "Saved.DeferredClimbing", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.DeferredClimbing", true)!, settings);
#else
            throw new InvalidOperationException("Saved compilation requires .NET 10");
#endif
        }
        else transform.Load(style, settings, null);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }

    [Theory]
    [InlineData("doc/rows/item/x/ancestor::*/@id except doc/rows/item/y/ancestor::*/@id", "a")]
    [InlineData("doc/rows/item[1]/x/ancestor::*/@id except doc/rows/item[3]/y/ancestor::*/@id", "a")]
    [InlineData("doc/rows/item/y/ancestor::*/@id except doc/rows/item/x/ancestor::*/@id", "b")]
    [InlineData("(doc/rows/item/x/ancestor::item union doc/rows/item/y/ancestor::doc)/@id", "R|a|c")]
    [InlineData("doc/rows/item/x/ancestor::*[1]/@id except doc/rows/item/y/ancestor::*[1]/@id", "a")]
    [InlineData("subsequence((doc/rows/item/x/ancestor::*/@id except doc/rows/item/missing/ancestor::*/@id), 2, 2)", "G|a")]
    [InlineData("head(doc/rows/item/x/ancestor::*/@id except doc/rows/item/missing/ancestor::*/@id)", "R")]
    [InlineData("count(doc/rows/item/x/ancestor::* except doc/rows/item/y/ancestor::*)", "1")]
    public void LegacyDebugAndNormalPathsKeepDeferredOrdering(string expression, string expected)
    {
        foreach (bool debug in new[] { false, true })
        {
            Assert.Equal(expected, Run(expression, false, debug));
            Assert.Equal(expected, Run(expression, true, debug));
        }
    }

    [Fact]
    public void UnmatchedProjectionPredicatesDoNotRunDuringPotentialRegistration()
    {
        const string expression = "(doc/rows/item/missing/ancestor::*[error()] except doc/rows/item/y/ancestor::*) ! name()";
        Assert.Empty(Run(expression, false));
        Assert.Empty(Run(expression, true));
    }

    [Fact]
    public void MatchedProjectionErrorsAreNotHiddenByTheDeferredQueue()
    {
        const string expression = "(doc/rows/item/x/ancestor::*[error()] except doc/rows/item/y/ancestor::*) ! name()";
        foreach (bool forward in new[] { false, true })
            Assert.Equal("FOER0000", Assert.Throws<XdmException>(() => Run(expression, forward)).Code);
    }

    [Theory]
    [InlineData("doc/rows/item/x/ancestor::*/@id except doc/rows/item/y/ancestor::*/@id", "a")]
    [InlineData("(doc/rows/item/x/ancestor::item union doc/rows/item/y/ancestor::doc)/@id", "R|a|c")]
    public void ExplicitReplayDoesNotMakeMembershipFinalBeforeItsScan(string expression, string expected)
    {
        using var input = XmlReader.Create(new StringReader(Input));
        using var document = new StreamedDocument(input);
        var source = ((StreamedNavigator)document.CreateNavigator()).BufferSubtree();
        var transform = new XslCompiledTransform();
        using var sheet = XmlReader.Create(new StringReader(Sheet(expression)));
        transform.Load(sheet, new XsltSettings { StreamedExecution = true }, null);
        using var output = new StringWriter();
        transform.Transform(source, null, output);
        Assert.Equal(expected, output.ToString());
    }

    [Theory]
    [InlineData("<xsl:fork><xsl:sequence><xsl:value-of select='doc/rows/item/x/ancestor::*/@id except doc/rows/item/y/ancestor::*/@id'/></xsl:sequence><xsl:sequence><xsl:value-of select='doc/rows/item/x/ancestor::*/@id except doc/rows/item/y/ancestor::*/@id'/></xsl:sequence></xsl:fork>", "aa")]
    [InlineData("<xsl:for-each select='doc/rows/item'><xsl:value-of select='x/ancestor::item/@id except y/ancestor::item/@id'/></xsl:for-each>", "a")]
    [InlineData("<xsl:try><xsl:value-of select='doc/rows/item/x/ancestor::*/@id except doc/rows/item/missing/ancestor::*/@id'/><xsl:sequence select='error()'/><xsl:catch>caught</xsl:catch></xsl:try>", "caught")]
    public void ForksLocalInvocationsAndRollbackKeepTheirOwnDeferredState(string body, string expected)
    {
        string sheet = Sheet("()").Replace("<xsl:value-of select=\"()\" separator=\"|\"/>", body);
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XslCompiledTransform(debug);
            using var style = XmlReader.Create(new StringReader(sheet));
            transform.Load(style, new XsltSettings { StreamedExecution = forward, StreamingBufferCapacity = 1 }, null);
            using var input = XmlReader.Create(new StringReader(Input));
            using var output = new StringWriter();
            using var cancellation = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            transform.Transform(input, new XsltArgumentList { CancellationToken = cancellation.Token }, output);
            Assert.Equal(expected, output.ToString());
            Assert.NotEqual(ReadState.Closed, input.ReadState);
        }
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedAssembliesResumeEveryQueuedResult(bool debug)
    {
        const string expression = "doc/rows/item/x/ancestor::*/@id except doc/rows/item/missing/ancestor::*/@id";
        Assert.Equal("R|G|a|c", Run(expression, true, debug, saved: true));
    }
#endif
}
