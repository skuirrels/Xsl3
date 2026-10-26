// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

/// <summary>Document identity and atomic resource claims for one transformation.</summary>
internal sealed class DocumentResources
{
    private sealed class Loading(int thread, long id)
    {
        internal readonly int Thread = thread;
        internal readonly long Id = id;
        internal bool Complete;
        internal XPathNavigator? Value;
        internal ExceptionDispatchInfo? Fault;
        internal EvaluationWaitGraph.Node? Dependency;
    }

    private readonly object _gate = new();
    private Dictionary<Uri, XPathNavigator>? _cache;
    private Dictionary<Uri, Loading>? _loading;
    private Dictionary<int, Loading>? _waiting;
    private Dictionary<Uri, int>? _pending;
    private HashSet<Uri>? _read, _written;
    private CopyOriginRegistry? _copies;
    private bool _shared;
    private CancellationToken _evaluationCancellation;
    private long _nextLoading;
    private EvaluationWaitGraph? _evaluations;
    internal EvaluationWaitGraph Evaluations
    {
        get
        {
            lock (_gate)
            {
                if (_evaluations == null)
                {
                    _evaluations = new();
                    if (_loading != null)
                        foreach (Loading entry in _loading.Values) entry.Dependency = _evaluations.Start(entry.Thread);
                }
                return _evaluations;
            }
        }
    }
    internal readonly DocumentOrderComparer Order = new();
    internal int WaitingCount { get { lock (_gate) return _waiting?.Count ?? 0; } }

    internal XmlResolver? TrackResolver(XmlResolver? resolver)
    {
        while (resolver is ResourceResolver tracked)
        {
            if (ReferenceEquals(tracked.Resources, this)) return tracked;
            resolver = tracked.Inner;
        }
        return resolver == null ? null : new ResourceResolver(resolver, this);
    }

    internal static void RethrowReadControlError(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
            if (current is OperationCanceledException or XdmException { Code: "XTDE1500" })
                ExceptionDispatchInfo.Capture(current).Throw();
    }

    internal bool IsUnused
    {
        get
        {
            lock (_gate)
                if (_cache?.Count > 0 || _loading?.Count > 0 || _waiting?.Count > 0 || _pending?.Count > 0
                    || _read?.Count > 0 || _written?.Count > 0 || _copies != null) return false;
            return Order.IsUnused;
        }
    }

    internal void Share(CancellationToken cancellation)
    {
        lock (_gate)
        {
            if (_shared) return;
            _evaluationCancellation = cancellation;
            _ = Evaluations;
            _shared = true;
        }
    }

    internal IReadOnlySet<long>? CaptureDependencies(IReadOnlySet<long>? inherited)
    {
        HashSet<long>? result = inherited == null ? null : new(inherited);
        int thread = Environment.CurrentManagedThreadId;
        lock (_gate)
            if (_loading != null)
                foreach (Loading entry in _loading.Values)
                    if (entry.Thread == thread) (result ??= new()).Add(entry.Id);
        return result;
    }

    internal void RecordCopy(XPathNavigator copy, XPathNavigator original, bool validated = false)
    {
        CopyOriginRegistry? registry = Volatile.Read(ref _copies);
        if (registry == null)
        {
            registry = new();
            registry = Interlocked.CompareExchange(ref _copies, registry, null) ?? registry;
        }
        registry.Set(copy, original, validated);
    }

    internal bool TryGetCopy(XPathNavigator copy, [NotNullWhen(true)] out CopyOriginRegistry.Origin? origin)
    {
        origin = null;
        return Volatile.Read(ref _copies)?.TryGet(copy, out origin) == true;
    }

    private static XdmException ReadWrite(Uri uri) => new("XTDE1500",
        $"'{uri}' can't be both read and written by the same transformation");

    internal void ClaimWrite(string uri)
    {
        // A host result-document handler can accept a relative target when no base output URI
        // was supplied. Keep that identity; resolution belongs to the caller's explicit contract.
        var address = new Uri(uri, UriKind.RelativeOrAbsolute);
        lock (_gate)
        {
            if (_read?.Contains(address) == true || _pending?.ContainsKey(address) == true) throw ReadWrite(address);
            if (!(_written ??= new()).Add(address))
                throw new XdmException("XTDE1490", $"Two final result trees can't have the same URI: {uri}");
        }
    }

    private void StartRead(Uri uri)
    {
        if (_written?.Contains(uri) == true) throw ReadWrite(uri);
        _pending ??= new();
        _pending[uri] = _pending.GetValueOrDefault(uri) + 1;
    }

    private void FinishRead(Uri uri, bool success)
    {
        if (success) (_read ??= new()).Add(uri);
        int remaining = _pending![uri] - 1;
        if (remaining == 0) _pending.Remove(uri);
        else _pending[uri] = remaining;
    }

    internal sealed class ReadClaim(DocumentResources owner, Uri uri) : IDisposable
    {
        private bool _complete;
        internal void Commit()
        {
            lock (owner._gate)
            {
                if (_complete) throw new InvalidOperationException("The source read claim has already completed");
                owner.FinishRead(uri, success: true);
                _complete = true;
            }
        }
        public void Dispose()
        {
            lock (owner._gate)
            {
                if (_complete) return;
                owner.FinishRead(uri, success: false);
                _complete = true;
            }
        }
    }

    internal ReadClaim BeginRead(Uri uri)
    {
        lock (_gate) StartRead(uri);
        return new(this, uri);
    }

    internal void Cache(Uri uri, XPathNavigator value)
    {
        lock (_gate)
        {
            if (_written?.Contains(uri) == true) throw ReadWrite(uri);
            (_cache ??= new(DocumentFragment.Comparer))[uri] = value.Clone();
            (_read ??= new()).Add(uri);
        }
    }

    internal bool TryGet(Uri uri, out XPathNavigator? value)
    {
        lock (_gate)
        {
            value = _cache?.TryGetValue(uri, out XPathNavigator? cached) == true ? cached.Clone() : null;
            return value != null;
        }
    }

    internal void MarkRead(Uri uri)
    {
        lock (_gate)
        {
            if (_written?.Contains(uri) == true) throw ReadWrite(uri);
            (_read ??= new()).Add(uri);
        }
    }

    internal XPathNavigator? Load(Uri uri, Func<XPathNavigator?> load, CancellationToken cancellation, IReadOnlySet<long>? ancestors = null,
        IReadOnlySet<EvaluationWaitGraph.Node>? dependencies = null)
    {
        Loading entry;
        bool owner;
        bool shared;
        CancellationToken evaluation;
        int thread = Environment.CurrentManagedThreadId;
        lock (_gate)
        {
            cancellation.ThrowIfCancellationRequested();
            if (_cache?.TryGetValue(uri, out XPathNavigator? cached) == true) return cached.Clone();
            if (_loading?.TryGetValue(uri, out entry!) == true) owner = false;
            else
            {
                StartRead(uri);
                entry = new(thread, ++_nextLoading);
                entry.Dependency = _evaluations?.Start(thread);
                (_loading ??= new(DocumentFragment.Comparer)).Add(uri, entry);
                owner = true;
            }
            shared = _shared;
            evaluation = _evaluationCancellation;
        }
        if (!owner)
        {
            using var registration = cancellation.UnsafeRegister(static state => ((DocumentResources)state!).Wake(), this);
            lock (_gate)
            {
                while (!entry.Complete)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (entry.Thread == thread || ancestors?.Contains(entry.Id) == true)
                        throw new XdmException("FODC0002", $"Circular retrieval of document '{uri}'");
                    using var dependency = entry.Dependency == null ? default : _evaluations!.Wait(entry.Dependency, dependencies,
                        () => new XdmException("FODC0002", $"Circular retrieval of document '{uri}'"));
                    for (Loading other = entry; _evaluations == null && _waiting?.TryGetValue(other.Thread, out Loading? pending) == true;)
                    {
                        // A notified waiter may not have reacquired the monitor yet. Its completed
                        // dependency no longer participates in the graph, even if its owner moved on.
                        if (pending.Complete) break;
                        if (pending.Thread == thread) throw new XdmException("FODC0002", $"Circular retrieval of document '{uri}'");
                        other = pending;
                    }
                    (_waiting ??= new()).Add(thread, entry);
                    try { Monitor.Wait(_gate); }
                    finally { _waiting.Remove(thread); }
                }
                cancellation.ThrowIfCancellationRequested();
                entry.Fault?.Throw();
                return entry.Value?.Clone();
            }
        }

        // A following prong's cancellation must not abort a document needed by an earlier prong.
        // Waiters still use their own token. Restore the owner's context on every exit.
        CancellationToken previous = XmlQueryRuntime.Cancellation;
        if (shared) XmlQueryRuntime.Cancellation = evaluation;
        try
        {
            entry.Value = load()?.Clone();
            return entry.Value?.Clone();
        }
        catch (Exception error)
        {
            entry.Fault = ExceptionDispatchInfo.Capture(error);
            throw;
        }
        finally
        {
            if (shared) XmlQueryRuntime.Cancellation = previous;
            lock (_gate)
            {
                if (entry.Value != null) (_cache ??= new(DocumentFragment.Comparer))[uri] = entry.Value;
                FinishRead(uri, entry.Value != null);
                _loading!.Remove(uri);
                entry.Complete = true;
                _evaluations?.Finish(entry.Dependency);
                Monitor.PulseAll(_gate);
            }
        }
    }

    private void Wake() { lock (_gate) Monitor.PulseAll(_gate); }
}
