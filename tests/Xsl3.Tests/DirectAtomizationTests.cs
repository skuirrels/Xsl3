// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;
using Xunit;

namespace Xsl3.Tests;

public sealed class DirectAtomizationTests
{
    private static XsltLibrary Library()
    {
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager()) { Xslt2 = true };
        return new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter()).XsltFunctions;
    }

    [Fact]
    public void TheDirectEntryPointKeepsCanonicalAtomsArraysNodesAndErrors()
    {
        XsltLibrary library = Library();
        FunctionDefinition canonical = FunctionLibrary.Find(QName.Ns.Fn, "data", 1)!;
        var node = new XPathDocument(new StringReader("<doc>value<!--comment--><?pi data?></doc>")).CreateNavigator();
        node.MoveToChild(XPathNodeType.Element);
        IList<XPathItem>[] inputs = {
            Array.Empty<XPathItem>(),
            new XPathItem[] { AtomicValue.OfString("text"), AtomicValue.OfBoolean(true) },
            new XPathItem[] { node },
            new XPathItem[] { ArrayItem.Empty.Append(new XPathItem[] { AtomicValue.OfString("one"), AtomicValue.OfString("two") }) },
            new XPathItem[] { MapItem.Empty },
        };
        foreach (IList<XPathItem> input in inputs)
        {
            IList<XPathItem>? expected = null;
            Exception? error = Record.Exception(() => expected = canonical.Invoke(library.X31Context, new[] { input }));
            if (error is XdmException failure)
                Assert.Equal(failure.Code, Assert.Throws<XdmException>(() => library.X31Data(input)).Code);
            else
            {
                Assert.Null(error);
                var actual = library.X31Data(input).Cast<AtomicValue>().ToArray();
                Assert.Equal(expected!.Cast<AtomicValue>().Select(a => (a.Type, a.Value)), actual.Select(a => (a.Type, a.Value)));
            }
        }
    }

    [Fact]
    public void AtomicResultsSurviveReuseOfTheQilArgumentBuffer()
    {
        XsltLibrary library = Library();
        var input = new XmlQueryItemSequence();
        input.Add(AtomicValue.OfString("first"));
        IList<XPathItem> first = library.X31Data(input);
        input.Clear(); input.Add(AtomicValue.OfString("second"));
        IList<XPathItem> second = library.X31Data(input);
        input.Clear();
        Assert.Equal("first", Assert.Single(first).Value);
        Assert.Equal("second", Assert.Single(second).Value);
    }

    [Fact]
    public void TheDirectEntryPointHonorsCancellation()
    {
        XsltLibrary library = Library();
        CancellationToken previous = XmlQueryRuntime.Cancellation;
        using var cancellation = new CancellationTokenSource();
        try
        {
            cancellation.Cancel(); XmlQueryRuntime.Cancellation = cancellation.Token;
            Assert.Throws<OperationCanceledException>(() => library.X31Data(Array.Empty<XPathItem>()));
        }
        finally { XmlQueryRuntime.Cancellation = previous; }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_command")]
    private static extern ref XmlILCommand? Command(XslCompiledTransform transform);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamedNodeCastsDoNotRequireNumericBuiltinIds(bool debug)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:template match="/"><xsl:for-each select="doc/value"><xsl:value-of select="xs:integer(.)"/><xsl:text>;</xsl:text></xsl:for-each></xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XslCompiledTransform(debug);
        using var stylesheet = XmlReader.Create(new StringReader(sheet));
        transform.Load(stylesheet, new XsltSettings { StreamedExecution = true }, null);
        Assert.False(Command(transform)!.StaticData.UsesBuiltinFunctionIds);
        using var input = XmlReader.Create(new StringReader("<doc><value>9007199254740993</value><value>2</value></doc>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("9007199254740993;2;", output.ToString());
    }
}
