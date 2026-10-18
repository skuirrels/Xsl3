<?xml version="1.0"?>
<!-- A join through xsl:key, plus format-number. -->
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:key name="product" match="product" use="@sku"/>
  <xsl:template match="/orders">
    <lines>
      <xsl:for-each select="order/line">
        <line order="{../@id}" name="{key('product', @sku)/@name}" amount="{format-number(@qty * key('product', @sku)/@price, '#,##0.00')}"/>
      </xsl:for-each>
    </lines>
  </xsl:template>
</xsl:stylesheet>
