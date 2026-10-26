// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class MaterializedMergeTests
{
    private const string Input = "<doc><n k='1'/><n k='2'/><n k='3'/></doc>";
    private const string Source = "<xsl:merge-source select='doc/n'><xsl:merge-key select='@k'/></xsl:merge-source>";
    private static string Sheet(string body) => "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:output omit-xml-declaration='yes'/><xsl:template match='/'>" + body + "</xsl:template></xsl:stylesheet>";

    private static void Run(string sheet, XmlWriter output, bool debug, bool legacy, Action<string>? message = null)
    {
        using var input = XmlReader.Create(new StringReader(Input));
        if (legacy)
        {
            var transform = new XslCompiledTransform(debug);
            transform.Load(XmlReader.Create(new StringReader(sheet)), new XsltSettings(), null);
            var args = new XsltArgumentList();
            args.XsltMessageEncountered += (_, e) => message?.Invoke(e.Message);
            transform.Transform(input, args, output);
        }
        else
        {
            var transform = new XsltCompiler { Settings = new XsltSettings { IncludeDebugInformation = debug } }.Compile(new StringReader(sheet)).CreateTransformer();
            transform.MessageHandler = message;
            transform.ApplyTemplates(input, output);
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MergeActionSequencePreservesNodeIdentity(bool debug, bool legacy)
    {
        string body = "<xsl:variable name='merged' as='node()*'><xsl:merge>" + Source
            + "<xsl:merge-action><xsl:sequence select='current-merge-group()'/></xsl:merge-action></xsl:merge></xsl:variable>"
            + "<out><xsl:value-of select='$merged[1] is doc/n[1]'/></out>";
        using var text = new StringWriter();
        using var writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true });
        Run(Sheet(body), writer, debug, legacy); writer.Flush();
        Assert.Equal("<out>true</out>", text.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EachActionEmitsBeforeTheFollowingActionRuns(bool debug, bool legacy)
    {
        string body = "<out><xsl:merge>" + Source
            + "<xsl:merge-action><xsl:message select='position()'/><g><xsl:value-of select='position()'/><xsl:text>/</xsl:text><xsl:value-of select='last()'/></g></xsl:merge-action>"
            + "</xsl:merge></out>";
        var messages = new List<string>(); var firstMessageCounts = new List<int>();
        using var text = new StringWriter();
        using var writer = new ObservingWriter(text, value => { if (value == "g") firstMessageCounts.Add(messages.Count); });
        Run(Sheet(body), writer, debug, legacy, messages.Add); writer.Flush();
        Assert.Equal(new[] { "1", "2", "3" }, messages);
        Assert.Equal(new[] { 1, 2, 3 }, firstMessageCounts);
        Assert.Equal("<out><g>1/3</g><g>2/3</g><g>3/3</g></out>", text.ToString());
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedAssemblyPreservesGroupFocusAndSize(bool debug)
    {
        string sheet = Sheet("<out><xsl:merge>" + Source
            + "<xsl:merge-action><g p='{position()}' n='{last()}' k='{current-merge-key()}' id='{@k}'/></xsl:merge-action></xsl:merge></out>");
        using var assembly = new MemoryStream(); var settings = new XsltSettings();
        new XslCompiledTransform(debug).CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null,
            "MaterializedMerge" + Guid.NewGuid().ToString("N"), "Saved.MaterializedMerge", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.MaterializedMerge", true)!, settings);
        using var input = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("<out><g p=\"1\" n=\"3\" k=\"1\" id=\"1\"/><g p=\"2\" n=\"3\" k=\"2\" id=\"2\"/><g p=\"3\" n=\"3\" k=\"3\" id=\"3\"/></out>", output.ToString());
    }

#endif

    private sealed class ObservingWriter(TextWriter text, Action<string> observe) : XmlTextWriter(text)
    {
        public override void WriteStartElement(string? prefix, string localName, string? ns)
        {
            observe(localName);
            base.WriteStartElement(prefix, localName, ns);
        }
    }
}
