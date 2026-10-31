// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Threading;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class LiteralSimpleContentTests
{
    private static string Sheet(string body) => "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:err='http://www.w3.org/2005/xqt-errors' exclude-result-prefixes='err'><xsl:mode streamable='yes'/><xsl:output omit-xml-declaration='yes'/><xsl:template match='/'>" + body + "</xsl:template></xsl:stylesheet>";

    private static string Run(string body, bool debug, bool forward, int delivery)
    {
        string sheet = Sheet(body);
        var settings = new XsltSettings { StreamedExecution = forward, IncludeDebugInformation = debug };
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var output = new StringWriter();
        if (delivery == 1)
            new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(input, output);
        else
        {
            var transform = new XslCompiledTransform(debug);
#if NET10_0_OR_GREATER
            if (delivery == 2)
            {
                using var image = new MemoryStream();
                transform.CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null,
                    "LiteralContent" + Guid.NewGuid().ToString("N"), "Saved.Content", image);
                transform.Load(System.Reflection.Assembly.Load(image.ToArray()).GetType("Saved.Content", true)!, settings);
            }
            else
#endif
                transform.Load(XmlReader.Create(new StringReader(sheet)), settings, null);
            transform.Transform(input, null, output);
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LiteralTextKeepsConstructionAndJoiningSemantics(bool debug, bool forward)
    {
        var cases = new (string Body, string Expected)[] {
            ("<xsl:variable name='v' as='node()*'><xsl:value-of><xsl:text>a</xsl:text></xsl:value-of><xsl:value-of><xsl:text>b</xsl:text></xsl:value-of></xsl:variable><out n='{count($v)}' same='{$v[1] is $v[2]}'><xsl:value-of select=\"string-join($v,'|')\"/></out>", "<out n=\"2\" same=\"false\">a|b</out>"),
            ("<out><xsl:attribute name='a' separator='|'><xsl:text>one</xsl:text></xsl:attribute><xsl:comment><xsl:text>comment</xsl:text></xsl:comment><xsl:processing-instruction name='p'><xsl:text>data</xsl:text></xsl:processing-instruction><xsl:value-of separator='|'><xsl:text>text</xsl:text></xsl:value-of></out>", "<out a=\"one\"><!--comment--><?p data?>text</out>"),
            ("<xsl:variable name='v' as='text()'><xsl:value-of><xsl:text/></xsl:value-of></xsl:variable><out n='{count($v)}' text='{$v instance of text()}'/>", "<out n=\"1\" text=\"true\"/>"),
            ("<xsl:value-of separator='|'><xsl:value-of select=\"('a','b')\" separator=':'/></xsl:value-of>", "a:b"),
            ("<xsl:value-of><xsl:text disable-output-escaping='yes'>&lt;b&gt;</xsl:text></xsl:value-of>", "&lt;b&gt;"),
            ("<xsl:value-of separator='|'><xsl:text>a</xsl:text><xsl:sequence select=\"''\"/><xsl:text>b</xsl:text></xsl:value-of>", "a||b"),
            ("<xsl:try><xsl:value-of separator=\"{error(QName('urn:content','separator'), 'separator')}\"><xsl:text>one</xsl:text></xsl:value-of><xsl:catch errors='*'><xsl:value-of select='local-name-from-QName($err:code)'/></xsl:catch></xsl:try>", "separator"),
            ("<out><xsl:try><xsl:attribute name='a'><xsl:value-of select=\"error(QName('urn:content','content'), 'content')\"/></xsl:attribute><xsl:catch errors='*'><xsl:attribute name='caught' select='local-name-from-QName($err:code)'/></xsl:catch></xsl:try></out>", "<out caught=\"content\"/>")
        };
        int deliveries = 2;
#if NET10_0_OR_GREATER
        deliveries = 3;
#endif
        for (int delivery = 0; delivery < deliveries; delivery++)
            foreach (var item in cases)
                Assert.Equal(item.Expected, Run(item.Body, debug, forward, delivery));
    }

    private sealed class CancellingWriter(CancellationTokenSource cancellation) : StringWriter
    {
        private void CancelAfterOutput() { if (GetStringBuilder().Length != 0) cancellation.Cancel(); }
        public override void Write(char[] buffer, int index, int count) { base.Write(buffer, index, count); CancelAfterOutput(); }
        public override void Write(ReadOnlySpan<char> buffer) { base.Write(buffer); CancelAfterOutput(); }
        public override void Write(string? value) { base.Write(value); CancelAfterOutput(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiteralContentStillChecksCancellationInsideAnAlreadyRunningLoop(bool debug)
    {
        string sheet = Sheet("<xsl:for-each select='1 to 10000'><row><xsl:attribute name='a'><xsl:text>value</xsl:text></xsl:attribute></row></xsl:for-each>");
        var transform = new XslCompiledTransform(debug);
        transform.Load(XmlReader.Create(new StringReader(sheet)), new() { StreamedExecution = true }, null);
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var cancellation = new CancellationTokenSource();
        using var output = new CancellingWriter(cancellation);
        var arguments = new XsltArgumentList { CancellationToken = cancellation.Token };
        var error = Assert.Throws<XslTransformException>(() => transform.Transform(input, arguments, output));
        Assert.Contains("cancelled", error.Message);
        Assert.Equal(cancellation.Token, Assert.IsType<OperationCanceledException>(error.InnerException).CancellationToken);
        Assert.True(output.GetStringBuilder().Length < "<row a=\"value\"/>".Length * 10000);
    }
}
