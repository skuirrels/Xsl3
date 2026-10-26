// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private static XdmException InvalidComplexContent(FunctionItem function) =>
        new("XTDE0450", $"{function.Describe()} can't be in the content of a node");

    private sealed class ComplexContent(XPathItem input) : FunctionItem
    {
        internal XPathItem? Input = input;
        internal XPathNavigator? Children;
        internal readonly Stack<(ArrayItem Array, int Member, int Item)> Arrays = new();
        internal bool Atomic;
        internal ExceptionDispatchInfo? Fault;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a forward complex-content constructor";

        internal void Complete()
        {
            if (Input is PullState source) source.Complete();
            Input = null;
            Children = null;
            Arrays.Clear();
        }
    }

    public XPathItem X31ContentNew(XPathItem producer)
    {
        XmlQueryRuntime.CheckCancellation();
        return new ComplexContent(X31PullNew(producer));
    }

    public IList<XPathItem> X31ContentNext(XPathItem state)
    {
        var content = (ComplexContent)state;
        content.Fault?.Throw();
        try
        {
            while (true)
            {
                XmlQueryRuntime.CheckCancellation();
                if (content.Children != null)
                {
                    if (content.Children.MoveToNext()) return Sequences.One(content.Children.Clone());
                    content.Children = null;
                }
                XPathItem? item = null;
                while (content.Arrays.Count != 0 && item == null)
                {
                    var (array, member, index) = content.Arrays.Pop();
                    while (member < array.Members.Count)
                    {
                        XmlQueryRuntime.CheckCancellation();
                        IList<XPathItem> values = array.Members[member];
                        if (index < values.Count)
                        {
                            item = values[index];
                            content.Arrays.Push((array, member, index + 1));
                            break;
                        }
                        member++;
                        index = 0;
                    }
                }
                if (item == null)
                {
                    if (content.Input == null) return Sequences.Empty;
                    IList<XPathItem> population = X31PullNext(content.Input);
                    if (population.Count == 0)
                    {
                        content.Complete();
                        return Sequences.Empty;
                    }
                    item = population[0];
                }
                if (item is ArrayItem nested)
                {
                    content.Arrays.Push((nested, 0, 0));
                    continue;
                }
                if (item is XPathNavigator node)
                {
                    content.Atomic = false;
                    if (node.NodeType != XPathNodeType.Root) return Sequences.One(node);
                    XPathNavigator child = node.Clone();
                    if (!child.MoveToFirstChild()) continue;
                    content.Children = child;
                    return Sequences.One(child.Clone());
                }
                if (item is FunctionItem function) throw InvalidComplexContent(function);
                string text = Sequences.Atomize(item).Value;
                if (content.Atomic) text = " " + text;
                content.Atomic = true;
                if (text.Length != 0) return Sequences.One(AtomicValue.OfString(text));
            }
        }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        {
            content.Complete();
            content.Fault = ExceptionDispatchInfo.Capture(error);
            throw;
        }
    }
}
