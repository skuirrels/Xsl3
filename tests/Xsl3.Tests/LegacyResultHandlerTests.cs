// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Reflection;
using System.Xml;
using Xsl3.Runtime;
using Xunit;

namespace Xsl3.Tests;

public class LegacyResultHandlerTests
{
    private const string Sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:mode streamable='yes'/><xsl:output omit-xml-declaration='yes'/>"
        + "<xsl:template match='/'><xsl:result-document href='urn:out'><out><xsl:value-of select='count(doc/row)'/></out></xsl:result-document><done/></xsl:template></xsl:stylesheet>";

    [Theory]
    [InlineData(false)]
#if NET9_0_OR_GREATER
    [InlineData(true)]
#endif
    public void PerRunPublicHandlerWorksWithCompiledAndSavedStylesheets(bool saved)
    {
        var unit = new XslCompiledTransform();
        var settings = new XsltSettings { StreamedExecution = true };
        using var stylesheet = XmlReader.Create(new StringReader(Sheet));
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            unit.CompileToAssembly(stylesheet, settings, null, "LegacyHandler", "Saved.LegacyHandler", assembly);
            unit.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.LegacyHandler", true)!, settings);
#endif
        }
        else unit.Load(stylesheet, settings, null);
        for (int call = 0; call < 2; call++)
        {
            var secondary = new TrackedWriter();
            var args = new XsltArgumentList { ResultDocumentHandler = uri => { Assert.Equal("urn:out", uri.ToString()); return secondary; } };
            using var input = XmlReader.Create(new StringReader("<doc><row/><row/></doc>")); using var output = new StringWriter();
            unit.Transform(input, args, output);
            Assert.Equal("<done/>", output.ToString()); Assert.Equal("<out>2</out>", secondary.ToString()); Assert.True(secondary.Closed);
            Assert.NotEqual(ReadState.Closed, input.ReadState);
        }
    }

    [Fact]
    public void HandlerIsScopedAndExplicitTransformerDeliveryTakesPrecedence()
    {
        var unit = new XslCompiledTransform(); unit.Load(XmlReader.Create(new StringReader(Sheet)), new(), null);
        Func<string, string, TextWriter>? saved = XmlQueryRuntime.ResultDocumentHandler;
        Func<string, string, TextWriter> ambient = (_, _) => throw new InvalidOperationException("Ambient handler used");
        try
        {
            XmlQueryRuntime.ResultDocumentHandler = ambient;
            var args = new XsltArgumentList { ResultDocumentHandler = _ => throw new InvalidOperationException("Argument handler used") };
            var secondary = new TrackedWriter();
            using var input = XmlReader.Create(new StringReader("<doc/>")); using var output = new StringWriter();
            unit.TransformTo(input, args, output, null, null, (_, _) => secondary);
            Assert.Equal("<out>0</out>", secondary.ToString()); Assert.True(secondary.Closed);
            Assert.Same(ambient, XmlQueryRuntime.ResultDocumentHandler);
        }
        finally { XmlQueryRuntime.ResultDocumentHandler = saved; }
    }

    [Fact]
    public void AThrowingHandlerRestoresTheAmbientHandler()
    {
        var unit = new XslCompiledTransform(); unit.Load(XmlReader.Create(new StringReader(Sheet)), new(), null);
        Func<string, string, TextWriter>? saved = XmlQueryRuntime.ResultDocumentHandler;
        Func<string, string, TextWriter> ambient = (_, _) => new StringWriter();
        try
        {
            XmlQueryRuntime.ResultDocumentHandler = ambient;
            var failure = new InvalidOperationException("handler failed");
            using var input = XmlReader.Create(new StringReader("<doc/>")); using var output = new StringWriter();
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => unit.Transform(input, new XsltArgumentList { ResultDocumentHandler = _ => throw failure }, output)));
            Assert.Same(ambient, XmlQueryRuntime.ResultDocumentHandler);
        }
        finally { XmlQueryRuntime.ResultDocumentHandler = saved; }
    }

    [Fact]
    public void RelativeTargetsReachTheHandlerAndItDoesNotLeakToTheNextInvocation()
    {
        var unit = new XslCompiledTransform();
        unit.Load(XmlReader.Create(new StringReader(Sheet.Replace("urn:out", "part.xml", StringComparison.Ordinal))), new(), null);
        var secondary = new TrackedWriter();
        var args = new XsltArgumentList { ResultDocumentHandler = uri => {
            Assert.False(uri.IsAbsoluteUri); Assert.Equal("part.xml", uri.ToString()); return secondary;
        } };
        using (var input = XmlReader.Create(new StringReader("<doc/>")))
        using (var output = new StringWriter()) unit.Transform(input, args, output);
        Assert.True(secondary.Closed); Assert.Equal("<out>0</out>", secondary.ToString());
        using var secondInput = XmlReader.Create(new StringReader("<doc/>")); using var secondOutput = new StringWriter();
        Assert.Contains("XTDE1480", Assert.Throws<Xsl3.Xdm.XdmException>(() => unit.Transform(secondInput, null, secondOutput)).Message);
    }

    private sealed class TrackedWriter : StringWriter
    {
        internal bool Closed;
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }
}
