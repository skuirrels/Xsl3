// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xunit;
using Stock = System.Xml.Xsl;

namespace Xsl3.Tests;

/// <summary>
/// Pins every bridge to System.Private.Xml internals (src/Xsl3/Bridges, docs/internals.md) against the
/// runtime the tests run on. [UnsafeAccessor] and reflection bind at run time, so a servicing update that
/// renames or trims one of those members would otherwise surface only when a stylesheet first needs it.
/// </summary>
/// <remarks>
/// The bridges are C# 14 extension members. The 10.0.1xx compiler rejects extension members imported from
/// another assembly through InternalsVisibleTo (CS0570), so these tests call the static implementation
/// methods behind them (<c>get_X</c>/<c>set_X</c> for properties) directly.
/// </remarks>
public class BridgeTests
{
    [Fact]
    public void EngineIsXsl3()
    {
        Assert.Equal("Xsl3", typeof(XslCompiledTransform).Assembly.GetName().Name);
    }

    [Fact]
    public void ReaderResolverBridgePreservesExplicitPolicy()
    {
        var resolver = new XmlUrlResolver();
        var settings = new XmlReaderSettings { XmlResolver = resolver };
        Assert.Same(resolver, XmlReaderSettingsInternals.GetEffectiveResolver(settings));
        settings.XmlResolver = null;
        Assert.Null(XmlReaderSettingsInternals.GetEffectiveResolver(settings));
    }

    [Fact]
    public void XmlWriterSettingsInternalsRoundTrip()
    {
        var settings = new XmlWriterSettings();
        XmlWriterSettingsInternals.set_DocTypeSystem(settings, "sys");
        XmlWriterSettingsInternals.set_DocTypePublic(settings, "pub");
        XmlWriterSettingsInternals.set_MediaType(settings, "text/x-test");
        XmlWriterSettingsInternals.set_MergeCDataSections(settings, true);
        XmlWriterSettingsInternals.set_AutoXmlDeclaration(settings, true);
        XmlWriterSettingsInternals.set_Standalone(settings, XmlStandalone.Yes);
        XmlWriterSettingsInternals.set_IndentInternal(settings, TriState.True);
        XmlWriterSettingsInternals.get_CDataSectionElements(settings).Add(new XmlQualifiedName("code", "urn:x"));
        XmlWriterSettingsInternals.SetOutputMethod(settings, XmlOutputMethod.Html);

        Assert.Equal("sys", XmlWriterSettingsInternals.get_DocTypeSystem(settings));
        Assert.Equal("pub", XmlWriterSettingsInternals.get_DocTypePublic(settings));
        Assert.Equal("text/x-test", XmlWriterSettingsInternals.get_MediaType(settings));
        Assert.True(XmlWriterSettingsInternals.get_MergeCDataSections(settings));
        Assert.True(XmlWriterSettingsInternals.get_AutoXmlDeclaration(settings));
        Assert.Equal(XmlStandalone.Yes, XmlWriterSettingsInternals.get_Standalone(settings));
        Assert.Equal(TriState.True, XmlWriterSettingsInternals.get_IndentInternal(settings));
        Assert.True(settings.Indent); // the public view of IndentInternal
        Assert.Equal(XmlOutputMethod.Html, settings.OutputMethod);
        Assert.True(XmlWriterSettingsInternals.get_IsQuerySpecific(settings));

        // Static-data serialization (Load(Type) of a compiled stylesheet) round-trips every field.
        var ms = new MemoryStream();
        using (var writer = new XmlQueryDataWriter(ms))
        {
            XmlWriterSettingsInternals.GetObjectData(settings, writer);
        }
        XmlWriterSettings copy = XmlWriterSettingsInternals.Create(new XmlQueryDataReader(new MemoryStream(ms.ToArray())));
        Assert.Equal(XmlWriterSettingsInternals.get_DocTypeSystem(settings), XmlWriterSettingsInternals.get_DocTypeSystem(copy));
        Assert.Equal(XmlWriterSettingsInternals.get_DocTypePublic(settings), XmlWriterSettingsInternals.get_DocTypePublic(copy));
        Assert.Equal(XmlWriterSettingsInternals.get_MediaType(settings), XmlWriterSettingsInternals.get_MediaType(copy));
        Assert.Equal(XmlWriterSettingsInternals.get_Standalone(settings), XmlWriterSettingsInternals.get_Standalone(copy));
        Assert.Equal(XmlWriterSettingsInternals.get_IndentInternal(settings), XmlWriterSettingsInternals.get_IndentInternal(copy));
        Assert.Equal(settings.OutputMethod, copy.OutputMethod);
        Assert.Equal(XmlWriterSettingsInternals.get_CDataSectionElements(settings), XmlWriterSettingsInternals.get_CDataSectionElements(copy));

        XmlWriterSettingsInternals.set_ReadOnly(settings, true);
        Assert.True(XmlWriterSettingsInternals.get_ReadOnly(settings));
        Assert.Throws<XmlException>(() => settings.Indent = false);
    }

    [Fact]
    public void XsltOutputSettingsDriveStockXmlWriter()
    {
        // OutputSettings must stay a real XmlWriterSettings that stock XmlWriter.Create honours.
        var xslt = new XslCompiledTransform();
        xslt.Load(XmlReader.Create(new StringReader(
            """<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"><xsl:output method="xml" doctype-system="a.dtd" omit-xml-declaration="yes"/><xsl:template match="/"><r/></xsl:template></xsl:stylesheet>""")));
        var sw = new StringWriter();
        using (XmlWriter w = XmlWriter.Create(sw, xslt.OutputSettings))
        {
            xslt.Transform(new XPathDocument(new StringReader("<x/>")), null, w);
        }
        Assert.Equal("<!DOCTYPE r SYSTEM \"a.dtd\"><r />", sw.ToString());
    }

    [Fact]
    public void UniqueIdMatchesStockForForeignNavigators()
    {
        var nav = new XPathDocument(new StringReader("<a><b/><c x='1'/></a>")).CreateNavigator();
        PropertyInfo stockUniqueId = typeof(XPathNavigator).GetProperty("UniqueId", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (XPathNavigator node in nav.Select("//node() | //@*").Cast<XPathNavigator>())
        {
            Assert.Equal((string)stockUniqueId.GetValue(node)!, XPathNavigatorInternals.get_UniqueId(node));
        }
    }

    [Fact]
    public void NamespaceManagerDeclarations()
    {
        var nsmgr = new XmlNamespaceManager(new NameTable());
        nsmgr.PushScope();
        nsmgr.AddNamespace("p", "urn:p");
        Assert.True(XmlNamespaceManagerInternals.GetNamespaceDeclaration(nsmgr, 0, out string? prefix, out string? uri));
        Assert.Equal("p", prefix);
        Assert.Equal("urn:p", uri);
    }

    [Fact]
    public void AtomicValueConstructors()
    {
        XmlSchemaType type(XmlTypeCode c) => XmlSchemaType.GetBuiltInSimpleType(c);
        Assert.True(XmlAtomicValueInternals.Create(type(XmlTypeCode.Boolean), true).ValueAsBoolean);
        Assert.Equal(1.5, XmlAtomicValueInternals.Create(type(XmlTypeCode.Double), 1.5).ValueAsDouble);
        Assert.Equal(3, XmlAtomicValueInternals.Create(type(XmlTypeCode.Int), 3).ValueAsInt);
        Assert.Equal(4L, XmlAtomicValueInternals.Create(type(XmlTypeCode.Long), 4L).ValueAsLong);
        Assert.Equal("s", XmlAtomicValueInternals.Create(type(XmlTypeCode.String), "s").Value);
        Assert.Equal(new DateTime(2026, 1, 2), XmlAtomicValueInternals.Create(type(XmlTypeCode.DateTime), new DateTime(2026, 1, 2)).ValueAsDateTime);
        Assert.Equal("7", XmlAtomicValueInternals.Create(type(XmlTypeCode.Integer), (object)7m).Value);
        var resolver = new XmlNamespaceManager(new NameTable());
        resolver.AddNamespace("p", "urn:p");
        Assert.Equal("p:n", XmlAtomicValueInternals.Create(type(XmlTypeCode.QName), "p:n", resolver).Value);
        Assert.Equal("p:n", XmlAtomicValueInternals.Create(type(XmlTypeCode.QName), (object)new XmlQualifiedName("n", "urn:p"), resolver).Value);
    }

    [Fact]
    public void ExceptionConstructorsFormatMessages()
    {
        Assert.Equal(string.Format(SR.Xml_InvalidCharacter, "a", "b"), XmlExceptionInternals.Create(SR.Xml_InvalidCharacter, new[] { "a", "b" }).Message);
        Assert.StartsWith(string.Format(SR.Xml_UserException, "x"), XmlExceptionInternals.Create(SR.Xml_UserException, "x", 1, 2).Message);
        Assert.Equal(2, XmlExceptionInternals.Create(SR.Xml_UserException, "x", 1, 2).LinePosition);
        Assert.NotEmpty(XmlExceptionInternals.BuildCharExceptionArgs("a\u0001", 1));
        Assert.NotEmpty(XmlExceptionInternals.BuildCharExceptionArgs(new[] { 'a', '\u0001' }, 2, 1));
        Assert.NotEmpty(XmlExceptionInternals.BuildCharExceptionArgs('\u0001', 'a'));
    }

    [Fact]
    public void QualifiedNameInit()
    {
        var qname = new XmlQualifiedName("a", "urn:a");
        XmlQualifiedNameInternals.Init(qname, "b", "urn:b");
        Assert.Equal(new XmlQualifiedName("b", "urn:b"), qname);
        Assert.Equal(new XmlQualifiedName("b", "urn:b").GetHashCode(), qname.GetHashCode());
    }

    [Fact]
    public void DefaultPermissiveResolver()
    {
        Assert.NotNull(XmlReaderSettingsInternals.GetDefaultPermissiveResolver());
    }

    [Fact]
    public void BuiltInSchemaTypesAreTheStockSingletons()
    {
        Type datatypes = typeof(XmlSchemaType).Assembly.GetType("System.Xml.Schema.DatatypeImplementation")!;
        object? Stock(Type t, string name) => t.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);

        Assert.Same(Stock(typeof(XmlSchemaComplexType), "AnyType"), XmlSchemaComplexTypeInternals.get_AnyType());
        Assert.Same(Stock(typeof(XmlSchemaComplexType), "UntypedAnyType"), XmlSchemaComplexTypeInternals.get_UntypedAnyType());
        Assert.Same(Stock(datatypes, "AnySimpleType"), DatatypeImplementation.AnySimpleType);
        Assert.Same(Stock(datatypes, "AnyAtomicType"), DatatypeImplementation.AnyAtomicType);
        Assert.Same(Stock(datatypes, "UntypedAtomicType"), DatatypeImplementation.UntypedAtomicType);
    }

    [Fact]
    public void UntypedConverterMatchesStock()
    {
        var resolver = new XmlNamespaceManager(new NameTable());
        resolver.AddNamespace("p", "urn:p");
        Assert.Equal("1.5", XmlUntypedConverter.Untyped.ToString(1.5, null));
        Assert.Equal("true", XmlUntypedConverter.Untyped.ToString(true, null));
        Assert.Equal("p:n", XmlUntypedConverter.Untyped.ToString(new XmlQualifiedName("n", "urn:p"), resolver));
    }

    [Fact]
    public void ReaderCapturePreservesResolverAndWhitespaceContracts() => Assert.Equal(9, ReaderBridgeContracts.Verify());

    [Fact]
    public void ReaderInternals()
    {
        using var created = XmlReader.Create(new StringReader("<a/>"), new XmlReaderSettings { IgnoreWhitespace = true });
        Assert.True(XmlReaderInternals.TryGetTextReaderImplResolver(created, out _));
        // XmlReader.Create filters whitespace itself; WhitespaceHandling.None is only set through XmlTextReader.
        Assert.False(XmlReaderInternals.IsTextReaderImplStrippingAllWhitespace(created));
        using var stripping = new XmlTextReader(new StringReader("<a/>")) { WhitespaceHandling = WhitespaceHandling.None };
        var impl = (XmlReader)typeof(XmlTextReader).GetProperty("Impl", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(stripping)!;
        Assert.True(XmlReaderInternals.IsTextReaderImplStrippingAllWhitespace(impl));

        var resolver = new XmlUrlResolver();
        using var textReader = new XmlTextReader(new StringReader("<a/>")) { XmlResolver = resolver };
        Assert.Same(resolver, XmlReaderInternals.GetResolver(textReader));
    }

    /// <summary>The DTD's attribute types (for fn:idref) are reached through System.Private.Xml internals that bind here.</summary>
    [Fact]
    public void DtdAttributeTypesBind() => Assert.True(System.Xml.DtdInfoBridge.IdrefBridgeBinds);

    /// <summary>
    /// DTD-declared IDs reach the vendored XPathDocument through DtdInfoBridge. xsl:strip-space makes the
    /// engine wrap the input reader, which is the case where the stock internal property can't be forwarded.
    /// (Stock registers IDs only for declared elements: an ATTLIST without an ELEMENT declaration is ignored.)
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdFunctionSeesDtdIds(bool stripSpace)
    {
        string xsl = $"""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              {(stripSpace ? "<xsl:strip-space elements='*'/>" : "")}
              <xsl:output omit-xml-declaration="yes"/>
              <xsl:template match="/"><r><xsl:value-of select="id('b')/@n"/>|<xsl:value-of select="count(id('a c'))"/></r></xsl:template>
            </xsl:stylesheet>
            """;
        string xml = """
            <!DOCTYPE doc [
              <!ELEMENT doc (item*)>
              <!ELEMENT item EMPTY>
              <!ATTLIST item key ID #IMPLIED n CDATA #IMPLIED>
            ]>
            <doc>
              <item key="a" n="1"/>
              <item key="b" n="2"/>
              <item key="c" n="3"/>
            </doc>
            """;
        var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse };

        var stock = new Stock.XslCompiledTransform();
        stock.Load(XmlReader.Create(new StringReader(xsl)));
        var expected = new StringWriter();
        stock.Transform(XmlReader.Create(new StringReader(xml), readerSettings), null, expected);

        var xsl3 = new XslCompiledTransform();
        xsl3.Load(XmlReader.Create(new StringReader(xsl)));
        var actual = new StringWriter();
        xsl3.Transform(XmlReader.Create(new StringReader(xml), readerSettings), null, actual);

        Assert.Equal("<r>2|2</r>", expected.ToString());
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Theory]
    [InlineData("xml", "utf-8")]
    [InlineData("xml", "utf-16")]
    [InlineData("html", "utf-8")]
    [InlineData("html", "iso-8859-1")]
    [InlineData("text", "utf-8")]
    public void StreamOutputMatchesStockByteForByte(string method, string encoding)
    {
        string xsl = $$"""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="{{method}}" encoding="{{encoding}}" indent="yes" cdata-section-elements="code" doctype-public="-//X//Y" doctype-system="x.dtd"/>
              <xsl:template match="/"><html><head/><body><p a="&lt;&amp;&#233;">x &lt; y &#233; &#x1F600;</p><code>a &lt; b</code><br/><script>if (a &lt; b) {}</script></body></html></xsl:template>
            </xsl:stylesheet>
            """;
        var input = new XPathDocument(new StringReader("<x/>"));

        var stock = new Stock.XslCompiledTransform();
        stock.Load(XmlReader.Create(new StringReader(xsl)));
        var expected = new MemoryStream();
        stock.Transform(input, null, expected);

        var xsl3 = new XslCompiledTransform();
        xsl3.Load(XmlReader.Create(new StringReader(xsl)));
        var actual = new MemoryStream();
        xsl3.Transform(input, null, actual);

        Assert.Equal(Convert.ToHexString(expected.ToArray()), Convert.ToHexString(actual.ToArray()));
        Assert.NotEmpty(Encoding.UTF8.GetString(actual.ToArray()));
    }
}
