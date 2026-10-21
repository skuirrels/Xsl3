<?xml version="1.0" encoding="UTF-8"?>
<!-- Micro benchmark (remaining-scope 1.3): the identity transform by on-no-match="shallow-copy", one rule overridden. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:mode on-no-match="shallow-copy"/>
  <xsl:template match="tags"><tags count="{count(tokenize(.))}"><xsl:apply-templates/></tags></xsl:template>
</xsl:stylesheet>
