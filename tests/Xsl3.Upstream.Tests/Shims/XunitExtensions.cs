// Licensed under the MIT license. See LICENSE.TXT in the repository root.

#nullable enable

// Stand-ins for the parts of dotnet/arcade's Microsoft.DotNet.XUnitExtensions and TestUtilities that the
// ported XSLT tests use. Those packages are only published to dotnet's internal feeds. Semantics follow
// arcade: conditions skip a test when false, and platform/outer-loop/active-issue attributes become
// "category" traits that the test run filters on (see eng/test.ps1).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace System
{
    public static class PlatformDetection
    {
        public static bool IsWindows => OperatingSystem.IsWindows();
        public static bool IsiOS => OperatingSystem.IsIOS();
        public static bool IstvOS => OperatingSystem.IsTvOS();
        public static bool IsAppleMobile => OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst();
        public static bool IsCoreCLR => !IsMonoRuntime && !IsNativeAot;
        public static bool IsMonoRuntime => Type.GetType("Mono.RuntimeStructs") != null;
        public static bool IsNativeAot => !RuntimeFeature.IsDynamicCodeSupported && !IsMonoRuntime;
        public static bool IsMultithreadingSupported => !OperatingSystem.IsBrowser() && !OperatingSystem.IsWasi();
        public static bool IsReflectionEmitSupported => RuntimeFeature.IsDynamicCodeSupported;
        public static bool IsInAppContainer => false;

        // Same probe as arcade: create a file and look for it under a different case.
        public static bool FileCreateCaseSensitive { get; } = GetFileCreateCaseSensitive();

        private static bool GetFileCreateCaseSensitive()
        {
            string path = IO.Path.Combine(IO.Path.GetTempPath(), "CaseSensitive" + Guid.NewGuid().ToString("N"));
            IO.File.WriteAllText(path, string.Empty);
            try
            {
                return !IO.File.Exists(path.ToLowerInvariant());
            }
            finally
            {
                IO.File.Delete(path);
            }
        }
    }

    namespace Tests
    {
        // Upstream XslCompilerTests imports this namespace; nothing it needs from it is used here.
        internal static class NamespacePlaceholder { }
    }
}

namespace Xunit
{
    [Flags]
    public enum TestPlatforms
    {
        Windows = 1,
        Linux = 2,
        OSX = 4,
        FreeBSD = 8,
        NetBSD = 16,
        illumos = 32,
        Solaris = 64,
        iOS = 128,
        tvOS = 256,
        Android = 512,
        Browser = 1024,
        MacCatalyst = 2048,
        LinuxBionic = 4096,
        Wasi = 8192,
        Haiku = 16384,
        AnyUnix = FreeBSD | Linux | NetBSD | OSX | illumos | Solaris | iOS | tvOS | MacCatalyst | Android | Browser | LinuxBionic | Wasi | Haiku,
        Any = ~0,
    }

    internal static class Conditions
    {
        public static string? Evaluate(Type calleeType, string[] conditionMemberNames)
        {
            var failed = conditionMemberNames.Where(name => !Evaluate(calleeType, name)).ToArray();
            return failed.Length == 0 ? null : $"Condition(s) not met: {string.Join(", ", failed.Select(n => calleeType.Name + "." + n))}";
        }

        private static bool Evaluate(Type type, string name)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            for (Type? t = type; t != null; t = t.BaseType)
            {
                if (t.GetProperty(name, Flags) is PropertyInfo p) return (bool)p.GetValue(null)!;
                if (t.GetField(name, Flags) is FieldInfo f) return (bool)f.GetValue(null)!;
                if (t.GetMethod(name, Flags, Type.EmptyTypes) is MethodInfo m) return (bool)m.Invoke(null, null)!;
            }
            throw new InvalidOperationException($"Condition member {type.Name}.{name} not found.");
        }

        public static bool IsCurrent(TestPlatforms platforms) =>
            (platforms.HasFlag(TestPlatforms.Windows) && OperatingSystem.IsWindows()) ||
            (platforms.HasFlag(TestPlatforms.Linux) && OperatingSystem.IsLinux()) ||
            (platforms.HasFlag(TestPlatforms.OSX) && OperatingSystem.IsMacOS()) ||
            (platforms.HasFlag(TestPlatforms.FreeBSD) && OperatingSystem.IsFreeBSD()) ||
            (platforms.HasFlag(TestPlatforms.iOS) && OperatingSystem.IsIOS()) ||
            (platforms.HasFlag(TestPlatforms.tvOS) && OperatingSystem.IsTvOS()) ||
            (platforms.HasFlag(TestPlatforms.Android) && OperatingSystem.IsAndroid()) ||
            (platforms.HasFlag(TestPlatforms.Browser) && OperatingSystem.IsBrowser()) ||
            (platforms.HasFlag(TestPlatforms.MacCatalyst) && OperatingSystem.IsMacCatalyst()) ||
            (platforms.HasFlag(TestPlatforms.Wasi) && OperatingSystem.IsWasi());
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [XunitTestCaseDiscoverer("Xunit.Sdk.FactDiscoverer", "xunit.execution.{Platform}")]
    public sealed class ConditionalFactAttribute : FactAttribute
    {
        public ConditionalFactAttribute(Type calleeType, params string[] conditionMemberNames)
        {
            Skip = Conditions.Evaluate(calleeType, conditionMemberNames);
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [XunitTestCaseDiscoverer("Xunit.Sdk.TheoryDiscoverer", "xunit.execution.{Platform}")]
    public sealed class ConditionalTheoryAttribute : TheoryAttribute
    {
        public ConditionalTheoryAttribute(Type calleeType, params string[] conditionMemberNames)
        {
            Skip = Conditions.Evaluate(calleeType, conditionMemberNames);
        }
    }

    /// <summary>
    /// Arcade skips every test in the class when a condition is false. xunit v2 has no class-level skip
    /// without a custom framework, so here a false condition fails loudly instead of silently running.
    /// Every condition the XSLT tests use holds on the platforms Xsl3 runs its tests on.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class ConditionalClassAttribute : Attribute
    {
        public ConditionalClassAttribute(Type calleeType, params string[] conditionMemberNames)
        {
            string? failure = Conditions.Evaluate(calleeType, conditionMemberNames);
            if (failure != null)
            {
                throw new PlatformNotSupportedException(failure);
            }
        }
    }

    [TraitDiscoverer("Xunit.CategoryTraitDiscoverer", "Xsl3.Upstream.Tests")]
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class PlatformSpecificAttribute : Attribute, ITraitAttribute
    {
        public PlatformSpecificAttribute(TestPlatforms platforms) => Category = Conditions.IsCurrent(platforms) ? null : "failing";

        public string? Category { get; }
    }

    [TraitDiscoverer("Xunit.CategoryTraitDiscoverer", "Xsl3.Upstream.Tests")]
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class OuterLoopAttribute : Attribute, ITraitAttribute
    {
        public OuterLoopAttribute(string? reason = null) { }

        public string? Category => "OuterLoop";
    }

    [TraitDiscoverer("Xunit.CategoryTraitDiscoverer", "Xsl3.Upstream.Tests")]
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class ActiveIssueAttribute : Attribute, ITraitAttribute
    {
        public ActiveIssueAttribute(string issue) => Category = "failing";

        public ActiveIssueAttribute(string issue, Type calleeType, params string[] conditionMemberNames) =>
            Category = Conditions.Evaluate(calleeType, conditionMemberNames) == null ? "failing" : null;

        public ActiveIssueAttribute(string issue, TestPlatforms platforms) => Category = Conditions.IsCurrent(platforms) ? "failing" : null;

        public string? Category { get; }
    }

    public sealed class CategoryTraitDiscoverer : ITraitDiscoverer
    {
        public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
        {
            string? category = traitAttribute.GetNamedArgument<string?>("Category");
            if (category != null)
            {
                yield return new KeyValuePair<string, string>("category", category);
            }
        }
    }
}
