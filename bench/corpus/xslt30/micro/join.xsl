<?xml version="1.0" encoding="UTF-8"?>
<!-- Micro benchmark (remaining-scope 1.5): a join written without xsl:key, a filter on the whole document inside a loop
     (/orders/product[@sku = $l/@sku]), which automatic indexing turns from O(n²) to O(n). -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
  <xsl:template match="/orders">
    <invoices>
      <xsl:for-each select="order">
        <invoice id="{@id}" amount="{sum(for $l in line return xs:decimal(/orders/product[@sku = $l/@sku]/@price) * xs:integer($l/@qty))}"/>
      </xsl:for-each>
    </invoices>
  </xsl:template>
</xsl:stylesheet>
