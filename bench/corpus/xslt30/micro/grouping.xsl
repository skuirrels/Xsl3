<?xml version="1.0" encoding="UTF-8"?>
<!-- Micro benchmark (remaining-scope 1.3): xsl:for-each-group by a key, with aggregates per group. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
  <xsl:template match="/orders">
    <customers>
      <xsl:for-each-group select="order" group-by="customer">
        <xsl:sort select="current-grouping-key()"/>
        <customer name="{current-grouping-key()}" orders="{count(current-group())}" total="{sum(current-group()/total)}"/>
      </xsl:for-each-group>
      <xsl:for-each-group select="order" group-adjacent="xs:integer(@id) idiv 100">
        <block n="{current-grouping-key()}" max="{max(current-group()/total)}"/>
      </xsl:for-each-group>
    </customers>
  </xsl:template>
</xsl:stylesheet>
