<?xml version="1.0" encoding="UTF-8"?>
<!-- Micro benchmark (remaining-scope 1.3): deep recursion in stylesheet functions and named templates. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema"
    xmlns:f="urn:bench" exclude-result-prefixes="xs f">
  <!-- Not a tail call: the depth is the number of items. -->
  <xsl:function name="f:sum" as="xs:decimal">
    <xsl:param name="items" as="xs:decimal*"/>
    <xsl:param name="i" as="xs:integer"/>
    <xsl:sequence select="if ($i gt count($items)) then 0 else $items[$i] + f:sum($items, $i + 1)"/>
  </xsl:function>
  <xsl:template name="count-down">
    <xsl:param name="n" as="xs:integer"/>
    <xsl:param name="acc" as="xs:integer" select="0"/>
    <xsl:choose>
      <xsl:when test="$n le 0"><xsl:sequence select="$acc"/></xsl:when>
      <xsl:otherwise><xsl:call-template name="count-down"><xsl:with-param name="n" select="$n - 1"/><xsl:with-param name="acc" select="$acc + $n"/></xsl:call-template></xsl:otherwise>
    </xsl:choose>
  </xsl:template>
  <xsl:template match="/orders">
    <xsl:variable name="totals" select="subsequence(order/total, 1, 2000) ! xs:decimal(.)"/>
    <out sum="{f:sum($totals, 1)}"><xsl:call-template name="count-down"><xsl:with-param name="n" select="count(order)"/></xsl:call-template></out>
  </xsl:template>
</xsl:stylesheet>
