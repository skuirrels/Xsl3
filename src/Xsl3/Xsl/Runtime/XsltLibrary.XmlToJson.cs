// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Xml.XPath;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class XmlToJsonCall(FunctionDefinition function, double context, IList<XPathItem> options) : FunctionItem
    {
        internal readonly FunctionDefinition Function = function;
        internal readonly double Context = context;
        internal IList<XPathItem> Options = options;
        internal int Count;
        internal bool Ended;
        internal IList<XPathItem>? Result;
        internal ExceptionDispatchInfo? BodyFault, Fault;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a forward XML-to-JSON call";
    }

    public XPathItem X31XmlToJsonNew(double context, IList<XPathItem> options, bool hasOptions)
    {
        XmlQueryRuntime.CheckCancellation();
        return new XmlToJsonCall(FunctionLibrary.Find(QName.Ns.Fn, "xml-to-json", hasOptions ? 2 : 1)!, context, Copy(options));
    }

    public void X31XmlToJsonItem(XPathItem state, XPathItem item)
    {
        XmlQueryRuntime.CheckCancellation();
        var call = (XmlToJsonCall)state;
        if (call.Ended) throw new InvalidOperationException("The XML-to-JSON call has ended");
        if (call.Count != 0)
        {
            call.Count = 2;
            call.Result = null; call.BodyFault = null;
            return;
        }
        call.Count = 1;
        try { call.Result = InvokeXmlToJson(call, One(item)); }
        catch (XdmException error) when (FunctionLibrary.IsXmlToJsonError(error))
        {
            // Argument evaluation and cardinality checks precede the function body.
            // Keep a body error pending while the source can still raise an error or
            // reveal a second item; retain no source position after this invocation.
            call.BodyFault = ExceptionDispatchInfo.Capture(error);
        }
    }

    public IList<XPathItem> X31XmlToJsonEnd(XPathItem state)
    {
        var call = (XmlToJsonCall)state;
        call.Fault?.Throw();
        if (call.Ended) return call.Result ?? Array.Empty<XPathItem>();
        try
        {
            XmlQueryRuntime.CheckCancellation();
            if (call.Count > 1) throw Err.XPTY0004($"Argument 1 of {call.Function} must be node()?, but contains more than one item");
            call.BodyFault?.Throw();
            return call.Result ??= InvokeXmlToJson(call, Array.Empty<XPathItem>());
        }
        catch (Exception error) when (System.Xml.XmlException.IsCatchableException(error))
        {
            call.Result = null;
            call.Fault = ExceptionDispatchInfo.Capture(error);
            throw;
        }
        finally
        {
            call.Ended = true;
            call.Options = Array.Empty<XPathItem>(); call.BodyFault = null;
        }
    }

    public IList<XPathItem> X31XmlToJsonRun(XPathItem state, XPathItem producer, double descendantType)
    {
        var call = (XmlToJsonCall)state;
        call.Fault?.Throw();
        if (call.Ended) return call.Result ?? Array.Empty<XPathItem>();
        var cursor = new PullState(this, (FunctionItem)producer);
        var descendant = descendantType < 0 ? null : (NodeKindTest)((SequenceTypeTest)Constant((int)descendantType)).Item!;
        try
        {
            while (true)
            {
                IList<XPathItem> next = X31PullNext(cursor);
                if (next.Count == 0) break;
                if (next.Count != 1) throw new InvalidOperationException("A node population must yield at most one item");
                using var match = call.Count == 0 && descendant != null && next[0] is Xsl3.Streaming.StreamedNavigator { NodeType: XPathNodeType.Root or XPathNodeType.Element } node
                    ? node.Document.MatchConsumption(node, descendant) : null;
                X31XmlToJsonItem(call, next[0]);
                if (match?.Matched == true)
                {
                    // A predicate-free descendant selection includes this observed
                    // matching descendant of its first member: cardinality is invalid.
                    call.Count = 2; call.Result = null; call.BodyFault = null;
                    return X31XmlToJsonEnd(call);
                }
            }
            return X31XmlToJsonEnd(call);
        }
        catch (Exception error) when (System.Xml.XmlException.IsCatchableException(error))
        {
            call.Result = null; call.Options = Array.Empty<XPathItem>(); call.BodyFault = null;
            call.Fault = ExceptionDispatchInfo.Capture(error); call.Ended = true;
            throw;
        }
        finally { cursor.Complete(); }
    }

    // Header predicates are evaluated once in document order, including headers
    // absorbed by conversion of the first selected node. Only cardinality facts
    // survive absorption; no consumed node or subtree is returned or retained.
    public void X31XmlToJsonHeaders(XPathItem state, XPathNavigator root, XPathItem selector)
    {
        var call = (XmlToJsonCall)state;
        call.Fault?.Throw();
        if (call.Ended) throw new InvalidOperationException("The XML-to-JSON call has ended");
        ExceptionDispatchInfo? selectorFault = null;
        bool Selected(XPathNavigator node)
        {
            selectorFault?.Throw();
            try { return Sequences.EffectiveBooleanValue(CallFunction((FunctionItem)selector, new[] { One(node) }, owned: false)); }
            catch (Exception error) when (System.Xml.XmlException.IsCatchableException(error))
            { selectorFault = ExceptionDispatchInfo.Capture(error); throw; }
        }
        void Consumed(Xsl3.Streaming.StreamedNavigator node)
        {
            if (Selected(node)) { call.Count = 2; call.Result = null; call.BodyFault = null; }
        }
        try
        {
            if (root is Xsl3.Streaming.StreamedNavigator streamed)
            {
                if (streamed.NodeType is XPathNodeType.Root or XPathNodeType.Element
                    && !streamed.PositionNode.EmptyElement && !streamed.Replayable
                    && (streamed.PositionNode.Consumed || streamed.PositionNode.Skipped))
                    throw Xsl3.Streaming.StreamedDocument.PositionError();
                var position = (Xsl3.Streaming.StreamedNavigator)streamed.Clone();
                using var observation = streamed.Document.ObserveConsumedHeaders(streamed, Consumed);
                do
                {
                    XmlQueryRuntime.CheckCancellation();
                    if (Selected(position)) X31XmlToJsonItem(call, position);
                    selectorFault?.Throw();
                } while (position.MoveToNextDescendant(streamed.PositionNode));
            }
            else
            {
                var headers = root.SelectDescendants(XPathNodeType.All, matchSelf: true);
                while (headers.MoveNext())
                {
                    XmlQueryRuntime.CheckCancellation();
                    if (Selected(headers.Current!)) X31XmlToJsonItem(call, headers.Current!);
                }
            }
        }
        catch (Exception error) when (System.Xml.XmlException.IsCatchableException(error))
        {
            call.Result = null; call.Options = Array.Empty<XPathItem>(); call.BodyFault = null;
            call.Fault = ExceptionDispatchInfo.Capture(error); call.Ended = true;
            throw;
        }
    }

    private IList<XPathItem> InvokeXmlToJson(XmlToJsonCall call, IList<XPathItem> input) => call.Function.Arity == 2
        ? Call(call.Function.Id, call.Context, input, call.Options) : Call(call.Function.Id, call.Context, input);
}
