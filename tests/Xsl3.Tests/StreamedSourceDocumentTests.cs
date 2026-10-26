// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedSourceDocumentTests
{
    private const string Xsl = "http://www.w3.org/1999/XSL/Transform";
    private const string Input = "<doc><record n='1'>1</record><record n='2'>2</record><record n='3'>3</record></doc>";
    private const string ValidationSchema = "<xsl:import-schema><xs:schema>"
        + "<xs:simpleType name='numbers'><xs:list itemType='xs:integer'/></xs:simpleType>"
        + "<xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='record' type='xs:integer' minOccurs='0' maxOccurs='unbounded'/></xs:sequence><xs:attribute name='version' type='xs:integer' default='7'/></xs:complexType></xs:element>"
        + "<xs:element name='value' type='xs:integer' default='9'/><xs:element name='text' type='xs:string'/>"
        + "<xs:element name='lax' type='xs:anyType'/>"
        + "<xs:element name='ids'><xs:complexType><xs:sequence><xs:element name='item' minOccurs='0' maxOccurs='unbounded'><xs:complexType><xs:attribute name='id' type='xs:ID'/><xs:attribute name='ref' type='xs:IDREF'/></xs:complexType></xs:element></xs:sequence></xs:complexType></xs:element>"
        + "<xs:element name='unique'><xs:complexType><xs:sequence><xs:element name='item' type='xs:integer' maxOccurs='unbounded'/></xs:sequence></xs:complexType><xs:unique name='u'><xs:selector xpath='item'/><xs:field xpath='.'/></xs:unique></xs:element>"
        + "</xs:schema></xsl:import-schema>";

    private static Xslt30Transformer Transformer(string body, string declarations = "", bool streamed = true, bool documentFunctions = true, bool schemaAware = false, string inputAnnotations = "")
    {
        string annotations = inputAnnotations.Length == 0 ? "" : $"input-type-annotations='{inputAnnotations}'";
        string sheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:xs='http://www.w3.org/2001/XMLSchema' {annotations}>{declarations}<xsl:output method='text'/>"
            + $"<xsl:template name='xsl:initial-template'>{body}</xsl:template></xsl:stylesheet>";
        return new XsltCompiler { Settings = new() { StreamedExecution = streamed, EnableDocumentFunction = documentFunctions, SchemaAware = schemaAware } }.Compile(new StringReader(sheet)).CreateTransformer();
    }

    private static string Source(string body, string attributes = "", string href = "file:///stream/input.xml") =>
        $"<xsl:source-document href='{href}' streamable='yes' {attributes}>{body}</xsl:source-document>";

    [Theory]
    [InlineData(false, false, "x-15 6y")]
    [InlineData(false, true, "x-15y")]
    [InlineData(true, false, "x-15 6y")]
    [InlineData(true, true, "x-15y")]
    public void AttributeValueTemplatesUseTypedValuesBeforeBackwardsCompatibilityTruncation(bool forward, bool backwards, string expected)
    {
        string declarations = "<xsl:import-schema><xs:schema><xs:simpleType name='values'><xs:list itemType='xs:double'/></xs:simpleType>"
            + "<xs:element name='doc'><xs:complexType><xs:attribute name='values' type='values'/></xs:complexType></xs:element></xs:schema></xsl:import-schema>";
        string version = backwards ? "xsl:version='1.0'" : "";
        var transform = Transformer(Source("<xsl:for-each select='doc'><xsl:variable name='temp'><out " + version
            + " a='x{@values}y'/></xsl:variable><xsl:value-of select='$temp/out/@a'/></xsl:for-each>", "validation='strict'"), declarations,
            streamed: forward, schemaAware: true);
        transform.DocumentResolver = new SourceResolver("<doc values=' -15.00 6.00 '/>");
        Assert.Equal(expected, transform.CallTemplate(null).ToString());
    }

    [Theory]
    [InlineData(false, "<doc>a<!--split-->b</doc>", "ab")]
    [InlineData(true, "<doc>a<!--split-->b</doc>", "ab")]
    [InlineData(false, "<doc> <!--split--> </doc>", "  ")]
    [InlineData(true, "<doc> <!--split--> </doc>", "  ")]
    [InlineData(false, "<doc>a<!--split--> </doc>", "a ")]
    [InlineData(true, "<doc>a<!--split--> </doc>", "a ")]
    [InlineData(false, "<doc xml:space='preserve'> <!--split--> </doc>", "  ")]
    [InlineData(true, "<doc xml:space='preserve'> <!--split--> </doc>", "  ")]
    public void AttributeValueTemplatesMergeAdjacentTextNodesBeforeAtomization(bool forward, string xml, string expected)
    {
        var transform = Transformer(Source("<xsl:for-each select='doc'><xsl:variable name='temp'><out a='{text()}'/></xsl:variable>"
            + "<xsl:value-of select='$temp/out/@a'/></xsl:for-each>"), streamed: forward, schemaAware: true);
        transform.DocumentResolver = new SourceResolver(xml);
        Assert.Equal(expected, Assert.Single(transform.CallTemplate(null)).StringValue);
    }

    [Theory]
    [InlineData("doc/comment()", false)]
    [InlineData("doc/comment()", true)]
    [InlineData("doc/processing-instruction()", false)]
    [InlineData("doc/processing-instruction()", true)]
    [InlineData("doc/namespace::p", false)]
    [InlineData("doc/namespace::p", true)]
    public void SumRejectsStringValuedNodeKindsRatherThanParsingThemAsDoubles(string select, bool forward)
    {
        var transform = Transformer(Source($"<xsl:sequence select='sum({select})'/>"), streamed: forward);
        transform.DocumentResolver = new SourceResolver("<doc xmlns:p='urn:value'><!--1--><?p 1?></doc>");
        Assert.Contains("FORG0006", Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null)).Message);
    }

    [Theory]
    [InlineData("xs:integer", "9007199254740993 1", "9007199254740994", "integer", true)]
    [InlineData("xs:decimal", "0.1 0.2", "0.3", "decimal", true)]
    [InlineData("xs:double", "13.24 8.12", "21.36", "double", true)]
    [InlineData("xs:integer", "9007199254740993 1", "9007199254740994", "integer", false)]
    [InlineData("xs:decimal", "0.1 0.2", "0.3", "decimal", false)]
    [InlineData("xs:double", "13.24 8.12", "21.36", "double", false)]
    public void SumAtomizesTypedListAttributesAndPreservesNumericTypes(string itemType, string values, string expected, string type, bool forward)
    {
        string declarations = "<xsl:import-schema><xs:schema><xs:simpleType name='values'><xs:list itemType='" + itemType + "'/></xs:simpleType>"
            + "<xs:element name='doc'><xs:complexType><xs:attribute name='values' type='values'/></xs:complexType></xs:element></xs:schema></xsl:import-schema>";
        var transform = Transformer(Source("<xsl:sequence select='sum(doc/@values)'/>", "validation='strict'"), declarations,
            streamed: forward, schemaAware: true);
        transform.DocumentResolver = new SourceResolver($"<doc values='{values}'/>");
        var actual = Assert.IsType<XdmAtomicValue>(Assert.Single(transform.CallTemplate(null)));
        Assert.Equal(expected, actual.StringValue);
        Assert.Equal(new QName("http://www.w3.org/2001/XMLSchema", type), actual.TypeName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SumPreservesExactIntegersAcrossTypedElements(bool forward)
    {
        string declarations = "<xsl:import-schema><xs:schema><xs:element name='doc'><xs:complexType><xs:sequence>"
            + "<xs:element name='item' type='xs:integer' maxOccurs='unbounded'/></xs:sequence></xs:complexType></xs:element></xs:schema></xsl:import-schema>";
        var transform = Transformer(Source("<xsl:sequence select='sum(doc/item)'/>", "validation='strict'"), declarations,
            streamed: forward, schemaAware: true);
        transform.DocumentResolver = new SourceResolver("<doc><item>9007199254740993</item><item>1</item></doc>");
        var actual = Assert.IsType<XdmAtomicValue>(Assert.Single(transform.CallTemplate(null)));
        Assert.Equal("9007199254740994", actual.StringValue);
        Assert.Equal(new QName("http://www.w3.org/2001/XMLSchema", "integer"), actual.TypeName);
    }

    [Theory]
    [InlineData("strict", "<doc><record>1</record></doc>", "doc/record instance of element(*, xs:integer)", "true")]
    [InlineData("lax", "<doc><record>1</record></doc>", "doc/record instance of element(*, xs:integer)", "true")]
    [InlineData("strict", "<doc/>", "doc/@version instance of attribute(*, xs:integer)", "true")]
    [InlineData("strict", "<doc/>", "data(doc/@version)", "7")]
    [InlineData("strict", "<value/>", "data(value)", "9")]
    [InlineData("strict", "<value></value>", "data(value)", "9")]
    [InlineData("strict", "<text>  </text>", "string-length(text)", "2")]
    [InlineData("strict", "<lax><unknown><value>5</value></unknown></lax>", "lax/unknown/value instance of element(*, xs:integer)", "true")]
    [InlineData("lax", "<unknown><doc><record>5</record></doc></unknown>", "unknown/doc/record instance of element(*, xs:integer)", "true")]
    [InlineData("strict", "<!DOCTYPE doc [<!ATTLIST doc version CDATA '8'>]><doc/>", "data(doc/@version)", "8")]
    public void ForwardValidationAppliesImportedSchemasAndDefaults(string mode, string xml, string select, string expected)
    {
        var resolver = new SourceResolver(xml);
        var transform = Transformer(Source($"<xsl:value-of select='{select}'/>", $"validation='{mode}'"), ValidationSchema + "<xsl:strip-space elements='*'/>", schemaAware: true);
        transform.DocumentResolver = resolver;
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal(expected, output.ToString());
        Assert.True(resolver.Streams[0].Closed);
    }

    [Theory]
    [InlineData("strict", "<doc><record>bad</record></doc>", "XTTE1510")]
    [InlineData("lax", "<doc><record>bad</record></doc>", "XTTE1515")]
    [InlineData("strict", "<unknown/>", "XTTE1512")]
    [InlineData("strict", "<other xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:xs='http://www.w3.org/2001/XMLSchema' xsi:type='xs:integer'>5</other>", "XTTE1512")]
    [InlineData("lax", "<doc wrong='1'/>", "XTTE1510")]
    [InlineData("strict", "<ids><item id='a'/><item id='a'/></ids>", "XTTE1555")]
    [InlineData("strict", "<ids><item ref='missing'/></ids>", "XTTE1555")]
    [InlineData("strict", "<unique><item>1</item><item>1</item></unique>", "XTTE1510")]
    [InlineData("lax", "<unknown><doc><record>bad</record></doc></unknown>", "XTTE1515")]
    [InlineData("lax", "<unknown xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:nil='true'><value xsi:nil='true'/></unknown>", "XTTE1510")]
    [InlineData("lax", "<unknown xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:xs='http://www.w3.org/2001/XMLSchema' xsi:type='xs:string'><value xsi:type='xs:string'>5</value></unknown>", "XTTE1510")]
    public void ForwardValidationReportsErrorsAndClosesItsInput(string mode, string xml, string code)
    {
        var resolver = new SourceResolver(xml);
        // Even an unused source must finish its requested validation, including end-of-document checks.
        var transform = Transformer(Source("<xsl:sequence select='1'/>", $"validation='{mode}'"), ValidationSchema, schemaAware: true);
        transform.DocumentResolver = resolver;
        Assert.Contains(code, Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null)).Message);
        Assert.True(resolver.Streams[0].Closed);
    }

    [Theory]
    [InlineData("<xsl:try><xsl:value-of select='count(doc/record)'/><xsl:catch>inner</xsl:catch></xsl:try>")]
    [InlineData("<xsl:message><xsl:value-of select='count(doc/record)'/></xsl:message>")]
    public void ValidationErrorsEscapeInnerHandlersAndCanBeCaughtAroundTheSource(string body)
    {
        var resolver = new SourceResolver("<doc><record>bad</record></doc>");
        var transform = Transformer("<xsl:try>" + Source(body, "validation='strict'") + "<xsl:catch>outer</xsl:catch></xsl:try>", ValidationSchema, schemaAware: true);
        transform.DocumentResolver = resolver;
        var messages = new List<string>();
        transform.MessageHandler = messages.Add;
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("outer", output.ToString());
        Assert.Empty(messages);
        Assert.True(resolver.Streams[0].Closed);
    }

    [Fact]
    public void ExplicitValidationOverridesInputAnnotationStripping()
    {
        var transform = Transformer(Source("<xsl:value-of select='doc/record instance of element(*, xs:integer)'/>", "validation='strict'"), ValidationSchema, schemaAware: true, inputAnnotations: "strip");
        transform.DocumentResolver = new SourceResolver("<doc><record>1</record></doc>");
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("true", output.ToString());
    }

    [Fact]
    public void SchemaLocationsInTheInputDoNotFetchAdditionalSchemas()
    {
        var resolver = new SourceResolver("<doc xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:noNamespaceSchemaLocation='file:///not-imported.xsd'><record>1</record></doc>");
        var transform = Transformer(Source("<xsl:value-of select='count(doc/record)'/>", "validation='strict'"), ValidationSchema, schemaAware: true);
        transform.DocumentResolver = resolver;
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("1", output.ToString());
        Assert.Single(resolver.Streams);
    }

    [Theory]
    [InlineData("xs:integer", "<anything>5</anything>", "data(*) instance of xs:integer", "true")]
    [InlineData("xs:anyType", "<anything><record>5</record></anything>", "*/record instance of element(*, xs:anyType)", "true")]
    [InlineData("xs:untypedAtomic", "<anything>five</anything>", "data(*) instance of xs:untypedAtomic", "true")]
    [InlineData("xs:integer", "<doc>5</doc>", "data(*) instance of xs:integer", "true")]
    [InlineData("numbers", "<anything>1 2 3</anything>", "sum(data(*))", "6")]
    public void ProcessorStipulatedTypesAreAppliedWithoutChangingTheSource(string type, string xml, string select, string expected)
    {
        var transform = Transformer(Source($"<xsl:value-of select='{select}'/>", $"type='{type}'"), ValidationSchema, schemaAware: true);
        transform.DocumentResolver = new SourceResolver(xml);
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal(expected, output.ToString());
    }

    [Theory]
    [InlineData("xs:integer", "<anything>bad</anything>")]
    [InlineData("xs:untypedAtomic", "<anything><child/></anything>")]
    public void InvalidProcessorStipulatedTypesReportXtTe1540(string type, string xml)
    {
        var resolver = new SourceResolver(xml);
        var transform = Transformer(Source("<xsl:sequence select='1'/>", $"type='{type}'"), schemaAware: true);
        transform.DocumentResolver = resolver;
        Assert.Contains("XTTE1540", Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null)).Message);
        Assert.True(resolver.Streams[0].Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidationKeepsSuppliedReadersOpenAndRespectsTheirSubtree(bool positioned)
    {
        using XmlReader reader = XmlReader.Create(new StringReader(positioned
            ? "<outer><doc><record>1</record></doc><after/></outer>" : "<doc><record>1</record></doc>"));
        if (positioned) Assert.True(reader.ReadToDescendant("doc"));
        var transform = Transformer(Source("<xsl:value-of select='count(doc/record)'/>", "validation='strict'"), ValidationSchema, schemaAware: true);
        transform.DocumentResolver = new SourceResolver("") { SuppliedReader = reader };
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("1", output.ToString());
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
        if (positioned)
        {
            Assert.True(reader.Read());
            Assert.Equal("after", reader.LocalName);
        }
    }

    [Fact]
    public void ValidationProducesResultsBeforeTheReaderReachesTheEnd()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<doc><record>1</record><record>2</record></doc>"));
        var transform = Transformer(Source("<xsl:for-each select='doc/record'><xsl:value-of select='.'/></xsl:for-each>", "validation='strict'"), ValidationSchema, schemaAware: true);
        transform.DocumentResolver = new SourceResolver("") { SuppliedReader = reader };
        using var output = new StringWriter();
        using var writer = new ObservingWriter(output, () => Assert.False(reader.EOF));
        transform.CallTemplate(null, writer);
        Assert.Equal("12", output.ToString());
        Assert.True(writer.Observed);
    }

    [Theory]
    [InlineData("xsi:type='xs:integer'", "5", "xs:integer", "")]
    [InlineData("xsi:type='xs:missing'", "text", "xs:missing", "")]
    [InlineData("xsi:type=''", "", "", "")]
    [InlineData("xsi:nil='true'", "content", "", "true")]
    [InlineData("xsi:nil='not-a-boolean'", "content", "", "not-a-boolean")]
    [InlineData("xsi:type='xs:string' xsi:nil='true'", "content", "xs:string", "true")]
    public void UndeclaredLaxRootsKeepInstanceAttributesWithoutApplyingTheirElementConstraints(string attributes, string content, string type, string nil)
    {
        foreach (bool streamed in new[] { false, true })
        {
            var resolver = new SourceResolver($"<unknown xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' {attributes}>{content}</unknown>");
            var transform = Transformer(Source("<xsl:for-each select='unknown'><xsl:value-of select='. instance of element(*, xs:anyType), . instance of element(*, xs:untyped), nilled(.), string(@xsi:type), string(@xsi:nil)' separator='|'/></xsl:for-each>", "validation='lax' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'"), streamed: streamed, schemaAware: true);
            transform.DocumentResolver = resolver;
            using var output = new StringWriter();
            transform.CallTemplate(null, output);
            Assert.Equal($"true|false|false|{type}|{nil}", output.ToString());
            Assert.True(resolver.Streams[0].Closed);
        }
    }

    [Theory]
    [InlineData("xsi:type='xs:string'")]
    [InlineData("xsi:nil='true'")]
    public void UndeclaredLaxRootInstanceAttributesDoNotSuppressDescendantValidation(string attributes)
    {
        var transform = Transformer(Source("<xsl:sequence select='1'/>", "validation='lax'"), ValidationSchema, schemaAware: true);
        var resolver = new SourceResolver($"<unknown xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' {attributes}><value>bad</value></unknown>");
        transform.DocumentResolver = resolver;
        Assert.Contains("XTTE1515", Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null)).Message);
        Assert.True(resolver.Streams[0].Closed);
    }

    [Fact]
    public void RootAttributeViewsPreserveReaderNavigationAndAttributeTypes()
    {
        var schemas = new XmlSchemaSet();
        using (var schema = XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:attribute name='level' type='xs:integer'/><xs:element name='value' type='xs:integer'/></xs:schema>"))) schemas.Add(null, schema);
        schemas.Compile();
        using var input = XmlReader.Create(new StringReader("<unknown xmlns:x='http://www.w3.org/2001/XMLSchema-instance' level='4' x:type='missing' x:nil='true'><value>7</value></unknown>"));
        using var reader = new Streaming.StreamedValidation(schemas, false).Wrap(input, true);
        Assert.True(reader.Read());
        Assert.Equal(4, reader.AttributeCount);
        Assert.True(reader.HasAttributes);
        Assert.Equal("missing", reader.GetAttribute("type", XmlSchema.InstanceNamespace));
        Assert.Equal("true", reader.GetAttribute("x:nil"));
        Assert.True(reader.MoveToAttribute("level"));
        Assert.Equal(XmlTypeCode.Integer, reader.SchemaInfo!.SchemaType!.TypeCode);
        Assert.Equal(reader.SchemaInfo.SchemaType.Datatype!.ValueType, reader.ValueType);
        Assert.Equal(4, reader.ReadContentAsInt());
        Assert.Equal(reader.ValueType, reader.ReadContentAsObject().GetType());
        Assert.Equal(4L, reader.ReadContentAs(typeof(long), null));
        Assert.Equal("level", reader.LocalName);
        Assert.Equal(1, reader.Depth);
        Assert.True(reader.MoveToNextAttribute());
        Assert.Equal("x:type", reader.Name);
        Assert.Null(reader.SchemaInfo);
        Assert.True(reader.ReadAttributeValue());
        Assert.Equal(XmlNodeType.Text, reader.NodeType);
        Assert.Equal("missing", reader.Value);
        Assert.Equal(2, reader.Depth);
        Assert.False(reader.ReadAttributeValue());
        Assert.True(reader.MoveToElement());
        reader.MoveToAttribute(3);
        Assert.Equal("true", reader.Value);
        Assert.True(reader.Read());
        Assert.Equal("value", reader.LocalName);
        Assert.Equal(XmlTypeCode.Integer, reader.SchemaInfo!.SchemaType!.TypeCode);
        Assert.Equal(0, reader.AttributeCount);
        while (reader.Read()) { }
    }

    [Fact]
    public void ElementIdentityErrorsUseTheElementValidationCode()
    {
        var transform = Transformer("<xsl:variable name='tree' as='element()'><unique xsl:validation='strict'><item>1</item><item>1</item></unique></xsl:variable><xsl:sequence select='$tree'/>", ValidationSchema, streamed: false, schemaAware: true);
        Assert.Contains("XTTE1510", Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null)).Message);
    }

    [Theory]
    [InlineData("count(doc/record)", "3")]
    [InlineData("sum(doc/record/@n)", "6")]
    [InlineData("sum(doc/record)", "6")]
    public void SourceExpressionsMatchOrdinaryExecution(string select, string expected)
    {
        foreach (bool streamed in new[] { false, true })
        {
            var resolver = new SourceResolver(Input);
            var transform = Transformer(Source($"<xsl:value-of select='{select}'/>"), streamed: streamed);
            transform.DocumentResolver = resolver;
            using var output = new StringWriter();
            transform.CallTemplate(null, output);
            Assert.Equal(expected, output.ToString());
            Assert.True(resolver.Streams[0].Closed);
        }
    }

    [Theory]
    [InlineData("sum(copy-of(doc/record))", false)]
    [InlineData("sum(snapshot(doc/record))", false)]
    [InlineData("sum(copy-of(doc/record))", true)]
    [InlineData("sum(snapshot(doc/record))", true)]
    public void CopyFunctionsGroundEachSelectedRecordBeforeAdvancing(string select, bool schemaAware)
    {
        var resolver = new SourceResolver(Input);
        var transform = Transformer(Source($"<xsl:value-of select='{select}'/>"), schemaAware: schemaAware);
        transform.DocumentResolver = resolver;
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("6", output.ToString());
        Assert.True(resolver.Streams[0].Closed);
    }

    [Theory]
    [InlineData("sum(data(doc/record))", "6")]
    [InlineData("sum(doc/record/data(.))", "6")]
    [InlineData("count(doc/record/data(.))", "3")]
    [InlineData("string-join(doc/record/data(.), &quot;|&quot;)", "1|2|3")]
    [InlineData("count(//record)", "3")]
    [InlineData("sum(//record/@n)", "6")]
    [InlineData("sum(//record/data(.))", "6")]
    [InlineData("sum(//record/xs:integer(.))", "6")]
    [InlineData("string-join(//record/data(.), &quot;|&quot;)", "1|2|3")]
    public void AtomizationConsumesEachRecordBeforeAdvancing(string select, string expected)
    {
        var transform = Transformer(Source($"<xsl:value-of select='{select}'/>"));
        transform.DocumentResolver = new SourceResolver(Input);
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal(expected, output.ToString());
    }

    [Fact]
    public void DescendantSelectionStillIncludesNestedMatches()
    {
        var transform = Transformer(Source("<xsl:value-of select='count(//record)'/>"));
        transform.DocumentResolver = new SourceResolver("<doc><record><record/></record></doc>");
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("2", output.ToString());
    }

    [Theory]
    [InlineData("data")]
    [InlineData("copy-of")]
    [InlineData("snapshot")]
    public void AbsorbedDescendantsCanContainOtherNonMatchingElements(string select)
    {
        var transform = Transformer(Source($"<xsl:value-of select='{select}(//record)' separator=''/>"));
        transform.DocumentResolver = new SourceResolver("<doc><record><value>1</value></record><record><value>2</value></record></doc>");
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("12", output.ToString());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("position() = 1")]
    [InlineData("last()")]
    [InlineData("f:last()")]
    [InlineData("(f:last#0)()")]
    public void DescendantAbbreviationKeepsPredicatesInTheirSiblingFocus(string predicate)
    {
        string inner = Source($"<xsl:value-of select='count(//record[{predicate}])'/>", "xmlns:f='http://www.w3.org/2005/xpath-functions'")
            .Replace("streamable='yes'", "streamable='no'", StringComparison.Ordinal);
        var transform = Transformer(Source(inner));
        transform.DocumentResolver = new SourceResolver("<doc><group><record/><record/></group><group><record/><record/></group></doc>");
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("2", output.ToString());
    }

    [Fact]
    public void APositionIndependentPredicateCanFilterForwardDescendants()
    {
        var transform = Transformer(Source("<xsl:value-of select='count(//record[parent::group/@id = &quot;yes&quot;])'/>"));
        transform.DocumentResolver = new SourceResolver("<doc><group id='yes'><record/><record/></group><group id='no'><record/><record/></group></doc>");
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("2", output.ToString());
    }

    [Theory]
    [InlineData("copy-of")]
    [InlineData("snapshot")]
    [InlineData("data")]
    public void AbsorbingOverlappingDescendantsNeverSilentlyDropsNestedMatches(string function)
    {
        var transform = Transformer(Source($"<xsl:sequence select='{function}(//record)'/>"));
        transform.DocumentResolver = new SourceResolver("<doc><record><record>2</record></record></doc>");
        XdmValue? value = null;
        Exception? error = Record.Exception(() => value = transform.CallTemplate(null));
        if (error != null)
        {
            // Overlapping absorption remains an S3 gap. Until replay is implemented, fail explicitly
            // instead of reporting a successful result that has lost the nested selected record.
            Assert.Contains("stream-position", Assert.IsType<XsltDynamicException>(error).Message);
        }
        else
        {
            Assert.NotNull(value);
            Assert.Equal(2, value.Count);
            Assert.Equal(new[] { "2", "2" }, System.Linq.Enumerable.Select(value.Items, item => item.Value));
        }
    }

    [Theory]
    [InlineData("<xsl:copy-of select='doc/record' copy-accumulators='yes'/>", false)]
    [InlineData("<xsl:copy-of select='doc/record' copy-namespaces='no'/>", false)]
    [InlineData("<xsl:copy-of select='doc/record' validation='preserve'/>", true)]
    public void CopyInstructionsGroundEachSelectedRecordBeforeAdvancing(string instruction, bool schemaAware)
    {
        var transform = Transformer(Source(instruction), schemaAware: schemaAware);
        transform.DocumentResolver = new SourceResolver(Input);
        XdmValue value = transform.CallTemplate(null);
        Assert.Equal(3, value.Count);
        Assert.Equal(new[] { "1", "2", "3" }, System.Linq.Enumerable.Select(value.Items, item => item.Value));
    }

    [Theory]
    [InlineData("preserve", true, true)]
    [InlineData("preserve", true, false)]
    [InlineData("preserve", false, true)]
    [InlineData("preserve", false, false)]
    [InlineData("strip", true, true)]
    [InlineData("strip", true, false)]
    [InlineData("strip", false, true)]
    [InlineData("strip", false, false)]
    [InlineData("", true, true)]
    [InlineData("", true, false)]
    [InlineData("", false, true)]
    [InlineData("", false, false)]
    public void CopyOptionsIndependentlyControlAnnotationsAndNamespaces(string validation, bool namespaces, bool accumulators)
    {
        foreach (bool streamed in new[] { false, true })
        {
            string option = validation.Length == 0 ? "" : $"validation='{validation}'";
            var transform = Transformer(Source($"<xsl:copy-of select='//record' {option} copy-namespaces='{(namespaces ? "yes" : "no")}' copy-accumulators='{(accumulators ? "yes" : "no")}'/>", "validation='strict'"), ValidationSchema, streamed: streamed, schemaAware: true);
            transform.DocumentResolver = new SourceResolver("<doc xmlns:u='urn:unused'><record>1</record><record>2</record></doc>");
            XdmValue result = transform.CallTemplate(null);
            Assert.Equal(2, result.Count);
            foreach (var item in result.Items)
            {
                var node = Assert.IsAssignableFrom<System.Xml.XPath.XPathNavigator>(item);
                Assert.Equal(validation == "preserve", Schema.SchemaTypes.TypeOf(node)?.TypeCode == XmlTypeCode.Integer);
                Assert.Equal(namespaces, node.LookupNamespace("u") == "urn:unused");
            }
            Assert.Equal(new[] { "1", "2" }, System.Linq.Enumerable.Select(result.Items, item => item.Value));
        }
    }

    [Theory]
    [InlineData("preserve", true)]
    [InlineData("preserve", false)]
    [InlineData("strip", true)]
    [InlineData("strip", false)]
    public void NamespaceSensitiveCopiesRespectBothOptions(string validation, bool accumulators)
    {
        string schema = "<xsl:import-schema><xs:schema><xs:element name='name' type='xs:QName'/></xs:schema></xsl:import-schema>";
        foreach (bool streamed in new[] { false, true })
        {
            var transform = Transformer(Source($"<xsl:copy-of select='//name' validation='{validation}' copy-namespaces='no' copy-accumulators='{(accumulators ? "yes" : "no")}'/>", "validation='lax'"), schema, streamed: streamed, schemaAware: true);
            transform.DocumentResolver = new SourceResolver("<unknown xmlns:p='urn:value'><name>p:value</name></unknown>");
            if (validation == "preserve") Assert.Contains("XTTE0950", Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null)).Message);
            else
            {
                XdmValue result = transform.CallTemplate(null);
                var node = Assert.IsAssignableFrom<System.Xml.XPath.XPathNavigator>(Assert.Single(result.Items));
                Assert.Null(Schema.SchemaTypes.TypeOf(node));
                Assert.Null(node.LookupNamespace("p"));
                Assert.Equal("p:value", node.Value);
            }
        }
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    public void PreservingCopyAnnotationsDoesNotImplicitlyCopyAccumulators(string accumulators)
    {
        const string accumulator = "<xsl:accumulator name='a' as='xs:integer' initial-value='0'><xsl:accumulator-rule match='record' select='$value + 1'/></xsl:accumulator>";
        foreach (bool streamed in new[] { false, true })
        {
            string copies = Source($"<xsl:copy-of select='doc/record' validation='preserve' copy-accumulators='{accumulators}'/>");
            var transform = Transformer($"<xsl:variable name='copies' as='element()*'>{copies}</xsl:variable><xsl:for-each select='$copies'><xsl:value-of select='accumulator-after(&quot;a&quot;)'/></xsl:for-each>", accumulator, streamed: streamed, schemaAware: true);
            transform.DocumentResolver = new SourceResolver(Input);
            if (accumulators == "yes") Assert.Contains("XTDE3362", Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null)).Message);
            else
            {
                using var output = new StringWriter();
                transform.CallTemplate(null, output);
                Assert.Equal("111", output.ToString());
            }
        }
    }

    [Fact]
    public void AValidatedCopyFailsBeforeRequestingTheNextSelectedRecord()
    {
        string schema = "<xsl:import-schema><xs:schema><xs:element name='record' type='xs:integer'/></xs:schema></xsl:import-schema>";
        using var reader = XmlReader.Create(new StringReader("<doc><record>bad</record><record>2</record></doc>"));
        var transform = Transformer(Source("<xsl:copy-of select='doc/record' validation='strict'/>"), schema, schemaAware: true);
        transform.DocumentResolver = new SourceResolver("") { SuppliedReader = reader };
        Assert.Contains("XTTE1510", Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null)).Message);
        Assert.False(reader.EOF);
        Assert.Equal(XmlNodeType.EndElement, reader.NodeType);
        Assert.Equal("record", reader.LocalName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyFunctionPreservesTypedInputInBothExecutionPaths(bool streamed)
    {
        var transform = Transformer(Source("<xsl:sequence select='copy-of(doc/record)'/>", "validation='strict'"), ValidationSchema, streamed: streamed, schemaAware: true);
        transform.DocumentResolver = new SourceResolver("<doc><record>1</record><record>2</record></doc>");
        XdmValue copies = transform.CallTemplate(null);
        Assert.Equal(2, copies.Count);
        foreach (var item in copies.Items)
            Assert.Equal(XmlTypeCode.Integer, Schema.SchemaTypes.TypeOf(Assert.IsAssignableFrom<System.Xml.XPath.XPathNavigator>(item))!.TypeCode);
    }

    [Fact]
    public void StrippingCopyAnnotationsKeepsAttributeIdentityProperties()
    {
        foreach (bool streamed in new[] { false, true })
        {
            string copy = Source("<xsl:copy-of select='ids' validation='strip'/>", "validation='strict'");
            var transform = Transformer($"<xsl:variable name='copied'><wrapped>{copy}</wrapped></xsl:variable><xsl:value-of select='count(id(&quot;a&quot;, $copied)), count(idref(&quot;a&quot;, $copied)), data($copied//item/@id) instance of xs:untypedAtomic' separator='|'/>", ValidationSchema, streamed: streamed, schemaAware: true);
            transform.DocumentResolver = new SourceResolver("<ids><item id='a'/><item ref='a'/></ids>");
            using var output = new StringWriter();
            transform.CallTemplate(null, output);
            Assert.Equal("1|1|true", output.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopiedSourceDocumentsHaveNoDocumentUri(bool streamed)
    {
        var transform = Transformer(Source("<xsl:value-of select='empty(document-uri(copy-of(.)))'/>"), streamed: streamed);
        transform.DocumentResolver = new SourceResolver(Input);
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("true", output.ToString());
    }

    [Fact]
    public void TypedSnapshotsPreserveSelectedTypesAndGiveAncestorsAnyType()
    {
        var schemas = new XmlSchemaSet();
        using XmlReader schema = XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='record' type='xs:integer' maxOccurs='unbounded'/></xs:sequence><xs:attribute name='version' type='xs:integer'/></xs:complexType></xs:element></xs:schema>"));
        schemas.Add(null, schema);
        using XmlReader reader = XmlReader.Create(new StringReader("<doc version='7'><record>1</record><record>2</record></doc>"),
            new() { ValidationType = ValidationType.Schema, Schemas = schemas }, "file:///stream/typed.xml");
        var transform = Transformer(Source("<xsl:sequence select='snapshot(doc/record)'/>", "validation='preserve'"), schemaAware: true);
        transform.DocumentResolver = new SourceResolver("") { SuppliedReader = reader };
        XdmValue value = transform.CallTemplate(null);
        Assert.Equal(2, value.Count);
        foreach (System.Xml.XPath.XPathItem item in value.Items)
        {
            var node = (System.Xml.XPath.XPathNavigator)item;
            Assert.Equal(XmlTypeCode.Integer, node.SchemaInfo?.SchemaType?.TypeCode);
            var parent = node.Clone();
            Assert.True(parent.MoveToParent());
            Assert.Same(Schema.SchemaTypes.AnyType, parent.SchemaInfo?.SchemaType);
            Assert.False(parent.SchemaInfo?.IsNil);
            Assert.Equal("file:///stream/typed.xml", parent.BaseURI);
            Assert.True(parent.MoveToAttribute("version", ""));
            Assert.Equal(XmlTypeCode.Integer, parent.SchemaInfo?.SchemaType?.TypeCode);
        }
        Assert.Equal(new[] { "1", "2" }, System.Linq.Enumerable.Select(value.Items, item => item.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttributeSnapshotsKeepOtherAttributesButNoParentChildren(bool schemaAware)
    {
        var transform = Transformer(Source("<xsl:sequence select='snapshot(doc/record/@n)'/>"), schemaAware: schemaAware);
        transform.DocumentResolver = new SourceResolver(Input);
        XdmValue value = transform.CallTemplate(null);
        Assert.Equal(3, value.Count);
        foreach (System.Xml.XPath.XPathItem item in value.Items)
        {
            var node = ((System.Xml.XPath.XPathNavigator)item).Clone();
            Assert.True(node.MoveToParent());
            Assert.True(node.HasAttributes);
            Assert.False(node.HasChildren);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotPreservesAParentlessNamespaceNode(bool schemaAware)
    {
        var transform = Transformer(Source("<xsl:variable name='n' as='namespace-node()'><xsl:namespace name='p'>urn:test</xsl:namespace></xsl:variable><xsl:sequence select='snapshot($n)'/>"), schemaAware: schemaAware);
        transform.DocumentResolver = new SourceResolver(Input);
        XdmValue value = transform.CallTemplate(null);
        var node = Assert.IsAssignableFrom<System.Xml.XPath.XPathNavigator>(Assert.Single(value.Items));
        Assert.Equal(System.Xml.XPath.XPathNodeType.Namespace, node.NodeType);
        Assert.Equal("p", node.LocalName);
        Assert.Equal("urn:test", node.Value);
        Assert.False(node.MoveToParent());
    }

    [Fact]
    public void PerRecordResultsAreWrittenBeforeTheReaderReachesTheEnd()
    {
        using XmlReader reader = XmlReader.Create(new StringReader(Input));
        var resolver = new SourceResolver(Input) { SuppliedReader = reader };
        var transform = Transformer(Source("<xsl:for-each select='doc/record'><xsl:value-of select='@n'/></xsl:for-each>"));
        transform.DocumentResolver = resolver;
        using var output = new StringWriter();
        using var writer = new ObservingWriter(output, () => Assert.False(reader.EOF));
        transform.CallTemplate(null, writer);
        Assert.Equal("123", output.ToString());
        Assert.True(writer.Observed);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Theory]
    [InlineData("<doc><record>1</record><record>", "FODC0002")]
    [InlineData(Input, "XTMM9000")]
    public void OwnedSourcesCloseOnParsingAndBodyErrors(string xml, string code)
    {
        var resolver = new SourceResolver(xml);
        string body = code == "FODC0002" ? "<xsl:value-of select='count(doc/record)'/>" : "<xsl:message terminate='yes'>stop</xsl:message>";
        var transform = Transformer(Source(body));
        transform.DocumentResolver = resolver;
        Assert.Contains(code, Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null, new StringWriter())).Message);
        Assert.True(resolver.Streams[0].Closed);
    }

    [Fact]
    public void SkippingAnUnselectedSubtreeKeepsWhitespaceReaderState()
    {
        var resolver = new SourceResolver("<doc><skip xml:space='preserve'> <nested/> </skip> <record/> </doc>");
        var transform = Transformer(Source("<xsl:value-of select='count(doc/node()[not(self::skip)])'/>"), "<xsl:strip-space elements='*'/>");
        transform.DocumentResolver = resolver;
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("1", output.ToString());
    }

    [Fact]
    public void DtdElementContentWhitespaceMatchesOrdinaryInput()
    {
        const string xml = "<!DOCTYPE doc [<!ELEMENT doc (record*)><!ELEMENT record EMPTY>]><doc xml:space='preserve'> <record/> </doc>";
        foreach (bool streamed in new[] { false, true })
        {
            var transform = Transformer(Source("<xsl:value-of select='count(doc/node())'/>"), streamed: streamed);
            transform.DocumentResolver = new SourceResolver(xml);
            using var output = new StringWriter();
            transform.CallTemplate(null, output);
            Assert.Equal("1", output.ToString());
        }
    }

    [Theory]
    [InlineData("file:///stream/input.xml#xpointer(/)", true, "FODC0002")]
    [InlineData("file:///stream/input.xml", false, "FODC0002")]
    public void InvalidAndDisabledDocumentAccessNeverOpensTheResolver(string href, bool enabled, string code)
    {
        var resolver = new SourceResolver(Input);
        var transform = Transformer(Source("<xsl:value-of select='count(doc/record)'/>", href: href), documentFunctions: enabled);
        transform.DocumentResolver = resolver;
        Assert.Contains(code, Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null, new StringWriter())).Message);
        Assert.Empty(resolver.Streams);
    }

    [Fact]
    public void ASourceCaughtByAnOuterTryClosesBeforeItsCatchRuns()
    {
        var resolver = new SourceResolver(Input) { BeforeOpen = streams => { if (streams.Count > 0) Assert.True(streams[^1].Closed); } };
        var transform = Transformer("<xsl:try>" + Source("<xsl:message terminate='yes'>stop</xsl:message>")
            + "<xsl:catch>" + Source("<xsl:value-of select='count(doc/record)'/>") + "</xsl:catch></xsl:try>");
        transform.DocumentResolver = resolver;
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("3", output.ToString());
        Assert.Equal(2, resolver.Streams.Count);
        Assert.All(resolver.Streams, s => Assert.True(s.Closed));
    }

    [Fact]
    public void NestedSourcesHaveSeparateReadersAndCloseAtTheirOwnExit()
    {
        var resolver = new SourceResolver(Input);
        string inner = Source("<xsl:value-of select='sum(doc/record/@n)'/>", href: "file:///stream/inner.xml");
        var transform = Transformer(Source("<xsl:for-each select='doc/record'>" + inner + "<xsl:value-of select='@n'/></xsl:for-each>"));
        transform.DocumentResolver = resolver;
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("616263", output.ToString());
        Assert.Equal(4, resolver.Streams.Count);
        Assert.All(resolver.Streams, s => Assert.True(s.Closed));
    }

    [Fact]
    public void NoAccumulatorAppliesWhenTheSourceOmitsUseAccumulators()
    {
        var transform = Transformer(Source("<xsl:value-of select='accumulator-before(&quot;a&quot;)'/>"),
            "<xsl:accumulator name='a' initial-value='0' streamable='yes'><xsl:accumulator-rule match='*' select='$value + 1'/></xsl:accumulator>");
        transform.DocumentResolver = new SourceResolver(Input);
        Assert.Contains("XTDE3362", Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null, new StringWriter())).Message);
    }

    [Fact]
    public void DeclaredStreamedAccumulatorObserversRunOnTheReader()
    {
        var resolver = new SourceResolver(Input);
        var transform = Transformer(Source("<xsl:value-of select='accumulator-after(&quot;a&quot;)'/>", "use-accumulators='a'"),
            "<xsl:accumulator name='a' initial-value='0' streamable='yes'><xsl:accumulator-rule match='*' select='$value + 1'/></xsl:accumulator>");
        transform.DocumentResolver = resolver;
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("4", output.ToString());
        Assert.True(Assert.Single(resolver.Streams).Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidatedSourceObserversSeeDefaultedAttributesAndText(bool forward)
    {
        string declarations = ValidationSchema + "<xsl:accumulator name='n' as='xs:integer' initial-value='0' streamable='yes'>"
            + "<xsl:accumulator-rule match='doc' select='$value + xs:integer(@version)'/>"
            + "<xsl:accumulator-rule match='record/text()' select='$value + xs:integer(.)'/></xsl:accumulator>";
        var transform = Transformer(Source("<xsl:value-of select='accumulator-after(&quot;n&quot;)'/>", "use-accumulators='n' validation='strict'"),
            declarations, streamed: forward, schemaAware: true);
        var resolver = new SourceResolver("<doc><record>1</record><record>2</record><record>3</record></doc>");
        transform.DocumentResolver = resolver;
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("13", output.ToString()); // schema default 7, plus the source text 1 + 2 + 3
        Assert.True(Assert.Single(resolver.Streams).Closed);
    }

    [Fact]
    public void CancellationDuringSkippedContentClosesTheSource()
    {
        using var cancellation = new CancellationTokenSource();
        var resolver = new SourceResolver(Input) { OnRead = () => cancellation.Cancel() };
        var transform = Transformer(Source("<xsl:value-of select='count(doc/missing)'/>"));
        transform.DocumentResolver = resolver;
        transform.CancellationToken = cancellation.Token;
        Assert.Contains("cancelled", Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null, new StringWriter())).Message);
        Assert.True(resolver.Streams[0].Closed);
    }

    [Fact]
    public void UndeclaredLaxRootsUseAnyType()
    {
        var resolver = new SourceResolver(Input);
        var transform = Transformer(Source("<xsl:value-of select='doc/record instance of element(*, xs:anyType)+'/>", "validation='lax'"), schemaAware: true);
        transform.DocumentResolver = resolver;
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal("true", output.ToString());
        Assert.True(resolver.Streams[0].Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamedReadAndResultDocumentUrisCannotOverlap(bool writeFirst)
    {
        var resolver = new SourceResolver(Input);
        string read = Source("<xsl:value-of select='count(doc/record)'/>");
        const string write = "<xsl:result-document href='input.xml'><out/></xsl:result-document>";
        var transform = Transformer(writeFirst ? write + read : read + write);
        transform.DocumentResolver = resolver;
        transform.BaseOutputUri = new Uri("file:///stream/output.xml");
        transform.ResultDocumentHandler = _ => new StringWriter();
        Assert.Contains("XTDE1500", Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null, new StringWriter())).Message);
        if (writeFirst) Assert.Empty(resolver.Streams);
        else Assert.True(resolver.Streams[0].Closed);
    }

    [Theory]
    [InlineData("strip", "", "false")]
    [InlineData("preserve", "", "true")]
    [InlineData("preserve", "strip", "true")]
    [InlineData("", "strip", "false")]
    public void AnnotationPolicyAppliesToAnAlreadyValidatingInput(string validation, string inputAnnotations, string expected)
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='record' type='xs:integer'/></xs:sequence></xs:complexType></xs:element></xs:schema>")));
        using XmlReader reader = XmlReader.Create(new StringReader("<doc><record>1</record></doc>"), new() { ValidationType = ValidationType.Schema, Schemas = schemas });
        var transform = Transformer(Source("<xsl:value-of select='doc/record instance of element(*, xs:integer)'/>", validation.Length == 0 ? "" : $"validation='{validation}'"), schemaAware: true, inputAnnotations: inputAnnotations);
        transform.DocumentResolver = new SourceResolver("") { SuppliedReader = reader };
        using var output = new StringWriter();
        transform.CallTemplate(null, output);
        Assert.Equal(expected, output.ToString());
    }

    [Fact]
    public void ACloseFailureDoesNotHideASourceError()
    {
        var resolver = new SourceResolver(Input) { StreamReadable = false, ThrowOnClose = true };
        var transform = Transformer(Source("<xsl:value-of select='count(doc/record)'/>"));
        transform.DocumentResolver = resolver;
        XsltDynamicException error = Assert.Throws<XsltDynamicException>(() => transform.CallTemplate(null, new StringWriter()));
        Assert.Contains("FODC0002", error.Message);
        Assert.Contains("read failure", error.Message);
        Assert.DoesNotContain("close failure", error.Message);
        Assert.True(resolver.Streams[0].Closed);
    }

    private sealed class ObservingWriter(TextWriter output, Action observe) : XmlTextWriter(output)
    {
        internal bool Observed;
        public override void WriteString(string? value)
        {
            if (!Observed && !string.IsNullOrEmpty(value)) { observe(); Observed = true; }
            base.WriteString(value);
        }
    }

    private sealed class SourceResolver(string xml) : XmlResolver
    {
        internal readonly List<TrackedStream> Streams = new();
        internal XmlReader? SuppliedReader;
        internal Action<List<TrackedStream>>? BeforeOpen;
        internal Action? OnRead;
        internal bool StreamReadable = true, ThrowOnClose;
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            BeforeOpen?.Invoke(Streams);
            if (SuppliedReader != null) return SuppliedReader;
            var stream = new TrackedStream(Encoding.UTF8.GetBytes(xml), OnRead, StreamReadable, ThrowOnClose);
            Streams.Add(stream);
            return stream;
        }
    }

    private sealed class TrackedStream(byte[] bytes, Action? onRead, bool readable, bool throwOnClose) : MemoryStream(bytes)
    {
        internal bool Closed;
        public override bool CanRead => readable && base.CanRead;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!readable) throw new IOException("read failure");
            onRead?.Invoke();
            return base.Read(buffer, offset, count);
        }
        public override int Read(Span<byte> buffer)
        {
            if (!readable) throw new IOException("read failure");
            onRead?.Invoke();
            return base.Read(buffer);
        }
        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
            if (disposing && throwOnClose) throw new IOException("close failure");
        }
    }
}
