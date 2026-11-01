// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.IlGen;
using Xsl3.Xslt;
using MS.Internal.Xml.XPath;

namespace Xsl3.Runtime
{
    /// <summary>
    /// XmlQueryRuntime is passed as the first parameter to all generated query methods.
    ///
    /// XmlQueryRuntime contains runtime support for generated ILGen queries:
    ///   1. Stack of output writers (stack handles nested document construction)
    ///   2. Manages list of all xml types that are used within the query
    ///   3. Manages list of all atomized names that are used within the query
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed partial class XmlQueryRuntime
    {
        private List<Streaming.StreamedDocument>? _streamedSources;

        internal void RegisterStreamedSource(Streaming.StreamedDocument document) => (_streamedSources ??= new()).Add(document);

        internal void CloseStreamedSource(Streaming.StreamedDocument document)
        {
            _streamedSources?.Remove(document);
            document.Dispose();
        }

        internal int StreamedSourceMark => _streamedSources?.Count ?? 0;

        internal bool CanCatchStreamedValidation(Exception error, int sourceMark)
        {
            for (Exception? current = error; current != null; current = current.InnerException)
                if (current is Xdm.XdmException { ValidationSource: { } source } && _streamedSources != null)
                {
                    int index = _streamedSources.IndexOf(source);
                    if (index >= 0 && index < sourceMark) return false;
                }
            return true;
        }

        internal void CloseStreamedSources(int mark = 0)
        {
            if (_streamedSources == null) return;
            // Close every source, including when a provider's Close throws. The execution finally block
            // preserves the primary transformation error; normal source exits report Close errors directly.
            while (_streamedSources.Count > mark)
            {
                int index = _streamedSources.Count - 1;
                Streaming.StreamedDocument document = _streamedSources[index];
                _streamedSources.RemoveAt(index);
                try { document.Dispose(); }
                catch (Exception e) when (XmlException.IsCatchableException(e)) { }
            }
        }

        // Xsl3: cancellation of a running transformation (SPEC §21.1). Checked, with the stack, at every template and
        // function entry; set for the current thread by the API that runs the transformation.
        [System.ThreadStatic]
        private static System.Threading.CancellationToken t_cancellation;

        internal static System.Threading.CancellationToken Cancellation
        {
            get => t_cancellation;
            set => t_cancellation = value;
        }

        // Xsl3: opens the file of a secondary result when XsltSettings.EnableResultDocument is set (its URI already
        // checked to be under the base output URI); the result is written in its own encoding.
        [System.ThreadStatic]
        private static System.Func<string, System.IO.Stream>? t_resultDocumentOpener;

        internal static System.Func<string, System.IO.Stream>? ResultDocumentOpener
        {
            get => t_resultDocumentOpener;
            set => t_resultDocumentOpener = value;
        }

        // Xsl3: XsltSettings.EnableEnvironmentVariables for the running transformation (fn:environment-variable).
        [System.ThreadStatic]
        private static bool t_environmentVariables;

        internal static bool EnvironmentVariables
        {
            get => t_environmentVariables;
            set => t_environmentVariables = value;
        }

        // Xsl3: XsltSettings.Timeout of the running transformation: the match timeout of its regular expressions, which
        // the checks at template and function entry don't reach (a match that runs this long ends the run as cancelled).
        [System.ThreadStatic]
        private static System.TimeSpan? t_regexTimeout;

        internal static System.TimeSpan? RegexTimeout
        {
            get => t_regexTimeout;
            set => t_regexTimeout = value;
        }

        // Xsl3: the package library of the running transformation (XslCompiledTransform.Packages), for fn:transform.
        [System.ThreadStatic]
        private static System.Collections.Generic.IReadOnlyList<(string Name, string Version, System.Uri Location)>? t_packages;

        internal static System.Collections.Generic.IReadOnlyList<(string Name, string Version, System.Uri Location)>? Packages
        {
            get => t_packages;
            set => t_packages = value;
        }

        // Xsl3: XsltSettings.EnableEvaluate for the running (or loading) transformation, which allows fn:transform;
        // null outside one, where fn:transform is refused.
        [System.ThreadStatic]
        private static bool? t_dynamicTransforms;

        internal static bool? DynamicTransforms
        {
            get => t_dynamicTransforms;
            set => t_dynamicTransforms = value;
        }

        // Xsl3: XsltSettings.EnableExtensionFunctions for the running transformation; on outside one.
        [System.ThreadStatic]
        private static bool t_noExtensionFunctions;

        internal static bool ExtensionFunctions
        {
            get => !t_noExtensionFunctions;
            set => t_noExtensionFunctions = !value;
        }

        // Xsl3: the seed of fn:random-number-generator() without an argument: one per transformation.
        private ulong? _randomSeed;
        internal ulong RandomSeed => _randomSeed ??= (ulong)System.Random.Shared.NextInt64();

        // Xsl3: XsltSettings.EnableDocumentFunction for the running transformation (fn:doc, fn:unparsed-text and the
        // like, SPEC §21.1); null outside a transformation (XPath evaluated directly, with its own resolver).
        [System.ThreadStatic]
        private static bool? t_documentFunctions;

        internal static bool? DocumentFunctions
        {
            get => t_documentFunctions;
            set => t_documentFunctions = value;
        }

        // Xsl3: where xsl:result-document writes secondary results (SPEC §21.1, EnableResultDocument): given the
        // absolute URI and the output method, a writer for the serialization. Null: secondary results are refused.
        [System.ThreadStatic]
        private static System.Func<string, string, System.IO.TextWriter>? t_resultDocuments;

        internal static System.Func<string, string, System.IO.TextWriter>? ResultDocumentHandler
        {
            get => t_resultDocuments;
            set => t_resultDocuments = value;
        }

        // Xsl3: given each secondary result's tree (with its type annotations) as it is written, by its absolute URI;
        // for the W3C test runner's assertions on typed result documents (si-result-document-116). Not public.
        [System.ThreadStatic]
        private static System.Action<string, XPathNavigator>? t_resultTrees;

        internal static System.Action<string, XPathNavigator>? ResultTreeHandler
        {
            get => t_resultTrees;
            set => t_resultTrees = value;
        }

        // Xsl3: the base output URI (XSLT 3.0 §2.3.6): what secondary results' href resolves against and what
        // current-output-uri() returns for the principal result. Null: absent.
        [System.ThreadStatic]
        private static string? t_baseOutputUri;

        internal static string? BaseOutputUri
        {
            get => t_baseOutputUri;
            set => t_baseOutputUri = value;
        }

        // Xsl3: the compiled dispatcher of function items, for the run-time library.
        internal System.Func<XmlQueryRuntime, XPathItem, IList<XPathItem>>? Dispatcher { get; }

        // Xsl3: compiled by the XSLT 2.0+ path.
        internal bool IsXslt2 { get; }
        internal bool StreamedInitialMode { get; }

        /// <summary>Xsl3 (P6): default-validation="preserve": nodes copied into constructed trees keep their type annotations.</summary>
        internal bool PreserveAnnotations
        {
            get => AnnotationScopes is { Count: > 0 } scopes ? scopes.Peek() : _preserveAnnotations;
            set => _preserveAnnotations = value;
        }

        private bool _preserveAnnotations;

        /// <summary>Xsl3 (P6): the element a constructor with validation="preserve" starts next is annotated xs:anyType.</summary>
        internal bool PendingAnyType { get; set; }

        /// <summary>Xsl3 (P6): the constructors being evaluated that preserve (true) or strip (false) type annotations.</summary>
        internal Stack<bool>? AnnotationScopes { get; set; }

        internal Stack<bool>? CopyAnnotationScopes()
        {
            if (AnnotationScopes == null) return null;
            bool[] saved = AnnotationScopes.ToArray(); Array.Reverse(saved);
            return new Stack<bool>(saved);
        }

        /// <summary>Xsl3 (P6): the stylesheet was compiled by a schema-aware processor (XsltSettings.SchemaAware).</summary>
        internal bool SchemaAware { get; set; }

        // Xsl3: the constants compiled code refers to by number (ConstantPool); null for the process-wide table.
        internal Xsl3.XPath31.ConstantTable? Constants { get; }

        /// <summary>Xsl3 (P4): documents the transform parses become compact trees (always for 2.0+).</summary>
        internal bool UseCompactTree { get; }

        // Xsl3: the principal result of the running XSLT 2.0+ transformation, for xsl:result-document without href.
        [System.ThreadStatic]
        private static PrincipalResult? t_principal;

        internal static PrincipalResult? Principal
        {
            get => t_principal;
            set => t_principal = value;
        }

        internal DocumentResources Documents { get; private set; }
        internal IReadOnlySet<long>? DocumentDependencies { get; private set; }
        internal IReadOnlySet<EvaluationWaitGraph.Node>? EvaluationDependencies { get; private set; }

        // Attach before evaluating the new runtime. Source/output stacks and execution policies
        // remain independent responsibilities of the fork runtime constructor.
        internal void ShareDocuments(XmlQueryRuntime owner)
        {
            if (!ReferenceEquals(_ctxt.DataSources, owner._ctxt.DataSources))
                throw new InvalidOperationException("Shared documents must use the same resolver");
            if (!Documents.IsUnused)
                throw new InvalidOperationException("A runtime must share documents before using resources or document IDs");
            UseSharedDocuments(owner);
        }

        private void UseSharedDocuments(XmlQueryRuntime owner)
        {
            owner.Documents.Share(Cancellation);
            DocumentDependencies = owner.Documents.CaptureDependencies(owner.DocumentDependencies);
            Documents = owner.Documents;
            EvaluationDependencies = Documents.Evaluations.Capture(owner.EvaluationDependencies);
        }

        internal static void CheckCancellation() => t_cancellation.ThrowIfCancellationRequested();

        /// <summary>Called by generated code on entry to every template and function.</summary>
        public static void EnterFunction()
        {
            System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
            t_cancellation.ThrowIfCancellationRequested();
        }

        /// <summary>Xsl3: the templates and functions running, innermost last (XsltSettings.EnableTracing).</summary>
        internal readonly System.Collections.Generic.List<XsltFrame> TraceStack = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Exception, object> s_stackGates = new();

        /// <summary>Xsl3: records the stylesheet stack on an exception leaving the transformation; never handles it.</summary>
        internal bool AttachStylesheetStack(System.Exception e)
        {
            // Shared globals publish the original exception to every prong. Exception.Data
            // is not a concurrent dictionary; coordinate annotations without retaining errors.
            lock (s_stackGates.GetValue(e, static _ => new object()))
            {
                if (e.Data.Contains(XsltDynamicException.StackKey))
                {
                    return false;
                }
                if (TraceStack.Count > 0)
                {
                    var frames = TraceStack.ToArray();
                    System.Array.Reverse(frames);
                    e.Data[XsltDynamicException.StackKey] = frames;
                }
                else if (_frames != null && StackOfMethods(e) is { Length: > 0 } frames)
                {
                    e.Data[XsltDynamicException.StackKey] = frames;
                }
                return false;
            }
        }

        private readonly System.Collections.Generic.Dictionary<string, int>? _frames;

        /// <summary>
        /// Xsl3 (4.5): without tracing, the stylesheet stack read from the .NET one: the templates and functions by the
        /// names of their methods. Frames the JIT inlined or a tail call replaced are missing.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "A frame whose method metadata was trimmed has no method: the stack then leaves it out, as it does inlined frames.")]
        private XsltFrame[] StackOfMethods(System.Exception e)
        {
            var frames = new System.Collections.Generic.List<XsltFrame>();
            foreach (System.Diagnostics.StackFrame frame in new System.Diagnostics.StackTrace(e, false).GetFrames())
            {
                if (frame.GetMethod()?.Name is string name && _frames!.TryGetValue(name, out int id))
                {
                    frames.Add((XsltFrame)Xsl3.XPath31.ConstantPool.Get(Constants, id));
                }
            }
            return frames.ToArray();
        }

        /// <summary>Xsl3: the lines of the instructions in xsl:try bodies being evaluated, innermost last ($err:line-number).</summary>
        internal readonly System.Collections.Generic.List<int> TryLines = new();

        // Xsl3: the nesting of templates and functions, counted when XsltSettings.MaxRecursionDepth is set (SPEC §21.2).
        [System.ThreadStatic]
        private static int t_depth;

        internal static int Depth
        {
            get => t_depth;
            set => t_depth = value;
        }

        /// <summary>On entry, with MaxRecursionDepth: one level deeper, at most <paramref name="max"/>.</summary>
        public static void EnterFunctionCounted(int max)
        {
            EnterFunction();
            if (++t_depth > max)
            {
                t_depth--;
                throw new System.InsufficientExecutionStackException($"The transformation nested templates and functions more than {max} deep (XsltSettings.MaxRecursionDepth)");
            }
        }

        /// <summary>On return (or before a tail call), with MaxRecursionDepth.</summary>
        public static void ExitFunctionCounted() => t_depth--;

        // Early-Bound Library Objects
        private readonly XmlQueryContext _ctxt;
        private XsltLibrary? _xsltLib;
        private readonly EarlyBoundInfo[]? _earlyInfo;
        private readonly object[]? _earlyObjects;
        private object? _earlyObjectGate;
        private readonly XmlQueryStaticData _staticData;

        // Global variables and parameters
        private readonly string[]? _globalNames;
        private readonly object[]? _globalValues;
        private readonly bool[]? _globalInProgress; // Xsl3: globals being computed, to detect circular definitions
        private ExceptionDispatchInfo?[]? _globalFailures;
        private SharedGlobalBindings? _sharedGlobals;
        private HashSet<int>? _globalAncestors;

        // Called on the invoking thread before prongs start. Other runtime state still needs its
        // own isolation/coordination; sharing globals alone does not make a runtime a fork view.
        internal void ShareGlobalBindings(XmlQueryRuntime owner)
        {
            if (!ReferenceEquals(_globalNames, owner._globalNames))
                throw new InvalidOperationException("Shared global bindings must belong to the same compiled stylesheet");
            if (_sharedGlobals != null || _globalFailures != null || _globalValues != null && Array.Exists(_globalValues, static value => value != null)
                || _globalInProgress != null && Array.Exists(_globalInProgress, static active => active))
                throw new InvalidOperationException("A runtime must share globals before evaluating its own bindings");
            owner._sharedGlobals ??= new SharedGlobalBindings(owner._globalNames ?? Array.Empty<string>(),
                owner._globalValues ?? Array.Empty<object>(), owner._globalInProgress ?? Array.Empty<bool>(), owner._globalFailures, Cancellation, owner.Documents.Evaluations);
            _sharedGlobals = owner._sharedGlobals;
            _globalAncestors = _sharedGlobals.CaptureDependencies(owner._globalAncestors);
            EvaluationDependencies = owner.Documents.Evaluations.Capture(owner.EvaluationDependencies);
        }

        // Names, prefix mappings, and name filters
        private readonly XmlNameTable _nameTableQuery;
        private readonly string[]? _atomizedNames;             // Names after atomization
        private readonly XmlNavigatorFilter[]? _filters;       // Name filters (contain atomized names)
        private readonly StringPair[][]? _prefixMappingsList;  // Lists of prefix mappings (used to resolve computed names)

        // Xml types
        private readonly XmlQueryType[]? _types;

        // Collations
        private readonly XmlCollation[]? _collations;

        // Indexes
        private ArrayList[]? _indexes;

        // Output construction
        private XmlQueryOutput _output;
        private readonly Stack<XmlQueryOutput> _stkOutput;

        // Xsl3: the output state before xsl:try's body; an error leaves the constructions it was in the middle of
        // pushed on the stack, and the catch restores this.
        // Xsl3: the state an error caught by xsl:try unwinds to: the output stack and the call depth.
        internal (int Depth, XmlQueryOutput Output, int Calls) SaveOutput() => (_stkOutput.Count, _output, t_depth);

        internal void RestoreOutput((int Depth, XmlQueryOutput Output, int Calls) saved)
        {
            while (_stkOutput.Count > saved.Depth)
            {
                _stkOutput.Pop();
            }
            _output = saved.Output;
            t_depth = saved.Calls;
        }


        // Xsl3: a secondary result has its own output, but shares the invocation's resources and
        // function dispatcher. Restore nested constructions as well when its body throws.
        internal void WithOutput(XmlSequenceWriter writer, Action body)
        {
            var saved = SaveOutput();
            _stkOutput.Push(_output);
            _output = new XmlQueryOutput(this, writer);
            try { body(); }
            finally { RestoreOutput(saved); }
        }

        //-----------------------------------------------
        // Constructors
        //-----------------------------------------------

        /// <summary>
        /// This constructor is internal so that external users cannot construct it (and therefore we do not have to test it separately).
        /// </summary>
        internal XmlQueryRuntime(XmlQueryStaticData data, object defaultDataSource, XmlResolver dataSources, XsltArgumentList? argList, XmlSequenceWriter seqWrt,
            XmlQueryRuntime? resourceOwner = null)
            : this(data, defaultDataSource, dataSources, argList, seqWrt, resourceOwner, forkOwner: null)
        {
        }

        private XmlQueryRuntime(XmlQueryStaticData data, object defaultDataSource, XmlResolver dataSources, XsltArgumentList? argList, XmlSequenceWriter seqWrt,
            XmlQueryRuntime? resourceOwner, XmlQueryRuntime? forkOwner)
        {
            Debug.Assert(data != null);
            _staticData = data;
            Documents = resourceOwner?.Documents ?? new();
            if (resourceOwner != null)
            {
                if (!ReferenceEquals(dataSources, resourceOwner._ctxt.DataSources))
                    throw new InvalidOperationException("Shared documents must use the same resolver");
                UseSharedDocuments(resourceOwner);
            }
            Dispatcher = data.Dispatcher;
            IsXslt2 = data.Xslt2;
            StreamedInitialMode = data.StreamedInitialMode;
            UseCompactTree = data.CompactTree;
            Constants = data.Constants;
            _frames = data.Frames;
            string[]? names = data.Names;
            Int32Pair[]? filters = data.Filters;
            WhitespaceRuleLookup? wsRules;
            int i;

            // Early-Bound Library Objects
            wsRules = (data.WhitespaceRules != null && data.WhitespaceRules.Count != 0) ? new WhitespaceRuleLookup(data.WhitespaceRules) : null;
            _ctxt = forkOwner == null ? new XmlQueryContext(this, defaultDataSource, dataSources, argList, wsRules)
                : new XmlQueryContext(this, forkOwner._ctxt);
            _xsltLib = null;
            _earlyInfo = data.EarlyBound;
            _earlyObjects = forkOwner?._earlyObjects ?? ((_earlyInfo != null) ? new object[_earlyInfo.Length] : null);
            if (forkOwner != null && _earlyObjects != null)
                _earlyObjectGate = forkOwner._earlyObjectGate ??= new object();

            // Global variables and parameters
            _globalNames = data.GlobalNames;
            _globalValues = (_globalNames != null) ? new object[_globalNames.Length] : null;
            _globalInProgress = (_globalNames != null) ? new bool[_globalNames.Length] : null;

            // Names
            _nameTableQuery = _ctxt.QueryNameTable;
            _atomizedNames = null;

            if (names != null)
            {
                // Atomize all names in "nameTableQuery".  Use names from the default data source's
                // name table when possible.
                XmlNameTable? nameTableDefault = _ctxt.DefaultNameTable;
                _atomizedNames = new string[names.Length];

                if (nameTableDefault != _nameTableQuery && nameTableDefault != null)
                {
                    // Ensure that atomized names from the default data source are added to the
                    // name table used in this query
                    for (i = 0; i < names.Length; i++)
                    {
                        string? name = nameTableDefault.Get(names[i]);
                        _atomizedNames[i] = _nameTableQuery.Add(name ?? names[i]);
                    }
                }
                else
                {
                    // Enter names into nametable used in this query
                    for (i = 0; i < names.Length; i++)
                        _atomizedNames[i] = _nameTableQuery.Add(names[i]);
                }
            }

            // Name filters
            _filters = null;
            if (filters != null)
            {
                // Construct name filters.  Each pair of integers in the filters[] array specifies the
                // (localName, namespaceUri) of the NameFilter to be created.
                _filters = new XmlNavigatorFilter[filters.Length];

                for (i = 0; i < filters.Length; i++)
                    _filters[i] = XmlNavNameFilter.Create(_atomizedNames![filters[i].Left], _atomizedNames[filters[i].Right]);
            }

            // Prefix maping lists
            _prefixMappingsList = data.PrefixMappingsList;

            // Xml types
            _types = data.Types;

            // Xml collations
            _collations = data.Collations;

            // Indexes
            _indexes = null;

            // Output construction
            _stkOutput = new Stack<XmlQueryOutput>(16);
            _output = new XmlQueryOutput(this, seqWrt);
        }


        //-----------------------------------------------
        // Debugger Utility Methods
        //-----------------------------------------------

        /// <summary>
        /// Return array containing the names of all the global variables and parameters used in this query, in this format:
        ///     {namespace}prefix:local-name
        /// </summary>
        public string[]? DebugGetGlobalNames()
        {
            return _globalNames;
        }

        /// <summary>
        /// Get the value of a global value having the specified name.  Always return the global value as a list of XPathItem.
        /// Return null if there is no global value having the specified name.
        /// </summary>
        public IList? DebugGetGlobalValue(string name)
        {
            for (int idx = 0; idx < _globalNames!.Length; idx++)
            {
                if (_globalNames[idx] == name)
                {
                    Debug.Assert(IsGlobalComputed(idx), "Cannot get the value of a global value until it has been computed.");
                    Debug.Assert(GetGlobalValue(idx) is IList<XPathItem>, "Only debugger should call this method, and all global values should have type item* in debugging scenarios.");
                    return (IList)GetGlobalValue(idx);
                }
            }
            return null;
        }

        /// <summary>
        /// Set the value of a global value having the specified name.  If there is no such value, this method is a no-op.
        /// </summary>
        public void DebugSetGlobalValue(string name, object value)
        {
            for (int idx = 0; idx < _globalNames!.Length; idx++)
            {
                if (_globalNames[idx] == name)
                {
                    Debug.Assert(IsGlobalComputed(idx), "Cannot get the value of a global value until it has been computed.");
                    Debug.Assert(GetGlobalValue(idx) is IList<XPathItem>, "Only debugger should call this method, and all global values should have type item* in debugging scenarios.");

                    // Always convert "value" to a list of XPathItem using the item* converter
                    // Xsl3: upstream uses System.Private.Xml's internal XmlAnyListConverter.ItemList. This method only
                    // serves debuggers, so only item values and sequences of items are accepted.
                    object converted = value switch
                    {
                        IList<XPathItem> items => items,
                        XPathItem item => new XmlQueryItemSequence(item),
                        IEnumerable<XPathItem> items => new List<XPathItem>(items),
                        _ => throw new NotSupportedException(),
                    };
                    if (_sharedGlobals != null) _sharedGlobals.SetDebugValue(idx, converted);
                    else _globalValues![idx] = converted;
                    break;
                }
            }
        }

        /// <summary>
        /// Convert sequence to it's appropriate XSLT type and return to caller.
        /// </summary>
        public object? DebugGetXsltValue(IList? seq)
        {
            if (seq != null && seq.Count == 1)
            {
                XPathItem? item = seq[0] as XPathItem;
                if (item != null && !item.IsNode)
                {
                    return item.TypedValue;
                }
                else if (item is RtfNavigator)
                {
                    return ((RtfNavigator)item).ToNavigator();
                }
            }

            return seq;
        }


        //-----------------------------------------------
        // Early-Bound Library Objects
        //-----------------------------------------------

        internal const BindingFlags EarlyBoundFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        internal const BindingFlags LateBoundFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        /// <summary>
        /// Return the object that manages external user context information such as data sources, parameters, extension objects, etc.
        /// </summary>
        public XmlQueryContext ExternalContext
        {
            get { return _ctxt; }
        }

        /// <summary>
        /// Return the object that manages the state needed to implement various Xslt functions.
        /// </summary>
        public XsltLibrary XsltFunctions => _xsltLib ??= new XsltLibrary(this);

        /// <summary>
        /// Get the early-bound extension object identified by "index".  If it does not yet exist, create an instance using the
        /// corresponding ConstructorInfo.
        /// </summary>
        public object GetEarlyBoundObject(int index)
        {
            if (_earlyObjectGate != null)
            {
                lock (_earlyObjectGate)
                    return _earlyObjects![index] ??= _earlyInfo![index].CreateObject();
            }
            object obj;
            Debug.Assert(_earlyObjects != null && index < _earlyObjects.Length, "Early bound object does not exist");

            obj = _earlyObjects[index];
            if (obj == null)
            {
                // Early-bound object does not yet exist, so create it now
                obj = _earlyInfo![index].CreateObject();
                _earlyObjects[index] = obj;
            }

            return obj;
        }

        /// <summary>
        /// Return true if the early bound object identified by "namespaceUri" contains a method that matches "name".
        /// </summary>
        [RequiresUnreferencedCode(Scripts.ExtensionFunctionCannotBeStaticallyAnalyzed)]
        public bool EarlyBoundFunctionExists(string name, string namespaceUri)
        {
            if (_earlyInfo == null)
                return false;

            for (int idx = 0; idx < _earlyInfo.Length; idx++)
            {
                if (namespaceUri == _earlyInfo[idx].NamespaceUri)
                    return new XmlExtensionFunction(name, namespaceUri, -1, _earlyInfo[idx].EarlyBoundType, EarlyBoundFlags).CanBind();
            }

            return false;
        }


        //-----------------------------------------------
        // Global variables and parameters
        //-----------------------------------------------

        /// <summary>
        /// Return true if the global value specified by idxValue was previously computed.
        /// </summary>
        public bool IsGlobalComputed(int index)
        {
            if (_sharedGlobals != null)
            {
                if (_sharedGlobals.IsComputed(index, _globalAncestors, Cancellation, EvaluationDependencies)) return true;
                // A following prong's cancellation must not poison a transformation-wide value
                // that an earlier prong still needs. Generated success/failure paths restore its token.
                Cancellation = _sharedGlobals.EvaluationCancellation;
                return false;
            }
            if (_globalValues![index] != null)
            {
                return true;
            }
            _globalFailures?[index]?.Throw();
            // Xsl3: the generated code computes the value after a false answer; asking again before it is
            // set means the value depends on itself (XTDE0640), which would otherwise recurse without end.
            if (_globalInProgress![index])
            {
                throw new XslTransformException("XTDE0640: circular definition of global variable or parameter " + _globalNames![index]);
            }
            _globalInProgress[index] = true;
            return false;
        }

        /// <summary>
        /// Return the value that is bound to the global variable or parameter specified by idxValue.
        /// If the value has not yet been computed, then compute it now and store it in this.globalValues.
        /// </summary>
        public object GetGlobalValue(int index)
        {
            Debug.Assert(IsGlobalComputed(index), "Cannot get the value of a global value until it has been computed.");
            return _sharedGlobals != null ? _sharedGlobals.Get(index) : _globalValues![index];
        }

        /// <summary>
        /// Return the value that is bound to the global variable or parameter specified by idxValue.
        /// If the value has not yet been computed, then compute it now and store it in this.globalValues.
        /// </summary>
        public void SetGlobalValue(int index, object value)
        {
            if (_sharedGlobals != null)
            {
                if (_sharedGlobals.Set(index, value) is { } previous) Cancellation = previous;
                return;
            }
            Debug.Assert(_globalValues![index] == null, "Global value should only be set once.");
            _globalValues![index] = value;
            _globalInProgress![index] = false;
        }

        /// <summary>Generated global bindings publish failures before unwinding to their caller.</summary>
        public void GlobalValueFailed(int index, Exception error)
        {
            // Annotation is best-effort, as in the execution catch filter. A user exception's
            // Data getter can throw; that must not replace the failure or leave a global claimed.
            RecordFailureStack(error);
            if (_sharedGlobals != null)
            {
                if (_sharedGlobals.Fail(index, error) is { } previous) Cancellation = previous;
                return;
            }
            (_globalFailures ??= new ExceptionDispatchInfo?[_globalNames!.Length])[index] ??= ExceptionDispatchInfo.Capture(error);
            _globalInProgress![index] = false;
        }

        internal void RecordFailureStack(Exception error)
        {
            try { AttachStylesheetStack(error); }
            catch (Exception annotationError) when (XmlException.IsCatchableException(annotationError)) { }
        }


        //-----------------------------------------------
        // Names, prefix mappings, and name filters
        //-----------------------------------------------

        /// <summary>
        /// Return the name table used to atomize all names used by the query.
        /// </summary>
        public XmlNameTable NameTable
        {
            get { return _nameTableQuery; }
        }

        /// <summary>
        /// Get the atomized name at the specified index in the array of names.
        /// </summary>
        public string GetAtomizedName(int index)
        {
            Debug.Assert(_atomizedNames != null);
            return _atomizedNames[index];
        }

        /// <summary>
        /// Get the name filter at the specified index in the array of filters.
        /// </summary>
        public XmlNavigatorFilter GetNameFilter(int index)
        {
            Debug.Assert(_filters != null);
            return _filters[index];
        }

        /// <summary>
        /// XPathNodeType.All: Filters all nodes
        /// XPathNodeType.Attribute: Filters attributes
        /// XPathNodeType.Namespace: Not allowed
        /// XPathNodeType.XXX: Filters all nodes *except* those having XPathNodeType.XXX
        /// </summary>
        public XmlNavigatorFilter GetTypeFilter(XPathNodeType nodeType)
        {
            if (nodeType == XPathNodeType.All)
                return XmlNavNeverFilter.Create();

            if (nodeType == XPathNodeType.Attribute)
                return XmlNavAttrFilter.Create();

            return XmlNavTypeFilter.Create(nodeType);
        }

        /// <summary>
        /// Parse the specified tag name (foo:bar) and resolve the resulting prefix.  If the prefix cannot be resolved,
        /// then throw an error.  Return an XmlQualifiedName.
        /// </summary>
        public XmlQualifiedName ParseTagName(string tagName, int indexPrefixMappings)
        {
            string localName;
            string? ns;

            // Parse the tagName as a prefix, localName pair and resolve the prefix
            ParseTagName(tagName, indexPrefixMappings, out _, out localName, out ns);
            return new XmlQualifiedName(localName, ns);
        }

        /// <summary>
        /// Parse the specified tag name (foo:bar).  Return an XmlQualifiedName consisting of the parsed local name
        /// and the specified namespace.
        /// </summary>
        public XmlQualifiedName ParseTagName(string tagName, string ns)
        {
            string localName;

            // Parse the tagName as a prefix, localName pair
            ValidateNames.ParseQNameThrow(tagName, out _, out localName);
            return new XmlQualifiedName(localName, ns);
        }

        /// <summary>
        /// Parse the specified tag name (foo:bar) and resolve the resulting prefix.  If the prefix cannot be resolved,
        /// then throw an error.  Return the prefix, localName, and namespace URI.
        /// </summary>
        internal void ParseTagName(string tagName, int idxPrefixMappings, out string prefix, out string localName, out string ns)
        {
            Debug.Assert(_prefixMappingsList != null);

            // Parse the tagName as a prefix, localName pair
            ValidateNames.ParseQNameThrow(tagName, out prefix, out localName);

            // Map the prefix to a namespace URI
            ns = null!;
            foreach (StringPair pair in _prefixMappingsList[idxPrefixMappings])
            {
                if (prefix == pair.Left)
                {
                    ns = pair.Right;
                    break;
                }
            }

            // Throw exception if prefix could not be resolved
            if (ns == null)
            {
                // Check for mappings that are always in-scope
                if (prefix.Length == 0)
                    ns = "";
                else if (prefix.Equals("xml"))
                    ns = XmlReservedNs.NsXml;
                // It is not correct to resolve xmlns prefix in XPath but removing it would be a breaking change.
                else if (prefix.Equals("xmlns"))
                    ns = XmlReservedNs.NsXmlNs;
                else if (IsXslt2)
                    // Xsl3: a computed element name's undeclared prefix (an attribute's is XTDE0860, XmlQueryOutput).
                    throw new Xsl3.Xdm.XdmException("XTDE0830", $"The prefix '{prefix}' of the computed name '{tagName}' is not declared");
                else
                    throw new XslTransformException(SR.Xslt_InvalidPrefix, prefix);
            }
        }

        /// <summary>
        /// Return true if the nav1's LocalName and NamespaceURI properties equal nav2's corresponding properties.
        /// </summary>
        public bool IsQNameEqual(XPathNavigator n1, XPathNavigator n2)
        {
            if ((object)n1.NameTable == (object)n2.NameTable)
            {
                // Use atomized comparison
                return (object)n1.LocalName == (object)n2.LocalName && (object)n1.NamespaceURI == (object)n2.NamespaceURI;
            }

            return (n1.LocalName == n2.LocalName) && (n1.NamespaceURI == n2.NamespaceURI);
        }

        /// <summary>
        /// Return true if the specified navigator's LocalName and NamespaceURI properties equal the argument names.
        /// </summary>
        public bool IsQNameEqual(XPathNavigator navigator, int indexLocalName, int indexNamespaceUri)
        {
            if ((object)navigator.NameTable == (object)_nameTableQuery)
            {
                // Use atomized comparison
                return ((object)GetAtomizedName(indexLocalName) == (object)navigator.LocalName &&
                        (object)GetAtomizedName(indexNamespaceUri) == (object)navigator.NamespaceURI);
            }

            // Use string comparison
            return (GetAtomizedName(indexLocalName) == navigator.LocalName) && (GetAtomizedName(indexNamespaceUri) == navigator.NamespaceURI);
        }

        /// <summary>
        /// Get the Xml query type at the specified index in the array of types.
        /// </summary>
        internal XmlQueryType GetXmlType(int idxType)
        {
            Debug.Assert(_types != null);
            return _types[idxType];
        }

        /// <summary>
        /// Forward call to ChangeTypeXsltArgument(XmlQueryType, object, Type).
        /// </summary>
        public object ChangeTypeXsltArgument(int indexType, object value, Type destinationType)
        {
            return ChangeTypeXsltArgument(GetXmlType(indexType), value, destinationType);
        }

        /// <summary>
        /// Convert from the Clr type of "value" to Clr type "destinationType" using V1 Xslt rules.
        /// These rules include converting any Rtf values to Nodes.
        /// </summary>
        internal static object ChangeTypeXsltArgument(XmlQueryType xmlType, object value, Type destinationType)
        {
            Debug.Assert(XmlILTypeHelper.GetStorageType(xmlType).IsAssignableFrom(value.GetType()),
                         "Values passed to ChangeTypeXsltArgument should be in ILGen's default Clr representation.");

            Debug.Assert(destinationType == typeof(object) || !destinationType.IsAssignableFrom(value.GetType()),
                         $"No need to call ChangeTypeXsltArgument since value is already assignable to destinationType {destinationType}");

            switch (xmlType.TypeCode)
            {
                case XmlTypeCode.String:
                    if (destinationType == typeof(DateTime))
                        value = XsltConvert.ToDateTime((string)value);
                    break;

                case XmlTypeCode.Double:
                    if (destinationType != typeof(double))
                        value = Convert.ChangeType(value, destinationType, CultureInfo.InvariantCulture);
                    break;

                case XmlTypeCode.Node:
                    Debug.Assert(xmlType != XmlQueryTypeFactory.Node && xmlType != XmlQueryTypeFactory.NodeS,
                                 "Rtf values should have been eliminated by caller.");

                    if (destinationType == typeof(XPathNodeIterator))
                    {
                        value = new XPathArrayIterator((IList)value);
                    }
                    else if (destinationType == typeof(XPathNavigator[]))
                    {
                        // Copy sequence to XPathNavigator[]
                        IList<XPathNavigator> seq = (IList<XPathNavigator>)value;
                        XPathNavigator[] navArray = new XPathNavigator[seq.Count];

                        for (int i = 0; i < seq.Count; i++)
                            navArray[i] = seq[i];

                        value = navArray;
                    }
                    break;

                case XmlTypeCode.Item:
                    {
                        // Only typeof(object) is supported as a destination type
                        if (destinationType != typeof(object))
                            throw new XslTransformException(SR.Xslt_UnsupportedClrType, destinationType.Name);

                        // Convert to default, backwards-compatible representation
                        //   1. NodeSet: System.Xml.XPath.XPathNodeIterator
                        //   2. Rtf: System.Xml.XPath.XPathNavigator
                        //   3. Other:   Default V1 representation
                        IList<XPathItem> seq = (IList<XPathItem>)value;
                        if (seq.Count == 1)
                        {
                            XPathItem item = seq[0];

                            if (item.IsNode)
                            {
                                // Node or Rtf
                                RtfNavigator? rtf = item as RtfNavigator;
                                if (rtf != null)
                                    value = rtf.ToNavigator();
                                else
                                    value = new XPathArrayIterator((IList)value);
                            }
                            else
                            {
                                // Atomic value
                                value = item.TypedValue;
                            }
                        }
                        else
                        {
                            // Nodeset
                            value = new XPathArrayIterator((IList)value);
                        }
                        break;
                    }
            }

            Debug.Assert(destinationType.IsAssignableFrom(value.GetType()), $"ChangeType from type {value.GetType().Name} to type {destinationType.Name} failed");
            return value;
        }

        /// <summary>
        /// Forward call to ChangeTypeXsltResult(XmlQueryType, object)
        /// </summary>
        public object ChangeTypeXsltResult(int indexType, object value)
        {
            return ChangeTypeXsltResult(GetXmlType(indexType), value);
        }

        /// <summary>
        /// Convert from the Clr type of "value" to the default Clr type that ILGen uses to represent the xml type, using
        /// the conversion rules of the xml type.
        /// </summary>
        internal object ChangeTypeXsltResult(XmlQueryType xmlType, object? value)
        {
            if (value == null)
                throw new XslTransformException(SR.Xslt_ItemNull, string.Empty);

            switch (xmlType.TypeCode)
            {
                case XmlTypeCode.String:
                    if (value.GetType() == typeof(DateTime))
                        value = XsltConvert.ToString((DateTime)value);
                    break;

                case XmlTypeCode.Double:
                    if (value.GetType() != typeof(double))
                        value = ((IConvertible)value).ToDouble(null);

                    break;

                case XmlTypeCode.Node:
                    if (!xmlType.IsSingleton)
                    {
                        XPathArrayIterator? iter = value as XPathArrayIterator;

                        // Special-case XPathArrayIterator in order to avoid copies
                        if (iter != null && iter.AsList is XmlQueryNodeSequence sequence)
                        {
                            value = sequence;
                        }
                        else
                        {
                            // Iterate over list and ensure it only contains nodes
                            XmlQueryNodeSequence seq = new XmlQueryNodeSequence();
                            IList? list = value as IList;

                            if (list != null)
                            {
                                for (int i = 0; i < list.Count; i++)
                                    seq.Add(EnsureNavigator(list[i]));
                            }
                            else
                            {
                                foreach (object o in (IEnumerable)value)
                                    seq.Add(EnsureNavigator(o));
                            }

                            value = seq;
                        }

                        // Always sort node-set by document order
                        value = ((XmlQueryNodeSequence)value).DocOrderDistinct(Documents.Order);
                    }
                    break;

                case XmlTypeCode.Item:
                    {
                        // Xsl3: 2.0+ stylesheets map more CLR types to XDM values (SPEC §7.1); 1.0 keeps stock's rules.
                        if (IsXslt2 && ClrValues.TryConvert(value, out IList<XPathItem> converted))
                        {
                            value = converted;
                            break;
                        }
                        Type sourceType = value.GetType();
                        IXPathNavigable? navigable;

                        // If static type is item, then infer type based on dynamic value
                        switch (XsltConvert.InferXsltType(sourceType).TypeCode)
                        {
                            case XmlTypeCode.Boolean:
                                value = new XmlQueryItemSequence(XmlAtomicValueInternals.Create(XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.Boolean), value));
                                break;

                            case XmlTypeCode.Double:
                                value = new XmlQueryItemSequence(XmlAtomicValueInternals.Create(XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.Double), ((IConvertible)value).ToDouble(null)));
                                break;

                            case XmlTypeCode.String:
                                if (sourceType == typeof(DateTime))
                                    value = new XmlQueryItemSequence(XmlAtomicValueInternals.Create(XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.String), XsltConvert.ToString((DateTime)value)));
                                else
                                    value = new XmlQueryItemSequence(XmlAtomicValueInternals.Create(XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.String), value));
                                break;

                            case XmlTypeCode.Node:
                                // Support XPathNavigator[]
                                value = ChangeTypeXsltResult(XmlQueryTypeFactory.NodeS, value);
                                break;

                            case XmlTypeCode.Item:
                                // Support XPathNodeIterator
                                if (value is XPathNodeIterator)
                                {
                                    value = ChangeTypeXsltResult(XmlQueryTypeFactory.NodeS, value);
                                    break;
                                }

                                // Support IXPathNavigable and XPathNavigator
                                navigable = value as IXPathNavigable;
                                if (navigable != null)
                                {
                                    if (value is XPathNavigator navigator)
                                        value = new XmlQueryNodeSequence(navigator);
                                    else
                                        value = new XmlQueryNodeSequence(navigable.CreateNavigator()!);
                                    break;
                                }

                                throw new XslTransformException(SR.Xslt_UnsupportedClrType, sourceType.Name);
                        }
                        break;
                    }
            }

            Debug.Assert(XmlILTypeHelper.GetStorageType(xmlType).IsAssignableFrom(value.GetType()), $"Xml type {xmlType} is not represented in ILGen as {value.GetType().Name}");

            return value;
        }

        /// <summary>
        /// Ensure that "value" is a navigator and not null.
        /// </summary>
        private static XPathNavigator EnsureNavigator(object? value)
        {
            XPathNavigator? nav = value as XPathNavigator;

            if (nav == null)
                throw new XslTransformException(SR.Xslt_ItemNull, string.Empty);

            return nav;
        }

        /// <summary>
        /// Return true if the type of every item in "seq" matches the xml type identified by "idxType".
        /// </summary>
        public bool MatchesXmlType(IList<XPathItem> seq, int indexType)
        {
            XmlQueryType typBase = GetXmlType(indexType);

            XmlQueryCardinality card = seq.Count switch
            {
                0 => XmlQueryCardinality.Zero,
                1 => XmlQueryCardinality.One,
                _ => XmlQueryCardinality.More,
            };

            if (!(card <= typBase.Cardinality))
                return false;

            typBase = typBase.Prime;
            for (int i = 0; i < seq.Count; i++)
            {
                if (!CreateXmlType(seq[i]).IsSubtypeOf(typBase))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Return true if the type of "item" matches the xml type identified by "idxType".
        /// </summary>
        public bool MatchesXmlType(XPathItem item, int indexType)
        {
            return CreateXmlType(item).IsSubtypeOf(GetXmlType(indexType));
        }

        /// <summary>
        /// Return true if the type of "seq" is a subtype of a singleton type identified by "code".
        /// </summary>
        public bool MatchesXmlType(IList<XPathItem> seq, XmlTypeCode code)
        {
            if (seq.Count != 1)
                return false;

            return MatchesXmlType(seq[0], code);
        }

        /// <summary>
        /// Return true if the type of "item" is a subtype of the type identified by "code".
        /// </summary>
        public bool MatchesXmlType(XPathItem item, XmlTypeCode code)
        {
            // All atomic type codes appear after AnyAtomicType
            if (code > XmlTypeCode.AnyAtomicType)
                return !item.IsNode && item.XmlType is { } annotation && annotation.TypeCode == code;

            // Handle node code and AnyAtomicType
            switch (code)
            {
                // Native atomic values need no schema lookup. Other non-node items must have
                // an atomic annotation: functions, maps, arrays and control items have none.
                case XmlTypeCode.AnyAtomicType: return item is Xsl3.Xdm.AtomicValue || !item.IsNode && item.XmlType != null;
                case XmlTypeCode.Node: return item.IsNode;
                case XmlTypeCode.Item: return true;
                default:
                    if (!item.IsNode)
                        return false;

                    switch (((XPathNavigator)item).NodeType)
                    {
                        case XPathNodeType.Root: return code == XmlTypeCode.Document;
                        case XPathNodeType.Element: return code == XmlTypeCode.Element;
                        case XPathNodeType.Attribute: return code == XmlTypeCode.Attribute;
                        case XPathNodeType.Namespace: return code == XmlTypeCode.Namespace;
                        case XPathNodeType.Text: return code == XmlTypeCode.Text;
                        case XPathNodeType.SignificantWhitespace: return code == XmlTypeCode.Text;
                        case XPathNodeType.Whitespace: return code == XmlTypeCode.Text;
                        case XPathNodeType.ProcessingInstruction: return code == XmlTypeCode.ProcessingInstruction;
                        case XPathNodeType.Comment: return code == XmlTypeCode.Comment;
                    }
                    break;
            }

            Debug.Fail($"XmlTypeCode {code} was not fully handled.");
            return false;
        }

        /// <summary>
        /// Create an XmlQueryType that represents the type of "item".
        /// </summary>
        private static XmlQueryType CreateXmlType(XPathItem item)
        {
            if (item.IsNode)
            {
                // Rtf
                RtfNavigator? rtf = item as RtfNavigator;
                if (rtf != null)
                    return XmlQueryTypeFactory.Node;

                // Node
                XPathNavigator nav = (XPathNavigator)item;
                switch (nav.NodeType)
                {
                    case XPathNodeType.Root:
                    case XPathNodeType.Element:
                        if (nav.XmlType == null)
                            return XmlQueryTypeFactory.Type(nav.NodeType, XmlQualifiedNameTest.New(nav.LocalName, nav.NamespaceURI), XmlSchemaComplexType.UntypedAnyType, false);

                        // Xsl3 (P6): a compact tree's validated node has a type but no declaration.
                        return XmlQueryTypeFactory.Type(nav.NodeType, XmlQualifiedNameTest.New(nav.LocalName, nav.NamespaceURI), nav.XmlType, nav.SchemaInfo?.SchemaElement?.IsNillable ?? false);

                    case XPathNodeType.Attribute:
                        if (nav.XmlType == null)
                            return XmlQueryTypeFactory.Type(nav.NodeType, XmlQualifiedNameTest.New(nav.LocalName, nav.NamespaceURI), DatatypeImplementation.UntypedAtomicType, false);

                        return XmlQueryTypeFactory.Type(nav.NodeType, XmlQualifiedNameTest.New(nav.LocalName, nav.NamespaceURI), nav.XmlType, false);
                }

                return XmlQueryTypeFactory.Type(nav.NodeType, XmlQualifiedNameTest.Wildcard, XmlSchemaComplexType.AnyType, false);
            }

            // Function items, maps and arrays have no schema annotation. QIL's general
            // type model represents them as item(), for both item and sequence checks.
            XmlSchemaType? annotation = item.XmlType;
            return annotation == null ? XmlQueryTypeFactory.Item
                : XmlQueryTypeFactory.Type((XmlSchemaSimpleType)annotation, true);
        }


        //-----------------------------------------------
        // Xml collations
        //-----------------------------------------------

        /// <summary>
        /// Get a collation that was statically created.
        /// </summary>
        public XmlCollation GetCollation(int index)
        {
            Debug.Assert(_collations != null);
            return _collations[index];
        }

        /// <summary>
        /// Create a collation from a string.
        /// </summary>
        public XmlCollation? CreateCollation(string collation)
        {
            return XmlCollation.Create(collation);
        }


        //-----------------------------------------------
        // Document Ordering and Identity
        //-----------------------------------------------

        /// <summary>
        /// Compare the relative positions of two navigators.  Return -1 if navThis is before navThat, 1 if after, and
        /// 0 if they are positioned to the same node.
        /// </summary>
        public int ComparePosition(XPathNavigator navigatorThis, XPathNavigator navigatorThat)
        {
            return Documents.Order.Compare(navigatorThis, navigatorThat);
        }

        /// <summary>
        /// Get a comparer which guarantees a stable ordering among nodes, even those from different documents.
        /// </summary>
        public IList<XPathNavigator> DocOrderDistinct(IList<XPathNavigator> seq)
        {
            if (seq.Count <= 1)
                return seq;

            XmlQueryNodeSequence nodeSeq = (XmlQueryNodeSequence)seq;

            return nodeSeq.DocOrderDistinct(Documents.Order);
        }

        /// <summary>
        /// Generate a unique string identifier for the specified node.  Do this by asking the navigator for an identifier
        /// that is unique within the document, and then prepend a document index.
        /// </summary>
        public string GenerateId(XPathNavigator navigator)
        {
            return string.Create(CultureInfo.InvariantCulture, $"ID{Documents.Order.GetDocumentIndex(navigator)}{navigator.UniqueId}");
        }


        //-----------------------------------------------
        // Indexes
        //-----------------------------------------------

        /// <summary>
        /// If an index having the specified Id has already been created over the "context" document, then return it
        /// in "index" and return true.  Otherwise, create a new, empty index and return false.
        /// </summary>
        private XPathNavigator? _indexRoot;
        private SharedKeyIndexes? _sharedIndexes;
        private List<(int Id, XPathNavigator Root, ExceptionDispatchInfo Fault)>? _indexFaults;

        private void ShareIndexes(XmlQueryRuntime owner)
        {
            if (owner._sharedIndexes == null)
            {
                var shared = new SharedKeyIndexes(owner.Documents.Evaluations, Cancellation);
                if (owner._indexes != null)
                    for (int id = 0; id < owner._indexes.Length; id++)
                        if (owner._indexes[id] is { } entries)
                            for (int i = 0; i < entries.Count; i += 2)
                                shared.Seed(id, (XPathNavigator)entries[i]!, owner.GenerateId((XPathNavigator)entries[i]!), (XmlILIndex)entries[i + 1]!, null, false);
                if (owner._indexesInProgress != null)
                    foreach (var entry in owner._indexesInProgress)
                        shared.Seed(entry.Id, entry.Root, owner.GenerateId(entry.Root), null, null, true);
                if (owner._indexFaults != null)
                    foreach (var entry in owner._indexFaults)
                        shared.Seed(entry.Id, entry.Root, owner.GenerateId(entry.Root), null, entry.Fault, false);
                owner._sharedIndexes = shared;
                owner._indexes = null;
                owner._indexesInProgress = null;
                owner._indexFaults = null;
            }
            _sharedIndexes = owner._sharedIndexes;
        }

        public bool FindIndex(XPathNavigator context, int indexId, out XmlILIndex index)
        {
            XPathNavigator navRoot;
            ArrayList docIndexes;
            Debug.Assert(context != null);

            // Get root of document (Xsl3 P4: in a navigator kept for the purpose, rather than a clone per key() call)
            if (_indexRoot == null || !_indexRoot.MoveTo(context))
            {
                _indexRoot = context.Clone();
            }
            navRoot = _indexRoot;
            navRoot.MoveToRoot();
            if (_sharedIndexes != null)
                return _sharedIndexes.Find(indexId, navRoot, GenerateId(navRoot), Cancellation, EvaluationDependencies, out index);
            if (_indexFaults != null)
                foreach (var fault in _indexFaults)
                    if (fault.Id == indexId && fault.Root.IsSamePosition(navRoot)) fault.Fault.Throw();

            // Search pre-existing indexes in order to determine whether the specified index has already been created
            if (_indexes != null && indexId < _indexes.Length)
            {
                docIndexes = _indexes[indexId];
                if (docIndexes != null)
                {
                    // Search for an index defined over the specified document
                    for (int i = 0; i < docIndexes.Count; i += 2)
                    {
                        // If we find a matching document, then return the index saved in the next slot
                        if (((XPathNavigator)docIndexes[i]!).IsSamePosition(navRoot))
                        {
                            index = (XmlILIndex)docIndexes[i + 1]!;
                            return true;
                        }
                    }
                }
            }

            if (IsXslt2)
            {
                // Xsl3: an index asked for while it is being built means the key's definition uses itself (XTDE0640).
                _indexesInProgress ??= new List<(int, XPathNavigator)>();
                foreach (var (id, root) in _indexesInProgress)
                {
                    if (id == indexId && root.IsSamePosition(navRoot))
                    {
                        throw new Xsl3.Xdm.XdmException("XTDE0640", "The definition of a key depends on itself");
                    }
                }
                _indexesInProgress.Add((indexId, navRoot.Clone()));
            }

            // Return a new, empty index
            index = new XmlILIndex();
            return false;
        }

        // Xsl3: the indexes being built (2.0+), by key and document.
        private List<(int Id, XPathNavigator Root)>? _indexesInProgress;

        /// <summary>Generated key builds publish failures before unwinding to their caller.</summary>
        public void IndexValueFailed(XPathNavigator context, int indexId, Exception error)
        {
            RecordFailureStack(error);
            XPathNavigator root = context.Clone();
            root.MoveToRoot();
            if (_sharedIndexes != null)
            {
                if (_sharedIndexes.Publish(indexId, root, GenerateId(root), null, error) is { } previous) Cancellation = previous;
            }
            else if (_indexesInProgress?.RemoveAll(e => e.Id == indexId && e.Root.IsSamePosition(root)) > 0)
                (_indexFaults ??= new()).Add((indexId, root, ExceptionDispatchInfo.Capture(error)));
        }

        /// <summary>
        /// Add a newly built index over the specified "context" document to the existing collection of indexes.
        /// </summary>
        public void AddNewIndex(XPathNavigator context, int indexId, XmlILIndex index)
        {
            XPathNavigator navRoot;
            ArrayList docIndexes;
            Debug.Assert(context != null);

            // Get root of document
            navRoot = context.Clone();
            navRoot.MoveToRoot();
            if (_sharedIndexes != null)
            {
                if (_sharedIndexes.Publish(indexId, navRoot, GenerateId(navRoot), index, null) is { } previous) Cancellation = previous;
                return;
            }
            _indexesInProgress?.RemoveAll(e => e.Id == indexId && e.Root.IsSamePosition(navRoot));

            // Ensure that a slot exists for the new index
            if (_indexes == null)
            {
                _indexes = new ArrayList[indexId + 4];
            }
            else if (indexId >= _indexes.Length)
            {
                // Resize array
                ArrayList[] indexesNew = new ArrayList[indexId + 4];
                Array.Copy(_indexes, indexesNew, _indexes.Length);
                _indexes = indexesNew;
            }

            docIndexes = (ArrayList)_indexes[indexId];
            if (docIndexes == null)
            {
                docIndexes = new ArrayList();
                _indexes[indexId] = docIndexes;
            }

            docIndexes.Add(navRoot);
            docIndexes.Add(index);
        }


        //-----------------------------------------------
        // Output construction
        //-----------------------------------------------

        /// <summary>
        /// Get output writer object.
        /// </summary>
        public XmlQueryOutput Output
        {
            get { return _output; }
        }

        /// <summary>
        /// Start construction of a nested sequence of items. Return a new XmlQueryOutput that will be
        /// used to construct this new sequence.
        /// </summary>
        public void StartSequenceConstruction(out XmlQueryOutput output)
        {
            // Push current writer
            _stkOutput.Push(_output);

            // Create new writers
            // Xsl3 (perf): 2.0+ reuses the outputs of finished constructions (DocBook xslTNG makes hundreds of thousands).
            if (IsXslt2 && _freeOutputs is { Count: > 0 } free)
            {
                output = _output = free.Pop();
                output.Reset(new XmlCachedSequenceWriter(compact: true), null);
                return;
            }
            output = _output = new XmlQueryOutput(this, new XmlCachedSequenceWriter(compact: IsXslt2));
        }

        // Xsl3 (perf): outputs of finished nested constructions, to reuse (2.0+).
        private Stack<XmlQueryOutput>? _freeOutputs;

        private void Release(XmlQueryOutput finished)
        {
            if (IsXslt2 && (_freeOutputs ??= new Stack<XmlQueryOutput>()).Count < 64)
            {
                _freeOutputs.Push(finished);
            }
        }

        /// <summary>
        /// End construction of a nested sequence of items and return the items as an IList{XPathItem}
        /// internal class.  Return previous XmlQueryOutput.
        /// </summary>
        public IList<XPathItem> EndSequenceConstruction(out XmlQueryOutput output)
        {
            IList<XPathItem> seq = ((XmlCachedSequenceWriter)_output.SequenceWriter!).ResultSequence;

            // Restore previous XmlQueryOutput
            Release(_output);
            output = _output = _stkOutput.Pop();

            return seq;
        }

        /// <summary>
        /// Start construction of an Rtf. Return a new XmlQueryOutput object that will be used to construct this Rtf.
        /// </summary>
        public void StartRtfConstruction(string baseUri, out XmlQueryOutput output)
        {
            // Push current writer
            _stkOutput.Push(_output);

            // Create new XmlQueryOutput over an Rtf writer
            if (IsXslt2 && _freeOutputs is { Count: > 0 } free)
            {
                output = _output = free.Pop();
                output.Reset(null, new XmlEventCache(baseUri, true));
                return;
            }
            output = _output = new XmlQueryOutput(this, new XmlEventCache(baseUri, true));
        }

        /// <summary>
        /// End construction of an Rtf and return it as an RtfNavigator.  Return previous XmlQueryOutput object.
        /// </summary>
        public XPathNavigator EndRtfConstruction(out XmlQueryOutput output)
        {
            XmlEventCache events;

            events = (XmlEventCache)_output.Writer!;

            // Restore previous XmlQueryOutput
            Release(_output);
            output = _output = _stkOutput.Pop();

            // Return Rtf as an RtfNavigator
            events.EndEvents();
            return new RtfTreeNavigator(events, _nameTableQuery, compact: IsXslt2);
        }

        /// <summary>
        /// Construct a new RtfTextNavigator from the specified "text".  This is much more efficient than calling
        /// StartNodeConstruction(), StartRtf(), WriteString(), EndRtf(), and EndNodeConstruction().
        /// </summary>
        public XPathNavigator TextRtfConstruction(string text, string baseUri)
        {
            return new RtfTextNavigator(text, baseUri, compact: IsXslt2);
        }


        //-----------------------------------------------
        // Miscellaneous
        //-----------------------------------------------

        /// <summary>
        /// Report query execution information to event handler.
        /// </summary>
        public void SendMessage(string message)
        {
            _ctxt.OnXsltMessageEncountered(message);
        }

        /// <summary>
        /// Throw an Xml exception having the specified message text.
        /// </summary>
        public void ThrowException(string text)
        {
            throw new XslTransformException(text);
        }

        /// <summary>
        /// Position navThis to the same location as navThat.
        /// </summary>
        internal static XPathNavigator SyncToNavigator(XPathNavigator? navigatorThis, XPathNavigator navigatorThat)
        {
            if (navigatorThis == null || !navigatorThis.MoveTo(navigatorThat))
                return navigatorThat.Clone();

            return navigatorThis;
        }

        /// <summary>
        /// Function is called in Debug mode on each time context node change.
        /// </summary>
        public static int OnCurrentNodeChanged(XPathNavigator currentNode)
        {
            return 0;
        }

        // 'true' if current Namespace "inherited" from it's parent. Not defined locally.
        private static bool IsInheritedNamespace(XPathNavigator node)
        {
            Debug.Assert(node.NodeType == XPathNodeType.Namespace);
            XPathNavigator nav = node.Clone();
            if (nav.MoveToParent())
            {
                if (nav.MoveToFirstNamespace(XPathNamespaceScope.Local))
                {
                    do
                    {
                        if ((object)nav.LocalName == (object)node.LocalName)
                        {
                            return false;
                        }
                    } while (nav.MoveToNextNamespace(XPathNamespaceScope.Local));
                }
            }
            return true;
        }
    }
}
