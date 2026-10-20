<?xml version="1.0"?>
<!-- Grouping with xsl:for-each-group, the XSLT 2.0+ replacement for Muenchian grouping. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:template match="/orders">
    <customers>
      <xsl:for-each-group select="order" group-by="customer">
        <xsl:sort select="current-grouping-key()"/>
        <customer name="{current-grouping-key()}" orders="{count(current-group())}" total="{sum(current-group()/total)}"/>
      </xsl:for-each-group>
    </customers>
  </xsl:template>
</xsl:stylesheet>
