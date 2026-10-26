// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Security;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedTemplateParameterTests
{
    private const string Input = "<doc><row><price>1.25</price></row><row><price>2.75</price></row></doc>";

    private const string DeferredErrorSheet = "<xsl:stylesheet version='4.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:t='http://www.w3.org/2001/XMLSchema'><xsl:output method='text'/>"
            + "<xsl:template match='/'><xsl:call-template name='emit'><xsl:with-param name='value' select='1'/></xsl:call-template></xsl:template>"
            + "<xsl:template name='emit'><xsl:param name='value' required='yes' as='t:decimal' xmlns:t='urn:missing-type'/><xsl:value-of select='$value'/></xsl:template></xsl:stylesheet>";

    private static string Sheet(string type, string argument = "price", string parameterNamespace = "", string content = "", string beforeCall = "", string? argumentType = null, bool argumentContent = false) =>
        "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' exclude-result-prefixes='xs'>"
        + "<xsl:mode streamable='yes'/><xsl:output omit-xml-declaration='yes'/><xsl:template match='/'>"
        + "<out><xsl:for-each select='doc/row'>" + beforeCall + "<xsl:call-template name='emit'>"
        + WithParam(argument, argumentType, argumentContent) + "</xsl:call-template></xsl:for-each></out></xsl:template>"
        + "<xsl:template name='emit'><xsl:context-item use='absent'/><xsl:param name='price' as='" + type + "' " + parameterNamespace + "/>"
        + (content.Length == 0 ? "<price><xsl:value-of select='$price'/></price>" : content) + "</xsl:template></xsl:stylesheet>";

    private static string WithParam(string argument, string? type, bool content)
    {
        string start = "<xsl:with-param name='price'" + (type == null ? "" : " as='" + type + "'");
        return content ? start + "><xsl:sequence select='" + SecurityElement.Escape(argument) + "'/></xsl:with-param>"
            : start + " select='" + SecurityElement.Escape(argument) + "'/>";
    }

    private static string Run(string sheet, string input = Input, bool forward = true, bool legacy = false, bool debug = false)
    {
        var settings = new XsltSettings { StreamedExecution = forward };
        using var reader = XmlReader.Create(new StringReader(input)); using var output = new StringWriter();
        if (legacy)
        {
            var transform = new XslCompiledTransform(enableDebug: debug);
            transform.Load(XmlReader.Create(new StringReader(sheet)), settings, null);
            transform.Transform(reader, null, output);
        }
        else new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(reader, output);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
        return output.ToString();
    }

    [Theory]
    [InlineData("xs:decimal", false)]
    [InlineData("xs:decimal?", false)]
    [InlineData("xs:decimal+", false)]
    [InlineData("xs:anyAtomicType*", false)]
    [InlineData("xs:decimal", true)]
    [InlineData("xs:decimal?", true)]
    [InlineData("xs:decimal+", true)]
    [InlineData("xs:anyAtomicType*", true)]
    public void AtomicTemplateParametersConsumeTheirNodesBeforeTheArgumentIteratorAdvances(string type, bool legacy)
    {
        string sheet = Sheet(type);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><price>1.25</price><price>2.75</price></out>", Run(sheet, forward: forward, legacy: legacy));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DebugCallsKeepTheAtomicAdaptationInsideTheArgumentBinding(bool debug)
    {
        Assert.Equal("<out><price>1.25</price><price>2.75</price></out>", Run(Sheet("xs:decimal"), legacy: true, debug: debug));
    }

    [Theory]
    [InlineData("xs:decimal", "<doc><row><price>bad</price></row></doc>")]
    [InlineData("xs:decimal", "<doc><row><price>1</price><price>2</price></row></doc>")]
    [InlineData("xs:decimal", "<doc><row/></doc>")]
    public void TemplateConversionRetainsTheCanonicalErrorCode(string type, string input)
    {
        string sheet = Sheet(type);
        var ordinary = Assert.Throws<XsltDynamicException>(() => Run(sheet, input, forward: false));
        var streamed = Assert.Throws<XsltDynamicException>(() => Run(sheet, input));
        Assert.Contains("XTTE0590", ordinary.Message); Assert.Contains("XTTE0590", streamed.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParameterTypeNamesUseTheParameterDeclarationNamespaces(bool forward)
    {
        string sheet = Sheet("local:decimal", parameterNamespace: "xmlns:local='http://www.w3.org/2001/XMLSchema'");
        Assert.Equal("<out><price>1.25</price><price>2.75</price></out>", Run(sheet, forward: forward));
    }

    [Theory]
    [InlineData("xs:decimal", "data(price)")]
    [InlineData("element(price)", "copy-of(price)")]
    [InlineData("item()*", "copy-of(price)")]
    public void AtomicAndExplicitlyGroundedArgumentsRetainTheirValues(string type, string argument)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><price>1.25</price><price>2.75</price></out>", Run(Sheet(type, argument), forward: forward));
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PositionOnlyCallersDoNotCountTheForwardPopulation(bool legacy, bool debug)
    {
        string sheet = Sheet("xs:decimal", beforeCall: "<n><xsl:value-of select='position()'/></n>");
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><n>1</n><price>1.25</price><n>2</n><price>2.75</price></out>", Run(sheet, forward: forward, legacy: legacy, debug: debug));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitContextSizeOnGroundedPopulationsRemainsAvailableInDebugCalls(bool debug)
    {
        const string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:mode streamable='yes'/>"
            + "<xsl:output omit-xml-declaration='yes'/><xsl:template match='/'><out><xsl:for-each select='1 to 2'><xsl:call-template name='emit'/></xsl:for-each></out></xsl:template>"
            + "<xsl:template name='emit'><item n='{position()}' last='{last()}'/></xsl:template></xsl:stylesheet>";
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><item n=\"1\" last=\"2\"/><item n=\"2\" last=\"2\"/></out>", Run(sheet, forward: forward, legacy: true, debug: debug));
    }

    [Theory]
    [InlineData("element(price)*", false)]
    [InlineData("node()+", false)]
    [InlineData("element(price)*", true)]
    [InlineData("node()+", true)]
    [InlineData("xs:decimal", false)]
    public void ExplicitWithParamTypesPreserveTheirChecksBeforeTheCalleeConversion(string type, bool content)
    {
        string sheet = Sheet("xs:decimal", argumentType: type, argumentContent: content);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><price>1.25</price><price>2.75</price></out>", Run(sheet, forward: forward));
    }

    [Theory]
    [InlineData("xs:string")]
    [InlineData("element(other)")]
    public void AWithParamTypeCannotBeBypassedByTheCalleeAtomicConversion(string type)
    {
        string sheet = Sheet("xs:decimal", argumentType: type);
        foreach (bool forward in new[] { false, true })
            Assert.Contains("XTTE0590", Assert.Throws<XsltDynamicException>(() => Run(sheet, forward: forward)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DebugNodeHooksAcceptAtomicMapArrayFunctionAndNodeContextItems(bool debug)
    {
        const string values = "(1, map{'x':2}, [3], function(){4}, parse-xml('<n/>')/*)";
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema'><xsl:mode streamable='yes'/><xsl:output method='text'/>"
            + "<xsl:template match='/'><xsl:for-each select='" + SecurityElement.Escape(values) + "'><xsl:value-of select=\"if (. instance of xs:integer) then 'integer' else if (. instance of map(*)) then 'map' else if (. instance of array(*)) then 'array' else if (. instance of function(*)) then 'function' else 'node'\"/><xsl:text>|</xsl:text></xsl:for-each></xsl:template></xsl:stylesheet>";
        foreach (bool forward in new[] { false, true })
            Assert.Equal("integer|map|array|function|node|", Run(sheet, forward: forward, legacy: true, debug: debug));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardCompatibleTypeErrorsRetainTheDeclarationNamespace(bool legacy)
    {
        Exception? error = Record.Exception(() => Run(DeferredErrorSheet, legacy: legacy));
        Assert.True(error is XsltDynamicException or Xsl3.Xdm.XdmException);
        Assert.Contains("XPST0051", error!.Message);
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData("xs:decimal", null, false, false)]
    [InlineData("xs:decimal+", "node()+", false, false)]
    [InlineData("xs:decimal", "element(price)*", true, false)]
    [InlineData("local:decimal", null, false, false)]
    [InlineData("xs:decimal", null, false, true)]
    [InlineData("local:decimal", "node()+", true, true)]
    public void SavedAssembliesKeepDeclarationTypesAndIncrementalArgumentChecks(string type, string? argumentType, bool content, bool debug)
    {
        var settings = new XsltSettings { StreamedExecution = true };
        string sheet = Sheet(type, parameterNamespace: "xmlns:local='http://www.w3.org/2001/XMLSchema'", argumentType: argumentType, argumentContent: content);
        using var assembly = new MemoryStream(); using var stylesheet = XmlReader.Create(new StringReader(sheet));
        new XslCompiledTransform(enableDebug: debug).CompileToAssembly(stylesheet, settings, null, "Xsl3.Tests.TemplateParameters", "Saved.TemplateParameters", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.TemplateParameters", true)!, settings);
        using var reader = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter();
        transform.Transform(reader, null, output);
        Assert.Equal("<out><price>1.25</price><price>2.75</price></out>", output.ToString());
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }
    [Fact]
    public void SavedAssembliesRetainDeferredDeclarationErrors()
    {
        using var assembly = new MemoryStream(); using var stylesheet = XmlReader.Create(new StringReader(DeferredErrorSheet));
        new XslCompiledTransform().CompileToAssembly(stylesheet, new(), null, "Xsl3.Tests.DeferredType", "Saved.DeferredType", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.DeferredType", true)!, new());
        using var reader = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter();
        Exception? error = Record.Exception(() => transform.Transform(reader, null, output));
        Assert.True(error is XsltDynamicException or Xsl3.Xdm.XdmException);
        Assert.Contains("XPST0051", error!.Message);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }
#endif

}
