// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;
using Xunit;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Tests;

public sealed class AtomicTypeCodeTests
{
    private static XmlQueryRuntime Runtime(StaticDataManager manager) => new(
        new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), manager) { Xslt2 = true },
        XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter());

    [Fact]
    public void GeneralSequenceChecksRejectFunctionsAsAtomicAndAcceptThemAsItems()
    {
        var manager = new StaticDataManager();
        int atomic = manager.DeclareXmlType(T.AnyAtomicType), item = manager.DeclareXmlType(T.ItemS);
        var runtime = Runtime(manager);
        var definition = FunctionLibrary.Find(QName.Ns.Fn, "true", 0)!;
        XPathItem[] functions = { MapItem.Empty, ArrayItem.Empty, new LibraryFunction(definition, 0, runtime.XsltFunctions.X31Context) };
        foreach (XPathItem function in functions)
        {
            Assert.False(runtime.MatchesXmlType(new[] { function }, atomic));
            Assert.True(runtime.MatchesXmlType(new[] { function }, item));
            Assert.True(runtime.MatchesXmlType(new[] { AtomicValue.OfString("text"), function }, item));
        }
    }

    [Fact]
    public void SpecificAtomicCodesRejectFunctionsWithoutDereferencingTheirMissingSchemaType()
    {
        var runtime = Runtime(new StaticDataManager());
        var definition = FunctionLibrary.Find(QName.Ns.Fn, "true", 0)!;
        XPathItem[] functions = { MapItem.Empty, ArrayItem.Empty, new LibraryFunction(definition, 0, runtime.XsltFunctions.X31Context) };
        foreach (XPathItem function in functions)
        {
            Assert.False(runtime.MatchesXmlType(function, XmlTypeCode.String));
            Assert.False(runtime.MatchesXmlType(new[] { function }, XmlTypeCode.Integer));
        }
    }

    [Fact]
    public void AtomicCodeMatchesTheGeneralTypeTestForNodesAtomsAndFunctions()
    {
        var manager = new StaticDataManager();
        int index = manager.DeclareXmlType(T.AnyAtomicType);
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), manager) { Xslt2 = true };
        var runtime = new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter());
        var node = new XPathDocument(new StringReader("<doc/>")).CreateNavigator();
        var definition = FunctionLibrary.Find(QName.Ns.Fn, "true", 0)!;
        XPathItem[] values = { AtomicValue.OfInteger(BigInteger.One << 200), AtomicValue.OfString("text"),
            AtomicValue.OfBoolean(true), node, MapItem.Empty, ArrayItem.Empty,
            new LibraryFunction(definition, 0, runtime.XsltFunctions.X31Context), new IterationControl(false) };
        foreach (XPathItem item in values)
        {
            bool expected = runtime.MatchesXmlType(item, index);
            Assert.Equal(expected, runtime.MatchesXmlType(item, XmlTypeCode.AnyAtomicType));
            Assert.Equal(expected, runtime.MatchesXmlType(new[] { item }, XmlTypeCode.AnyAtomicType));
        }
        Assert.False(runtime.MatchesXmlType(Array.Empty<XPathItem>(), XmlTypeCode.AnyAtomicType));
        Assert.False(runtime.MatchesXmlType(new[] { values[0], values[1] }, XmlTypeCode.AnyAtomicType));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void GeneratedAtomicChecksDoNotSaveTheirAtomicType(int shape, bool expected)
    {
        var f = new QilFactory();
        QilParameter parameter = f.Parameter(T.ItemS);
        parameter.DebugName = "item";
        QilFunction function = f.Function(f.FormalParameterList(new QilNode[] { parameter }),
            f.IsType(parameter, T.AnyAtomicType), f.False(), T.BooleanX);
        function.DebugName = "AtomicCheck";
        QilNode argument = shape switch
        {
            0 => f.Sequence(),
            1 => f.LiteralString("value"),
            2 => f.Sequence(new QilNode[] { f.LiteralString("first"), f.LiteralString("second") }),
            _ => f.XmlContext(),
        };
        QilExpression expression = f.QilExpression(f.Invoke(function, f.ActualParameterList(new[] { argument })), f);
        expression.FunctionList.Add(function);
        // Keep the formal parameter unknown instead of inlining a constant argument.
        expression.IsDebug = true;
        expression.DefaultWriterSettings = new XmlWriterSettings();
        XmlILCommand command = new XmlILGenerator().Generate(expression, null)!;
        Assert.DoesNotContain(command.StaticData.Types ?? Array.Empty<XmlQueryType>(), type => type.TypeCode == XmlTypeCode.AnyAtomicType);
        IList<XPathItem> result = command.Evaluate(new XPathDocument(new StringReader("<doc/>")).CreateNavigator(), null, null);
        Assert.Equal(expected, Assert.Single(result).ValueAsBoolean);
    }
}
