// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xunit;

namespace Xsl3.Tests;

public class DirectProviderLoadTests
{
    private static byte[] Data(bool earlyBound, out Type[]? types)
    {
        var manager = new StaticDataManager();
        if (earlyBound) manager.DeclareEarlyBound("urn:direct-provider:extension", typeof(Extension));
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), manager);
        data.GetObjectData(out byte[] bytes, out types);
        return bytes;
    }

    private sealed class Provider(byte[] bytes) : ICompiledStylesheetProvider
    {
        public string CompilerVersion { get; init; } = typeof(XslCompiledTransform).Assembly.GetName().Version!.ToString();
        public ReadOnlyMemory<byte> QueryData => bytes;
        public Type[]? EarlyBoundTypes { get; init; }
        public Action<XmlQueryRuntime> Execute { get; init; } = _ => throw new InvalidOperationException("Rejected metadata must never execute");
        public Func<XmlQueryRuntime, XPathItem, IList<XPathItem>>? Dispatcher => null;
    }

    public sealed class Extension
    {
        public Extension() => throw new InvalidOperationException("Metadata parsing must not construct the extension");
    }

    [Fact]
    public void NullAdapterResetsPreviouslyLoadedState()
    {
        var transform = new XslCompiledTransform();
        transform.Load(new Provider(Data(false, out _)));
        Assert.NotNull(transform.OutputSettings);
        Assert.Throws<ArgumentNullException>(() => transform.Load((ICompiledStylesheetProvider)null!));
        Assert.Null(transform.OutputSettings);
    }

    [Fact]
    public void IncompatibleVersionRejectsBeforeDataOrExecution()
    {
        Version current = typeof(XslCompiledTransform).Assembly.GetName().Version!;
        var provider = new Provider(Array.Empty<byte>()) { CompilerVersion = new Version(current.Major + 1, 0).ToString() };
        Assert.Equal("compiledStylesheet", Assert.Throws<ArgumentException>(() => new XslCompiledTransform().Load(provider)).ParamName);
    }

    [Fact]
    public void AdvertisedEarlyBoundClassesRequireTheExistingLoader()
    {
        byte[] bytes = Data(true, out Type[]? types);
        Assert.Throws<NotSupportedException>(() => new XslCompiledTransform().Load(new Provider(bytes) { EarlyBoundTypes = types }));
        // The original loader's schema still accepts the same real serialized metadata.
        Assert.Same(typeof(Extension), Assert.Single(new XmlQueryStaticData(bytes, types).EarlyBound!).EarlyBoundType);
    }

    [Fact]
    public void EncodedEarlyBoundClassesCannotBypassRejectionByOmittingTypes()
    {
        byte[] bytes = Data(true, out _);
        Assert.Throws<NotSupportedException>(() => new XslCompiledTransform().Load(new Provider(bytes)));
    }

    [Fact]
    public void EmptyQueryDataRetainsItsActualParseFailure()
    {
        Assert.Throws<EndOfStreamException>(() => new XslCompiledTransform().Load(new Provider(Array.Empty<byte>())));
    }

    [Fact]
    public void DedicatedTypeLoaderRejectsNull()
    {
        Assert.Equal("compiledStylesheet", Assert.Throws<ArgumentNullException>(() => new XslCompiledTransform().LoadCompiledAdapter(null!)).ParamName);
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(ICompiledStylesheetProvider))]
    public void DedicatedTypeLoaderRejectsTypesWithoutTheEmittedContract(Type type)
    {
        Assert.Equal("compiledStylesheet", Assert.Throws<ArgumentException>(() => new XslCompiledTransform().LoadCompiledAdapter(type)).ParamName);
    }

    [Fact]
    public void MissingExecutionDelegateIsRejected()
    {
        var provider = new Provider(Data(false, out _)) { Execute = null! };
        Assert.Throws<ArgumentNullException>(() => new XslCompiledTransform().Load(provider));
    }
}
