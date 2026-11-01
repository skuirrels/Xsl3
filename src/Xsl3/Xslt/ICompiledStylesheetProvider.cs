// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.XPath;
using Xsl3.Runtime;

namespace Xsl3;

/// <summary>Stateless adapter emitted by the saved stylesheet compiler for direct loading.</summary>
/// <remarks>Compiler infrastructure. Implementations must not depend on instance construction.
/// The compiler version and read-only query data describe the generated code; execution delegates
/// must belong to that same compilation. Existing saved types keep the legacy loader.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ICompiledStylesheetProvider
{
    string CompilerVersion { get; }
    ReadOnlyMemory<byte> QueryData { get; }
    Type[]? EarlyBoundTypes { get; }
    Action<XmlQueryRuntime> Execute { get; }
    Func<XmlQueryRuntime, XPathItem, IList<XPathItem>>? Dispatcher { get; }
}
