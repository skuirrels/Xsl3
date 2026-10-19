// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Diagnostics;
using System.Xml;
using Xsl3.Qil;
using ContextInfo = Xsl3.Xslt.XsltInput.ContextInfo;
using F = Xsl3.Xslt.AstFactory;
using QName = Xsl3.Xslt.XsltInput.DelayedQName;
using TypeFactory = Xsl3.XmlQueryTypeFactory;
using XsltAttribute = Xsl3.Xslt.XsltInput.XsltAttribute;

namespace Xsl3.Xslt
{
    // Xsl3: the XSLT 2.0 declarations and instructions (SPEC §22, P2). Upstream's dormant #if XSLT2 parsers are
    // replaced by these; the AST carries 2.0 attributes in XslNode.V2.
    internal sealed partial class XsltLoader
    {
        // The modes of the template being loaded when its mode attribute lists several, or is #all (2.0).
        private List<QilName>? _templateModes;
        private bool _templateAllModes;

        // http://www.w3.org/TR/xslt20/#element-sequence
        private readonly XsltAttribute[] _sequenceAttributes = {
            new XsltAttribute("select", V2Req),
        };
        private XslNode XslSequence()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_sequenceAttributes);
            string? select = ParseStringAttribute(0, "select");
            // 3.0 allows a sequence constructor instead of select; xsl:fallback children are ignored.
            List<XslNode> content = LoadFallbacksOrContent();
            CheckError(select != null && content.Count != 0, /*[XTSE3185]*/SR.Xslt_ElementCntSel, "xsl:sequence");
            return SetInfo(F.XslNode(XslNodeType.Sequence, null, select, _input.XslVersion), content, ctxInfo);
        }

        // Children other than xsl:fallback: a sequence constructor (3.0); fallbacks are only for processors that
        // don't know the instruction.
        private List<XslNode> LoadFallbacksOrContent()
        {
            List<XslNode> content = LoadInstructions(InstructionFlags.AllowFallback);
            if (content.Count != 0)
            {
                content = LoadEndTag(content);
            }
            return content;
        }

        // http://www.w3.org/TR/xslt20/#element-for-each-group
        private readonly XsltAttribute[] _forEachGroupAttributes = {
            new XsltAttribute("select",              V2Req),
            new XsltAttribute("group-by",            V2Opt),
            new XsltAttribute("group-adjacent",      V2Opt),
            new XsltAttribute("group-starting-with", V2Opt),
            new XsltAttribute("group-ending-with",   V2Opt),
            new XsltAttribute("collation",           V2Opt),
            new XsltAttribute("composite",           V2Opt),
        };
        private XslNode XslForEachGroup()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_forEachGroupAttributes);
            string? select = ParseStringAttribute(0, "select");
            var v2 = new XslNodeV2
            {
                GroupBy = ParseStringAttribute(1, "group-by"),
                GroupAdjacent = ParseStringAttribute(2, "group-adjacent"),
                GroupStartingWith = ParseStringAttribute(3, "group-starting-with"),
                GroupEndingWith = ParseStringAttribute(4, "group-ending-with"),
                Collation = ParseStringAttribute(5, "collation"),
                Composite = ParseYesNoAttribute(6, "composite") == TriState.True,
            };
            int kinds = (v2.GroupBy != null ? 1 : 0) + (v2.GroupAdjacent != null ? 1 : 0) + (v2.GroupStartingWith != null ? 1 : 0) + (v2.GroupEndingWith != null ? 1 : 0);
            CheckError(kinds != 1, /*[XTSE1080]*/SR.Xslt_InvalidAttrValue, "group-by", "(exactly one grouping attribute is required)");
            if (v2.Collation != null && v2.GroupBy == null && v2.GroupAdjacent == null)
            {
                ReportError(SR.Xml_UserException, "XTSE1090: xsl:for-each-group has a collation only with group-by or group-adjacent");
            }
            // The current template rule becomes null, so we must not allow xsl:apply-import's within this element
            _input.CanHaveApplyImports = false;
            List<XslNode> content = LoadInstructions(InstructionFlags.AllowSort);
            ctxInfo.SaveExtendedLineInfo(_input);
            XslNode result = SetInfo(new XslNodeEx(XslNodeType.ForEachGroup, null, select, ctxInfo, _input.XslVersion), content, ctxInfo);
            result.V2 = v2;
            return result;
        }

        // http://www.w3.org/TR/xslt20/#element-analyze-string
        private readonly XsltAttribute[] _analyzeStringAttributes = {
            new XsltAttribute("select", V2Req),
            new XsltAttribute("regex",  V2Req),
            new XsltAttribute("flags",  V2Opt),
        };
        private XslNode XslAnalyzeString()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_analyzeStringAttributes);
            string? select = ParseStringAttribute(0, "select");
            var v2 = new XslNodeV2 { Regex = ParseStringAttribute(1, "regex"), Flags = ParseStringAttribute(2, "flags") ?? "" };

            XslNode? matching = null, nonMatching = null;
            QName parentName = _input.ElementName;
            if (_input.MoveToFirstChild())
            {
                do
                {
                    switch (_input.NodeType)
                    {
                        case XmlNodeType.Element:
                            if (_input.IsXsltKeyword(_atoms.MatchingSubstring))
                            {
                                ContextInfo childInfo = _input.GetAttributes();
                                CheckError(nonMatching != null, /*[XTSE0010]*/SR.Xslt_AnalyzeStringChildOrder);
                                CheckError(matching != null, /*[XTSE0010]*/SR.Xslt_AnalyzeStringDupChild, _atoms.MatchingSubstring);
                                _input.CanHaveApplyImports = false;
                                matching = SetInfo(F.XslNode(XslNodeType.MatchingSubstring, null, null, _input.XslVersion), LoadInstructions(), childInfo);
                            }
                            else if (_input.IsXsltKeyword(_atoms.NonMatchingSubstring))
                            {
                                ContextInfo childInfo = _input.GetAttributes();
                                CheckError(nonMatching != null, /*[XTSE0010]*/SR.Xslt_AnalyzeStringDupChild, _atoms.NonMatchingSubstring);
                                _input.CanHaveApplyImports = false;
                                nonMatching = SetInfo(F.XslNode(XslNodeType.NonMatchingSubstring, null, null, _input.XslVersion), LoadInstructions(), childInfo);
                            }
                            else if (_input.IsXsltKeyword(_atoms.Fallback))
                            {
                                XslFallback();
                            }
                            else
                            {
                                ReportError(/*[XTSE0010]*/SR.Xslt_UnexpectedElement, _input.QualifiedName, parentName);
                                _input.SkipNode();
                            }
                            break;
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            break;
                        default:
                            ReportError(/*[XTSE0010]*/SR.Xslt_TextNodesNotAllowed, parentName);
                            break;
                    }
                } while (_input.MoveToNextSibling());
            }
            CheckError(matching == null && nonMatching == null, /*[XTSE1130]*/SR.Xslt_AnalyzeStringEmpty);
            ctxInfo.SaveExtendedLineInfo(_input);

            var content = new List<XslNode>();
            if (matching != null) content.Add(matching);
            if (nonMatching != null) content.Add(nonMatching);
            XslNode result = SetInfo(F.XslNode(XslNodeType.AnalyzeString, null, select, _input.XslVersion), content, ctxInfo);
            result.V2 = v2;
            return result;
        }

        // http://www.w3.org/TR/xslt20/#element-namespace
        private readonly XsltAttribute[] _namespaceAttributes = {
            new XsltAttribute("name",   V2Req),
            new XsltAttribute("select", V2Opt),
        };
        private XslNode XslNamespace()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_namespaceAttributes);
            string? name = ParseStringAttribute(0, "name");
            string? select = ParseStringAttribute(1, "select");
            List<XslNode> content = LoadContent(select != null);
            XslNode result = SetInfo(F.XslNode(XslNodeType.Namespace, null, select, _input.XslVersion), content, ctxInfo);
            result.V2 = new XslNodeV2 { NameAvt = name ?? "" };
            return result;
        }

        // http://www.w3.org/TR/xslt20/#element-perform-sort
        private readonly XsltAttribute[] _performSortAttributes = {
            new XsltAttribute("select", V2Opt),
        };
        private XslNode XslPerformSort()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_performSortAttributes);
            string? select = ParseStringAttribute(0, "select");
            List<XslNode> content = LoadInstructions(InstructionFlags.AllowSort);
            ctxInfo.SaveExtendedLineInfo(_input);
            if (select != null)
            {
                foreach (XslNode node in content)
                {
                    if (node.NodeType != XslNodeType.Sort)
                    {
                        ReportError(/*[XTSE1040]*/SR.Xslt_PerformSortCntSel);
                        break;
                    }
                }
            }
            return SetInfo(F.XslNode(XslNodeType.PerformSort, null, select, _input.XslVersion), content, ctxInfo);
        }

        // http://www.w3.org/TR/xslt20/#element-document
        private readonly XsltAttribute[] _documentAttributes = {
            new XsltAttribute("type",       V2Opt),
            new XsltAttribute("validation", V2Opt),
        };
        private XslNode XslDocument()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_documentAttributes);
            ParseTypeAttribute(0);
            ParseValidationAttribute(1, /*defVal:*/false);
            return SetInfo(F.XslNode(XslNodeType.Document, null, null, _input.XslVersion), LoadEndTag(LoadInstructions()), ctxInfo);
        }

        // http://www.w3.org/TR/xslt20/#element-next-match
        private XslNode XslNextMatch()
        {
            ContextInfo ctxInfo = _input.GetAttributes();
            List<XslNode> content = LoadWithParams(InstructionFlags.AllowFallback);
            ctxInfo.SaveExtendedLineInfo(_input);
            return SetInfo(F.XslNode(XslNodeType.NextMatch, null, null, _input.XslVersion), content, ctxInfo);
        }

        // http://www.w3.org/TR/xslt20/#element-result-document
        private readonly XsltAttribute[] _resultDocumentAttributes = {
            new XsltAttribute("format",                 V2Opt),
            new XsltAttribute("href",                   V2Opt),
            new XsltAttribute("validation",             V2Opt),
            new XsltAttribute("type",                   V2Opt),
            new XsltAttribute("method",                 V2Opt),
            new XsltAttribute("byte-order-mark",        V2Opt),
            new XsltAttribute("cdata-section-elements", V2Opt),
            new XsltAttribute("doctype-public",         V2Opt),
            new XsltAttribute("doctype-system",         V2Opt),
            new XsltAttribute("encoding",               V2Opt),
            new XsltAttribute("escape-uri-attributes",  V2Opt),
            new XsltAttribute("include-content-type",   V2Opt),
            new XsltAttribute("indent",                 V2Opt),
            new XsltAttribute("media-type",             V2Opt),
            new XsltAttribute("normalization-form",     V2Opt),
            new XsltAttribute("omit-xml-declaration",   V2Opt),
            new XsltAttribute("standalone",             V2Opt),
            new XsltAttribute("undeclare-prefixes",     V2Opt),
            new XsltAttribute("use-character-maps",     V2Opt),
            new XsltAttribute("output-version",         V2Opt),
        };
        private XslNode XslResultDocument()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_resultDocumentAttributes);
            var v2 = new XslNodeV2
            {
                Format = ParseStringAttribute(0, "format"),
                Href = ParseStringAttribute(1, "href"),
                Method = ParseStringAttribute(4, "method"),
            };
            _input.CanHaveApplyImports = false;
            XslNode result = SetInfo(F.XslNode(XslNodeType.ResultDocument, null, null, _input.XslVersion), LoadEndTag(LoadInstructions()), ctxInfo);
            result.V2 = v2;
            return result;
        }

        // http://www.w3.org/TR/xslt20/#stylesheet-functions
        private readonly XsltAttribute[] _functionAttributes = {
            new XsltAttribute("name",           V2Req),
            new XsltAttribute("as",             V2Opt),
            new XsltAttribute("override",       V2Opt),
            new XsltAttribute("visibility",     V2Opt),
            new XsltAttribute("streamability",  V2Opt),
            new XsltAttribute("override-extension-function", V2Opt),
            new XsltAttribute("new-each-time",  V2Opt),
            new XsltAttribute("cache",          V2Opt),
        };
        private void LoadFunction(NsDecl? stylesheetNsList)
        {
            Debug.Assert(_curTemplate == null);
            ContextInfo ctxInfo = _input.GetAttributes(_functionAttributes);
            ctxInfo.nsList = MergeNamespaces(ctxInfo.nsList, stylesheetNsList);

            QilName? name = ParseQNameAttribute(0);
            string? asType = ParseStringAttribute(1, "as");
            ParseYesNoAttribute(2, "override"); // validated (XTSE0020); stylesheet functions never override extension functions
            CheckError(name != null && name.NamespaceUri.Length == 0, /*[XTSE0740]*/SR.Xslt_InvalidAttrValue, "name", name?.QualifiedName);

            // A function is a template without match, name or mode for the loader's purposes: parameters, then the body.
            _input.CanHaveApplyImports = false;
            var function = new Template(name, /*match:*/null, nullMode, double.NaN, _input.XslVersion);
            function.IsFunction = true;
            function.V2 = new XslNodeV2 { As = asType };
            _curTemplate = function;
            SetInfo(function, LoadEndTag(LoadInstructions(InstructionFlags.AllowParam)), ctxInfo);
            _curTemplate = null;
            if (name != null)
            {
                int arity = 0;
                foreach (XslNode n in function.Content)
                {
                    if (n.NodeType == XslNodeType.Param) arity++;
                }
                if (!_compiler.AddFunction(name, arity, function, _curStylesheet!.ImportPrecedence))
                {
                    ReportError(/*[XTSE0770]*/SR.Xslt_DupTemplateName, name.QualifiedName);
                }
            }
        }

        // http://www.w3.org/TR/xslt20/#element-character-map
        private readonly XsltAttribute[] _characterMapAttributes = {
            new XsltAttribute("name",               V2Req),
            new XsltAttribute("use-character-maps", V2Opt),
        };
        private readonly XsltAttribute[] _outputCharacterAttributes = {
            new XsltAttribute("character", V2Req),
            new XsltAttribute("string",    V2Req),
        };
        private void LoadCharacterMap(NsDecl? stylesheetNsList)
        {
            ContextInfo ctxInfo = _input.GetAttributes(_characterMapAttributes);
            ctxInfo.nsList = MergeNamespaces(ctxInfo.nsList, stylesheetNsList);
            QilName? name = ParseQNameAttribute(0);
            string? uses = ParseStringAttribute(1, "use-character-maps");
            var map = new CharacterMap(name, uses == null ? new List<QilName>() : ParseQNameList(uses));

            QName parentName = _input.ElementName;
            if (_input.MoveToFirstChild())
            {
                do
                {
                    switch (_input.NodeType)
                    {
                        case XmlNodeType.Element:
                            if (_input.IsXsltKeyword(_atoms.OutputCharacter))
                            {
                                _input.GetAttributes(_outputCharacterAttributes);
                                string? character = ParseStringAttribute(0, "character");
                                string? s = ParseStringAttribute(1, "string");
                                if (character != null && s != null)
                                {
                                    bool single = character.Length == 1 || (character.Length == 2 && char.IsSurrogatePair(character[0], character[1]));
                                    CheckError(!single, /*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "character", character);
                                    if (single)
                                    {
                                        map.Characters[char.ConvertToUtf32(character, 0)] = s;
                                    }
                                }
                                CheckNoContent();
                            }
                            else
                            {
                                ReportError(/*[XTSE0010]*/SR.Xslt_UnexpectedElement, _input.QualifiedName, parentName);
                                _input.SkipNode();
                            }
                            break;
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            break;
                        default:
                            ReportError(/*[XTSE0010]*/SR.Xslt_TextNodesNotAllowed, parentName);
                            break;
                    }
                } while (_input.MoveToNextSibling());
            }
            if (name != null)
            {
                _compiler.CharacterMaps[name] = map;
            }
        }

        private List<QilName> ParseQNameList(string value)
        {
            var result = new List<QilName>();
            foreach (string qname in XmlConvert.SplitString(value))
            {
                result.Add(CreateXPathQName(qname));
            }
            return result;
        }
    }
}
