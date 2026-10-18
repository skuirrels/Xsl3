<?xml version="1.0"?>
<!-- XSLT 1.0 string tokenizing by tail recursion, the idiom XSLT 2.0's tokenize() replaces. -->
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:template match="/orders">
    <tags>
      <xsl:for-each select="order">
        <xsl:call-template name="split">
          <xsl:with-param name="s" select="concat(tags, ' ')"/>
        </xsl:call-template>
      </xsl:for-each>
    </tags>
  </xsl:template>
  <xsl:template name="split">
    <xsl:param name="s"/>
    <xsl:if test="string-length($s) &gt; 0">
      <tag><xsl:value-of select="translate(substring-before($s, ' '), 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ')"/></tag>
      <xsl:call-template name="split">
        <xsl:with-param name="s" select="substring-after($s, ' ')"/>
      </xsl:call-template>
    </xsl:if>
  </xsl:template>
</xsl:stylesheet>
