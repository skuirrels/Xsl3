// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// <spec>http://webdata/xml/specs/XslCompiledTransform.xml</spec>
//------------------------------------------------------------------------------

using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml.XPath;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Xslt;

namespace Xsl3
{
    //----------------------------------------------------------------------------------------------------
    //  Clarification on null values in this API:
    //      stylesheet, stylesheetUri   - cannot be null
    //      settings                    - if null, XsltSettings.Default will be used
    //      stylesheetResolver          - if null, XmlNullResolver will be used for includes/imports.
    //                                    However, if the principal stylesheet is given by its URI, that
    //                                    URI will be resolved using XmlUrlResolver (for compatibility
    //                                    with XslTransform and XmlReader).
    //      typeBuilder                 - cannot be null
    //      scriptAssemblyPath          - can be null only if scripts are disabled
    //      compiledStylesheet          - cannot be null
    //      executeMethod, queryData    - cannot be null
    //      earlyBoundTypes             - null means no script types
    //      documentResolver            - if null, XmlNullResolver will be used
    //      input, inputUri             - cannot be null
    //      arguments                   - null means no arguments
    //      results, resultsFile        - cannot be null
    //----------------------------------------------------------------------------------------------------

    // Xsl3 (Sink B, B1): only compiling from source needs dynamic code; a precompiled stylesheet (Load(Type),
    // Load(MethodInfo, ...)) and the transforms themselves don't.
    public sealed class XslCompiledTransform
    {
        internal const string CompilesIL = "Loading a stylesheet from source generates IL at run time. Under NativeAOT, load a stylesheet precompiled with Xsl3.Build (Load(Type)).";

        // Version for GeneratedCodeAttribute
        private static readonly Version? s_version = typeof(XslCompiledTransform).Assembly.GetName().Version;

        // Options of compilation
        private readonly bool _enableDebug;

        // Results of compilation
        private CompilerErrorCollection? _compilerErrorColl;
        private QilExpression? _qil;

        // Executable command for the compiled stylesheet
        private XmlILCommand? _command;

        public XslCompiledTransform() { }

        public XslCompiledTransform(bool enableDebug)
        {
            _enableDebug = enableDebug;
        }

        /// <summary>
        /// This function is called on every recompilation to discard all previous results
        /// </summary>
        private void Reset()
        {
            _compilerErrorColl = null;
            OutputSettings = null;
            _qil = null;
            _command = null;
            _settings = null;
        }

        /// <summary>
        /// Writer settings specified in the stylesheet
        /// </summary>
        public XmlWriterSettings? OutputSettings { get; private set; }

        // Xsl3: upstream passes OutputSettings to XmlWriter.Create, which substitutes its defaults for null
        // (before a stylesheet is loaded; the transform then fails with Xslt_NoStylesheetLoaded).
        private static readonly XmlWriterSettings s_defaultWriterSettings = new XmlWriterSettings();

        //------------------------------------------------
        // Load methods
        //------------------------------------------------

        // SxS: This method does not take any resource name and does not expose any resources to the caller.
        // It's OK to suppress the SxS warning.
        [RequiresDynamicCode(CompilesIL)]
        public void Load(XmlReader stylesheet)
        {
            Reset();
            LoadInternal(stylesheet, XsltSettings.Default, CreateDefaultResolver(), originalStylesheetResolver: null);
        }

        // SxS: This method does not take any resource name and does not expose any resources to the caller.
        // It's OK to suppress the SxS warning.
        [RequiresDynamicCode(CompilesIL)]
        public void Load(XmlReader stylesheet, XsltSettings? settings, XmlResolver? stylesheetResolver)
        {
            Reset();
            LoadInternal(stylesheet, settings, stylesheetResolver, stylesheetResolver);
        }

        // SxS: This method does not take any resource name and does not expose any resources to the caller.
        // It's OK to suppress the SxS warning.
        [RequiresDynamicCode(CompilesIL)]
        public void Load(IXPathNavigable stylesheet)
        {
            Reset();
            LoadInternal(stylesheet, XsltSettings.Default, CreateDefaultResolver(), originalStylesheetResolver: null);
        }

        // SxS: This method does not take any resource name and does not expose any resources to the caller.
        // It's OK to suppress the SxS warning.
        [RequiresDynamicCode(CompilesIL)]
        public void Load(IXPathNavigable stylesheet, XsltSettings? settings, XmlResolver? stylesheetResolver)
        {
            Reset();
            LoadInternal(stylesheet, settings, stylesheetResolver, stylesheetResolver);
        }

        [RequiresDynamicCode(CompilesIL)]
        public void Load(string stylesheetUri)
        {
            Reset();
            ArgumentNullException.ThrowIfNull(stylesheetUri);
            LoadInternal(stylesheetUri, XsltSettings.Default, CreateDefaultResolver(), originalStylesheetResolver: null);
        }

        [RequiresDynamicCode(CompilesIL)]
        public void Load(string stylesheetUri, XsltSettings? settings, XmlResolver? stylesheetResolver)
        {
            Reset();
            ArgumentNullException.ThrowIfNull(stylesheetUri);
            LoadInternal(stylesheetUri, settings, stylesheetResolver, stylesheetResolver);
        }

        // The 'originalStylesheetResolver' argument should be the original XmlResolver
        // that was passed to the caller (or null), *before* any default substitutions
        // were made by the caller.
        [RequiresDynamicCode(CompilesIL)]
        private void LoadInternal(object stylesheet, XsltSettings? settings, XmlResolver? stylesheetResolver, XmlResolver? originalStylesheetResolver)
        {
            ArgumentNullException.ThrowIfNull(stylesheet);

            settings ??= XsltSettings.Default;
            _settings = settings.Clone();
            // Xsl3: the stylesheet's own constants, for every unit compiled for it (ConstantPool).
            using var constants = Xsl3.XPath31.ConstantPool.Use(new Xsl3.XPath31.ConstantTable());
            // Xsl3: static expressions (use-when, static variables) may call fn:transform only with EnableEvaluate.
            bool? savedDynamic = XmlQueryRuntime.DynamicTransforms;
            XmlQueryRuntime.DynamicTransforms = settings.EnableEvaluate;
            try
            {
                CompileXsltToQil(stylesheet, settings, stylesheetResolver, originalStylesheetResolver);
            }
            finally
            {
                XmlQueryRuntime.DynamicTransforms = savedDynamic;
            }
            CompilerError? error = GetFirstError();
            if (error != null)
            {
                throw new XslLoadException(error);
            }
            if (!settings.CheckOnly)
            {
                CompileQilToMsil();
            }
        }

        [MemberNotNull(nameof(_compilerErrorColl))]
        [MemberNotNull(nameof(_qil))]
        [RequiresDynamicCode(CompilesIL)]
        private void CompileXsltToQil(object stylesheet, XsltSettings settings, XmlResolver? stylesheetResolver, XmlResolver? originalStylesheetResolver)
        {
            var compiler = new Compiler(settings, _enableDebug, null)
            {
                InitialTemplate = InitialTemplate, InitialMode = InitialMode, StaticParameters = StaticParameters, Packages = Packages,
                InitialFunction = InitialFunction, InitialFunctionArity = InitialFunctionArity, RawResult = RawResult, InitialMatchSelection = InitialMatchSelection,
                InitialParameters = InitialParameters,
            };
            _compilerErrorColl = compiler.Compile(stylesheet, stylesheetResolver, originalStylesheetResolver, out _qil);
            _qil.CompactTree = UseCompactTree;
        }

        /// <summary>
        /// Xsl3: whether an XSLT 1.0 stylesheet builds the documents it parses itself (an input given as a URI or
        /// reader, and those document() loads) as compact trees rather than XPathDocument's, as stock does. Set before
        /// Load. The compact tree takes about 60% of the memory and parses faster; it reads the same, except that
        /// generate-id() gives different (equally unique) ids. XSLT 2.0+ stylesheets always use it.
        /// </summary>
        public bool UseCompactTree { get; set; }

        // Xsl3: the settings of the last Load (run-time ones: result documents, environment variables, time limit, thread).
        private XsltSettings? _settings;

        /// <summary>
        /// Xsl3: the base output URI (XSLT 3.0 §2.3.6): what <c>xsl:result-document</c> resolves a relative href against
        /// and <c>current-output-uri()</c> returns. It names the principal result's file (<c>file:///out/main.xml</c>),
        /// or a directory when it ends with <c>/</c> or names an existing directory. With
        /// <see cref="XsltSettings.EnableResultDocument"/>, secondary results are confined to that directory and its
        /// subdirectories, and are not written through symbolic links below it. <c>Transform(string, string)</c> uses
        /// the results file when this is null. One value serves every <c>Transform</c> call on this instance.
        /// </summary>
        public Uri? BaseOutputUri
        {
            get => _baseOutputUri;
            set => _baseOutputUri = value is { IsAbsoluteUri: false } ? throw new ArgumentException("The base output URI must be absolute.", nameof(value)) : value;
        }
        private Uri? _baseOutputUri;

        // Xsl3: entry points for the XSLT 3.0 API (and the W3C harness), set before Load. The public
        // XsltCompiler/Xslt30Transformer API of SPEC §7.2 will supply these.
        internal XmlQualifiedName? InitialTemplate { get; set; }

        /// <summary>
        /// The initial template's or mode's parameters (XSLT 3.0 §2.3.4), tunnel ones too: the value of the n-th is the
        /// parameter named <see cref="InitialParameter"/>(n).
        /// </summary>
        internal System.Collections.Generic.IList<(XmlQualifiedName Name, bool Tunnel)>? InitialParameters { get; set; }

        internal static XmlQualifiedName InitialParameter(int position) => InitialFunctionArguments.Parameter(position);

        /// <summary>
        /// The initial function (XSLT 3.0 §2.3.4) and its arity: the transformation calls it, with the arguments
        /// supplied as parameters named <see cref="InitialFunctionArgument"/>(1…n), and delivers its result.
        /// </summary>
        internal XmlQualifiedName? InitialFunction { get; set; }

        internal int InitialFunctionArity { get; set; }


        /// <summary>Raw result delivery (XSLT 3.0 §2.3.6): the principal result is the sequence, read with <see cref="TransformToItems"/>.</summary>
        internal bool RawResult { get; set; }

        /// <summary>Runs a stylesheet loaded with <see cref="RawResult"/> and returns its result sequence.</summary>
        internal System.Collections.Generic.IList<XPathItem> TransformToItems(XPathNavigator input, XsltArgumentList? arguments, XmlResolver? documentResolver) =>
            TransformToItems(input, arguments, documentResolver, BaseOutputUri);

        /// <summary>Xsl3: <see cref="TransformToItems(XPathNavigator, XsltArgumentList?, XmlResolver?)"/> with this run's base output URI (the 3.0 API).</summary>
        internal System.Collections.Generic.IList<XPathItem> TransformToItems(XPathNavigator input, XsltArgumentList? arguments, XmlResolver? documentResolver, Uri? baseOutputUri,
            Func<string, string, TextWriter>? resultDocumentHandler = null)
        {
            CheckCommand();
            CheckInput(input);
            System.Collections.Generic.IList<XPathItem> items = Array.Empty<XPathItem>();
            Run(arguments, baseOutputUri, () => items = _command.Evaluate(input, documentResolver, arguments), resultDocumentHandler);
            return items;
        }

        /// <summary>
        /// Xsl3: serializes a run as <c>xsl:output</c> says to a stream, a text writer or an XML writer, with this run's
        /// base output URI (the 3.0 API).
        /// </summary>
        internal void TransformTo(XPathNavigator input, XsltArgumentList? arguments, object destination, XmlResolver? documentResolver, Uri? baseOutputUri,
            Func<string, string, TextWriter>? resultDocumentHandler = null)
        {
            CheckCommand();
            CheckInput(input);
            switch (destination)
            {
                case Stream stream:
                {
                    using XmlWriter writer = CreateResultWriter(stream);
                    Execute(writer);
                    break;
                }
                case TextWriter text:
                {
                    using XmlWriter writer = CreateResultWriter(text);
                    Execute(writer);
                    break;
                }
                case XmlWriter xml:
                    Execute(xml);
                    break;
                default:
                    throw new ArgumentException("The destination must be a Stream, a TextWriter or an XmlWriter", nameof(destination));
            }

            void Execute(XmlWriter results)
            {
                if (results is RawResultWriter raw)
                {
                    Run(arguments, baseOutputUri, () => raw.Write(_command!.Evaluate(input, documentResolver, arguments)), resultDocumentHandler);
                    return;
                }
                Run(arguments, baseOutputUri, () => _command!.Execute(input, documentResolver, arguments, results), resultDocumentHandler);
            }
        }

        /// <summary>Xsl3: the errors and warnings of the last Load.</summary>
        internal CompilerErrorCollection? CompilerErrors => _compilerErrorColl;

        /// <summary>The name of the parameter that supplies argument <paramref name="position"/> (from 1) of the initial function.</summary>
        internal static XmlQualifiedName InitialFunctionArgument(int position) => InitialFunctionArguments.Name(position);

        internal XmlQualifiedName? InitialMode { get; set; }

        /// <summary>
        /// Xsl3: the initial match selection (XSLT 3.0 §2.3.3) is any sequence, supplied as the parameter
        /// <see cref="InitialFunctionArguments.Selection"/>, rather than the input document.
        /// </summary>
        internal bool InitialMatchSelection { get; set; }

        /// <summary>
        /// Xsl3: an input that stands for none: an initial named template then has no context item, and an initial mode
        /// raises XTDE0044 (XSLT 3.0 §2.3.2–3).
        /// </summary>
        internal static readonly XPathNavigator AbsentInput = Xsl3.Tree.CompactDocument.Load(
            XmlReader.Create(new StringReader(""), new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment })).CreateNavigator();

        /// <summary>Xsl3: values of the stylesheet's static parameters (xsl:param static="yes"), set before Load.</summary>
        internal System.Collections.Generic.Dictionary<XmlQualifiedName, System.Collections.Generic.IList<XPathItem>>? StaticParameters { get; set; }

        /// <summary>Xsl3: the packages xsl:use-package can name: their names, versions and locations.</summary>
        internal System.Collections.Generic.IReadOnlyList<(string Name, string Version, Uri Location)>? Packages { get; set; }

        /// <summary>
        /// Returns the first compiler error except warnings
        /// </summary>
        private CompilerError? GetFirstError()
        {
            foreach (CompilerError error in _compilerErrorColl!)
            {
                if (!error.IsWarning)
                {
                    return error;
                }
            }
            return null;
        }

        [RequiresDynamicCode(CompilesIL)]
        private void CompileQilToMsil()
        {
            _command = new XmlILGenerator().Generate(_qil!, null)!;
            OutputSettings = _command.StaticData.DefaultWriterSettings;
            _qil = null;
        }

#if NET9_0_OR_GREATER
        /// <summary>
        /// Xsl3 (Sink B): compiles a stylesheet into an assembly saved to <paramref name="output"/>, holding the public type
        /// <paramref name="typeName"/> that <see cref="Load(Type)"/> loads without compiling (docs/plan/p4-sink-b.md).
        /// </summary>
        [RequiresDynamicCode(CompilesIL)]
        internal void CompileToAssembly(XmlReader stylesheet, XsltSettings? settings, XmlResolver? stylesheetResolver, string assemblyName, string typeName, Stream output)
        {
            Reset();
            settings ??= XsltSettings.Default;
            using var constants = Xsl3.XPath31.ConstantPool.Use(new Xsl3.XPath31.ConstantTable());
            CompileXsltToQil(stylesheet, settings, stylesheetResolver, stylesheetResolver);
            if (GetFirstError() is CompilerError error)
            {
                throw new XslLoadException(error);
            }
            var assembly = new PersistedAssemblyBuilder(new AssemblyName(assemblyName), typeof(object).Assembly);
            ModuleBuilder module = assembly.DefineDynamicModule(assemblyName);
            TypeBuilder type = module.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract | TypeAttributes.Class);
            type.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(System.CodeDom.Compiler.GeneratedCodeAttribute).GetConstructor(new[] { typeof(string), typeof(string) })!,
                new object[] { typeof(XslCompiledTransform).FullName!, s_version?.ToString() ?? "0.0.0.0" }));
            new XmlILGenerator().Generate(_qil!, type);
            _qil = null;
            assembly.Save(output);
        }
#endif

        //------------------------------------------------
        // Load compiled stylesheet from a Type
        //------------------------------------------------
        /// <summary>
        /// Xsl3: loads a precompiled stylesheet (<see cref="Load(Type)"/>) with run-time settings: what it may reach (documents,
        /// result documents, environment variables, evaluation) and its limits. Settings that act at compile time were
        /// fixed when it was compiled.
        /// </summary>
        [RequiresUnreferencedCode("This method will get fields and types from the assembly of the passed in compiledStylesheet and call their constructors which cannot be statically analyzed")]
        public void Load(Type compiledStylesheet, XsltSettings? settings)
        {
            Load(compiledStylesheet);
            _settings = (settings ?? XsltSettings.Default).Clone();
        }

        [RequiresUnreferencedCode("This method will get fields and types from the assembly of the passed in compiledStylesheet and call their constructors which cannot be statically analyzed")]
        public void Load(Type compiledStylesheet)
        {
            Reset();
            ArgumentNullException.ThrowIfNull(compiledStylesheet);
            object[] customAttrs = compiledStylesheet.GetCustomAttributes(typeof(GeneratedCodeAttribute), false);
            GeneratedCodeAttribute? generatedCodeAttr = customAttrs.Length > 0 ? (GeneratedCodeAttribute)customAttrs[0] : null;

            // If GeneratedCodeAttribute is not there, it is not a compiled stylesheet class
            if (generatedCodeAttr != null && generatedCodeAttr.Tool == typeof(XslCompiledTransform).FullName)
            {
                if (s_version < Version.Parse(generatedCodeAttr.Version!))
                {
                    throw new ArgumentException(SR.Format(SR.Xslt_IncompatibleCompiledStylesheetVersion, generatedCodeAttr.Version, s_version), nameof(compiledStylesheet));
                }

                FieldInfo? fldData = compiledStylesheet.GetField(XmlQueryStaticData.DataFieldName, BindingFlags.Static | BindingFlags.NonPublic);
                FieldInfo? fldTypes = compiledStylesheet.GetField(XmlQueryStaticData.TypesFieldName, BindingFlags.Static | BindingFlags.NonPublic);

                // If private fields are not there, it is not a compiled stylesheet class
                if (fldData != null && fldTypes != null)
                {
                    // Retrieve query static data from the type
                    byte[]? queryData = fldData.GetValue(/*this:*/null) as byte[];

                    if (queryData != null)
                    {
                        MethodInfo? executeMethod = compiledStylesheet.GetMethod("Execute", BindingFlags.Static | BindingFlags.NonPublic);
                        Type[]? earlyBoundTypes = (Type[]?)fldTypes.GetValue(null);

                        // Load the stylesheet
                        Load(executeMethod!, queryData, earlyBoundTypes);
                        return;
                    }
                }
            }

            // Throw an exception if the command was not loaded
            if (_command == null)
            {
                throw new ArgumentException(SR.Format(SR.Xslt_NotCompiledStylesheet, compiledStylesheet.FullName), nameof(compiledStylesheet));
            }
        }

        [RequiresUnreferencedCode("This method will call into constructors of the earlyBoundTypes array which cannot be statically analyzed.")]
        public void Load(MethodInfo executeMethod, byte[] queryData, Type[]? earlyBoundTypes)
        {
            Reset();
            ArgumentNullException.ThrowIfNull(executeMethod);
            ArgumentNullException.ThrowIfNull(queryData);

            Delegate delExec = executeMethod is DynamicMethod dm
                ? dm.CreateDelegate(typeof(ExecuteDelegate))
                : executeMethod.CreateDelegate<ExecuteDelegate>();

            var staticData = new XmlQueryStaticData(queryData, earlyBoundTypes);
            if (staticData.DispatcherName is string dispatcher && executeMethod.DeclaringType?.GetMethod(dispatcher, BindingFlags.Static | BindingFlags.NonPublic) is MethodInfo dispatch)
            {
                // Xsl3 (Sink B): the saved assembly's function item dispatcher.
                staticData.Dispatcher = dispatch.CreateDelegate<Func<XmlQueryRuntime, XPathItem, System.Collections.Generic.IList<XPathItem>>>();
            }
            _command = new XmlILCommand((ExecuteDelegate)delExec, staticData);
            OutputSettings = _command.StaticData.DefaultWriterSettings;
        }

        //------------------------------------------------
        // Transform methods which take an IXPathNavigable
        //------------------------------------------------

        // The writer for the Stream and TextWriter overloads: upstream's raw writer chain, unless serialization
        // parameters beyond XmlWriterSettings (Runtime.OutputExtras) are to be applied in front of a normal writer.
        private XmlWriter CreateResultWriter(Stream results) => CreateLimitedWriter(_settings?.MaxOutputBytes is long max ? new OutputLimit.LimitedStream(results, max) : results);

        private XmlWriter CreateResultWriter(TextWriter results) => CreateLimitedWriter(_settings?.MaxOutputBytes is long max ? new OutputLimit.LimitedTextWriter(results, max) : results);

        private XmlWriter CreateLimitedWriter(Stream results) =>
            _command?.StaticData.OutputExtras is { RawMethod: not null } raw ? new RawResultWriter(raw, OutputSettings ?? s_defaultWriterSettings, null, results)
            : _command?.StaticData.Xslt2 == true
                ? DirectPrincipalSettings() is XmlWriterSettings direct
                    ? XmlWriterSettingsInternals.CreateRawWriter(direct, results)
                    : new PrincipalResult(s => XmlWriter.Create(results, s), OutputSettings ?? s_defaultWriterSettings, _command.StaticData.OutputExtras)
                : XmlWriterSettingsInternals.CreateRawWriter(OutputSettings ?? s_defaultWriterSettings, results);

        private XmlWriter CreateLimitedWriter(TextWriter results) =>
            _command?.StaticData.OutputExtras is { RawMethod: not null } raw ? new RawResultWriter(raw, OutputSettings ?? s_defaultWriterSettings, results, null)
            : _command?.StaticData.Xslt2 == true
                ? DirectPrincipalSettings() is XmlWriterSettings direct
                    ? XmlWriterSettingsInternals.CreateRawWriter(direct, results)
                    : new PrincipalResult(s => XmlWriter.Create(results, s), OutputSettings ?? s_defaultWriterSettings, _command.StaticData.OutputExtras)
                : XmlWriterSettingsInternals.CreateRawWriter(OutputSettings ?? s_defaultWriterSettings, results);

        /// <summary>
        /// Xsl3 (P4): for a 2.0+ stylesheet that needs nothing of the principal result writer (no xsl:result-document,
        /// no serialization parameters beyond XmlWriterSettings, the xml method), the settings to write the result
        /// with a raw writer directly, as 1.0 does: those PrincipalResult.Create would give its writer, with the
        /// encoding named as the stylesheet names it. Null when the principal result writer is needed.
        /// </summary>
        private XmlWriterSettings? DirectPrincipalSettings()
        {
            XmlQueryStaticData data = _command!.StaticData;
            XmlWriterSettings settings = OutputSettings ?? s_defaultWriterSettings;
            if (data.ResultDocuments || data.OutputExtras is { IsEmpty: false } || settings.OutputMethod is not (XmlOutputMethod.Xml or XmlOutputMethod.AutoDetect)
                || settings.ConformanceLevel == ConformanceLevel.Fragment)
            {
                return null;
            }
            if (settings.OmitXmlDeclaration)
            {
                return settings;
            }
            XmlWriterSettings s = settings.Clone();
            s.Encoding = new NamedEncoding(settings.Encoding, data.OutputExtras?.Encoding ?? settings.Encoding.WebName.ToUpperInvariant());
            return s;
        }

        /// <summary>
        /// Xsl3: transforms to a <see cref="TextWriter"/>, resolving documents with <paramref name="documentResolver"/>.
        /// The output is serialized as the stylesheet's output definition (or an xsl:result-document without href) says.
        /// </summary>
        public void Transform(IXPathNavigable input, XsltArgumentList? arguments, TextWriter results, XmlResolver? documentResolver)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            using XmlWriter writer = CreateResultWriter(results);
            Transform(input, arguments, writer, documentResolver);
        }

        /// <summary>Xsl3: transforms to a <see cref="Stream"/>, resolving documents with <paramref name="documentResolver"/>.</summary>
        public void Transform(IXPathNavigable input, XsltArgumentList? arguments, Stream results, XmlResolver? documentResolver)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            using XmlWriter writer = CreateResultWriter(results);
            Transform(input, arguments, writer, documentResolver);
        }

        /// <summary>Xsl3: transforms to a <see cref="Stream"/>, resolving documents with <paramref name="documentResolver"/>.</summary>
        public void Transform(XmlReader input, XsltArgumentList? arguments, Stream results, XmlResolver? documentResolver)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            using XmlWriter writer = CreateResultWriter(results);
            Transform(input, arguments, writer, documentResolver);
        }

        /// <summary>Xsl3: transforms to a <see cref="TextWriter"/>, resolving documents with <paramref name="documentResolver"/>.</summary>
        public void Transform(XmlReader input, XsltArgumentList? arguments, TextWriter results, XmlResolver? documentResolver)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            using XmlWriter writer = CreateResultWriter(results);
            Transform(input, arguments, writer, documentResolver);
        }

        public void Transform(IXPathNavigable input, XmlWriter results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            Transform(input, null, results, CreateDefaultResolver());
        }

        public void Transform(IXPathNavigable input, XsltArgumentList? arguments, XmlWriter results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            Transform(input, arguments, results, CreateDefaultResolver());
        }

        public void Transform(IXPathNavigable input, XsltArgumentList? arguments, TextWriter results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            using XmlWriter writer = CreateResultWriter(results);
            Transform(input, arguments, writer, CreateDefaultResolver());
        }

        public void Transform(IXPathNavigable input, XsltArgumentList? arguments, Stream results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            using XmlWriter writer = CreateResultWriter(results);
            Transform(input, arguments, writer, CreateDefaultResolver());
        }

        //------------------------------------------------
        // Transform methods which take an XmlReader
        //------------------------------------------------

        public void Transform(XmlReader input, XmlWriter results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            Transform(input, null, results, CreateDefaultResolver());
        }

        public void Transform(XmlReader input, XsltArgumentList? arguments, XmlWriter results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            Transform(input, arguments, results, CreateDefaultResolver());
        }

        public void Transform(XmlReader input, XsltArgumentList? arguments, TextWriter results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            using XmlWriter writer = CreateResultWriter(results);
            Transform(input, arguments, writer, CreateDefaultResolver());
        }

        public void Transform(XmlReader input, XsltArgumentList? arguments, Stream results)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            using XmlWriter writer = CreateResultWriter(results);
            Transform(input, arguments, writer, CreateDefaultResolver());
        }

        //------------------------------------------------
        // Transform methods which take a uri
        // SxS Note: Annotations should propagate to the caller to have them either check that
        // the passed URIs are SxS safe or decide that they don't have to be SxS safe and
        // suppress the message.
        //------------------------------------------------

        public void Transform(string inputUri, XmlWriter results)
        {
            ArgumentNullException.ThrowIfNull(inputUri);
            ArgumentNullException.ThrowIfNull(results);

            using XmlReader reader = XmlReader.Create(inputUri);
            Transform(reader, null, results, CreateDefaultResolver());
        }

        public void Transform(string inputUri, XsltArgumentList? arguments, XmlWriter results)
        {
            ArgumentNullException.ThrowIfNull(inputUri);
            ArgumentNullException.ThrowIfNull(results);

            using XmlReader reader = XmlReader.Create(inputUri);
            Transform(reader, arguments, results, CreateDefaultResolver());
        }

        public void Transform(string inputUri, XsltArgumentList? arguments, TextWriter results)
        {
            ArgumentNullException.ThrowIfNull(inputUri);
            ArgumentNullException.ThrowIfNull(results);

            using XmlReader reader = XmlReader.Create(inputUri);
            using XmlWriter writer = CreateResultWriter(results);
            Transform(reader, arguments, writer, CreateDefaultResolver());
        }

        public void Transform(string inputUri, XsltArgumentList? arguments, Stream results)
        {
            ArgumentNullException.ThrowIfNull(inputUri);
            ArgumentNullException.ThrowIfNull(results);

            using XmlReader reader = XmlReader.Create(inputUri);
            using XmlWriter writer = CreateResultWriter(results);
            Transform(reader, arguments, writer, CreateDefaultResolver());
        }

        public void Transform(string inputUri, string resultsFile)
        {
            ArgumentNullException.ThrowIfNull(inputUri);
            ArgumentNullException.ThrowIfNull(resultsFile);

            // SQLBUDT 276415: Prevent wiping out the content of the input file if the output file is the same
            using XmlReader reader = XmlReader.Create(inputUri);
            using XmlWriter writer = XmlWriterSettingsInternals.CreateRawWriter(OutputSettings ?? s_defaultWriterSettings, resultsFile);
            CheckCommand();
            // Xsl3: secondary results go next to the results file unless BaseOutputUri says otherwise.
            Run(null, BaseOutputUri ?? new Uri(Path.GetFullPath(resultsFile)), () => _command.Execute(reader, CreateDefaultResolver(), null, writer));
        }

        //------------------------------------------------
        // Main Transform overloads
        //------------------------------------------------

        // SxS: This method does not take any resource name and does not expose any resources to the caller.
        // It's OK to suppress the SxS warning.
        public void Transform(XmlReader input, XsltArgumentList? arguments, XmlWriter results, XmlResolver? documentResolver)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            CheckCommand();
            if (results is RawResultWriter raw)
            {
                Run(arguments, BaseOutputUri, () => raw.Write(_command.Evaluate(input, documentResolver, arguments)));
                return;
            }
            Run(arguments, BaseOutputUri, () => _command.Execute(input, documentResolver, arguments, results));
        }

        // SxS: This method does not take any resource name and does not expose any resources to the caller.
        // It's OK to suppress the SxS warning.
        public void Transform(IXPathNavigable input, XsltArgumentList? arguments, XmlWriter results, XmlResolver? documentResolver)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            CheckCommand();
            CheckInput(input as XPathNavigator);
            if (results is RawResultWriter raw)
            {
                Run(arguments, BaseOutputUri, () => raw.Write(_command.Evaluate(input.CreateNavigator()!, documentResolver, arguments)));
                return;
            }
            Run(arguments, BaseOutputUri, () => _command.Execute(input.CreateNavigator()!, documentResolver, arguments, results));
        }

        /// <summary>
        /// Xsl3: runs a transformation with the run-time settings in force for the current thread (SPEC §21.1–2):
        /// cancellation (the argument list's token, the timeout), the base output URI, secondary results written under
        /// it, environment variables; on a dedicated thread if asked. What a caller has already set for the thread
        /// (the W3C harness sets these itself) is kept.
        /// </summary>
        // Xsl3: an initial mode with no input (AbsentInput) has no initial match selection (XTDE0044).
        private void CheckInput(XPathNavigator? input)
        {
            if (input != null && _command?.StaticData.Xslt2 == true && InitialTemplate == null && InitialFunction == null && !InitialMatchSelection
                && AbsentInput.IsSamePosition(input))
            {
                throw new Xsl3.Xdm.XdmException("XTDE0044", "The transformation applies templates, but there is no initial match selection");
            }
        }

        private void Run(XsltArgumentList? arguments, Uri? baseOutputUri, Action transform, Func<string, string, TextWriter>? resultDocumentHandler = null)
        {
            XsltSettings settings = _settings ?? XsltSettings.Default;
            System.Threading.CancellationToken token = arguments?.CancellationToken ?? default;
            using System.Threading.CancellationTokenSource? limit = settings.Timeout is TimeSpan timeout
                ? System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token) : null;
            if (limit != null)
            {
                limit.CancelAfter(settings.Timeout!.Value);
                token = limit.Token;
            }
            void Body()
            {
                System.Threading.CancellationToken savedToken = XmlQueryRuntime.Cancellation;
                string? savedBase = XmlQueryRuntime.BaseOutputUri;
                Func<string, Stream>? savedOpener = XmlQueryRuntime.ResultDocumentOpener;
                bool savedEnvironment = XmlQueryRuntime.EnvironmentVariables;
                bool? savedDocuments = XmlQueryRuntime.DocumentFunctions;
                int savedDepth = XmlQueryRuntime.Depth;
                bool savedExtensions = XmlQueryRuntime.ExtensionFunctions;
                bool? savedDynamic = XmlQueryRuntime.DynamicTransforms;
                var savedPackages = XmlQueryRuntime.Packages;
                Func<string, string, TextWriter>? savedHandler = XmlQueryRuntime.ResultDocumentHandler;
                try
                {
                    if (resultDocumentHandler != null)
                    {
                        XmlQueryRuntime.ResultDocumentHandler = resultDocumentHandler; // the 3.0 API's, for this run
                    }
                    if (token.CanBeCanceled)
                    {
                        XmlQueryRuntime.Cancellation = token;
                    }
                    // This transform's own base output URI and file opener, even inside another transform on the
                    // thread (through an extension object or a resolver); a host's handler (the W3C harness) stays.
                    if (baseOutputUri != null)
                    {
                        XmlQueryRuntime.BaseOutputUri = baseOutputUri.AbsoluteUri;
                    }
                    XmlQueryRuntime.ResultDocumentOpener = settings.EnableResultDocument && XmlQueryRuntime.ResultDocumentHandler == null
                        ? ResultFileOpener(XmlQueryRuntime.BaseOutputUri)
                        : null;
                    XmlQueryRuntime.EnvironmentVariables = settings.EnableEnvironmentVariables;
                    XmlQueryRuntime.DocumentFunctions = settings.EnableDocumentFunction;
                    XmlQueryRuntime.Depth = 0;
                    XmlQueryRuntime.ExtensionFunctions = settings.EnableExtensionFunctions;
                    XmlQueryRuntime.DynamicTransforms = settings.EnableEvaluate;
                    XmlQueryRuntime.Packages = Packages ?? savedPackages;
                    transform();
                }
                finally
                {
                    XmlQueryRuntime.Cancellation = savedToken;
                    XmlQueryRuntime.BaseOutputUri = savedBase;
                    XmlQueryRuntime.ResultDocumentOpener = savedOpener;
                    XmlQueryRuntime.EnvironmentVariables = savedEnvironment;
                    XmlQueryRuntime.DocumentFunctions = savedDocuments;
                    XmlQueryRuntime.Depth = savedDepth;
                    XmlQueryRuntime.ExtensionFunctions = savedExtensions;
                    XmlQueryRuntime.DynamicTransforms = savedDynamic;
                    XmlQueryRuntime.Packages = savedPackages;
                    XmlQueryRuntime.ResultDocumentHandler = savedHandler;
                }
            }
            if (!settings.UseDedicatedThread)
            {
                Body();
                return;
            }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    Body();
                }
                catch (Exception e)
                {
                    failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e);
                }
            }, settings.DedicatedThreadStackSize);
            thread.Start();
            thread.Join();
            failure?.Throw();
        }

        /// <summary>
        /// Xsl3: the default destination of secondary results with XsltSettings.EnableResultDocument: a file, which
        /// must be under the base output URI's directory (or the error is XTDE1480; so it is without a base output URI).
        /// </summary>
        private static Func<string, Stream> ResultFileOpener(string? baseOutputUri) => uri =>
        {
            Uri target = new Uri(uri, UriKind.RelativeOrAbsolute);
            if (baseOutputUri == null || !target.IsAbsoluteUri || !target.IsFile || !new Uri(baseOutputUri).IsFile)
            {
                throw new Xsl3.Xdm.XdmException("XTDE1480", $"Secondary results are written only under the base output URI (XslCompiledTransform.BaseOutputUri); can't write {uri}");
            }
            string basePath = Path.GetFullPath(new Uri(baseOutputUri).LocalPath);
            // A directory when it says so (a trailing separator) or exists as one; else the principal result's file.
            string directory = Path.EndsInDirectorySeparator(basePath) || Directory.Exists(basePath)
                ? Path.TrimEndingDirectorySeparator(basePath)
                : Path.GetDirectoryName(basePath) ?? "";
            if (directory.Length == 0 || Path.GetPathRoot(directory) is string root && Path.TrimEndingDirectorySeparator(root) == directory)
            {
                throw new Xsl3.Xdm.XdmException("XTDE1480", $"Secondary results aren't written under a file system root ({basePath})");
            }
            string file = Path.GetFullPath(target.LocalPath);
            // Windows and macOS file systems are usually case-insensitive.
            StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!file.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            {
                throw new Xsl3.Xdm.XdmException("XTDE1480", $"Secondary results are written only under {directory}; can't write {file}");
            }
            // Not through a symbolic link (or junction) below the directory, which could lead out of it.
            for (string? path = file; path != null && path.Length > directory.Length; path = Path.GetDirectoryName(path))
            {
                FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                if (info.Exists && info.LinkTarget != null)
                {
                    throw new Xsl3.Xdm.XdmException("XTDE1480", $"Secondary results aren't written through the symbolic link {path}");
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            return new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read);
        };

        [MemberNotNull(nameof(_command))]
        private void CheckCommand()
        {
            if (_command == null)
            {
                throw new InvalidOperationException(SR.Xslt_NoStylesheetLoaded);
            }
        }

        private static XmlResolver CreateDefaultResolver()
        {
            if (LocalAppContextSwitches.AllowDefaultResolver)
            {
                return XmlReaderSettings.GetDefaultPermissiveResolver();
            }

            return XmlResolver.ThrowingResolver;
        }

        /// <summary>
        /// Xsl3: the target of a transformation whose output method is json or adaptive: the raw result sequence
        /// is serialized to the text writer or stream (Runtime.ItemSerializer); it isn't written as XML events.
        /// </summary>
        private sealed class RawResultWriter(Xsl3.Runtime.OutputExtras extras, XmlWriterSettings settings, TextWriter? text, Stream? stream) : XmlWriter
        {
            public void Write(System.Collections.Generic.IList<System.Xml.XPath.XPathItem> items)
            {
                if (extras.RawMethod == "sequence")
                {
                    XmlWriterSettings s = settings.Clone();
                    s.CloseOutput = false;
                    Xsl3.Runtime.ItemSerializer.WriteSequence(items, s, extras,
                        ws => text != null ? XmlWriter.Create(text, ws) : XmlWriter.Create(stream!, ws));
                    return;
                }
                if (text != null)
                {
                    Xsl3.Runtime.ItemSerializer.Write(items, extras, text);
                    text.Flush();
                    return;
                }
                System.Text.Encoding encoding = extras.Encoding.Equals("UTF-8", StringComparison.OrdinalIgnoreCase)
                    ? new System.Text.UTF8Encoding(false) : System.Text.Encoding.GetEncoding(extras.Encoding);
                using var writer = new StreamWriter(stream!, encoding, 4096, leaveOpen: true);
                Xsl3.Runtime.ItemSerializer.Write(items, extras, writer);
            }

            private static NotSupportedException Unused() => new("The json and adaptive output methods don't write XML events");

            public override WriteState WriteState => WriteState.Start;
            public override void Flush() => text?.Flush();
            public override string? LookupPrefix(string ns) => null;
            public override void WriteBase64(byte[] buffer, int index, int count) => throw Unused();
            public override void WriteCData(string? text) => throw Unused();
            public override void WriteCharEntity(char ch) => throw Unused();
            public override void WriteChars(char[] buffer, int index, int count) => throw Unused();
            public override void WriteComment(string? text) => throw Unused();
            public override void WriteDocType(string name, string? pubid, string? sysid, string? subset) => throw Unused();
            public override void WriteEndAttribute() => throw Unused();
            public override void WriteEndDocument() => throw Unused();
            public override void WriteEndElement() => throw Unused();
            public override void WriteEntityRef(string name) => throw Unused();
            public override void WriteFullEndElement() => throw Unused();
            public override void WriteProcessingInstruction(string name, string? text) => throw Unused();
            public override void WriteRaw(char[] buffer, int index, int count) => throw Unused();
            public override void WriteRaw(string data) => throw Unused();
            public override void WriteStartAttribute(string? prefix, string localName, string? ns) => throw Unused();
            public override void WriteStartDocument() => throw Unused();
            public override void WriteStartDocument(bool standalone) => throw Unused();
            public override void WriteStartElement(string? prefix, string localName, string? ns) => throw Unused();
            public override void WriteString(string? text) => throw Unused();
            public override void WriteSurrogateCharEntity(char lowChar, char highChar) => throw Unused();
            public override void WriteWhitespace(string? ws) => throw Unused();
        }
    }
}
