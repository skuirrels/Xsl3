// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Xml.XPath;
using System.Xml.Schema;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    // Like the map builder and pending dynamic call, this is an opaque item confined to generated
    // code. It contains no navigator or user value and is allocated independently for each call.
    private sealed class SingletonArgument : FunctionItem
    {
        internal bool Seen;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a streamed singleton argument check";
    }

    public XPathItem X31SingletonNew() => new SingletonArgument();

    public XPathNavigator X31SingletonNode(XPathItem state, XPathNavigator node)
    {
        var argument = (SingletonArgument)state;
        if (argument.Seen) throw Err.XPTY0004("A singleton streamed function argument contains more than one node");
        argument.Seen = true;
        return node;
    }

    public bool X31SingletonSeen(XPathItem state) => ((SingletonArgument)state).Seen;

    /// <summary>Resolve the entry point's selection independently of its grounded global context.</summary>
    public XPathNavigator X31InitialSource(bool accumulators) => _runtime.ExternalContext.InitialSelection(accumulators);

    public XPathNavigator X31InitialObservedSource(IList<XPathItem> items, double tableId) =>
        _runtime.ExternalContext.InitialSelection(accumulators: true, source => ObservePrincipalAccumulators(source, items, tableId));

    /// <summary>Open a forward source-document input. Compiled stylesheets own its execution scope.</summary>
    public XPathNavigator X31OpenStreamedSource(string href, string baseUri, string accumulators, string validation, string type)
    {
        if (XmlQueryRuntime.DocumentFunctions == false)
            throw new XdmException("FODC0002", $"Document '{href}' can't be retrieved (XsltSettings.EnableDocumentFunction is off)");
        // This entry point has no schema context. Validated sources use the schema-bearing entry point below;
        // requested observers use the binding-bearing entry points. Do not silently materialize either as a tree.
        if (accumulators.Length != 0 || validation is "strict" or "lax" || type.Length != 0)
            throw new XdmException("streaming-unimplemented", "This streamed source entry point has no schema context or accumulator observers")
                { Name = new QName("http://xsl3/err", "streaming-unimplemented") };
        return OpenStreamedSourceCore(href, baseUri, validation, type, schemas: null);
    }

    /// <summary>Close a forward source at the end of its body.</summary>
    public IList<XPathItem> X31CloseStreamedSource(XPathNavigator source)
    {
        if (source is Streaming.StreamedNavigator streamed)
        {
            streamed.Document.CompleteValidation();
            _runtime.CloseStreamedSource(streamed.Document);
        }
        return Array.Empty<XPathItem>();
    }

    /// <summary>Open a forward source with its imported schemas and requested validation mode.</summary>
    public XPathNavigator X31OpenValidatedStreamedSource(string href, string baseUri, string accumulators, string validation, string type, double schemasId)
    {
        if (XmlQueryRuntime.DocumentFunctions == false)
            throw new XdmException("FODC0002", $"Document '{href}' can't be retrieved (XsltSettings.EnableDocumentFunction is off)");
        if (accumulators.Length != 0)
            throw new XdmException("streaming-unimplemented", "This compiled entry point has no streamed accumulator bindings")
                { Name = new QName("http://xsl3/err", "streaming-unimplemented") };
        return OpenStreamedSourceCore(href, baseUri, validation, type, (XmlSchemaSet)Constant((int)schemasId));
    }

    public XPathNavigator X31OpenObservedStreamedSource(string href, string baseUri, string accumulators, string validation, string type,
        IList<XPathItem> items, double tableId, double package) =>
        ObserveAccumulators(OpenStreamedSourceCore(href, baseUri, validation, type, schemas: null), items, tableId, accumulators, (int)package);

    public XPathNavigator X31OpenObservedValidatedStreamedSource(string href, string baseUri, string accumulators, string validation, string type,
        double schemasId, IList<XPathItem> items, double tableId, double package) =>
        ObserveAccumulators(OpenStreamedSourceCore(href, baseUri, validation, type, (XmlSchemaSet)Constant((int)schemasId)), items, tableId, accumulators, (int)package);

    private XPathNavigator OpenStreamedSourceCore(string href, string baseUri, string validation, string type, XmlSchemaSet? schemas)
    {
        if (XmlQueryRuntime.DocumentFunctions == false)
            throw new XdmException("FODC0002", $"Document '{href}' can't be retrieved (XsltSettings.EnableDocumentFunction is off)");
        bool validating = validation is "strict" or "lax" || type.Length != 0;
        if (!validating)
            return _runtime.ExternalContext.OpenStreamedSource(href, baseUri,
                validation == "strip" || validation.Length == 0 && Tree.CompactDocument.StripInputAnnotations);
        if (schemas == null)
            throw new XdmException("streaming-unimplemented", "This streamed source entry point has no schema context")
                { Name = new QName("http://xsl3/err", "streaming-unimplemented") };
        XmlSchemaType? schemaType = null;
        if (type.Length != 0)
        {
            int end = type.IndexOf('}');
            schemaType = FindValidationType(schemas, new XmlQualifiedName(type[(end + 1)..], type[2..end]))
                ?? throw new XdmException("XTTE1540", $"No type definition is available for {type}");
        }
        return _runtime.ExternalContext.OpenStreamedSource(href, baseUri, stripAnnotations: false,
            new Streaming.StreamedValidation(schemas, validation == "strict", schemaType));
    }
}
