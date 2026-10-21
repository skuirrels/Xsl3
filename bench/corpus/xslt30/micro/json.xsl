<?xml version="1.0" encoding="UTF-8"?>
<!-- Micro benchmark (remaining-scope 1.3): maps and arrays built per order and serialized with the json method. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
  <xsl:output method="json" indent="no"/>
  <xsl:template match="/orders">
    <xsl:sequence select="array { order ! map { 'id': xs:integer(@id), 'customer': string(customer), 'total': xs:decimal(total),
        'tags': array { tokenize(tags) }, 'lines': array { line ! map { 'sku': string(@sku), 'qty': xs:integer(@qty) } } } }"/>
  </xsl:template>
</xsl:stylesheet>
