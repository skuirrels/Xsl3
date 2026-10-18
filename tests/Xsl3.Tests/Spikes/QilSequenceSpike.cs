// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Qil;
using Xunit;

namespace Xsl3.Tests.Spikes;

/// <summary>Raw QIL trees compiled by the stock IL generator and evaluated to sequences (XmlILCommand.Evaluate).</summary>
public class QilSequenceSpike
{
    private static IList<XPathItem> Run(QilFactory f, QilNode root, object? context = null)
    {
        QilExpression qil = f.QilExpression(root, f);
        qil.DefaultWriterSettings = new XmlWriterSettings();
        XmlILCommand command = new XmlILGenerator().Generate(qil, null)!;
        return command.Evaluate(context ?? new XPathDocument(new StringReader("<doc><a>1</a><a>2</a></doc>")).CreateNavigator(), null, null);
    }

    [Fact]
    public void AtomicSequence()
    {
        var f = new QilFactory();
        var items = Run(f, f.Sequence(new QilNode[] { f.LiteralDouble(1.5), f.LiteralString("x"), f.LiteralDecimal(2.25m), f.True() }));
        Assert.Equal(new[] { "1.5", "x", "2.25", "true" }, items.Select(i => i.Value));
    }

    [Fact]
    public void LoopOverNodes()
    {
        var f = new QilFactory();
        // for $n in /descendant::node() return string($n)
        QilIterator n = f.For(f.Descendant(f.Root(f.XmlContext())));
        var items = Run(f, f.Loop(n, f.XPathNodeValue(n)));
        Assert.Equal(new[] { "12", "1", "1", "2", "2" }, items.Select(i => i.Value));
    }
}
