// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Schema;
using Xsl3.Streaming;

namespace Xsl3.Runtime;

// The first prong relays individual writer operations to the invoking thread. Later
// prongs own cached output, which XSLT permits to grow until declaration-order replay.
internal sealed class ForkOutput(XmlQueryRuntime parent, ForkCaller caller, bool first) : XmlSequenceWriter
{
    private readonly XmlCachedSequenceWriter? _cache = first ? null : new(compact: true);
    private XmlQueryRuntime? _owner;
    private XmlRawWriter? _target;
    private bool _standalone;
    private XmlQueryOutput? _output;
    private readonly PrincipalResult? _principal = XmlQueryRuntime.Principal;

    internal void Attach(XmlQueryRuntime owner)
    {
        _owner = owner;
        owner.ForkPrincipal = WritePrincipal;
    }

    private IList<XPathItem> WritePrincipal(IList<XPathItem> items, XmlWriterSettings settings, OutputExtras? extras, bool raw)
    {
        // Principal ownership is transformation-wide. The real writer and its
        // configuration stay on the invoking thread, including through nested forks.
        var prepared = _owner!.XsltFunctions.PrepareForkOutput(items);
        CancellationToken cancellation = XmlQueryRuntime.Cancellation;
        return caller.Invoke(() => {
            CancellationToken saved = XmlQueryRuntime.Cancellation;
            try
            {
                XmlQueryRuntime.Cancellation = cancellation;
                cancellation.ThrowIfCancellationRequested();
                IList<XPathItem> values = parent.XsltFunctions.ProjectForkBindings(prepared);
                IList<XPathItem> result = parent.ForkPrincipal is { } outer ? outer(values, settings, extras, raw)
                    : parent.XsltFunctions.WritePrincipalResult(_principal, values, settings, extras, raw);
                cancellation.ThrowIfCancellationRequested();
                return result;
            }
            finally { XmlQueryRuntime.Cancellation = saved; }
        }, cancellation);
    }
    internal void Replay()
    {
        if (_cache != null)
            foreach (XPathItem item in _cache.ResultSequence)
            {
                XmlQueryRuntime.CheckCancellation();
                parent.Output.WriteItem(item);
            }
    }

    private void Send(Action<XmlRawWriter> action) => caller.Invoke(() => action(_target!), XmlQueryRuntime.Cancellation);

    public override XmlRawWriter StartTree(XPathNodeType rootType, IXmlNamespaceResolver? nsResolver, XmlNameTable nameTable)
    {
        if (_cache != null) return _cache.StartTree(rootType, nsResolver, nameTable);
        caller.Invoke(() => {
            _output = parent.Output;
            _standalone = _output.AtSequence;
            if (_standalone)
            {
                _output.StartTree(rootType);
                _target = _output.Writer!;
            }
            else _target = new XmlRawWriterWrapper(_output);
        }, XmlQueryRuntime.Cancellation);
        return new Relay(this) { NamespaceResolver = nsResolver };
    }

    public override void EndTree()
    {
        if (_cache != null) { _cache.EndTree(); return; }
        caller.Invoke(() => {
            if (_standalone) _output!.EndTree();
            _target = null;
            _output = null;
        }, XmlQueryRuntime.Cancellation);
    }

    public override void WriteItem(XPathItem item)
    {
        if (item is StreamedNavigator { NodeType: not (XPathNodeType.Attribute or XPathNodeType.Namespace) } source)
        {
            // Consume the view on its worker. Moving a live view into a caller callback
            // could block that caller behind another prong's pending source request.
            XmlQueryOutput output = _owner!.Output;
            if (source.NodeType == XPathNodeType.Root)
            {
                output.WriteStartRoot();
                output.XsltCopyOf(source);
                output.WriteEndRoot();
            }
            else output.XsltCopyOf(source);
            return;
        }
        if (_cache != null) _cache.WriteItem(item);
        else caller.Invoke(() => parent.Output.WriteItem(item), XmlQueryRuntime.Cancellation);
    }

    private sealed class Relay(ForkOutput sink) : XmlRawWriter, IAnnotatingWriter
    {
        public override void WriteStartElement(string? prefix, string localName, string? ns) => sink.Send(w => w.WriteStartElement(prefix, localName, ns));
        public override void WriteStartAttribute(string? prefix, string localName, string? ns) => sink.Send(w => w.WriteStartAttribute(prefix, localName, ns));
        public override void WriteEndAttribute() => sink.Send(w => w.WriteEndAttribute());
        public override void WriteString(string? text) => sink.Send(w => w.WriteString(text));
        public override void WriteCData(string? text) => sink.Send(w => w.WriteCData(text));
        public override void WriteComment(string? text) => sink.Send(w => w.WriteComment(text));
        public override void WriteProcessingInstruction(string name, string? text) => sink.Send(w => w.WriteProcessingInstruction(name, text));
        public override void WriteWhitespace(string? ws) => sink.Send(w => w.WriteWhitespace(ws));
        public override void WriteRaw(string data) => sink.Send(w => w.WriteRaw(data));
        public override void WriteRaw(char[] buffer, int index, int count) => sink.Send(w => w.WriteRaw(buffer, index, count));
        public override void WriteChars(char[] buffer, int index, int count) => sink.Send(w => w.WriteChars(buffer, index, count));
        public override void WriteEntityRef(string name) => sink.Send(w => w.WriteEntityRef(name));
        public override void WriteCharEntity(char ch) => sink.Send(w => w.WriteCharEntity(ch));
        public override void WriteSurrogateCharEntity(char lowChar, char highChar) => sink.Send(w => w.WriteSurrogateCharEntity(lowChar, highChar));
        public override void Flush() => sink.Send(w => w.Flush());
        internal override void StartElementContent() => sink.Send(w => w.StartElementContent());
        internal override void WriteEndElement(string prefix, string localName, string ns) => sink.Send(w => w.WriteEndElement(prefix, localName, ns));
        internal override void WriteFullEndElement(string prefix, string localName, string ns) => sink.Send(w => w.WriteFullEndElement(prefix, localName, ns));
        internal override void WriteNamespaceDeclaration(string prefix, string ns) => sink.Send(w => w.WriteNamespaceDeclaration(prefix, ns));
        private void Annotate(Action<IAnnotatingWriter> action) => sink.Send(w => {
            IAnnotatingWriter? typed = AnnotatingWriters.Of(sink._standalone ? w : sink._output!.Writer);
            if (typed != null) action(typed);
        });
        public void AnnotateElement(XmlSchemaType type, bool nilled) => Annotate(w => w.AnnotateElement(type, nilled));
        public void AnnotateAttribute(string ns, string local, XmlSchemaType type) => Annotate(w => w.AnnotateAttribute(ns, local, type));
        public void MarkIdAttribute(string ns, string local) => Annotate(w => w.MarkIdAttribute(ns, local));
        public void MarkIdrefAttribute(string ns, string local) => Annotate(w => w.MarkIdrefAttribute(ns, local));
    }
}
