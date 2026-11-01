// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Schema;
using Xsl3.Tree;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public sealed class DirectAttributeCopyTests
{
    private static string Sheet(string body, string validation = "strip") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            xmlns:xs="http://www.w3.org/2001/XMLSchema" default-validation="{{validation}}">
          <xsl:mode streamable="yes"/>
          <xsl:output method="text"/>
          <xsl:template match="/"><xsl:for-each select="doc/row">{{body}}</xsl:for-each></xsl:template>
        </xsl:stylesheet>
        """;

    private static XmlReader TypedInput()
    {
        const string schema = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="doc"><xs:complexType><xs:sequence>
                <xs:element name="row"><xs:complexType>
                  <xs:attribute name="id" type="xs:ID"/>
                  <xs:attribute name="ref" type="xs:IDREFS"/>
                  <xs:attribute name="name" type="xs:QName"/>
                  <xs:attribute name="number" type="xs:decimal"/>
                </xs:complexType></xs:element>
              </xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """;
        var schemas = new XmlSchemaSet();
        using (var source = XmlReader.Create(new StringReader(schema))) schemas.Add(null, source);
        return XmlReader.Create(new StringReader("<doc xmlns:p='urn:direct-attribute'><row id='target' ref='target target' name='p:value' number='1.25'/></doc>"),
            new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StrippedRawCopiesKeepIdentityWithoutKeepingTypesUnderPreserveDefault(bool debug)
    {
        const string body = "<xsl:copy-of select='@*' validation='strip'/><xsl:copy-of select='@*' validation='strip'/>";
        foreach (bool forward in new[] { false, true })
        {
            using var input = TypedInput();
            XdmValue result = new XsltCompiler { Settings = new() { StreamedExecution = forward, SchemaAware = true, IncludeDebugInformation = debug } }
                .Compile(new StringReader(Sheet(body, "preserve"))).CreateTransformer().ApplyTemplates(input);
            Assert.Equal(8, result.Count);
            for (int i = 0; i < result.Count; i++)
            {
                var copy = Assert.IsType<CompactNavigator>(Assert.IsType<XdmNode>(result[i]).Navigator);
                Assert.Null(SchemaTypes.TypeOf(copy));
                Assert.Equal(i % 4 == 0, copy.IsIdAttribute);
                Assert.Equal(i % 4 == 1, copy.IsIdrefAttribute);
                Assert.False(copy.MoveToParent());
                if (i >= 4) Assert.False(copy.IsSamePosition(Assert.IsType<XdmNode>(result[i - 4]).Navigator));
            }
            Assert.NotEqual(ReadState.Closed, input.ReadState);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StrippedAttributesInsertedIntoPreservedContentRetainOnlyIdentity(bool legacy, bool debug)
    {
        const string body = """
            <xsl:variable name="tree" as="document-node()"><xsl:document validation="preserve">
              <out xsl:validation="preserve"><xsl:copy-of select="@*" validation="strip"/></out>
            </xsl:document></xsl:variable>
            <xsl:value-of select="concat(count(id('target', $tree)), '|', count(idref(('target', 'other'), $tree)), '|',
              every $a in $tree/out/@* satisfies $a instance of attribute(*, xs:untypedAtomic))"/>
            """;
        foreach (bool forward in new[] { false, true })
        {
            using var input = TypedInput();
            Assert.Equal("1|1|true", Run(Sheet(body, "preserve"), input, forward, legacy, debug));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStaticallyTypedAttributeCopyDoesNotRetainItsSourceType(bool debug)
    {
        const string body = """
            <xsl:variable name="original" as="attribute(*, xs:ID)"><xsl:copy-of select="@id" validation="preserve"/></xsl:variable>
            <xsl:variable name="copy" as="attribute()"><xsl:copy-of select="$original" validation="strip"/></xsl:variable>
            <xsl:value-of select="concat($copy instance of attribute(*, xs:ID), '|', $copy instance of attribute(*, xs:untypedAtomic))"/>
            """;
        foreach (bool forward in new[] { false, true })
        foreach (bool legacy in new[] { false, true })
        {
            using var input = TypedInput();
            Assert.Equal("false|true", Run(Sheet(body, "preserve"), input, forward, legacy, debug));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectConstructionKeepsNamespaceFixupXmlIdAndLastAttributeWins(bool debug)
    {
        const string body = """
            <xsl:variable name="tree" as="element()"><p:out xmlns:p="urn:other">
              <xsl:copy-of select="@*" copy-namespaces="no" copy-accumulators="yes"/>
              <xsl:attribute name="plain">replacement</xsl:attribute>
            </p:out></xsl:variable>
            <xsl:value-of select="concat(namespace-uri($tree), '|', namespace-uri($tree/@*:key), '|',
              $tree/@*:key, '|', $tree/@xml:id, '|', $tree/@plain, '|', count($tree/@*))"/>
            """;
        foreach (bool forward in new[] { false, true })
        foreach (bool legacy in new[] { false, true })
        {
            using var input = XmlReader.Create(new StringReader("<doc xmlns:p='urn:direct-attribute'><row p:key='one &amp; two' xml:id='  normalized  ' plain='original'/></doc>"));
            Assert.Equal("urn:other|urn:direct-attribute|one & two|normalized|replacement|3", Run(Sheet(body), input, forward, legacy, debug));
        }
    }

    [Theory]
    [InlineData("<xsl:document><xsl:copy-of select='@*'/></xsl:document>", "XTDE0420")]
    [InlineData("<out><xsl:text>child</xsl:text><xsl:copy-of select='@*'/></out>", "XTDE0410")]
    public void InvalidAttributeConstructionKeepsItsDynamicError(string body, string code)
    {
        foreach (bool forward in new[] { false, true })
        foreach (bool legacy in new[] { false, true })
        {
            using var input = XmlReader.Create(new StringReader("<doc><row key='value'/></doc>"));
            if (legacy)
                Assert.Equal(code, Assert.Throws<XdmException>(() => Run(Sheet(body), input, forward, legacy, false)).Code);
            else
                Assert.Equal(code, Assert.IsType<XdmException>(Assert.Throws<XsltDynamicException>(() => Run(Sheet(body), input, forward, legacy, false)).InnerException).Code);
        }
    }

    private static string Run(string sheet, XmlReader input, bool forward, bool legacy, bool debug)
    {
        var settings = new XsltSettings { StreamedExecution = forward, SchemaAware = true, IncludeDebugInformation = debug };
        using var output = new StringWriter();
        if (legacy)
        {
            var transform = new XslCompiledTransform(debug);
            using var source = XmlReader.Create(new StringReader(sheet));
            transform.Load(source, settings, null);
            transform.Transform(input, null, output);
        }
        else
            new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(input, output);
        return output.ToString();
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedStylesUseDirectAttributeConstructionAndRetainStripping(bool debug)
    {
        const string body = """
            <xsl:variable name="tree" as="element()"><out xsl:validation="preserve"><xsl:copy-of select="@*" validation="strip"/></out></xsl:variable>
            <xsl:value-of select="every $a in $tree/@* satisfies $a instance of attribute(*, xs:untypedAtomic)"/>
            """;
        var settings = new XsltSettings { StreamedExecution = true, SchemaAware = true };
        using var image = new MemoryStream();
        using (var source = XmlReader.Create(new StringReader(Sheet(body, "preserve"))))
            new XslCompiledTransform(debug).CompileToAssembly(source, settings, null, "DirectAttributeCopy", "Saved.Copy", image);
        image.Position = 0;
        using (var pe = new PEReader(image, PEStreamOptions.LeaveOpen))
        {
            MetadataReader metadata = pe.GetMetadataReader();
            Assert.Contains(metadata.MemberReferences, h => metadata.GetString(metadata.GetMemberReference(h).Name) == "XsltCopyOfStrippedAttribute");
        }
        var transform = new XslCompiledTransform();
        transform.Load(Assembly.Load(image.ToArray()).GetType("Saved.Copy", true)!, settings);
        using var input = TypedInput();
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("true", output.ToString());
    }
#endif
}
