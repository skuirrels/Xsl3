// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using CompilerError = Xsl3.Xslt.CompilerError;
using CompilerErrorCollection = Xsl3.Xslt.CompilerErrorCollection;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.Xdm;

namespace Xsl3
{
    /// <summary>
    /// Compiles XSLT 3.0 stylesheets and packages for the XSLT 3.0 API (SPEC §7.2; docs/plan/api.md). A compiler holds
    /// the compile-time inputs; it can compile any number of stylesheets, but is not meant to be used by several
    /// threads at once. The executables it makes are immutable and thread-safe.
    /// </summary>
    public sealed class XsltCompiler
    {
        /// <summary>The settings: what stylesheets may reach (documents, result documents, evaluation, …) and run-time limits.</summary>
        public XsltSettings Settings { get; set; } = XsltSettings.Default;

        /// <summary>Resolves the modules a stylesheet includes or imports, and its DTD. Null: files and web URIs are read.</summary>
        public XmlResolver? StylesheetResolver { get; set; }

        /// <summary>Values of the stylesheet's static parameters (<c>xsl:param static="yes"</c>).</summary>
        public IDictionary<QName, XdmValue> StaticParameters { get; } = new Dictionary<QName, XdmValue>();

        /// <summary>The packages <c>xsl:use-package</c> can name.</summary>
        public XsltPackageLibrary Packages { get; } = new();

        /// <summary>Compiles the stylesheet (or package) at <paramref name="stylesheet"/>.</summary>
        /// <exception cref="XsltStaticException">The stylesheet has static errors.</exception>
        [RequiresDynamicCode(XslCompiledTransform.CompilesIL)]
        public XsltExecutable Compile(Uri stylesheet)
        {
            ArgumentNullException.ThrowIfNull(stylesheet);
            if (!stylesheet.IsAbsoluteUri)
            {
                throw new ArgumentException("The stylesheet URI must be absolute.", nameof(stylesheet));
            }
            XmlResolver resolver = StylesheetResolver ?? new XmlUrlResolver();
            return Build(() => XmlReader.Create(stylesheet.AbsoluteUri, ReaderSettings(resolver)), resolver);
        }

        /// <summary>Compiles the stylesheet <paramref name="stylesheet"/> reads, whose base URI is the reader's.</summary>
        /// <exception cref="XsltStaticException">The stylesheet has static errors.</exception>
        [RequiresDynamicCode(XslCompiledTransform.CompilesIL)]
        public XsltExecutable Compile(XmlReader stylesheet)
        {
            ArgumentNullException.ThrowIfNull(stylesheet);
            // Kept as text: an executable compiles the stylesheet again for each other entry point.
            var text = new StringWriter();
            using (XmlWriter writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true }))
            {
                writer.WriteNode(stylesheet, defattr: true);
            }
            return Compile(new StringReader(text.ToString()), Uri.TryCreate(stylesheet.BaseURI, UriKind.Absolute, out Uri? baseUri) ? baseUri : null);
        }

        /// <summary>Compiles the stylesheet text <paramref name="stylesheet"/>; <paramref name="baseUri"/> resolves its relative URIs.</summary>
        /// <exception cref="XsltStaticException">The stylesheet has static errors.</exception>
        [RequiresDynamicCode(XslCompiledTransform.CompilesIL)]
        public XsltExecutable Compile(TextReader stylesheet, Uri? baseUri = null)
        {
            ArgumentNullException.ThrowIfNull(stylesheet);
            string text = stylesheet.ReadToEnd();
            XmlResolver resolver = StylesheetResolver ?? new XmlUrlResolver();
            return Build(() => XmlReader.Create(new StringReader(text), ReaderSettings(resolver), baseUri?.AbsoluteUri), resolver);
        }

        /// <summary>
        /// The stylesheet <paramref name="document"/> names in an <c>xml-stylesheet</c> processing instruction
        /// (W3C Associating Style Sheets with XML documents; XSLT 3.0 §3.12 embedded stylesheet modules): the first
        /// one before the document element whose type is an XSLT or XML media type and that isn't an alternate,
        /// resolved against the document's base URI. A fragment identifier (<c>#id</c>) names an embedded module.
        /// Null if there is none.
        /// </summary>
        public static Uri? GetAssociatedStylesheet(XdmNode document)
        {
            ArgumentNullException.ThrowIfNull(document);
            XPathNavigator node = document.Navigator;
            node.MoveToRoot();
            Uri? baseUri = document.BaseUri ?? (Uri.TryCreate(node.BaseURI, UriKind.Absolute, out Uri? b) ? b : null);
            if (!node.MoveToFirstChild())
            {
                return null;
            }
            do
            {
                if (node.NodeType == XPathNodeType.Element)
                {
                    break;
                }
                if (node.NodeType != XPathNodeType.ProcessingInstruction || node.LocalName != "xml-stylesheet")
                {
                    continue;
                }
                Dictionary<string, string> pseudo = PseudoAttributes(node.Value);
                if (pseudo.TryGetValue("href", out string? href) &&
                    pseudo.GetValueOrDefault("type", "").Trim().ToLowerInvariant() is "text/xsl" or "text/xml" or "application/xml" or "application/xslt+xml" &&
                    pseudo.GetValueOrDefault("alternate", "no") != "yes")
                {
                    return baseUri != null ? new Uri(baseUri, href) : Uri.TryCreate(href, UriKind.Absolute, out Uri? absolute) ? absolute : null;
                }
            }
            while (node.MoveToNext());
            return null;
        }

        // The pseudo-attributes of an xml-stylesheet processing instruction: name="value" or name='value'.
        private static Dictionary<string, string> PseudoAttributes(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"([\w:.-]+)\s*=\s*(?:""([^""]*)""|'([^']*)')"))
            {
                result.TryAdd(m.Groups[1].Value, System.Net.WebUtility.HtmlDecode(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value));
            }
            return result;
        }

        /// <summary>Compiles the stylesheet <paramref name="document"/> names in an <c>xml-stylesheet</c> processing instruction.</summary>
        /// <exception cref="ArgumentException">The document names no stylesheet.</exception>
        /// <exception cref="XsltStaticException">The stylesheet has static errors.</exception>
        [RequiresDynamicCode(XslCompiledTransform.CompilesIL)]
        public XsltExecutable CompileAssociatedStylesheet(XdmNode document)
        {
            Uri stylesheet = GetAssociatedStylesheet(document) ?? throw new ArgumentException("The document has no xml-stylesheet processing instruction naming an XSLT stylesheet", nameof(document));
            return Compile(new StringReader(IncludeWrapper(stylesheet)));
        }

        /// <summary>A stylesheet that includes the module at <paramref name="module"/>, which may be embedded (a fragment identifier).</summary>
        internal static string IncludeWrapper(Uri module) =>
            $"<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:include href='{System.Security.SecurityElement.Escape(module.AbsoluteUri)}'/></xsl:stylesheet>";

        private static XmlReaderSettings ReaderSettings(XmlResolver resolver) =>
            new() { DtdProcessing = DtdProcessing.Parse, XmlResolver = resolver, MaxCharactersFromEntities = 10_000_000 };

        [RequiresDynamicCode(XslCompiledTransform.CompilesIL)]
        private XsltExecutable Build(Func<XmlReader> open, XmlResolver resolver)
        {
            var statics = StaticParameters.ToDictionary(p => new XmlQualifiedName(p.Key.LocalName, p.Key.NamespaceUri), p => p.Value.Items);
            var executable = new XsltExecutable(open, resolver, Settings.Clone(), statics, Packages.Entries.ToArray());
            executable.Principal(); // static errors now
            return executable;
        }
    }

    /// <summary>The packages <c>xsl:use-package</c> can find, by name and version (XSLT 3.0 §3.5).</summary>
    public sealed class XsltPackageLibrary
    {
        private readonly List<(string Name, string Version, Uri Location)> _entries = new();

        internal IReadOnlyList<(string Name, string Version, Uri Location)> Entries => _entries;

        /// <summary>Adds the package at <paramref name="location"/>, with the name and version its <c>xsl:package</c> element declares.</summary>
        public void Add(Uri location)
        {
            ArgumentNullException.ThrowIfNull(location);
            using XmlReader reader = XmlReader.Create(location.AbsoluteUri, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            reader.MoveToContent();
            string name = reader.GetAttribute("name") ?? throw new ArgumentException($"{location} is not a named xsl:package", nameof(location));
            Add(name, reader.GetAttribute("package-version") ?? "1", location);
        }

        /// <summary>Adds the package <paramref name="name"/>, version <paramref name="version"/>, at <paramref name="location"/>.</summary>
        public void Add(string name, string version, Uri location)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(version);
            ArgumentNullException.ThrowIfNull(location);
            _entries.Add((name, version.Trim(), location));
        }
    }

    /// <summary>
    /// A compiled stylesheet: immutable, thread-safe, and meant to be cached. Each transformer it creates runs it. An
    /// executable compiles the stylesheet once for each entry point it is started at (docs/plan/api.md decision 2).
    /// </summary>
    public sealed class XsltExecutable
    {
        private readonly Func<XmlReader> _open;
        private readonly XmlResolver _resolver;
        private readonly XsltSettings _settings;
        private readonly Dictionary<XmlQualifiedName, IList<XPathItem>> _staticParameters;
        private readonly (string, string, Uri)[] _packages;
        private readonly ConcurrentDictionary<string, Lazy<XslCompiledTransform>> _units = new(StringComparer.Ordinal);

        internal XsltExecutable(Func<XmlReader> open, XmlResolver resolver, XsltSettings settings, Dictionary<XmlQualifiedName, IList<XPathItem>> staticParameters,
            (string, string, Uri)[] packages)
        {
            _open = open;
            _resolver = resolver;
            _settings = settings;
            _staticParameters = staticParameters;
            _packages = packages;
        }

        internal XsltSettings Settings => _settings;

        /// <summary>Creates a transformer: cheap, for one thread at a time.</summary>
        public Xslt30Transformer CreateTransformer() => new(this);

        internal XslCompiledTransform Principal() => Unit("principal", _ => { }, raw: false);

        /// <summary>The unit compiled for an entry point, keyed by <paramref name="key"/>; compiled the first time it's asked for.</summary>
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Executables are made only by XsltCompiler.Compile, which requires dynamic code.")]
        internal XslCompiledTransform Unit(string key, Action<XslCompiledTransform> entryPoint, bool raw)
        {
            Lazy<XslCompiledTransform> unit = _units.GetOrAdd(key + (raw ? "|raw" : ""), _ => new Lazy<XslCompiledTransform>(() =>
            {
                var transform = new XslCompiledTransform
                {
                    StaticParameters = _staticParameters,
                    Packages = _packages,
                    RawResult = raw,
                };
                entryPoint(transform);
                try
                {
                    using XmlReader reader = _open();
                    transform.Load(reader, _settings, _resolver);
                }
                catch (XsltException e)
                {
                    var errors = new List<XsltStaticError>();
                    foreach (CompilerError error in transform.CompilerErrors ?? new CompilerErrorCollection())
                    {
                        errors.Add(new XsltStaticError(error));
                    }
                    throw new XsltStaticException(errors, e);
                }
                catch (Exception e) when (XsltDynamicException.From(e) is XsltDynamicException d)
                {
                    // An error evaluating a static expression (use-when, a static variable).
                    throw new XsltStaticException(Array.Empty<XsltStaticError>(), d);
                }
                return transform;
            }));
            try
            {
                return unit.Value;
            }
            catch
            {
                _units.TryRemove(new KeyValuePair<string, Lazy<XslCompiledTransform>>(key + (raw ? "|raw" : ""), unit));
                throw;
            }
        }

        /// <summary>The output properties of the stylesheet's unnamed output definition (<c>xsl:output</c>).</summary>
        public XmlWriterSettings? OutputSettings => Principal().OutputSettings;
    }

    /// <summary>
    /// Runs an <see cref="XsltExecutable"/>: applies templates, calls a named template or calls a stylesheet function
    /// (XSLT 3.0 §2.3), with stylesheet parameters, a global context item and handlers for messages, secondary results
    /// and trace output. A transformer is cheap to create and is for one thread at a time; it can run many times.
    /// Dynamic errors are raised as <see cref="XsltDynamicException"/>.
    /// </summary>
    public sealed class Xslt30Transformer
    {
        private readonly XsltExecutable _executable;

        internal Xslt30Transformer(XsltExecutable executable)
        {
            _executable = executable;
        }

        /// <summary>Values of the stylesheet parameters (global <c>xsl:param</c>s), by name.</summary>
        public IDictionary<QName, XdmValue> StylesheetParameters { get; } = new Dictionary<QName, XdmValue>();

        /// <summary>Sets <see cref="StylesheetParameters"/> to <paramref name="parameters"/>.</summary>
        public void SetStylesheetParameters(IDictionary<QName, XdmValue> parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);
            StylesheetParameters.Clear();
            foreach (var (name, value) in parameters)
            {
                StylesheetParameters[name] = value;
            }
        }

        /// <summary>
        /// The global context item (XSLT 3.0 §5.3.3): the context item of global variables, and of a named template or
        /// function the run starts with. Null: absent, unless templates are applied to a single node, which is then it.
        /// </summary>
        public XdmNode? GlobalContextItem { get; set; }

        /// <summary>
        /// The base output URI (XSLT 3.0 §2.3.6), for this transformer's runs: what <c>xsl:result-document</c> resolves
        /// relative hrefs against. With <see cref="XsltSettings.EnableResultDocument"/> and no
        /// <see cref="ResultDocumentHandler"/>, secondary results are files confined to its directory.
        /// </summary>
        public Uri? BaseOutputUri
        {
            get => _baseOutputUri;
            set => _baseOutputUri = value is { IsAbsoluteUri: false } ? throw new ArgumentException("The base output URI must be absolute.", nameof(value)) : value;
        }
        private Uri? _baseOutputUri;

        /// <summary>Resolves the documents <c>doc()</c> and <c>document()</c> load, when the settings allow them. Null: files and web URIs are read.</summary>
        public XmlResolver? DocumentResolver { get; set; }

        /// <summary>Stops a run when cancelled (the error is <c>Q{http://xsl3/err}cancelled</c>).</summary>
        public System.Threading.CancellationToken CancellationToken { get; set; }

        /// <summary>Receives the text of each <c>xsl:message</c>.</summary>
        public Action<string>? MessageHandler { get; set; }

        /// <summary>Receives <c>fn:trace</c> output: the value's text and the label.</summary>
        public Action<string, string>? TraceHandler { get; set; }

        /// <summary>
        /// Receives each secondary result (<c>xsl:result-document</c>): given its absolute URI, returns the writer it is
        /// serialized to, which Xsl3 disposes when the result is complete. Set, it replaces writing files.
        /// </summary>
        public Func<Uri, TextWriter>? ResultDocumentHandler { get; set; }

        // ------------------------------------------------------------ apply-templates

        /// <summary>
        /// Applies templates to <paramref name="selection"/> in <paramref name="mode"/> (the default mode if null) and
        /// returns the result sequence (raw delivery, XSLT 3.0 §2.3.6).
        /// </summary>
        public XdmValue ApplyTemplates(XdmValue selection, QName? mode = null, IDictionary<QName, XdmValue>? parameters = null, IDictionary<QName, XdmValue>? tunnelParameters = null)
        {
            ArgumentNullException.ThrowIfNull(selection);
            return Raw(PrepareApply(selection, mode, parameters, tunnelParameters, raw: true));
        }

        /// <summary>
        /// Applies templates to <paramref name="selection"/> in <paramref name="mode"/> and serializes the result to
        /// <paramref name="destination"/> as <c>xsl:output</c> says.
        /// </summary>
        public void ApplyTemplates(XdmValue selection, Stream destination, QName? mode = null, IDictionary<QName, XdmValue>? parameters = null, IDictionary<QName, XdmValue>? tunnelParameters = null) =>
            Serialize(PrepareApply(selection, mode, parameters, tunnelParameters, raw: false), destination);

        /// <inheritdoc cref="ApplyTemplates(XdmValue, Stream, QName?, IDictionary{QName, XdmValue}?, IDictionary{QName, XdmValue}?)"/>
        public void ApplyTemplates(XdmValue selection, TextWriter destination, QName? mode = null, IDictionary<QName, XdmValue>? parameters = null, IDictionary<QName, XdmValue>? tunnelParameters = null) =>
            Serialize(PrepareApply(selection, mode, parameters, tunnelParameters, raw: false), destination);

        /// <inheritdoc cref="ApplyTemplates(XdmValue, Stream, QName?, IDictionary{QName, XdmValue}?, IDictionary{QName, XdmValue}?)"/>
        public void ApplyTemplates(XdmValue selection, XmlWriter destination, QName? mode = null, IDictionary<QName, XdmValue>? parameters = null, IDictionary<QName, XdmValue>? tunnelParameters = null) =>
            Serialize(PrepareApply(selection, mode, parameters, tunnelParameters, raw: false), destination);

        private Run PrepareApply(XdmValue selection, QName? mode, IDictionary<QName, XdmValue>? parameters, IDictionary<QName, XdmValue>? tunnelParameters, bool raw)
        {
            ArgumentNullException.ThrowIfNull(selection);
            var (initialParameters, arguments) = InitialParameters(parameters, tunnelParameters);
            // A single node is the input, as for XslCompiledTransform; any other selection is passed as a parameter.
            bool byParameter = !(selection is XdmNode node && (GlobalContextItem == null || GlobalContextItem.Navigator.IsSamePosition(node.Navigator)));
            string key = $"apply|{mode?.EQName ?? "#default"}|{byParameter}|{Signature(initialParameters)}";
            XslCompiledTransform unit = _executable.Unit(key, t =>
            {
                t.InitialMode = mode == null ? null : new XmlQualifiedName(mode.LocalName, mode.NamespaceUri);
                t.InitialMatchSelection = byParameter;
                t.InitialParameters = initialParameters;
            }, raw);
            if (byParameter)
            {
                arguments.Add((InitialFunctionArguments.Selection, selection.Items));
            }
            XPathNavigator input = byParameter ? GlobalContextItem?.Navigator ?? XslCompiledTransform.AbsentInput : ((XdmNode)selection).Navigator;
            return new Run(unit, input, arguments);
        }

        // ------------------------------------------------------------ call-template

        /// <summary>
        /// Calls the named template <paramref name="name"/> (<c>xsl:initial-template</c> if null) and returns the result
        /// sequence. Its context item is the <see cref="GlobalContextItem"/>, if set.
        /// </summary>
        public XdmValue CallTemplate(QName? name, IDictionary<QName, XdmValue>? parameters = null, IDictionary<QName, XdmValue>? tunnelParameters = null) =>
            Raw(PrepareCall(name, parameters, tunnelParameters, raw: true));

        /// <summary>Calls the named template <paramref name="name"/> and serializes the result to <paramref name="destination"/>.</summary>
        public void CallTemplate(QName? name, Stream destination, IDictionary<QName, XdmValue>? parameters = null, IDictionary<QName, XdmValue>? tunnelParameters = null) =>
            Serialize(PrepareCall(name, parameters, tunnelParameters, raw: false), destination);

        /// <inheritdoc cref="CallTemplate(QName?, Stream, IDictionary{QName, XdmValue}?, IDictionary{QName, XdmValue}?)"/>
        public void CallTemplate(QName? name, TextWriter destination, IDictionary<QName, XdmValue>? parameters = null, IDictionary<QName, XdmValue>? tunnelParameters = null) =>
            Serialize(PrepareCall(name, parameters, tunnelParameters, raw: false), destination);

        /// <inheritdoc cref="CallTemplate(QName?, Stream, IDictionary{QName, XdmValue}?, IDictionary{QName, XdmValue}?)"/>
        public void CallTemplate(QName? name, XmlWriter destination, IDictionary<QName, XdmValue>? parameters = null, IDictionary<QName, XdmValue>? tunnelParameters = null) =>
            Serialize(PrepareCall(name, parameters, tunnelParameters, raw: false), destination);

        private Run PrepareCall(QName? name, IDictionary<QName, XdmValue>? parameters, IDictionary<QName, XdmValue>? tunnelParameters, bool raw)
        {
            name ??= new QName("http://www.w3.org/1999/XSL/Transform", "initial-template", "xsl");
            var (initialParameters, arguments) = InitialParameters(parameters, tunnelParameters);
            XslCompiledTransform unit = _executable.Unit($"call|{name.EQName}|{Signature(initialParameters)}", t =>
            {
                t.InitialTemplate = new XmlQualifiedName(name.LocalName, name.NamespaceUri);
                t.InitialParameters = initialParameters;
            }, raw);
            return new Run(unit, GlobalContextItem?.Navigator ?? XslCompiledTransform.AbsentInput, arguments);
        }

        // ------------------------------------------------------------ function call

        /// <summary>Calls the public stylesheet function <paramref name="name"/> with <paramref name="arguments"/> and returns its result.</summary>
        public XdmValue CallFunction(QName name, params XdmValue[] arguments)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(arguments);
            XslCompiledTransform unit = _executable.Unit($"function|{name.EQName}#{arguments.Length}", t =>
            {
                t.InitialFunction = new XmlQualifiedName(name.LocalName, name.NamespaceUri);
                t.InitialFunctionArity = arguments.Length;
            }, raw: true);
            var values = new List<(XmlQualifiedName, IList<XPathItem>)>();
            for (int i = 0; i < arguments.Length; i++)
            {
                values.Add((InitialFunctionArguments.Name(i + 1), (arguments[i] ?? XdmValue.Empty).Items));
            }
            return Raw(new Run(unit, GlobalContextItem?.Navigator ?? XslCompiledTransform.AbsentInput, values));
        }

        // ------------------------------------------------------------ running

        private sealed record Run(XslCompiledTransform Unit, XPathNavigator Input, List<(XmlQualifiedName Name, IList<XPathItem> Value)> Values);

        private static (List<(XmlQualifiedName, bool)>? Names, List<(XmlQualifiedName, IList<XPathItem>)> Values) InitialParameters(
            IDictionary<QName, XdmValue>? parameters, IDictionary<QName, XdmValue>? tunnelParameters)
        {
            var values = new List<(XmlQualifiedName, IList<XPathItem>)>();
            var names = new List<(XmlQualifiedName, bool)>();
            foreach (var (tunnel, set) in new[] { (false, parameters), (true, tunnelParameters) })
            {
                // In a fixed order, so the same names use the same compiled unit.
                foreach (var (name, value) in set?.OrderBy(p => p.Key.EQName, StringComparer.Ordinal) ?? Enumerable.Empty<KeyValuePair<QName, XdmValue>>())
                {
                    names.Add((new XmlQualifiedName(name.LocalName, name.NamespaceUri), tunnel));
                    values.Add((XslCompiledTransform.InitialParameter(names.Count), (value ?? XdmValue.Empty).Items));
                }
            }
            return (names.Count > 0 ? names : null, values);
        }

        private static string Signature(List<(XmlQualifiedName Name, bool Tunnel)>? parameters) =>
            parameters == null ? "" : string.Join(" ", parameters.Select(p => (p.Tunnel ? "tunnel:" : "") + "Q{" + p.Name.Namespace + "}" + p.Name.Name));

        private XsltArgumentList Arguments(Run run)
        {
            var arguments = new XsltArgumentList { CancellationToken = CancellationToken, TraceHandler = TraceHandler };
            foreach (var (name, value) in StylesheetParameters)
            {
                arguments.AddParam(name.LocalName, name.NamespaceUri, (value ?? XdmValue.Empty).Items);
            }
            foreach (var (name, value) in run.Values)
            {
                arguments.AddParam(name.Name, name.Namespace, value);
            }
            if (MessageHandler is Action<string> handler)
            {
                arguments.XsltMessageEncountered += (_, e) => handler(e.Message);
            }
            return arguments;
        }

        private Func<string, string, TextWriter>? Handler() =>
            ResultDocumentHandler is Func<Uri, TextWriter> handler ? (uri, _) => handler(new Uri(uri, UriKind.RelativeOrAbsolute)) : null;

        private XdmValue Raw(Run run)
        {
            try
            {
                return XdmValue.Wrap(run.Unit.TransformToItems(run.Input, Arguments(run), DocumentResolver ?? new XmlUrlResolver(), BaseOutputUri, Handler()));
            }
            catch (Exception e) when (XsltDynamicException.From(e) is XsltDynamicException d && !ReferenceEquals(d, e))
            {
                throw d;
            }
        }

        private void Serialize(Run run, object destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            try
            {
                run.Unit.TransformTo(run.Input, Arguments(run), destination, DocumentResolver ?? new XmlUrlResolver(), BaseOutputUri, Handler());
            }
            catch (Exception e) when (XsltDynamicException.From(e) is XsltDynamicException d && !ReferenceEquals(d, e))
            {
                throw d;
            }
        }
    }
}
