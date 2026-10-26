// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;
using Xunit;

namespace Xsl3.Tests;

public class FunctionSignatureTests
{
    [Fact]
    public void MetadataDoesNotResolveTypes()
    {
        int calls = 0;
        var signature = new FunctionDefinition.Signature(Array.Empty<(string, SequenceTypeTest)>(), SequenceTypeTest.AnySequence);
        var definition = new FunctionDefinition(7, new QName("urn:test", "metadata"), 0,
            () => { calls++; return signature; }, (_, _) => Array.Empty<System.Xml.XPath.XPathItem>(), false);
        Assert.Equal(7, definition.Id);
        Assert.Equal(0, definition.Arity);
        Assert.Equal("metadata", definition.Name.LocalName);
        Assert.False(definition.IsVariadic);
        Assert.Equal(0, calls);
        Assert.Same(signature.ReturnType, definition.ReturnType);
        Assert.Same(signature.Parameters, definition.Parameters);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ConcurrentTypeRequestsPublishOneSignature()
    {
        int calls = 0;
        var signature = new FunctionDefinition.Signature(new[] { ("value", SequenceTypeTest.AnySequence) }, SequenceTypeTest.AnySequence);
        var definition = new FunctionDefinition(0, new QName("urn:test", "concurrent"), 1,
            () => { Interlocked.Increment(ref calls); return signature; }, (_, args) => args[0], false);
        Parallel.For(0, 100, _ =>
        {
            Assert.Same(signature.Parameters, definition.Parameters);
            Assert.Same(signature.ReturnType, definition.ReturnType);
            Assert.Same(signature.Parameters[0].Item2, definition.ParameterType(0));
        });
        Assert.Equal(1, calls);
    }

    [Fact]
    public void SignatureErrorsAreRetained()
    {
        int calls = 0;
        var error = new InvalidOperationException("invalid signature");
        var definition = new FunctionDefinition(0, new QName("urn:test", "invalid"), 1,
            () => { calls++; throw error; }, (_, args) => args[0], false);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => definition.Parameters));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => definition.ReturnType));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void EveryCanonicalSignatureResolvesAndRetainsItsFingerprint()
    {
        Parallel.ForEach(FunctionLibrary.Definitions, definition =>
        {
            Assert.Equal(definition.Arity, definition.Parameters.Count);
            Assert.NotNull(definition.ReturnType);
            foreach (var parameter in definition.Parameters) Assert.NotNull(parameter.Type);
            Assert.Same(definition, FunctionLibrary.ById(definition.Id));
            Assert.Same(definition, FunctionLibrary.Find(definition.Name.NamespaceUri, definition.Name.LocalName, definition.Arity));
            if (definition.IsVariadic)
                Assert.Same(definition, FunctionLibrary.Find(definition.Name.NamespaceUri, definition.Name.LocalName, definition.Arity + 3));
        });
        uint hash = 2166136261;
        foreach (FunctionDefinition definition in FunctionLibrary.Definitions)
            foreach (char c in definition.Name.NamespaceUri + " " + definition.Name.LocalName + " " + definition.Arity.ToString(CultureInfo.InvariantCulture) + ";")
                hash = unchecked((hash ^ c) * 16777619);
        Assert.Equal(unchecked((int)hash), FunctionLibrary.Fingerprint);
    }
}
