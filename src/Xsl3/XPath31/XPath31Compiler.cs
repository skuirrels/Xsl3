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

        /// <summary>Xsl3 (P6): the in-scope schema definitions (XPath 3.1 §2.1.1): the imported schemas, compiled; or null.</summary>
        public System.Xml.Schema.XmlSchemaSet? Schemas { get; set; }

        public string DefaultFunctionNamespace { get; set; } = QName.Ns.Fn;

        /// <summary>The static base URI (XPath 3.1 §2.1.1), or null if absent.</summary>
        public string? BaseUri { get; set; }

        /// <summary>Host compilation may consume forward nodes; do not collect operands before atomizing them.</summary>
        internal bool ForwardOnly { get; set; }

        /// <summary>XSLT's MapExpr uses XTDE3365 for duplicate keys (§21.4).</summary>
        internal bool Xslt { get; set; }

        /// <summary>External variables: their values are supplied to <see cref="XPath31Executable.Evaluate"/>.</summary>
        public List<QName> ExternalVariables { get; } = new();

        /// <summary>The default collation URI (XPath 3.1 §2.1.1); FOCH0002 at evaluation if it isn't supported.</summary>
        public string DefaultCollation { get; set; } = Collation.CodepointUri;

        /// <summary>
        /// XPath 1.0 compatibility mode (XPath 2.0 §2.1.1): set for expressions in a region of a stylesheet whose
        /// [xsl:]version is below 2.0 (XSLT 3.0 §3.9, backwards-compatible behaviour).
        /// </summary>
        public bool BackwardsCompatible { get; set; }

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

        /// <summary>The context item as an optional sequence, without demanding an absent focus.</summary>
        QilNode? ContextItemOrEmpty() => ContextItem();

        QilNode ContextPosition();

        QilNode ContextLast();

        // An empty value preserves absent focus until a captured numeric function is called.
        QilNode? CaptureNumericFocus(bool size) => null;

        /// <summary>A variable in scope in the host, or null.</summary>
        QilNode? ResolveVariable(QName name);

        /// <summary>Whether the host declaration permits only grounded values for this binding.</summary>
        bool IsGroundedVariable(QName name) => false;

        /// <summary>The lifted functions of the host's compilation unit, or null to keep them per expression.</summary>
        FunctionItemRegistry? FunctionItems { get; }

        /// <summary>A suspended population using the host's capture and pull ABI, when supported.</summary>
        QilNode? LiftPopulation(QilNode population) => null;

        /// <summary>A motionless node-header test through the host's capture ABI.</summary>
        QilNode? LiftHeaderTest(System.Func<QilNode, QilNode> predicate) => null;


        /// <summary>Independent map-entry values evaluated through the host's fork runtime, when supported.</summary>
        QilNode? ParallelMap(IReadOnlyList<QilNode> entries, bool xslt) => null;

        /// <summary>
        /// A call of a host function (or of a function the host overrides), or null to use the F&amp;O library.
        /// <paramref name="compile"/> compiles an argument to boxed QIL using the declared parameter type, if known.
        /// </summary>
        QilNode? ResolveFunction(QName name, IReadOnlyList<Expr> args, CompileXPathArgument compile);

        /// <summary>The declared parameter and result types of a host function, for a named reference to it; null if unknown.</summary>
        (IReadOnlyList<SequenceTypeTest> Parameters, SequenceTypeTest Result)? FunctionSignature(QName name, int arity) => null;

        /// <summary>
        /// Until disposed, host functions resolve to the components their package declares, not to their overrides,
        /// and abstract ones not at all (function-lookup in a used package); null if the host has no such components.
        /// </summary>
        System.IDisposable? DeclaredComponents() => null;

        /// <summary>Isolates host dynamic context that is not part of a function value's closure.</summary>
        System.IDisposable? FunctionBody() => null;
    }

    internal delegate QilNode CompileXPathArgument(Expr expression, ItemTest? requiredType = null);

    internal sealed partial class XPath31Compiler
    {
        private readonly QilFactory _qf;
        private readonly QilPatternFactory _f;
        private readonly IXPath31Environment? _env;
        private readonly XPathStaticContext _sc;
        private readonly StaticTypeResolver _types;
        private IXmlNamespaceResolver? _namespacesValue;
        private readonly List<(QName Name, QilNode Value)> _scope = new();
        private Focus? _focus;

        /// <summary>
        /// The focus inside a path step, predicate or simple map; null at the top level. A QIL graph is a tree, so
        /// position and last are factories: each use gets its own node (iterators may be referenced freely).
        /// </summary>
        private sealed record Focus(QilNode Item, System.Func<QilNode> Position, System.Func<QilNode>? Last, QilNode? ExactPosition = null, QilNode? OptionalItems = null);

        private XPath31Compiler(XPathStaticContext sc, QilPatternFactory? f = null, IXPath31Environment? env = null)
        {
            _f = f ?? new QilPatternFactory(new QilFactory(), false);
            _qf = _f.BaseFactory;
            _env = env;
            _sc = sc;
            _types = new StaticTypeResolver(p => sc.Namespaces.TryGetValue(p, out string? uri) ? uri : null, sc.DefaultElementNamespace, sc.Schemas);
        }

        // Xsl3: the static context's namespaces as a resolver, and its StaticInfo constant, made when first needed (most
        // compilers, such as Box's, need neither).
        private IXmlNamespaceResolver _namespaces => _namespacesValue ??= _sc.CreateResolver();

        // The StaticInfo id passed to runtime calls that read the static context; -1 until first needed.
        private int _staticIdValue = -1;

        private int StaticIdValue => _staticIdValue >= 0 ? _staticIdValue : _staticIdValue = ConstantPool.Add(new StaticInfo(_sc.BaseUri, _sc.DefaultCollation,
            new DecimalFormatTable(_sc.DefaultDecimalFormat, new Dictionary<QName, XPathDecimalFormat>(_sc.DecimalFormats), _namespaces)) { Namespaces = _namespaces });

        private QilNode StaticId() => _f.Double(StaticIdValue);

        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles the expression to IL at run time.")]
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
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles the expression to IL at run time.")]
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
            return compiler.Items(sc.ForwardOnly ? compiler.BuildValueSource(ast) : compiler.Build(ast));
        }

        internal static QilNode AtomizeSimpleContent(QilNode value, XPathStaticContext sc, QilPatternFactory f, IXPath31Environment env) =>
            new XPath31Compiler(sc, f, env).AtomizeEach(value, preserveTextNodes: true);

        /// <summary>
        /// Xsl3 (P4): a value template's expression as a native string: directly when it is one string or one node,
        /// otherwise its items, atomized and joined with single spaces (XSLT 3.0 §5.6.1).
        /// </summary>
        public static QilNode CompileEmbeddedString(Expr ast, XPathStaticContext sc, QilPatternFactory f, IXPath31Environment env)
        {
            var compiler = new XPath31Compiler(sc, f, env);
            QilNode built = sc.ForwardOnly ? compiler.BuildValueSource(ast) : compiler.Build(ast);
            // Several nodes are joined with spaces here, not an error: they don't take the optional-string path.
            bool severalNodes = built.XmlType!.IsSubtypeOf(T.NodeNotRtfS) && !built.XmlType.IsSubtypeOf(T.NodeNotRtfQ);
            if (!severalNodes && compiler.OptionalString(built) is QilNode s)
            {
                return s;
            }
            if (sc.Schemas == null && built.XmlType!.IsSubtypeOf(s_elementsOrAttributes))
            {
                QilIterator i = f.For(built);
                return f.BaseFactory.StrConcat(f.String(" "), f.Loop(i, f.XPathNodeValue(compiler.ValueReplayOperand(i, built))));
            }
            if (sc.ForwardOnly) built = compiler.AtomizeEach(built, preserveTextNodes: true);
            return compiler.Call(nameof(XsltLibrary.X31AvtString), T.StringX, compiler.Items(built));
        }

        /// <summary>Compiles a boxed host value (a variable reference, say) to an item* node.</summary>
        public static QilNode Box(QilNode n, QilPatternFactory f) => new XPath31Compiler(s_boxContext, f, null).Items(n);

        // (Boxing reads nothing of the static context: one for all.)
        private static readonly XPathStaticContext s_boxContext = new();

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

        private static readonly Dictionary<string, MethodInfo> s_methods = X31Methods();

        // Xsl3: only the X31* methods are bound; the extension-function members the enumeration also meets aren't, nor
        // the obsolete overloads kept for assemblies saved by earlier versions.
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "Binds only XsltLibrary's X31* methods, none of which is RequiresUnreferencedCode.")]
        private static Dictionary<string, MethodInfo> X31Methods() => typeof(XsltLibrary)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.StartsWith("X31", System.StringComparison.Ordinal) && !m.IsDefined(typeof(System.ObsoleteAttribute), false))
            .ToDictionary(m => m.Name);

        private QilNode Call(string method, XmlQueryType type, params QilNode[] args) =>
            _f.XsltInvokeEarlyBound(_f.QName(method, ""), s_methods[method], type, args);

        /// <summary>A call of an XsltLibrary X31 method, for host code (XSLT) that builds QIL around embedded expressions.</summary>
        internal static QilNode Invoke(QilPatternFactory f, string method, XmlQueryType type, params QilNode[] args) =>
            f.XsltInvokeEarlyBound(f.QName(method, ""), s_methods[method], type, args);

        private QilNode Constant(AtomicValue value)
        {
            QilNode constant = Call(nameof(XsltLibrary.X31Constant), T.Item, _f.Double(ConstantPool.Add(value)));
            // ConstantPool entries created here are already atomic. Streamed comparisons and
            // function arguments need no per-item atomization to consume them safely.
            if (_sc.ForwardOnly) s_atomicItems.AddOrUpdate(constant, value.Type);
            if (value.Type == AtomicTypes.String)
            {
                // Xsl3 (P4): known, for calls that take native strings.
                s_literalStrings.AddOrUpdate(constant, value.Value);
            }
            return constant;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<QilNode, string> s_literalStrings = new();

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
        private QilNode Bool(QilNode n) => IsBoolean(n) ? n
            : _sc.ForwardOnly && n.XmlType!.IsSubtypeOf(T.NodeNotRtfS) ? _f.Not(_f.IsEmpty(n))
            : _sc.ForwardOnly ? ForwardBoolean(n)
            : Call(nameof(XsltLibrary.X31Ebv), T.BooleanX, Items(n));

        private QilNode Nodes(QilNode n, string errorCode)
        {
            if (n.XmlType!.IsSubtypeOf(T.NodeNotRtfS)) return n;
            if (!_sc.ForwardOnly) return Call(nameof(XsltLibrary.X31Nodes), T.NodeNotRtfS, Items(n), _f.String(errorCode));
            // A generic item parameter may carry live nodes. Check each item before
            // its population advances rather than collecting the complete argument.
            QilIterator item = _f.For(Items(n));
            return _f.Loop(item, Call(nameof(XsltLibrary.X31Nodes), T.NodeNotRtfS, Items(item), _f.String(errorCode)));
        }

        private static XPathStaticException NotYetSupported(Expr e, string what) =>
            new("XSL30001", $"{what} is not supported yet", e.Span.Start);

        // ------------------------------------------------------------ focus

        private QilNode FocusItem(string? absentCode = null)
        {
            if (absentCode != null && !_focusAbsent)
            {
                QilNode? optional = _focus?.OptionalItems;
                if (_focus == null)
                    optional = _env == null ? Call(nameof(XsltLibrary.X31ContextItemOrEmpty), T.ItemS)
                        : _env.ContextItemOrEmpty() ?? _f.Sequence();
                if (optional != null)
                {
                    if (optional.XmlType!.IsSingleton) return optional;
                    QilIterator items = _f.Let(Items(optional));
                    return _f.Loop(items, _f.Conditional(_f.IsEmpty(items),
                        Call(nameof(XsltLibrary.X31RaiseItem), T.Item, _f.String(absentCode), _f.String("The context item is absent")),
                        Call(nameof(XsltLibrary.X31SingleItemOf), T.Item, items)));
                }
            }
            return _focus?.Item
                ?? (_focusAbsent ? Call(nameof(XsltLibrary.X31RaiseItem), T.Item, _f.String(absentCode ?? "XPDY0002"), _f.String("The context item is absent in a function body")) : null)
                ?? (_env != null
                    ? _env.ContextItem() ?? Call(nameof(XsltLibrary.X31RaiseItem), T.Item, _f.String(absentCode ?? "XPDY0002"), _f.String("The context item is absent"))
                    : Call(nameof(XsltLibrary.X31ContextItem), T.Item));
        }

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
        private (QilIterator Iterator, QilNode Body, QilIterator? Let) WithFocus(QilNode input, bool needsLast, System.Func<QilNode> body, Action? positionUsed = null)
        {
            System.Func<QilNode>? last = null;
            QilIterator? let = null;
            if (needsLast)
            {
                positionUsed?.Invoke();
                QilIterator bound = let = _f.Let(input);
                last = () => _f.XsltConvert(_f.Length(bound), T.DoubleX);
            }
            QilIterator it = _f.For(let ?? input);
            Focus? saved = _focus;
            _focus = new Focus(it, () => { positionUsed?.Invoke(); return _f.XsltConvert(_f.PositionOf(it), T.DoubleX); }, last);
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
        private bool UsesLast(Expr e) => (FocusAnalysis.Analyze(e,
            name => _types.Expand(name, _sc.DefaultFunctionNamespace).NamespaceUri == QName.Ns.Fn) & FocusDependency.Size) != 0;

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
                case ContextItemExpr dot:
                    return FocusItem(dot.AbsentCode);
                case VarRef v:
                    return Variable(v);
                case BinaryExpr b:
                    return Binary(b);
                case UnaryExpr u:
                    return Call(nameof(XsltLibrary.X31Unary), T.ItemS, NumericOperand(u.Operand), u.Minus ? _f.True() : _f.False());
                case CompatArgument c:
                    return Call(nameof(XsltLibrary.X31CompatArgument), T.ItemS, Items(Build(c.Operand)), _f.Double(c.Kind));
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
                case FunctionCall c when _sc.BackwardsCompatible:
                    try
                    {
                        return FunctionCall(c, c.Name, c.Arguments);
                    }
                    catch (XPathStaticException x) when (x.Code == "XPST0017" && _types.Expand(c.Name, _sc.DefaultFunctionNamespace).NamespaceUri != QName.Ns.Fn)
                    {
                        // Backwards-compatible behaviour: an unknown extension function is an error only if it is called
                        // (XTDE1425, XSLT 3.0 §24.1).
                        return Call(nameof(XsltLibrary.X31RaiseItem), T.Item, _f.String("XTDE1425"), _f.String(x.Message));
                    }
                case FunctionCall c:
                    return FunctionCall(c, c.Name, c.Arguments);
                case ArrowExpr { FunctionName: LexicalQName name } a:
                    return FunctionCall(a, name, a.Arguments.Prepend(a.Target).ToList());
                case InstanceOfExpr x:
                    if (_sc.ForwardOnly) return ForwardTypeTest(x.Operand, x.Type, assertion: false);
                    return Call(nameof(XsltLibrary.X31InstanceOf), T.BooleanX, Items(Build(x.Operand)), TypeId(x.Type));
                case TreatExpr x:
                    if (_sc.ForwardOnly) return ForwardTypeTest(x.Operand, x.Type, assertion: true);
                    return Call(nameof(XsltLibrary.X31Treat), T.ItemS, Items(Build(x.Operand)), TypeId(x.Type));
                case CastExpr x:
                    return Cast(x.Operand, CastTargetId(x.Type), x.Type.AllowsEmpty);
                case CastableExpr x:
                    return Call(nameof(XsltLibrary.X31Castable), T.BooleanX, AtomicOperand(x.Operand), CastTargetId(x.Type), x.Type.AllowsEmpty ? _f.True() : _f.False());
                case InlineFunctionExpr fn:
                    return InlineFunction(fn);
                case NamedFunctionRef r:
                    return NamedFunctionReference(r);
                case DynamicFunctionCall d:
                    return DynamicFunctionCall(d);
                case ArrowExpr { FunctionExpr: Expr fe } a:
                    return DynamicFunctionCall(new DynamicFunctionCall(fe, a.Arguments.Prepend(a.Target).ToList()) { Span = a.Span });
                case MapConstructor map:
                    return Map(map);
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
            if (_sc.ForwardOnly && GroundedSetOperand(bindings[index].Value)) GroundedSetBindings.Values.AddOrUpdate(it, GroundedSetBindings.Marker);
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
            if (_sc.ForwardOnly && GroundedSetOperand(bindings[index].Value)) GroundedSetBindings.Values.AddOrUpdate(it, GroundedSetBindings.Marker);
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
                case >= BinaryOp.GeneralEq and <= BinaryOp.GeneralGe when _sc.BackwardsCompatible:
                    return Call(nameof(XsltLibrary.X31CompatGeneralCompare), T.BooleanX, Items(Build(b.Left)), Items(Build(b.Right)), _f.Double(b.Op - BinaryOp.GeneralEq), StaticId());
                case BinaryOp.GeneralEq when b.Right is BinaryExpr { Op: BinaryOp.Range } range:
                    // $x = (a to b): a range test, not a comparison with each integer of the range.
                    return Call(nameof(XsltLibrary.X31InRange), T.BooleanX, AtomicOperand(b.Left), AtomicOperand(range.Left), AtomicOperand(range.Right), StaticId());
                case BinaryOp.GeneralEq when b.Left is BinaryExpr { Op: BinaryOp.Range } range:
                    return Call(nameof(XsltLibrary.X31InRange), T.BooleanX, AtomicOperand(b.Right), AtomicOperand(range.Left), AtomicOperand(range.Right), StaticId());
                case BinaryOp.GeneralLt or BinaryOp.GeneralLe or BinaryOp.GeneralGt or BinaryOp.GeneralGe when b.Right is BinaryExpr { Op: BinaryOp.Range } range:
                    return Call(nameof(XsltLibrary.X31RangeCompare), T.BooleanX, AtomicOperand(b.Left), AtomicOperand(range.Left), AtomicOperand(range.Right), _f.Double(b.Op - BinaryOp.GeneralEq), StaticId());
                case BinaryOp.GeneralLt or BinaryOp.GeneralLe or BinaryOp.GeneralGt or BinaryOp.GeneralGe when b.Left is BinaryExpr { Op: BinaryOp.Range } range:
                {
                    // (a to b) < $x is $x > (a to b).
                    BinaryOp mirrored = b.Op switch { BinaryOp.GeneralLt => BinaryOp.GeneralGt, BinaryOp.GeneralLe => BinaryOp.GeneralGe, BinaryOp.GeneralGt => BinaryOp.GeneralLt, _ => BinaryOp.GeneralLe };
                    return Call(nameof(XsltLibrary.X31RangeCompare), T.BooleanX, AtomicOperand(b.Right), AtomicOperand(range.Left), AtomicOperand(range.Right), _f.Double(mirrored - BinaryOp.GeneralEq), StaticId());
                }
                case BinaryOp.GeneralEq: case BinaryOp.GeneralNe: case BinaryOp.GeneralLt:
                case BinaryOp.GeneralLe: case BinaryOp.GeneralGt: case BinaryOp.GeneralGe:
                    return Call(nameof(XsltLibrary.X31GeneralCompare), T.BooleanX, AtomicOperand(b.Left), AtomicOperand(b.Right), _f.Double(b.Op - BinaryOp.GeneralEq), StaticId());
                case BinaryOp.ValueEq: case BinaryOp.ValueNe: case BinaryOp.ValueLt:
                case BinaryOp.ValueLe: case BinaryOp.ValueGt: case BinaryOp.ValueGe:
                    return Call(nameof(XsltLibrary.X31ValueCompare), T.ItemS, AtomicOperand(b.Left), AtomicOperand(b.Right), _f.Double(b.Op - BinaryOp.ValueEq), StaticId());
                case BinaryOp.Is: case BinaryOp.Precedes: case BinaryOp.Follows:
                    return Call(nameof(XsltLibrary.X31NodeCompare), T.ItemS, Items(Build(b.Left)), Items(Build(b.Right)), _f.Double(b.Op - BinaryOp.Is));
                case BinaryOp.StringConcat:
                    return Call(nameof(XsltLibrary.X31StringConcat), T.ItemS, Items(Build(b.Left)), Items(Build(b.Right)));
                case BinaryOp.Range when _sc.BackwardsCompatible:
                    return Call(nameof(XsltLibrary.X31Range), T.ItemS, Build(new CompatArgument(b.Left, 3) { Span = b.Left.Span }), Build(new CompatArgument(b.Right, 3) { Span = b.Right.Span }));
                case BinaryOp.Range:
                    return Call(nameof(XsltLibrary.X31Range), T.ItemS, Items(Build(b.Left)), Items(Build(b.Right)));
                case BinaryOp.Union: case BinaryOp.Intersect: case BinaryOp.Except:
                    if (ForwardClimbingSet(b) is { } climbing) return climbing;
                    if (ForwardSharedSet(b) is { } shared) return shared;
                    if (ForwardHeaderSet(b) is { } headerSet) return headerSet;
                    if (ForwardIndependentSet(b) is { } forward) return forward;
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
                    QilNode arithmetic = Call(nameof(XsltLibrary.X31Arithmetic), T.ItemS, NumericOperand(b.Left), NumericOperand(b.Right), _f.Double((int)op));
                    // Arithmetic produces atomic values (or empty), never streamed nodes. Keep that
                    // contract when a surrounding comparison decides whether to atomize its operands.
                    if (_sc.ForwardOnly) s_atomicItems.AddOrUpdate(arithmetic, AtomicTypes.AnyAtomicType);
                    return arithmetic;
            }
        }

        // ------------------------------------------------------------ paths

        // E1/E2: E1 must be nodes (XPTY0019); E2 is evaluated for each, with it as the focus. Node results are
        // in document order without duplicates; atomic results keep their order.
        // Xsl3 (P4): node sequences known to be in document order, distinct and all at one depth (from the host: a
        // group of a population of child steps). A child or attribute step from them needs no merge into document
        // order: their results, one after another, already are in it.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<QilNode, object> s_sameDepth = new();

        internal static QilNode SameDepth(QilNode nodes)
        {
            s_sameDepth.AddOrUpdate(nodes, s_sameDepth);
            return nodes;
        }

        private QilNode Path(PathExpr p)
        {
            if (ForwardSetPath(p) is { } setPath) return setPath;
            if (ForwardSequencePath(p) is { } sequencePath) return sequencePath;
            if (ForwardDescendantChildren(p) is { } children) return children;
            if (_sc.ForwardOnly && NeedsHeaderPath(p) && ForwardHeaderSet(p) is { } headers) return headers;
            if (_sc.ForwardOnly && p.Descendants && p.Right is AxisStep { Axis: Axis.Child } step)
            {
                // A//child::N is A/descendant::N. The expanded descendant-or-self/child form would
                // traverse each streamed ancestor twice. Predicates can follow the rewrite only if their
                // compiled conditions never use the outer position or size (including numeric predicates).
                bool positional = false;
                QilNode candidate = Join(Build(p.Left), () => StepFor(Axis.Descendant, step.Test, step.Predicates, () => positional = true), needsLast: false);
                if (!positional) return candidate;
            }
            if (!_sc.ForwardOnly && IndexedPath(p) is QilNode indexed)
            {
                return indexed;
            }
            return PathAsWritten(p);
        }

        private QilNode PathAsWritten(PathExpr p)
        {
            if (ParameterChildPath(p) is { } parameterPath) return parameterPath;
            QilNode left = Build(p.Left);
            // A singleton (including an optional one) has disjoint child and attribute results.
            // Retain this proof before Join introduces a sort that would exhaust a live selection.
            if (!p.Descendants && p.Right is AxisStep { Axis: Axis.Child or Axis.Attribute } step
                && (s_sameDepth.TryGetValue(left, out _) || _sc.ForwardOnly && (!left.XmlType!.MaybeMany || DisjointChildAnchors(p.Left)
                    || step.Axis == Axis.Attribute && left.XmlType.IsDod))
                && left.XmlType!.IsSubtypeOf(T.NodeNotRtfS))
            {
                var (sit, sbody, sinput) = WithFocus(left, UsesLast(p.Right), () => Build(p.Right));
                QilNode loop = WrapLet(sinput, _f.Loop(sit, sbody));
                return step.Axis == Axis.Child ? SameDepth(loop)
                    : _sc.ForwardOnly && left.XmlType.IsDod ? _f.TypeAssert(loop, T.NodeSDod) : loop;
            }
            if (p.Descendants)
            {
                left = Join(left, () => StepFor(Axis.DescendantOrSelf, new KindNodeTest(new AnyKindTest()), System.Array.Empty<Expr>()), needsLast: false);
            }
            return Join(left, () => Build(p.Right), UsesLast(p.Right));
        }

        private static int s_indexSites;

        /// <summary>
        /// Xsl3 (perf, remaining-scope 1.5): automatic indexing. An absolute path whose last step filters on one attribute
        /// or child compared with a value that doesn't depend on the focus (<c>/orders/product[@sku = $line/@sku]</c>)
        /// looks the value up in an index of the document, built on the second evaluation for that document, instead of
        /// testing every node. Only where <c>=</c> compares strings: the codepoint collation, no XPath 1.0 compatibility
        /// mode, and (checked each time) values that are nodes or strings; otherwise the path is evaluated as written.
        /// </summary>
        private QilNode? IndexedPath(PathExpr p)
        {
            if (_sc.BackwardsCompatible || _sc.DefaultCollation != Functions.Collation.CodepointUri ||
                p.Right is not AxisStep { Predicates: [BinaryExpr { Op: BinaryOp.GeneralEq } eq] } step || !Absolute(p.Left))
            {
                return null;
            }
            (Expr? key, Expr? value) = KeyStep(eq.Left) && FocusFreeValue(eq.Right) ? (eq.Left, eq.Right)
                : KeyStep(eq.Right) && FocusFreeValue(eq.Left) ? (eq.Right, eq.Left) : (null, null);
            if (key == null)
            {
                return null;
            }
            int site = System.Threading.Interlocked.Increment(ref s_indexSites);
            QilIterator values = _f.Let(Items(Build(value!)));
            // (The context node: the runtime finds its root, and scans, so that the path raises XPDY0050, when the root
            // isn't a document node.)
            QilIterator root = _f.Let(FocusNode());
            QilIterator state = _f.Let(Call(nameof(XsltLibrary.X31IndexState), T.DoubleX, _f.Double(site), root, values));
            // One pass over the candidates (the path without its predicate: the predicate doesn't use the position, so
            // filtering them all is filtering each step's), with each one's key values compiled once: kept when a key
            // equals a value (state 0, the path as written), or put in the index being built (state 2).
            var all = new PathExpr(p.Left, step with { Predicates = System.Array.Empty<Expr>() }, p.Descendants) { Span = p.Span };
            var (it, body, input) = WithFocus(Nodes(Build(all), "XPTY0019"), needsLast: false, () =>
            {
                QilNode node = FocusNode();
                return _f.Conditional(Call(nameof(XsltLibrary.X31IndexFilter), T.BooleanX, state, node, Items(Build(key)), values, StaticId()),
                    FocusNode(), _f.Sequence());
            });
            QilNode scan = WrapLet(input, _f.Loop(it, body));
            QilNode lookup = Call(nameof(XsltLibrary.X31IndexGet), T.NodeNotRtfS, _f.Double(site), root, values);
            // The scan unless the index is there; the lookup unless the path is evaluated as written.
            return _f.Loop(values, _f.Loop(root, _f.Loop(state, _f.Sequence(
                _f.Conditional(_f.Eq(state, _f.Double(1)), _f.Sequence(), scan),
                _f.Conditional(_f.Eq(state, _f.Double(0)), _f.Sequence(), lookup)))));
        }

        // A path from the root with no predicates: the same nodes wherever it is evaluated in the document.
        private static bool Absolute(Expr e) => e switch
        {
            RootExpr => true,
            PathExpr { Left: var left, Right: AxisStep { Predicates.Count: 0 } } => Absolute(left),
            _ => false,
        };

        // One attribute or child, by name.
        private static bool KeyStep(Expr e) =>
            e is AxisStep { Axis: Axis.Attribute or Axis.Child, Test: NameTest { LocalName: not null }, Predicates.Count: 0 };

        // A value that doesn't depend on the focus: a variable, a string literal, or steps without predicates from a variable.
        private static bool FocusFreeValue(Expr e) => e switch
        {
            VarRef or StringLiteral => true,
            PathExpr { Descendants: false, Left: var left, Right: AxisStep { Predicates.Count: 0 } } => FocusFreeValue(left),
            _ => false,
        };

        private QilNode Join(QilNode left, System.Func<QilNode> right, bool needsLast)
        {
            var (it, body, input) = WithFocus(Nodes(left, "XPTY0019"), needsLast, right);
            QilNode loop = WrapLet(input, _f.Loop(it, Items(body)));
            if (_sc.ForwardOnly && AtomicItems(body)) return loop;
            return body.XmlType!.IsNode ? _f.DocOrderDistinct(loop) : Call(nameof(XsltLibrary.X31PathResult), T.ItemS, loop);
        }

        private QilNode SimpleMap(SimpleMapExpr m)
        {
            var (it, body, input) = WithFocus(Items(Build(m.Left)), UsesLast(m.Right), () => Items(Build(m.Right)));
            return WrapLet(input, _f.Loop(it, body));
        }

        private QilNode Filter(FilterExpr fe)
        {
            if (fe.Predicate is not IntegerLiteral && FocusFree(fe.Predicate))
            {
                // Xsl3 (perf): a predicate that can't depend on the focus ($seq[$i], $seq[$i + 1]) has one value for every
                // item: evaluated once, when there are items to filter, and used as a position by index.
                QilIterator items = _f.Let(Items(Build(fe.Base)));
                return _f.Loop(items, _f.Conditional(_f.IsEmpty(items), _f.TypeAssert(_f.Sequence(), T.ItemS),
                    Call(nameof(XsltLibrary.X31Subscript), T.ItemS, items, Items(Build(fe.Predicate)))));
            }
            return Predicate(Items(Build(fe.Base)), fe.Predicate);
        }

        // Whether an expression's value can't depend on the focus it is evaluated in: variables, numbers and arithmetic.
        private static bool FocusFree(Expr e) => e switch
        {
            VarRef or IntegerLiteral or DecimalLiteral or DoubleLiteral => true,
            BinaryExpr { Op: BinaryOp.Add or BinaryOp.Subtract or BinaryOp.Multiply or BinaryOp.Divide or BinaryOp.IntegerDivide or BinaryOp.Modulo } b =>
                FocusFree(b.Left) && FocusFree(b.Right),
            UnaryExpr u => FocusFree(u.Operand),
            SequenceExpr { Items.Count: 1 } s => FocusFree(s.Items[0]),
            _ => false,
        };

        // E[P]: keeps the items for which P is true, or whose position equals P when P is numeric.
        private QilNode Predicate(QilNode input, Expr predicate, Action? positionUsed = null)
        {
            if (_sc.ForwardOnly && predicate is IntegerLiteral { Value.Sign: > 0 } integer)
            {
                positionUsed?.Invoke();
                return ForwardIntegerPredicate(input, Constant(AtomicValue.OfInteger(integer.Value)));
            }
            var (it, condition, bound) = WithFocus(input, UsesLast(predicate), () => PredicateCondition(predicate), positionUsed);
            return WrapLet(bound, _f.Filter(it, condition));
        }

        private QilNode PredicateCondition(Expr predicate)
        {
            if (predicate is IntegerLiteral i)
            {
                return _f.Eq(FocusPosition(), _f.Double((double)i.Value));
            }
            QilNode p = Build(predicate);
            // A node sequence is true by existence, never by its numeric position. Keeping
            // that proof avoids expanding A//N[@a] into two traversals of a forward reader.
            if (_sc.ForwardOnly && p.XmlType!.IsSubtypeOf(T.NodeNotRtfS)) return Bool(p);
            return IsBoolean(p) ? p : Call(nameof(XsltLibrary.X31Predicate), T.BooleanX, Items(p), FocusPosition());
        }

        private QilNode Step(AxisStep step) => StepFor(step.Axis, step.Test, step.Predicates);

        private QilNode StepFor(Axis axis, XPath31.NodeTest test, IReadOnlyList<Expr> predicates, Action? positionUsed = null, bool topmost = false)
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
            if (topmost) Xsl3.IlGen.OptimizerPatterns.Write(nodes).AddPattern(Xsl3.IlGen.OptimizerPatternName.Topmost);
            nodes = ApplyNodeTest(nodes, axis, test);
            foreach (Expr predicate in predicates)
            {
                nodes = Predicate(nodes, predicate, positionUsed);
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

        private static XmlNodeKindFlags NodeTestKinds(Axis axis, NodeTest test) => test switch
        {
            NameTest => PrincipalKind(axis),
            KindNodeTest { Kind: DocumentTest } => XmlNodeKindFlags.Document,
            KindNodeTest { Kind: ElementTest } => XmlNodeKindFlags.Element,
            KindNodeTest { Kind: AttributeTest } => XmlNodeKindFlags.Attribute,
            KindNodeTest { Kind: TextTest } => XmlNodeKindFlags.Text,
            KindNodeTest { Kind: CommentTest } => XmlNodeKindFlags.Comment,
            KindNodeTest { Kind: PITest } => XmlNodeKindFlags.PI,
            KindNodeTest { Kind: NamespaceNodeTest } => XmlNodeKindFlags.Namespace,
            _ => XmlNodeKindFlags.Any,
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
                    if (axis == Axis.Attribute && ns != null && local != null && nodes.NodeType == QilNodeType.Content)
                    {
                        // Xsl3 (P4): @name is at most one attribute, as the optimizer would find later; knowing it now
                        // lets the typed paths take it as an optional string.
                        return _qf.Attribute(((QilUnary)nodes).Child, _f.QName(local, ns));
                    }
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
                    kinds = NodeTestKinds(axis, test);
                    // Xsl3 (P6): a test that names a type, or schema-element()/schema-attribute(), is decided at run time
                    // when there are schemas: a node may then be typed.
                    bool typed = resolved.TypeName != null && _sc.Schemas != null;
                    if (!resolved.AnyType && !typed)
                    {
                        return _f.Sequence(); // element(N, T) where T excludes untyped nodes
                    }
                    if (resolved.DocumentElement != null || typed)
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
            if (_sc.ForwardOnly && axis == Axis.Attribute && condition != null)
            {
                // Attribute name tests must stay above an attribute-only iterator. A mixed content scan
                // would advance through children to prove that there are no more matching attributes.
                QilIterator attribute = _f.For(nodes);
                nodes = _f.Filter(attribute, _f.IsType(attribute, T.Attribute));
                QilIterator named = _f.For(nodes);
                return _f.Filter(named, condition(named));
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
            QName typeName = _types.Expand(type.TypeName, _sc.DefaultElementNamespace);
            if (StaticTypeResolver.FindCastTarget(typeName) == null && _types.FindSchemaCastTarget(typeName, _namespaces) is { } schemaTarget)
            {
                return _f.Double(ConstantPool.Add(schemaTarget)); // Xsl3 (P6): a simple type an imported schema defines
            }
            var (target, kind) = _types.ResolveCastTarget(type.TypeName);
            if (target.IsAbstract)
            {
                throw new XPathStaticException("XPST0080", $"Cannot cast to {target.Name}", type.Span.Start);
            }
            return _f.Double(ConstantPool.Add(new CastTarget(target, _namespaces, kind)));
        }

        // Functions whose zero-argument form applies to the context item (F&O 3.1 §1.6.3).
        internal static readonly HashSet<string> s_contextFunctions = new(System.StringComparer.Ordinal)
        {
            "string", "data", "number", "string-length", "normalize-space", "name", "local-name", "namespace-uri",
            "root", "node-name", "has-children", "base-uri", "document-uri", "generate-id", "path", "nilled",
        };

        // An operand of arithmetic in XPath 1.0 compatibility mode: its first item as a number (XPath 2.0 §3.4).
        private QilNode NumericOperand(Expr expression)
        {
            if (!_sc.BackwardsCompatible) return AtomicOperand(expression);
            QilNode operand = Items(Build(expression));
            if (_sc.ForwardOnly && !AtomicItems(operand) && !operand.XmlType!.IsEmpty)
            {
                // Compatibility mode converts only the first item. Convert it while
                // live, preserving its scalar atomization check and empty -> NaN rule.
                QilNode first = ForwardSubsequence(operand, _f.Double(1), _f.Double(1), head: true);
                QilIterator item = _f.For(first);
                operand = _f.Loop(item, Call(nameof(XsltLibrary.X31CompatArgument), T.ItemS, Items(item), _f.Double(3)));
            }
            return Call(nameof(XsltLibrary.X31CompatNumeric), T.ItemS, operand);
        }

        /// <summary>
        /// In XPath 1.0 compatibility mode, an argument of a built-in function that expects one item: its first item,
        /// as a string or number where those are expected (XPath 2.0 §3.1.5).
        /// </summary>
        private sealed record CompatArgument(Expr Operand, int Kind) : Expr;

        private IReadOnlyList<Expr> CompatArguments(QName name, IReadOnlyList<Expr> args)
        {
            if (!_sc.BackwardsCompatible || name.NamespaceUri != QName.Ns.Fn || FunctionLibrary.Find(name.NamespaceUri, name.LocalName, args.Count) is not FunctionDefinition def)
            {
                return args;
            }
            var converted = new List<Expr>(args.Count);
            for (int i = 0; i < args.Count; i++)
            {
                SequenceTypeTest type = def.Parameters[Math.Min(i, def.Parameters.Count - 1)].Type;
                bool single = type.Cardinality is Cardinality.ExactlyOne or Cardinality.ZeroOrOne;
                int kind = !single ? 0
                    : type.Item is AtomicTypeTest atomic ? atomic.Type == AtomicTypes.String ? 1 : atomic.Type == AtomicTypes.Double ? 2 : 3
                    : type.Item is NumericTest ? 2
                    : type.Item != null ? 4 : 0;
                converted.Add(kind == 0 ? args[i] : new CompatArgument(args[i], kind) { Span = args[i].Span });
            }
            return converted;
        }

        private QilNode FunctionCall(Expr call, LexicalQName lexicalName, IReadOnlyList<Expr> args)
        {
            if (args.Any(a => a is ArgumentPlaceholder))
            {
                return PartialApplication(call, null, lexicalName, args);
            }
            QName name = _types.Expand(lexicalName, _sc.DefaultFunctionNamespace);
            args = CompatArguments(name, args);
            if (_sc.ForwardOnly && name.NamespaceUri == QName.Ns.Fn && name.LocalName == "outermost" && args.Count == 1
                && (OutermostStep(args[0]) ?? ForwardOutermost(args[0])) is { } outermost) return outermost;
            if (HigherOrderFunction(name, args) is QilNode hof)
            {
                return hof;
            }
            if (_focusAbsent && name.NamespaceUri == QName.Ns.Fn && name.LocalName == "regex-group" && args.Count == 1)
            {
                // The captured substrings are cleared in a function body (XSLT 3.0 §5.4.4): a zero-length string.
                return Call(nameof(XsltLibrary.X31StringItem), T.Item, _f.String(""));
            }
            if (_focusAbsent && name.NamespaceUri == QName.Ns.Fn && name.LocalName is "current" or "current-group" or "current-grouping-key")
            {
                // XSLT's focus-dependent functions have no focus in a function body (the host's would be out of scope).
                // (XSLT 3.0 §20.4.1 and §14.2.1: XTDE1360 for current(), XTDE1061 and XTDE1071 for the grouping functions.)
                string code = name.LocalName switch { "current" => "XTDE1360", "current-group" => "XTDE1061", _ => "XTDE1071" };
                return Call(nameof(XsltLibrary.X31RaiseItem), T.Item, _f.String(code), _f.String($"{name.LocalName}() can't be used in a function body"));
            }

            // Constructor functions: xs:T($arg) is $arg cast as xs:T? (XPath 3.1 §3.1.5.1).
            if (name.NamespaceUri == QName.Ns.Xs && name.LocalName == "error" && args.Count == 1)
            {
                // xs:error (XPath 3.1 §3.1.5.1): a union type with no members: () casts to (), anything else is FORG0001.
                return Call(nameof(XsltLibrary.X31CastToError), T.ItemS, AtomicOperand(args[0]));
            }
            if (name.NamespaceUri == QName.Ns.Xs)
            {
                var found = StaticTypeResolver.FindCastTarget(name);
                if (found is not var (type, kind) || type.IsAbstract || args.Count != 1)
                {
                    throw new XPathStaticException("XPST0017", $"Unknown function {name.EQName}#{args.Count}", call.Span.Start);
                }
                int target = ConstantPool.Add(new CastTarget(type, _namespaces, kind));
                return Cast(args[0], _f.Double(target), allowEmpty: true);
            }

            if (args.Count == 1 && _types.FindSchemaCastTarget(name, _namespaces) is { } schemaConstructor)
            {
                // Xsl3 (P6): the constructor function of a simple type an imported schema defines.
                return Cast(args[0], _f.Double(ConstantPool.Add(schemaConstructor)), allowEmpty: true);
            }
            if (_env?.ResolveFunction(name, args, FunctionArgument) is QilNode hostCall)
            {
                return hostCall;
            }

            if (name.NamespaceUri == QName.Ns.Fn)
            {
                switch (name.LocalName, args.Count)
                {
                    case ("head", 1) when _sc.ForwardOnly:
                        return ForwardSubsequence(Build(args[0]), _f.Double(1), _f.Double(1), head: true);
                    case ("tail", 1) when _sc.ForwardOnly:
                        return ForwardSubsequence(Build(args[0]), _f.Double(2), null);
                    case ("subsequence", 2 or 3) when _sc.ForwardOnly:
                        return ForwardSubsequence(Build(args[0]), AtomicOperand(args[1]), args.Count == 3 ? AtomicOperand(args[2]) : null);
                    case ("remove", 2) when _sc.ForwardOnly:
                        return ForwardRemove(Build(args[0]), AtomicOperand(args[1]));
                    case ("unordered", 1) when _sc.ForwardOnly:
                        return Build(args[0]);
                    case ("zero-or-one" or "one-or-more" or "exactly-one", 1) when _sc.ForwardOnly:
                        return ForwardCardinality(args[0], name.LocalName);
                    case ("insert-before", 3) when _sc.ForwardOnly:
                        return ForwardInsertBefore(Build(args[0]), AtomicOperand(args[1]), Build(args[2]), DisjointChildAnchors(args[0]));
                    case ("trace", 1 or 2) when _sc.ForwardOnly:
                        return ForwardTrace(call, name, args[0], args.Count == 2 ? args[1] : null);
                    case ("deep-equal", 2 or 3) when _sc.ForwardOnly && ForwardDeepEqual(args) is { } comparison:
                        return comparison;
                    case ("xml-to-json", 1 or 2) when _sc.ForwardOnly:
                        return ForwardXmlToJson(args[0], args.Count == 2 ? args[1] : null);
                    case ("position", 0):
                        return _focus?.ExactPosition ?? FocusPosition();
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
                    case ("empty" or "exists", 1):
                    {
                        QilNode source = Items(Build(args[0]));
                        if (_sc.ForwardOnly && s_functionParameters.TryGetValue(source, out QilNode? parameter))
                        {
                            QilNode exists = StatefulCall(nameof(XsltLibrary.X31FunctionParameterExists), T.BooleanX, parameter);
                            return name.LocalName == "exists" ? exists : _f.Not(exists);
                        }
                        QilNode empty = _f.IsEmpty(source);
                        return name.LocalName == "empty" ? empty : _f.Not(empty);
                    }
                    case ("string-length" or "normalize-space", 0) when _sc.Schemas != null:
                        // Xsl3 (P6): of the context item's string value, not its typed value (F&O 3.1 §5.4.4, §5.4.5).
                        return FunctionCall(call, lexicalName, new List<Expr> { new FunctionCall(new LexicalQName(null, "string", QName.Ns.Fn), new List<Expr> { new ContextItemExpr() }) { Span = call.Span } });
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
            if (name.NamespaceUri == QName.Ns.Fn)
            {
                // Xsl3 (P4): string functions of single strings or nodes run on native strings, without item lists.
                List<QilNode> built = _sc.ForwardOnly ? args.Select(BuildValueSource).ToList() : args.Select(Build).ToList();
                switch (name.LocalName, built.Count)
                {
                    case ("count", 1) when _sc.ForwardOnly && s_functionParameters.TryGetValue(built[0], out QilNode? parameter):
                        return StatefulCall(nameof(XsltLibrary.X31FunctionParameterCount), T.Item, parameter);
                    case ("count", 1):
                        // A native double is an xs:integer to Items (as positions are).
                        return _f.XsltConvert(_f.Length(Items(built[0])), T.DoubleX);
                    case ("data", 1) when _sc.ForwardOnly:
                        return AtomizeEach(built[0]);
                    case ("string-join", 1) when _sc.ForwardOnly:
                        // F&O 3.1 §5.4.2 defines the omitted separator as "".
                        return ForwardStringJoin(built[0], _f.String(""));
                    case ("string-join", 2) when _sc.ForwardOnly && !built[1].XmlType!.IsNode && NativeString(built[1]) is QilNode separator:
                        return ForwardStringJoin(built[0], separator);
                    case ("string", 1) when _sc.ForwardOnly && !AtomicItems(built[0]):
                        return CallLibrary(call, name, new List<QilNode> { StringArguments(built[0]) });
                    case ("avg" or "min" or "max", 1) when _sc.ForwardOnly && UntypedNumericNodes(built[0])
                        && (name.LocalName == "avg" || _sc.DefaultCollation == Functions.Collation.CodepointUri):
                        return ForwardUntypedAggregate(name.LocalName, built[0]);
                    case ("sum", 1) when UntypedNumericNodes(built[0]):
                    {
                        // Untyped nodes have one untypedAtomic value each, converted to double by sum.
                        // Typed nodes can have list values, decimals or exact integers and use atomization below.
                        QilNode doubles = UntypedDoubles(built[0]);
                        // A list-valued library argument would cache every double. The QIL aggregate
                        // folds the forward iterator in order and yields at most one value; the existing
                        // helper preserves xs:integer zero for empty input and xs:double otherwise.
                        return Call(nameof(XsltLibrary.X31SumDoubles), T.Item, _sc.ForwardOnly ? _f.Sum(doubles) : doubles);
                    }
                    case ("number", 1) when OptionalString(built[0]) is QilNode numberString:
                        // A string, or zero or one node (whose value is xs:untypedAtomic): cast as a string; () is "", NaN.
                        return Call(nameof(XsltLibrary.X31NumberOfString), T.Item, numberString);
                    case ("format-number", 2) when NativeString(built[1]) is QilNode picture:
                        return Call(nameof(XsltLibrary.X31FormatNumber), T.StringX, Items(built[0]), picture, StaticId());
                }
                return TypedCall(name.LocalName, built) ?? CallLibrary(call, name, built.Select(Items).ToList());
            }
            return CallLibrary(call, name, args.Select(a => Items(Build(a))).ToList());
        }

        private bool UntypedNumericNodes(QilNode nodes) => _sc.Schemas == null && nodes.XmlType!.IsSubtypeOf(T.NodeNotRtfS)
            && (nodes.XmlType.NodeKinds & (XmlNodeKindFlags.Comment | XmlNodeKindFlags.PI | XmlNodeKindFlags.Namespace)) == 0;

        private QilNode ForwardStringJoin(QilNode values, QilNode separator) =>
            Call(nameof(XsltLibrary.X31StringJoin), T.StringX,
                AtomicItems(values) ? Items(values) : AtomizeEach(values), separator);

        private QilNode UntypedDoubles(QilNode nodes)
        {
            QilIterator item = _f.For(nodes);
            return _f.Loop(item, Call(nameof(XsltLibrary.X31UntypedDouble), T.DoubleX, _f.XPathNodeValue(ValueReplayOperand(item, nodes))));
        }

        /// <summary>
        /// Xsl3 (P4): the native string an argument converts to (XPath 3.1 §3.1.5.2) when its static type makes that
        /// certain: a native string, or exactly one node (whose typed value, with no schema, is its string value as
        /// xs:untypedAtomic, cast to xs:string). Null for anything else, which takes the general path.
        /// </summary>
        private QilNode? NativeString(QilNode n)
        {
            if (n.XmlType!.TypeCode == System.Xml.Schema.XmlTypeCode.String && n.XmlType.IsSingleton)
            {
                return n;
            }
            if (n.XmlType.IsSubtypeOf(T.NodeNotRtf) && _sc.Schemas == null)
            {
                return _f.XPathNodeValue(ValueReplayOperand(n, n)); // (with schemas, P6, a node may be typed: it is atomized instead)
            }
            if (s_literalStrings.TryGetValue(n, out string? literal))
            {
                return _f.String(literal);
            }
            if (n is QilIterator { NodeType: QilNodeType.For } item && StringItems(item.Binding!))
            {
                // An item of a sequence of strings (the context item in a for-each over tokenize(), say).
                return Call(nameof(XsltLibrary.X31ItemString), T.StringX, n);
            }
            return null;
        }

        /// <summary>
        /// Xsl3 (P4): the native string an xs:string? argument converts to: as NativeString, or for zero or one node,
        /// its string value or "" (the functions taking xs:string? treat the empty sequence as "").
        /// </summary>
        private QilNode? OptionalString(QilNode n)
        {
            if (NativeString(n) is QilNode s)
            {
                return s;
            }
            if (_sc.Schemas != null)
            {
                return null; // Xsl3 (P6): a node may be typed, and is then atomized to its typed value
            }
            if (n.XmlType!.IsSubtypeOf(T.NodeNotRtfQ))
            {
                QilIterator i = _f.For(n);
                return _f.StrConcat(_f.Loop(i, _f.XPathNodeValue(i)));
            }
            if (n.XmlType.IsSubtypeOf(T.NodeNotRtfS))
            {
                // Nodes not known to be at most one (a child step): checked when run, XPTY0004 for more.
                return Call(nameof(XsltLibrary.X31OptionalNodeString), T.StringX, n);
            }
            return null;
        }

        // Elements and attributes, whose simple content (XSLT 3.0 §5.7.2) is their string values joined with spaces.
        private static readonly XmlQueryType s_elementsOrAttributes = XmlQueryTypeFactory.Choice(T.ElementS, T.AttributeS);

        // Xsl3 (P4): calls of library functions declared to return strings (xs:string*, xs:string?, ...), so that the
        // items of what they return are known to be strings. Shared by the compilers of a stylesheet's expressions.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<QilNode, object> s_stringItems = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<QilNode, object> s_atomicItems = new();

        private static bool AtomicItems(QilNode node) => node.XmlType!.IsAtomicValue || node.NodeType switch
        {
            QilNodeType.Loop => AtomicItems(node[1]),
            QilNodeType.Sequence => Enumerable.Range(0, node.Count).All(i => AtomicItems(node[i])),
            _ => s_atomicItems.TryGetValue(node, out _),
        };

        /// <summary>Whether every item of a sequence is certainly an xs:string (or a string subtype).</summary>
        private static bool StringItems(QilNode n) => n.NodeType switch
        {
            QilNodeType.Loop => StringItems(n[1]),
            QilNodeType.Sequence => n.Count > 0 && Enumerable.Range(0, n.Count).All(i => StringItems(n[i])),
            _ => s_stringItems.TryGetValue(n, out _),
        };

        /// <summary>
        /// Xsl3 (P4): a call of a string function as a direct call on native values, when every argument converts to a
        /// native string for certain and the comparisons use the codepoint collation; null otherwise. The functions
        /// are those of the library (the same helpers), so the results are the same.
        /// </summary>
        private QilNode? TypedCall(string local, List<QilNode> a)
        {
            // Which arguments are xs:string? (or xs:anyAtomicType?, as concat's), where zero or one node will do.
            bool Optional(int i) => (local, i) switch
            {
                ("translate", > 0) or ("tokenize", > 0) => false,
                _ => true,
            };
            var s = new QilNode?[a.Count];
            for (int i = 0; i < a.Count; i++)
            {
                if ((s[i] = Optional(i) ? OptionalString(a[i]) : NativeString(a[i])) == null)
                {
                    return null;
                }
            }
            bool codepoint = _sc.DefaultCollation == Functions.Collation.CodepointUri;
            switch (local, a.Count)
            {
                case ("string", 1):
                    return s[0];
                case ("upper-case", 1):
                    return Call(nameof(XsltLibrary.X31UpperCase), T.StringX, s[0]!);
                case ("lower-case", 1):
                    return Call(nameof(XsltLibrary.X31LowerCase), T.StringX, s[0]!);
                case ("normalize-space", 1):
                    return Call(nameof(XsltLibrary.X31NormalizeSpace), T.StringX, s[0]!);
                case ("string-length", 1):
                    // A native double is an xs:integer to Items (as positions are).
                    return Call(nameof(XsltLibrary.X31StringLength), T.DoubleX, s[0]!);
                case ("translate", 3):
                    return Call(nameof(XsltLibrary.X31Translate), T.StringX, s[0]!, s[1]!, s[2]!);
                case ("concat", >= 2):
                    return _f.StrConcat(s!);
                case ("contains", 2) when codepoint:
                    return Call(nameof(XsltLibrary.X31Contains), T.BooleanX, s[0]!, s[1]!);
                case ("starts-with", 2) when codepoint:
                    return Call(nameof(XsltLibrary.X31StartsWith), T.BooleanX, s[0]!, s[1]!);
                case ("ends-with", 2) when codepoint:
                    return Call(nameof(XsltLibrary.X31EndsWith), T.BooleanX, s[0]!, s[1]!);
                case ("substring-before", 2) when codepoint:
                    return Call(nameof(XsltLibrary.X31SubstringBefore), T.StringX, s[0]!, s[1]!);
                case ("substring-after", 2) when codepoint:
                    return Call(nameof(XsltLibrary.X31SubstringAfter), T.StringX, s[0]!, s[1]!);
                case ("tokenize", 2) when s[1] is QilLiteral { NodeType: QilNodeType.LiteralString } pattern && Functions.FunctionLibrary.IsLiteralPattern((string)pattern, ""):
                {
                    QilNode tokens = Call(nameof(XsltLibrary.X31TokenizeLiteral), T.ItemS, s[0]!, s[1]!);
                    s_stringItems.AddOrUpdate(tokens, AtomicTypes.String);
                    return tokens;
                }
            }
            return null;
        }

        private QilNode CallLibrary(Expr call, QName name, List<QilNode> args)
        {
            FunctionDefinition? def = FunctionLibrary.Find(name.NamespaceUri, name.LocalName, args.Count);
            if (def == null)
            {
                throw new XPathStaticException("XPST0017", $"Unknown function {name.EQName}#{args.Count}", call.Span.Start);
            }
            if (_sc.ForwardOnly)
            {
                for (int i = 0; i < args.Count; i++)
                    if (def.ParameterType(i).Item is AtomicTypeTest or NumericTest or UnionTypeTest && !AtomicItems(args[i]))
                    {
                        args[i] = AtomizeEach(args[i]);
                    }
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
            QilNode result = Call(method, T.ItemS, args.Prepend(StaticId()).Prepend(_f.Double(def.Id)).ToArray());
            if (_sc.ForwardOnly && def.ReturnType.Item is AtomicTypeTest atomicResult)
                s_atomicItems.AddOrUpdate(result, atomicResult);
            if (def.ReturnType.Item is AtomicTypeTest { } atomic && atomic.Type.DerivesFrom(AtomicTypes.String))
            {
                s_stringItems.AddOrUpdate(result, atomic);
            }
            return result;
        }
    }

    /// <summary>A compiled XPath 3.1 expression. Immutable and thread-safe; evaluate it as often as needed.</summary>
    internal sealed class XPath31Executable
    {
        public const string ControlNamespace = "urn:xsl3:xpath31";
        public const string ContextItemParam = "context-item";
        public const string ImplicitTimezoneParam = "implicit-timezone";
        public const string ReaderSettingsParam = "reader-settings";
        public const string CurrentDateTimeParam = "current-dateTime";

        private readonly XmlILCommand _command;

        internal XPath31Executable(XmlILCommand command)
        {
            _command = command;
        }

        public IList<XPathItem> Evaluate(XPathItem? contextItem = null, IReadOnlyDictionary<QName, IList<XPathItem>>? variables = null, int? implicitTimezoneMinutes = null, XmlResolver? resolver = null,
            XsDateTime? currentDateTime = null, XmlQueryRuntime? resourceOwner = null)
        {
            var args = new XsltArgumentList();
            if (currentDateTime is XsDateTime now)
            {
                // The host's execution scope (xsl:evaluate): fn:current-dateTime is the host's.
                args.AddParam(CurrentDateTimeParam, ControlNamespace, now);
            }
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
            // Hosted evaluation receives its focus through the explicit context-item parameter.
            // Treating that focus as another principal input could replace the host's URI mapping.
            return _command.Evaluate(resourceOwner == null ? (contextItem as XPathNavigator)! : XslCompiledTransform.AbsentInput,
                resolver, args, resourceOwner);
        }
    }
}
