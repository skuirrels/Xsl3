// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Streaming;

// Optional test evidence. Installing a capture never selects an execution path or reads an input.
// The ambient slot and all queues are allocated only by an explicit capture, not ordinary execution.
internal sealed class StreamingExecutionCapture : IDisposable
{
    internal enum SourceKind { Initial, Default, SourceDocument }
    internal enum Stage { Unclassified, Preparation, Compilation, Stylesheet, Assertion }
    internal sealed record SourceEntry(string Kind, bool RequestedForward, string Status, bool? ActualForward, string? NavigatorType);
    internal sealed record ExecutionEntry(int Id, int? Parent, string Stage, bool StreamedInitialMode, string InputKind,
        bool ForwardInput, bool BodyCompleted, SourceEntry[] Sources);
    internal sealed record Report(int FormatVersion, int Started, ExecutionEntry[] Executions);
    internal sealed record CompilationEntry(string Stage, bool HasStreamedInputs, bool OptimizationCompleted, int VerifierStarted, int VerifierCompleted);
    internal sealed record CompilationReport(int FormatVersion, CompilationEntry[] Compilations);
    internal sealed record Ambient(StreamingExecutionCapture Owner, Execution? Execution, Stage Stage = Stage.Unclassified);

    private static AsyncLocal<Ambient?>? s_slot;
    private readonly AsyncLocal<Ambient?> _slot;
    private readonly Ambient? _previous;
    private readonly ConcurrentQueue<ExecutionEntry> _executions = new();
    private readonly ConcurrentQueue<Compilation> _compilations = new();
    private int _started, _disposed;

    internal StreamingExecutionCapture()
    {
        var slot = Volatile.Read(ref s_slot);
        if (slot == null)
        {
            var candidate = new AsyncLocal<Ambient?>();
            slot = Interlocked.CompareExchange(ref s_slot, candidate, null) ?? candidate;
        }
        _slot = slot;
        _previous = slot.Value;
        slot.Value = new(this, null);
    }

    internal Report Snapshot() => new(2, Volatile.Read(ref _started), _executions.ToArray());
    internal CompilationReport SnapshotCompilations() => new(1, System.Linq.Enumerable.ToArray(
        System.Linq.Enumerable.Select(_compilations, compilation => compilation.Snapshot())));

    // The compiler records eligibility independently of whether the optimizer calls its verifier.
    internal static Compilation? BeginCompilation(bool hasStreamedInputs)
    {
        Ambient? current = Volatile.Read(ref s_slot)?.Value;
        if (current == null || Volatile.Read(ref current.Owner._disposed) != 0) return null;
        var compilation = new Compilation(current.Stage, hasStreamedInputs);
        current.Owner._compilations.Enqueue(compilation);
        return compilation;
    }

    internal sealed class Compilation(Stage stage, bool hasStreamedInputs)
    {
        private int _optimizationCompleted, _verifierStarted, _verifierCompleted;
        internal void CompleteOptimization() => Volatile.Write(ref _optimizationCompleted, 1);
        internal void BeginVerification() => Interlocked.Increment(ref _verifierStarted);
        internal void CompleteVerification() => Interlocked.Increment(ref _verifierCompleted);
        internal CompilationEntry Snapshot() => new(stage.ToString(), hasStreamedInputs,
            Volatile.Read(ref _optimizationCompleted) != 0, Volatile.Read(ref _verifierStarted), Volatile.Read(ref _verifierCompleted));
    }

    // Harness boundaries identify the operation being observed. The scope only changes
    // capture metadata; it cannot choose a source, compile a stylesheet or execute a command.
    internal static IDisposable? ObserveStage(Stage stage)
    {
        Ambient? current = Volatile.Read(ref s_slot)?.Value;
        return current == null || Volatile.Read(ref current.Owner._disposed) != 0
            ? null : new StageScope(current, stage);
    }

    private sealed class StageScope : IDisposable
    {
        private readonly Ambient _previous, _current;
        private int _disposed;

        internal StageScope(Ambient previous, Stage stage)
        {
            _previous = previous;
            _current = previous with { Stage = stage };
            previous.Owner._slot.Value = _current;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && ReferenceEquals(_previous.Owner._slot.Value, _current))
                _previous.Owner._slot.Value = _previous;
        }
    }

    internal static Execution? BeginExecution(bool streamedInitialMode, object input)
    {
        Ambient? current = Volatile.Read(ref s_slot)?.Value;
        return current == null || Volatile.Read(ref current.Owner._disposed) != 0
            ? null : new Execution(current, streamedInitialMode, input);
    }

    internal static void RecordSource(SourceKind kind, bool requestedForward, XPathNavigator selection)
    {
        if (CurrentExecution is { } execution)
            execution.Record(new(kind.ToString(), requestedForward, "Opened", selection is StreamedNavigator,
                selection.GetType().FullName ?? selection.GetType().Name));
    }

    internal static void RecordSourceFailure(SourceKind kind, bool requestedForward) =>
        CurrentExecution?.Record(new(kind.ToString(), requestedForward, "Failed", null, null));

    private static Execution? CurrentExecution
    {
        get
        {
            Ambient? current = Volatile.Read(ref s_slot)?.Value;
            return current == null || Volatile.Read(ref current.Owner._disposed) != 0 ? null : current.Execution;
        }
    }

    internal sealed class Execution : IDisposable
    {
        private readonly Ambient _previous;
        private readonly ConcurrentQueue<SourceEntry> _sources = new();
        private readonly int _id;
        private readonly bool _streamedInitialMode, _forwardInput;
        private readonly string _inputKind;
        private bool _bodyCompleted;
        private int _disposed;

        internal Execution(Ambient previous, bool streamedInitialMode, object input)
        {
            _previous = previous;
            _id = Interlocked.Increment(ref previous.Owner._started);
            _streamedInitialMode = streamedInitialMode;
            object source = input is InitialSource initial ? initial.Input : input;
            _inputKind = source switch
            {
                XmlReader => "reader", Stream => "stream", Uri => "uri", string => "location",
                XPathNavigator => "navigator", IXPathNavigable => "navigable", _ => "other",
            };
            _forwardInput = source is XmlReader or Stream or Uri or string;
            previous.Owner._slot.Value = new(previous.Owner, this, previous.Stage);
        }

        internal void CompleteBody() => _bodyCompleted = true;
        internal void Record(SourceEntry source) => _sources.Enqueue(source);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            StreamingExecutionCapture owner = _previous.Owner;
            if (owner._slot.Value?.Execution == this) owner._slot.Value = _previous;
            if (Volatile.Read(ref owner._disposed) == 0)
                owner._executions.Enqueue(new(_id, _previous.Execution?._id, _previous.Stage.ToString(), _streamedInitialMode,
                    _inputKind, _forwardInput, _bodyCompleted, _sources.ToArray()));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (ReferenceEquals(_slot.Value?.Owner, this)) _slot.Value = _previous;
    }
}
