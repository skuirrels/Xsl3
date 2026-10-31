// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class GroundedAdjacentGroupingTests
{
    private const string Input = "<doc><row id='1' k='a'/><row id='2' k='a'/><row id='3' k='b'/><row id='4' k='b'/><row id='5' k='a'/></doc>";
    private static string Sheet(string body) => "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' exclude-result-prefixes='xs'><xsl:output omit-xml-declaration='yes'/><xsl:template match='/'>" + body + "</xsl:template></xsl:stylesheet>";

    private static string Run(string sheet, bool debug, bool legacy, Action<string>? message = null, Action<string, string>? trace = null)
    {
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        var settings = new XsltSettings { StreamedExecution = false, IncludeDebugInformation = debug };
        if (legacy)
        {
            var transform = new XslCompiledTransform(debug);
            transform.Load(XmlReader.Create(new StringReader(sheet)), settings, null);
            var arguments = new XsltArgumentList { TraceHandler = trace };
            arguments.XsltMessageEncountered += (_, e) => message?.Invoke(e.Message);
            transform.Transform(input, arguments, output);
        }
        else
        {
            var transform = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
            transform.MessageHandler = message;
            transform.TraceHandler = trace;
            transform.ApplyTemplates(input, output);
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PopulationKeysRunOnceBeforeActionsAndKeepTheirFocus(bool debug, bool legacy)
    {
        var events = new List<string>();
        string sheet = Sheet("""
            <out><xsl:for-each-group select="doc/row" group-adjacent="trace((position()-1) idiv 2, concat('key:',position(),':',last()))">
              <xsl:message select="concat('action:',position(),':',last())"/>
              <g p="{position()}" size="{last()}" key="{current-grouping-key()}" n="{count(current-group())}" again="{count(current-group())}" ids="{current-group() ! string(@id)}"/>
            </xsl:for-each-group></out>
            """);
        Assert.Equal("<out><g p=\"1\" size=\"3\" key=\"0\" n=\"2\" again=\"2\" ids=\"1 2\"/><g p=\"2\" size=\"3\" key=\"1\" n=\"2\" again=\"2\" ids=\"3 4\"/><g p=\"3\" size=\"3\" key=\"2\" n=\"1\" again=\"1\" ids=\"5\"/></out>",
            Run(sheet, debug, legacy, events.Add, (label, _) => events.Add(label)));
        Assert.Equal(new[] { "key:1:5", "key:2:5", "key:3:5", "key:4:5", "key:5:5", "action:1:3", "action:2:3", "action:3:3" }, events);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RetainedGroupsSurviveCursorCompletionAndCallSiteReuse(bool debug, bool legacy)
    {
        string sheet = Sheet("""
            <xsl:variable name="source" select="."/>
            <xsl:variable name="captured" as="function(*)*">
              <xsl:for-each select="1 to 2">
                <xsl:for-each-group select="$source/doc/row" group-adjacent="@k">
                  <xsl:variable name="members" select="current-group()"/>
                  <xsl:variable name="size" select="last()"/>
                  <xsl:sequence select="function() { ($members, $size) }"/>
                </xsl:for-each-group>
              </xsl:for-each>
            </xsl:variable>
            <out><xsl:for-each select="reverse($captured)">
              <xsl:variable name="value" select=".()"/>
              <g ids="{$value[. instance of node()] ! string(@id)}" size="{$value[last()]}"
                 same="{every $n in $value[. instance of node()] satisfies $n is $source/doc/row[@id = $n/@id]}"/>
            </xsl:for-each></out>
            """);
        Assert.Equal("<out><g ids=\"5\" size=\"3\" same=\"true\"/><g ids=\"3 4\" size=\"3\" same=\"true\"/><g ids=\"1 2\" size=\"3\" same=\"true\"/><g ids=\"5\" size=\"3\" same=\"true\"/><g ids=\"3 4\" size=\"3\" same=\"true\"/><g ids=\"1 2\" size=\"3\" same=\"true\"/></out>", Run(sheet, debug, legacy));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ForksCaptureTheCountAndCurrentGroupWithoutTheMutableCursor(bool debug, bool legacy)
    {
        string sheet = Sheet("<out><xsl:for-each-group select='doc/row' group-adjacent='@k'><g><xsl:fork><xsl:sequence select='last()'/><xsl:sequence select='count(current-group())'/></xsl:fork></g></xsl:for-each-group></out>");
        Assert.Equal("<out><g>3 2</g><g>3 2</g><g>3 1</g></out>", Run(sheet, debug, legacy));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InvalidLateKeysFailBeforeAnyAction(bool debug, bool legacy)
    {
        var messages = new List<string>();
        string sheet = Sheet("<xsl:for-each-group select='doc/row' group-adjacent='if (@id=5) then () else @k'><xsl:message select='position()'/></xsl:for-each-group>");
        Exception error = legacy
            ? Assert.Throws<Xsl3.Xdm.XdmException>(() => Run(sheet, debug, legacy, messages.Add))
            : Assert.Throws<XsltDynamicException>(() => Run(sheet, debug, legacy, messages.Add));
        Assert.Contains("XTTE1100", error.Message);
        Assert.Empty(messages);
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedGroupsKeepContextSizeAndMemberIdentity(bool debug)
    {
        string sheet = Sheet("<xsl:variable name='source' select='.'/><out><xsl:for-each-group select='doc/row' group-adjacent='@k'><g p='{position()}' size='{last()}' same='{current-group()[1] is $source/doc/row[@id=current()/@id]}' ids='{current-group() ! string(@id)}'/></xsl:for-each-group></out>");
        var settings = new XsltSettings { StreamedExecution = false };
        using var image = new MemoryStream();
        new XslCompiledTransform(debug).CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null,
            "GroundedAdjacent" + Guid.NewGuid().ToString("N"), "Saved.Adjacent", image);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(image.ToArray()).GetType("Saved.Adjacent", true)!, settings);
        using var input = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("<out><g p=\"1\" size=\"3\" same=\"true\" ids=\"1 2\"/><g p=\"2\" size=\"3\" same=\"true\" ids=\"3 4\"/><g p=\"3\" size=\"3\" same=\"true\" ids=\"5\"/></out>", output.ToString());
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }
#endif
}
