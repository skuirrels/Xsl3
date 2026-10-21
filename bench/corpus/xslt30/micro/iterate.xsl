<?xml version="1.0" encoding="UTF-8"?>
<!-- Micro benchmark (remaining-scope 1.3): xsl:iterate with running parameters. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
  <xsl:template match="/orders">
    <out>
      <xsl:iterate select="order">
        <xsl:param name="running" as="xs:decimal" select="0"/>
        <xsl:param name="best" as="xs:decimal" select="0"/>
        <xsl:on-completion><total running="{$running}" best="{$best}"/></xsl:on-completion>
        <xsl:variable name="t" select="xs:decimal(total)"/>
        <xsl:if test="$t gt $best"><record id="{@id}" total="{$t}"/></xsl:if>
        <xsl:next-iteration>
          <xsl:with-param name="running" select="$running + $t"/>
          <xsl:with-param name="best" select="max(($best, $t))"/>
        </xsl:next-iteration>
      </xsl:iterate>
    </out>
  </xsl:template>
</xsl:stylesheet>
