<?xml version="1.0" encoding="UTF-8"?>
<!-- Micro benchmark (remaining-scope 1.3): string functions and concatenation. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:template match="/orders">
    <out>
      <xsl:for-each select="order">
        <o><xsl:value-of select="string-join((upper-case(substring-after(customer, ' ')), translate(tags, 'aeiou', 'AEIOU'),
            substring(normalize-space(customer), 1, 4), string-length(tags), contains(tags, 'new'), starts-with(customer, 'Customer 0')), '|')"/></o>
      </xsl:for-each>
      <all><xsl:value-of select="string-length(string-join(order/customer, ','))"/></all>
    </out>
  </xsl:template>
</xsl:stylesheet>
