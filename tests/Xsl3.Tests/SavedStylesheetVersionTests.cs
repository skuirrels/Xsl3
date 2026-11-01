// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using Xsl3.Runtime;
using Xunit;

namespace Xsl3.Tests;

public class SavedStylesheetVersionTests
{
    [Fact]
    public void RuntimeVersionMetadataMatchesTheActualAssemblyHeader()
    {
        Assembly assembly = typeof(XslCompiledTransform).Assembly;
        AssemblyMetadataAttribute metadata = Assert.Single(assembly.GetCustomAttributes<AssemblyMetadataAttribute>(),
            a => a.Key == "Xsl3.RuntimeAssemblyVersion");
        Version supplied = Version.Parse(metadata.Value!);
        Assert.Equal(assembly.GetName().Version, new Version(supplied.Major, supplied.Minor,
            Math.Max(0, supplied.Build), Math.Max(0, supplied.Revision)));
        Assert.Equal(assembly.GetName().Version!.ToString(), typeof(XslCompiledTransform)
            .GetField("s_version", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
    }

#if NET9_0_OR_GREATER
    private const string Sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:output method='text'/><xsl:template match='/'/></xsl:stylesheet>";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmittedVersionMatchesTheAssemblyHeaderAndCompatibilityChecksRemainExact(bool debug)
    {
        Version current = typeof(XslCompiledTransform).Assembly.GetName().Version!;
        using var image = new MemoryStream();
        using (var sheet = XmlReader.Create(new StringReader(Sheet)))
            new XslCompiledTransform(debug).CompileToAssembly(sheet, new(), null, "VersionContract", "Saved.VersionContract", image);
        Type saved = Assembly.Load(image.ToArray()).GetType("Saved.VersionContract", true)!;
        Assert.Equal(current.ToString(), saved.GetCustomAttribute<GeneratedCodeAttribute>()!.Version);
        byte[] data = (byte[])saved.GetField(XmlQueryStaticData.DataFieldName, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        foreach (string version in new[] { current.ToString(), current.ToString(2), "000" + current.ToString(), new Version(0, 0, 0, 0).ToString() })
        {
            Assert.False(current < Version.Parse(version));
            var transform = new XslCompiledTransform();
            transform.Load(WithVersion(version, data), new());
            using var input = XmlReader.Create(new StringReader("<doc/>"));
            using var output = new StringWriter();
            transform.Transform(input, null, output);
            Assert.Equal("", output.ToString());
        }
        string newer = new Version(current.Major + 1, 0, 0, 0).ToString();
        var newerError = Assert.Throws<ArgumentException>(() => new XslCompiledTransform().Load(WithVersion(newer, data), new()));
        Assert.Equal("compiledStylesheet", newerError.ParamName);
        Assert.Contains(newer, newerError.Message);

        foreach (string? version in new[] { null, "", "invalid", "1.2.3.4.5", "-1.0", "2147483648.0" })
        {
            Exception expected = Assert.IsAssignableFrom<Exception>(Record.Exception(() => Version.Parse(version!)));
            Exception actual = Assert.IsAssignableFrom<Exception>(Record.Exception(() => new XslCompiledTransform().Load(WithVersion(version, data), new())));
            Assert.Equal(expected.GetType(), actual.GetType());
        }
    }

    private static Type WithVersion(string? version, byte[] data)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("VersionContract"), AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder type = assembly.DefineDynamicModule("VersionContract").DefineType("Saved.VersionContract", TypeAttributes.Public);
        type.SetCustomAttribute(new CustomAttributeBuilder(typeof(GeneratedCodeAttribute).GetConstructor([typeof(string), typeof(string)])!,
            [typeof(XslCompiledTransform).FullName!, version!]));
        type.DefineField(XmlQueryStaticData.DataFieldName, typeof(byte[]), FieldAttributes.Static | FieldAttributes.Private);
        type.DefineField(XmlQueryStaticData.TypesFieldName, typeof(Type[]), FieldAttributes.Static | FieldAttributes.Private);
        MethodBuilder execute = type.DefineMethod("Execute", MethodAttributes.Private | MethodAttributes.Static, typeof(void), [typeof(XmlQueryRuntime)]);
        execute.GetILGenerator().Emit(OpCodes.Ret);
        Type result = type.CreateType()!;
        result.GetField(XmlQueryStaticData.DataFieldName, BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, data);
        return result;
    }
#endif
}
