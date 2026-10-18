<?xml version="1.0"?>
<!-- Muenchian grouping: keys, generate-id() and aggregation. -->
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:key name="by-customer" match="order" use="customer"/>
  <xsl:template match="/orders">
    <customers>
      <xsl:for-each select="order[generate-id() = generate-id(key('by-customer', customer)[1])]">
        <xsl:sort select="customer"/>
        <customer name="{customer}" orders="{count(key('by-customer', customer))}" total="{sum(key('by-customer', customer)/total)}"/>
      </xsl:for-each>
    </customers>
  </xsl:template>
</xsl:stylesheet>
