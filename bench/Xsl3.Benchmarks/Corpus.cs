// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace Xsl3.Benchmarks;

/// <summary>
/// The benchmark corpus (SPEC §19.5). Stylesheets live in bench/corpus; input documents are generated,
/// deterministically, rather than committed.
/// </summary>
public static class Corpus
{
    public static string Stylesheet(string name) => Path.Combine(AppContext.BaseDirectory, "corpus", "xslt10", name + ".xsl");

    /// <summary>The XSLT 3.0 rewrite of a 1.0 benchmark (SPEC §19.6, rewritten pairs): same output, idiomatic 3.0.</summary>
    public static string Rewrite(string name) => Path.Combine(AppContext.BaseDirectory, "corpus", "xslt30", "pairs", name + ".xsl");

    public static readonly string[] Pairs = { "identity", "sort", "muenchian", "string-recursion", "key-join", "html-report" };

    /// <summary>An order book: <paramref name="orders"/> orders over a product catalogue, about 450 bytes per order.</summary>
    public static string Orders(int orders) => Orders(orders, 500);

    /// <summary>An order book with a catalogue of <paramref name="products"/> products.</summary>
    public static string Orders(int orders, int products)
    {
        var random = new Random(42);
        string[] customers = new string[Math.Max(10, orders / 20)];
        for (int i = 0; i < customers.Length; i++)
        {
            customers[i] = "Customer " + i.ToString("D5", CultureInfo.InvariantCulture);
        }
        string[] words = { "red", "green", "blue", "fast", "slow", "new", "used", "large", "small", "bulk", "gift", "sale" };

        var sb = new StringBuilder(orders * 460);
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true }))
        {
            w.WriteStartElement("orders");
            for (int p = 0; p < products; p++)
            {
                w.WriteStartElement("product");
                w.WriteAttributeString("sku", "P" + p.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("name", "Product " + p.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("price", (random.Next(100, 100000) / 100.0).ToString("0.00", CultureInfo.InvariantCulture));
                w.WriteEndElement();
            }
            for (int o = 0; o < orders; o++)
            {
                w.WriteStartElement("order");
                w.WriteAttributeString("id", o.ToString(CultureInfo.InvariantCulture));
                w.WriteElementString("customer", customers[random.Next(customers.Length)]);
                w.WriteElementString("total", (random.Next(100, 100000) / 100.0).ToString("0.00", CultureInfo.InvariantCulture));
                var tags = new StringBuilder();
                for (int t = random.Next(1, 6); t > 0; t--)
                {
                    tags.Append(words[random.Next(words.Length)]).Append(' ');
                }
                w.WriteElementString("tags", tags.ToString().TrimEnd());
                for (int l = random.Next(1, 4); l > 0; l--)
                {
                    w.WriteStartElement("line");
                    w.WriteAttributeString("sku", "P" + random.Next(products).ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("qty", random.Next(1, 10).ToString(CultureInfo.InvariantCulture));
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        return sb.ToString();
    }
}
