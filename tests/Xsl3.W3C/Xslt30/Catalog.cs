// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Xsl3.W3C.Xslt30;

/// <summary>
/// The W3C XSLT 3.0 test suite catalogue (https://github.com/w3c/xslt30-test): test sets, test cases, their
/// dependencies and expected results. See the suite's admin/catalog-schema.xsd for the format.
/// </summary>
public sealed class Catalog
{
    public static readonly XNamespace Ns = "http://www.w3.org/2012/10/xslt-test-catalog";

    private Catalog(string root) => Root = root;

    public string Root { get; }

    public IReadOnlyList<TestSet> TestSets { get; private set; } = Array.Empty<TestSet>();

    public IReadOnlyDictionary<string, TestEnvironment> Environments { get; private set; } = new Dictionary<string, TestEnvironment>();

    public IEnumerable<TestCase> TestCases => TestSets.SelectMany(s => s.TestCases);

    /// <summary>
    /// The suite checkout: <c>$XSL3_XSLT30TESTS</c>, else <c>external/xslt30-test</c> above the test binaries
    /// (eng/fetch-w3c.sh puts it there). Null when absent, in which case these tests are skipped.
    /// </summary>
    public static string? Locate()
    {
        string? env = Environment.GetEnvironmentVariable("XSL3_XSLT30TESTS");
        if (!string.IsNullOrEmpty(env))
        {
            return File.Exists(Path.Combine(env, "catalog.xml")) ? env : null;
        }
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "external", "xslt30-test");
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
        var result = new Catalog(root);
        result.Environments = catalog.Elements(Ns + "environment").Where(e => e.Attribute("name") != null)
            .ToDictionary(e => (string)e.Attribute("name")!, e => TestEnvironment.Read(e, root));
        result.TestSets = catalog.Elements(Ns + "test-set")
            .Select(e => TestSet.Load(result, (string)e.Attribute("name")!, Path.Combine(root, (string)e.Attribute("file")!)))
            .ToList();
        return result;
    }
}

public sealed class TestSet
{
    private TestSet(Catalog catalog, string name, string file)
    {
        Catalog = catalog;
        Name = name;
        File = file;
    }

    public Catalog Catalog { get; }

    public string Name { get; }

    public string File { get; }

    public string Directory => Path.GetDirectoryName(File)!;

    public IReadOnlyList<Dependency> Dependencies { get; private set; } = Array.Empty<Dependency>();

    public IReadOnlyDictionary<string, TestEnvironment> Environments { get; private set; } = new Dictionary<string, TestEnvironment>();

    public IReadOnlyList<TestCase> TestCases { get; private set; } = Array.Empty<TestCase>();

    public TestEnvironment? FindEnvironment(string name) =>
        Environments.TryGetValue(name, out TestEnvironment? e) ? e : Catalog.Environments.GetValueOrDefault(name);

    internal static TestSet Load(Catalog catalog, string name, string file)
    {
        // Whitespace is kept: expected results may be whitespace-sensitive.
        XElement root = XDocument.Load(file, LoadOptions.PreserveWhitespace | LoadOptions.SetBaseUri).Root!;
        var set = new TestSet(catalog, name, file);
        set.Environments = root.Elements(Catalog.Ns + "environment").Where(e => e.Attribute("name") != null)
            .ToDictionary(e => (string)e.Attribute("name")!, e => TestEnvironment.Read(e, set.Directory));
        set.Dependencies = Dependency.Read(root.Element(Catalog.Ns + "dependencies"));
        set.TestCases = root.Elements(Catalog.Ns + "test-case").Select(e => TestCase.Read(set, e)).ToList();
        return set;
    }
}

public sealed class TestCase
{
    private TestCase(TestSet set, XElement element)
    {
        Set = set;
        Element = element;
        Name = (string)element.Attribute("name")!;
        Dependencies = Dependency.Read(element.Element(Catalog.Ns + "dependencies"));
        TestElement = element.Element(Catalog.Ns + "test")!;
        Result = element.Element(Catalog.Ns + "result")!.Elements().Single();
    }

    public TestSet Set { get; }

    public XElement Element { get; }

    public string Name { get; }

    /// <summary><c>set/case</c>: the key used in baselines and reports.</summary>
    public string Id => Set.Name + "/" + Name;

    public IReadOnlyList<Dependency> Dependencies { get; }

    /// <summary>The test case's dependencies; a kind it doesn't mention is inherited from the test set.</summary>
    public IEnumerable<Dependency> EffectiveDependencies =>
        Dependencies.Concat(Set.Dependencies.Where(s => !Dependencies.Any(d => d.Type == s.Type)));

    public XElement TestElement { get; }

    /// <summary>The <c>&lt;result&gt;</c> element's single child: the assertion tree.</summary>
    public XElement Result { get; }

    /// <summary>The environments: by reference, inline, or none (several may be combined).</summary>
    public IReadOnlyList<TestEnvironment?> Environments =>
        Element.Elements(Catalog.Ns + "environment")
            .Select(e => (string?)e.Attribute("ref") is string r ? Set.FindEnvironment(r) : TestEnvironment.Read(e, Set.Directory))
            .ToList();

    internal static TestCase Read(TestSet set, XElement element) => new(set, element);

    public override string ToString() => Id;
}

/// <summary>A dependency: the element name (<c>spec</c>, <c>feature</c>, …), its value and whether it must be satisfied.</summary>
public sealed record Dependency(string Type, string Value, bool Satisfied)
{
    internal static IReadOnlyList<Dependency> Read(XElement? dependencies) =>
        dependencies == null
            ? Array.Empty<Dependency>()
            : dependencies.Elements()
                .Select(d => new Dependency(d.Name.LocalName, ((string?)d.Attribute("value") ?? "").Trim(), (string?)d.Attribute("satisfied") != "false"))
                .ToList();
}

/// <summary>An &lt;environment&gt;: source documents, parameters, collections and other context.</summary>
public sealed class TestEnvironment
{
    public sealed record Source(string? Role, string? File, string? Content, string? Uri, string? Validation, bool DefinesStylesheet, string? Select = null);

    public sealed record Param(string Name, string? Select, string? As, bool Static, XElement Element);

    /// <summary>A schema the environment supplies (P6): for validating sources, or for xsl:import-schema to find by namespace.</summary>
    public sealed record Schema(string? File, string? Uri, string? Role, string XsdVersion);

    /// <summary>Whether the runner is schema-aware (the sa profile, XSL3_XSLT30_PROFILE=sa): it then honours schemas and validation.</summary>
    public static bool SchemaAware { get; } = Environment.GetEnvironmentVariable("XSL3_XSLT30_PROFILE") == "sa";

    private TestEnvironment(XElement element, string baseDirectory)
    {
        Element = element;
        BaseDirectory = baseDirectory;
        Sources = element.Elements(Catalog.Ns + "source").Select(s => new Source(
            (string?)s.Attribute("role"),
            (string?)s.Attribute("file") is string f ? Path.Combine(baseDirectory, f) : null,
            s.Element(Catalog.Ns + "content")?.Value,
            (string?)s.Attribute("uri"),
            // (In the sa profile, a source of an environment whose schema the sources reference is validated laxly.)
            (string?)s.Attribute("validation") ?? (SchemaAware && element.Elements(Catalog.Ns + "schema").Any(x => (string?)x.Attribute("role") == "source-reference") ? "lax" : null),
            (string?)s.Attribute("defines-stylesheet") is "true" or "yes",
            (string?)s.Attribute("select"))).ToList();
        Params = element.Elements(Catalog.Ns + "param").Select(Param0).ToList();
        Schemas = element.Elements(Catalog.Ns + "schema").Select(s => new Schema(
            (string?)s.Attribute("file") is string f ? Path.Combine(baseDirectory, f) : null,
            (string?)s.Attribute("uri"),
            (string?)s.Attribute("role"),
            ((string?)s.Attribute("xsd-version") ?? "1.0").Trim())).ToList();
        Stylesheets = element.Elements(Catalog.Ns + "stylesheet").Concat(element.Elements(Catalog.Ns + "package").Where(p => (string?)p.Attribute("role") == "principal"))
            .Where(s => (string?)s.Attribute("role") is null or "principal" && s.Attribute("file") != null)
            .Select(s => Path.Combine(baseDirectory, (string)s.Attribute("file")!)).ToList();
        Packages = SecondaryPackages(element, baseDirectory);
    }

    /// <summary>The packages (other than the principal one) an environment or test supplies for xsl:use-package.</summary>
    // The package's name and version are its xsl:package's (the catalog's uri and package-version otherwise).
    internal static List<(string Name, string Version, Uri Location)> SecondaryPackages(XElement element, string baseDirectory)
    {
        var packages = new List<(string, string, Uri)>();
        foreach (XElement p in element.Elements(Catalog.Ns + "package"))
        {
            if ((string?)p.Attribute("role") == "principal" || p.Attribute("file") == null)
            {
                continue;
            }
            string path = Path.GetFullPath(Path.Combine(baseDirectory, (string)p.Attribute("file")!));
            string? name = (string?)p.Attribute("uri"), version = (string?)p.Attribute("package-version");
            try
            {
                XElement root = XDocument.Load(path).Root!;
                name = (string?)root.Attribute("name") ?? name;
                version = (string?)root.Attribute("package-version") ?? version;
            }
            catch (Exception)
            {
                // unreadable: the catalog's values
            }
            if (name != null)
            {
                packages.Add((name.Trim(), (version ?? "1").Trim(), new Uri(path)));
            }
        }
        return packages;
    }

    public IReadOnlyList<(string Name, string Version, Uri Location)> Packages { get; }

    /// <summary>The principal stylesheet the environment supplies to tests that don't name their own.</summary>
    public IReadOnlyList<string> Stylesheets { get; }

    internal static Param Param0(XElement p) =>
        new((string)p.Attribute("name")!, (string?)p.Attribute("select"), (string?)p.Attribute("as"), (string?)p.Attribute("static") is "yes" or "true", p);

    public XElement Element { get; }

    public string BaseDirectory { get; }

    public IReadOnlyList<Source> Sources { get; }

    public IReadOnlyList<Param> Params { get; }

    /// <summary>Parts of the environment the runner can't honour yet; tests that use them aren't run.</summary>
    public IEnumerable<string> UnsupportedParts =>
        Element.Elements().Select(e => e.Name.LocalName).Where(n => n is "collection" || n == "schema" && !SchemaAware)
            .Concat(Schemas.Where(s => SchemaAware && (s.XsdVersion != "1.0" || s.File == null)).Select(s => s.File == null ? "schema without a file" : "schema: XSD " + s.XsdVersion))
            .Concat(Sources.Where(s => !SchemaAware && s.Validation is "strict" or "lax").Select(_ => "validation"))
            .Distinct();

    public IReadOnlyList<Schema> Schemas { get; }

    public static TestEnvironment Read(XElement element, string baseDirectory) => new(element, baseDirectory);
}
