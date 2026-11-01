// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Runtime;

public sealed partial class XmlQueryContext
{
    private static XmlReaderSettings InputReaderSettings(XmlResolver resolver, bool xslt2) => new()
    {
        NameTable = new NameTable(),
        DtdProcessing = xslt2 ? DtdProcessing.Parse : DtdProcessing.Prohibit,
        XmlResolver = xslt2 && !ReferenceEquals(resolver, XmlResolver.ThrowingResolver) ? resolver : null,
        MaxCharactersFromEntities = 10_000_000,
    };

    private readonly Streaming.InitialSource? _initialSource;
    private XPathNavigator? _initialSelection;
    private bool HasInitialSource => _initialSource != null || _forkContextOwner?.HasInitialSource == true;

    internal XPathNavigator InitialSelection(bool accumulators, Action<XPathNavigator>? observe = null)
    {
        if (_forkContextOwner != null)
        {
            if (_initialSelection is Streaming.StreamedNavigator)
                throw new InvalidOperationException("A fork's streamed focus must be supplied by its tee view");
            if (_initialSelection != null) return _initialSelection;
            if (_forkContextOwner.HasInitialSource)
                throw new InvalidOperationException("A fork runtime cannot open its parent's initial source");
            return DefaultDataSource;
        }
        void Configure(XPathNavigator selection)
        {
            if (_runtime.StreamedInitialMode && accumulators && selection is Streaming.StreamedNavigator streamed && streamed.Document.Observer == null)
                observe!(selection);
        }
        if (_runtime.StreamedInitialMode && accumulators && observe == null)
            throw new Xsl3.Xdm.XdmException("streaming-unimplemented", "This compiled entry point has no streamed accumulator bindings")
                { Name = new Xsl3.Xdm.QName("http://xsl3/err", "streaming-unimplemented") };
        if (_initialSelection != null) { Configure(_initialSelection); return _initialSelection; }
        if (_initialSource == null) return DefaultDataSource;
        XmlQueryRuntime.Cancellation.ThrowIfCancellationRequested();

        object input = _initialSource.Input;
        XmlReader? reader = input as XmlReader;
        Stream? ownedStream = null;
        bool ownsReader = false;
        Uri? uri = null;
        DocumentFragment? fragment = null;
        bool completed = false;
        DocumentResources.ReadClaim? readClaim = null;
        try
        {
            if (input is Uri location)
            {
                uri = _dataSources.ResolveUri(null, location.OriginalString);
                fragment = DocumentFragment.Parse(uri);
                uri = fragment.Value.DocumentUri;
                readClaim = _runtime.Documents.BeginRead(fragment.Value.ResourceUri);
                input = _dataSources.GetEntity(fragment.Value.ResourceUri, null, null)
                    ?? throw new Xsl3.Xdm.XdmException("FODC0002", $"No initial source was returned for '{uri}'");
                reader = input as XmlReader;
                ownsReader = reader != null;
                ownedStream = input as Stream;
            }
            if (input is Stream stream)
            {
                XmlReaderSettings settings = InputReaderSettings(_dataSources, _runtime.IsXslt2);
                if (settings.DtdProcessing == DtdProcessing.Parse)
                    settings.XmlResolver = _runtime.Documents.TrackResolver(XmlReaderSettingsInternals.GetEffectiveResolver(settings));
                settings.CloseInput = ownedStream != null;
                reader = XmlReader.Create(stream, settings, fragment?.ResourceUri.ToString() ?? uri?.ToString() ?? _initialSource.BaseUri?.OriginalString ?? "");
                ownsReader = true;
            }
            if (reader == null)
                throw new Xsl3.Xdm.XdmException("FODC0002", "An initial source resolver must return a Stream or XmlReader");
            if (uri == null && Uri.TryCreate(reader.BaseURI, UriKind.Absolute, out Uri? readerUri)) uri = readerUri;
            if (uri != null)
            {
                readClaim ??= _runtime.Documents.BeginRead(uri);
                readClaim.Commit();
            }
            if (fragment is { Id: not null } selected)
            {
                reader = selected.Select(reader, ownsReader, XmlQueryRuntime.Cancellation);
                ownsReader = true;
            }
            reader = ScopePositionedReader(reader, ref ownsReader);
            XmlReader filtered = WhitespaceRuleReader.CreateReader(reader, _wsRules, Tree.CompactDocument.StripInputAnnotations);
            if (_runtime.StreamedInitialMode)
            {
                var chain = new Streaming.StreamedInputReader(filtered, ownsReader, XmlQueryRuntime.Cancellation,
                    Tree.CompactDocument.StripInputAnnotations);
                var document = new Streaming.StreamedDocument(chain, ownsReader: true, cancellation: XmlQueryRuntime.Cancellation);
                _runtime.RegisterStreamedSource(document);
                _initialSelection = document.CreateNavigator();
                ownsReader = false; // the runtime now owns the chain
                ownedStream = null;
                Configure(_initialSelection);
            }
            else
            {
                _initialSelection = BuildTree(filtered);
                if (uri != null) _runtime.Documents.Cache(uri, _initialSelection);
            }
            Streaming.StreamingExecutionCapture.RecordSource(Streaming.StreamingExecutionCapture.SourceKind.Initial,
                _runtime.StreamedInitialMode, _initialSelection);
            completed = true;
            return _initialSelection;
        }
        catch (Exception error) when (XmlException.IsCatchableException(error))
        {
            Streaming.StreamingExecutionCapture.RecordSourceFailure(Streaming.StreamingExecutionCapture.SourceKind.Initial,
                _runtime.StreamedInitialMode);
            if (error is Xsl3.Xdm.XdmException or OperationCanceledException) throw;
            DocumentResources.RethrowReadControlError(error);
            throw new Xsl3.Xdm.XdmException("FODC0002", $"Could not read the initial source: {error.Message}");
        }
        finally
        {
            try
            {
                if (ownsReader) reader?.Dispose();
                else if (reader == null) ownedStream?.Dispose();
            }
            catch (Exception cleanup) when (!completed && XmlException.IsCatchableException(cleanup)) { }
            finally { readClaim?.Dispose(); }
        }
    }

    private static XmlReader ScopePositionedReader(XmlReader reader, ref bool ownsReader)
    {
        if (reader.ReadState != ReadState.Interactive || reader.NodeType != XmlNodeType.Element) return reader;
        // Xsl3 (P7C): both entry paths expose only the selected subtree. Disposing its view
        // leaves a borrowed reader at the boundary, and also closes an owned reader.
        XmlReader subtree = DocumentFragment.ScopePositionedReader(reader, ownsReader);
        ownsReader = true;
        return subtree;
    }

    // Accumulator applicability follows the initial selection, not an independently supplied global context.
    internal bool IsInitialSelectionDocument(XPathNavigator root)
    {
        if (_forkContextOwner != null) return _forkContextOwner.IsInitialSelectionDocument(root);
        static bool SameDocument(XPathNavigator node, XPathNavigator root)
        {
            XPathNavigator document = node.Clone();
            document.MoveToRoot();
            return document.IsSamePosition(root);
        }
        if (_initialSource != null) return _initialSelection != null && SameDocument(_initialSelection, root);
        if (_argList?.GetParam(InitialFunctionArguments.Selection.Name, InitialFunctionArguments.Selection.Namespace) is IList<XPathItem> selection)
        {
            foreach (XPathItem item in selection)
                if (item is XPathNavigator node && SameDocument(node, root)) return true;
            return false;
        }
        return _defaultDataSource != null && SameDocument(_defaultDataSource, root);
    }

    internal void CloseInitialSource()
    {
        if (_forkContextOwner != null) return;
        if (_initialSelection is Streaming.StreamedNavigator streamed)
            _runtime.CloseStreamedSource(streamed.Document);
    }
}
