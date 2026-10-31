// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class ForkHeaderAtomizationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundaryAndAncestorSchemaSnapshotsBelongToTheirProng(bool ancestor)
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='row'><xs:complexType><xs:attribute name='k' type='xs:integer'/></xs:complexType></xs:element></xs:sequence><xs:attribute name='kind' type='xs:string'/></xs:complexType></xs:element></xs:schema>")));
        using var input = XmlReader.Create(new StringReader("<doc kind='root'><row k='7'/></doc>"), new() { ValidationType = ValidationType.Schema, Schemas = schemas });
        using var document = new StreamedDocument(input);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild()); Assert.True(focus.MoveToFirstChild());
        using var fork = document.Fork(focus);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var barrier = new Barrier(2);
        fork.Run(new Action<StreamedNavigator, CancellationToken>[] {
            (view, token) => Check(view, token, 0), (view, token) => Check(view, token, 1)
        }, 1, cancellation.Token);

        void Check(StreamedNavigator view, CancellationToken token, int index)
        {
            if (ancestor) Assert.True(view.MoveToParent());
            var attribute = view.Clone(); Assert.True(attribute.MoveToAttribute(ancestor ? "kind" : "k", ""));
            var nodeInfo = Assert.IsType<XmlSchemaInfo>(view.SchemaInfo);
            var attributeInfo = Assert.IsType<XmlSchemaInfo>(attribute.SchemaInfo);
            XmlSchemaValidity nodeBefore = nodeInfo.Validity, attributeBefore = attributeInfo.Validity;
            XmlSchemaValidity changed = nodeBefore == XmlSchemaValidity.Invalid ? XmlSchemaValidity.Valid : XmlSchemaValidity.Invalid;
            barrier.SignalAndWait(token);
            if (index == 0) { nodeInfo.Validity = changed; attributeInfo.Validity = XmlSchemaValidity.Invalid; }
            barrier.SignalAndWait(token);
            Assert.Equal(index == 0 ? changed : nodeBefore, view.SchemaInfo!.Validity);
            Assert.Equal(index == 0 ? XmlSchemaValidity.Invalid : attributeBefore, attribute.SchemaInfo!.Validity);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void ProngPreseededNamesOwnElementAttributeAndNamespaceReferences(int capacity)
    {
        using var input = XmlReader.Create(new StringReader("<doc><p:row xmlns:p='urn:row' p:id='v'/></doc>"));
        using var document = new StreamedDocument(input);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild());
        using var fork = document.Fork(focus);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var tables = new XmlNameTable[2];
        var names = new string[2];
        fork.Run(new Action<StreamedNavigator, CancellationToken>[] {
            (view, _) => Check(view, 0), (view, _) => Check(view, 1)
        }, capacity, cancellation.Token);
        Assert.NotSame(tables[0], tables[1]);
        Assert.NotSame(names[0], names[1]);

        void Check(StreamedNavigator view, int index)
        {
            tables[index] = view.NameTable;
            string Atom(string value) => view.NameTable.Add(new string(value.ToCharArray()));
            string local = Atom("row"), prefix = Atom("p"), uri = Atom("urn:row"), attributeName = Atom("id");
            Assert.True(view.MoveToFirstChild());
            Assert.Same(local, view.LocalName); Assert.Same(prefix, view.Prefix); Assert.Same(uri, view.NamespaceURI);
            names[index] = view.LocalName;
            var attribute = view.Clone(); Assert.True(attribute.MoveToAttribute("id", "urn:row"));
            Assert.Same(attributeName, attribute.LocalName); Assert.Same(prefix, attribute.Prefix); Assert.Same(uri, attribute.NamespaceURI);
            var ns = view.Clone(); Assert.True(ns.MoveToNamespace("p"));
            Assert.Same(prefix, ns.LocalName); Assert.Same(uri, ns.Value);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void SchemaAttributeSnapshotsStayIndependentAfterBothViewsAdvance(int capacity)
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='n'><xs:complexType><xs:attribute name='k' type='xs:integer'/></xs:complexType></xs:element></xs:sequence></xs:complexType></xs:element></xs:schema>")));
        using var input = XmlReader.Create(new StringReader("<doc><n k='7'/></doc>"), new() { ValidationType = ValidationType.Schema, Schemas = schemas });
        using var document = new StreamedDocument(input);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var barrier = new Barrier(2);
        var snapshots = new XmlSchemaInfo[2];
        fork.Run(new Action<StreamedNavigator, CancellationToken>[] {
            (view, token) => Check(view, token, 0), (view, token) => Check(view, token, 1)
        }, capacity, cancellation.Token);
        Assert.NotSame(snapshots[0], snapshots[1]);

        void Check(StreamedNavigator view, CancellationToken token, int index)
        {
            Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToFirstChild());
            var attribute = view.Clone(); Assert.True(attribute.MoveToAttribute("k", ""));
            barrier.SignalAndWait(token); // Both event projections must exist before reading their metadata.
            snapshots[index] = Assert.IsType<XmlSchemaInfo>(attribute.SchemaInfo);
            Assert.Equal(XmlTypeCode.Integer, snapshots[index].SchemaType!.TypeCode);
            barrier.SignalAndWait(token);
            if (index == 0) snapshots[index].Validity = XmlSchemaValidity.Invalid;
            barrier.SignalAndWait(token);
            Assert.Equal(index == 0 ? XmlSchemaValidity.Invalid : XmlSchemaValidity.Valid, attribute.SchemaInfo!.Validity);
        }
    }
}
