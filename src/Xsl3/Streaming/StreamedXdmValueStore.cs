// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

/// <summary>
/// Serializes value structure and supported callable graphs into the source's scratch stream.
/// Compiled type, code and library-context models stay shared. Nodes and opaque function items retain identity;
/// those explicitly grounded/user-provided values can require memory proportional to their contents.
/// </summary>
internal sealed class StreamedXdmValueStore(Action? check = null)
{
    private enum Kind : byte { Atomic, Map, Array, Shared, Identity, Compiled, Coerced, Library, Random }
    private enum AtomicKind : byte { Lexical, String, Double, Float, QName }
    private readonly Dictionary<AtomicType, int> _typeIds = new();
    private readonly List<AtomicType> _types = new();
    private sealed record CompiledCode(XmlQueryRuntime Owner, int Id, FunctionSignature Signature);
    private readonly Dictionary<CompiledCode, int> _codeIds = new();
    private readonly List<CompiledCode> _codes = new();
    private sealed record CoercedCode(SequenceTypeTest[] Parameters, SequenceTypeTest Return);
    private sealed class CoercedCodeComparer : IEqualityComparer<CoercedCode>
    {
        public bool Equals(CoercedCode? x, CoercedCode? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null || !ReferenceEquals(x.Return, y.Return) || x.Parameters.Length != y.Parameters.Length) return false;
            for (int i = 0; i < x.Parameters.Length; i++) if (!ReferenceEquals(x.Parameters[i], y.Parameters[i])) return false;
            return true;
        }

        public int GetHashCode(CoercedCode code)
        {
            var hash = new HashCode();
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(code.Return));
            foreach (var parameter in code.Parameters) hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(parameter));
            return hash.ToHashCode();
        }
    }
    private readonly Dictionary<CoercedCode, int> _coercionIds = new(new CoercedCodeComparer());
    private readonly List<CoercedCode> _coercions = new();
    private readonly Dictionary<FunctionDefinition, int> _definitionIds = new(ReferenceEqualityComparer.Instance);
    private readonly List<FunctionDefinition> _definitions = new();
    private readonly Dictionary<FunctionContext, int> _contextIds = new(ReferenceEqualityComparer.Instance);
    private readonly List<FunctionContext> _contexts = new();
    private sealed record CoercedCompletion(CoercedCode Code, int ObjectId, XPathItem[] Target);
    private readonly record struct ReadFrame(XPathItem[] Items, int Next, CoercedCompletion? Completion = null);
    private readonly Dictionary<XPathItem, int> _identityIds = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(int Document, string Position), int> _nodeIds = new();
    private readonly List<XPathItem> _identities = new();
    private DocumentOrderComparer? _documents;

    private void Check()
    {
        XmlQueryRuntime.CheckCancellation();
        check?.Invoke();
    }

    internal void Clear()
    {
        _typeIds.Clear(); _types.Clear(); _codeIds.Clear(); _codes.Clear();
        _coercionIds.Clear(); _coercions.Clear();
        _definitionIds.Clear(); _definitions.Clear(); _contextIds.Clear(); _contexts.Clear();
        _identityIds.Clear(); _nodeIds.Clear(); _identities.Clear(); _documents = null;
    }

    internal void Write(BinaryWriter writer, IList<XPathItem>? value)
    {
        Check();
        writer.Write(value != null);
        if (value == null) return;
        writer.Write(value.Count);
        if (value.Count == 1 && value[0] is AtomicValue single)
        { writer.Write((byte)Kind.Atomic); WriteAtomic(writer, single); return; }
        var objects = new Dictionary<XPathItem, int>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(IList<XPathItem> Items, int Next)>();
        pending.Push((value, 0));
        while (pending.TryPop(out var frame))
        {
            Check();
            if (frame.Next == frame.Items.Count) continue;
            XPathItem item = frame.Items[frame.Next];
            pending.Push((frame.Items, frame.Next + 1));
            if (item is AtomicValue atomic) { writer.Write((byte)Kind.Atomic); WriteAtomic(writer, atomic); continue; }
            if (item is MapItem or ArrayItem or CompiledFunction or CoercedFunction or LibraryFunction or NativeFunction { RandomState: not null })
            {
                if (objects.TryGetValue(item, out int shared))
                { writer.Write((byte)Kind.Shared); writer.Write(shared); continue; }
                objects.Add(item, objects.Count);
                IReadOnlyList<IList<XPathItem>> members;
                if (item is MapItem map)
                {
                    writer.Write((byte)Kind.Map); writer.Write(map.Count);
                    var entries = new List<IList<XPathItem>>(map.Count);
                    foreach (var entry in map.Entries)
                    {
                        Check();
                        WriteAtomic(writer, entry.Key); writer.Write(entry.Value.Count); entries.Add(entry.Value);
                    }
                    members = entries;
                }
                else if (item is ArrayItem array)
                {
                    writer.Write((byte)Kind.Array); writer.Write(array.Count);
                    members = array.Members;
                    foreach (var member in members) { Check(); writer.Write(member.Count); }
                }
                else if (item is CompiledFunction function)
                {
                    var code = new CompiledCode(function.Owner, function.Id, function.Signature);
                    if (!_codeIds.TryGetValue(code, out int codeId))
                    { _codeIds.Add(code, codeId = _codes.Count); _codes.Add(code); }
                    writer.Write((byte)Kind.Compiled); writer.Write(codeId);
                    members = function.Captured;
                    writer.Write(members.Count);
                    foreach (var member in members) { Check(); writer.Write(member.Count); }
                }
                else if (item is CoercedFunction coerced)
                {
                    var parameters = new SequenceTypeTest[coerced.Arity];
                    for (int i = 0; i < parameters.Length; i++) { Check(); parameters[i] = coerced.ParameterType(i); }
                    var code = new CoercedCode(parameters, coerced.ReturnType);
                    if (!_coercionIds.TryGetValue(code, out int coercionId))
                    { _coercionIds.Add(code, coercionId = _coercions.Count); _coercions.Add(code); }
                    writer.Write((byte)Kind.Coerced); writer.Write(coercionId);
                    members = new[] { new XPathItem[] { coerced.Target } };
                }
                else if (item is NativeFunction { RandomState: { } state })
                {
                    writer.Write((byte)Kind.Random); writer.Write(state.Value); writer.Write(state.Permute);
                    members = Array.Empty<IList<XPathItem>>();
                }
                else
                {
                    var library = (LibraryFunction)item;
                    if (!_definitionIds.TryGetValue(library.Definition, out int definition))
                    { _definitionIds.Add(library.Definition, definition = _definitions.Count); _definitions.Add(library.Definition); }
                    if (!_contextIds.TryGetValue(library.Context, out int context))
                    { _contextIds.Add(library.Context, context = _contexts.Count); _contexts.Add(library.Context); }
                    // Variadic arity is a recorded value, not part of a retained metadata key.
                    writer.Write((byte)Kind.Library); writer.Write(definition); writer.Write(context); writer.Write(library.Arity);
                    members = Array.Empty<IList<XPathItem>>();
                }
                for (int i = members.Count - 1; i >= 0; i--) pending.Push((members[i], 0));
                continue;
            }
            writer.Write((byte)Kind.Identity);
            int id;
            if (item is XPathNavigator node)
            {
                var key = ((_documents ??= new()).GetDocumentIndex(node), node.UniqueId);
                if (!_nodeIds.TryGetValue(key, out id))
                { _nodeIds.Add(key, id = _identities.Count); _identities.Add(node.Clone()); }
            }
            else if (!_identityIds.TryGetValue(item, out id))
            { _identityIds.Add(item, id = _identities.Count); _identities.Add(item); }
            writer.Write(id);
        }
    }

    internal IList<XPathItem>? Read(BinaryReader reader)
    {
        Check();
        if (!reader.ReadBoolean()) return null;
        var result = new XPathItem[Count(reader)];
        Kind? firstKind = result.Length == 1 ? (Kind)reader.ReadByte() : null;
        if (firstKind == Kind.Atomic) { result[0] = ReadAtomic(reader); return result; }
        var objects = new List<XPathItem?>();
        Dictionary<int, List<(XPathItem[] Items, int Index)>>? waiting = null;
        Dictionary<XPathItem[], ReadFrame>? deferred = null;
        var pending = new Stack<ReadFrame>();
        pending.Push(new(result, 0));
        while (pending.TryPop(out var frame))
        {
            Check();
            if (frame.Completion is { } completion)
            {
                // An immutable wrapper's target can depend on an outer wrapper through a container or closure.
                // Publish it only when that target resolves, without changing the runtime function representation.
                if (completion.Target[0] == null)
                {
                    (deferred ??= new()).Add(completion.Target, frame);
                    continue;
                }
                if (completion.Target[0] is not FunctionItem target)
                    throw new InvalidDataException("A scratch coercion target is not a function");
                var function = new CoercedFunction(target, completion.Code.Parameters, completion.Code.Return);
                objects[completion.ObjectId] = function; frame.Items[frame.Next] = function;
                if (waiting != null && waiting.Remove(completion.ObjectId, out var uses))
                    foreach (var use in uses)
                    {
                        Check(); use.Items[use.Index] = function;
                        if (deferred != null && deferred.Remove(use.Items, out var ready)) pending.Push(ready);
                    }
                continue;
            }
            if (frame.Next == frame.Items.Length) continue;
            pending.Push(new(frame.Items, frame.Next + 1));
            XPathItem item;
            Kind kind = firstKind ?? (Kind)reader.ReadByte();
            firstKind = null;
            switch (kind)
            {
                case Kind.Atomic: item = ReadAtomic(reader); break;
                case Kind.Shared:
                {
                    int id = reader.ReadInt32();
                    if (objects[id] is { } shared) { item = shared; break; }
                    waiting ??= new();
                    if (!waiting.TryGetValue(id, out var uses)) waiting.Add(id, uses = new());
                    uses.Add((frame.Items, frame.Next));
                    continue;
                }
                case Kind.Identity:
                    item = _identities[reader.ReadInt32()];
                    if (item is XPathNavigator node) item = node.Clone();
                    break;
                case Kind.Map:
                {
                    int count = Count(reader);
                    var entries = new KeyValuePair<AtomicValue, IList<XPathItem>>[count];
                    var members = new XPathItem[count][];
                    for (int i = 0; i < count; i++)
                    {
                        Check();
                        AtomicValue key = ReadAtomic(reader);
                        members[i] = new XPathItem[Count(reader)];
                        entries[i] = new(key, members[i]);
                    }
                    item = MapItem.Of(entries); objects.Add(item);
                    for (int i = count - 1; i >= 0; i--) pending.Push(new(members[i], 0));
                    break;
                }
                case Kind.Array:
                {
                    int count = Count(reader);
                    var members = new XPathItem[count][];
                    for (int i = 0; i < count; i++) { Check(); members[i] = new XPathItem[Count(reader)]; }
                    item = ArrayItem.Of(members); objects.Add(item);
                    for (int i = count - 1; i >= 0; i--) pending.Push(new(members[i], 0));
                    break;
                }
                case Kind.Compiled:
                {
                    var code = _codes[reader.ReadInt32()];
                    var function = new CompiledFunction(code.Id, code.Signature, code.Owner);
                    item = function; objects.Add(item);
                    int count = Count(reader);
                    var captures = new XPathItem[count][];
                    for (int i = 0; i < count; i++)
                    {
                        Check(); captures[i] = new XPathItem[Count(reader)]; function.Captured.Add(captures[i]);
                    }
                    for (int i = count - 1; i >= 0; i--) pending.Push(new(captures[i], 0));
                    break;
                }
                case Kind.Coerced:
                {
                    var code = _coercions[reader.ReadInt32()];
                    int id = objects.Count; objects.Add(null);
                    var target = new XPathItem[1];
                    pending.Push(new(frame.Items, frame.Next, new(code, id, target)));
                    pending.Push(new(target, 0));
                    continue;
                }
                case Kind.Library:
                {
                    var definition = _definitions[reader.ReadInt32()];
                    var context = _contexts[reader.ReadInt32()];
                    int arity = reader.ReadInt32();
                    if (arity < 0) throw new InvalidDataException("A scratch library function has negative arity");
                    item = new LibraryFunction(definition, arity, context); objects.Add(item);
                    break;
                }
                case Kind.Random:
                    item = NativeFunction.Random(reader.ReadUInt64(), reader.ReadBoolean()); objects.Add(item);
                    break;
                default: throw new InvalidDataException("Unknown scratch XDM value kind");
            }
            frame.Items[frame.Next] = item;
        }
        if (waiting?.Count > 0 || deferred?.Count > 0) throw new InvalidDataException("A scratch function graph has unresolved references");
        return result;
    }

    internal static int Count(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        return count >= 0 ? count : throw new InvalidDataException("A scratch XDM population has a negative length");
    }

    private void WriteAtomic(BinaryWriter writer, AtomicValue value)
    {
        if (!_typeIds.TryGetValue(value.Type, out int type))
        { _typeIds.Add(value.Type, type = _types.Count); _types.Add(value.Type); }
        writer.Write(type);
        switch (value)
        {
            case StringValue s: writer.Write((byte)AtomicKind.String); writer.Write(s.String); break;
            case DoubleValue d: writer.Write((byte)AtomicKind.Double); writer.Write(d.Double); break;
            case FloatValue f: writer.Write((byte)AtomicKind.Float); writer.Write(f.Float); break;
            case QNameValue q:
                writer.Write((byte)AtomicKind.QName);
                writer.Write(q.QName.NamespaceUri); writer.Write(q.QName.LocalName); writer.Write(q.QName.Prefix);
                break;
            default: writer.Write((byte)AtomicKind.Lexical); writer.Write(value.Value); break;
        }
    }

    private AtomicValue ReadAtomic(BinaryReader reader)
    {
        AtomicType type = _types[reader.ReadInt32()];
        return (AtomicKind)reader.ReadByte() switch
        {
            AtomicKind.String => new StringValue(type, reader.ReadString()),
            AtomicKind.Double => new DoubleValue(type, reader.ReadDouble()),
            AtomicKind.Float => new FloatValue(type, reader.ReadSingle()),
            AtomicKind.QName => new QNameValue(type, new QName(reader.ReadString(), reader.ReadString(), reader.ReadString())),
            AtomicKind.Lexical => Casting.Cast(AtomicValue.OfUntyped(reader.ReadString()), AtomicTypes.Of(type.Primitive)).WithType(type),
            _ => throw new InvalidDataException("Unknown scratch atomic value kind"),
        };
    }
}

internal interface IStreamedHeaderMetadata
{
    IStreamedHeaderMetadataCodec Codec { get; }
    void Write(BinaryWriter writer, StreamedXdmValueStore values);
}

internal interface IStreamedHeaderMetadataCodec
{
    object Read(BinaryReader reader, StreamedXdmValueStore values, StreamedDocument document, long number);
}
