// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31
{
    /// <summary>One alternative of an XSLT pattern: the condition on the node being matched, and its default priority.</summary>
    internal sealed record PatternAlternative(QilIterator Node, QilNode Condition, double Priority);

    internal sealed partial class XPath31Compiler
    {
        /// <summary>
        /// Compiles an XSLT pattern (XSLT 3.0 §5.5) to one condition per top-level alternative. Each condition tests
        /// the iterator <paramref name="newNode"/> returned for it; a condition starts with the node kind and name
        /// tests, as (And (And (IsType n kind) (Eq (NameOf n) qname)) rest), which the template matcher indexes.
        /// </summary>
        /// <remarks>
        /// A path pattern is matched from its last step upwards: the node must pass the last step's node test and
        /// predicates, and its parent (for "/") or one of its ancestors (for "//") must match the rest. Predicates
        /// that may depend on the position are tested by selecting the step from the parent; patterns outside
        /// the forms recognised here are evaluated as expressions from each ancestor-or-self (the definition).
        /// </remarks>
        /// <param name="newNode">The iterator for an alternative; its argument is whether the alternative is a
        /// predicate pattern (".", ".[…]"), which matches any item, not only nodes.</param>
        public static List<PatternAlternative> CompilePattern(Expr ast, XPathStaticContext sc, QilPatternFactory f, IXPath31Environment env, System.Func<bool, QilIterator> newNode)
        {
            var compiler = new XPath31Compiler(sc, f, env);
            var result = new List<PatternAlternative>();
            if (ast is BinaryExpr { Op: BinaryOp.Union } && Alternatives(ast).Any(IsPredicatePattern))
            {
                // Pattern30 ::= PredicatePattern | UnionExprP: a predicate pattern is a whole pattern.
                throw new XPathStaticException("XTSE0340", "A predicate pattern can't be an operand of a union", ast.Span.Start);
            }
            foreach (Expr alternative in Alternatives(ast))
            {
                QilIterator node = newNode(IsPredicatePattern(alternative));
                QilNode condition = compiler.PatternCondition(alternative, node);
                result.Add(new PatternAlternative(node, condition, DefaultPriority(alternative)));
            }
            return result;
        }

        /// <summary>A predicate pattern (XSLT 3.0 §5.5.2): "." with zero or more predicates.</summary>
        public static bool IsPredicatePattern(Expr e) => Innermost(e) is ContextItemExpr;

        // The base of a filter expression's filters (".[a][b]" is ".").
        private static Expr Innermost(Expr e)
        {
            while (e is FilterExpr fe)
            {
                e = fe.Base;
            }
            return e;
        }

        /// <summary>Whether some alternative of a pattern is a predicate pattern (so may match items that aren't nodes).</summary>
        /// <summary>
        /// Xsl3 (P4): whether an expression is a relative path of child steps (<c>order</c>, <c>a/b[@x]</c>): from one
        /// context node, its nodes are in document order, distinct and all at one depth, and so is any subsequence
        /// of them in their order (such as a group of xsl:for-each-group).
        /// </summary>
        public static bool IsChildPath(string? expression)
        {
            if (expression == null)
            {
                return false; // (a missing select, reported by the loader)
            }
            try
            {
                return IsChildSteps(Parser.ParseXPath(expression));
            }
            catch (XPathStaticException)
            {
                return false;
            }

            static bool IsChildSteps(Expr e) => e switch
            {
                AxisStep { Axis: Axis.Child } => true,
                PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Child } } p => IsChildSteps(p.Left),
                _ => false,
            };
        }

        /// <summary>Xsl3 (P4): whether an expression is a call of fn:number (whose value is always one xs:double).</summary>
        public static bool IsNumberCall(string expression)
        {
            try
            {
                return Parser.ParseXPath(expression) is FunctionCall { Name: { Prefix: null or "fn", LocalName: "number" }, Arguments.Count: <= 1 };
            }
            catch (XPathStaticException)
            {
                return false;
            }
        }

        public static bool MatchesItems(string pattern)
        {
            try
            {
                return Alternatives(Parser.ParseXPath(pattern)).Any(IsPredicatePattern);
            }
            catch (XPathStaticException)
            {
                return false;
            }
        }

        private static IEnumerable<Expr> Alternatives(Expr e) =>
            e is BinaryExpr { Op: BinaryOp.Union } u ? Alternatives(u.Left).Concat(Alternatives(u.Right)) : new[] { e };

        // XSLT 3.0 §6.5: the default priority of a pattern alternative.
        private static double DefaultPriority(Expr e)
        {
            if (e is RootExpr)
            {
                return -0.5;
            }
            if (IsPredicatePattern(e))
            {
                return e is ContextItemExpr ? -1 : 1;
            }
            if (e is not AxisStep { Predicates.Count: 0 } step || step.Axis is not (Axis.Child or Axis.Attribute or Axis.Namespace))
            {
                return 0.5;
            }
            return step.Test switch
            {
                NameTest { LocalName: not null, AnyNamespace: false } => 0,
                NameTest { LocalName: null, Prefix: not null } or NameTest { LocalName: null, BracedUri: not null } => -0.25,
                NameTest { AnyNamespace: true, LocalName: not null } => -0.25,
                NameTest => -0.5,
                KindNodeTest { Kind: PITest { Name: not null } } => 0,
                KindNodeTest { Kind: SchemaElementTest or SchemaAttributeTest } => 0.25,
                KindNodeTest { Kind: ElementTest el } => KindPriority(el.Name != null, el.TypeName != null),
                KindNodeTest { Kind: AttributeTest at } => KindPriority(at.Name != null, at.TypeName != null),
                // document-node(E): the priority E would have (XSLT 3.0 §6.5), so document-node(element(*)) is -0.5.
                KindNodeTest { Kind: DocumentTest { ElementTest: SchemaElementTest } } => 0.25,
                KindNodeTest { Kind: DocumentTest { ElementTest: ElementTest el } } => KindPriority(el.Name != null, el.TypeName != null),
                _ => -0.5,
            };
        }

        // element(N, T) and attribute(N, T) are 0.25; with a name or a type, 0; with neither (or *), -0.5.
        private static double KindPriority(bool name, bool type) => name && type ? 0.25 : name || type ? 0 : -0.5;

        private QilNode PatternCondition(Expr alternative, QilIterator node)
        {
            switch (alternative)
            {
                // root(.)//(A except B) takes the difference for each context node: it is the difference of the
                // patterns only when the context doesn't matter (B is absolute) or is the same for both.
                case BinaryExpr { Op: BinaryOp.Intersect } b when IsAbsolute(b.Left) || IsAbsolute(b.Right) || SameDepth(b.Left, b.Right):
                    return _f.And(PatternCondition(b.Left, node), PatternCondition(b.Right, node));
                case BinaryExpr { Op: BinaryOp.Except } b when IsAbsolute(b.Right) || SameDepth(b.Left, b.Right):
                    return _f.And(PatternCondition(b.Left, node), _f.Not(PatternCondition(b.Right, node)));
                case BinaryExpr { Op: BinaryOp.Intersect or BinaryOp.Except } b:
                    if (!IsPattern30(b))
                    {
                        throw new XPathStaticException("XTSE0340", $"'{b}' is not a valid pattern", b.Span.Start);
                    }
                    return General(b, node);
                case BinaryExpr { Op: BinaryOp.Union } b:
                    return _f.Or(PatternCondition(b.Left, node), PatternCondition(b.Right, node));
            }
            // (a|b)[@x]: a relative pattern whose predicates don't count, which the node itself must satisfy.
            if (alternative is FilterExpr fe && Innermost(fe) is var inner && inner is not ContextItemExpr && !IsAbsolute(inner) &&
                Unwrap(fe) is var predicates && predicates.All(IsNonPositional) && IsPattern30(inner))
            {
                return _f.And(PatternCondition(inner, node), SelfPredicates(node, predicates));
            }
            // A parenthesized union as a step (x/(a|b)) is the union of the paths through each operand.
            List<Expr> paths = Distribute(alternative);
            if (paths.Count > 1)
            {
                return paths.Select(path => PatternCondition(path, node)).Aggregate(_f.Or);
            }
            var steps = new List<(Expr Step, bool Descendants)>();
            if (Flatten(alternative, steps))
            {
                return MatchStep(steps, steps.Count - 1, node);
            }
            if (!IsPattern30(alternative))
            {
                throw new XPathStaticException("XTSE0340", $"'{alternative}' is not a valid pattern", alternative.Span.Start);
            }
            return General(alternative, node);
        }

        // A path pattern that starts at "/" or a rooted function call or variable: it doesn't depend on the context.
        private static bool IsAbsolute(Expr e) => e switch
        {
            PathExpr p => IsAbsolute(p.Left),
            RootExpr => true,
            BinaryExpr { Op: BinaryOp.Union or BinaryOp.Intersect or BinaryOp.Except } b => IsAbsolute(b.Left) && IsAbsolute(b.Right),
            _ => IsRooted30(e),
        };

        // Two relative paths of child, attribute, namespace and self steps that go down the same number of levels.
        private static bool SameDepth(Expr a, Expr b) => Depth(a) is int d && Depth(b) == d;

        private static int? Depth(Expr e) => e switch
        {
            PathExpr { Descendants: false } p => Depth(p.Left) + Depth(p.Right),
            AxisStep { Axis: Axis.Child or Axis.Attribute or Axis.Namespace } => 1,
            AxisStep { Axis: Axis.Self } => 0,
            _ => null,
        };

        // The union-free paths of a path pattern: P/(A|B)/C is P/A/C | P/B/C.
        private static List<Expr> Distribute(Expr e)
        {
            switch (e)
            {
                case BinaryExpr { Op: BinaryOp.Union } u:
                    return Distribute(u.Left).Concat(Distribute(u.Right)).ToList();
                case PathExpr p:
                    List<Expr> left = Distribute(p.Left), right = Distribute(p.Right);
                    if (left.Count == 1 && right.Count == 1)
                    {
                        return new List<Expr> { e };
                    }
                    return left.SelectMany(l => right.Select(r => Join(l, r, p.Descendants, e))).ToList();
                default:
                    return new List<Expr> { e };
            }
        }

        // left/right (or left//right), with right's own steps re-associated to the left.
        private static Expr Join(Expr left, Expr right, bool descendants, Expr at) => right is PathExpr p
            ? new PathExpr(Join(left, p.Left, descendants, at), p.Right, p.Descendants) { Span = at.Span }
            : new PathExpr(left, right, descendants) { Span = at.Span };

        // Pattern30 (XSLT 3.0 §5.5.2) other than predicate patterns: unions, intersections and differences of
        // path patterns, which start with "/", "//", a rooted function call or variable, or a relative path.
        private static bool IsPattern30(Expr e) => e switch
        {
            BinaryExpr { Op: BinaryOp.Union or BinaryOp.Intersect or BinaryOp.Except } b => IsPattern30(b.Left) && IsPattern30(b.Right),
            _ => IsPathPattern30(e, leftmost: true),
        };

        private static bool IsPathPattern30(Expr e, bool leftmost) => e switch
        {
            PathExpr { Left: RootExpr } p when leftmost => IsPathPattern30(p.Right, leftmost: false),
            PathExpr p => IsPathPattern30(p.Left, leftmost) && IsStepPattern30(p.Right),
            RootExpr => leftmost,
            _ when leftmost && IsRooted30(e) => true,
            _ => IsStepPattern30(e),
        };

        // StepExprP: a forward axis step with predicates, or a parenthesized pattern with predicates.
        private static bool IsStepPattern30(Expr e) => e switch
        {
            AxisStep s => s.Axis is Axis.Child or Axis.Attribute or Axis.Self or Axis.Descendant or Axis.DescendantOrSelf or Axis.Namespace,
            PathExpr p => IsPathPattern30(p, leftmost: false),
            FilterExpr fe => Innermost(fe) is not ContextItemExpr and var inner && (IsPattern30(inner) || IsRooted30(inner)),
            BinaryExpr { Op: BinaryOp.Union or BinaryOp.Intersect or BinaryOp.Except } => IsPattern30(e),
            _ => false,
        };

        // A rooted path's start: $v or doc(), id(), element-with-id(), key() or root() with literal or variable
        // arguments, with predicates.
        private static bool IsRooted30(Expr e) => Innermost(e) switch
        {
            VarRef => true,
            XPath31.FunctionCall { Name: { Prefix: null or "fn" } n, Arguments: var a } => a.All(IsLiteralOrVariable) && (n.LocalName, a.Count) switch
            {
                ("doc", 1) or ("id" or "element-with-id", 1 or 2) or ("key", 2 or 3) or ("root", 0 or 1) => true,
                _ => false,
            },
            _ => false,
        };

        // The steps of a path pattern, left to right, each with whether "//" precedes it. False if it isn't one.
        private static bool Flatten(Expr e, List<(Expr Step, bool Descendants)> steps)
        {
            switch (e)
            {
                case PathExpr p:
                    if (!Flatten(p.Left, steps) || !IsStep(p.Right))
                    {
                        return false;
                    }
                    steps.Add((p.Right, p.Descendants));
                    return true;
                case RootExpr:
                case XPath31.FunctionCall:
                case VarRef:
                case ContextItemExpr:
                case FilterExpr:
                case BinaryExpr { Op: BinaryOp.Union or BinaryOp.Intersect or BinaryOp.Except }:
                    steps.Add((e, false));
                    return IsRoot(e);
                case AxisStep:
                    steps.Add((e, false));
                    return IsStep(e);
                default:
                    return false;
            }
        }

        private static bool IsStep(Expr e) => e is AxisStep { Axis: Axis.Child or Axis.Attribute or Axis.Self or Axis.Descendant or Axis.DescendantOrSelf or Axis.Namespace };

        // What may start a path pattern besides a step: "/", id(), key(), doc(), element-with-id(), root(), $var, ".",
        // a parenthesised pattern, and those with predicates.
        private static bool IsRoot(Expr e) => e switch
        {
            RootExpr or VarRef or ContextItemExpr => true,
            // id() and key() take literals and variable references (XSLT 3.0 §5.5.2).
            XPath31.FunctionCall { Name: { Prefix: null or "fn", LocalName: "key" }, Arguments: var a } => a.Count == 2 && a[0] is StringLiteral && IsLiteralOrVariable(a[1]),
            XPath31.FunctionCall { Name: { Prefix: null or "fn", LocalName: "id" or "element-with-id" }, Arguments: var a } => a.Count is 1 or 2 && a.All(IsLiteralOrVariable),
            XPath31.FunctionCall { Name: { Prefix: null or "fn", LocalName: "doc" }, Arguments: var a } => a.Count == 1 && IsLiteralOrVariable(a[0]),
            XPath31.FunctionCall { Name: { Prefix: null or "fn", LocalName: "root" }, Arguments: var a } => a.Count == 0 || a.Count == 1 && a[0] is VarRef,
            // (/a|$v)[1]: absolute patterns are expressions too; a relative one, (a|b)[1], counts from the parent.
            FilterExpr fe => IsRoot(fe.Base) || Innermost(fe) is var inner && IsAbsolute(inner) && IsPattern30(inner),
            BinaryExpr { Op: BinaryOp.Union or BinaryOp.Intersect or BinaryOp.Except } b => IsAbsolute(b) && IsPattern(b.Left) && IsPattern(b.Right),
            _ => false,
        };

        private static bool IsLiteralOrVariable(Expr e) => e is StringLiteral or IntegerLiteral or DecimalLiteral or DoubleLiteral or VarRef;

        private static bool IsPattern(Expr e) =>
            e is BinaryExpr { Op: BinaryOp.Union or BinaryOp.Intersect or BinaryOp.Except } b ? IsPattern(b.Left) && IsPattern(b.Right)
            : Flatten(e, new List<(Expr, bool)>());

        // Whether `node` matches steps[0..k], with steps[k] matching the node itself.
        private QilNode MatchStep(List<(Expr Step, bool Descendants)> steps, int k, QilIterator node)
        {
            Expr step = steps[k].Step;
            if (step is RootExpr)
            {
                return _f.IsType(node, T.Document);
            }
            if (step is not AxisStep axisStep)
            {
                // A pattern root that is an expression ($v, key(…), (a|b), …): the node is one of its nodes. The
                // expression is evaluated with the node as the context item (whose root key() and id() search).
                return k == 0 && step is ContextItemExpr ? _f.True()
                    : k == 0 && step is FilterExpr fe0 && IsPredicatePattern(fe0) ? SelfPredicates(node, Unwrap(fe0))
                    : IsOneOf(node, () => OnlyNodes(Build(step)), node);
            }

            var (kinds, nameTest) = NodeTestParts(axisStep.Axis, axisStep.Test, axisStep.ExplicitAxis);
            QilNode condition = kinds == 0 ? _f.False() : _f.IsType(node, T.NodeChoice(kinds));
            if (nameTest != null)
            {
                condition = _f.And(condition, nameTest(node));
            }

            if (axisStep.Predicates.Count > 0)
            {
                if (axisStep.Predicates.All(IsNonPositional) || axisStep.Axis == Axis.Self)
                {
                    condition = _f.And(condition, SelfPredicates(node, axisStep.Predicates));
                }
                else if (axisStep.Axis is Axis.Child or Axis.Attribute or Axis.Namespace)
                {
                    // The predicates count the node's siblings: select the step from the parent and look for the node.
                    QilIterator parent = _f.For(_f.Parent(node));
                    condition = _f.And(condition, _f.Not(_f.IsEmpty(_f.Loop(parent,
                        IsOneOfSequence(node, WithFocusItem(parent, () => StepFor(axisStep.Axis, axisStep.Test, axisStep.Predicates)))))));
                }
                else
                {
                    return General(steps, node);
                }
            }

            // The rest of the pattern, matched by the parent, an ancestor, or the node itself.
            if (k == 0)
            {
                // A relative pattern: the first step's axis from some node of the tree (child-or-top for child::).
                return axisStep.Axis == Axis.Descendant ? _f.And(condition, _f.Not(_f.IsEmpty(_f.Parent(node)))) : condition;
            }
            bool descendants = steps[k].Descendants;
            QilNode up = axisStep.Axis switch
            {
                Axis.Self when !descendants => MatchStep(steps, k - 1, node),
                Axis.Self or Axis.DescendantOrSelf => Upwards(steps, k - 1, _f.AncestorOrSelf(node)),
                Axis.Descendant => Upwards(steps, k - 1, _f.Ancestor(node)),
                _ when descendants => Upwards(steps, k - 1, _f.Ancestor(node)),
                _ => Upwards(steps, k - 1, _f.Parent(node)),
            };
            return _f.And(condition, up);
        }

        private static IReadOnlyList<Expr> Unwrap(FilterExpr fe)
        {
            var predicates = new List<Expr>();
            Expr e = fe;
            while (e is FilterExpr f)
            {
                predicates.Insert(0, f.Predicate);
                e = f.Base;
            }
            return predicates;
        }

        // Some node of `candidates` matches steps[0..k].
        private QilNode Upwards(List<(Expr Step, bool Descendants)> steps, int k, QilNode candidates)
        {
            QilIterator it = _f.For(candidates);
            return _f.Not(_f.IsEmpty(_f.Filter(it, MatchStep(steps, k, it))));
        }

        // Predicates on the node itself (a singleton focus): position() and last() are 1.
        private QilNode SelfPredicates(QilIterator node, IReadOnlyList<Expr> predicates)
        {
            Focus? saved = _focus;
            _focus = new Focus(node, () => _f.Double(1), () => _f.Double(1));
            try
            {
                QilNode result = _f.True();
                foreach (Expr p in predicates)
                {
                    result = _f.And(result, PredicateCondition(p));
                }
                return result;
            }
            finally
            {
                _focus = saved;
            }
        }

        // The definition (XSLT 3.0 §5.5.3): the node is selected by the pattern as an expression from some
        // ancestor-or-self of it.
        private QilNode General(List<(Expr Step, bool Descendants)> steps, QilIterator node)
        {
            Expr path = steps[0].Step;
            for (int i = 1; i < steps.Count; i++)
            {
                path = new PathExpr(path, steps[i].Step, steps[i].Descendants);
            }
            return General(path, node);
        }

        // The definition (XSLT 3.0 §5.5.3): the node is one of the pattern's nodes evaluated from an ancestor-or-self.
        private QilNode General(Expr path, QilIterator node)
        {
            path = OrTop(path);
            QilIterator m = _f.For(_f.AncestorOrSelf(node));
            return _f.Not(_f.IsEmpty(_f.Filter(m, IsOneOf(node, () => OnlyNodes(Build(path)), m))));
        }

        // The equivalent expression's first steps: child::T (or attribute::T) of a relative path is child-or-top::T,
        // which also selects a parentless context node: here child::T | self::T[empty(..)] (§5.5.3).
        private static Expr OrTop(Expr e)
        {
            switch (e)
            {
                case BinaryExpr { Op: BinaryOp.Union or BinaryOp.Intersect or BinaryOp.Except } b:
                    return b with { Left = OrTop(b.Left), Right = OrTop(b.Right) };
                case PathExpr p:
                    return p with { Left = OrTop(p.Left) };
                case AxisStep { Axis: Axis.Child, Test: not KindNodeTest { Kind: DocumentTest } } s:
                    var parentless = Parser.ParseXPath("empty(..) and not(. instance of document-node())");
                    var top = s with { Axis = Axis.Self, Predicates = new[] { parentless }.Concat(s.Predicates).ToList() };
                    return new BinaryExpr(BinaryOp.Union, s, top) { Span = s.Span };
                default:
                    return e;
            }
        }

        // The nodes of a pattern's expression: the other items can't be the node matched (an error in a pattern is
        // a non-match, so $v needn't hold nodes only).
        private QilNode OnlyNodes(QilNode n)
        {
            if (n.XmlType!.IsSubtypeOf(T.NodeNotRtfS))
            {
                return n;
            }
            QilIterator x = _f.For(Items(n));
            return _f.TypeAssert(_f.Filter(x, _f.IsType(x, T.NodeNotRtf)), T.NodeNotRtfS);
        }

        // `node` is one of the nodes `nodes` builds with `focus` as the context item.
        private QilNode IsOneOf(QilIterator node, System.Func<QilNode> nodes, QilIterator focus) =>
            _f.Not(_f.IsEmpty(IsOneOfSequence(node, WithFocusItem(focus, nodes))));

        private QilNode IsOneOfSequence(QilIterator node, QilNode nodes)
        {
            QilIterator x = _f.For(nodes);
            return _f.Filter(x, _f.Is(x, node));
        }

        private QilNode WithFocusItem(QilNode item, System.Func<QilNode> body)
        {
            Focus? saved = _focus;
            _focus = new Focus(item, () => _f.Double(1), () => _f.Double(1));
            try
            {
                return body();
            }
            finally
            {
                _focus = saved;
            }
        }

        // A predicate whose value can't be a number (a number would compare with the position) and that doesn't
        // ask for the position or size: it can be tested on the node alone.
        private static bool IsNonPositional(Expr p)
        {
            string text = p.ToString();
            if (text.Contains("position(", System.StringComparison.Ordinal) || text.Contains("last(", System.StringComparison.Ordinal))
            {
                return false;
            }
            return p switch
            {
                BinaryExpr { Op: BinaryOp.Or or BinaryOp.And or >= BinaryOp.GeneralEq and <= BinaryOp.Follows or BinaryOp.Union or BinaryOp.Intersect or BinaryOp.Except } => true,
                AxisStep or RootExpr or ContextItemExpr or StringLiteral or QuantifiedExpr or InstanceOfExpr or CastableExpr => true,
                PathExpr { Right: AxisStep } => true,
                FilterExpr fe => IsNonPositional(fe.Base),
                XPath31.FunctionCall { Name.Prefix: null or "fn", Name.LocalName: var name } => s_booleanOrStringFunctions.Contains(name),
                _ => false,
            };
        }

        private static readonly HashSet<string> s_booleanOrStringFunctions = new(System.StringComparer.Ordinal)
        {
            "not", "exists", "empty", "boolean", "true", "false", "contains", "starts-with", "ends-with", "matches", "lang",
            "deep-equal", "has-children", "nilled", "string", "name", "local-name", "namespace-uri", "normalize-space",
            "concat", "string-join", "substring", "substring-before", "substring-after", "upper-case", "lower-case",
            "translate", "key", "id", "current", "generate-id", "element-available", "function-available", "type-available",
        };

        /// <summary>The node kinds a step's node test admits on its axis, and its name condition, if any.</summary>
        private (XmlNodeKindFlags Kinds, System.Func<QilIterator, QilNode>? Name) NodeTestParts(Axis axis, NodeTest test, bool explicitAxis)
        {
            XmlNodeKindFlags kinds;
            System.Func<QilIterator, QilNode>? condition = null;
            switch (test)
            {
                case NameTest n:
                {
                    kinds = PrincipalKind(axis);
                    string defaultNs = kinds == XmlNodeKindFlags.Element ? _sc.DefaultElementNamespace : "";
                    string? ns = n.AnyNamespace ? null
                        : n.BracedUri ?? (n.Prefix != null ? _types.Expand(new LexicalQName(n.Prefix, "x"), "").NamespaceUri
                        : n.LocalName == null ? null : defaultNs);
                    string? local = n.LocalName;
                    if (ns != null || local != null)
                    {
                        condition = it =>
                            ns != null && local != null ? _f.Eq(_f.NameOf(it), _f.QName(local, ns)) :
                            ns != null ? _f.Eq(_f.NamespaceUriOf(it), _f.String(ns)) :
                            _f.Eq(_f.LocalNameOf(it), _f.String(local!));
                    }
                    break;
                }
                case KindNodeTest { Kind: var kind }:
                {
                    NodeKindTest resolved = _types.ResolveKind(kind);
                    kinds = kind switch
                    {
                        AnyKindTest => XmlNodeKindFlags.Any,
                        DocumentTest => XmlNodeKindFlags.Document,
                        ElementTest or SchemaElementTest => XmlNodeKindFlags.Element,
                        AttributeTest or SchemaAttributeTest => XmlNodeKindFlags.Attribute,
                        TextTest => XmlNodeKindFlags.Text,
                        CommentTest => XmlNodeKindFlags.Comment,
                        PITest => XmlNodeKindFlags.PI,
                        NamespaceNodeTest => XmlNodeKindFlags.Namespace,
                        _ => XmlNodeKindFlags.Any,
                    };
                    if (!resolved.AnyType)
                    {
                        return (0, null); // element(N, T) where T excludes untyped nodes
                    }
                    if (resolved.DocumentElement != null)
                    {
                        int id = ConstantPool.Add(new SequenceTypeTest(resolved, Cardinality.ExactlyOne));
                        condition = it => Call(nameof(XsltLibrary.X31InstanceOf), T.BooleanX, it, _f.Double(id));
                    }
                    else if (resolved.LocalName != null || resolved.NamespaceUri != null)
                    {
                        string? ns = resolved.NamespaceUri, local = resolved.LocalName;
                        condition = it =>
                            ns != null && local != null ? _f.Eq(_f.NameOf(it), _f.QName(local, ns)) :
                            local != null ? _f.Eq(_f.LocalNameOf(it), _f.String(local)) :
                            _f.Eq(_f.NamespaceUriOf(it), _f.String(ns!));
                    }
                    break;
                }
                default:
                    throw new System.InvalidOperationException();
            }
            // A pattern's child:: step admits the nodes a child axis can select (and is "child-or-top": a
            // parentless node too); self:: and descendant-or-self:: admit any kind the test does.
            XmlNodeKindFlags axisKinds = axis switch
            {
                Axis.Attribute => XmlNodeKindFlags.Attribute,
                Axis.Namespace => XmlNodeKindFlags.Namespace,
                Axis.Self or Axis.DescendantOrSelf => XmlNodeKindFlags.Any,
                // document-node() as a pattern step matches a document node (which can only be the first step).
                _ when test is KindNodeTest { Kind: DocumentTest } && !explicitAxis => XmlNodeKindFlags.Document,
                _ => XmlNodeKindFlags.Content,
            };
            return (kinds & axisKinds, condition);
        }
    }
}
