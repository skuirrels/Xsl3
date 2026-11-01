// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Schema;
using Xsl3.Schema;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public partial class SchemaDefaultContentTests
{
    public static IEnumerable<object[]> DirectCases()
    {
        foreach (DefaultCase item in ContentCases())
        foreach (bool strict in new[] { false, true }) yield return new object[] { item, strict };
    }

    public static IEnumerable<object[]> CompiledCases()
    {
        foreach (DefaultCase item in PipelineCases())
        foreach (bool strict in new[] { false, true })
        foreach (bool inherit in new[] { false, true })
        foreach (int delivery in Deliveries())
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
            yield return new object[] { item, strict, inherit, delivery, debug, forward };
    }

    private static IEnumerable<int> Deliveries()
    {
        yield return 0; yield return 1;
#if NET10_0_OR_GREATER
        yield return 2;
#endif
    }

    public static IEnumerable<object[]> TypedValueCases()
    {
        foreach (DefaultCase item in TypedCases())
        foreach (bool strict in new[] { false, true }) yield return new object[] { item, strict };
    }

    [Theory]
    [MemberData(nameof(TypedValueCases))]
    public void DefaultedValuesRetainQNameTimezoneAndDurationComponents(DefaultCase item, bool strict)
    {
        XmlSchemaSet schemas = Schemas(item);
        XmlSchemaType type = ((XmlSchemaElement)schemas.GlobalElements[new XmlQualifiedName("e")]!).ElementSchemaType!;
        using var input = XmlReader.Create(new StringReader(Element(item)));
        var original = new XPathDocument(input, XmlSpace.Preserve).CreateNavigator(); Assert.True(original.MoveToFirstChild());
        XPathNavigator result = NodeValidator.Validate(original, schemas, strict, null);
        Assert.Equal(ExpectedTyped(item, type), TypedSignature(result, type));
        Assert.Equal("urn:schema-default", result.LookupNamespace("p"));
    }

    [Theory]
    [MemberData(nameof(DirectCases))]
    public void DefaultedContentMatchesTheValidatingReader(DefaultCase item, bool strict)
    {
        ReferenceResult reference = Reference(item);
        using var reader = XmlReader.Create(new StringReader(Element(item)));
        XPathNavigator original = new XPathDocument(reader, XmlSpace.Preserve).CreateNavigator();
        Assert.True(original.MoveToFirstChild());
        XPathNavigator? result = null;
        Exception? error = Record.Exception(() => result = NodeValidator.Validate(original, Schemas(item), strict, null));
        if (!reference.Valid) { Assert.IsType<XdmException>(error); return; }
        Assert.Null(error); Assert.NotNull(result); Assert.Equal(reference.Trace, Trace(result!));
        Assert.Equal("e", original.LocalName);
    }

    [Theory]
    [MemberData(nameof(CompiledCases))]
    public void ValidatedCopiesKeepDefaultNodesAndParentPhases(DefaultCase item, bool strict, bool inherit, int delivery, bool debug, bool forward)
    {
        const int rows = 3;
        var settings = new XsltSettings { SchemaAware = true, StreamedExecution = forward, IncludeDebugInformation = debug };
        using var input = XmlReader.Create(new StringReader(Input(item, rows)));
        using var sink = new StringWriter(CultureInfo.InvariantCulture);
        using var capture = new StreamingExecutionCapture();
        using (StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet))
        {
            string sheet = Sheet(item, strict, inherit);
            if (delivery == 0) new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(input, sink);
            else
            {
                var transform = new XslCompiledTransform(debug);
                using var style = XmlReader.Create(new StringReader(sheet));
#if NET10_0_OR_GREATER
                if (delivery == 2)
                {
                    using var image = new MemoryStream();
                    transform.CompileToAssembly(style, settings, null, "SchemaDefaults" + Guid.NewGuid().ToString("N"), "Saved.SchemaDefaults", image);
                    transform.Load(Assembly.Load(image.ToArray()).GetType("Saved.SchemaDefaults", true)!, settings);
                }
                else
#endif
                    transform.Load(style, settings, null);
                transform.Transform(input, null, sink);
            }
        }
        Assert.Equal(Expected(item, rows, inherit), sink.ToString());
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        var execution = Assert.Single(capture.Snapshot().Executions);
        Assert.True(execution.BodyCompleted); var source = Assert.Single(execution.Sources);
        Assert.Equal(forward, source.RequestedForward); Assert.Equal(forward, source.ActualForward);
    }
}
