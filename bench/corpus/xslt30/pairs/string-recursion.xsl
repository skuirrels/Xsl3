<?xml version="1.0"?>
<!-- Tokenizing with tokenize() and upper-case(), the XSLT 2.0+ replacement for recursive templates. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" expand-text="yes">
  <xsl:template match="/orders">
    <tags>
      <xsl:for-each select="order/tags ! tokenize(., ' ')">
        <tag>{upper-case(.)}</tag>
      </xsl:for-each>
    </tags>
  </xsl:template>
</xsl:stylesheet>
