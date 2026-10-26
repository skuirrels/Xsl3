// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;

namespace Xsl3
{
    internal sealed class QueryReaderSettings
    {
        private readonly bool _validatingReader;
        private readonly XmlReaderSettings? _xmlReaderSettings;
        private readonly XmlNameTable? _xmlNameTable;
        private readonly EntityHandling _entityHandling;
        private readonly bool _namespaces;
        private readonly bool _normalization;
        private readonly DtdProcessing _dtdProcessing;
        private readonly WhitespaceHandling _whitespaceHandling;
        private readonly XmlResolver? _xmlResolver;

        // Reader policy is immutable after capture, but NameTable is not thread-safe. Each
        // fork uses its own table while retaining the configured resolver and parser options.
        private QueryReaderSettings(QueryReaderSettings owner)
        {
            _validatingReader = owner._validatingReader;
            _xmlReaderSettings = owner._xmlReaderSettings?.Clone();
            if (_xmlReaderSettings != null) _xmlReaderSettings.NameTable = new NameTable();
            _xmlNameTable = _xmlReaderSettings == null ? new NameTable() : null;
            _entityHandling = owner._entityHandling;
            _namespaces = owner._namespaces;
            _normalization = owner._normalization;
            _dtdProcessing = owner._dtdProcessing;
            _whitespaceHandling = owner._whitespaceHandling;
            _xmlResolver = owner._xmlResolver;
        }

        internal QueryReaderSettings Fork() => new(this);

        public QueryReaderSettings(XmlNameTable xmlNameTable)
        {
            Debug.Assert(xmlNameTable != null);
            _xmlReaderSettings = new XmlReaderSettings();
            _xmlReaderSettings.NameTable = xmlNameTable;
            _xmlReaderSettings.ConformanceLevel = ConformanceLevel.Document;
            _xmlReaderSettings.XmlResolver = null;
            _xmlReaderSettings.DtdProcessing = DtdProcessing.Prohibit;
            _xmlReaderSettings.CloseInput = true;
        }

        // Xsl3: explicit settings for documents the query loads (the XPath 3.1 API parses DTDs; XSLT 1.0 keeps upstream's defaults).
        public QueryReaderSettings(XmlReaderSettings settings)
        {
            _xmlReaderSettings = settings.Clone();
            _xmlReaderSettings.NameTable ??= new NameTable();
            _xmlReaderSettings.CloseInput = true;
        }

        public QueryReaderSettings(XmlReader reader)
        {
#pragma warning disable 618
            XmlValidatingReader? valReader = reader as XmlValidatingReader;
#pragma warning restore 618
            if (valReader != null)
            {
                // Unwrap validation reader
                _validatingReader = true;
                reader = valReader.Reader; // Xsl3: public equivalent of the internal Impl.Reader
            }

            _xmlReaderSettings = reader.Settings;
            if (_xmlReaderSettings != null)
            {
                _xmlReaderSettings = _xmlReaderSettings.Clone();
                _xmlReaderSettings.NameTable = reader.NameTable;
                _xmlReaderSettings.CloseInput = true;
                _xmlReaderSettings.LineNumberOffset = 0;
                _xmlReaderSettings.LinePositionOffset = 0;
                // Xsl3: XmlTextReaderImpl is internal to System.Private.Xml; see XmlReaderInternals.
                if (XmlReaderInternals.TryGetTextReaderImplResolver(reader, out XmlResolver? implResolver))
                {
                    _xmlReaderSettings.XmlResolver = implResolver;
                }
            }
            else
            {
                _xmlNameTable = reader.NameTable;
                XmlTextReader? xmlTextReader = reader as XmlTextReader;
                if (xmlTextReader != null)
                {
                    // Xsl3: XmlTextReader's public properties forward to the internal XmlTextReaderImpl that upstream reads directly.
                    _entityHandling = xmlTextReader.EntityHandling;
                    _namespaces = xmlTextReader.Namespaces;
                    _normalization = xmlTextReader.Normalization;
                    _dtdProcessing = xmlTextReader.DtdProcessing;
                    _whitespaceHandling = xmlTextReader.WhitespaceHandling;
                    _xmlResolver = XmlReaderInternals.GetResolver(xmlTextReader);
                }
                else
                {
                    _entityHandling = EntityHandling.ExpandEntities;
                    _namespaces = true;
                    _normalization = true;
                    _dtdProcessing = DtdProcessing.Prohibit;
                    _whitespaceHandling = WhitespaceHandling.All;
                    _xmlResolver = null;
                }
            }
        }

        public XmlReader CreateReader(Stream stream, string? baseUri, Runtime.DocumentResources? resources = null)
        {
            XmlReader reader;
            if (_xmlReaderSettings != null)
            {
                XmlReaderSettings settings = _xmlReaderSettings;
                if (resources != null && settings.DtdProcessing == DtdProcessing.Parse &&
                    XmlReaderSettingsInternals.GetEffectiveResolver(settings) is { } resolver)
                {
                    settings = settings.Clone();
                    settings.XmlResolver = resources.TrackResolver(resolver);
                }
                reader = XmlTextReader.Create(stream, settings, baseUri);
            }
            else
            {
                // Xsl3: upstream creates the internal XmlTextReaderImpl; XmlTextReader wraps exactly that.
                XmlTextReader readerImpl = new XmlTextReader(baseUri!, stream, _xmlNameTable!);
                readerImpl.EntityHandling = _entityHandling;
                readerImpl.Namespaces = _namespaces;
                readerImpl.Normalization = _normalization;
                readerImpl.DtdProcessing = _dtdProcessing;
                readerImpl.WhitespaceHandling = _whitespaceHandling;
                readerImpl.XmlResolver = resources != null && _dtdProcessing == DtdProcessing.Parse ? resources.TrackResolver(_xmlResolver) : _xmlResolver;
                reader = readerImpl;
            }
            if (_validatingReader)
            {
#pragma warning disable 618
                reader = new XmlValidatingReader(reader);
#pragma warning restore 618
            }
            return reader;
        }

        public XmlNameTable NameTable
        {
            get { return _xmlReaderSettings != null ? _xmlReaderSettings.NameTable! : _xmlNameTable!; }
        }
    }
}
