// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Xml;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xunit;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Tests;

public sealed class SavedFunctionUsageTests
{
    private static XmlQueryStaticData Data(bool usesIds) => new(new XmlWriterSettings(),
        Array.Empty<WhitespaceRule>(), new StaticDataManager { UsesBuiltinFunctionIds = usesIds });

    private static byte[] Save(XmlQueryStaticData data)
    {
        data.GetObjectData(out byte[] bytes, out Type[]? types);
        Assert.Null(types);
        return bytes;
    }

    private static void ChangeFingerprint(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var reader = new BinaryReader(input);
        reader.Read7BitEncodedInt();
        data[(int)input.Position] ^= 1;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewMetadataRoundTripsItsUsageProof(bool usesIds)
    {
        byte[] bytes = Save(Data(usesIds));
        Assert.Equal(usesIds, new XmlQueryStaticData(bytes, null).UsesBuiltinFunctionIds);
        ChangeFingerprint(bytes);
        if (usesIds) Assert.Throws<NotSupportedException>(() => new XmlQueryStaticData(bytes, null));
        else Assert.False(new XmlQueryStaticData(bytes, null).UsesBuiltinFunctionIds);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void OldFormatsRetainTheirUnconditionalCompatibilityCheck(int version)
    {
        byte[] current = Save(Data(false));
        // v4 added the initial-mode flag; v6 appends the usage proof. The empty
        // constant table here is deliberately valid in every tested old format.
        byte[] previous = current[..^(version == 3 ? 2 : 1)];
        Assert.Equal(6, previous[0]);
        previous[0] = (byte)version;
        Assert.True(new XmlQueryStaticData(previous, null).UsesBuiltinFunctionIds);
        ChangeFingerprint(previous);
        Assert.Throws<NotSupportedException>(() => new XmlQueryStaticData(previous, null));
    }

    [Fact]
    public void ATruncatedUsageProofCannotBeTreatedAsNoReferences()
    {
        byte[] bytes = Save(Data(false));
        Assert.Throws<EndOfStreamException>(() => new XmlQueryStaticData(bytes[..^1], null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryEmittedNumericLibraryCallRequiresTheFingerprint(int arity)
    {
        var f = new QilFactory();
        var arguments = new QilNode[2 + arity];
        // This test generates metadata only; the dummy function/static-context
        // indices are never executed. Its parameter shapes use the real call ABI.
        arguments[0] = f.LiteralDouble(0);
        arguments[1] = f.LiteralDouble(0);
        for (int i = 2; i < arguments.Length; i++) arguments[i] = f.Sequence();
        QilNode call = XPath31Compiler.Invoke(new QilPatternFactory(f, debug: false), "X31Call" + arity, T.ItemS, arguments);
        QilExpression expression = f.QilExpression(call, f);
        expression.DefaultWriterSettings = new XmlWriterSettings();
        XmlILCommand command = new XmlILGenerator().Generate(expression, null)!;
        Assert.True(command.StaticData.UsesBuiltinFunctionIds);
        byte[] bytes = Save(command.StaticData);
        ChangeFingerprint(bytes);
        Assert.Throws<NotSupportedException>(() => new XmlQueryStaticData(bytes, null));
    }

    [Fact]
    public void AnEmittedLiteralDoesNotDependOnRegistryOrdering()
    {
        var f = new QilFactory();
        QilExpression expression = f.QilExpression(f.LiteralString("literal"), f);
        expression.DefaultWriterSettings = new XmlWriterSettings();
        XmlILCommand command = new XmlILGenerator().Generate(expression, null)!;
        Assert.False(command.StaticData.UsesBuiltinFunctionIds);
        byte[] bytes = Save(command.StaticData);
        ChangeFingerprint(bytes);
        Assert.False(new XmlQueryStaticData(bytes, null).UsesBuiltinFunctionIds);
    }
}
