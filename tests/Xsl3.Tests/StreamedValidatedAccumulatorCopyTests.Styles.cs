// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Globalization;
using System.Linq;

namespace Xsl3.Tests;

public partial class StreamedValidatedAccumulatorCopyTests
{
    public static string Sheet(bool inherit, string validation)
    {
        string attribute = validation is "anyType" or "untypedAtomic" ? "type='xs:" + validation + "'" : "validation='" + validation + "'";
        return $$"""
            <xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema'>
              <xsl:import-schema><xs:schema><xs:element name='row' type='xs:string' default='schema-provided'/></xs:schema></xsl:import-schema>
              <xsl:mode streamable='yes' use-accumulators='n'/><xsl:output method='text'/>
              <xsl:accumulator name='n' streamable='yes' initial-value='0'>
                <xsl:accumulator-rule match='row' select='$value + 1'/>
              </xsl:accumulator>
              <xsl:template match='/'>
                <xsl:for-each select='doc/row'>
                  <xsl:variable name='copy' as='element(row)'><xsl:copy-of select='.' copy-accumulators='{{(inherit ? "yes" : "no")}}' {{attribute}}/></xsl:variable>
                  <xsl:for-each select='$copy'>
                    <xsl:value-of select="accumulator-before('n'), accumulator-after('n')" separator=','/><xsl:text>|</xsl:text>
                  </xsl:for-each>
                </xsl:for-each>
              </xsl:template>
            </xsl:stylesheet>
            """;
    }

    public static string Input(int rows) => "<doc>" + string.Concat(Enumerable.Repeat("<row/>", rows)) + "</doc>";

    public static string Expected(int rows, bool inherit) => string.Concat(Enumerable.Range(1, rows).Select(row => {
        string phase = (inherit ? row : 1).ToString(CultureInfo.InvariantCulture);
        return phase + "," + phase + "|";
    }));

}
