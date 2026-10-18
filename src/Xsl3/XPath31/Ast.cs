// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Xsl3.XPath31
{
    // The XPath 3.1 abstract syntax tree (SPEC §9.2). It mirrors the EBNF of XPath 3.1 Appendix A closely
    // enough that each node names the production it came from, but drops purely syntactic levels
    // (parentheses, single-operand chains). Names are kept lexical (prefix + local, or a braced URI): they
    // are resolved by static analysis, which owns the namespace context and reports XPST0081.
    //
    // ToString() renders a fully parenthesised, canonical form, used by the parser tests and for debugging.

    /// <summary>A position in the expression text, for error messages and debug mapping.</summary>
    internal readonly record struct SourceSpan(int Start, int End)
    {
        public static readonly SourceSpan None = new(-1, -1);
    }

    /// <summary>A lexical QName: <c>prefix:local</c>, <c>local</c>, or <c>Q{uri}local</c>.</summary>
    internal sealed record LexicalQName(string? Prefix, string LocalName, string? BracedUri = null)
    {
        public bool IsUriQualified => BracedUri != null;

        public override string ToString() =>
            BracedUri != null ? $"Q{{{BracedUri}}}{LocalName}" :
            Prefix != null ? $"{Prefix}:{LocalName}" : LocalName;
    }

    internal abstract record AstNode
    {
        public SourceSpan Span { get; init; } = SourceSpan.None;
    }

    internal abstract record Expr : AstNode;

    // ---------------------------------------------------------------- primary expressions

    internal sealed record IntegerLiteral(System.Numerics.BigInteger Value) : Expr
    {
        public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A DecimalLiteral, kept as its lexical form; conversion belongs to the XDM layer.</summary>
    internal sealed record DecimalLiteral(string Lexical) : Expr
    {
        public override string ToString() => Lexical;
    }

    internal sealed record DoubleLiteral(string Lexical) : Expr
    {
        public override string ToString() => Lexical;
    }

    internal sealed record StringLiteral(string Value) : Expr
    {
        public override string ToString() => "\"" + Value.Replace("\"", "\"\"") + "\"";
    }

    internal sealed record VarRef(LexicalQName Name) : Expr
    {
        public override string ToString() => "$" + Name;
    }

    internal sealed record ContextItemExpr : Expr
    {
        public override string ToString() => ".";
    }

    /// <summary><c>()</c>.</summary>
    internal sealed record EmptySequenceExpr : Expr
    {
        public override string ToString() => "()";
    }

    /// <summary>An argument (<c>?</c>) left open in a partial function application.</summary>
    internal sealed record ArgumentPlaceholder : Expr
    {
        public override string ToString() => "?";
    }

    internal sealed record FunctionCall(LexicalQName Name, IReadOnlyList<Expr> Arguments) : Expr
    {
        public bool IsPartialApplication => Arguments.Any(a => a is ArgumentPlaceholder);

        public override string ToString() => $"{Name}({string.Join(", ", Arguments)})";
    }

    /// <summary><c>f#n</c>. The arity is kept unbounded: one beyond the implementation's limit is FOAR0002, not a syntax error.</summary>
    internal sealed record NamedFunctionRef(LexicalQName Name, System.Numerics.BigInteger Arity) : Expr
    {
        public override string ToString() => $"{Name}#{Arity}";
    }

    internal sealed record Param(LexicalQName Name, SequenceType? Type);

    internal sealed record InlineFunctionExpr(IReadOnlyList<Param> Params, SequenceType? ReturnType, Expr Body) : Expr
    {
        public override string ToString() =>
            $"function({string.Join(", ", Params.Select(p => "$" + p.Name + (p.Type != null ? " as " + p.Type : "")))})" +
            (ReturnType != null ? " as " + ReturnType : "") + " { " + Body + " }";
    }

    internal sealed record MapConstructorEntry(Expr Key, Expr Value);

    internal sealed record MapConstructor(IReadOnlyList<MapConstructorEntry> Entries) : Expr
    {
        public override string ToString() => "map { " + string.Join(", ", Entries.Select(e => e.Key + ": " + e.Value)) + " }";
    }

    /// <summary><c>[a, b, c]</c>: one member per expression.</summary>
    internal sealed record SquareArrayConstructor(IReadOnlyList<Expr> Members) : Expr
    {
        public override string ToString() => "[" + string.Join(", ", Members) + "]";
    }

    /// <summary><c>array { E }</c>: one member per item of E.</summary>
    internal sealed record CurlyArrayConstructor(Expr? Content) : Expr
    {
        public override string ToString() => "array { " + (Content?.ToString() ?? "") + " }";
    }

    // ---------------------------------------------------------------- lookups and postfix

    internal enum KeySpecifierKind { NCName, Integer, Parenthesized, Wildcard }

    internal sealed record KeySpecifier(KeySpecifierKind Kind, string? Name = null, System.Numerics.BigInteger Integer = default, Expr? Expr = null)
    {
        public override string ToString() => Kind switch
        {
            KeySpecifierKind.NCName => Name!,
            KeySpecifierKind.Integer => Integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
            KeySpecifierKind.Parenthesized => "(" + Expr + ")",
            _ => "*",
        };
    }

    /// <summary><c>?key</c> with no left-hand side: applies to the context item.</summary>
    internal sealed record UnaryLookup(KeySpecifier Key) : Expr
    {
        public override string ToString() => "?" + Key;
    }

    /// <summary><c>E?key</c>.</summary>
    internal sealed record PostfixLookup(Expr Base, KeySpecifier Key) : Expr
    {
        public override string ToString() => $"({Base})?{Key}";
    }

    /// <summary><c>E[P]</c> on a primary expression (a filter expression, not a step predicate).</summary>
    internal sealed record FilterExpr(Expr Base, Expr Predicate) : Expr
    {
        public override string ToString() => $"({Base})[{Predicate}]";
    }

    /// <summary><c>E(args)</c>: a dynamic function call.</summary>
    internal sealed record DynamicFunctionCall(Expr Function, IReadOnlyList<Expr> Arguments) : Expr
    {
        public override string ToString() => $"({Function})({string.Join(", ", Arguments)})";
    }

    // ---------------------------------------------------------------- paths

    internal enum Axis
    {
        Child, Descendant, Attribute, Self, DescendantOrSelf, FollowingSibling, Following, Namespace,
        Parent, Ancestor, PrecedingSibling, Preceding, AncestorOrSelf,
    }

    internal static class AxisNames
    {
        public static readonly IReadOnlyDictionary<string, Axis> ByName = new Dictionary<string, Axis>
        {
            ["child"] = Axis.Child, ["descendant"] = Axis.Descendant, ["attribute"] = Axis.Attribute,
            ["self"] = Axis.Self, ["descendant-or-self"] = Axis.DescendantOrSelf,
            ["following-sibling"] = Axis.FollowingSibling, ["following"] = Axis.Following,
            ["namespace"] = Axis.Namespace, ["parent"] = Axis.Parent, ["ancestor"] = Axis.Ancestor,
            ["preceding-sibling"] = Axis.PrecedingSibling, ["preceding"] = Axis.Preceding,
            ["ancestor-or-self"] = Axis.AncestorOrSelf,
        };

        public static string Name(Axis axis) => ByName.First(kv => kv.Value == axis).Key;

        public static bool IsReverse(Axis axis) => axis >= Axis.Parent;
    }

    internal abstract record NodeTest : AstNode;

    /// <summary>
    /// A name test. <see cref="LocalName"/> or <see cref="Namespace"/> is null for a wildcard part:
    /// <c>*</c> (both null), <c>p:*</c> (prefix set, local null), <c>*:n</c>, <c>Q{u}*</c>.
    /// </summary>
    internal sealed record NameTest(string? Prefix, string? LocalName, string? BracedUri, bool AnyNamespace) : NodeTest
    {
        public static NameTest Of(LexicalQName name) => new(name.Prefix, name.LocalName, name.BracedUri, false);

        public bool IsWildcard => LocalName == null || AnyNamespace;

        public override string ToString() =>
            AnyNamespace ? "*:" + (LocalName ?? "*") :
            BracedUri != null ? $"Q{{{BracedUri}}}{LocalName ?? "*"}" :
            Prefix != null ? $"{Prefix}:{LocalName ?? "*"}" :
            LocalName ?? "*";
    }

    internal sealed record KindNodeTest(KindTest Kind) : NodeTest
    {
        public override string ToString() => Kind.ToString();
    }

    internal sealed record AxisStep(Axis Axis, NodeTest Test, IReadOnlyList<Expr> Predicates) : Expr
    {
        public override string ToString() => AxisNames.Name(Axis) + "::" + Test + string.Concat(Predicates.Select(p => "[" + p + "]"));
    }

    /// <summary>The leading <c>/</c> of an absolute path: the root of the context node's tree.</summary>
    internal sealed record RootExpr : Expr
    {
        public override string ToString() => "/";
    }

    /// <summary>
    /// <c>Left/Right</c>, or <c>Left//Right</c> when <see cref="Descendants"/> is set (which abbreviates
    /// <c>Left/descendant-or-self::node()/Right</c>).
    /// </summary>
    internal sealed record PathExpr(Expr Left, Expr Right, bool Descendants) : Expr
    {
        public override string ToString() => $"({Left}){(Descendants ? "//" : "/")}({Right})";
    }

    /// <summary><c>Left ! Right</c>.</summary>
    internal sealed record SimpleMapExpr(Expr Left, Expr Right) : Expr
    {
        public override string ToString() => $"({Left}) ! ({Right})";
    }

    // ---------------------------------------------------------------- operators

    internal enum BinaryOp
    {
        Or, And,
        GeneralEq, GeneralNe, GeneralLt, GeneralLe, GeneralGt, GeneralGe,
        ValueEq, ValueNe, ValueLt, ValueLe, ValueGt, ValueGe,
        Is, Precedes, Follows,
        StringConcat, Range,
        Add, Subtract, Multiply, Divide, IntegerDivide, Modulo,
        Union, Intersect, Except,
    }

    internal static class BinaryOps
    {
        public static string Token(BinaryOp op) => op switch
        {
            BinaryOp.Or => "or", BinaryOp.And => "and",
            BinaryOp.GeneralEq => "=", BinaryOp.GeneralNe => "!=", BinaryOp.GeneralLt => "<",
            BinaryOp.GeneralLe => "<=", BinaryOp.GeneralGt => ">", BinaryOp.GeneralGe => ">=",
            BinaryOp.ValueEq => "eq", BinaryOp.ValueNe => "ne", BinaryOp.ValueLt => "lt",
            BinaryOp.ValueLe => "le", BinaryOp.ValueGt => "gt", BinaryOp.ValueGe => "ge",
            BinaryOp.Is => "is", BinaryOp.Precedes => "<<", BinaryOp.Follows => ">>",
            BinaryOp.StringConcat => "||", BinaryOp.Range => "to",
            BinaryOp.Add => "+", BinaryOp.Subtract => "-", BinaryOp.Multiply => "*",
            BinaryOp.Divide => "div", BinaryOp.IntegerDivide => "idiv", BinaryOp.Modulo => "mod",
            BinaryOp.Union => "union", BinaryOp.Intersect => "intersect", BinaryOp.Except => "except",
            _ => op.ToString(),
        };
    }

    internal sealed record BinaryExpr(BinaryOp Op, Expr Left, Expr Right) : Expr
    {
        public override string ToString() => $"({Left} {BinaryOps.Token(Op)} {Right})";
    }

    /// <summary>Unary <c>-</c> or <c>+</c>. Unary plus still atomises and requires a numeric operand.</summary>
    internal sealed record UnaryExpr(bool Minus, Expr Operand) : Expr
    {
        public override string ToString() => $"({(Minus ? "-" : "+")}{Operand})";
    }

    /// <summary><c>Target => f(args)</c>. Exactly one of <see cref="FunctionName"/> and <see cref="FunctionExpr"/> is set.</summary>
    internal sealed record ArrowExpr(Expr Target, LexicalQName? FunctionName, Expr? FunctionExpr, IReadOnlyList<Expr> Arguments) : Expr
    {
        public override string ToString() => $"({Target} => {(object?)FunctionName ?? FunctionExpr}({string.Join(", ", Arguments)}))";
    }

    internal sealed record InstanceOfExpr(Expr Operand, SequenceType Type) : Expr
    {
        public override string ToString() => $"({Operand} instance of {Type})";
    }

    internal sealed record TreatExpr(Expr Operand, SequenceType Type) : Expr
    {
        public override string ToString() => $"({Operand} treat as {Type})";
    }

    internal sealed record CastableExpr(Expr Operand, SingleType Type) : Expr
    {
        public override string ToString() => $"({Operand} castable as {Type})";
    }

    internal sealed record CastExpr(Expr Operand, SingleType Type) : Expr
    {
        public override string ToString() => $"({Operand} cast as {Type})";
    }

    // ---------------------------------------------------------------- composite expressions

    /// <summary><c>A, B, C</c>.</summary>
    internal sealed record SequenceExpr(IReadOnlyList<Expr> Items) : Expr
    {
        public override string ToString() => "(" + string.Join(", ", Items) + ")";
    }

    internal sealed record Binding(LexicalQName Variable, Expr Value);

    internal sealed record ForExpr(IReadOnlyList<Binding> Bindings, Expr Return) : Expr
    {
        public override string ToString() => "(for " + string.Join(", ", Bindings.Select(b => $"${b.Variable} in {b.Value}")) + " return " + Return + ")";
    }

    internal sealed record LetExpr(IReadOnlyList<Binding> Bindings, Expr Return) : Expr
    {
        public override string ToString() => "(let " + string.Join(", ", Bindings.Select(b => $"${b.Variable} := {b.Value}")) + " return " + Return + ")";
    }

    internal sealed record QuantifiedExpr(bool Every, IReadOnlyList<Binding> Bindings, Expr Satisfies) : Expr
    {
        public override string ToString() => "(" + (Every ? "every " : "some ") + string.Join(", ", Bindings.Select(b => $"${b.Variable} in {b.Value}")) + " satisfies " + Satisfies + ")";
    }

    internal sealed record IfExpr(Expr Condition, Expr Then, Expr Else) : Expr
    {
        public override string ToString() => $"(if ({Condition}) then {Then} else {Else})";
    }

    // ---------------------------------------------------------------- types

    internal enum Occurrence { ExactlyOne, ZeroOrOne, ZeroOrMore, OneOrMore }

    /// <summary>A SequenceType. <see cref="Item"/> is null for <c>empty-sequence()</c>.</summary>
    internal sealed record SequenceType(ItemType? Item, Occurrence Occurrence) : AstNode
    {
        public static readonly SequenceType EmptySequence = new(null, Occurrence.ZeroOrOne);

        public override string ToString() => Item == null ? "empty-sequence()" : Item + Occurrence switch
        {
            Occurrence.ZeroOrOne => "?",
            Occurrence.ZeroOrMore => "*",
            Occurrence.OneOrMore => "+",
            _ => "",
        };
    }

    /// <summary><c>cast as T</c> / <c>castable as T?</c>.</summary>
    internal sealed record SingleType(LexicalQName TypeName, bool AllowsEmpty) : AstNode
    {
        public override string ToString() => TypeName + (AllowsEmpty ? "?" : "");
    }

    internal abstract record ItemType : AstNode;

    internal sealed record AnyItemType : ItemType
    {
        public override string ToString() => "item()";
    }

    internal sealed record AtomicOrUnionType(LexicalQName Name) : ItemType
    {
        public override string ToString() => Name.ToString();
    }

    internal sealed record AnyFunctionTest : ItemType
    {
        public override string ToString() => "function(*)";
    }

    internal sealed record TypedFunctionTest(IReadOnlyList<SequenceType> Parameters, SequenceType Return) : ItemType
    {
        public override string ToString() => $"function({string.Join(", ", Parameters)}) as {Return}";
    }

    internal sealed record AnyMapTest : ItemType
    {
        public override string ToString() => "map(*)";
    }

    internal sealed record TypedMapTest(LexicalQName KeyType, SequenceType ValueType) : ItemType
    {
        public override string ToString() => $"map({KeyType}, {ValueType})";
    }

    internal sealed record AnyArrayTest : ItemType
    {
        public override string ToString() => "array(*)";
    }

    internal sealed record TypedArrayTest(SequenceType MemberType) : ItemType
    {
        public override string ToString() => $"array({MemberType})";
    }

    // Kind tests double as item types and as node tests in steps.
    internal abstract record KindTest : ItemType;

    internal sealed record AnyKindTest : KindTest
    {
        public override string ToString() => "node()";
    }

    internal sealed record TextTest : KindTest
    {
        public override string ToString() => "text()";
    }

    internal sealed record CommentTest : KindTest
    {
        public override string ToString() => "comment()";
    }

    internal sealed record NamespaceNodeTest : KindTest
    {
        public override string ToString() => "namespace-node()";
    }

    internal sealed record PITest(string? Name) : KindTest
    {
        public override string ToString() => $"processing-instruction({Name})";
    }

    /// <summary><c>document-node(E)</c>, where E is an element test, a schema-element test, or absent.</summary>
    internal sealed record DocumentTest(KindTest? ElementTest) : KindTest
    {
        public override string ToString() => $"document-node({ElementTest})";
    }

    /// <summary><c>element(N, T?)</c>; a null <see cref="Name"/> is the wildcard <c>*</c>.</summary>
    internal sealed record ElementTest(LexicalQName? Name, bool HasName, LexicalQName? TypeName, bool Nillable) : KindTest
    {
        public override string ToString() =>
            !HasName ? "element()" : $"element({(object?)Name ?? "*"}{(TypeName != null ? ", " + TypeName + (Nillable ? "?" : "") : "")})";
    }

    internal sealed record AttributeTest(LexicalQName? Name, bool HasName, LexicalQName? TypeName) : KindTest
    {
        public override string ToString() =>
            !HasName ? "attribute()" : $"attribute({(object?)Name ?? "*"}{(TypeName != null ? ", " + TypeName : "")})";
    }

    internal sealed record SchemaElementTest(LexicalQName Name) : KindTest
    {
        public override string ToString() => $"schema-element({Name})";
    }

    internal sealed record SchemaAttributeTest(LexicalQName Name) : KindTest
    {
        public override string ToString() => $"schema-attribute({Name})";
    }
}
