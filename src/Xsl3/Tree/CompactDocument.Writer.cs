// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Tree
{
    public sealed partial class CompactDocument
    {
        /// <summary>XSL3_COMPACT_TREE=1 or 0 makes every tree compact, or none (for testing); otherwise null.</summary>
        internal static readonly bool? Override = Environment.GetEnvironmentVariable("XSL3_COMPACT_TREE") switch
        {
            "1" => true,
            "0" => false,
            _ => null,
        };

        /// <summary>
        /// A writer for a temporary tree, and the way to its root once the writer is closed: a compact tree, or (when
        /// compact is false) XPathDocument's, as upstream builds them.
        /// </summary>
        internal static (XmlRawWriter Writer, Func<XPathNavigator> Root) TemporaryTree(XmlNameTable nameTable, string baseUri, bool fragment, bool noDocumentUri, bool compact)
        {
            if (Override ?? compact)
            {
                var writer = new Writer(nameTable, baseUri, fragment);
                writer.Document.NoDocumentUri = noDocumentUri;
                return (writer, () => writer.Document.CreateNavigator());
            }
            var doc = new InternalXPathDocument(nameTable) { NoDocumentUri = noDocumentUri };
            XmlRawWriter raw = doc.LoadFromWriter(InternalXPathDocument.LoadFlags.AtomizeNames | (fragment ? InternalXPathDocument.LoadFlags.Fragment : InternalXPathDocument.LoadFlags.None), baseUri);
            return (raw, doc.CreateNavigator);
        }

        /// <summary>
        /// Builds a compact temporary tree (SPEC §14.4) from the raw-writer events XmlQueryOutput sends, as
        /// XPathDocumentBuilder does for XPathDocument: text blocks merge, CDATA and raw text become text, and a
        /// fragment (a tree without a document node) has its one top-level node, even a parentless attribute or
        /// namespace node, as its root.
        /// </summary>
        internal sealed class Writer : XmlRawWriter
        {
            private readonly Builder _builder;
            private CompactDocument? _result;

            // The attribute being written, and its value so far.
            private string? _attrPrefix, _attrLocal, _attrNs;
            private string? _attrValue;
            private StringBuilder? _attrMore;

            public Writer(XmlNameTable nameTable, string baseUri, bool fragment)
            {
                _builder = new Builder(nameTable, baseUri, fragment, small: true);
                _builder.Document._writerBuilt = true;
            }

            /// <summary>The tree (complete once the writer is closed).</summary>
            public CompactDocument Document => _result ?? _builder.Document;

            public override void WriteDocType(string name, string? pubid, string? sysid, string? subset)
            {
            }

            public override void WriteStartElement(string? prefix, string localName, string? ns) =>
                _builder.StartElement(prefix ?? string.Empty, localName, ns ?? string.Empty, null);

            /// <summary>Xsl3: the base URI of the element just started, a copy's (XSLT 3.0 §11.9.1, §11.9.2).</summary>
            internal void SetElementBaseUri(string baseUri) => _builder.SetElementBaseUri(baseUri);

        internal void SetDocumentBaseUri(string baseUri) => _builder.SetDocumentBaseUri(baseUri);

            public override void WriteEndElement() => _builder.EndElement(emptyTag: true);

            public override void WriteFullEndElement() => _builder.EndElement(emptyTag: false);

            internal override void WriteEndElement(string prefix, string localName, string namespaceName) => _builder.EndElement(emptyTag: true);

            internal override void WriteFullEndElement(string prefix, string localName, string namespaceName) => _builder.EndElement(emptyTag: false);

            public override void WriteStartAttribute(string? prefix, string localName, string? ns)
            {
                _attrPrefix = prefix ?? string.Empty;
                _attrLocal = localName;
                _attrNs = ns ?? string.Empty;
                _attrValue = null;
                _attrMore?.Clear();
            }

            public override void WriteEndAttribute()
            {
                string value = _attrMore is { Length: > 0 } more ? (_attrValue ?? string.Empty) + more.ToString() : _attrValue ?? string.Empty;
                _builder.Attribute(_attrPrefix!, _attrLocal!, _attrNs!, value);
                _attrLocal = null;
                _attrValue = null;
                _attrMore?.Clear();
            }

            internal override void WriteNamespaceDeclaration(string prefix, string namespaceName)
            {
                if (prefix == Xsl3.Runtime.XmlQueryOutput.NoInheritPrefix)
                {
                    NoInherit();
                    return;
                }
                _builder.NamespaceDeclaration(prefix, namespaceName);
            }

            /// <summary>Xsl3: inherit-namespaces="no" on the open element: its children don't inherit its namespaces.</summary>
            internal void NoInherit() => _builder.NoInherit();

            internal override void StartElementContent()
            {
            }

            internal override void WriteXmlDeclaration(XmlStandalone standalone)
            {
            }

            internal override void WriteXmlDeclaration(string xmldecl)
            {
            }

            public override void WriteCData(string? text) => Text(text, XPathNodeType.Text);

            public override void WriteComment(string? text) => _builder.Comment(text ?? string.Empty);

            public override void WriteProcessingInstruction(string name, string? text) =>
                _builder.ProcessingInstruction(name, text ?? string.Empty);

            public override void WriteWhitespace(string? ws) => Text(ws, XPathNodeType.Whitespace);

            public override void WriteString(string? text) => Text(text, XPathNodeType.Text);

            public override void WriteChars(char[] buffer, int index, int count) => Text(new string(buffer, index, count), XPathNodeType.Text);

            public override void WriteRaw(string data) => Text(data, XPathNodeType.Text, raw: true);

            public override void WriteRaw(char[] buffer, int index, int count) => Text(new string(buffer, index, count), XPathNodeType.Text, raw: true);

            public override void WriteEntityRef(string name) => throw new NotImplementedException();

            public override void WriteCharEntity(char ch) => Text(char.ToString(ch), XPathNodeType.Text);

            public override void WriteSurrogateCharEntity(char lowChar, char highChar) => Text(new string([highChar, lowChar]), XPathNodeType.Text);

            /// <summary>Text goes to the attribute being written, if there is one, and otherwise to the tree.</summary>
            private void Text(string? text, XPathNodeType type, bool raw = false)
            {
                text ??= string.Empty;
                if (_attrLocal != null)
                {
                    if (_attrValue == null)
                    {
                        _attrValue = text;
                    }
                    else
                    {
                        (_attrMore ??= new StringBuilder()).Append(text);
                    }
                    return;
                }
                if (text.Length == 0)
                {
                    // XPathDocumentBuilder's text builder ignores empty blocks.
                    return;
                }
                _builder.Text(text, type, raw);
            }

            public override void Close()
            {
                _result ??= _builder.Finish();
            }

            public override void Flush()
            {
            }
        }
    }
}
