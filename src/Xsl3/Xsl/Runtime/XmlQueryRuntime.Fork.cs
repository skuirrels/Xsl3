// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Globalization;
using System.Threading;
using Xsl3.Tree;
using Xsl3.Streaming;

namespace Xsl3.Runtime;

public sealed partial class XmlQueryRuntime
{
    private XmlQueryRuntime? _forkRoot;
    private ExecutionState? _forkState;
    private int _forkStarted;
    private ForkCaller? _forkCallbacks;
    internal Func<System.Collections.Generic.IList<System.Xml.XPath.XPathItem>, System.Xml.XmlWriterSettings, OutputExtras?, bool,
        System.Collections.Generic.IList<System.Xml.XPath.XPathItem>>? ForkPrincipal;

    // Construct on the invoking thread, before starting workers. Mutable execution caches,
    // outputs, reader name tables and source ownership belong to each prong. The global item
    // remains the transformation's global item; the prong's focus travels as a function argument.
    internal XmlQueryRuntime CreateForkRuntime(XmlSequenceWriter results) => CreateForkRuntime(results, null);

    internal XmlQueryRuntime CreateForkRuntime(XmlSequenceWriter results, ForkCaller? callbacks)
    {
        ArgumentNullException.ThrowIfNull(results);
        var child = new XmlQueryRuntime(_staticData, XslCompiledTransform.AbsentInput, _ctxt.DataSources, null, results,
            resourceOwner: this, forkOwner: this)
        {
            _forkRoot = _forkRoot ?? this,
            _forkCallbacks = _forkCallbacks ?? callbacks,
            _forkState = new ExecutionState(_forkCallbacks ?? callbacks),
            _randomSeed = RandomSeed,
            _preserveAnnotations = PreserveAnnotations,
            SchemaAware = SchemaAware,
        };
        child.ShareGlobalBindings(this);
        child.ShareIndexes(this);
        child.TraceStack.AddRange(TraceStack);
        child.TryLines.AddRange(TryLines);
        child.XsltFunctions.InheritForkContext(XsltFunctions);
        child.EvaluationDependencies = Documents.Evaluations.Capture(EvaluationDependencies);
        return child;
    }

    internal void InvokeOnCaller(Action callback)
    {
        if (_forkCallbacks is { } caller) caller.Invoke(callback, Cancellation);
        else callback();
    }

    internal bool SharesForkContext(XmlQueryRuntime owner) =>
        ReferenceEquals(_forkRoot ?? this, owner._forkRoot ?? owner);

    // Principal thread state remains local. Compiled forks coordinate explicit
    // principal writes through ForkPrincipal callbacks on the invoking thread.
    internal void RunFork(Action<XmlQueryRuntime> body, CancellationToken cancellation, PrincipalResult? principal)
    {
        ArgumentNullException.ThrowIfNull(body);
        ExecutionState state = _forkState ?? throw new InvalidOperationException("This runtime is not a fork prong");
        if (Interlocked.Exchange(ref _forkStarted, 1) != 0)
            throw new InvalidOperationException("A fork runtime can only be executed once");
        using var linked = cancellation.CanBeCanceled && state.Token.CanBeCanceled && cancellation != state.Token
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellation, state.Token) : null;
        CancellationToken effective = linked?.Token ?? (cancellation.CanBeCanceled ? cancellation : state.Token);
        var saved = new ExecutionState();
        try
        {
            state.Apply(effective, principal);
            effective.ThrowIfCancellationRequested();
            body(this);
        }
        catch (Exception error) when (AttachStylesheetStack(error))
        {
            throw; // The filter only attaches the prong's stylesheet stack.
        }
        finally
        {
            try { CloseStreamedSources(); }
            finally { saved.Apply(saved.Token, saved.PrincipalWriter); }
        }
    }

    /// <summary>All transformation-scoped thread state, captured before a prong starts.</summary>
    private sealed class ExecutionState
    {
        internal readonly CancellationToken Token = Cancellation;
        internal readonly PrincipalResult? PrincipalWriter = Principal;
        private readonly Func<string, System.IO.Stream>? _opener = ResultDocumentOpener;
        private readonly Func<string, string, System.IO.TextWriter>? _handler = ResultDocumentHandler;
        private readonly Action<string, System.Xml.XPath.XPathNavigator>? _trees = ResultTreeHandler;
        private readonly bool _environment = EnvironmentVariables, _extensions = ExtensionFunctions;
        private readonly bool? _documents = DocumentFunctions, _dynamic = DynamicTransforms;
        private readonly TimeSpan? _regexTimeout = RegexTimeout;
        private readonly System.Collections.Generic.IReadOnlyList<(string Name, string Version, Uri Location)>? _packages = Packages;
        private readonly string? _baseOutput = BaseOutputUri;
        private readonly int _depth = Depth;
        private readonly bool _strip = CompactDocument.StripInputAnnotations, _html5 = HtmlVoidElements.Html5;
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture, _uiCulture = CultureInfo.CurrentUICulture;

        internal ExecutionState(ForkCaller? caller = null)
        {
            if (caller == null) return;
            var opener = _opener;
            var handler = _handler;
            var trees = _trees;
            if (opener != null) _opener = uri => caller.Invoke(() => opener(uri), Cancellation);
            if (handler != null) _handler = (uri, method) => caller.Invoke(() => handler(uri, method), Cancellation);
            if (trees != null) _trees = (uri, tree) => caller.Invoke(() => trees(uri, tree), Cancellation);
        }

        internal void Apply(CancellationToken cancellation, PrincipalResult? principal)
        {
            Cancellation = cancellation;
            Principal = principal;
            ResultDocumentOpener = _opener;
            ResultDocumentHandler = _handler;
            ResultTreeHandler = _trees;
            EnvironmentVariables = _environment;
            ExtensionFunctions = _extensions;
            DocumentFunctions = _documents;
            DynamicTransforms = _dynamic;
            RegexTimeout = _regexTimeout;
            Packages = _packages;
            BaseOutputUri = _baseOutput;
            Depth = _depth;
            CompactDocument.StripInputAnnotations = _strip;
            HtmlVoidElements.Html5 = _html5;
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
