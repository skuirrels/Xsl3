// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.XPath;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31
{
    /// <summary>The static context an XPath expression is compiled in (XPath 3.1 §2.1.1).</summary>
    internal sealed class XPathStaticContext
    {
        public XPathStaticContext()
        {
            Namespaces = new Dictionary<string, string>(System.StringComparer.Ordinal)
            {
                ["xml"] = QName.Ns.Xml,
                ["xs"] = QName.Ns.Xs,
                ["xsi"] = "http://www.w3.org/2001/XMLSchema-instance",
                ["fn"] = QName.Ns.Fn,
                ["math"] = QName.Ns.Math,
                ["map"] = QName.Ns.Map,
                ["array"] = QName.Ns.Array,
                ["err"] = QName.Ns.Err,
            };
        }

        /// <summary>Statically known namespaces, prefix to URI.</summary>
        public Dictionary<string, string> Namespaces { get; }

        public string DefaultElementNamespace { get; set; } = "";

        public string DefaultFunctionNamespace { get; set; } = QName.Ns.Fn;

        /// <summary>The static base URI (XPath 3.1 §2.1.1), or null if absent.</summary>
        public string? BaseUri { get; set; }

        /// <summary>External variables: their values are supplied to <see cref="XPath31Executable.Evaluate"/>.</summary>
        public List<QName> ExternalVariables { get; } = new();

        /// <summary>The default collation URI (XPath 3.1 §2.1.1); FOCH0002 at evaluation if it isn't supported.</summary>
        public string DefaultCollation { get; set; } = Collation.CodepointUri;

        /// <summary>The unnamed decimal format used by fn:format-number#2.</summary>
        public XPathDecimalFormat DefaultDecimalFormat { get; set; } = XPathDecimalFormat.Default;

        /// <summary>Statically known named decimal formats.</summary>
        public Dictionary<QName, XPathDecimalFormat> DecimalFormats { get; } = new();

        internal IXmlNamespaceResolver CreateResolver()
        {
            var manager = new XmlNamespaceManager(new NameTable());
            foreach (var (prefix, uri) in Namespaces)
            {
                if (prefix != "xml" && prefix != "xmlns")
                {
                    manager.AddNamespace(prefix, uri);
                }
            }
            if (DefaultElementNamespace.Length > 0)
            {
                // Unprefixed names cast to xs:QName take the default element namespace (F&O 3.1 §19.2.1).
                manager.AddNamespace("", DefaultElementNamespace);
            }
            return manager;
        }
    }

    /// <summary>
    /// Compiles an XPath 3.1 expression (SPEC §9.2) to QIL, then to IL through the upstream generator.
    /// </summary>
    /// <remarks>
    /// Phase 1 lowering (docs/plan/p1-xdm-xpath.md): every XPath value is boxed (QIL type item/item* holding
    /// Xdm.AtomicValue objects and navigators). Structure — loops, filters, axes, document order,
    /// conditionals — is native QIL; the atomic semantics are calls into XsltLibrary.XPath31.cs. Typed,
    /// unboxed lowering is P4.
    /// </remarks>
    /// <summary>
    /// What a host language supplies to an embedded XPath expression (XSLT 3.0 §5.3): the focus at the top level,
    /// the variables in scope, and the host's functions. Values are boxed QIL (item/item*), or native QIL booleans,
    /// doubles and strings, which the compiler boxes.
    /// </summary>
    internal interface IXPath31Environment
    {
        /// <summary>The context item, or null if it is absent (XPDY0002 if used).</summary>
        QilNode? ContextItem();

        QilNode ContextPosition();

        QilNode ContextLast();

        /// <summary>A variable in scope in the host, or null.</summary>
        QilNode? ResolveVariable(QName name);

        /// <summary>The lifted functions of the host's compilation unit, or null to keep them per expression.</summary>
        FunctionItemRegistry? FunctionItems { get; }

        /// <summary>
        /// A call of a host function (or of a function the host overrides), or null to use the F&amp;O library.
        /// <paramref name="compile"/> compiles an argument to boxed QIL.
        /// </summary>
        QilNode? ResolveFunction(QName name, IReadOnlyList<Expr> args, System.Func<Expr, QilNode> compile);
    }

    internal sealed partial class XPath31Compiler
    {
        private readonly QilFactory _qf;
        private readonly QilPatternFactory _f;
        private readonly IXPath31Environment? _env;
        private readonly XPathStaticContext _sc;
        private readonly StaticTypeResolver _types;
        private readonly IXmlNamespaceResolver _namespaces;
        private readonly List<(QName Name, QilNode Value)> _scope = new();
        private Focus? _focus;

        /// <summary>
        /// The focus inside a path step, predicate or simple map; null at the top level. A QIL graph is a tree, so
        /// position and last are factories: each use gets its own node (iterators may be referenced freely).
        /// </summary>
        private sealed record Focus(QilNode Item, System.Func<QilNode> Position, System.Func<QilNode>? Last);

        private XPath31Compiler(XPathStaticContext sc, QilPatternFactory? f = null, IXPath31Environment? env = null)
        {
            _f = f ?? new QilPatternFactory(new QilFactory(), false);
            _qf = _f.BaseFactory;
            _env = env;
            _sc = sc;
            _types = new StaticTypeResolver(p => sc.Namespaces.TryGetValue(p, out string? uri) ? uri : null, sc.DefaultElementNamespace);
            _namespaces = sc.CreateResolver();
            _staticId = ConstantPool.Add(new StaticInfo(sc.BaseUri, sc.DefaultCollation,
                new DecimalFormatTable(sc.DefaultDecimalFormat, new Dictionary<QName, XPathDecimalFormat>(sc.DecimalFormats), _namespaces)));
        }

        // The StaticInfo id passed to runtime calls that read the static context.
        private readonly int _staticId;

        private QilNode StaticId() => _f.Double(_staticId);

        public static XPath31Executable Compile(string expression, XPathStaticContext sc)
        {
            Expr ast = Parser.ParseXPath(expression);
            var compiler = new XPath31Compiler(sc);
            QilExpression qil = compiler.CompileQuery(ast);
            XmlILCommand command = new XmlILGenerator().Generate(qil, null)!;
            return new XPath31Executable(command);
        }

        /// <summary>
        /// Compiles a standalone expression whose focus, variables and extra functions come from a host
        /// environment (XSLT's use-when, say). The environment is made for the compiler's QIL factory.
        /// </summary>
        public static XPath31Executable CompileHosted(Expr ast, XPathStaticContext sc, System.Func<QilPatternFactory, IXPath31Environment> environment)
        {
            var f = new QilPatternFactory(new QilFactory(), false);
            var compiler = new XPath31Compiler(sc, f, environment(f));
            QilList functions = compiler._qf.FunctionList();
            compiler._ownRegistry = new FunctionItemRegistry(f, functions.Add);
            QilExpression qil = compiler._qf.QilExpression(compiler.Items(compiler.Build(ast)), compiler._qf);
            compiler._ownRegistry.Finish();
            qil.Dispatcher = compiler._ownRegistry.DispatcherIfUsed;
            qil.FunctionList = functions;
            qil.DefaultWriterSettings = new XmlWriterSettings();
            XmlILCommand command = new XmlILGenerator().Generate(qil, null)!;
            return new XPath31Executable(command);
        }

        /// <summary>
        /// Compiles an expression embedded in a host language (XSLT) into the host's QIL graph. The result is
        /// boxed: item or item*.
        /// </summary>
        public static QilNode CompileEmbedded(Expr ast, XPathStaticContext sc, QilPatternFactory f, IXPath31Environment env)
        {
            var compiler = new XPath31Compiler(sc, f, env);
            return compiler.Items(compiler.Build(ast));
        }

        /// <summary>Compiles a boxed host value (a variable reference, say) to an item* node.</summary>
        public static QilNode Box(QilNode n, QilPatternFactory f) => new XPath31Compiler(new XPathStaticContext(), f, null).Items(n);

        private QilExpression CompileQuery(Expr ast)
        {
            QilList functions = _qf.FunctionList();
            _ownRegistry = new FunctionItemRegistry(_f, functions.Add);
            // External variables are bound once, at the top, from the argument list.
            var lets = new List<QilIterator>();
            foreach (QName v in _sc.ExternalVariables)
            {
                QilIterator let = _f.Let(Call(nameof(XsltLibrary.X31Variable), T.ItemS, _f.String(v.LocalName), _f.String(v.NamespaceUri)));
                lets.Add(let);
                _scope.Add((v, let));
            }
            QilNode root = Items(Build(ast));
            for (int i = lets.Count - 1; i >= 0; i--)
            {
                root = _f.Loop(lets[i], root);
            }
            QilExpression qil = _qf.QilExpression(root, _qf);
            _ownRegistry.Finish();
            qil.Dispatcher = _ownRegistry.DispatcherIfUsed;
            qil.FunctionList = functions;
            qil.DefaultWriterSettings = new XmlWriterSettings();
            return qil;
        }

        // ------------------------------------------------------------ runtime calls and conversions

        private static readonly Dictionary<string, MethodInfo> s_methods = typeof(XsltLibrary)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.StartsWith("X31", System.StringComparison.Ordinal))
            .ToDictionary(m => m.Name);

        private QilNode Call(string method, XmlQueryType type, params QilNode[] args) =>
            _f.XsltInvokeEarlyBound(_f.QName(method, ""), s_methods[method], type, args);

        /// <summary>A call of an XsltLibrary X31 method, for host code (XSLT) that builds QIL around embedded expressions.</summary>
        internal static QilNode Invoke(QilPatternFactory f, string method, XmlQueryType type, params QilNode[] args) =>
            f.XsltInvokeEarlyBound(f.QName(method, ""), s_methods[method], type, args);

        private QilNode Constant(AtomicValue value) => Call(nameof(XsltLibrary.X31Constant), T.Item, _f.Double(ConstantPool.Add(value)));

        private static bool IsBoolean(QilNode n) => n.XmlType!.TypeCode == System.Xml.Schema.XmlTypeCode.Boolean && n.XmlType.IsSingleton;

        private static bool IsDouble(QilNode n) => n.XmlType!.TypeCode == System.Xml.Schema.XmlTypeCode.Double && n.XmlType.IsSingleton;

        /// <summary>A node whose value is an XPath sequence (item, item*, node, node*).</summary>
        private QilNode Items(QilNode n)
        {
            if (IsBoolean(n))
            {
                return Call(nameof(XsltLibrary.X31BooleanItem), T.Item, n);
            }
            if (IsDouble(n))
            {
                // Only positions are native doubles; position() and last() are xs:integer.
                return Call(nameof(XsltLibrary.X31DoubleAsInteger), T.Item, n);
            }
            if (n.XmlType!.TypeCode == System.Xml.Schema.XmlTypeCode.String && n.XmlType.IsSingleton)
            {
                // Native strings come from the host (XSLT 1.0 values, AVT parts).
                return Call(nameof(XsltLibrary.X31StringItem), T.Item, n);
            }
            return n;
        }

        /// <summary>The effective boolean value, as a native QIL boolean.</summary>
        private QilNode Bool(QilNode n) => IsBoolean(n) ? n : Call(nameof(XsltLibrary.X31Ebv), T.BooleanX, Items(n));

        private QilNode Nodes(QilNode n, string errorCode) =>
            n.XmlType!.IsSubtypeOf(T.NodeNotRtfS) ? n : Call(nameof(XsltLibrary.X31Nodes), T.NodeNotRtfS, Items(n), _f.String(errorCode));

        private static XPathStaticException NotYetSupported(Expr e, string what) =>
            new("XSL30001", $"{what} is not supported yet", e.Span.Start);

        // ------------------------------------------------------------ focus

        private QilNode FocusItem() =>
            _focus?.Item
            ?? (_focusAbsent ? Call(nameof(XsltLibrary.X31RaiseItem), T.Item, _f.String("XPDY0002"), _f.String("The context item is absent in a function body")) : null)
            ?? (_env != null
                ? _env.ContextItem() ?? Call(nameof(XsltLibrary.X31RaiseItem), T.Item, _f.String("XPDY0002"), _f.String("The context item is absent"))
                : Call(nameof(XsltLibrary.X31ContextItem), T.Item));

        private QilNode FocusNode()
        {
            QilNode item = FocusItem();
            return item.XmlType!.IsSubtypeOf(T.NodeNotRtf) ? item : Call(nameof(XsltLibrary.X31Node), T.NodeNotRtf, item);
        }

        private QilNode FocusPosition()
        {
            return _focus != null ? _focus.Position()
                : _focusAbsent ? Call(nameof(XsltLibrary.X31RaiseDouble), T.DoubleX, _f.String("XPDY0002"), _f.String("The focus is absent in a function body"))
                : _env != null ? _env.ContextPosition() : Call(nameof(XsltLibrary.X31TopLevelFocus), T.DoubleX);
        }

        private QilNode FocusLast() =>
            _focus != null ? (_focus.Last ?? throw new System.InvalidOperationException("last() not prepared"))()
            : _focusAbsent ? Call(nameof(XsltLibrary.X31RaiseDouble), T.DoubleX, _f.String("XPDY0002"), _f.String("The focus is absent in a function body"))
            : _env != null ? _env.ContextLast()
            : Call(nameof(XsltLibrary.X31TopLevelFocus), T.DoubleX);

        /// <summary>
        /// Iterates <paramref name="input"/> with each item as the focus, building the body with <paramref name="body"/>.
        /// last() needs the input bound to a variable; it is only bound when <paramref name="needsLast"/>.
        /// </summary>
        private (QilIterator Iterator, QilNode Body, QilIterator? Let) WithFocus(QilNode input, bool needsLast, System.Func<QilNode> body)
        {
            System.Func<QilNode>? last = null;
            QilIterator? let = null;
            if (needsLast)
            {
                QilIterator bound = let = _f.Let(input);
                last = () => _f.XsltConvert(_f.Length(bound), T.DoubleX);
            }
            QilIterator it = _f.For(let ?? input);
            Focus? saved = _focus;
            _focus = new Focus(it, () => _f.XsltConvert(_f.PositionOf(it), T.DoubleX), last);
            try
            {
                QilNode result = body();
                return (it, result, let);
            }
            finally
            {
                _focus = saved;
            }
        }

        // Binds the variable WithFocus introduced for last(), if it did. (Never a variable of the user's: that is
        // already bound where it was declared.)
        private QilNode WrapLet(QilIterator? let, QilNode loop) => let != null ? _f.Loop(let, loop) : loop;

        // Whether an expression calls last() for the focus it is evaluated in.
        private static bool UsesLast(Expr e) => e switch
        {
            FunctionCall { Name: { Prefix: null or "fn", LocalName: "last" }, Arguments.Count: 0 } => true,
            // last() inside a nested path step or predicate refers to that inner focus
            PathExpr p => UsesLast(p.Left),
            SimpleMapExpr m => UsesLast(m.Left),
            AxisStep => false,
            FilterExpr f => UsesLast(f.Base),
            _ => Children(e).Any(UsesLast),
        };

        private static IEnumerable<Expr> Children(Expr e) => e switch
        {
            BinaryExpr b => new[] { b.Left, b.Right },
            UnaryExpr u => new[] { u.Operand },
            SequenceExpr s => s.Items,
            FunctionCall c => c.Arguments,
            IfExpr i => new[] { i.Condition, i.Then, i.Else },
            ForExpr f => f.Bindings.Select(b => b.Value).Append(f.Return),
            LetExpr l => l.Bindings.Select(b => b.Value).Append(l.Return),
            QuantifiedExpr q => q.Bindings.Select(b => b.Value).Append(q.Satisfies),
            InstanceOfExpr x => new[] { x.Operand },
            TreatExpr x => new[] { x.Operand },
            CastExpr x => new[] { x.Operand },
            CastableExpr x => new[] { x.Operand },
            ArrowExpr a => a.Arguments.Prepend(a.Target),
            _ => System.Array.Empty<Expr>(),
        };

        // ------------------------------------------------------------ expressions

        private QilNode Build(Expr e)
        {
            switch (e)
            {
                case IntegerLiteral i:
                    return Constant(AtomicValue.OfInteger(i.Value));
                case DecimalLiteral d:
                    return Constant(Casting.FromString(d.Lexical, AtomicTypes.Decimal));
                case DoubleLiteral d:
                    return Constant(Casting.FromString(d.Lexical, AtomicTypes.Double));
                case StringLiteral s:
                    return Constant(AtomicValue.OfString(s.Value));
                case EmptySequenceExpr:
                    return _f.Sequence();
                case SequenceExpr s:
                    return _f.Sequence(s.Items.Select(i => Items(Build(i))).ToArray());
                case ContextItemExpr:
                    return FocusItem();
                case VarRef v:
                    return Variable(v);
                case BinaryExpr b:
                    return Binary(b);
                case UnaryExpr u:
                    return Call(nameof(XsltLibrary.X31Unary), T.ItemS, Items(Build(u.Operand)), u.Minus ? _f.True() : _f.False());
                case IfExpr i:
                    return _f.Conditional(Bool(Build(i.Condition)), Items(Build(i.Then)), Items(Build(i.Else)));
                case ForExpr f:
                    return For(f.Bindings, 0, f.Return);
                case LetExpr l:
                    return Let(l.Bindings, 0, l.Return);
                case QuantifiedExpr q:
                    return Quantified(q);
                case RootExpr:
                {
                    // fn:root(self::node()) treat as document-node() (XPath 3.1 §3.3.5): XPDY0050 if it isn't one.
                    QilIterator root = _f.Let(_f.Root(FocusNode()));
                    return _f.Loop(root, _f.Conditional(_f.IsType(root, T.Document), root,
                        _f.TypeAssert(Call(nameof(XsltLibrary.X31RaiseItem), T.Item, _f.String("XPDY0050"), _f.String("The root of the tree containing the context node is not a document node")), T.Document)));
                }
                case AxisStep step:
                    return Step(step);
                case PathExpr p:
                    return Path(p);
                case SimpleMapExpr m:
                    return SimpleMap(m);
                case FilterExpr fe:
                    return Filter(fe);
                case FunctionCall c:
                    return FunctionCall(c, c.Name, c.Arguments);
                case ArrowExpr { FunctionName: LexicalQName name } a:
                    return FunctionCall(a, name, a.Arguments.Prepend(a.Target).ToList());
                case InstanceOfExpr x:
                    return Call(nameof(XsltLibrary.X31InstanceOf), T.BooleanX, Items(Build(x.Operand)), TypeId(x.Type));
                case TreatExpr x:
                    return Call(nameof(XsltLibrary.X31Treat), T.ItemS, Items(Build(x.Operand)), TypeId(x.Type));
                case CastExpr x:
                    return Call(nameof(XsltLibrary.X31Cast), T.ItemS, Items(Build(x.Operand)), CastTargetId(x.Type), x.Type.AllowsEmpty ? _f.True() : _f.False());
                case CastableExpr x:
                    return Call(nameof(XsltLibrary.X31Castable), T.BooleanX, Items(Build(x.Operand)), CastTargetId(x.Type), x.Type.AllowsEmpty ? _f.True() : _f.False());
                case InlineFunctionExpr fn:
                    return InlineFunction(fn);
                case NamedFunctionRef r:
                    return NamedFunctionReference(r);
                case DynamicFunctionCall d:
                    return DynamicFunctionCall(d);
                case ArrowExpr { FunctionExpr: Expr fe } a:
                    return DynamicFunctionCall(new DynamicFunctionCall(fe, a.Arguments.Prepend(a.Target).ToList()) { Span = a.Span });
                case MapConstructor map:
                {
                    QilNode builder = Call(nameof(XsltLibrary.X31MapNew), T.Item);
                    foreach (MapConstructorEntry entry in map.Entries)
                    {
                        builder = Call(nameof(XsltLibrary.X31MapEntry), T.Item, builder, Items(Build(entry.Key)), Items(Build(entry.Value)));
                    }
                    return Call(nameof(XsltLibrary.X31MapDone), T.Item, builder);
                }
                case SquareArrayConstructor array:
                {
                    QilNode builder = Call(nameof(XsltLibrary.X31ArrayNew), T.Item);
                    foreach (Expr member in array.Members)
                    {
                        builder = Call(nameof(XsltLibrary.X31ArrayMember), T.Item, builder, Items(Build(member)));
                    }
                    return Call(nameof(XsltLibrary.X31ArrayDone), T.Item, builder);
                }
                case CurlyArrayConstructor array:
                    return Call(nameof(XsltLibrary.X31ArrayOfItems), T.Item, array.Content == null ? _f.Sequence() : Items(Build(array.Content)));
                case UnaryLookup lookup:
                    return Lookup(FocusItem(), lookup.Key);
                case PostfixLookup lookup:
                    return Lookup(Items(Build(lookup.Base)), lookup.Key);
                case ArgumentPlaceholder:
                    throw NotYetSupported(e, "Partial function application (phase P3)");
            }
            throw new System.InvalidOperationException("Unexpected AST node " + e.GetType().Name);
        }

        // E?K (XPath 3.1 §3.11.3): the key specifier is a name, an integer, a parenthesized expression or *.
        private QilNode Lookup(QilNode bases, KeySpecifier key)
        {
            QilNode keys = key.Kind switch
            {
                KeySpecifierKind.NCName => Constant(AtomicValue.OfString(key.Name!)),
                KeySpecifierKind.Integer => Constant(AtomicValue.OfInteger(key.Integer)),
                KeySpecifierKind.Parenthesized => Items(Build(key.Expr!)),
                _ => _f.Sequence(),
            };
            return Call(nameof(XsltLibrary.X31Lookup), T.ItemS, bases, keys, key.Kind == KeySpecifierKind.Wildcard ? _f.True() : _f.False());
        }

        private QilNode Variable(VarRef v)
        {
            QName name = _types.Expand(v.Name, "");
            for (int i = _scope.Count - 1; i >= 0; i--)
            {
                if (_scope[i].Name == name)
                {
                    return _scope[i].Value;
                }
            }
            if (_env?.ResolveVariable(name) is QilNode hostVariable)
            {
                return hostVariable;
            }
            throw new XPathStaticException("XPST0008", $"Variable ${v.Name} is not declared", v.Span.Start);
        }

        private T Scoped<T>(QName name, QilNode value, System.Func<T> body)
        {
            _scope.Add((name, value));
            try
            {
                return body();
            }
            finally
            {
                _scope.RemoveAt(_scope.Count - 1);
            }
        }

        private QilNode For(IReadOnlyList<Binding> bindings, int index, Expr ret)
        {
            if (index == bindings.Count)
            {
                return Items(Build(ret));
            }
            QilIterator it = _f.For(Items(Build(bindings[index].Value)));
            QilNode body = Scoped(_types.Expand(bindings[index].Variable, ""), it, () => For(bindings, index + 1, ret));
            return _f.Loop(it, body);
        }

        private QilNode Let(IReadOnlyList<Binding> bindings, int index, Expr ret)
        {
            if (index == bindings.Count)
            {
                return Items(Build(ret));
            }
            QilIterator it = _f.Let(Items(Build(bindings[index].Value)));
            QilNode body = Scoped(_types.Expand(bindings[index].Variable, ""), it, () => Let(bindings, index + 1, ret));
            return _f.Loop(it, body);
        }

        // some/every: whether any (or no) combination of the bindings satisfies (or fails) the condition.
        private QilNode Quantified(QuantifiedExpr q)
        {
            QilNode Matches(int index)
            {
                if (index == q.Bindings.Count)
                {
                    QilNode condition = Bool(Build(q.Satisfies));
                    return _f.Conditional(q.Every ? _f.Not(condition) : condition, Call(nameof(XsltLibrary.X31BooleanItem), T.Item, _f.True()), _f.Sequence());
                }
                QilIterator it = _f.For(Items(Build(q.Bindings[index].Value)));
                QilNode body = Scoped(_types.Expand(q.Bindings[index].Variable, ""), it, () => Matches(index + 1));
                return _f.Loop(it, body);
            }
            QilNode witnesses = Matches(0);
            return q.Every ? _f.IsEmpty(witnesses) : _f.Not(_f.IsEmpty(witnesses));
        }

        private QilNode Binary(BinaryExpr b)
        {
            switch (b.Op)
            {
                case BinaryOp.Or:
                    return _f.Or(Bool(Build(b.Left)), Bool(Build(b.Right)));
                case BinaryOp.And:
                    return _f.And(Bool(Build(b.Left)), Bool(Build(b.Right)));
                case BinaryOp.GeneralEq: case BinaryOp.GeneralNe: case BinaryOp.GeneralLt:
                case BinaryOp.GeneralLe: case BinaryOp.GeneralGt: case BinaryOp.GeneralGe:
                    return Call(nameof(XsltLibrary.X31GeneralCompare), T.BooleanX, Items(Build(b.Left)), Items(Build(b.Right)), _f.Double(b.Op - BinaryOp.GeneralEq), StaticId());
                case BinaryOp.ValueEq: case BinaryOp.ValueNe: case BinaryOp.ValueLt:
                case BinaryOp.ValueLe: case BinaryOp.ValueGt: case BinaryOp.ValueGe:
                    return Call(nameof(XsltLibrary.X31ValueCompare), T.ItemS, Items(Build(b.Left)), Items(Build(b.Right)), _f.Double(b.Op - BinaryOp.ValueEq), StaticId());
                case BinaryOp.Is: case BinaryOp.Precedes: case BinaryOp.Follows:
                    return Call(nameof(XsltLibrary.X31NodeCompare), T.ItemS, Items(Build(b.Left)), Items(Build(b.Right)), _f.Double(b.Op - BinaryOp.Is));
                case BinaryOp.StringConcat:
                    return Call(nameof(XsltLibrary.X31StringConcat), T.ItemS, Items(Build(b.Left)), Items(Build(b.Right)));
                case BinaryOp.Range:
                    return Call(nameof(XsltLibrary.X31Range), T.ItemS, Items(Build(b.Left)), Items(Build(b.Right)));
                case BinaryOp.Union: case BinaryOp.Intersect: case BinaryOp.Except:
                    return Call(nameof(XsltLibrary.X31SetOp), T.ItemS, Items(Build(b.Left)), Items(Build(b.Right)), _f.Double(b.Op - BinaryOp.Union));
                default:
                    ArithmeticOp op = b.Op switch
                    {
                        BinaryOp.Add => ArithmeticOp.Add,
                        BinaryOp.Subtract => ArithmeticOp.Subtract,
                        BinaryOp.Multiply => ArithmeticOp.Multiply,
                        BinaryOp.Divide => ArithmeticOp.Divide,
                        BinaryOp.IntegerDivide => ArithmeticOp.IntegerDivide,
                        _ => ArithmeticOp.Modulo,
                    };
                    return Call(nameof(XsltLibrary.X31Arithmetic), T.ItemS, Items(Build(b.Left)), Items(Build(b.Right)), _f.Double((int)op));
            }
        }

        // ------------------------------------------------------------ paths

        // E1/E2: E1 must be nodes (XPTY0019); E2 is evaluated for each, with it as the focus. Node results are
        // in document order without duplicates; atomic results keep their order.
        private QilNode Path(PathExpr p)
        {
            QilNode left = Build(p.Left);
            if (p.Descendants)
            {
                left = Join(left, () => StepFor(Axis.DescendantOrSelf, new KindNodeTest(new AnyKindTest()), System.Array.Empty<Expr>()), needsLast: false);
            }
            return Join(left, () => Build(p.Right), UsesLast(p.Right));
        }

        private QilNode Join(QilNode left, System.Func<QilNode> right, bool needsLast)
        {
            var (it, body, input) = WithFocus(Nodes(left, "XPTY0019"), needsLast, right);
            QilNode loop = WrapLet(input, _f.Loop(it, Items(body)));
            return body.XmlType!.IsNode ? _f.DocOrderDistinct(loop) : Call(nameof(XsltLibrary.X31PathResult), T.ItemS, loop);
        }

        private QilNode SimpleMap(SimpleMapExpr m)
        {
            var (it, body, input) = WithFocus(Items(Build(m.Left)), UsesLast(m.Right), () => Items(Build(m.Right)));
            return WrapLet(input, _f.Loop(it, body));
        }

        private QilNode Filter(FilterExpr fe) => Predicate(Items(Build(fe.Base)), fe.Predicate);

        // E[P]: keeps the items for which P is true, or whose position equals P when P is numeric.
        private QilNode Predicate(QilNode input, Expr predicate)
        {
            var (it, condition, bound) = WithFocus(input, UsesLast(predicate), () => PredicateCondition(predicate));
            return WrapLet(bound, _f.Filter(it, condition));
        }

        private QilNode PredicateCondition(Expr predicate)
        {
            if (predicate is IntegerLiteral i)
            {
                return _f.Eq(FocusPosition(), _f.Double((double)i.Value));
            }
            QilNode p = Build(predicate);
            return IsBoolean(p) ? p : Call(nameof(XsltLibrary.X31Predicate), T.BooleanX, Items(p), FocusPosition());
        }

        private QilNode Step(AxisStep step) => StepFor(step.Axis, step.Test, step.Predicates);

        private QilNode StepFor(Axis axis, XPath31.NodeTest test, IReadOnlyList<Expr> predicates)
        {
            QilNode context = FocusNode();
            QilNode nodes = axis switch
            {
                Axis.Child or Axis.Attribute => _f.Content(context),
                Axis.Descendant => _f.Descendant(context),
                Axis.DescendantOrSelf => _f.DescendantOrSelf(context),
                Axis.Self => context,
                Axis.Parent => _f.Parent(context),
                Axis.Ancestor => _f.Ancestor(context),
                Axis.AncestorOrSelf => _f.AncestorOrSelf(context),
                Axis.Following => _f.XPathFollowing(context),
                Axis.FollowingSibling => _f.FollowingSibling(context),
                Axis.Preceding => _f.XPathPreceding(context),
                Axis.PrecedingSibling => _f.PrecedingSibling(context),
                _ => _f.XPathNamespace(context),
            };
            nodes = ApplyNodeTest(nodes, axis, test);
            foreach (Expr predicate in predicates)
            {
                nodes = Predicate(nodes, predicate);
            }
            // Reverse axes deliver nodes nearest first, which is what their predicates count; the step's result is
            // in document order.
            return AxisNames.IsReverse(axis) && axis != Axis.Parent ? _f.DocOrderDistinct(nodes) : nodes;
        }

        private static XmlNodeKindFlags AxisKinds(Axis axis) => axis switch
        {
            Axis.Attribute => XmlNodeKindFlags.Attribute,
            Axis.Namespace => XmlNodeKindFlags.Namespace,
            Axis.Self or Axis.DescendantOrSelf or Axis.AncestorOrSelf => XmlNodeKindFlags.Any,
            Axis.Parent or Axis.Ancestor => XmlNodeKindFlags.Element | XmlNodeKindFlags.Document,
            _ => XmlNodeKindFlags.Content,
        };

        private static XmlNodeKindFlags PrincipalKind(Axis axis) => axis switch
        {
            Axis.Attribute => XmlNodeKindFlags.Attribute,
            Axis.Namespace => XmlNodeKindFlags.Namespace,
            _ => XmlNodeKindFlags.Element,
        };

        private QilNode ApplyNodeTest(QilNode nodes, Axis axis, XPath31.NodeTest test)
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
                    condition = it =>
                        ns != null && local != null ? _f.Eq(_f.NameOf(it), _f.QName(local, ns)) :
                        ns != null ? _f.Eq(_f.NamespaceUriOf(it), _f.String(ns)) :
                        local != null ? _f.Eq(_f.LocalNameOf(it), _f.String(local)) :
                        _f.True();
                    break;
                }
                case KindNodeTest { Kind: var kind }:
                {
                    NodeKindTest resolved = _types.ResolveKind(kind);
                    kinds = kind switch
                    {
                        AnyKindTest => XmlNodeKindFlags.Any,
                        DocumentTest => XmlNodeKindFlags.Document,
                        ElementTest => XmlNodeKindFlags.Element,
                        AttributeTest => XmlNodeKindFlags.Attribute,
                        TextTest => XmlNodeKindFlags.Text,
                        CommentTest => XmlNodeKindFlags.Comment,
                        PITest => XmlNodeKindFlags.PI,
                        NamespaceNodeTest => XmlNodeKindFlags.Namespace,
                        _ => XmlNodeKindFlags.Any,
                    };
                    if (!resolved.AnyType)
                    {
                        return _f.Sequence(); // element(N, T) where T excludes untyped nodes
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

            kinds &= AxisKinds(axis);
            if (kinds == 0)
            {
                return _f.Sequence();
            }
            QilIterator i = _f.For(nodes);
            QilNode test2 = _f.IsType(i, T.NodeChoice(kinds));
            if (condition != null)
            {
                test2 = _f.And(test2, condition(i));
            }
            return _f.Filter(i, test2);
        }

        // ------------------------------------------------------------ functions and types

        private QilNode TypeId(SequenceType type) => _f.Double(ConstantPool.Add(_types.Resolve(type)));

        private QilNode CastTargetId(SingleType type)
        {
            var (target, kind) = _types.ResolveCastTarget(type.TypeName);
            if (target.IsAbstract)
            {
                throw new XPathStaticException("XPST0080", $"Cannot cast to {target.Name}", type.Span.Start);
            }
            return _f.Double(ConstantPool.Add(new CastTarget(target, _namespaces, kind)));
        }

        // Functions whose zero-argument form applies to the context item (F&O 3.1 §1.6.3).
        private static readonly HashSet<string> s_contextFunctions = new(System.StringComparer.Ordinal)
        {
            "string", "data", "number", "string-length", "normalize-space", "name", "local-name", "namespace-uri",
            "root", "node-name", "has-children", "base-uri", "document-uri", "generate-id", "path", "nilled",
        };

        private QilNode FunctionCall(Expr call, LexicalQName lexicalName, IReadOnlyList<Expr> args)
        {
            if (args.Any(a => a is ArgumentPlaceholder))
            {
                return PartialApplication(call, null, lexicalName, args);
            }
            QName name = _types.Expand(lexicalName, _sc.DefaultFunctionNamespace);
            if (HigherOrderFunction(name, args) is QilNode hof)
            {
                return hof;
            }
            if (_focusAbsent && name.NamespaceUri == QName.Ns.Fn && name.LocalName is "current" or "current-group" or "current-grouping-key" or "regex-group")
            {
                // XSLT's focus-dependent functions have no focus in a function body (the host's would be out of scope).
                return Call(nameof(XsltLibrary.X31RaiseItem), T.Item, _f.String("XPDY0002"), _f.String($"{name.LocalName}() can't be used in a function body"));
            }

            // Constructor functions: xs:T($arg) is $arg cast as xs:T? (XPath 3.1 §3.1.5.1).
            if (name.NamespaceUri == QName.Ns.Xs)
            {
                var found = StaticTypeResolver.FindCastTarget(name);
                if (found is not var (type, kind) || type.IsAbstract || args.Count != 1)
                {
                    throw new XPathStaticException("XPST0017", $"Unknown function {name.EQName}#{args.Count}", call.Span.Start);
                }
                int target = ConstantPool.Add(new CastTarget(type, _namespaces, kind));
                return Call(nameof(XsltLibrary.X31Cast), T.ItemS, Items(Build(args[0])), _f.Double(target), _f.True());
            }

            if (_env?.ResolveFunction(name, args, a => Items(Build(a))) is QilNode hostCall)
            {
                return hostCall;
            }

            if (name.NamespaceUri == QName.Ns.Fn)
            {
                switch (name.LocalName, args.Count)
                {
                    case ("position", 0):
                        return FocusPosition();
                    case ("last", 0):
                        return FocusLast();
                    case ("true", 0):
                        return _f.True();
                    case ("false", 0):
                        return _f.False();
                    case ("not", 1):
                        return _f.Not(Bool(Build(args[0])));
                    case ("boolean", 1):
                        return Bool(Build(args[0]));
                    case ("empty", 1):
                        return _f.IsEmpty(Items(Build(args[0])));
                    case ("exists", 1):
                        return _f.Not(_f.IsEmpty(Items(Build(args[0]))));
                    case (var local, 0) when s_contextFunctions.Contains(local):
                        return CallLibrary(call, name, new List<QilNode> { FocusItem() });
                    case ("lang" or "id" or "element-with-id" or "idref", 1):
                        // The node argument defaults to the context item.
                        return CallLibrary(call, name, new List<QilNode> { Items(Build(args[0])), FocusItem() });
                    case ("concat", > 4):
                        // concat is associative: concat(a, b, c, d, e, …) = concat(concat(a, b, c, d), e, …)
                        return FunctionCall(call, lexicalName, args.Skip(4).Prepend(new FunctionCall(lexicalName, args.Take(4).ToList()) { Span = call.Span }).ToList());
                }
            }
            return CallLibrary(call, name, args.Select(a => Items(Build(a))).ToList());
        }

        private QilNode CallLibrary(Expr call, QName name, List<QilNode> args)
        {
            FunctionDefinition? def = FunctionLibrary.Find(name.NamespaceUri, name.LocalName, args.Count);
            if (def == null)
            {
                throw new XPathStaticException("XPST0017", $"Unknown function {name.EQName}#{args.Count}", call.Span.Start);
            }
            string method = args.Count switch
            {
                0 => nameof(XsltLibrary.X31Call0),
                1 => nameof(XsltLibrary.X31Call1),
                2 => nameof(XsltLibrary.X31Call2),
                3 => nameof(XsltLibrary.X31Call3),
                4 => nameof(XsltLibrary.X31Call4),
                5 => nameof(XsltLibrary.X31Call5),
                _ => throw NotYetSupported(call, $"Calling {name}#{args.Count}"),
            };
            return Call(method, T.ItemS, args.Prepend(StaticId()).Prepend(_f.Double(def.Id)).ToArray());
        }
    }

    /// <summary>A compiled XPath 3.1 expression. Immutable and thread-safe; evaluate it as often as needed.</summary>
    internal sealed class XPath31Executable
    {
        public const string ControlNamespace = "urn:xsl3:xpath31";
        public const string ContextItemParam = "context-item";
        public const string ImplicitTimezoneParam = "implicit-timezone";
        public const string ReaderSettingsParam = "reader-settings";

        private readonly XmlILCommand _command;

        internal XPath31Executable(XmlILCommand command)
        {
            _command = command;
        }

        public IList<XPathItem> Evaluate(XPathItem? contextItem = null, IReadOnlyDictionary<QName, IList<XPathItem>>? variables = null, int? implicitTimezoneMinutes = null, XmlResolver? resolver = null)
        {
            var args = new XsltArgumentList();
            // fn:doc parses internal DTD subsets (for entities, attribute defaults and IDs); an external subset
            // is fetched through the caller's resolver, if any.
            args.AddParam(ReaderSettingsParam, ControlNamespace, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = resolver, MaxCharactersFromEntities = 10_000_000 });
            if (contextItem != null)
            {
                args.AddParam(ContextItemParam, ControlNamespace, contextItem);
            }
            if (implicitTimezoneMinutes is int tz)
            {
                args.AddParam(ImplicitTimezoneParam, ControlNamespace, tz);
            }
            if (variables != null)
            {
                foreach (var (name, value) in variables)
                {
                    args.AddParam(name.LocalName, name.NamespaceUri, value);
                }
            }
            return _command.Evaluate((contextItem as XPathNavigator)!, resolver, args);
        }
    }
}
