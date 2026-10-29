// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace Xsl3.Tests;

/// <summary>The six P7G contracts, shared by managed tests and saved-style consumers.</summary>
internal static class CopiedAttributeIdentityFixtures
{
    internal const string AttributeNamespace = "urn:p7g:identity";

    internal static string StyleName(string declaration, string validation, bool forward, bool debug) =>
        $"Identity_{declaration}_{validation}_{(forward ? "Forward" : "Grounded")}_{(debug ? "Debug" : "Normal")}";

    internal static IEnumerable<(string Declaration, string Validation)> Contracts()
    {
        foreach (string declaration in new[] { "ID", "IDREF", "IDREFS" })
        foreach (string validation in new[] { "preserve", "strip" })
            yield return (declaration, validation);
    }

    internal static string Input(string declaration, bool declaredElements, string target = "target", string other = "other") => $$"""
        <!DOCTYPE doc [
          {{(declaredElements ? "<!ELEMENT doc (p:row)><!ELEMENT p:row EMPTY>" : "")}}
          <!ATTLIST p:row p:identity {{declaration}} #IMPLIED>
        ]>
        <doc xmlns:p="{{AttributeNamespace}}"><p:row p:identity="{{(declaration == "IDREFS" ? $"  {target} {other} {target}  " : target)}}" xml:id="  normalized  " plain="{{target}}"/></doc>
        """;

    internal static XmlReader Reader(string declaration, bool declaredElements, bool positioned = false,
        string target = "target", string other = "other")
    {
        var reader = XmlReader.Create(new StringReader(Input(declaration, declaredElements, target, other)),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse }, "urn:p7g:source");
        if (positioned) reader.MoveToContent();
        return reader;
    }

    internal static string DetachedSheet(string validation) => Wrapper($$"""
        <xsl:copy-of select="@p:identity" validation="{{validation}}"/>
        <xsl:copy-of select="@p:identity" validation="{{validation}}"/>
        """);

    internal static string SourceLookupSheet() => Wrapper("""
        <xsl:value-of select="concat(count(id('target', root(.))), '|', count(idref(('target', 'other'), root(.))), '|', count(id('normalized', root(.))))"/>
        """, streamable: false);

    internal static string RoundTripSheet(string validation) => Wrapper($$"""
        <xsl:variable name="attributes" as="attribute()*"><xsl:copy-of select="@*" validation="{{validation}}"/></xsl:variable>
        <xsl:variable name="second" as="attribute()*"><xsl:copy-of select="$attributes" validation="{{validation}}"/></xsl:variable>
        <xsl:variable name="preserved" as="document-node()">
          <xsl:document validation="preserve"><copied xsl:validation="preserve"><xsl:sequence select="$second/self::attribute()"/></copied></xsl:document>
        </xsl:variable>
        <xsl:variable name="stripped" as="document-node()">
          <xsl:document validation="strip"><copied xsl:validation="strip"><xsl:sequence select="$second/self::attribute()"/></copied></xsl:document>
        </xsl:variable>
        <xsl:variable name="target" select="tokenize(string(@p:identity), '\s+')[1]"/>
        <xsl:value-of select="concat(count($attributes/..), '|', $attributes[1] is @p:identity, '|', $attributes[1] is $second[1], '|', name($second[1]), '|', namespace-uri($second[1]), '|', string($second[1]), '|', $second[1] instance of attribute(*, xs:untypedAtomic), ';')"/>
        <xsl:for-each select="($preserved, $stripped)">
          <xsl:value-of select="concat(count(id($target, .)), '|', count(idref($target, .)), '|', count(idref(tokenize(string(copied/@p:identity), '\s+'), .)), '|', count(id('normalized', .)), '|', string(copied/@xml:id), '|', string(copied/@plain), '|', count(idref(copied/@plain, .)/self::attribute(plain)), '|', copied/@p:identity instance of attribute(*, xs:untypedAtomic), ';')"/>
        </xsl:for-each>
        """);

    // Expectations follow XSLT 3.0 §§4.5, 5.7.1 and 25.4.1.1: DTD identity is independent
    // of type annotations, and preserve/strip keep both identity properties on copied attributes.
    internal static string Expected(string declaration, string target = "target", string other = "other")
    {
        bool id = declaration == "ID";
        string value = declaration == "IDREFS" ? $"{target} {other} {target}" : target;
        string result = $"{(id ? 1 : 0)}|{(id ? 0 : 1)}|{(id ? 0 : 1)}|1|normalized|{target}|0|true;";
        return $"0|false|false|p:identity|{AttributeNamespace}|{value}|true;{result}{result}";
    }

    private static string Wrapper(string body, bool streamable = true) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:p="{{AttributeNamespace}}">
          <xsl:mode streamable="{{(streamable ? "yes" : "no")}}"/><xsl:output method="text"/>
          <xsl:template match="/"><xsl:for-each select="doc/p:row">{{body}}</xsl:for-each></xsl:template>
        </xsl:stylesheet>
        """;
}
