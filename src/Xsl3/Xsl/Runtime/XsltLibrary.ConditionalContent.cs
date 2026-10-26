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
    private sealed class ConditionalContent(XPathItem input) : FunctionItem
    {
        internal XPathItem? Input = input, Body;
        // Only the undecided vacuous prefix and its deferred instructions are retained.
        // After the first non-vacuous item, every ordinary item is transmitted directly.
        internal readonly Queue<XPathItem> Pending = new();
        internal bool NonVacuous, Draining, Ended;
        internal ExceptionDispatchInfo? Fault;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a forward conditional sequence constructor";

        internal void Complete()
        {
            if (Input is PullState source) source.Complete();
            if (Body is PullState body) body.Complete();
            Input = Body = null;
            Pending.Clear();
            Ended = true;
        }
    }

    public XPathItem X31OnEmptyNew(XPathItem producer)
    {
        XmlQueryRuntime.CheckCancellation();
        return new ConditionalContent(X31PullNew(producer));
    }

    public IList<XPathItem> X31OnEmptyNext(XPathItem state)
    {
        var content = (ConditionalContent)state;
        content.Fault?.Throw();
        try
        {
            while (true)
            {
                XmlQueryRuntime.CheckCancellation();
                if (content.Body != null)
                {
                    IList<XPathItem> next = X31PullNext(content.Body);
                    if (next.Count != 0) return next;
                    content.Body = null;
                }
                if (content.Draining)
                {
                    if (content.Pending.Count != 0)
                    {
                        XPathItem pending = content.Pending.Dequeue();
                        if (pending is DeferredItem deferred)
                        {
                            if (content.NonVacuous && !deferred.OnEmpty) content.Body = X31PullNew(deferred.Body);
                            continue;
                        }
                        return Sequences.One(pending);
                    }
                    content.Draining = false;
                }
                if (content.Ended) return Sequences.Empty;
                IList<XPathItem> population = X31PullNext(content.Input!);
                if (population.Count == 0)
                {
                    content.Input = null;
                    content.Ended = content.Draining = true;
                    continue;
                }
                XPathItem item = population[0];
                if (item is DeferredItem conditional)
                {
                    if (conditional.OnEmpty)
                    {
                        if (!content.NonVacuous)
                        {
                            content.Pending.Clear();
                            content.Body = X31PullNew(conditional.Body);
                        }
                    }
                    else if (content.NonVacuous) content.Body = X31PullNew(conditional.Body);
                    else content.Pending.Enqueue(conditional);
                    continue;
                }
                if (content.NonVacuous) return population;
                content.Pending.Enqueue(item);
                if (!IsVacuous(item)) content.NonVacuous = content.Draining = true;
            }
        }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        {
            content.Complete();
            content.Fault = ExceptionDispatchInfo.Capture(error);
            throw;
        }
    }

    public bool X31PopulatedItem(XPathItem item)
    {
        XmlQueryRuntime.CheckCancellation();
        return !DeemedEmpty(item);
    }
}
