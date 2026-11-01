// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public sealed class DirectStringJoinTests
{
    private const string Input = "<doc><row><a>before<b>nested</b>after</a><c>end</c></row></doc>";

    private static string Sheet(string select) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/"><xsl:value-of select="{{select}}"/></xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string select, bool forward, XmlReader input, bool schemaAware = false, bool debug = false)
    {
        var transform = new XslCompiledTransform(debug);
        using var stylesheet = XmlReader.Create(new StringReader(Sheet(select)));
        transform.Load(stylesheet, new XsltSettings { StreamedExecution = forward, SchemaAware = schemaAware }, null);
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        return output.ToString();
    }

    [Theory]
    [InlineData("string-join(doc/row/descendant::*, '|')", "beforenestedafter|nested|end")]
    [InlineData("string-join(doc/row/descendant::*)", "beforenestedafternestedend")]
    [InlineData("string-join((), '|')", "")]
    [InlineData("string-join((9007199254740993, true(), xs:decimal('1.25')), '|')", "9007199254740993|true|1.25")]
    [InlineData("string-join([1, [2, 3], ()], '|')", "1|2|3")]
    [InlineData("string-join(('one', '', 'two'), '')", "onetwo")]
    [InlineData("string-join(('one', '', 'two'), '&#x1F600;')", "one😀😀two")]
    public void DirectJoinsKeepValuesTypesArraysAndOverlappingDescendants(string select, string expected)
    {
        foreach (bool forward in new[] { false, true })
        foreach (bool debug in new[] { false, true })
        {
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal(expected, Run(select, forward, input, debug: debug));
        }
    }

    [Theory]
    [InlineData("string-join((1, 2), ())", "XPTY0004")]
    [InlineData("string-join((1, 2), 1)", "XPTY0004")]
    [InlineData("string-join((1, 2), ('a', 'b'))", "XPTY0004")]
    [InlineData("string-join((1, map{}), '|')", "FOTY0013")]
    public void ConversionAndCardinalityErrorsAreRetained(string select, string code)
    {
        foreach (bool forward in new[] { false, true })
        {
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal(code, Assert.Throws<XdmException>(() => Run(select, forward, input)).Code);
        }
    }

    [Fact]
    public void TypedListsAndNilledElementsKeepTheirAtomizedCardinality()
    {
        const string schema = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:simpleType name="integers"><xs:list itemType="xs:integer"/></xs:simpleType>
              <xs:element name="doc"><xs:complexType><xs:sequence>
                <xs:element name="value" type="integers" nillable="true" maxOccurs="unbounded"/>
              </xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """;
        var schemas = new XmlSchemaSet();
        using (var definition = XmlReader.Create(new StringReader(schema))) schemas.Add(null, definition);
        const string xml = "<doc xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'><value>9007199254740993 2</value><value xsi:nil='true'/></doc>";
        foreach (bool forward in new[] { false, true })
        {
            using var input = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas });
            Assert.Equal("9007199254740993|2", Run("string-join(doc/value, '|')", forward, input, schemaAware: true));
        }
    }

    [Theory]
    [InlineData("string-join(doc/value, '|')", "FOTY0012")]
    [InlineData("doc/separator ! string-join(('a', 'b'), .)", "XPTY0004")]
    public void SchemaContentAndTypedSeparatorsRetainTheirErrors(string select, string code)
    {
        const string schema = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="doc"><xs:complexType><xs:sequence>
                <xs:element name="value"><xs:complexType><xs:sequence>
                  <xs:element name="child" type="xs:string"/>
                </xs:sequence></xs:complexType></xs:element>
                <xs:element name="separator" type="xs:integer"/>
              </xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """;
        var schemas = new XmlSchemaSet();
        using (var definition = XmlReader.Create(new StringReader(schema))) schemas.Add(null, definition);
        foreach (bool forward in new[] { false, true })
        {
            using var input = XmlReader.Create(new StringReader("<doc><value><child>x</child></value><separator>1</separator></doc>"),
                new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas });
            Assert.Equal(code, Assert.Throws<XdmException>(() => Run(select, forward, input, schemaAware: true)).Code);
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_command")]
    private static extern ref XmlILCommand? Command(XslCompiledTransform transform);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStreamedJoinWithAKnownSeparatorDoesNotEmitNumericBuiltinIds(bool debug)
    {
        var transform = new XslCompiledTransform(debug);
        using var stylesheet = XmlReader.Create(new StringReader(Sheet("string-join(doc/row/descendant::*, '|')")));
        transform.Load(stylesheet, new XsltSettings { StreamedExecution = true }, null);
        Assert.False(Command(transform)!.StaticData.UsesBuiltinFunctionIds);
    }

    [Fact]
    public void TheDirectEntryPointHonorsCancellation()
    {
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true };
        var runtime = new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter());
        CancellationToken previous = XmlQueryRuntime.Cancellation;
        using var cancellation = new CancellationTokenSource();
        try
        {
            cancellation.Cancel(); XmlQueryRuntime.Cancellation = cancellation.Token;
            Assert.Throws<OperationCanceledException>(() => runtime.XsltFunctions.X31StringJoin(Array.Empty<System.Xml.XPath.XPathItem>(), "|"));
        }
        finally { XmlQueryRuntime.Cancellation = previous; }
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedJoinsKeepTheDirectEntryPointAndOverlappingValues(bool debug)
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var image = new MemoryStream();
        using var stylesheet = XmlReader.Create(new StringReader(Sheet("string-join(doc/row/descendant::*, '|')")));
        new XslCompiledTransform(debug).CompileToAssembly(stylesheet, settings, null, "DirectStringJoin", "Saved.Join", image);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(image.ToArray()).GetType("Saved.Join", true)!, settings);
        Assert.False(Command(transform)!.StaticData.UsesBuiltinFunctionIds);
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("beforenestedafter|nested|end", output.ToString());
    }
#endif
}
