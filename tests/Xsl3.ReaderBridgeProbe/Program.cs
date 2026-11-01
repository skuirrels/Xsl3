// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Runtime.CompilerServices;
using Xsl3.Tests;

// PublishAot also disables the dynamic-code switch in the managed intermediate.
// Check the CoreCLR assembly list as well so that artifact cannot stand in for the native consumer.
if (RuntimeFeature.IsDynamicCodeSupported || AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") != null)
{
    Console.Error.WriteLine("Publish this reader bridge probe with NativeAOT before running it.");
    return 1;
}
Console.WriteLine($"Reader bridge contracts passed: {ReaderBridgeContracts.Verify()} checks; dynamic code supported: {RuntimeFeature.IsDynamicCodeSupported}");
return 0;
