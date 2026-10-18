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
        private readonly bool _prohibitDtd;
        private readonly WhitespaceHandling _whitespaceHandling;
        private readonly XmlResolver? _xmlResolver;

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
                    _prohibitDtd = (xmlTextReader.DtdProcessing == DtdProcessing.Prohibit);
                    _whitespaceHandling = xmlTextReader.WhitespaceHandling;
                    _xmlResolver = XmlReaderInternals.GetResolver(xmlTextReader);
                }
                else
                {
                    _entityHandling = EntityHandling.ExpandEntities;
                    _namespaces = true;
                    _normalization = true;
                    _prohibitDtd = true;
                    _whitespaceHandling = WhitespaceHandling.All;
                    _xmlResolver = null;
                }
            }
        }

        public XmlReader CreateReader(Stream stream, string? baseUri)
        {
            XmlReader reader;
            if (_xmlReaderSettings != null)
            {
                reader = XmlTextReader.Create(stream, _xmlReaderSettings, baseUri);
            }
            else
            {
                // Xsl3: upstream creates the internal XmlTextReaderImpl; XmlTextReader wraps exactly that.
                XmlTextReader readerImpl = new XmlTextReader(baseUri!, stream, _xmlNameTable!);
                readerImpl.EntityHandling = _entityHandling;
                readerImpl.Namespaces = _namespaces;
                readerImpl.Normalization = _normalization;
                readerImpl.DtdProcessing = _prohibitDtd ? DtdProcessing.Prohibit : DtdProcessing.Parse;
                readerImpl.WhitespaceHandling = _whitespaceHandling;
                readerImpl.XmlResolver = _xmlResolver;
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
