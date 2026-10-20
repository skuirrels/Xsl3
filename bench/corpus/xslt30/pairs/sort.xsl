<?xml version="1.0"?>
<!-- Multi-key sort with number and string keys, with text value templates. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" expand-text="yes">
  <xsl:template match="/orders">
    <sorted>
      <xsl:for-each select="order">
        <xsl:sort select="customer"/>
        <xsl:sort select="number(total)" order="descending"/>
        <o id="{@id}" c="{customer}" t="{total}"/>
      </xsl:for-each>
    </sorted>
  </xsl:template>
</xsl:stylesheet>
