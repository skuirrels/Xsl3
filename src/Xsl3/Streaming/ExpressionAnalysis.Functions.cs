// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

internal sealed partial class ExpressionAnalysis
{
    private readonly Dictionary<(QName Name, int Arity), FunctionItemTest> _signatures = new();
    private readonly Dictionary<(QName Name, int Arity), AnalysisFunction?> _hostFunctions = new();

    private AnalysisFunction? HostFunction(QName name, int arity)
    {
        var key = (name, arity);
        if (_hostFunctions.TryGetValue(key, out AnalysisFunction? cached)) return cached;
        AnalysisFunction? policy = _options.ResolveFunction?.Invoke(name, arity);
        if (policy != null)
        {
            if (policy.Signature.Parameters is not { } parameters || parameters.Count != arity
                || policy.Signature.ReturnType == null || policy.Usages.Count != arity
                || policy.Usages.Any(usage => !Enum.IsDefined(usage)) || !Enum.IsDefined(policy.Category)
                || policy.Category != FunctionStreamabilityCategory.Unclassified && arity == 0)
                throw new InvalidOperationException($"Invalid streamability signature/policy for {name.EQName}#{arity}");
            // The host may reuse mutable collections. Keep one consistent policy per analysis.
            policy = policy with { Signature = new(Array.AsReadOnly(parameters.ToArray()), policy.Signature.ReturnType),
                Usages = Array.AsReadOnly(policy.Usages.ToArray()) };
        }
        _hostFunctions.Add(key, policy);
        return policy;
    }

    private QName ExpandFunction(LexicalQName name) => _options.Types.Expand(name, _options.DefaultFunctionNamespace);

    private static int CheckedArity(BigInteger arity) => arity >= 0 && arity <= int.MaxValue ? (int)arity
        : throw new XPathStaticException("FOAR0002", $"The arity {arity} is too large", -1);

    private bool IsFocusDependent(QName name, int arity) => name.NamespaceUri == QName.Ns.Fn
        ? arity == 0 && (XPath31Compiler.s_contextFunctions.Contains(name.LocalName)
            || name.LocalName is "position" or "last" or "current" or "copy-of" or "snapshot")
            || (name.LocalName, arity) is ("lang" or "id" or "idref" or "element-with-id" or "unparsed-entity-uri" or "unparsed-entity-public-id"
                or "accumulator-before" or "accumulator-after", 1) or ("key", 2)
        : HostFunction(name, arity)?.FocusDependent == true;

    private FunctionItemTest Signature(QName name, int arity)
    {
        var key = (name, arity);
        if (!_signatures.TryGetValue(key, out FunctionItemTest? signature))
        {
            signature = ResolveSignature(name, arity);
            _signatures.Add(key, signature);
        }
        return signature;
    }

    private FunctionItemTest ResolveSignature(QName name, int arity)
    {
        if (name.NamespaceUri == QName.Ns.Fn && arity == 0 && XPath31Compiler.s_contextFunctions.Contains(name.LocalName))
            return new(Array.Empty<SequenceTypeTest>(), Signature(name, 1).ReturnType);
        if (name.NamespaceUri == QName.Ns.Fn && arity == 1 && name.LocalName is "lang" or "id" or "idref" or "element-with-id")
        {
            FunctionItemTest explicitNode = Signature(name, 2);
            return new(explicitNode.Parameters!.Take(1).ToArray(), explicitNode.ReturnType);
        }
        if (FunctionLibrary.Find(name.NamespaceUri, name.LocalName, arity) is { } defined)
            return new(new ParameterTypes(defined, arity), defined.ReturnType);
        if (XPath31Compiler.CompiledFunctionSignature(name, arity, _options.Types) is { } compiled)
            return new(compiled.Parameters, compiled.Result);
        if (HostFunction(name, arity) is { } host) return host.Signature;
        if (name.NamespaceUri == QName.Ns.Array && ArrayIntrinsicSignature(name.LocalName, arity) is { } arraySignature) return arraySignature;
        // XSLT built-ins are compiled by the host, rather than registered in FunctionLibrary.
        // Their signatures below are from the XSLT 3.0 function declarations (§18, §20).
        if (name.NamespaceUri == QName.Ns.Fn && SpecialSignature(name.LocalName, arity) is { } signature) return signature;
        if (arity == 1 && (name.NamespaceUri == QName.Ns.Xs || _options.Types.FindSchemaType(name) != null))
        {
            LexicalQName lexical = new(null, name.LocalName, name.NamespaceUri);
            ItemTest? result = name == QName.Xs("error") ? null : name == QName.Xs("numeric") ? NumericTest.Instance
                : StaticTypeResolver.FindCastTarget(name) is { } target ? new AtomicTypeTest(target.Type)
                : ResolveType(new(new AtomicOrUnionType(lexical), Occurrence.ZeroOrOne)).Item;
            return new(new[] { new SequenceTypeTest(new AtomicTypeTest(AtomicTypes.AnyAtomicType), Cardinality.ZeroOrOne) }, new(result, Cardinality.ZeroOrOne));
        }
        throw new NotSupportedException($"Streamability signature/policy is not available for {name.EQName}#{arity}");
    }

    // Variadic named references can have very large arities. Retain their signature without
    // allocating a parameter array proportional to an arity written in source text.
    private sealed class ParameterTypes(FunctionDefinition definition, int count) : IReadOnlyList<SequenceTypeTest>
    {
        public int Count => count;
        public SequenceTypeTest this[int index] => index >= 0 && index < count ? definition.ParameterType(index) : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<SequenceTypeTest> GetEnumerator()
        {
            for (int i = 0; i < count; i++) yield return definition.ParameterType(i);
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static FunctionItemTest? ArrayIntrinsicSignature(string name, int arity)
    {
        string? signature = (name, arity) switch
        {
            ("fold-left", 3) => "function(array(*), item()*, function(item()*, item()*) as item()*) as item()*",
            ("fold-right", 3) => "function(array(*), item()*, function(item()*, item()*) as item()*) as item()*",
            ("for-each-pair", 3) => "function(array(*), array(*), function(item()*, item()*) as item()*) as array(*)",
            ("sort", 3) => "function(array(*), xs:string?, function(item()*) as xs:anyAtomicType*) as array(*)",
            _ => null,
        };
        return signature == null ? null : (FunctionItemTest)new StaticTypeResolver(ExpressionAnalysisOptions.StandardNamespace)
            .Resolve(Parser.ParseSequenceType(signature)).Item!;
    }

    private static FunctionItemTest? SpecialSignature(string name, int arity)
    {
        // Only functions absent from the canonical registry are described here. Occurrence indicators
        // remain available for the singleton-transmission rule and dynamic function signatures.
        string? text = (name, arity) switch
        {
            ("position" or "last", 0) => "function() as xs:integer",
            ("current", 0) => "function() as item()",
            ("current-group", 0) => "function() as item()*",
            ("current-grouping-key" or "current-merge-key", 0) => "function() as xs:anyAtomicType*",
            ("current-merge-group", 0) => "function() as item()*",
            ("current-merge-group", 1) => "function(xs:string) as item()*",
            ("copy-of" or "snapshot", 0) => "function() as item()",
            ("copy-of" or "snapshot", 1) => "function(item()*) as item()*",
            ("accumulator-before" or "accumulator-after", 1) => "function(xs:string) as item()*",
            ("key", 2) => "function(xs:string, xs:anyAtomicType*) as node()*",
            ("key", 3) => "function(xs:string, xs:anyAtomicType*, node()) as node()*",
            ("document", 1) => "function(item()*) as node()*",
            ("document", 2) => "function(item()*, node()) as node()*",
            ("unparsed-entity-uri" or "unparsed-entity-public-id", 1) => "function(xs:string) as xs:anyURI",
            ("unparsed-entity-uri" or "unparsed-entity-public-id", 2) => "function(xs:string, node()) as xs:anyURI",
            ("current-output-uri", 0) => "function() as xs:anyURI?",
            ("available-system-properties", 0) => "function() as xs:QName*",
            ("system-property", 1) => "function(xs:string) as xs:string",
            ("function-available", 1) => "function(xs:string) as xs:boolean",
            ("function-available", 2) => "function(xs:string, xs:integer) as xs:boolean",
            ("element-available" or "type-available", 1) => "function(xs:string) as xs:boolean",
            ("regex-group", 1) => "function(xs:integer) as xs:string",
            // Signatures of compiler intrinsics not yet in its shared signature table (F&O 3.1).
            ("fold-right", 3) => "function(item()*, item()*, function(item(), item()*) as item()*) as item()*",
            ("function-lookup", 2) => "function(xs:QName, xs:integer) as function(*)?",
            ("function-name", 1) => "function(function(*)) as xs:QName?",
            ("function-arity", 1) => "function(function(*)) as xs:integer",
            ("apply", 2) => "function(function(*), array(*)) as item()*",
            ("sort", 3) => "function(item()*, xs:string?, function(item()) as xs:anyAtomicType*) as item()*",
            _ => null,
        };
        if (text == null) return null;
        var resolver = new StaticTypeResolver(ExpressionAnalysisOptions.StandardNamespace);
        return (FunctionItemTest)resolver.Resolve(Parser.ParseSequenceType(text)).Item!;
    }

    private Streamability Function_19_8_8_14(FunctionCall call, StreamabilityContext context)
    {
        QName name = ExpandFunction(call.Name);
        FunctionItemTest signature = Signature(name, call.Arguments.Count);
        if (call.IsPartialApplication)
        {
            Streamability partial = Partial(call.Arguments, signature, context, null,
                HostFunction(name, call.Arguments.Count)?.DeclaredStreamable == true);
            if (IsFocusDependent(name, call.Arguments.Count) && context.Posture != Posture.Grounded)
                return partial with { Posture = Posture.Roaming, Sweep = Sweep.FreeRanging };
            return partial;
        }
        if (call.Arguments.Count == 1 && (name.NamespaceUri == QName.Ns.Xs || _options.Types.FindSchemaType(name) != null))
            return General_19_8_1(CastType(new(null, name.LocalName, name.NamespaceUri)), "19.8.8.14", new AnalysisOperand(Visit(call.Arguments[0], context), OperandUsage.Absorption));
        if (name.NamespaceUri == QName.Ns.Fn)
        {
            Streamability? special = SpecialFunction_19_8_9(name.LocalName, call.Arguments, context);
            if (special.HasValue) return special.Value;
        }
        if (_hostFunctions.TryGetValue((name, call.Arguments.Count), out AnalysisFunction? resolved)
            && resolved is { Category: not FunctionStreamabilityCategory.Unclassified } stylesheet)
        {
            Streamability[] supplied = call.Arguments.Select(a => Visit(a, context)).ToArray();
            return FunctionStreamability.Call(stylesheet, supplied) with { ItemType = signature.ReturnType?.Item };
        }
        IReadOnlyList<Expr> arguments = call.Arguments;
        // Fill implicit focus operands before consulting the roles table (§19.8.9).
        if (name.NamespaceUri == QName.Ns.Fn)
        {
            if (arguments.Count == 0 && (XPath31Compiler.s_contextFunctions.Contains(name.LocalName) || name.LocalName is "copy-of" or "snapshot"))
                arguments = new Expr[] { new ContextItemExpr() };
            else if (arguments.Count == 1 && name.LocalName is "lang" or "id" or "idref" or "element-with-id")
                arguments = arguments.Append(new ContextItemExpr()).ToArray();
            else if ((arguments.Count == 2 && name.LocalName == "key") || (arguments.Count == 1 && name.LocalName is "unparsed-entity-uri" or "unparsed-entity-public-id"))
                arguments = arguments.Append(new RootExpr()).ToArray();
        }
        IReadOnlyList<OperandUsage> usages = BuiltInOperandUsages.Find(name, arguments.Count)
            ?? HostFunction(name, call.Arguments.Count)?.Usages
            ?? throw new NotSupportedException($"Operand roles are not available for {name.EQName}#{arguments.Count}");
        if (usages.Count != arguments.Count) throw new InvalidOperationException($"Operand-role count does not match {name.EQName}#{arguments.Count}");
        Streamability[] values = arguments.Select(a => Visit(a, context)).ToArray();
        UType type = UTypes.FromItem(signature.ReturnType?.Item);
        bool transmitted = false;
        UType transmittedTypes = UType.Empty;
        for (int i = 0; i < values.Length; i++)
            if (usages[i] == OperandUsage.Transmission) { transmitted = true; transmittedTypes |= values[i].Type; }
        if (transmitted) type &= transmittedTypes;
        Streamability result = General_19_8_1(type, "19.8.9", values.Select((v, i) => new AnalysisOperand(v, usages[i])).ToArray());
        if (name.NamespaceUri == QName.Ns.Fn && name.LocalName is "head" or "exactly-one" or "zero-or-one" && result.Posture == Posture.Crawling)
            result = result with { Posture = Posture.Striding };
        ItemTest? item = signature.ReturnType?.Item;
        if (type == UType.Function && transmitted)
        {
            ItemTest?[] shapes = values.Where((v, i) => usages[i] == OperandUsage.Transmission && v.Type != UType.Empty)
                .Select(v => v.ItemType).ToArray();
            if (shapes.Length > 0 && shapes.All(s => Equals(s, shapes[0]))) item = shapes[0];
        }
        return result with { ItemType = item };
    }

    private Streamability? SpecialFunction_19_8_9(string name, IReadOnlyList<Expr> arguments, StreamabilityContext context)
    {
        switch (name, arguments.Count)
        {
            case ("position", 0): return Grounded(UType.Decimal, "19.8.9.16");
            case ("last", 0): return context.Posture is Posture.Grounded or Posture.Climbing
                ? Grounded(UType.Decimal, "19.8.9.14") : Roaming(UType.Decimal, "19.8.9.14");
            case ("current", 0):
                return new(_outerContext.Type, _options.InPattern ? Posture.Climbing
                    : _outerContext.Posture == Posture.Grounded ? Posture.Grounded
                    : _higherOrderDepth > 0 ? Posture.Climbing : context.Posture, Sweep.Motionless, "19.8.9.3") { ItemType = _outerContext.ItemType };
            case ("current-group", 0):
                if (_options.AllowStreamabilityRewrites && _options.CurrentGroupSelection is { Posture: Posture.Grounded } groundedGroup)
                    return groundedGroup with { Sweep = Sweep.Motionless, Rule = "19.8.9.4" };
                return _higherOrderDepth == 0 && _focusDepth == 0 && _options.CurrentGroupSelection is { } group
                    ? group with { Rule = "19.8.9.4" } : Roaming(UType.Any, "19.8.9.4");
            case ("current-grouping-key", 0): return Grounded(UType.Atomic, "19.8.9.5");
            case ("current-merge-key", 0): return Grounded(UType.Atomic, "19.8.9.7");
            case ("current-merge-group", 0 or 1):
                foreach (Expr argument in arguments) Visit(argument, context);
                return Grounded(UType.Any, "19.8.9.6");
            case ("accumulator-before", 1):
                return Visit(arguments[0], context).Sweep == Sweep.Motionless ? Grounded(UType.Any, "19.8.9.2") : Roaming(UType.Any, "19.8.9.2");
            case ("accumulator-after", 1): return AccumulatorAfter_19_8_9_1(arguments[0], context);
            case ("root", 0 or 1): return Root_19_8_9_18(arguments.Count == 0 ? new ContextItemExpr() : arguments[0], context);
            case ("outermost", 1):
            {
                Streamability value = Visit(arguments[0], context);
                Streamability result = General_19_8_1(value.Type & UType.Nodes, "19.8.9.15", new AnalysisOperand(value, OperandUsage.Transmission));
                return result.Posture == Posture.Crawling ? result with { Posture = Posture.Striding } : result;
            }
            case ("innermost" or "reverse", 1):
            {
                Streamability value = Visit(arguments[0], context);
                return General_19_8_1(value.Type, name == "innermost" ? "19.8.9.13" : "19.8.9.17", new AnalysisOperand(value, OperandUsage.Navigation)) with { ItemType = value.ItemType };
            }
            case ("fold-left" or "fold-right", 3):
            case ("for-each" or "filter", 2):
            case ("for-each-pair", 3):
                return HigherOrderFunction_19_8_9(name, arguments, context);
            case ("function-lookup", 2):
                return General_19_8_1(UType.Function, "19.8.9.12", arguments.Select(a => new AnalysisOperand(Visit(a, context), OperandUsage.Absorption)).ToArray())
                    with { ItemType = FunctionItemTest.Instance };
            default: return null;
        }
    }

    private Streamability AccumulatorAfter_19_8_9_1(Expr argument, StreamabilityContext context)
    {
        const string rule = "19.8.9.1";
        // Unlike the general roaming verdict, §19.8.9.1 explicitly keeps posture grounded.
        Sweep sweep = Visit(argument, context).Sweep != Sweep.Motionless ? Sweep.FreeRanging
            : context.Posture == Posture.Grounded || (context.Type & UType.ParentNodes) == 0 ? Sweep.Motionless
            : _options.AccumulatorPhase == AccumulatorPhase.Start ? Sweep.FreeRanging
            : _options.AccumulatorPhase == AccumulatorPhase.End ? Sweep.Motionless
            : !_options.InSequenceConstructor || _focusDepth != 0 ? Sweep.FreeRanging
            : _options.PrecedingConsumingInstruction ? Sweep.Motionless : Sweep.Consuming;
        return Grounded(UType.Any, rule, sweep);
    }

    private Streamability Root_19_8_9_18(Expr expression, StreamabilityContext context)
    {
        Streamability value = Visit(expression, context);
        if (value.Type == UType.Document && value.Posture == Posture.Striding) return value with { Rule = "19.8.9.18" };
        var ancestors = new AxisStep(Axis.AncestorOrSelf, new KindNodeTest(new AnyKindTest()), Array.Empty<Expr>());
        Streamability right = FocusVisit(ancestors, new(value.Type, value.Posture) { ItemType = value.ItemType });
        Streamability path = new(right.Type, right.Posture, Wider(value.Sweep, right.Sweep), "19.8.8.8");
        Streamability result = General_19_8_1(path.Type, "19.8.9.18", new AnalysisOperand(path, OperandUsage.Transmission));
        return result.Posture == Posture.Crawling ? result with { Posture = Posture.Striding } : result;
    }

    private Streamability HigherOrderFunction_19_8_9(string name, IReadOnlyList<Expr> arguments, StreamabilityContext context)
    {
        Streamability[] values = arguments.Select(a => Visit(a, context)).ToArray();
        FunctionItemTest? callback = CallableSignature(values[^1].ItemType);
        OperandUsage CallbackUsage(int index) => callback?.Parameters is { } parameters && index < parameters.Count
            ? TypeDeterminedUsage(parameters[index].Item) : OperandUsage.Navigation;
        var operands = new List<AnalysisOperand>();
        operands.Add(new(values[0], name is "fold-right" or "filter" ? OperandUsage.Navigation : CallbackUsage(name == "fold-left" ? 1 : 0)));
        if (name is "fold-left" or "fold-right") operands.Add(new(values[1], name == "fold-left" ? OperandUsage.Absorption : OperandUsage.Navigation));
        if (name == "for-each-pair") operands.Add(new(values[1], CallbackUsage(1)));
        operands.Add(new(values[^1], OperandUsage.Inspection));
        UType type = name == "filter" ? values[0].Type : name is "fold-left" or "fold-right" ? UType.Any
            : callback?.ReturnType is { } resultType ? UTypes.FromSequence(resultType) : UType.Any;
        string rule = name switch { "fold-left" => "19.8.9.8", "fold-right" => "19.8.9.9", "for-each" => "19.8.9.10", "for-each-pair" => "19.8.9.11", _ => "19.8.9" };
        return General_19_8_1(type, rule, operands.ToArray())
            with { ItemType = name == "filter" ? values[0].ItemType : name is "fold-left" or "fold-right" ? null : callback?.ReturnType?.Item };
    }

    private static FunctionItemTest? CallableSignature(ItemTest? item) => item switch
    {
        FunctionItemTest function => function,
        MapTest map => new(new[] { new SequenceTypeTest(new AtomicTypeTest(AtomicTypes.AnyAtomicType), Cardinality.ExactlyOne) }, map.ValueType ?? SequenceTypeTest.AnySequence),
        ArrayTest array => new(new[] { new SequenceTypeTest(new AtomicTypeTest(AtomicTypes.Integer), Cardinality.ExactlyOne) }, array.MemberType ?? SequenceTypeTest.AnySequence),
        _ => null,
    };

    private Streamability NamedFunction_19_8_8_15(NamedFunctionRef reference, StreamabilityContext context)
    {
        QName name = ExpandFunction(reference.Name);
        int arity = CheckedArity(reference.Arity);
        FunctionItemTest signature = Signature(name, arity);
        return (IsFocusDependent(name, arity) && context.Posture != Posture.Grounded ? Roaming(UType.Function, "19.8.8.15") : Grounded(UType.Function, "19.8.8.15"))
            with { ItemType = signature };
    }

    private Streamability InlineFunction_19_8_8_16(InlineFunctionExpr expression)
    {
        var bound = expression.Params.Select(p => _options.Types.Expand(p.Name, "")).ToHashSet();
        bool captures = CapturesStreamedVariable(expression.Body, bound);
        SequenceTypeTest[] parameters = expression.Params.Select(p => p.Type == null ? SequenceTypeTest.AnySequence : ResolveType(p.Type)).ToArray();
        SequenceTypeTest result = expression.ReturnType == null ? SequenceTypeTest.AnySequence : ResolveType(expression.ReturnType);
        return (captures ? Roaming(UType.Function, "19.8.8.16") : Grounded(UType.Function, "19.8.8.16"))
            with { ItemType = new FunctionItemTest(parameters, result) };
    }

    private bool CapturesStreamedVariable(Expr expression, HashSet<QName> bound)
    {
        if (expression is VarRef variable)
        {
            QName name = _options.Types.Expand(variable.Name, "");
            return !bound.Contains(name) && _variables.TryGetValue(name, out Streamability value) && value.Posture != Posture.Grounded;
        }
        if (expression is InlineFunctionExpr inline)
            return CapturesStreamedVariable(inline.Body, bound.Concat(inline.Params.Select(p => _options.Types.Expand(p.Name, ""))).ToHashSet());
        IReadOnlyList<Binding>? bindings = expression switch { LetExpr l => l.Bindings, ForExpr f => f.Bindings, QuantifiedExpr q => q.Bindings, _ => null };
        if (bindings != null)
        {
            var local = new HashSet<QName>(bound);
            foreach (Binding binding in bindings)
            {
                if (CapturesStreamedVariable(binding.Value, local)) return true;
                local.Add(_options.Types.Expand(binding.Variable, ""));
            }
            Expr body = expression switch { LetExpr l => l.Return, ForExpr f => f.Return, QuantifiedExpr q => q.Satisfies, _ => throw new InvalidOperationException() };
            return CapturesStreamedVariable(body, local);
        }
        return ChildrenOf(expression).Any(child => CapturesStreamedVariable(child, bound));
    }

    private Streamability Partial(IReadOnlyList<Expr> arguments, FunctionItemTest? signature, StreamabilityContext context, Streamability? target,
        bool declaredStreamable = false)
    {
        var operands = new List<AnalysisOperand>();
        var parameters = new List<SequenceTypeTest>();
        bool capturesStreamingParameter = false;
        if (target.HasValue) operands.Add(new(target.Value, OperandUsage.Inspection));
        for (int i = 0; i < arguments.Count; i++)
        {
            SequenceTypeTest type = signature?.Parameters is { } types && i < types.Count ? types[i] : SequenceTypeTest.AnySequence;
            if (arguments[i] is ArgumentPlaceholder) parameters.Add(type);
            else
            {
                Streamability value = Visit(arguments[i], context);
                capturesStreamingParameter |= declaredStreamable && i == 0 && value.Posture != Posture.Grounded;
                operands.Add(new(value, TypeDeterminedUsage(type.Item)));
            }
        }
        return (capturesStreamingParameter ? Roaming(UType.Function, "19.8.8.14")
            : General_19_8_1(UType.Function, "19.8.8.14", operands.ToArray()))
            with { ItemType = new FunctionItemTest(parameters, signature?.ReturnType ?? SequenceTypeTest.AnySequence) };
    }

    private Streamability DynamicCall_19_8_8_11(DynamicFunctionCall expression, StreamabilityContext context)
    {
        Streamability target = Visit(expression.Function, context);
        FunctionItemTest? signature = CallableSignature(target.ItemType);
        if (expression.Arguments.Any(a => a is ArgumentPlaceholder)) return Partial(expression.Arguments, signature, context, target);
        var operands = new List<AnalysisOperand> { new(target, OperandUsage.Inspection) };
        for (int i = 0; i < expression.Arguments.Count; i++)
        {
            ItemTest? required = signature?.Parameters is { } parameters && i < parameters.Count ? parameters[i].Item : null;
            operands.Add(new(Visit(expression.Arguments[i], context), TypeDeterminedUsage(required)));
        }
        return General_19_8_1(signature?.ReturnType == null ? UType.Any : UTypes.FromSequence(signature.ReturnType), "19.8.8.11", operands.ToArray())
            with { ItemType = signature?.ReturnType?.Item };
    }

    private Streamability Arrow_19_8_8(ArrowExpr arrow, StreamabilityContext context)
    {
        Expr[] arguments = arrow.Arguments.Prepend(arrow.Target).ToArray();
        return arrow.FunctionName != null ? Visit(new FunctionCall(arrow.FunctionName, arguments), context)
            : Visit(new DynamicFunctionCall(arrow.FunctionExpr!, arguments), context);
    }
}
