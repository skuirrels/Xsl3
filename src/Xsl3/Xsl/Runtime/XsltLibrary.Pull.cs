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
    private sealed class PullState(XsltLibrary owner, FunctionItem producer) : FunctionItem
    {
        internal XsltLibrary? Owner = owner;
        internal FunctionItem? Producer = producer;
        internal PendingCall? Call;
        internal object?[]? Slots;
        internal int Position;
        internal Stack<bool>? Annotations;
        internal bool PendingAnyType;
        internal ExceptionDispatchInfo? Fault;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a suspended streamed population";

        internal void Complete()
        {
            Position = -1;
            Slots = null;
            Producer = null;
            Call = null;
            Owner = null;
            Annotations = null;
            PendingAnyType = false;
        }
    }

    public XPathItem X31PullNew(XPathItem producer) => new PullState(this, (FunctionItem)producer)
    {
        Annotations = _runtime.CopyAnnotationScopes(),
        PendingAnyType = _runtime.PendingAnyType,
    };

    public static int X31PullPosition(IList<XPathItem> state) => ((PullState)state[0]).Position;

    public static object?[]? X31PullSlots(IList<XPathItem> state) => ((PullState)state[0]).Slots;

    public static IList<XPathItem> X31PullYield(IList<XPathItem> state, object?[] slots, XPathItem item)
    {
        var cursor = (PullState)state[0];
        cursor.Slots = slots;
        cursor.Position = 1;
        return Sequences.One(item is XPathNavigator node ? node.Clone() : item);
    }

    public static IList<XPathItem> X31PullEnd(IList<XPathItem> state)
    {
        ((PullState)state[0]).Complete();
        return Sequences.Empty;
    }

    public IList<XPathItem> X31PullNext(XPathItem state)
    {
        var cursor = (PullState)state;
        cursor.Fault?.Throw();
        if (cursor.Position < 0) return Sequences.Empty;
        XmlQueryRuntime runtime = cursor.Owner!._runtime;
        Stack<bool>? callerAnnotations = runtime.AnnotationScopes;
        bool callerPendingType = runtime.PendingAnyType;
        runtime.AnnotationScopes = cursor.Annotations;
        runtime.PendingAnyType = cursor.PendingAnyType;
        try
        {
            XmlQueryRuntime.CheckCancellation();
            if (cursor.Producer is CompiledFunction)
            {
                // A compiled producer cannot mutate its argument list. Its one opaque state
                // argument is unchanged between resumes, so conversion and copying happen once.
                if (cursor.Call == null)
                {
                    cursor.Call = new PendingCall(cursor.Producer);
                    cursor.Owner!.X31CallArgument(cursor.Call, Sequences.One(cursor));
                }
                return cursor.Owner!.InvokeCall(cursor.Call);
            }
            return cursor.Owner!.CallFunction(cursor.Producer!, Sequences.One(cursor));
        }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        {
            cursor.Complete();
            cursor.Fault = ExceptionDispatchInfo.Capture(error);
            throw;
        }
        finally
        {
            // Annotation scopes belong to the suspended producer. The consumer may copy
            // a yielded node under a different validation policy before the next resume.
            if (cursor.Position >= 0)
            {
                cursor.Annotations = runtime.AnnotationScopes;
                cursor.PendingAnyType = runtime.PendingAnyType;
            }
            runtime.AnnotationScopes = callerAnnotations;
            runtime.PendingAnyType = callerPendingType;
        }
    }
}
