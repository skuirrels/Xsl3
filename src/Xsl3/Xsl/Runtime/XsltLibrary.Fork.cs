// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    public XPathItem X31BufferForkItem(XPathItem item) => item is StreamedNavigator source
        ? source.BufferForFork() : item is XPathNavigator node ? node.Clone() : item;

    // Code generation pushes this argument directly to the prong's writer. The ordinary
    // method retains the same behavior for callers that already hold a materialized value.
    public IList<XPathItem> X31ForkPush(IList<XPathItem> items)
    {
        foreach (XPathItem item in items) { XmlQueryRuntime.CheckCancellation(); _runtime.Output.WriteItem(item); }
        return Array.Empty<XPathItem>();
    }

    public void X31Fork(IList<XPathItem> prongs, XPathItem focus, double capacity) =>
        RunForkCalls(prongs, focus, capacity, (i, runtime, function) => runtime.XsltFunctions.CallFunction(function));

    private void RunForkCalls(IList<XPathItem> prongs, XPathItem focus, double capacity,
        Action<int, XmlQueryRuntime, FunctionItem> execute, bool serialGrounded = false)
    {
        if (capacity < 1 || capacity > int.MaxValue || capacity != Math.Truncate(capacity))
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (prongs.Count == 0) return;
        PreparedForkBindings prepared = PrepareForkBindings(prongs);
        if (serialGrounded && prepared.Sources.Count == 0)
        {
            for (int i = 0; i < prongs.Count; i++)
            {
                XmlQueryRuntime.CheckCancellation();
                execute(i, _runtime, (FunctionItem)prongs[i]);
            }
            return;
        }
        StreamedNavigator? source = focus as StreamedNavigator;
        if (source == null || source.NodeType is not (XPathNodeType.Root or XPathNodeType.Element))
        {
            source = null;
            foreach (StreamedNavigator candidate in prepared.Sources)
            {
                if (!candidate.CanFork) continue;
                if (source != null && !ReferenceEquals(source.Document, candidate.Document))
                    throw new XdmException("streaming-unimplemented", "A fork with captured operands from multiple forward readers needs coordinated input tees")
                        { Name = new QName(StreamedDocument.ErrorNamespace, "streaming-unimplemented") };
                if (source == null || candidate.PositionNode.Number < source.PositionNode.Number) source = candidate;
            }
        }
        CancellationToken cancellation = XmlQueryRuntime.Cancellation;
        using var caller = new ForkCaller(cancellation);
        var sinks = new ForkOutput[prongs.Count];
        var children = new XmlQueryRuntime[prongs.Count];
        var parameters = new List<ForkParameterSummary>?[prongs.Count];
        for (int i = 0; i < children.Length; i++)
        {
            sinks[i] = new(_runtime, caller, first: i == 0);
            children[i] = _runtime.CreateForkRuntime(sinks[i], caller);
            sinks[i].Attach(children[i]);
        }

        void Execute(int i, StreamedNavigator? view, CancellationToken token)
        {
            children[i].RunFork(r => {
                // Live operands use the tee view. Explicit group buffers and retained
                // headers can construct private positions without another input reader.
                try
                {
                    IList<XPathItem> functions = view == null ? r.XsltFunctions.ProjectForkBindings(prepared) : r.XsltFunctions.ProjectForkBindings(prepared, view);
                    execute(i, r, (FunctionItem)functions[i]);
                    parameters[i] = r.XsltFunctions.CompleteForkParameters();
                }
                finally { r.XsltFunctions._projectedForkParameters = null; }
            }, token, null);
        }

        if (source is { Replayable: false })
        {
            using var input = source.Document.Fork(source);
            var bodies = new Action<StreamedNavigator, CancellationToken>[prongs.Count];
            for (int i = 0; i < bodies.Length; i++)
            {
                int index = i;
                bodies[i] = (view, token) => Execute(index, view, token);
            }
            input.Run(bodies, (int)capacity, cancellation, caller);
        }
        else
        {
            var bodies = new Action<StreamTee<object>.Reader, CancellationToken>[prongs.Count];
            for (int i = 0; i < bodies.Length; i++)
            {
                int index = i;
                bodies[i] = (_, token) => Execute(index, null, token);
            }
            ForkProngs.Run<object>(() => null, bodies, (int)capacity, cancellation, caller);
        }
        var completed = new Dictionary<FunctionParameter, ForkParameterSummary>();
        foreach (var summaries in parameters)
            if (summaries != null)
                foreach (ForkParameterSummary summary in summaries)
                {
                    if (completed.TryGetValue(summary.Original, out var prior) && prior.Count != summary.Count)
                        throw new InvalidOperationException("Forked pure parameter factories disagree on cardinality");
                    completed.TryAdd(summary.Original, summary);
                }
        foreach (ForkParameterSummary summary in completed.Values)
        {
            XPathNavigator? first = summary.First == null ? null : (XPathNavigator)ProjectForkBindings(summary.First)[0];
            summary.Original.AcceptFork(summary.Count, first);
        }
        foreach (ForkOutput sink in sinks) sink.Replay();
    }
}
