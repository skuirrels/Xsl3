// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class OutermostRuntimeTests
{
    private static XsltLibrary Runtime()
    {
        var data = new XmlQueryStaticData(new XmlWriterSettings(), Array.Empty<WhitespaceRule>(), new StaticDataManager())
            { Xslt2 = true, Constants = new ConstantTable() };
        return new XmlQueryRuntime(data, XslCompiledTransform.AbsentInput, XmlResolver.ThrowingResolver, null, new XmlCachedSequenceWriter()).XsltFunctions;
    }

    private static NativeFunction Producer(Func<IList<XPathItem>> next) =>
        new(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence, _ => next(), "test outermost population");

    private static XPathNavigator[] Nodes()
    {
        var root = XdmNode.Load(new StringReader("<doc><a><nested/></a><b/></doc>")).Navigator;
        Assert.True(root.MoveToFirstChild()); Assert.True(root.MoveToFirstChild());
        var a = root.Clone(); Assert.True(root.MoveToFirstChild()); var nested = root.Clone();
        Assert.True(root.MoveToParent()); Assert.True(root.MoveToNext());
        return new[] { a, nested, root.Clone() };
    }

    [Fact]
    public void OrderedInputsSkipDuplicatesAndDescendantsWithoutReadingPastTheAcceptedNode()
    {
        var library = Runtime(); var nodes = Nodes();
        XPathItem[] items = { nodes[0], nodes[0], nodes[1], nodes[2] }; int reads = 0;
        var state = library.X31OutermostNew(Producer(() => reads < items.Length ? new[] { items[reads++] } : End()));
        IList<XPathItem> End() { reads++; return Array.Empty<XPathItem>(); }
        Assert.True(Assert.Single(library.X31OutermostNext(state)).IsSamePosition(nodes[0]));
        Assert.Equal(1, reads);
        Assert.True(Assert.Single(library.X31OutermostNext(state)).IsSamePosition(nodes[2]));
        Assert.Equal(4, reads);
        Assert.Empty(library.X31OutermostNext(state)); Assert.Empty(library.X31OutermostNext(state));
        Assert.Equal(5, reads);
    }

    [Fact]
    public void AProducerFailureIsRetainedAndNeverResumed()
    {
        var library = Runtime(); var node = Nodes()[0]; int reads = 0;
        var failure = new XdmException("TEST0001", "failed producer");
        var state = library.X31OutermostNew(Producer(() => ++reads == 1 ? new XPathItem[] { node } : throw failure));
        Assert.Single(library.X31OutermostNext(state));
        Assert.Same(failure, Assert.Throws<XdmException>(() => library.X31OutermostNext(state)));
        Assert.Same(failure, Assert.Throws<XdmException>(() => library.X31OutermostNext(state)));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void AnIncorrectOrderProofFailsInsteadOfReturningAnIncorrectSet()
    {
        var library = Runtime(); var nodes = Nodes(); int reads = 0;
        var state = library.X31OutermostNew(Producer(() => new XPathItem[] { nodes[++reads == 1 ? 2 : 0] }));
        Assert.Single(library.X31OutermostNext(state));
        var failure = Assert.Throws<InvalidOperationException>(() => library.X31OutermostNext(state));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => library.X31OutermostNext(state)));
        Assert.Equal(2, reads);
    }
}
