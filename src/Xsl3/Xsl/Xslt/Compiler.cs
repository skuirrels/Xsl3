// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Xml.XPath;
using Xsl3.Qil;

namespace Xsl3.Xslt
{
    internal enum XslVersion
    {
        Version10 = 0,
        ForwardsCompatible = 1,
        Current = Version10,
    }

    // RootLevel is underdeveloped consept currently. I plane to move here more collections from Compiler.
    // Compiler is like a stylesheet in some sense. it has a lot of properties of stylesheet. Instead of
    // inhereting from Styleseet (or StylesheetLevel) I desided to aggregate special subclass of StylesheetLevel.
    // One more reason to for this design is to normolize apply-templates and apply-imports to one concept:
    // apply-templates is apply-imports(compiler.Root).
    // For now I don't create new files for these new classes to simplify integrations WebData <-> WebData_xsl
    internal sealed class RootLevel : StylesheetLevel
    {
        public RootLevel(Stylesheet principal)
        {
            base.Imports = new Stylesheet[] { principal };
        }
    }

    internal sealed class Compiler
    {
        public XsltSettings Settings;
        public bool IsDebug;
        public string? ScriptAssemblyPath;
        public int Version;                // 0 - Auto; 1 - XSLT 1.0; 2 - XSLT 2.0
        public string? inputTypeAnnotations;   // null - "unspecified"; "preserve"; "strip"

        public CompilerErrorCollection CompilerErrorColl;        // Results of the compilation
        public int CurrentPrecedence;  // Decreases by 1 with each import
        public XslNode? StartApplyTemplates;

        // Xsl3: the entry point (XSLT 3.0 §2.3): a named template, else template rules in a mode.
        public XmlQualifiedName? InitialTemplate;
        public XmlQualifiedName? InitialFunction; // Xsl3: the initial function (XSLT 3.0 §2.3.4), with its arity
        public int InitialFunctionArity;
        public bool RawResult; // Xsl3: deliver the principal result as the raw sequence (XSLT 3.0 §2.3.6), not a tree
        public XmlQualifiedName? InitialMode;

        // Xsl3: static parameters supplied by the API, and the values of the static variables and parameters
        // (by expanded name), as ConstantPool ids of their sequences.
        public Dictionary<XmlQualifiedName, IList<System.Xml.XPath.XPathItem>>? StaticParameters;
        public IReadOnlyList<(string Name, string Version, Uri Location)>? Packages; // Xsl3: the package library
        public Dictionary<Xsl3.Xdm.QName, int> StaticValues = new Dictionary<Xsl3.Xdm.QName, int>();
        public RootLevel? Root;
        [Obsolete(Obsoletions.XsltSettingsEnableScriptMessage, DiagnosticId = Obsoletions.XsltSettingsEnableScriptDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public Scripts Scripts;
        public Output Output = new Output();
        public List<VarPar> ExternalPars = new List<VarPar>();
        public List<VarPar> GlobalVars = new List<VarPar>();
        public List<WhitespaceRule> WhitespaceRules = new List<WhitespaceRule>();
        public DecimalFormats DecimalFormats = new DecimalFormats();
        public Keys Keys = new Keys();
        public List<ProtoTemplate> AllTemplates = new List<ProtoTemplate>();

        public Dictionary<QilName, VarPar> AllGlobalVarPars = new Dictionary<QilName, VarPar>();

        // Xsl3: stylesheet functions by name and arity (the highest import precedence wins), and character maps.
        public Dictionary<(QilName Name, int Arity), (Template Function, int Precedence)> Functions = new Dictionary<(QilName, int), (Template, int)>();
        public Dictionary<QilName, CharacterMap> CharacterMaps = new Dictionary<QilName, CharacterMap>();
        public Dictionary<QilName, Output> NamedOutputs = new Dictionary<QilName, Output>();
        public bool XhtmlHtmlLiteral; // Xsl3: a 2.0+ literal result element is XHTML's html element
        public Dictionary<QilName, ModeDecl> ModeDecls = new Dictionary<QilName, ModeDecl>();
        public QilName? DefaultInitialMode; // Xsl3: the principal stylesheet module's default-mode
        public (string Use, string? As)? GlobalContextItem; // Xsl3: the principal package's xsl:global-context-item
        // Xsl3: packages (Packages.cs): their tables, overridden components' redirects, overrides' originals.
        public List<PackageInfo> PackageInfos = new();
        public Dictionary<string, QilName> PackageRedirects = new(System.StringComparer.Ordinal);
        public Dictionary<XslNode, QilName> PackageOriginals = new();
        public Dictionary<QilName, (XslNode Declaration, int Precedence)> Accumulators = new(); // Xsl3: xsl:accumulator

        /// <summary>Xsl3: false if a function with this name and arity already has the same import precedence.</summary>
        public bool AddFunction(QilName name, int arity, Template function, int precedence)
        {
            if (Functions.TryGetValue((name, arity), out var existing))
            {
                if (existing.Precedence == precedence)
                {
                    return false;
                }
                if (existing.Precedence > precedence)
                {
                    return true;
                }
            }
            Functions[(name, arity)] = (function, precedence);
            return true;
        }

        /// <summary>
        /// Xsl3: whether stylesheets with version 2.0 or later compile as XSLT 2.0 (the 3.0 processor) rather than as
        /// XSLT 1.0 in forwards-compatible mode. On by default; XSL3_XSLT2=0 turns it off (for comparisons).
        /// </summary>
        public static bool Xslt2Enabled = System.Environment.GetEnvironmentVariable("XSL3_XSLT2") != "0";
        public Dictionary<QilName, Template> NamedTemplates = new Dictionary<QilName, Template>();
        public Dictionary<QilName, AttributeSet> AttributeSets = new Dictionary<QilName, AttributeSet>();
        public Dictionary<string, NsAlias> NsAliases = new Dictionary<string, NsAlias>();

        private readonly Dictionary<string, int> _moduleOrder = new Dictionary<string, int>();

        public Compiler(XsltSettings settings, bool debug, string? scriptAssemblyPath)
        {
            Debug.Assert(CompilerErrorColl == null, "Compiler cannot be reused");

            Settings = settings;
            IsDebug = settings.IncludeDebugInformation | debug;
            ScriptAssemblyPath = scriptAssemblyPath;

            CompilerErrorColl = new CompilerErrorCollection();
#pragma warning disable SYSLIB0062 // Scripts field is obsolete
            Scripts = new Scripts(this);
#pragma warning restore SYSLIB0062
        }

        public CompilerErrorCollection Compile(object stylesheet, XmlResolver? xmlResolver, XmlResolver? origResolver, out QilExpression qil)
        {
            Debug.Assert(stylesheet != null);
            Debug.Assert(Root == null, "Compiler cannot be reused");

            new XsltLoader().Load(this, stylesheet, xmlResolver, origResolver);
            PackageComponents.Build(this); // Xsl3: the packages' component tables (Packages.cs)
            if (Version == 2)
            {
                // Xsl3: the merged declarations of each decimal format must give distinct characters (XTSE1300).
                foreach (DecimalFormatDecl format in DecimalFormats)
                {
                    var signs = new[] { format.Characters[0], format.Characters[1], format.Characters[2], format.Characters[3], format.Characters[5], format.Characters[6], format.ExponentSeparator };
                    if (System.Linq.Enumerable.Count(System.Linq.Enumerable.Distinct(signs)) != signs.Length || format.Characters[4] is var zero && System.Linq.Enumerable.Any(signs, c => c >= zero && c <= zero + 9))
                    {
                        ReportError(format.Line ?? SourceLineInfo.NoSource, SR.Xml_UserException, $"XTSE1300: the characters of the decimal format {format.Name} are not distinct");
                    }
                }
            }
            qil = QilGenerator.CompileStylesheet(this);
            SortErrors();
            return CompilerErrorColl;
        }

        public Stylesheet CreateStylesheet()
        {
            Stylesheet sheet = new Stylesheet(this, CurrentPrecedence);
            if (CurrentPrecedence-- == 0)
            {
                Root = new RootLevel(sheet);
            }
            return sheet;
        }

        public void AddModule(string baseUri)
        {
            _moduleOrder.TryAdd(baseUri, _moduleOrder.Count);
        }

        /// <param name="package">Xsl3: the package whose aliases apply (namespace aliases are local to a package).</param>
        public void ApplyNsAliases(ref string? prefix, ref string nsUri, int package = 0)
        {
            NsAlias? alias;
            if (NsAliases.TryGetValue(PackageComponents.MangleNamespace(package, nsUri), out alias))
            {
                nsUri = alias.ResultNsUri;
                prefix = alias.ResultPrefix;
            }
        }

        // Returns true in case of redefinition
        public bool SetNsAlias(string ssheetNsUri, string resultNsUri, string? resultPrefix, int importPrecedence)
        {
            NsAlias? oldNsAlias;
            if (NsAliases.TryGetValue(ssheetNsUri, out oldNsAlias))
            {
                // Namespace alias for this stylesheet namespace URI has already been defined
                Debug.Assert(importPrecedence <= oldNsAlias.ImportPrecedence, "Stylesheets must be processed in the order of decreasing import precedence");
                if (importPrecedence < oldNsAlias.ImportPrecedence || resultNsUri == oldNsAlias.ResultNsUri)
                {
                    // Either the identical definition or lower precedence - ignore it
                    return false;
                }
                // Recover by choosing the declaration that occurs later in the stylesheet
            }
            NsAliases[ssheetNsUri] = new NsAlias(resultNsUri, resultPrefix, importPrecedence);
            return oldNsAlias != null;
        }

        private void MergeWhitespaceRules(Stylesheet sheet)
        {
            for (int idx = 0; idx <= 2; idx++)
            {
                sheet.WhitespaceRules![idx].Reverse();
                this.WhitespaceRules.AddRange(sheet.WhitespaceRules[idx]);
            }
            sheet.WhitespaceRules = null;
        }

        private void MergeAttributeSets(Stylesheet sheet)
        {
            foreach (QilName attSetName in sheet.AttributeSets!.Keys)
            {
                AttributeSet? attSet;
                if (!this.AttributeSets.TryGetValue(attSetName, out attSet))
                {
                    this.AttributeSets[attSetName] = sheet.AttributeSets[attSetName];
                }
                else
                {
                    // Lower import precedence - insert before all previous definitions
                    attSet.MergeContent(sheet.AttributeSets[attSetName]);
                }
            }
            sheet.AttributeSets = null;
        }

        private void MergeGlobalVarPars(Stylesheet sheet)
        {
            foreach (VarPar var in sheet.GlobalVarPars!)
            {
                Debug.Assert(var.NodeType == XslNodeType.Variable || var.NodeType == XslNodeType.Param);
                if (!AllGlobalVarPars.ContainsKey(var.Name!))
                {
                    if (var.NodeType == XslNodeType.Variable)
                    {
                        GlobalVars.Add(var);
                    }
                    else
                    {
                        ExternalPars.Add(var);
                    }
                    AllGlobalVarPars[var.Name!] = var;
                }
            }
            sheet.GlobalVarPars = null;
        }

        public void MergeWithStylesheet(Stylesheet sheet)
        {
            MergeWhitespaceRules(sheet);
            MergeAttributeSets(sheet);
            MergeGlobalVarPars(sheet);
        }

        public static string ConstructQName(string prefix, string localName)
        {
            if (prefix.Length == 0)
            {
                return localName;
            }
            else
            {
                return $"{prefix}:{localName}";
            }
        }

        public bool ParseQName(string qname, out string prefix, out string localName, IErrorHelper errorHelper)
        {
            Debug.Assert(qname != null);
            try
            {
                ValidateNames.ParseQNameThrow(qname, out prefix, out localName);
                return true;
            }
            catch (XmlException e)
            {
                errorHelper.ReportError(/*[XT_042]*/e.Message, null);
                prefix = PhantomNCName;
                localName = PhantomNCName;
                return false;
            }
        }

        public bool ParseNameTest(string nameTest, out string? prefix, out string? localName, IErrorHelper errorHelper)
        {
            Debug.Assert(nameTest != null);
            try
            {
                ValidateNames.ParseNameTestThrow(nameTest, out prefix, out localName);
                return true;
            }
            catch (XmlException e)
            {
                errorHelper.ReportError(/*[XT_043]*/e.Message, null);
                prefix = PhantomNCName;
                localName = PhantomNCName;
                return false;
            }
        }

        public static void ValidatePiName(string name, IErrorHelper errorHelper)
        {
            Debug.Assert(name != null);
            try
            {
                ValidateNames.ValidateNameThrow(
                    /*prefix:*/string.Empty, /*localName:*/name, /*ns:*/string.Empty,
                    XPathNodeType.ProcessingInstruction, ValidateNames.Flags.AllExceptPrefixMapping
                );
            }
            catch (XmlException e)
            {
                errorHelper.ReportError(/*[XT_044]*/e.Message, null);
            }
        }

        public readonly string PhantomNCName = "error";
        private int _phantomNsCounter;

        public string CreatePhantomNamespace()
        {
            // Prepend invalid XmlChar to ensure this name would not clash with any namespace name in the stylesheet
            return $"\0namespace{_phantomNsCounter++}";
        }

        public static bool IsPhantomNamespace(string namespaceName)
        {
            return namespaceName.Length > 0 && namespaceName[0] == '\0';
        }

        public static bool IsPhantomName(QilName qname)
        {
            string nsUri = qname.NamespaceUri;
            return nsUri.Length > 0 && nsUri[0] == '\0';
        }

        // -------------------------------- Error Handling --------------------------------

        private int ErrorCount
        {
            get
            {
                return CompilerErrorColl.Count;
            }
            set
            {
                Debug.Assert(value <= ErrorCount);
                for (int idx = ErrorCount - 1; idx >= value; idx--)
                {
                    CompilerErrorColl.RemoveAt(idx);
                }
            }
        }

        private int _savedErrorCount = -1;

        public void EnterForwardsCompatible()
        {
            Debug.Assert(_savedErrorCount == -1, "Nested EnterForwardsCompatible calls");
            _savedErrorCount = ErrorCount;
        }

        // Returns true if no errors were suppressed
        public bool ExitForwardsCompatible(bool fwdCompat)
        {
            Debug.Assert(_savedErrorCount != -1, "ExitForwardsCompatible without EnterForwardsCompatible");
            if (fwdCompat && ErrorCount > _savedErrorCount)
            {
                ErrorCount = _savedErrorCount;
                Debug.Assert((_savedErrorCount = -1) < 0);
                return false;
            }
            Debug.Assert((_savedErrorCount = -1) < 0);
            return true;
        }

        public CompilerError CreateError(ISourceLineInfo lineInfo, string res, params string?[]? args)
        {
            AddModule(lineInfo.Uri!);
            return new CompilerError(
                lineInfo.Uri!, lineInfo.Start.Line, lineInfo.Start.Pos, /*errorNumber:*/string.Empty,
                /*errorText:*/XslTransformException.CreateMessage(res, args)
            );
        }

        public void ReportError(ISourceLineInfo lineInfo, string res, params string?[]? args)
        {
            CompilerError error = CreateError(lineInfo, res, args);
            CompilerErrorColl.Add(error);
        }

        public void ReportWarning(ISourceLineInfo lineInfo, string res, params string?[]? args)
        {
            int warningLevel = 1;
            if (0 <= Settings.WarningLevel && Settings.WarningLevel < warningLevel)
            {
                // Ignore warning
                return;
            }
            CompilerError error = CreateError(lineInfo, res, args);
            if (Settings.TreatWarningsAsErrors)
            {
                error.ErrorText = XslTransformException.CreateMessage(SR.Xslt_WarningAsError, error.ErrorText);
                CompilerErrorColl.Add(error);
            }
            else
            {
                error.IsWarning = true;
                CompilerErrorColl.Add(error);
            }
        }

        private void SortErrors()
        {
            CompilerErrorCollection errorColl = this.CompilerErrorColl;
            if (errorColl.Count > 1)
            {
                CompilerError[] errors = new CompilerError[errorColl.Count];
                errorColl.CopyTo(errors, 0);
                Array.Sort<CompilerError>(errors, new CompilerErrorComparer(_moduleOrder));
                errorColl.Clear();
                errorColl.AddRange(errors);
            }
        }

        private sealed class CompilerErrorComparer : IComparer<CompilerError>
        {
            private readonly Dictionary<string, int> _moduleOrder;

            public CompilerErrorComparer(Dictionary<string, int> moduleOrder)
            {
                _moduleOrder = moduleOrder;
            }

            public int Compare(CompilerError? x, CompilerError? y)
            {
                if ((object?)x == (object?)y)
                    return 0;

                if (x == null)
                    return -1;

                if (y == null)
                    return 1;

                int result = _moduleOrder[x.FileName].CompareTo(_moduleOrder[y.FileName]);
                if (result != 0)
                    return result;

                result = x.Line.CompareTo(y.Line);
                if (result != 0)
                    return result;

                result = x.Column.CompareTo(y.Column);
                if (result != 0)
                    return result;

                result = x.IsWarning.CompareTo(y.IsWarning);
                if (result != 0)
                    return result;

                result = string.CompareOrdinal(x.ErrorNumber, y.ErrorNumber);
                if (result != 0)
                    return result;

                return string.CompareOrdinal(x.ErrorText, y.ErrorText);
            }
        }
    }

    /// <summary>Xsl3: an xsl:mode declaration (XSLT 3.0 §3.5.4.1).</summary>
    internal sealed class ModeDecl
    {
        public string OnNoMatch = "text-only-copy";
        public string OnMultipleMatch = "use-last";
        public Dictionary<string, string> Attributes = new(); // the attributes given, for XTSE0545
        public int ImportPrecedence;
        public ModeDeclNode? Declaration; // Xsl3: for packages: the declaration's line, package and visibility
    }

    internal sealed class Output
    {
        public XmlWriterSettings Settings;
        public string? Version;
        public string? Encoding;
        public XmlQualifiedName? Method;

        // All the xsl:output elements occurring in a stylesheet are merged into a single effective xsl:output element.
        // We store the import precedence of each attribute value to catch redefinitions with the same import precedence.
        public const int NeverDeclaredPrec = int.MinValue;
        public int MethodPrec = NeverDeclaredPrec;
        public int VersionPrec = NeverDeclaredPrec;
        public int EncodingPrec = NeverDeclaredPrec;
        public int OmitXmlDeclarationPrec = NeverDeclaredPrec;
        public int StandalonePrec = NeverDeclaredPrec;
        public int DocTypePublicPrec = NeverDeclaredPrec;
        public int DocTypeSystemPrec = NeverDeclaredPrec;
        public int IndentPrec = NeverDeclaredPrec;
        public int MediaTypePrec = NeverDeclaredPrec;

        // Xsl3: XSLT 2.0 serialization parameters (applied through Runtime.OutputExtras).
        public bool Xhtml;
        public TriState ByteOrderMark = TriState.Unknown;
        public bool? IncludeContentType;
        public string? NormalizationForm;
        public List<QilName>? UseCharacterMaps;
        public List<int>? UseCharacterMapsPrec; // Xsl3: the import precedence of each of UseCharacterMaps
        public Dictionary<string, int> ParameterPrec = new(); // Xsl3: the import precedence each 2.0+ parameter was set at
        public Runtime.ParameterDocument.Parameters? ParameterDocument; // Xsl3: parameter-document, less the declaration's own attributes
        public string? HtmlVersion; // Xsl3: xsl:output/@html-version
        public bool EscapeUriAttributes = true; // Xsl3
        public string? ItemSeparator, JsonNodeOutputMethod; // Xsl3
        public bool AllowDuplicateNames; // Xsl3

        public Output()
        {
            Settings = new XmlWriterSettings();
            Settings.SetOutputMethod(XmlOutputMethod.AutoDetect);
            Settings.AutoXmlDeclaration = true;
            Settings.ConformanceLevel = ConformanceLevel.Auto;
            Settings.MergeCDataSections = true;
        }
    }

    internal sealed class DecimalFormats : KeyedCollection<XmlQualifiedName, DecimalFormatDecl>
    {
        protected override XmlQualifiedName GetKeyForItem(DecimalFormatDecl format)
        {
            return format.Name;
        }
    }

    internal sealed class DecimalFormatDecl
    {
        public readonly XmlQualifiedName Name;
        public string InfinitySymbol; // Xsl3: not readonly, as 2.0 declarations of one format merge
        public string NanSymbol;
        public readonly char[] Characters;
        public int ImportPrecedence; // Xsl3: in 2.0 a definition of higher import precedence wins
        public char ExponentSeparator = 'e'; // Xsl3: XSLT 3.0
        public readonly HashSet<int> Specified = new(); // Xsl3: the attributes (by number) declarations gave
        public ISourceLineInfo? Line; // Xsl3: the first declaration

        public static readonly DecimalFormatDecl Default = new DecimalFormatDecl(new XmlQualifiedName(), "Infinity", "NaN", ".,%\u20300#;-");

        public DecimalFormatDecl(XmlQualifiedName name, string infinitySymbol, string nanSymbol, string characters)
        {
            Debug.Assert(characters.Length == 8);
            this.Name = name;
            this.InfinitySymbol = infinitySymbol;
            this.NanSymbol = nanSymbol;
            this.Characters = characters.ToCharArray();
        }
    }

    internal sealed class NsAlias
    {
        public readonly string ResultNsUri;
        public readonly string? ResultPrefix;
        public readonly int ImportPrecedence;

        public NsAlias(string resultNsUri, string? resultPrefix, int importPrecedence)
        {
            this.ResultNsUri = resultNsUri;
            this.ResultPrefix = resultPrefix;
            this.ImportPrecedence = importPrecedence;
        }
    }
}
