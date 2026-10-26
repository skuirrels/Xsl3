// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;

namespace Xsl3.Tests;

internal static partial class StreamingProbe
{
    private static partial Invocation CreateInvocation(string workload, bool forward)
    {
        // RunChild selects the development mode for the lifetime of this process.
        // Use the same public settings and transformer APIs as an ordinary consumer.
        var executable = new XsltCompiler { Settings = new() { EnableDocumentFunction = true, EnableResultDocument = true } }
            .Compile(new StringReader(Stylesheet(workload)));
        return (input, output, resolver, secondary) => {
            var transformer = executable.CreateTransformer();
            transformer.DocumentResolver = resolver;
            transformer.ResultDocumentHandler = secondary;
            transformer.ApplyTemplates(input, output);
        };
    }
}
