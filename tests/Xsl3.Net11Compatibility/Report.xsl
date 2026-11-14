<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:mode streamable="yes"/>
  <xsl:output method="text"/>
  <xsl:template match="/">
    <xsl:apply-templates select="report/item"/>
  </xsl:template>
  <xsl:template match="item">
    <xsl:if test="position() gt 1"><xsl:text>,</xsl:text></xsl:if>
    <xsl:value-of select="."/>
  </xsl:template>
</xsl:stylesheet>
