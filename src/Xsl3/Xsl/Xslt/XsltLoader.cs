// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//#define XSLT2

using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.XPath;
using Xsl3.Qil;
using ContextInfo = Xsl3.Xslt.XsltInput.ContextInfo;
using F = Xsl3.Xslt.AstFactory;
using QName = Xsl3.Xslt.XsltInput.DelayedQName;
using TypeFactory = Xsl3.XmlQueryTypeFactory;
using XsltAttribute = Xsl3.Xslt.XsltInput.XsltAttribute;

namespace Xsl3.Xslt
{
    internal sealed partial class XsltLoader : IErrorHelper
    {
        private Compiler _compiler = null!;
        private XmlResolver _xmlResolver = null!;
        private QueryReaderSettings _readerSettings = null!;
        private KeywordsTable _atoms = null!; // XSLT keywords atomized with QueryReaderSettings.NameTabel
        private XsltInput _input = null!;     // Current input stream
        private Stylesheet? _curStylesheet;   // Current stylesheet
        private Template? _curTemplate;       // Current template

        internal static readonly QilName nullMode = F.QName(string.Empty);
        // Xsl3: mode="#current" on xsl:apply-templates, resolved by QilGenerator to the mode of the template rule.
        internal static readonly QilName CurrentMode = F.QName("current-mode", "urn:xsl3:mode", "");

        // Flags which control attribute versioning
        public const int V1Opt = 1;
        public const int V1Req = 2;
        public const int V2Opt = 4;
        public const int V2Req = 8;

        public void Load(Compiler compiler, object stylesheet, XmlResolver? xmlResolver, XmlResolver? origResolver)
        {
            // Xsl3: static expressions read documents (doc('') in a shadow attribute) as document() does: through the
            // stylesheet's resolver, when EnableDocumentFunction allows it.
            XmlResolver? savedStaticResolver = UseWhen.Resolver;
            bool savedSchemaAware = UseWhen.SchemaAware;
            UseWhen.Resolver = compiler.Settings.EnableDocumentFunction ? xmlResolver : null;
            UseWhen.SchemaAware = compiler.Settings.SchemaAware;
            try
            {
                LoadCore(compiler, stylesheet, xmlResolver, origResolver);
            }
            finally
            {
                UseWhen.Resolver = savedStaticResolver;
                UseWhen.SchemaAware = savedSchemaAware;
            }
        }

        private void LoadCore(Compiler compiler, object stylesheet, XmlResolver? xmlResolver, XmlResolver? origResolver)
        {
            Debug.Assert(compiler != null);
            _compiler = compiler;
            _xmlResolver = xmlResolver ?? XmlResolver.ThrowingResolver;

            XmlReader? reader = stylesheet as XmlReader;
            if (reader != null)
            {
                _readerSettings = new QueryReaderSettings(reader);
                Load(reader);
            }
            else
            {
                // We should take DefaultReaderSettings from Compiler.Settings.DefaultReaderSettings.

                string? uri = stylesheet as string;
                if (uri != null)
                {
                    // If the stylesheet has been provided as a string (URI, really), then we'll bounce
                    // through an XmlResolver to look up its contents. There's a complication here since
                    // the default resolver provided by our caller is likely a throwing resolver, and
                    // a throwing resolver would fail even when attempting to read this URL. We need
                    // to at minimum allow this URL to be read, because the user after all did explicitly
                    // ask us to do so.
                    //
                    // In this case, we'll rely on the 'origResolver' argument, which is the XmlResolver
                    // which was provided *to our caller* before any default substitution took place.
                    // If an explicit resolver was specified, we'll honor it. Otherwise we'll substitute
                    // an XmlUrlResolver for this one read operation. The stored resolver (which is used
                    // for reads beyond the initial stylesheet read) will use a throwing resolver as its
                    // default, as shown at the very top of this method.

                    origResolver ??= XmlReaderSettings.GetDefaultPermissiveResolver();
                    Uri resolvedUri = origResolver.ResolveUri(null, uri);
                    if (resolvedUri == null)
                    {
                        throw new XslLoadException(SR.Xslt_CantResolve, uri);
                    }

                    _readerSettings = new QueryReaderSettings(new NameTable());
                    using (reader = CreateReader(resolvedUri, origResolver))
                    {
                        Load(reader);
                    }
                }
                else
                {
                    IXPathNavigable? navigable = stylesheet as IXPathNavigable;
                    if (navigable != null)
                    {
                        reader = navigable.CreateNavigator()!.ReadSubtree(); // Xsl3: public route to the internal XPathNavigatorReader.Create
                        _readerSettings = new QueryReaderSettings(reader.NameTable);
                        Load(reader);
                    }
                    else
                    {
                        Debug.Fail("Should never get here");
                    }
                }
            }
            Debug.Assert(compiler.Root != null);
            // Xsl3: the entry point may be a named template or a mode instead of the default mode.
            if (compiler.InitialTemplate is XmlQualifiedName initialTemplate)
            {
                compiler.StartApplyTemplates = new XslNodeEx(XslNodeType.CallTemplate, F.QName(initialTemplate.Name, initialTemplate.Namespace, ""), null, XslVersion.Current);
            }
            else if (compiler.InitialMatchSelection)
            {
                // Xsl3: apply templates to the items supplied as a parameter (fn:transform's initial-match-selection).
                compiler.StartApplyTemplates = new XslNodeEx(XslNodeType.ApplyTemplates,
                    compiler.InitialMode is XmlQualifiedName sm ? F.QName(sm.Name, sm.Namespace, "") : compiler.DefaultInitialMode ?? nullMode,
                    $"Q{{{Runtime.InitialFunctionArguments.Namespace}}}selection()", XslVersion.Current);
            }
            else if (compiler.InitialParameters != null)
            {
                // Xsl3: with parameters, the 2.0 apply-templates compiles them (applied to the initial context item).
                compiler.StartApplyTemplates = new XslNodeEx(XslNodeType.ApplyTemplates,
                    compiler.InitialMode is XmlQualifiedName pm ? F.QName(pm.Name, pm.Namespace, "") : compiler.DefaultInitialMode ?? nullMode, ".", XslVersion.Current);
            }
            else
            {
                compiler.StartApplyTemplates = F.ApplyTemplates(compiler.InitialMode is XmlQualifiedName m ? F.QName(m.Name, m.Namespace, "") : compiler.DefaultInitialMode ?? nullMode);
            }
            if (compiler.InitialParameters is { } initialParameters)
            {
                // Xsl3: the initial template's or mode's parameters (XSLT 3.0 §2.3.4): with-params whose values are
                // supplied as reserved run-time parameters.
                for (int i = 0; i < initialParameters.Count; i++)
                {
                    (XmlQualifiedName name, bool tunnel) = initialParameters[i];
                    XmlQualifiedName value = Runtime.InitialFunctionArguments.Parameter(i + 1);
                    VarPar withParam = F.VarPar(XslNodeType.WithParam, F.QName(name.Name, name.Namespace, ""), $"Q{{{value.Namespace}}}{value.Name}()", XslVersion.Current);
                    withParam.V2 = new XslNodeV2 { Tunnel = tunnel };
                    compiler.StartApplyTemplates.AddContent(withParam);
                }
            }
            ProcessOutputSettings();
            foreach (AttributeSet attSet in compiler.AttributeSets.Values)
            {
                CheckAttributeSetsDfs(attSet); // Check attribute sets for circular references using dfs marking method
            }
        }

        private void Load(XmlReader reader)
        {
            _atoms = new KeywordsTable(reader.NameTable);
            AtomizeAttributes();
            LoadStylesheet(reader, /*include:*/false);
        }


        private void AtomizeAttributes(XsltAttribute[] attributes)
        {
            for (int i = 0; i < attributes.Length; i++)
            {
                attributes[i].name = _atoms.NameTable.Add(attributes[i].name);
            }
        }
        private void AtomizeAttributes()
        {
            AtomizeAttributes(_stylesheetAttributes);
            AtomizeAttributes(_importIncludeAttributes);
            AtomizeAttributes(_loadStripSpaceAttributes);
            AtomizeAttributes(_outputAttributes);
            AtomizeAttributes(_keyAttributes);
            AtomizeAttributes(_decimalFormatAttributes);
            AtomizeAttributes(_namespaceAliasAttributes);
            AtomizeAttributes(_attributeSetAttributes);
            AtomizeAttributes(_templateAttributes);
            AtomizeAttributes(_scriptAttributes);
            AtomizeAttributes(_assemblyAttributes);
            AtomizeAttributes(_usingAttributes);
            AtomizeAttributes(_applyTemplatesAttributes);
            AtomizeAttributes(_callTemplateAttributes);
            AtomizeAttributes(_copyAttributes);
            AtomizeAttributes(_copyOfAttributes);
            AtomizeAttributes(_ifAttributes);
            AtomizeAttributes(_forEachAttributes);
            AtomizeAttributes(_messageAttributes);
            AtomizeAttributes(_numberAttributes);
            AtomizeAttributes(_valueOfAttributes);
            AtomizeAttributes(_variableAttributes);
            AtomizeAttributes(_paramAttributes);
            AtomizeAttributes(_withParamAttributes);
            AtomizeAttributes(_commentAttributes);
            AtomizeAttributes(_processingInstructionAttributes);
            AtomizeAttributes(_textAttributes);
            AtomizeAttributes(_elementAttributes);
            AtomizeAttributes(_attributeAttributes);
            AtomizeAttributes(_sortAttributes);
            // Xsl3: XSLT 2.0 declarations and instructions
            AtomizeAttributes(_sequenceAttributes);
            AtomizeAttributes(_forEachGroupAttributes);
            AtomizeAttributes(_analyzeStringAttributes);
            AtomizeAttributes(_namespaceAttributes);
            AtomizeAttributes(_performSortAttributes);
            AtomizeAttributes(_documentAttributes);
            AtomizeAttributes(_resultDocumentAttributes);
            AtomizeAttributes(_functionAttributes);
            AtomizeAttributes(_characterMapAttributes);
            AtomizeAttributes(_outputCharacterAttributes);
            AtomizeAttributes(_modeAttributes);
            AtomizeAttributes(_onEmptyAttributes);
            AtomizeAttributes(_tryAttributes);
            AtomizeAttributes(_iterateAttributes);
            AtomizeAttributes(_sourceDocumentAttributes);
            AtomizeAttributes(_evaluateAttributes);
            AtomizeAttributes(_assertAttributes);
            AtomizeAttributes(_contextItemAttributes);
            AtomizeAttributes(_mapEntryAttributes);
            AtomizeAttributes(_packageRuleAttributes);
            AtomizeAttributes(_usePackageAttributes);
            AtomizeAttributes(_accumulatorAttributes);
            AtomizeAttributes(_mergeSourceAttributes);
            AtomizeAttributes(_mergeKeyAttributes);
            AtomizeAttributes(_accumulatorRuleAttributes);
            AtomizeAttributes(_catchAttributes);
            AtomizeAttributes(_importSchemaAttributes);

        }

        private bool V1
        {
            get
            {
                Debug.Assert(_compiler.Version != 0, "Version should be already decided at this point");
                return _compiler.Version == 1;
            }
        }
        private bool V2 { get { return !V1; } }

        // Import/Include XsltInput management

        private readonly HybridDictionary _documentUriInUse = new HybridDictionary();

        private Uri ResolveUri(string relativeUri, string baseUri)
        {
            Uri? resolvedBaseUri = (baseUri.Length != 0) ? _xmlResolver.ResolveUri(null, baseUri) : null;
            Uri resolvedUri = _xmlResolver.ResolveUri(resolvedBaseUri, relativeUri);
            if (resolvedUri == null)
            {
                throw new XslLoadException(SR.Xslt_CantResolve, relativeUri);
            }
            return resolvedUri;
        }

        private XmlReader CreateReader(Uri uri, XmlResolver xmlResolver)
        {
            object? input = xmlResolver.GetEntity(uri, null, null);

            Stream? stream = input as Stream;
            if (stream != null)
            {
                return _readerSettings.CreateReader(stream, uri.ToString());
            }

            XmlReader? reader = input as XmlReader;
            if (reader != null)
            {
                return reader;
            }

            IXPathNavigable? navigable = input as IXPathNavigable;
            if (navigable != null)
            {
                return navigable.CreateNavigator()!.ReadSubtree(); // Xsl3: public route to the internal XPathNavigatorReader.Create
            }

            throw new XslLoadException(SR.Xslt_CannotLoadStylesheet, uri.ToString(), input == null ? "null" : input.GetType().ToString());
        }

        private Stylesheet LoadStylesheet(Uri uri, bool include)
        {
            if (V2 && uri.IsAbsoluteUri && uri.Fragment.Length > 1)
            {
                // Xsl3: an embedded stylesheet module (XSLT 3.0 §3.12): the element of the document with that id.
                string id = Uri.UnescapeDataString(uri.Fragment[1..]);
                using XmlReader document = CreateReader(new Uri(uri.GetLeftPart(UriPartial.Query)), _xmlResolver);
                while (document.Read())
                {
                    if (document.NodeType == XmlNodeType.Element
                        && (document.GetAttribute("id") == id || document.GetAttribute("id", XmlReservedNs.NsXml) == id))
                    {
                        using XmlReader embedded = document.ReadSubtree();
                        embedded.Read();
                        return LoadStylesheet(embedded, include);
                    }
                }
                throw new XslLoadException(SR.Xslt_CantResolve, uri.ToString());
            }
            using (XmlReader reader = CreateReader(uri, _xmlResolver))
            {
                return LoadStylesheet(reader, include);
            }
        }

        // Xsl3: packages (Packages.cs): the package whose modules are being loaded, the package an xsl:override's
        // declarations override components of (-1 outside xsl:override), and the packages found by location.
        private int _packageId;
        private int _overrideTarget = -1;
        private bool _inOverride; // Xsl3: loading the declarations of an xsl:override
        private bool _packageTopModule;
        private bool _loadingInclude;
        private readonly HashSet<int> _packageTopModules = new();

        // XSLT 3.0 §3.5.1: integers separated by dots, then optionally "-" and an NCName (XML 1.0 fifth edition).
        private static bool IsPackageVersion(string version)
        {
            version = version.Trim();
            int dash = version.IndexOf('-');
            if (!System.Text.RegularExpressions.Regex.IsMatch(dash < 0 ? version : version[..dash], @"^[0-9]+(\.[0-9]+)*$"))
            {
                return false;
            }
            if (dash < 0)
            {
                return true;
            }
            string name = version[(dash + 1)..];
            for (int i = 0; i < name.Length; i++)
            {
                int c = char.IsSurrogatePair(name, i) ? char.ConvertToUtf32(name[i], name[++i]) : name[i];
                bool start = c is '_' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= 0xC0 and <= 0xD6 or >= 0xD8 and <= 0xF6 or >= 0xF8 and <= 0x2FF or
                    >= 0x370 and <= 0x37D or >= 0x37F and <= 0x1FFF or >= 0x200C and <= 0x200D or >= 0x2070 and <= 0x218F or >= 0x2C00 and <= 0x2FEF or
                    >= 0x3001 and <= 0xD7FF or >= 0xF900 and <= 0xFDCF or >= 0xFDF0 and <= 0xFFFD or >= 0x10000 and <= 0xEFFFF;
                bool other = c is '-' or '.' or 0xB7 or >= '0' and <= '9' or >= 0x300 and <= 0x36F or >= 0x203F and <= 0x2040;
                if (!(start || other && i > 0))
                {
                    return false;
                }
            }
            return name.Length > 0;
        }
        private readonly Dictionary<string, int> _packageLocations = new(StringComparer.Ordinal);

        // A declaration of a used package is registered under a name of its own package (Packages.cs).
        private void PackageDeclaration(XslNode declaration, string? visibility)
        {
            declaration.PackageId = _packageId;
            declaration.Visibility = visibility;
            declaration.OverrideOf = _overrideTarget;
            if (_packageId > 0 && declaration.Name != null)
            {
                declaration.Name.NamespaceUri = PackageComponents.MangleNamespace(_packageId, declaration.Name.NamespaceUri);
            }
        }

        // A mode named in a used package (or in an xsl:override, the overridden package's mode) is that package's.
        private QilName PackageMode(QilName mode)
        {
            int package = _overrideTarget >= 0 ? _overrideTarget : _packageId;
            return package > 0 && !ReferenceEquals(mode, CurrentMode) ? PackageComponents.Mangle(mode, package) : mode;
        }

        private Stylesheet LoadStylesheet(XmlReader reader, bool include)
        {
            string baseUri = reader.BaseURI!;
            Debug.Assert(!_documentUriInUse.Contains(baseUri), "Circular references must be checked while processing xsl:include and xsl:import");
            _documentUriInUse.Add(baseUri, null);
            _compiler.AddModule(baseUri);

            Stylesheet? prevStylesheet = _curStylesheet;
            XsltInput prevInput = _input;
            bool prevTopModule = _packageTopModule;
            Stylesheet thisStylesheet = include ? _curStylesheet! : _compiler.CreateStylesheet();

            _input = new XsltInput(reader, _compiler, _atoms);
            _curStylesheet = thisStylesheet;
            bool prevLoadingInclude = _loadingInclude;
            _loadingInclude = include;

            try
            {
                LoadDocument();
                if (!include)
                {
                    _compiler.MergeWithStylesheet(_curStylesheet);

                    List<Uri> importHrefs = _curStylesheet.ImportHrefs;
                    _curStylesheet.Imports = new Stylesheet[importHrefs.Count];
                    // Imports should be compiled in the reverse order. Template lookup logic relies on that.
                    for (int i = importHrefs.Count; 0 <= --i;)
                    {
                        // Xsl3: a used package is loaded like an import, as its own package.
                        int savedPackage = _packageId;
                        if (_packageLocations.TryGetValue(importHrefs[i].ToString(), out int package))
                        {
                            _packageId = package;
                        }
                        _importPath.Add(_curStylesheet.ImportPositions[i]);
                        try
                        {
                            _curStylesheet.Imports[i] = LoadStylesheet(importHrefs[i], /*include:*/false);
                        }
                        finally
                        {
                            _packageId = savedPackage;
                            _importPath.RemoveAt(_importPath.Count - 1);
                        }
                    }
                }
            }
            catch (XslLoadException)
            {
                throw;
            }
            catch (Exception e)
            {
                if (!XmlException.IsCatchableException(e))
                {
                    throw;
                }
                // Note that XmlResolver or XmlReader may throw XmlException with SourceUri == null.
                // In that case we report current line information from XsltInput.
                XmlException? ex = e as XmlException;
                ISourceLineInfo lineInfo = (ex != null && ex.SourceUri != null ?
                    new SourceLineInfo(ex.SourceUri, ex.LineNumber, ex.LinePosition, ex.LineNumber, ex.LinePosition) :
                    _input.BuildReaderLineInfo()
                );
                if (V2 && e is System.IO.IOException or UnauthorizedAccessException or System.Net.WebException or NotSupportedException)
                {
                    // Xsl3: a module that xsl:import or xsl:include names and that can't be read (XTSE0165).
                    var load = new XslLoadException(SR.Xml_UserException, $"XTSE0165: {e.Message}");
                    load.SetSourceLineInfo(lineInfo);
                    throw load;
                }
                throw new XslLoadException(e, lineInfo);
            }
            finally
            {
                _documentUriInUse.Remove(baseUri);
                _input = prevInput;
                _curStylesheet = prevStylesheet;
                _packageTopModule = prevTopModule;
                _loadingInclude = prevLoadingInclude;
            }
            return thisStylesheet;
        }

        private void LoadDocument()
        {
            if (!_input.FindStylesheetElement())
            {
                ReportError(/*[XT_002]*/SR.Xslt_WrongStylesheetElement);
                return;
            }
            Debug.Assert(_input.NodeType == XmlNodeType.Element);
            if (_input.IsXsltNamespace())
            {
                if (
                    _input.IsKeyword(_atoms.Stylesheet) ||
                    _input.IsKeyword(_atoms.Transform) ||
                    _input.LocalName == "package" // Xsl3: a package used on its own is its stylesheet (XSLT 3.0 §3.5)
                )
                {
                    LoadRealStylesheet();
                }
                else
                {
                    ReportError(/*[XT_002]*/SR.Xslt_WrongStylesheetElement);
                    _input.SkipNode();
                }
            }
            else
            {
                LoadSimplifiedStylesheet();
            }
            _input.Finish();
        }

        private void LoadSimplifiedStylesheet()
        {
            Debug.Assert(!_input.IsXsltNamespace());
            Debug.Assert(_curTemplate == null);

            // Prefix will be fixed later in LoadLiteralResultElement()
            _curTemplate = F.Template(/*name:*/null, /*match:*/"/", /*mode:*/nullMode, /*priority:*/double.NaN, _input.XslVersion);

            // This template has mode=null match="/" and no imports
            _input.CanHaveApplyImports = true;
            XslNode lre = LoadLiteralResultElement(/*asStylesheet:*/true);
            if (lre != null)
            {
                SetLineInfo(_curTemplate, lre.SourceLine);

                List<XslNode> content = new List<XslNode>();
                content.Add(lre);
                SetContent(_curTemplate, content);
                if (!_curStylesheet!.AddTemplate(_curTemplate))
                {
                    Debug.Fail("AddTemplate() returned false for simplified stylesheet");
                }
            }
            _curTemplate = null;
        }

        private readonly XsltAttribute[] _stylesheetAttributes = {
            new XsltAttribute("version",                V1Req | V2Req),
            new XsltAttribute("id",                     V1Opt | V2Opt),
            new XsltAttribute("default-validation",     V2Opt),
            new XsltAttribute("input-type-annotations", V2Opt),
            // Xsl3: xsl:package (XSLT 3.0 §3.5.1)
            new XsltAttribute("name",                   V2Opt),
            new XsltAttribute("package-version",        V2Opt),
            new XsltAttribute("declared-modes",         V2Opt),
        };
        private void LoadRealStylesheet()
        {
            Debug.Assert(_input.IsXsltNamespace() && (_input.IsKeyword(_atoms.Stylesheet) || _input.IsKeyword(_atoms.Transform) || _input.LocalName == "package"));
            bool isPackage = _input.LocalName == "package"; // (before the attributes are read)
            ContextInfo ctxInfo = _input.GetAttributes(_stylesheetAttributes);

            if (ParseValidationAttribute(2, /*defVal:*/true) is string defaultValidation && _compiler.DefaultValidation == null)
            {
                _compiler.DefaultValidation = defaultValidation; // Xsl3 (P6): the principal module's, read first
            }
            ParseInputTypeAnnotationsAttribute(3);
            // Xsl3: xsl:use-package is only allowed in the top-level module of a package and the modules it
            // includes, not those it imports (XTSE3008).
            if (!_loadingInclude)
            {
                _packageTopModule = V2 && _packageTopModules.Add(_packageId);
            }
            if (V2 && isPackage && (_loadingInclude || !_packageTopModule))
            {
                // Xsl3: xsl:import and xsl:include name stylesheet modules; a package is used with xsl:use-package.
                ReportError(SR.Xml_UserException, "XTSE0165: an imported or included module is an xsl:package, not a stylesheet module");
            }
            // Xsl3: name, package-version and declared-modes belong to xsl:package only (XTSE0090).
            if (V2 && !isPackage && !_input.ForwardCompatibility)
            {
                string element = "xsl:" + _input.LocalName;
                foreach ((int index, string attribute) in new[] { (4, "name"), (5, "package-version"), (6, "declared-modes") })
                {
                    if (_input.MoveToXsltAttribute(index, attribute))
                    {
                        ReportError(/*[XTSE0090]*/SR.Xslt_InvalidAttribute, attribute, element);
                    }
                }
            }
            if (V2 && isPackage && _input.MoveToXsltAttribute(5, "package-version") && !IsPackageVersion(_input.Value))
            {
                ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "package-version", _input.Value);
            }
            if (V2)
            {
                // Xsl3: the package this module begins (XSLT 3.0 §3.5): the principal one, or one xsl:use-package found.
                while (_compiler.PackageInfos.Count <= _packageId)
                {
                    _compiler.PackageInfos.Add(new PackageInfo(_compiler.PackageInfos.Count));
                }
                PackageInfo info = _compiler.PackageInfos[_packageId];
                if (isPackage && !info.IsPackage)
                {
                    info.IsPackage = true;
                    info.DeclaredModes = ParseYesNoAttribute(6, "declared-modes") != TriState.False;
                }
            }
            if (V2 && _compiler.DefaultInitialMode == null)
            {
                // Xsl3: the principal module's default-mode is the default initial mode (XSLT 3.0 §2.3.2).
                _compiler.DefaultInitialMode = DefaultMode31();
            }

            QName parentName = _input.ElementName;
            if (_input.ModuleExcluded)
            {
                // Xsl3: use-when="false()" on the stylesheet element: the module contributes nothing.
                _input.SkipNode();
                return;
            }
            // Xsl3: XSLT 3.0 lets xsl:import come after other declarations (XTSE0200 is 2.0 only).
            bool importAnywhere = V2 && _input.LastVersion >= 3.0;
            LoadDeclarations(ctxInfo, parentName, importAnywhere);
        }

        // The declarations among the children of the current element: the stylesheet element, or xsl:override
        // (Xsl3: XSLT 3.0 §3.5.3), whose declarations belong to the using package.
        private void LoadDeclarations(ContextInfo ctxInfo, QName parentName, bool importAnywhere)
        {
            if (_input.MoveToFirstChild())
            {
                bool atTop = true;
                do
                {
                    bool isImport = false;
                    switch (_input.NodeType)
                    {
                        case XmlNodeType.Element:
                            _curStylesheet!.Declarations++;
                            if (_inOverride && !(_input.IsXsltNamespace() && _input.LocalName is "template" or "function" or "variable" or "param" or "attribute-set"))
                            {
                                // XSLT 3.0 §3.5.3.4: only those components can be overridden.
                                ReportError(SR.Xml_UserException, $"XTSE0010: xsl:override can't contain {_input.QualifiedName}");
                                _input.SkipNode();
                                break;
                            }
                            if (_input.IsXsltNamespace())
                            {
                                if (_input.IsKeyword(_atoms.Import))
                                {
                                    if (!atTop && !importAnywhere)
                                    {
                                        ReportError(/*[XT0200]*/SR.Xslt_NotAtTop, _input.QualifiedName, parentName);
                                        _input.SkipNode();
                                    }
                                    else
                                    {
                                        isImport = true;
                                        LoadImport();
                                    }
                                }
                                else if (_input.IsKeyword(_atoms.Include))
                                {
                                    LoadInclude();
                                }
                                else if (_input.IsKeyword(_atoms.StripSpace))
                                {
                                    LoadStripSpace(ctxInfo.nsList);
                                }
                                else if (_input.IsKeyword(_atoms.PreserveSpace))
                                {
                                    LoadPreserveSpace(ctxInfo.nsList);
                                }
                                else if (_input.IsKeyword(_atoms.Output))
                                {
                                    LoadOutput();
                                }
                                else if (_input.IsKeyword(_atoms.Key))
                                {
                                    LoadKey(ctxInfo.nsList);
                                }
                                else if (_input.IsKeyword(_atoms.DecimalFormat))
                                {
                                    LoadDecimalFormat(ctxInfo.nsList);
                                }
                                else if (_input.IsKeyword(_atoms.NamespaceAlias))
                                {
                                    LoadNamespaceAlias(ctxInfo.nsList);
                                }
                                else if (_input.IsKeyword(_atoms.AttributeSet))
                                {
                                    LoadAttributeSet(ctxInfo.nsList);
                                }
                                else if (_input.IsKeyword(_atoms.Variable))
                                {
                                    LoadGlobalVariableOrParameter(ctxInfo.nsList);
                                }
                                else if (_input.IsKeyword(_atoms.Param))
                                {
                                    LoadGlobalVariableOrParameter(ctxInfo.nsList);
                                }
                                else if (_input.IsKeyword(_atoms.Template))
                                {
                                    LoadTemplate(ctxInfo.nsList);
                                }
                                else if (V2 && _input.IsKeyword(_atoms.CharacterMap))
                                {
                                    LoadCharacterMap(ctxInfo.nsList);
                                }
                                else if (V2 && _input.LocalName == "mode")
                                {
                                    LoadMode();
                                }
                                else if (V2 && _input.LocalName == "accumulator")
                                {
                                    LoadAccumulator(ctxInfo.nsList);
                                }
                                else if (V2 && _input.LocalName == "use-package")
                                {
                                    LoadUsePackage(ctxInfo, parentName);
                                }
                                else if (V2 && _input.LocalName == "expose")
                                {
                                    PackageRule? expose = LoadPackageRule("expose");
                                    if (expose != null)
                                    {
                                        _compiler.PackageInfos[_packageId].Expose.Add(expose);
                                    }
                                }
                                else if (V2 && _input.LocalName is "global-context-item")
                                {
                                    LoadGlobalContextItem();
                                }
                                else if (V2 && _input.IsKeyword(_atoms.Function))
                                {
                                    LoadFunction(ctxInfo.nsList);
                                }
                                else if (V2 && _input.LocalName == "import-schema" && _compiler.Settings.SchemaAware)
                                {
                                    LoadImportSchema();
                                }
                                else if (V2 && _input.LocalName == "import-schema")
                                {
                                    // Xsl3: schema-awareness is P6 (SPEC §18); until then this is a basic processor (XSLT 3.0 §3.14).
                                    _input.GetVersionAttribute();
                                    ReportError(SR.Xml_UserException, "XTSE1650: xsl:import-schema needs a schema-aware processor, and Xsl3 isn't one yet");
                                    _input.SkipNode();
                                }
                                else
                                {
                                    _input.GetVersionAttribute();
                                    if (!_input.ForwardCompatibility)
                                    {
                                        ReportError(/*[XT_003]*/SR.Xslt_UnexpectedElement, _input.QualifiedName, parentName);
                                    }
                                    _input.SkipNode();
                                }
                            }
                            else if (_input.IsNs(_atoms.UrnMsxsl) && _input.IsKeyword(_atoms.Script))
                            {
                                LoadMsScript(ctxInfo.nsList);
                            }
                            else
                            {
                                if (_input.IsNullNamespace())
                                {
                                    ReportError(/*[XT0130]*/SR.Xslt_NullNsAtTopLevel, _input.LocalName);
                                }
                                // Ignoring non-recognized namespace per XSLT spec 2.2
                                _input.SkipNode();
                            }
                            atTop = isImport;
                            break;

                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            break;
                        default:
                            Debug.Assert(_input.NodeType == XmlNodeType.Text);
                            ReportError(/*[XT0120]*/SR.Xslt_TextNodesNotAllowed, parentName);
                            break;
                    }
                } while (_input.MoveToNextSibling());
            }
        }


        private readonly XsltAttribute[] _importIncludeAttributes = { new XsltAttribute("href", V1Req | V2Req) };
        // SxS: This method reads resource names from source document and does not expose any resources to the caller.
        // It's OK to suppress the SxS warning.
        private void LoadImport()
        {
            _input.GetAttributes(_importIncludeAttributes);

            if (_input.MoveToXsltAttribute(0, "href"))
            {
                // Resolve href right away using the current BaseUri (it might change later)
                Uri uri = ResolveUri(_input.Value, (V2 ? _input.StaticBaseUri : null) ?? _input.BaseUri!); // Xsl3: xml:base applies in 2.0+

                // Check for circular references
                if (_documentUriInUse.Contains(uri.ToString()))
                {
                    if (V2)
                    {
                        // Xsl3: an import cycle is XTSE0210 (an include cycle XTSE0180).
                        ReportError(SR.Xml_UserException, $"XTSE0210: the stylesheet '{_input.Value}' imports itself, directly or indirectly");
                    }
                    else
                    {
                        ReportError(/*[XT0210]*/SR.Xslt_CircularInclude, _input.Value);
                    }
                }
                else
                {
                    _curStylesheet!.ImportHrefs.Add(uri);
                    _curStylesheet.ImportPositions.Add(_curStylesheet.Declarations);
                    if (V2)
                    {
                        // Xsl3: the imported module's static variables are in scope after the xsl:import (static-021).
                        PreloadImportedStatics(uri, new HashSet<string>(StringComparer.Ordinal));
                    }
                }
            }
            else
            {
                // The error was already reported. Ignore the instruction
            }

            CheckNoContent();
        }

        // SxS: This method reads resource names from source document and does not expose any resources to the caller.
        // It's OK to suppress the SxS warning.
        private void LoadInclude()
        {
            _input.GetAttributes(_importIncludeAttributes);

            if (_input.MoveToXsltAttribute(0, "href"))
            {
                Uri uri = ResolveUri(_input.Value, (V2 ? _input.StaticBaseUri : null) ?? _input.BaseUri!); // Xsl3: xml:base applies in 2.0+

                // Check for circular references
                if (_documentUriInUse.Contains(uri.ToString()))
                {
                    ReportError(/*[XT0180]*/SR.Xslt_CircularInclude, _input.Value);
                }
                else
                {
                    LoadStylesheet(uri, /*include:*/ true);
                }
            }
            else
            {
                // The error was already reported. Ignore the instruction
            }

            CheckNoContent();
        }

        private readonly XsltAttribute[] _loadStripSpaceAttributes = { new XsltAttribute("elements", V1Req | V2Req) };
        private void LoadStripSpace(NsDecl? stylesheetNsList)
        {
            ContextInfo ctxInfo = _input.GetAttributes(_loadStripSpaceAttributes);
            ctxInfo.nsList = MergeNamespaces(ctxInfo.nsList, stylesheetNsList);

            if (_input.MoveToXsltAttribute(0, _atoms.Elements))
            {
                ParseWhitespaceRules(_input.Value, false);
            }
            CheckNoContent();
        }

        private void LoadPreserveSpace(NsDecl? stylesheetNsList)
        {
            ContextInfo ctxInfo = _input.GetAttributes(_loadStripSpaceAttributes);
            ctxInfo.nsList = MergeNamespaces(ctxInfo.nsList, stylesheetNsList);

            if (_input.MoveToXsltAttribute(0, _atoms.Elements))
            {
                ParseWhitespaceRules(_input.Value, true);
            }
            CheckNoContent();
        }

        private readonly XsltAttribute[] _outputAttributes = {
            new XsltAttribute("name",                   V2Opt),
            new XsltAttribute("method",                 V1Opt | V2Opt),
            new XsltAttribute("byte-order-mark",        V2Opt),
            new XsltAttribute("cdata-section-elements", V1Opt | V2Opt),
            new XsltAttribute("doctype-public",         V1Opt | V2Opt),
            new XsltAttribute("doctype-system",         V1Opt | V2Opt),
            new XsltAttribute("encoding",               V1Opt | V2Opt),
            new XsltAttribute("escape-uri-attributes",  V2Opt),
            new XsltAttribute("include-content-type",   V2Opt),
            new XsltAttribute("indent",                 V1Opt | V2Opt),
            new XsltAttribute("media-type",             V1Opt | V2Opt),
            new XsltAttribute("normalization-form",     V2Opt),
            new XsltAttribute("omit-xml-declaration",   V1Opt | V2Opt),
            new XsltAttribute("standalone",             V1Opt | V2Opt),
            new XsltAttribute("undeclare-prefixes",     V2Opt),
            new XsltAttribute("use-character-maps",     V2Opt),
            new XsltAttribute("version",                V1Opt | V2Opt),
            // Xsl3: XSLT 3.0
            new XsltAttribute("html-version",           V2Opt),
            new XsltAttribute("build-tree",             V2Opt),
            new XsltAttribute("item-separator",         V2Opt),
            new XsltAttribute("json-node-output-method", V2Opt),
            new XsltAttribute("allow-duplicate-names",  V2Opt),
            new XsltAttribute("parameter-document",     V2Opt),
            new XsltAttribute("suppress-indentation",   V2Opt),
        };
        // Xsl3: xsl:output's attributes are being read (an invalid yes/no is SEPM0016 there).
        private bool _serializationAttributes;

        private void LoadOutput()
        {
            _serializationAttributes = true;
            try
            {
                LoadOutputCore();
            }
            finally
            {
                _serializationAttributes = false;
            }
        }

        private void LoadOutputCore()
        {
            _input.GetAttributes(_outputAttributes);

            QilName? name = ParseQNameAttribute(0);
            if (name != null && !V2) ReportNYI("xsl:output/@name");
            // Xsl3: a named output definition (for xsl:result-document format="…") is kept apart from the unnamed one.
            Output output = name == null ? _compiler.Output : _compiler.NamedOutputs.TryGetValue(name, out Output? named) ? named : _compiler.NamedOutputs[name] = new Output();
            XmlWriterSettings settings = output.Settings;
            int currentPrec = _compiler.CurrentPrecedence;
            TriState triState;

            if (_input.MoveToXsltAttribute(1, "method"))
            {
                if (output.MethodPrec <= currentPrec)
                {
                    _compiler.EnterForwardsCompatible();
                    XmlOutputMethod outputMethod;
                    XmlQualifiedName? method = ParseOutputMethod(_input.Value, out outputMethod);
                    if (_compiler.ExitForwardsCompatible(_input.ForwardCompatibility) && method != null)
                    {
                        if (currentPrec == output.MethodPrec && !output.Method!.Equals(method))
                        {
                            ReportOutputRedefinition("method");
                        }
                        settings.SetOutputMethod(outputMethod);
                        output.Method = method;
                        output.MethodPrec = currentPrec;
                        output.Xhtml = method.Name == "xhtml" && method.Namespace.Length == 0;
                    }
                }
            }

            TriState byteOrderMask = ParseYesNoAttribute(2, "byte-order-mark");
            if (byteOrderMask != TriState.Unknown)
            {
                if (!V2) ReportNYI("xsl:output/@byte-order-mark");
                else if (TakesParameter31(output, "byte-order-mark", currentPrec)) output.ByteOrderMark = byteOrderMask;
            }

            if (_input.MoveToXsltAttribute(3, "cdata-section-elements"))
            {
                // Do not check the import precedence, the effective value is the union of all specified values
                _compiler.EnterForwardsCompatible();
                string[] qnames = XmlConvert.SplitString(_input.Value);
                List<XmlQualifiedName> list = new List<XmlQualifiedName>();
                for (int i = 0; i < qnames.Length; i++)
                {
                    list.Add(ResolveQName(/*ignoreDefaultNs:*/false, qnames[i]));
                }
                if (_compiler.ExitForwardsCompatible(_input.ForwardCompatibility))
                {
                    settings.CDataSectionElements.AddRange(list);
                }
            }

            if (_input.MoveToXsltAttribute(4, "doctype-public"))
            {
                if (output.DocTypePublicPrec <= currentPrec)
                {
                    if (currentPrec == output.DocTypePublicPrec && settings.DocTypePublic != _input.Value)
                    {
                        ReportOutputRedefinition("doctype-public");
                    }
                    // Xsl3: "" means absent, so a lower-precedence value can be overridden (XSLT 2.0 erratum E31).
                    settings.DocTypePublic = V2 && _input.Value.Length == 0 ? null : _input.Value;
                    output.DocTypePublicPrec = currentPrec;
                }
            }

            if (_input.MoveToXsltAttribute(5, "doctype-system"))
            {
                if (output.DocTypeSystemPrec <= currentPrec)
                {
                    if (currentPrec == output.DocTypeSystemPrec && settings.DocTypeSystem != _input.Value)
                    {
                        ReportOutputRedefinition("doctype-system");
                    }
                    settings.DocTypeSystem = V2 && _input.Value.Length == 0 ? null : _input.Value;
                    output.DocTypeSystemPrec = currentPrec;
                }
            }

            if (_input.MoveToXsltAttribute(6, "encoding"))
            {
                if (output.EncodingPrec <= currentPrec)
                {
                    try
                    {
                        // Encoding.GetEncoding() should never throw NotSupportedException, only ArgumentException
                        // Xsl3: in 2.0+ the common aliases too (utf8, as TEI's LaTeX stylesheets write it).
                        Encoding encoding = V2 ? Xsl3.Runtime.SerializationParameters.EncodingByName(_input.Value) : Encoding.GetEncoding(_input.Value);
                        if (currentPrec == output.EncodingPrec && output.Encoding != _input.Value)
                        {
                            ReportOutputRedefinition("encoding");
                        }
                        settings.Encoding = encoding;
                        output.Encoding = _input.Value;
                        output.EncodingPrec = currentPrec;
                    }
                    catch (ArgumentException)
                    {
                        if (V2)
                        {
                            ReportError(SR.Xml_UserException, $"SESU0007: the encoding '{_input.Value}' is not supported");
                        }
                        else if (!_input.ForwardCompatibility)
                        {
                            ReportWarning(/*[XT_004]*/SR.Xslt_InvalidEncoding, _input.Value);
                        }
                    }
                }
            }

            // Xsl3: URI escaping of HTML attributes isn't done by the html writer either way.
            bool escapeUriAttributes = ParseYesNoAttribute(7, "escape-uri-attributes") != TriState.False;
            if (!escapeUriAttributes && !V2) ReportNYI("xsl:output/@escape-uri-attributes == flase()");
            if (V2 && _input.AttributeExists(7, "escape-uri-attributes") && TakesParameter31(output, "escape-uri-attributes", currentPrec))
            {
                output.EscapeUriAttributes = escapeUriAttributes;
            }

            TriState includeContentType = ParseYesNoAttribute(8, "include-content-type");
            if (includeContentType != TriState.Unknown)
            {
                if (!V2) { if (includeContentType == TriState.False) ReportNYI("xsl:output/@include-content-type == flase()"); }
                else if (TakesParameter31(output, "include-content-type", currentPrec)) output.IncludeContentType = includeContentType == TriState.True;
            }

            triState = ParseYesNoAttribute(9, "indent");
            if (triState != TriState.Unknown)
            {
                if (output.IndentPrec <= currentPrec)
                {
                    bool indent = (triState == TriState.True);
                    if (currentPrec == output.IndentPrec && settings.Indent != indent)
                    {
                        ReportOutputRedefinition("indent");
                    }
                    settings.Indent = indent;
                    output.IndentPrec = currentPrec;
                }
            }

            if (_input.MoveToXsltAttribute(10, "media-type"))
            {
                if (output.MediaTypePrec <= currentPrec)
                {
                    if (currentPrec == output.MediaTypePrec && settings.MediaType != _input.Value)
                    {
                        ReportOutputRedefinition("media-type");
                    }
                    settings.MediaType = _input.Value;
                    output.MediaTypePrec = currentPrec;
                }
            }

            if (_input.MoveToXsltAttribute(11, "normalization-form"))
            {
                if (!V2)
                {
                    ReportNYI("xsl:output/@normalization-form");
                }
                else if (Runtime.OutputExtras.TryParseNormalizationForm(_input.Value, out _))
                {
                    if (TakesParameter31(output, "normalization-form", currentPrec))
                    {
                        output.NormalizationForm = _input.Value;
                    }
                }
                else
                {
                    ReportError(SR.Xml_UserException, $"SESU0011: normalization-form=\"{_input.Value}\" is not supported");
                }
            }

            triState = ParseYesNoAttribute(12, "omit-xml-declaration");
            if (triState != TriState.Unknown)
            {
                if (output.OmitXmlDeclarationPrec <= currentPrec)
                {
                    bool omitXmlDeclaration = (triState == TriState.True);
                    if (currentPrec == output.OmitXmlDeclarationPrec && settings.OmitXmlDeclaration != omitXmlDeclaration)
                    {
                        ReportOutputRedefinition("omit-xml-declaration");
                    }
                    settings.OmitXmlDeclaration = omitXmlDeclaration;
                    output.OmitXmlDeclarationPrec = currentPrec;
                }
            }

            // Xsl3: standalone="omit" (XSLT 2.0) is the default, no standalone pseudo-attribute.
            triState = V2 && _input.MoveToXsltAttribute(13, "standalone") && _input.Value.Trim() == "omit" ? TriState.Unknown : ParseYesNoAttribute(13, "standalone");
            if (triState != TriState.Unknown)
            {
                if (output.StandalonePrec <= currentPrec)
                {
                    XmlStandalone standalone = (triState == TriState.True) ? XmlStandalone.Yes : XmlStandalone.No;
                    if (currentPrec == output.StandalonePrec && settings.Standalone != standalone)
                    {
                        ReportOutputRedefinition("standalone");
                    }
                    settings.Standalone = standalone;
                    output.StandalonePrec = currentPrec;
                }
            }

            // Xsl3: undeclare-prefixes only matters for XML 1.1 output, which isn't supported.
            bool undeclarePrefixes = ParseYesNoAttribute(14, "undeclare-prefixes") == TriState.True;
            if (undeclarePrefixes && !V2) ReportNYI("xsl:output/@undeclare-prefixes == true()");

            List<QilName> useCharacterMaps = ParseUseCharacterMaps(15);
            if (useCharacterMaps.Count != 0)
            {
                if (V2)
                {
                    // Xsl3: the maps of every declaration are used; where they map the same character, those of the
                    // higher import precedence win (they come later in the list).
                    output.UseCharacterMaps ??= new List<QilName>();
                    output.UseCharacterMapsPrec ??= new List<int>();
                    int at = output.UseCharacterMapsPrec.FindLastIndex(p => p <= currentPrec) + 1;
                    output.UseCharacterMaps.InsertRange(at, useCharacterMaps);
                    output.UseCharacterMapsPrec.InsertRange(at, System.Linq.Enumerable.Repeat(currentPrec, useCharacterMaps.Count));
                }
                else ReportNYI("xsl:output/@use-character-maps");
            }

            if (_input.MoveToXsltAttribute(16, "version"))
            {
                if (output.VersionPrec <= currentPrec)
                {
                    if (currentPrec == output.VersionPrec && output.Version != _input.Value)
                    {
                        ReportOutputRedefinition("version");
                    }
                    // BUGBUG: Check that version is a valid nmtoken
                    // ignore version since we support only one version for both xml (1.0) and html (4.0)
                    output.Version = _input.Value;
                    output.VersionPrec = currentPrec;
                }
            }

            if (V2 && _input.MoveToXsltAttribute(19, "item-separator") && TakesParameter31(output, "item-separator", currentPrec))
            {
                output.ItemSeparator = _input.Value;
            }
            if (V2 && _input.MoveToXsltAttribute(20, "json-node-output-method") && TakesParameter31(output, "json-node-output-method", currentPrec))
            {
                output.JsonNodeOutputMethod = _input.Value.Trim();
            }
            if (V2)
            {
                TriState duplicates = ParseYesNoAttribute(21, "allow-duplicate-names");
                if (duplicates != TriState.Unknown && TakesParameter31(output, "allow-duplicate-names", currentPrec))
                {
                    output.AllowDuplicateNames = duplicates == TriState.True;
                }
            }
            if (V2 && _input.MoveToXsltAttribute(17, "html-version"))
            {
                // Xsl3: XSLT 3.0 §26 (HTML5 serialization, Serialization 3.1 §7.1).
                if (!IsDecimal31(_input.Value))
                {
                    ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "html-version", _input.Value);
                }
                else if (TakesParameter31(output, "html-version", currentPrec))
                {
                    output.HtmlVersion = _input.Value.Trim().TrimStart('+');
                }
            }
            if (V2 && _input.MoveToXsltAttribute(22, "parameter-document"))
            {
                // Xsl3: Serialization 3.1 §3.1. The declaration's own attributes override its parameter document.
                Uri documentUri = ResolveUri(_input.Value.Trim(), _input.BaseUri!);
                var own = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 1; i < _outputAttributes.Length; i++)
                {
                    if (_input.MoveToXsltAttribute(i, _outputAttributes[i].name))
                    {
                        own.Add(_outputAttributes[i].name);
                    }
                }
                try
                {
                    using XmlReader documentReader = CreateReader(documentUri, _xmlResolver);
                    Runtime.ParameterDocument.Parameters parameters = Runtime.ParameterDocument.Read(documentReader);
                    if (TakesParameter31(output, "parameter-document", currentPrec))
                    {
                        output.ParameterDocument = parameters with { Values = parameters.Values.FindAll(p => !own.Contains(p.Name)) };
                        // The method and item-separator decide how the result is built (a tree or the raw sequence).
                        foreach (var (parameter, value) in output.ParameterDocument.Values)
                        {
                            if (parameter == "method" && value.StartsWith("Q{}", StringComparison.Ordinal) && output.MethodPrec <= currentPrec &&
                                ParseOutputMethod(value[3..], out XmlOutputMethod documentMethod) is XmlQualifiedName method)
                            {
                                settings.SetOutputMethod(documentMethod);
                                output.Method = method;
                                output.MethodPrec = currentPrec;
                                output.Xhtml = method.Name == "xhtml";
                            }
                            else if (parameter == "item-separator" && TakesParameter31(output, "item-separator", currentPrec))
                            {
                                output.ItemSeparator = value;
                            }
                        }
                    }
                }
                catch (Xdm.XdmException e)
                {
                    ReportError(SR.Xml_UserException, e.Message);
                }
            }
            if (V2 && _checkedOutputs.Add(name?.ToString() ?? ""))
            {
                // The first declaration of an output definition read has the highest import precedence: those it
                // overrides are not checked.
                CheckSerializationParameters31();
            }

            CheckNoContent();
        }

        private readonly HashSet<string> _checkedOutputs = new();

        // Xsl3: whether a 2.0+ serialization parameter of the declaration being read is the one to use: no
        // declaration of a higher import precedence has set it.
        private static bool TakesParameter31(Output output, string parameter, int precedence)
        {
            if (output.ParameterPrec.TryGetValue(parameter, out int existing) && existing > precedence)
            {
                return false;
            }
            output.ParameterPrec[parameter] = precedence;
            return true;
        }

        // Xsl3: combinations of serialization parameters that are errors (Serialization 3.1 §3).
        private void CheckSerializationParameters31()
        {
            string? Value(int attNum, string name) => _input.MoveToXsltAttribute(attNum, name) ? _input.Value.Trim() : null;
            string? method = Value(1, "method"), version = Value(16, "version"), standalone = Value(13, "standalone");
            bool omitDeclaration = Value(12, "omit-xml-declaration") is "yes" or "true" or "1";
            if (method is null or "xml" or "xhtml" && omitDeclaration && (standalone is not (null or "omit") || version is not (null or "1.0") && Value(5, "doctype-system") != null))
            {
                ReportError(SR.Xml_UserException, "SEPM0009: omit-xml-declaration=\"yes\" conflicts with standalone or with a version other than 1.0 and a doctype-system");
            }
            if (Value(14, "undeclare-prefixes") is "yes" or "true" or "1" && method is null or "xml" or "xhtml" && version is null or "1.0")
            {
                ReportError(SR.Xml_UserException, "SEPM0010: undeclare-prefixes=\"yes\" requires XML 1.1 output");
            }
            if (method == "html" && version is not (null or "1.0" or "4.0" or "4.01" or "5" or "5.0"))
            {
                ReportError(SR.Xml_UserException, $"SESU0013: HTML version {version} is not supported");
            }
            if (Value(4, "doctype-public") is { } publicId && System.Linq.Enumerable.Any(publicId, c => !(c is ' ' or '\r' or '\n' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' || "-'()+,./:=?;!*#@$_%".Contains(c))))
            {
                ReportError(SR.Xml_UserException, "SEPM0016: the doctype-public value isn't a valid public identifier");
            }
        }

        /*
            Default values for method="xml" :   version="1.0"   indent="no"     media-type="text/xml"
            Default values for method="html":   version="4.0"   indent="yes"    media-type="text/html"
            Default values for method="text":                                   media-type="text/plain"
        */
        private void ProcessOutputSettings()
        {
            ProcessOutputSettings(_compiler.Output, V2);
            foreach (Output named in _compiler.NamedOutputs.Values)
            {
                ProcessOutputSettings(named, V2);
            }
        }

        private static void ProcessOutputSettings(Output output, bool v2)
        {
            XmlWriterSettings settings = output.Settings;
            // Xsl3: in 2.0 a UTF-8 serialization has no byte order mark unless byte-order-mark="yes" asks for one.
            if (v2 && output.ByteOrderMark == TriState.Unknown && settings.Encoding is UTF8Encoding)
            {
                output.ByteOrderMark = TriState.False;
            }
            // Xsl3: byte-order-mark (XSLT 2.0) chooses the encoding's preamble; .NET's UTF-8 and UTF-16 have one by default.
            if (output.ByteOrderMark != TriState.Unknown)
            {
                bool bom = output.ByteOrderMark == TriState.True;
                settings.Encoding = settings.Encoding switch
                {
                    UTF8Encoding => new UTF8Encoding(bom),
                    UnicodeEncoding u => new UnicodeEncoding(u.CodePage == 1201, bom),
                    var e => e,
                };
            }

            // Xsl3: the xml and xhtml methods write a carriage return in text as &#xD; (Serialization 3.1 §5.1.6), and
            // keep it as it is in comments and processing instructions, where it can't be escaped.
            if (v2 && settings.OutputMethod is XmlOutputMethod.Xml or XmlOutputMethod.AutoDetect)
            {
                settings.NewLineHandling = NewLineHandling.Entitize;
            }

            // version is ignored, indent="no" by default
            // Xsl3: html and xhtml indent by default (Serialization 3.1 §2).
            if ((settings.OutputMethod == XmlOutputMethod.Html || v2 && output.Xhtml) && output.IndentPrec == Output.NeverDeclaredPrec)
            {
                settings.Indent = true;
            }
            if (output.MediaTypePrec == Output.NeverDeclaredPrec)
            {
                settings.MediaType =
                    settings.OutputMethod == XmlOutputMethod.Xml ? "text/xml" :
                    settings.OutputMethod == XmlOutputMethod.Html ? "text/html" :
                    settings.OutputMethod == XmlOutputMethod.Text ? "text/plain" : null;
            }
        }

        private void CheckUseAttrubuteSetInList(IList<XslNode> list)
        {
            foreach (XslNode xslNode in list)
            {
                switch (xslNode.NodeType)
                {
                    case XslNodeType.UseAttributeSet:
                        AttributeSet? usedAttSet;
                        if (_compiler.AttributeSets.TryGetValue(xslNode.Name!, out usedAttSet))
                        {
                            CheckAttributeSetsDfs(usedAttSet);
                        }
                        else
                        {
                            // The error will be reported in QilGenerator while compiling this attribute set.
                        }
                        break;
                    case XslNodeType.List:
                        CheckUseAttrubuteSetInList(xslNode.Content);
                        break;
                }
            }
        }

        private void CheckAttributeSetsDfs(AttributeSet attSet)
        {
            Debug.Assert(attSet != null);
            switch (attSet.CycleCheck)
            {
                case CycleCheck.NotStarted:
                    attSet.CycleCheck = CycleCheck.Processing;
                    CheckUseAttrubuteSetInList(attSet.Content);
                    attSet.CycleCheck = CycleCheck.Completed;
                    break;
                case CycleCheck.Completed:
                    break;
                default:
                    Debug.Assert(attSet.CycleCheck == CycleCheck.Processing);
                    Debug.Assert(attSet.Content[0].SourceLine != null);
                    _compiler.ReportError(/*[XT0720]*/attSet.Content[0].SourceLine!, SR.Xslt_CircularAttributeSet, attSet.Name!.QualifiedName);
                    break;
            }
        }

        private readonly XsltAttribute[] _keyAttributes = {
            new XsltAttribute("name",      V1Req | V2Req),
            new XsltAttribute("match",     V1Req | V2Req),
            new XsltAttribute("use",       V1Req | V2Opt),
            new XsltAttribute("collation", V2Opt),
            new XsltAttribute("composite", V2Opt), // Xsl3: XSLT 3.0 §20.2.1
        };
        private void LoadKey(NsDecl? stylesheetNsList)
        {
            ContextInfo ctxInfo = _input.GetAttributes(_keyAttributes);
            ctxInfo.nsList = MergeNamespaces(ctxInfo.nsList, stylesheetNsList);

            QilName keyName = ParseQNameAttribute(0)!;
            if (V2 && _packageId > 0)
            {
                keyName = PackageComponents.Mangle(keyName, _packageId); // Xsl3: keys are local to a package
            }
            string? match = ParseStringAttribute(1, "match");
            string? use = ParseStringAttribute(2, "use");
            string? collation = ParseCollationAttribute(3);
            bool composite = V2 && ParseYesNoAttribute(4, "composite") == TriState.True;

            _input.MoveToElement();

            List<XslNode>? content = null;

            if (V1)
            {
                if (use == null)
                {
                    _input.SkipNode();
                }
                else
                {
                    CheckNoContent();
                }
            }
            else
            {
                content = LoadInstructions();
                // Load the end tag only if the content is not empty
                if (content.Count != 0)
                {
                    content = LoadEndTag(content);
                }
                if ((use == null) == (content.Count == 0))
                {
                    ReportError(/*[XTSE1205]*/SR.Xslt_KeyCntUse);
                }
            }

            Key key = (Key)SetInfo(F.Key(keyName, match, use, _input.XslVersion), V2 ? content : null, ctxInfo);
            key.PackageId = _packageId; // Xsl3: its code's names are its package's
            if (collation != null || composite)
            {
                key.V2 = new XslNodeV2 { Collation = collation, Composite = composite };
            }

            if (_compiler.Keys.Contains(keyName))
            {
                // Add to the list of previous definitions
                _compiler.Keys[keyName].Add(key);
            }
            else
            {
                // First definition of key with that name
                List<Key> defList = new List<Key>();
                defList.Add(key);
                _compiler.Keys.Add(defList);
            }
        }

        private readonly XsltAttribute[] _decimalFormatAttributes = {
            new XsltAttribute("name",               V1Opt | V2Opt),
            new XsltAttribute("infinity",           V1Opt | V2Opt),
            new XsltAttribute("NaN",                V1Opt | V2Opt),
            new XsltAttribute("decimal-separator",  V1Opt | V2Opt),
            new XsltAttribute("grouping-separator", V1Opt | V2Opt),
            new XsltAttribute("percent",            V1Opt | V2Opt),
            new XsltAttribute("per-mille",          V1Opt | V2Opt),
            new XsltAttribute("zero-digit",         V1Opt | V2Opt),
            new XsltAttribute("digit",              V1Opt | V2Opt),
            new XsltAttribute("pattern-separator",  V1Opt | V2Opt),
            new XsltAttribute("minus-sign",         V1Opt | V2Opt),
            new XsltAttribute("exponent-separator", V2Opt), // Xsl3: XSLT 3.0
        };
        private unsafe void LoadDecimalFormat(NsDecl? stylesheetNsList)
        {
            const int NumCharAttrs = 8, NumSignAttrs = 7;
            ContextInfo ctxInfo = _input.GetAttributes(_decimalFormatAttributes);
            ctxInfo.nsList = MergeNamespaces(ctxInfo.nsList, stylesheetNsList);

            XmlQualifiedName name;
            if (_input.MoveToXsltAttribute(0, "name"))
            {
                _compiler.EnterForwardsCompatible();
                name = ResolveQName(/*ignoreDefaultNs:*/true, _input.Value);
                if (!_compiler.ExitForwardsCompatible(_input.ForwardCompatibility))
                {
                    name = new XmlQualifiedName();
                }
            }
            else
            {
                // Use name="" for the default decimal-format
                name = new XmlQualifiedName();
            }
            if (V2 && _packageId > 0)
            {
                // Xsl3: decimal formats are local to a package (XSLT 3.0 §3.5.1).
                name = new XmlQualifiedName(name.Name, PackageComponents.MangleNamespace(_packageId, name.Namespace));
            }

            string infinity = DecimalFormatDecl.Default.InfinitySymbol;
            if (_input.MoveToXsltAttribute(1, "infinity"))
            {
                infinity = _input.Value;
            }

            string nan = DecimalFormatDecl.Default.NanSymbol;
            if (_input.MoveToXsltAttribute(2, "NaN"))
            {
                nan = _input.Value;
            }

            char[] DefaultValues = DecimalFormatDecl.Default.Characters;
            if (V2)
            {
                // Xsl3: declarations of one format merge, each giving some attributes (XSLT 3.0 §4.7): the distinct
                // characters are checked once all are read (Compiler.Compile). A character may be outside the BMP.
                Span<int> codePoints = stackalloc int[NumCharAttrs];
                for (int idx = 0; idx < NumCharAttrs; idx++)
                {
                    codePoints[idx] = ParseCodePointAttribute(3 + idx, _decimalFormatAttributes[3 + idx].name, DefaultValues[idx]);
                }
                // zero-digit must be a digit whose value is zero (XTSE1295).
                if (!(System.Text.Rune.GetUnicodeCategory(new System.Text.Rune(codePoints[4])) == System.Globalization.UnicodeCategory.DecimalDigitNumber
                    && System.Text.Rune.GetNumericValue(new System.Text.Rune(codePoints[4])) == 0))
                {
                    ReportError(SR.Xml_UserException, $"XTSE1295: zero-digit '{char.ConvertFromUtf32(codePoints[4])}' is not a digit with numeric value zero");
                }
                MergeDecimalFormat31(name, infinity, nan, codePoints, ParseCodePointAttribute(11, "exponent-separator", 'e'), ctxInfo);
                CheckNoContent();
                return;
            }
            Span<char> characters = stackalloc char[NumCharAttrs];
            Debug.Assert(NumCharAttrs == DefaultValues.Length);

            for (int idx = 0; idx < NumCharAttrs; idx++)
            {
                characters[idx] = ParseCharAttribute(3 + idx, _decimalFormatAttributes[3 + idx].name, DefaultValues[idx]);
            }

            // Check all NumSignAttrs signs are distinct
            for (int i = 0; i < NumSignAttrs; i++)
            {
                for (int j = i + 1; j < NumSignAttrs; j++)
                {
                    if (characters[i] == characters[j])
                    {
                        // Try move to second attribute and if it is missing to first.
                        bool dummy = _input.MoveToXsltAttribute(3 + j, _decimalFormatAttributes[3 + j].name) || _input.MoveToXsltAttribute(3 + i, _decimalFormatAttributes[3 + i].name);
                        Debug.Assert(dummy, "One of the attrs should have lineInfo. If both are default they can't conflict.");
                        ReportError(/*[XT1300]*/SR.Xslt_DecimalFormatSignsNotDistinct, _decimalFormatAttributes[3 + i].name, _decimalFormatAttributes[3 + j].name);
                        break;
                    }
                }
            }

            if (V2 && _compiler.DecimalFormats.Contains(name) && _compiler.DecimalFormats[name].ImportPrecedence > _curStylesheet!.ImportPrecedence)
            {
                // Xsl3: overridden by a definition of higher import precedence (modules are read in decreasing order).
            }
            else if (_compiler.DecimalFormats.Contains(name))
            {
                // Check all attributes have the same values
                DecimalFormatDecl format = _compiler.DecimalFormats[name];
                _input.MoveToXsltAttribute(1, "infinity");
                CheckError(infinity != format.InfinitySymbol, /*[XT1290]*/SR.Xslt_DecimalFormatRedefined, "infinity", infinity);
                _input.MoveToXsltAttribute(2, "NaN");
                CheckError(nan != format.NanSymbol, /*[XT1290]*/SR.Xslt_DecimalFormatRedefined, "NaN", nan);
                for (int idx = 0; idx < NumCharAttrs; idx++)
                {
                    _input.MoveToXsltAttribute(3 + idx, _decimalFormatAttributes[3 + idx].name);
                    CheckError(characters[idx] != format.Characters[idx], /*[XT1290]*/SR.Xslt_DecimalFormatRedefined, _decimalFormatAttributes[3 + idx].name, char.ToString(characters[idx]));
                }
                Debug.Assert(name.Equals(format.Name));
            }
            else
            {
                // Add format to the global collection
                DecimalFormatDecl format = new DecimalFormatDecl(name, infinity, nan, new string(characters));
                format.ImportPrecedence = _curStylesheet!.ImportPrecedence;
                _compiler.DecimalFormats.Add(format);
            }
            CheckNoContent();
        }

        // Xsl3: a 2.0 xsl:decimal-format declaration, merged with the others of its format and import precedence.
        private void MergeDecimalFormat31(XmlQualifiedName name, string infinity, string nan, ReadOnlySpan<int> characters, int exponent, ContextInfo ctxInfo)
        {
            // Each attribute comes from the declaration of highest import precedence that gives it (modules are read in
            // decreasing order).
            bool lower = _compiler.DecimalFormats.Contains(name) && _compiler.DecimalFormats[name].ImportPrecedence > _curStylesheet!.ImportPrecedence;
            DecimalFormatDecl format;
            if (_compiler.DecimalFormats.Contains(name))
            {
                format = _compiler.DecimalFormats[name];
            }
            else
            {
                format = new DecimalFormatDecl(name, DecimalFormatDecl.Default.InfinitySymbol, DecimalFormatDecl.Default.NanSymbol, new string(DecimalFormatDecl.Default.Characters));
                format.ImportPrecedence = _curStylesheet!.ImportPrecedence;
                format.Line = ctxInfo.lineInfo;
                _compiler.DecimalFormats.Add(format);
            }
            for (int attNum = 1; attNum <= 11; attNum++)
            {
                if (!_input.AttributeExists(attNum, _decimalFormatAttributes[attNum].name))
                {
                    continue;
                }
                string value = attNum switch
                {
                    1 => infinity,
                    2 => nan,
                    11 => char.ConvertFromUtf32(exponent),
                    _ => char.ConvertFromUtf32(characters[attNum - 3]),
                };
                string current = attNum switch
                {
                    1 => format.InfinitySymbol,
                    2 => format.NanSymbol,
                    11 => char.ConvertFromUtf32(format.Exponent),
                    _ => char.ConvertFromUtf32(format.CodePoints[attNum - 3]),
                };
                if (lower && format.Specified.Contains(attNum))
                {
                    continue;
                }
                if (format.Specified.Contains(attNum) && current != value)
                {
                    _input.MoveToXsltAttribute(attNum, _decimalFormatAttributes[attNum].name);
                    ReportError(/*[XTSE1290]*/SR.Xslt_DecimalFormatRedefined, _decimalFormatAttributes[attNum].name, value);
                    continue;
                }
                format.Specified.Add(attNum);
                switch (attNum)
                {
                    case 1: format.InfinitySymbol = value; break;
                    case 2: format.NanSymbol = value; break;
                    case 11: format.Exponent = exponent; break;
                    default: format.CodePoints[attNum - 3] = characters[attNum - 3]; break;
                }
            }
        }

        private readonly XsltAttribute[] _namespaceAliasAttributes = {
            new XsltAttribute("stylesheet-prefix", V1Req | V2Req),
            new XsltAttribute("result-prefix",     V1Req | V2Req)
        };
        private void LoadNamespaceAlias(NsDecl? stylesheetNsList)
        {
            ContextInfo ctxInfo = _input.GetAttributes(_namespaceAliasAttributes);
            ctxInfo.nsList = MergeNamespaces(ctxInfo.nsList, stylesheetNsList);

            string? stylesheetNsUri = null;
            string? resultPrefix = null;
            string? resultNsUri = null;

            if (_input.MoveToXsltAttribute(0, "stylesheet-prefix"))
            {
                if (_input.Value.Length == 0)
                {
                    ReportError(/*[XT_005]*/SR.Xslt_EmptyNsAlias, "stylesheet-prefix");
                }
                else
                {
                    stylesheetNsUri = _input.LookupXmlNamespace(_input.Value == "#default" ? string.Empty : _input.Value);
                }
            }

            if (_input.MoveToXsltAttribute(1, "result-prefix"))
            {
                if (_input.Value.Length == 0)
                {
                    ReportError(/*[XT_005]*/SR.Xslt_EmptyNsAlias, "result-prefix");
                }
                else
                {
                    resultPrefix = _input.Value == "#default" ? string.Empty : _input.Value;
                    resultNsUri = _input.LookupXmlNamespace(resultPrefix);
                }
            }

            CheckNoContent();

            if (stylesheetNsUri == null || resultNsUri == null)
            {
                // At least one of attributes is missing or invalid
                return;
            }
            if (_compiler.SetNsAlias(V2 ? PackageComponents.MangleNamespace(_packageId, stylesheetNsUri) : stylesheetNsUri, resultNsUri, resultPrefix, _curStylesheet!.ImportPrecedence))
            {
                // Namespace alias redefinition
                _input.MoveToElement();
                if (V2)
                {
                    // Xsl3: an error in 2.0 (XTSE0810), a warning in 1.0.
                    ReportError(SR.Xml_UserException, $"XTSE0810: two xsl:namespace-alias declarations for {stylesheetNsUri} have the same import precedence");
                }
                else
                {
                    ReportWarning(/*[XT0810]*/SR.Xslt_DupNsAlias, stylesheetNsUri);
                }
            }
        }

        private readonly XsltAttribute[] _attributeSetAttributes = {
            new XsltAttribute("name",               V1Req | V2Req),
            new XsltAttribute("use-attribute-sets", V1Opt | V2Opt),
            new XsltAttribute("streamable",         V2Opt), // Xsl3: XSLT 3.0 (no streaming: only validated)
        };
        private void LoadAttributeSet(NsDecl? stylesheetNsList)
        {
            ContextInfo ctxInfo = _input.GetAttributes(_attributeSetAttributes);
            if (V2)
            {
                ParseYesNoAttribute(2, "streamable");
            }
            ctxInfo.nsList = MergeNamespaces(ctxInfo.nsList, stylesheetNsList);

            QilName? setName = ParseQNameAttribute(0);
            if (V2 && setName != null && _packageId > 0)
            {
                setName.NamespaceUri = PackageComponents.MangleNamespace(_packageId, setName.NamespaceUri);
            }
            string? setVisibility = _input.Visibility;
            Debug.Assert(setName != null, "Required attribute always != null");

            AttributeSet? set;
            if (!_curStylesheet!.AttributeSets!.TryGetValue(setName, out set))
            {
                set = F.AttributeSet(setName);
                // First definition for setName within this stylesheet
                _curStylesheet.AttributeSets[setName] = set;
                if (!_compiler.AttributeSets.ContainsKey(setName))
                {
                    // First definition for setName overall, adding it to the list here
                    // to ensure stable order of prototemplate functions in QilExpression
                    _compiler.AllTemplates.Add(set);
                }
            }
            if (V2)
            {
                set.PackageId = _packageId;
                set.Visibility ??= setVisibility;
                set.OverrideOf = _overrideTarget;
            }

            List<XslNode> content = new List<XslNode>();
            if (_input.MoveToXsltAttribute(1, "use-attribute-sets"))
            {
                AddUseAttributeSets(content);
            }

            QName parentName = _input.ElementName;
            if (_input.MoveToFirstChild())
            {
                do
                {
                    switch (_input.NodeType)
                    {
                        case XmlNodeType.Element:
                            // Only xsl:attribute's are allowed here
                            if (_input.IsXsltKeyword(_atoms.Attribute))
                            {
                                AddInstruction(content, XslAttribute());
                            }
                            else
                            {
                                ReportError(/*[XT_006]*/SR.Xslt_UnexpectedElement, _input.QualifiedName, parentName);
                                _input.SkipNode();
                            }
                            break;
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            break;
                        default:
                            Debug.Assert(_input.NodeType == XmlNodeType.Text);
                            ReportError(/*[XT_006]*/SR.Xslt_TextNodesNotAllowed, parentName);
                            break;
                    }
                } while (_input.MoveToNextSibling());
            }
            set.AddContent(SetInfo(F.List(), LoadEndTag(content), ctxInfo));
        }

        private void LoadGlobalVariableOrParameter(NsDecl? stylesheetNsList)
        {
            Debug.Assert(_curTemplate == null);
            Debug.Assert(!_input.CanHaveApplyImports);
            VarPar var = XslVarPar();
            if (V2)
            {
                PackageDeclaration(var, var.Visibility);
            }
            // Preserving namespaces to parse content later
            var.Namespaces = MergeNamespaces(var.Namespaces, stylesheetNsList);
            CheckError(!_curStylesheet!.AddVarPar(var), /*[XT0630]*/SR.Xslt_DupGlobalVariable, var.Name!.QualifiedName);
        }

        //: http://www.w3.org/TR/xslt#section-Defining-Template-Rules
        private readonly XsltAttribute[] _templateAttributes = {
            new XsltAttribute("match",    V1Opt | V2Opt),
            new XsltAttribute("name",     V1Opt | V2Opt),
            new XsltAttribute("priority", V1Opt | V2Opt),
            new XsltAttribute("mode",     V1Opt | V2Opt),
            new XsltAttribute("as",       V2Opt)
        };
        private void LoadTemplate(NsDecl? stylesheetNsList)
        {
            Debug.Assert(_curTemplate == null);
            ContextInfo ctxInfo = _input.GetAttributes(_templateAttributes);
            string? templateVisibility = _input.Visibility;
            ctxInfo.nsList = MergeNamespaces(ctxInfo.nsList, stylesheetNsList);

            string? match = ParseStringAttribute(0, "match");
            QilName? name = ParseQNameAttribute(1);
            double priority = double.NaN;
            if (_input.MoveToXsltAttribute(2, "priority"))
            {
                priority = XPathConvert.StringToDouble(_input.Value);
                if (V2 && !double.IsNaN(priority) && !IsDecimalLexical(_input.Value))
                {
                    ReportError(SR.Xml_UserException, $"XTSE0530: priority=\"{_input.Value}\" is not a decimal");
                }
                if (double.IsNaN(priority) && !_input.ForwardCompatibility)
                {
                    ReportError(/*[XT0530]*/SR.Xslt_InvalidAttrValue, "priority", _input.Value);
                }
            }
            QilName mode = V1 ? ParseModeAttribute(3) : ParseModeListAttribute(3);

            if (match == null)
            {
                CheckError(!_input.AttributeExists(1, "name"), /*[XT_007]*/SR.Xslt_BothMatchNameAbsent);
                CheckError(_input.AttributeExists(3, "mode"), /*[XT_008]*/SR.Xslt_ModeWithoutMatch);
                mode = nullMode;
                if (_input.AttributeExists(2, "priority"))
                {
                    if (V1)
                    {
                        ReportWarning(/*[XT_008]*/SR.Xslt_PriorityWithoutMatch);
                    }
                    else
                    {
                        ReportError(/*[XT_008]*/SR.Xslt_PriorityWithoutMatch);
                    }
                }
            }

            string? asType = V2 ? ParseStringAttribute(4, "as") : null;

            CheckReservedName(name, "template");
            _curTemplate = F.Template(name, match, mode, priority, _input.XslVersion);
            if (V2)
            {
                PackageDeclaration(_curTemplate, templateVisibility);
            }
            if (V2)
            {
                _curTemplate.V2 = new XslNodeV2 { As = asType, Modes = _templateModes, AllModes = _templateAllModes };
                _templateModes = null;
                _templateAllModes = false;
            }

            // Template without match considered to not have mode and can't call xsl:apply-imports
            // Xsl3: in 2.0+ a named template can: the current template rule is its caller's (XSLT 3.0 §6.8).
            _input.CanHaveApplyImports = (match != null) || V2;
            _curTemplate.Sheet = _curStylesheet;

            SetInfo(_curTemplate,
                LoadEndTag(LoadInstructions(InstructionFlags.AllowParam)), ctxInfo
            );

            if (!_curStylesheet!.AddTemplate(_curTemplate))
            {
                ReportError(/*[XT0660]*/SR.Xslt_DupTemplateName, _curTemplate.Name!.QualifiedName);
            }
            _curTemplate = null;
        }



        private readonly XsltAttribute[] _scriptAttributes = {
            new XsltAttribute("implements-prefix", V1Req | V2Req),
            new XsltAttribute("language",          V1Opt | V2Opt)
        };
        private void LoadMsScript(NsDecl? stylesheetNsList)
        {
            ContextInfo ctxInfo = _input.GetAttributes(_scriptAttributes);
            ctxInfo.nsList = MergeNamespaces(ctxInfo.nsList, stylesheetNsList);


            string? scriptNs = null;
            if (_input.MoveToXsltAttribute(0, "implements-prefix"))
            {
                if (_input.Value.Length == 0)
                {
                    ReportError(/*[XT_009]*/SR.Xslt_EmptyAttrValue, "implements-prefix", _input.Value);
                }
                else
                {
                    scriptNs = _input.LookupXmlNamespace(_input.Value);
                    if (scriptNs == XmlReservedNs.NsXslt)
                    {
                        ReportError(/*[XT_036]*/SR.Xslt_ScriptXsltNamespace);
                        scriptNs = null;
                    }
                }
            }

            scriptNs ??= _compiler.CreatePhantomNamespace();
            ParseStringAttribute(1, "language");

#pragma warning disable SYSLIB0062 // XsltSettings.EnableScript is obsolete
            if (!_compiler.Settings.EnableScript)
            {
                _compiler.Scripts.ScriptClasses[scriptNs] = null;
                _input.SkipNode();
                return;
            }
#pragma warning restore SYSLIB0062

            throw new PlatformNotSupportedException(SR.CompilingScriptsNotSupported); // Not adding any scripts as script compilation is not available
        }

        private readonly XsltAttribute[] _assemblyAttributes = {
            new XsltAttribute("name", V1Opt | V2Opt),
            new XsltAttribute("href", V1Opt | V2Opt)
        };

        private readonly XsltAttribute[] _usingAttributes = {
            new XsltAttribute("namespace", V1Req | V2Req)
        };

        // ----------------- Template level methods --------------------------
        // Each instruction in AST tree has nsdecl list attuched to it.
        // Load*() methods do this treek. Xsl*() methods rely on LoadOneInstruction() to do this.

        private enum InstructionFlags
        {
            None = 0x00,
            AllowParam = 0x01,
            AllowSort = 0x02,
            AllowFallback = 0x04,
        }

        private List<XslNode> LoadInstructions()
        {
            return LoadInstructions(new List<XslNode>(), InstructionFlags.None);
        }

        private List<XslNode> LoadInstructions(InstructionFlags flags)
        {
            return LoadInstructions(new List<XslNode>(), flags);
        }

        private List<XslNode> LoadInstructions(List<XslNode> content)
        {
            return LoadInstructions(content, InstructionFlags.None);
        }

        private const int MAX_LOADINSTRUCTIONS_DEPTH = 1024;
        private int _loadInstructionsDepth;
        private List<XslNode> LoadInstructions(List<XslNode> content, InstructionFlags flags)
        {
            if (++_loadInstructionsDepth > MAX_LOADINSTRUCTIONS_DEPTH)
            {
                if (LocalAppContextSwitches.LimitXPathComplexity)
                {
                    throw XslLoadException.Create(SR.Xslt_InputTooComplex);
                }
            }
            QName parentName = _input.ElementName;
            if (_input.MoveToFirstChild())
            {
                bool atTop = true;
                int sortNumber = 0;
                XslNode? result;

                do
                {
                    switch (_input.NodeType)
                    {
                        case XmlNodeType.Element:
                            string nspace = _input.NamespaceUri;
                            string name = _input.LocalName;
                            if (nspace == _atoms.UriXsl)
                            {
                                InstructionFlags instrFlag = (
                                    Ref.Equal(name, _atoms.Param) ? InstructionFlags.AllowParam :
                                    Ref.Equal(name, _atoms.Sort) ? InstructionFlags.AllowSort :
                                    /*else */ InstructionFlags.None
                                );
                                if (instrFlag != InstructionFlags.None)
                                {
                                    string? error = (
                                        (flags & instrFlag) == 0 ? /*[XT_013]*/SR.Xslt_UnexpectedElement :
                                        !atTop ? /*[XT_014]*/SR.Xslt_NotAtTop :
                                        /*else*/ null
                                    );
                                    if (error != null)
                                    {
                                        ReportError(error, _input.QualifiedName, parentName);
                                        atTop = false;
                                        _input.SkipNode();
                                        continue;
                                    }
                                    // Xsl3: whitespace before xsl:param or xsl:sort is stripped even under
                                    // xml:space="preserve" (XSLT 3.0 §4.3).
                                    while (V2 && content.Count > 0 && content[^1] is Text { Hints: SerializationHints.None } ws && ws.NodeType == XslNodeType.Text
                                        && XmlCharType.IsOnlyWhitespace(ws.Select))
                                    {
                                        content.RemoveAt(content.Count - 1);
                                    }
                                }
                                else if (V2 && name == "context-item")
                                {
                                    // Xsl3: the first child of xsl:template (XSLT 3.0 §10.1.2), before its parameters.
                                    if (!atTop || content.Exists(n => n.NodeType != XslNodeType.Text) || !((string)parentName).EndsWith("template", StringComparison.Ordinal))
                                    {
                                        ReportError(SR.Xml_UserException, "XTSE0010: xsl:context-item must be the first child of xsl:template");
                                    }
                                    // Whitespace before it is stripped, even under xml:space="preserve" (XSLT 3.0 §4.3).
                                    content.RemoveAll(n => n is Text { Hints: SerializationHints.None } t && t.NodeType == XslNodeType.Text && XmlCharType.IsOnlyWhitespace(t.Select));
                                }
                                else
                                {
                                    atTop = false;
                                }
                                result = (
                                    Ref.Equal(name, _atoms.ApplyImports) ? XslApplyImports() :
                                    Ref.Equal(name, _atoms.ApplyTemplates) ? XslApplyTemplates() :
                                    Ref.Equal(name, _atoms.CallTemplate) ? XslCallTemplate() :
                                    Ref.Equal(name, _atoms.Copy) ? XslCopy() :
                                    Ref.Equal(name, _atoms.CopyOf) ? XslCopyOf() :
                                    Ref.Equal(name, _atoms.Fallback) ? XslFallback() :
                                    Ref.Equal(name, _atoms.If) ? XslIf() :
                                    Ref.Equal(name, _atoms.Choose) ? XslChoose() :
                                    Ref.Equal(name, _atoms.ForEach) ? XslForEach() :
                                    Ref.Equal(name, _atoms.Message) ? XslMessage() :
                                    Ref.Equal(name, _atoms.Number) ? XslNumber() :
                                    Ref.Equal(name, _atoms.ValueOf) ? XslValueOf() :
                                    Ref.Equal(name, _atoms.Comment) ? XslComment() :
                                    Ref.Equal(name, _atoms.ProcessingInstruction) ? XslProcessingInstruction() :
                                    Ref.Equal(name, _atoms.Text) ? XslText() :
                                    Ref.Equal(name, _atoms.Element) ? XslElement() :
                                    Ref.Equal(name, _atoms.Attribute) ? XslAttribute() :
                                    Ref.Equal(name, _atoms.Variable) ? XslVarPar() :
                                    Ref.Equal(name, _atoms.Param) ? XslVarPar() :
                                    Ref.Equal(name, _atoms.Sort) ? XslSort(sortNumber++) :
                                    V2 && Ref.Equal(name, _atoms.AnalyzeString) ? XslAnalyzeString() :
                                    V2 && name == "namespace" ? XslNamespace() :
                                    V2 && Ref.Equal(name, _atoms.PerformSort) ? XslPerformSort() :
                                    V2 && Ref.Equal(name, _atoms.Document) ? XslDocument() :
                                    V2 && Ref.Equal(name, _atoms.ForEachGroup) ? XslForEachGroup() :
                                    V2 && Ref.Equal(name, _atoms.NextMatch) ? XslNextMatch() :
                                    V2 && name == "where-populated" ? XslWherePopulated() :
                                    V2 && name == "fork" ? XslFork() :
                                    V2 && name == "try" ? XslTry() :
                                    V2 && name == "iterate" ? XslIterate() :
                                    V2 && name == "source-document" ? XslSourceDocument() :
                                    V2 && name == "evaluate" ? XslEvaluate() :
                                    V2 && name == "assert" ? XslAssert() :
                                    V2 && name == "context-item" ? XslContextItem() :
                                    V2 && name == "map" ? XslMap() :
                                    V2 && name == "merge" ? XslMerge() :
                                    V2 && name == "map-entry" ? XslMapEntry() :
                                    V2 && name == "on-completion" ? XslOnCompletion() :
                                    V2 && name == "next-iteration" ? XslNextIteration() :
                                    V2 && name == "break" ? XslBreak() :
                                    V2 && name == "catch" ? XslCatch() :
                                    V2 && name == "on-empty" ? XslOnEmpty(XslNodeType.OnEmpty) :
                                    V2 && name == "on-non-empty" ? XslOnEmpty(XslNodeType.OnNonEmpty) :
                                    V2 && Ref.Equal(name, _atoms.Sequence) ? XslSequence() :
                                    V2 && Ref.Equal(name, _atoms.ResultDocument) ? XslResultDocument() :
                                    /*default:*/                                   LoadUnknownXsltInstruction(parentName)
                                );
                            }
                            else
                            {
                                atTop = false;
                                result = LoadLiteralResultElement(/*asStylesheet:*/false);
                            }
                            break;
                        case XmlNodeType.SignificantWhitespace:
                            if (V2 && content.Count > 0 && content[^1] is Text { Hints: SerializationHints.None } previous && previous.NodeType == XslNodeType.Text)
                            {
                                // Xsl3: text either side of a stylesheet comment or PI is one text node (XSLT 3.0
                                // §4.2), so a text value template may span it.
                                content[^1] = SetLineInfo(F.Text(previous.Select + _input.Value), previous.SourceLine);
                                continue;
                            }
                            result = SetLineInfo(F.Text(_input.Value), _input.BuildLineInfo());
                            break;
                        case XmlNodeType.Whitespace:
                            continue;
                        default:
                            Debug.Assert(_input.NodeType == XmlNodeType.Text);
                            atTop = false;
                            goto case XmlNodeType.SignificantWhitespace;
                    }
                    AddInstruction(content, result);
                } while (_input.MoveToNextSibling());
            }
            --_loadInstructionsDepth;
            return content;
        }

        private List<XslNode> LoadWithParams(InstructionFlags flags)
        {
            QName parentName = _input.ElementName;
            List<XslNode> content = new List<XslNode>();
            /* Process children */
            if (_input.MoveToFirstChild())
            {
                int sortNumber = 0;
                do
                {
                    switch (_input.NodeType)
                    {
                        case XmlNodeType.Element:
                            if (_input.IsXsltKeyword(_atoms.WithParam))
                            {
                                XslNode withParam = XslVarPar();
                                CheckWithParam(content, withParam);
                                AddInstruction(content, withParam);
                            }
                            else if (flags == InstructionFlags.AllowSort && _input.IsXsltKeyword(_atoms.Sort))
                            {
                                AddInstruction(content, XslSort(sortNumber++));
                            }
                            else if (flags == InstructionFlags.AllowFallback && _input.IsXsltKeyword(_atoms.Fallback))
                            {
                                XslFallback();
                            }
                            else
                            {
                                ReportError(/*[XT_016]*/SR.Xslt_UnexpectedElement, _input.QualifiedName, parentName);
                                _input.SkipNode();
                            }
                            break;
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            break;
                        default:
                            Debug.Assert(_input.NodeType == XmlNodeType.Text);
                            ReportError(/*[XT_016]*/SR.Xslt_TextNodesNotAllowed, parentName);
                            break;
                    }
                } while (_input.MoveToNextSibling());
            }
            return content;
        }

        // http://www.w3.org/TR/xslt#apply-imports
        private XslNode? XslApplyImports()
        {
            ContextInfo ctxInfo = _input.GetAttributes();
            if (!_input.CanHaveApplyImports && V2 && _input.BackwardCompatibility)
            {
                // Xsl3: backwards-compatible behaviour: an error only if evaluated without a current template rule.
                _input.SkipNode();
                return SetLineInfo(F.XslNode(XslNodeType.Sequence, null,
                    "error(QName('http://www.w3.org/2005/xqt-errors', 'err:XTDE0560'), 'xsl:apply-imports has no current template rule here')", XslVersion.Current), ctxInfo.lineInfo);
            }
            if (!_input.CanHaveApplyImports)
            {
                ReportError(/*[XT_015]*/SR.Xslt_InvalidApplyImports);
                _input.SkipNode();
                return null;
            }

            List<XslNode>? content = LoadWithParams(InstructionFlags.None);

            ctxInfo.SaveExtendedLineInfo(_input);

            if (V1)
            {
                if (content.Count != 0)
                {
                    ISourceLineInfo contentInfo = content[0].SourceLine!;
                    if (!_input.ForwardCompatibility)
                    {
                        _compiler.ReportError(contentInfo, /*[XT0260]*/SR.Xslt_NotEmptyContents, _atoms.ApplyImports);
                    }
                    else
                    {
                        return SetInfo(F.Error(XslLoadException.CreateMessage(contentInfo, /*[XT0260]*/SR.Xslt_NotEmptyContents, _atoms.ApplyImports)), null, ctxInfo);
                    }
                }
                content = null;
            }
            else if (content.Count == 0)
            {
                content = null;
            }

            return SetInfo(F.ApplyImports(/*Mode:*/_curTemplate!.Mode, _curStylesheet, _input.XslVersion), content, ctxInfo);
        }

        // http://www.w3.org/TR/xslt#section-Applying-Template-Rules
        private readonly XsltAttribute[] _applyTemplatesAttributes = {
            new XsltAttribute("select", V1Opt | V2Opt),
            new XsltAttribute("mode",   V1Opt | V2Opt)
        };
        private XslNode XslApplyTemplates()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_applyTemplatesAttributes);

            string? explicitSelect = ParseStringAttribute(0, "select");
            string select = explicitSelect ?? "node()";
            QilName mode = ParseModeAttribute(1);

            List<XslNode> content = LoadWithParams(InstructionFlags.AllowSort);
            ctxInfo.SaveExtendedLineInfo(_input);
            XslNodeEx applyTemplates = F.ApplyTemplates(mode, select, ctxInfo, _input.XslVersion);
            applyTemplates.DefaultSelect = explicitSelect == null;
            return SetInfo(applyTemplates, content, ctxInfo);
        }

        // http://www.w3.org/TR/xslt#named-templates
        // http://www.w3.org/TR/xslt#element-call-template
        private readonly XsltAttribute[] _callTemplateAttributes = {
            new XsltAttribute("name", V1Req | V2Req)
        };
        private XslNode XslCallTemplate()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_callTemplateAttributes);
            QilName? name = ParseQNameAttribute(0);

            List<XslNode> content = LoadWithParams(InstructionFlags.None);
            ctxInfo.SaveExtendedLineInfo(_input);
            return SetInfo(F.CallTemplate(name, ctxInfo), content, ctxInfo);
        }

        // http://www.w3.org/TR/xslt#copying
        // http://www.w3.org/TR/xslt20/#element-copy
        private readonly XsltAttribute[] _copyAttributes = {
            new XsltAttribute("copy-namespaces",    V2Opt),
            new XsltAttribute("inherit-namespaces", V2Opt),
            new XsltAttribute("use-attribute-sets", V1Opt | V2Opt),
            new XsltAttribute("type",               V2Opt),
            new XsltAttribute("validation",         V2Opt),
            new XsltAttribute("select",             V2Opt), // Xsl3: XSLT 3.0
        };
        private XslNode XslCopy()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_copyAttributes);

            TriState copyNamespacesAtt = ParseYesNoAttribute(0, "copy-namespaces");
            TriState inheritNamespacesAtt = ParseYesNoAttribute(1, "inherit-namespaces");
            if (V1 && copyNamespacesAtt == TriState.False) ReportNYI("xsl:copy[@copy-namespaces    = 'no']");
            if (V1 && inheritNamespacesAtt == TriState.False) ReportNYI("xsl:copy[@inherit-namespaces = 'no']");

            List<XslNode> content = new List<XslNode>();
            if (_input.MoveToXsltAttribute(2, "use-attribute-sets"))
            {
                AddUseAttributeSets(content);
            }

            XmlQualifiedName? type = ParseTypeAttribute(3);
            string? validation = ParseValidationAttribute(4, /*defVal:*/false);

            string? select = V2 ? ParseStringAttribute(5, "select") : null;
            XslNode copy = SetInfo(F.Copy(), LoadEndTag(LoadInstructions(content)), ctxInfo);
            if (V2)
            {
                copy.V2 = new XslNodeV2 { CopyNamespaces = copyNamespacesAtt, InheritNamespaces = inheritNamespacesAtt };
                WithValidation(copy, validation, type);
            }
            if (select != null)
            {
                // Xsl3: xsl:copy select (XSLT 3.0 §11.9.1) copies the item it selects, which is the context item
                // of its content; none gives nothing, several are an error (XTTE3180).
                string single = $"let $Q{{urn:xsl3}}copy := ({select}) return if (count($Q{{urn:xsl3}}copy) gt 1) " +
                    "then error(QName('http://www.w3.org/2005/xqt-errors', 'XTTE3180'), 'The select expression of xsl:copy gives more than one item') " +
                    "else $Q{urn:xsl3}copy";
                return SetInfo(F.ForEach(single, ctxInfo, _input.XslVersion), new List<XslNode> { copy }, ctxInfo);
            }
            return copy;
        }

        private readonly XsltAttribute[] _copyOfAttributes = {
            new XsltAttribute("select",          V1Req | V2Req),
            new XsltAttribute("copy-namespaces", V2Opt),
            new XsltAttribute("type",            V2Opt),
            new XsltAttribute("validation",      V2Opt)
        };
        private XslNode XslCopyOf()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_copyOfAttributes);
            string? select = ParseStringAttribute(0, "select");
            TriState copyNamespacesAtt = ParseYesNoAttribute(1, "copy-namespaces");
            if (V1 && copyNamespacesAtt == TriState.False) ReportNYI("xsl:copy-of[@copy-namespaces    = 'no']");

            XmlQualifiedName? type = ParseTypeAttribute(2);
            string? validation = ParseValidationAttribute(3, /*defVal:*/false);

            CheckNoContent();
            XslNode copyOf = SetInfo(F.CopyOf(select, _input.XslVersion), null, ctxInfo);
            if (V2)
            {
                copyOf.V2 = new XslNodeV2 { CopyNamespaces = copyNamespacesAtt, CopyAccumulators = _input.CopyAccumulators is "yes" or "true" or "1" };
                WithValidation(copyOf, validation, type);
                if (_input.CopyAccumulators is not (null or "yes" or "true" or "1" or "no" or "false" or "0"))
                {
                    ReportError(/*[XTSE0020]*/SR.Xslt_InvalidAttrValue, "copy-accumulators", _input.CopyAccumulators);
                }
            }
            return copyOf;
        }

        // http://www.w3.org/TR/xslt#fallback
        // See LoadFallbacks() for real fallback implementation
        private XslNode? XslFallback()
        {
            _input.GetAttributes();
            _input.SkipNode();
            return null;
        }

        private readonly XsltAttribute[] _ifAttributes = {
            new XsltAttribute("test", V1Req | V2Req)
        };
        private XslNode XslIf()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_ifAttributes);
            string? test = ParseStringAttribute(0, "test");

            return SetInfo(F.If(test, _input.XslVersion), LoadInstructions(), ctxInfo);
        }

        private XslNode XslChoose()
        {
            ContextInfo ctxInfo = _input.GetAttributes();

            List<XslNode> content = new List<XslNode>();
            bool otherwise = false;
            bool when = false;

            QName parentName = _input.ElementName;
            if (_input.MoveToFirstChild())
            {
                do
                {
                    switch (_input.NodeType)
                    {
                        case XmlNodeType.Element:
                            XslNode? node = null;
                            if (Ref.Equal(_input.NamespaceUri, _atoms.UriXsl))
                            {
                                if (Ref.Equal(_input.LocalName, _atoms.When))
                                {
                                    if (otherwise)
                                    {
                                        ReportError(/*[XT_018]*/SR.Xslt_WhenAfterOtherwise);
                                        _input.SkipNode();
                                        continue;
                                    }
                                    else
                                    {
                                        when = true;
                                        node = XslIf();
                                    }
                                }
                                else if (Ref.Equal(_input.LocalName, _atoms.Otherwise))
                                {
                                    if (otherwise)
                                    {
                                        ReportError(/*[XT_019]*/SR.Xslt_DupOtherwise);
                                        _input.SkipNode();
                                        continue;
                                    }
                                    else
                                    {
                                        otherwise = true;
                                        node = XslOtherwise();
                                    }
                                }
                            }
                            if (node == null)
                            {
                                ReportError(/*[XT_020]*/SR.Xslt_UnexpectedElement, _input.QualifiedName, parentName);
                                _input.SkipNode();
                                continue;
                            }
                            AddInstruction(content, node);
                            break;
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            break;
                        default:
                            Debug.Assert(_input.NodeType == XmlNodeType.Text);
                            ReportError(/*[XT_020]*/SR.Xslt_TextNodesNotAllowed, parentName);
                            break;
                    }
                } while (_input.MoveToNextSibling());
            }
            CheckError(!when, /*[XT_021]*/SR.Xslt_NoWhen);
            return SetInfo(F.Choose(), content, ctxInfo);
        }

        private XslNode XslOtherwise()
        {
            ContextInfo ctxInfo = _input.GetAttributes();
            return SetInfo(F.Otherwise(), LoadInstructions(), ctxInfo);
        }

        private readonly XsltAttribute[] _forEachAttributes = {
            new XsltAttribute("select", V1Req | V2Req)
        };
        private XslNode XslForEach()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_forEachAttributes);

            string? select = ParseStringAttribute(0, "select");
            // The current template rule becomes null, so we must not allow xsl:apply-import's within this element
            _input.CanHaveApplyImports = false;
            List<XslNode> content = LoadInstructions(InstructionFlags.AllowSort);
            ctxInfo.SaveExtendedLineInfo(_input);

            return SetInfo(F.ForEach(select, ctxInfo, _input.XslVersion),
                content, ctxInfo
            );
        }

        // http://www.w3.org/TR/xslt#message
        // http://www.w3.org/TR/xslt20/#element-message
        private readonly XsltAttribute[] _messageAttributes = {
            new XsltAttribute("select",    V2Opt),
            new XsltAttribute("terminate", V1Opt | V2Opt),
            new XsltAttribute("error-code", V2Opt), // Xsl3: XSLT 3.0
        };
        private XslNode XslMessage()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_messageAttributes);

            string? select = ParseStringAttribute(0, "select");
            // Xsl3: in 2.0 terminate is an attribute value template.
            string? terminateAvt = V2 && _input.MoveToXsltAttribute(1, "terminate") && _input.Value.Contains('{') ? _input.Value : null;
            bool terminate = terminateAvt == null && ParseYesNoAttribute(1, /*attName:*/"terminate") == TriState.True;
            // Xsl3: the error a terminating message raises (XSLT 3.0 §23.1): err:XTMM9000 unless error-code names
            // another: a local name for one in the err namespace, else an EQName.
            string errorCode = "XTMM9000";
            string? errorCodeAvt = V2 && _input.MoveToXsltAttribute(2, "error-code") && _input.Value.Replace("{{", "").Replace("}}", "") is var computed && computed.Contains('{') ? _input.Value : null;
            // (An attribute value template with no expressions, only doubled braces, is a literal: Q{{ns}}local.)
            if (V2 && _input.MoveToXsltAttribute(2, "error-code") && _input.Value.Replace("{{", "").Replace("}}", "") is var unescaped && !unescaped.Contains('{') && !unescaped.Contains('}'))
            {
                string token = _input.Value.Replace("{{", "{").Replace("}}", "}").Trim();
                int colon = token.StartsWith("Q{", System.StringComparison.Ordinal) ? -1 : token.IndexOf(':');
                string? ns = token.StartsWith("Q{", System.StringComparison.Ordinal) && token.IndexOf('}') is int close and > 1
                    ? token[2..close] : colon > 0 ? _input.LookupXmlNamespace(token[..colon]) : "";
                if (token.StartsWith("Q{", System.StringComparison.Ordinal))
                {
                    token = token[(token.IndexOf('}') + 1)..]; // (the local name, below)
                    colon = -1;
                }
                if (ns == "http://www.w3.org/2005/xqt-errors")
                {
                    errorCode = token[(colon + 1)..];
                }
                else if (ns != null && token.Length > 0)
                {
                    errorCode = "Q{" + ns + "}" + token[(colon + 1)..];
                }
            }

            List<XslNode> content = LoadInstructions();
            if (content.Count != 0)
            {
                content = LoadEndTag(content);
            }
            if (select != null)
            {
                // Xsl3: with the message's line, which gives its static base URI.
                content.Insert(0, V2 ? SetLineInfo(F.CopyOf(select, _input.XslVersion), ctxInfo.lineInfo) : F.CopyOf(select, _input.XslVersion));
            }

            XslNode message = SetInfo(F.Message(terminate), content, ctxInfo);
            if (V2)
            {
                message.V2 = new XslNodeV2 { NameAvt = terminateAvt, ErrorCode = errorCode, ErrorCodeAvt = errorCodeAvt };
            }
            return message;
        }

        // http://www.w3.org/TR/xslt#number
        // http://www.w3.org/TR/xslt20/#element-number
        private readonly XsltAttribute[] _numberAttributes = {
            new XsltAttribute("value",              V1Opt | V2Opt),
            new XsltAttribute("select",             V2Opt),
            new XsltAttribute("level",              V1Opt | V2Opt),
            new XsltAttribute("count",              V1Opt | V2Opt),
            new XsltAttribute("from",               V1Opt | V2Opt),
            new XsltAttribute("format",             V1Opt | V2Opt),
            new XsltAttribute("lang",               V1Opt | V2Opt),
            new XsltAttribute("letter-value",       V1Opt | V2Opt),
            new XsltAttribute("ordinal",            V2Opt),
            new XsltAttribute("grouping-separator", V1Opt | V2Opt),
            new XsltAttribute("grouping-size",      V1Opt | V2Opt),
            new XsltAttribute("start-at",           V2Opt), // Xsl3: XSLT 3.0 §12.1
        };
        private XslNode XslNumber()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_numberAttributes);

            string? value = ParseStringAttribute(0, "value");
            string? select = ParseStringAttribute(1, "select");
            if (V1 && select != null) ReportNYI("xsl:number/@select");
            NumberLevel level = NumberLevel.Single;
            if (_input.MoveToXsltAttribute(2, "level"))
            {
                switch (_input.Value)
                {
                    case "single": level = NumberLevel.Single; break;
                    case "multiple": level = NumberLevel.Multiple; break;
                    case "any": level = NumberLevel.Any; break;
                    default:
                        if (!_input.ForwardCompatibility)
                        {
                            ReportError(/*[XT_022]*/SR.Xslt_InvalidAttrValue, "level", _input.Value);
                        }
                        break;
                }
            }
            string? count = ParseStringAttribute(3, "count");
            string? from = ParseStringAttribute(4, "from");
            if (V2 && value != null && (count != null || from != null || _input.AttributeExists(2, "level") || select != null))
            {
                ReportError(SR.Xml_UserException, "XTSE0975: xsl:number with value can't have select, level, count or from");
            }
            string? format = ParseStringAttribute(5, "format");
            string? lang = ParseStringAttribute(6, "lang");
            string? letterValue = ParseStringAttribute(7, "letter-value");
            string? ordinal = ParseStringAttribute(8, "ordinal");
            if (V1 && !string.IsNullOrEmpty(ordinal)) ReportNYI("xsl:number/@ordinal");
            string? groupingSeparator = ParseStringAttribute(9, "grouping-separator");
            string? groupingSize = ParseStringAttribute(10, "grouping-size");
            string? startAt = V2 ? ParseStringAttribute(11, "start-at") : null;

            // Default values for xsl:number :  level="single"  format="1"
            format ??= "1";

            CheckNoContent();
            XslNode number = SetInfo(
                F.Number(level, count, from, value,
                    format, lang, letterValue, groupingSeparator, groupingSize,
                    _input.XslVersion
                ),
                null, ctxInfo
            );
            if (V2)
            {
                number.V2 = new XslNodeV2 { Select = select, Ordinal = ordinal, StartAt = startAt };
            }
            return number;
        }

        // http://www.w3.org/TR/xslt#value-of
        private readonly XsltAttribute[] _valueOfAttributes = {
            new XsltAttribute("select",                  V1Req | V2Opt),
            new XsltAttribute("separator",               V2Opt),
            new XsltAttribute("disable-output-escaping", V1Opt | V2Opt)
        };
        private XslNode XslValueOf()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_valueOfAttributes);

            string? select = ParseStringAttribute(0, "select");
            string? separator = ParseStringAttribute(1, "separator");
            bool doe = ParseYesNoAttribute(2, /*attName:*/"disable-output-escaping") == TriState.True;

            List<XslNode>? content = null;

            if (V1)
            {
                if (select == null)
                {
                    _input.SkipNode();
                    return SetInfo(F.Error(XslLoadException.CreateMessage(ctxInfo.lineInfo, SR.Xslt_MissingAttribute, "select")), null, ctxInfo);
                }
                CheckNoContent();
            }
            else
            {
                content = LoadContent(select != null);
            }

            XslNode valueOf = SetInfo(F.XslNode(doe ? XslNodeType.ValueOfDoe : XslNodeType.ValueOf, null, select, _input.XslVersion),
                content, ctxInfo
            );
            if (V2)
            {
                valueOf.V2 = new XslNodeV2 { Separator = separator };
            }
            return valueOf;
        }

        //                    required tunnel select
        // variable              -        -      +
        // with-param            -        +      +
        // stylesheet/param      +        -      +
        // template/param        +        +      +
        // function/param        -        -      -
        // xsl:variable     http://www.w3.org/TR/xslt#local-variables
        // xsl:param        http://www.w3.org/TR/xslt#element-param
        // xsl:with-param   http://www.w3.org/TR/xslt#element-with-param
        private readonly XsltAttribute[] _variableAttributes = {
            new XsltAttribute("name",     V1Req | V2Req),
            new XsltAttribute("select",   V1Opt | V2Opt),
            new XsltAttribute("as",       V2Opt),
            new XsltAttribute("required", 0),
            new XsltAttribute("tunnel",   0),
            new XsltAttribute("static",   V2Opt),
            new XsltAttribute("visibility", V2Opt),
        };
        private readonly XsltAttribute[] _paramAttributes = {
            new XsltAttribute("name",     V1Req | V2Req),
            new XsltAttribute("select",   V1Opt | V2Opt),
            new XsltAttribute("as",       V2Opt),
            new XsltAttribute("required", V2Opt),
            new XsltAttribute("tunnel",   V2Opt),
            new XsltAttribute("static",   V2Opt),
        };
        private readonly XsltAttribute[] _withParamAttributes = {
            new XsltAttribute("name",    V1Req | V2Req),
            new XsltAttribute("select",  V1Opt | V2Opt),
            new XsltAttribute("as",      V2Opt),
            new XsltAttribute("required", 0),
            new XsltAttribute("tunnel",   V2Opt)
        };
        private VarPar XslVarPar()
        {
            string localName = _input.LocalName;
            XslNodeType nodeType = (
                Ref.Equal(localName, _atoms.Variable) ? XslNodeType.Variable :
                Ref.Equal(localName, _atoms.Param) ? XslNodeType.Param :
                Ref.Equal(localName, _atoms.WithParam) ? XslNodeType.WithParam :
                XslNodeType.Unknown
            );
            Debug.Assert(nodeType != XslNodeType.Unknown);
            ContextInfo ctxInfo = _input.GetAttributes(
                nodeType == XslNodeType.Variable ? _variableAttributes :
                nodeType == XslNodeType.Param ? _paramAttributes :
                /*default:*/                       _withParamAttributes
            );
            string? visibility = nodeType == XslNodeType.Variable && _input.MoveToXsltAttribute(6, "visibility") ? _input.Value.Trim() : null;

            QilName? name = ParseQNameAttribute(0);
            string? select = ParseStringAttribute(1, "select");
            string? asType = ParseStringAttribute(2, "as");
            TriState required = ParseYesNoAttribute(3, "required");
            if (V1 && required == TriState.True) ReportNYI("xsl:param/@required == true()");
            // Xsl3: a function's parameters are always required; required="no" is an error (XTSE0020).
            if (V2 && required == TriState.False && nodeType == XslNodeType.Param && _curTemplate is { IsFunction: true })
            {
                ReportError(SR.Xml_UserException, "XTSE0020: a function parameter can't have required=\"no\"");
            }

            if (V1 && asType != null)
            {
                ReportNYI("xsl:param/@as");
            }

            TriState tunnel = ParseYesNoAttribute(4, "tunnel");
            if (tunnel != TriState.Unknown)
            {
                // Xsl3: in 2.0+ a global parameter may say tunnel="no"; only tunnel="yes" is the error (XTSE0020).
                if (nodeType == XslNodeType.Param && _curTemplate == null && (V1 || tunnel == TriState.True))
                {
                    if (!_input.ForwardCompatibility)
                    {
                        ReportError(/*[???]*/SR.Xslt_NonTemplateTunnel, name!.ToString());
                    }
                }
                else
                {
                    if (V1 && tunnel == TriState.True) ReportNYI("xsl:param/@tunnel == true()");
                }
            }

            TriState isStatic = nodeType != XslNodeType.WithParam && V2 ? ParseYesNoAttribute(5, "static") : TriState.Unknown;
            List<XslNode> content = LoadContent(select != null);
            CheckError((required == TriState.True) && (select != null || content.Count != 0), /*[???]*/SR.Xslt_RequiredAndSelect, name!.ToString());

            VarPar result = F.VarPar(nodeType, name, select, _input.XslVersion);
            SetInfo(result, content, ctxInfo);
            if (V2)
            {
                result.V2 = new XslNodeV2 { As = asType, Required = required == TriState.True, Tunnel = tunnel == TriState.True };
                if (isStatic == TriState.True)
                {
                    if (_curTemplate != null)
                    {
                        ReportError(SR.Xml_UserException, "XTSE0020: only global variables and parameters can be static");
                    }
                    else
                    {
                        if (content.Count != 0)
                        {
                            // (With a select attribute too, the general rule's XTSE0620; use-when-0431.)
                            ReportError(SR.Xml_UserException, (select != null ? "XTSE0620" : "XTSE0010") + ": a static variable or parameter takes its value from select");
                        }
                        if (visibility != null)
                        {
                            // Xsl3: static variables aren't package components (XTSE0090).
                            ReportError(/*[XTSE0090]*/SR.Xslt_InvalidAttribute, "visibility", "xsl:" + localName);
                        }
                        result.V2.StaticValue = EvaluateStatic(result);
                    }
                }
            }
            result.Visibility = visibility;
            return result;
        }

        // http://www.w3.org/TR/xslt#section-Creating-Comments
        // http://www.w3.org/TR/xslt20/#element-comment
        private readonly XsltAttribute[] _commentAttributes = {
            new XsltAttribute("select", V2Opt)
        };
        private XslNode XslComment()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_commentAttributes);
            string? select = ParseStringAttribute(0, "select");
            if (V1 && select != null) ReportNYI("xsl:comment/@select");

            XslNode comment = SetInfo(F.Comment(), LoadContent(select != null), ctxInfo);
            if (V2)
            {
                comment.V2 = new XslNodeV2 { Select = select };
            }
            return comment;
        }

        private List<XslNode> LoadContent(bool hasSelect)
        {
            QName parentName = _input.ElementName;
            List<XslNode> content = LoadInstructions();
            CheckError(hasSelect && content.Count != 0, /*[XT0620]*/SR.Xslt_ElementCntSel, parentName);
            // Load the end tag only if the content is not empty
            if (content.Count != 0)
            {
                content = LoadEndTag(content);
            }
            return content;
        }

        // http://www.w3.org/TR/xslt#section-Creating-Processing-Instructions
        // http://www.w3.org/TR/xslt20/#element-processing-instruction
        private readonly XsltAttribute[] _processingInstructionAttributes = {
            new XsltAttribute("name",   V1Req | V2Req),
            new XsltAttribute("select", V2Opt)
        };
        private XslNode XslProcessingInstruction()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_processingInstructionAttributes);
            string name = ParseNCNameAttribute(0);
            string? select = ParseStringAttribute(1, "select");
            if (V1 && select != null) ReportNYI("xsl:processing-instruction/@select");

            XslNode pi = SetInfo(F.PI(name, _input.XslVersion), LoadContent(select != null), ctxInfo);
            if (V2)
            {
                pi.V2 = new XslNodeV2 { Select = select };
            }
            return pi;
        }

        // http://www.w3.org/TR/xslt#section-Creating-Text
        private readonly XsltAttribute[] _textAttributes = {
            new XsltAttribute("disable-output-escaping", V1Opt | V2Opt)
        };
        private XslNode XslText()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_textAttributes);

            bool doe = ParseYesNoAttribute(0, /*attName:*/ "disable-output-escaping") == TriState.True;
            SerializationHints hints = doe ? SerializationHints.DisableOutputEscaping : SerializationHints.None;

            // We are not using StringBuilder here because in most cases there will be just one text node.
            List<XslNode> content = new List<XslNode>();

            QName parentName = _input.ElementName;
            if (_input.MoveToFirstChild())
            {
                do
                {
                    switch (_input.NodeType)
                    {
                        case XmlNodeType.Text:
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            // xsl:text may contain multiple child text nodes separated by comments and PIs, which are ignored by XsltInput
                            content.Add(F.Text(_input.Value, hints));
                            break;
                        default:
                            Debug.Assert(_input.NodeType == XmlNodeType.Element);
                            ReportError(/*[XT_023]*/SR.Xslt_UnexpectedElement, _input.QualifiedName, parentName);
                            _input.SkipNode();
                            break;
                    }
                } while (_input.MoveToNextSibling());
            }

            if (V2 && content.Count == 0)
            {
                // Xsl3: a zero-length text node, which separates adjacent atomic values (XSLT 3.0 §5.7.1)
                // before zero-length text nodes are discarded.
                content.Add(F.Text("", hints));
            }
            // Empty xsl:text elements will be ignored
            return SetInfo(F.List(), content, ctxInfo);
        }

        // http://www.w3.org/TR/xslt#section-Creating-Elements-with-xsl:element
        // http://www.w3.org/TR/xslt20/#element-element
        private readonly XsltAttribute[] _elementAttributes = {
            new XsltAttribute("name",               V1Req | V2Req),
            new XsltAttribute("namespace",          V1Opt | V2Opt),
            new XsltAttribute("inherit-namespaces", V2Opt),
            new XsltAttribute("use-attribute-sets", V1Opt | V2Opt),
            new XsltAttribute("type",               V2Opt),
            new XsltAttribute("validation",         V2Opt)
        };
        private XslNode XslElement()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_elementAttributes);

            string name = ParseNCNameAttribute(0);
            string? ns = ParseStringAttribute(1, "namespace");
            CheckError(ns == XmlReservedNs.NsXmlNs, /*[XT_024]*/SR.Xslt_ReservedNS, ns);

            bool inheritNamespaces = ParseYesNoAttribute(2, "inherit-namespaces") != TriState.False;
            if (V1 && !inheritNamespaces) ReportNYI("xsl:copy[@inherit-namespaces = 'no']");

            XmlQualifiedName? type = ParseTypeAttribute(4);
            string? validation = ParseValidationAttribute(5, /*defVal:*/false);

            List<XslNode> content = new List<XslNode>();
            if (_input.MoveToXsltAttribute(3, "use-attribute-sets"))
            {
                AddUseAttributeSets(content);
            }
            XslNode element = SetInfo(F.Element(name, ns, _input.XslVersion),
                LoadEndTag(LoadInstructions(content)), ctxInfo
            );
            if (V2 && !inheritNamespaces)
            {
                element.V2 = new XslNodeV2 { InheritNamespaces = TriState.False };
            }
            return WithValidation(element, validation, type);
        }

        // http://www.w3.org/TR/xslt#creating-attributes
        // http://www.w3.org/TR/xslt20#creating-attributes
        private readonly XsltAttribute[] _attributeAttributes = {
            new XsltAttribute("name",       V1Req | V2Req),
            new XsltAttribute("namespace",  V1Opt | V2Opt),
            new XsltAttribute("select",     V2Opt),
            new XsltAttribute("separator",  V2Opt),
            new XsltAttribute("type",       V2Opt),
            new XsltAttribute("validation", V2Opt)
        };
        private XslNode XslAttribute()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_attributeAttributes);

            string name = ParseNCNameAttribute(0);
            string? ns = ParseStringAttribute(1, "namespace");
            CheckError(ns == XmlReservedNs.NsXmlNs, /*[XT_024]*/SR.Xslt_ReservedNS, ns);

            string? select = ParseStringAttribute(2, "select");
            if (V1 && select != null) ReportNYI("xsl:attribute/@select");
            string? separator = ParseStringAttribute(3, "separator");
            if (V1 && separator != null) ReportNYI("xsl:attribute/@separator");

            XmlQualifiedName? type = ParseTypeAttribute(4);
            string? validation = ParseValidationAttribute(5, /*defVal:*/false);

            XslNode attribute = SetInfo(F.Attribute(name, ns, _input.XslVersion), LoadContent(select != null), ctxInfo);
            if (V2)
            {
                attribute.V2 = new XslNodeV2 { Select = select, Separator = separator };
            }
            return WithValidation(attribute, validation, type);
        }

        // http://www.w3.org/TR/xslt#sorting
        // http://www.w3.org/TR/xslt20/#element-sort
        private readonly XsltAttribute[] _sortAttributes = {
            new XsltAttribute("select",     V1Opt | V2Opt),
            new XsltAttribute("lang",       V1Opt | V2Opt),
            new XsltAttribute("order",      V1Opt | V2Opt),
            new XsltAttribute("collation",  V1Opt | V2Opt),
            new XsltAttribute("stable",     V1Opt | V2Opt),
            new XsltAttribute("case-order", V1Opt | V2Opt),
            new XsltAttribute("data-type",  V1Opt | V2Opt)
        };
        private XslNode XslSort(int sortNumber)
        {
            ContextInfo ctxInfo = _input.GetAttributes(_sortAttributes);

            string? select = ParseStringAttribute(0, "select");
            string? lang = ParseStringAttribute(1, "lang");
            string? order = ParseStringAttribute(2, "order");
            string? collation = V2 ? ParseStringAttribute(3, "collation") : null;
            if (V1) ParseCollationAttribute(3);
            // Xsl3: stable is an attribute value template in 2.0; sorts are always stable, so its value doesn't matter.
            TriState stable = V2 && _input.MoveToXsltAttribute(4, "stable") && _input.Value.Contains('{') ? TriState.True : ParseYesNoAttribute(4, "stable");
            string? caseOrder = ParseStringAttribute(5, "case-order");
            string? dataType = ParseStringAttribute(6, "data-type");

            if (stable != TriState.Unknown)
            {
                CheckError(sortNumber != 0, SR.Xslt_SortStable);
            }

            List<XslNode>? content = null;
            if (V1)
            {
                CheckNoContent();
            }
            else
            {
                content = LoadContent(select != null);
            }

            if (V1 || content == null || content.Count == 0)
            {
                select ??= ".";
            }

            XslNode sort = SetInfo(F.Sort(select, lang, dataType, order, caseOrder, _input.XslVersion),
                V1 ? null : content, ctxInfo
            );
            if (V2)
            {
                sort.V2 = new XslNodeV2 { Collation = collation, Required = stable != TriState.False };
            }
            return sort;
        }



        // http://www.w3.org/TR/xslt#literal-result-element
        private XslNode LoadLiteralResultElement(bool asStylesheet)
        {
            Debug.Assert(_input.NodeType == XmlNodeType.Element);
            string prefix = _input.Prefix;
            string name = _input.LocalName;
            string nsUri = _input.NamespaceUri;

            ContextInfo ctxInfo = _input.GetLiteralAttributes(asStylesheet);
            if (V2 && name == "html" && nsUri == "http://www.w3.org/1999/xhtml" && _compiler.PrincipalVersion >= 2.0)
            {
                // Xsl3: the result may start with XHTML's html element, whose default output method is xhtml; but xml
                // when the principal module says version="1.0" (XSLT 3.0 §26.1, backwards-019).
                _compiler.XhtmlHtmlLiteral = true;
            }

            if (_input.IsExtensionNamespace(nsUri))
            {
                // This is not a literal result element, so drop all attributes we have collected
                return SetInfo(F.List(), LoadFallbacks(name), ctxInfo);
            }

            List<XslNode> content = new List<XslNode>();
            string? literalValidation = null;
            XmlQualifiedName? literalType = null;

            for (int i = 1; _input.MoveToLiteralAttribute(i); i++)
            {
                if (_input.IsXsltNamespace() && _input.IsKeyword(_atoms.UseAttributeSets))
                {
                    AddUseAttributeSets(content);
                }
            }

            for (int i = 1; _input.MoveToLiteralAttribute(i); i++)
            {
                if (!_input.IsXsltNamespace())
                {
                    XslNode att = F.LiteralAttribute(F.QName(_input.LocalName, _input.NamespaceUri, _input.Prefix), _input.Value, _input.XslVersion);
                    // QilGenerator takes care of AVTs, and needs line info
                    AddInstruction(content, SetLineInfo(att, ctxInfo.lineInfo));
                }
                else if (V2 && _compiler.Settings.SchemaAware && _input.LocalName == "type")
                {
                    literalType = ResolveTypeName(_input.Value); // Xsl3 (P6)
                }
                else if (V2 && _compiler.Settings.SchemaAware && _input.LocalName == "validation" && _input.Value.Trim() is "strict" or "lax" or "preserve" or "strip")
                {
                    literalValidation = _input.Value.Trim();
                }
                else if (V2 && (_input.LocalName == "type" || _input.LocalName == "validation" && _input.Value.Trim() is "strict" or "lax"))
                {
                    // A basic XSLT processor has no schema to validate against (XSLT 3.0 §2.10).
                    ReportError(SR.Xml_UserException, $"XTSE1660: xsl:{_input.LocalName}=\"{_input.Value}\" needs a schema-aware processor");
                }
                else if (V2 && !_input.ForwardCompatibility && !s_literalXsltAttributes.Contains(_input.LocalName))
                {
                    // Xsl3: XSLT 2.0 §11.1.2: no other attributes in the XSLT namespace (XTSE0805).
                    ReportError(SR.Xml_UserException, $"XTSE0805: xsl:{_input.LocalName} is not an attribute of a literal result element");
                }
                else
                {
                    // ignore all other xslt attributes. See XslInput.GetLiteralAttributes()
                }
            }

            // Xsl3: xsl:version below 2.0 asks for backwards-compatible behaviour (XSLT 3.0 §3.9): the scope manager marks
            // the region, and its nodes carry XslVersion.BackwardsCompatible.
            content = LoadEndTag(LoadInstructions(content));
            return WithValidation(SetInfo(F.LiteralElement(F.QName(name, nsUri, prefix)), content, ctxInfo), literalValidation, literalType);
        }

        // Xsl3: conflicting xsl:output values at the same import precedence are an error in 2.0 (XTSE1560).
        private void ReportOutputRedefinition(string attribute, string? value = null)
        {
            if (V2)
            {
                ReportError(SR.Xml_UserException, $"XTSE1560: xsl:output/@{attribute} has two values at the same import precedence");
            }
            else
            {
                ReportWarning(SR.Xslt_AttributeRedefinition, attribute);
            }
        }

        // Xsl3: the namespaces whose names stylesheets can't declare (XSLT 3.0 §3.5, XTSE0080).
        internal static bool IsReservedNamespace(string ns) => ns is XmlReservedNs.NsXslt or XmlReservedNs.NsXml
            or "http://www.w3.org/2005/xpath-functions" or "http://www.w3.org/2001/XMLSchema" or "http://www.w3.org/2001/XMLSchema-instance"
            or "http://www.w3.org/2005/xpath-functions/math" or "http://www.w3.org/2005/xpath-functions/map" or "http://www.w3.org/2005/xpath-functions/array";

        internal static bool IsDecimalLexical(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, @"^\s*[+-]?(\d+(\.\d*)?|\.\d+)\s*$");

        private void CheckReservedName(QilName? name, string what)
        {
            // xsl:initial-template (XSLT 3.0) is the one name in the XSLT namespace a template may have.
            if (V2 && name != null && IsReservedNamespace(name.NamespaceUri) && !(name.NamespaceUri == XmlReservedNs.NsXslt && name.LocalName == "initial-template"))
            {
                ReportError(SR.Xml_UserException, $"XTSE0080: the {what} {name.QualifiedName} is in a reserved namespace");
            }
        }

        // Xsl3: a static variable or parameter (XSLT 3.0 §9.6): its value is computed now, from a supplied static
        // parameter or from select, and serves use-when, shadow attributes and the stylesheet.
        // Xsl3: the import positions above the module being loaded (ImportPositions), from the principal module down.
        private readonly List<int> _importPath = new List<int>();

        // Xsl3: the static variables declared so far: where in the tree of modules, and their values.
        private readonly Dictionary<Xsl3.Xdm.QName, List<(int Package, int[] Path, IList<System.Xml.XPath.XPathItem> Value, bool IsParam)>> _staticDeclarations = new();

        /// <summary>
        /// XTSE3450 (XSLT 3.0 §9.6): a static variable must have the same value as one of the same name declared earlier
        /// in tree order with lower import precedence: one in a module that isn't the later one's own or an ancestor of it.
        /// A declaration's path is the positions of the imports above its module and its position in the module.
        /// </summary>
        private void CheckStaticConsistency(Xsl3.Xdm.QName name, VarPar var, IList<System.Xml.XPath.XPathItem> value)
        {
            int[] path = new int[_importPath.Count + 1];
            _importPath.CopyTo(path);
            path[^1] = _curStylesheet!.Declarations;
            if (!_staticDeclarations.TryGetValue(name, out var declarations))
            {
                _staticDeclarations[name] = declarations = new();
            }
            bool isParam = var.NodeType == XslNodeType.Param;
            foreach (var (package, otherPath, otherValue, otherIsParam) in declarations)
            {
                if (package != _packageId)
                {
                    continue;
                }
                bool otherFirst = TreeOrder(otherPath, path) < 0;
                int[] earlier = otherFirst ? otherPath : path, later = otherFirst ? path : otherPath;
                // The earlier one's module is the later one's or an ancestor of it: the same or higher precedence.
                bool ancestor = earlier.Length <= later.Length && earlier.AsSpan(0, earlier.Length - 1).SequenceEqual(later.AsSpan(0, earlier.Length - 1));
                // (A parameter and a variable of the same name are inconsistent whatever their values, static-023.)
                if (!ancestor && (otherIsParam != isParam || !Xsl3.XPath31.Functions.DeepEqual.Sequences(otherValue, value, new Xsl3.XPath31.Functions.FunctionContext(
                        Xsl3.XPath31.Functions.FunctionContext.LocalTimezoneMinutes, (a, b) => (int)a.ComparePosition(b)))))
                {
                    ReportError(SR.Xml_UserException, $"XTSE3450: the static variable ${var.Name!.QualifiedName} is declared with another value at a lower import precedence earlier in the stylesheet");
                    break;
                }
            }
            declarations.Add((_packageId, path, value, isParam));
        }

        private static int TreeOrder(int[] a, int[] b)
        {
            for (int i = 0; i < a.Length && i < b.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return a[i] < b[i] ? -1 : 1;
                }
            }
            return a.Length - b.Length;
        }

        private int? EvaluateStatic(VarPar var)
        {
            var name = new Xsl3.Xdm.QName(var.Name!.NamespaceUri, var.Name.LocalName);
            IList<System.Xml.XPath.XPathItem> value;
            var key = new XmlQualifiedName(var.Name.LocalName, var.Name.NamespaceUri);
            // The error when the value doesn't match the as type: XTDE0050 for a supplied value, XTDE0700 for a parameter's
            // implicit default (the empty sequence), else XTTE0590 (static-012, static-013).
            string conversionCode = "XTTE0590";
            if (var.NodeType == XslNodeType.Param && _compiler.StaticParameters != null && _compiler.StaticParameters.TryGetValue(key, out var supplied))
            {
                value = supplied;
                conversionCode = "XTDE0050";
            }
            else if (var.Select != null)
            {
                try
                {
                    value = UseWhen.EvaluateValue(var.Select, _input.StaticNamespaces(), _input.XPathDefaultNamespace, _input.StaticBaseUri, _compiler.StaticValues, _compiler.Settings.EnableEvaluate);
                }
                catch (Xsl3.XPath31.XPathStaticException e)
                {
                    ReportError(SR.Xml_UserException, $"{e.Code}: {e.Message}");
                    return null;
                }
                catch (Xsl3.Xdm.XdmException e)
                {
                    ReportError(SR.Xml_UserException, $"{e.Code}: {e.Message}");
                    return null;
                }
            }
            else if (var.V2!.Required)
            {
                ReportError(SR.Xml_UserException, $"XTDE0050: no value was supplied for the required static parameter ${var.Name.QualifiedName}");
                return null;
            }
            else
            {
                // Without select: a zero-length string, or the empty sequence when there is an as attribute (§9.3).
                value = var.V2!.As != null ? System.Array.Empty<System.Xml.XPath.XPathItem>() : Xsl3.Xdm.Sequences.One(Xsl3.Xdm.AtomicValue.OfString(""));
                conversionCode = var.NodeType == XslNodeType.Param ? "XTDE0700" : conversionCode;
            }
            if (var.V2!.As != null)
            {
                try
                {
                    var sc = new Xsl3.XPath31.StaticTypeResolver(p => _input.StaticNamespaces().TryGetValue(p, out string? uri) ? uri : null, _input.XPathDefaultNamespace);
                    value = Xsl3.XPath31.Functions.Coercion.CoerceTo(value, sc.Resolve(Xsl3.XPath31.Parser.ParseSequenceType(var.V2.As)), conversionCode, () => $"${var.Name.QualifiedName}");
                }
                catch (Xsl3.Xdm.XdmException e)
                {
                    ReportError(SR.Xml_UserException, $"{e.Code}: {e.Message}");
                    return null;
                }
            }
            int id = Xsl3.XPath31.ConstantPool.Add(value);
            CheckStaticConsistency(name, var, value);
            if (_compiler.StaticValues.TryGetValue(name, out int earlier) && !_input.ForwardCompatibility)
            {
                // The declaration loaded first stays (the importing module's are loaded before its imports).
                return earlier;
            }
            _compiler.StaticValues[name] = id;
            return id;
        }

        private static readonly HashSet<string> s_literalXsltAttributes = new(StringComparer.Ordinal)
        {
            "version", "extension-element-prefixes", "exclude-result-prefixes", "use-attribute-sets", "xpath-default-namespace",
            "default-collation", "use-when", "type", "validation", "inherit-namespaces", "expand-text", "default-mode",
            "default-validation",
        };

        private void CheckWithParam(List<XslNode> content, XslNode withParam)
        {
            Debug.Assert(content != null && withParam != null);
            Debug.Assert(withParam.NodeType == XslNodeType.WithParam);
            foreach (XslNode node in content)
            {
                if (node.NodeType == XslNodeType.WithParam && node.Name!.Equals(withParam.Name))
                {
                    ReportError(/*[XT0670]*/SR.Xslt_DuplicateWithParam, withParam.Name!.QualifiedName);
                    break;
                }
            }
        }

        private static void AddInstruction(List<XslNode> content, XslNode? instruction)
        {
            Debug.Assert(content != null);
            if (instruction != null)
            {
                content.Add(instruction);
            }
        }

        private List<XslNode> LoadEndTag(List<XslNode> content)
        {
            Debug.Assert(content != null);
            if (_compiler.IsDebug && !_input.IsEmptyElement)
            {
                AddInstruction(content, SetLineInfo(F.Nop(), _input.BuildLineInfo()));
            }
            return content;
        }

        private XslNode? LoadUnknownXsltInstruction(string parentName)
        {
            _input.GetVersionAttribute();
            if (!_input.ForwardCompatibility)
            {
                ReportError(/*[XT_026]*/SR.Xslt_UnexpectedElement, _input.QualifiedName, parentName);
                _input.SkipNode();
                return null;
            }
            else
            {
                ContextInfo ctxInfo = _input.GetAttributes();
                List<XslNode> fallbacks = LoadFallbacks(_input.LocalName);
                return SetInfo(F.List(), fallbacks, ctxInfo);
            }
        }

        private List<XslNode> LoadFallbacks(string instrName)
        {
            _input.MoveToElement();
            ISourceLineInfo extElmLineInfo = _input.BuildNameLineInfo();
            List<XslNode> fallbacksArray = new List<XslNode>();
            /* Process children */
            if (_input.MoveToFirstChild())
            {
                do
                {
                    if (_input.IsXsltKeyword(_atoms.Fallback))
                    {
                        ContextInfo ctxInfo = _input.GetAttributes();
                        fallbacksArray.Add(SetInfo(F.List(), LoadInstructions(), ctxInfo));
                    }
                    else
                    {
                        _input.SkipNode();
                    }
                } while (_input.MoveToNextSibling());
            }

            // Generate runtime error if there is no fallbacks
            if (fallbacksArray.Count == 0)
            {
                fallbacksArray.Add(
                    // Xsl3: in 2.0+, XTDE1450 when it is evaluated.
                    F.Error((V2 ? "XTDE1450: " : "") + XslLoadException.CreateMessage(extElmLineInfo, SR.Xslt_UnknownExtensionElement, instrName))
                );
            }
            return fallbacksArray;
        }

        // ------------------ little helper methods ---------------------

        // Xsl3: the mode of [xsl:]default-mode in scope (XSLT 3.0 §6.6.1); the unnamed mode without one.
        private QilName DefaultMode31()
        {
            string? mode = V2 ? _input.DefaultMode : null;
            if (mode == null || mode == "#unnamed")
            {
                return PackageMode(nullMode);
            }
            _compiler.EnterForwardsCompatible();
            QilName result = CreateXPathQName(mode);
            return PackageMode(_compiler.ExitForwardsCompatible(_input.ForwardCompatibility) ? result : nullMode);
        }

        // Suppresses errors if FCB is enabled
        private QilName ParseModeAttribute(int attNum)
        {
            //Debug.Assert(
            //    input.IsXsltKeyword(atoms.ApplyTemplates) ||
            //    input.IsXsltKeyword(atoms.Template) && V1
            //);
            if (!_input.MoveToXsltAttribute(attNum, "mode"))
            {
                return DefaultMode31();
            }
            // mode is always optional attribute
            _compiler.EnterForwardsCompatible();
            string qname = V1 ? _input.Value : _input.Value.Trim();
            QilName mode;
            if (!V1 && qname == "#default")
            {
                mode = DefaultMode31();
            }
            else if (V2 && qname == "#unnamed")
            {
                mode = nullMode; // Xsl3: XSLT 3.0
            }
            else if (!V1 && qname == "#current")
            {
                mode = CurrentMode;
            }
            else if (!V1 && qname == "#all")
            {
                ReportError(SR.Xslt_ModeListAll);
                mode = nullMode;
            }
            else
            {
                mode = CreateXPathQName(qname);
            }
            if (!_compiler.ExitForwardsCompatible(_input.ForwardCompatibility))
            {
                mode = nullMode;
            }
            return V2 ? PackageMode(mode) : mode;
        }

        // Parse mode when it is list. V1: -, V2: xsl:template
        // Suppresses errors if FCB is enabled
        private static bool IsLexicalQName(string name)
        {
            try
            {
                ValidateNames.ParseQNameThrow(name, out _, out _);
                return true;
            }
            catch (XmlException)
            {
                return false;
            }
        }

        private QilName ParseModeListAttribute(int attNum)
        {
            //Debug.Assert(input.IsXsltKeyword(atoms.Template) && !V1);
            if (!_input.MoveToXsltAttribute(attNum, "mode"))
            {
                return DefaultMode31();
            }

            string modeList = _input.Value;
            if (modeList == "#all")
            {
                // Xsl3: the template applies in every mode.
                _templateAllModes = true;
                return nullMode;
            }
            else
            {
                string[] list = XmlConvert.SplitString(modeList);
                List<QilName> modes = new List<QilName>(list.Length);

                _compiler.EnterForwardsCompatible();  // mode is always optional attribute

                if (list.Length == 0)
                {
                    ReportError(SR.Xslt_ModeListEmpty);
                }
                else
                {
                    foreach (string qname in list)
                    {
                        QilName mode;
                        if (qname == "#default")
                        {
                            mode = DefaultMode31();
                        }
                        else if (qname == "#unnamed")
                        {
                            mode = nullMode; // Xsl3: XSLT 3.0
                        }
                        else if (qname == "#current")
                        {
                            ReportNYI("xsl:apply-templates[@mode='#current']");
                            break;
                        }
                        else if (qname == "#all")
                        {
                            ReportError(SR.Xslt_ModeListAll);
                            break;
                        }
                        else if (V2 && !qname.StartsWith("Q{", System.StringComparison.Ordinal) && !IsLexicalQName(qname))
                        {
                            // Xsl3: a token that is neither a QName nor #default, #unnamed or #all (XTSE0550).
                            ReportError(SR.Xml_UserException, $"XTSE0550: '{qname}' in the mode attribute is not a mode name");
                            break;
                        }
                        else
                        {
                            mode = CreateXPathQName(qname);
                        }
                        bool dup = false;
                        foreach (QilName m in modes)
                        {
                            dup |= m.Equals(mode);
                        }
                        if (dup)
                        {
                            ReportError(SR.Xslt_ModeListDup, qname);
                        }
                        else
                        {
                            modes.Add(mode);
                        }
                    }
                }

                if (!_compiler.ExitForwardsCompatible(_input.ForwardCompatibility))
                {
                    modes.Clear();
                    modes.Add(nullMode);
                }
                for (int m = 0; m < modes.Count; m++)
                {
                    modes[m] = PackageMode(modes[m]);
                }
                if (1 < modes.Count)
                {
                    // Xsl3: the template is registered in each mode.
                    _templateModes = modes;
                    return modes[0];
                }
                if (modes.Count == 0)
                {
                    return PackageMode(nullMode);
                }
                return modes[0];
            }
        }

        private string? ParseCollationAttribute(int attNum)
        {
            if (_input.MoveToXsltAttribute(attNum, "collation"))
            {
                if (V2)
                {
                    return _input.Value;
                }
                ReportNYI("@collation");
            }
            return null;
        }

        // Does not suppress errors
        private bool ResolveQName(bool ignoreDefaultNs, string qname, out string localName, out string namespaceName, out string prefix)
        {
            if (qname == null)
            {
                // That means stylesheet is incorrect
                prefix = _compiler.PhantomNCName;
                localName = _compiler.PhantomNCName;
                namespaceName = _compiler.CreatePhantomNamespace();
                return false;
            }
            if (!V1)
            {
                // Xsl3: QName-valued attributes may have surrounding whitespace, and in 3.0 be EQNames (Q{uri}local).
                qname = qname.Trim();
                if (qname.StartsWith("Q{", StringComparison.Ordinal) && qname.IndexOf('}') is int close and > 1 &&
                    Xsl3.Xdm.Casting.IsNCName(qname[(close + 1)..]))
                {
                    prefix = string.Empty;
                    localName = qname[(close + 1)..];
                    namespaceName = qname[2..close].Trim();
                    return true;
                }
            }
            if (!_compiler.ParseQName(qname, out prefix, out localName, (IErrorHelper)this))
            {
                namespaceName = _compiler.CreatePhantomNamespace();
                return false;
            }
            if (ignoreDefaultNs && prefix.Length == 0)
            {
                namespaceName = string.Empty;
            }
            else
            {
                namespaceName = _input.LookupXmlNamespace(prefix)!;
                if (namespaceName == null)
                {
                    namespaceName = _compiler.CreatePhantomNamespace();
                    return false;
                }
            }
            return true;
        }

        // Does not suppress errors
        private QilName? ParseQNameAttribute(int attNum)
        {
            bool required = _input.IsRequiredAttribute(attNum);
            QilName? result = null;
            if (!required)
            {
                _compiler.EnterForwardsCompatible();
            }
            if (_input.MoveToXsltAttribute(attNum, "name"))
            {
                string prefix, localName, namespaceName;
                if (ResolveQName(/*ignoreDefaultNs:*/true, _input.Value, out localName, out namespaceName, out prefix))
                {
                    result = F.QName(localName, namespaceName, prefix);
                }
            }
            if (!required)
            {
                _compiler.ExitForwardsCompatible(_input.ForwardCompatibility);
            }
            if (result == null && required)
            {
                result = F.QName(_compiler.PhantomNCName, _compiler.CreatePhantomNamespace(), _compiler.PhantomNCName);
            }
            return result;
        }

        private string ParseNCNameAttribute(int attNum)
        {
            Debug.Assert(_input.IsRequiredAttribute(attNum), "It happened that @name as NCName is always required attribute");
            if (_input.MoveToXsltAttribute(attNum, "name"))
            {
                return _input.Value;
            }
            return _compiler.PhantomNCName;
        }

        // Does not suppress errors
        private QilName CreateXPathQName(string qname)
        {
            string prefix, localName, namespaceName;
            ResolveQName(/*ignoreDefaultNs:*/true, qname, out localName, out namespaceName, out prefix);
            return F.QName(localName, namespaceName, prefix);
        }

        // Does not suppress errors
        private XmlQualifiedName ResolveQName(bool ignoreDefaultNs, string qname)
        {
            string localName, namespaceName;
            ResolveQName(ignoreDefaultNs, qname, out localName, out namespaceName, out _);
            return new XmlQualifiedName(localName, namespaceName);
        }

        // Does not suppress errors
        private void ParseWhitespaceRules(string elements, bool preserveSpace)
        {
            if (!string.IsNullOrEmpty(elements))
            {
                string[] tokens = XmlConvert.SplitString(elements);
                for (int i = 0; i < tokens.Length; i++)
                {
                    string? prefix, localName, namespaceName;
                    if (V2 && TryParseNameTest20(tokens[i], out localName, out namespaceName))
                    {
                        AddWhitespaceRule((localName == null ? 1 : 0) + (namespaceName == null ? 1 : 0), new WhitespaceRule(localName, namespaceName, preserveSpace));
                        continue;
                    }
                    if (!_compiler.ParseNameTest(tokens[i], out prefix, out localName, (IErrorHelper)this))
                    {
                        namespaceName = _compiler.CreatePhantomNamespace();
                    }
                    else if (string.IsNullOrEmpty(prefix))
                    {
                        namespaceName = prefix;
                    }
                    else
                    {
                        namespaceName = _input.LookupXmlNamespace(prefix) ?? _compiler.CreatePhantomNamespace();
                    }
                    int index = (
                        (localName == null ? 1 : 0) +
                        (namespaceName == null ? 1 : 0)
                    );
                    AddWhitespaceRule(index, new WhitespaceRule(localName, namespaceName, preserveSpace));
                }
            }
        }

        private void AddWhitespaceRule(int index, WhitespaceRule rule)
        {
            // Xsl3: the same name test in xsl:strip-space and xsl:preserve-space at one import precedence (XTSE0270).
            if (V2 && _curStylesheet!.WhitespaceRules![index].Exists(r => r.LocalName == rule.LocalName && r.NamespaceName == rule.NamespaceName && r.PreserveSpace != rule.PreserveSpace))
            {
                ReportError(SR.Xml_UserException, $"XTSE0270: {rule.LocalName ?? "*"} is in both xsl:strip-space and xsl:preserve-space");
            }
            _curStylesheet!.AddWhitespaceRule(index, rule);
        }

        // Xsl3: the 2.0/3.0 name tests of xsl:strip-space and xsl:preserve-space that 1.0 lacks: *:name, Q{uri}name,
        // Q{uri}*, and unprefixed names in the xpath-default-namespace. False for the others (prefix:name, *, prefix:*).
        private bool TryParseNameTest20(string token, out string? localName, out string? namespaceName)
        {
            localName = namespaceName = null;
            if (token.StartsWith("*:", StringComparison.Ordinal))
            {
                localName = token[2..];
                return true;
            }
            if (token.StartsWith("Q{", StringComparison.Ordinal) && token.IndexOf('}') is int close and > 0)
            {
                namespaceName = token[2..close];
                string local = token[(close + 1)..];
                localName = local == "*" ? null : local;
                return true;
            }
            if (token != "*" && !token.Contains(':'))
            {
                localName = token;
                namespaceName = _input.XPathDefaultNamespace;
                return true;
            }
            return false;
        }

        // Does not suppress errors.  In case of error, null is returned.
        private XmlQualifiedName? ParseOutputMethod(string attValue, out XmlOutputMethod method)
        {
            string prefix, localName, namespaceName;
            ResolveQName(/*ignoreDefaultNs:*/true, attValue, out localName, out namespaceName, out prefix);
            method = XmlOutputMethod.AutoDetect;

            if (Compiler.IsPhantomNamespace(namespaceName))
            {
                return null;
            }
            else if (prefix.Length == 0)
            {
                switch (localName)
                {
                    case "xml": method = XmlOutputMethod.Xml; break;
                    case "html": method = XmlOutputMethod.Html; break;
                    case "text": method = XmlOutputMethod.Text; break;
                    // Xsl3: XHTML is XML syntax with the XHTML rules, which Runtime.OutputExtras applies.
                    case "xhtml" when V2: method = XmlOutputMethod.Xml; break;
                    // Xsl3: the raw result sequence, serialized by Runtime.ItemSerializer (XSLT 3.0 §26).
                    case "json" or "adaptive" when V2: method = XmlOutputMethod.Text; break;
                    default:
                        ReportError(/*[XT1570]*/SR.Xslt_InvalidAttrValue, nameof(method), attValue);
                        return null;
                }
            }
            else
            {
                if (!_input.ForwardCompatibility)
                {
                    ReportWarning(/*[XT1570]*/SR.Xslt_InvalidMethod, attValue);
                }
            }
            return new XmlQualifiedName(localName, namespaceName);
        }

        // Suppresses errors if FCB is enabled
        private void AddUseAttributeSets(List<XslNode> list)
        {
            Debug.Assert(_input.LocalName == "use-attribute-sets", "we are positioned on this attribute");
            Debug.Assert(list != null && list.Count == 0, "It happened that we always add use-attribute-sets first. Otherwise we can't call list.Clear()");

            _compiler.EnterForwardsCompatible();
            foreach (string qname in XmlConvert.SplitString(_input.Value))
            {
                AddInstruction(list, SetLineInfo(F.UseAttributeSet(CreateXPathQName(qname)), _input.BuildLineInfo()));
            }
            if (!_compiler.ExitForwardsCompatible(_input.ForwardCompatibility))
            {
                // There were errors in the list, ignore the whole list
                list.Clear();
            }
        }

        private List<QilName> ParseUseCharacterMaps(int attNum)
        {
            List<QilName> useCharacterMaps = new List<QilName>();
            if (_input.MoveToXsltAttribute(attNum, "use-character-maps"))
            {
                _compiler.EnterForwardsCompatible();
                foreach (string qname in XmlConvert.SplitString(_input.Value))
                {
                    // Xsl3: character maps are local to their package (use-package-106).
                    useCharacterMaps.Add(PackageComponents.Mangle(CreateXPathQName(qname), _packageId));
                }
                if (!_compiler.ExitForwardsCompatible(_input.ForwardCompatibility))
                {
                    useCharacterMaps.Clear(); // There were errors in the list, ignore the whole list
                }
            }
            return useCharacterMaps;
        }

        private string? ParseStringAttribute(int attNum, string attName)
        {
            if (_input.MoveToXsltAttribute(attNum, attName))
            {
                return _input.Value;
            }
            return null;
        }

        // Xsl3: a single character, which may be outside the BMP (2.0).
        private int ParseCodePointAttribute(int attNum, string attName, int defVal)
        {
            if (_input.MoveToXsltAttribute(attNum, attName))
            {
                string value = _input.Value;
                if (value.Length == 1 && !char.IsSurrogate(value[0]) || value.Length == 2 && char.IsSurrogatePair(value[0], value[1]))
                {
                    return char.ConvertToUtf32(value, 0);
                }
                if (_input.IsRequiredAttribute(attNum) || !_input.ForwardCompatibility)
                {
                    ReportError(/*[XT_029]*/SR.Xslt_CharAttribute, attName);
                }
            }
            return defVal;
        }

        private char ParseCharAttribute(int attNum, string attName, char defVal)
        {
            if (_input.MoveToXsltAttribute(attNum, attName))
            {
                if (_input.Value.Length == 1)
                {
                    return _input.Value[0];
                }
                else
                {
                    if (_input.IsRequiredAttribute(attNum) || !_input.ForwardCompatibility)
                    {
                        ReportError(/*[XT_029]*/SR.Xslt_CharAttribute, attName);
                    }
                }
            }
            return defVal;
        }

        // Suppresses errors if FCB is enabled
        private TriState ParseYesNoAttribute(int attNum, string attName)
        {
            Debug.Assert(!_input.IsRequiredAttribute(attNum), "All Yes/No attributes are optional.");
            if (_input.MoveToXsltAttribute(attNum, attName))
            {
                // Xsl3: XSLT 3.0 also accepts true/false and 1/0, with surrounding whitespace.
                switch (V2 ? _input.Value.Trim() : _input.Value)
                {
                    case "yes": return TriState.True;
                    case "no": return TriState.False;
                    case "true" or "1" when V2: return TriState.True;
                    case "false" or "0" when V2: return TriState.False;
                    default:
                        if (!_input.ForwardCompatibility)
                        {
                            if (V2 && _serializationAttributes)
                            {
                                // Xsl3: a serialization parameter of xsl:output (SEPM0016).
                                ReportError(SR.Xml_UserException, $"SEPM0016: the value of the '{attName}' attribute must be 'yes' or 'no'");
                            }
                            else
                            {
                                ReportError(/*[XT_028]*/SR.Xslt_BistateAttribute, attName, "yes", "no");
                            }
                        }
                        break;
                }
            }
            return TriState.Unknown;
        }

        private XmlQualifiedName? ParseTypeAttribute(int attNum)
        {
            Debug.Assert(!_input.IsRequiredAttribute(attNum), "All 'type' attributes are optional.");
            if (_input.MoveToXsltAttribute(attNum, "type"))
            {
                if (V2 && _compiler.Settings.SchemaAware)
                {
                    return ResolveTypeName(_input.Value);
                }
                if (V2)
                {
                    ReportError(SR.Xml_UserException, "XTSE1660: the type attribute needs a schema-aware processor");
                }
                else
                {
                    CheckError(true, /*[???]*/SR.Xslt_SchemaAttribute, "type");
                }
            }
            return null;
        }

        private string? ParseValidationAttribute(int attNum, bool defVal)
        {
            Debug.Assert(!_input.IsRequiredAttribute(attNum), "All 'validation' attributes are optional.");
            string attributeName = defVal ? _atoms.DefaultValidation : "validation";
            if (_input.MoveToXsltAttribute(attNum, attributeName))
            {
                string value = V2 ? _input.Value.Trim() : _input.Value;
                if (V2 && _compiler.Settings.SchemaAware && (value is "strip" or "preserve" || !defVal && value is "strict" or "lax"))
                {
                    return value; // Xsl3 (P6)
                }
                if (value == "strip" || V2 && value == "preserve")
                {
                    // no error: a basic XSLT 3.0 processor has no type annotations to preserve
                }
                else if (V2 && value is "strict" or "lax" && !defVal)
                {
                    ReportError(SR.Xml_UserException, $"XTSE1660: {attributeName}=\"{value}\" needs a schema-aware processor");
                }
                else if (
                  value == "preserve" ||
                  value == "strict" && !defVal ||
                  value == "lax" && !defVal
              )
                {
                    ReportError(/*[???]*/SR.Xslt_SchemaAttributeValue, attributeName, value);
                }
                else if (!_input.ForwardCompatibility)
                {
                    ReportError(/*[???]*/SR.Xslt_InvalidAttrValue, attributeName, value);
                }
            }
            return null;
        }

        // Xsl3 (P6): a type attribute's name; an unprefixed one is in the xpath-default-namespace (XSLT 3.0 §5.1.2).
        private XmlQualifiedName ResolveTypeName(string value)
        {
            XmlQualifiedName name = ResolveQName(/*ignoreDefaultNs:*/true, value);
            return name.Namespace.Length == 0 && !value.TrimStart().StartsWith("Q{", StringComparison.Ordinal) && _input.XPathDefaultNamespace.Length > 0
                ? new XmlQualifiedName(name.Name, _input.XPathDefaultNamespace)
                : name;
        }

        // Xsl3 (P6): the validation and type attributes of an instruction that constructs or copies nodes. Without
        // either, a default-validation in scope that differs from the stylesheet's is the instruction's validation.
        private T WithValidation<T>(T node, string? validation, XmlQualifiedName? type) where T : XslNode
        {
            if (validation == null && type == null && _compiler.Settings.SchemaAware && _input.DefaultValidation is string scoped &&
                scoped != (_compiler.DefaultValidation ?? "strip") && scoped is "preserve" or "strip")
            {
                validation = scoped;
            }
            if (validation != null && type != null)
            {
                ReportError(SR.Xml_UserException, "XTSE1505: an instruction can't have both a validation and a type attribute");
            }
            else if (type is { Name: "untyped", Namespace: XmlReservedNs.NsXs } || type is { Name: "untypedAtomic", Namespace: XmlReservedNs.NsXs } && node.NodeType == XslNodeType.Attribute)
            {
                // type="xs:untyped" (xs:untypedAtomic for attributes) is validation="strip" (XSLT 3.0 §25.4.1).
            }
            else if (validation != null || type != null)
            {
                node.V2 ??= new XslNodeV2();
                node.V2.Validation = validation;
                node.V2.ValidationType = type;
            }
            return node;
        }

        private void ParseInputTypeAnnotationsAttribute(int attNum)
        {
            Debug.Assert(!_input.IsRequiredAttribute(attNum), "All 'input-type-validation' attributes are optional.");
            if (_input.MoveToXsltAttribute(attNum, "input-type-annotations"))
            {
                string value = _input.Value;
                switch (value)
                {
                    case "unspecified":
                        break;
                    case "strip":
                    case "preserve":
                        if (_compiler.inputTypeAnnotations == null)
                        {
                            _compiler.inputTypeAnnotations = value;
                        }
                        else
                        {
                            CheckError(_compiler.inputTypeAnnotations != value, /*[XTSE0265]*/SR.Xslt_InputTypeAnnotations);
                        }
                        break;
                    default:
                        if (!_input.ForwardCompatibility)
                        {
                            ReportError(/*[???]*/SR.Xslt_InvalidAttrValue, "input-type-annotations", value);
                        }
                        break;
                }
            }
        }

        private void CheckNoContent()
        {
            _input.MoveToElement();
            QName parentName = _input.ElementName;
            ISourceLineInfo? errorLineInfo = SkipEmptyContent();

            if (errorLineInfo != null)
            {
                _compiler.ReportError(errorLineInfo, /*[XT0260]*/SR.Xslt_NotEmptyContents, parentName);
            }
        }

        // Returns ISourceLineInfo of the first violating (non-whitespace) node, or null otherwise
        private ISourceLineInfo? SkipEmptyContent()
        {
            ISourceLineInfo? result = null;

            // Really EMPTY means no content at all, but for the sake of compatibility with MSXML we allow whitespace
            if (_input.MoveToFirstChild())
            {
                do
                {
                    // NOTE: XmlNodeType.SignificantWhitespace is not allowed here
                    if (_input.NodeType != XmlNodeType.Whitespace)
                    {
                        result ??= _input.BuildNameLineInfo();
                        _input.SkipNode();
                    }
                } while (_input.MoveToNextSibling());
            }
            return result;
        }

        private static XslNode SetLineInfo(XslNode node, ISourceLineInfo? lineInfo)
        {
            Debug.Assert(node != null);
            node.SourceLine = lineInfo;
            return node;
        }

        private static void SetContent(XslNode node, List<XslNode>? content)
        {
            Debug.Assert(node != null);
            if (content != null && content.Count == 0)
            {
                content = null; // Actualy we can reuse this ArrayList.
            }
            node.SetContent(content);
        }

        internal static XslNode SetInfo(XslNode to, List<XslNode>? content, ContextInfo info)
        {
            Debug.Assert(to != null);
            if (info.backwards && to.XslVersion == XslVersion.Current)
            {
                to.XslVersion = XslVersion.BackwardsCompatible; // Xsl3: nodes made with XslVersion.Current
            }
            to.Namespaces = info.nsList;
            SetContent(to, content);
            SetLineInfo(to, info.lineInfo);
            return to;
        }

        // NOTE! We inverting namespace order that is irelevant for namespace of the same node, but
        // for included styleseets we don't keep stylesheet as a node and adding it's namespaces to
        // each toplevel element by MergeNamespaces().
        // Namespaces of stylesheet can be overridden in template and to make this works correclety we
        // should attache them after NsDec of top level elements.
        // Toplevel element almost never contais NsDecl and in practice node duplication will not happened, but if they have
        // we should copy NsDecls of stylesheet locally in toplevel elements.
        private static NsDecl? MergeNamespaces(NsDecl? thisList, NsDecl? parentList)
        {
            if (parentList == null)
            {
                return thisList;
            }
            if (thisList == null)
            {
                return parentList;
            }
            // Clone all nodes and attache them to nodes of thisList;
            while (parentList != null)
            {
                bool duplicate = false;
                for (NsDecl? tmp = thisList; tmp != null; tmp = tmp.Prev)
                {
                    if (Ref.Equal(tmp.Prefix, parentList.Prefix) && (
                        tmp.Prefix != null ||           // Namespace declaration
                        tmp.NsUri == parentList.NsUri   // Extension or excluded namespace
                    ))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                {
                    thisList = new NsDecl(thisList, parentList.Prefix, parentList.NsUri);
                }
                parentList = parentList.Prev;
            }
            return thisList;
        }

        // -------------------------------- IErrorHelper --------------------------------

        public void ReportError(string res, params string?[]? args)
        {
            _compiler.ReportError(_input.BuildNameLineInfo(), res, args);
        }

        public void ReportWarning(string res, params string?[]? args)
        {
            _compiler.ReportWarning(_input.BuildNameLineInfo(), res, args);
        }

        private void ReportNYI(string? arg)
        {
            if (!_input.ForwardCompatibility)
            {
                ReportError(SR.Xslt_NotYetImplemented, arg);
            }
        }

        public void CheckError(bool cond, string res, params string?[]? args)
        {
            if (cond)
            {
                _compiler.ReportError(_input.BuildNameLineInfo(), res, args);
            }
        }
    }
}
