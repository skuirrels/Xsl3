<?xml version="1.0" encoding="UTF-8"?>
<!-- Micro benchmark (remaining-scope 1.3): fold-left and higher-order functions over a sequence. -->
<xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
  <xsl:template match="/orders">
    <xsl:variable name="totals" select="order/total ! xs:decimal(.)"/>
    <xsl:variable name="stats" select="fold-left($totals, [0, 0], function($acc, $t) { [$acc(1) + $t, max(($acc(2), $t))] })"/>
    <out sum="{$stats(1)}" max="{$stats(2)}" big="{count(filter($totals, function($t) { $t gt 500 }))}"
         doubled="{sum(for-each($totals, function($t) { $t * 2 }))}" pairs="{sum(for-each-pair($totals, tail($totals), function($a, $b) { abs($a - $b) }))}"/>
  </xsl:template>
</xsl:stylesheet>
