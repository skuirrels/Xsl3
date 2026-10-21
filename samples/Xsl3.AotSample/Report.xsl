<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:f="urn:report" exclude-result-prefixes="xs f">
  <xsl:output method="xml" omit-xml-declaration="yes"/>
  <xsl:param name="title" as="xs:string" select="'Orders'"/>

  <xsl:function name="f:total" as="xs:decimal">
    <xsl:param name="orders" as="element(order)*"/>
    <xsl:sequence select="sum($orders ! (xs:decimal(@price) * xs:integer(@quantity)))"/>
  </xsl:function>

  <xsl:template match="/orders">
    <report title="{$title}">
      <xsl:for-each-group select="order" group-by="@customer">
        <xsl:sort select="current-grouping-key()"/>
        <customer name="{current-grouping-key()}" orders="{count(current-group())}" total="{f:total(current-group())}"/>
      </xsl:for-each-group>
      <xsl:variable name="totals" select="map:merge(order ! map{string(@customer): f:total(.)}, map{'duplicates': 'combine'})" xmlns:map="http://www.w3.org/2005/xpath-functions/map"/>
      <largest><xsl:value-of select="max(for $k in map:keys($totals) return sum($totals($k)))" xmlns:map="http://www.w3.org/2005/xpath-functions/map"/></largest>
    </report>
  </xsl:template>
</xsl:stylesheet>
