// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Xml;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.W3C;

/// <summary>
/// Real 3.0 stylesheets (remaining-scope 1.3): DocBook xslTNG 2.7.1's HTML conversion of its sample article and of
/// docbook/book.xml, a book that uses the common elements (lists, sections, a table, a program listing, cross
/// references, a footnote, an index). There is no expected output to compare with (the project's tests run on
/// Saxon), so the test checks the structure the stylesheets make. Fetched by eng/fetch-bench.sh; skipped without it.
/// </summary>
public class DocBookStylesheetTests
{
    private readonly ITestOutputHelper _output;

    public DocBookStylesheetTests(ITestOutputHelper output) => _output = output;

    private static readonly Lazy<XslCompiledTransform?> s_transform = new(() =>
    {
        string? xslt = Locate();
        if (xslt == null)
        {
            return null;
        }
        // DocBook runs its pipeline with fn:transform, and reads its localizations with document().
        var transform = new XslCompiledTransform();
        transform.Load(xslt, new XsltSettings(enableDocumentFunction: true, enableScript: false) { EnableEvaluate = true }, new XmlUrlResolver());
        return transform;
    });

    internal static string? Locate()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "external", "bench", "docbook", "docbook-xslTNG-nosaxon-2.7.1", "xslt", "docbook.xsl");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private string? Run(string input)
    {
        XslCompiledTransform? transform = s_transform.Value;
        if (transform == null)
        {
            _output.WriteLine("DocBook xslTNG not found (run eng/fetch-bench.sh); skipping.");
            return null;
        }
        var text = new StringWriter();
        using (XmlReader source = XmlReader.Create(input))
        {
            transform.Transform(source, null, text, new XmlUrlResolver());
        }
        return text.ToString();
    }

    private static string InThisDirectory(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "Xsl3.W3C", "docbook", file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException(file);
    }

    [Fact]
    public void SampleArticle()
    {
        string? xslt = Locate();
        string? html = xslt == null ? Run("") : Run(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(xslt))!, "samples", "article.xml"));
        if (html == null)
        {
            return;
        }
        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("<title>Sample Article</title>", html, StringComparison.Ordinal);
        Assert.Contains("<header><h1>Sample Article</h1></header>", html, StringComparison.Ordinal);
        Assert.Contains("<span id=\"docbook\" class=\"phrase\">DocBook</span>", html, StringComparison.Ordinal);
        Assert.Contains("<q id=\"smoketest\">smoke\ntest</q>", html.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void Book()
    {
        string? html = Locate() == null ? Run("") : Run(InThisDirectory("book.xml"));
        if (html == null)
        {
            return;
        }
        foreach (string expected in new[]
        {
            "<h1>Xsl3 Test Book</h1>",
            // The table of contents and the lists of tables and examples (the separator is a no-break space).
            "<a href=\"#preface\">Preface</a>",
            "<a href=\"#lists\"><span class=\"label\">1</span><span class=\"sep\">.\u00A0</span>Lists</a>",
            "<a href=\"#appendix\"><span class=\"label\">A</span><span class=\"sep\">.\u00A0</span>An appendix</a>",
            "<div class=\"title\">List of Tables</div>",
            // Cross references, by the chapter's label and title.
            "<a href=\"#tables\" class=\"xref xref-chapter\">Chapter\u00A0<span class=\"label\">2</span>, Tables and listings</a>",
            // Lists, the table and the program listing.
            "<ol class=\"orderedlist\" type=\"a\">",
            "<dt><span class=\"term\">Term</span></dt>",
            "<tr><td>two</td><td>2</td></tr>",
            "<code>&lt;out&gt;listing&lt;/out&gt;</code>",
            // The footnote, with links both ways.
            "<a href=\"#lists_para1_footnote1-fnote\">1</a>",
            "<div class=\"footnote-body\"><p>A footnote in the first chapter.</p></div>",
            // The index: its entries grouped by letter (xsl:for-each-group, current-group() in a called template).
            "<h3>D</h3>",
            "<li>definition, <span class=\"indexed-section\" title=\"Chapter\u00A01.\u00A0Lists\"><a class=\"indexref\" href=\"#lists_vl1_vle1_li1_para1_indexterm1\">1</a></span></li>",
        })
        {
            Assert.Contains(expected, html, StringComparison.Ordinal);
        }
    }
}
