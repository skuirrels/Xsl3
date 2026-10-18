// Licensed under the MIT license. See LICENSE.TXT in the repository root.

// Upstream compiles Common/tests/TestUtilities/System/DisableParallelizationPerAssembly.cs into this
// test assembly: the XSLT tests share output file names.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Xsl3.Upstream.Tests
{
    internal static class TestSwitches
    {
        // Tests toggle Switch.System.Xml.AllowDefaultResolver around individual cases, which only works when
        // switch values are not cached. Upstream sets this in the test runtimeconfig; XsltApiV2 also sets it in
        // its constructor, but a test class that reads the switch earlier would cache "false" for the whole run.
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void DisableSwitchCaching() => System.AppContext.SetSwitch("TestSwitch.LocalAppContext.DisableCaching", true);
    }
}
