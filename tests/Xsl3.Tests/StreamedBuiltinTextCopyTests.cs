// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedBuiltinTextCopyTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string scenario in new[] { "mixed", "text-rule", "predicate-rule", "all-modes", "current-mode", "raw-text", "parameters", "tunnel" })
        foreach (string api in new[] { "modern", "legacy"
#if NET10_0_OR_GREATER
            , "saved"
#endif
        })
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
            yield return new object[] { scenario, api, debug, forward };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BuiltinTextCopyPreservesDispatchAndConstruction(string scenario, string api, bool debug, bool forward)
    {
        string input = "<doc>A<![CDATA[&B]]><e>C</e> D<!--comment--><?pi data?></doc>";
        string body = "<xsl:apply-templates select='doc/node()' mode='m'/>";
        string rules = "";
        string expected = "<out>A&amp;B<e>C</e> D<!--comment--><?pi data?></out>";
        switch (scenario)
        {
            case "text-rule":
            case "predicate-rule":
            case "all-modes":
                string pattern = scenario == "predicate-rule" ? "node()[self::text()]" : "text()";
                string mode = scenario == "all-modes" ? "#all" : "m";
                rules = $"<xsl:template match='{pattern}' mode='{mode}'><xsl:text>[</xsl:text><xsl:value-of select='.'/><xsl:text>]</xsl:text></xsl:template>";
                expected = "<out>[A&amp;B]<e>[C]</e>[ D]<!--comment--><?pi data?></out>";
                break;
            case "current-mode":
                rules = "<xsl:template match='e' mode='#all'><xsl:copy><xsl:apply-templates mode='#current'/></xsl:copy></xsl:template>";
                break;
            case "raw-text":
                body = "<xsl:variable name='v' as='text()'><xsl:text disable-output-escaping='yes'>&lt;raw/&gt;</xsl:text></xsl:variable>"
                    + "<xsl:apply-templates select='$v' mode='m'/>";
                expected = "<out><raw/></out>";
                break;
            case "parameters":
            case "tunnel":
                string tunnel = scenario == "tunnel" ? " tunnel='yes'" : "";
                body = $"<xsl:apply-templates select='doc/node()' mode='m'><xsl:with-param name='p' select='17'{tunnel}/></xsl:apply-templates>";
                rules = $"<xsl:template match='e' mode='m'><xsl:param name='p'{tunnel}/><xsl:copy><xsl:value-of select='$p'/><xsl:apply-templates mode='#current'/></xsl:copy></xsl:template>";
                expected = "<out>A&amp;B<e>17C</e> D<!--comment--><?pi data?></out>";
                break;
        }
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>"
            + "<xsl:mode streamable='yes'/><xsl:mode name='m' streamable='yes' on-no-match='shallow-copy'/>"
            + "<xsl:output omit-xml-declaration='yes'/><xsl:template match='/'><out>" + body
            + "</out></xsl:template>" + rules + "</xsl:stylesheet>";
        var settings = new XsltSettings { StreamedExecution = forward, IncludeDebugInformation = debug };
        using var source = XmlReader.Create(new StringReader(input));
        using var output = new StringWriter();
        if (api == "modern")
        {
            var transform = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
            transform.ApplyTemplates(source, output);
        }
        else
        {
            var transform = new XslCompiledTransform(debug);
            using var style = XmlReader.Create(new StringReader(sheet));
#if NET10_0_OR_GREATER
            if (api == "saved")
            {
                using var image = new MemoryStream();
                transform.CompileToAssembly(style, settings, null, "BuiltinTextCopy", "Saved.BuiltinTextCopy", image);
                transform.Load(Assembly.Load(image.ToArray()).GetType("Saved.BuiltinTextCopy", throwOnError: true)!);
            }
            else
#endif
                transform.Load(style, settings, null);
            transform.Transform(source, null, output);
        }
        Assert.Equal(expected, output.ToString().Replace(" />", "/>"));
        Assert.NotEqual(ReadState.Closed, source.ReadState);
    }
}
