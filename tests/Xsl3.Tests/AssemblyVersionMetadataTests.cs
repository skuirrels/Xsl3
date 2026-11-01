// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Reflection;
using System.Linq;
using Xunit;

namespace Xsl3.Tests;

public class AssemblyVersionMetadataTests
{
    private const string Key = "Xsl3.RuntimeAssemblyVersion";
    private static AssemblyMetadataAttribute SdkSource => new(Key + ".Source", "SDK");
    private static string Header => typeof(XslCompiledTransform).Assembly.GetName().Version!.ToString();

    [Fact]
    public void UniqueSdkMetadataKeepsTheActualVersion()
    {
        var metadata = typeof(XslCompiledTransform).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        Assert.Equal(Header, Assert.Single(metadata, item => item.Key == Key).Value);
        Assert.Equal("SDK", Assert.Single(metadata, item => item.Key == Key + ".Source").Value);
        Assert.Equal(Header, XslCompiledTransform.ReadAssemblyVersionText(metadata));
    }

    [Fact]
    public void MissingReservedMetadataUsesTheAssemblyHeader()
    {
        Assert.Equal(Header, XslCompiledTransform.ReadAssemblyVersionText(Array.Empty<AssemblyMetadataAttribute>()));
        Assert.Equal(Header, XslCompiledTransform.ReadAssemblyVersionText(new[] { new AssemblyMetadataAttribute("Other", "99.0.0.0") }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateReservedKeysUseTheAssemblyHeader(bool conflicting)
    {
        var metadata = new[] {
            SdkSource,
            new AssemblyMetadataAttribute(Key, conflicting ? "99.0.0.0" : Header),
            new AssemblyMetadataAttribute(Key, Header)
        };
        Assert.Equal(Header, XslCompiledTransform.ReadAssemblyVersionText(metadata));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4.3")]
    [InlineData("4.3.*")]
    [InlineData("0004.03.002.01")]
    [InlineData("65536.0.0.0")]
    [InlineData("0.65536.0.0")]
    [InlineData("0.0.65536.0")]
    [InlineData("0.0.0.65536")]
    [InlineData("999999999999999999999999999999.0.0.0")]
    [InlineData("4.3.2.1.0")]
    [InlineData("4.3.x.1")]
    public void InvalidOrCompilerNormalizedMetadataUsesTheAssemblyHeader(string? value)
    {
        Assert.Equal(Header, XslCompiledTransform.ReadAssemblyVersionText(new[] { SdkSource, new AssemblyMetadataAttribute(Key, value) }));
    }

    [Fact]
    public void CanonicalMetadataWithoutSdkProvenanceUsesTheHeader() =>
        Assert.Equal(Header, XslCompiledTransform.ReadAssemblyVersionText(new[] { new AssemblyMetadataAttribute(Key, "99.0.0.0") }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sdk")]
    [InlineData("manual")]
    public void UnrecognizedProvenanceUsesTheHeader(string? source) =>
        Assert.Equal(Header, XslCompiledTransform.ReadAssemblyVersionText(new[] {
            new AssemblyMetadataAttribute(Key + ".Source", source), new AssemblyMetadataAttribute(Key, "99.0.0.0") }));

    [Fact]
    public void DuplicateSdkProvenanceUsesTheHeader() =>
        Assert.Equal(Header, XslCompiledTransform.ReadAssemblyVersionText(new[] { SdkSource, SdkSource, new AssemblyMetadataAttribute(Key, "99.0.0.0") }));
}
