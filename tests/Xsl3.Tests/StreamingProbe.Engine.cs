// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;

namespace Xsl3.Tests;

internal static partial class StreamingProbe
{
    private static partial Invocation CreateInvocation(string workload, bool forward)
    {
        var executable = new XsltCompiler { Settings = new() { StreamedExecution = forward, EnableDocumentFunction = true, EnableResultDocument = true } }
            .Compile(new StringReader(Stylesheet(workload)));
        return (input, output, resolver, secondary) => {
            var transformer = executable.CreateTransformer();
            transformer.DocumentResolver = resolver;
            transformer.ResultDocumentHandler = secondary;
            transformer.ApplyTemplates(input, output);
        };
    }
}
