<?xml version="1.0"?>
<!-- A join through a map, plus format-number. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:map="http://www.w3.org/2005/xpath-functions/map" exclude-result-prefixes="map">
  <xsl:template match="/orders">
    <xsl:variable name="products" as="map(*)" select="map:merge(product ! map:entry(string(@sku), .))"/>
    <lines>
      <xsl:for-each select="order/line">
        <xsl:variable name="product" select="$products(string(@sku))"/>
        <line order="{../@id}" name="{$product/@name}" amount="{format-number(@qty * $product/@price, '#,##0.00')}"/>
      </xsl:for-each>
    </lines>
  </xsl:template>
</xsl:stylesheet>
