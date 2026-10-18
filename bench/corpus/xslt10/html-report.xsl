<?xml version="1.0"?>
<!-- HTML output method with xsl:number, conditional content and attribute value templates. -->
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="html" indent="yes"/>
  <xsl:template match="/orders">
    <html>
      <head><title>Orders</title></head>
      <body>
        <table>
          <xsl:apply-templates select="order"/>
        </table>
      </body>
    </html>
  </xsl:template>
  <xsl:template match="order">
    <tr class="{substring('odd even', 1 + 4 * (position() mod 2), 4)}">
      <td><xsl:number/></td>
      <td><xsl:value-of select="customer"/></td>
      <td>
        <xsl:choose>
          <xsl:when test="total &gt; 500">large</xsl:when>
          <xsl:when test="total &gt; 100">medium</xsl:when>
          <xsl:otherwise>small</xsl:otherwise>
        </xsl:choose>
      </td>
      <td><xsl:value-of select="count(line)"/></td>
    </tr>
  </xsl:template>
</xsl:stylesheet>
