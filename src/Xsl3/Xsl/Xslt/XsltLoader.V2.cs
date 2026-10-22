// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Linq;
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
            new XsltAttribute("select", V2Opt), // Xsl3: optional in 3.0, which allows a sequence constructor
        };
        // XSLT 3.0 §8.4
        private XslNode XslWherePopulated()
        {
            ContextInfo ctxInfo = _input.GetAttributes();
            return SetInfo(F.XslNode(XslNodeType.WherePopulated, null, null, _input.XslVersion), LoadEndTag(LoadInstructions()), ctxInfo);
        }

        private readonly XsltAttribute[] _onEmptyAttributes = {
            new XsltAttribute("select", V2Opt),
        };
        // XSLT 3.0 §8.3
        private readonly XsltAttribute[] _tryAttributes = {
            new XsltAttribute("select",          V2Opt),
            new XsltAttribute("rollback-output", V2Opt),
        };
        private readonly XsltAttribute[] _catchAttributes = {
            new XsltAttribute("errors", V2Opt),
            new XsltAttribute("select", V2Opt),
        };

        // XSLT 3.0 §3.5.2
        private readonly XsltAttribute[] _usePackageAttributes = {
            new XsltAttribute("name",            V2Req),
            new XsltAttribute("package-version", V2Opt),
        };

        /// <summary>
        /// xsl:use-package: the package is found in the package library (<see cref="Compiler.Packages"/>) and loaded
        /// as an import, so the using package's declarations, including those in xsl:override, take precedence over
        /// it. Visibility isn't enforced: the packages' components share one name space.
        /// </summary>
        // XSLT 3.0 §3.5.1: ranges separated by commas, each *, V, V+, V.*, "to V" or "V to V" (V a package version).
        private static bool IsPackageVersionRanges(string ranges)
        {
            foreach (string part in ranges.Split(','))
            {
                string range = part.Trim();
                string[] words = range.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                bool ok = range == "*"
                    || words is ["to", var high] && IsPackageVersion(high)
                    || words is [var low, "to", var high2] && IsPackageVersion(low) && IsPackageVersion(high2)
                    || words.Length == 1 && (IsPackageVersion(range)
                        || range.EndsWith('+') && IsPackageVersion(range[..^1])
                        || range.EndsWith(".*", StringComparison.Ordinal) && IsPackageVersion(range[..^2]));
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        private void LoadUsePackage(ContextInfo ctxInfo, QName parentName)
        {
            _input.GetAttributes(_usePackageAttributes);
            if (!_packageTopModule)
            {
                ReportError(SR.Xml_UserException, "XTSE3008: xsl:use-package must be in the top-level module of a package, not one it imports or includes");
            }
            string name = ParseStringAttribute(0, "name")?.Trim() ?? "";
            string version = ParseStringAttribute(1, "package-version")?.Trim() ?? "*";
            ISourceLineInfo line = _input.BuildNameLineInfo();
            Uri? location = Xsl3.Xslt.PackageVersions.Find(_compiler.Packages, name, version);
            UsedPackage? used = null;
            if (!IsPackageVersionRanges(version))
            {
                // Xsl3: not a list of package version ranges (XSLT 3.0 §3.5.1): XTSE0020.
                ReportError(SR.Xml_UserException, $"XTSE0020: '{version}' is not a package-version range");
            }
            else if (location == null)
            {
                ReportError(SR.Xml_UserException, $"XTSE3000: no package named {name} with version {version} is available");
            }
            else
            {
                // Each package is loaded once, with an id of its own.
                if (!_packageLocations.TryGetValue(location.ToString(), out int id))
                {
                    id = _compiler.PackageInfos.Count;
                    _compiler.PackageInfos.Add(new PackageInfo(id));
                    _packageLocations[location.ToString()] = id;
                    if (!_documentUriInUse.Contains(location.ToString()) && !_curStylesheet!.ImportHrefs.Contains(location))
                    {
                        _curStylesheet!.ImportHrefs.Add(location);
                        _curStylesheet.ImportPositions.Add(_curStylesheet.Declarations);
                    }
                }
                used = new UsedPackage(id, line);
                _compiler.PackageInfos[_packageId].Uses.Add(used);
            }
            if (_input.MoveToFirstChild())
            {
                do
                {
                    if (_input.NodeType == XmlNodeType.Element && _input.IsXsltNamespace() && _input.LocalName == "override")
                    {
                        _input.GetAttributes();
                        int savedTarget = _overrideTarget;
                        _overrideTarget = used?.Id ?? -1;
                        _inOverride = true;
                        try
                        {
                            LoadDeclarations(ctxInfo, parentName, importAnywhere: true);
                        }
                        finally
                        {
                            _overrideTarget = savedTarget;
                            _inOverride = false;
                        }
                    }
                    else if (_input.NodeType == XmlNodeType.Element && _input.IsXsltNamespace() && _input.LocalName == "accept")
                    {
                        if (LoadPackageRule("accept") is PackageRule accept)
                        {
                            used?.Accept.Add(accept);
                        }
                    }
                    else if (_input.NodeType == XmlNodeType.Element)
                    {
                        ReportError(SR.Xml_UserException, $"XTSE0010: {_input.QualifiedName} isn't allowed in xsl:use-package");
                        _input.SkipNode();
                    }
                }
                while (_input.MoveToNextSibling());
            }
        }

        // XSLT 3.0 §3.5.3
        private readonly XsltAttribute[] _packageRuleAttributes = {
            new XsltAttribute("component",  V2Req),
            new XsltAttribute("names",      V2Req),
            new XsltAttribute("visibility", V2Req),
        };

        // xsl:expose or xsl:accept: the component kind, name tests (prefixes resolved) and visibility.
        private PackageRule? LoadPackageRule(string element)
        {
            _input.GetAttributes(_packageRuleAttributes);
            ISourceLineInfo line = _input.BuildNameLineInfo();
            string component = ParseStringAttribute(0, "component")?.Trim() ?? "*";
            string names = ParseStringAttribute(1, "names") ?? "";
            string visibility = ParseStringAttribute(2, "visibility")?.Trim() ?? "private";
            CheckNoContent();
            char kind = component switch
            {
                "template" => 'T',
                "function" => 'F',
                "attribute-set" => 'A',
                "variable" => 'V',
                "mode" => 'M',
                "*" => '*',
                _ => '?',
            };
            if (kind == '?')
            {
                ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "component", component);
                return null;
            }
            bool hiddenAllowed = element == "accept";
            if (visibility is not ("public" or "private" or "final" or "abstract") && !(hiddenAllowed && visibility == "hidden"))
            {
                ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "visibility", visibility);
                return null;
            }
            var tests = new List<PackageNameTest>();
            foreach (string token in names.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string nameToken = token;
                int? arity = null;
                if (token.StartsWith('#'))
                {
                    // #unnamed and #default: the unnamed mode is always private to its package.
                    ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "names", token);
                    continue;
                }
                int hash = token.LastIndexOf('#');
                if (hash > 0 && int.TryParse(token.AsSpan(hash + 1), out int n))
                {
                    arity = n;
                    nameToken = token[..hash];
                }
                if (nameToken == "*")
                {
                    tests.Add(new PackageNameTest(null, null, arity, token));
                }
                else if (nameToken.StartsWith("*:", StringComparison.Ordinal))
                {
                    tests.Add(new PackageNameTest(null, nameToken[2..], arity, token));
                }
                else if (nameToken.EndsWith(":*", StringComparison.Ordinal))
                {
                    tests.Add(new PackageNameTest(_input.LookupXmlNamespace(nameToken[..^2]) ?? "", null, arity, token));
                }
                else if (nameToken.StartsWith("Q{", StringComparison.Ordinal) && nameToken.IndexOf('}') is int close and > 0)
                {
                    string local = nameToken[(close + 1)..];
                    tests.Add(new PackageNameTest(nameToken[2..close], local == "*" ? null : local, arity, token));
                }
                else
                {
                    int colon = nameToken.IndexOf(':');
                    string ns = colon < 0 ? "" : _input.LookupXmlNamespace(nameToken[..colon]) ?? "";
                    tests.Add(new PackageNameTest(ns, colon < 0 ? nameToken : nameToken[(colon + 1)..], arity, token));
                }
            }
            if (kind == '*' && tests.Any(t => !t.IsWildcard))
            {
                ReportError(SR.Xml_UserException, $"XTSE3022: xsl:{element} with component=\"*\" must name components with wildcards");
            }
            return new PackageRule(kind, tests, visibility, line);
        }

        // XSLT 3.0 §15
        private readonly XsltAttribute[] _mergeSourceAttributes = {
            new XsltAttribute("name",              V2Opt),
            new XsltAttribute("for-each-item",     V2Opt),
            new XsltAttribute("for-each-source",   V2Opt),
            new XsltAttribute("select",            V2Req),
            new XsltAttribute("streamable",        V2Opt),
            new XsltAttribute("use-accumulators",  V2Opt),
            new XsltAttribute("sort-before-merge", V2Opt),
            new XsltAttribute("validation",        V2Opt),
            new XsltAttribute("type",              V2Opt),
        };
        private readonly XsltAttribute[] _mergeKeyAttributes = {
            new XsltAttribute("select",     V2Opt),
            new XsltAttribute("lang",       V2Opt),
            new XsltAttribute("order",      V2Opt),
            new XsltAttribute("collation",  V2Opt),
            new XsltAttribute("case-order", V2Opt),
            new XsltAttribute("data-type",  V2Opt),
        };

        private XslNode XslMerge()
        {
            ContextInfo ctxInfo = _input.GetAttributes();
            var content = new List<XslNode>();
            bool action = false;
            if (_input.MoveToFirstChild())
            {
                do
                {
                    if (_input.NodeType != XmlNodeType.Element)
                    {
                        if (_input.NodeType == XmlNodeType.Text)
                        {
                            ReportError(SR.Xml_UserException, "XTSE0010: text isn't allowed in xsl:merge");
                        }
                        continue;
                    }
                    string local = _input.LocalName;
                    if (!_input.IsXsltNamespace())
                    {
                        ReportError(SR.Xml_UserException, $"XTSE0010: {_input.QualifiedName} isn't allowed in xsl:merge");
                        _input.SkipNode();
                    }
                    else if (local == "merge-source" && !action)
                    {
                        content.Add(XslMergeSource());
                    }
                    else if (local == "merge-action" && !action && content.Count > 0)
                    {
                        action = true;
                        ContextInfo actionInfo = _input.GetAttributes();
                        content.Add(SetInfo(F.XslNode(XslNodeType.MergeAction, null, null, _input.XslVersion), LoadEndTag(LoadInstructions()), actionInfo));
                    }
                    else if (local == "fallback" && action)
                    {
                        _input.SkipNode();
                    }
                    else
                    {
                        ReportError(SR.Xml_UserException, $"XTSE0010: xsl:{local} isn't allowed here in xsl:merge");
                        _input.SkipNode();
                    }
                }
                while (_input.MoveToNextSibling());
            }
            if (!action)
            {
                ReportError(SR.Xml_UserException, "XTSE0010: xsl:merge needs one or more xsl:merge-source elements and an xsl:merge-action");
            }
            return SetInfo(F.XslNode(XslNodeType.Merge, null, null, _input.XslVersion), content, ctxInfo);
        }

        private XslNode XslMergeSource()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_mergeSourceAttributes);
            string? name = ParseStringAttribute(0, "name")?.Trim();
            if (name != null && (name.Length == 0 || ValidateNames.ParseNCName(name) != name.Length))
            {
                ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "name", name);
            }
            string? forEachItem = ParseStringAttribute(1, "for-each-item");
            string? forEachSource = ParseStringAttribute(2, "for-each-source");
            string? validation = ParseValidationAttribute(7, /*defVal:*/false);
            XmlQualifiedName? type = ParseTypeAttribute(8);
            if (validation != null && type != null)
            {
                ReportError(SR.Xml_UserException, "XTSE1505: xsl:merge-source can't have both a validation and a type attribute");
            }
            if (forEachSource != null)
            {
                // The documents, with their applicable accumulators; validated as validation or type asks (Xsl3, P6).
                string? accumulators = ParseStringAttribute(5, "use-accumulators") is string useAccumulators ? AccumulatorNames(useAccumulators) : null;
                string documents = $"({forEachSource}) ! doc(.)";
                if (validation is "strict" or "lax" || type != null && type is not { Name: "untyped", Namespace: XmlReservedNs.NsXs })
                {
                    documents = $"Q{{{Runtime.SourceDocuments.Namespace}}}validate({documents}, '{validation ?? ""}', '{(type == null ? "" : $"Q{{{type.Namespace}}}{type.Name}")}')";
                }
                forEachSource = ApplicableAccumulators31(documents, accumulators);
            }
            string? select = ParseStringAttribute(3, "select");
            ParseYesNoAttribute(4, "streamable");
            TriState sortBefore = ParseYesNoAttribute(6, "sort-before-merge");
            if (forEachItem != null && forEachSource != null)
            {
                ReportError(SR.Xml_UserException, "XTSE3195: xsl:merge-source can't have both for-each-item and for-each-source");
            }
            var keys = new List<XslNode>();
            if (_input.MoveToFirstChild())
            {
                do
                {
                    if (_input.NodeType == XmlNodeType.Element && _input.IsXsltNamespace() && _input.LocalName == "merge-key")
                    {
                        ContextInfo keyInfo = _input.GetAttributes(_mergeKeyAttributes);
                        string? keySelect = ParseStringAttribute(0, "select");
                        string? order = ParseStringAttribute(2, "order")?.Trim();
                        string? collation = ParseStringAttribute(3, "collation")?.Trim();
                        string? dataType = ParseStringAttribute(5, "data-type")?.Trim();
                        string? lang = ParseStringAttribute(1, "lang")?.Trim();
                        string? caseOrder = ParseStringAttribute(4, "case-order")?.Trim();
                        List<XslNode> keyContent = LoadInstructions();
                        CheckError(keySelect != null && keyContent.Count != 0, /*[XTSE3200]*/SR.Xslt_ElementCntSel, "xsl:merge-key");
                        if (order is not (null or "ascending" or "descending") && !order.Contains('{'))
                        {
                            ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "order", order);
                        }
                        XslNode key = F.XslNode(XslNodeType.MergeKey, null, keySelect ?? (keyContent.Count == 0 ? "." : null), _input.XslVersion);
                        key.V2 = new XslNodeV2 { Order = order ?? "ascending", Collation = collation, DataType = dataType, Lang = lang, CaseOrder = caseOrder };
                        keys.Add(SetInfo(key, LoadEndTag(keyContent), keyInfo));
                    }
                    else if (_input.NodeType == XmlNodeType.Element)
                    {
                        ReportError(SR.Xml_UserException, $"XTSE0010: {_input.QualifiedName} isn't allowed in xsl:merge-source");
                        _input.SkipNode();
                    }
                }
                while (_input.MoveToNextSibling());
            }
            if (keys.Count == 0)
            {
                ReportError(SR.Xml_UserException, "XTSE0010: xsl:merge-source needs one or more xsl:merge-key elements");
            }
            XslNode source = F.XslNode(XslNodeType.MergeSource, null, select, _input.XslVersion);
            source.V2 = new XslNodeV2 { NameAvt = name, ForEachItem = forEachItem, ForEachSource = forEachSource, SortBeforeMerge = sortBefore == TriState.True };
            return SetInfo(source, keys, ctxInfo);
        }

        // XSLT 3.0 §18.2.1
        private readonly XsltAttribute[] _accumulatorAttributes = {
            new XsltAttribute("name",          V2Req),
            new XsltAttribute("initial-value", V2Req),
            new XsltAttribute("as",            V2Opt),
            new XsltAttribute("streamable",    V2Opt),
        };
        private readonly XsltAttribute[] _accumulatorRuleAttributes = {
            new XsltAttribute("match",  V2Req),
            new XsltAttribute("phase",  V2Opt),
            new XsltAttribute("select", V2Opt),
        };

        private void LoadAccumulator(NsDecl? stylesheetNsList)
        {
            ContextInfo ctxInfo = _input.GetAttributes(_accumulatorAttributes);
            ctxInfo.nsList = MergeNamespaces(ctxInfo.nsList, stylesheetNsList);
            QilName? name = ParseQNameAttribute(0);
            if (name != null && _packageId > 0)
            {
                name = PackageComponents.Mangle(name, _packageId); // accumulators are local to a package
            }
            string? initial = ParseStringAttribute(1, "initial-value");
            string? asType = ParseStringAttribute(2, "as");
            ParseYesNoAttribute(3, "streamable");
            var rules = new List<XslNode>();
            if (_input.MoveToFirstChild())
            {
                do
                {
                    if (_input.NodeType == XmlNodeType.Element && _input.IsXsltNamespace() && _input.LocalName == "accumulator-rule")
                    {
                        ContextInfo ruleInfo = _input.GetAttributes(_accumulatorRuleAttributes);
                        string? match = ParseStringAttribute(0, "match");
                        string phase = ParseStringAttribute(1, "phase")?.Trim() ?? "start";
                        if (phase is not ("start" or "end"))
                        {
                            ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "phase", phase);
                        }
                        string? select = ParseStringAttribute(2, "select");
                        List<XslNode> content = LoadInstructions();
                        CheckError(select != null && content.Count != 0, /*[XTSE3300]*/SR.Xslt_ElementCntSel, "xsl:accumulator-rule");
                        XslNode rule = F.XslNode(XslNodeType.AccumulatorRule, null, select, _input.XslVersion);
                        rule.V2 = new XslNodeV2 { Match = match, Phase = phase };
                        rules.Add(SetInfo(rule, LoadEndTag(content), ruleInfo));
                    }
                    else if (_input.NodeType == XmlNodeType.Element)
                    {
                        ReportError(SR.Xml_UserException, $"XTSE0010: {_input.QualifiedName} isn't allowed in xsl:accumulator");
                        _input.SkipNode();
                    }
                    else if (_input.NodeType == XmlNodeType.Text)
                    {
                        ReportError(SR.Xml_UserException, "XTSE0010: text isn't allowed in xsl:accumulator");
                    }
                }
                while (_input.MoveToNextSibling());
            }
            if (rules.Count == 0)
            {
                ReportError(SR.Xml_UserException, "XTSE0010: xsl:accumulator needs at least one xsl:accumulator-rule");
            }
            if (name == null)
            {
                return;
            }
            XslNode decl = F.XslNode(XslNodeType.Accumulator, name, initial, _input.XslVersion);
            decl.PackageId = _packageId;
            decl.V2 = new XslNodeV2 { As = asType };
            SetInfo(decl, rules, ctxInfo);
            int precedence = _curStylesheet!.ImportPrecedence;
            if (_compiler.Accumulators.TryGetValue(name, out var existing))
            {
                if (existing.Precedence == precedence)
                {
                    ReportError(SR.Xml_UserException, $"XTSE3350: there are two accumulators named {name.QualifiedName}");
                }
                if (existing.Precedence >= precedence)
                {
                    return; // modules are read in decreasing order of precedence
                }
            }
            _compiler.Accumulators[name] = (decl, precedence);
        }

        // XSLT 3.0 §21.2
        private readonly XsltAttribute[] _mapEntryAttributes = {
            new XsltAttribute("key",    V2Req),
            new XsltAttribute("select", V2Opt),
        };

        private XslNode XslMap()
        {
            ContextInfo ctxInfo = _input.GetAttributes();
            return SetInfo(F.XslNode(XslNodeType.Map, null, null, _input.XslVersion), LoadEndTag(LoadInstructions()), ctxInfo);
        }

        private XslNode XslMapEntry()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_mapEntryAttributes);
            string? key = ParseStringAttribute(0, "key");
            string? select = ParseStringAttribute(1, "select");
            List<XslNode> content = LoadInstructions();
            CheckError(select != null && content.Count != 0, /*[XTSE3280]*/SR.Xslt_ElementCntSel, "xsl:map-entry");
            XslNode node = F.XslNode(XslNodeType.MapEntry, null, select, _input.XslVersion);
            node.V2 = new XslNodeV2 { NameAvt = key ?? "()" };
            return SetInfo(node, LoadEndTag(content), ctxInfo);
        }

        // XSLT 3.0 §10.1.2
        private readonly XsltAttribute[] _contextItemAttributes = {
            new XsltAttribute("as",  V2Opt),
            new XsltAttribute("use", V2Opt),
        };

        private XslNode XslContextItem()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_contextItemAttributes);
            string? asType = ParseStringAttribute(0, "as");
            string? use = ParseStringAttribute(1, "use")?.Trim();
            if (use is not (null or "required" or "optional" or "absent"))
            {
                ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "use", use);
            }
            else if (asType != null && asType.TrimEnd().Length > 0 && asType.TrimEnd()[^1] is '?' or '*' or '+')
            {
                ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "as", asType);
            }
            else if (use == "absent" && asType != null)
            {
                ReportError(SR.Xml_UserException, "XTSE3088: xsl:context-item with use=\"absent\" can't have an as attribute");
            }
            else if (use is "optional" or "absent" && _curTemplate is { Name: null })
            {
                // A template rule always has a context item (XSLT 3.0 §10.1.2).
                ReportError(SR.Xml_UserException, $"XTSE0020: xsl:context-item on a template without a name can only have use=\"required\", not \"{use}\"");
            }
            CheckNoContent();
            XslNode node = F.XslNode(XslNodeType.ContextItem, null, use, _input.XslVersion);
            node.V2 = new XslNodeV2 { As = asType };
            return SetInfo(node, null, ctxInfo);
        }

        /// <summary>
        /// A use-accumulators value: "#all", or its names as expanded names (EQNames), sorted, separated by spaces.
        /// </summary>
        private string AccumulatorNames(string value)
        {
            value = value.Trim();
            if (value == "#all")
            {
                return value;
            }
            return string.Join(" ", value.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries).Select(token =>
            {
                if (token.StartsWith("Q{", System.StringComparison.Ordinal))
                {
                    return token; // already an EQName
                }
                int colon = token.IndexOf(':');
                return colon < 0 ? "Q{}" + token : "Q{" + (_input.LookupXmlNamespace(token[..colon]) ?? "") + "}" + token[(colon + 1)..];
            }).Distinct().OrderBy(n => n, System.StringComparer.Ordinal));
        }

        /// <summary>
        /// The documents of xsl:source-document and xsl:merge-source/@for-each-source, with the accumulators
        /// applicable to them (use-accumulators; none by default, XSLT 3.0 §18.2.2).
        /// </summary>
        private static string ApplicableAccumulators31(string documents, string? names) =>
            $"Q{{{Runtime.SourceDocuments.Namespace}}}doc({documents}, '{(names ?? "").Replace("'", "''")}')";

        // XSLT 3.0 §18.1
        private readonly XsltAttribute[] _sourceDocumentAttributes = {
            new XsltAttribute("href",             V2Req),
            new XsltAttribute("streamable",       V2Opt),
            new XsltAttribute("use-accumulators", V2Opt),
            new XsltAttribute("validation",       V2Opt),
            new XsltAttribute("type",             V2Opt),
        };

        /// <summary>
        /// xsl:source-document without streaming: the content is evaluated with the document as the context item,
        /// as by xsl:for-each over doc(href), the attribute value template.
        /// </summary>
        private XslNode XslSourceDocument()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_sourceDocumentAttributes);
            string href = ParseStringAttribute(0, "href") ?? "";
            ParseYesNoAttribute(1, "streamable");
            string? accumulators = ParseStringAttribute(2, "use-accumulators") is string useAccumulators ? AccumulatorNames(useAccumulators) : null;
            string? validation = ParseValidationAttribute(3, /*defVal:*/false);
            XmlQualifiedName? type = ParseTypeAttribute(4);
            string documents = $"doc({AvtAsXPath31(href)})";
            if (validation != null && type != null)
            {
                ReportError(SR.Xml_UserException, "XTSE1505: xsl:source-document can't have both a validation and a type attribute");
            }
            else if (validation is "strict" or "lax" || type != null)
            {
                // Xsl3 (P6): the document, validated.
                documents = $"Q{{{Runtime.SourceDocuments.Namespace}}}validate({documents}, '{validation ?? ""}', '{(type == null ? "" : $"Q{{{type.Namespace}}}{type.Name}")}')";
            }
            _input.CanHaveApplyImports = false;
            List<XslNode> content = LoadInstructions();
            ctxInfo.SaveExtendedLineInfo(_input);
            return SetInfo(F.ForEach(ApplicableAccumulators31(documents, accumulators), ctxInfo, _input.XslVersion), content, ctxInfo);
        }

        // XSLT 3.0 §10.4
        private readonly XsltAttribute[] _evaluateAttributes = {
            new XsltAttribute("xpath",             V2Req),
            new XsltAttribute("as",                V2Opt),
            new XsltAttribute("base-uri",          V2Opt),
            new XsltAttribute("with-params",       V2Opt),
            new XsltAttribute("context-item",      V2Opt),
            new XsltAttribute("namespace-context", V2Opt),
            new XsltAttribute("schema-aware",      V2Opt),
        };

        // The xsl:evaluate instructions loaded so far, for the names of the variables they bind.
        private int _evaluateCount;

        /// <summary>
        /// xsl:evaluate (docs/plan/p5-evaluate.md, decision 1): an xsl:sequence whose content binds each xsl:with-param
        /// to a variable with an internal name, then calls Q{urn:xsl3:evaluate}evaluate with the target expression, the
        /// context item, the namespace context, the base URI and the parameters; with an as attribute, the result goes
        /// through a variable of that type. When dynamic evaluation is disabled (XsltSettings.EnableEvaluate), the
        /// xsl:fallback children, or XTDE3175.
        /// </summary>
        private XslNode XslEvaluate()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_evaluateAttributes);
            string xpath = ParseStringAttribute(0, "xpath") ?? "''";
            string? asType = ParseStringAttribute(1, "as");
            string? baseUri = ParseStringAttribute(2, "base-uri");
            string? withParams = ParseStringAttribute(3, "with-params");
            string? contextItem = ParseStringAttribute(4, "context-item");
            string? namespaceContext = ParseStringAttribute(5, "namespace-context");
            string? schemaAware = ParseStringAttribute(6, "schema-aware");
            if (schemaAware != null && !schemaAware.Contains('{') && schemaAware.Trim() is not ("yes" or "no" or "true" or "false" or "1" or "0"))
            {
                ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "schema-aware", schemaAware);
            }

            var parameters = new List<XslNode>();
            var fallbacks = new List<XslNode>();
            if (_input.MoveToFirstChild())
            {
                do
                {
                    switch (_input.NodeType)
                    {
                        case XmlNodeType.Element:
                            if (_input.IsXsltKeyword(_atoms.WithParam))
                            {
                                XslNode withParam = XslVarPar();
                                CheckWithParam(parameters, withParam);
                                parameters.Add(withParam);
                            }
                            else if (_input.IsXsltKeyword(_atoms.Fallback))
                            {
                                ContextInfo fallbackInfo = _input.GetAttributes();
                                fallbacks.Add(SetInfo(F.List(), LoadInstructions(), fallbackInfo));
                            }
                            else
                            {
                                ReportError(/*[XTSE0010]*/SR.Xslt_UnexpectedElement, _input.QualifiedName, "xsl:evaluate");
                                _input.SkipNode();
                            }
                            break;
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            break;
                        default:
                            ReportError(/*[XTSE0010]*/SR.Xslt_TextNodesNotAllowed, "xsl:evaluate");
                            break;
                    }
                } while (_input.MoveToNextSibling());
            }
            ctxInfo.SaveExtendedLineInfo(_input);

            if (!_compiler.Settings.EnableEvaluate)
            {
                // Statically disabled (XSLT 3.0 §10.4.4).
                if (fallbacks.Count == 0)
                {
                    fallbacks.Add(SetLineInfo(F.XslNode(XslNodeType.Sequence, null,
                        $"error(QName('{Xdm.XdmException.ErrorNamespace}', 'err:XTDE3175'), 'xsl:evaluate is disabled (XsltSettings.EnableEvaluate)')", _input.XslVersion), ctxInfo.lineInfo));
                }
                return SetInfo(F.List(), fallbacks, ctxInfo);
            }

            static string Literal(string s) => "'" + s.Replace("'", "''") + "'";
            int id = _evaluateCount++;
            var content = new List<XslNode>();
            var entries = new List<string>();
            for (int i = 0; i < parameters.Count; i++)
            {
                XslNode p = parameters[i];
                QilName variable = F.QName($"e{id}-{i}", Runtime.DynamicEvaluation.Namespace, "");
                VarPar v = F.VarPar(XslNodeType.Variable, variable, p.Select, p.XslVersion);
                v.V2 = new XslNodeV2 { As = p.V2?.As };
                v.Namespaces = p.Namespaces;
                v.SourceLine = p.SourceLine;
                v.SetContent(p.Content.Count == 0 ? null : new List<XslNode>(p.Content));
                content.Add(v);
                entries.Add($"QName({Literal(p.Name!.NamespaceUri)}, {Literal(p.Name.LocalName)}): $Q{{{Runtime.DynamicEvaluation.Namespace}}}{variable.LocalName}");
            }
            // Xsl3 (P6): only with schema-aware="yes" does the target expression see the imported schemas (XSLT 3.0 §10.4.1).
            string function = schemaAware?.Trim() is "yes" or "true" or "1" ? "evaluate" : "evaluate-without-schemas";
            if (schemaAware != null && schemaAware.Contains('{'))
            {
                // A computed value: checked when the instruction is evaluated (XTDE0030); the schemas are then in scope.
                function = "evaluate";
                xpath = $"(if (normalize-space({AvtAsXPath31(schemaAware)}) = ('yes', 'no', 'true', 'false', '1', '0')) then ({xpath}) " +
                    $"else error(QName('{Xdm.XdmException.ErrorNamespace}', 'err:XTDE0030'), 'schema-aware must be yes or no'))";
            }
            string call = $"Q{{{Runtime.DynamicEvaluation.Namespace}}}{function}(({xpath}), ({contextItem ?? "()"}), ({namespaceContext ?? "()"}), {(namespaceContext != null ? "true()" : "false()")}, " +
                $"{(baseUri != null ? AvtAsXPath31(baseUri) : "()")}, ({withParams ?? "map{}"}), map{{{string.Join(", ", entries)}}})";
            if (asType == null)
            {
                content.Add(SetLineInfo(F.XslNode(XslNodeType.Sequence, null, call, _input.XslVersion), ctxInfo.lineInfo));
            }
            else
            {
                // The result is converted to the required type by the function conversion rules, as a variable's is.
                QilName result = F.QName($"e{id}", Runtime.DynamicEvaluation.Namespace, "");
                VarPar v = F.VarPar(XslNodeType.Variable, result, call, _input.XslVersion);
                v.V2 = new XslNodeV2 { As = asType };
                SetLineInfo(v, ctxInfo.lineInfo);
                content.Add(v);
                content.Add(SetLineInfo(F.XslNode(XslNodeType.Sequence, null, $"$Q{{{Runtime.DynamicEvaluation.Namespace}}}{result.LocalName}", _input.XslVersion), ctxInfo.lineInfo));
            }
            return SetInfo(F.XslNode(XslNodeType.Sequence, null, null, _input.XslVersion), content, ctxInfo);
        }

        // XSLT 3.0 §23.2
        private readonly XsltAttribute[] _assertAttributes = {
            new XsltAttribute("test",       V2Req),
            new XsltAttribute("select",     V2Opt),
            new XsltAttribute("error-code", V2Opt),
        };

        // The xsl:assert instructions loaded so far, for the names of the variables they bind.
        private int _assertCount;

        /// <summary>
        /// xsl:assert (XSLT 3.0 §23.2). With XsltSettings.EnableAssertions off, nothing (it is checked, then dropped).
        /// On, <c>xsl:if test="not(test)"</c> around a variable holding the message (select, else the content) and a
        /// call of fn:error with the error code (XTMM9001 by default), the message as description and as error value.
        /// </summary>
        private XslNode? XslAssert()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_assertAttributes);
            string test = ParseStringAttribute(0, "test") ?? "true()";
            string? select = ParseStringAttribute(1, "select");
            string? errorCode = ParseStringAttribute(2, "error-code");
            List<XslNode> content = LoadInstructions();
            CheckError(select != null && content.Count != 0, /*[XTSE3205]*/SR.Xslt_ElementCntSel, "xsl:assert");
            if (!_compiler.Settings.EnableAssertions)
            {
                return null;
            }

            // The error code: an EQName or a lexical QName, as an expression of an xs:QName.
            string code;
            if (errorCode == null)
            {
                code = $"QName('{Xdm.XdmException.ErrorNamespace}', 'err:XTMM9001')";
            }
            else if (errorCode.Replace("{{", "").Replace("}}", "") is var lone && (lone.Contains('{') || lone.Contains('}'))
                && !System.Text.RegularExpressions.Regex.IsMatch(errorCode.Trim(), @"^Q\{[^{}]*\}[^{}:]+$"))
            {
                // An attribute value template with an expression, whose value would be resolved against the
                // instruction's namespaces. (Q{uri}local is taken as written.)
                ReportError(SR.Xml_UserException, "XTSE0020: a computed error-code on xsl:assert isn't supported yet");
                return null;
            }
            else
            {
                // The attribute's value: {{ and }} stand for braces (Q{{uri}}local is an EQName).
                string value = (lone.Contains('{') ? errorCode : errorCode.Replace("{{", "{").Replace("}}", "}")).Trim();
                int colon = value.IndexOf(':');
                if (value.StartsWith("Q{", StringComparison.Ordinal) && value.IndexOf('}') is int close and > 0)
                {
                    code = $"QName('{value[2..close].Replace("'", "''")}', '{value[(close + 1)..]}')";
                }
                else
                {
                    string ns = colon < 0 ? "" : _input.LookupXmlNamespace(value[..colon]) ?? "";
                    code = $"QName('{ns.Replace("'", "''")}', '{value}')";
                }
            }

            QilName message = F.QName($"a{_assertCount++}", Runtime.DynamicEvaluation.Namespace, "");
            VarPar variable = F.VarPar(XslNodeType.Variable, message, select, _input.XslVersion);
            variable.V2 = new XslNodeV2();
            SetLineInfo(variable, ctxInfo.lineInfo);
            variable.SetContent(content.Count == 0 ? null : content);
            string reference = $"$Q{{{Runtime.DynamicEvaluation.Namespace}}}{message.LocalName}";
            XslNode raise = SetLineInfo(F.XslNode(XslNodeType.Sequence, null,
                $"error({code}, string-join(for $i in {reference} return if ($i instance of function(*)) then '' else string($i), ' '), {reference})", _input.XslVersion), ctxInfo.lineInfo);
            // (Parenthesized, so a test that is a sequence expression stays one argument.)
            XslNode check = F.XslNode(XslNodeType.If, null, $"not(({test}))", _input.XslVersion);
            return SetInfo(check, new List<XslNode> { variable, raise }, ctxInfo);
        }

        // An attribute value template as an XPath expression of its string value.
        internal static string AvtAsXPath31(string avt)
        {
            var parts = new List<string>();
            var literal = new System.Text.StringBuilder();
            void Flush()
            {
                if (literal.Length != 0)
                {
                    parts.Add("'" + literal.ToString().Replace("'", "''") + "'");
                    literal.Clear();
                }
            }
            int i = 0;
            while (i < avt.Length)
            {
                char ch = avt[i];
                if ((ch == '{' || ch == '}') && i + 1 < avt.Length && avt[i + 1] == ch)
                {
                    literal.Append(ch);
                    i += 2;
                    continue;
                }
                if (ch == '{')
                {
                    // The expression ends at the matching brace outside string literals.
                    int depth = 0, j = i + 1;
                    char quote = '\0';
                    for (; j < avt.Length; j++)
                    {
                        char c = avt[j];
                        if (quote != '\0')
                        {
                            if (c == quote)
                            {
                                quote = '\0';
                            }
                        }
                        else if (c is '\'' or '"')
                        {
                            quote = c;
                        }
                        else if (c == '{')
                        {
                            depth++;
                        }
                        else if (c == '}')
                        {
                            if (depth == 0)
                            {
                                break;
                            }
                            depth--;
                        }
                    }
                    Flush();
                    parts.Add($"string-join(({avt[(i + 1)..System.Math.Min(j, avt.Length)]}) ! string(.), ' ')");
                    i = j + 1;
                    continue;
                }
                literal.Append(ch);
                i++;
            }
            Flush();
            return parts.Count == 0 ? "''" : parts.Count == 1 ? parts[0] : $"string-join(({string.Join(", ", parts)}), '')";
        }

        // XSLT 3.0 §7.2
        private readonly XsltAttribute[] _iterateAttributes = {
            new XsltAttribute("select", V2Req),
        };

        private XslNode XslIterate()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_iterateAttributes);
            string? select = ParseStringAttribute(0, "select");
            List<XslNode> content = LoadInstructions(InstructionFlags.AllowParam);
            if (content.Count(n => n.NodeType == XslNodeType.OnCompletion) > 1)
            {
                ReportError(SR.Xml_UserException, "XTSE0010: xsl:iterate has more than one xsl:on-completion");
            }
            return SetInfo(F.XslNode(XslNodeType.Iterate, null, select, _input.XslVersion), LoadEndTag(content), ctxInfo);
        }

        private XslNode XslOnCompletion()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_onEmptyAttributes);
            string? select = ParseStringAttribute(0, "select");
            List<XslNode> content = LoadInstructions();
            CheckError(select != null && content.Count != 0, /*[XTSE3125]*/SR.Xslt_ElementCntSel, "xsl:on-completion");
            return SetInfo(F.XslNode(XslNodeType.OnCompletion, null, select, _input.XslVersion), LoadEndTag(content), ctxInfo);
        }

        private XslNode XslNextIteration()
        {
            ContextInfo ctxInfo = _input.GetAttributes();
            List<XslNode> content = LoadWithParams(InstructionFlags.None);
            return SetInfo(F.XslNode(XslNodeType.NextIteration, null, null, _input.XslVersion), content, ctxInfo);
        }

        private XslNode XslBreak()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_onEmptyAttributes);
            string? select = ParseStringAttribute(0, "select");
            List<XslNode> content = LoadInstructions();
            CheckError(select != null && content.Count != 0, /*[XTSE3125]*/SR.Xslt_ElementCntSel, "xsl:break");
            return SetInfo(F.XslNode(XslNodeType.Break, null, select, _input.XslVersion), LoadEndTag(content), ctxInfo);
        }

        private XslNode XslTry()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_tryAttributes);
            string? select = ParseStringAttribute(0, "select");
            ParseYesNoAttribute(1, "rollback-output");
            List<XslNode> content = LoadInstructions();
            // The content: the sequence constructor, then one or more xsl:catch (and xsl:fallback) (XTSE0010).
            int firstCatch = content.FindIndex(n => n.NodeType == XslNodeType.Catch);
            bool ordered = firstCatch >= 0 && content.Skip(firstCatch).All(n => n.NodeType is XslNodeType.Catch or XslNodeType.Nop || n.NodeType == XslNodeType.Text && IsWhitespace31(n));
            if (!ordered)
            {
                ReportError(SR.Xml_UserException, "XTSE0010: xsl:try must end with one or more xsl:catch elements");
            }
            if (select != null && content.Take(firstCatch < 0 ? content.Count : firstCatch).Any(n => !(n.NodeType == XslNodeType.Text && IsWhitespace31(n)) && n.NodeType != XslNodeType.Nop))
            {
                ReportError(SR.Xml_UserException, "XTSE3140: xsl:try has both a select attribute and a sequence constructor");
            }
            return SetInfo(F.XslNode(XslNodeType.Try, null, select, _input.XslVersion), LoadEndTag(content), ctxInfo);
        }

        private static bool IsWhitespace31(XslNode text) => text.Select != null && XmlCharType.IsOnlyWhitespace(text.Select);

        private XslNode XslCatch()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_catchAttributes);
            var errors = new List<string>();
            if (_input.MoveToXsltAttribute(0, "errors"))
            {
                foreach (string token in _input.Value.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    errors.Add(ResolveNameTest31(token));
                }
            }
            else
            {
                errors.Add("*");
            }
            string? select = ParseStringAttribute(1, "select");
            List<XslNode> content = LoadInstructions();
            CheckError(select != null && content.Count != 0, /*[XTSE3150]*/SR.Xslt_ElementCntSel, "xsl:catch");
            XslNode node = F.XslNode(XslNodeType.Catch, null, select, _input.XslVersion);
            node.V2 = new XslNodeV2 { Errors = errors };
            return SetInfo(node, LoadEndTag(content), ctxInfo);
        }

        // A NameTest of xsl:catch/@errors, with its prefix resolved: "*", "Q{ns}*", "*:local" or "Q{ns}local".
        private string ResolveNameTest31(string token)
        {
            if (token == "*" || token.StartsWith("*:", StringComparison.Ordinal) || token.StartsWith("Q{", StringComparison.Ordinal))
            {
                return token;
            }
            int colon = token.IndexOf(':');
            string prefix = colon < 0 ? "" : token[..colon];
            string local = colon < 0 ? token : token[(colon + 1)..];
            // Unprefixed names are in no namespace (the default namespace doesn't apply).
            string ns = prefix.Length == 0 ? "" : _input.LookupXmlNamespace(prefix) ?? "";
            return $"Q{{{ns}}}{local}";
        }

        // XSLT 3.0 §18.2.4: without streaming, xsl:fork is the concatenation of its prongs.
        private XslNode XslFork()
        {
            ContextInfo ctxInfo = _input.GetAttributes();
            return SetInfo(F.List(), LoadEndTag(LoadInstructions()), ctxInfo);
        }

        private XslNode XslOnEmpty(XslNodeType kind)
        {
            ContextInfo ctxInfo = _input.GetAttributes(_onEmptyAttributes);
            string? select = ParseStringAttribute(0, "select");
            List<XslNode> content = LoadInstructions();
            CheckError(select != null && content.Count != 0, /*[XTSE3185]*/SR.Xslt_ElementCntSel, kind == XslNodeType.OnEmpty ? "xsl:on-empty" : "xsl:on-non-empty");
            return SetInfo(F.XslNode(kind, null, select, _input.XslVersion), LoadEndTag(content), ctxInfo);
        }

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
            if (kinds != 1)
            {
                ReportError(SR.Xml_UserException, "XTSE1080: xsl:for-each-group needs exactly one of group-by, group-adjacent, group-starting-with and group-ending-with");
            }
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
            var v2 = new XslNodeV2 { Regex = ParseStringAttribute(1, "regex"), Flags = ParseStringAttribute(2, "flags") ?? "", EmptyMatches = true }; // Xsl3 is a 3.0 processor: XTDE1150 is gone, even for a 2.0 stylesheet

            XslNode? matching = null, nonMatching = null;
            QName parentName = _input.ElementName;
            bool sawFallback = false;
            if (_input.MoveToFirstChild())
            {
                do
                {
                    switch (_input.NodeType)
                    {
                        case XmlNodeType.Element:
                            if (sawFallback && !_input.IsXsltKeyword(_atoms.Fallback))
                            {
                                // Xsl3: xsl:fallback children come after the others (XTSE0010).
                                ReportError(SR.Xml_UserException, $"XTSE0010: {_input.QualifiedName} can't follow xsl:fallback in {parentName}");
                            }
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
                                sawFallback = true;
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
            XmlQualifiedName? type = ParseTypeAttribute(0);
            string? validation = ParseValidationAttribute(1, /*defVal:*/false);
            return WithValidation(SetInfo(F.XslNode(XslNodeType.Document, null, null, _input.XslVersion), LoadEndTag(LoadInstructions()), ctxInfo), validation, type);
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
            new XsltAttribute("html-version",           V2Opt),
            new XsltAttribute("build-tree",             V2Opt),
            new XsltAttribute("item-separator",         V2Opt),
            new XsltAttribute("json-node-output-method", V2Opt),
            new XsltAttribute("allow-duplicate-names",  V2Opt),
            new XsltAttribute("parameter-document",     V2Opt),
            new XsltAttribute("suppress-indentation",   V2Opt),
        };
        internal static bool IsDecimal31(string value) =>
            decimal.TryParse(value.Trim(), System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out _);

        private XslNode XslResultDocument()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_resultDocumentAttributes);
            var v2 = new XslNodeV2
            {
                Format = ParseStringAttribute(0, "format"),
                Href = ParseStringAttribute(1, "href"),
                Method = ParseStringAttribute(4, "method"),
                Serialization = new List<(string, string)>(),
            };
            // The serialization attributes are attribute value templates; use-character-maps names maps.
            for (int i = 4; i < _resultDocumentAttributes.Length; i++)
            {
                string attName = _resultDocumentAttributes[i].name;
                if (_input.MoveToXsltAttribute(i, attName))
                {
                    if (attName == "use-character-maps")
                    {
                        v2.UseCharacterMaps = _input.Value;
                    }
                    else if (attName == "html-version" && !_input.Value.Contains('{') && !IsDecimal31(_input.Value))
                    {
                        ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, attName, _input.Value);
                    }
                    else
                    {
                        v2.Serialization.Add((attName, _input.Value));
                    }
                }
            }
            XmlQualifiedName? type = ParseTypeAttribute(3);
            string? validation = ParseValidationAttribute(2, /*defVal:*/false);
            _input.CanHaveApplyImports = false;
            XslNode result = SetInfo(F.XslNode(XslNodeType.ResultDocument, null, null, _input.XslVersion), LoadEndTag(LoadInstructions()), ctxInfo);
            result.V2 = v2;
            return WithValidation(result, validation, type);
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
            string? functionVisibility = _input.MoveToXsltAttribute(3, "visibility") ? _input.Value.Trim() : null;
            ctxInfo.nsList = MergeNamespaces(ctxInfo.nsList, stylesheetNsList);

            QilName? name = ParseQNameAttribute(0);
            string? asType = ParseStringAttribute(1, "as");
            // Validated (XTSE0020); stylesheet functions never override extension functions.
            TriState overrides = ParseYesNoAttribute(2, "override");
            TriState overridesExtension = ParseYesNoAttribute(5, "override-extension-function");
            if (overrides != TriState.Unknown && overridesExtension != TriState.Unknown && overrides != overridesExtension)
            {
                ReportError(SR.Xml_UserException, "XTSE0020: override and override-extension-function have different values");
            }
            string newEachTime = "maybe";
            if (_input.MoveToXsltAttribute(6, "new-each-time"))
            {
                newEachTime = _input.Value.Trim() switch
                {
                    "yes" or "true" or "1" => "yes",
                    "no" or "false" or "0" => "no",
                    "maybe" => "maybe",
                    _ => "",
                };
                if (newEachTime.Length == 0)
                {
                    ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "new-each-time", _input.Value);
                }
            }
            if (name != null && name.NamespaceUri.Length == 0)
            {
                ReportError(SR.Xml_UserException, $"XTSE0740: the stylesheet function {name.QualifiedName} has no namespace prefix");
            }

            // A function is a template without match, name or mode for the loader's purposes: parameters, then the body.
            _input.CanHaveApplyImports = false;
            var function = new Template(name, /*match:*/null, nullMode, double.NaN, _input.XslVersion);
            function.IsFunction = true;
            PackageDeclaration(function, functionVisibility);
            bool cache = ParseYesNoAttribute(7, "cache") == TriState.True;
            function.V2 = new XslNodeV2 { As = asType, NewEachTime = newEachTime, Cache = cache };
            _curTemplate = function;
            SetInfo(function, LoadEndTag(LoadInstructions(InstructionFlags.AllowParam)), ctxInfo);
            _curTemplate = null;
            CheckReservedName(name, "function");
            // Function parameters: distinct names (XTSE0580), no default value (XTSE0760), not tunnel (XTSE0020).
            var paramNames = new HashSet<QilName>();
            foreach (XslNode n in function.Content)
            {
                if (n.NodeType != XslNodeType.Param)
                {
                    continue;
                }
                if (!paramNames.Add(n.Name!))
                {
                    ReportError(SR.Xml_UserException, $"XTSE0580: two parameters of the function are named {n.Name!.QualifiedName}");
                }
                if (n.Select != null || n.Content.Count > 0)
                {
                    ReportError(SR.Xml_UserException, $"XTSE0760: the function parameter {n.Name!.QualifiedName} can't have a default value");
                }
                if (n.V2?.Tunnel == true)
                {
                    ReportError(SR.Xml_UserException, $"XTSE0020: the function parameter {n.Name!.QualifiedName} can't be a tunnel parameter");
                }
            }
            if (name != null)
            {
                int arity = 0;
                foreach (XslNode n in function.Content)
                {
                    if (n.NodeType == XslNodeType.Param) arity++;
                }
                if (!_compiler.AddFunction(name, arity, function, _curStylesheet!.ImportPrecedence))
                {
                    ReportError(SR.Xml_UserException, $"XTSE0770: two functions are named {name.QualifiedName} with arity {arity} and the same import precedence");
                }
            }
        }

        private readonly XsltAttribute[] _globalContextItemAttributes = {
            new XsltAttribute("as",  V2Opt),
            new XsltAttribute("use", V2Opt),
        };

        // The xsl:global-context-item declarations seen: by package (the first one), and the modules that have one.
        private readonly Dictionary<int, (string Use, string? As)> _globalContextItems = new();
        private readonly HashSet<(int, string)> _globalContextItemModules = new();

        // XSLT 3.0 §5.3.2
        private void LoadGlobalContextItem()
        {
            _input.GetAttributes(_globalContextItemAttributes);
            string? asType = ParseStringAttribute(0, "as");
            string use = ParseStringAttribute(1, "use")?.Trim() ?? "optional";
            if (use is not ("required" or "optional" or "absent"))
            {
                ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "use", use);
            }
            else if (use == "absent" && asType != null)
            {
                ReportError(SR.Xml_UserException, "XTSE3089: xsl:global-context-item with use=\"absent\" can't have an as attribute");
            }
            else if (!_globalContextItemModules.Add((_packageId, _input.BaseUri ?? "")))
            {
                ReportError(SR.Xml_UserException, "XTSE3087: a stylesheet module has more than one xsl:global-context-item");
            }
            else
            {
                string? normalized = asType == null ? null : string.Concat(asType.Where(c => !char.IsWhiteSpace(c)));
                if (_globalContextItems.TryGetValue(_packageId, out var existing))
                {
                    if (existing.Use != use || existing.As != normalized)
                    {
                        ReportError(SR.Xml_UserException, "XTSE3087: the modules of a package have inconsistent xsl:global-context-item declarations");
                    }
                }
                else
                {
                    _globalContextItems[_packageId] = (use, normalized);
                    if (_packageId == 0)
                    {
                        _compiler.GlobalContextItem = (use, asType);
                    }
                    else if (use == "required")
                    {
                        // A library package's declaration is ignored unless it requires a context item it can't get.
                        ReportError(SR.Xml_UserException, "XTTE0590: a used package's xsl:global-context-item requires a global context item");
                    }
                }
            }
            CheckNoContent();
        }

        // XSLT 3.0 §3.5.4.1
        private readonly XsltAttribute[] _modeAttributes = {
            new XsltAttribute("name",                      V2Opt),
            new XsltAttribute("on-no-match",               V2Opt),
            new XsltAttribute("on-multiple-match",         V2Opt),
            new XsltAttribute("warning-on-no-match",       V2Opt),
            new XsltAttribute("warning-on-multiple-match", V2Opt),
            new XsltAttribute("typed",                     V2Opt),
            new XsltAttribute("visibility",                V2Opt),
            new XsltAttribute("streamable",                V2Opt),
            new XsltAttribute("use-accumulators",          V2Opt),
        };
        private void LoadMode()
        {
            _input.GetAttributes(_modeAttributes);
            if (_input.MoveToXsltAttribute(0, "name") && _input.Value.Trim() == "#unnamed")
            {
                // The name is an EQName; #unnamed is for the attributes that refer to modes (package-909).
                ReportError(SR.Xml_UserException, "XTSE0020: xsl:mode name=\"#unnamed\" isn't an EQName; an unnamed mode's xsl:mode has no name attribute");
            }
            QilName mode = PackageMode(_input.MoveToXsltAttribute(0, "name") && _input.Value.Trim() != "#unnamed" ? ParseQNameAttribute(0) ?? nullMode : nullMode);
            var decl = new ModeDecl { ImportPrecedence = _curStylesheet!.ImportPrecedence };
            decl.Declaration = new ModeDeclNode(mode) { PackageId = _packageId, OverrideOf = _overrideTarget, SourceLine = _input.BuildNameLineInfo() };
            if (_input.MoveToXsltAttribute(6, "visibility"))
            {
                decl.Declaration.Visibility = _input.Value.Trim();
                if (mode.LocalName.Length == 0 && decl.Declaration.Visibility != "private")
                {
                    // The unnamed mode is always private (XSLT 3.0 §3.5.4.1).
                    ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "visibility", decl.Declaration.Visibility);
                }
            }
            if (_input.MoveToXsltAttribute(5, "typed"))
            {
                decl.Typed = _input.Value.Trim();
            }
            if (_input.MoveToXsltAttribute(5, "typed") && _input.Value.Trim() is not ("yes" or "no" or "true" or "false" or "1" or "0" or "strict" or "lax" or "unspecified"))
            {
                ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "typed", _input.Value);
            }
            if (_input.MoveToXsltAttribute(1, "on-no-match"))
            {
                decl.OnNoMatch = _input.Value.Trim();
                if (decl.OnNoMatch is not ("text-only-copy" or "shallow-copy" or "deep-copy" or "shallow-skip" or "deep-skip" or "fail"))
                {
                    ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "on-no-match", decl.OnNoMatch);
                    decl.OnNoMatch = "text-only-copy";
                }
            }
            if (_input.MoveToXsltAttribute(2, "on-multiple-match"))
            {
                decl.OnMultipleMatch = _input.Value.Trim();
            }
            ParseYesNoAttribute(3, "warning-on-no-match");
            ParseYesNoAttribute(4, "warning-on-multiple-match");
            ParseYesNoAttribute(7, "streamable");
            for (int i = 1; i < _modeAttributes.Length; i++)
            {
                if (_input.MoveToXsltAttribute(i, _modeAttributes[i].name))
                {
                    string value = _input.Value.Trim();
                    if (_modeAttributes[i].name == "use-accumulators")
                    {
                        value = AccumulatorNames(value);
                    }
                    decl.Attributes[_modeAttributes[i].name] = value;
                }
            }
            // Each attribute comes from the declaration of highest import precedence that gives it (modules are read
            // in decreasing precedence); declarations of the same precedence may not give an attribute two values
            // (XTSE0545).
            if (!_compiler.ModeDecls.TryGetValue(mode, out ModeDecl? existing))
            {
                _compiler.ModeDecls[mode] = decl;
            }
            else if (existing.ImportPrecedence != decl.ImportPrecedence)
            {
                foreach (var (attribute, value) in decl.Attributes)
                {
                    if (existing.Attributes.TryAdd(attribute, value))
                    {
                        if (attribute == "on-no-match")
                        {
                            existing.OnNoMatch = decl.OnNoMatch;
                        }
                        else if (attribute == "on-multiple-match")
                        {
                            existing.OnMultipleMatch = decl.OnMultipleMatch;
                        }
                        else if (attribute == "typed")
                        {
                            existing.Typed = decl.Typed;
                        }
                    }
                }
            }
            else
            {
                foreach (var (attribute, value) in decl.Attributes)
                {
                    if (existing.Attributes.TryGetValue(attribute, out string? other) && other != value)
                    {
                        ReportError(SR.Xml_UserException, $"XTSE0545: two xsl:mode declarations give {attribute} different values");
                    }
                    existing.Attributes[attribute] = value;
                }
                if (decl.Attributes.ContainsKey("on-no-match"))
                {
                    existing.OnNoMatch = decl.OnNoMatch;
                }
                if (decl.Attributes.ContainsKey("on-multiple-match"))
                {
                    existing.OnMultipleMatch = decl.OnMultipleMatch;
                }
            }
            CheckNoContent();
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
            // Character maps are local to their package: their names are qualified with it (use-package-106).
            QilName? name = ParseQNameAttribute(0) is QilName declared ? PackageComponents.Mangle(declared, _packageId) : null;
            string? uses = ParseStringAttribute(1, "use-character-maps");
            var map = new CharacterMap(name, uses == null ? new List<QilName>() : ParseQNameList(uses).ConvertAll(n => PackageComponents.Mangle(n, _packageId)));

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
                // Modules are read in decreasing import precedence: a second map of the same name at the same
                // precedence is an error (XTSE1580); one of lower precedence is overridden.
                if (_compiler.CharacterMaps.TryGetValue(name, out CharacterMap? existing))
                {
                    if (existing.Precedence == _curStylesheet!.ImportPrecedence)
                    {
                        ReportError(SR.Xml_UserException, $"XTSE1580: two character maps are named {name.QualifiedName}");
                    }
                }
                else
                {
                    map.Precedence = _curStylesheet!.ImportPrecedence;
                    _compiler.CharacterMaps[name] = map;
                }
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
