// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml;
using Xsl3.Tests;

if (args.Length != 0) { Console.Error.WriteLine("Usage: identity-probe"); return 64; }
int passed = 0, failed = 0;
foreach (var (declaration, validation) in CopiedAttributeIdentityFixtures.Contracts())
foreach (bool forward in new[] { false, true })
foreach (bool debug in new[] { false, true })
{
    string name = CopiedAttributeIdentityFixtures.StyleName(declaration, validation, forward, debug);
    // Enumerate the required matrix independently of generated bindings: a missing saved
    // style must fail instead of silently reducing the number of tested contracts.
    var transform = SavedIdentity.Load(name);
    foreach (bool declared in new[] { false, true })
    foreach (bool positioned in new[] { false, true })
    foreach (var (target, other) in new[] { ("target", "other"), ("second", "third") })
    {
        string scenario = $"{name} declared={declared} positioned={positioned} target={target}";
        try
        {
            using var input = CopiedAttributeIdentityFixtures.Reader(declaration, declared, positioned, target, other);
            using var output = new StringWriter();
            transform.Transform(input, null, output);
            string expected = CopiedAttributeIdentityFixtures.Expected(declaration, target, other);
            if (output.ToString() != expected) throw new InvalidOperationException($"Expected: {expected}\nActual: {output}");
            if (input.ReadState == ReadState.Closed) throw new InvalidOperationException("The borrowed reader was closed");
            Console.WriteLine("PASS " + scenario);
            passed++;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL {scenario}: {error}");
            failed++;
        }
    }
}
Console.WriteLine($"{RuntimeInformation.FrameworkDescription}: passed={passed} failed={failed}");
return failed == 0 && passed != 0 ? 0 : 1;
