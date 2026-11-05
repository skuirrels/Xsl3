// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    // Keep the shipped XPathItem state API for previously saved stylesheets. Newly
    // emitted IL stores the same writer directly in a local, avoiding this wrapper.
    private sealed class SimpleContentState : FunctionItem
    {
        private SimpleContentWriter _writer;
        internal SimpleContentState(string separator, XmlQueryOutput output, bool raw) =>
            _writer.Create(separator, output, raw);
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a simple-content text constructor";
        internal void Accept(XPathItem item) => _writer.Accept(item);
        internal void End() => _writer.End();
    }

    /// <summary>The value of a single literal text constructor, preserving its cancellation checkpoint.</summary>
    public string X31SimpleContentText(string value)
    {
        XmlQueryRuntime.CheckCancellation();
        return value;
    }

    public XPathItem X31SimpleContentNew(string separator, XmlQueryOutput output, bool raw)
    {
        XmlQueryRuntime.CheckCancellation();
        return new SimpleContentState(separator, output, raw);
    }
    public void X31SimpleContentAccept(XPathItem state, XPathItem item) => ((SimpleContentState)state).Accept(item);
    public void X31SimpleContentEnd(XPathItem state) => ((SimpleContentState)state).End();
}
