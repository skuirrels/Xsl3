// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Xsl3.W3C.Qt3;

/// <summary>
/// A QT3 &lt;environment&gt;: source documents, parameters, namespaces and other static/dynamic context
/// a test needs. Declared in the catalogue or a test set and referenced by name, or inline in a test case.
/// </summary>
public sealed class TestEnvironment
{
    public sealed record Source(string Role, string File, string? Validation);

    public sealed record Param(string Name, string? Select, bool Declared);

    /// <summary>A document or text resource the test may fetch by URI (source/@uri, resource/@uri).</summary>
    public sealed record Resource(string Uri, string File);

    private TestEnvironment(XElement element, string baseDirectory)
    {
        Element = element;
        BaseDirectory = baseDirectory;
        Name = (string?)element.Attribute("name");
        Sources = element.Elements(Catalog.Ns + "source")
            .Select(s => new Source((string?)s.Attribute("role") ?? "", Path.Combine(baseDirectory, (string)s.Attribute("file")!), (string?)s.Attribute("validation")))
            .ToList();
        Params = element.Elements(Catalog.Ns + "param")
            .Select(p => new Param((string)p.Attribute("name")!, (string?)p.Attribute("select"), (string?)p.Attribute("declared") == "true"))
            .ToList();
        Namespaces = element.Elements(Catalog.Ns + "namespace")
            .Select(n => ((string)n.Attribute("prefix")!, (string)n.Attribute("uri")!))
            .ToList();
        ContextItem = (string?)element.Element(Catalog.Ns + "context-item")?.Attribute("select");
        Resources = element.Elements().Where(e => e.Name == Catalog.Ns + "source" || e.Name == Catalog.Ns + "resource")
            .Where(e => e.Attribute("uri") != null && e.Attribute("file") != null)
            .Select(e => new Resource((string)e.Attribute("uri")!, Path.Combine(baseDirectory, (string)e.Attribute("file")!)))
            .ToList();
        StaticBaseUri = (string?)element.Element(Catalog.Ns + "static-base-uri")?.Attribute("uri");
    }

    public XElement Element { get; }

    public string BaseDirectory { get; }

    public string? Name { get; }

    public IReadOnlyList<Source> Sources { get; }

    public IReadOnlyList<Param> Params { get; }

    public IReadOnlyList<(string Prefix, string Uri)> Namespaces { get; }

    public IReadOnlyList<Resource> Resources { get; }

    /// <summary>&lt;static-base-uri uri="…"/&gt;; "#UNDEFINED" means absent.</summary>
    public string? StaticBaseUri { get; }

    /// <summary>&lt;decimal-format&gt; declarations: the unnamed one has no name attribute.</summary>
    public IEnumerable<XElement> DecimalFormats => Element.Elements(Catalog.Ns + "decimal-format");

    /// <summary>A context item given as an expression (&lt;context-item select="…"/&gt;).</summary>
    public string? ContextItem { get; }

    /// <summary>Elements of the environment that the runner can't honour yet; tests that rely on them may fail.</summary>
    public IEnumerable<string> UnsupportedParts =>
        Element.Elements().Select(e => e.Name.LocalName)
            .Where(n => n is "schema" or "collation" or "collection")
            .Distinct();

    public static TestEnvironment Read(XElement element, string baseDirectory) => new(element, baseDirectory);

    public static readonly TestEnvironment Empty = new(new XElement(Catalog.Ns + "environment", new XAttribute("name", "empty")), "");
}
