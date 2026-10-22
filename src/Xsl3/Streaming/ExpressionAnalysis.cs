// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using Xsl3.XPath31;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

/// <summary>
/// P7 S1: expression streamability under XSLT 3.0 §19, over the existing XPath AST. The containing
/// declaration supplies static context through options, via ConstructAnalysis in the stylesheet compiler.
/// </summary>
internal sealed partial class ExpressionAnalysis
{
    private readonly Dictionary<Expr, Streamability> _results = new(ReferenceEqualityComparer.Instance);
    private readonly ExpressionAnalysisOptions _options;
    private readonly Dictionary<QName, Streamability> _variables = new();
    private StreamabilityContext _outerContext;
    private int _higherOrderDepth;
    private int _focusDepth;
    private bool _recordResults = true;

    internal ExpressionAnalysis(ExpressionAnalysisOptions? options = null) => _options = options ?? new();

    internal IReadOnlyDictionary<Expr, Streamability> Results => _results;

    internal Streamability Analyze(Expr expression, StreamabilityContext context)
    {
        Reset(context);
        return Visit(expression, context);
    }

    internal bool IsMotionlessPattern(Expr pattern)
    {
        var context = new StreamabilityContext(UType.Document, Posture.Striding);
        Reset(new(UType.Any, Posture.Grounded));
        UType matchType = Visit(pattern, new(UType.Any, Posture.Grounded)).Type;
        Reset(context);
        _outerContext = new(matchType, Posture.Striding);
        return !CapturesStreamedVariable(pattern, new()) && (XPath31Compiler.IsPredicatePattern(pattern)
            ? MotionlessPredicatePattern(pattern) : ScanningPattern(pattern, context));
    }

    private void Reset(StreamabilityContext context)
    {
        // A parsed AST can be shared by compilation's parser cache. Assess it afresh for this context.
        _results.Clear();
        _variables.Clear();
        _signatures.Clear();
        _hostFunctions.Clear();
        foreach (var (name, variable) in _options.Variables)
        {
            _variables[name] = (variable.StreamingParameter ?? Grounded(UTypes.FromSequence(variable.Type), "19.8.8.12"))
                with { ItemType = variable.Type.Item };
        }
        _outerContext = context;
        _higherOrderDepth = _focusDepth = 0;
    }

    private Streamability Visit(Expr expression, StreamabilityContext context, bool predicate = false)
    {
        Streamability result = expression switch
        {
            AxisStep step => AxisStep_19_8_8_9(step, context),
            PathExpr path => Path_19_8_8_8(path, context),
            SimpleMapExpr map => SimpleMap_19_8_8_7(map, context),
            ContextItemExpr => new(context.Type, context.Posture, Sweep.Motionless, "19.8.8.13") { ItemType = context.ItemType },
            RootExpr => Root_19_8_9_18(new ContextItemExpr(), context) with { Type = UType.Document },
            SequenceExpr sequence => General_19_8_1(SequenceType(sequence.Items, context, out AnalysisOperand[] operands), "19.8.1", operands),
            BinaryExpr binary => Binary_19_8_8(binary, context, predicate),
            UnaryExpr unary => General_19_8_1(UType.Numeric, "19.8.1", new AnalysisOperand(Visit(unary.Operand, context), OperandUsage.Absorption)),
            VarRef variable => Variable_19_8_8_12(variable),
            LetExpr let => Bindings_19_8_8(let.Bindings, 0, let.Return, context, BindingKind.Let),
            ForExpr loop => Bindings_19_8_8(loop.Bindings, 0, loop.Return, context, BindingKind.For),
            QuantifiedExpr quantified => Bindings_19_8_8(quantified.Bindings, 0, quantified.Satisfies, context, BindingKind.Quantified),
            IfExpr conditional => If_19_8_8_3(conditional, context),
            InstanceOfExpr instance => TypeTest_19_8_8(instance.Operand, instance.Type, context, false),
            TreatExpr treat => TypeTest_19_8_8(treat.Operand, treat.Type, context, true),
            CastExpr cast => General_19_8_1(CastType(cast.Type.TypeName), "19.8.1", new AnalysisOperand(Visit(cast.Operand, context), OperandUsage.Absorption)),
            CastableExpr cast => Castable(cast, context),
            FilterExpr filter => Filter_19_8_8_10(filter, context),
            FunctionCall call => Function_19_8_8_14(call, context),
            NamedFunctionRef reference => NamedFunction_19_8_8_15(reference, context),
            InlineFunctionExpr inline => InlineFunction_19_8_8_16(inline),
            DynamicFunctionCall call => DynamicCall_19_8_8_11(call, context),
            ArrowExpr arrow => Arrow_19_8_8(arrow, context),
            MapConstructor map => Map_19_8_8_17(map, context),
            SquareArrayConstructor array => Array_19_8_8(array.Members, context),
            CurlyArrayConstructor array => Array_19_8_8(array.Content == null ? Array.Empty<Expr>() : new[] { array.Content }, context),
            PostfixLookup lookup => Lookup_19_8_8_18(lookup.Base, lookup.Key, context),
            UnaryLookup lookup => Lookup_19_8_8_18(new ContextItemExpr(), lookup.Key, context),
            EmptySequenceExpr => new(UType.Empty, Posture.Grounded, Sweep.Motionless, "19.8.1"),
            IntegerLiteral or DecimalLiteral => new(UType.Decimal, Posture.Grounded, Sweep.Motionless, "19.8.1"),
            DoubleLiteral => new(UType.Double, Posture.Grounded, Sweep.Motionless, "19.8.1"),
            StringLiteral => new(UType.String, Posture.Grounded, Sweep.Motionless, "19.8.1"),
            _ => throw new NotSupportedException($"Streamability analysis of {expression.GetType().Name} is not implemented"),
        };
        if (_recordResults) _results[expression] = result;
        return result;
    }

    private static UType AxisType_19_1_1(AxisStep step, UType contextType)
    {
        UType nodes = contextType & UType.Nodes;
        if (nodes == UType.Empty)
        {
            return UType.Empty;
        }
        UType reachable = step.Axis switch
        {
            Axis.Self => nodes,
            Axis.Attribute => (nodes & UType.Element) != 0 ? UType.Attribute : UType.Empty,
            Axis.Namespace => (nodes & UType.Element) != 0 ? UType.Namespace : UType.Empty,
            Axis.Child or Axis.Descendant => Children(nodes),
            Axis.DescendantOrSelf => nodes | Children(nodes),
            Axis.Parent or Axis.Ancestor => Ancestors(nodes),
            Axis.AncestorOrSelf => nodes | Ancestors(nodes),
            Axis.Following or Axis.FollowingSibling or Axis.Preceding or Axis.PrecedingSibling =>
                nodes == UType.Document ? UType.Empty : UType.ChildNodes,
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };
        return reachable & NodeTestType(step.Test, step.Axis);
    }

    private static UType Children(UType nodes) => (nodes & UType.ParentNodes) != 0 ? UType.ChildNodes : UType.Empty;

    private static UType Ancestors(UType nodes) => nodes == UType.Document ? UType.Empty : UType.ParentNodes;

    private static UType NodeTestType(NodeTest test, Axis axis) => test switch
    {
        // Namespace nodes have names in no namespace (XDM §6.4.1). A prefixed name test
        // necessarily names a non-empty namespace; Q{} and *:local can still select them.
        NameTest { Prefix: not null } or NameTest { BracedUri.Length: > 0 } when axis == Axis.Namespace => UType.Empty,
        NameTest => axis switch
        {
            Axis.Attribute => UType.Attribute,
            Axis.Namespace => UType.Namespace,
            _ => UType.Element,
        },
        KindNodeTest { Kind: AnyKindTest } => UType.Nodes,
        KindNodeTest { Kind: DocumentTest } => UType.Document,
        KindNodeTest { Kind: ElementTest or SchemaElementTest } => UType.Element,
        KindNodeTest { Kind: AttributeTest or SchemaAttributeTest } => UType.Attribute,
        KindNodeTest { Kind: TextTest } => UType.Text,
        KindNodeTest { Kind: CommentTest } => UType.Comment,
        KindNodeTest { Kind: PITest } => UType.ProcessingInstruction,
        KindNodeTest { Kind: NamespaceNodeTest } => UType.Namespace,
        _ => throw new NotSupportedException($"No U-type for node test {test}"),
    };

    private Streamability AxisStep_19_8_8_9(AxisStep step, StreamabilityContext context)
    {
        UType type = AxisType_19_1_1(step, context.Type);
        (Posture posture, Sweep sweep) = context.Posture switch
        {
            Posture.Grounded => (Posture.Grounded, Sweep.Motionless),
            Posture.Roaming => (Posture.Roaming, Sweep.FreeRanging),
            _ when type == UType.Empty => (Posture.Grounded, Sweep.Motionless),
            Posture.Climbing when step.Axis is Axis.Self or Axis.Parent or Axis.Ancestor or Axis.AncestorOrSelf =>
                (Posture.Climbing, Sweep.Motionless),
            Posture.Climbing when step.Axis is Axis.Attribute or Axis.Namespace => (Posture.Striding, Sweep.Motionless),
            Posture.Striding or Posture.Crawling when step.Axis is Axis.Parent or Axis.Ancestor or Axis.AncestorOrSelf =>
                (Posture.Climbing, Sweep.Motionless),
            Posture.Striding when step.Axis is Axis.Self or Axis.Attribute or Axis.Namespace =>
                (Posture.Striding, Sweep.Motionless),
            Posture.Striding when step.Axis == Axis.Child => (Posture.Striding, Sweep.Consuming),
            Posture.Striding when step.Axis is Axis.Descendant or Axis.DescendantOrSelf =>
                ((type & UType.Element) != 0 ? Posture.Crawling : Posture.Striding, Sweep.Consuming),
            Posture.Crawling when step.Axis is Axis.Attribute or Axis.Namespace => (Posture.Striding, Sweep.Motionless),
            Posture.Crawling when step.Axis == Axis.Self =>
                ((type & UType.Element) != 0 ? Posture.Crawling : Posture.Striding, Sweep.Motionless),
            _ => (Posture.Roaming, Sweep.FreeRanging),
        };
        // Even when the axis verdict is fixed, record and validate all predicate expressions.
        var predicateContext = new StreamabilityContext(type, posture);
        Streamability[] predicates = step.Predicates.Select(p => FocusVisit(p, predicateContext, predicate: true)).ToArray();
        if (context.Posture is not (Posture.Grounded or Posture.Roaming) && type != UType.Empty)
        {
            // §19.8.8.9(4) precedes the non-motionless predicate test. For that test predicates
            // use the axis's provisional posture, including crawling for descendant element steps.
            if (context.Posture == Posture.Striding && step.Axis is Axis.Descendant or Axis.DescendantOrSelf
                && step.Predicates.Where((p, i) => IsFocusIndependentNumeric(p, predicates[i])).Any())
            {
                posture = Posture.Striding;
                sweep = Sweep.Consuming;
            }
            else if (predicates.Any(p => p.Sweep != Sweep.Motionless))
            {
                posture = Posture.Roaming;
                sweep = Sweep.FreeRanging;
            }
        }
        return new(type, posture, sweep, "19.8.8.9");
    }

    private Streamability Path_19_8_8_8(PathExpr path, StreamabilityContext context)
    {
        // §19.8.8.8 first expands E//F to (E/descendant-or-self::node())/F. Preserve the
        // original expression for the scanning-expression test (a leading dot is not a pattern).
        // E//child::N is exactly E/descendant::N when N has no predicates. Do not
        // move predicates: E//N[1] selects the first N child of each parent, not the first descendant N.
        bool rewrite = _options.AllowStreamabilityRewrites && path.Descendants
            && path.Right is AxisStep { Axis: Axis.Child, Predicates.Count: 0 };
        Expr rightExpr = rewrite ? ((AxisStep)path.Right) with { Axis = Axis.Descendant } : path.Right;
        Expr leftExpr = path.Descendants && !rewrite
            ? new PathExpr(path.Left, new AxisStep(Axis.DescendantOrSelf, new KindNodeTest(new AnyKindTest()), Array.Empty<Expr>()), false)
            : path.Left;
        Streamability left = Visit(leftExpr, context);
        Streamability right = FocusVisit(rightExpr, new(left.Type, left.Posture) { ItemType = left.ItemType });
        Posture posture = right.Posture;
        Sweep sweep = Wider(left.Sweep, right.Sweep);
        if (posture == Posture.Roaming && context.Posture is Posture.Striding or Posture.Crawling && IsScanningPath(path, context))
        {
            // §19.8.8.8 and §19.8.10: scan descendants and match a motionless pattern,
            // rather than revisiting descendants in a nested loop. This is consuming.
            posture = (right.Type & UType.Element) != 0 ? Posture.Crawling : Posture.Striding;
            sweep = Sweep.Consuming;
        }
        return new(right.Type, posture, sweep, "19.8.8.8") { ItemType = right.ItemType };
    }

    private Streamability SimpleMap_19_8_8_7(SimpleMapExpr map, StreamabilityContext context)
    {
        Streamability left = Visit(map.Left, context);
        Streamability right = FocusVisit(map.Right, new(left.Type, left.Posture) { ItemType = left.ItemType });
        return new(right.Type, right.Posture, Wider(left.Sweep, right.Sweep), "19.8.8.7") { ItemType = right.ItemType };
    }

    private bool IsScanningPath(Expr expression, StreamabilityContext context)
    {
        // Pattern checks use their own focus. Keep these speculative assessments out of the
        // side table: the emitter needs the expression's actual context, not a pattern's context.
        bool recording = _recordResults;
        _recordResults = false;
        try { return !CapturesStreamedVariable(expression, new()) && ScanningPattern(expression, context, _options.AllowStreamabilityRewrites); }
        finally { _recordResults = recording; }
    }

    private static Sweep Wider(Sweep left, Sweep right) => left > right ? left : right;
}
