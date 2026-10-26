// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;

namespace Xsl3.Tests;

internal static partial class StreamingProbe
{
    internal static readonly string[] Workloads = { "identity", "adjacent", "accumulator", "iterate", "merge", "fork", "source-document", "climbing-union", "climbing-intersection", "type-assertion", "type-inspection", "copy-select", "conditional-content", "template-parameter", "function-parameter", "overlap-values" };
    private const string SourceUri = "urn:stream-probe:source", LeftUri = "urn:stream-probe:left", RightUri = "urn:stream-probe:right";
    private const string FirstUri = "urn:stream-probe:first", SecondUri = "urn:stream-probe:second";
    internal static string Stylesheet(string workload)
    {
        const string copy = "<xsl:template match='row' mode='#all'><xsl:copy><xsl:copy-of select='@*'/><xsl:attribute name='seen'>yes</xsl:attribute><xsl:apply-templates mode='#current'/></xsl:copy></xsl:template>";
        string declarations = workload switch {
            "source-document" => "<xsl:mode name='s' streamable='yes' on-no-match='shallow-copy'/>",
            "accumulator" => "<xsl:mode streamable='yes' use-accumulators='total'/><xsl:accumulator name='total' as='xs:integer' initial-value='0' streamable='yes'><xsl:accumulator-rule match='row' phase='end' select='$value + xs:integer(@amount)'/></xsl:accumulator>",
            _ => "<xsl:mode streamable='yes' on-no-match='shallow-copy'/>",
        };
        string body = workload switch {
            "identity" => "<xsl:apply-templates select='doc'/>",
            "copy-select" => "<xsl:copy select='doc'><xsl:apply-templates/></xsl:copy>",
            "conditional-content" => "<doc><xsl:on-non-empty><begin/></xsl:on-non-empty><xsl:where-populated><xsl:sequence select='doc/row'/></xsl:where-populated><xsl:on-non-empty><end/></xsl:on-non-empty><xsl:on-empty><empty/></xsl:on-empty></doc>",
            "template-parameter" => "<xsl:for-each select='doc/row'><xsl:value-of select='position()'/><xsl:text>|</xsl:text><xsl:call-template name='emit-value'><xsl:with-param name='value' as='element(value)' select='value'/></xsl:call-template></xsl:for-each>",
            "function-parameter" => "<xsl:for-each select='doc/row'><xsl:value-of select='position()'/><xsl:text>|</xsl:text><xsl:value-of select='Q{urn:stream-probe:parameter}relay(value)'/><xsl:text>&#10;</xsl:text></xsl:for-each>",
            "overlap-values" => "<xsl:for-each select='doc/row'><xsl:value-of select=\"string-join(descendant::*, '|')\"/><xsl:text>|</xsl:text><xsl:value-of select='position()'/><xsl:text>&#10;</xsl:text></xsl:for-each>",
            "source-document" => $"<xsl:source-document href='{SourceUri}' streamable='yes'><xsl:apply-templates select='doc' mode='s'/></xsl:source-document>",
            "adjacent" => "<xsl:for-each-group select='doc/row' group-adjacent='@group'><xsl:value-of select='current-grouping-key()'/><xsl:text>|</xsl:text><xsl:value-of select='sum(current-group()/@amount)'/><xsl:text>&#10;</xsl:text></xsl:for-each-group>",
            "accumulator" => "<xsl:value-of select=\"accumulator-after('total')\"/>",
            "climbing-union" or "climbing-intersection" => "<xsl:for-each select='doc/row[@group = &quot;a&quot;]/value/ancestor::*/@id "
                + (workload == "climbing-union" ? "union" : "intersect")
                + " doc/row[@amount = &quot;3&quot;]/text/em/ancestor::*/@id'><xsl:value-of select='.'/><xsl:text>&#10;</xsl:text></xsl:for-each>",
            "type-assertion" => "<xsl:for-each select='doc/row treat as element(row)+'><xsl:value-of select='@id'/><xsl:text>&#10;</xsl:text></xsl:for-each>",
            "type-inspection" => "<xsl:value-of select='doc/row instance of element(row)+'/>",
            "iterate" => "<xsl:iterate select='doc/row'><xsl:param name='balance' as='xs:integer' select='0'/><xsl:on-completion select='$balance'/><xsl:next-iteration><xsl:with-param name='balance' select='$balance + xs:integer(@amount)'/></xsl:next-iteration></xsl:iterate>",
            "merge" => $"<xsl:merge><xsl:merge-source name='a' for-each-source=\"'{LeftUri}'\" streamable='yes' select='doc/row'><xsl:merge-key select='xs:integer(@id)'/></xsl:merge-source><xsl:merge-source name='b' for-each-source=\"'{RightUri}'\" streamable='yes' select='doc/row'><xsl:merge-key select='xs:integer(@id)'/></xsl:merge-source><xsl:merge-action><xsl:value-of select='current-merge-key()'/><xsl:text>|</xsl:text><xsl:value-of select='sum(current-merge-group()/@amount)'/><xsl:text>&#10;</xsl:text></xsl:merge-action></xsl:merge>",
            "fork" => $"<xsl:fork><xsl:sequence><xsl:result-document href='{FirstUri}'><xsl:for-each select='doc/row'><xsl:value-of select='@id'/><xsl:text>&#10;</xsl:text></xsl:for-each></xsl:result-document></xsl:sequence><xsl:sequence><xsl:result-document href='{SecondUri}'><xsl:for-each select='doc/row'><xsl:value-of select='xs:integer(@amount) + xs:integer(value)'/><xsl:text>&#10;</xsl:text></xsl:for-each></xsl:result-document></xsl:sequence></xsl:fork>",
            _ => throw new ArgumentException("Unknown workload", nameof(workload)),
        };
        string method = workload is "identity" or "source-document" or "copy-select" or "conditional-content" ? "xml" : "text";
        string templates = workload is "identity" or "source-document" or "copy-select" ? copy
            : workload == "template-parameter" ? "<xsl:template name='emit-value'><xsl:context-item use='absent'/><xsl:param name='value' as='xs:integer'/><xsl:value-of select='$value'/><xsl:text>&#10;</xsl:text></xsl:template>"
            : workload == "function-parameter" ? "<xsl:function xmlns:p='urn:stream-probe:parameter' name='p:relay' streamability='absorbing' as='xs:integer'><xsl:param name='items' as='item()*'/><xsl:sequence select='p:consume($items)'/></xsl:function><xsl:function xmlns:p='urn:stream-probe:parameter' name='p:consume' streamability='absorbing' as='xs:integer'><xsl:param name='items' as='element(value)*'/><xsl:sequence select='xs:integer($items)'/></xsl:function>" : "";
        return $"<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' exclude-result-prefixes='xs'>{declarations}<xsl:output method='{method}' omit-xml-declaration='yes' encoding='utf-8' byte-order-mark='no'/><xsl:template match='/'>{body}</xsl:template>{templates}</xsl:stylesheet>";
    }

}
