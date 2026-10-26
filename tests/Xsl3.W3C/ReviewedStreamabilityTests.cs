// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Xunit;
using Catalog = Xsl3.W3C.Xslt30.Catalog;

namespace Xsl3.W3C;

public class ReviewedStreamabilityTests
{
    private const string SetFile = "tests/sandp/_example-test-set.xml";

    [Fact]
    public void PreservesOriginalPartsAndBindingsWithoutChangingTheSource()
    {
        WithReview((source, prepared, manifest) =>
        {
            byte[] original = File.ReadAllBytes(Path.Combine(source, SetFile));
            Catalog reviewed = Catalog.LoadReviewed(source, prepared);
            Assert.Equal(2, reviewed.TestCases.Count());
            Assert.Empty(reviewed.RetiredAnalysisCases);
            Assert.Empty(reviewed.ReviewedOriginalCodes);
            Assert.NotNull(reviewed.ReviewManifestHash);
            Assert.All(reviewed.TestCases, test => Assert.Equal("urn:source", test.TestElement.Descendants(Catalog.Ns + "xpath").Single().GetNamespaceOfPrefix("ex")!.NamespaceName));
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(source, SetFile)));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsCorruptedSourceOrGeneratedFiles(bool sourceFile)
    {
        WithReview((source, prepared, manifest) =>
        {
            File.AppendAllText(Path.Combine(sourceFile ? source : prepared, SetFile), " ");
            Assert.Throws<InvalidDataException>(() => Catalog.LoadReviewed(source, prepared));
        });
    }

    [Fact]
    public void CannotDropPartsEvenWhenHashesAndPopulationMetadataAreUpdated()
    {
        WithReview((source, prepared, manifest) =>
        {
            string path = Path.Combine(prepared, SetFile);
            XElement set = XElement.Load(path);
            set.Elements(Catalog.Ns + "test-case").Last().Remove();
            set.Save(path);
            manifest["Mappings"]![0]!["Groups"]!.AsArray().RemoveAt(1);
            manifest["ReviewedCases"] = 1;
            manifest["Expressions"] = 1;
            Save(prepared, manifest);
            Assert.Throws<InvalidDataException>(() => Catalog.LoadReviewed(source, prepared));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CannotChangeExpressionMeaningByUpdatingItsHash(bool namespaceBinding)
    {
        WithReview((source, prepared, manifest) =>
        {
            string path = Path.Combine(prepared, SetFile);
            XElement set = XElement.Load(path);
            if (namespaceBinding) set.SetAttributeValue(XNamespace.Xmlns + "ex", "urn:changed");
            else set.Descendants(Catalog.Ns + "xpath").First().Value = "ex:b/@code";
            set.Save(path);
            Save(prepared, manifest);
            Assert.Throws<InvalidDataException>(() => Catalog.LoadReviewed(source, prepared));
        });
    }

    [Fact]
    public void CannotRetireANonemptySourceCase()
    {
        WithReview((source, prepared, manifest) =>
        {
            string path = Path.Combine(prepared, SetFile);
            XElement set = XElement.Load(path);
            set.Elements(Catalog.Ns + "test-case").Remove();
            set.Save(path);
            manifest["Mappings"]![0]!["Groups"] = new JsonArray();
            manifest["Mappings"]![0]!["InvalidReason"] = "pretend empty";
            manifest["InvalidCases"] = new JsonArray("example/source");
            manifest["ReviewedCases"] = 0;
            manifest["Expressions"] = 0;
            Save(prepared, manifest);
            Assert.Throws<InvalidDataException>(() => Catalog.LoadReviewed(source, prepared));
        });
    }

    [Fact]
    public void RejectsFixturePathsOutsideTheirRoot()
    {
        WithReview((source, prepared, manifest) =>
        {
            manifest["GeneratedHashes"]!["../raw/catalog.xml"] = Hash(Path.Combine(source, "catalog.xml"));
            File.WriteAllText(Path.Combine(prepared, "review.json"), manifest.ToJsonString());
            Assert.Throws<InvalidDataException>(() => Catalog.LoadReviewed(source, prepared));
        });
    }

    private static void WithReview(Action<string, string, JsonObject> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "xsl3-reviewed-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "raw"), prepared = Path.Combine(root, "reviewed");
        Directory.CreateDirectory(Path.Combine(source, "tests", "sandp"));
        Directory.CreateDirectory(Path.Combine(prepared, "tests", "sandp"));
        try
        {
            XNamespace ns = Catalog.Ns;
            File.WriteAllText(Path.Combine(source, "catalog.xml"), $"<catalog xmlns='{ns}'/>");
            string Case(string name, string parts, string posture) => $"<test-case name='{name}'><test><posture-and-sweep context-posture='striding'>{parts}</posture-and-sweep></test><result><assert-posture-and-sweep result-posture='{posture}' result-sweep='consuming'/></result></test-case>";
            const string first = "<xpath part='1'>ex:a/@code</xpath>", second = "<xpath part='2'>ex:a/..</xpath>";
            string Set(string cases) => $"<test-set xmlns='{ns}' xmlns:ex='urn:source' name='example'><dependencies><sweep_and_posture value='supports-sweep-and-posture-assessments'/></dependencies>{cases}</test-set>";
            File.WriteAllText(Path.Combine(source, SetFile), Set(Case("source", first + second, "climbing")));
            File.WriteAllText(Path.Combine(prepared, SetFile), Set(Case("source--striding", first, "striding") + Case("source--climbing", second, "climbing")));
            JsonObject manifest = JsonSerializer.SerializeToNode(new
            {
                Version = 1,
                SourceHashes = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["catalog.xml"] = Hash(Path.Combine(source, "catalog.xml")), [SetFile] = Hash(Path.Combine(source, SetFile)),
                },
                GeneratedHashes = new System.Collections.Generic.Dictionary<string, string> { [SetFile] = Hash(Path.Combine(prepared, SetFile)) },
                Sets = new[] { new { OriginalFile = SetFile, ReviewedFile = SetFile } },
                Mappings = new[] { new { OriginalId = "example/source", Groups = new[]
                {
                    new { ReviewedId = "example/source--striding", Parts = new[] { "1" }, Posture = "striding", Sweep = "consuming" },
                    new { ReviewedId = "example/source--climbing", Parts = new[] { "2" }, Posture = "climbing", Sweep = "consuming" },
                } } },
                OriginalCases = 1, ReviewedCases = 2, Expressions = 2, InvalidCases = Array.Empty<string>(), StaticCorrections = Array.Empty<object>(),
            })!.AsObject();
            Save(prepared, manifest);
            action(source, prepared, manifest);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Save(string prepared, JsonObject manifest)
    {
        manifest["GeneratedHashes"]![SetFile] = Hash(Path.Combine(prepared, SetFile));
        File.WriteAllText(Path.Combine(prepared, "review.json"), manifest.ToJsonString());
    }
}
