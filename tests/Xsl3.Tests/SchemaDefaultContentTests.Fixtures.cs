// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Schema;

namespace Xsl3.Tests;

public partial class SchemaDefaultContentTests
{
    public sealed record DefaultCase(string Constraint, string Type, string Shape);
    public sealed record ReferenceResult(bool Valid, string? Trace);

    public static IEnumerable<DefaultCase> ContentCases()
    {
        foreach (string constraint in new[] { "default", "fixed" })
        foreach (string type in new[] { "string", "normalizedString", "token", "integer", "list", "union", "complex", "mixed" })
        foreach (string shape in new[] { "empty", "comment", "pi", "both", "whitespace", "explicit", "nil", "child" })
            yield return new(constraint, type, shape);
    }

    public static IEnumerable<DefaultCase> TypedCases()
    {
        foreach (string constraint in new[] { "default", "fixed" })
        foreach (string type in new[] { "QName", "dateTime", "duration" })
        foreach (string shape in new[] { "empty", "both" }) yield return new(constraint, type, shape);
    }

    public static IEnumerable<DefaultCase> PipelineCases()
    {
        yield return new("default", "string", "comment");
        yield return new("fixed", "string", "pi");
        yield return new("default", "normalizedString", "both");
        yield return new("default", "token", "both");
        yield return new("fixed", "integer", "comment");
        yield return new("default", "list", "pi");
        yield return new("fixed", "union", "both");
        yield return new("default", "complex", "comment");
        yield return new("default", "mixed", "both");
    }

    public static string Schema(DefaultCase item)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = XmlWriter.Create(output, new() { OmitXmlDeclaration = true, NewLineHandling = NewLineHandling.Entitize }))
        {
            writer.WriteStartElement("xs", "schema", XmlSchema.Namespace);
            writer.WriteAttributeString("xmlns", "p", null, "urn:schema-default");
            if (item.Type is "list" or "union")
            {
                writer.WriteStartElement("xs", "simpleType", XmlSchema.Namespace); writer.WriteAttributeString("name", "valueType");
                writer.WriteStartElement("xs", item.Type, XmlSchema.Namespace);
                writer.WriteAttributeString(item.Type == "list" ? "itemType" : "memberTypes", item.Type == "list" ? "xs:integer" : "xs:integer xs:token");
                writer.WriteEndElement(); writer.WriteEndElement();
            }
            else if (item.Type == "complex")
            {
                writer.WriteStartElement("xs", "complexType", XmlSchema.Namespace); writer.WriteAttributeString("name", "valueType");
                writer.WriteStartElement("xs", "simpleContent", XmlSchema.Namespace);
                writer.WriteStartElement("xs", "extension", XmlSchema.Namespace); writer.WriteAttributeString("base", "xs:token");
                writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
            }
            else if (item.Type == "mixed")
            {
                writer.WriteStartElement("xs", "complexType", XmlSchema.Namespace); writer.WriteAttributeString("name", "valueType");
                writer.WriteAttributeString("mixed", "true"); writer.WriteEndElement();
            }
            writer.WriteStartElement("xs", "element", XmlSchema.Namespace); writer.WriteAttributeString("name", "e");
            writer.WriteAttributeString("type", item.Type is "list" or "union" or "complex" or "mixed" ? "valueType" : "xs:" + item.Type);
            writer.WriteAttributeString("nillable", "true");
            writer.WriteAttributeString(item.Constraint, item.Type switch {
                "string" => "schema-provided", "normalizedString" => " a\tb\nc ", "token" or "complex" or "mixed" => " a   b ",
                "integer" or "union" => " +017 ", "list" => " 1   +02 3 ",
                "QName" => "p:value", "dateTime" => "2026-01-01T01:02:03+01:00", "duration" => "P1Y13M", _ => throw new ArgumentOutOfRangeException(nameof(item)),
            });
            writer.WriteEndElement(); writer.WriteEndElement();
        }
        return output.ToString();
    }

    public static XmlSchemaSet Schemas(DefaultCase item)
    {
        var schemas = new XmlSchemaSet();
        using var input = XmlReader.Create(new StringReader(Schema(item)));
        schemas.Add(null, input); schemas.Compile(); return schemas;
    }

    public static string Element(DefaultCase item) => (item.Shape switch {
        "empty" => "<e/>", "comment" => "<e><!--comment--></e>", "pi" => "<e><?pi value?></e>",
        "both" => "<e><!--comment--><?pi value?></e>", "whitespace" => "<e> </e>",
        "explicit" => "<e>" + (item.Type is "integer" or "union" ? "9" : item.Type == "list" ? "9 10" : "given") + "</e>",
        "nil" => "<e xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:nil='true'><!--comment--></e>",
        "child" => "<e><child/></e>", _ => throw new ArgumentOutOfRangeException(nameof(item)),
    }).Replace("<e", "<e xmlns:p='urn:schema-default'", StringComparison.Ordinal);

    public static ReferenceResult Reference(DefaultCase item)
    {
        try
        {
            XmlSchemaSet schemas = Schemas(item);
            using var reader = XmlReader.Create(new StringReader(Element(item)), new() { Schemas = schemas, ValidationType = ValidationType.Schema });
            var position = new XPathDocument(reader, XmlSpace.Preserve).CreateNavigator(); position.MoveToFirstChild();
            return new(true, Trace(position, ((XmlSchemaElement)schemas.GlobalElements[new XmlQualifiedName("e")]!).ElementSchemaType));
        }
        catch (XmlSchemaValidationException) { return new(false, null); }
    }

    public static string Trace(XPathNavigator position, XmlSchemaType? declaredType = null)
    {
        string value = position.Value;
        XmlSchemaType? type = declaredType ?? SchemaTypes.TypeOf(position);
        bool typedText = type?.TypeCode == XmlTypeCode.Integer || type?.Datatype?.Variety is XmlSchemaDatatypeVariety.List or XmlSchemaDatatypeVariety.Union;
        if (typedText && value.Length != 0) value = string.Join(" ", SchemaTypes.TypedValue(position, type!).Select(item => item.Value));
        var text = new StringBuilder(value).Append('|');
        var child = position.Clone();
        if (child.MoveToFirstChild()) do
        {
            // XPath's text() includes all three navigator text representations.
            text.Append(child.NodeType switch {
                XPathNodeType.Comment => "C:", XPathNodeType.ProcessingInstruction => "P:" + child.LocalName + ":",
                XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace => "T:",
                _ => throw new InvalidOperationException("Unexpected validated child: " + child.NodeType),
            }).Append(typedText && child.NodeType is (XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace) ? value : child.Value).Append('|');
        } while (child.MoveToNext());
        return text.ToString();
    }

    public static string TypedSignature(XPathNavigator node, XmlSchemaType type) =>
        string.Join("|", SchemaTypes.TypedValue(node, type).Select(value => value.XmlType!.QualifiedName + ":" + value.Value));

    public static string ExpectedTyped(DefaultCase item, XmlSchemaType type)
    {
        XmlSchemaElement declaration = (XmlSchemaElement)Schemas(item).GlobalElements[new XmlQualifiedName("e")]!;
        string value = declaration.DefaultValue ?? declaration.FixedValue!;
        using var input = XmlReader.Create(new StringReader("<e xmlns:p='urn:schema-default'>" + System.Security.SecurityElement.Escape(value) + "</e>"));
        var explicitNode = new XPathDocument(input, XmlSpace.Preserve).CreateNavigator(); explicitNode.MoveToFirstChild();
        return TypedSignature(explicitNode, type);
    }

    private static string ValueExpression(DefaultCase item, bool parent = false) =>
        item.Type is "integer" or "list" or "union" ? (parent ? "if (self::text()) then data(..) else ." : "data(.)") : ".";

    public static string Input(DefaultCase item, int rows) => "<doc>" + string.Concat(Enumerable.Repeat(Element(item), rows)) + "</doc>";

    public static string Expected(DefaultCase item, int rows, bool inherit)
    {
        ReferenceResult reference = Reference(item);
        if (!reference.Valid) throw new InvalidOperationException("Pipeline input must be schema-valid");
        return string.Concat(Enumerable.Range(1, rows).Select(row => {
            string phase = (inherit ? row : 1).ToString(CultureInfo.InvariantCulture);
            return phase + "," + phase + "~" + reference.Trace + ";";
        }));
    }

    public static string Sheet(DefaultCase item, bool strict, bool inherit) => $$"""
        <xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>
          <xsl:import-schema>{{Schema(item)}}</xsl:import-schema>
          <xsl:mode streamable='yes' use-accumulators='n'/><xsl:output method='text'/>
          <xsl:accumulator name='n' streamable='yes' initial-value='0'><xsl:accumulator-rule match='e' select='$value + 1'/></xsl:accumulator>
          <xsl:template match='/'>
            <xsl:for-each select='doc/e'>
              <xsl:variable name='copy' as='element(e)'><xsl:copy-of select='.' validation='{{(strict ? "strict" : "lax")}}' copy-accumulators='{{(inherit ? "yes" : "no")}}'/></xsl:variable>
              <xsl:for-each select='$copy'>
                <xsl:value-of select="accumulator-before('n'), accumulator-after('n')" separator=','/><xsl:text>~</xsl:text>
                <xsl:value-of select='{{ValueExpression(item)}}' separator=' '/><xsl:text>|</xsl:text>
                <xsl:for-each select='node()'>
                  <xsl:choose><xsl:when test='self::comment()'><xsl:text>C:</xsl:text></xsl:when>
                    <xsl:when test='self::processing-instruction()'><xsl:text>P:</xsl:text><xsl:value-of select='name()'/><xsl:text>:</xsl:text></xsl:when>
                    <xsl:otherwise><xsl:text>T:</xsl:text></xsl:otherwise></xsl:choose>
                  <xsl:value-of select='{{ValueExpression(item, parent: true)}}' separator=' '/><xsl:text>|</xsl:text>
                </xsl:for-each><xsl:text>;</xsl:text>
              </xsl:for-each>
            </xsl:for-each>
          </xsl:template>
        </xsl:stylesheet>
        """;
}
