<?xml version="1.0"?>
<!-- Temporary trees: a tree per order (variables with content), queried, then grouped. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
  <xsl:template match="/orders">
    <xsl:variable name="summaries" as="element(summary)*">
      <xsl:for-each select="order">
        <xsl:variable name="o">
          <summary id="{@id}" customer="{customer}">
            <xsl:for-each select="line">
              <item sku="{@sku}" qty="{@qty}"/>
            </xsl:for-each>
            <xsl:copy-of select="tags"/>
          </summary>
        </xsl:variable>
        <xsl:sequence select="$o/summary"/>
      </xsl:for-each>
    </xsl:variable>
    <report>
      <xsl:for-each-group select="$summaries" group-by="@customer">
        <customer name="{current-grouping-key()}" orders="{count(current-group())}" items="{sum(current-group()/item/@qty)}"/>
      </xsl:for-each-group>
    </report>
  </xsl:template>
</xsl:stylesheet>
