// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    // This value travels only through the generated function ABI, never as a user item.
    private sealed class FunctionArgument(XsltLibrary owner, FunctionItem producer, bool orderedChildren, bool forkable = false) : AtomicValue(AtomicTypes.String)
    {
        internal readonly XsltLibrary Owner = owner;
        internal readonly PullState Source = new(owner, producer);
        internal readonly bool OrderedChildren = orderedChildren;
        internal readonly bool Forkable = forkable;
        public override string Value => throw new InvalidOperationException("A streamed argument cannot be atomized");
        public override object ClrValue => throw new InvalidOperationException("A streamed argument cannot be atomized");
    }

    private sealed class FunctionParameter : AtomicValue
    {
        private readonly FunctionArgument? _source;
        private readonly SequenceTypeTest _type;
        private readonly IList<XPathItem>? _items;
        private XPathItem? _first;
        private Dictionary<long, XPathItem>? _grounded;
        private long _read;
        private bool _ended;
        private ExceptionDispatchInfo? _fault;

        internal FunctionParameter(IList<XPathItem> items, SequenceTypeTest type, bool prepared = false) : base(AtomicTypes.String)
        {
            _type = type;
            if (prepared) { _items = items; return; }
            if (items.Count == 1 && items[0] is FunctionArgument source)
            {
                _source = source;
                // One header of lookahead permits existence inspection before consumption.
                if (!source.Forkable || type is not { Cardinality: Cardinality.ZeroOrMore,
                    Item: AnyItemTest or NodeKindTest { Kind: null, LocalName: null, NamespaceUri: null, TypeName: null, Names: null, AnyType: true } }) NewItem();
            }
            else
            {
                if (!type.Matches(items)) throw TypeError(type);
                // Generated argument lists can be reused while the callee iterates.
                // Own the positions, as other retained XDM values do.
                _items = Copy(items);
            }
        }

        private static XdmException TypeError(SequenceTypeTest type) => new("XPTY0004", $"The function argument does not match {type}");

        private XPathItem? NewItem()
        {
            _fault?.Throw();
            try { return ReadNew(); }
            catch (Exception error) when (XmlException.IsCatchableException(error))
            {
                _fault = ExceptionDispatchInfo.Capture(error);
                throw;
            }
        }

        private XPathItem? ReadNew()
        {
            XmlQueryRuntime.CheckCancellation();
            if (_ended) return null;
            IList<XPathItem> next = _source!.Owner.X31PullNext(_source.Source);
            if (next.Count > 1) throw new InvalidOperationException("A streamed argument producer must yield at most one item");
            if (next.Count == 0)
            {
                _ended = true;
                if (_read == 0 && !_type.AllowsEmpty) throw TypeError(_type);
                return null;
            }
            XPathItem item = next[0];
            if (!_type.Item!.Matches(item)) throw TypeError(_type);
            _read++;
            if (_read == 1) _first = Unshare(item);
            if (item is not StreamedNavigator streamed || streamed.Replayable)
                (_grounded ??= new()).Add(_read - 1, Unshare(item));
            return item;
        }

        internal XPathItem? Next(ref long index)
        {
            _fault?.Throw();
            XmlQueryRuntime.CheckCancellation();
            if (_items != null) return index < _items.Count ? Unshare(_items[(int)index++]) : null;
            if (_grounded?.TryGetValue(index, out XPathItem? retained) == true) { index++; return Unshare(retained); }
            if (index == 0 && _first != null) { index++; return Unshare(_first); }
            if (index < _read) throw StreamedDocument.PositionError();
            XPathItem? next = NewItem();
            if (next != null) index++;
            return next;
        }

        internal void Finish()
        {
            XmlQueryRuntime.CheckCancellation();
            if (_items != null) return;
            while (NewItem() != null) { }
        }

        internal bool Exists
        {
            get
            {
                if (_items != null) return _items.Count != 0;
                if (_read == 0 && !_ended) NewItem();
                return _read != 0;
            }
        }

        internal FunctionParameter? ForkOrigin;
        internal IList<XPathItem>? GroundedItems => _items;
        internal SequenceTypeTest RequiredType => _type;
        internal bool CanFork => _source is { Forkable: true, Source.Position: 0, Source.Slots: null } && _read == 0 && !_ended;
        internal FunctionArgument ForkArgument => CanFork ? _source!
            : throw new XdmException("streaming-unimplemented", "Forking an already inspected or stateful streamed parameter requires continuation projection")
                { Name = new QName(StreamedDocument.ErrorNamespace, "streaming-unimplemented") };

        internal void AcceptFork(long count, XPathNavigator? first)
        {
            _read = count;
            _first = first;
            _ended = true;
            _source?.Source.Complete();
        }

        internal long ReadCount => _items?.Count ?? _read;
        internal XPathNavigator? FirstHeader => (_items != null ? _items.Count == 0 ? null : _items[0] : _first) as XPathNavigator;
        // A materialized singleton is still a disjoint anchor when its node is live.
        // Losing that fact would normalize/cache its child population before use.
        internal bool OrderedChildren => _source?.OrderedChildren == true || _items?.Count <= 1;
        internal XPathItem Count()
        {
            Finish();
            return AtomicValue.OfInteger(_items?.Count ?? _read);
        }

        public override string Value => throw new InvalidOperationException("A streamed parameter cannot be atomized");
        public override object ClrValue => throw new InvalidOperationException("A streamed parameter cannot be atomized");
    }

    private sealed class FunctionParameterCursor(FunctionParameter parameter) : AtomicValue(AtomicTypes.String)
    {
        internal readonly FunctionParameter Parameter = parameter;
        internal long Position;
        public override string Value => throw new InvalidOperationException("A parameter cursor cannot be atomized");
        public override object ClrValue => throw new InvalidOperationException("A parameter cursor cannot be atomized");
    }

    public XPathItem X31FunctionArgument(XPathItem producer, bool orderedChildren) => new FunctionArgument(this, (FunctionItem)producer, orderedChildren);
    public XPathItem X31FunctionArgumentForkable(XPathItem producer, bool orderedChildren) => new FunctionArgument(this, (FunctionItem)producer, orderedChildren, forkable: true);
    public IList<XPathItem> X31FunctionArgumentForward(XPathItem producer, bool orderedChildren, XPathItem parameter)
    {
        var original = (FunctionParameter)parameter;
        return original.GroundedItems is { } items ? Copy(items)
            : Sequences.One(new FunctionArgument(this, (FunctionItem)producer, orderedChildren, original.CanFork));
    }
    public XPathItem X31FunctionParameterNew(IList<XPathItem> items, double type) => new FunctionParameter(items, (SequenceTypeTest)Constant((int)type));
    public XPathItem X31FunctionParameterCursor(XPathItem parameter) => new FunctionParameterCursor((FunctionParameter)parameter);
    public bool X31FunctionParameterOrderedChildren(XPathItem parameter) => ((FunctionParameter)parameter).OrderedChildren;
    public bool X31FunctionParameterExists(XPathItem parameter)
    {
        XmlQueryRuntime.CheckCancellation();
        return ((FunctionParameter)parameter).Exists;
    }
    public XPathItem X31FunctionParameterCount(XPathItem parameter) => ((FunctionParameter)parameter).Count();
    public IList<XPathNavigator> X31FunctionParameterNext(XPathItem cursor)
    {
        var state = (FunctionParameterCursor)cursor;
        return state.Parameter.Next(ref state.Position) is { } node ? new[] { (XPathNavigator)node } : Array.Empty<XPathNavigator>();
    }
    public IList<XPathItem> X31FunctionParameterItemNext(XPathItem cursor)
    {
        var state = (FunctionParameterCursor)cursor;
        return state.Parameter.Next(ref state.Position) is { } item ? new[] { item } : Sequences.Empty;
    }
    public IList<XPathItem> X31FunctionParameterEnd(XPathItem parameter, IList<XPathItem> result)
    {
        ((FunctionParameter)parameter).Finish();
        return result;
    }
}
