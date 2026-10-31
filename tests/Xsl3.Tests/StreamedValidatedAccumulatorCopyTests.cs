// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public partial class StreamedValidatedAccumulatorCopyTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (int delivery in Deliveries())
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
        foreach (bool inherit in new[] { false, true })
        foreach (string validation in new[] { "preserve", "strip", "lax", "strict", "anyType", "untypedAtomic" })
            yield return new object[] { delivery, debug, forward, inherit, validation };
    }

    private static IEnumerable<int> Deliveries()
    {
        yield return 0;
        yield return 1;
#if NET10_0_OR_GREATER
        yield return 2;
#endif
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ValidatedCopiesInheritSourcePhasesInsteadOfRestartingRules(int delivery, bool debug, bool forward, bool inherit, string validation)
    {
        const int rows = 3;
        Assert.Equal(Expected(rows, inherit), Transform(Sheet(inherit, validation), Input(rows), delivery, debug, forward));
    }

    public static IEnumerable<object[]> InvalidElementCases()
    {
        foreach (int delivery in Deliveries())
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
        foreach (bool inherit in new[] { false, true })
            yield return new object[] { delivery, debug, forward, inherit };
    }

    [Theory]
    [MemberData(nameof(InvalidElementCases))]
    public void SimpleTypeCopiesStillRejectElementContent(int delivery, bool debug, bool forward, bool inherit)
    {
        Exception? error = Record.Exception(() => Transform(Sheet(inherit, "untypedAtomic"),
            "<doc><row><child/></row></doc>", delivery, debug, forward));
        string? code = error switch {
            XsltDynamicException modern => modern.Code.LocalName,
            XdmException legacy => legacy.Code,
            _ => null,
        };
        Assert.Equal("XTTE1540", code);
    }

    private static string Transform(string sheet, string xml, int delivery, bool debug, bool forward)
    {
        var settings = new XsltSettings { StreamedExecution = forward, IncludeDebugInformation = debug, SchemaAware = true };
        using var input = XmlReader.Create(new StringReader(xml));
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var capture = new StreamingExecutionCapture();
        using (StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet))
        {
            if (delivery == 0)
                new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(input, output);
            else
            {
                var transform = new XslCompiledTransform(debug);
                using var style = XmlReader.Create(new StringReader(sheet));
#if NET10_0_OR_GREATER
                if (delivery == 2)
                {
                    using var image = new MemoryStream();
                    transform.CompileToAssembly(style, settings, null, "ValidatedAccumulator" + Guid.NewGuid().ToString("N"), "Saved.ValidatedAccumulator", image);
                    transform.Load(Assembly.Load(image.ToArray()).GetType("Saved.ValidatedAccumulator", true)!, settings);
                }
                else
#endif
                    transform.Load(style, settings, null);
                transform.Transform(input, null, output);
            }
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        var execution = Assert.Single(capture.Snapshot().Executions);
        Assert.True(execution.BodyCompleted);
        var source = Assert.Single(execution.Sources);
        Assert.Equal(forward, source.RequestedForward);
        Assert.Equal(forward, source.ActualForward);
        return output.ToString();
    }
}
