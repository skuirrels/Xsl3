<?xml version="1.0" encoding="UTF-8"?>
<!-- Micro benchmark (remaining-scope 1.3): maps built with map:merge and xsl:map, looked up, and folded into. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema"
    xmlns:map="http://www.w3.org/2005/xpath-functions/map" exclude-result-prefixes="xs map">
  <xsl:template match="/orders">
    <xsl:variable name="prices" select="map:merge(product ! map { string(@sku): xs:decimal(@price) })"/>
    <xsl:variable name="per-customer" as="map(xs:string, xs:integer)">
      <xsl:map>
        <xsl:for-each-group select="order" group-by="customer">
          <xsl:map-entry key="string(current-grouping-key())" select="count(current-group())"/>
        </xsl:for-each-group>
      </xsl:map>
    </xsl:variable>
    <xsl:variable name="counts" select="fold-left(order/line/@sku, map {}, function($m, $s) { map:put($m, string($s), ($m(string($s)), 0)[1] + 1) })"/>
    <out lines="{sum(order/line ! $prices(string(@sku)))}" customers="{map:size($per-customer)}" skus="{map:size($counts)}"
         max="{max(map:keys($counts) ! $counts(.))}"/>
  </xsl:template>
</xsl:stylesheet>
