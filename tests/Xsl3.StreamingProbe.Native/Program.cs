// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Runtime.CompilerServices;
using Xsl3.Tests;

// The AOT SDK also sets IsDynamicCodeSupported=false in the managed intermediate's
// runtimeconfig. CoreCLR's trusted-platform-assembly list still identifies that host.
if (RuntimeFeature.IsDynamicCodeSupported || AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") != null)
{
    Console.Error.WriteLine("This probe must be published with NativeAOT; JIT execution is not a native measurement.");
    return 1;
}
if (args.Length == 0 || args[0] != "stream-probe") return 64;
return StreamingProbe.RunCommand(args[1..]);
