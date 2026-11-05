// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

/// <summary>Mutable local state used by generated IL for one streamed text constructor.</summary>
/// <remarks>Generated code initializes and updates this value by reference, like the runtime iterators.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public struct SimpleContentWriter
{
    private XmlQueryOutput? _output;
    private string? _separator;
    private bool _raw, _tree;
    private bool _part, _text, _ended;
    private ExceptionDispatchInfo? _fault;
    /// <summary>Initialize one compiler-emitted text constructor before evaluating its items.</summary>
    public void Create(string separator, XmlQueryOutput output, bool raw)
    {
        XmlQueryRuntime.CheckCancellation();
        this = default;
        _separator = separator; _output = output; _raw = raw;
        _tree = output.BeginSimpleContent();
        output.WriteSimpleContentChunk(string.Empty, raw);
    }

    private void Write(string value) => _output!.WriteSimpleContentChunk(value, _raw);
    private void Part(string value)
    {
        if (_part) Write(_separator!);
        Write(value); _part = true;
    }
    /// <summary>Write the next item using the canonical simple-content atomization rules.</summary>
    public void Accept(XPathItem item)
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
    /// <summary>Complete this constructor; a recorded failure remains terminal.</summary>
    public void End()
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
