// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

/// <summary>
/// Retains path order across expanded function bodies. A proof tracks provenance, order and disjointness,
/// including fixed child depths. Unrelated or repeated populations retain their canonicalization.
/// </summary>
internal sealed class StreamedPathOrder(QilFactory factory) : QilReplaceVisitor(factory)
{
    private sealed record Shape(QilNode? Anchor, int? Depth, bool Disjoint = true);
    private static readonly Shape Empty = new(null, 0);
    private readonly Dictionary<QilNode, Shape?> _shapes = new();
    private readonly HashSet<QilNode> _active = new();

    internal void Rewrite(QilExpression expression) => Visit(expression);

    protected override QilNode VisitDocOrderDistinct(QilUnary order)
    {
        base.VisitDocOrderDistinct(order);
        if (Proof(order.Child) == null) return order;
        // Keep the native node and document-order type contract at the replacement boundary.
        // This assertion introduces no new cardinality constraint or runtime helper.
        QilNode result = f.TypeAssert(order.Child, order.XmlType!);
        result.SourceLine = order.SourceLine;
        return result;
    }

    private Shape? Proof(QilNode node)
    {
        if (_shapes.TryGetValue(node, out Shape? known)) return known;
        if (!_active.Add(node)) return null;
        Shape? result;
        try
        {
            result = Derive(node);
            // Any single node is ordered and distinct, even when its provenance is opaque.
            // Its own anchor prevents an enclosing repeated loop from acquiring a false proof.
            if (result == null && node.XmlType is { IsNode: true, MaybeMany: false }) result = new(node, 0);
            if (result == null && node.XmlType is { IsNode: true, IsDod: true }) result = new(node, null, false);
        }
        finally { _active.Remove(node); }
        _shapes.Add(node, result);
        return result;
    }

    private Shape? Derive(QilNode node)
    {
        if (node.XmlType!.IsEmpty) return Empty;
        switch (node.NodeType)
        {
            case QilNodeType.For:
                return node.XmlType!.IsNode || Proof(((QilIterator)node).Binding!) != null ? new(node, 0) : null;
            case QilNodeType.Let: return Proof(((QilIterator)node).Binding!);
            case QilNodeType.TypeAssert: return Proof(((QilTargetType)node).Source);
            case QilNodeType.Content:
                return Proof(((QilUnary)node).Child) is { Disjoint: true } parent ? Below(parent) : null;
            case QilNodeType.Descendant:
            case QilNodeType.DescendantOrSelf:
                return Proof(((QilUnary)node).Child) is { } root ? root == Empty ? Empty : new(root.Anchor, null, false) : null;
            case QilNodeType.Filter:
                return Proof(((QilLoop)node).Variable.Binding!);
            case QilNodeType.Loop:
            {
                var loop = (QilLoop)node;
                Shape? body = Proof(loop.Body);
                if (loop.Variable.NodeType == QilNodeType.Let || body == Empty) return body;
                Shape? input = Proof(loop.Variable.Binding!);
                if (input == Empty) return Empty;
                if (body?.Anchor == loop.Variable)
                    return input == null || !input.Disjoint && body.Depth != 0 ? null
                        : new(input.Anchor, input.Depth.HasValue && body.Depth.HasValue ? checked(input.Depth.Value + body.Depth.Value) : null,
                            input.Disjoint && body.Disjoint);
                return !loop.Variable.Binding!.XmlType!.MaybeMany ? body : null;
            }
            case QilNodeType.Conditional:
            {
                var choice = (QilTernary)node;
                Shape? left = Proof(choice.Center), right = Proof(choice.Right);
                return left == Empty ? right : right == Empty || left != null && left == right ? left : null;
            }
            case QilNodeType.Sequence:
            {
                Shape? result = Empty;
                foreach (QilNode child in node)
                {
                    Shape? part = Proof(child);
                    if (part == null) return null;
                    if (part == Empty) continue;
                    if (result != Empty) return null;
                    result = part;
                }
                return result;
            }
            case QilNodeType.DocOrderDistinct:
                return Proof(((QilUnary)node).Child);
            case QilNodeType.XsltInvokeEarlyBound:
            {
                var call = (QilInvokeEarlyBound)node;
                if (call.ClrMethod.DeclaringType != typeof(XsltLibrary)) return null;
                switch (call.ClrMethod.Name)
                {
                    case nameof(XsltLibrary.X31OutermostNext):
                    case nameof(XsltLibrary.X31OutermostChildNext): return new(node, null);
                    case nameof(XsltLibrary.X31Nodes):
                    case nameof(XsltLibrary.X31NodeItem): return Proof(call.Arguments[0]);
                    case nameof(XsltLibrary.X31SingletonNode): return Proof(call.Arguments[1]);
                    case nameof(XsltLibrary.X31Raise):
                    case nameof(XsltLibrary.X31RaiseItem): return Empty;
                    case nameof(XsltLibrary.X31Coerce):
                        return call.Arguments[1] is QilLiteral { Value: double id }
                            && ConstantPool.Get((int)id) is SequenceTypeTest { Item: NodeKindTest or AnyItemTest }
                            ? Proof(call.Arguments[0]) : null;
                }
                return null;
            }
            default: return null;
        }
    }

    private static Shape Below(Shape parent) => parent == Empty ? Empty : new(parent.Anchor, parent.Depth.HasValue ? checked(parent.Depth.Value + 1) : null);
}
