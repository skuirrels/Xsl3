// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Runtime.CompilerServices;

namespace Xsl3.W3C;

internal static class AssemblySetup
{
    // Regular expressions that backtrack without end (the unicode-90 set has some) time out instead of eating
    // memory. Regex reads this once, so it is set before any test runs.
    [ModuleInitializer]
    internal static void Initialize() => AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(10));
}
