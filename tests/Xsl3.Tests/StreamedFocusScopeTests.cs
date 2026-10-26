// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Linq;
using System.Security;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedFocusScopeTests
{
    private const string Input = "<doc><row><price>1.25</price></row><row><price>2.75</price></row></doc>";
    private static string Sheet(string expression = "price", string beforeCall = "", string declarations = "", bool expandText = false) =>
        "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:fn='http://www.w3.org/2005/xpath-functions' exclude-result-prefixes='xs fn' expand-text='" + (expandText ? "yes" : "no") + "'>"
        + "<xsl:mode streamable='yes'/><xsl:output omit-xml-declaration='yes'/><xsl:template match='/'>"
        + "<out><xsl:for-each select='doc/row'>" + beforeCall + "<xsl:call-template name='emit'><xsl:with-param name='price' select='"
        + SecurityElement.Escape(expression) + "'/></xsl:call-template></xsl:for-each></out></xsl:template>"
        + "<xsl:template name='emit'><xsl:context-item use='absent'/><xsl:param name='price' as='xs:double'/><price><xsl:value-of select='$price'/></price></xsl:template>"
        + declarations + "</xsl:stylesheet>";

    private static string Run(string sheet, bool forward, bool debug = false, string input = Input)
    {
        var transform = new XslCompiledTransform(enableDebug: debug);
        transform.Load(XmlReader.Create(new StringReader(sheet)), new XsltSettings { StreamedExecution = forward }, null);
        using var reader = XmlReader.Create(new StringReader(input)); using var output = new StringWriter();
        transform.Transform(reader, null, output);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
        return output.ToString();
    }

    [Theory]
    [InlineData("price + (1,2)[last()]", "3.25", "4.75")]
    [InlineData("price + ((1,2) ! last())[1]", "3.25", "4.75")]
    [InlineData("price + count((1,2)[position() = last()])", "2.25", "3.75")]
    [InlineData("price + (1,2)[fn:last()]", "3.25", "4.75")]
    [InlineData("price + (1,2)[Q{http://www.w3.org/2005/xpath-functions}last()]", "3.25", "4.75")]
    [InlineData("price + (1,2)[let $f := last#0 return $f()]", "3.25", "4.75")]
    [InlineData("price + (1,2)[let $f := function-lookup(QName('http://www.w3.org/2005/xpath-functions','last'),0) return $f()]", "3.25", "4.75")]
    [InlineData("price + ([(1,2)[last()]]?1)", "3.25", "4.75")]
    [InlineData("price + (map{1:(1,2)[last()]}?1)", "3.25", "4.75")]
    [InlineData("let $f := function(){(1,2)[last()]} return price + $f()", "3.25", "4.75")]
    public void LocallyBoundXPathFocusDoesNotCountTheCaller(string expression, string first, string second)
    {
        string sheet = Sheet(expression);
        foreach (bool forward in new[] { false, true })
            foreach (bool debug in new[] { false, true })
                Assert.Equal("<out><price>" + first + "</price><price>" + second + "</price></out>", Run(sheet, forward, debug));
    }

    [Theory]
    [InlineData("<xsl:for-each select='1 to 2'><xsl:value-of select='last()'/></xsl:for-each>", "22")]
    [InlineData("<n value='{(1,2)[last()]}'/>", "<n value=\"2\"/>")]
    [InlineData("last()", "last()")]
    [InlineData("<xsl:text>function-lookup</xsl:text>", "function-lookup")]
    public void LocalInstructionFocusAndLiteralTextDoNotCountTheCaller(string prefix, string output)
    {
        string sheet = Sheet("price + 0", beforeCall: prefix);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out>" + output + "<price>1.25</price>" + output + "<price>2.75</price></out>", Run(sheet, forward));
    }

    [Fact]
    public void TextValueTemplatesKeepTheirOwnXPathFocus()
    {
        string sheet = Sheet("price + 0", beforeCall: "{(1,2)[last()]}", expandText: true);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out>2<price>1.25</price>2<price>2.75</price></out>", Run(sheet, forward));
    }

    [Fact]
    public void AnUncalledTemplateCannotAddAContextSizeArgumentToOtherCalls()
    {
        string sheet = Sheet("price + 0", declarations: "<xsl:template name='unused' visibility='public'><xsl:value-of select='last()'/></xsl:template>");
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><price>1.25</price><price>2.75</price></out>", Run(sheet, forward));
    }

    [Theory]
    [InlineData("price + 0", "1.25", "2.75")]
    [InlineData("-price", "-1.25", "-2.75")]
    [InlineData("2 * price", "2.5", "5.5")]
    public void ArithmeticAbsorbsNodesWhileTheyAreLive(string expression, string first, string second)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><price>" + first + "</price><price>" + second + "</price></out>", Run(Sheet(expression), forward));
    }

    [Theory]
    [InlineData("(price,map{}) + 0", "1.25|2.75|")]
    [InlineData("-(price,map{})", "-1.25|-2.75|")]
    [InlineData("(price, map{}) * 2", "2.5|5.5|")]
    [InlineData("() + 0", "NaN|NaN|")]
    public void CompatibilityArithmeticConvertsOnlyTheFirstItemInANonStreamableMode(string expression, string expected)
    {
        // Compatibility arithmetic still needs coverage with either execution setting, but §3.9.1
        // makes these instructions ineligible for a guaranteed-streamable mode.
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:mode streamable='no'/><xsl:output method='text'/>"
            + "<xsl:template match='/'><xsl:for-each select='doc/row' version='1.0'><xsl:value-of select='" + SecurityElement.Escape(expression)
            + "'/><xsl:text>|</xsl:text></xsl:for-each></xsl:template></xsl:stylesheet>";
        foreach (bool forward in new[] { false, true }) Assert.Equal(expected, Run(sheet, forward));
    }

    [Theory]
    [InlineData("last()")]
    [InlineData("[last()]?1")]
    [InlineData("map{1:last()}?1")]
    [InlineData("let $f := last#0 return $f()")]
    [InlineData("let $f := function-lookup#2 return $f(QName('http://www.w3.org/2005/xpath-functions','last'),0)()")]
    public void IncomingFocusInsideXPathConstructorsAndFunctionReferencesRemainsAvailable(string size)
    {
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:mode streamable='yes'/><xsl:output method='text'/>"
            + "<xsl:template match='/'><xsl:for-each select='1 to 3'><xsl:call-template name='end'/></xsl:for-each></xsl:template>"
            + "<xsl:template name='end'><xsl:value-of select='" + SecurityElement.Escape(size) + "'/></xsl:template></xsl:stylesheet>";
        foreach (bool forward in new[] { false, true })
            foreach (bool debug in new[] { false, true }) Assert.Equal("333", Run(sheet, forward, debug));
    }

    [Theory]
    [InlineData("m")]
    [InlineData("#all")]
    [InlineData("m other")]
    public void ModeDispatchPreservesItsTemplatesNumericFocus(string modes)
    {
        string activation = modes == "#all"
            ? "<xsl:template name='stream-source' visibility='public'><xsl:source-document streamable='yes' href='file:///unused.xml'/></xsl:template>"
            : "<xsl:mode streamable='yes'/>";
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>" + activation + "<xsl:output method='text'/>"
            + "<xsl:template match='/' priority='10'><xsl:apply-templates select='1 to 3' mode='m'/></xsl:template>"
            + "<xsl:template match='.[. instance of xs:integer]' xmlns:xs='http://www.w3.org/2001/XMLSchema' mode='" + modes + "'><xsl:call-template name='end'/></xsl:template>"
            + "<xsl:template name='end'><xsl:value-of select='position()'/><xsl:value-of select='last()'/></xsl:template></xsl:stylesheet>";
        foreach (bool forward in new[] { false, true })
            foreach (bool debug in new[] { false, true }) Assert.Equal("132333", Run(sheet, forward, debug));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModernApiKeepsLocalFocusAndArithmetic(bool forward)
    {
        using var reader = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter();
        new XsltCompiler { Settings = new XsltSettings { StreamedExecution = forward } }
            .Compile(new StringReader(Sheet("price + (1,2)[last()]"))).CreateTransformer().ApplyTemplates(reader, output);
        Assert.Equal("<out><price>3.25</price><price>4.75</price></out>", output.ToString());
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Theory]
    [InlineData("(1 to 3) ! ([last()]?1)", "3 3 3")]
    [InlineData("(1 to 3) ! (map{1:last()}?1)", "3 3 3")]
    [InlineData("(1 to 3) ! (map{last():7}?3)", "7 7 7")]
    [InlineData("(1 to 3) ! (let $f := function-lookup#2 return $f(QName('http://www.w3.org/2005/xpath-functions','last'),0)())", "3 3 3")]
    [InlineData("exists(last#0)", "true")]
    [InlineData("exists(position#0)", "true")]
    [InlineData("exists(function-lookup#2)", "true")]
    [InlineData("(function(){exists(last#0)})()", "true")]
    public void XPathScopesKeepRequiredContextSizeAndAllowAbsentFocusReferences(string expression, string expected)
    {
        var query = Xsl3.XPath31.XPath31Compiler.Compile(expression, new Xsl3.XPath31.XPathStaticContext());
        Assert.Equal(expected, string.Join(" ", query.Evaluate().Select(item => item.Value)));
    }

    [Theory]
    [InlineData("last#0()")]
    [InlineData("position#0()")]
    [InlineData("let $f := function-lookup#2 return $f(QName('http://www.w3.org/2005/xpath-functions','last'),0)()")]
    public void CallingANumericReferenceWithAbsentCapturedFocusRetainsItsError(string expression)
    {
        var query = Xsl3.XPath31.XPath31Compiler.Compile(expression, new Xsl3.XPath31.XPathStaticContext());
        var error = Assert.Throws<Xsl3.Xdm.XdmException>(() => query.Evaluate());
        Assert.Contains("XPDY0002", error.Message);
    }

    [Theory]
    [InlineData("exists((1,2) ! last())")]
    [InlineData("exists((1,2) ! position())")]
    [InlineData("exists((1,2)[last()])")]
    public void ScanningPredicatesMayUseGroundedLocalNumericFocus(string predicate)
    {
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:mode streamable='yes'/><xsl:output method='text'/>"
            + "<xsl:template match='/'><xsl:value-of select='count(descendant::row/price[" + predicate + "])'/></xsl:template></xsl:stylesheet>";
        foreach (bool forward in new[] { false, true })
        {
            Assert.Equal("2", Run(sheet, forward));
            Assert.Equal("3", Run(sheet, forward, input: "<doc><row><price>1</price><row><price>2</price></row><price>3</price></row></doc>"));
        }
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedAssembliesKeepScopeAnalysisAndAtomicOperands(bool debug)
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream(); using var stylesheet = XmlReader.Create(new StringReader(Sheet("price + (1,2)[last()]")));
        new XslCompiledTransform(enableDebug: debug).CompileToAssembly(stylesheet, settings, null, "Xsl3.Tests.FocusScope", "Saved.FocusScope", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.FocusScope", true)!, settings);
        using var reader = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter();
        transform.Transform(reader, null, output);
        Assert.Equal("<out><price>3.25</price><price>4.75</price></out>", output.ToString());
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }
#endif

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransitiveCallsStillReceiveRealPositionAndContextSize(bool debug)
    {
        const string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:mode streamable='yes'/><xsl:output omit-xml-declaration='yes'/>"
            + "<xsl:template match='/'><out><xsl:for-each select='1 to 3'><xsl:call-template name='middle'/></xsl:for-each></out></xsl:template>"
            + "<xsl:template name='middle'><xsl:call-template name='end'/></xsl:template><xsl:template name='end'><item n='{position()}' last='{last()}'/></xsl:template></xsl:stylesheet>";
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><item n=\"1\" last=\"3\"/><item n=\"2\" last=\"3\"/><item n=\"3\" last=\"3\"/></out>", Run(sheet, forward, debug));
    }
}
