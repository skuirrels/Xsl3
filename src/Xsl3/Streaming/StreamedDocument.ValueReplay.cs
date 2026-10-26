// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Xsl3.Streaming;

internal sealed partial class StreamedDocument
{
    private List<(WeakReference<StreamedDescendantCursor> Owner, StreamedValueReplay Store)>? _valueReplays;
    private StreamedValueReplay? _spareValueReplay;

    internal int RetainedValueHeaderCount
    {
        get
        {
            int count = 0;
            if (_valueReplays != null)
                foreach (var entry in _valueReplays) count += entry.Store.OpenHeaderCount;
            return count;
        }
    }

    internal int RetainedValueBufferBytes
    {
        get
        {
            int size = _spareValueReplay?.MemoryCapacity ?? 0;
            if (_valueReplays != null)
                foreach (var entry in _valueReplays) size += entry.Store.MemoryCapacity;
            return size;
        }
    }

    internal IEnumerable<string> ValueReplayFiles
    {
        get
        {
            if (_valueReplays != null)
                foreach (var entry in _valueReplays)
                    foreach (string path in entry.Store.Files) yield return path;
            if (_spareValueReplay != null)
                foreach (string path in _spareValueReplay.Files) yield return path;
        }
    }

    internal void CheckValueReplay() => Check();

    internal StreamedValueReplay CreateValueReplay(StreamedDescendantCursor owner)
    {
        var store = _spareValueReplay ?? new StreamedValueReplay(this);
        _spareValueReplay = null;
        (_valueReplays ??= new()).Add((new(owner), store));
        return store;
    }

    private void ObserveValueReplays(bool newNode)
    {
        if (_valueReplays == null) return;
        foreach (var entry in _valueReplays) entry.Store.Observe(_current, newNode);
    }

    private void PruneValueReplays()
    {
        if (_valueReplays == null) return;
        for (int i = _valueReplays.Count - 1; i >= 0; i--)
            if (!_valueReplays[i].Owner.TryGetTarget(out var owner) || owner.Finished)
            {
                _valueReplays[i].Store.Dispose();
                _valueReplays.RemoveAt(i);
            }
    }

    internal void ReleaseValueReplay(StreamedValueReplay store)
    {
        store.Reset();
        if (_spareValueReplay == null) _spareValueReplay = store;
        else store.Dispose();
        _valueReplays!.RemoveAll(entry => ReferenceEquals(entry.Store, store));
    }

    private void DisposeValueReplays()
    {
        ExceptionDispatchInfo? failure = null;
        try
        {
            if (_valueReplays != null)
                foreach (var entry in _valueReplays)
                {
                    try { entry.Store.Dispose(); }
                    catch (Exception error) when (System.Xml.XmlException.IsCatchableException(error))
                    { failure ??= ExceptionDispatchInfo.Capture(error); }
                }
        }
        finally
        {
            _valueReplays?.Clear();
            _spareValueReplay?.Dispose();
            _spareValueReplay = null;
        }
        failure?.Throw();
    }
}
