// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace Xsl3.Runtime
{
    /// <summary>
    /// </summary>
    internal sealed class WhitespaceRuleReader : XmlWrappingReader
    {
        private readonly WhitespaceRuleLookup _wsRules;
        private readonly bool _stripAnnotations;
        private readonly BitStack _stkStrip;
        private bool _shouldStrip, _preserveAdjacent;
        private string? _val;

        public static XmlReader CreateReader(XmlReader baseReader, WhitespaceRuleLookup? wsRules, bool stripAnnotations = false)
        {
            if (wsRules == null)
            {
                return baseReader;    // There is no rules to process
            }
            XmlReaderSettings? readerSettings = baseReader.Settings;
            if (readerSettings != null)
            {
                if (readerSettings.IgnoreWhitespace)
                {
                    return baseReader;        // V2 XmlReader that strips all WS
                }
            }
            else
            {
                XmlTextReader? txtReader = baseReader as XmlTextReader;
                if (txtReader != null && txtReader.WhitespaceHandling == WhitespaceHandling.None)
                {
                    return baseReader;        // V1 XmlTextReader that strips all WS
                }
                // Xsl3: XmlTextReaderImpl is internal to System.Private.Xml; see XmlReaderInternals.
                if (XmlReaderInternals.IsTextReaderImplStrippingAllWhitespace(baseReader))
                {
                    return baseReader;        // XmlTextReaderImpl that strips all WS
                }
            }
            return new WhitespaceRuleReader(baseReader, wsRules, stripAnnotations);
        }

        private WhitespaceRuleReader(XmlReader baseReader, WhitespaceRuleLookup wsRules, bool stripAnnotations) : base(baseReader)
        {
            Debug.Assert(wsRules != null);

            _val = null;
            _stkStrip = new BitStack();
            _shouldStrip = false;
            _preserveAdjacent = false;

            _wsRules = wsRules;
            _stripAnnotations = stripAnnotations;
            _wsRules.Atomize(baseReader.NameTable);
        }

        /// <summary>
        /// Override Value in order to possibly prepend extra whitespace.
        /// </summary>
        public override string Value
        {
            get { return _val ?? base.Value; }
        }

        /// <summary>
        /// Override Read in order to search for strippable whitespace, to concatenate adjacent text nodes, and to
        /// resolve entities.
        /// </summary>
        public override bool Read()
        {
            string? ws = null;

            // Clear text value
            _val = null;

            while (base.Read())
            {
                switch (base.NodeType)
                {
                    case XmlNodeType.Element:
                        // Push boolean indicating whether whitespace children of this element should be stripped
                        if (!base.IsEmptyElement)
                        {
                            _stkStrip.PushBit(_shouldStrip);

                            // Strip if rules say we should and we're not within the scope of xml:space="preserve"
                            _shouldStrip = _wsRules.ShouldStripSpace(base.LocalName, base.NamespaceURI) && (base.XmlSpace != XmlSpace.Preserve)
                                // Xsl3 (P6): not in an element whose type gives it simple content (XSLT 3.0 §4.3).
                                && (_stripAnnotations || !(base.SchemaInfo?.SchemaType is System.Xml.Schema.XmlSchemaSimpleType or System.Xml.Schema.XmlSchemaComplexType { ContentType: System.Xml.Schema.XmlSchemaContentType.TextOnly }));
                        }
                        break;

                    case XmlNodeType.EndElement:
                        // Restore parent shouldStrip setting
                        _shouldStrip = _stkStrip.PopBit();
                        break;

                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                        // If preserving adjacent text, don't perform any further checks
                        if (_preserveAdjacent)
                            return true;

                        if (_shouldStrip)
                        {
                            // Reader may report whitespace as Text or CDATA
                            if (XmlCharType.IsOnlyWhitespace(base.Value))
                                goto case XmlNodeType.Whitespace;

                            // If whitespace was cached, then prepend it to text or CDATA value
                            if (ws != null)
                                _val = string.Concat(ws, base.Value);

                            // Preserve adjacent whitespace
                            _preserveAdjacent = true;
                            return true;
                        }
                        break;

                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        // If preserving adjacent text, don't perform any further checks
                        if (_preserveAdjacent)
                            return true;

                        if (_shouldStrip)
                        {
                            // Save whitespace until it can be determined whether it will be stripped
                            ws = ws == null ?
                                base.Value :
                                string.Concat(ws, base.Value);

                            // Read next event
                            continue;
                        }
                        break;
                    case XmlNodeType.EndEntity:
                        // Read next event
                        continue;
                }

                // No longer preserve adjacent space
                _preserveAdjacent = false;
                return true;
            }

            return false;
        }
    }
}
