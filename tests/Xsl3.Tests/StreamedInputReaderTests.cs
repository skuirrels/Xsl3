// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedInputReaderTests
{
    [Theory]
    [InlineData("<first/>")]
    [InlineData("<first></first>")]
    public void AReaderFailureAfterAnElementEndDoesNotPopItsParentOnRetry(string first)
    {
        using var reader = new OnceFailingReader(XmlReader.Create(new StringReader(
            $"<doc><group>{first}<second/></group><outside/></doc>")));
        using var document = new StreamedDocument(reader);
        var nav = document.CreateNavigator();
        Assert.True(nav.MoveToFirstChild()); // doc
        Assert.True(nav.MoveToFirstChild()); // group
        Assert.True(nav.MoveToFirstChild()); // first
        Assert.False(nav.MoveToFirstChild()); // finish an explicitly closed empty element
        reader.FailNextRead = true;
        Assert.Throws<IOException>(() => nav.MoveToNext());
        Assert.Equal("first", nav.LocalName);
        Assert.True(nav.MoveToNext());
        Assert.Equal("second", nav.LocalName);
        Assert.False(nav.MoveToNext());
        Assert.True(nav.MoveToParent());
        Assert.Equal("group", nav.LocalName);
        Assert.True(nav.MoveToNext());
        Assert.Equal("outside", nav.LocalName);
        Assert.False(nav.MoveToNext());
    }

    [Fact]
    public void SkippingPreservesDtdWhitespaceRulesForTheFollowingSibling()
    {
        const string xml = "<!DOCTYPE doc [<!ELEMENT doc (skip,keep)><!ELEMENT skip ANY><!ELEMENT inside (#PCDATA)><!ELEMENT keep (#PCDATA)>]>"
            + "<doc><skip><inside>discard</inside></skip> \n <keep>retain</keep></doc>";
        using var input = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse });
        using var reader = new StreamedInputReader(input, false, default);
        Assert.True(reader.Read()); // DTD
        Assert.True(reader.Read()); // doc
        Assert.True(reader.Read()); // skip
        Assert.Equal("skip", reader.LocalName);
        reader.Skip();
        Assert.Equal(XmlNodeType.Element, reader.NodeType);
        Assert.Equal("keep", reader.LocalName);
        Assert.Equal("retain", reader.ReadElementContentAsString());
    }

    [Fact]
    public void SkippingStillValidatesEveryDiscardedNode()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader("""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="doc"><xs:complexType><xs:sequence>
                <xs:element name="skip"><xs:complexType><xs:sequence><xs:element name="inside" type="xs:integer"/></xs:sequence></xs:complexType></xs:element>
              </xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """)));
        using var input = XmlReader.Create(new StringReader("<doc><skip><inside>invalid</inside></skip></doc>"),
            new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas });
        using var reader = new StreamedInputReader(input, false, default);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Contains("FODC0002", Assert.Throws<XdmException>(() => reader.Skip()).Message);
    }

    [Fact]
    public void CancellationStopsASkipBeforeReadingTheFollowingNode()
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new CancellingReader(XmlReader.Create(new StringReader("<doc><skip><inside/><after/></skip><keep/></doc>")), cancellation);
        using var reader = new StreamedInputReader(input, false, cancellation.Token);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Throws<OperationCanceledException>(() => reader.Skip());
        Assert.Equal("inside", input.LocalName);
    }

    [Fact]
    public void MalformedSkippedContentUsesTheNormalReadError()
    {
        using var input = XmlReader.Create(new StringReader("<doc><skip><inside></skip></doc>"));
        using var reader = new StreamedInputReader(input, false, default);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Contains("FODC0002", Assert.Throws<XdmException>(() => reader.Skip()).Message);
    }

    private sealed class OnceFailingReader(XmlReader input) : XmlWrappingReader(input)
    {
        internal bool FailNextRead;

        public override bool Read()
        {
            if (FailNextRead)
            {
                FailNextRead = false;
                throw new IOException("Injected read failure before advancing the input");
            }
            return base.Read();
        }
    }

    private sealed class CancellingReader(XmlReader input, CancellationTokenSource cancellation) : XmlWrappingReader(input)
    {
        public override bool Read()
        {
            bool read = base.Read();
            if (read && NodeType == XmlNodeType.Element && LocalName == "inside") cancellation.Cancel();
            return read;
        }
    }
}
