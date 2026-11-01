// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using Xsl3.XPath31;
using Xsl3.Xdm;

namespace Xsl3.Xslt;

// Optional optimization proof. Failure leaves the full fork input unchanged.
// No names or values from an input fixture participate in this analysis.
internal sealed class ForkProjectionAnalysis
{
    private sealed class Shape
    {
        internal readonly Dictionary<(string Local, string Namespace), Shape> Children = new();
        internal bool Whole;
    }

    private readonly Shape _root = new();

    internal static string?[]? Analyze(IList<XslNode> prongs, XPathStaticContext context, bool expandText = false)
    {
        var analysis = new ForkProjectionAnalysis();
        var namespaces = new Dictionary<string, string>(context.Namespaces, StringComparer.Ordinal) {
            [XsltInput.XPathDefaultNamespacePrefix] = context.DefaultElementNamespace,
            [XsltInput.ExpandTextPrefix] = expandText ? "yes" : "no",
        };
        try
        {
            if (!prongs.All(prong => analysis.Instruction(prong, analysis._root, namespaces))
                || analysis._root.Whole || analysis._root.Children.Count == 0) return null;
        }
        catch (XPathStaticException) { return null; }
        var data = new List<string?>();
        void Append(Shape shape, int parent, string local, string ns)
        {
            int index = data.Count / 4;
            data.Add(parent.ToString(InvariantNumberFormat.Info)); data.Add(local); data.Add(ns); data.Add(shape.Whole ? "1" : "0");
            foreach (var child in shape.Children) Append(child.Value, index, child.Key.Local, child.Key.Namespace);
        }
        Append(analysis._root, -1, "", "");
        return data.ToArray();
    }

    private bool Instruction(XslNode node, Shape focus, Dictionary<string, string> inherited)
    {
        if (node.XslVersion != XslVersion.Current || node.V2 is { IsFork: true } or { IsSourceDocument: true } or { Evaluate: not null } or { Assert: not null }) return false;
        var namespaces = new Dictionary<string, string>(inherited, StringComparer.Ordinal);
        for (NsDecl? ns = node.Namespaces; ns != null; ns = ns.Prev)
            if (ns.Prefix != null && ns.NsUri != null) namespaces[ns.Prefix] = ns.NsUri;
        bool Children(Shape current) => node.Content.All(child => Instruction(child, current, namespaces));
        switch (node.NodeType)
        {
            case XslNodeType.Nop: return true;
            case XslNodeType.List: return Children(focus);
            case XslNodeType.Sequence: return node.Select == null && Children(focus);
            case XslNodeType.Text:
                return !namespaces.TryGetValue(XsltInput.ExpandTextPrefix, out string? expand) || expand != "yes"
                    || node.Select?.IndexOfAny(new[] { '{', '}' }) < 0;
            case XslNodeType.ForEach:
                return node.Select != null && Path(Parser.ParseXPath(node.Select), focus, namespaces, attributes: false) is { } selected && Children(selected);
            case XslNodeType.ValueOf:
            case XslNodeType.ValueOfDoe:
                return node.Select != null && Literal(node.V2?.Separator) && Expression(Parser.ParseXPath(node.Select), focus, namespaces);
            case XslNodeType.ResultDocument:
                return Literal(node.V2?.Href) && Literal(node.V2?.Format) && Literal(node.V2?.Method)
                    && (node.V2?.Serialization == null || node.V2.Serialization.All(item => Literal(item.Value))) && Children(focus);
            default: return false;
        }
    }

    private static bool Literal(string? value) => value == null || value.IndexOfAny(new[] { '{', '}' }) < 0;

    private bool Expression(Expr expression, Shape focus, Dictionary<string, string> namespaces)
    {
        switch (expression)
        {
            case IntegerLiteral or DecimalLiteral or DoubleLiteral or StringLiteral: return true;
            case SequenceExpr sequence: return sequence.Items.All(item => Expression(item, focus, namespaces));
            case UnaryExpr unary: return Expression(unary.Operand, focus, namespaces);
            case BinaryExpr { Op: BinaryOp.Add or BinaryOp.Subtract or BinaryOp.Multiply or BinaryOp.Divide or BinaryOp.IntegerDivide or BinaryOp.Modulo } binary:
                return Expression(binary.Left, focus, namespaces) && Expression(binary.Right, focus, namespaces);
            case FunctionCall call:
                string? uri = call.Name.BracedUri ?? (call.Name.Prefix == null ? QName.Ns.Fn : namespaces.GetValueOrDefault(call.Name.Prefix));
                return uri == QName.Ns.Xs && call.Arguments.Count == 1 && Expression(call.Arguments[0], focus, namespaces);
            case CastExpr cast: return Expression(cast.Operand, focus, namespaces);
            case ContextItemExpr:
                focus.Whole = true; return true;
            default:
                Shape? selected = Path(expression, focus, namespaces, attributes: true);
                if (selected == null) return false;
                if (!IsAttributePath(expression)) selected.Whole = true;
                return true;
        }
    }

    private static bool IsAttributePath(Expr expression) => expression is AxisStep { Axis: Axis.Attribute }
        || expression is PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Attribute } };

    private Shape? Path(Expr expression, Shape focus, Dictionary<string, string> namespaces, bool attributes)
    {
        if (expression is ContextItemExpr) return focus;
        if (expression is PathExpr { Descendants: false } path)
        {
            Shape? parent = Path(path.Left, focus, namespaces, attributes: false);
            return parent == null ? null : Path(path.Right, parent, namespaces, attributes);
        }
        if (expression is not AxisStep { Predicates.Count: 0 } step) return null;
        if (step.Axis == Axis.Attribute) return attributes ? focus : null;
        if (step.Axis != Axis.Child || step.Test is not NameTest { IsWildcard: false, LocalName: { } local } name) return null;
        string? uri = name.BracedUri ?? (name.Prefix == null
            ? namespaces.GetValueOrDefault(XsltInput.XPathDefaultNamespacePrefix, "") : namespaces.GetValueOrDefault(name.Prefix));
        if (uri == null) return null;
        if (!focus.Children.TryGetValue((local, uri), out Shape? child)) focus.Children.Add((local, uri), child = new());
        return child;
    }
}
