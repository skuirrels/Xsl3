// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class ForkCompletedSchemaTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void NestedClosingEventsKeepFinalDefaultAndValidationInfoPrivate(int capacity)
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader("""
            <xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'>
              <xs:element name='n' type='xs:int' default='17'/>
              <xs:element name='group'><xs:complexType><xs:sequence>
                <xs:element ref='n' maxOccurs='unbounded'/>
              </xs:sequence></xs:complexType></xs:element>
              <xs:element name='doc'><xs:complexType><xs:choice maxOccurs='unbounded'>
                <xs:element ref='n'/><xs:element ref='group'/>
              </xs:choice></xs:complexType></xs:element>
            </xs:schema>
            """)));
        using var input = XmlReader.Create(new StringReader("<doc><n> 7 </n><group><n></n><n>11</n></group><n>13</n></doc>"),
            new() { ValidationType = ValidationType.Schema, Schemas = schemas });
        using var document = new StreamedDocument(input);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var barrier = new Barrier(2);
        var snapshots = new List<XmlSchemaInfo>[] { new(), new() };
        fork.Run(new Action<StreamedNavigator, CancellationToken>[] {
            (view, token) => Check(view, token, 0), (view, token) => Check(view, token, 1)
        }, capacity, cancellation.Token);
        for (int i = 0; i < 4; i++) Assert.NotSame(snapshots[0][i], snapshots[1][i]);

        void Check(StreamedNavigator view, CancellationToken token, int prong)
        {
            int[] expected = { 7, 17, 11, 13 };
            Assert.True(view.MoveToFirstChild()); // doc
            Assert.True(view.MoveToFirstChild()); // n
            CheckNode(0);
            Assert.True(view.MoveToNext()); // group
            Assert.True(view.MoveToFirstChild()); // defaulted n
            CheckNode(1);
            Assert.True(view.MoveToNext()); // n
            CheckNode(2);
            Assert.False(view.MoveToNext());
            Assert.True(view.MoveToParent()); // group
            Assert.True(view.MoveToNext()); // n
            CheckNode(3);
            Assert.False(view.MoveToNext());
            barrier.SignalAndWait(token);
            if (prong == 0) foreach (var info in snapshots[prong]) info.Validity = XmlSchemaValidity.Invalid;
            barrier.SignalAndWait(token);
            foreach (var info in snapshots[prong])
                Assert.Equal(prong == 0 ? XmlSchemaValidity.Invalid : XmlSchemaValidity.Valid, info.Validity);

            void CheckNode(int i)
            {
                var node = view;
                var before = Assert.IsType<XmlSchemaInfo>(node.SchemaInfo);
                Assert.Equal(expected[i], Assert.IsType<int>(node.TypedValue));
                var after = Assert.IsType<XmlSchemaInfo>(node.SchemaInfo);
                Assert.Equal(XmlSchemaValidity.Valid, after.Validity);
                Assert.Equal(i == 1, after.IsDefault);
                Assert.Equal(XmlTypeCode.Int, after.SchemaType!.TypeCode);
                Assert.NotSame(before, after);
                snapshots[prong].Add(after);
            }
        }
    }
}
