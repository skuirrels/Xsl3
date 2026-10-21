// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.W3C;

/// <summary>
/// Real 2.0/3.0 stylesheets (remaining-scope 1.3): the TEI Stylesheets' HTML, LaTeX and text conversions against the
/// output their own test suite expects (Test/expected-results, made with Saxon). Compared as text: markup is compared
/// as the words it shows, dates (the run's own) don't count. Fetched by eng/fetch-bench.sh; skipped without it.
/// </summary>
public class TeiStylesheetTests
{
    private readonly ITestOutputHelper _output;

    public TeiStylesheetTests(ITestOutputHelper output) => _output = output;

    // The conversions of the TEI test suite's Makefile that have an expected result: input, stylesheet, expected file.
    public static TheoryData<string, string, string> Conversions => new()
    {
        { "test.xml", "html/html.xsl", "test.html" },
        { "test20.xml", "html/html.xsl", "test20.html" },
        { "test.xml", "latex/latex.xsl", "test.tex" },
        { "test5.xml", "latex/latex.xsl", "test5.tex" },
        { "test20.xml", "latex/latex.xsl", "test20.tex" },
        { "testnotes1.xml", "latex/latex.xsl", "testnotes1.tex" },
        { "test14.xml", "txt/tei-to-text.xsl", "test.text" },
    };

    internal static string? Locate()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "external", "bench", "tei");
            if (File.Exists(Path.Combine(candidate, "html", "html.xsl")))
            {
                return candidate;
            }
        }
        return null;
    }

    [Theory]
    [MemberData(nameof(Conversions))]
    public void ConversionGivesTheExpectedText(string input, string stylesheet, string expected)
    {
        string? root = Locate();
        if (root == null)
        {
            _output.WriteLine("TEI Stylesheets not found (run eng/fetch-bench.sh); skipping.");
            return;
        }
        var transform = new XslCompiledTransform();
        var resolver = new XmlUrlResolver();
        using (XmlReader reader = XmlReader.Create(Path.Combine(root, stylesheet), new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse }))
        {
            transform.Load(reader, new XsltSettings(enableDocumentFunction: true, enableScript: false), resolver);
        }
        var text = new StringWriter();
        using (XmlReader source = XmlReader.Create(Path.Combine(root, "Test", input), new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse }))
        {
            transform.Transform(source, null, text, resolver);
        }
        bool markup = expected.EndsWith(".html", StringComparison.Ordinal);
        string[] actual = Words(text.ToString(), markup), wanted = Words(File.ReadAllText(Path.Combine(root, "Test", "expected-results", expected)), markup);
        int at = Enumerable.Range(0, Math.Min(actual.Length, wanted.Length)).FirstOrDefault(i => actual[i] != wanted[i], -1);
        Assert.True(at < 0 && actual.Length == wanted.Length,
            $"{input} with {stylesheet} differs from {expected} at word {at}: '{string.Join(' ', actual.Skip(Math.Max(0, at - 5)).Take(12))}' " +
            $"against '{string.Join(' ', wanted.Skip(Math.Max(0, at - 5)).Take(12))}' ({actual.Length} words against {wanted.Length})");
    }

    // The words of a result: for markup, the text it shows (no head, no scripts); dates and "(revised: date)", which
    // depend on the day of the run, as one placeholder.
    private static string[] Words(string s, bool markup)
    {
        if (markup)
        {
            s = Regex.Replace(s, "(?s)<head.*?</head>|<script.*?</script>", " ");
            s = WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", " "));
        }
        s = Regex.Replace(s, @"\d{4}-\d{2}-\d{2}(T[\d:.]+Z?)?", "DATE");
        s = s.Replace(" (revised: DATE)", "", StringComparison.Ordinal);
        return s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }
}
