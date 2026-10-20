<?xml version="1.0"?>
<!-- HTML output with conditional content; position() instead of xsl:number, text value templates. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" expand-text="yes">
  <xsl:output method="html" indent="yes" version="4.0"/>
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
    <tr class="{if (position() mod 2 = 1) then 'even' else 'odd '}">
      <td>{position()}</td>
      <td>{customer}</td>
      <td>{if (total > 500) then 'large' else if (total > 100) then 'medium' else 'small'}</td>
      <td>{count(line)}</td>
    </tr>
  </xsl:template>
</xsl:stylesheet>
