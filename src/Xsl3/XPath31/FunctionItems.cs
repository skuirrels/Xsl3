// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Xdm;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31
{
    /// <summary>
    /// The lifted functions of one compiled unit (a query, a stylesheet) and the dispatcher that dynamic calls go
    /// through (docs/plan/p3-xslt30.md, decisions 3 and 4). The dispatcher is completed by <see cref="Finish"/>,
    /// once every function item expression has been compiled.
    /// </summary>
    internal sealed class FunctionItemRegistry
    {
        private static int s_nextId;

        private readonly QilPatternFactory _f;
        private readonly System.Action<QilFunction> _addFunction;
        private readonly List<(int Id, QilFunction Function, int Captured, int Arity)> _lifted = new();
        private QilFunction? _dispatcher;
        private readonly Dictionary<string, QilFunction> _helpers = new();

        public FunctionItemRegistry(QilPatternFactory f, System.Action<QilFunction> addFunction)
        {
            _f = f;
            _addFunction = addFunction;
        }

        /// <summary>dispatch($call as item()) as item()*: invokes the lifted function a PendingCall goes to.</summary>
        public QilFunction Dispatcher
        {
            get
            {
                if (_dispatcher == null)
                {
                    _dispatcher = _f.Function(_f.FormalParameterList(_f.Parameter(T.Item)), _f.True(), T.ItemS);
                    _dispatcher.DebugName = "<xsl3:dispatch>";
                    _addFunction(_dispatcher);
                }
                return _dispatcher;
            }
        }

        public int Lift(QilFunction function, int captured, int arity)
        {
            int id = System.Threading.Interlocked.Increment(ref s_nextId);
            _lifted.Add((id, function, captured, arity));
            _addFunction(function);
            // A function item may be called from outside the unit (from the run-time library, or from another unit:
            // a target expression of xsl:evaluate), which needs the dispatcher even if the unit makes no dynamic call.
            _ = Dispatcher;
            return id;
        }

        /// <summary>A helper function of this unit (the folds), made once.</summary>
        public QilFunction Helper(string name, System.Func<QilFunction> make)
        {
            if (!_helpers.TryGetValue(name, out QilFunction? helper))
            {
                _helpers[name] = helper = make();
                helper.DebugName = $"<xsl3:{name}>";
                _addFunction(helper);
            }
            return helper;
        }

        /// <summary>The dispatcher, if any function item was compiled (for QilExpression.Dispatcher).</summary>
        public QilFunction? DispatcherIfUsed => _dispatcher;

        public void Finish()
        {
            if (_dispatcher == null)
            {
                return;
            }
            var call = (QilParameter)_dispatcher.Arguments[0];
            QilIterator target = _f.Let(XPath31Compiler.Invoke(_f, nameof(XsltLibrary.X31CallTarget), T.DoubleX, call));
            QilNode body = XPath31Compiler.Invoke(_f, nameof(XsltLibrary.X31CallRuntime), T.ItemS, call);
            for (int i = _lifted.Count - 1; i >= 0; i--)
            {
                var (id, function, captured, arity) = _lifted[i];
                QilList args = _f.ActualParameterList();
                for (int c = 0; c < captured; c++)
                {
                    args.Add(XPath31Compiler.Invoke(_f, nameof(XsltLibrary.X31CallCaptured), T.ItemS, call, _f.Double(c)));
                }
                for (int a = 0; a < arity; a++)
                {
                    args.Add(XPath31Compiler.Invoke(_f, nameof(XsltLibrary.X31CallArg), T.ItemS, call, _f.Double(a)));
                }
                body = _f.Conditional(_f.Eq(target, _f.Double(id)), _f.Invoke(function, args), body);
            }
            _dispatcher.Definition = _f.Loop(target, body);
        }
    }

    internal sealed partial class XPath31Compiler
    {
        // While compiling the body of a lifted function: the focus is absent there (XPDY0002).
        private bool _focusAbsent;

        private FunctionItemRegistry Registry =>
            _ownRegistry ?? _env?.FunctionItems ?? throw new System.InvalidOperationException("No function item registry");

        private FunctionItemRegistry? _ownRegistry;

        // ---------------------------------------------------------------- lifting

        /// <summary>
        /// A function item for a lifted function: <paramref name="body"/> is compiled as a QIL function of the
        /// captured values and then the parameters; the result builds the item from the captured values, which
        /// are evaluated here.
        /// </summary>
        private QilNode LiftFunction(IReadOnlyList<(QName Name, QilNode Value)> captured, IReadOnlyList<(QName Name, SequenceTypeTest Type)> parameters,
            SequenceTypeTest? returnType, QName? name, System.Func<QilNode> body, QilNode? focus = null)
        {
            QilList formal = _f.FormalParameterList();
            var scope = new List<(QName, QilNode)>();
            QilParameter? focusParam = null;
            if (focus != null)
            {
                focusParam = _f.Parameter(T.ItemS);
                formal.Add(focusParam);
            }
            foreach (var (n, _) in captured)
            {
                QilParameter p = _f.Parameter(T.ItemS);
                formal.Add(p);
                scope.Add((n, p));
            }
            var lets = new List<QilIterator>();
            foreach (var (n, type) in parameters)
            {
                QilParameter p = _f.Parameter(T.ItemS);
                formal.Add(p);
                if (type.Item is AnyItemTest && type.Cardinality == Cardinality.ZeroOrMore)
                {
                    scope.Add((n, p));
                }
                else
                {
                    // The function conversion rules to the declared type (XPTY0004) happen when the call is made;
                    // they are repeated here for calls that come through the dispatcher.
                    QilIterator let = _f.Let(Call(nameof(XsltLibrary.X31Coerce), T.ItemS, p, _f.Double(ConstantPool.Add(type)), _f.String("XPTY0004")));
                    lets.Add(let);
                    scope.Add((n, let));
                }
            }
            QilFunction function = _f.Function(formal, _f.True(), T.ItemS);

            List<(QName, QilNode)> savedScope = new(_scope);
            Focus? savedFocus = _focus;
            bool savedAbsent = _focusAbsent;
            _scope.Clear();
            _scope.AddRange(scope);
            _focus = focusParam != null ? new Focus(Call(nameof(XsltLibrary.X31SingleItemOf), T.Item, focusParam), () => _f.Double(1), () => _f.Double(1)) : null;
            _focusAbsent = focusParam == null;
            QilNode result;
            try
            {
                result = Items(body());
                if (returnType != null)
                {
                    result = Call(nameof(XsltLibrary.X31Coerce), T.ItemS, result, _f.Double(ConstantPool.Add(returnType)), _f.String("XPTY0004"));
                }
            }
            finally
            {
                _scope.Clear();
                _scope.AddRange(savedScope);
                _focus = savedFocus;
                _focusAbsent = savedAbsent;
            }
            result = _f.TypeAssert(result, T.ItemS);
            for (int i = lets.Count - 1; i >= 0; i--)
            {
                result = _f.Loop(lets[i], result);
            }
            function.Definition = result;
            function.DebugName = name is QName q ? $"<{q.EQName}#{parameters.Count}>" : "<inline function>";

            int id = Registry.Lift(function, captured.Count + (focus != null ? 1 : 0), parameters.Count);
            var signature = new FunctionSignature(name, parameters.Select(p => p.Type).ToArray(), returnType ?? SequenceTypeTest.AnySequence);
            QilNode item = Call(nameof(XsltLibrary.X31FunctionNew), T.Item, _f.Double(id), _f.Double(ConstantPool.Add(signature)));
            if (focus != null)
            {
                item = Call(nameof(XsltLibrary.X31FunctionCapture), T.Item, item, focus);
            }
            foreach (var (_, value) in captured)
            {
                item = Call(nameof(XsltLibrary.X31FunctionCapture), T.Item, item, value);
            }
            return item;
        }

        // function($a as T, …) as R { E } (XPath 3.1 §3.1.7)
        private QilNode InlineFunction(InlineFunctionExpr fn)
        {
            var parameters = fn.Params.Select(p => (_types.Expand(p.Name, ""), p.Type == null ? SequenceTypeTest.AnySequence : _types.Resolve(p.Type))).ToList();
            if (parameters.Select(p => p.Item1).Distinct().Count() != parameters.Count)
            {
                throw new XPathStaticException("XQST0039", "Two parameters of the inline function have the same name", fn.Span.Start);
            }
            var captured = new List<(QName, QilNode)>();
            foreach (QName free in FreeVariables(fn))
            {
                captured.Add((free, Items(Variable(new VarRef(new LexicalQName(null, free.LocalName, free.NamespaceUri)) { Span = fn.Span }))));
            }
            return LiftFunction(captured, parameters, fn.ReturnType == null ? null : _types.Resolve(fn.ReturnType), null, () => Build(fn.Body));
        }

        private static readonly LexicalQName[] s_params = Enumerable.Range(1, 64).Select(i => new LexicalQName(null, "§p" + i, "")).ToArray();

        // f#n (XPath 3.1 §3.1.6): a function that calls f with its arguments. Focus-dependent functions capture
        // the focus.
        private QilNode NamedFunctionReference(NamedFunctionRef r)
        {
            int arity = (int)r.Arity;
            QName name = _types.Expand(r.Name, _sc.DefaultFunctionNamespace);
            var parameters = new List<(QName, SequenceTypeTest)>();
            Functions.FunctionDefinition? def = name.NamespaceUri is QName.Ns.Fn or QName.Ns.Math or QName.Ns.Map or QName.Ns.Array
                ? Functions.FunctionLibrary.Find(name.NamespaceUri, name.LocalName, arity) : null;
            // A stylesheet function's declared types (so local:f#2 instance of function(xs:long) works).
            var host = def == null ? _env?.FunctionSignature(name, arity) : null;
            for (int i = 0; i < arity; i++)
            {
                parameters.Add((new QName("", s_params[i].LocalName), def?.ParameterType(i) ?? host?.Parameters[i] ?? SequenceTypeTest.AnySequence));
            }
            var call = new XPath31.FunctionCall(r.Name, Enumerable.Range(0, arity).Select(i => (Expr)new VarRef(s_params[i])).ToList()) { Span = r.Span };
            bool focusDependent = name.NamespaceUri == QName.Ns.Fn && (arity == 0 && (s_contextFunctions.Contains(name.LocalName) || name.LocalName is "position" or "last")
                || (name.LocalName, arity) is ("lang" or "id" or "idref" or "element-with-id" or "unparsed-entity-uri" or "unparsed-entity-public-id", 1) or ("key", 2));
            return LiftFunction(System.Array.Empty<(QName, QilNode)>(), parameters, def?.ReturnType ?? host?.Result, name, () => Build(call),
                focus: focusDependent && !_focusAbsent ? Items(CapturedFocus()) : null);
        }

        /// <summary>
        /// The context item for a focus-dependent function item to keep, or the empty sequence when there is none: the
        /// reference itself is fine without a focus, and calling it raises XPDY0002 (X31SingleItemOf).
        /// </summary>
        private QilNode CapturedFocus()
        {
            if (_focus != null)
            {
                return _focus.Item;
            }
            return _env != null ? _env.ContextItem() ?? _f.Sequence() : Call(nameof(XsltLibrary.X31ContextItemOrEmpty), T.ItemS);
        }

        // f(a, ?, c) and $f(a, ?, c) (XPath 3.1 §3.1.5.4): a function of the placeholders; the other arguments are
        // evaluated now.
        private QilNode PartialApplication(Expr call, Expr? function, LexicalQName? name, IReadOnlyList<Expr> args)
        {
            var captured = new List<(QName, QilNode)>();
            var parameters = new List<(QName, SequenceTypeTest)>();
            var callArgs = new List<Expr>();
            Functions.FunctionDefinition? def = null;
            if (name != null)
            {
                QName q = _types.Expand(name, _sc.DefaultFunctionNamespace);
                def = Functions.FunctionLibrary.Find(q.NamespaceUri, q.LocalName, args.Count);
            }
            int p = 0, c = 0;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] is ArgumentPlaceholder)
                {
                    LexicalQName pn = s_params[p++];
                    parameters.Add((new QName("", pn.LocalName), def?.ParameterType(i) ?? SequenceTypeTest.AnySequence));
                    callArgs.Add(new VarRef(pn));
                }
                else
                {
                    var cn = new LexicalQName(null, "§c" + (++c), "");
                    captured.Add((new QName("", cn.LocalName), Items(Build(args[i]))));
                    callArgs.Add(new VarRef(cn));
                }
            }
            Expr body;
            if (function != null)
            {
                var fn = new LexicalQName(null, "§f", "");
                captured.Insert(0, (new QName("", fn.LocalName), Items(Build(function))));
                body = new DynamicFunctionCall(new VarRef(fn), callArgs) { Span = call.Span };
            }
            else
            {
                body = new XPath31.FunctionCall(name!, callArgs) { Span = call.Span };
            }
            return LiftFunction(captured, parameters, def?.ReturnType, null, () => Build(body));
        }

        // ---------------------------------------------------------------- dynamic calls

        // E(args) (XPath 3.1 §3.1.5.1): the call goes through the dispatcher.
        private QilNode DynamicCall(QilNode function, IReadOnlyList<QilNode> args)
        {
            QilNode call = Call(nameof(XsltLibrary.X31CallNew), T.Item, function, _f.Double(args.Count));
            foreach (QilNode arg in args)
            {
                call = Call(nameof(XsltLibrary.X31CallArgument), T.Item, call, arg);
            }
            return _f.Invoke(Registry.Dispatcher, _f.ActualParameterList(call));
        }

        private QilNode DynamicFunctionCall(DynamicFunctionCall d)
        {
            if (d.Arguments.Any(a => a is ArgumentPlaceholder))
            {
                return PartialApplication(d, d.Function, null, d.Arguments);
            }
            if (d.Arguments.Count == 1)
            {
                // Xsl3 (P4): $map(key), the usual one-argument call, looks the key up directly; anything else that
                // is called with one argument goes through the dispatcher as before.
                QilIterator fn = _f.Let(Items(Build(d.Function)));
                QilIterator arg = _f.Let(Items(Build(d.Arguments[0])));
                return _f.Loop(fn, _f.Loop(arg, _f.Conditional(
                    Call(nameof(XsltLibrary.X31IsMap), T.BooleanX, fn),
                    Call(nameof(XsltLibrary.X31MapLookup), T.ItemS, fn, arg),
                    DynamicCall(fn, new QilNode[] { arg }))));
            }
            return DynamicCall(Items(Build(d.Function)), d.Arguments.Select(a => Items(Build(a))).ToList());
        }

        // ---------------------------------------------------------------- higher-order functions

        /// <summary>The higher-order functions of F&amp;O 3.1, compiled so their calls go through the dispatcher; null for others.</summary>
        /// <summary>The higher-order functions compiled here rather than in the function library (for function-available).</summary>
        public static readonly HashSet<(string Ns, string Local, int Arity)> CompiledHigherOrderFunctions = new()
        {
            (QName.Ns.Fn, "for-each", 2), (QName.Ns.Fn, "filter", 2), (QName.Ns.Fn, "fold-left", 3), (QName.Ns.Fn, "fold-right", 3),
            (QName.Ns.Fn, "for-each-pair", 3), (QName.Ns.Fn, "sort", 3), (QName.Ns.Fn, "apply", 2), (QName.Ns.Fn, "function-name", 1),
            (QName.Ns.Fn, "function-arity", 1), (QName.Ns.Fn, "function-lookup", 2), (QName.Ns.Map, "for-each", 2),
            (QName.Ns.Array, "for-each", 2), (QName.Ns.Array, "filter", 2), (QName.Ns.Array, "fold-left", 3), (QName.Ns.Array, "fold-right", 3),
            (QName.Ns.Array, "for-each-pair", 3), (QName.Ns.Array, "sort", 3),
        };

        private QilNode? HigherOrderFunction(QName name, IReadOnlyList<Expr> args)
        {
            string key = name.NamespaceUri switch
            {
                QName.Ns.Fn => "fn:",
                QName.Ns.Map => "map:",
                QName.Ns.Array => "array:",
                _ => "",
            } + name.LocalName + "#" + args.Count;
            switch (key)
            {
                case "fn:for-each#2":
                {
                    QilIterator f = _f.Let(Items(Build(args[1])));
                    QilIterator x = _f.For(Items(Build(args[0])));
                    return _f.Loop(f, _f.Loop(x, DynamicCall(f, new[] { (QilNode)x })));
                }
                case "fn:filter#2":
                {
                    QilIterator f = _f.Let(Items(Build(args[1])));
                    QilIterator x = _f.For(Items(Build(args[0])));
                    return _f.Loop(f, _f.Filter(x, Call(nameof(XsltLibrary.X31BooleanResult), T.BooleanX, DynamicCall(f, new[] { (QilNode)x }))));
                }
                case "fn:fold-left#3":
                    return Fold(left: true, wrapped: false, Items(Build(args[0])), Items(Build(args[1])), Items(Build(args[2])));
                case "fn:fold-right#3":
                    return Fold(left: false, wrapped: false, Items(Build(args[0])), Items(Build(args[1])), Items(Build(args[2])));
                case "fn:for-each-pair#3":
                    return ForEachPair(Items(Build(args[0])), Items(Build(args[1])), Items(Build(args[2])), wrapped: false);
                case "fn:sort#3":
                {
                    QilIterator items = _f.Let(Items(Build(args[0])));
                    QilIterator f = _f.Let(Items(Build(args[2])));
                    QilIterator x = _f.For(items);
                    QilNode keys = _f.Loop(x, Call(nameof(XsltLibrary.X31Wrap), T.Item, DynamicCall(f, new[] { (QilNode)x })));
                    return _f.Loop(items, _f.Loop(f, Call(nameof(XsltLibrary.X31SortByKeys), T.ItemS, items, keys, Items(Build(args[1])), _f.Double(_staticId))));
                }
                case "fn:apply#2":
                    return _f.Invoke(Registry.Dispatcher, _f.ActualParameterList(Call(nameof(XsltLibrary.X31ApplyCall), T.Item, Items(Build(args[0])), Items(Build(args[1])))));
                case "fn:function-name#1":
                    return Call(nameof(XsltLibrary.X31FunctionName), T.ItemS, Function(Items(Build(args[0]))));
                case "fn:function-arity#1":
                    return Call(nameof(XsltLibrary.X31FunctionArity), T.Item, Function(Items(Build(args[0]))));
                case "fn:function-lookup#2":
                    if (args[0] is XPath31.FunctionCall { Name.LocalName: "QName", Arguments: [StringLiteral ns, StringLiteral lexical] } && args[1] is IntegerLiteral arity)
                    {
                        // A literal name: the function is looked up as a named function reference would be,
                        // which finds the host's functions too; no such function gives the empty sequence.
                        string local = lexical.Value.Contains(':') ? lexical.Value[(lexical.Value.IndexOf(':') + 1)..] : lexical.Value;
                        if (ns.Value == "http://www.w3.org/1999/XSL/Transform" && local == "original")
                        {
                            return _f.Sequence(); // xsl:original isn't available to function-lookup (XSLT 3.0 §3.5.3.3)
                        }
                        try
                        {
                            return NamedFunctionReference(new NamedFunctionRef(new LexicalQName(null, local, ns.Value), arity.Value) { Span = args[0].Span });
                        }
                        catch (XPathStaticException e) when (e.Code == "XPST0017")
                        {
                            return _f.Sequence();
                        }
                    }
                    return Call(nameof(XsltLibrary.X31FunctionLookup), T.ItemS, Items(Build(args[0])), Items(Build(args[1])), _f.Double(_staticId));
                case "map:for-each#2":
                {
                    QilIterator m = _f.Let(Items(Build(args[0])));
                    QilIterator f = _f.Let(Items(Build(args[1])));
                    QilIterator k = _f.For(Call(nameof(XsltLibrary.X31MapKeys), T.ItemS, m));
                    return _f.Loop(m, _f.Loop(f, _f.Loop(k, DynamicCall(f, new[] { (QilNode)k, Call(nameof(XsltLibrary.X31MapGet), T.ItemS, m, k) }))));
                }
                case "array:for-each#2":
                {
                    QilIterator f = _f.Let(Items(Build(args[1])));
                    QilIterator m = _f.For(Call(nameof(XsltLibrary.X31ArrayMembersWrapped), T.ItemS, Items(Build(args[0]))));
                    QilNode results = _f.Loop(m, Call(nameof(XsltLibrary.X31Wrap), T.Item, DynamicCall(f, new[] { Unwrap(m) })));
                    return _f.Loop(f, Call(nameof(XsltLibrary.X31ArrayOfWrapped), T.Item, results));
                }
                case "array:filter#2":
                {
                    QilIterator f = _f.Let(Items(Build(args[1])));
                    QilIterator m = _f.For(Call(nameof(XsltLibrary.X31ArrayMembersWrapped), T.ItemS, Items(Build(args[0]))));
                    QilNode kept = _f.Filter(m, Call(nameof(XsltLibrary.X31BooleanResult), T.BooleanX, DynamicCall(f, new[] { Unwrap(m) })));
                    return _f.Loop(f, Call(nameof(XsltLibrary.X31ArrayOfWrapped), T.Item, kept));
                }
                case "array:fold-left#3":
                    return Fold(left: true, wrapped: true, Call(nameof(XsltLibrary.X31ArrayMembersWrapped), T.ItemS, Items(Build(args[0]))), Items(Build(args[1])), Items(Build(args[2])));
                case "array:fold-right#3":
                    return Fold(left: false, wrapped: true, Call(nameof(XsltLibrary.X31ArrayMembersWrapped), T.ItemS, Items(Build(args[0]))), Items(Build(args[1])), Items(Build(args[2])));
                case "array:for-each-pair#3":
                    return Call(nameof(XsltLibrary.X31ArrayOfWrapped), T.Item, ForEachPair(
                        Call(nameof(XsltLibrary.X31ArrayMembersWrapped), T.ItemS, Items(Build(args[0]))),
                        Call(nameof(XsltLibrary.X31ArrayMembersWrapped), T.ItemS, Items(Build(args[1]))), Items(Build(args[2])), wrapped: true));
                case "array:sort#3":
                {
                    QilIterator members = _f.Let(Call(nameof(XsltLibrary.X31ArrayMembersWrapped), T.ItemS, Items(Build(args[0]))));
                    QilIterator f = _f.Let(Items(Build(args[2])));
                    QilIterator x = _f.For(members);
                    QilNode keys = _f.Loop(x, Call(nameof(XsltLibrary.X31Wrap), T.Item, DynamicCall(f, new[] { Unwrap(x) })));
                    return _f.Loop(members, _f.Loop(f, Call(nameof(XsltLibrary.X31ArrayOfWrapped), T.Item,
                        Call(nameof(XsltLibrary.X31SortByKeys), T.ItemS, members, keys, Items(Build(args[1])), _f.Double(_staticId)))));
                }
            }
            return null;
        }

        private QilNode Unwrap(QilNode wrapped) => Call(nameof(XsltLibrary.X31Unwrap), T.ItemS, wrapped);

        // A single function item argument (XPTY0004 otherwise).
        private QilNode Function(QilNode value) => Call(nameof(XsltLibrary.X31Coerce), T.ItemS, value,
            _f.Double(ConstantPool.Add(new SequenceTypeTest(FunctionItemTest.Instance, Cardinality.ExactlyOne))), _f.String("XPTY0004"));

        // fold-left/fold-right: a loop in the run-time library, which calls the function through the dispatcher
        // (the dispatcher is created here, so that the run-time library has it).
        private QilNode Fold(bool left, bool wrapped, QilNode items, QilNode zero, QilNode function)
        {
            _ = Registry.Dispatcher;
            return Call(nameof(XsltLibrary.X31Fold), T.ItemS, items, zero, function, left ? _f.True() : _f.False(), wrapped ? _f.True() : _f.False());
        }

        private QilNode FoldRecursive(bool left, bool wrapped, QilNode items, QilNode zero, QilNode function)
        {
            QilFunction helper = Registry.Helper((left ? "fold-left" : "fold-right") + (wrapped ? "-array" : ""), () =>
            {
                QilParameter f = _f.Parameter(T.ItemS), seq = _f.Parameter(T.ItemS), i = _f.Parameter(T.DoubleX), acc = _f.Parameter(T.ItemS);
                QilFunction fold = _f.Function(_f.FormalParameterList(f, seq, i, acc), _f.True(), T.ItemS);
                QilNode item = Call(nameof(XsltLibrary.X31ItemAt), T.ItemS, seq, i);
                if (wrapped)
                {
                    item = Call(nameof(XsltLibrary.X31Unwrap), T.ItemS, Call(nameof(XsltLibrary.X31SingleItemOf), T.Item, item));
                }
                QilNode next = left ? DynamicCall(f, new[] { (QilNode)acc, item }) : DynamicCall(f, new[] { item, (QilNode)acc });
                QilNode done = left ? _f.Gt(i, Call(nameof(XsltLibrary.X31Count), T.DoubleX, seq)) : _f.Lt(i, _f.Double(1));
                fold.Definition = _f.Conditional(done, acc,
                    _f.Invoke(fold, _f.ActualParameterList(f, seq, left ? _f.Add(i, _f.Double(1)) : _f.Subtract(i, _f.Double(1)), next)));
                return fold;
            });
            QilIterator s = _f.Let(items);
            QilNode start = left ? _f.Double(1) : Call(nameof(XsltLibrary.X31Count), T.DoubleX, s);
            return _f.Loop(s, _f.Invoke(helper, _f.ActualParameterList(function, s, start, zero)));
        }

        // for-each-pair: f($a[i], $b[i]) for the positions both have.
        private QilNode ForEachPair(QilNode first, QilNode second, QilNode function, bool wrapped)
        {
            QilIterator a = _f.Let(first), b = _f.Let(second), f = _f.Let(function);
            QilIterator x = _f.For(a);
            QilIterator position = _f.Let(_f.XsltConvert(_f.PositionOf(x), T.DoubleX));
            QilNode y = Call(nameof(XsltLibrary.X31ItemAt), T.ItemS, b, position);
            QilNode left = wrapped ? Unwrap(x) : x, right = wrapped ? Unwrap(Call(nameof(XsltLibrary.X31SingleItemOf), T.Item, y)) : y;
            QilNode result = DynamicCall(f, new[] { left, right });
            if (wrapped)
            {
                result = Call(nameof(XsltLibrary.X31Wrap), T.Item, result);
            }
            QilNode body = _f.Loop(position, _f.Conditional(_f.Le(position, Call(nameof(XsltLibrary.X31Count), T.DoubleX, b)), result, _f.Sequence()));
            return _f.Loop(a, _f.Loop(b, _f.Loop(f, _f.Loop(x, body))));
        }

        // ---------------------------------------------------------------- free variables

        /// <summary>The variables an inline function refers to that it doesn't bind, in order of first use.</summary>
        private List<QName> FreeVariables(InlineFunctionExpr fn)
        {
            var result = new List<QName>();
            var bound = new List<QName>(fn.Params.Select(p => _types.Expand(p.Name, "")));
            Walk(fn.Body, bound, result);
            return result;
        }

        private void Walk(Expr? e, List<QName> bound, List<QName> free)
        {
            switch (e)
            {
                case null:
                    return;
                case VarRef v:
                {
                    QName name = _types.Expand(v.Name, "");
                    if (!bound.Contains(name) && !free.Contains(name))
                    {
                        free.Add(name);
                    }
                    return;
                }
                case ForExpr or LetExpr or QuantifiedExpr:
                {
                    var bindings = e switch { ForExpr f => f.Bindings, LetExpr l => l.Bindings, _ => ((QuantifiedExpr)e).Bindings };
                    int mark = bound.Count;
                    foreach (Binding binding in bindings)
                    {
                        Walk(binding.Value, bound, free);
                        bound.Add(_types.Expand(binding.Variable, ""));
                    }
                    Walk(e switch { ForExpr f => f.Return, LetExpr l => l.Return, _ => ((QuantifiedExpr)e).Satisfies }, bound, free);
                    bound.RemoveRange(mark, bound.Count - mark);
                    return;
                }
                case InlineFunctionExpr inner:
                {
                    int mark = bound.Count;
                    bound.AddRange(inner.Params.Select(p => _types.Expand(p.Name, "")));
                    Walk(inner.Body, bound, free);
                    bound.RemoveRange(mark, bound.Count - mark);
                    return;
                }
            }
            foreach (Expr child in AllChildren(e))
            {
                Walk(child, bound, free);
            }
        }

        private static IEnumerable<Expr> AllChildren(Expr e) => e switch
        {
            XPath31.FunctionCall c => c.Arguments,
            DynamicFunctionCall d => d.Arguments.Prepend(d.Function),
            MapConstructor m => m.Entries.SelectMany(x => new[] { x.Key, x.Value }),
            SquareArrayConstructor a => a.Members,
            CurlyArrayConstructor { Content: Expr c } => new[] { c },
            UnaryLookup { Key.Expr: Expr k } => new[] { k },
            PostfixLookup p => p.Key.Expr is Expr k ? new[] { p.Base, k } : new[] { p.Base },
            FilterExpr f => new[] { f.Base, f.Predicate },
            AxisStep s => s.Predicates,
            PathExpr p => new[] { p.Left, p.Right },
            SimpleMapExpr m => new[] { m.Left, m.Right },
            ArrowExpr a => (a.FunctionExpr is Expr fe ? new[] { a.Target, fe } : new[] { a.Target }).Concat(a.Arguments),
            _ => Children(e),
        };
    }
}
