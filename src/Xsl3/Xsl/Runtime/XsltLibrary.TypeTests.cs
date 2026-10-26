// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Runtime.ExceptionServices;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    // Checking a sequence type needs only a saturated count and the match result.
    // In particular, the state must never own an inspected or transmitted node.
    private sealed class SequenceTypeInspection(SequenceTypeTest type, string errorCode) : FunctionItem
    {
        internal readonly SequenceTypeTest Type = type;
        internal readonly string ErrorCode = errorCode;
        internal bool Assertion => ErrorCode.Length != 0;
        internal int Count;
        internal bool Matches = true, Ended;
        internal ExceptionDispatchInfo? Fault;
        internal XdmException Error() => new(ErrorCode, ErrorCode == "XPDY0050" ? $"The value does not match the type {Type} required by 'treat as'" : $"The value does not match the required type {Type}");
        internal void Fail(Exception error) { Ended = true; Fault = ExceptionDispatchInfo.Capture(error); }
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a forward sequence type check";
    }

    public XPathItem X31TypeTestNew(double typeId, string errorCode)
    {
        XmlQueryRuntime.CheckCancellation();
        return new SequenceTypeInspection((SequenceTypeTest)Constant((int)typeId), errorCode);
    }

    public bool X31TypeTestAccept(XPathItem state, XPathItem item)
    {
        var check = (SequenceTypeInspection)state;
        check.Fault?.Throw();
        try
        {
            XmlQueryRuntime.CheckCancellation();
            if (check.Ended) throw new InvalidOperationException("The sequence type check is complete");
            if (check.Count < 2) check.Count++;
            if (check.Matches && (check.Type.Item == null || check.Count > 1 && !check.Type.AllowsMany
                || !check.Type.Item.Matches(item))) check.Matches = false;
            if (check.Assertion && !check.Matches) throw check.Error();
            return true;
        }
        catch (Exception error) when (XmlException.IsCatchableException(error)) { check.Fail(error); throw; }
    }

    public bool X31TypeTestResult(XPathItem state)
    {
        var check = (SequenceTypeInspection)state;
        check.Fault?.Throw();
        try
        {
            XmlQueryRuntime.CheckCancellation();
            if (!check.Ended)
            {
                check.Matches &= check.Count != 0 || check.Type.AllowsEmpty;
                check.Ended = true;
                if (check.Assertion && !check.Matches) throw check.Error();
            }
            return check.Matches;
        }
        catch (Exception error) when (XmlException.IsCatchableException(error)) { check.Fail(error); throw; }
    }

    public void X31TypeTestInspect(XPathItem state, XPathItem item) => _ = X31TypeTestAccept(state, item);

    public void X31TreatEnd(XPathItem state) => _ = X31TypeTestResult(state);
}
