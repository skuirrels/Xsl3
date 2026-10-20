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

    [RequiresDynamicCode("XslCompiledTransform requires dynamic code because it generates IL at runtime.")]
    public sealed class XslCompiledTransform
    {
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
        public void Load(XmlReader stylesheet)
        {
            Reset();
            LoadInternal(stylesheet, XsltSettings.Default, CreateDefaultResolver(), originalStylesheetResolver: null);
        }

        // SxS: This method does not take any resource name and does not expose any resources to the caller.
        // It's OK to suppress the SxS warning.
        public void Load(XmlReader stylesheet, XsltSettings? settings, XmlResolver? stylesheetResolver)
        {
            Reset();
            LoadInternal(stylesheet, settings, stylesheetResolver, stylesheetResolver);
        }

        // SxS: This method does not take any resource name and does not expose any resources to the caller.
        // It's OK to suppress the SxS warning.
        public void Load(IXPathNavigable stylesheet)
        {
            Reset();
            LoadInternal(stylesheet, XsltSettings.Default, CreateDefaultResolver(), originalStylesheetResolver: null);
        }

        // SxS: This method does not take any resource name and does not expose any resources to the caller.
        // It's OK to suppress the SxS warning.
        public void Load(IXPathNavigable stylesheet, XsltSettings? settings, XmlResolver? stylesheetResolver)
        {
            Reset();
            LoadInternal(stylesheet, settings, stylesheetResolver, stylesheetResolver);
        }

        public void Load(string stylesheetUri)
        {
            Reset();
            ArgumentNullException.ThrowIfNull(stylesheetUri);
            LoadInternal(stylesheetUri, XsltSettings.Default, CreateDefaultResolver(), originalStylesheetResolver: null);
        }

        public void Load(string stylesheetUri, XsltSettings? settings, XmlResolver? stylesheetResolver)
        {
            Reset();
            ArgumentNullException.ThrowIfNull(stylesheetUri);
            LoadInternal(stylesheetUri, settings, stylesheetResolver, stylesheetResolver);
        }

        // The 'originalStylesheetResolver' argument should be the original XmlResolver
        // that was passed to the caller (or null), *before* any default substitutions
        // were made by the caller.
        private void LoadInternal(object stylesheet, XsltSettings? settings, XmlResolver? stylesheetResolver, XmlResolver? originalStylesheetResolver)
        {
            ArgumentNullException.ThrowIfNull(stylesheet);

            settings ??= XsltSettings.Default;
            CompileXsltToQil(stylesheet, settings, stylesheetResolver, originalStylesheetResolver);
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
        private void CompileXsltToQil(object stylesheet, XsltSettings settings, XmlResolver? stylesheetResolver, XmlResolver? originalStylesheetResolver)
        {
            var compiler = new Compiler(settings, _enableDebug, null) { InitialTemplate = InitialTemplate, InitialMode = InitialMode, StaticParameters = StaticParameters, Packages = Packages };
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

        // Xsl3: entry points for the XSLT 3.0 API (and the W3C harness), set before Load. The public
        // XsltCompiler/Xslt30Transformer API of SPEC §7.2 will supply these.
        internal XmlQualifiedName? InitialTemplate { get; set; }

        internal XmlQualifiedName? InitialMode { get; set; }

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

        private void CompileQilToMsil()
        {
            _command = new XmlILGenerator().Generate(_qil!, null)!;
            OutputSettings = _command.StaticData.DefaultWriterSettings;
            _qil = null;
        }

        //------------------------------------------------
        // Load compiled stylesheet from a Type
        //------------------------------------------------
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

            _command = new XmlILCommand((ExecuteDelegate)delExec, new XmlQueryStaticData(queryData, earlyBoundTypes));
            OutputSettings = _command.StaticData.DefaultWriterSettings;
        }

        //------------------------------------------------
        // Transform methods which take an IXPathNavigable
        //------------------------------------------------

        // The writer for the Stream and TextWriter overloads: upstream's raw writer chain, unless serialization
        // parameters beyond XmlWriterSettings (Runtime.OutputExtras) are to be applied in front of a normal writer.
        private XmlWriter CreateResultWriter(Stream results) =>
            _command?.StaticData.OutputExtras is { RawMethod: not null } raw ? new RawResultWriter(raw, OutputSettings ?? s_defaultWriterSettings, null, results)
            : _command?.StaticData.Xslt2 == true
                ? new PrincipalResult(s => XmlWriter.Create(results, s), OutputSettings ?? s_defaultWriterSettings, _command.StaticData.OutputExtras)
                : XmlWriterSettingsInternals.CreateRawWriter(OutputSettings ?? s_defaultWriterSettings, results);

        private XmlWriter CreateResultWriter(TextWriter results) =>
            _command?.StaticData.OutputExtras is { RawMethod: not null } raw ? new RawResultWriter(raw, OutputSettings ?? s_defaultWriterSettings, results, null)
            : _command?.StaticData.Xslt2 == true
                ? new PrincipalResult(s => XmlWriter.Create(results, s), OutputSettings ?? s_defaultWriterSettings, _command.StaticData.OutputExtras)
                : XmlWriterSettingsInternals.CreateRawWriter(OutputSettings ?? s_defaultWriterSettings, results);

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
            Transform(reader, null, writer, CreateDefaultResolver());
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
                raw.Write(_command.Evaluate(input, documentResolver, arguments));
                return;
            }
            _command.Execute(input, documentResolver, arguments, results);
        }

        // SxS: This method does not take any resource name and does not expose any resources to the caller.
        // It's OK to suppress the SxS warning.
        public void Transform(IXPathNavigable input, XsltArgumentList? arguments, XmlWriter results, XmlResolver? documentResolver)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(results);

            CheckCommand();
            if (results is RawResultWriter raw)
            {
                raw.Write(_command.Evaluate(input.CreateNavigator()!, documentResolver, arguments));
                return;
            }
            _command.Execute(input.CreateNavigator()!, documentResolver, arguments, results);
        }

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
