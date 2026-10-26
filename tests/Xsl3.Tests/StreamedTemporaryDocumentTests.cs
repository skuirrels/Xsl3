// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedTemporaryDocumentTests
{
    private const string Input = "<doc><row keep='yes'>one</row><row>two</row></doc>";
    private static string Sheet(bool conditional, bool matching, bool sourceDocument)
    {
        string selected = matching ? "//row[@keep]" : "//row[@missing]";
        string document = "<xsl:where-populated><xsl:document><xsl:copy-of select='" + selected + "'/></xsl:document></xsl:where-populated>";
        if (conditional) document = "<xsl:sequence select='$empty'/><xsl:on-empty>" + document + "</xsl:on-empty>";
        string body = "<xsl:variable name='tree' as='document-node()?'>" + document + "</xsl:variable><out empty='{empty($tree)}' rows='{count($tree/row)}'/>";
        if (sourceDocument) body = "<xsl:source-document href='urn:rows' streamable='yes'>" + body + "</xsl:source-document>";
        return "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:mode streamable='yes'/><xsl:param name='empty' select='()'/>"
            + "<xsl:output omit-xml-declaration='yes'/><xsl:template " + (sourceDocument ? "name='xsl:initial-template'" : "match='/'") + ">" + body + "</xsl:template></xsl:stylesheet>";
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void PopulatedTemporaryDocumentsAreGroundedAndCanBeReused(bool conditional, bool matching, bool sourceDocument)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transformer = new XsltCompiler { Settings = new() { StreamedExecution = forward, EnableDocumentFunction = true } }
                .Compile(new StringReader(Sheet(conditional, matching, sourceDocument))).CreateTransformer();
            using var output = new StringWriter();
            var resolver = new RowsResolver(); transformer.DocumentResolver = resolver;
            if (sourceDocument) transformer.CallTemplate(null, output);
            else
            {
                using var input = XmlReader.Create(new StringReader(Input));
                transformer.ApplyTemplates(input, output); Assert.NotEqual(ReadState.Closed, input.ReadState);
            }
            Assert.Equal(matching ? "<out empty=\"false\" rows=\"1\"/>" : "<out empty=\"true\" rows=\"0\"/>", output.ToString());
            foreach (MemoryStream stream in resolver.Streams) Assert.False(stream.CanRead);
        }
    }

    private sealed class RowsResolver : XmlResolver
    {
        internal List<MemoryStream> Streams { get; } = new();
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(Input)); Streams.Add(stream); return stream;
        }
    }
}
