// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Xslt;

namespace Xsl3.Runtime
{
    /// <summary>
    /// The context of a query consists of all user-provided information which influences the operation of the
    /// query. The context manages the following information:
    ///
    ///   1. Input data sources, including the default data source if one exists
    ///   2. Extension objects
    ///   3. External parameters
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed partial class XmlQueryContext
    {
        private readonly XmlQueryRuntime _runtime;
        private readonly XPathNavigator? _defaultDataSource;
        private readonly XmlResolver _dataSources;

        // Xsl3: fn:unparsed-text and friends read resources through the same resolver as documents.
        internal XmlResolver DataSources => _dataSources;
        private readonly XsltArgumentList? _argList;
        private XmlExtensionFunctionTable? _extFuncsLate;
        private readonly WhitespaceRuleLookup? _wsRules;
        private readonly QueryReaderSettings _readerSettings; // If we create reader out of stream we will use these settings

        // This context borrows the global item and initial-selection identity; it never owns
        // the parent's input. Extension binding caches and query name tables remain local.
        private readonly XmlQueryContext? _forkContextOwner;

        internal XmlQueryContext(XmlQueryRuntime runtime, XmlQueryContext owner)
        {
            _runtime = runtime;
            _dataSources = owner._dataSources;
            _argList = owner._argList;
            _wsRules = owner._wsRules;
            _readerSettings = owner._readerSettings.Fork();
            _defaultDataSource = owner._defaultDataSource?.Clone();
            _initialSelection = owner._initialSelection?.Clone();
            _forkContextOwner = owner;
        }

        /// <summary>
        /// This constructor is internal so that external users cannot construct it (and therefore we do not have to test it separately).
        /// </summary>
        internal XmlQueryContext(XmlQueryRuntime runtime, object defaultDataSource, XmlResolver dataSources, XsltArgumentList? argList, WhitespaceRuleLookup? wsRules)
        {
            _runtime = runtime;
            _dataSources = dataSources;
            _argList = argList;
            _wsRules = wsRules;

            if (defaultDataSource is Streaming.InitialSource source)
            {
                _initialSource = source;
                defaultDataSource = source.GlobalContext;
            }
            else if (runtime.StreamedInitialMode && defaultDataSource is XmlReader inputReader)
            {
                _initialSource = new(inputReader, null, XslCompiledTransform.AbsentInput);
                defaultDataSource = XslCompiledTransform.AbsentInput;
            }
            object settingsSource = _initialSource?.Input ?? defaultDataSource;
            if (settingsSource is XmlReader)
            {
                _readerSettings = new QueryReaderSettings((XmlReader)settingsSource);
            }
            else if (argList?.GetParam(Xsl3.XPath31.XPath31Executable.ReaderSettingsParam, Xsl3.XPath31.XPath31Executable.ControlNamespace) is XmlReaderSettings readerSettings)
            {
                // Xsl3: reader settings supplied by the XPath 3.1 API.
                _readerSettings = new QueryReaderSettings(readerSettings);
            }
            else if (runtime.IsXslt2)
            {
                // Xsl3: documents a 2.0+ stylesheet loads have their DTDs read (ID attributes, entities, element
                // content whitespace), the external subset through the transformation's resolver, with entity
                // expansion capped. 1.0 keeps upstream's settings, which refuse DTDs.
                _readerSettings = new QueryReaderSettings(InputReaderSettings(dataSources, xslt2: true));
            }
            else
            {
                // Consider allowing users to set DefaultReaderSettings in XsltArgumentList
                // readerSettings = argList.DefaultReaderSettings;
                _readerSettings = new QueryReaderSettings(new NameTable());
            }

            if (!runtime.IsXslt2 && _initialSource != null)
            {
                _defaultDataSource = InitialSelection(accumulators: false);
                return;
            }
            if (defaultDataSource is XPathNavigator absent && XslCompiledTransform.AbsentInput.IsSamePosition(absent))
            {
                // Whitespace stripping must not copy away the sentinel's absence identity.
                _defaultDataSource = absent;
            }
            else if (defaultDataSource is string s)
            {
                // Load the default document from a Uri
                _defaultDataSource = GetDataSource(s, null);

                if (_defaultDataSource == null)
                    throw new XslTransformException(SR.XmlIl_UnknownDocument, s);
            }
            else if (defaultDataSource != null)
            {
                _defaultDataSource = ConstructDocument(defaultDataSource, null, null);
                if (runtime.IsXslt2 && _defaultDataSource?.Clone() is { } root && MoveToRoot(root) &&
                    Uri.TryCreate(root.BaseURI, UriKind.Absolute, out Uri? inputUri))
                {
                    // Xsl3: doc() of the input document's URI is the input document (fn:doc is stable).
                    _runtime.Documents.Cache(inputUri, root);
                }
            }
        }

        private static bool MoveToRoot(XPathNavigator navigator)
        {
            navigator.MoveToRoot();
            return navigator.NodeType == XPathNodeType.Root;
        }


        //-----------------------------------------------
        // Input data sources
        //-----------------------------------------------

        /// <summary>
        /// Returns the name table that should be used in the query to atomize search names and to load
        /// new documents.
        /// </summary>
        public XmlNameTable QueryNameTable
        {
            get { return _readerSettings.NameTable; }
        }

        /// <summary>
        /// Returns the name table used by the default data source, or null if there is no default data source.
        /// </summary>
        public XmlNameTable? DefaultNameTable
        {
            get { return _defaultDataSource?.NameTable; }
        }

        /// <summary>Xsl3: whether there is a default document.</summary>
        internal bool HasDefaultDataSource => _defaultDataSource != null;

        /// <summary>
        /// Return the document which is queried by default--i.e. no data source is explicitly selected in the query.
        /// </summary>
        public XPathNavigator DefaultDataSource
        {
            get
            {
                // Throw exception if there is no default data source to return
                if (_defaultDataSource == null)
                    throw new XslTransformException(SR.XmlIl_NoDefaultDocument, string.Empty);

                return _defaultDataSource;
            }
        }

        /// <summary>
        /// Fetch the data source specified by "uriRelative" and "uriBase" from the XmlResolver that the user provided.
        /// If the resolver returns a stream or reader, create an instance of XPathDocument.  If the resolver returns an
        /// XPathNavigator, return the navigator.  Throw an exception if no data source was found.
        /// </summary>
        public XPathNavigator? GetDataSource(string uriRelative, string? uriBase) => GetDataSourceCore(uriRelative, uriBase, documentView: false);

        internal XPathNavigator? GetDocument(string uriRelative, string? uriBase) => GetDataSourceCore(uriRelative, uriBase, documentView: true);

        private XPathNavigator? GetDataSourceCore(string uriRelative, string? uriBase, bool documentView)
        {
            try
            {
                // Xsl3: fn:doc and source-document validate the URI before provider normalization.
                if (documentView) DocumentFragment.ValidateReference(uriRelative);
                Uri? uriResolvedBase = uriBase != null ? _dataSources.ResolveUri(null, uriBase) : null;
                Uri uriResolved = _dataSources.ResolveUri(uriResolvedBase, uriRelative)
                    ?? throw new XmlException("The resolver did not resolve the requested document URI");
                DocumentFragment fragment;
                try { fragment = DocumentFragment.Parse(uriResolved); }
                catch (Xsl3.Xdm.XdmException error) when (!documentView && error.Code == "FODC0002")
                {
                    throw new Xsl3.Xdm.XdmException("XTDE1160", error.Message);
                }
                uriResolved = documentView ? fragment.DocumentUri : fragment.ResourceUri;
                XPathNavigator? result = _runtime.Documents.TryGet(uriResolved, out XPathNavigator? cached) ? cached : _runtime.Documents.Load(uriResolved, () => {
                    object? input = _dataSources.GetEntity(fragment.ResourceUri, null, null);
                    if (input is Stream or XmlReader or IXPathNavigable) _runtime.Documents.MarkRead(fragment.ResourceUri);
                    return input == null ? null : ConstructDocument(input, uriRelative, uriResolved);
                }, XmlQueryRuntime.Cancellation, _runtime.DocumentDependencies, _runtime.EvaluationDependencies);
                // document() returns nodes of the stable complete resource; fn:doc() uses the
                // implementation-defined selected-document view. Their root identities differ.
                return !documentView && fragment.Id != null && result != null
                    ? fragment.Find(result, XmlQueryRuntime.Cancellation) : result;
            }
            catch (XslTransformException)
            {
                // Don't need to wrap XslTransformException
                throw;
            }
            catch (Xsl3.Xdm.XdmException e) when (e.Code is "XTDE1500" or "FODC0005" or "XTDE1160")
            {
                throw;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                if (!XmlException.IsCatchableException(e))
                {
                    throw;
                }
                DocumentResources.RethrowReadControlError(e);
                if (_runtime.IsXslt2)
                {
                    // Xsl3: in 2.0+ a document that can't be retrieved or parsed is FODC0002 (XSLT 3.0 §20.1).
                    throw new Xsl3.Xdm.XdmException("FODC0002", XslTransformException.CreateMessage(SR.XmlIl_DocumentLoadError, uriRelative) + " " + e.Message);
                }
                throw new XslTransformException(e, SR.XmlIl_DocumentLoadError, uriRelative);
            }
        }

        internal XPathNavigator OpenStreamedSource(string href, string baseUri, bool stripAnnotations, Streaming.StreamedValidation? validation = null)
        {
            XmlReader? reader = null;
            Stream? stream = null;
            bool ownsReader = false;
            DocumentResources.ReadClaim? readClaim = null;
            try
            {
                DocumentFragment.ValidateReference(href);
                Uri? resolvedBase = baseUri.Length == 0 ? null : _dataSources.ResolveUri(null, baseUri);
                Uri uri = _dataSources.ResolveUri(resolvedBase, href);
                DocumentFragment fragment = DocumentFragment.Parse(uri);
                readClaim = _runtime.Documents.BeginRead(fragment.ResourceUri);

                // Xsl3: this scope applies the requested schema, so negotiate bytes before a provider
                // prevalidates them. Reader-only resolvers retain their existing delivery contract.
                Type? requestedType = validation != null && _dataSources.SupportsType(fragment.ResourceUri, typeof(Stream))
                    ? typeof(Stream) : null;
                object? entity = _dataSources.GetEntity(fragment.ResourceUri, null, requestedType);
                if (entity is Stream input)
                {
                    stream = input;
                    reader = _readerSettings.CreateReader(input, fragment.ResourceUri.ToString(), _runtime.Documents);
                    ownsReader = true;
                }
                else if (entity is XmlReader supplied) reader = supplied;
                else throw new Xsl3.Xdm.XdmException("FODC0002", "A streamed source resolver must return a Stream or XmlReader");
                readClaim.Commit();
                if (fragment.Id != null)
                {
                    reader = fragment.Select(reader, ownsReader, XmlQueryRuntime.Cancellation);
                    ownsReader = true; // the selected view owns its subtree reader, even for borrowed input
                }

                // Xsl3 (P7C): resolver readers have the same positioned-input boundary as initial readers,
                // including sources that do not request schema validation.
                reader = ScopePositionedReader(reader, ref ownsReader);
                XmlReader validating = validation?.Wrap(reader, ownsReader) ?? reader;
                var chain = new Streaming.StreamedInputReader(WhitespaceRuleReader.CreateReader(validating, _wsRules, stripAnnotations), ownsReader || validation != null, XmlQueryRuntime.Cancellation, stripAnnotations);
                var document = new Streaming.StreamedDocument(chain, ownsReader: true, cancellation: XmlQueryRuntime.Cancellation, completeValidation: validation != null);
                _runtime.RegisterStreamedSource(document);
                validation?.Attach(document);
                return document.CreateNavigator();
            }
            catch (Exception e) when (XmlException.IsCatchableException(e))
            {
                try
                {
                    if (ownsReader) reader?.Dispose();
                    else if (reader == null) stream?.Dispose();
                }
                catch (Exception cleanup) when (XmlException.IsCatchableException(cleanup))
                {
                    // Preserve the source-opening error if a provider also fails while closing it.
                }
                if (e is Xsl3.Xdm.XdmException or OperationCanceledException) throw;
                DocumentResources.RethrowReadControlError(e);
                throw new Xsl3.Xdm.XdmException("FODC0002", $"Could not open streamed source '{href}': {e.Message}");
            }
            finally { readClaim?.Dispose(); }
        }

        /// <summary>
        /// Ensure that "dataSource" is cached as an XPathDocument and return a navigator over the document.
        /// </summary>
        /// <summary>
        /// Xsl3 (P4): the tree for a document the transform parses itself (its input from a URI or reader, doc(),
        /// document()): the compact tree for 2.0+ stylesheets, XPathDocument's (as stock builds) for 1.0 unless the
        /// compact tree is asked for. XSL3_COMPACT_TREE=1 or 0 overrides the choice (for testing).
        /// </summary>
        private XPathNavigator BuildTree(XmlReader reader, bool typed = false)
        {
            bool compact = Xsl3.Tree.CompactDocument.Override ?? (_runtime.IsXslt2 || _runtime.UseCompactTree);
            var namespaces = DocumentFragment.RootNamespaces(reader);
            var ids = DocumentFragment.IdDeclarations(reader);
            var idrefs = DocumentFragment.IdrefDeclarations(reader);
            if ((compact || typed) && (namespaces != null || ids != null))
            {
                var builder = new Xsl3.Tree.CompactDocument.Builder(reader.NameTable, reader.BaseURI);
                builder.Read(reader, XmlSpace.Preserve, typed: typed, rootNamespaces: namespaces, idDeclarations: ids, idrefDeclarations: idrefs);
                return builder.Finish().CreateNavigator();
            }
            return compact || typed
                ? Xsl3.Tree.CompactDocument.Load(reader, XmlSpace.Preserve, typed).CreateNavigator()
                : new InternalXPathDocument(reader, XmlSpace.Preserve).CreateNavigator();
        }


        private XPathNavigator? ConstructDocument(object dataSource, string? uriRelative, Uri? uriResolved)
        {
            Debug.Assert(dataSource != null, "GetType() below assumes dataSource is not null");
            DocumentFragment fragment = uriResolved == null ? default : DocumentFragment.Parse(uriResolved);
            Stream? stream = dataSource as Stream;
            if (stream != null)
            {
                // Create document from stream
                XmlReader? reader = null;
                bool completed = false;
                try
                {
                    reader = _readerSettings.CreateReader(stream, fragment.ResourceUri?.ToString(), _runtime.Documents);
                    using XmlReader? selected = fragment.Id == null ? null : fragment.Select(reader, ownsInput: false, XmlQueryRuntime.Cancellation);
                    // Create WhitespaceRuleReader if whitespace should be stripped
                    XPathNavigator value = BuildTree(WhitespaceRuleReader.CreateReader(selected ?? reader, _wsRules), typed: selected != null);
                    completed = true;
                    return value;
                }
                finally
                {
                    // Always close reader that was opened here
                    try
                    {
                        if (reader != null) reader.Close();
                        else stream.Dispose();
                    }
                    catch (Exception) when (!completed) { }
                }
            }
            else if (dataSource is XmlReader reader)
            {
                // Create document from reader
                // Create WhitespaceRuleReader if whitespace should be stripped
                using XmlReader? selected = fragment.Id == null ? null : fragment.Select(reader, ownsInput: false, XmlQueryRuntime.Cancellation);
                return BuildTree(WhitespaceRuleReader.CreateReader(selected ?? reader, _wsRules), typed: selected != null);
            }
            else if (dataSource is IXPathNavigable xPathNavigable)
            {
                if (fragment.Id != null)
                {
                    XPathNavigator navigator = xPathNavigable.CreateNavigator()!;
                    using XmlReader selected = fragment.Select(navigator, XmlQueryRuntime.Cancellation);
                    return BuildTree(WhitespaceRuleReader.CreateReader(selected, _wsRules), typed: true);
                }
                if (_wsRules != null)
                {
                    XPathNavigator? loaded = xPathNavigable.CreateNavigator();
                    if (_runtime.IsXslt2 && loaded is { NodeType: XPathNodeType.Root })
                    {
                        // Xsl3: in 2.0+ a loaded document is copied with the whitespace stripped (XSLT 3.0 §4.3).
                        using XmlReader copy = loaded.ReadSubtree();
                        // (A typed tree's copy keeps its annotations: P6.)
                        XPathNavigator tree = BuildTree(WhitespaceRuleReader.CreateReader(copy, _wsRules), typed: loaded is Xsl3.Tree.CompactNavigator { Typed: true });
                        if (tree is Xsl3.Tree.CompactNavigator compact)
                        {
                            compact.Document.SetIdSource(loaded); // its DTD's IDs, which the copy can't read
                        }
                        return tree;
                    }
                    throw new XslTransformException(SR.XmlIl_CantStripNav, string.Empty);
                }

                return xPathNavigable.CreateNavigator();
            }

            Debug.Assert(uriRelative != null, "Relative URI should not be null");
            throw new XslTransformException(SR.XmlIl_CantResolveEntity, uriRelative, dataSource.GetType().ToString());
        }


        //-----------------------------------------------
        // External parameters
        //-----------------------------------------------

        /// <summary>
        /// Get a named parameter from the external argument list.  Return null if no argument list was provided, or if
        /// there is no parameter by that name.
        /// </summary>
        public object? GetParameter(string localName, string namespaceUri)
        {
            return _argList?.GetParam(localName, namespaceUri);
        }


        //-----------------------------------------------
        // Extension objects
        //-----------------------------------------------

        /// <summary>
        /// Return the extension object that is mapped to the specified namespace, or null if no object is mapped.
        /// </summary>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
            Justification = XsltArgumentList.ExtensionObjectSuppresion)]
        public object? GetLateBoundObject(string namespaceUri)
        {
            return ExtensionObject(namespaceUri);
        }

        // Xsl3: none when XsltSettings.EnableExtensionFunctions is off.
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
            Justification = XsltArgumentList.ExtensionObjectSuppresion)]
        private object? ExtensionObject(string namespaceUri) =>
            XmlQueryRuntime.ExtensionFunctions ? _argList?.GetExtensionObject(namespaceUri) : null;

        /// <summary>
        /// Return true if the late bound object identified by "namespaceUri" contains a method that matches "name".
        /// </summary>
        [RequiresUnreferencedCode(Scripts.ExtensionFunctionCannotBeStaticallyAnalyzed)]
        public bool LateBoundFunctionExists(string name, string namespaceUri)
        {
            object? instance;

            if (_argList == null)
                return false;

            instance = ExtensionObject(namespaceUri);
            if (instance == null)
                return false;

            return new XmlExtensionFunction(name, namespaceUri, -1, instance.GetType(), XmlQueryRuntime.LateBoundFlags).CanBind();
        }

        /// <summary>
        /// Get a late-bound extension object from the external argument list.  Bind to a method on the object and invoke it,
        /// passing "args" as arguments.
        /// </summary>
        [RequiresUnreferencedCode(Scripts.ExtensionFunctionCannotBeStaticallyAnalyzed)]
        public IList<XPathItem> InvokeXsltLateBoundFunction(string name, string namespaceUri, IList<XPathItem>[] args)
        {
            object? instance;
            object[] objActualArgs;
            XmlQueryType xmlTypeFormalArg;
            Type clrTypeFormalArg;
            object? objRet;

            // Get external object instance from argument list (throw if either the list or the instance doesn't exist)
            instance = ExtensionObject(namespaceUri);
            if (instance == null)
                throw new XslTransformException(SR.XmlIl_UnknownExtObj, namespaceUri);

            // Bind to a method on the instance object
            _extFuncsLate ??= new XmlExtensionFunctionTable();

            // Bind to the instance, looking for a matching method (throws if no matching method)
            XmlExtensionFunction extFunc = _extFuncsLate.Bind(name, namespaceUri, args.Length, instance.GetType(), XmlQueryRuntime.LateBoundFlags);

            // Create array which will contain the actual arguments
            objActualArgs = new object[args.Length];

            for (int i = 0; i < args.Length; i++)
            {
                // 1. Assume that the input value can only have one of the following 5 Xslt types:
                //      xs:double, xs:string, xs:boolean, node* (can be rtf)
                // 2. Convert each Rtf value to a NodeSet containing one node.  Now the value may only have one of the 4 Xslt types.
                // 3. Convert from one of the 4 Xslt internal types to the Xslt internal type which is closest to the formal
                //    argument's Xml type (inferred from the Clr type of the formal argument).

                xmlTypeFormalArg = extFunc.GetXmlArgumentType(i);
                switch (xmlTypeFormalArg.TypeCode)
                {
                    case XmlTypeCode.Boolean: objActualArgs[i] = XsltConvert.ToBoolean(args[i]); break;
                    case XmlTypeCode.Double: objActualArgs[i] = XsltConvert.ToDouble(args[i]); break;
                    case XmlTypeCode.String: objActualArgs[i] = XsltConvert.ToString(args[i]); break;
                    case XmlTypeCode.Node:
                        if (xmlTypeFormalArg.IsSingleton)
                            objActualArgs[i] = XsltConvert.ToNode(args[i]);
                        else
                            objActualArgs[i] = XsltConvert.ToNodeSet(args[i]);
                        break;
                    case XmlTypeCode.Item:
                        objActualArgs[i] = args[i];
                        break;
                    default:
                        Debug.Fail($"This XmlTypeCode should never be inferred from a Clr type: {xmlTypeFormalArg.TypeCode}");
                        break;
                }

                // 4. Change the Clr representation to the Clr type of the formal argument
                clrTypeFormalArg = extFunc.GetClrArgumentType(i);
                if (xmlTypeFormalArg.TypeCode == XmlTypeCode.Item || !clrTypeFormalArg.IsAssignableFrom(objActualArgs[i].GetType()))
                    objActualArgs[i] = XmlQueryRuntime.ChangeTypeXsltArgument(xmlTypeFormalArg, objActualArgs[i], clrTypeFormalArg);
            }

            // 1. Invoke the late bound method
            objRet = extFunc.Invoke(instance, objActualArgs);

            // 2. Convert to IList<XPathItem>
            if (objRet == null && extFunc.ClrReturnType == typeof(void))
                return XmlQueryNodeSequence.Empty;

            return (IList<XPathItem>)_runtime.ChangeTypeXsltResult(XmlQueryTypeFactory.ItemS, objRet);
        }


        //-----------------------------------------------
        // Event
        //-----------------------------------------------

        /// <summary>
        /// Fire the XsltMessageEncounteredEvent, passing the specified text as the message.
        /// </summary>
        /// <summary>Xsl3: the trace listener of the argument list, for stylesheets compiled with EnableTracing.</summary>
        internal IXsltTraceListener? TraceListener => _argList?.TraceListener;

        internal bool HasTraceHandler => _argList?.TraceHandler != null;

        /// <summary>Xsl3: fn:trace, to XsltArgumentList.TraceHandler if there is one.</summary>
        internal void OnTrace(string label, IList<XPathItem> value)
        {
            if (_argList?.TraceHandler is Action<string, string> handler)
            {
                string text = string.Join(", ", System.Linq.Enumerable.Select(value, item => item switch
                {
                    XPathNavigator node => node.NodeType == XPathNodeType.Root ? node.InnerXml : node.OuterXml,
                    Xdm.FunctionItem function => function.Describe(),
                    _ => item.Value,
                }));
                _runtime.InvokeOnCaller(() => handler(label, text));
            }
        }

        public void OnXsltMessageEncountered(string message)
        {
            _argList?.xsltMessageEncountered?.Invoke(this, new XmlILQueryEventArgs(message));
        }
    }

    /// <summary>
    /// Simple implementation of XsltMessageEncounteredEventArgs.
    /// </summary>
    internal sealed class XmlILQueryEventArgs : XsltMessageEncounteredEventArgs
    {
        private readonly string _message;

        public XmlILQueryEventArgs(string message)
        {
            _message = message;
        }

        public override string Message
        {
            get { return _message; }
        }
    }
}
