// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;
public class XmlToJsonHeaderRuntimeTests
{
    private static XsltLibrary Runtime()
    {
        var constants = new ConstantTable();
        Assert.Equal(0, constants.Add(new StaticInfo(null, Collation.CodepointUri, DecimalFormatTable.Empty)));
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true, Constants = constants };
        return new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter()).XsltFunctions;
    }


    [Fact]
    public void ConsumedSubtreesCannotBecomeEmptyArguments()
    {
        var library = Runtime();
        using var input = XmlReader.Create(new StringReader("<doc><row/></doc>"));
        using var document = new StreamedDocument(input);
        var root = (StreamedNavigator)document.CreateNavigator();
        _ = root.Value;
        var state = library.X31XmlToJsonNew(0, Array.Empty<XPathItem>(), false);
        var selector = new NativeFunction(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence,
            _ => new XPathItem[] { AtomicValue.OfBoolean(false) }, "test header predicate");
        var error = Assert.Throws<XdmException>(() => library.X31XmlToJsonHeaders(state, root, selector));
        Assert.Equal("stream-position", error.Code);
        Assert.Same(error, Assert.Throws<XdmException>(() => library.X31XmlToJsonEnd(state)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedReplayOrAttributeHeadersKeepTheirActualEmptySelection(bool attribute)
    {
        var library = Runtime();
        using var input = XmlReader.Create(new StringReader("<doc id='a'><row/></doc>"));
        using var document = new StreamedDocument(input);
        var root = (StreamedNavigator)document.CreateNavigator();
        if (attribute)
        {
            Assert.True(root.MoveToFirstChild());
            var parent = root.Clone();
            Assert.True(root.MoveToFirstAttribute());
            _ = parent.Value;
        }
        else
        {
            root.BufferSubtree();
            _ = root.Value;
        }
        var state = library.X31XmlToJsonNew(0, Array.Empty<XPathItem>(), false);
        var selector = new NativeFunction(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence,
            _ => new XPathItem[] { AtomicValue.OfBoolean(false) }, "test header predicate");
        library.X31XmlToJsonHeaders(state, root, selector);
        Assert.Empty(library.X31XmlToJsonEnd(state));
    }

}
