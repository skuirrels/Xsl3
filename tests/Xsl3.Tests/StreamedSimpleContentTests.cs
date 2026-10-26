// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using System.Xml.Schema;
using Xunit;

namespace Xsl3.Tests;

public class StreamedSimpleContentTests
{
    private sealed class ObservingWriter(System.Action first) : StringWriter
    {
        private bool _seen;
        private void Observe(int count) { if (!_seen && count != 0) { _seen = true; first(); } }
        public override void Write(string? value) { Observe(value?.Length ?? 0); base.Write(value); }
        public override void Write(char[] buffer, int index, int count) { Observe(count); base.Write(buffer, index, count); }
        public override void Write(System.ReadOnlySpan<char> buffer) { Observe(buffer.Length); base.Write(buffer); }
        public override void Write(char value) { Observe(1); base.Write(value); }
    }

    private static string Sheet(string body) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/">{{body}}</xsl:template>
        </xsl:stylesheet>
        """;

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(false, false, 2)]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 2)]
    public void AValueOfPopulationWritesBeforeItExhaustsTheInput(bool debug, bool modern, int wrapper)
    {
        var xml = new System.Text.StringBuilder("<doc>");
        var expected = new System.Text.StringBuilder();
        for (int i = 0; i < 5000; i++)
        {
            string value = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            xml.Append("<record>").Append(value).Append("</record>");
            if (i != 0) expected.Append('|');
            expected.Append(value);
        }
        xml.Append("</doc>");
        const string body = "<xsl:value-of select='doc/record' separator='|'/>";
        string content = wrapper switch {
            1 => "<xsl:choose><xsl:when test=\"system-property('xsl:version')='3.0'\">" + body + "</xsl:when><xsl:otherwise>" + body + "</xsl:otherwise></xsl:choose>",
            2 => "<xsl:for-each select='.'>" + body + "</xsl:for-each>",
            _ => body,
        };
        string sheet = Sheet(content);
        using var input = XmlReader.Create(new StringReader(xml.ToString()));
        bool emitted = false;
        using var output = new ObservingWriter(() => { Assert.False(input.EOF); emitted = true; });
        var settings = new XsltSettings { StreamedExecution = true, StreamingBufferCapacity = 1 };
        if (modern) new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(input, output);
        else
        {
            var transform = new XslCompiledTransform(enableDebug: debug);
            transform.Load(XmlReader.Create(new StringReader(sheet)), settings, null);
            transform.Transform(input, null, output);
        }
        Assert.True(emitted);
        Assert.Equal(expected.ToString(), output.ToString());
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Theory]
    [InlineData("<xsl:variable name='v' as='text()'><xsl:value-of select='doc/record' separator='|'/></xsl:variable><xsl:value-of select='count($v)'/><xsl:text>;</xsl:text><xsl:value-of select='$v instance of text()'/><xsl:text>;</xsl:text><xsl:sequence select='$v'/>", "1;true;a|b")]
    [InlineData("<xsl:variable name='v' as='text()'><xsl:value-of select='()'/></xsl:variable><xsl:value-of select='count($v)'/><xsl:text>;</xsl:text><xsl:value-of select='$v instance of text()'/>", "1;true")]
    [InlineData("<xsl:variable name='v' as='node()*'><xsl:value-of select='doc/record' separator='|'/><xsl:value-of select=\"'z'\"/></xsl:variable><xsl:value-of select='count($v)'/><xsl:text>;</xsl:text><xsl:value-of select='$v[1] is $v[2]'/><xsl:text>;</xsl:text><xsl:value-of select=\"string-join($v, '#')\"/>", "2;false;a|b#z")]
    [InlineData("<xsl:try><xsl:value-of select='(doc/record, map{})' separator='|'/><xsl:catch errors='*'>caught</xsl:catch></xsl:try>", "caught")]
    public void IncrementalWritingPreservesTextNodeBoundariesAndRollback(string body, string expected)
    {
        foreach (bool debug in new[] { false, true })
        {
            var transform = new XslCompiledTransform(enableDebug: debug);
            transform.Load(XmlReader.Create(new StringReader(Sheet(body))), new() { StreamedExecution = true }, null);
            using var input = XmlReader.Create(new StringReader("<doc><record>a</record><record>b</record></doc>"));
            using var output = new StringWriter();
            transform.Transform(input, null, output);
            Assert.Equal(expected, output.ToString());
        }
    }

    [Theory]
    [InlineData("''", "a||b")]
    [InlineData("[2]", "a|2|b")]
    [InlineData("[2, 3]", "a|2|3|b")]
    [InlineData("[[], [2, 3]]", "a|2|3|b")]
    [InlineData("[]", "a|b")]
    public void AtomizedItemsPreserveTextBoundaries(string replacement, string expected)
    {
        string sheet = Sheet($"<xsl:value-of select=\"doc/node() ! (if (self::boundary) then {replacement} else .)\" separator='|'/>");
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(sheet)).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader("<doc>a<boundary/>b</doc>"));
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(reader)));
        }
    }

    [Fact]
    public void UntypedNodesAreConsumedBeforeAdvancingToTheNextSibling()
    {
        string sheet = Sheet("<xsl:for-each select='doc/record'><xsl:value-of select='@id'/><xsl:text>|</xsl:text><xsl:value-of select='label' separator='|'/><xsl:text>;</xsl:text></xsl:for-each>");
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(sheet)).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader("<doc><record id='1'><label>a<!--split-->b</label><label>c</label></record><record id='2'><label>d</label></record></doc>"));
            Assert.Equal("1|ab|c;2|d;", string.Concat(transform.ApplyTemplates(reader)));
        }
    }

    [Theory]
    [InlineData("map{}")]
    [InlineData("[map{}]")]
    public void NonAtomizableItemsStillReportTheCanonicalError(string select)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet($"<xsl:value-of select='{select}'/>"))).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader("<doc/>"));
            Assert.Contains("FOTY0013", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(reader)).Message);
        }
    }

    [Fact]
    public void TypedListValuesKeepEveryAtomAndItsSeparator()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader("""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:simpleType name="integers"><xs:list itemType="xs:integer"/></xs:simpleType>
              <xs:element name="doc"><xs:complexType><xs:sequence>
                <xs:element name="record" type="integers" maxOccurs="unbounded"/>
              </xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """)));
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { SchemaAware = true, StreamedExecution = forward } }
                .Compile(new StringReader(Sheet("<xsl:value-of select='doc/record' separator='|'/>"))).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader("<doc><record>1 2</record><record/><record>3 4</record></doc>"),
                new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas });
            Assert.Equal("1|2|3|4", string.Concat(transform.ApplyTemplates(reader)));
        }
    }

#if NET9_0_OR_GREATER
    [Fact]
    public void SavedStreamedSimpleContentKeepsTheSameOutput()
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(Sheet("<xsl:value-of select='doc/record' separator='|'/>"))),
            settings, null, "SavedSimpleContent", "Saved.SimpleContent", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.SimpleContent", true)!, settings);
        using var input = XmlReader.Create(new StringReader("<doc><record>a</record><record>b</record></doc>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("a|b", output.ToString());
    }
#endif

    [Fact]
    public void AnEmptyArrayStillSeparatesOriginallyNonAdjacentText()
    {
        string sheet = Sheet("<xsl:value-of select='doc/node() ! (if (self::empty) then [] else .)' separator='|'/>");
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet)).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader("<doc>a<empty/>b</doc>"));
            Assert.Equal("a|b", string.Concat(transform.ApplyTemplates(reader)));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EmptyTypedValuesStillSeparateOriginallyNonAdjacentText(bool nilled, bool avt)
    {
        const string schema = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:simpleType name="integers"><xs:list itemType="xs:integer"/></xs:simpleType>
              <xs:element name="doc"><xs:complexType mixed="true"><xs:sequence>
                <xs:element name="empty" type="integers" nillable="true"/>
              </xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """;
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader(schema)));
        string input = nilled ? "<doc>a<empty xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:nil='true'/>b</doc>"
            : "<doc>a<empty/>b</doc>";
        // The literal attribute exercises the AVT path independently of xsl:attribute's simple content.
        string body = avt ? "<out v='{doc/node()}'/>" : "<xsl:value-of select='doc/node()' separator='|'/>";
        string sheet = Sheet(body);
        if (avt) sheet = sheet.Replace("method=\"text\"", "method='xml' omit-xml-declaration='yes'");
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { SchemaAware = true, StreamedExecution = forward } }
                .Compile(new StringReader(sheet)).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader(input), new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas });
            using var output = new StringWriter();
            transform.ApplyTemplates(reader, output);
            if (avt) Assert.Contains("v=\"a b\"", output.ToString());
            else Assert.Equal("a|b", output.ToString());
        }
    }
}
