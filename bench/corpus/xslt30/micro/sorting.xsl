<?xml version="1.0" encoding="UTF-8"?>
<!-- Micro benchmark (remaining-scope 1.3): multi-key sorting, numeric and string, and fn:sort. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
  <xsl:template match="/orders">
    <sorted>
      <xsl:for-each select="order">
        <xsl:sort select="xs:decimal(total)" order="descending"/>
        <xsl:sort select="customer"/>
        <o id="{@id}"/>
      </xsl:for-each>
      <by-tags><xsl:value-of select="sort(order/tags, (), function($t) { string-length($t) })[position() le 10]"/></by-tags>
    </sorted>
  </xsl:template>
</xsl:stylesheet>
