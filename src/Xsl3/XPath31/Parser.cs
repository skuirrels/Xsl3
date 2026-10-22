// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Xsl3.XPath31
{
    /// <summary>
    /// Recursive-descent parser for XPath 3.1 (Appendix A.1), producing the AST in Ast.cs. Each method is
    /// named after the production it parses. Syntax errors are <see cref="XPathStaticException"/> with code
    /// XPST0003.
    /// </summary>
    internal sealed class Parser
    {
        private readonly List<Token> _tokens;
        private int _pos;

        private Parser(string? text)
        {
            // (A missing expression: an attribute a stylesheet left out, which the loader has reported.)
            _tokens = Lexer.Tokenize(text ?? throw new XPathStaticException("XPST0003", "An expression is missing", 0));
        }

        /// <summary>Parses a complete XPath expression.</summary>
        public static Expr ParseXPath(string text)
        {
            // Xsl3 (perf): a stylesheet's compilation parses an expression or pattern several times (the loader's checks,
            // pattern analysis, compilation). The trees are immutable and don't depend on the context, so within a
            // compilation (CompilationScope) each text is parsed once. Errors aren't kept.
            Dictionary<string, Expr>? cache = t_parsed;
            if (cache != null && text != null && cache.TryGetValue(text, out Expr? parsed))
            {
                return parsed;
            }
            var parser = new Parser(text);
            Expr expr = parser.Expr();
            parser.ExpectEnd();
            if (cache != null)
            {
                cache[text!] = expr;
            }
            return expr;
        }

        [System.ThreadStatic]
        private static Dictionary<string, Expr>? t_parsed;

        /// <summary>Parses each expression text once until disposed (one stylesheet's compilation on this thread).</summary>
        internal static System.IDisposable CompilationScope() => new Scope();

        private sealed class Scope : System.IDisposable
        {
            private readonly Dictionary<string, Expr>? _saved = t_parsed;

            public Scope() => t_parsed = new Dictionary<string, Expr>(System.StringComparer.Ordinal);

            public void Dispose() => t_parsed = _saved;
        }

        private Parser(List<Token> tokens)
        {
            _tokens = tokens;
        }

        /// <summary>
        /// Parses the expression of an attribute or text value template, starting after its '{' at
        /// <paramref name="start"/> and ending at the matching '}', whose position is returned in
        /// <paramref name="end"/>. Braces inside the expression (in string literals, comments, map constructors)
        /// don't end it. Returns null for an empty expression.
        /// </summary>
        public static Expr? ParseEnclosedExpr(string text, int start, out int end)
        {
            var tokens = new List<Token>();
            int i = start, depth = 0;
            while (true)
            {
                bool space = Lexer.SkipSpace(text, ref i);
                if (i >= text.Length)
                {
                    // A left curly bracket with no matching right one (XSLT 3.0 §5.6.1).
                    throw new XPathStaticException("XTSE0350", "Missing '}' at the end of an expression in a value template", start);
                }
                Token t = Lexer.NextToken(text, ref i, space);
                if (t.Is("{"))
                {
                    depth++;
                }
                else if (t.Is("}"))
                {
                    if (depth == 0)
                    {
                        end = t.Start;
                        break;
                    }
                    depth--;
                }
                tokens.Add(t);
            }
            if (tokens.Count == 0)
            {
                return null;
            }
            tokens.Add(new Token(TokenKind.End, "", end, end, false));
            var parser = new Parser(tokens);
            Expr expr = parser.Expr();
            parser.ExpectEnd();
            return expr;
        }

        /// <summary>Parses a complete SequenceType (used by host languages, e.g. XSLT's <c>as</c> attributes).</summary>
        public static SequenceType ParseSequenceType(string text)
        {
            var parser = new Parser(text);
            SequenceType type = parser.SequenceType();
            parser.ExpectEnd();
            return type;
        }

        // ------------------------------------------------------------ token helpers

        private Token Current => _tokens[_pos];

        private Token Peek(int ahead = 1) => _tokens[Math.Min(_pos + ahead, _tokens.Count - 1)];

        private Token Advance()
        {
            Token t = _tokens[_pos];
            if (_pos < _tokens.Count - 1)
            {
                _pos++;
            }
            return t;
        }

        private bool At(string symbol) => Current.Is(symbol);

        private bool AtKeyword(string keyword) => Current.IsKeyword(keyword);

        private bool Accept(string symbol)
        {
            if (At(symbol))
            {
                Advance();
                return true;
            }
            return false;
        }

        private bool AcceptKeyword(string keyword)
        {
            if (AtKeyword(keyword))
            {
                Advance();
                return true;
            }
            return false;
        }

        private Token Expect(string symbol)
        {
            if (!At(symbol))
            {
                throw Error($"Expected '{symbol}' but found {Current}");
            }
            return Advance();
        }

        private void ExpectKeyword(string keyword)
        {
            if (!AtKeyword(keyword))
            {
                throw Error($"Expected '{keyword}' but found {Current}");
            }
            Advance();
        }

        private void ExpectEnd()
        {
            if (Current.Kind != TokenKind.End)
            {
                throw Error($"Unexpected {Current} after the end of the expression");
            }
        }

        private XPathStaticException Error(string message) => XPathStaticException.Syntax(message, Current.Start);

        // Stamps a node with the span from "start" to the end of the last consumed token.
        private T At<T>(int start, T node) where T : AstNode => (T)((AstNode)node with { Span = new SourceSpan(start, _tokens[Math.Max(_pos - 1, 0)].End) });

        private static bool IsName(Token t) => t.Kind is TokenKind.Name or TokenKind.UriQualifiedName;

        private LexicalQName EQName()
        {
            Token t = Current;
            if (t.Kind == TokenKind.Name)
            {
                Advance();
                return new LexicalQName(t.Prefix, t.LocalName!);
            }
            if (t.Kind == TokenKind.UriQualifiedName)
            {
                Advance();
                return new LexicalQName(null, t.LocalName!, t.Uri);
            }
            throw Error($"Expected a name but found {t}");
        }

        private string NCName()
        {
            Token t = Current;
            if (t.Kind == TokenKind.Name && t.Prefix == null)
            {
                Advance();
                return t.LocalName!;
            }
            throw Error($"Expected an NCName but found {t}");
        }

        // ------------------------------------------------------------ expressions

        // Expr ::= ExprSingle ("," ExprSingle)*
        private Expr Expr()
        {
            int start = Current.Start;
            Expr first = ExprSingle();
            if (!At(","))
            {
                return first;
            }
            var items = new List<Expr> { first };
            while (Accept(","))
            {
                items.Add(ExprSingle());
            }
            return At(start, new SequenceExpr(items));
        }

        // ExprSingle ::= ForExpr | LetExpr | QuantifiedExpr | IfExpr | OrExpr
        private Expr ExprSingle()
        {
            // Keywords introduce these expressions only when followed by "$" or "(": "for" alone is a name test.
            if (Peek().Is("$"))
            {
                if (AtKeyword("for")) return ForExpr();
                if (AtKeyword("let")) return LetExpr();
                if (AtKeyword("some") || AtKeyword("every")) return QuantifiedExpr();
            }
            if (AtKeyword("if") && Peek().Is("("))
            {
                return IfExpr();
            }
            return OrExpr();
        }

        private Expr ForExpr()
        {
            int start = Current.Start;
            ExpectKeyword("for");
            var bindings = new List<Binding>();
            do
            {
                Expect("$");
                LexicalQName name = EQName();
                ExpectKeyword("in");
                bindings.Add(new Binding(name, ExprSingle()));
            }
            while (Accept(","));
            ExpectKeyword("return");
            return At(start, new ForExpr(bindings, ExprSingle()));
        }

        private Expr LetExpr()
        {
            int start = Current.Start;
            ExpectKeyword("let");
            var bindings = new List<Binding>();
            do
            {
                Expect("$");
                LexicalQName name = EQName();
                Expect(":=");
                bindings.Add(new Binding(name, ExprSingle()));
            }
            while (Accept(","));
            ExpectKeyword("return");
            return At(start, new LetExpr(bindings, ExprSingle()));
        }

        private Expr QuantifiedExpr()
        {
            int start = Current.Start;
            bool every = AtKeyword("every");
            Advance();
            var bindings = new List<Binding>();
            do
            {
                Expect("$");
                LexicalQName name = EQName();
                ExpectKeyword("in");
                bindings.Add(new Binding(name, ExprSingle()));
            }
            while (Accept(","));
            ExpectKeyword("satisfies");
            return At(start, new QuantifiedExpr(every, bindings, ExprSingle()));
        }

        private Expr IfExpr()
        {
            int start = Current.Start;
            ExpectKeyword("if");
            Expect("(");
            Expr condition = Expr();
            Expect(")");
            ExpectKeyword("then");
            Expr then = ExprSingle();
            ExpectKeyword("else");
            return At(start, new IfExpr(condition, then, ExprSingle()));
        }

        // OrExpr ::= AndExpr ( "or" AndExpr )*
        private Expr OrExpr()
        {
            int start = Current.Start;
            Expr left = AndExpr();
            while (AcceptKeyword("or"))
            {
                left = At(start, new BinaryExpr(BinaryOp.Or, left, AndExpr()));
            }
            return left;
        }

        private Expr AndExpr()
        {
            int start = Current.Start;
            Expr left = ComparisonExpr();
            while (AcceptKeyword("and"))
            {
                left = At(start, new BinaryExpr(BinaryOp.And, left, ComparisonExpr()));
            }
            return left;
        }

        // ComparisonExpr ::= StringConcatExpr ( (ValueComp | GeneralComp | NodeComp) StringConcatExpr )?
        // Comparisons don't associate: "a = b = c" is a syntax error.
        private Expr ComparisonExpr()
        {
            int start = Current.Start;
            Expr left = StringConcatExpr();
            BinaryOp? op = ComparisonOperator();
            if (op == null)
            {
                return left;
            }
            Advance();
            Expr result = At(start, new BinaryExpr(op.Value, left, StringConcatExpr()));
            if (ComparisonOperator() != null)
            {
                throw Error("Comparison operators can't be chained; use parentheses");
            }
            return result;
        }

        private BinaryOp? ComparisonOperator()
        {
            Token t = Current;
            if (t.Kind == TokenKind.Symbol)
            {
                return t.Text switch
                {
                    "=" => BinaryOp.GeneralEq, "!=" => BinaryOp.GeneralNe,
                    "<" => BinaryOp.GeneralLt, "<=" => BinaryOp.GeneralLe,
                    ">" => BinaryOp.GeneralGt, ">=" => BinaryOp.GeneralGe,
                    "<<" => BinaryOp.Precedes, ">>" => BinaryOp.Follows,
                    _ => null,
                };
            }
            if (t.Kind == TokenKind.Name && t.Prefix == null)
            {
                return t.LocalName switch
                {
                    "eq" => BinaryOp.ValueEq, "ne" => BinaryOp.ValueNe, "lt" => BinaryOp.ValueLt,
                    "le" => BinaryOp.ValueLe, "gt" => BinaryOp.ValueGt, "ge" => BinaryOp.ValueGe,
                    "is" => BinaryOp.Is,
                    _ => null,
                };
            }
            return null;
        }

        // StringConcatExpr ::= RangeExpr ( "||" RangeExpr )*
        private Expr StringConcatExpr()
        {
            int start = Current.Start;
            Expr left = RangeExpr();
            while (Accept("||"))
            {
                left = At(start, new BinaryExpr(BinaryOp.StringConcat, left, RangeExpr()));
            }
            return left;
        }

        // RangeExpr ::= AdditiveExpr ( "to" AdditiveExpr )?
        private Expr RangeExpr()
        {
            int start = Current.Start;
            Expr left = AdditiveExpr();
            if (AcceptKeyword("to"))
            {
                return At(start, new BinaryExpr(BinaryOp.Range, left, AdditiveExpr()));
            }
            return left;
        }

        private Expr AdditiveExpr()
        {
            int start = Current.Start;
            Expr left = MultiplicativeExpr();
            while (true)
            {
                if (Accept("+")) left = At(start, new BinaryExpr(BinaryOp.Add, left, MultiplicativeExpr()));
                else if (Accept("-")) left = At(start, new BinaryExpr(BinaryOp.Subtract, left, MultiplicativeExpr()));
                else return left;
            }
        }

        private Expr MultiplicativeExpr()
        {
            int start = Current.Start;
            Expr left = UnionExpr();
            while (true)
            {
                BinaryOp op;
                if (At("*")) op = BinaryOp.Multiply;
                else if (AtKeyword("div")) op = BinaryOp.Divide;
                else if (AtKeyword("idiv")) op = BinaryOp.IntegerDivide;
                else if (AtKeyword("mod")) op = BinaryOp.Modulo;
                else return left;
                Advance();
                left = At(start, new BinaryExpr(op, left, UnionExpr()));
            }
        }

        private Expr UnionExpr()
        {
            int start = Current.Start;
            Expr left = IntersectExceptExpr();
            while (AcceptKeyword("union") || Accept("|"))
            {
                left = At(start, new BinaryExpr(BinaryOp.Union, left, IntersectExceptExpr()));
            }
            return left;
        }

        private Expr IntersectExceptExpr()
        {
            int start = Current.Start;
            Expr left = InstanceofExpr();
            while (true)
            {
                if (AcceptKeyword("intersect")) left = At(start, new BinaryExpr(BinaryOp.Intersect, left, InstanceofExpr()));
                else if (AcceptKeyword("except")) left = At(start, new BinaryExpr(BinaryOp.Except, left, InstanceofExpr()));
                else return left;
            }
        }

        private Expr InstanceofExpr()
        {
            int start = Current.Start;
            Expr operand = TreatExpr();
            if (AtKeyword("instance") && Peek().IsKeyword("of"))
            {
                Advance();
                Advance();
                return At(start, new InstanceOfExpr(operand, SequenceType()));
            }
            return operand;
        }

        private Expr TreatExpr()
        {
            int start = Current.Start;
            Expr operand = CastableExpr();
            if (AtKeyword("treat") && Peek().IsKeyword("as"))
            {
                Advance();
                Advance();
                return At(start, new TreatExpr(operand, SequenceType()));
            }
            return operand;
        }

        private Expr CastableExpr()
        {
            int start = Current.Start;
            Expr operand = CastExpr();
            if (AtKeyword("castable") && Peek().IsKeyword("as"))
            {
                Advance();
                Advance();
                return At(start, new CastableExpr(operand, SingleType()));
            }
            return operand;
        }

        private Expr CastExpr()
        {
            int start = Current.Start;
            Expr operand = ArrowExpr();
            if (AtKeyword("cast") && Peek().IsKeyword("as"))
            {
                Advance();
                Advance();
                return At(start, new CastExpr(operand, SingleType()));
            }
            return operand;
        }

        // ArrowExpr ::= UnaryExpr ( "=>" ArrowFunctionSpecifier ArgumentList )*
        // ArrowFunctionSpecifier ::= EQName | VarRef | ParenthesizedExpr
        private Expr ArrowExpr()
        {
            int start = Current.Start;
            Expr target = UnaryExpr();
            while (Accept("=>"))
            {
                LexicalQName? name = null;
                Expr? function = null;
                if (IsName(Current))
                {
                    name = EQName();
                }
                else if (At("$"))
                {
                    function = VarRef();
                }
                else if (At("("))
                {
                    function = ParenthesizedExpr();
                }
                else
                {
                    throw Error($"Expected a function name, variable or parenthesized expression after '=>' but found {Current}");
                }
                target = At(start, new ArrowExpr(target, name, function, ArgumentList()));
            }
            return target;
        }

        // UnaryExpr ::= ("-" | "+")* ValueExpr
        private Expr UnaryExpr()
        {
            int start = Current.Start;
            if (Accept("-"))
            {
                return At(start, new UnaryExpr(true, UnaryExpr()));
            }
            if (Accept("+"))
            {
                return At(start, new UnaryExpr(false, UnaryExpr()));
            }
            return SimpleMapExpr();
        }

        // SimpleMapExpr ::= PathExpr ("!" PathExpr)*
        private Expr SimpleMapExpr()
        {
            int start = Current.Start;
            Expr left = PathExpr();
            while (Accept("!"))
            {
                left = At(start, new SimpleMapExpr(left, PathExpr()));
            }
            return left;
        }

        // PathExpr ::= ("/" RelativePathExpr?) | ("//" RelativePathExpr) | RelativePathExpr
        private Expr PathExpr()
        {
            int start = Current.Start;
            if (Accept("/"))
            {
                Expr root = At(start, new RootExpr());
                // xgc: leading-lone-slash. "/" starts a relative path whenever the next token can.
                if (!CanStartRelativePath())
                {
                    return root;
                }
                return RelativePathExpr(start, root, descendants: false);
            }
            if (Accept("//"))
            {
                return RelativePathExpr(start, At(start, new RootExpr()), descendants: true);
            }
            return RelativePathExpr(start, null, descendants: false);
        }

        private bool CanStartRelativePath()
        {
            Token t = Current;
            return t.Kind switch
            {
                TokenKind.Name or TokenKind.UriQualifiedName or TokenKind.Wildcard or TokenKind.IntegerLiteral or
                TokenKind.DecimalLiteral or TokenKind.DoubleLiteral or TokenKind.StringLiteral => true,
                TokenKind.Symbol => t.Text is "*" or "@" or "." or ".." or "$" or "(" or "?" or "[",
                _ => false,
            };
        }

        // RelativePathExpr ::= StepExpr (("/" | "//") StepExpr)*
        private Expr RelativePathExpr(int start, Expr? left, bool descendants)
        {
            Expr path = left == null ? StepExpr() : At(start, new PathExpr(left, StepExpr(), descendants));
            while (true)
            {
                if (Accept("/")) path = At(start, new PathExpr(path, StepExpr(), false));
                else if (Accept("//")) path = At(start, new PathExpr(path, StepExpr(), true));
                else return path;
            }
        }

        // StepExpr ::= PostfixExpr | AxisStep
        private Expr StepExpr()
        {
            Token t = Current;
            int start = t.Start;

            if (t.Is(".."))
            {
                Advance();
                return At(start, new AxisStep(Axis.Parent, new KindNodeTest(new AnyKindTest()), PredicateList()));
            }
            if (t.Is("@"))
            {
                Advance();
                NodeTest test = NodeTest(Axis.Attribute);
                return At(start, new AxisStep(Axis.Attribute, test, PredicateList()));
            }
            if (t.Kind == TokenKind.Name && t.Prefix == null && Peek().Is("::"))
            {
                if (!AxisNames.ByName.TryGetValue(t.LocalName!, out Axis axis))
                {
                    throw Error($"'{t.LocalName}' is not an axis name");
                }
                Advance();
                Advance();
                NodeTest test = NodeTest(axis);
                return At(start, new AxisStep(axis, test, PredicateList()) { ExplicitAxis = true });
            }
            if (t.Kind == TokenKind.Wildcard || t.Is("*"))
            {
                return At(start, new AxisStep(Axis.Child, NodeTest(Axis.Child), PredicateList()));
            }
            if (IsName(t))
            {
                Token next = Peek();
                bool isKindTestName = t.Kind == TokenKind.Name && t.Prefix == null && IsKindTestName(t.LocalName!);
                if (next.Is("(") && isKindTestName)
                {
                    // An abbreviated step with a kind test: "text()", "element(a)"; attribute() selects attributes.
                    KindTest kind = KindTest();
                    // An abbreviated step is on the child axis, except that attribute tests select attributes and
                    // namespace-node() selects namespace nodes (XPath 3.1 §3.3.5).
                    Axis axis = kind switch
                    {
                        AttributeTest or SchemaAttributeTest => Axis.Attribute,
                        NamespaceNodeTest => Axis.Namespace,
                        _ => Axis.Child,
                    };
                    return At(start, new AxisStep(axis, new KindNodeTest(kind) { Span = kind.Span }, PredicateList()));
                }
                if (!next.Is("(") && !next.Is("#") && !(next.Is("{") && t.Kind == TokenKind.Name && t.Prefix == null && t.LocalName is "map" or "array"))
                {
                    LexicalQName name = EQName();
                    return At(start, new AxisStep(Axis.Child, NameTest.Of(name), PredicateList()));
                }
            }
            return PostfixExpr();
        }

        private static bool IsKindTestName(string name) => name is
            "node" or "text" or "comment" or "processing-instruction" or "element" or "attribute" or
            "document-node" or "schema-element" or "schema-attribute" or "namespace-node";

        // NodeTest ::= KindTest | NameTest
        private NodeTest NodeTest(Axis axis)
        {
            Token t = Current;
            int start = t.Start;
            if (t.Kind == TokenKind.Name && t.Prefix == null && Peek().Is("(") && IsKindTestName(t.LocalName!))
            {
                KindTest kind = KindTest();
                return At(start, new KindNodeTest(kind));
            }
            if (t.Is("*"))
            {
                Advance();
                return At(start, new NameTest(null, null, null, false));
            }
            if (t.Kind == TokenKind.Wildcard)
            {
                Advance();
                return At(start, new NameTest(t.Prefix, t.LocalName, t.Uri, t.AnyNamespace));
            }
            if (IsName(t))
            {
                return At(start, NameTest.Of(EQName()));
            }
            throw Error($"Expected a node test but found {t}");
        }

        private List<Expr> PredicateList()
        {
            var predicates = new List<Expr>();
            while (Accept("["))
            {
                predicates.Add(Expr());
                Expect("]");
            }
            return predicates;
        }

        // PostfixExpr ::= PrimaryExpr (Predicate | ArgumentList | Lookup)*
        private Expr PostfixExpr()
        {
            int start = Current.Start;
            Expr expr = PrimaryExpr();
            while (true)
            {
                if (Accept("["))
                {
                    Expr predicate = Expr();
                    Expect("]");
                    expr = At(start, new FilterExpr(expr, predicate));
                }
                else if (At("("))
                {
                    expr = At(start, new DynamicFunctionCall(expr, ArgumentList()));
                }
                else if (At("?") && CanStartKeySpecifier(Peek()))
                {
                    Advance();
                    expr = At(start, new PostfixLookup(expr, KeySpecifier()));
                }
                else
                {
                    return expr;
                }
            }
        }

        private static bool CanStartKeySpecifier(Token t) =>
            (t.Kind == TokenKind.Name && t.Prefix == null) || t.Kind == TokenKind.IntegerLiteral || t.Is("(") || t.Is("*");

        // KeySpecifier ::= NCName | IntegerLiteral | ParenthesizedExpr | "*"
        private KeySpecifier KeySpecifier()
        {
            Token t = Current;
            if (t.Kind == TokenKind.Name && t.Prefix == null)
            {
                Advance();
                return new KeySpecifier(KeySpecifierKind.NCName, Name: t.LocalName);
            }
            if (t.Kind == TokenKind.IntegerLiteral)
            {
                Advance();
                return new KeySpecifier(KeySpecifierKind.Integer, Integer: BigInteger.Parse(t.Text, CultureInfo.InvariantCulture));
            }
            if (t.Is("("))
            {
                return new KeySpecifier(KeySpecifierKind.Parenthesized, Expr: ParenthesizedExpr());
            }
            if (t.Is("*"))
            {
                Advance();
                return new KeySpecifier(KeySpecifierKind.Wildcard);
            }
            throw Error($"Expected a key specifier after '?' but found {t}");
        }

        // ArgumentList ::= "(" (Argument ("," Argument)*)? ")"
        // Argument ::= ExprSingle | ArgumentPlaceholder
        private List<Expr> ArgumentList()
        {
            Expect("(");
            var args = new List<Expr>();
            if (Accept(")"))
            {
                return args;
            }
            do
            {
                if (At("?") && (Peek().Is(",") || Peek().Is(")")))
                {
                    int start = Current.Start;
                    Advance();
                    args.Add(At(start, new ArgumentPlaceholder()));
                }
                else
                {
                    args.Add(ExprSingle());
                }
            }
            while (Accept(","));
            Expect(")");
            return args;
        }

        // PrimaryExpr ::= Literal | VarRef | ParenthesizedExpr | ContextItemExpr | FunctionCall
        //               | FunctionItemExpr | MapConstructor | ArrayConstructor | UnaryLookup
        private Expr PrimaryExpr()
        {
            Token t = Current;
            int start = t.Start;
            switch (t.Kind)
            {
                case TokenKind.IntegerLiteral:
                    Advance();
                    return At(start, new IntegerLiteral(BigInteger.Parse(t.Text, CultureInfo.InvariantCulture)));
                case TokenKind.DecimalLiteral:
                    Advance();
                    return At(start, new DecimalLiteral(t.Text));
                case TokenKind.DoubleLiteral:
                    Advance();
                    return At(start, new DoubleLiteral(t.Text));
                case TokenKind.StringLiteral:
                    Advance();
                    return At(start, new StringLiteral(t.Text));
                case TokenKind.Name:
                case TokenKind.UriQualifiedName:
                    return NamedPrimary();
                case TokenKind.Symbol:
                    switch (t.Text)
                    {
                        case "$":
                            return VarRef();
                        case "(":
                            return ParenthesizedExpr();
                        case ".":
                            Advance();
                            return At(start, new ContextItemExpr());
                        case "[":
                            return SquareArrayConstructor();
                        case "?":
                            if (CanStartKeySpecifier(Peek()))
                            {
                                Advance();
                                return At(start, new UnaryLookup(KeySpecifier()));
                            }
                            break;
                    }
                    break;
            }
            throw Error($"Unexpected {t}");
        }

        // Primary expressions that start with a name: function calls, named function references, inline
        // functions, map and array constructors.
        private Expr NamedPrimary()
        {
            Token t = Current;
            int start = t.Start;
            Token next = Peek();
            bool unprefixed = t.Kind == TokenKind.Name && t.Prefix == null;

            if (unprefixed && t.LocalName == "function" && next.Is("("))
            {
                return InlineFunctionExpr();
            }
            if (unprefixed && t.LocalName == "map" && next.Is("{"))
            {
                return MapConstructor();
            }
            if (unprefixed && t.LocalName == "array" && next.Is("{"))
            {
                Advance();
                Expect("{");
                Expr? content = At("}") ? null : Expr();
                Expect("}");
                return At(start, new CurlyArrayConstructor(content));
            }
            if (next.Is("#"))
            {
                // xgc: reserved-function-names applies to named function references too ("if#1").
                if (unprefixed && IsReservedFunctionName(t.LocalName!))
                {
                    throw Error($"'{t.LocalName}' is a reserved function name; write a prefix to refer to a function with this name");
                }
                LexicalQName name = EQName();
                Advance();
                if (Current.Kind != TokenKind.IntegerLiteral)
                {
                    throw Error($"Expected an arity after '#' but found {Current}");
                }
                BigInteger arity = BigInteger.Parse(Current.Text, CultureInfo.InvariantCulture);
                Advance();
                return At(start, new NamedFunctionRef(name, arity));
            }
            if (next.Is("("))
            {
                // xgc: reserved-function-names. These unprefixed names never denote a function call.
                if (unprefixed && IsReservedFunctionName(t.LocalName!))
                {
                    throw Error($"'{t.LocalName}' is a reserved function name; write a prefix to call a function with this name");
                }
                LexicalQName name = EQName();
                return At(start, new FunctionCall(name, ArgumentList()));
            }
            throw Error($"Unexpected {t}");
        }

        private static bool IsReservedFunctionName(string name) => name is
            "attribute" or "comment" or "document-node" or "element" or "empty-sequence" or "function" or "if" or
            "item" or "map" or "array" or "namespace-node" or "node" or "processing-instruction" or
            "schema-attribute" or "schema-element" or "switch" or "text" or "typeswitch";

        private Expr VarRef()
        {
            int start = Current.Start;
            Expect("$");
            return At(start, new VarRef(EQName()));
        }

        // ParenthesizedExpr ::= "(" Expr? ")"
        private Expr ParenthesizedExpr()
        {
            int start = Current.Start;
            Expect("(");
            if (Accept(")"))
            {
                return At(start, new EmptySequenceExpr());
            }
            Expr inner = Expr();
            Expect(")");
            // Keep a span covering the parentheses, but no node for them.
            return inner with { Span = new SourceSpan(start, _tokens[_pos - 1].End) };
        }

        // InlineFunctionExpr ::= "function" "(" ParamList? ")" ("as" SequenceType)? FunctionBody
        private Expr InlineFunctionExpr()
        {
            int start = Current.Start;
            Advance();
            Expect("(");
            var parameters = new List<Param>();
            if (!At(")"))
            {
                do
                {
                    Expect("$");
                    LexicalQName name = EQName();
                    SequenceType? type = null;
                    if (AcceptKeyword("as"))
                    {
                        type = SequenceType();
                    }
                    parameters.Add(new Param(name, type));
                }
                while (Accept(","));
            }
            Expect(")");
            SequenceType? returnType = null;
            if (AcceptKeyword("as"))
            {
                returnType = SequenceType();
            }
            Expect("{");
            Expr body = At("}") ? At(Current.Start, new EmptySequenceExpr()) : Expr();
            Expect("}");
            return At(start, new InlineFunctionExpr(parameters, returnType, body));
        }

        // MapConstructor ::= "map" "{" (MapConstructorEntry ("," MapConstructorEntry)*)? "}"
        private Expr MapConstructor()
        {
            int start = Current.Start;
            Advance();
            Expect("{");
            var entries = new List<MapConstructorEntry>();
            if (!At("}"))
            {
                do
                {
                    Expr key = ExprSingle();
                    Expect(":");
                    entries.Add(new MapConstructorEntry(key, ExprSingle()));
                }
                while (Accept(","));
            }
            Expect("}");
            return At(start, new MapConstructor(entries));
        }

        private Expr SquareArrayConstructor()
        {
            int start = Current.Start;
            Expect("[");
            var members = new List<Expr>();
            if (!At("]"))
            {
                do
                {
                    members.Add(ExprSingle());
                }
                while (Accept(","));
            }
            Expect("]");
            return At(start, new SquareArrayConstructor(members));
        }

        // ------------------------------------------------------------ types

        // SingleType ::= SimpleTypeName "?"?
        private SingleType SingleType()
        {
            int start = Current.Start;
            LexicalQName name = EQName();
            bool optional = Accept("?");
            return At(start, new SingleType(name, optional));
        }

        // SequenceType ::= ("empty-sequence" "(" ")") | (ItemType OccurrenceIndicator?)
        private SequenceType SequenceType()
        {
            int start = Current.Start;
            if (AtKeyword("empty-sequence") && Peek().Is("("))
            {
                Advance();
                Expect("(");
                Expect(")");
                return At(start, Xsl3.XPath31.SequenceType.EmptySequence);
            }
            ItemType item = ItemType();
            // xgc: occurrence-indicators. An indicator directly after an item type always binds to it.
            Occurrence occurrence = Occurrence.ExactlyOne;
            if (Accept("?")) occurrence = Occurrence.ZeroOrOne;
            else if (Accept("*")) occurrence = Occurrence.ZeroOrMore;
            else if (Accept("+")) occurrence = Occurrence.OneOrMore;
            return At(start, new SequenceType(item, occurrence));
        }

        // ItemType ::= KindTest | ("item" "(" ")") | FunctionTest | MapTest | ArrayTest | AtomicOrUnionType | ParenthesizedItemType
        private ItemType ItemType()
        {
            Token t = Current;
            int start = t.Start;
            if (t.Is("("))
            {
                Advance();
                ItemType inner = ItemType();
                Expect(")");
                return inner;
            }
            if (t.Kind == TokenKind.Name && t.Prefix == null && Peek().Is("("))
            {
                switch (t.LocalName)
                {
                    case "item":
                        Advance();
                        Expect("(");
                        Expect(")");
                        return At(start, new AnyItemType());
                    case "function":
                        return FunctionTest();
                    case "map":
                        return MapTest();
                    case "array":
                        return ArrayTest();
                }
                if (IsKindTestName(t.LocalName!))
                {
                    return KindTest();
                }
            }
            if (IsName(t))
            {
                return At(start, new AtomicOrUnionType(EQName()));
            }
            throw Error($"Expected an item type but found {t}");
        }

        private ItemType FunctionTest()
        {
            int start = Current.Start;
            Advance();
            Expect("(");
            if (Accept("*"))
            {
                Expect(")");
                return At(start, new AnyFunctionTest());
            }
            var parameters = new List<SequenceType>();
            if (!At(")"))
            {
                do
                {
                    parameters.Add(SequenceType());
                }
                while (Accept(","));
            }
            Expect(")");
            ExpectKeyword("as");
            return At(start, new TypedFunctionTest(parameters, SequenceType()));
        }

        private ItemType MapTest()
        {
            int start = Current.Start;
            Advance();
            Expect("(");
            if (Accept("*"))
            {
                Expect(")");
                return At(start, new AnyMapTest());
            }
            LexicalQName key = EQName();
            Expect(",");
            SequenceType value = SequenceType();
            Expect(")");
            return At(start, new TypedMapTest(key, value));
        }

        private ItemType ArrayTest()
        {
            int start = Current.Start;
            Advance();
            Expect("(");
            if (Accept("*"))
            {
                Expect(")");
                return At(start, new AnyArrayTest());
            }
            SequenceType member = SequenceType();
            Expect(")");
            return At(start, new TypedArrayTest(member));
        }

        private KindTest KindTest()
        {
            Token t = Current;
            int start = t.Start;
            string name = t.LocalName!;
            Advance();
            Expect("(");
            KindTest result;
            switch (name)
            {
                case "node":
                    result = new AnyKindTest();
                    break;
                case "text":
                    result = new TextTest();
                    break;
                case "comment":
                    result = new CommentTest();
                    break;
                case "namespace-node":
                    result = new NamespaceNodeTest();
                    break;
                case "processing-instruction":
                    string? target = null;
                    if (Current.Kind == TokenKind.StringLiteral)
                    {
                        // The string is normalised and must be an NCName (XPDY0004 / XPTY0004 territory at analysis).
                        target = Advance().Text.Trim(' ', '\t', '\r', '\n');
                    }
                    else if (!At(")"))
                    {
                        target = NCName();
                    }
                    result = new PITest(target);
                    break;
                case "document-node":
                    KindTest? element = null;
                    if (AtKeyword("element") && Peek().Is("(") || AtKeyword("schema-element") && Peek().Is("("))
                    {
                        element = KindTest();
                    }
                    else if (!At(")"))
                    {
                        throw Error($"Expected element(...) or schema-element(...) in document-node() but found {Current}");
                    }
                    result = new DocumentTest(element);
                    break;
                case "element":
                    result = ElementOrAttributeTest(isElement: true);
                    break;
                case "attribute":
                    result = ElementOrAttributeTest(isElement: false);
                    break;
                case "schema-element":
                    result = new SchemaElementTest(EQName());
                    break;
                case "schema-attribute":
                    result = new SchemaAttributeTest(EQName());
                    break;
                default:
                    throw Error($"'{name}' is not a kind test");
            }
            Expect(")");
            return At(start, result);
        }

        // ElementTest ::= "element" "(" (ElementNameOrWildcard ("," TypeName "?"?)?)? ")"
        // AttributeTest ::= "attribute" "(" (AttribNameOrWildcard ("," TypeName)?)? ")"
        private KindTest ElementOrAttributeTest(bool isElement)
        {
            if (At(")"))
            {
                return isElement ? new ElementTest(null, false, null, false) : new AttributeTest(null, false, null);
            }
            LexicalQName? name = Accept("*") ? null : EQName();
            LexicalQName? typeName = null;
            bool nillable = false;
            if (Accept(","))
            {
                typeName = EQName();
                if (isElement)
                {
                    nillable = Accept("?");
                }
            }
            return isElement ? new ElementTest(name, true, typeName, nillable) : new AttributeTest(name, true, typeName);
        }
    }
}
