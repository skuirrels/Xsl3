// Licensed under the MIT license. See LICENSE.TXT in the repository root.

// Upstream compiles Common/tests/TestUtilities/System/DisableParallelizationPerAssembly.cs into this
// test assembly: the XSLT tests share output file names.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
