// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.Qil;
using Xsl3.XPath31;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Xslt
{
    /// <summary>
    /// Xsl3: which 2.0 templates can only return nodes. A template function typed node* streams its result to the
    /// output writer, as in 1.0; one typed item* has its result cached and copied, which costs time and loses
    /// disable-output-escaping. A template may return atomic values (or other items) when its sequence constructor
    /// can: xsl:sequence, xsl:copy-of and xsl:perform-sort of expressions that aren't known to be nodes, an as
    /// attribute, xsl:copy of an atomic context item, or a call of a template that may.
    /// </summary>
    internal sealed partial class QilGenerator
    {
        private HashSet<ProtoTemplate>? _itemTemplates31;
        private HashSet<QilName>? _itemModes31;
        private bool _itemAllModes31;

        /// <summary>The result type of a template's function: item* if it may return items other than nodes.</summary>
        private XmlQueryType TemplateType31(ProtoTemplate tmpl) =>
            ItemTemplates31().Contains(tmpl) ? T.ItemS : T.NodeNotRtfS;

        /// <summary>The result type of an apply-templates (or apply-imports, next-match) function for a mode.</summary>
        private XmlQueryType ModeType31(QilName mode)
        {
            ItemTemplates31();
            return _itemAllModes31 || _itemModes31!.Contains(mode) ? T.ItemS : T.NodeNotRtfS;
        }

        private HashSet<ProtoTemplate> ItemTemplates31()
        {
            if (_itemTemplates31 != null)
            {
                return _itemTemplates31;
            }
            _itemTemplates31 = new HashSet<ProtoTemplate>();
            _itemModes31 = new HashSet<QilName>();
            // A fixed point: a template that calls one that may return items may return items.
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (ProtoTemplate tmpl in _compiler.AllTemplates)
                {
                    if (tmpl is not Template t || _itemTemplates31.Contains(t))
                    {
                        continue;
                    }
                    // A named template gets its caller's context item; a predicate pattern matches any item.
                    bool contextMayBeAtomic = t.Match == null || t.Name != null || Xsl3.XPath31.XPath31Compiler.MatchesItems(t.Match);
                    if (t.V2?.As != null || MayReturnItems(t.Content, contextMayBeAtomic, new Scope31(null, null, null)))
                    {
                        _itemTemplates31.Add(t);
                        if (t.Match != null)
                        {
                            AddItemModes(t);
                        }
                        changed = true;
                    }
                }
            }
            return _itemTemplates31;
        }

        private void AddItemModes(Template t)
        {
            if (t.V2?.AllModes == true)
            {
                _itemAllModes31 = true;
            }
            else if (t.V2?.Modes != null)
            {
                foreach (QilName m in t.V2.Modes)
                {
                    _itemModes31!.Add(m);
                }
            }
            else
            {
                _itemModes31!.Add(t.Mode);
            }
        }

        // The variables in scope while analysing: a variable's declaration tells whether its value is nodes.
        private sealed record Scope31(Scope31? Parent, QilName? Name, XslNode? Declaration)
        {
            public XslNode? Lookup(string local, string ns)
            {
                for (Scope31? s = this; s != null; s = s.Parent)
                {
                    if (s.Name != null && s.Name.LocalName == local && s.Name.NamespaceUri == ns)
                    {
                        return s.Declaration;
                    }
                }
                return null;
            }
        }

        private bool MayReturnItems(IList<XslNode> content, bool contextMayBeAtomic, Scope31 scope)
        {
            foreach (XslNode node in content)
            {
                switch (node.NodeType)
                {
                    case XslNodeType.Variable:
                    case XslNodeType.Param:
                        scope = new Scope31(scope, node.Name, node);
                        break;
                    case XslNodeType.Sequence:
                    case XslNodeType.CopyOf:
                        if (node.Select != null ? !IsNodeExpression(node.Select, contextMayBeAtomic, scope) : MayReturnItems(node.Content, contextMayBeAtomic, scope))
                        {
                            return true;
                        }
                        break;
                    case XslNodeType.PerformSort:
                        if (node.Select != null ? !IsNodeExpression(node.Select, contextMayBeAtomic, scope) : MayReturnItems(node.Content, contextMayBeAtomic, scope))
                        {
                            return true;
                        }
                        break;
                    case XslNodeType.Copy:
                        if (contextMayBeAtomic)
                        {
                            return true;
                        }
                        break;
                    case XslNodeType.ForEach:
                        if (MayReturnItems(node.Content, node.Select == null || !IsNodeExpression(node.Select, contextMayBeAtomic, scope), scope))
                        {
                            return true;
                        }
                        break;
                    case XslNodeType.ForEachGroup:
                        if (MayReturnItems(node.Content, node.Select == null || !IsNodeExpression(node.Select, contextMayBeAtomic, scope), scope))
                        {
                            return true;
                        }
                        break;
                    case XslNodeType.Map:
                    case XslNodeType.MapEntry:
                        return true;
                    case XslNodeType.Merge:
                        foreach (XslNode part in node.Content)
                        {
                            if (part.NodeType == XslNodeType.MergeAction && MayReturnItems(part.Content, contextMayBeAtomic: true, scope))
                            {
                                return true;
                            }
                        }
                        break;
                    case XslNodeType.Iterate:
                        if (MayReturnItems(node.Content, node.Select == null || !IsNodeExpression(node.Select, contextMayBeAtomic, scope), scope))
                        {
                            return true;
                        }
                        break;
                    case XslNodeType.Try:
                    case XslNodeType.Catch:
                    case XslNodeType.OnCompletion:
                    case XslNodeType.Break:
                    case XslNodeType.OnEmpty:
                    case XslNodeType.OnNonEmpty:
                    case XslNodeType.WherePopulated:
                        if (node.Select != null ? !IsNodeExpression(node.Select, contextMayBeAtomic, scope) : MayReturnItems(node.Content, contextMayBeAtomic, scope))
                        {
                            return true;
                        }
                        break;
                    case XslNodeType.AnalyzeString:
                        // The matching and non-matching substrings are strings.
                        if (MayReturnItems(node.Content, contextMayBeAtomic: true, scope))
                        {
                            return true;
                        }
                        break;
                    case XslNodeType.If:
                    case XslNodeType.Choose:
                    case XslNodeType.Otherwise:
                    case XslNodeType.List:
                    case XslNodeType.MatchingSubstring:
                    case XslNodeType.NonMatchingSubstring:
                        if (MayReturnItems(node.Content, contextMayBeAtomic, scope))
                        {
                            return true;
                        }
                        break;
                    case XslNodeType.CallTemplate:
                        if (node.Name != null && _compiler.NamedTemplates.TryGetValue(node.Name, out Template? called) && _itemTemplates31!.Contains(called))
                        {
                            return true;
                        }
                        break;
                    case XslNodeType.ApplyTemplates:
                        if (_itemAllModes31 || node.Name == null || _itemModes31!.Contains(node.Name) || ReferenceEquals(node.Name, XsltLoader.CurrentMode) && _itemModes31.Count > 0)
                        {
                            return true;
                        }
                        break;
                    case XslNodeType.ApplyImports:
                    case XslNodeType.NextMatch:
                        if (_itemAllModes31 || _itemModes31!.Count > 0)
                        {
                            return true;
                        }
                        break;
                    default:
                        // Node constructors (literal elements, xsl:element, xsl:value-of, …), declarations, and
                        // instructions with no result (xsl:message, xsl:result-document).
                        break;
                }
            }
            return false;
        }

        /// <summary>
        /// The body of a template that the analysis found returns only nodes, typed as such: parts whose static
        /// type is wider (a variable holding a temporary tree, say) are asserted to be nodes, through the
        /// sequences, loops and conditionals that pass results on, so that the constructors around them still
        /// stream to the writer.
        /// </summary>
        private QilNode ToNodes31(QilNode n)
        {
            if (n.XmlType!.IsSubtypeOf(T.NodeNotRtfS))
            {
                return n;
            }
            switch (n.NodeType)
            {
                case QilNodeType.Sequence:
                    for (int i = 0; i < n.Count; i++)
                    {
                        n[i] = ToNodes31(n[i]);
                    }
                    n.XmlType = QilTypeChecker.CheckSequence((QilList)n);
                    return n;
                case QilNodeType.Loop:
                    var loop = (QilLoop)n;
                    loop.Body = ToNodes31(loop.Body);
                    n.XmlType = QilTypeChecker.CheckLoop(loop);
                    return n;
                case QilNodeType.Conditional:
                    var cond = (QilTernary)n;
                    cond.Center = ToNodes31(cond.Center);
                    cond.Right = ToNodes31(cond.Right);
                    n.XmlType = QilTypeChecker.CheckConditional(cond);
                    return n;
                default:
                    return _f.TypeAssert(n, n.XmlType.IsSingleton ? T.NodeNotRtf : T.NodeNotRtfS);
            }
        }

        // Whether an expression's value is statically nodes only.
        private bool IsNodeExpression(string expression, bool contextMayBeAtomic, Scope31 scope)
        {
            Expr ast;
            try
            {
                ast = Parser.ParseXPath(expression);
            }
            catch (System.Exception)
            {
                return false; // the error is reported when the expression is compiled
            }
            return IsNodeExpression(ast, contextMayBeAtomic, scope, 0);
        }

        private bool IsNodeExpression(Expr e, bool contextMayBeAtomic, Scope31 scope, int depth)
        {
            if (depth > 8)
            {
                return false;
            }
            switch (e)
            {
                case AxisStep:
                case RootExpr:
                case EmptySequenceExpr:
                case BinaryExpr { Op: BinaryOp.Union or BinaryOp.Intersect or BinaryOp.Except }:
                    return true;
                case ContextItemExpr:
                    return !contextMayBeAtomic;
                case PathExpr p:
                    // The left side must be nodes; the result is nodes when the last step is.
                    return IsNodeExpression(p.Right, contextMayBeAtomic: false, scope, depth + 1);
                case FilterExpr fe:
                    return IsNodeExpression(fe.Base, contextMayBeAtomic, scope, depth + 1);
                case IfExpr i:
                    return IsNodeExpression(i.Then, contextMayBeAtomic, scope, depth + 1) && IsNodeExpression(i.Else, contextMayBeAtomic, scope, depth + 1);
                case SequenceExpr s:
                    foreach (Expr item in s.Items)
                    {
                        if (!IsNodeExpression(item, contextMayBeAtomic, scope, depth + 1))
                        {
                            return false;
                        }
                    }
                    return true;
                case XPath31.FunctionCall { Name.Prefix: null or "fn", Name.LocalName: var name, Arguments.Count: var count }:
                    return name switch
                    {
                        "key" or "id" or "idref" or "element-with-id" or "doc" or "document" or "root" or "outermost" or "innermost" => true,
                        "current" => !contextMayBeAtomic,
                        _ => false,
                    };
                case VarRef v when v.Name.Prefix == null && depth < 4:
                    XslNode? decl = scope.Lookup(v.Name.LocalName, "") ?? GlobalDeclaration(v.Name.LocalName);
                    if (decl == null || decl.NodeType == XslNodeType.Param || decl.V2?.As != null)
                    {
                        return false; // a parameter's value comes from outside; as="…" isn't analysed
                    }
                    // A variable with content and no select is a temporary tree.
                    return decl.Select == null || IsNodeExpression(Parse(decl.Select), contextMayBeAtomic, scope, depth + 4);
                default:
                    return false;
            }
        }

        private static Expr Parse(string expression)
        {
            try
            {
                return Parser.ParseXPath(expression);
            }
            catch (System.Exception)
            {
                return new ContextItemExpr();
            }
        }

        private XslNode? GlobalDeclaration(string local)
        {
            foreach (VarPar v in _compiler.GlobalVars)
            {
                if (v.Name!.LocalName == local && v.Name.NamespaceUri.Length == 0)
                {
                    return v;
                }
            }
            foreach (VarPar v in _compiler.ExternalPars)
            {
                if (v.Name!.LocalName == local && v.Name.NamespaceUri.Length == 0)
                {
                    return v;
                }
            }
            return null;
        }
    }
}
