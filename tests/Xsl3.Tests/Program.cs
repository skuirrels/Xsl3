// Licensed under the MIT license. See LICENSE.TXT in the repository root.

namespace Xsl3.Tests;

/// <summary>
/// The test assembly's entry point. The test runner doesn't use it; tests that need a process of their own run the
/// assembly with an argument naming what to do.
/// </summary>
public static class Program
{
    public const string MemoryProbe = "memory-probe";

    public static int Main(string[] args)
    {
        if (args is [MemoryProbe])
        {
            CompactTreeMemoryTests.Probe();
            return 0;
        }
        return 1;
    }
}
