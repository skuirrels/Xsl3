// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Reflection;
using System.Security;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedJsonTests
{
    private const string JsonNamespace = "http://www.w3.org/2005/xpath-functions";
    private const string MapInput = "<j:map xmlns:j='" + JsonNamespace + "'><j:string key='s'>one</j:string><j:number key='n'>2.5</j:number><j:boolean key='b'>1</j:boolean><j:null key='z'/><j:array key='a'><j:string>two</j:string><j:number>3</j:number></j:array></j:map>";
    private const string MapOutput = "{\"s\":\"one\",\"n\":2.5,\"b\":true,\"z\":null,\"a\":[\"two\",3]}";

    [Theory]
    [InlineData("xml-to-json(.)", MapInput, MapOutput)]
    [InlineData("xml-to-json(/*)", MapInput, MapOutput)]
    [InlineData("xml-to-json(/j:map)", MapInput, MapOutput)]
    [InlineData("xml-to-json(doc/j:map)", "<doc>" + MapInput + "</doc>", MapOutput)]
    [InlineData("xml-to-json(doc/j:string)", "<doc xmlns:j='" + JsonNamespace + "'><j:string>a<![CDATA[b]]><!--ignored--><?pi ignored?> c</j:string></doc>", "\"ab c\"")]
    [InlineData("xml-to-json(doc/j:number)", "<doc xmlns:j='" + JsonNamespace + "'><j:number>1e2</j:number></doc>", "100")]
    [InlineData("xml-to-json(doc/j:boolean)", "<doc xmlns:j='" + JsonNamespace + "'><j:boolean> 0 </j:boolean></doc>", "false")]
    [InlineData("xml-to-json(doc/j:null)", "<doc xmlns:j='" + JsonNamespace + "'><j:null><!--ignored--><?pi ignored?></j:null></doc>", "null")]
    [InlineData("xml-to-json(doc/missing)", "<doc/>", "")]
    [InlineData("xml-to-json(())", "<doc/>", "")]
    [InlineData("xml-to-json((), map{'indent':42})", "<doc/>", "")]
    [InlineData("doc/row ! xml-to-json(j:number)", "<doc xmlns:j='" + JsonNamespace + "'><row><j:number>1</j:number></row><row><j:number>2</j:number></row></doc>", "1|2")]
    [InlineData("xml-to-json(/*, map{'indent':true()})", "<j:map xmlns:j='" + JsonNamespace + "'><j:number key='n'>1</j:number></j:map>", "{\n  \"n\": 1\n}")]
    [InlineData("/* => xml-to-json()", MapInput, MapOutput)]
    [InlineData("xml-to-json(//j:number)", "<doc xmlns:j='" + JsonNamespace + "'><row><j:number>1</j:number></row></doc>", "1")]
    [InlineData("xml-to-json((//j:map)[1])", "<j:map xmlns:j='" + JsonNamespace + "'><j:map key='nested'><j:null key='z'/></j:map></j:map>", "{\"nested\":{\"z\":null}}")]
    [InlineData("xml-to-json(//j:map)", MapInput, MapOutput)]
    [InlineData("xml-to-json(//element())", "<j:map xmlns:j='" + JsonNamespace + "'/>", "{}")]
    [InlineData("xml-to-json(//*[@key='selected']/*)", "<j:map xmlns:j='" + JsonNamespace + "'><j:array key='selected'><j:map><j:null key='n'/></j:map></j:array></j:map>", "{\"n\":null}")]
    [InlineData("xml-to-json(//*[@key='selected']/*, map{'indent':true()})", "<j:map xmlns:j='" + JsonNamespace + "'><j:array key='selected'><j:map><j:null key='n'/></j:map></j:array></j:map>", "{\n  \"n\": null\n}")]

    [InlineData("xml-to-json(//*[@key='selected']/*)", "<doc xmlns:j='" + JsonNamespace + "'><j:array key='selected'/><j:array key='selected'><j:map/></j:array></doc>", "{}")]
    [InlineData("xml-to-json(//*[@key='absent']/*)", "<doc xmlns:j='" + JsonNamespace + "'><j:array key='selected'><j:map/></j:array></doc>", "")]
    [InlineData("xml-to-json(/doc/descendant::j:array[@key='selected']/*)", "<doc xmlns:j='" + JsonNamespace + "'><j:array key='selected'><j:map/></j:array></doc>", "{}")]
    [InlineData("doc/row ! xml-to-json(descendant::j:array[@key='selected']/*)", "<doc xmlns:j='" + JsonNamespace + "'><row><j:array key='selected'><j:map/></j:array></row><row><j:array key='selected'><j:map/></j:array></row></doc>", "{}|{}")]
    public void ConversionConsumesEachSelectedNodeWhileItIsReadable(string select, string xml, string expected)
    {
        Assert.Equal(expected, Run(select, xml, false));
        Assert.Equal(expected, Run(select, xml, true));
    }

    [Theory]
    [InlineData("xml-to-json(.)", "<wrong/>", "FOJS0006")]
    [InlineData("xml-to-json(/*)", "<j:map xmlns:j='" + JsonNamespace + "'><j:string>missing key</j:string></j:map>", "FOJS0006")]
    [InlineData("xml-to-json(.)", "<j:map xmlns:j='" + JsonNamespace + "'><j:null key='a'/><j:null key='a'/></j:map>", "FOJS0006")]
    [InlineData("xml-to-json(.)", "<j:array xmlns:j='" + JsonNamespace + "'><j:null key='a'/></j:array>", "FOJS0006")]
    [InlineData("xml-to-json(.)", "<j:string xmlns:j='" + JsonNamespace + "'><j:null/></j:string>", "FOJS0006")]
    [InlineData("xml-to-json(.)", "<j:number xmlns:j='" + JsonNamespace + "'>NaN</j:number>", "FOJS0006")]
    [InlineData("xml-to-json(.)", "<j:boolean xmlns:j='" + JsonNamespace + "'>wrong</j:boolean>", "FOJS0006")]
    [InlineData("xml-to-json(.)", "<j:null xmlns:j='" + JsonNamespace + "'> </j:null>", "FOJS0006")]
    [InlineData("xml-to-json(.)", "<j:string xmlns:j='" + JsonNamespace + "' escaped='true'>\\q</j:string>", "FOJS0007")]
    [InlineData("xml-to-json(doc/*)", "<doc><wrong/><other/></doc>", "XPTY0004")]
    [InlineData("xml-to-json((doc/*, 1))", "<doc><wrong/></doc>", "XPTY0004")]
    [InlineData("xml-to-json((), 1)", "<doc/>", "XPTY0004")]
    [InlineData("xml-to-json(doc/*, map{'indent':42})", "<doc><wrong/></doc>", "XPTY0004")]
    [InlineData("xml-to-json((doc/*, error(QName('', 'TEST0001'))))", "<doc><wrong/></doc>", "TEST0001")]
    [InlineData("xml-to-json(doc/*)", "<doc xmlns:j='" + JsonNamespace + "'><j:null/><j:null/></doc>", "XPTY0004")]
    [InlineData("xml-to-json(//j:map)", "<j:map xmlns:j='" + JsonNamespace + "'><j:map key='nested'/></j:map>", "XPTY0004")]
    [InlineData("xml-to-json(//j:map)", "<j:map xmlns:j='" + JsonNamespace + "' wrong='attribute'><j:map key='nested'/></j:map>", "XPTY0004")]
    [InlineData("xml-to-json(//j:number)", "<doc xmlns:j='" + JsonNamespace + "'><j:number>1</j:number><j:number>2</j:number></doc>", "XPTY0004")]
    [InlineData("xml-to-json(//j:map)", "<j:map xmlns:j='" + JsonNamespace + "'><j:number key='n'>wrong</j:number></j:map>", "FOJS0006")]
    [InlineData("xml-to-json(//node())", "<j:map xmlns:j='" + JsonNamespace + "'><j:null key='n'/></j:map>", "XPTY0004")]
    [InlineData("xml-to-json(//element())", "<j:map xmlns:j='" + JsonNamespace + "'><j:null key='n'/></j:map>", "XPTY0004")]
    [InlineData("xml-to-json(//text())", "<doc>one<!--split-->two</doc>", "XPTY0004")]
    [InlineData("xml-to-json(//*[@key='selected']/*)", "<j:map xmlns:j='" + JsonNamespace + "'><j:array key='selected'><j:map><j:null key='n'/></j:map><j:map/><j:map/></j:array></j:map>", "XPTY0004")]
    [InlineData("xml-to-json(//*[@key='selected']/*)", "<j:map xmlns:j='" + JsonNamespace + "'><j:array key='selected'><j:map><j:array key='selected'><j:null/></j:array></j:map></j:array></j:map>", "XPTY0004")]

    [InlineData("xml-to-json(//*[@key='selected'][xs:integer(@id) gt 0]/*)", "<doc xmlns:j='" + JsonNamespace + "'><j:array key='selected' id='bad'/></doc>", "FORG0001")]
    [InlineData("xml-to-json(//*[@key='selected'][xs:integer(@id) gt 0]/*)", "<doc xmlns:j='" + JsonNamespace + "'><j:array key='selected' id='1'><j:map><j:array key='selected' id='bad'><j:map/></j:array></j:map></j:array></doc>", "FORG0001")]
    public void ConversionKeepsCanonicalValidationAndArgumentErrors(string select, string xml, string expected)
    {
        Assert.Equal(expected, Assert.Throws<XdmException>(() => Run(select, xml, false)).Code);
        Assert.Equal(expected, Assert.Throws<XdmException>(() => Run(select, xml, true)).Code);
    }

    [Theory]
    [InlineData("<j:string xmlns:j='" + JsonNamespace + "' escaped='true'>" + "\\q" + "</j:string><j:null xmlns:j='" + JsonNamespace + "'/>")]
    [InlineData("<j:string xmlns:j='" + JsonNamespace + "' escaped='true'>" + "\\q" + "</j:string>outside")]
    [InlineData("<!--no document element-->")]
    public void DocumentStructureErrorsRetainPrecedenceOverConversionErrors(string xml)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal("FOJS0006", Assert.Throws<XdmException>(() => Run("xml-to-json(.)", xml, forward, fragment: true)).Code);
    }

    [Fact]
    public void ASecondSelectedNodePreventsAnyConversionResultFromBeingReturned()
    {
        var transform = new XslCompiledTransform();
        using var stylesheet = XmlReader.Create(new StringReader(Sheet("xml-to-json(doc/*)")));
        transform.Load(stylesheet, new() { StreamedExecution = true }, null);
        using var input = XmlReader.Create(new StringReader("<doc xmlns:j='" + JsonNamespace + "'><j:null/><j:null/></doc>"));
        using var output = new StringWriter();
        Assert.Equal("XPTY0004", Assert.Throws<XdmException>(() => transform.Transform(input, null, output)).Code);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void PerRecordConversionFinishesBeforeTheNextRecordIsRead()
    {
        string stylesheet = Sheet("", "<xsl:for-each select='doc/row'><xsl:message select='xml-to-json(descendant::j:map)'/></xsl:for-each>");
        var transform = new XslCompiledTransform();
        using var source = XmlReader.Create(new StringReader(stylesheet)); transform.Load(source, new() { StreamedExecution = true }, null);
        using var input = XmlReader.Create(new StringReader("<doc><row>" + MapInput + "</row><row>" + MapInput + "</row></doc>"));
        using var output = new StringWriter(); int calls = 0;
        var arguments = new XsltArgumentList();
        arguments.XsltMessageEncountered += (_, message) => {
            Assert.Equal(MapOutput, message.Message);
            if (calls++ == 0) { Assert.Equal("row", input.LocalName); Assert.Equal(XmlNodeType.EndElement, input.NodeType); }
        };
        transform.Transform(input, arguments, output);
        Assert.Equal(2, calls); Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void FunctionItemCallsStillRespectThePrescribedStreamabilityRules()
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XslCompiledTransform();
            using var stylesheet = XmlReader.Create(new StringReader(Sheet("xml-to-json#1(.)")));
            Assert.Contains("XTSE3430", Assert.Throws<XslLoadException>(() => transform.Load(stylesheet, new() { StreamedExecution = forward }, null)).Message);
        }
    }

    [Fact]
    public void CompletedConsumptionObservationsAreNotRetainedByTheirDocument()
    {
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var document = new Xsl3.Streaming.StreamedDocument(input);
        WeakReference observation = CompletedObservation(document);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(observation.IsAlive);
        GC.KeepAlive(document);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CompletedObservation(Xsl3.Streaming.StreamedDocument document)
    {
        using var match = document.MatchConsumption((Xsl3.Streaming.StreamedNavigator)document.CreateNavigator(), new NodeKindTest(null));
        return new WeakReference(match);
    }

    [Fact]
    public void TheExecutableApiConvertsTheStreamedDocument()
    {
        var executable = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(Sheet("xml-to-json(.)")));
        using var input = XmlReader.Create(new StringReader(MapInput));
        Assert.Equal(MapOutput, string.Concat(executable.CreateTransformer().ApplyTemplates(input)));
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

#if NET9_0_OR_GREATER
    [Fact]
    public void ConversionSurvivesSavedCompilation() => Assert.Equal(MapOutput, Run("xml-to-json(/*)", MapInput, true, true));
#endif

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeaderPredicateErrorsKeepArgumentPrecedence(bool nested)
    {
        const string select = "xml-to-json(//*[@key='selected'][trace(true(), 'prefix')]/*)";
        string content = nested
            ? "<j:array key='selected'><j:map><j:array key='selected'><j:map/></j:array></j:map></j:array>"
            : "<j:array key='selected'><j:string escaped='true'>\\q</j:string></j:array><j:array key='selected'/>";
        string xml = "<doc xmlns:j='" + JsonNamespace + "'>" + content + "</doc>";
        foreach (bool forward in new[] { false, true })
        {
            int calls = 0;
            var failure = new XdmException(nested ? "FOJS0006" : "ARG0001", "Header predicate failure");
            var arguments = new XsltArgumentList { TraceHandler = (_, _) => { if (++calls == 2) throw failure; } };
            var transform = new XslCompiledTransform();
            using var stylesheet = XmlReader.Create(new StringReader(Sheet(select)));
            transform.Load(stylesheet, new() { StreamedExecution = forward }, null);
            using var input = XmlReader.Create(new StringReader(xml)); using var output = new StringWriter();
            Assert.Same(failure, Assert.Throws<XdmException>(() => transform.Transform(input, arguments, output)));
            Assert.Empty(output.ToString()); Assert.Equal(2, calls); Assert.NotEqual(ReadState.Closed, input.ReadState);
        }
    }

    [Fact]
    public void HeaderObservationIsRemovedAfterDisposal()
    {
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var document = new Xsl3.Streaming.StreamedDocument(input);
        WeakReference observation = CompletedHeaders(document);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(observation.IsAlive); GC.KeepAlive(document);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CompletedHeaders(Xsl3.Streaming.StreamedDocument document)
    {
        using var observation = document.ObserveConsumedHeaders((Xsl3.Streaming.StreamedNavigator)document.CreateNavigator(), static _ => {});
        return new WeakReference(observation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeaderPathUsesTheExecutableReaderApi(bool multiple)
    {
        string members = "<j:map><j:null key='n'/></j:map>" + (multiple ? "<j:map/>" : "");
        string xml = "<doc xmlns:j='" + JsonNamespace + "'><j:array key='selected'>" + members + "</j:array></doc>";
        var compiler = new XsltCompiler { Settings = new() { StreamedExecution = true } };
        var executable = compiler.Compile(new StringReader(Sheet("xml-to-json(//*[@key='selected']/*)")));
        using var reader = XmlReader.Create(new StringReader(xml)); using var output = new StringWriter();
        if (multiple)
        {
            var error = Assert.Throws<XsltDynamicException>(() => executable.CreateTransformer().ApplyTemplates(reader, output));
            Assert.Equal("XPTY0004", error.Code.LocalName); Assert.Empty(output.ToString());
        }
        else
        {
            executable.CreateTransformer().ApplyTemplates(reader, output); Assert.Equal("{\"n\":null}", output.ToString());
        }
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Fact]
    public void HeaderPathUsesTheGroundedOperandWithoutForwardReaderState()
    {
        string xml = "<doc xmlns:j='" + JsonNamespace + "'><j:array key='selected'><j:map/></j:array></doc>";
        var compiler = new XsltCompiler { Settings = new() { StreamedExecution = true } };
        var executable = compiler.Compile(new StringReader(Sheet("xml-to-json(//*[@key='selected']/*)")));
        Assert.Equal("{}", string.Concat(executable.CreateTransformer().ApplyTemplates(XdmNode.Load(new StringReader(xml)))));
    }

    [Theory]
    [InlineData("xml-to-json(//*[@key='selected']/*[1])")]
    [InlineData("xml-to-json(//*[@key='selected'][if (@bad) then error(QName('', 'ARG0001')) else true()]/*)")]
    public void RoamingInputsRetainStaticRejection(string select)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XslCompiledTransform();
            using var stylesheet = XmlReader.Create(new StringReader(Sheet(select)));
            Assert.Contains("XTSE3430", Assert.Throws<XslLoadException>(() => transform.Load(stylesheet, new() { StreamedExecution = forward }, null)).Message);
        }
    }

    [Fact]
    public void AbsorbedHeadersAndEmptyPrefixesRunPredicatesOnce()
    {
        const string select = "xml-to-json(//*[@key='selected'][trace(true(), 'prefix')]/*)";
        string xml = "<doc xmlns:j='" + JsonNamespace + "'><j:array key='selected'/><j:array key='selected'><j:map><j:null key='n'/></j:map></j:array></doc>";
        foreach (bool forward in new[] { false, true })
        {
            int count = 0;
            var arguments = new XsltArgumentList { TraceHandler = (label, value) => { Assert.Equal("prefix", label); Assert.Equal("true", value); count++; } };
            var transform = new XslCompiledTransform();
            using var stylesheet = XmlReader.Create(new StringReader(Sheet(select)));
            transform.Load(stylesheet, new() { StreamedExecution = forward }, null);
            using var input = XmlReader.Create(new StringReader(xml)); using var output = new StringWriter();
            transform.Transform(input, arguments, output);
            Assert.Equal("{\"n\":null}", output.ToString()); Assert.Equal(2, count);
        }
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FilteredChildContinuationSurvivesSavedCompilation(bool debug)
    {
        string xml = "<doc xmlns:j='" + JsonNamespace + "'><j:array key='selected'><j:map><j:null key='n'/></j:map></j:array></doc>";
        Assert.Equal("{\"n\":null}", Run("xml-to-json(//*[@key='selected']/*)", xml, true, saved: true, debug: debug));
    }
#endif

    private static string Sheet(string select, string? body = null) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:j="{{JsonNamespace}}" xmlns:xs="http://www.w3.org/2001/XMLSchema">
        <xsl:mode streamable="yes"/><xsl:output method="text"/>
        <xsl:template match="/">{{body ?? $"<xsl:value-of select='{SecurityElement.Escape(select)}' separator='|'/>"}}</xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string select, string xml, bool forward, bool saved = false, bool fragment = false, bool debug = false)
    {
        var transform = new XslCompiledTransform(debug);
        using var stylesheet = XmlReader.Create(new StringReader(Sheet(select)));
        var settings = new XsltSettings { StreamedExecution = forward };
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(stylesheet, settings, null, "Json", "Saved.Json", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.Json", true)!);
#endif
        }
        else transform.Load(stylesheet, settings, null);
        using var input = XmlReader.Create(new StringReader(xml), new() { ConformanceLevel = fragment ? ConformanceLevel.Fragment : ConformanceLevel.Document }); using var output = new StringWriter();
        try { transform.Transform(input, null, output); return output.ToString(); }
        finally { Assert.NotEqual(ReadState.Closed, input.ReadState); }
    }
}
