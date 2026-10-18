// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// APIs that upstream code uses but that are newer than Xsl3's oldest target framework.

#if !NET9_0_OR_GREATER
namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>Polyfill of the .NET 9 attribute. The .NET 8 trimmer ignores it, so it only documents intent there.</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    internal sealed class FeatureSwitchDefinitionAttribute : Attribute
    {
        public FeatureSwitchDefinitionAttribute(string switchName) => SwitchName = switchName;

        public string SwitchName { get; }
    }
}
#endif
