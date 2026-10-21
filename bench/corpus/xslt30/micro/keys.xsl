<?xml version="1.0" encoding="UTF-8"?>
<!-- Micro benchmark (remaining-scope 1.3): xsl:key lookups in a join of order lines against products. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
  <xsl:key name="product" match="product" use="@sku"/>
  <xsl:key name="by-customer" match="order" use="customer"/>
  <xsl:template match="/orders">
    <invoices>
      <xsl:for-each select="order">
        <invoice id="{@id}" amount="{sum(line ! (xs:decimal(key('product', @sku)/@price) * xs:integer(@qty)))}"
                 siblings="{count(key('by-customer', customer)) - 1}"/>
      </xsl:for-each>
    </invoices>
  </xsl:template>
</xsl:stylesheet>
