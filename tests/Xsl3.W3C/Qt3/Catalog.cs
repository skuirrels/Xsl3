// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Xsl3.W3C.Qt3;

/// <summary>
/// The W3C QT3 test suite catalogue (https://github.com/w3c/qt3tests): test sets, test cases, their
/// dependencies and expected results. See the suite's catalog-schema.html for the format.
/// </summary>
public sealed class Catalog
{
    public static readonly XNamespace Ns = "http://www.w3.org/2010/09/qt-fots-catalog";

    private Catalog(string root, IReadOnlyList<TestSet> testSets, IReadOnlyDictionary<string, TestEnvironment> environments)
    {
        Root = root;
        TestSets = testSets;
        Environments = environments;
    }

    /// <summary>The environments declared in catalog.xml, shared by all test sets.</summary>
    public IReadOnlyDictionary<string, TestEnvironment> Environments { get; }

    public string Root { get; }

    public IReadOnlyList<TestSet> TestSets { get; }

    public IEnumerable<TestCase> TestCases => TestSets.SelectMany(s => s.TestCases);

    /// <summary>
    /// The suite checkout: <c>$XSL3_QT3TESTS</c>, else <c>external/qt3tests</c> above the test binaries
    /// (eng/fetch-w3c.sh puts it there). Null when absent, in which case the W3C tests are skipped.
    /// </summary>
    public static string? Locate()
    {
        string? env = Environment.GetEnvironmentVariable("XSL3_QT3TESTS");
        if (!string.IsNullOrEmpty(env))
        {
            return File.Exists(Path.Combine(env, "catalog.xml")) ? env : null;
        }
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "external", "qt3tests");
            if (File.Exists(Path.Combine(candidate, "catalog.xml")))
            {
                return candidate;
            }
        }
        return null;
    }

    public static Catalog Load(string root)
    {
        XElement catalog = XDocument.Load(Path.Combine(root, "catalog.xml")).Root!;
        var environments = catalog.Elements(Ns + "environment").ToDictionary(e => (string)e.Attribute("name")!, e => TestEnvironment.Read(e, root));
        var result = new Catalog(root, new List<TestSet>(), environments);
        var sets = (List<TestSet>)result.TestSets;
        foreach (XElement entry in catalog.Elements(Ns + "test-set"))
        {
            string file = Path.Combine(root, (string)entry.Attribute("file")!);
            sets.Add(TestSet.Load(result, (string)entry.Attribute("name")!, file));
        }
        return result;
    }
}

public sealed class TestSet
{
    private TestSet(Catalog catalog, string name, string file, IReadOnlyList<Dependency> dependencies, IReadOnlyDictionary<string, TestEnvironment> environments)
    {
        Catalog = catalog;
        Name = name;
        File = file;
        Dependencies = dependencies;
        Environments = environments;
    }

    public Catalog Catalog { get; }

    /// <summary>Environments declared in this test set; they take precedence over the catalogue's.</summary>
    public IReadOnlyDictionary<string, TestEnvironment> Environments { get; }

    public TestEnvironment? FindEnvironment(string name) =>
        Environments.TryGetValue(name, out TestEnvironment? e) ? e : Catalog.Environments.GetValueOrDefault(name);

    public string Name { get; }

    public string File { get; }

    public IReadOnlyList<Dependency> Dependencies { get; }

    public IReadOnlyList<TestCase> TestCases { get; private set; } = Array.Empty<TestCase>();

    internal static TestSet Load(Catalog catalog, string name, string file)
    {
        // Whitespace is kept: an expected string value may be whitespace only (codepoints-to-string(13)).
        XElement root = XDocument.Load(file, LoadOptions.PreserveWhitespace).Root!;
        string dir = Path.GetDirectoryName(file)!;
        var environments = root.Elements(Catalog.Ns + "environment").Where(e => e.Attribute("name") != null)
            .ToDictionary(e => (string)e.Attribute("name")!, e => TestEnvironment.Read(e, dir));
        var set = new TestSet(catalog, name, file, Dependency.Read(root), environments);
        set.TestCases = root.Elements(Catalog.Ns + "test-case").Select(e => TestCase.Read(set, e)).ToList();
        return set;
    }
}

public sealed class TestCase
{
    private TestCase(TestSet set, string name, IReadOnlyList<Dependency> dependencies, XElement test, XElement result, XElement element)
    {
        Set = set;
        Name = name;
        Dependencies = dependencies;
        TestElement = test;
        Result = result;
        Element = element;
    }

    public TestSet Set { get; }

    public string Name { get; }

    /// <summary><c>set/case</c>: the key used in baselines and reports.</summary>
    public string Id => Set.Name + "/" + Name;

    /// <summary>The test case's own dependencies. A dependency type it doesn't mention is inherited from the set.</summary>
    public IReadOnlyList<Dependency> Dependencies { get; }

    public IEnumerable<Dependency> EffectiveDependencies =>
        Dependencies.Concat(Set.Dependencies.Where(s => !Dependencies.Any(d => d.Type == s.Type)));

    public XElement TestElement { get; }

    /// <summary>The <c>&lt;result&gt;</c> element's single child: the assertion tree.</summary>
    public XElement Result { get; }

    public XElement Element { get; }

    /// <summary>The test's environment: inline, by reference, or the empty environment. Null if a reference doesn't resolve.</summary>
    public TestEnvironment? Environment
    {
        get
        {
            XElement? env = Element.Element(Catalog.Ns + "environment");
            if (env == null)
            {
                return TestEnvironment.Empty;
            }
            string? reference = (string?)env.Attribute("ref");
            return reference != null ? Set.FindEnvironment(reference) : TestEnvironment.Read(env, Path.GetDirectoryName(Set.File)!);
        }
    }

    /// <summary>The expression: inline text, or the file named by <c>&lt;test file="…"&gt;</c>.</summary>
    public string Expression
    {
        get
        {
            string? file = (string?)TestElement.Attribute("file");
            return file != null
                ? System.IO.File.ReadAllText(Path.Combine(Path.GetDirectoryName(Set.File)!, file))
                : TestElement.Value;
        }
    }

    /// <summary>The error codes this test accepts, if every acceptable outcome is an error.</summary>
    public IReadOnlyList<string>? ExpectedErrorsOnly()
    {
        IEnumerable<XElement> options = Result.Name.LocalName == "any-of" ? Result.Elements() : new[] { Result };
        var codes = new List<string>();
        foreach (XElement option in options)
        {
            if (option.Name.LocalName != "error")
            {
                return null;
            }
            codes.Add((string)option.Attribute("code")!);
        }
        return codes;
    }

    /// <summary>Whether any acceptable outcome is the error <paramref name="code"/> (or a wildcard error).</summary>
    public bool AcceptsError(string code) =>
        Result.DescendantsAndSelf(Catalog.Ns + "error").Any(e => (string?)e.Attribute("code") is var c && (c == code || c == "*"));

    internal static TestCase Read(TestSet set, XElement element) =>
        new(set, (string)element.Attribute("name")!, Dependency.Read(element),
            element.Element(Catalog.Ns + "test")!, element.Element(Catalog.Ns + "result")!.Elements().Single(), element);

    public override string ToString() => Id;
}

/// <summary>A <c>&lt;dependency type="…" value="…" satisfied="…"/&gt;</c>.</summary>
public sealed record Dependency(string Type, string Value, bool Satisfied)
{
    internal static IReadOnlyList<Dependency> Read(XElement parent) =>
        parent.Elements(Catalog.Ns + "dependency")
            .Select(d => new Dependency((string)d.Attribute("type")!, (string)d.Attribute("value")!, (string?)d.Attribute("satisfied") != "false"))
            .ToList();
}
