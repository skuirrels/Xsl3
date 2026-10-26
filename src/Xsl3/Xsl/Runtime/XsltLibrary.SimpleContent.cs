// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Runtime.ExceptionServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class SimpleContentState : FunctionItem
    {
        private XmlQueryOutput? _output;
        private readonly string _separator;
        private readonly bool _raw, _tree;
        private bool _part, _text, _ended;
        private ExceptionDispatchInfo? _fault;
        internal SimpleContentState(string separator, XmlQueryOutput output, bool raw)
        {
            _separator = separator; _output = output; _raw = raw;
            _tree = output.BeginSimpleContent();
            output.WriteSimpleContentChunk(string.Empty, raw);
        }
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a simple-content text constructor";

        private void Write(string value) => _output!.WriteSimpleContentChunk(value, _raw);
        private void Part(string value)
        {
            if (_part) Write(_separator);
            Write(value); _part = true;
        }
        internal void Accept(XPathItem item)
        {
            _fault?.Throw();
            if (_ended) throw new InvalidOperationException("Simple content is complete");
            try
            {
                XmlQueryRuntime.CheckCancellation();
                if (item is XPathNavigator { NodeType: XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace } text)
                {
                    string value = text.Value;
                    if (value.Length == 0) return;
                    if (_text) Write(value); else Part(value);
                    _text = true;
                    return;
                }
                _text = false;
                if (item is AtomicValue || item is XPathNavigator { NodeType: XPathNodeType.Root or XPathNodeType.Element } container
                    && Xsl3.Schema.SchemaTypes.TypeOf(container) == null) Part(item.Value);
                else if (item is ArrayItem || item is XPathNavigator node && Xsl3.Schema.SchemaTypes.TypeOf(node) != null)
                    foreach (XPathItem atom in Sequences.Atomize(Sequences.One(item))) Part(atom.Value);
                else Part(Sequences.Atomize(item).Value);
            }
            catch (Exception error) when (XmlException.IsCatchableException(error))
            {
                _fault = ExceptionDispatchInfo.Capture(error); _output = null;
                throw;
            }
        }
        internal void End()
        {
            _fault?.Throw();
            if (_ended) return;
            try
            {
                XmlQueryRuntime.CheckCancellation();
                _output!.EndSimpleContent(_tree);
                _output = null; _ended = true;
            }
            catch (Exception error) when (XmlException.IsCatchableException(error))
            {
                _fault = ExceptionDispatchInfo.Capture(error); _output = null;
                throw;
            }
        }
    }

    public XPathItem X31SimpleContentNew(string separator, XmlQueryOutput output, bool raw)
    {
        XmlQueryRuntime.CheckCancellation();
        return new SimpleContentState(separator, output, raw);
    }
    public void X31SimpleContentAccept(XPathItem state, XPathItem item) => ((SimpleContentState)state).Accept(item);
    public void X31SimpleContentEnd(XPathItem state) => ((SimpleContentState)state).End();
}
