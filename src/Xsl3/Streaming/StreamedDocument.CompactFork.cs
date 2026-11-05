// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Streaming;

internal sealed partial class StreamedDocument
{
    private CompactForkProducer? _compactProducer;
    internal bool HasCompactForkProducer => RuntimeFeature.IsDynamicCodeSupported && _compactProducer != null;

    private bool CompactObservationsAbsent => _observer == null && _capturing == null && _valueReplays == null
        && _consumedHeaders == null && _consumptionMatches == null && _descendants == null;

    private bool CanUseCompactForkProducer(Node boundary)
    {
        // JIT measurements benefit from compact frames. NativeAOT retains the
        // original navigator producer: the compact implementation increases RSS.
        if (!RuntimeFeature.IsDynamicCodeSupported) return false;
        if (_disposed || !ReferenceEquals(boundary, Root) || _current != Root || Root.ChildrenStarted || _ready
            || _open.Count != 1 || _forkBoundary != null || _completeValidation || !CompactObservationsAbsent
            || _idDeclarations != null || _idrefDeclarations != null || _reader.ReadState != ReadState.Initial
            || _reader.Settings is not { ValidationType: ValidationType.None, DtdProcessing: DtdProcessing.Prohibit }
            || _reader.SchemaInfo != null) return false;
        XmlReader input = _reader;
        while (input is StreamedInputReader or Runtime.WhitespaceRuleReader)
            input = ((XmlWrappingReader)input).InnerReader;
        return XmlReaderInternals.IsTextReaderImpl(input);
    }

    // Mutable producer state occupies only the open ancestor stack. Published headers
    // own an immutable metadata record; they never point at these mutable stack entries.
    private struct CompactFrame
    {
        internal long Number, FirstChild;
        internal int Depth;
        internal XPathNodeType Kind;
        internal string LocalName, Namespace, Prefix, Text, BaseUri, Language;
        internal Attribute[] Attributes;
        internal Namespace[] Namespaces;
        internal bool Empty, Ended, Started;
        internal CompactNamespaceScope? Scope;
        internal CompactHeaderData? Published;

        internal CompactFrame(long number, XPathNodeType kind, int depth, string baseUri, string language)
        {
            Number = number; Kind = kind; Depth = depth; BaseUri = baseUri; Language = language;
            LocalName = Namespace = Prefix = Text = "";
            Attributes = Array.Empty<Attribute>(); Namespaces = Array.Empty<Namespace>();
            FirstChild = -1; Empty = Ended = Started = false; Scope = null; Published = null;
        }
    }

    private sealed class CompactNamespaceScope(CompactNamespaceScope? parent, Namespace[] declarations)
    {
        private readonly CompactNamespaceScope? _parent = parent;
        private readonly Namespace[] _declarations = declarations;
        internal string? Lookup(string prefix)
        {
            for (CompactNamespaceScope? scope = this; scope != null; scope = scope._parent)
                for (int i = scope._declarations.Length - 1; i >= 0; i--)
                    if (scope._declarations[i].Prefix == prefix) return scope._declarations[i].Uri;
            return prefix.Length == 0 ? "" : null;
        }
    }

    // The immutable start event is also the metadata owner retained until its end.
    // End events reference that owner; no event points into the mutable slot array.
    private sealed class CompactHeaderData(in CompactFrame frame) : ForkEvent(in frame, false)
    {
        protected override long CompactNumber { get; } = frame.Number;
        protected override int CompactDepth { get; } = frame.Depth;
        protected override XPathNodeType CompactKind { get; } = frame.Kind;
        protected override string CompactLocalName { get; } = frame.LocalName;
        protected override string CompactNamespace { get; } = frame.Namespace;
        protected override string CompactPrefix { get; } = frame.Prefix;
        protected override string CompactText { get; } = frame.Text;
        protected override string CompactBaseUri { get; } = frame.BaseUri;
        protected override string CompactLanguage { get; } = frame.Language;
        private readonly CompactNamespaceScope? _scope = frame.Scope;
        protected override string? CompactLookupNamespace(string prefix) => _scope?.Lookup(prefix) ?? (prefix.Length == 0 ? "" : null);
    }

    private sealed class CompactForkEvent(in CompactFrame frame, CompactHeaderData data, bool end) : ForkEvent(in frame, end)
    {
        protected override long CompactNumber => data.Number;
        protected override int CompactDepth => data.Depth;
        protected override XPathNodeType CompactKind => data.Kind;
        protected override string CompactLocalName => data.LocalName;
        protected override string CompactNamespace => data.Namespace;
        protected override string CompactPrefix => data.Prefix;
        protected override string CompactText => data.Text;
        protected override string CompactBaseUri => data.BaseUri;
        protected override string CompactLanguage => data.Language;
        protected override string? CompactLookupNamespace(string prefix) => data.LookupNamespace(prefix);
    }

    private sealed class CompactForkProducer : IDisposable
    {
        private readonly StreamedDocument _source;
        private readonly Node _boundary;
        private readonly ForkInputProjection.Matcher _projection;
        // Only the open ancestors and one current leaf/closed element are live.
        // Array indexing exposes slots by reference; no List<Frame> or current-frame
        // struct copies occur in the token loop.
        private CompactFrame[] _frames = new CompactFrame[4];
        private int _openCount = 1, _currentIndex;
        private bool _complete, _disposed;
        internal int RetainedFrames => _openCount + (_currentIndex >= _openCount ? 1 : 0);

        internal CompactForkProducer(StreamedDocument source, Node boundary, ForkInputProjection.Matcher projection)
        {
            _source = source; _boundary = boundary; _projection = projection;
            _frames[0] = new(boundary.Number, boundary.Kind, boundary.Depth, boundary.BaseUri, boundary.Language);
            source._compactProducer = this;
        }

        private ref CompactFrame Child(XPathNodeType kind)
        {
            if (_openCount == _frames.Length) Array.Resize(ref _frames, checked(_frames.Length * 2));
            ref CompactFrame parent = ref _frames[_openCount - 1];
            long number = ++_source._number;
            parent.Started = true;
            if (parent.FirstChild < 0) parent.FirstChild = number;
            if (parent.Kind == XPathNodeType.Root)
            {
                _boundary.ChildrenStarted = true;
                _boundary.FirstChild = parent.FirstChild;
            }
            _currentIndex = _openCount;
            _frames[_currentIndex] = new(number, kind, parent.Depth + 1, _source._reader.BaseURI, _source._reader.XmlLang) { Scope = parent.Scope };
            if (kind == XPathNodeType.Element) _openCount++;
            return ref _frames[_currentIndex];
        }

        private void ReleaseCurrentOutsideAncestors()
        {
            if (_currentIndex >= _openCount) _frames[_currentIndex] = default;
        }

        private ForkProngs.SourceRead<ForkEvent> Finish()
        {
            ReleaseCurrentOutsideAncestors();
            _complete = true; _currentIndex = 0;
            ref CompactFrame root = ref _frames[0];
            root.Ended = root.Started = true;
            _boundary.Ended = _boundary.ChildrenStarted = true;
            _boundary.FirstChild = root.FirstChild;
            return new(new ForkEvent(_boundary, true), false);
        }

        internal ForkProngs.SourceRead<ForkEvent> Read()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _source.Check();
            if (_complete) return new(null, true);
            if (_openCount > 1 && _frames[_openCount - 1].Ended) _openCount--;
            XmlReader reader = _source._reader;
            bool end = false;
            while (true)
            {
                bool read = _source._ready || reader.Read();
                _source._ready = false;
                if (!read) return Finish();
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                    {
                        ref CompactFrame frame = ref Child(XPathNodeType.Element);
                        frame.LocalName = reader.LocalName; frame.Namespace = reader.NamespaceURI; frame.Prefix = reader.Prefix;
                        frame.Empty = frame.Ended = reader.IsEmptyElement;
                        _source.ReadElementHeaders(frame.Number, frame.Depth == _boundary.Depth + 1,
                            out frame.Attributes, out frame.Namespaces, out _);
                        if (frame.Namespaces.Length != 0) frame.Scope = new(frame.Scope, frame.Namespaces);
                        break;
                    }
                    case XmlNodeType.EndElement:
                    {
                        ReleaseCurrentOutsideAncestors();
                        _currentIndex = _openCount - 1;
                        ref CompactFrame frame = ref _frames[_currentIndex];
                        frame.Ended = frame.Started = true;
                        end = true;
                        break;
                    }
                    case XmlNodeType.Text: case XmlNodeType.CDATA: case XmlNodeType.SignificantWhitespace: case XmlNodeType.Whitespace:
                    {
                        int textIndex = -1;
                        string first = ""; StringBuilder? value = null;
                        do
                        {
                            if (_source.KeepText(_openCount > 1))
                            {
                                XPathNodeType kind = reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA ? XPathNodeType.Text
                                    : reader.NodeType == XmlNodeType.SignificantWhitespace && reader.XmlSpace == XmlSpace.Preserve
                                        ? XPathNodeType.SignificantWhitespace : XPathNodeType.Whitespace;
                                if (textIndex < 0) { Child(kind); textIndex = _currentIndex; }
                                if (first.Length == 0) first = reader.Value;
                                else (value ??= new StringBuilder(first)).Append(reader.Value);
                                ref CompactFrame text = ref _frames[textIndex];
                                if (kind == XPathNodeType.Text || kind == XPathNodeType.SignificantWhitespace && text.Kind == XPathNodeType.Whitespace)
                                    text.Kind = kind;
                            }
                            _source.AdvanceTextReader();
                        } while (_source._ready && reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace);
                        if (textIndex < 0)
                        {
                            if (_source._ready) continue;
                            return Finish();
                        }
                        ref CompactFrame currentText = ref _frames[textIndex];
                        currentText.Text = value?.ToString() ?? first;
                        if (currentText.Text.Length == 0)
                        {
                            ref CompactFrame parent = ref _frames[_openCount - 1];
                            if (parent.FirstChild == currentText.Number)
                            {
                                parent.FirstChild = -1;
                                if (parent.Kind == XPathNodeType.Root) _boundary.FirstChild = -1;
                            }
                            if (_source._ready) continue;
                            return Finish();
                        }
                        break;
                    }
                    case XmlNodeType.Comment: case XmlNodeType.ProcessingInstruction:
                    {
                        ref CompactFrame frame = ref Child(reader.NodeType == XmlNodeType.Comment ? XPathNodeType.Comment : XPathNodeType.ProcessingInstruction);
                        frame.LocalName = reader.NodeType == XmlNodeType.ProcessingInstruction ? reader.LocalName : "";
                        frame.Text = reader.Value;
                        break;
                    }
                    default: continue;
                }
                break;
            }
            ref CompactFrame current = ref _frames[_currentIndex];
            if (!_projection.Keep(current.Depth, current.Kind, current.LocalName, current.Namespace, end)) return default;
            current.Published ??= new(in current);
            ForkEvent published = end ? new CompactForkEvent(in current, current.Published, true) : current.Published;
            return new(published, false);
        }

        private static Node Restore(in CompactFrame frame, Node? parent) => new(frame.Number, frame.Kind, parent)
        {
            LocalName = frame.LocalName, Namespace = frame.Namespace, Prefix = frame.Prefix, Text = frame.Text,
            BaseUri = frame.BaseUri, Language = frame.Language, Attributes = frame.Attributes, Namespaces = frame.Namespaces,
            EmptyElement = frame.Empty, Ended = frame.Ended, ChildrenStarted = frame.Started, FirstChild = frame.FirstChild,
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(_source._compactProducer, this)) _source._compactProducer = null;
            try
            {
                if (_source._disposed) return;
                ref CompactFrame root = ref _frames[0];
                _boundary.FirstChild = root.FirstChild; _boundary.ChildrenStarted = root.Started; _boundary.Ended = root.Ended;
                _source._open.Clear(); _source._open.Add(_boundary);
                Node parent = _boundary;
                for (int i = 1; i < _openCount; i++)
                {
                    parent = Restore(in _frames[i], parent);
                    _source._open.Add(parent);
                }
                _source._current = _currentIndex < _openCount ? _source._open[_currentIndex]
                    : Restore(in _frames[_currentIndex], _source._open[_frames[_currentIndex].Depth - _boundary.Depth - 1]);
            }
            finally { Array.Clear(_frames); }
        }
    }
}
