<?xml version="1.0" encoding="UTF-8"?>
<!-- Micro benchmark (remaining-scope 1.3): matches, replace, tokenize and xsl:analyze-string. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:template match="/orders">
    <out>
      <xsl:for-each select="order">
        <o sale="{matches(tags, '(^|\s)sale(\s|$)')}" code="{replace(customer, '^Customer (\d+)$', 'C-$1')}" words="{count(tokenize(tags, '\s+'))}">
          <xsl:analyze-string select="tags" regex="(\w)(\w*)">
            <xsl:matching-substring><xsl:value-of select="upper-case(regex-group(1)) || regex-group(2)"/></xsl:matching-substring>
            <xsl:non-matching-substring>_</xsl:non-matching-substring>
          </xsl:analyze-string>
        </o>
      </xsl:for-each>
    </out>
  </xsl:template>
</xsl:stylesheet>
