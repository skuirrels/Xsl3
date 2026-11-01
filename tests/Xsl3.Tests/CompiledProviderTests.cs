// Licensed under the MIT license. See LICENSE.TXT in the repository root.
#if NET9_0_OR_GREATER
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class CompiledProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedProviderPreservesVersionDataAndFunctionDispatch(bool debug)
    {
        const string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:output method='text'/><xsl:template match='/'><xsl:variable name='f' select='function($v){$v+1}'/><xsl:value-of select='$f(41)'/></xsl:template></xsl:stylesheet>";
        var ordinary = new XslCompiledTransform(debug);
        ordinary.Load(XmlReader.Create(new StringReader(sheet)));
        using var expected = new StringWriter();
        ordinary.Transform(XmlReader.Create(new StringReader("<doc/>")), null, expected);
        using var image = new MemoryStream();
        var compiler = new XslCompiledTransform(debug);
        string name = "Provider_" + Guid.NewGuid().ToString("N");
        compiler.CompileToAssembly(XmlReader.Create(new StringReader(sheet)), new XsltSettings(), null, name, name, image);
        image.Position = 0;
        Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(image);
        Type type = assembly.GetType(name, throwOnError: true)!;
        Assert.True(type.IsSealed && !type.IsAbstract);
        Assert.True(typeof(ICompiledStylesheetProvider).IsAssignableFrom(type));
        var provider = (ICompiledStylesheetProvider)RuntimeHelpers.GetUninitializedObject(type);
        Assert.Equal(typeof(XslCompiledTransform).Assembly.GetName().Version!.ToString(), provider.CompilerVersion);
        Assert.NotEmpty(provider.QueryData.ToArray());
        Assert.NotNull(provider.Execute);
        Assert.NotNull(provider.Dispatcher);
        var saved = new XslCompiledTransform();
        saved.Load(type, new XsltSettings());
        using var actual = new StringWriter();
        saved.Transform(XmlReader.Create(new StringReader("<doc/>")), null, actual);
        Assert.Equal(expected.ToString(), actual.ToString());
    }
}
#endif
